using System;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Product.Azure.Host;

/// <summary>
/// A group of routes a part of the host adds to the product's route table, such as the approval runner's routes.
/// <see cref="ProductPipeline.MapProduct"/> maps every registered module after the product's own literal routes and
/// before the REST tool facade (<c>POST /api/{tool}</c>), so both hosts serve the same paths from the same code and a
/// module's literal route never loses to the facade.
/// </summary>
public interface IProductEndpointModule
{
    /// <summary>Maps the module's routes, each with its own authorization policy.</summary>
    void Map(IEndpointRouteBuilder endpoints);
}

/// <summary>Registration of endpoint modules.</summary>
public static class ProductModules
{
    /// <summary>Registers a module once. Registering the same module type again adds nothing.</summary>
    public static IServiceCollection AddProductEndpointModule<TModule>(this IServiceCollection services)
        where TModule : class, IProductEndpointModule
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProductEndpointModule, TModule>());
        return services;
    }

    /// <summary>Maps every registered module, in registration order.</summary>
    public static IEndpointRouteBuilder MapProductModules(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        foreach (IProductEndpointModule module in endpoints.ServiceProvider.GetServices<IProductEndpointModule>())
        {
            module.Map(endpoints);
        }

        return endpoints;
    }
}
