using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Schema;

// The configuration JSON Schemas of config/schema, embedded in this assembly at build time,
// so V01 checks the same shape in the build, in tests and inside a host.
public static class ConfigurationSchemas
{
    public const string BundleId = "config-bundle.schema.json";
    public const string ClientBindingPatchId = "client-binding-patch.schema.json";

    // Entry points of config-bundle.schema.json (see config/schema/README.md).
    public const string Root = BundleId;
    public const string PresetFile = BundleId + "#/$defs/presetFile";
    public const string EntityFile = BundleId + "#/$defs/entityFile";
    public const string PresetPatch = BundleId + "#/$defs/presetPatch";
    public const string DraftPatch = BundleId + "#/$defs/draftPatch";
    public const string DraftOperation = BundleId + "#/$defs/draftOperation";
    public const string PresetOperation = BundleId + "#/$defs/presetOperation";
    public const string PresetBundle = BundleId + "#/$defs/presetBundle";
    public const string SnapshotBundle = BundleId + "#/$defs/snapshotBundle";
    public const string ValidationReport = BundleId + "#/$defs/validationReport";
    public const string ClientBindingPatch = ClientBindingPatchId;

    private static readonly Lazy<IReadOnlyList<JsonTreeNode>> LazyDocuments = new(
        () => new[] { LoadEmbedded(typeof(ConfigurationSchemas).Assembly, BundleId), LoadEmbedded(typeof(ConfigurationSchemas).Assembly, ClientBindingPatchId) });

    private static readonly Lazy<JsonSchemaSet> LazySet = new(() => JsonSchemaSet.Create(LazyDocuments.Value));

    // The two configuration schemas as parsed JSON, for a caller that builds a larger set (the fixture schema refers to them).
    public static IReadOnlyList<JsonTreeNode> Documents => LazyDocuments.Value;

    // The schema set with both configuration schemas. Built once, then shared.
    public static JsonSchemaSet Set => LazySet.Value;

    // Reads a schema embedded under a logical name that ends with ".Schemas.<fileName>".
    // Matching the end keeps it working after the product rename.
    public static JsonTreeNode LoadEmbedded(Assembly assembly, string fileName)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        string suffix = ".Schemas." + fileName;
        string? name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
        if (name == null)
        {
            throw new InvalidOperationException("The schema " + fileName + " is not embedded in " + assembly.GetName().Name + ".");
        }

        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("The schema " + fileName + " cannot be read.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return CanonicalJson.Parse(reader.ReadToEnd());
    }
}
