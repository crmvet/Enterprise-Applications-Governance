using System;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V28: minimum count at least 1, with no zero waiver: the only way to accept without a card is the locked setting
// engine-acceptable-decision-types (28, issue 1). Interval days above 0. Lead, repeat, escalate, timeout and due days
// at least 0. workload-grant-max-days above 0, and every Workload Grant (C42) has an Expires On no later than that many
// days after the validation date (document 26, section 3.4). The validation date is a fact: the core reads no clock.
// Without it the expiry window is not checked in preset build or draft mode, but a grant without Expires On still fails.
// At activation a missing validation date is an error, so a grant never takes effect unchecked.
// WORKING DEFAULT (open design question 8): no zero waiver; engine acceptance only through the locked list.
// WORKING DEFAULT (open design question 12): workload grants are recorded and expiring.
public sealed class V28ValueRanges : ValidationRule, IContentRule
{
    public static readonly V28ValueRanges Instance = new();

    private V28ValueRanges()
        : base("V28", ReasonCodes.ValueRange, "Counts and day values are in range")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        foreach (ContentRow rule in RowFacts.Active(content, "approval-rule"))
        {
            if (RowFacts.TryNumber(JsonNodes.Get(rule.Node, "minimumCount"), out decimal count) && count < 1)
            {
                issues.Add(IssueWithCode(
                    ReasonCodes.ApprovalRuleZeroCount,
                    "Approval Rule " + Q(rule.KeyPath) + " has minimumCount " + RowFacts.Number(count) + ". There is no zero waiver: set it to 1 or more. "
                    + "Only the locked setting engine-acceptable-decision-types lets the engine accept without a card.",
                    rule.Entity,
                    rule.KeyPath,
                    rule.Pointer));
            }

            AtLeast(rule, "timeoutDays", 0, issues);
        }

        foreach (ContentRow cadence in RowFacts.Active(content, "review-cadence"))
        {
            AtLeast(cadence, "intervalDays", 1, issues);
            AtLeast(cadence, "leadDays", 0, issues);
            AtLeast(cadence, "escalateAfterDays", 0, issues);
        }

        foreach (ContentRow reminder in RowFacts.Active(content, "reminder-rule"))
        {
            AtLeast(reminder, "leadDays", 0, issues);
            AtLeast(reminder, "repeatDays", 0, issues);
            AtLeast(reminder, "escalateAfterDays", 0, issues);
        }

        foreach (ContentRow changeType in RowFacts.Active(content, "change-type"))
        {
            AtLeast(changeType, "reviewDueDays", 0, issues);
        }

        foreach (ContentRow policy in RowFacts.Active(content, "gate-policy"))
        {
            JsonTreeNode? emergency = JsonNodes.Get(policy.Node, "emergencyRule");
            if (emergency != null && RowFacts.TryNumber(JsonNodes.Get(emergency, "approvalDueWorkingDays"), out decimal due) && due < 0)
            {
                issues.Add(Issue(
                    "Gate Policy " + Q(policy.KeyPath) + " has an emergency rule with approvalDueWorkingDays " + RowFacts.Number(due) + ". Use 0 or more.",
                    policy.Entity,
                    policy.KeyPath,
                    policy.Pointer));
            }
        }

        CheckWorkloadGrants(context, issues);
    }

    private void CheckWorkloadGrants(ValidationContext context, ValidationIssues issues)
    {
        ContentRow? setting = RowFacts.Active(context.Content, "setting")
            .FirstOrDefault(s => string.Equals(s.KeyPath, WellKnown.WorkloadGrantMaxDays, StringComparison.Ordinal) && !RowFacts.Has(s, "portfolioId"));
        decimal? maxDays = null;
        if (setting != null && RowFacts.TryNumber(JsonNodes.Get(setting.Node, "value"), out decimal days))
        {
            if (days <= 0 || decimal.Truncate(days) != days)
            {
                issues.Add(Issue(
                    "Setting " + Q(WellKnown.WorkloadGrantMaxDays) + " is " + RowFacts.Number(days) + ". It must be a whole number above 0: every workload grant expires.",
                    setting.Entity,
                    setting.KeyPath,
                    setting.Pointer));
            }
            else
            {
                maxDays = days;
            }
        }

        foreach (ContentRow grant in RowFacts.Active(context.Content, "workload-grant"))
        {
            string? expiresText = RowFacts.Text(grant, "expiresOn");
            if (expiresText == null)
            {
                issues.Add(Issue(
                    "Workload Grant " + Q(grant.KeyPath) + " has no expiresOn. Every grant expires: set a date at most " + (maxDays.HasValue ? RowFacts.Number(maxDays.Value) : "workload-grant-max-days")
                    + " days after the day it is validated. Renewal is a new decision.",
                    grant.Entity,
                    grant.KeyPath,
                    grant.Pointer));
                continue;
            }

            if (!context.Facts.ValidatedOn.HasValue && context.Mode == ValidationMode.Activation)
            {
                // Activation is where a grant takes effect, so it never skips the expiry window. Like V34, it fails loudly.
                issues.Add(Issue(
                    "Workload Grant " + Q(grant.KeyPath) + " cannot be checked against workload-grant-max-days, because activation was given no validation date. "
                    + "Pass the validation date to OP-09 at activation.",
                    grant.Entity,
                    grant.KeyPath,
                    grant.Pointer));
                continue;
            }

            if (!maxDays.HasValue || !context.Facts.ValidatedOn.HasValue || !RowFacts.TryDate(expiresText, out DateTime expires))
            {
                // Without the setting, the validation date or a readable date, V38, the caller or V01 says what is missing.
                continue;
            }

            DateTime validatedOn = context.Facts.ValidatedOn.Value;
            DateTime latest = validatedOn.AddDays((double)maxDays.Value);
            if (expires > latest)
            {
                issues.Add(Issue(
                    "Workload Grant " + Q(grant.KeyPath) + " expires on " + expiresText + ", more than " + RowFacts.Number(maxDays.Value) + " days (workload-grant-max-days) after the validation date "
                    + RowFacts.Date(validatedOn) + ". Set expiresOn to " + RowFacts.Date(latest) + " or earlier. Renewal is a new decision.",
                    grant.Entity,
                    grant.KeyPath,
                    grant.Pointer));
            }
        }
    }

    private void AtLeast(ContentRow row, string field, int minimum, ValidationIssues issues)
    {
        if (RowFacts.TryNumber(JsonNodes.Get(row.Node, field), out decimal value) && value < minimum)
        {
            string rule = minimum == 0 ? "Use 0 or more" : "Use a number above 0";
            issues.Add(Issue(
                row.LogicalName + " " + Q(row.KeyPath) + " has " + field + " " + RowFacts.Number(value) + ". " + rule + ".",
                row.Entity,
                row.KeyPath,
                row.Pointer));
        }
    }
}
