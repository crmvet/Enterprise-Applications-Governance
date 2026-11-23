using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Configuration.Patches;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Composition;

// Where a configuration comes from, before it is composed: a preset selection, a bundle, or any raw document,
// followed by patches in order.
public sealed class ContentSource
{
    private ContentSource(ContentSourceKind kind, string? presetKey, IList<string> overlays, JsonTreeNode? document, string where, IList<ConfigurationPatch> patches)
    {
        Kind = kind;
        PresetKey = presetKey;
        Overlays = new ReadOnlyCollection<string>(overlays);
        Document = document;
        Where = where;
        Patches = new ReadOnlyCollection<ConfigurationPatch>(patches);
    }

    public ContentSourceKind Kind { get; }

    // The full preset of a preset selection.
    public string? PresetKey { get; }

    // Overlays applied in order after the full preset (document 21, section 6.7).
    public IReadOnlyList<string> Overlays { get; }

    // The bundle or raw document.
    public JsonTreeNode? Document { get; }

    // A name for messages.
    public string Where { get; }

    // Patches applied in order after the base: draft patches, client binding patches, inline operations.
    public IReadOnlyList<ConfigurationPatch> Patches { get; }

    // One full preset and any overlays, in order.
    public static ContentSource Preset(string presetKey, IEnumerable<string>? overlays = null)
    {
        if (string.IsNullOrEmpty(presetKey))
        {
            throw new ArgumentException("A preset selection names a full preset.", nameof(presetKey));
        }

        return new ContentSource(ContentSourceKind.Preset, presetKey, (overlays ?? Enumerable.Empty<string>()).ToList(), null, presetKey, new List<ConfigurationPatch>());
    }

    // A bundle: a built preset, a snapshot (C02 Content Json) or an export.
    public static ContentSource Bundle(JsonTreeNode bundle, string where)
    {
        return new ContentSource(ContentSourceKind.Bundle, null, new List<string>(), bundle ?? throw new ArgumentNullException(nameof(bundle)), where, new List<ConfigurationPatch>());
    }

    // Any document. Only V01 checks its shape; the content rules read whatever entities it holds.
    public static ContentSource Raw(JsonTreeNode document, string where)
    {
        return new ContentSource(ContentSourceKind.Raw, null, new List<string>(), document ?? throw new ArgumentNullException(nameof(document)), where, new List<ConfigurationPatch>());
    }

    // The same source with one more patch at the end.
    public ContentSource WithPatch(ConfigurationPatch patch)
    {
        if (patch == null)
        {
            throw new ArgumentNullException(nameof(patch));
        }

        var patches = Patches.ToList();
        patches.Add(patch);
        return new ContentSource(Kind, PresetKey, Overlays.ToList(), Document, Where, patches);
    }
}

public enum ContentSourceKind
{
    Preset,
    Bundle,
    Raw,
}

// A request that cannot be composed at all, such as an unknown preset key. OP-09 answers invalid-input.
public sealed class ConfigurationInputException : Exception
{
    public ConfigurationInputException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    // An input reason code from the catalog, such as input.unknown-key.
    public string Code { get; }
}
