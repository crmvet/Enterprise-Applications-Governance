using System;
using System.IO;

namespace Product.Engine.Testing;

// Finds the src/ folder: the folder that holds config/schema/config-bundle.schema.json and engine/.
public static class SourceRoot
{
    // Walks up from the start folder. Returns null when no parent is src/.
    public static string? Find(string start)
    {
        if (string.IsNullOrEmpty(start))
        {
            return null;
        }

        DirectoryInfo? dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config", "schema", "config-bundle.schema.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "engine")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    // Finds src/ from the current folder, then from the folder of this assembly. Throws when neither works.
    public static string Require(string? start = null)
    {
        string? found = Find(start ?? Directory.GetCurrentDirectory()) ?? Find(AppDomain.CurrentDomain.BaseDirectory);
        if (found == null)
        {
            throw new DirectoryNotFoundException("Cannot find src/: no parent folder holds config/schema/config-bundle.schema.json. Pass --root <src>.");
        }

        return found;
    }

    // config/presets: the shipped presets.
    public static string PresetsFolder(string root)
    {
        return Path.Combine(root, "config", "presets");
    }

    // fixtures/presets: fictional presets that only fixtures use, such as two overlays that conflict. Never shipped.
    public static string FixturePresetsFolder(string root)
    {
        return Path.Combine(root, "fixtures", "presets");
    }

    // config/build: where build-presets writes the flat bundles. Ignored by git.
    public static string BuildFolder(string root)
    {
        return Path.Combine(root, "config", "build");
    }
}
