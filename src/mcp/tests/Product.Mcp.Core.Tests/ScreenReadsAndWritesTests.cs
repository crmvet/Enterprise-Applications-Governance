using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The reads and writes of the remaining screens (D-36; document 24, B24-11): the AI activity, the
// audit trail, the evidence pack, the tier assessment, the documentation list, the places, the gap check runs, and the
// dependency and portfolio writes. Each case runs real tools through the server core against the in-memory store.
public sealed class ScreenReadsAndWritesTests
{
    private const string NewIntake = "{\"title\":\"Fixture crew overtime\",\"need\":\"Track overtime\",\"proposedKindKey\":\"power-platform-app\"}";

    // The keynote: Claude Code drafts an intake for the owner, the owner edits it in the web app and submits it, and
    // Claude Code then tries to answer an approval. The AI activity shows the draft, who took it forward and when, that a
    // person edited it first, and the refused attempt with its rule. The owner sees only the AI work done for them; the
    // platform admin sees the estate; a reader may not ask for someone else's.
    [Fact]
    public void AiActivityShowsTheDraftWhoTookItForwardAndTheRefusedAttempt()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        string intake = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_intake_draft", NewIntake)), "recordId");
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        h.Ok(McpHarness.TokenOwnerWeb, "save_intake_draft", "{\"intakeRequestId\":\"" + intake + "\",\"title\":\"Fixture crew overtime tracker\",\"need\":\"Track overtime per crew\",\"proposedKindKey\":\"power-platform-app\"}", server: web);
        h.Ok(McpHarness.TokenOwnerWeb, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}", server: web);
        string pending = h.Store.Query(RowQuery.For("approval-record").Where("outcomeMeaning", "pending")).First().Id;
        h.Fails(McpHarness.TokenOwner, "answer_approval", "{\"approvalRecordId\":\"" + pending + "\",\"answer\":\"approve\"}", McpErrorCodes.AiCannotDoThis);

        JsonTreeNode mine = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_ai_activity", "{}"));
        Assert.Equal("mine", Js.Text(mine, "scope"));
        JsonTreeNode draft = Js.Items(mine, "items").Single(i => Js.Text(i, "kind") == "draft" && McpHarness.Text(i, "record.recordId") == intake);
        Assert.Equal("submitted", Js.Text(draft, "state"));
        Assert.Equal(McpHarness.ClaudeCode, Js.Text(draft, "clientApp"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(draft, "onBehalfOf.objectId"));
        Assert.Equal("person", McpHarness.Text(draft, "takenForward.actorKind"));
        Assert.Equal("true", McpHarness.Text(draft, "takenForward.fromHistory"));
        Assert.Equal("true", McpHarness.Text(draft, "editedByPersonFirst"));
        JsonTreeNode refused = Js.Items(mine, "items").Single(i => Js.Text(i, "kind") == "refusal");
        Assert.Equal("answer_approval", McpHarness.Text(refused, "refusal.tool"));
        Assert.Equal(McpErrorCodes.AiCannotDoThis, McpHarness.Text(refused, "refusal.code"));
        Assert.Equal("approval-record", McpHarness.Text(refused, "record.entity"));
        Assert.Equal(1, Js.Number(Js.Member(mine, "counts"), "refused"));
        Assert.True(Js.Number(Js.Member(mine, "counts"), "takenForwardByPerson") >= 1);

        JsonTreeNode waiting = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_ai_activity", "{\"view\":\"refused\"}"));
        Assert.Single(Js.Items(waiting, "items"));

        h.Fails(McpHarness.TokenReader, "get_ai_activity", "{\"onBehalfOfObjectId\":\"" + McpHarness.Owner + "\"}", McpErrorCodes.NotAuthorized);
        JsonTreeNode estate = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"onBehalfOfObjectId\":\"" + McpHarness.Owner + "\"}"));
        Assert.Equal("estate", Js.Text(estate, "scope"));
        Assert.Contains(Js.Items(estate, "items"), i => McpHarness.Text(i, "record.recordId") == intake);
    }

    // The audit trail is the auditor's and the platform admin's (document 26, section 5.3). A record's trail lists every
    // call on it, each with its tool, the actor kind and the client, and the status it left; names come from the store.
    [Fact]
    public void OnlyTheAuditorAndThePlatformAdminReadTheAuditTrail()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.SubmittedIntake(h);
        h.Fails(McpHarness.TokenReader, "get_audit_trail", "{\"recordId\":\"" + intake + "\"}", McpErrorCodes.NotAuthorized);

        JsonTreeNode trail = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_audit_trail", "{\"recordId\":\"" + intake + "\"}"));
        var items = Js.Items(trail, "items");
        Assert.Contains(items, i => Js.Text(i, "tool") == "save_intake_draft" && Js.Text(i, "actorKind") == "ai-client-for-person" && Js.Text(i, "statusAfter") == "draft");
        JsonTreeNode submit = items.Single(i => Js.Text(i, "tool") == "submit_record");
        Assert.Equal("submitted", Js.Text(submit, "statusAfter"));
        Assert.Equal("intake-request", McpHarness.Text(submit, "record.entity"));
        Assert.Equal("Fixture travel requests", McpHarness.Text(submit, "record.label").Split(new[] { ": " }, System.StringSplitOptions.None).Last());
        Assert.Equal(McpHarness.Owner, McpHarness.Text(submit, "onBehalfOf.objectId"));

        // Refused calls are recorded with their rule: the reader's AI client asking for the trail, and another AI client's
        // write that the Tool Policy refused.
        h.Fails(McpHarness.TokenDeputy, "save_dependency", "{\"fromApplicationId\":\"" + McpHarness.Expense + "\",\"toApplicationId\":\"" + McpHarness.Travel + "\",\"dependencyTypeKey\":\"reads-data-from\"}", McpErrorCodes.NotAuthorized);
        JsonTreeNode refusals = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_audit_trail", "{\"operations\":[\"call-refused\"]}"));
        Assert.Contains(Js.Items(refusals, "items"), i => Js.Text(i, "tool") == "get_audit_trail" && McpHarness.Text(i, "actor.objectId") == McpHarness.Reader);
        JsonTreeNode entry = Js.Items(refusals, "items").Single(i => Js.Text(i, "tool") == "save_dependency");
        Assert.Equal(McpErrorCodes.NotAuthorized, McpHarness.Text(entry, "refusal.code"));
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(entry, "actor.objectId"));
    }

    // The evidence pack holds the decision with every approval request, the deployments with their flags, and the
    // configuration version stamped on each row, beside the version in force.
    [Fact]
    public void TheEvidencePackStampsEveryRowWithItsVersion()
    {
        McpHarness h = McpHarness.Create();
        string decision = Scenarios.SubmittedRetirement(h);
        Scenarios.Deployment(h);

        JsonTreeNode pack = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_evidence", "{\"decisionId\":\"" + decision + "\"}"));
        JsonTreeNode item = Assert.Single(Js.Items(pack, "decisions"));
        Assert.NotEmpty(Js.Items(item, "approvals"));
        Assert.Equal(1, Js.Number(item, "configurationSetVersion"));
        Assert.Equal(1, Js.Number(Js.Member(pack, "configurationInForce"), "configurationSetVersion"));
        Assert.Contains(Js.Items(pack, "versionsStamped"), v => v.TryGetInt64(out long n) && n == 1);

        JsonTreeNode app = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_evidence", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"));
        Assert.Single(Js.Items(app, "deployments"));
        Assert.Equal(1, Js.Number(Js.Member(app, "counts"), "deployments"));
        Assert.NotNull(Js.Member(Js.Items(app, "deployments")[0], "flags"));
        h.Fails(McpHarness.TokenReader, "get_evidence", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"decisionId\":\"" + decision + "\"}", McpErrorCodes.Validation);

        // The decision log filters by portfolio and names each decision's subject; an exception names its standard.
        JsonTreeNode log = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_decision_log", "{\"portfolioId\":\"" + McpHarness.Portfolio + "\"}"));
        JsonTreeNode logged = Js.Items(log, "items").Single(i => Js.Text(i, "decisionId") == decision);
        Assert.Equal("Fixture travel planner", Js.Text(logged, "subjectLabel"));
        Assert.Equal("null", McpHarness.Text(logged, "exception"));
    }

    // A person registers a target in the web app, and record_deployment then takes deployments to it. A second target
    // with the same link kind and external ID is refused and named; an AI client is never served the tool; the external
    // ID of a target is hidden from a caller who holds only the reader role.
    [Fact]
    public void APersonRegistersATargetAndADeploymentLandsOnIt()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        JsonTreeNode saved = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "save_deployment_target", Scenarios.TargetArguments(), server: web));
        string target = Js.Text(saved, "deploymentTargetId")!;
        Assert.Equal("person", McpHarness.Text(saved, "performedBy.actorKind"));
        Assert.Equal("production", McpHarness.Text(saved, "target.targetStage.key"));

        h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-ops", target));
        JsonTreeNode duplicate = h.Fails(McpHarness.TokenAdminWeb, "save_deployment_target", Scenarios.TargetArguments(), McpErrorCodes.Validation, server: web);
        Assert.Contains("Fixture operations production", McpHarness.Text(duplicate, "error.message"));
        h.Fails(McpHarness.TokenAdmin, "save_deployment_target", Scenarios.TargetArguments(), McpErrorCodes.AiCannotDoThis);
        h.Fails(McpHarness.TokenOwnerWeb, "save_deployment_target", Scenarios.TargetArguments(), McpErrorCodes.NotAuthorized, server: web);

        JsonTreeNode asReader = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_deployment_targets", "{\"targetStageKeys\":[\"production\"]}"));
        JsonTreeNode listed = Js.Items(asReader, "items").Single(t => Js.Text(t, "deploymentTargetId") == target);
        Assert.Equal("null", McpHarness.Text(listed, "externalId"));
        Assert.Equal("1.0.0", McpHarness.Text(Js.Items(listed, "applications").Single(), "artifactVersion"));
        JsonTreeNode asAdmin = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_deployment_targets", "{\"text\":\"operations\"}"));
        Assert.Equal("fixture-env-ops", McpHarness.Text(Assert.Single(Js.Items(asAdmin, "items")), "externalId"));
    }

    // A dependency is recorded as reported, a second save for the same pair changes it, the engine refuses a dependency on
    // its own application and any change to an ended one, and a person who owns neither application nor holds the role is
    // refused by the Tool Policy.
    [Fact]
    public void ADependencyIsRecordedChangedAndEnded()
    {
        McpHarness h = McpHarness.Create();
        string link = "{\"fromApplicationId\":\"" + McpHarness.Expense + "\",\"toApplicationId\":\"" + McpHarness.Travel + "\",\"dependencyTypeKey\":\"reads-data-from\"";
        JsonTreeNode first = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_dependency", link + ",\"criticalityKey\":\"low\"}"));
        string dependency = Js.Text(first, "dependencyId")!;
        Assert.True(Js.Flag(first, "created"));
        Assert.True(Js.Flag(first, "isAiCreated"));

        JsonTreeNode again = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_dependency", link + ",\"criticalityKey\":\"high\"}"));
        Assert.Equal(dependency, Js.Text(again, "dependencyId"));
        Assert.False(Js.Flag(again, "created"));
        Assert.Equal("high", McpHarness.Text(again, "criticality.key"));

        JsonTreeNode self = h.Fails(McpHarness.TokenOwner, "save_dependency", "{\"fromApplicationId\":\"" + McpHarness.Expense + "\",\"toApplicationId\":\"" + McpHarness.Expense + "\",\"dependencyTypeKey\":\"reads-data-from\"}", McpErrorCodes.RuleViolation);
        Assert.Contains("own application", McpHarness.Text(self, "error.message"));

        h.Ok(McpHarness.TokenOwner, "save_dependency", "{\"dependencyId\":\"" + dependency + "\",\"endedOn\":\"2026-11-22\"}");
        h.Fails(McpHarness.TokenOwner, "save_dependency", "{\"dependencyId\":\"" + dependency + "\",\"criticalityKey\":\"low\"}", McpErrorCodes.RuleViolation);
        h.Fails(McpHarness.TokenDeputy, "save_dependency", link + "}", McpErrorCodes.NotAuthorized);

        JsonTreeNode card = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_application_card", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"sections\":[\"dependencies\"]}"));
        Assert.Equal(0, Js.Number(Js.Member(card, "dependencies"), "dependsOnCount"));
    }

    // The platform admin creates a portfolio inside another; a portfolio never moves inside its own child; a person who
    // holds neither the platform admin nor the PMO role is refused, and an AI client is never served the tool.
    [Fact]
    public void APortfolioIsCreatedAndNeverSitsInsideItself()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        JsonTreeNode created = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "save_portfolio", "{\"name\":\"Fixture field operations\",\"levelKey\":\"application-group\",\"parentPortfolioId\":\"" + McpHarness.Portfolio + "\",\"accountablePrincipalObjectId\":\"" + McpHarness.Owner + "\"}", server: web));
        string child = Js.Text(created, "portfolioId")!;
        Assert.Equal(2, Js.Items(Js.Member(created, "portfolio"), "path").Count);
        Assert.Equal(McpHarness.Owner, McpHarness.Text(created, "portfolio.accountablePrincipal.objectId"));

        JsonTreeNode cycle = h.Fails(McpHarness.TokenAdminWeb, "save_portfolio", "{\"portfolioId\":\"" + McpHarness.Portfolio + "\",\"parentPortfolioId\":\"" + child + "\"}", McpErrorCodes.RuleViolation, server: web);
        Assert.Contains("never sits inside itself", McpHarness.Text(cycle, "error.message"));
        h.Fails(McpHarness.TokenAdminWeb, "save_portfolio", "{\"name\":\"Fixture field operations\",\"levelKey\":\"application-group\",\"parentPortfolioId\":\"" + McpHarness.Portfolio + "\"}", McpErrorCodes.Validation, server: web);
        h.Fails(McpHarness.TokenOwnerWeb, "save_portfolio", "{\"name\":\"Fixture other\",\"levelKey\":\"portfolio\"}", McpErrorCodes.NotAuthorized, server: web);
        h.Fails(McpHarness.TokenAdmin, "save_portfolio", "{\"name\":\"Fixture other\",\"levelKey\":\"portfolio\"}", McpErrorCodes.AiCannotDoThis);

        JsonTreeNode listed = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_portfolios", "{\"parentPortfolioId\":\"" + McpHarness.Portfolio + "\"}"));
        Assert.Equal(child, McpHarness.Text(Assert.Single(Js.Items(listed, "items")), "portfolioId"));
    }

    // The tier assessment names every answer, the answers that drive the tier and what the tier needs; an application
    // with no assessment answers null and points to the tier questions.
    [Fact]
    public void TheTierAssessmentNamesTheDrivingAnswers()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.SubmittedIntake(h);
        JsonTreeNode assessment = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_tier_assessment", "{\"intakeRequestId\":\"" + intake + "\"}"));
        Assert.Equal(4, Js.Items(assessment, "answers").Count);
        Assert.Contains(Js.Items(assessment, "answers"), a => Js.Flag(a, "isDriving") == true);
        Assert.Equal(intake, McpHarness.Text(assessment, "assessment.intakeRequestId"));
        Assert.Equal("false", McpHarness.Text(assessment, "assessment.isFrozen"));

        JsonTreeNode none = h.Ok(McpHarness.TokenReader, "get_tier_assessment", "{\"businessApplicationId\":\"" + McpHarness.Travel + "\"}");
        Assert.Equal("null", McpHarness.Text(McpHarness.Data(none), "assessment"));
        Assert.Equal("get_tier_questions", McpHarness.Text(Js.Items(none, "nextSteps")[0], "tool"));
    }

    // The documentation list computes missing documents across the portfolio; mineOnly keeps the person's own.
    [Fact]
    public void TheDocumentListComputesMissingDocuments()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode all = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_documents", "{}"));
        long missing = Js.Number(Js.Member(all, "counts"), "missing") ?? 0;
        Assert.True(missing >= 1);
        Assert.Contains(Js.Items(all, "items"), d => Js.Text(d, "documentLinkId") == McpHarness.WikiLink && Js.Text(d, "applicationName") == "Fixture expense approvals");

        JsonTreeNode onlyMissing = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_documents", "{\"state\":\"missing\"}"));
        Assert.Equal(missing, Js.Number(onlyMissing, "totalCount"));
        Assert.All(Js.Items(onlyMissing, "items"), d => Assert.Equal("null", McpHarness.Text(d, "documentLinkId")));

        JsonTreeNode readersOwn = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_documents", "{\"mineOnly\":true}"));
        Assert.Equal(0, Js.Number(readersOwn, "totalCount"));
    }

    // Gap check runs follow claim-visibility (Balanced: admins-and-claimant): the platform admin sees the run; an
    // application owner is served the tool and gets the runs counted in couldNotRead.
    [Fact]
    public void GapCheckRunsFollowClaimVisibility()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.Claim(h);
        JsonTreeNode runs = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_gap_check_runs", "{}"));
        JsonTreeNode run = Assert.Single(Js.Items(runs, "items"));
        // A list from the admin's own tool names no final page, so its run stays open until a final page comes.
        Assert.Equal("not-finished", Js.Text(run, "state"));
        Assert.Equal(1, Js.Number(run, "newClaims"));
        Assert.Equal(McpHarness.Admin, McpHarness.Text(run, "runBy.objectId"));

        JsonTreeNode hidden = h.Ok(McpHarness.TokenOwner, "list_gap_check_runs", "{}");
        Assert.Empty(Js.Items(McpHarness.Data(hidden), "items"));
        Assert.Contains(Js.Items(hidden, "couldNotRead"), c => Js.Text(c, "kind") == "gap-check-run" && Js.Number(c, "count") == 1);
    }

    // The deployments list filters by named flags and counts each flag for the same filters.
    [Fact]
    public void DeploymentsFilterByFlagAndCountEachFlag()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.Deployment(h);
        h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-2", McpHarness.Production));
        JsonTreeNode all = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_deployments", "{\"latestOnly\":false}"));
        Assert.Equal(2, Js.Number(all, "totalCount"));
        JsonTreeNode counts = Js.Member(all, "counts")!;
        Assert.Equal(2, Js.Number(counts, "records"));
        foreach ((string key, string count) in new[] { ("other-path", "otherPath"), ("missing-approval", "missingApproval"), ("outside-window", "outsideWindow"), ("ai-only-code-approval", "aiOnlyCodeApproval") })
        {
            JsonTreeNode only = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_deployments", "{\"latestOnly\":false,\"flagKeys\":[\"" + key + "\"]}"));
            Assert.Equal(Js.Number(counts, count), Js.Number(only, "totalCount"));
        }

        JsonTreeNode onProduction = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_deployments", "{\"deploymentTargetId\":\"" + McpHarness.Production + "\"}"));
        JsonTreeNode item = Assert.Single(Js.Items(onProduction, "items"));
        Assert.Equal("true", McpHarness.Text(item, "isAiCreated"));
        Assert.Equal("ai-client-for-person", McpHarness.Text(item, "performedBy.actorKind"));
    }
}
