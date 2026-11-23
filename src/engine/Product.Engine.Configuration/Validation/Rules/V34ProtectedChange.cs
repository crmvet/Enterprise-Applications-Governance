using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V34 (safety floor, error at activation): protected changes are approved under the active version's rules
// (document 21, section 4.4). A protected change alters who may change configuration: approval rules, decision
// rights or separation rules for the two configuration decision types, roles with Can Change Configuration,
// and locked settings. A draft can never lower its own bar.
//
// OP-02 creates the approval records from the version active at submission (document 21, section 4.7). At
// activation the facts say which version that was. When the active version has moved since, the approvals came from
// rules that are no longer in force, so a protected change must be submitted again. When the facts do not say, the
// rule fails loudly (document 21, rule 8). Edits to label, description and sort order only are not protected.
// The bootstrap of version 1 has no active version, so the rule does not apply to it (section 4.8).
// WORKING DEFAULT (open design question 4): the active rules name one configuration admin on one card.
public sealed class V34ProtectedChange : ValidationRule, IContentRule
{
    public static readonly V34ProtectedChange Instance = new();

    // Fields the content comparison skips: text for people, the origin stamp, and the active flag (compared on its own).
    private static readonly HashSet<string> SkippedFields = new(StringComparer.Ordinal) { "label", "description", "sortOrder", "origin", "isActive" };

    private V34ProtectedChange()
        : base("V34", ReasonCodes.ConfigProtectedChange, "Protected changes are approved under the active rules")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (context.Mode != ValidationMode.Activation || context.Active == null)
        {
            return;
        }

        List<ContentRow> changed = ProtectedChanges(context.Active, context.Content).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        int? approvals = context.Facts.ApprovalRulesVersionNumber;
        int? active = context.Facts.ActiveVersionNumber;
        string? problem = null;
        if (!approvals.HasValue || !active.HasValue)
        {
            problem = "the facts do not say which version's rules created the approvals, so the engine cannot show they are the active rules. Supply the approvals' version and the active version";
        }
        else if (approvals.Value != active.Value)
        {
            problem = "the approvals came from the rules of version " + approvals.Value + ", but version " + active.Value + " is active. Withdraw the draft and submit it again, so the active rules decide who approves";
        }

        if (problem == null)
        {
            return;
        }

        foreach (ContentRow row in changed)
        {
            issues.Add(Issue(
                row.LogicalName + " " + Q(row.KeyPath) + " is a protected change (it alters who may change configuration), and " + problem + ".",
                row.Entity,
                row.KeyPath,
                row.Pointer));
        }
    }

    // Rows of the candidate (or of the active version, when retired or gone) whose protected content differs.
    public static IEnumerable<ContentRow> ProtectedChanges(ConfigurationContent active, ConfigurationContent candidate)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentRow row in candidate.Rows.Concat(active.Rows))
        {
            if (row.Key == null || !seen.Add(row.Entity + "\u0000" + row.KeyPath))
            {
                continue;
            }

            active.TryFindRow(row.Entity, row.KeyPath, out ContentRow? before);
            candidate.TryFindRow(row.Entity, row.KeyPath, out ContentRow? after);
            if (!IsProtected(before) && !IsProtected(after))
            {
                continue;
            }

            if (!SameContent(before, after))
            {
                yield return after ?? before!;
            }
        }
    }

    private static bool IsProtected(ContentRow? row)
    {
        if (row == null)
        {
            return false;
        }

        string? type = RowFacts.Text(row, "decisionTypeKey");
        bool configurationType = type != null && WellKnown.ConfigurationDecisionTypes.Contains(type, StringComparer.Ordinal);
        switch (row.Entity)
        {
            case "approval-rule":
            case "decision-right":
                return configurationType;
            case "separation-rule":
                // A rule with no decision type applies to every type that requires approval, unless a tier or stage
                // scope keeps it away from configuration decisions, which carry neither.
                return configurationType || (type == null && !RowFacts.Has(row, "tierKey") && !RowFacts.Has(row, "targetStageKey"));
            case "role":
                return RowFacts.IsTrue(row, "canChangeConfiguration");
            case "setting":
                return RowFacts.IsTrue(row, "isLocked");
            default:
                return false;
        }
    }

    private static bool SameContent(ContentRow? before, ContentRow? after)
    {
        if (before == null || after == null)
        {
            return before == null && after == null;
        }

        if (before.IsEffectivelyActive != after.IsEffectivelyActive)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        names.UnionWith(before.Node.Members.Select(m => m.Key));
        names.UnionWith(after.Node.Members.Select(m => m.Key));
        foreach (string name in names.Where(n => !SkippedFields.Contains(n)))
        {
            JsonTreeNode? x = JsonNodes.Get(before.Node, name);
            JsonTreeNode? y = JsonNodes.Get(after.Node, name);
            bool same = x == null ? y == null : y != null && x.Equals(y);
            if (!same)
            {
                return false;
            }
        }

        return true;
    }
}
