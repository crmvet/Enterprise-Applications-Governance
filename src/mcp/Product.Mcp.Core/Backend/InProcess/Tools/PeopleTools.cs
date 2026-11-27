using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// People search (tool 61; document 24, B24-9): the pickers of the web app name a person by their name, never by an
// object ID a person would have to know.
internal static partial class ToolHandlers
{
    // ---- 61. find_people ----

    // Finds people by name among the store's Principal References (O05): every word given must start a word of the
    // display label, or the text must start the user principal name part the label holds. Users only unless the caller
    // asks for other kinds. An erased reference is never found. A person the last owner check found disabled or deleted
    // is found but marked, so a picker can say so; a leave date is HR data and is never returned here (only a platform
    // admin reads it, in list_owner_assignments). Names that start with the text come first, then the others, by name.
    //
    // Judgment call (D-41): the store's references, not a live directory search. They hold every person the product has
    // met: the seeded directory, the owner check's results and every approver. A directory search through Microsoft Graph
    // can join this read once the directory reads are live (B24-9).
    private static JsonTreeNode FindPeople(ToolScope s)
    {
        string text = s.RequireText("text").Trim();
        if (text.Length < 2)
        {
            throw s.Invalid("text", "Type at least two letters of the person's name.");
        }

        IReadOnlyList<string> kinds = s.Strings("principalKinds");
        var wanted = new HashSet<string>(kinds.Count == 0 ? new[] { "user" } : kinds, StringComparer.Ordinal);
        bool activeOnly = s.Flag("activeOnly") ?? false;
        string[] words = text.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<(StoredRow Row, string Label, bool Starts)>();
        foreach (StoredRow person in s.Rows.Query(RowQuery.For("principal-reference")))
        {
            if (person.Flag("isErased") || !wanted.Contains(person.Text("principalKind") ?? string.Empty) || person.Text("entraObjectId") == null)
            {
                continue;
            }

            string? state = person.Text("directoryState");
            if (activeOnly && (state == "disabled" || state == "deleted"))
            {
                continue;
            }

            string label = person.Text("displayLabel") ?? string.Empty;
            string lower = label.ToLowerInvariant();
            string[] labelWords = lower.Split(new[] { ' ', '\t', '-', '.', '@', '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries);
            bool all = words.All(w => labelWords.Any(l => l.StartsWith(w, StringComparison.Ordinal)));
            if (!all && !lower.StartsWith(text.ToLowerInvariant(), StringComparison.Ordinal))
            {
                continue;
            }

            matches.Add((person, label, lower.StartsWith(words[0], StringComparison.Ordinal)));
        }

        var ordered = matches.OrderBy(m => m.Starts ? 0 : 1).ThenBy(m => m.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Row.Id, StringComparer.Ordinal).ToList();
        int top = (int)Math.Min(50, Math.Max(1, Js.Number(s.Args, "top") ?? 20));
        JsonTreeNode items = Arr(ordered.Take(top).Select(m => Js.Obj(
            ("objectId", JsonTreeNode.FromString(m.Row.Text("entraObjectId")!)),
            ("displayLabel", JsonTreeNode.FromString(m.Label.Length == 0 ? m.Row.Text("entraObjectId")! : m.Label)),
            ("principalKind", JsonTreeNode.FromString(m.Row.Text("principalKind")!)),
            ("directoryState", Js.Str(m.Row.Text("directoryState"))))));
        string summary = ordered.Count == 0
            ? "No person the product knows matches \"" + text + "\"."
            : Count(ordered.Count, "person matches", "people match") + " \"" + text + "\"" + (ordered.Count > top ? "; the first " + top + " are listed. Type more of the name to narrow it." : ".");
        return s.Answer(Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(ordered.Count))), summary);
    }
}
