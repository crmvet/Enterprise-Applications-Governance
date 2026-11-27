using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Product.Azure.Host;

namespace Product.Azure.Tests;

// Microsoft Entra sign-in for a host outside Development (no development identity): the production JWT bearer path with
// the tenant's signing key replaced by a test key, given in memory so nothing goes to the network. The same arrangement
// as EntraSignInTests, for tests that need real tokens in another environment, such as Demo.
internal static class EntraTokens
{
    public static readonly string Issuer = "https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0";

    private static readonly RsaSecurityKey TenantKey = new(RSA.Create(2048)) { KeyId = "tenant-key" };

    // A host in the environment, signing people in with Entra tokens only.
    public static HostSettings Host(string environment)
    {
        var settings = new HostSettings { Environment = environment, DevIdentity = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        settings.Services = services => services.PostConfigure<JwtBearerOptions>(ProductAuthenticationDefaults.EntraScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(TenantKey);
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            options.TokenValidationParameters.ValidIssuers = new[] { Issuer };
        });
        return settings;
    }

    // The claims of a token the product's web app asked for on a person's behalf.
    public static Dictionary<string, object> WebApp(string objectId, params string[] groups)
    {
        return Delegated(objectId, Personas.WebApp, "api.access", groups);
    }

    // The claims of a token Claude Code asked for on a person's behalf.
    public static Dictionary<string, object> ClaudeCode(string objectId, params string[] groups)
    {
        return Delegated(objectId, Personas.ClaudeCode, "mcp.access api.access", groups);
    }

    public static string Token(Dictionary<string, object> claims)
    {
        DateTime expires = DateTime.UtcNow.AddMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Personas.Audience,
            IssuedAt = expires.AddMinutes(-60),
            NotBefore = expires.AddMinutes(-60),
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(TenantKey, SecurityAlgorithms.RsaSha256),
        });
    }

    // Adds the bearer token, and the demo persona header when a persona is named.
    public static Action<HttpRequestMessage> Bearer(string token, string? demoPersona = null)
    {
        return request =>
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (demoPersona != null)
            {
                request.Headers.TryAddWithoutValidation(ProductDemo.PersonaHeader, demoPersona);
            }
        };
    }

    private static Dictionary<string, object> Delegated(string objectId, string clientAppId, string scopes, string[] groups)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["oid"] = objectId,
            ["sub"] = "pairwise-subject-" + objectId.Substring(objectId.Length - 4),
            ["tid"] = Personas.Tenant,
            ["azp"] = clientAppId,
            ["scp"] = scopes,
            ["ver"] = "2.0",
            ["name"] = "Signed-in operator " + objectId.Substring(objectId.Length - 4),
        };
        if (groups.Length > 0)
        {
            claims["groups"] = groups;
        }

        return claims;
    }
}
