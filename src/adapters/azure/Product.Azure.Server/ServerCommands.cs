using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Product.Azure.Host;
using Product.Azure.Host.Seeding;
using Product.Azure.Stores;
using Product.Azure.Stores.SqlServer;
using Product.Engine.Configuration.Presets;
using Product.Engine.Core.Canonical;

namespace Product.Azure.Server;

/// <summary>
/// The local server's commands besides running: <c>seed</c> (a fictional demo into an empty store), <c>migrate</c> (the
/// Azure SQL scripts, as a deployment identity) and <c>schema-script</c> (the generated table script). Each reads the same
/// configuration as the server (appsettings, environment variables, the remaining command line arguments).
/// </summary>
public static class ServerCommands
{
    /// <summary>The usage text.</summary>
    public const string Usage = @"Usage: dotnet Product.Azure.Server.dll [command] [options] [--Configuration:Key=value ...]

Commands:
  (none)          Run the server.
  seed            Seed a fictional demo into an empty store (Development, or Demo for a demo install):
                    --preset <key>       shipped preset, default balanced
                    --overlay <key>      overlay preset, repeatable
                    --binding <file>     seed patch, default seed/demo-binding.patch.json
                    --rows <file>        seed rows, default seed/walkthrough-world.json
                    --validated-on <d>   validation date (UTC), default today
  migrate         Apply the Azure SQL scripts as the signed-in deployment identity:
                    --connection <name>  connection string name, default Product
                    --app-identity <n>   database user name of the app's managed identity
  schema-script   Write the table script generated from the logical model:
                    --out <file>         output file, default standard output
  refresh-due-states
                  Run the engine's daily due-state refresh once, now, as the engine (document 23, S2):
                    --dry-run            run every item and write nothing";

    /// <summary>
    /// The environment name of a demo install, where the seed command may write the fictional demo estate and the web app
    /// may offer the demo persona switch (D-39). The same name as <see cref="ProductDemo.EnvironmentName"/>.
    /// </summary>
    public const string DemoEnvironment = ProductDemo.EnvironmentName;

    /// <summary>Seeds the demo. Returns the process exit code.</summary>
    public static int Seed(string[] args, TextWriter output)
    {
        (Dictionary<string, List<string>> options, string[] configurationArgs) = Parse(args, "--preset", "--overlay", "--binding", "--rows", "--validated-on");
        (IConfiguration configuration, IHostEnvironment environment) = Configuration(configurationArgs);
        // Seeding writes fictional rows past the engine, so it runs only in Development, or in an environment named Demo
        // for a demo install whose web app shows the demo badge (Product:WebApp:Demo, D-33). Never for a real install.
        if (!environment.IsDevelopment() && !environment.IsEnvironment(DemoEnvironment))
        {
            output.WriteLine("seed is refused in the " + environment.EnvironmentName + " environment; it runs only in Development, or in " + DemoEnvironment + " for a demo install. A real install gets version 1 through the bootstrap.");
            return 1;
        }

        string preset = One(options, "--preset") ?? "balanced";
        List<string> overlays = options.TryGetValue("--overlay", out List<string>? list) ? list : new List<string>();
        string bindingPath = FromBase(One(options, "--binding") ?? Path.Combine("seed", "demo-binding.patch.json"));
        string rowsPath = FromBase(One(options, "--rows") ?? Path.Combine("seed", "walkthrough-world.json"));
        DateTime validatedOn = One(options, "--validated-on") is string day
            ? DateTime.SpecifyKind(DateTime.Parse(day, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc)
            : DateTime.UtcNow;

        PresetCatalog catalog = PresetCatalog.Load(new[] { FromBase(configuration["Product:PresetsPath"] ?? "presets") });
        JsonTreeNode binding = ProductSeeding.ReadJson(bindingPath);
        JsonTreeNode rows = ProductSeeding.ReadJson(rowsPath);
        using IProductStore store = ServerStore.Create(configuration, environment);
        if (store.HasInForceVersion)
        {
            output.WriteLine("The store already has a configuration version in force, so nothing was seeded. Seed an empty store, or remove the local file.");
            return 1;
        }

        SeedResult result = ProductSeeding.SeedDemo(store, catalog, preset, overlays, binding, rows, validatedOn, DateTime.UtcNow);
        output.WriteLine("Seeded " + ((Product.Engine.Runtime.Ports.IRecordStore)store).StoreId + ": " + result.ConfigurationSetKey + " version " + result.VersionNumber + " (" + result.ContentHash + "), " + result.RowsSeeded + " rows.");
        return 0;
    }

    /// <summary>Applies the Azure SQL migrations. Returns the process exit code.</summary>
    public static int Migrate(string[] args, TextWriter output)
    {
        (Dictionary<string, List<string>> options, string[] configurationArgs) = Parse(args, "--connection", "--app-identity");
        (IConfiguration configuration, _) = Configuration(configurationArgs);
        string name = One(options, "--connection") ?? "Product";
        string? connection = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connection))
        {
            output.WriteLine("ConnectionStrings:" + name + " is not set. Pass it in configuration, with Authentication=Active Directory Default for the signed-in deployment identity.");
            return 1;
        }

        string? identity = One(options, "--app-identity") ?? configuration["Product:Migrate:AppIdentityName"];
        if (string.IsNullOrWhiteSpace(identity))
        {
            output.WriteLine("Name the app's managed identity with --app-identity (or Product:Migrate:AppIdentityName): the scripts create its database user.");
            return 1;
        }

        var variables = new Dictionary<string, string>(StringComparer.Ordinal) { ["AppIdentityName"] = identity! };
        SqlServerMigrations.Apply(connection!, variables, output);
        output.WriteLine("Schema version " + SqlServerMigrations.AppliedVersion(connection!) + " is applied (this build needs " + SqlServerMigrations.RequiredVersion + ").");
        return 0;
    }

    /// <summary>
    /// Runs the engine's due-state refresh once, as the engine itself, against the store the server is configured for
    /// (document 23, S2; document 25, B25-18). The Functions app runs the same refresh from its timer.
    /// Returns the process exit code.
    /// </summary>
    public static int RefreshDueStates(string[] args, TextWriter output)
    {
        bool dryRun = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));
        (IConfiguration configuration, IHostEnvironment environment) = Configuration(args.Where(a => !string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase)).ToArray());
        PresetCatalog catalog = PresetCatalog.Load(new[] { FromBase(configuration["Product:PresetsPath"] ?? "presets") });
        using IProductStore store = ServerStore.Create(configuration, environment);
        var runtime = new Product.Engine.Runtime.EngineRuntime((Product.Engine.Runtime.Ports.IRecordStore)store, (Product.Engine.Runtime.Ports.IConfigurationStore)store, catalog);
        int max = int.TryParse(configuration["Product:DueStateRefresh:MaxItems"], NumberStyles.None, CultureInfo.InvariantCulture, out int configured) && configured > 0 ? configured : Product.Engine.Runtime.DueStateRefreshRequest.DefaultMaxItems;
        Product.Engine.Runtime.RuntimeOutcome outcome = DueStateRefreshRunner.RunOnce(runtime, Product.Engine.Runtime.DueStateRefreshRequest.CommandTrigger, dryRun, max, null);
        if (!outcome.Succeeded)
        {
            output.WriteLine("The due-state refresh did not run: " + outcome.Outcome + (outcome.Reasons.Count == 0 ? "." : " (" + outcome.Reasons[0].Code + ": " + outcome.Reasons[0].Message + ")"));
            return 1;
        }

        JsonTreeNode counts = Product.Mcp.Core.Json.Js.Member(outcome.Payload, "counts") ?? Product.Mcp.Core.Json.Js.EmptyObject;
        output.WriteLine((dryRun ? "Dry run of the due-state refresh, nothing written: " : "The due-state refresh ran: ") + Product.Mcp.Core.Json.Js.Write(counts));
        foreach (Product.Engine.Core.Reason warning in outcome.Reasons)
        {
            output.WriteLine("  " + warning.Code + ": " + warning.Message);
        }

        return 0;
    }

    /// <summary>Writes the generated table script. Returns the process exit code.</summary>
    public static int SchemaScript(string[] args, TextWriter output)
    {
        (Dictionary<string, List<string>> options, _) = Parse(args, "--out");
        string script = SqlServerSchemaScript.GenerateTables(EntityCatalog.Embedded);
        string? path = One(options, "--out");
        if (path == null)
        {
            output.Write(script);
            return 0;
        }

        File.WriteAllText(Path.GetFullPath(path), script, new UTF8Encoding(false));
        output.WriteLine("Wrote " + Path.GetFullPath(path) + ".");
        return 0;
    }

    // The same configuration the server reads, without building a host.
    private static (IConfiguration Configuration, IHostEnvironment Environment) Configuration(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
        ProductServer.AddBaseDirectorySettings(builder.Configuration, builder.Environment);
        return (builder.Configuration, builder.Environment);
    }

    // Splits the command's own options from the rest, which is configuration.
    private static (Dictionary<string, List<string>> Options, string[] Remaining) Parse(string[] args, params string[] known)
    {
        var options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? name = known.FirstOrDefault(k => string.Equals(k, arg, StringComparison.OrdinalIgnoreCase) || arg.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                rest.Add(arg);
                continue;
            }

            string? value = arg.Length > name.Length && arg[name.Length] == '=' ? arg.Substring(name.Length + 1) : i + 1 < args.Length ? args[++i] : null;
            if (value == null)
            {
                throw new ArgumentException(name + " needs a value.");
            }

            if (!options.TryGetValue(name, out List<string>? values))
            {
                values = new List<string>();
                options[name] = values;
            }

            values.Add(value);
        }

        return (options, rest.ToArray());
    }

    private static string? One(Dictionary<string, List<string>> options, string name)
    {
        return options.TryGetValue(name, out List<string>? values) && values.Count > 0 ? values[values.Count - 1] : null;
    }

    private static string FromBase(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string fromCurrent = Path.GetFullPath(path);
        return File.Exists(fromCurrent) || Directory.Exists(fromCurrent) ? fromCurrent : Path.Combine(AppContext.BaseDirectory, path);
    }
}
