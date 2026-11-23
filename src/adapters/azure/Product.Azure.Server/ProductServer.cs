using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Product.Azure.Host;
using Product.Azure.Server.DevIdentity;
using Product.Azure.Stores;

namespace Product.Azure.Server;

/// <summary>
/// The Kestrel host: the product's services and pipeline over the store the configuration names, the development
/// identity for local runs only, and Azure Monitor through OpenTelemetry when a connection string is set.
/// </summary>
public static class ProductServer
{
    /// <summary>Builds the server. The development identity guard runs before the host is built.</summary>
    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        AddBaseDirectorySettings(builder.Configuration, builder.Environment);
        DevIdentityGuard.Validate(builder.Environment, builder.Configuration);

        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
        builder.Services.AddProduct(builder.Configuration);
        builder.Services.AddProductStore(services => ServerStore.Create(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IHostEnvironment>()));
        builder.Services.AddDevIdentity(builder.Configuration);
        builder.Services.AddHostedService<StoreWarmup>();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        WebApplication app = builder.Build();
        app.UseProductPipeline();
        return app;
    }

    /// <summary>
    /// When the content root has no appsettings.json (for example <c>dotnet Product.Azure.Server.dll</c> started from
    /// another folder; <c>dotnet run</c> starts the program in its project folder, which has the files), the
    /// settings files next to the assembly are read instead, with the lowest precedence, so environment variables and the
    /// command line still win.
    /// </summary>
    public static void AddBaseDirectorySettings(IConfigurationBuilder configuration, IHostEnvironment environment)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (environment == null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        string baseDirectory = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(environment.ContentRootPath, "appsettings.json")) || !File.Exists(Path.Combine(baseDirectory, "appsettings.json")))
        {
            return;
        }

        int index = 0;
        foreach (string name in new[] { "appsettings.json", "appsettings." + environment.EnvironmentName + ".json" })
        {
            if (File.Exists(Path.Combine(baseDirectory, name)))
            {
                configuration.Sources.Insert(index++, new JsonConfigurationSource
                {
                    Path = name,
                    Optional = true,
                    ReloadOnChange = false,
                    FileProvider = new PhysicalFileProvider(baseDirectory),
                });
            }
        }
    }

    // Opens the store when the server starts, so a wrong store setting or a missing migration stops the server at once
    // instead of failing the first request.
    private sealed class StoreWarmup : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<StoreWarmup> _logger;

        public StoreWarmup(IServiceProvider services, ILogger<StoreWarmup> logger)
        {
            _services = services;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IProductStore store = _services.GetRequiredService<IProductStore>();
            _logger.LogInformation("Store {StoreId} is open; a configuration version is {InForce}.", ((Product.Engine.Runtime.Ports.IRecordStore)store).StoreId, store.HasInForceVersion ? "in force" : "not in force yet (run the seed command for a local demo)");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
