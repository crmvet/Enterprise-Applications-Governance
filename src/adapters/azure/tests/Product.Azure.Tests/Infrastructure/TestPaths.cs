using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Product.Engine.Testing;

namespace Product.Azure.Tests;

// Where the tests find src/, the seed files, the walkthrough transcript and the local server's project folder.
internal static class TestPaths
{
    private static readonly Lazy<string> LazySource = new(() =>
    {
        string? project = typeof(TestPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ProjectDirectory")?.Value;
        return SourceRoot.Find(project ?? string.Empty) ?? SourceRoot.Require(AppContext.BaseDirectory);
    });

    // src/
    public static string Source => LazySource.Value;

    // src/adapters/azure
    public static string Adapter => Path.Combine(Source, "adapters", "azure");

    // src/adapters/azure/Product.Azure.Server, the local server's content root.
    public static string ServerProject => Path.Combine(Adapter, "Product.Azure.Server");

    // src/adapters/azure/seed/demo-binding.patch.json
    public static string DemoBinding => Path.Combine(Adapter, "seed", "demo-binding.patch.json");

    // src/adapters/azure/seed/walkthrough-world.json
    public static string WalkthroughWorld => Path.Combine(Adapter, "seed", "walkthrough-world.json");

    // src/demo/walkthrough/transcript.json, the in-process walkthrough as the MCP core tests checked it in.
    public static string Transcript => Path.Combine(Source, "demo", "walkthrough", "transcript.json");

    // A new empty folder for one test's files.
    public static string NewScratchFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "product-azure-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // Removes a scratch folder, ignoring files another process still holds.
    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
