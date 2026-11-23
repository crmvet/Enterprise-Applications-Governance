using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Validation;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Presets;

// Every preset folder under one or more roots, such as config/presets (document 21, section 8.1).
// Loading never throws for a broken preset: its problems are kept on the preset and reported when it is used.
public sealed class PresetCatalog
{
    public const string ManifestFile = "preset.json";
    public const string PatchFile = "patch.json";

    private readonly Dictionary<string, PresetSource> _byKey;

    private PresetCatalog(IList<PresetSource> presets, IList<ValidationIssue> issues)
    {
        Presets = new ReadOnlyCollection<PresetSource>(presets.OrderBy(p => p.Key, StringComparer.Ordinal).ToList());
        Issues = new ReadOnlyCollection<ValidationIssue>(issues);
        _byKey = new Dictionary<string, PresetSource>(StringComparer.Ordinal);
        foreach (PresetSource preset in Presets)
        {
            if (!_byKey.ContainsKey(preset.Key))
            {
                _byKey.Add(preset.Key, preset);
            }
        }
    }

    public static PresetCatalog Empty { get; } = new(new List<PresetSource>(), new List<ValidationIssue>());

    // Every preset, sorted by key.
    public IReadOnlyList<PresetSource> Presets { get; }

    // Problems of the catalog as a whole, such as one preset key in two roots.
    public IReadOnlyList<ValidationIssue> Issues { get; }

    public IEnumerable<PresetSource> FullPresets => Presets.Where(p => p.IsFull);

    public IEnumerable<PresetSource> Overlays => Presets.Where(p => p.IsOverlay);

    public bool TryGet(string key, [NotNullWhen(true)] out PresetSource? preset)
    {
        return _byKey.TryGetValue(key, out preset);
    }

    // Every non-empty set of overlays, in key order, that a client may apply to this full preset.
    // A set is left out when any two of its presets, the full preset included, list each other in conflictsWith (V43).
    // The preset build and the tests both use this list, so they check the same selections.
    public IEnumerable<string[]> AllowedOverlayCombinations(PresetSource full)
    {
        if (full == null)
        {
            throw new ArgumentNullException(nameof(full));
        }

        PresetSource[] overlays = Overlays.ToArray();
        if (overlays.Length > 16)
        {
            throw new InvalidOperationException("Too many overlays to list every combination: " + overlays.Length + ".");
        }

        for (int mask = 1; mask < (1 << overlays.Length); mask++)
        {
            var chosen = new List<PresetSource>();
            for (int i = 0; i < overlays.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    chosen.Add(overlays[i]);
                }
            }

            var all = new List<PresetSource> { full };
            all.AddRange(chosen);
            bool conflict = all.Any(a => all.Any(b => !ReferenceEquals(a, b) && a.ConflictsWith.Contains(b.Key, StringComparer.Ordinal)));
            if (!conflict)
            {
                yield return chosen.Select(o => o.Key).ToArray();
            }
        }
    }

    // Loads every folder with a preset.json under the roots. A root that does not exist is skipped.
    public static PresetCatalog Load(IEnumerable<string> roots)
    {
        if (roots == null)
        {
            throw new ArgumentNullException(nameof(roots));
        }

        var presets = new List<PresetSource>();
        var issues = new List<ValidationIssue>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!File.Exists(Path.Combine(folder, ManifestFile)))
                {
                    continue;
                }

                PresetSource preset = LoadFolder(folder);
                if (seen.TryGetValue(preset.Key, out string? other))
                {
                    var collector = new ValidationIssues();
                    V06ReferenceResolves.Instance.Missing(
                        "preset key " + "`" + preset.Key + "` is used by two folders (" + other + " and " + folder + "). Give each preset its own key.",
                        null,
                        preset.Key,
                        folder,
                        collector);
                    issues.AddRange(collector.Items);
                    continue;
                }

                seen.Add(preset.Key, folder);
                presets.Add(preset);
            }
        }

        return new PresetCatalog(presets, issues);
    }

    // Reads one preset folder and checks each file against its schema profile (V01).
    public static PresetSource LoadFolder(string folder)
    {
        string folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var issues = new ValidationIssues();
        V01SchemaShape shape = V01SchemaShape.Instance;

        // preset.json: the manifest (C03 fields).
        string manifestWhere = folderName + "/" + ManifestFile;
        JsonTreeNode? manifest = ReadJson(Path.Combine(folder, ManifestFile), manifestWhere, issues);
        string key = folderName;
        PresetKind? kind = null;
        string? extends = null;
        var conflictsWith = new List<string>();
        if (manifest != null)
        {
            shape.CheckDocument(manifest, ConfigurationSchemas.PresetFile, manifestWhere, issues);
            string? declared = JsonNodes.GetString(manifest, "presetKey");
            if (declared != null && !string.Equals(declared, folderName, StringComparison.Ordinal))
            {
                Structure(issues, manifestWhere, "presetKey " + Q(declared) + " does not match the folder name " + Q(folderName) + ". Rename one so they match.", folderName);
            }

            string? kindText = JsonNodes.GetString(manifest, "presetKind");
            if (kindText != null && EngineContract.PresetKinds.TryParse(kindText, out PresetKind parsedKind))
            {
                kind = parsedKind;
            }

            extends = JsonNodes.GetString(manifest, "extends");
            JsonTreeNode? conflicts = JsonNodes.Get(manifest, "conflictsWith");
            if (conflicts != null && conflicts.Kind == JsonTreeKind.Array)
            {
                conflictsWith.AddRange(conflicts.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()));
            }
        }

        // patch.json: the operations of an overlay or of a preset with extends.
        ConfigurationPatch? patch = null;
        string patchPath = Path.Combine(folder, PatchFile);
        if (File.Exists(patchPath))
        {
            string patchWhere = folderName + "/" + PatchFile;
            JsonTreeNode? patchDocument = ReadJson(patchPath, patchWhere, issues);
            if (patchDocument != null)
            {
                PatchKind patchKind = kind == PresetKind.Overlay ? PatchKind.Overlay : PatchKind.PresetExtends;
                patch = ConfigurationPatch.FromDocument(patchDocument, patchKind, patchWhere);
                shape.CheckDocument(patchDocument, ConfigurationSchemas.PresetPatch, patchWhere, issues, PatchLocator(patch));
                CheckPatchManifest(patchDocument, folderName, kind, extends, conflictsWith, patchWhere, issues);
            }
        }

        // Per-entity files: every other .json file in the folder.
        var entityFiles = new List<PresetEntityFile>();
        foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            if (string.Equals(name, ManifestFile, StringComparison.Ordinal) || string.Equals(name, PatchFile, StringComparison.Ordinal))
            {
                continue;
            }

            string where = folderName + "/" + name;
            JsonTreeNode? document = ReadJson(file, where, issues);
            if (document == null)
            {
                continue;
            }

            string? entity = JsonNodes.GetString(document, "entity");
            JsonTreeNode? rows = JsonNodes.Get(document, "rows");
            shape.CheckDocument(document, ConfigurationSchemas.EntityFile, where, issues, EntityFileLocator(entity, rows));
            if (entity == null || rows == null || rows.Kind != JsonTreeKind.Array)
            {
                continue;
            }

            if (!string.Equals(name, entity + ".json", StringComparison.Ordinal))
            {
                Structure(issues, where, "the file holds entity " + Q(entity) + ", so it must be named " + Q(entity + ".json") + ". Rename the file.", folderName);
            }

            entityFiles.Add(new PresetEntityFile(name, entity, rows));
        }

        CheckStructure(folderName, kind, extends, entityFiles.Count, patch != null || File.Exists(patchPath), issues);
        return new PresetSource(key, folder, manifest, kind, extends, conflictsWith, entityFiles, patch, issues.Items.ToList());
    }

    private static void CheckStructure(string folderName, PresetKind? kind, string? extends, int entityFileCount, bool hasPatch, ValidationIssues issues)
    {
        string where = folderName + "/" + ManifestFile;
        if (kind == PresetKind.Overlay)
        {
            if (!hasPatch)
            {
                Structure(issues, where, "an overlay needs patch.json with its operations. Add it.", folderName);
            }

            if (entityFileCount > 0)
            {
                Structure(issues, where, "an overlay holds only patch.json, not per-entity files. Move the rows into patch.json as upsert operations.", folderName);
            }
        }
        else if (kind == PresetKind.Full && extends != null)
        {
            if (!hasPatch)
            {
                Structure(issues, where, "a full preset that extends " + Q(extends) + " needs patch.json with its changes. Add it.", folderName);
            }

            if (entityFileCount > 0)
            {
                Structure(issues, where, "a full preset that extends another holds only patch.json, not per-entity files. Move the rows into patch.json as upsert operations.", folderName);
            }
        }
        else if (kind == PresetKind.Full)
        {
            if (hasPatch)
            {
                Structure(issues, where, "a full preset without extends holds per-entity files, not patch.json. Remove patch.json, or set extends.", folderName);
            }

            if (entityFileCount == 0)
            {
                Structure(issues, where, "a full preset without extends needs per-entity files. Add them.", folderName);
            }
        }
    }

    private static void CheckPatchManifest(JsonTreeNode patchDocument, string folderName, PresetKind? kind, string? extends, List<string> conflictsWith, string where, ValidationIssues issues)
    {
        JsonTreeNode? manifest = JsonNodes.Get(patchDocument, "manifest");
        if (manifest == null)
        {
            return;
        }

        string? presetKey = JsonNodes.GetString(manifest, "presetKey");
        if (presetKey != null && !string.Equals(presetKey, folderName, StringComparison.Ordinal))
        {
            Structure(issues, where, "manifest presetKey " + Q(presetKey) + " does not match the folder " + Q(folderName) + ". Make them match.", folderName);
        }

        string? patchKind = JsonNodes.GetString(manifest, "presetKind");
        if (patchKind != null && kind != null && !string.Equals(patchKind, EngineContract.PresetKinds.ToWire(kind.Value), StringComparison.Ordinal))
        {
            Structure(issues, where, "manifest presetKind " + Q(patchKind) + " does not match preset.json. Make them match.", folderName);
        }

        string? patchExtends = JsonNodes.GetString(manifest, "extends");
        if (!string.Equals(patchExtends, extends, StringComparison.Ordinal))
        {
            Structure(issues, where, "manifest extends " + Q(patchExtends ?? "null") + " does not match preset.json (" + Q(extends ?? "null") + "). Make them match.", folderName);
        }

        JsonTreeNode? conflicts = JsonNodes.Get(manifest, "conflictsWith");
        if (conflicts != null && conflicts.Kind == JsonTreeKind.Array)
        {
            string[] listed = conflicts.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            if (!listed.SequenceEqual(conflictsWith.OrderBy(s => s, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                Structure(issues, where, "manifest conflictsWith does not match preset.json. Make them match.", folderName);
            }
        }
    }

    private static JsonTreeNode? ReadJson(string path, string where, ValidationIssues issues)
    {
        if (JsonText.TryReadFile(path, out JsonTreeNode? node, out string? error))
        {
            return node;
        }

        Structure(issues, where, "the file " + error + ". Fix the JSON.", null);
        return null;
    }

    // Folder and file problems are shape problems of the preset source, so they report as V01.
    private static void Structure(ValidationIssues issues, string where, string message, string? presetKey)
    {
        issues.Add(new ValidationIssue(
            V01SchemaShape.Instance.RuleId,
            V01SchemaShape.Instance.Code,
            V01SchemaShape.Instance.Severity,
            V01SchemaShape.Instance.RuleId + ": in " + where + ", " + message,
            null,
            presetKey,
            where));
    }

    private static Func<string, RowLocation?> EntityFileLocator(string? entity, JsonTreeNode? rows)
    {
        return pointer =>
        {
            IReadOnlyList<string> tokens = JsonPointer.Split(pointer);
            if (entity == null || rows == null || tokens.Count < 2 || tokens[0] != "rows" || !int.TryParse(tokens[1], out int index) || index >= rows.Items.Count)
            {
                return entity == null ? null : new RowLocation(entity, null);
            }

            return new RowLocation(entity, JsonNodes.GetString(rows.Items[index], "key"));
        };
    }

    internal static Func<string, RowLocation?> PatchLocator(ConfigurationPatch patch)
    {
        return pointer =>
        {
            ConfigurationOperation? op = patch.Operations.FirstOrDefault(o => pointer == o.Pointer || pointer.StartsWith(o.Pointer + "/", StringComparison.Ordinal));
            return op == null ? null : new RowLocation(op.Entity, op.Key);
        };
    }

    private static string Q(string value)
    {
        return "`" + value + "`";
    }
}
