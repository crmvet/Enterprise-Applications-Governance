using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;

namespace Product.Azure.Host;

/// <summary>
/// Registration and mapping of the approval runner's routes (document 23, S1; working default Q19). The runner is a
/// Power Automate flow group in the client's tenant (flows/README.md). It pulls work from the approval outbox with its
/// app-only token, sends one approval card per Approval Record, and posts each answer to the approval outcome endpoint.
/// No route here writes an outcome, and none approves.
/// </summary>
public static class ApprovalRunnerRegistration
{
    /// <summary><c>POST</c>: lease pending items.</summary>
    public const string ClaimsRoute = "/engine/v1/approval-runner/claims";

    /// <summary><c>POST</c>: the card for a claimed item went out.</summary>
    public const string SentRoute = "/engine/v1/approval-runner/items/{id}/sent";

    /// <summary><c>POST</c>: give a claimed item back.</summary>
    public const string ReleaseRoute = "/engine/v1/approval-runner/items/{id}/release";

    /// <summary>
    /// Registers the runner's options (section <c>Product:ApprovalRunner</c>, checked at startup), the approval outbox
    /// over the registered <see cref="IProductStore"/>, and the runner's routes as an endpoint module, which
    /// <c>MapProduct</c> maps on every host. <c>AddProduct</c> calls it; calling it again adds nothing new.
    /// </summary>
    public static IServiceCollection AddProductApprovalRunner(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.AddOptions<ApprovalRunnerOptions>().Bind(configuration.GetSection(ApprovalRunnerOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ApprovalRunnerOptions>, ApprovalRunnerOptionsValidator>());
        services.TryAddSingleton(sp => new ApprovalOutbox(sp.GetRequiredService<IProductStore>()));
        services.AddProductEndpointModule<ApprovalRunnerModule>();
        return services;
    }

    /// <summary>
    /// Maps the runner's three routes, each behind the approval writer policy (an app-only token with the
    /// <c>Approval.Write</c> app role). Each handler then resolves the caller against the configuration in force and
    /// serves only the approval runner: a workload holding a current approval writer Workload Grant.
    /// </summary>
    public static IEndpointRouteBuilder MapProductApprovalRunner(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        endpoints.MapPost(ClaimsRoute, Handler(ApprovalRunnerEndpoints.HandleClaims)).RequireAuthorization(ProductPolicies.ApprovalWriter);
        endpoints.MapPost(SentRoute, Handler(ApprovalRunnerEndpoints.HandleSent)).RequireAuthorization(ProductPolicies.ApprovalWriter);
        endpoints.MapPost(ReleaseRoute, Handler(ApprovalRunnerEndpoints.HandleRelease)).RequireAuthorization(ProductPolicies.ApprovalWriter);
        return endpoints;
    }

    private static RequestDelegate Handler(RequestDelegate handler)
    {
        return handler;
    }

    // The runner's routes as an endpoint module. Switched off (Product:ApprovalRunner:Enabled false), every route still
    // exists and answers not_found, so a probe learns nothing about the outbox.
    private sealed class ApprovalRunnerModule : IProductEndpointModule
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapProductApprovalRunner();
        }
    }
}
