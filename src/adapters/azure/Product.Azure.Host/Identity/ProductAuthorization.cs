using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Host;

/// <summary>The caller kinds the REST routes accept.</summary>
public enum ProductCallerKind
{
    /// <summary>A delegated user token with the API scope.</summary>
    ApiCaller,

    /// <summary>An app-only token with the record app role.</summary>
    RecordWriter,

    /// <summary>An app-only token with the approval app role.</summary>
    ApprovalWriter,
}

/// <summary>An authorization requirement on the caller's token kind, scope and app role.</summary>
public sealed class ProductCallerRequirement : IAuthorizationRequirement
{
    /// <summary>Creates the requirement.</summary>
    public ProductCallerRequirement(ProductCallerKind kind)
    {
        Kind = kind;
    }

    /// <summary>The caller kind the route accepts.</summary>
    public ProductCallerKind Kind { get; }

    /// <summary>The plain refusal for a signed-in caller that is not this kind.</summary>
    public string Refusal => Kind switch
    {
        ProductCallerKind.ApiCaller => "This route takes a delegated user token with the API scope. App-only tokens use the record endpoint.",
        ProductCallerKind.RecordWriter => "The record endpoint takes only an app-only token with the Deployment.Record app role. People record deployments through record_deployment on MCP or on the page.",
        _ => "Only the approval runner's app-only token with the Approval.Write app role writes approval outcomes. AI clients never approve.",
    };
}

/// <summary>Checks a <see cref="ProductCallerRequirement"/> against the mapped caller.</summary>
public sealed class ProductCallerHandler : AuthorizationHandler<ProductCallerRequirement>
{
    private readonly ICallerMapper _mapper;
    private readonly IOptions<ProductAuthOptions> _auth;

    /// <summary>Creates the handler.</summary>
    public ProductCallerHandler(ICallerMapper mapper, IOptions<ProductAuthOptions> auth)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ProductCallerRequirement requirement)
    {
        CallerClaims? caller = _mapper.Map(context.User);
        ProductAuthOptions auth = _auth.Value;
        if (caller == null || (auth.TenantId.Length > 0 && !string.Equals(caller.TenantId, auth.TenantId, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        bool met = requirement.Kind switch
        {
            ProductCallerKind.ApiCaller => !caller.IsAppOnly && caller.HasScope(auth.ApiScope) && !caller.GroupsOverage,
            ProductCallerKind.RecordWriter => caller.IsAppOnly && caller.HasAppRole(auth.RecordAppRole),
            ProductCallerKind.ApprovalWriter => caller.IsAppOnly && caller.HasAppRole(auth.ApprovalAppRole),
            _ => false,
        };
        if (met)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Writes the refusals of the REST routes in the product's envelope: 401 not_signed_in when no caller signed in, 403
/// not_authorized when the caller is not the kind the route takes.
/// </summary>
public sealed class ProductAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (authorizeResult == null || authorizeResult.Succeeded)
        {
            return _default.HandleAsync(next, context, policy, authorizeResult!);
        }

        ContractSet contracts = context.RequestServices.GetRequiredService<ContractSet>();
        CallerClaims? caller = ProductRequest.Caller(context);
        string tenant = context.RequestServices.GetRequiredService<IOptions<ProductAuthOptions>>().Value.TenantId;
        if (caller != null && tenant.Length > 0 && !string.Equals(caller.TenantId, tenant, StringComparison.OrdinalIgnoreCase))
        {
            return HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotSignedIn, ClaimsTokenValidator.OtherTenantReason), contracts);
        }

        if (authorizeResult.Challenged || caller == null)
        {
            return HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotSignedIn, "Sign in with a bearer token from the client's tenant."), contracts);
        }

        string refusal = policy?.Requirements.OfType<ProductCallerRequirement>().FirstOrDefault()?.Refusal ?? "The caller may not use this route.";
        return HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotAuthorized, refusal), contracts);
    }
}

/// <summary>
/// The Entra scheme when sign-in is not configured (a local run without a tenant): every request is anonymous and a
/// bearer token is refused. Outside Development, startup fails before this could be used.
/// </summary>
public sealed class UnconfiguredEntraHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Creates the handler.</summary>
    public UnconfiguredEntraHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        return Task.FromResult(Request.Headers.ContainsKey("Authorization")
            ? AuthenticateResult.Fail("Entra sign-in is not configured on this server (Product:Auth:TenantId and Product:Auth:ClientId).")
            : AuthenticateResult.NoResult());
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = "Bearer";
        return Task.CompletedTask;
    }
}

internal static class ProductPolicyBuilder
{
    public static IEnumerable<(string Name, AuthorizationPolicy Policy)> Policies()
    {
        yield return (ProductPolicies.ApiCaller, Build(ProductCallerKind.ApiCaller));
        yield return (ProductPolicies.RecordWriter, Build(ProductCallerKind.RecordWriter));
        yield return (ProductPolicies.ApprovalWriter, Build(ProductCallerKind.ApprovalWriter));
    }

    private static AuthorizationPolicy Build(ProductCallerKind kind)
    {
        return new AuthorizationPolicyBuilder(ProductAuthenticationDefaults.PolicyScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new ProductCallerRequirement(kind))
            .Build();
    }
}
