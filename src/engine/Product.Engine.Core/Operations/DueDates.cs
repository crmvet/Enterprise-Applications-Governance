using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// OP-11 input (document 25, section 5.12): one due item, such as a review (O21), a document (O19), an approval
// request (O10) or a claim (O23). The adapter calls once per item in the daily due-state refresh (document 23, S2).
public sealed class DueRequest
{
    public DueRequest(string subjectKind, DateTime? dueOnUtc, DateTime? doneOnUtc = null, string? reviewTypeKey = null, string? tierKey = null)
    {
        if (!EngineKeys.ReminderSubjectKinds.Contains(subjectKind, StringComparer.Ordinal))
        {
            throw new ArgumentException("A subject kind is one of " + string.Join(", ", EngineKeys.ReminderSubjectKinds) + ".", nameof(subjectKind));
        }

        if ((reviewTypeKey == null) != (tierKey == null))
        {
            throw new ArgumentException("A review cadence is found by review type and tier together.", nameof(reviewTypeKey));
        }

        if (reviewTypeKey != null && (!ConfigurationKey.IsValid(reviewTypeKey) || !ConfigurationKey.IsValid(tierKey!)))
        {
            throw new ArgumentException("Review type and tier keys must use the configuration key format.", nameof(reviewTypeKey));
        }

        if (reviewTypeKey != null && subjectKind != EngineKeys.ReviewSubjectKind)
        {
            throw new ArgumentException("A review type is given only for a review.", nameof(reviewTypeKey));
        }

        if (dueOnUtc == null && doneOnUtc == null)
        {
            throw new ArgumentException("An open item needs its due date.", nameof(dueOnUtc));
        }

        SubjectKind = subjectKind;
        DueOnUtc = Utc(dueOnUtc, nameof(dueOnUtc));
        DoneOnUtc = Utc(doneOnUtc, nameof(doneOnUtc));
        ReviewTypeKey = reviewTypeKey;
        TierKey = tierKey;
    }

    // What is due: review, document, approval or claim (C26 Subject Kind).
    public string SubjectKind { get; }

    // When the item is due. Required while it is open.
    public DateTime? DueOnUtc { get; }

    // When it was done. A done item needs no reminder; a done review gets its next due date.
    public DateTime? DoneOnUtc { get; }

    // For a review: its type and the application's tier, which pick the C25 Review Cadence.
    public string? ReviewTypeKey { get; }

    public string? TierKey { get; }

    public static DueRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "subjectKind", "dueOnUtc", "doneOnUtc", "reviewTypeKey", "tierKey");
        string subjectKind = wire.RequiredKey("subjectKind");
        if (!EngineKeys.ReminderSubjectKinds.Contains(subjectKind, StringComparer.Ordinal))
        {
            throw OperationStop.Input(
                ReasonCodes.InputContractValueUnknown,
                "input.subjectKind is \"" + subjectKind + "\", which is not one of " + string.Join(", ", EngineKeys.ReminderSubjectKinds) + ".");
        }

        string? reviewTypeKey = wire.OptionalKey("reviewTypeKey");
        string? tierKey = wire.OptionalKey("tierKey");
        if (reviewTypeKey != null && subjectKind != EngineKeys.ReviewSubjectKind)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, "input.reviewTypeKey is given only for subject kind review.");
        }

        if ((reviewTypeKey == null) != (tierKey == null))
        {
            throw OperationStop.Input(
                reviewTypeKey == null ? ReasonCodes.InputMalformed : ReasonCodes.InputRequired,
                reviewTypeKey == null ? "input.tierKey is given only with input.reviewTypeKey." : "input.tierKey is required with input.reviewTypeKey, because the cadence depends on the tier.");
        }

        DateTime? dueOn = wire.OptionalUtc("dueOnUtc");
        DateTime? doneOn = wire.OptionalUtc("doneOnUtc");
        if (dueOn == null && doneOn == null)
        {
            throw OperationStop.Input(ReasonCodes.InputRequired, "input.dueOnUtc is required while the item is open (no doneOnUtc).");
        }

        return new DueRequest(subjectKind, dueOn, doneOn, reviewTypeKey, tierKey);
    }

    private static DateTime? Utc(DateTime? value, string name)
    {
        if (value.HasValue && value.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Every moment is a UTC date-time (DateTimeKind.Utc).", name);
        }

        return value;
    }
}

// One action for today, with the reminder rule that caused it. Flows send it (D-10); the engine only says what is due.
public sealed class ReminderStep
{
    public ReminderStep(ReminderAction action, string ruleKey, int reminderNumber, bool escalated, string? channelKey)
    {
        Action = action;
        RuleKey = ruleKey;
        ReminderNumber = reminderNumber;
        Escalated = escalated;
        ChannelKey = channelKey;
    }

    // remind (the first), repeat, or escalate (to the deputy).
    public ReminderAction Action { get; }

    // The C26 Reminder Rule.
    public string RuleKey { get; }

    // The number of this step among the rule's steps for the item, from 1. Flows key on the item ID, the rule and this
    // number, so a rerun never sends twice (document 23, S2).
    public int ReminderNumber { get; }

    // True from the escalation day on: the message goes to the deputy (document 23, S2 step 3).
    public bool Escalated { get; }

    // The rule's channel, or the reminder-channel setting when the rule names none.
    public string? ChannelKey { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("action", EngineContract.ReminderActions.ToWire(Action))
            .Add("ruleKey", RuleKey)
            .Add("reminderNumber", ReminderNumber)
            .Add("escalated", Escalated)
            .Add("channelKey", ChannelKey)
            .Build();
    }
}

// OP-11 output (document 25, section 5.12).
public sealed class DueResult : IOperationPayload
{
    public DueResult(string subjectKind, string? cadenceKey, DateTime? dueOnUtc, DateTime? nextDueOnUtc, bool isOverdue, IEnumerable<ReminderStep> actions, DateTime? nextActionOnUtc)
    {
        SubjectKind = subjectKind;
        CadenceKey = cadenceKey;
        DueOnUtc = dueOnUtc;
        NextDueOnUtc = nextDueOnUtc;
        IsOverdue = isOverdue;
        Actions = new ReadOnlyCollection<ReminderStep>(actions.ToList());
        NextActionOnUtc = nextActionOnUtc;
    }

    public string SubjectKind { get; }

    // The C25 Review Cadence used, for a review that has one at its tier.
    public string? CadenceKey { get; }

    // The due date the reminders count from. Null for a done item.
    public DateTime? DueOnUtc { get; }

    // For a done review with a cadence: the done date plus the cadence's Interval Days (O21 Next Due On).
    public DateTime? NextDueOnUtc { get; }

    // True when the item is open and asOfUtc is after its due date.
    public bool IsOverdue { get; }

    // Today's actions, by rule key.
    public IReadOnlyList<ReminderStep> Actions { get; }

    // The first day after today with an action of any rule (UTC midnight). Null when none follows.
    public DateTime? NextActionOnUtc { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("subjectKind", SubjectKind)
            .Add("cadenceKey", CadenceKey)
            .Add("dueOnUtc", DueOnUtc)
            .Add("nextDueOnUtc", NextDueOnUtc)
            .Add("isOverdue", IsOverdue)
            .Add("actions", WireWriter.Array(Actions.Select(step => step.ToJson())))
            .Add("nextActionOnUtc", NextActionOnUtc)
            .Build();
    }
}

// OP-11 Compute due dates and reminders (document 25, section 5.12; document 23, S2).
// - A done review gets its next due date: the done date plus the Interval Days of the C25 Review Cadence for its
//   type and tier. A done item gets no reminders.
// - An open item gets today's actions from every active C26 Reminder Rule of its subject kind: the first reminder
//   Lead Days before the due date, repeats every Repeat Days after it, and on Escalate After Days past the due date
//   the escalation to the deputy, which replaces any reminder that falls on the same day. From then on the messages go
//   to the deputy. Days are whole UTC calendar days (a proposal: C26 says days, where C20 says working days).
// - For a review, the cadence's Lead Days and Escalate After Days, when set, replace the rule's: the cadence is the
//   more specific row (per review type and tier). Repeat Days and the channel always come from the rule.
// - "Today" is the UTC date of asOfUtc. An action is listed only on its own day (document 25: "for today a list of
//   actions"); a day the refresh did not run is not caught up (a proposal).
public static class DueDatesOperation
{
    private const string Cadence = "review-cadence";
    private const string Rule = "reminder-rule";

    public static EvaluationResult<DueResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, DueRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, request));
    }

    public static EvaluationResult<DueResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, DueRequest.FromJson(input)));
    }

    private static OperationAnswer<DueResult> Answer(ConfigurationSnapshot snapshot, EvaluationContext context, DueRequest request)
    {
        var reasons = new List<Reason>();
        var trace = new List<RuleTrace>();
        SnapshotRow? cadence = null;
        if (request.ReviewTypeKey != null)
        {
            SnapshotRead.RequireExisting(snapshot, "review-type", request.ReviewTypeKey, "input.reviewTypeKey");
            SnapshotRead.RequireExisting(snapshot, "tier", request.TierKey!, "input.tierKey");
            cadence = FindCadence(snapshot, request.ReviewTypeKey, request.TierKey!);
            if (cadence == null)
            {
                reasons.Add(new Reason(
                    ReasonCodes.ReviewNoCadence,
                    ReasonCatalog.Get(ReasonCodes.ReviewNoCadence).DefaultSeverity,
                    "Review type " + SnapshotRead.Quote(request.ReviewTypeKey) + " has no active cadence at tier " + SnapshotRead.Quote(request.TierKey!) + ", so no next due date follows from it.",
                    "Review Type",
                    request.ReviewTypeKey));
            }
            else
            {
                trace.Add(new RuleTrace("Review Cadence", cadence.Key, RuleTraceRole.Matched));
            }
        }

        if (request.DoneOnUtc.HasValue)
        {
            DateTime? nextDue = null;
            if (cadence != null)
            {
                int interval = Days(cadence, Cadence, "intervalDays", minimum: 1)
                    ?? throw OperationStop.Broken(
                        ReasonCodes.ShapeSchema,
                        "Review Cadence " + SnapshotRead.Quote(cadence.Key) + " has no intervalDays, so the configuration is broken.",
                        "Review Cadence",
                        cadence.Key);
                nextDue = AddDays(request.DoneOnUtc.Value, interval, cadence, Cadence, "intervalDays");
            }

            return new OperationAnswer<DueResult>("computed", new DueResult(request.SubjectKind, cadence?.Key, null, nextDue, false, Array.Empty<ReminderStep>(), null), reasons, trace);
        }

        DateTime due = request.DueOnUtc!.Value;
        DateTime today = context.AsOfUtc.Date;
        List<SnapshotRow> rules = SnapshotRead.ActiveRows(snapshot, Rule)
            .Where(row => string.Equals(SnapshotRead.Text(row, "subjectKind"), request.SubjectKind, StringComparison.Ordinal))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToList();
        if (rules.Count == 0)
        {
            reasons.Add(new Reason(
                ReasonCodes.ReminderNoRule,
                ReasonCatalog.Get(ReasonCodes.ReminderNoRule).DefaultSeverity,
                "No active reminder rule exists for subject kind " + SnapshotRead.Quote(request.SubjectKind) + ", so nobody is reminded.",
                "Reminder Rule",
                null));
        }

        var actions = new List<ReminderStep>();
        DateTime? nextAction = null;
        string? defaultChannel = null;
        foreach (SnapshotRow rule in rules)
        {
            trace.Add(new RuleTrace("Reminder Rule", rule.Key, RuleTraceRole.Matched));
            SnapshotRow leadRow = cadence != null && SnapshotRead.HasValue(cadence, "leadDays") ? cadence : rule;
            SnapshotRow escalateRow = cadence != null && SnapshotRead.HasValue(cadence, "escalateAfterDays") ? cadence : rule;
            string leadEntity = ReferenceEquals(leadRow, rule) ? Rule : Cadence;
            string escalateEntity = ReferenceEquals(escalateRow, rule) ? Rule : Cadence;
            int lead = Days(leadRow, leadEntity, "leadDays", 0) ?? 0;
            int? escalate = Days(escalateRow, escalateEntity, "escalateAfterDays", 0);
            int repeat = Days(rule, Rule, "repeatDays", 0) ?? 0;
            var schedule = new Schedule(
                AddDays(due.Date, -lead, leadRow, leadEntity, "leadDays"),
                repeat,
                escalate.HasValue ? AddDays(due.Date, escalate.Value, escalateRow, escalateEntity, "escalateAfterDays") : (DateTime?)null);

            ReminderAction? action = schedule.ActionOn(today);
            if (action.HasValue)
            {
                string? channel = SnapshotRead.Text(rule, "channelKey");
                if (channel == null)
                {
                    defaultChannel ??= DefaultChannel(snapshot, trace);
                    channel = defaultChannel;
                }

                actions.Add(new ReminderStep(action.Value, rule.Key, schedule.NumberOn(today), schedule.IsEscalated(today), channel));
            }

            DateTime? after = schedule.NextAfter(today);
            if (after.HasValue && (!nextAction.HasValue || after.Value < nextAction.Value))
            {
                nextAction = after;
            }
        }

        var result = new DueResult(request.SubjectKind, cadence?.Key, due, null, context.AsOfUtc > due, actions, nextAction);
        return new OperationAnswer<DueResult>("computed", result, reasons, trace);
    }

    // The one active cadence for the review type and tier. Wildcard rows are expanded per tier in the snapshot, and V09
    // refuses two equally explicit rows for one combination, so two at run time are a broken configuration.
    private static SnapshotRow? FindCadence(ConfigurationSnapshot snapshot, string reviewTypeKey, string tierKey)
    {
        List<SnapshotRow> rows = SnapshotRead.ActiveRows(snapshot, Cadence)
            .Where(row => string.Equals(SnapshotRead.Text(row, "reviewTypeKey"), reviewTypeKey, StringComparison.Ordinal)
                && string.Equals(SnapshotRead.Text(row, "tierKey"), tierKey, StringComparison.Ordinal))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToList();
        if (rows.Count > 1)
        {
            throw OperationStop.Broken(
                ReasonCodes.RuleGroupAmbiguous,
                "Review Cadence rows " + SnapshotRead.Quote(rows[0].Key) + " and " + SnapshotRead.Quote(rows[1].Key) + " both cover review type " + SnapshotRead.Quote(reviewTypeKey)
                + " at tier " + SnapshotRead.Quote(tierKey) + ", so no single cadence wins. The configuration is broken.",
                "Review Cadence",
                rows[1].Key);
        }

        return rows.Count == 1 ? rows[0] : null;
    }

    // A day count column: absent gives null; below the minimum is a broken configuration.
    private static int? Days(SnapshotRow row, string entityKey, string column, int minimum)
    {
        int? value = SnapshotRead.Int(row, entityKey, column);
        if (value.HasValue && value.Value < minimum)
        {
            throw OperationStop.Broken(
                ReasonCodes.ValueRange,
                SnapshotRead.LogicalName(entityKey) + " " + SnapshotRead.Quote(row.Key) + " has a " + column + " below " + minimum.ToString(CultureInfo.InvariantCulture) + ", so the configuration is broken.",
                SnapshotRead.LogicalName(entityKey),
                row.Key);
        }

        return value;
    }

    // A date plus whole days, or a broken configuration when the result is outside the calendar.
    private static DateTime AddDays(DateTime date, int days, SnapshotRow row, string entityKey, string column)
    {
        double room = days >= 0 ? (DateTime.MaxValue - date).TotalDays : (date - DateTime.MinValue).TotalDays;
        if (Math.Abs((double)days) >= room)
        {
            throw OperationStop.Broken(
                ReasonCodes.ValueRange,
                SnapshotRead.LogicalName(entityKey) + " " + SnapshotRead.Quote(row.Key) + " has a " + column + " that leaves the calendar, so the configuration is broken.",
                SnapshotRead.LogicalName(entityKey),
                row.Key);
        }

        return date.AddDays(days);
    }

    private static string DefaultChannel(ConfigurationSnapshot snapshot, List<RuleTrace> trace)
    {
        JsonTreeNode value = SnapshotRead.RequireSetting(
            snapshot,
            EngineKeys.ReminderChannel,
            node => node.Kind == JsonTreeKind.String && ConfigurationKey.IsValid(node.GetString()),
            "the key of a channel");
        trace.Add(new RuleTrace("Setting", EngineKeys.ReminderChannel, RuleTraceRole.Matched));
        return value.GetString();
    }

    // The steps of one rule for one item: the first reminder, the repeats after it, and the escalation.
    private sealed class Schedule
    {
        private readonly DateTime _first;
        private readonly int _repeat;
        private readonly DateTime? _escalate;

        public Schedule(DateTime first, int repeat, DateTime? escalate)
        {
            _first = first;
            _repeat = repeat;
            _escalate = escalate;
        }

        public ReminderAction? ActionOn(DateTime day)
        {
            if (_escalate.HasValue && day == _escalate.Value)
            {
                return ReminderAction.Escalate;
            }

            if (day == _first)
            {
                return ReminderAction.Remind;
            }

            return IsRepeatDay(day) ? ReminderAction.Repeat : (ReminderAction?)null;
        }

        // How many steps fall on or before the day. An escalation on a reminder day replaces that reminder, so the
        // day counts once.
        public int NumberOn(DateTime day)
        {
            int count = 1 + (_repeat > 0 ? (int)((day - _first).TotalDays / _repeat) : 0);
            if (_escalate.HasValue && _escalate.Value <= day && _escalate.Value != _first && !IsRepeatDay(_escalate.Value))
            {
                count++;
            }

            return count;
        }

        public bool IsEscalated(DateTime day)
        {
            return _escalate.HasValue && day >= _escalate.Value;
        }

        public DateTime? NextAfter(DateTime day)
        {
            DateTime? next = null;
            if (day < _first)
            {
                next = _first;
            }
            else if (_repeat > 0)
            {
                long passed = (long)((day - _first).TotalDays / _repeat);
                double offset = (passed + 1) * (double)_repeat;
                if ((DateTime.MaxValue - _first).TotalDays > offset)
                {
                    next = _first.AddDays(offset);
                }
            }

            if (_escalate.HasValue && _escalate.Value > day && (!next.HasValue || _escalate.Value < next.Value))
            {
                next = _escalate.Value;
            }

            return next;
        }

        private bool IsRepeatDay(DateTime day)
        {
            if (_repeat <= 0 || day <= _first)
            {
                return false;
            }

            long days = (long)(day - _first).TotalDays;
            return days % _repeat == 0;
        }
    }
}
