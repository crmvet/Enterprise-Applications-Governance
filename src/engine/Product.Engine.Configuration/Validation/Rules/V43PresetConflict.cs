using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Presets;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V43: no two overlays that list each other in conflictsWith are applied together (document 21, section 6.7).
// One listing is enough: small team lists strict separation, so the two never combine, whichever lists which.
// A full preset may list overlays it cannot take, and those count too.
public sealed class V43PresetConflict : ValidationRule
{
    public static readonly V43PresetConflict Instance = new();

    private V43PresetConflict()
        : base("V43", ReasonCodes.PresetConflict, "Conflicting overlays are not combined")
    {
    }

    public void CheckSelection(PresetSource full, IReadOnlyList<PresetSource> overlays, ValidationIssues issues)
    {
        var chosen = new List<PresetSource> { full };
        chosen.AddRange(overlays);
        for (int i = 0; i < chosen.Count; i++)
        {
            for (int j = i + 1; j < chosen.Count; j++)
            {
                PresetSource a = chosen[i];
                PresetSource b = chosen[j];
                bool aListsB = a.ConflictsWith.Contains(b.Key, StringComparer.Ordinal);
                bool bListsA = b.ConflictsWith.Contains(a.Key, StringComparer.Ordinal);
                if (!aListsB && !bListsA)
                {
                    continue;
                }

                string who = aListsB ? Q(a.Key) + " lists " + Q(b.Key) : Q(b.Key) + " lists " + Q(a.Key);
                issues.Add(Issue(
                    "presets " + Q(a.Key) + " and " + Q(b.Key) + " conflict (" + who + " in conflictsWith), so they cannot be applied together. Pick one of them.",
                    null,
                    b.Key,
                    b.Key + "/preset.json"));
            }
        }
    }
}
