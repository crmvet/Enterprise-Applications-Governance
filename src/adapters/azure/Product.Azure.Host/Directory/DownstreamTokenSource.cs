using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

/// <summary>The downstream services the directory reads call.</summary>
public enum DownstreamApi
{
    /// <summary>Microsoft Graph (group members, groups, users' account state, app role assignments).</summary>
    MicrosoftGraph,

    /// <summary>The Power Platform API (the inventory resource query).</summary>
    PowerPlatform,
}

/// <summary>
/// A token for a downstream API on behalf of the signed-in person of the current request. The implementation exchanges the
/// person's incoming token with Microsoft Entra ID for a new token (the on-behalf-of flow). The incoming token is never
/// returned and never sent to a downstream service.
/// </summary>
public interface IDownstreamTokenSource
{
    /// <summary>A new token for <paramref name="api"/> acting as <paramref name="asPerson"/>, or why there is none.</summary>
    DownstreamToken ForUser(DownstreamApi api, BackendCaller asPerson, CancellationToken cancellationToken);
}

/// <summary>A downstream access token, or the plain reason there is none. The token text never shows in strings or logs.</summary>
public sealed class DownstreamToken
{
    private readonly string? _accessToken;

    private DownstreamToken(string? accessToken, string? problem)
    {
        _accessToken = accessToken;
        Problem = problem;
    }

    /// <summary>True when a token was issued.</summary>
    public bool Issued => _accessToken != null;

    /// <summary>Why no token was issued, in plain words; null when one was.</summary>
    public string? Problem { get; }

    /// <summary>An issued access token.</summary>
    public static DownstreamToken FromAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("An access token is one non-empty word.", nameof(accessToken));
        }

        return new DownstreamToken(accessToken, null);
    }

    /// <summary>No token, with the reason.</summary>
    public static DownstreamToken Refused(string problem)
    {
        return new DownstreamToken(null, string.IsNullOrWhiteSpace(problem) ? "No token was issued." : problem);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Issued ? "an issued token (not shown)" : "no token: " + Problem;
    }

    // Puts the token on a request to the downstream service, as the only credential the request carries.
    internal void Authorize(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken ?? throw new InvalidOperationException("No token was issued."));
    }
}

/// <summary>
/// The on-behalf-of token source. It asks Microsoft.Identity.Web for an authorization header for the current request's
/// signed-in user (<see cref="IAuthorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync"/>), with the product's
/// Entra scheme naming the confidential client (the API app registration and its secretless credential). Microsoft.Identity.Web
/// sends the incoming token to the Entra token endpoint as the user assertion and returns a new token for the downstream
/// resource's <c>.default</c> scope; the in-memory token cache keeps it until it expires.
/// <para>
/// Refused, with no call to Entra: no signed-in request; the development identity (it has no Entra token); an app-only
/// caller (no person to act for); an agent identity, an agent user or any non-user principal; and a caller whose object ID is
/// not the request's signed-in <c>oid</c>.
/// </para>
/// </summary>
/// <remarks>
/// https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow and
/// https://learn.microsoft.com/entra/msidweb/authentication/certificateless
/// </remarks>
public sealed class OnBehalfOfTokenSource : IDownstreamTokenSource
{
    // Microsoft.Identity.Web reads these claim names as a password sign-in request. A delegated Entra token never carries
    // them; a principal that does is refused instead of handed to the library.
    private static readonly string[] PasswordFlowClaims = { "xms_username", "xms_password" };

    private const string RefusedKey = "Product.Directory.Refused.";

    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger<OnBehalfOfTokenSource> _logger;

    /// <summary>Creates the source.</summary>
    public OnBehalfOfTokenSource(IHttpContextAccessor accessor, ILogger<OnBehalfOfTokenSource> logger)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The on-behalf-of scope of a downstream API.</summary>
    public static string ScopeOf(DownstreamApi api)
    {
        return api == DownstreamApi.MicrosoftGraph ? DirectoryOptions.GraphScope : DirectoryOptions.PowerPlatformScope;
    }

    /// <summary>The name of a downstream API for people.</summary>
    public static string NameOf(DownstreamApi api)
    {
        return api == DownstreamApi.MicrosoftGraph ? "Microsoft Graph" : "the Power Platform API";
    }

    /// <inheritdoc />
    public DownstreamToken ForUser(DownstreamApi api, BackendCaller asPerson, CancellationToken cancellationToken)
    {
        if (asPerson == null)
        {
            throw new ArgumentNullException(nameof(asPerson));
        }

        HttpContext? context = _accessor.HttpContext;
        if (context == null)
        {
            return DownstreamToken.Refused("There is no signed-in request to act for, so no token was exchanged.");
        }

        string? refusal = Refusal(context, asPerson);
        if (refusal != null)
        {
            return DownstreamToken.Refused(refusal);
        }

        // A refused exchange is not asked again within the same request: a tool that reads twice gets the same answer
        // without a second round trip to Entra.
        string refusedKey = RefusedKey + api;
        if (context.Items.TryGetValue(refusedKey, out object? earlier) && earlier is DownstreamToken refusedBefore)
        {
            return refusedBefore;
        }

        IAuthorizationHeaderProvider? provider = context.RequestServices.GetService<IAuthorizationHeaderProvider>();
        if (provider == null)
        {
            return DownstreamToken.Refused("Token exchange is not set up on this server: Microsoft.Identity.Web token acquisition is not registered.");
        }

        var options = new AuthorizationHeaderProviderOptions
        {
            AcquireTokenOptions = new AcquireTokenOptions { AuthenticationOptionsName = ProductAuthenticationDefaults.EntraScheme },
        };

        DownstreamToken result;
        try
        {
            // The storage and live-read ports are synchronous, so the exchange blocks this request's thread. ASP.NET Core
            // has no synchronization context, and the call is bounded by the caller's cancellation token.
            string header = provider
                .CreateAuthorizationHeaderForUserAsync(new[] { ScopeOf(api) }, options, context.User, cancellationToken)
                .GetAwaiter()
                .GetResult();
            const string bearer = "Bearer ";
            result = header == null || !header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase) || header.Length <= bearer.Length
                ? DownstreamToken.Refused("Microsoft Entra ID returned a token of a kind the product does not send.")
                : DownstreamToken.FromAccessToken(header.Substring(bearer.Length).Trim());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            MsalException? msal = FindMsal(exception);
            _logger.LogWarning(
                "The token exchange for {Api} failed with {ExceptionType} {ErrorCode} (Entra status {StatusCode}).",
                api,
                (msal ?? exception).GetType().Name,
                msal?.ErrorCode ?? "none",
                (msal as MsalServiceException)?.StatusCode ?? 0);
            result = DownstreamToken.Refused(Explain(api, exception, cancellationToken));
        }

        if (!result.Issued)
        {
            context.Items[refusedKey] = result;
        }

        return result;
    }

    // Why this request may not exchange a token for this caller, or null.
    internal static string? Refusal(HttpContext context, BackendCaller asPerson)
    {
        CallerClaims? caller = ProductRequest.Caller(context);
        if (caller == null)
        {
            return "The request is not signed in, so there is no person to act for.";
        }

        bool developmentIdentity = caller.IsDevIdentity
            || context.User.Identities.Any(i => string.Equals(i.AuthenticationType, ProductAuthenticationDefaults.DevelopmentScheme, StringComparison.Ordinal))
            || context.User.HasClaim(c => c.Type == ProductClaimTypes.DevIdentity);
        if (developmentIdentity)
        {
            return "The development identity has no Microsoft Entra token to exchange. Live directory reads need a real sign-in; for a local demo, use the recorded mode (Product:Directory:Mode Recorded).";
        }

        // A demo persona (D-39) is fictional: the bearer token on the request is the demo operator's own, and exchanging it
        // would read the directory as the operator while the product shows the persona.
        if (caller.DemoPersona != null)
        {
            return "A demo persona is fictional and has no Microsoft Entra token to exchange. Live directory reads need the person's own sign-in; for a demo install, use the recorded mode (Product:Directory:Mode Recorded).";
        }

        if (caller.IsAppOnly)
        {
            return "An app-only caller has no person to act for. The directory is read only on behalf of a signed-in person.";
        }

        if (asPerson.PrincipalKind != PrincipalKind.User)
        {
            return "Only a person reads the directory through the product. Agent identities, agent users and workloads never do.";
        }

        if (!string.Equals(caller.ObjectId, asPerson.ObjectId, StringComparison.OrdinalIgnoreCase))
        {
            return "The caller is not the person signed in on this request, so no token was exchanged.";
        }

        if (context.User.Claims.Any(c => PasswordFlowClaims.Contains(c.Type, StringComparer.OrdinalIgnoreCase)))
        {
            return "The signed-in principal carries claims the product does not accept for a token exchange.";
        }

        string authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return "The request carries no bearer token to exchange.";
        }

        return null;
    }

    // A plain explanation of a failed exchange. It names the fix (consent, a stronger sign-in, the API's own credential)
    // and never quotes the exception message, which belongs in Entra's diagnostics, not in an answer.
    internal static string Explain(DownstreamApi api, Exception exception, CancellationToken cancellationToken)
    {
        string service = NameOf(api);
        if (exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            return "Microsoft Entra ID did not exchange the token for " + service + " in time. Try again.";
        }

        MsalException? msal = FindMsal(exception);
        string code = msal?.ErrorCode ?? string.Empty;
        string text = msal?.Message ?? string.Empty;
        string correlation = msal is MsalServiceException withId && !string.IsNullOrEmpty(withId.CorrelationId) ? " Entra correlation ID: " + withId.CorrelationId + "." : string.Empty;

        // AADSTS65001: the user or administrator has not consented to the application.
        // https://learn.microsoft.com/entra/identity-platform/reference-error-codes
        bool consent = (msal is MsalUiRequiredException ui && ui.Classification == UiRequiredExceptionClassification.ConsentRequired)
            || code == "consent_required"
            || text.IndexOf("AADSTS65001", StringComparison.Ordinal) >= 0;
        if (consent)
        {
            return "An administrator has not granted consent for the product's API to call " + service + " on behalf of people. "
                + "A Global Administrator or Privileged Role Administrator grants admin consent for the API app registration's delegated permissions to "
                + service + " (" + Permissions(api) + ")." + correlation;
        }

        if (msal is MsalUiRequiredException required)
        {
            // AADSTS50076 and AADSTS50079 (multifactor authentication), AADSTS53003 (Conditional Access): Entra wants more
            // from the person's sign-in. A web API cannot prompt, so the person signs in again in their client.
            bool stronger = !string.IsNullOrEmpty(required.Claims)
                || code == "interaction_required"
                || new[] { "AADSTS50076", "AADSTS50079", "AADSTS53003" }.Any(c => text.IndexOf(c, StringComparison.Ordinal) >= 0);
            return stronger
                ? "Microsoft Entra ID needs more from your sign-in before the product may act for you on " + service + ", for example multifactor authentication or a Conditional Access policy. Sign in again in your client and retry." + correlation
                : "Microsoft Entra ID did not exchange your token for " + service + " (" + Code(code) + "). Sign in again and retry." + correlation;
        }

        if (msal is MsalServiceException service4xx)
        {
            bool clientProblem = code == "invalid_client" || code == "unauthorized_client"
                || new[] { "AADSTS700213", "AADSTS70021", "AADSTS700016", "AADSTS7000215", "AADSTS700027" }.Any(c => text.IndexOf(c, StringComparison.Ordinal) >= 0);
            if (clientProblem)
            {
                return "The product's API could not prove its own identity to Microsoft Entra ID, so it cannot act for you on " + service + ". "
                    + "An administrator checks the federated identity credential on the API app registration (it trusts the app's managed identity) and Product:Directory:Credential." + correlation;
            }

            if (service4xx.StatusCode == 429 || service4xx.StatusCode >= 500)
            {
                return "Microsoft Entra ID is not answering token requests right now (HTTP " + service4xx.StatusCode + "). Try again later." + correlation;
            }

            return "Microsoft Entra ID refused to exchange your token for " + service + " (" + Code(code) + ")." + correlation;
        }

        if (msal is MsalClientException)
        {
            return "The product's API has no working credential for the token exchange (" + Code(code) + "). An administrator checks Product:Directory:Credential and the app's managed identity.";
        }

        return "The token exchange for " + service + " is not set up on this server (" + exception.GetType().Name + "). It needs Microsoft Entra sign-in and the API app's secretless credential.";
    }

    // The delegated permissions an admin consents to on the API app registration, for the consent message.
    internal static string Permissions(DownstreamApi api)
    {
        return api == DownstreamApi.MicrosoftGraph
            ? "GroupMember.Read.All, User.Read.All, User-LifeCycleInfo.Read.All and Application.Read.All"
            : "ResourceQuery.Resources.Read on the Power Platform API";
    }

    private static string Code(string code)
    {
        return string.IsNullOrEmpty(code) ? "no error code" : code;
    }

    private static MsalException? FindMsal(Exception? exception)
    {
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            if (e is MsalException msal)
            {
                return msal;
            }
        }

        return null;
    }
}
