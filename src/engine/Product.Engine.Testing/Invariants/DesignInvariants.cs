using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Configuration.Presets;

namespace Product.Engine.Testing.Invariants;

// One rule that must hold for any configuration (document 25, section 10, "Invariant tests"). Every
// invariant in that section has a generated test. The text is the document's, word for word.
public sealed class DesignInvariant
{
    public DesignInvariant(string id, string text, string operations)
    {
        Id = id;
        Text = text;
        Operations = operations;
    }

    // INV-01 to INV-11, in the document's order. A proposal of this assembly: document 25 numbers none of them.
    public string Id { get; }

    public string Text { get; }

    // The operations whose answers the generated test checks.
    public string Operations { get; }

    public override string ToString()
    {
        return Id + " " + Text;
    }
}

// The eleven invariants of document 25, section 10, and the generator every invariant test runs over: each shipped full
// preset alone and with each allowed overlay combination (22 selections today), the same set build-presets validates.
public static class DesignInvariants
{
    public static readonly IReadOnlyList<DesignInvariant> All = new ReadOnlyCollection<DesignInvariant>(new[]
    {
        new DesignInvariant("INV-01", "an AI or workload actor never counts toward any approval", "OP-02, OP-03, OP-08"),
        new DesignInvariant("INV-02", "no decision outside the locked list is accepted without a person's card", "OP-02, OP-08"),
        new DesignInvariant("INV-03", "a consultation never counts", "OP-02, OP-03"),
        new DesignInvariant("INV-04", "a separation refusal never counts", "OP-02, OP-03"),
        new DesignInvariant("INV-05", "a recorded external change number alone never authorizes", "OP-14, OP-04"),
        new DesignInvariant("INV-06", "a missing gate policy never falls back to a level", "OP-04"),
        new DesignInvariant("INV-07", "a more serious answer never lowers the tier", "OP-01"),
        new DesignInvariant("INV-08", "Enforce never stops for window or path alone", "OP-04"),
        new DesignInvariant("INV-09", "OP-09 rejects any snapshot with the AI lock off", "OP-09, activation"),
        new DesignInvariant("INV-10", "no write outside activation changes a configuration row", "OP-08"),
        new DesignInvariant("INV-11", "the same input gives the same hash", "canonical hashing, the snapshot compiler, replay"),
    });

    public static DesignInvariant Get(string id)
    {
        return All.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("No design invariant " + id + ". Document 25, section 10 lists INV-01 to INV-11.", nameof(id));
    }

    // Every shipped full preset alone, then with each allowed overlay combination, in the catalog's order.
    public static IEnumerable<PresetSelection> Selections(PresetCatalog catalog)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        foreach (PresetSource full in catalog.FullPresets)
        {
            yield return new PresetSelection(full.Key, Array.Empty<string>());
            foreach (string[] overlays in catalog.AllowedOverlayCombinations(full))
            {
                yield return new PresetSelection(full.Key, overlays);
            }
        }
    }
}

// One preset selection: a full preset and the overlays applied after it, in order.
public sealed class PresetSelection
{
    public PresetSelection(string presetKey, IEnumerable<string> overlays)
    {
        PresetKey = presetKey ?? throw new ArgumentNullException(nameof(presetKey));
        Overlays = (overlays ?? Enumerable.Empty<string>()).ToArray();
    }

    public string PresetKey { get; }

    public string[] Overlays { get; }

    public override string ToString()
    {
        return Overlays.Length == 0 ? PresetKey : PresetKey + "+" + string.Join("+", Overlays);
    }
}

// Marks a test as the generated test of a design invariant. InvariantTests checks that every invariant has one.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class InvariantAttribute : Attribute
{
    public InvariantAttribute(string id)
    {
        Id = id;
    }

    public string Id { get; }
}
