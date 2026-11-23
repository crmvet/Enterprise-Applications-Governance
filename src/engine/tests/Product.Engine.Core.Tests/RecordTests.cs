using System;
using System.Linq;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Xunit;

namespace Product.Engine.Core.Tests;

// The common records of document 25, section 5.1.
public sealed class RecordTests
{
    private const string Hash = "8622543ce66b398c4922d3d34eb895c30ece337675c39bbfd0f0533c4d715065";

    private static readonly DateTime AsOf = new(2026, 11, 22, 9, 30, 0, DateTimeKind.Utc);

    private static ActorInfo Person()
    {
        return new ActorInfo("principal-a", PrincipalKind.User, ActorKindMeaning.Person, new[] { "pmo" });
    }

    private static EvaluationContext Context()
    {
        return new EvaluationContext(12, Hash, AsOf, Person(), "corr-1", "Claude Code");
    }

    // ---- ActorInfo ----

    [Fact]
    public void RoleKeysAreDistinctAndSorted()
    {
        var actor = new ActorInfo("principal-a", PrincipalKind.User, ActorKindMeaning.Person, new[] { "pmo", "architect", "pmo", "change-manager" });

        Assert.Equal(new[] { "architect", "change-manager", "pmo" }, actor.RoleKeys);
        Assert.True(actor.HoldsRole("pmo"));
        Assert.False(actor.HoldsRole("PMO"));
        Assert.False(actor.HoldsRole("auditor"));
    }

    [Fact]
    public void RoleOrderDoesNotChangeTheActor()
    {
        var first = new ActorInfo("p", PrincipalKind.User, ActorKindMeaning.Person, new[] { "b-role", "a-role" });
        var second = new ActorInfo("p", PrincipalKind.User, ActorKindMeaning.Person, new[] { "a-role", "b-role" });

        Assert.Equal(first.RoleKeys, second.RoleKeys);
    }

    [Theory]
    [InlineData("Platform Admin")]
    [InlineData("platform_admin")]
    [InlineData("")]
    public void RoleKeysMustUseTheKeyFormat(string role)
    {
        Assert.Throws<ArgumentException>(() => new ActorInfo("p", PrincipalKind.User, ActorKindMeaning.Person, new[] { role }));
    }

    [Fact]
    public void AnActorNeedsAPrincipalAndKnownKinds()
    {
        Assert.Throws<ArgumentException>(() => new ActorInfo(string.Empty, PrincipalKind.User, ActorKindMeaning.Person));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActorInfo("p", (PrincipalKind)42, ActorKindMeaning.Person));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActorInfo("p", PrincipalKind.User, (ActorKindMeaning)42));
    }

    [Fact]
    public void OnBehalfOfIsNullWhenAPersonActsDirectly()
    {
        var direct = new ActorInfo("p", PrincipalKind.User, ActorKindMeaning.Person, onBehalfOfPrincipalId: string.Empty);
        var forPerson = new ActorInfo("client", PrincipalKind.User, ActorKindMeaning.AiClientForPerson, onBehalfOfPrincipalId: "p");

        Assert.Null(direct.OnBehalfOfPrincipalId);
        Assert.Empty(direct.RoleKeys);
        Assert.False(direct.IsEngineNested);
        Assert.Equal("p", forPerson.OnBehalfOfPrincipalId);
    }

    // ---- EvaluationContext ----

    [Fact]
    public void AContextKeepsItsValues()
    {
        EvaluationContext context = Context();

        Assert.Equal(12, context.ConfigurationVersion);
        Assert.Equal(Hash, context.SnapshotHash);
        Assert.Equal(AsOf, context.AsOfUtc);
        Assert.Equal("corr-1", context.CorrelationId);
        Assert.Equal("Claude Code", context.ClientApp);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void TheMomentMustBeUtc(DateTimeKind kind)
    {
        var moment = new DateTime(2026, 11, 22, 9, 30, 0, kind);

        Assert.Throws<ArgumentException>(() => new EvaluationContext(1, Hash, moment, Person(), "corr-1"));
    }

    [Fact]
    public void AContextNeedsItsParts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationContext(-1, Hash, AsOf, Person(), "corr-1"));
        Assert.Throws<ArgumentException>(() => new EvaluationContext(1, string.Empty, AsOf, Person(), "corr-1"));
        Assert.Throws<ArgumentNullException>(() => new EvaluationContext(1, Hash, AsOf, null!, "corr-1"));
        Assert.Throws<ArgumentException>(() => new EvaluationContext(1, Hash, AsOf, Person(), string.Empty));
        Assert.Null(new EvaluationContext(1, Hash, AsOf, Person(), "corr-1", string.Empty).ClientApp);
    }

    // ---- Reason and RuleTrace ----

    [Theory]
    [InlineData("approval.not-role-holder", true)]
    [InlineData("a.b", true)]
    [InlineData("approval-rule.zero-count", true)]
    [InlineData("a.b.c", true)]
    [InlineData("approval", false)]
    [InlineData("Approval.x", false)]
    [InlineData("approval..x", false)]
    [InlineData(".x", false)]
    [InlineData("x.", false)]
    [InlineData("approval.not_role", false)]
    [InlineData("approval.not role", false)]
    [InlineData("", false)]
    public void ReasonCodesAreLowercaseAndDotted(string code, bool expected)
    {
        Assert.Equal(expected, Reason.IsWellFormedCode(code));
    }

    [Fact]
    public void AReasonNeedsAGoodCodeAndAMessage()
    {
        Assert.Throws<ArgumentException>(() => new Reason("Bad Code", ReasonSeverity.Error, "Text."));
        Assert.Throws<ArgumentException>(() => new Reason("a.b", ReasonSeverity.Error, string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Reason("a.b", (ReasonSeverity)9, "Text."));
    }

    [Fact]
    public void ReasonsCompareByValue()
    {
        var first = new Reason("a.b", ReasonSeverity.Warning, "Text.", "Decision", "row-1");
        var same = new Reason("a.b", ReasonSeverity.Warning, "Text.", "Decision", "row-1");
        var other = new Reason("a.b", ReasonSeverity.Warning, "Text.", "Decision", "row-2");

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.Null(new Reason("a.b", ReasonSeverity.Info, "Text.", string.Empty, string.Empty).Entity);
    }

    [Fact]
    public void RuleTracesCompareByValue()
    {
        var trace = new RuleTrace("Approval Rule", "go-live-tier-2-owner", RuleTraceRole.Won);

        Assert.Equal(new RuleTrace("Approval Rule", "go-live-tier-2-owner", RuleTraceRole.Won), trace);
        Assert.NotEqual(new RuleTrace("Approval Rule", "go-live-tier-2-owner", RuleTraceRole.Dropped), trace);
        Assert.Equal("Approval Rule go-live-tier-2-owner won", trace.ToString());
        Assert.Throws<ArgumentException>(() => new RuleTrace(string.Empty, "k", RuleTraceRole.Matched));
        Assert.Throws<ArgumentException>(() => new RuleTrace("Approval Rule", string.Empty, RuleTraceRole.Matched));
    }

    // ---- EvaluationResult ----

    [Fact]
    public void ResultsPutErrorsFirstThenSortByCode()
    {
        EvaluationResult<string> result = EvaluationResult.Create(
            Context(),
            "required",
            "payload",
            new[]
            {
                ReasonCatalog.Create(ReasonCodes.ApprovalConsultationNotCounted),
                ReasonCatalog.Create(ReasonCodes.GatePolicyMissing),
                ReasonCatalog.Create(ReasonCodes.ApprovalSeparationFlagged),
                ReasonCatalog.Create(ReasonCodes.ApprovalAiActorNotCounted),
                ReasonCatalog.Create(ReasonCodes.ApprovalExpired),
            });

        Assert.Equal(
            new[]
            {
                "gate-policy.missing",
                "approval.ai-actor-not-counted",
                "approval.separation-flagged",
                "approval.consultation-not-counted",
                "approval.expired",
            },
            result.Reasons.Select(reason => reason.Code).ToArray());
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void ReasonOrderInTheInputDoesNotChangeTheResult()
    {
        Reason[] reasons =
        {
            ReasonCatalog.Create(ReasonCodes.ApprovalExpired, "Approval Record", "r2"),
            ReasonCatalog.Create(ReasonCodes.ApprovalExpired, "Approval Record", "r1"),
            ReasonCatalog.Create(ReasonCodes.ApprovalNotApproved, "Approval Record", "r3"),
        };

        EvaluationResult<int> forward = EvaluationResult.Create(Context(), "pending", 0, reasons);
        EvaluationResult<int> backward = EvaluationResult.Create(Context(), "pending", 0, Enumerable.Reverse(reasons));

        Assert.Equal(forward.Reasons, backward.Reasons);
        Assert.False(forward.HasErrors);
    }

    [Fact]
    public void ResultsCarryTheVersionHashAndEngineVersion()
    {
        var trace = new[]
        {
            new RuleTrace("Approval Rule", "go-live-tier-2-owner", RuleTraceRole.Won),
            new RuleTrace("Approval Rule", "go-live-owner", RuleTraceRole.Dropped),
        };

        EvaluationResult<string> result = EvaluationResult.Create(Context(), "required", "payload", null, trace);

        Assert.Equal(12, result.ConfigurationVersion);
        Assert.Equal(Hash, result.SnapshotHash);
        Assert.Equal(EngineInfo.Version, result.EngineVersion);
        Assert.Equal("payload", result.Payload);
        Assert.Empty(result.Reasons);
        Assert.Equal(trace, result.Trace);
        Assert.False(result.HasErrors);
    }

    [Theory]
    [InlineData("Engine Accept")]
    [InlineData("engine_accept")]
    [InlineData("")]
    public void OutcomesAreKebabCaseWords(string outcome)
    {
        Assert.Throws<ArgumentException>(() => EvaluationResult.Create(Context(), outcome, 0));
    }

    [Fact]
    public void ResultsRefuseNullEntries()
    {
        Assert.Throws<ArgumentException>(() => EvaluationResult.Create(Context(), "pending", 0, new Reason[] { null! }));
        Assert.Throws<ArgumentException>(() => EvaluationResult.Create(Context(), "pending", 0, null, new RuleTrace[] { null! }));
        Assert.Throws<ArgumentNullException>(() => EvaluationResult.Create<int>(null!, "pending", 0));
    }
}
