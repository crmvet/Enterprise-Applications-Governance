using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Product.Azure.Stores;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

/// <summary>
/// Builds what the approval runner puts on one approval card, from the stored Approval Record, its Decision and the
/// configuration: a title and details the product writes (text a person or an AI client wrote is escaped, so it cannot
/// add links or formatting), the record link, who to ask, and the configuration keys the runner sends back with the
/// answer. It decides nothing about approvals: the engine already resolved the rules when it opened the record, and it
/// counts the answer when the runner posts it.
/// </summary>
internal sealed class ApprovalRequestCards
{
    private const int MaxTitle = 200;
    private const int MaxStatement = 1000;
    private const int MaxContext = 2000;

    private readonly IRecordReader _records;
    private readonly CompiledSnapshot _current;
    private readonly Func<int, CompiledSnapshot?> _version;
    private readonly string? _recordUrlBase;

    public ApprovalRequestCards(IRecordReader records, CompiledSnapshot current, Func<int, CompiledSnapshot?> version, string recordUrlBase)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _version = version ?? throw new ArgumentNullException(nameof(version));
        _recordUrlBase = Uri.TryCreate(recordUrlBase, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps ? recordUrlBase.TrimEnd('/') : null;
    }

    /// <summary>
    /// The outbox state for a claim candidate that should get no card, or null when it gets one: the record holds an
    /// outcome (answered, expired, cancelled), it passed its Expires On (expired), or its decision is no longer proposed
    /// (cancelled: the engine does not cancel open records yet, so the outbox stops here).
    /// </summary>
    public static string? SettledState(StoredRow? record, StoredRow? decision, DateTime nowUtc)
    {
        if (record == null)
        {
            return ApprovalOutboxStates.Cancelled;
        }

        string? settled = ApprovalOutboxStates.ForOutcomeMeaning(record.Text("outcomeMeaning"));
        if (settled != null)
        {
            return settled;
        }

        DateTime? expires = RecordEndpoints.Moment(record.Text("expiresOn"));
        if (expires.HasValue && nowUtc >= expires.Value)
        {
            return ApprovalOutboxStates.Expired;
        }

        return decision == null || decision.Text("statusMeaning") != "proposed" ? ApprovalOutboxStates.Cancelled : null;
    }

    /// <summary>One claimed item, as the claims route returns it.</summary>
    public JsonTreeNode Item(ApprovalOutboxItem claimed, StoredRow record, StoredRow decision, DateTime nowUtc)
    {
        CompiledSnapshot stamped = Stamped(decision);
        string? ruleKey = record.Text("approvalRule");
        SnapshotRow? rule = ruleKey == null ? null : Row(stamped, "approval-rule", ruleKey);
        bool consultation = record.Flag("isConsultation");
        bool typedReason = rule != null && Flag(rule, "requiresTypedReason");
        string typeLabel = Label(stamped, "decision-type", decision.Text("decisionType")) ?? "Decision";
        string subject = SubjectLabel(decision) ?? decision.Text("recordNumber") ?? "a record";
        DateTime? requested = RecordEndpoints.Moment(record.Text("requestedOn"));
        DateTime? expires = RecordEndpoints.Moment(record.Text("expiresOn"));

        return Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(record.Id)),
            ("claimId", JsonTreeNode.FromString(claimed.ClaimId ?? string.Empty)),
            ("decisionId", JsonTreeNode.FromString(decision.Id)),
            ("title", JsonTreeNode.FromString(Title(consultation ? "Consultation" : "Approval requested", typeLabel, subject))),
            ("detailsMarkdown", JsonTreeNode.FromString(Details(decision, typeLabel, subject, typedReason, consultation, requested, expires))),
            ("recordUrl", _recordUrlBase == null ? JsonTreeNode.Null : JsonTreeNode.FromString(_recordUrlBase + "/decision/" + Uri.EscapeDataString(decision.Id))),
            ("requestedOn", requested.HasValue ? Js.Utc(requested.Value) : JsonTreeNode.Null),
            ("expiresOn", expires.HasValue ? Js.Utc(expires.Value) : JsonTreeNode.Null),
            ("requiresTypedReason", JsonTreeNode.FromBoolean(typedReason)),
            ("isConsultation", JsonTreeNode.FromBoolean(consultation)),
            ("channel", JsonTreeNode.FromString(Channel(record))),
            ("ask", Ask(record, decision, nowUtc)),
            ("answerHints", AnswerHints()));
    }

    // ---- Who to ask (document 26, AN-04) ----

    // An owner role: the current holders in Owner Assignment for the decision's application, users only (a Principal
    // Reference that records another kind, or an erased one, is left out). A product role: the Entra groups bound to it
    // in the configuration in force; the runner lists their members and keeps users only.
    private JsonTreeNode Ask(StoredRow record, StoredRow decision, DateTime nowUtc)
    {
        string? ownerRole = record.Text("requiredOwnerRole");
        string? role = record.Text("requiredRole");
        var principals = new List<string>();
        var groups = new List<string>();
        if (ownerRole != null)
        {
            string? application = ApplicationOf(_records, decision);
            if (application != null)
            {
                List<StoredRow> current = CurrentAssignments(_records, application, ownerRole, nowUtc);
                List<StoredRow> holders = current.Where(a => !a.Flag("isDeputy")).ToList();
                foreach (StoredRow assignment in holders.Count > 0 ? holders : current)
                {
                    string? principal = assignment.Text("principal");
                    if (principal != null && IsAskableUser(principal) && !principals.Contains(principal, StringComparer.OrdinalIgnoreCase))
                    {
                        principals.Add(principal);
                    }
                }
            }
        }
        else if (role != null && _current.Snapshot.TryGetEntity("role-group-binding", out SnapshotEntity? bindings))
        {
            foreach (SnapshotRow binding in bindings.ActiveRows.OrderBy(b => b.KeyPath, StringComparer.Ordinal))
            {
                string? group = Text(binding, "entraGroupObjectId");
                if (string.Equals(Text(binding, "roleKey"), role, StringComparison.Ordinal) && group != null && !groups.Contains(group, StringComparer.OrdinalIgnoreCase))
                {
                    groups.Add(group);
                }
            }
        }

        return Js.Obj(
            ("kind", JsonTreeNode.FromString(ownerRole != null ? "owner-role" : "role")),
            ("roleKey", Js.Str(ownerRole ?? role)),
            ("principals", Js.Strs(principals)),
            ("groups", Js.Strs(groups)));
    }

    private bool IsAskableUser(string objectId)
    {
        StoredRow? reference = _records.Query(RowQuery.For("principal-reference").Where("entraObjectId", objectId)).FirstOrDefault();
        if (reference == null)
        {
            // No reference: the outcome route checks the responder again when the answer comes back.
            return true;
        }

        return !reference.Flag("isErased") && string.Equals(reference.Text("principalKind"), "user", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the principal holds the owner role on the decision's application on the day of <paramref name="onUtc"/>:
    /// a current Owner Assignment for that role, as holder or deputy. The approval outcome route checks the runner's
    /// Approver Holds Required Role against it, because for an owner role the product itself knows the answer.
    /// </summary>
    public static bool HoldsOwnerRole(IRecordReader records, StoredRow decision, string ownerRole, string principal, DateTime onUtc)
    {
        string? application = ApplicationOf(records, decision);
        return application != null
            && CurrentAssignments(records, application, ownerRole, onUtc).Any(a => string.Equals(a.Text("principal"), principal, StringComparison.OrdinalIgnoreCase));
    }

    private static List<StoredRow> CurrentAssignments(IRecordReader records, string application, string ownerRole, DateTime onUtc)
    {
        DateTime day = DateTime.SpecifyKind(onUtc.Date, DateTimeKind.Utc);
        return records.Query(RowQuery.For("owner-assignment").Where("businessApplication", application).Where("ownerRole", ownerRole))
            .Where(a => IsCurrent(a, day))
            .ToList();
    }

    private static bool IsCurrent(StoredRow assignment, DateTime today)
    {
        DateTime? start = RecordEndpoints.Moment(assignment.Text("startOn"));
        DateTime? end = RecordEndpoints.Moment(assignment.Text("endOn"));
        return (!start.HasValue || start.Value <= today) && (!end.HasValue || end.Value > today);
    }

    private static string? ApplicationOf(IRecordReader records, StoredRow decision)
    {
        string? application = decision.Text("subjectApplication");
        if (application != null)
        {
            return application;
        }

        foreach ((string column, string entity) in new[] { ("subjectChangeRequest", "change-request"), ("subjectIntakeRequest", "intake-request"), ("subjectDeploymentRecord", "deployment-record"), ("subjectComponent", "technical-component") })
        {
            string? id = decision.Text(column);
            string? found = id == null ? null : records.Get(entity, id)?.Text("businessApplication");
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    // ---- What the runner sends back ----

    // The configuration keys of the answer: the actor kind whose meaning is person, and the approval record statuses
    // whose meanings are approved and rejected. The runner never sends a meaning; the engine copies it from the key.
    private JsonTreeNode AnswerHints()
    {
        return Js.Obj(
            ("approverActorKind", Js.Str(KeyFor("actor-kind", "person", null))),
            ("approverPrincipalKind", JsonTreeNode.FromString("user")),
            ("approvedOutcome", Js.Str(KeyFor("status", "approved", "approval-record"))),
            ("rejectedOutcome", Js.Str(KeyFor("status", "rejected", "approval-record"))));
    }

    // The single active row of an entity with this engine meaning (for statuses, of this entity; the default one when
    // several), as the engine picks its own status keys.
    private string? KeyFor(string entity, string meaning, string? appliesTo)
    {
        if (!_current.Snapshot.TryGetEntity(entity, out SnapshotEntity? rows))
        {
            return null;
        }

        List<SnapshotRow> matches = rows.ActiveRows
            .Where(r => r.EngineMeaning == meaning && (appliesTo == null || Text(r, "appliesToEntity") == appliesTo))
            .ToList();
        List<SnapshotRow> defaults = matches.Where(r => Flag(r, "isDefaultForMeaning")).ToList();
        SnapshotRow? pick = defaults.Count == 1 ? defaults[0] : matches.Count >= 1 ? matches.OrderBy(r => r.KeyPath, StringComparer.Ordinal).First() : null;
        return pick?.KeyPath;
    }

    // The card's channel as the MCP tools report it when they open the record (submit_record's approvalsRequested): the
    // record's own channel, else the reminder-channel setting in force, else Teams.
    private string Channel(StoredRow record)
    {
        string? channel = record.Text("channel");
        if (channel == null)
        {
            SnapshotRow? setting = Row(_current, "setting", "reminder-channel");
            channel = setting != null && setting.IsActive ? Text(setting, "value") : null;
        }

        return channel ?? "teams";
    }

    // ---- The card's text ----

    private static string Title(string lead, string typeLabel, string subject)
    {
        string text = lead + ": " + Plain(typeLabel) + " for " + Plain(subject);
        return text.Length <= MaxTitle ? text : text.Substring(0, MaxTitle - 3) + "...";
    }

    private static string Details(StoredRow decision, string typeLabel, string subject, bool typedReason, bool consultation, DateTime? requested, DateTime? expires)
    {
        var text = new StringBuilder();
        text.Append("**Decision type:** ").Append(Markdown(typeLabel, 200)).Append("  \n");
        text.Append("**Subject:** ").Append(Markdown(subject, 300)).Append("  \n");
        string? number = decision.Text("recordNumber");
        if (number != null)
        {
            text.Append("**Record:** ").Append(Markdown(number, 64)).Append("  \n");
        }

        string? statement = decision.Text("statement");
        if (statement != null)
        {
            text.Append("\n**Statement**\n\n").Append(Markdown(statement, MaxStatement)).Append('\n');
        }

        string? context = decision.Text("context");
        if (context != null)
        {
            text.Append("\n**Context**\n\n").Append(Markdown(context, MaxContext)).Append('\n');
        }

        text.Append('\n');
        if (decision.Flag("isAiCreated"))
        {
            text.Append("An AI client drafted this decision for a person. Read it before you answer.\n\n");
        }

        if (consultation)
        {
            text.Append("This is a consultation: your answer is recorded as a recommendation and does not decide.\n\n");
        }

        if (typedReason)
        {
            text.Append("A reason is required: write it in the comments when you answer, or your answer does not count.\n\n");
        }

        if (requested.HasValue)
        {
            text.Append("Requested on ").Append(When(requested.Value)).Append(" UTC.");
        }

        if (expires.HasValue)
        {
            text.Append(requested.HasValue ? " " : string.Empty).Append("Expires on ").Append(When(expires.Value)).Append(" UTC.");
        }

        text.Append("\n\nOpen the record in the product for the full context. Only your own answer counts, and only while you hold the role this card asks for.");
        return text.ToString();
    }

    private static string When(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Text a person or an AI client wrote, made safe for an approval's Markdown details: control characters dropped,
    /// clipped, and every character the Markdown syntax uses escaped with a backslash, so the text cannot add a link, an
    /// image, a heading or emphasis.
    /// </summary>
    internal static string Markdown(string text, int max)
    {
        string plain = text.Replace("\r\n", "\n").Replace('\r', '\n');
        bool clipped = plain.Length > max;
        if (clipped)
        {
            plain = plain.Substring(0, Math.Max(0, max - 3));
        }

        var result = new StringBuilder(plain.Length + 16);
        foreach (string line in plain.Split('\n'))
        {
            if (result.Length > 0)
            {
                result.Append("  \n");
            }

            foreach (char c in line)
            {
                if (char.IsControl(c))
                {
                    continue;
                }

                if ("\\`*_{}[]()#+-.!|<>~".IndexOf(c) >= 0)
                {
                    result.Append('\\');
                }

                result.Append(c);
            }
        }

        return clipped ? result.Append("...").ToString() : result.ToString();
    }

    // A one-line text: control characters become spaces, runs of spaces become one.
    internal static string Plain(string text)
    {
        var result = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            bool blank = char.IsControl(c) || char.IsWhiteSpace(c);
            if (blank)
            {
                if (!space && result.Length > 0)
                {
                    result.Append(' ');
                }

                space = true;
                continue;
            }

            result.Append(c);
            space = false;
        }

        return result.ToString().TrimEnd();
    }

    private static string Clip(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
    }

    private string? SubjectLabel(StoredRow decision)
    {
        foreach ((string column, string entity) in new[]
        {
            ("subjectApplication", "business-application"), ("subjectIntakeRequest", "intake-request"), ("subjectChangeRequest", "change-request"),
            ("subjectConfigurationDraft", "configuration-draft"), ("subjectComponent", "technical-component"), ("subjectClaim", "claim"),
            ("subjectDeploymentRecord", "deployment-record"),
        })
        {
            string? id = decision.Text(column);
            StoredRow? subject = id == null ? null : _records.Get(entity, id);
            if (subject == null)
            {
                continue;
            }

            string? label = subject.Text("title") ?? subject.Text("name") ?? subject.Text("changeSummary") ?? subject.Text("labelAsSeen") ?? subject.Text("recordNumber");
            if (label != null)
            {
                return label;
            }
        }

        return null;
    }

    // ---- Configuration reads ----

    private CompiledSnapshot Stamped(StoredRow decision)
    {
        long? stamped = decision.Number("configurationSetVersion");
        if (!stamped.HasValue || stamped.Value == _current.VersionNumber || stamped.Value < 1 || stamped.Value > int.MaxValue)
        {
            return _current;
        }

        return _version((int)stamped.Value) ?? _current;
    }

    private static SnapshotRow? Row(CompiledSnapshot snapshot, string entity, string key)
    {
        return snapshot.Snapshot.TryGetRow(entity, key, out SnapshotRow? row) ? row : null;
    }

    private static string? Label(CompiledSnapshot snapshot, string entity, string? key)
    {
        SnapshotRow? row = key == null ? null : Row(snapshot, entity, key);
        return row == null ? key : Text(row, "label") ?? key;
    }

    private static string? Text(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    private static bool Flag(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
    }
}
