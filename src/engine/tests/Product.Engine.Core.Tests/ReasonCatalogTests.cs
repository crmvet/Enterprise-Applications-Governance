using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Xunit;

namespace Product.Engine.Core.Tests;

// The reason code catalog (document 25, sections 5, 7 and 16).
public sealed class ReasonCatalogTests
{
    // Every engine reason code document 25 names. Each must be in the catalog and not marked proposed.
    private static readonly string[] Document25Codes =
    {
        "ai.status-not-allowed", "approval-rule.change-coverage", "approval-rule.consultation-only", "approval-rule.coverage",
        "approval-rule.sequence", "approval-rule.tier-coverage", "approval-rule.workload-approver", "approval-rule.zero-count",
        "approval.ai-actor-not-counted", "approval.consultation-not-counted", "approval.expired", "approval.non-user-principal",
        "approval.not-approved", "approval.not-role-holder", "approval.outcome-writer", "approval.reason-missing",
        "approval.separation-exception-missing", "approval.separation-flagged", "approval.separation-refused",
        "approval.workload-not-counted", "audit.append-only", "authorization.ai-not-counted", "authorization.no-evidence",
        "authorization.not-attester", "cadence.coverage", "config.activation-only", "config.base-moved",
        "config.draft-frozen", "config.in-use", "config.key-immutable", "config.not-configuration-admin",
        "config.preset-read-only", "config.protected-change", "config.version-immutable", "decision-right.accountable",
        "decision-right.ai-letter", "decision-type.outcome", "decision.accept-by-engine-only", "decision.final",
        "engine.column-overwritten", "engine.unavailable", "gate-policy.coverage", "gate-policy.emergency-rule",
        "gate-policy.fail-mode", "gate-policy.missing", "gate.not-ready", "json.schema", "key.duplicate", "key.format",
        "key.immutable", "key.reused", "link.url-pattern", "link.wrong-category", "meaning.changed-in-use",
        "meaning.missing", "pattern.invalid", "preset.conflict", "preset.tenant-data", "ref.exactly-one", "ref.in-use",
        "ref.inactive", "ref.not-found", "ref.unresolved", "ref.wildcard", "register.decision-required",
        "request.idempotency-key-reused", "role.binding-not-group", "role.config-admin", "role.config-admin-missing",
        "role.unbound", "rule-group.ambiguous", "separation.shared-group", "setting.ai-approval-lock-off",
        "setting.locked-changed", "setting.required", "shape.schema", "snapshot.schema-unsupported", "snapshot.size",
        "stage.meaning-coverage", "stage.rank-duplicate", "standard-model.condition-kind", "standard-model.does-not-qualify",
        "standard-model.not-approved", "standard-model.type", "status.unknown", "taxonomy.minimum", "tier.none-active",
        "tier.question-options", "tier.rank-duplicate", "value.range", "vocabulary.source",
    };

    [Fact]
    public void CodesAreWellFormedUniqueAndSorted()
    {
        string[] codes = ReasonCatalog.All.Select(info => info.Code).ToArray();

        Assert.All(codes, code => Assert.True(Reason.IsWellFormedCode(code), code));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(codes.OrderBy(code => code, StringComparer.Ordinal).ToArray(), codes);
    }

    [Fact]
    public void ConstantsAndCatalogMatchOneToOne()
    {
        string[] constants = typeof(ReasonCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(constants, ReasonCatalog.All.Select(info => info.Code).ToArray());
    }

    [Fact]
    public void EveryCodeDocument25NamesIsInTheCatalog()
    {
        foreach (string code in Document25Codes)
        {
            Assert.True(ReasonCatalog.TryGet(code, out ReasonCodeInfo? info), code);
            Assert.False(info.IsProposed, code + " is named in document 25 and must not be marked proposed");
        }
    }

    [Fact]
    public void CodesNotInDocument25AreMarkedProposed()
    {
        foreach (ReasonCodeInfo info in ReasonCatalog.All.Where(info => !Document25Codes.Contains(info.Code, StringComparer.Ordinal)))
        {
            Assert.True(info.IsProposed, info.Code + " is not named in document 25, so it must be marked proposed");
        }
    }

    [Fact]
    public void EveryValidationRuleV01ToV44HasACode()
    {
        string[] missing = Enumerable.Range(1, 44)
            .Select(i => "V" + i.ToString("00", CultureInfo.InvariantCulture))
            .Where(ruleId => ReasonCatalog.ForRule(ruleId).Count == 0)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryDataTaxonomyRuleHasACode()
    {
        // V49 to V60 and G-18 to G-25 (docs/architecture/data-taxonomy.md, Guard rules and Validation rules; G-25 is D-41, question 7).
        string[] missing = Enumerable.Range(49, 12).Select(i => "V" + i.ToString("00", CultureInfo.InvariantCulture))
            .Concat(Enumerable.Range(18, 8).Select(i => "G-" + i.ToString("00", CultureInfo.InvariantCulture)))
            .Where(ruleId => ReasonCatalog.ForRule(ruleId).Count == 0)
            .ToArray();

        Assert.Empty(missing);
        Assert.Contains(ReasonCatalog.ForRule("G-25"), info => info.Code == "data.rule-never");
        Assert.Contains(ReasonCatalog.ForRule("G-20"), info => info.Code == "data.decision-cannot-allow");
    }

    [Fact]
    public void EveryGuardRuleG01ToG17HasACode()
    {
        string[] missing = Enumerable.Range(1, 17)
            .Select(i => "G-" + i.ToString("00", CultureInfo.InvariantCulture))
            .Where(ruleId => ReasonCatalog.ForRule(ruleId).Count == 0)
            .ToArray();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("V22", "decision-right.accountable")]
    [InlineData("V26", "rule-group.ambiguous")]
    [InlineData("V35", "role.binding-not-group")]
    [InlineData("G-12", "role.binding-not-group")]
    [InlineData("G-06", "approval.outcome-writer")]
    [InlineData("G-17", "decision.accept-by-engine-only")]
    public void RuleIdsMapToTheDocumentedCode(string ruleId, string code)
    {
        Assert.Contains(ReasonCatalog.ForRule(ruleId), info => info.Code == code);
    }

    [Fact]
    public void RulesWithTwoCodesMapToBoth()
    {
        Assert.Equal(new[] { "approval-rule.zero-count", "value.range" }, CodesFor("V28"));
        Assert.Equal(new[] { "setting.ai-approval-lock-off", "setting.locked-changed" }, CodesFor("V33"));
        Assert.Equal(new[] { "approval-rule.consultation-only", "approval-rule.sequence" }, CodesFor("V27"));
        Assert.Equal(new[] { "stage.meaning-coverage", "stage.rank-duplicate" }, CodesFor("V14"));
        Assert.Equal(new[] { "audit.append-only", "config.version-immutable" }, CodesFor("G-07"));
    }

    [Fact]
    public void ValidationSeveritiesFollowDocument25()
    {
        // Warnings in the V01 to V44 table and the engine checks. Every other validation code is an error by default.
        // The data taxonomy adds V53 and V59, both warnings (docs/architecture/data-taxonomy.md, Validation rules).
        string[] warnings = { "cadence.coverage", "role.unbound", "vocabulary.source", "data.classification-lower", "data.loosens" };

        string[] wrong = ReasonCatalog.All
            .Where(info => info.Categories.HasFlag(ReasonCategories.Validation))
            .Where(info => info.DefaultSeverity != (warnings.Contains(info.Code, StringComparer.Ordinal) ? ReasonSeverity.Warning : ReasonSeverity.Error))
            .Select(info => info.Code)
            .ToArray();

        Assert.Empty(wrong);
    }

    [Fact]
    public void OperationSeveritiesFollowDocument25()
    {
        Assert.Equal(ReasonSeverity.Info, ReasonCatalog.Get("approval.consultation-not-counted").DefaultSeverity);
        Assert.Equal(ReasonSeverity.Warning, ReasonCatalog.Get("approval.separation-flagged").DefaultSeverity);
        Assert.Equal(ReasonSeverity.Info, ReasonCatalog.Get("engine.column-overwritten").DefaultSeverity);
    }

    [Theory]
    [InlineData("gate-policy.missing")]
    [InlineData("rule-group.ambiguous")]
    [InlineData("decision-right.accountable")]
    [InlineData("engine.unavailable")]
    [InlineData("snapshot.schema-unsupported")]
    [InlineData("snapshot.hash-mismatch")]
    [InlineData("approval-rule.zero-count")]
    [InlineData("approval-rule.consultation-only")]
    [InlineData("gate-policy.coverage")]
    [InlineData("gate-policy.emergency-rule")]
    [InlineData("setting.required")]
    [InlineData("setting.locked-changed")]
    [InlineData("tier.question-options")]
    [InlineData("tier.rank-duplicate")]
    [InlineData("tier.no-applicable-questions")]
    [InlineData("ref.exactly-one")]
    [InlineData("shape.schema")]
    [InlineData("value.range")]
    [InlineData("ref.unresolved")]
    [InlineData("json.schema")]
    [InlineData("standard-model.condition-kind")]
    [InlineData("pattern.invalid")]
    [InlineData("meaning.missing")]
    [InlineData("stage.meaning-coverage")]
    public void BrokenConfigurationAtRunTimeIsUnavailable(string code)
    {
        // Document 25, section 7 and rule 10: never a silent fallback.
        Assert.True(ReasonCatalog.Get(code).Categories.HasFlag(ReasonCategories.Unavailable));
    }

    [Fact]
    public void ErrorModelCategoriesFollowSection7()
    {
        Assert.Equal(ReasonCategories.RefusedRole, ReasonCatalog.Get("approval.outcome-writer").Categories);
        Assert.Equal(ReasonCategories.RefusedRole, ReasonCatalog.Get("config.not-configuration-admin").Categories);
        Assert.Equal(ReasonCategories.Conflict, ReasonCatalog.Get("request.idempotency-key-reused").Categories);
        Assert.Equal(ReasonCategories.NotFound, ReasonCatalog.Get("ref.not-found").Categories);

        // An erasure, an agent write and the bootstrap are refused for the caller's kind, not for a rule.
        Assert.Equal(ReasonCategories.RefusedRole, ReasonCatalog.Get("principal.erasure-not-allowed").Categories);
        Assert.Equal(ReasonCategories.RefusedRole, ReasonCatalog.Get("actor.agent-write-refused").Categories);
        Assert.Equal(ReasonCategories.RefusedRole, ReasonCatalog.Get("bootstrap.not-allowed").Categories);
        Assert.Equal(ReasonSeverity.Info, ReasonCatalog.Get("ai.forced-draft").DefaultSeverity);
        Assert.True(ReasonCatalog.Get("audit.append-only").Categories.HasFlag(ReasonCategories.Conflict));
        Assert.All(ReasonCatalog.All.Where(info => info.Code.StartsWith("input.", StringComparison.Ordinal)),
            info => Assert.Equal(ReasonCategories.InvalidInput, info.Categories));
    }

    [Fact]
    public void MessagesAreOnePlainAsciiSentence()
    {
        var problems = new System.Collections.Generic.List<string>();
        foreach (ReasonCodeInfo info in ReasonCatalog.All)
        {
            string message = info.Message;
            if (!message.EndsWith(".", StringComparison.Ordinal))
            {
                problems.Add(info.Code + ": does not end with a full stop");
            }

            if (!char.IsUpper(message[0]))
            {
                problems.Add(info.Code + ": does not start with a capital letter");
            }

            if (message.IndexOf(". ", StringComparison.Ordinal) >= 0)
            {
                problems.Add(info.Code + ": more than one sentence");
            }

            if (message.Length > 160)
            {
                problems.Add(info.Code + ": longer than 160 characters");
            }

            if (message.IndexOf('<') >= 0 || message.IndexOf('>') >= 0)
            {
                problems.Add(info.Code + ": looks like HTML");
            }

            // ASCII only, which also keeps out en and em dashes.
            if (message.Any(c => c < 0x20 || c >= 0x7F))
            {
                problems.Add(info.Code + ": not plain ASCII");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void WorkingDefaultsPointAtOpenQuestions()
    {
        int[] openQuestions = { 1, 4, 7, 8, 9, 12 };

        Assert.All(ReasonCatalog.All.Where(info => info.WorkingDefaultQuestion.HasValue),
            info => Assert.Contains(info.WorkingDefaultQuestion!.Value, openQuestions));
        Assert.Equal(7, ReasonCatalog.Get("gate.ai-only-code-approval").WorkingDefaultQuestion);
        Assert.Equal(8, ReasonCatalog.Get("decision.engine-accepted").WorkingDefaultQuestion);
        Assert.Equal(8, ReasonCatalog.Get("decision.accept-by-engine-only").WorkingDefaultQuestion);
    }

    [Fact]
    public void CreateUsesTheCatalogSeverityAndMessage()
    {
        Reason reason = ReasonCatalog.Create(ReasonCodes.ApprovalOutcomeWriter, "Approval Record", "row-17");

        Assert.Equal("approval.outcome-writer", reason.Code);
        Assert.Equal(ReasonSeverity.Error, reason.Severity);
        Assert.Equal("Only the approval runner, or the person answering their own approval request on the product's page, can record an approval outcome.", reason.Message);
        Assert.Equal("Approval Record", reason.Entity);
        Assert.Equal("row-17", reason.Ref);
        Assert.Equal("[approval.outcome-writer] Only the approval runner, or the person answering their own approval request on the product's page, can record an approval outcome.", reason.ToString());
    }

    [Fact]
    public void CreateCanRaiseTheSeverity()
    {
        // At enforce, a missing approval stops the deployment, so the flag is raised to an error.
        Reason reason = ReasonCatalog.Create(ReasonCodes.GateMissingApproval, ReasonSeverity.Error);

        Assert.Equal(ReasonSeverity.Error, reason.Severity);
        Assert.Null(reason.Entity);
        Assert.Null(reason.Ref);
    }

    [Fact]
    public void UnknownCodesAreRefused()
    {
        Assert.False(ReasonCatalog.IsKnown("approval.made-up"));
        Assert.False(ReasonCatalog.IsKnown(null));
        Assert.False(ReasonCatalog.TryGet("approval.made-up", out _));
        Assert.Throws<ArgumentException>(() => ReasonCatalog.Get("approval.made-up"));
        Assert.Throws<ArgumentException>(() => ReasonCatalog.Create("approval.made-up"));
    }

    private static string[] CodesFor(string ruleId)
    {
        return ReasonCatalog.ForRule(ruleId).Select(info => info.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray();
    }
}
