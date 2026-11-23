using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Presets;

// One per-entity file of a preset folder: { "entity": "<entity-key>", "rows": [ ... ] } (document 21, section 8.1).
public sealed class PresetEntityFile
{
    internal PresetEntityFile(string fileName, string entity, JsonTreeNode rows)
    {
        FileName = fileName;
        Entity = entity;
        Rows = rows;
    }

    public string FileName { get; }

    public string Entity { get; }

    // The rows array as written. Non-object items are kept so pointers match the file.
    public JsonTreeNode Rows { get; }
}

// One preset folder: config/presets/<preset-key>/ with preset.json, per-entity files and an optional patch.json.
public sealed class PresetSource
{
    internal PresetSource(
        string key,
        string folder,
        JsonTreeNode? manifest,
        PresetKind? kind,
        string? extends,
        IList<string> conflictsWith,
        IList<PresetEntityFile> entityFiles,
        ConfigurationPatch? patch,
        IList<ValidationIssue> loadIssues)
    {
        Key = key;
        Folder = folder;
        Manifest = manifest;
        Kind = kind;
        Extends = extends;
        ConflictsWith = new ReadOnlyCollection<string>(conflictsWith);
        EntityFiles = new ReadOnlyCollection<PresetEntityFile>(entityFiles);
        Patch = patch;
        LoadIssues = new ReadOnlyCollection<ValidationIssue>(loadIssues);
    }

    // The preset key. It equals the folder name.
    public string Key { get; }

    // The folder on disk.
    public string Folder { get; }

    // preset.json as written, or null when it does not parse.
    public JsonTreeNode? Manifest { get; }

    // Full or overlay. Null when preset.json does not say a known kind.
    public PresetKind? Kind { get; }

    // The parent of a full preset written as changes. Null for Balanced and for overlays.
    public string? Extends { get; }

    public IReadOnlyList<string> ConflictsWith { get; }

    public IReadOnlyList<PresetEntityFile> EntityFiles { get; }

    // patch.json: the operations of an overlay, or of a full preset with extends.
    public ConfigurationPatch? Patch { get; }

    // Problems found while reading the folder: V01 per file, and folder structure.
    public IReadOnlyList<ValidationIssue> LoadIssues { get; }

    public bool IsFull => Kind == PresetKind.Full;

    public bool IsOverlay => Kind == PresetKind.Overlay;

    // The content version from preset.json, such as "1.0.0".
    public string? ContentVersion => Manifest == null ? null : Json.JsonNodes.GetString(Manifest, "contentVersion");
}
