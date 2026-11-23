using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders.InMemory;
using Microsoft.IdentityModel.JsonWebTokens;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

/// <summary>Service registration of the live directory reads.</summary>
public static class DirectoryRegistration
{
    /// <summary>
    /// Registers the live reads behind the MCP core's ports, so <c>get_role_holders</c>, <c>run_owner_check</c>,
    /// <c>check_role_bindings</c> (Microsoft Graph, <see cref="IDirectoryReader"/>) and <c>run_gap_check</c> (the Power
    /// Platform inventory, <see cref="IInventoryReader"/>) answer from the live services instead of
    /// <c>upstream_unavailable</c>. The mode comes from <c>Product:Directory:Mode</c> when the service provider is built:
    /// <list type="bullet">
    /// <item>Off (the default): no reader is bound, and the four tools answer as before.</item>
    /// <item>Live: <see cref="GraphDirectoryReader"/> and <see cref="PowerPlatformInventoryReader"/> over
    /// <see cref="DirectoryHttp"/>, with tokens from <see cref="OnBehalfOfTokenSource"/>. Microsoft.Identity.Web token
    /// acquisition and its in-memory token cache are registered for the product's Entra scheme, with the API app's
    /// secretless credential from <c>Product:Directory:Credential</c> when the scheme names none. The Entra scheme keeps the
    /// validated token on the signed-in principal (its bootstrap context), so the exchange finds the user assertion for the
    /// current request whether or not the scheme also stores it for downstream calls.</item>
    /// <item>Recorded (Development only): the same readers over recorded responses in <c>Product:Directory:RecordedPath</c>
    /// (relative to the content root); no token exchange and no network.</item>
    /// </list>
    /// Call it after <see cref="ProductServiceCollectionExtensions.AddProduct"/>. Calling it twice registers once.
    /// </summary>
    public static IServiceCollection AddProductDirectoryReads(this IServiceCollection services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (services.Any(d => d.ServiceType == typeof(DirectoryReadsMarker)))
        {
            return services;
        }

        services.AddSingleton<DirectoryReadsMarker>();
        services.AddOptions<DirectoryOptions>().BindConfiguration(DirectoryOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DirectoryOptions>, DirectoryOptionsValidator>());
        services.AddHttpContextAccessor();

        // Token acquisition for the web API (the on-behalf-of flow) with an in-memory cache. AddAuthentication first, so
        // Microsoft.Identity.Web picks its ASP.NET Core host. Both calls register once.
        // https://learn.microsoft.com/entra/msidweb/authentication/certificateless
        services.AddAuthentication();
        services.AddTokenAcquisition();
        services.AddInMemoryTokenCaches();

        services.AddOptions<MicrosoftIdentityOptions>(ProductAuthenticationDefaults.EntraScheme)
            .Configure<IOptions<DirectoryOptions>>((identity, directory) =>
            {
                if (directory.Value.Mode == DirectoryReadMode.Live && (identity.ClientCredentials == null || !identity.ClientCredentials.Any()))
                {
                    identity.ClientCredentials = new[] { Credential(directory.Value.Credential) };
                }
            });
        services.AddOptions<JwtBearerOptions>(ProductAuthenticationDefaults.EntraScheme)
            .PostConfigure<IOptions<DirectoryOptions>>((jwt, directory) =>
            {
                if (directory.Value.Mode != DirectoryReadMode.Live)
                {
                    return;
                }

                // The validated token stays on the signed-in identity (its bootstrap context) for this request only, where
                // Microsoft.Identity.Web looks for the user assertion of the on-behalf-of exchange. It is never logged,
                // returned or sent anywhere but the Entra token endpoint.
                jwt.TokenValidationParameters.SaveSigninToken = true;
                jwt.Events ??= new JwtBearerEvents();
                Func<TokenValidatedContext, Task> earlier = jwt.Events.OnTokenValidated;
                jwt.Events.OnTokenValidated = async context =>
                {
                    await earlier(context).ConfigureAwait(false);
                    if (context.Principal?.Identity is ClaimsIdentity identity && identity.BootstrapContext == null && context.SecurityToken is JsonWebToken validated)
                    {
                        identity.BootstrapContext = validated.EncodedToken;
                    }
                };
            });

        // One named client for both services: no redirects (a bearer token never follows one), no request logging
        // (URLs carry object IDs), and in recorded mode the recordings as the innermost handler.
        services.AddHttpClient(DirectoryHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => PrimaryHandler(sp))
            .RemoveAllLoggers();

        services.TryAddSingleton<IDownstreamTokenSource>(sp => Mode(sp) == DirectoryReadMode.Recorded
            ? new RecordedTokenSource()
            : new OnBehalfOfTokenSource(sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<ILogger<OnBehalfOfTokenSource>>()));
        services.TryAddSingleton<DirectoryHttp>();
        services.TryAddSingleton<GraphDirectoryReader>();
        services.TryAddSingleton<PowerPlatformInventoryReader>();

        // With the mode Off the factories answer null, so the in-process backend binds no reader (GetService).
        services.AddSingleton<IDirectoryReader>(sp => Mode(sp) == DirectoryReadMode.Off ? null! : sp.GetRequiredService<GraphDirectoryReader>());
        services.AddSingleton<IInventoryReader>(sp => Mode(sp) == DirectoryReadMode.Off ? null! : sp.GetRequiredService<PowerPlatformInventoryReader>());
        return services;
    }

    // The secretless client credential of the API app registration, in Microsoft.Identity.Web's terms.
    // https://learn.microsoft.com/entra/msidweb/authentication/credentials-overview
    internal static CredentialDescription Credential(DirectoryCredentialOptions options)
    {
        if (string.Equals(options.SourceType, DirectoryCredentialOptions.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return new CredentialDescription
            {
                SourceType = CredentialSource.SignedAssertionFilePath,
                SignedAssertionFileDiskPath = options.SignedAssertionFilePath,
            };
        }

        return new CredentialDescription
        {
            SourceType = CredentialSource.SignedAssertionFromManagedIdentity,
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId,
        };
    }

    private static DirectoryReadMode Mode(IServiceProvider services)
    {
        return services.GetRequiredService<IOptions<DirectoryOptions>>().Value.Mode;
    }

    private static HttpMessageHandler PrimaryHandler(IServiceProvider services)
    {
        DirectoryOptions options = services.GetRequiredService<IOptions<DirectoryOptions>>().Value;
        if (options.Mode == DirectoryReadMode.Recorded)
        {
            IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("The recorded directory mode runs only in the Development environment.");
            }

            string folder = Path.IsPathRooted(options.RecordedPath) ? options.RecordedPath : Path.Combine(environment.ContentRootPath, options.RecordedPath);
            return new RecordedDirectoryHandler(RecordedExchange.LoadFolder(folder));
        }

        return new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
    }

    // Marks the registration, so a second call adds nothing.
    private sealed class DirectoryReadsMarker
    {
    }
}
