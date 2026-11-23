using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Testing;

namespace Product.Engine.Configuration.Tests;

// Shared inputs: the src/ folder, the shipped presets plus the fixture-only presets, and small helpers.
// Every value here is fictional or comes from the shipped Balanced preset.
internal static class TestData
{
    private static readonly Lazy<string> LazyRoot = new(() => SourceRoot.Require(AppContext.BaseDirectory));

    private static readonly Lazy<PresetCatalog> LazyCatalog = new(() => PresetCatalog.Load(new[]
    {
        SourceRoot.PresetsFolder(Root),
        SourceRoot.FixturePresetsFolder(Root),
    }));

    public static string Root => LazyRoot.Value;

    public static PresetCatalog Catalog => LazyCatalog.Value;

    public static ConfigurationValidator Validator => new(Catalog);

    // Validates Balanced with inline draft operations, written as JSON text.
    public static ValidationOutcome Balanced(ValidationMode mode, string? operationsJson = null, IEnumerable<string>? overlays = null)
    {
        ContentSource source = ContentSource.Preset("balanced", overlays);
        if (operationsJson != null)
        {
            source = source.WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(operationsJson), PatchKind.Draft, "test operations"));
        }

        return Validator.Validate(new ValidationRequest(source, mode));
    }

    // Validates Balanced as the active version with a whole patch document applied (a draft or a client binding patch).
    public static ValidationOutcome BalancedWithPatch(string patchJson, PatchKind kind, ValidationMode mode = ValidationMode.Draft)
    {
        ConfigurationPatch patch = ConfigurationPatch.FromDocument(CanonicalJson.Parse(patchJson), kind, "test patch");
        return Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced").WithPatch(patch), mode));
    }

    public static string[] RuleIds(ValidationOutcome outcome)
    {
        return outcome.Report.Issues.Where(i => i.RuleId != null).Select(i => i.RuleId!).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    public static IEnumerable<ValidationIssue> Issues(ValidationOutcome outcome, string ruleId)
    {
        return outcome.Report.Issues.Where(i => i.RuleId == ruleId);
    }

    // A fresh temp folder for preset folders a test writes. The caller deletes it.
    public static string TempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "product-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    // Writes a file as UTF-8 without a byte order mark.
    public static void Write(string folder, string name, string text)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text, new System.Text.UTF8Encoding(false));
    }
}
