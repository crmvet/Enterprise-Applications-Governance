using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Product.Engine.Runtime.Ports;
using Product.Engine.Testing;
using Product.Engine.Testing.InMemory;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The in-memory store and the manual clock live in Product.Engine.Testing, never in the runtime, as the
// MCP doubles live in Product.Mcp.Testing. A host that references only Product.Engine.Runtime cannot
// bind a store whose rows live in one process's memory and vanish on restart, or a clock a caller sets by hand.
// The tests read references and types through reflection. They do not run the runtime.
public sealed class RuntimeDependencyRuleTests
{
    // Test frameworks and the testing assemblies: never a reference of the runtime.
    private static readonly string[] TestOnlyPrefixes = { "Product.Engine.Testing", "Product.Mcp.Testing", "xunit", "Microsoft.Testing." };

    [Fact]
    public void TheRuntimeReferencesNoTestAssembly()
    {
        Assembly runtime = typeof(EngineRuntime).Assembly;
        string[] testOnly = runtime.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => TestOnlyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(testOnly);
    }

    // The runtime defines no test double: nothing named in-memory, mock, fake, stub, manual or test, no implementation of
    // a storage port (adapters implement them), and no clock but the system clock. The one transaction type it holds is
    // the read cache of a read call (document 25, B25-23): internal, it wraps the transaction an adapter's store began
    // and keeps rows only for that one call, so it binds no store.
    [Fact]
    public void TheRuntimeHoldsNoTestDouble()
    {
        Type[] types = typeof(EngineRuntime).Assembly.GetTypes();

        Assert.DoesNotContain(types, t => Regex.IsMatch(t.Name, "^(InMemory|Mock|Fake|Stub|Manual|Test)", RegexOptions.CultureInvariant));
        Assert.DoesNotContain(types, t => !t.IsInterface && !t.IsAbstract && (typeof(IRecordStore).IsAssignableFrom(t) || typeof(IConfigurationStore).IsAssignableFrom(t)));
        Type cache = Assert.Single(types, t => !t.IsInterface && !t.IsAbstract && typeof(IStoreTransaction).IsAssignableFrom(t));
        Assert.Equal("CallReadCache", cache.Name);
        Assert.False(cache.IsPublic);
        Assert.Equal(new[] { typeof(IStoreTransaction) }, Assert.Single(cache.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { typeof(SystemClock) }, types.Where(t => !t.IsInterface && !t.IsAbstract && typeof(IEngineClock).IsAssignableFrom(t)).ToArray());
        Assert.Equal("Product.Engine.Testing", typeof(InMemoryStore).Assembly.GetName().Name);
        Assert.Equal("Product.Engine.Testing", typeof(ManualClock).Assembly.GetName().Name);
    }

    // No project under src/ references the engine testing assembly except test projects (a folder named tests) and the
    // engine command line, a tool that runs fixtures and replays. A host or adapter that needs
    // it must say so here, in review.
    [Fact]
    public void OnlyTestProjectsAndTheCommandLineReferenceTheTestingAssembly()
    {
        string root = SourceRoot.Require(AppContext.BaseDirectory);
        string[] projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == "bin" || part == "obj" || part == "node_modules"))
            .ToArray();
        Assert.Contains(projects, p => Path.GetFileName(p) == "Product.Engine.Testing.csproj");
        Assert.Contains(projects, p => Path.GetFileName(p) == "Product.Engine.Runtime.csproj");

        string[] offenders = projects
            .Where(p => Path.GetFileName(p) != "Product.Engine.Testing.csproj" && Path.GetFileName(p) != "Product.Engine.Cli.csproj")
            .Where(p => File.ReadAllText(p).IndexOf("Product.Engine.Testing.csproj", StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("tests"))
            .Select(p => p.Substring(root.Length))
            .ToArray();

        Assert.Empty(offenders);
    }
}
