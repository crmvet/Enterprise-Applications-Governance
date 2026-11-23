using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V33 (safety floor): locked settings are unchanged. The AI approval lock stays on and locked (decided, D-02 and
// D-10), and the engine acceptance list stays as shipped and locked. Both are compared with what this engine ships,
// so a preset cannot change them either. Any other setting locked in the active version must stay as it was.
// WORKING DEFAULT (open design question 8): the shipped acceptance list is lowest-tier intake and the standard-model
// change authorization. Extending it needs a product release.
public sealed class V33LockedSettings : ValidationRule, IContentRule
{
    public static readonly V33LockedSettings Instance = new();

    private V33LockedSettings()
        : base("V33", ReasonCodes.SettingLockedChanged, "Locked settings never change")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ContentRow? aiLock = Find(context.Content, WellKnown.AiApprovalLock);
        if (aiLock == null || JsonNodes.Get(aiLock.Node, "value")?.Kind != JsonTreeKind.True)
        {
            string state = aiLock == null ? "missing or retired" : "not on";
            issues.Add(IssueWithCode(
                ReasonCodes.SettingAiApprovalLockOff,
                "Setting " + Q(WellKnown.AiApprovalLock) + " is " + state + ". AI never approves: keep the row active with the value true.",
                "setting",
                WellKnown.AiApprovalLock,
                aiLock?.Pointer));
        }

        if (aiLock != null && !RowFacts.IsTrue(aiLock, "isLocked"))
        {
            issues.Add(Issue(
                "Setting " + Q(WellKnown.AiApprovalLock) + " is not locked. It must stay locked: set isLocked true.",
                "setting",
                WellKnown.AiApprovalLock,
                aiLock.Pointer));
        }

        ContentRow? acceptance = Find(context.Content, WellKnown.EngineAcceptableDecisionTypes);
        if (acceptance == null)
        {
            issues.Add(Issue(
                "Setting " + Q(WellKnown.EngineAcceptableDecisionTypes) + " is missing or retired. It is locked: restore the shipped row.",
                "setting",
                WellKnown.EngineAcceptableDecisionTypes,
                null));
        }
        else
        {
            if (!IsShippedList(JsonNodes.Get(acceptance.Node, "value")))
            {
                issues.Add(Issue(
                    "Setting " + Q(WellKnown.EngineAcceptableDecisionTypes) + " differs from the shipped list (" + Shipped()
                    + "). It is locked: changing or extending it needs a product release. Restore the shipped value.",
                    "setting",
                    acceptance.KeyPath,
                    acceptance.Pointer));
            }

            if (!RowFacts.IsTrue(acceptance, "isLocked"))
            {
                issues.Add(Issue(
                    "Setting " + Q(WellKnown.EngineAcceptableDecisionTypes) + " is not locked. It must stay locked: set isLocked true.",
                    "setting",
                    acceptance.KeyPath,
                    acceptance.Pointer));
            }
        }

        if (context.Active == null)
        {
            return;
        }

        // Settings the active version locks, other than the two above.
        foreach (ContentRow before in context.Active.RowsFor("setting").Where(r => r.Key != null && r.IsActive && RowFacts.IsTrue(r, "isLocked")))
        {
            if (string.Equals(before.KeyPath, WellKnown.AiApprovalLock, StringComparison.Ordinal)
                || string.Equals(before.KeyPath, WellKnown.EngineAcceptableDecisionTypes, StringComparison.Ordinal))
            {
                continue;
            }

            ContentRow? after = Find(context.Content, before.KeyPath);
            bool same = after != null
                && RowFacts.IsTrue(after, "isLocked")
                && Same(before, after, "valueType")
                && Same(before, after, "value");
            if (!same)
            {
                issues.Add(Issue(
                    "Setting " + Q(before.KeyPath) + " is locked in the active version, and this change alters or retires it. Locked settings never change: restore the row as it was.",
                    "setting",
                    before.KeyPath,
                    after?.Pointer ?? before.Pointer));
            }
        }
    }

    private static ContentRow? Find(ConfigurationContent content, string key)
    {
        return RowFacts.Active(content, "setting").FirstOrDefault(s => string.Equals(s.KeyPath, key, StringComparison.Ordinal));
    }

    private static bool Same(ContentRow a, ContentRow b, string field)
    {
        JsonTreeNode? x = JsonNodes.Get(a.Node, field);
        JsonTreeNode? y = JsonNodes.Get(b.Node, field);
        return x == null ? y == null : y != null && x.Equals(y);
    }

    // The same entries as the shipped list, in any order, with no other entry.
    private static bool IsShippedList(JsonTreeNode? value)
    {
        if (value == null || value.Kind != JsonTreeKind.Array || value.Items.Count != WellKnown.ShippedEngineAcceptance.Length)
        {
            return false;
        }

        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonTreeNode item in value.Items)
        {
            if (item.Kind != JsonTreeKind.Object || item.Members.Count != 2)
            {
                return false;
            }

            entries.Add(JsonNodes.GetString(item, "decisionTypeKey") + "\u0000" + JsonNodes.GetString(item, "condition"));
        }

        return WellKnown.ShippedEngineAcceptance.All(pair => entries.Contains(pair.Key + "\u0000" + pair.Value));
    }

    private static string Shipped()
    {
        return string.Join(", ", WellKnown.ShippedEngineAcceptance.Select(pair => Q(pair.Key) + " with " + Q(pair.Value)));
    }
}
