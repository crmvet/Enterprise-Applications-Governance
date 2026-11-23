using System;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Product.Azure.Functions;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Azure.Stores.SqlServer;

// The Azure Functions app (isolated worker, ASP.NET Core integration, Flex Consumption): the product's services over
// Azure SQL Database with the app's managed identity, and the product's pipeline built once and called from one
// catch-all HTTP function (ProductFunction). No development identity: this project does not reference the local server,
// and the startup guard refuses the switch.
FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
FunctionsHostGuard.Validate(builder.Environment, builder.Configuration);

builder.Services.AddProduct(builder.Configuration);
builder.Services.AddProductStore(services => OpenStore(services.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(services => new ProductRequestPipeline(ProductPipeline.Build(services)));
FunctionsTelemetry.Configure(builder.Services, builder.Configuration);

builder.Build().Run();

// Azure SQL only (ConnectionStrings:Product with a managed identity, never a password). The app identity never runs
// DDL, so the store only checks that the migrations it needs were applied by a deployment identity.
static IProductStore OpenStore(IConfiguration configuration)
{
    SqlServerStoreOptions options = FunctionsStoreSettings.Read(configuration);
    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:Product is not set. The template sets it as an app setting with the managed identity; it never holds a password.");
    }

    var store = new SqlServerProductStore(options);
    try
    {
        store.EnsureSchema();
    }
    catch
    {
        store.Dispose();
        throw;
    }

    return store;
}
