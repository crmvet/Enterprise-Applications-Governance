using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Helpers the tool handlers share: application facts, owners and their gaps, list values and record visibility.
internal static partial class ToolHandlers
{
    private static readonly string[] OpenRecordMeanings = { "draft", "submitted", "in-progress" };

    // The application's display name: Name, else the record number.
    private static string AppName(StoredRow app)
    {
        return app.Text("name") ?? app.Text("recordNumber") ?? app.Id;
    }

    private static StoredRow RequireApplication(ToolScope s, string field = "businessApplicationId")
    {
        return s.RequireRow("business-application", s.Text(field), field);
    }

    // A C07 list value: the column holds the value key, the list is fixed per column.
    private static JsonTreeNode ListValue(ToolScope s, string list, string? key)
    {
        if (key == null)
        {
            return JsonTreeNode.Null;
        }

        SnapshotRow? row = s.ConfigRow("list-value", list + "/" + key);
        return Js.Obj(("key", JsonTreeNode.FromString(key)), ("label", JsonTreeNode.FromString(row == null ? key : Js.Text(row.Columns, "label") ?? key)));
    }

    private static void RequireListValue(ToolScope s, string list, string? key, string field)
    {
        if (key == null)
        {
            return;
        }

        SnapshotRow? row = s.ConfigRow("list-value", list + "/" + key);
        if (row == null || !row.IsActive)
        {
            throw s.Invalid(field, field + " \"" + key + "\" is not an active value of the " + list + " list.");
        }
    }

    private static string? LifecycleMeaning(ToolScope s, string? stageKey)
    {
        return s.ConfigRow("lifecycle-stage", stageKey)?.EngineMeaning;
    }

    private static long StageRank(ToolScope s, string? stageKey)
    {
        SnapshotRow? row = s.ConfigRow("lifecycle-stage", stageKey);
        return row == null ? long.MaxValue : Js.Number(row.Columns, "stageRank") ?? long.MaxValue;
    }

    // The holder of an owner role, not a deputy, as a principalRef or null.
    private static JsonTreeNode OwnerRef(ToolScope s, IReadOnlyList<StoredRow> owners, string ownerRoleKey)
    {
        StoredRow? holder = owners.FirstOrDefault(o => o.Text("ownerRole") == ownerRoleKey && !o.Flag("isDeputy"));
        return holder == null ? JsonTreeNode.Null : s.PrincipalRef(holder.Text("principal"));
    }

    private static JsonTreeNode OwnerAssignment(ToolScope s, StoredRow row)
    {
        string role = row.Text("ownerRole") ?? string.Empty;
        return Js.Obj(
            ("ownerAssignmentId", JsonTreeNode.FromString(row.Id)),
            ("ownerRoleKey", JsonTreeNode.FromString(role)),
            ("label", JsonTreeNode.FromString(s.Label("owner-role", role))),
            ("principal", s.PrincipalRef(row.Text("principal"))),
            ("isDeputy", JsonTreeNode.FromBoolean(row.Flag("isDeputy"))),
            ("deputyFor", Js.Str(row.Text("deputyFor"))),
            ("startOn", ToolScope.DateOrNull(row.Text("startOn"))),
            ("endOn", ToolScope.DateOrNull(row.Text("endOn"))),
            ("ownershipDecisionId", Js.Str(row.Text("ownershipDecision"))));
    }

    // The owner roles a gate requires a holder for (owner-assigned gate requirements), and the roles that need a deputy
    // once held (C28 Requires Deputy). Judgment call: a gap is a required role with no holder, or a held role with
    // Requires Deputy and no deputy. Other roles may stay empty.
    private static List<(string Role, string Missing)> OwnerGaps(ToolScope s, IReadOnlyList<StoredRow> owners)
    {
        var required = new SortedSet<string>(
            s.ActiveRows("gate-requirement").Where(r => Js.Text(r.Columns, "requirementKind") == "owner-assigned").Select(r => Js.Text(r.Columns, "requiredOwnerRoleKey")).Where(k => k != null).Select(k => k!),
            StringComparer.Ordinal);
        var gaps = new List<(string, string)>();
        foreach (SnapshotRow role in s.ActiveRows("owner-role").OrderBy(r => r.KeyPath, StringComparer.Ordinal))
        {
            bool held = owners.Any(o => o.Text("ownerRole") == role.KeyPath && !o.Flag("isDeputy"));
            bool deputy = owners.Any(o => o.Text("ownerRole") == role.KeyPath && o.Flag("isDeputy"));
            if (!held && required.Contains(role.KeyPath))
            {
                gaps.Add((role.KeyPath, "holder"));
            }
            else if (held && !deputy && Js.Flag(role.Columns, "requiresDeputy") == true)
            {
                gaps.Add((role.KeyPath, "deputy"));
            }
        }

        return gaps;
    }

    // The application a record belongs to: its own column, or through its intake request or change request.
    private static string? ApplicationOf(ToolScope s, StoredRow row)
    {
        string? direct = row.Text("businessApplication") ?? row.Text("subjectApplication");
        if (direct != null)
        {
            return direct;
        }

        string? intake = row.Text("subjectIntakeRequest") ?? row.Text("intakeRequest");
        if (intake != null)
        {
            return s.Rows.Get("intake-request", intake)?.Text("businessApplication");
        }

        string? change = row.Text("subjectChangeRequest") ?? row.Text("changeRequest");
        return change == null ? null : s.Rows.Get("change-request", change)?.Text("businessApplication");
    }

    // intake-visibility (document 22; document 26, section 5.2). "own": the requester, plus PMO, platform admin and
    // architect. Any other value reads as open.
    private static bool CanReadIntake(ToolScope s, StoredRow intake)
    {
        if (s.Setting("intake-visibility") != "own")
        {
            return true;
        }

        return string.Equals(intake.Text("requester"), s.PersonId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(intake.Text("createdByPrincipal"), s.PersonId, StringComparison.OrdinalIgnoreCase)
            || s.HoldsRole("pmo") || s.HoldsRole("platform-admin") || s.HoldsRole("architect");
    }

    // claim-visibility "admins-and-claimant": platform admin, AI lead, and the suggested claimant or the claimant.
    private static bool CanReadClaim(ToolScope s, StoredRow claim)
    {
        if (s.Setting("claim-visibility") != "admins-and-claimant")
        {
            return true;
        }

        return s.HoldsRole("platform-admin") || s.HoldsRole("ai-lead")
            || string.Equals(claim.Text("suggestedClaimant"), s.PersonId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(claim.Text("claimedBy"), s.PersonId, StringComparison.OrdinalIgnoreCase);
    }

    // The portfolio and its parents, nearest first (the portfolio path OP-02 reads).
    private static List<string> PortfolioPath(ToolScope s, string? portfolioId)
    {
        var path = new List<string>();
        while (portfolioId != null && !path.Contains(portfolioId))
        {
            path.Add(portfolioId);
            portfolioId = s.Rows.Get("portfolio", portfolioId)?.Text("parentPortfolio");
        }

        return path;
    }

    private static bool IsOpen(StoredRow row)
    {
        return OpenRecordMeanings.Contains(row.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal);
    }

    private static bool Same(string? a, string? b)
    {
        return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonTreeNode Bucket(string? key, string label, int count)
    {
        return Js.Obj(("key", key == null ? JsonTreeNode.Null : JsonTreeNode.FromString(key)), ("label", JsonTreeNode.FromString(label)), ("count", JsonTreeNode.FromInt64(count)));
    }

    // The JSON array of a list of objects.
    private static JsonTreeNode Arr(IEnumerable<JsonTreeNode> items)
    {
        return Js.Arr(items);
    }

    private static JsonTreeNode Ids(IEnumerable<string> ids)
    {
        return Js.Strs(ids);
    }

    // Lowercase words of two letters or more, for the duplicate check.
    private static HashSet<string> Words(string? text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        if (text == null)
        {
            return words;
        }

        var current = new System.Text.StringBuilder();
        foreach (char c in text.ToLowerInvariant() + " ")
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else
            {
                if (current.Length >= 2)
                {
                    words.Add(current.ToString());
                }

                current.Clear();
            }
        }

        return words;
    }
}
