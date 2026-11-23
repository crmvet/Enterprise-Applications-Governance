using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One tier answer: a question (C09) and the chosen answer option (C10).
public sealed class TierAnswer
{
    public TierAnswer(string questionKey, string answerKey)
    {
        if (!ConfigurationKey.IsValid(questionKey))
        {
            throw new ArgumentException("A question key must use the configuration key format.", nameof(questionKey));
        }

        if (!ConfigurationKey.IsValid(answerKey))
        {
            throw new ArgumentException("An answer key must use the configuration key format.", nameof(answerKey));
        }

        QuestionKey = questionKey;
        AnswerKey = answerKey;
    }

    public string QuestionKey { get; }

    public string AnswerKey { get; }

    // The C10 key path: "question/answer".
    public string KeyPath => QuestionKey + "/" + AnswerKey;

    internal JsonTreeNode ToJson()
    {
        return new WireWriter().Add("questionKey", QuestionKey).Add("answerKey", AnswerKey).Build();
    }
}

// OP-01 input (document 25, section 5.3).
public sealed class TierRequest
{
    public TierRequest(string applicationKindKey, IEnumerable<TierAnswer> answers, string? proposedTierKey = null)
    {
        if (!ConfigurationKey.IsValid(applicationKindKey))
        {
            throw new ArgumentException("An application kind key must use the configuration key format.", nameof(applicationKindKey));
        }

        if (proposedTierKey != null && !ConfigurationKey.IsValid(proposedTierKey))
        {
            throw new ArgumentException("A tier key must use the configuration key format.", nameof(proposedTierKey));
        }

        ApplicationKindKey = applicationKindKey;
        Answers = new ReadOnlyCollection<TierAnswer>((answers ?? throw new ArgumentNullException(nameof(answers))).ToList());
        ProposedTierKey = proposedTierKey;
    }

    // The application kind (C15); filters questions by Applies To Kinds.
    public string ApplicationKindKey { get; }

    // The tier answers given.
    public IReadOnlyList<TierAnswer> Answers { get; }

    // A tier a person proposes, to compare with the computed one.
    public string? ProposedTierKey { get; }

    public static TierRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "applicationKindKey", "answers", "proposedTierKey");
        var answers = wire.Objects("answers", required: false, "questionKey", "answerKey")
            .Select(answer => new TierAnswer(answer.RequiredKey("questionKey"), answer.RequiredKey("answerKey")))
            .ToList();
        return new TierRequest(wire.RequiredKey("applicationKindKey"), answers, wire.OptionalKey("proposedTierKey"));
    }
}

// OP-01 output (document 25, section 5.3).
public sealed class TierResult : IOperationPayload
{
    public TierResult(
        string? computedTierKey,
        int? computedTierRank,
        IEnumerable<TierAnswer> decisiveAnswers,
        IEnumerable<string> missingQuestionKeys,
        IEnumerable<string> triggeredReviewTypeKeys,
        IEnumerable<string> triggeredGateRequirementKeys,
        bool proposedIsLower,
        IEnumerable<TierAnswer> ignoredAnswers)
    {
        ComputedTierKey = computedTierKey;
        ComputedTierRank = computedTierRank;
        DecisiveAnswers = new ReadOnlyCollection<TierAnswer>(decisiveAnswers.ToList());
        MissingQuestionKeys = new ReadOnlyCollection<string>(missingQuestionKeys.ToList());
        TriggeredReviewTypeKeys = new ReadOnlyCollection<string>(triggeredReviewTypeKeys.ToList());
        TriggeredGateRequirementKeys = new ReadOnlyCollection<string>(triggeredGateRequirementKeys.ToList());
        ProposedIsLower = proposedIsLower;
        IgnoredAnswers = new ReadOnlyCollection<TierAnswer>(ignoredAnswers.ToList());
    }

    // The tier the counted answers imply. Null when no applicable question has a counted answer.
    public string? ComputedTierKey { get; }

    public int? ComputedTierRank { get; }

    // The answers that implied the winning rank, by question key.
    public IReadOnlyList<TierAnswer> DecisiveAnswers { get; }

    // Applicable questions not yet answered, by key.
    public IReadOnlyList<string> MissingQuestionKeys { get; }

    // Review types the counted answers add (C10 Triggers Json), by key.
    public IReadOnlyList<string> TriggeredReviewTypeKeys { get; }

    // Gate requirements the counted answers add, by key.
    public IReadOnlyList<string> TriggeredGateRequirementKeys { get; }

    // True when the proposed tier ranks below the computed tier. Lowering needs a tier decision with a reason (O08).
    public bool ProposedIsLower { get; }

    // Answers that do not count: to a question that does not apply to the kind, or retired (a proposal of this build).
    public IReadOnlyList<TierAnswer> IgnoredAnswers { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("computedTierKey", ComputedTierKey)
            .Add("computedTierRank", ComputedTierRank)
            .Add("decisiveAnswers", WireWriter.Array(DecisiveAnswers.Select(answer => answer.ToJson())))
            .Add("missingQuestionKeys", MissingQuestionKeys)
            .Add("triggeredReviewTypeKeys", TriggeredReviewTypeKeys)
            .Add("triggeredGateRequirementKeys", TriggeredGateRequirementKeys)
            .Add("proposedIsLower", ProposedIsLower)
            .Add("ignoredAnswers", WireWriter.Array(IgnoredAnswers.Select(answer => answer.ToJson())))
            .Build();
    }
}

// OP-01 Evaluate tier (document 25, section 5.3).
// Applicable questions are the active C09 rows whose Applies To Kinds is empty or holds the kind. Each counted answer
// implies a tier (C10 Implied Tier). The computed tier is the implied tier with the highest rank: the most serious
// answer wins, so a more serious answer never lowers the tier. An unanswered applicable question makes the outcome
// incomplete. The tier itself is set only by an accepted tier decision (OP-08); AI may suggest, never set.
public static class TierOperation
{
    private const string Question = "tier-question";
    private const string Option = "tier-answer-option";
    private const string Tier = "tier";

    public static EvaluationResult<TierResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, TierRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, request));
    }

    public static EvaluationResult<TierResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, TierRequest.FromJson(input)));
    }

    private static OperationAnswer<TierResult> Answer(ConfigurationSnapshot snapshot, TierRequest request)
    {
        string kind = request.ApplicationKindKey;
        SnapshotRead.RequireExisting(snapshot, "application-kind", kind, "input.applicationKindKey");

        var answered = new HashSet<string>(StringComparer.Ordinal);
        foreach (TierAnswer answer in request.Answers)
        {
            if (!answered.Add(answer.QuestionKey))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.answers answers the question " + SnapshotRead.Quote(answer.QuestionKey) + " twice.", answer.QuestionKey);
            }
        }

        List<SnapshotRow> applicable = SnapshotRead.ActiveRows(snapshot, Question).Where(row => AppliesTo(row, kind)).ToList();
        if (applicable.Count == 0)
        {
            throw OperationStop.Broken(
                ReasonCodes.TierNoApplicableQuestions,
                "No active tier question applies to application kind " + SnapshotRead.Quote(kind) + ", so no tier can be computed. The configuration is broken.",
                "Tier Question",
                null);
        }

        var applicableKeys = new HashSet<string>(applicable.Select(row => row.KeyPath), StringComparer.Ordinal);
        var counted = new List<CountedAnswer>();
        var ignored = new List<TierAnswer>();
        foreach (TierAnswer answer in request.Answers.OrderBy(a => a.QuestionKey, StringComparer.Ordinal))
        {
            SnapshotRead.RequireExisting(snapshot, Question, answer.QuestionKey, "input.answers questionKey");
            SnapshotRow option = SnapshotRead.Find(snapshot, Option, answer.KeyPath)
                ?? throw OperationStop.Input(
                    ReasonCodes.InputUnknownKey,
                    "input.answers gives " + SnapshotRead.Quote(answer.AnswerKey) + " for question " + SnapshotRead.Quote(answer.QuestionKey) + ", which is not one of its answer options.",
                    answer.KeyPath);

            if (!applicableKeys.Contains(answer.QuestionKey) || !option.IsActive)
            {
                ignored.Add(answer);
                continue;
            }

            counted.Add(new CountedAnswer(answer, ImpliedTier(snapshot, option), SnapshotRead.Node(option, "triggers")));
        }

        string[] missing = applicable.Select(row => row.KeyPath).Where(key => !counted.Any(c => c.Answer.QuestionKey == key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        string? computedKey = null;
        int? computedRank = null;
        var decisive = new List<TierAnswer>();
        if (counted.Count > 0)
        {
            int top = counted.Max(c => c.TierRank);
            List<CountedAnswer> winners = counted.Where(c => c.TierRank == top).ToList();
            string[] winningTiers = winners.Select(c => c.TierKey).Distinct(StringComparer.Ordinal).ToArray();
            if (winningTiers.Length > 1)
            {
                throw OperationStop.Broken(
                    ReasonCodes.TierRankDuplicate,
                    "Tiers " + string.Join(" and ", winningTiers.Select(SnapshotRead.Quote)) + " share rank " + top + ", so the most serious answer names no single tier. The configuration is broken.",
                    "Tier",
                    winningTiers[1]);
            }

            computedKey = winningTiers[0];
            computedRank = top;
            decisive.AddRange(winners.Select(c => c.Answer));
        }

        var reasons = new List<Reason>();
        bool proposedIsLower = false;
        if (request.ProposedTierKey != null)
        {
            SnapshotRow proposed = SnapshotRead.RequireActive(snapshot, Tier, request.ProposedTierKey, "input.proposedTierKey");
            int proposedRank = TierRank(snapshot, proposed);
            proposedIsLower = computedRank.HasValue && proposedRank < computedRank.Value;
            if (proposedIsLower)
            {
                reasons.Add(ReasonCatalog.Create(ReasonCodes.TierProposedLower, "Tier", request.ProposedTierKey));
            }
        }

        if (missing.Length > 0)
        {
            reasons.Add(ReasonCatalog.Create(ReasonCodes.TierIncomplete, "Tier Question", missing[0]));
        }

        var trace = new List<RuleTrace>();
        foreach (TierAnswer answer in request.Answers.OrderBy(a => a.QuestionKey, StringComparer.Ordinal))
        {
            RuleTraceRole role = ignored.Contains(answer) ? RuleTraceRole.Skipped : decisive.Contains(answer) ? RuleTraceRole.Won : RuleTraceRole.Matched;
            trace.Add(new RuleTrace("Tier Answer Option", answer.KeyPath, role));
        }

        if (computedKey != null)
        {
            trace.Add(new RuleTrace("Tier", computedKey, RuleTraceRole.Won));
        }

        var result = new TierResult(
            computedKey,
            computedRank,
            decisive,
            missing,
            Sorted(counted.SelectMany(c => SnapshotRead.Strings(c.Triggers, "reviewTypeKeys"))),
            Sorted(counted.SelectMany(c => SnapshotRead.Strings(c.Triggers, "gateRequirementKeys"))),
            proposedIsLower,
            ignored);
        return new OperationAnswer<TierResult>(missing.Length > 0 ? "incomplete" : "computed", result, reasons, trace);
    }

    private static bool AppliesTo(SnapshotRow question, string kind)
    {
        IReadOnlyList<string> kinds = SnapshotRead.Strings(question, "appliesToKindKeys");
        return kinds.Count == 0 || kinds.Contains(kind, StringComparer.Ordinal);
    }

    // The implied tier must be an active tier with a rank (V13). Anything else is a broken configuration.
    private static KeyValuePair<string, int> ImpliedTier(ConfigurationSnapshot snapshot, SnapshotRow option)
    {
        string? tierKey = SnapshotRead.Text(option, "impliedTierKey");
        SnapshotRow? tier = SnapshotRead.FindActive(snapshot, Tier, tierKey);
        if (tierKey == null || tier == null)
        {
            throw OperationStop.Broken(
                ReasonCodes.TierQuestionOptions,
                "Tier answer option " + SnapshotRead.Quote(option.KeyPath) + " implies " + (tierKey == null ? "no tier" : "tier " + SnapshotRead.Quote(tierKey) + ", which is not active")
                + ", so the configuration is broken.",
                "Tier Answer Option",
                option.KeyPath);
        }

        return new KeyValuePair<string, int>(tierKey, TierRank(snapshot, tier));
    }

    private static int TierRank(ConfigurationSnapshot snapshot, SnapshotRow tier)
    {
        return SnapshotRead.Int(tier, Tier, "rank")
            ?? throw OperationStop.Broken(
                ReasonCodes.TierRankDuplicate,
                "Tier " + SnapshotRead.Quote(tier.KeyPath) + " has no rank, so tiers cannot be compared. The configuration is broken.",
                "Tier",
                tier.KeyPath);
    }

    private static string[] Sorted(IEnumerable<string> keys)
    {
        return keys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }

    private sealed class CountedAnswer
    {
        public CountedAnswer(TierAnswer answer, KeyValuePair<string, int> tier, JsonTreeNode? triggers)
        {
            Answer = answer;
            TierKey = tier.Key;
            TierRank = tier.Value;
            Triggers = triggers;
        }

        public TierAnswer Answer { get; }

        public string TierKey { get; }

        public int TierRank { get; }

        public JsonTreeNode? Triggers { get; }
    }
}
