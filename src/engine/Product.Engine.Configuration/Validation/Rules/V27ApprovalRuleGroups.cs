using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V27: sequential approval rules in one group have unique sequence numbers, and every approval rule group of a
// decision type with Requires Approval has at least one rule that is not a consultation. A consultation answer
// never counts (OP-02, rule 9), so a group of consultations only would let nobody's approval count.
public sealed class V27ApprovalRuleGroups : ValidationRule, IContentRule
{
    public static readonly V27ApprovalRuleGroups Instance = new();

    private V27ApprovalRuleGroups()
        : base("V27", ReasonCodes.ApprovalRuleSequence, "Approval rule groups have unique sequences and a counting rule")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var requiresApproval = new HashSet<string>(
            DecisionTypeInfo.Active(context.Content).Where(t => t.RequiresApproval).Select(t => t.Key),
            StringComparer.Ordinal);
        string sequential = EngineContract.ApprovalModes.ToWire(ApprovalMode.Sequential);

        foreach (RuleGroup group in RuleGroups.ApprovalRules(context.Content))
        {
            var seen = new Dictionary<string, ContentRow>(StringComparer.Ordinal);
            foreach (ContentRow row in group.Rows.Where(r => string.Equals(RowFacts.Text(r, "approvalMode"), sequential, StringComparison.Ordinal)))
            {
                JsonTreeNode? sequence = JsonNodes.Get(row.Node, "sequence");
                if (sequence == null || !JsonSchemaSet.IsInteger(sequence))
                {
                    // V01 reports a sequential rule without a whole-number sequence.
                    continue;
                }

                string number = sequence.GetNumberText();
                if (seen.TryGetValue(number, out ContentRow? first))
                {
                    issues.Add(Issue(
                        "Approval Rules " + Q(first.KeyPath) + " and " + Q(row.KeyPath) + " are sequential rules in the group of " + group.Describe() + " and share sequence " + number
                        + ". Give each sequential rule its own sequence number.",
                        row.Entity,
                        row.KeyPath,
                        row.Pointer));
                    continue;
                }

                seen.Add(number, row);
            }

            if (requiresApproval.Contains(group.DecisionTypeKey) && group.Rows.All(r => RowFacts.IsTrue(r, "isConsultation")))
            {
                issues.Add(IssueWithCode(
                    ReasonCodes.ApprovalRuleConsultationOnly,
                    "the Approval Rule group of " + group.Describe() + " (" + group.RowList() + ") holds only consultations, and a consultation never counts, so no approval could accept the decision. "
                    + "Add a rule that is not a consultation, or retire the group.",
                    "approval-rule",
                    group.Rows[0].KeyPath,
                    group.Rows[0].Pointer));
            }
        }
    }
}
