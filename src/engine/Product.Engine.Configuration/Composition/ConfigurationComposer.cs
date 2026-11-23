using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Validation;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Configuration.Wildcards;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Composition;

// A composed configuration: the content after presets, overlays, patches and wildcard expansion, as a bundle.
public sealed class ComposedConfiguration
{
    internal ComposedConfiguration(
        ConfigurationContent content,
        JsonTreeNode bundle,
        string profile,
        string where,
        bool isRaw,
        bool isComplete,
        ComposedConfiguration? beforePatches,
        IList<PresetSource> presets)
    {
        Content = content;
        Bundle = bundle;
        Profile = profile;
        Where = where;
        IsRaw = isRaw;
        IsComplete = isComplete;
        BeforePatches = beforePatches;
        Presets = presets.ToList();
    }

    public ConfigurationContent Content { get; }

    // { "schemaVersion", "manifest", "entities" }: a preset bundle in preset-build mode, a snapshot bundle otherwise.
    public JsonTreeNode Bundle { get; }

    // The schema profile the bundle must match.
    public string Profile { get; }

    public string Where { get; }

    // True for a raw source: V01 checked the document itself, not the composed bundle.
    public bool IsRaw { get; }

    // False when a preset in the chain could not be resolved. Its issues say why.
    public bool IsComplete { get; }

    // The same composition without the patches, when there were patches. The diff and V08 compare against it.
    public ComposedConfiguration? BeforePatches { get; }

    // The presets used, in the order they were applied.
    public IReadOnlyList<PresetSource> Presets { get; }
}

// Builds a configuration from a source (document 21, sections 6.7 and 8.4):
// 1. A full preset resolves its extends chain on source rows, then wildcards expand.
//    A preset with extends can change a wildcard row by its own key, because expansion comes after.
// 2. Overlays apply in order to the flat, expanded bundle, as document 21, section 6.7 says.
//    A later operation on the same entity and key wins.
// 3. Patches apply in order: draft patches, client binding patches, inline operations.
// After each step, any wildcard row that is still there expands, so the result never holds a wildcard.
public sealed class ConfigurationComposer
{
    public const string DefaultConfigurationSetKey = "working-set";

    private readonly PresetCatalog _catalog;

    public ConfigurationComposer(PresetCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public PresetCatalog Catalog => _catalog;

    // Composes the source. Problems go to issues. Throws ConfigurationInputException when the request itself
    // cannot be composed, such as an unknown preset key or an overlay given as the full preset.
    public ComposedConfiguration Compose(ContentSource source, ValidationMode mode, ValidationIssues issues, string? configurationSetKey = null)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (issues == null)
        {
            throw new ArgumentNullException(nameof(issues));
        }

        string setKey = configurationSetKey ?? DefaultConfigurationSetKey;
        bool workingSet = mode != ValidationMode.PresetBuild;
        var presets = new List<PresetSource>();
        ContentBuilder? builder;
        JsonTreeNode? sourceManifest = null;

        if (source.Kind == ContentSourceKind.Preset)
        {
            builder = ComposePresets(source, presets, issues);
        }
        else
        {
            // A bundle or a raw document: V01 checks the document as given, then its entities expand.
            ConfigurationContent given = ConfigurationContent.FromEntitiesNode(JsonNodes.Get(source.Document!, "entities"));
            V01SchemaShape.Instance.CheckDocument(source.Document!, ConfigurationSchemas.Root, source.Where, issues, V01SchemaShape.InContent(given));
            sourceManifest = JsonNodes.Get(source.Document!, "manifest");
            builder = new ContentBuilder(given);
            WildcardExpander.Expand(builder, issues);
        }

        bool isRaw = source.Kind == ContentSourceKind.Raw;
        if (builder == null)
        {
            JsonTreeNode emptyBundle = BuildBundle(JsonNodes.EmptyObject, ConfigurationContent.Empty);
            return new ComposedConfiguration(ConfigurationContent.Empty, emptyBundle, ConfigurationSchemas.Root, source.Where, isRaw, isComplete: false, null, presets);
        }

        JsonTreeNode manifest = BuildManifest(source, sourceManifest, presets, workingSet, setKey);
        string profile = ProfileFor(isRaw, workingSet, manifest);

        ComposedConfiguration? before = null;
        if (source.Patches.Count > 0)
        {
            ConfigurationContent beforeContent = builder.Clone().Freeze(stampOrigin: workingSet);
            before = new ComposedConfiguration(beforeContent, BuildBundle(manifest, beforeContent), profile, source.Where, isRaw, isComplete: true, null, presets);

            foreach (ConfigurationPatch patch in source.Patches)
            {
                CheckPatchShape(patch, issues);
                bool clientChange = workingSet && (patch.Kind == PatchKind.Draft || patch.Kind == PatchKind.ClientBinding);
                PatchApplier.Apply(builder, patch, markTouched: clientChange, issues);
                WildcardExpander.Expand(builder, issues);
            }
        }

        ConfigurationContent content = builder.Freeze(stampOrigin: workingSet);
        string where = source.Patches.Count > 0 || source.Kind == ContentSourceKind.Preset
            ? (workingSet ? "the composed working set" : "the built preset bundle")
            : source.Where;
        return new ComposedConfiguration(content, BuildBundle(manifest, content), profile, where, isRaw, isComplete: true, before, presets);
    }

    // Resolves and expands one full preset on its own: the flat bundle the build writes.
    public ComposedConfiguration BuildPreset(string presetKey, ValidationIssues issues)
    {
        return Compose(ContentSource.Preset(presetKey), ValidationMode.PresetBuild, issues);
    }

    private ContentBuilder? ComposePresets(ContentSource source, List<PresetSource> used, ValidationIssues issues)
    {
        issues.AddRange(_catalog.Issues);
        PresetSource full = RequirePreset(source.PresetKey!, PresetKind.Full);
        var overlays = new List<PresetSource>();
        foreach (string key in source.Overlays)
        {
            if (overlays.Any(o => o.Key == key))
            {
                throw new ConfigurationInputException(ReasonCodes.InputMalformed, "The overlay " + key + " is listed twice. List each overlay once.");
            }

            overlays.Add(RequirePreset(key, PresetKind.Overlay));
        }

        V43PresetConflict.Instance.CheckSelection(full, overlays, issues);

        ContentBuilder? builder = ResolveSource(full, new List<string>(), used, issues);
        if (builder == null)
        {
            return null;
        }

        WildcardExpander.Expand(builder, issues);
        foreach (PresetSource overlay in overlays)
        {
            used.Add(overlay);
            issues.AddRange(overlay.LoadIssues);
            if (overlay.Patch != null)
            {
                PatchApplier.Apply(builder, overlay.Patch, markTouched: false, issues);
                WildcardExpander.Expand(builder, issues);
            }
        }

        return builder;
    }

    // A full preset's source rows: its own entity files, or its parent's rows with its patch applied.
    private ContentBuilder? ResolveSource(PresetSource preset, List<string> chain, List<PresetSource> used, ValidationIssues issues)
    {
        if (chain.Contains(preset.Key, StringComparer.Ordinal))
        {
            V06ReferenceResolves.Instance.Missing(
                "the extends chain " + string.Join(" > ", chain.Concat(new[] { preset.Key })) + " loops, so preset `" + preset.Key + "` never resolves. Break the loop.",
                null,
                preset.Key,
                preset.Key + "/preset.json",
                issues);
            return null;
        }

        chain.Add(preset.Key);
        used.Insert(0, preset);
        issues.AddRange(preset.LoadIssues);

        if (preset.Extends == null)
        {
            var builder = new ContentBuilder();
            foreach (PresetEntityFile file in preset.EntityFiles)
            {
                builder.Rows(file.Entity).AddRange(file.Rows.Items);
            }

            return builder;
        }

        if (!_catalog.TryGet(preset.Extends, out PresetSource? parent) || !parent.IsFull)
        {
            V06ReferenceResolves.Instance.Missing(
                "preset `" + preset.Key + "` extends `" + preset.Extends + "`, which is not a full preset in the catalog. Fix extends, or add that preset.",
                null,
                preset.Key,
                preset.Key + "/preset.json",
                issues);
            return null;
        }

        ContentBuilder? parentRows = ResolveSource(parent, chain, used, issues);
        if (parentRows == null)
        {
            return null;
        }

        if (preset.Patch != null)
        {
            PatchApplier.Apply(parentRows, preset.Patch, markTouched: false, issues);
        }

        return parentRows;
    }

    private PresetSource RequirePreset(string key, PresetKind kind)
    {
        if (!_catalog.TryGet(key, out PresetSource? preset))
        {
            throw new ConfigurationInputException(ReasonCodes.InputUnknownKey, "No preset has the key " + key + ". Known presets: " + string.Join(", ", _catalog.Presets.Select(p => p.Key)) + ".");
        }

        if (preset.Kind != kind)
        {
            string wanted = kind == PresetKind.Full ? "a full preset" : "an overlay";
            throw new ConfigurationInputException(ReasonCodes.InputMalformed, "The preset " + key + " is not " + wanted + ". Pick " + wanted + " here.");
        }

        return preset;
    }

    private static void CheckPatchShape(ConfigurationPatch patch, ValidationIssues issues)
    {
        if (patch.Document == null)
        {
            return;
        }

        Func<string, RowLocation?> locate = PresetCatalog.PatchLocator(patch);
        if (!patch.IsInline)
        {
            V01SchemaShape.Instance.CheckDocument(patch.Document, patch.DocumentSchema, patch.Where, issues, locate);
            return;
        }

        // Inline operations: each one against the operation profile.
        for (int i = 0; i < patch.Document.Items.Count; i++)
        {
            JsonTreeNode item = patch.Document.Items[i];
            SchemaResult result = ConfigurationSchemas.Set.Evaluate(item, patch.OperationSchema);
            string prefix = JsonPointer.Append(JsonPointer.Root, i);
            var shifted = new SchemaResult(result.Issues.Select(s => new SchemaIssue(prefix + s.InstancePath, s.Message, s.KeywordPath)), result.Annotations);
            V01SchemaShape.Instance.Report(shifted, patch.Where, issues, locate);
        }
    }

    private JsonTreeNode BuildManifest(ContentSource source, JsonTreeNode? sourceManifest, List<PresetSource> presets, bool workingSet, string setKey)
    {
        if (source.Kind == ContentSourceKind.Raw)
        {
            return sourceManifest ?? JsonNodes.EmptyObject;
        }

        PresetSource? full = presets.FirstOrDefault(p => p.Key == source.PresetKey);
        bool sourceIsPresetBundle = sourceManifest != null && JsonNodes.GetString(sourceManifest, "presetKey") != null;

        if (!workingSet)
        {
            if (full?.Manifest != null)
            {
                return PresetManifest(full.Manifest);
            }

            return sourceManifest ?? JsonNodes.EmptyObject;
        }

        // A working set snapshot manifest. The version number stays out of Content Json, so a revert
        // reproduces the hash of the version it returns to.
        var members = new List<KeyValuePair<string, JsonTreeNode>> { JsonNodes.Member("kind", "bundle") };
        if (source.Kind == ContentSourceKind.Bundle && sourceManifest != null && !sourceIsPresetBundle)
        {
            foreach (KeyValuePair<string, JsonTreeNode> member in sourceManifest.Members)
            {
                if (member.Key != "kind" && member.Key != "versionNumber")
                {
                    members.Add(member);
                }
            }

            if (!members.Any(m => m.Key == "configurationSetKey"))
            {
                members.Add(JsonNodes.Member("configurationSetKey", setKey));
            }

            return JsonTreeNode.CreateObject(members);
        }

        members.Add(JsonNodes.Member("configurationSetKey", setKey));
        var applied = new List<JsonTreeNode>();
        if (source.Kind == ContentSourceKind.Preset)
        {
            members.Add(JsonNodes.Member("basedOnPresetKey", source.PresetKey!));
            foreach (string key in new[] { source.PresetKey! }.Concat(source.Overlays))
            {
                PresetSource? preset = presets.FirstOrDefault(p => p.Key == key);
                applied.Add(AppliedPreset(key, preset?.Kind, preset?.ContentVersion));
            }
        }
        else if (sourceIsPresetBundle)
        {
            string presetKey = JsonNodes.GetString(sourceManifest!, "presetKey")!;
            members.Add(JsonNodes.Member("basedOnPresetKey", presetKey));
            string? kindText = JsonNodes.GetString(sourceManifest!, "presetKind");
            PresetKind? kind = kindText != null && EngineContract.PresetKinds.TryParse(kindText, out PresetKind parsed) ? parsed : null;
            applied.Add(AppliedPreset(presetKey, kind, JsonNodes.GetString(sourceManifest!, "contentVersion")));
        }

        if (applied.Count > 0)
        {
            members.Add(JsonNodes.Member("appliedPresets", JsonTreeNode.CreateArray(applied)));
        }

        return JsonTreeNode.CreateObject(members);
    }

    private static JsonTreeNode AppliedPreset(string key, PresetKind? kind, string? contentVersion)
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>
        {
            JsonNodes.Member("presetKey", key),
            JsonNodes.Member("contentVersion", contentVersion ?? "0.0.0"),
        };
        if (kind != null)
        {
            members.Add(JsonNodes.Member("presetKind", EngineContract.PresetKinds.ToWire(kind.Value)));
        }

        return JsonTreeNode.CreateObject(members);
    }

    // preset.json becomes a bundle manifest: kind bundle, without the file's own schemaVersion and comments.
    private static JsonTreeNode PresetManifest(JsonTreeNode presetFile)
    {
        var members = presetFile.Members.Where(m => m.Key != "schemaVersion" && m.Key != "$comment").ToList();
        members.Add(JsonNodes.Member("kind", "bundle"));
        return JsonTreeNode.CreateObject(members);
    }

    private static string ProfileFor(bool isRaw, bool workingSet, JsonTreeNode manifest)
    {
        if (isRaw)
        {
            return ConfigurationSchemas.Root;
        }

        if (!workingSet && JsonNodes.GetString(manifest, "presetKey") != null)
        {
            return ConfigurationSchemas.PresetBundle;
        }

        return ConfigurationSchemas.SnapshotBundle;
    }

    internal static JsonTreeNode BuildBundle(JsonTreeNode manifest, ConfigurationContent content)
    {
        return JsonTreeNode.CreateObject(
            JsonNodes.Member("schemaVersion", "1.0"),
            JsonNodes.Member("manifest", manifest),
            JsonNodes.Member("entities", content.ToEntitiesNode()));
    }
}
