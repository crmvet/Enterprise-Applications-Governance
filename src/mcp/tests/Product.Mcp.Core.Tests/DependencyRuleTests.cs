using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Identity;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Testing;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The MCP server core stays independent of the store and the host.
// Store access goes through a backend port, so no store or host SDK may appear here.
// The tests read the assembly's metadata from the file, or its types through reflection. They do not run its code.
//
// The mock identity and the in-memory counters live in Product.Mcp.Testing, never in the production
// assembly, so a host that references only Product.Mcp.Core cannot bind them by accident.
public sealed class DependencyRuleTests
{
    private static readonly string[] ForbiddenPrefixes =
    {
        "Microsoft.Xrm.",
        "Microsoft.Crm.",
        "Microsoft.PowerPlatform.",
        "Microsoft.Data.SqlClient",
        "System.Data.SqlClient",
        "Microsoft.Azure.Functions.",
        "Microsoft.AspNetCore.",
    };

    // Test frameworks and the testing assembly: never a reference of the production assembly.
    private static readonly string[] TestOnlyPrefixes =
    {
        "Product.Mcp.Testing",
        "Product.Engine.Testing",
        "xunit",
        "Microsoft.Testing.",
    };

    // The stand-ins the testing assembly holds. None of them may exist in the production assembly, under any namespace.
    private static readonly Type[] TestDoubles = { typeof(MockTokenValidator), typeof(MockToken), typeof(InMemoryRateLimitStore), typeof(GuidIdRecordStore) };

    [Fact]
    public void McpCoreReferencesNoStoreOrHostSdk()
    {
        string[] forbidden = References().Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void McpCoreReferencesNoTestAssembly()
    {
        string[] testOnly = References().Where(name => TestOnlyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray();

        Assert.Empty(testOnly);
    }

    // The production assembly defines no test double: no type of the testing assembly's names, nothing named mock, fake
    // or in-memory, and no implementation of the identity, rate limit or record store ports. A host binds real ones.
    [Fact]
    public void McpCoreHoldsNoTestDouble()
    {
        Assembly core = typeof(McpServerCore).Assembly;
        Type[] types = core.GetTypes();
        string[] names = TestDoubles.Select(t => t.Name).ToArray();

        Assert.DoesNotContain(types, t => names.Contains(t.Name, StringComparer.Ordinal));
        Assert.DoesNotContain(types, t => Regex.IsMatch(t.Name, "^(Mock|Fake|Stub|InMemory|Test)", RegexOptions.CultureInvariant));
        Assert.DoesNotContain(types, t => !t.IsInterface && !t.IsAbstract && (typeof(ITokenValidator).IsAssignableFrom(t) || typeof(IRateLimitStore).IsAssignableFrom(t) || typeof(IRecordStore).IsAssignableFrom(t)));
        Assert.All(TestDoubles, t => Assert.Equal("Product.Mcp.Testing", t.Assembly.GetName().Name));
    }

    // No project under src/ references the testing assembly except test projects (a folder named tests). A future host
    // or harness that needs the mock must say so here, in review.
    [Fact]
    public void OnlyTestProjectsReferenceTheTestingAssembly()
    {
        string root = McpHarness.Root;
        string[] projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == "bin" || part == "obj" || part == "node_modules"))
            .ToArray();
        Assert.Contains(projects, p => Path.GetFileName(p) == "Product.Mcp.Testing.csproj");

        string[] offenders = projects
            .Where(p => Path.GetFileName(p) != "Product.Mcp.Testing.csproj")
            .Where(p => File.ReadAllText(p).IndexOf("Product.Mcp.Testing.csproj", StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("tests"))
            .Select(p => p.Substring(root.Length))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string[] References()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Product.Mcp.Core.dll");
        Assert.True(File.Exists(path), "Product.Mcp.Core.dll is missing from the test output.");

        using FileStream stream = File.OpenRead(path);
        using PEReader peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        return metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToArray();
    }
}
