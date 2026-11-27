using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Host;

/// <summary>
/// The demo persona switch (D-39). In a demo install only (the host runs in the environment named <c>Demo</c>), the person
/// signed in to the product's web app may send <see cref="PersonaHeader"/> naming one of the fictional personas of
/// <c>Product:WebApp:DemoPersonas</c>, and the request then runs as that persona: the persona's object ID and groups
/// replace the operator's, so the Tool Policy, the engine's checks and <c>SESSION_CONTEXT</c> all see the persona, and
/// every row and audit entry the request writes carries the Client App label <see cref="OperatorLabelSuffix"/> after the
/// web app's own label ("Web app, demo operator"). Outside the Demo environment the header is ignored, and a host that
/// names demo personas refuses to start. The header counts only on the person entry (the web app's own token on the REST
/// API); on the MCP endpoint and for any AI client or workload it is ignored.
/// </summary>
public static class ProductDemo
{
    /// <summary>The environment name of a demo install, the only one where demo personas work.</summary>
    public const string EnvironmentName = "Demo";

    /// <summary>The request header that names the demo persona to act as.</summary>
    public const string PersonaHeader = "X-Product-Demo-Persona";

    /// <summary>What follows the web app's Client App label on every write a demo persona makes.</summary>
    public const string OperatorLabelSuffix = ", demo operator";

    /// <summary>The configuration key of the inline persona list.</summary>
    public const string PersonasKey = ProductWebAppOptions.SectionName + ":DemoPersonas";

    /// <summary>The configuration key of the persona file.</summary>
    public const string PersonasFileKey = ProductWebAppOptions.SectionName + ":DemoPersonasFile";

    /// <summary>
    /// The form of every fictional ID in this product's demo data: <c>00000000-0000-4000-8000-</c> and twelve hex digits.
    /// A random Entra object ID never starts this way, so a persona that passes this check is never a real person or group.
    /// </summary>
    public const string FictionalIdPrefix = "00000000-0000-4000-8000-";

    private static readonly Regex PersonaName = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

    /// <summary>True when the host runs in the Demo environment.</summary>
    public static bool IsDemo(IHostEnvironment? environment)
    {
        return environment != null && environment.IsEnvironment(EnvironmentName);
    }

    /// <summary>
    /// The startup guard both hosts call before the host is built: throws when demo personas are named (inline or as a
    /// file) and the environment is not Demo. The web app options validator checks the same again when the host starts.
    /// </summary>
    public static void Validate(IHostEnvironment environment, IConfiguration configuration)
    {
        if (environment == null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (IsDemo(environment))
        {
            return;
        }

        if (configuration.GetSection(PersonasKey).GetChildren().Any() || !string.IsNullOrWhiteSpace(configuration[PersonasFileKey]))
        {
            throw new InvalidOperationException(Refusal(environment.EnvironmentName));
        }
    }

    /// <summary>The startup refusal outside the Demo environment.</summary>
    public static string Refusal(string environmentName)
    {
        return "Product:WebApp:DemoPersonas and Product:WebApp:DemoPersonasFile work only in the " + EnvironmentName + " environment (D-39), and this host runs in the "
            + environmentName + " environment. Remove them, or run the demo install in the environment named " + EnvironmentName + ".";
    }

    /// <summary>True when the text is a fictional ID of the product's demo data (<see cref="FictionalIdPrefix"/>).</summary>
    public static bool IsFictionalId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && Guid.TryParse(id, out _)
            && id!.Trim().Length == 36
            && id.Trim().StartsWith(FictionalIdPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a persona file: <c>{ "personas": [ { "name", "label", "objectId", "groups": [ ... ] } ] }</c>, bound the same
    /// way as the inline settings. Throws with a plain message when the file cannot be read.
    /// </summary>
    public static List<ProductDemoPersona> ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A persona file is named.", nameof(path));
        }

        IConfigurationRoot file;
        try
        {
            file = new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: false).Build();
        }
        catch (Exception exception) when (exception is FormatException || exception is IOException || exception is InvalidDataException)
        {
            throw new InvalidOperationException("Product:WebApp:DemoPersonasFile names " + Path.GetFileName(path) + ", which is not a JSON file this host can read: " + exception.Message, exception);
        }

        return file.GetSection("personas").Get<List<ProductDemoPersona>>() ?? new List<ProductDemoPersona>();
    }

    /// <summary>The full path of the persona file: as given when rooted, else under the content root when it is there, else under the application base directory.</summary>
    public static string ResolveFile(string path, IHostEnvironment environment)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string fromContent = Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));
        return File.Exists(fromContent) ? fromContent : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    /// <summary>
    /// Loads <see cref="ProductWebAppOptions.DemoPersonasFile"/> into the options, in the Demo environment only. A problem
    /// is kept for the options validator, which fails startup with it.
    /// </summary>
    internal static void LoadFile(ProductWebAppOptions options, IHostEnvironment environment)
    {
        options.FileDemoPersonas.Clear();
        options.DemoPersonasFileProblem = null;
        if (string.IsNullOrWhiteSpace(options.DemoPersonasFile) || !IsDemo(environment))
        {
            return;
        }

        string path = ResolveFile(options.DemoPersonasFile.Trim(), environment);
        if (!File.Exists(path))
        {
            options.DemoPersonasFileProblem = "Product:WebApp:DemoPersonasFile names " + options.DemoPersonasFile.Trim() + ", which is not in this deployment.";
            return;
        }

        try
        {
            options.FileDemoPersonas.AddRange(ReadFile(path));
        }
        catch (InvalidOperationException exception)
        {
            options.DemoPersonasFileProblem = exception.Message;
        }
    }

    /// <summary>The problems of the demo persona settings, for the options validator. Empty when they are sound.</summary>
    internal static IEnumerable<string> Problems(ProductWebAppOptions options, IHostEnvironment environment)
    {
        bool inline = options.DemoPersonas.Count > 0;
        bool file = !string.IsNullOrWhiteSpace(options.DemoPersonasFile);
        if (!inline && !file)
        {
            yield break;
        }

        if (!IsDemo(environment))
        {
            yield return Refusal(environment.EnvironmentName);
            yield break;
        }

        // The environment name alone is a setting anyone can get wrong. The switch also needs the install to say it is a
        // demo install (Product:WebApp:Demo, the badge every page shows), so a real install that names the Demo
        // environment by mistake never serves the switch against real data.
        if (!options.Demo)
        {
            yield return "Demo personas work only on a demo install: set Product:WebApp:Demo to true (the install holds the fictional demo estate and every page shows the demo badge), or remove the demo personas.";
        }

        if (inline && file)
        {
            yield return "Set Product:WebApp:DemoPersonas or Product:WebApp:DemoPersonasFile, not both.";
        }

        if (options.DemoPersonasFileProblem != null)
        {
            yield return options.DemoPersonasFileProblem;
        }
        else if (file && options.FileDemoPersonas.Count == 0)
        {
            yield return "Product:WebApp:DemoPersonasFile names a file with no persona in its \"personas\" list.";
        }

        if (options.ClientIds.Length == 0)
        {
            yield return "Product:WebApp:DemoPersonas needs the web app's client ID in Product:WebApp:ClientIds: the persona switch works only for the person signed in to the web app.";
        }

        if ((options.ClientLabel ?? string.Empty).Length + OperatorLabelSuffix.Length > 200)
        {
            yield return "Product:WebApp:ClientLabel is too long for the demo operator label (200 characters in all).";
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProductDemoPersona persona in options.AllDemoPersonas)
        {
            string name = persona.Name ?? string.Empty;
            string shown = string.IsNullOrWhiteSpace(name) ? "a demo persona" : "The demo persona " + name;
            if (!PersonaName.IsMatch(name))
            {
                yield return "Each demo persona needs a name of letters, digits, dots, dashes or underscores (at most 64 characters, starting with a letter or a digit).";
            }
            else if (!names.Add(name))
            {
                yield return "Two demo personas are named " + name + ". Each name is used once.";
            }

            if (string.IsNullOrWhiteSpace(persona.Label) || persona.Label.Length > 120)
            {
                yield return shown + " needs a label of at most 120 characters, such as \"Ravi Holm, data owner\".";
            }

            if (!IsFictionalId(persona.ObjectId))
            {
                yield return shown + " needs a fictional object ID in the form " + FictionalIdPrefix + "xxxxxxxxxxxx. A demo persona is never a real person.";
            }

            if (persona.Groups.Length == 0 || persona.Groups.Any(g => !IsFictionalId(g)))
            {
                yield return shown + " needs one or more fictional group IDs in the form " + FictionalIdPrefix + "xxxxxxxxxxxx, bound to product roles in the demo binding.";
            }

            if (persona.Teams.Any(g => !IsFictionalId(g)))
            {
                yield return shown + " names a team that is not a fictional group ID in the form " + FictionalIdPrefix + "xxxxxxxxxxxx.";
            }
        }
    }
}

/// <summary>
/// One fictional persona a demo operator may act as (D-39): what the web app shows, and the object ID and groups the
/// request then runs with. The groups are fictional groups the demo binding binds to product roles.
/// </summary>
public sealed class ProductDemoPersona
{
    /// <summary>The name the header sends, such as <c>ravi</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What the page shows, such as <c>Ravi Holm, data owner</c>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The persona's fictional Entra object ID, the one its Principal Reference row names.</summary>
    public string ObjectId { get; set; } = string.Empty;

    /// <summary>
    /// The persona's fictional group object IDs. No default: configuration binding adds a listed array to a default one.
    /// </summary>
    public string[] Groups { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The fictional teams the persona is in that no product role is bound to, such as a support team that holds support
    /// owner assignments. The Teams page lists the persona among their members (the demo's directory, list_teams and
    /// get_team); a request as the persona carries only <see cref="Groups"/>, so a team never gives the persona a role.
    /// Optional.
    /// </summary>
    public string[] Teams { get; set; } = Array.Empty<string>();
}

/// <summary>The demo persona a request runs as, and the operator who chose it. Set only by the demo persona switch.</summary>
public sealed class DemoPersonaUse
{
    /// <summary>Creates the record.</summary>
    public DemoPersonaUse(string name, string label, string operatorObjectId)
    {
        Name = name ?? string.Empty;
        Label = label ?? string.Empty;
        OperatorObjectId = operatorObjectId ?? string.Empty;
    }

    /// <summary>The persona's name.</summary>
    public string Name { get; }

    /// <summary>The persona's label.</summary>
    public string Label { get; }

    /// <summary>
    /// The object ID of the person who signed in and chose the persona. The store records the persona and the label
    /// "demo operator" (D-39), never this ID. The host logs it with the request's correlation ID
    /// (CallerContextMiddleware), and the request limits count it, not the persona.
    /// </summary>
    public string OperatorObjectId { get; }
}

/// <summary>The switch itself: picks the caller a request runs as.</summary>
internal static class DemoPersonaSwitch
{
    /// <summary>
    /// The caller the request runs as: the persona the header names, when the host runs in the Demo environment, demo
    /// personas are configured, and the signed-in caller is a person on the person entry (the web app's own token on the
    /// REST API), or a person's AI client on the MCP endpoint (the demo operator's Claude Code acting as a persona, so the
    /// story's AI build loop runs on a demo install; product review 2026-11-24). Otherwise the caller as signed in: the
    /// header is ignored. A header that names no configured persona on either entry of a demo install is refused, so a
    /// demo operator never acts as themselves by mistake.
    /// </summary>
    public static CallerClaims? Apply(HttpContext context, CallerClaims? caller, ProductWebAppOptions webApp, IHostEnvironment environment, out McpError? refusal)
    {
        return Apply(context, caller, webApp, environment, null, out refusal);
    }

    /// <summary>As above, with the MCP endpoint's path (Product:McpPath), where a person's AI client may act as a persona.</summary>
    public static CallerClaims? Apply(HttpContext context, CallerClaims? caller, ProductWebAppOptions webApp, IHostEnvironment environment, string? mcpPath, out McpError? refusal)
    {
        refusal = null;
        if (caller == null || caller.DemoPersona != null || caller.IsDevIdentity || !ProductDemo.IsDemo(environment) || webApp.AllDemoPersonas.Count == 0)
        {
            return caller;
        }

        if (!context.Request.Headers.TryGetValue(ProductDemo.PersonaHeader, out var values))
        {
            return caller;
        }

        string name = values.ToString().Trim();
        bool aiClientOnMcp = mcpPath != null && !caller.IsAppOnly && !caller.IsAgentToken && !webApp.IsWebAppClient(caller.ClientAppId)
            && context.Request.Path.StartsWithSegments("/" + mcpPath.Trim('/'), StringComparison.OrdinalIgnoreCase);
        if (name.Length == 0 || (!CallerFacts.IsPersonEntry(caller, context.Request.Path, webApp) && !aiClientOnMcp))
        {
            return caller;
        }

        ProductDemoPersona? persona = webApp.FindDemoPersona(name);
        if (persona == null)
        {
            refusal = new McpError(McpErrorCodes.Validation, "The " + ProductDemo.PersonaHeader + " header names no demo persona of this install. Pick one of the personas the web app's config.json lists, or leave the header out to act as yourself.", ProductDemo.PersonaHeader);
            return caller;
        }

        return caller.ForDemoPersona(persona);
    }
}
