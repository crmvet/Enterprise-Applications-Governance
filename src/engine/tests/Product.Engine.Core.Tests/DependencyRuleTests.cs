using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Product.Engine.Core.Tests;

// Guards the dependency rule of document 25, section 3.2:
// the core references no Dataverse SDK, no SQL driver, no JSON library and no logging library.
public sealed class DependencyRuleTests
{
    private static readonly string[] ForbiddenPrefixes =
    {
        "Microsoft.Xrm.",
        "Microsoft.Crm.",
        "Microsoft.PowerPlatform.",
        "Microsoft.Data.SqlClient",
        "System.Data.SqlClient",
        "System.Text.Json",
        "Newtonsoft.",
        "Microsoft.Extensions.",
        "Azure.",
    };

    [Fact]
    public void CoreReferencesOnlyTheBaseClassLibrary()
    {
        Assembly core = Assembly.Load(new AssemblyName("Product.Engine.Core"));

        string[] references = core.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        string[] notBaseLibrary = references
            .Where(name => !IsBaseClassLibrary(name))
            .ToArray();
        string[] forbidden = references
            .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(notBaseLibrary);
        Assert.Empty(forbidden);
    }

    private static bool IsBaseClassLibrary(string name)
    {
        return name == "mscorlib"
            || name == "netstandard"
            || name == "System"
            || name.StartsWith("System.", StringComparison.Ordinal);
    }
}
