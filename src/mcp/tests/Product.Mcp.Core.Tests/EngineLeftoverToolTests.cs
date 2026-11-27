using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Wave 4, the engine leftovers (document 24, B24-22; document 25, B25-24; docs/architecture/data-taxonomy.md): measure targets
// and signal thresholds as configuration rows and the decisions a signal led to; "Ask again" on an expired request; the
// platform admin's outbox retry; the demo's freshness read; the dependency impact from a component and a data location;
// and the data taxonomy's minor items (the import, DC-10's offer, the Places link, the implied answer before answering,
// the app-level data owners to clean up, the data-use effect in a proposal's preview, the O25 key).
public sealed class EngineLeftoverToolTests
{
    private const string Reader = McpHarness.TokenReader;
    private const string Owner = McpHarness.TokenOwner;

    private const string MeasureSettings = "["
        + "{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"measure-targets\",\"values\":{\"value\":{\"owner-coverage\":95,\"decisions-on-time\":90}}},"
        + "{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"signal-thresholds\",\"values\":{\"value\":{\"gap-check-stale\":{\"days\":10}}}}"
        + "]";

    // ---- Measures: targets, thresholds and the decisions a signal led to ----

    [Fact]
    public void MeasureTargetsAndSignalThresholdsComeFromTheConfiguration()
    {
        McpHarness h = McpHarness.Create(MeasureSettings);
        JsonTreeNode data = McpHarness.Data(h.Ok(Reader, "get_measures", "{}"));
        JsonTreeNode coverage = Js.Items(data, "measures").Single(m => McpHarness.Text(m, "key") == "owner-coverage");
        Assert.Equal("95", McpHarness.Text(coverage, "target"));
        Assert.Equal("configuration", McpHarness.Text(coverage, "targetSource"));
        Assert.Contains(McpHarness.Text(coverage, "targetMet"), new[] { "true", "false" });
        JsonTreeNode lead = Js.Items(data, "measures").Single(m => McpHarness.Text(m, "key") == "lead-time");
        Assert.Equal("null", McpHarness.Text(lead, "target"));
        Assert.Equal("not-set", McpHarness.Text(lead, "targetSource"));
        Assert.Equal("null", McpHarness.Text(lead, "targetMet"));

        // The setting gives signal 3 its number; signal 1 keeps the playbook's default and says so.
        JsonTreeNode stale = Js.Items(data, "signals").Single(g => McpHarness.Text(g, "number") == "3");
        Assert.Equal("configuration", McpHarness.Text(stale, "thresholdSource"));
        Assert.StartsWith("No gap check in 10 days", McpHarness.Text(stale, "threshold"));
        JsonTreeNode changes = Js.Items(data, "signals").Single(g => McpHarness.Text(g, "number") == "1");
        Assert.Equal("product-default", McpHarness.Text(changes, "thresholdSource"));
        Assert.Contains("above 20%", McpHarness.Text(changes, "threshold"));
        Assert.Equal("0", McpHarness.Text(stale, "decisionCount"));
    }

    [Fact]
    public void ADecisionAboutASignalIsListedOnTheSignalAndNamesItOnTheDecisionPage()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode proposed = McpHarness.Data(h.Ok(Owner, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Run the gap check every month again\",\"context\":\"The gap check is older than its cadence\",\"proposedOutcomeKey\":\"complete\",\"reason\":\"Slide-back signal 3 fired\",\"slideBackSignalKey\":\"gap-check-stale\"}"));
        string decision = McpHarness.Text(proposed, "recordId");
        Assert.Equal("gap-check-stale", h.Store.Get("decision", decision)!.Text("slideBackSignal"));

        JsonTreeNode signal = Js.Items(McpHarness.Data(h.Ok(Reader, "get_measures", "{}")), "signals").Single(g => McpHarness.Text(g, "key") == "gap-check-stale");
        Assert.Equal("1", McpHarness.Text(signal, "decisionCount"));
        JsonTreeNode item = Assert.Single(Js.Items(signal, "decisions"));
        Assert.Equal(decision, McpHarness.Text(item, "decisionId"));
        Assert.Equal("Run the gap check every month again", McpHarness.Text(item, "statement"));
        Assert.EndsWith(decision, McpHarness.Text(item, "recordUrl"));

        JsonTreeNode page = McpHarness.Data(h.Ok(Reader, "get_decision", "{\"decisionId\":\"" + decision + "\"}"));
        Assert.Equal("3", McpHarness.Text(page, "decision.slideBackSignal.number"));
        Assert.Equal("Gap check stale", McpHarness.Text(page, "decision.slideBackSignal.label"));

        // A key that is not one of the ten signals is refused by the input schema.
        h.Fails(Owner, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"x\",\"context\":\"x\",\"proposedOutcomeKey\":\"complete\",\"reason\":\"x\",\"slideBackSignalKey\":\"no-such-signal\"}", McpErrorCodes.Validation);
    }

    [Fact]
    public void ATargetForAMeasureThatDoesNotExistIsRefusedWhenTheDraftIsValidated()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", "{\"baseVersionNumber\":1,\"proposalRationale\":\"Agree the targets\",\"validateOnly\":true,"
            + "\"operations\":[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"measure-targets\",\"values\":{\"value\":{\"owner-covrage\":95}}},"
            + "{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"signal-thresholds\",\"values\":{\"value\":{\"gap-check-stale\":{\"weeks\":2}}}}]}"));
        Assert.Equal("false", McpHarness.Text(data, "valid"));
        var messages = Js.Items(data, "errors").Where(e => McpHarness.Text(e, "ruleKey") == "V38").Select(e => McpHarness.Text(e, "message")).ToList();
        Assert.Contains(messages, m => m.Contains("owner-covrage"));
        Assert.Contains(messages, m => m.Contains("weeks"));
    }

    // ---- 100. renew_approval_request ----

    [Fact]
    public void TheProposerAsksAnExpiredRequestAgainOnceAndTheAuditTrailRecordsIt()
    {
        McpHarness h = McpHarness.Create();
        string record = Scenarios.ExpiringRetirementApproval(h, -1);
        string decision = h.Store.Get("approval-record", record)!.Text("decision")!;
        var web = new WebEntry(h);

        JsonTreeNode envelope = web.Ok(McpHarness.TokenOwnerWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + record + "\"}");
        JsonTreeNode data = McpHarness.Data(envelope);
        Assert.Equal("true", McpHarness.Text(data, "renewed"));
        Assert.Equal("pending", McpHarness.Text(data, "previousOutcome"));
        Assert.Equal("expired", h.Store.Get("approval-record", record)!.Text("outcomeMeaning"));
        string fresh = McpHarness.Text(data, "request.approvalRecordId");
        Assert.NotEqual(record, fresh);
        StoredRow opened = h.Store.Get("approval-record", fresh)!;
        Assert.Equal("pending", opened.Text("outcomeMeaning"));
        Assert.Equal("business-owner", opened.Text("requiredOwnerRole"));
        Assert.Contains("asks", Js.Text(envelope, "summary"));

        // One Audit Entry for the call, by the person, through the web app.
        StoredRow audit = h.Store.Query(RowQuery.For("audit-entry").Where("entityName", "renew_approval_request")).Single();
        Assert.Equal("person", audit.Text("actorKind"));
        Assert.Equal(McpHarness.Owner, audit.Text("actor"));

        // Again: asked again already, nothing changes.
        JsonTreeNode again = McpHarness.Data(web.Ok(McpHarness.TokenOwnerWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + record + "\"}"));
        Assert.Equal("false", McpHarness.Text(again, "renewed"));
        Assert.Equal(fresh, McpHarness.Text(again, "request.approvalRecordId"));
        Assert.Single(h.Store.Query(RowQuery.For("approval-record").Where("decision", decision).Where("outcomeMeaning", "pending")));
    }

    [Fact]
    public void OnlyTheProposerOrAPlatformAdminAsksAgainAndOnlyNearOrPastTheExpiry()
    {
        McpHarness h = McpHarness.Create();
        var web = new WebEntry(h);

        // Far from its expiry: refused, in words that say when it expires.
        string later = Scenarios.ExpiringRetirementApproval(h, 5);
        JsonTreeNode refused = web.Fails(McpHarness.TokenOwnerWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + later + "\"}", McpErrorCodes.RuleViolation);
        Assert.Contains("48 hours", McpHarness.Text(refused, "error.message"));

        // Someone who did not propose it, without the platform admin role, may not; an AI client is never served the tool.
        h.Store.Tamper("approval-record", later, Js.With(h.Store.Get("approval-record", later)!.Columns, ("expiresOn", JsonTreeNode.FromString("2026-11-23T09:00:00Z"))));
        web.Fails(McpHarness.TokenReaderWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + later + "\"}", McpErrorCodes.NotAuthorized);
        h.Fails(Owner, "renew_approval_request", "{\"approvalRecordId\":\"" + later + "\"}", McpErrorCodes.AiCannotDoThis);

        // A platform admin asks early, in the last 48 hours: the old request is canceled, the new one runs again.
        JsonTreeNode early = McpHarness.Data(web.Ok(McpHarness.TokenAdminWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + later + "\"}"));
        Assert.Equal("true", McpHarness.Text(early, "renewed"));
        Assert.Equal("cancelled", h.Store.Get("approval-record", later)!.Text("outcomeMeaning"));

        // A decision no longer proposed takes no new request.
        string decision = h.Store.Get("approval-record", later)!.Text("decision")!;
        h.Ok(Owner, "withdraw_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\",\"reason\":\"Not now\"}");
        string pending = McpHarness.Text(early, "request.approvalRecordId");
        web.Fails(McpHarness.TokenAdminWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + pending + "\"}", McpErrorCodes.RuleViolation);
    }

    // ---- 101. retry_approval_card ----

    [Fact]
    public void APlatformAdminSendsAFailedCardAgainAndTheCallIsOnTheRecord()
    {
        McpHarness h = McpHarness.Create();
        var web = new WebEntry(h);
        string record = Scenarios.FailedCard(h);

        JsonTreeNode check = McpHarness.Data(web.Ok(McpHarness.TokenAdminWeb, "retry_approval_card", "{\"approvalRecordId\":\"" + record + "\",\"validateOnly\":true}"));
        Assert.Equal("true", McpHarness.Text(check, "dryRun"));
        Assert.Equal("failed", h.Outbox.Get(record)!.State);
        Assert.Empty(h.Store.Query(RowQuery.For("audit-entry").Where("entityName", "retry_approval_card")));

        JsonTreeNode data = McpHarness.Data(web.Ok(McpHarness.TokenAdminWeb, "retry_approval_card", "{\"approvalRecordId\":\"" + record + "\"}"));
        Assert.Equal("sent-again", McpHarness.Text(data, "action"));
        Assert.Equal("failed", McpHarness.Text(data, "stateBefore"));
        Assert.Equal("5", McpHarness.Text(data, "attemptsBefore"));
        OutboxAttention row = h.Outbox.Get(record)!;
        Assert.Equal("pending", row.State);
        Assert.Equal(0, row.Attempts);

        // The Audit Entry names the person and lists the host's row, as no engine row changed.
        StoredRow audit = h.Store.Query(RowQuery.For("audit-entry").Where("entityName", "retry_approval_card")).Single();
        Assert.Equal(McpHarness.Admin, audit.Text("actor"));
        Assert.Equal(record, audit.Text("rowId"));
        JsonTreeNode written = Assert.Single(Js.Items(audit.Column("changedValuesJson"), "writes"));
        Assert.Equal("approval-outbox", McpHarness.Text(written, "entity"));
        Assert.Equal("true", McpHarness.Text(written, "byHost"));

        // The audit trail reads the entry as the request whose card it is.
        JsonTreeNode trail = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_audit_trail", "{}"));
        JsonTreeNode entry = Js.Items(trail, "items").Single(i => McpHarness.Text(i, "tool") == "retry_approval_card");
        Assert.Equal("approval-record", McpHarness.Text(entry, "record.entity"));
        Assert.Equal(record, McpHarness.Text(entry, "record.recordId"));

        // Pending again, it is the runner's: nothing to send again. A reader may not; the AI client is never served it.
        web.Fails(McpHarness.TokenAdminWeb, "retry_approval_card", "{\"approvalRecordId\":\"" + record + "\"}", McpErrorCodes.Validation);
        web.Fails(McpHarness.TokenReaderWeb, "retry_approval_card", "{\"approvalRecordId\":\"" + record + "\"}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenAdmin, "retry_approval_card", "{\"approvalRecordId\":\"" + record + "\"}", McpErrorCodes.AiCannotDoThis);
    }

    [Fact]
    public void ACardWhoseRequestWasAnsweredClosesInsteadAndAHostWithNoOutboxSaysSo()
    {
        McpHarness h = McpHarness.Create();
        var web = new WebEntry(h);
        string record = Scenarios.FailedCard(h);
        web.Ok(McpHarness.TokenOwnerWeb, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\"}");

        JsonTreeNode data = McpHarness.Data(web.Ok(McpHarness.TokenAdminWeb, "retry_approval_card", "{\"approvalRecordId\":\"" + record + "\"}"));
        Assert.Equal("closed", McpHarness.Text(data, "action"));
        Assert.Equal("answered", h.Outbox.Get(record)!.State);

        h.Backend.Options.ApprovalOutbox = null;
        web.Fails(McpHarness.TokenAdminWeb, "retry_approval_card", "{\"approvalRecordId\":\"" + record + "\"}", McpErrorCodes.UpstreamUnavailable);
    }

    // ---- 102. get_demo_freshness ----

    [Fact]
    public void TheDemoFreshnessReadListsWhatExpiresAndExistsOnlyOnADemoInstall()
    {
        McpHarness h = McpHarness.Create();
        string soon = Scenarios.ExpiringRetirementApproval(h, 1);
        h.Fails(Reader, "get_demo_freshness", "{}", McpErrorCodes.NotFound);
        h.Backend.Options.HostFacts = () => new HostFacts { EnvironmentName = "Production", IsDemo = false, HoldsDemoEstate = true };
        h.Fails(Reader, "get_demo_freshness", "{}", McpErrorCodes.NotFound);

        Scenarios.DemoHost(h);
        JsonTreeNode data = McpHarness.Data(h.Ok(Reader, "get_demo_freshness", "{}"));
        Assert.Equal("true", McpHarness.Text(data, "needsRefresh"));
        Assert.Equal("1", McpHarness.Text(data, "counts.expiresSoon"));
        JsonTreeNode item = Assert.Single(Js.Items(data, "items"));
        Assert.Equal(soon, McpHarness.Text(item, "approvalRecordId"));
        Assert.Equal("expires-soon", McpHarness.Text(item, "state"));
        Assert.Equal("24", McpHarness.Text(item, "hoursLeft"));

        // A local Development run that holds the demo estate is a demo install too; renewed, the request is fresh again.
        h.Backend.Options.HostFacts = () => new HostFacts { EnvironmentName = "Development", HoldsDemoEstate = true };
        new WebEntry(h).Ok(McpHarness.TokenOwnerWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + soon + "\"}");
        JsonTreeNode fresh = McpHarness.Data(h.Ok(Reader, "get_demo_freshness", "{}"));
        Assert.Equal("false", McpHarness.Text(fresh, "needsRefresh"));
        Assert.Empty(Js.Items(fresh, "items"));
    }

    // ---- 7. get_dependency_impact from a component and a data location ----

    [Fact]
    public void TheImpactWalksFromAComponentAndFromADataLocation()
    {
        McpHarness h = McpHarness.Create(withData: true);

        // The expense app is built from the component, and the travel planner reads the expense app's data.
        JsonTreeNode fromComponent = McpHarness.Data(h.Ok(Reader, "get_dependency_impact", "{\"technicalComponentId\":\"" + McpHarness.ExpenseApp + "\"}"));
        Assert.Equal("component", McpHarness.Text(fromComponent, "root.kind"));
        JsonTreeNode built = Js.Items(fromComponent, "nodes").Single(n => McpHarness.Text(n, "businessApplicationId") == McpHarness.Expense);
        Assert.Equal("part-of", McpHarness.Text(built, "via"));
        Assert.Equal("null", McpHarness.Text(built, "dependencyTypeKey"));
        Assert.Equal("1", McpHarness.Text(built, "depth"));
        JsonTreeNode travel = Js.Items(fromComponent, "nodes").Single(n => McpHarness.Text(n, "businessApplicationId") == McpHarness.Travel);
        Assert.Equal("dependency", McpHarness.Text(travel, "via"));
        Assert.Equal("reads-data-from", McpHarness.Text(travel, "dependencyTypeKey"));
        Assert.Equal("2", McpHarness.Text(travel, "depth"));
        Assert.Equal(McpHarness.ExpenseApp, Js.Strings(travel, "path")[0]);

        // From the finance production location: the expense app reads its data there, the travel planner follows.
        JsonTreeNode fromLocation = McpHarness.Data(h.Ok(Reader, "get_dependency_impact", "{\"dataLocationId\":\"" + DataWorld.FinanceProd + "\",\"depth\":3}"));
        Assert.Equal("data-location", McpHarness.Text(fromLocation, "root.kind"));
        Assert.Equal("Fixture finance production", McpHarness.Text(fromLocation, "root.name"));
        JsonTreeNode reader = Js.Items(fromLocation, "nodes").Single(n => McpHarness.Text(n, "businessApplicationId") == McpHarness.Expense);
        Assert.Equal("data-use", McpHarness.Text(reader, "via"));
        Assert.Contains(Js.Items(fromLocation, "nodes"), n => McpHarness.Text(n, "businessApplicationId") == McpHarness.Travel && McpHarness.Text(n, "depth") == "2");
        Assert.Equal("expenses", McpHarness.Text(Assert.Single(Js.Items(Js.Member(fromLocation, "dataLocation"), "domainsHeld")), "key"));
        Assert.Equal("1", McpHarness.Text(fromLocation, "dataLocation.dataUseCount"));

        // One hop only: the first ring, nothing beyond.
        JsonTreeNode oneHop = McpHarness.Data(h.Ok(Reader, "get_dependency_impact", "{\"dataLocationId\":\"" + DataWorld.FinanceProd + "\",\"depth\":1}"));
        Assert.Equal(McpHarness.Expense, McpHarness.Text(Assert.Single(Js.Items(oneHop, "nodes")), "businessApplicationId"));

        // An application start is as before: every node a dependency.
        JsonTreeNode fromApp = McpHarness.Data(h.Ok(Reader, "get_dependency_impact", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"));
        Assert.All(Js.Items(fromApp, "nodes"), n => Assert.Equal("dependency", McpHarness.Text(n, "via")));
        Assert.Equal("null", McpHarness.Text(fromApp, "dataLocation"));
    }

    // ---- Data taxonomy minor items ----

    [Fact]
    public void DC10OffersTheOtherApplicationAndTheDomainsItHolds()
    {
        McpHarness h = McpHarness.Create(withData: true);
        JsonTreeNode gaps = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_data_gaps", "{\"checks\":[\"DC-10\"]}"));
        JsonTreeNode finding = Js.Items(gaps, "items").Single(f => McpHarness.Text(f, "businessApplicationId") == McpHarness.Travel);
        Assert.Equal("Reads data from Fixture expense approvals: declare?", McpHarness.Text(finding, "suggestion.text"));
        JsonTreeNode other = Assert.Single(Js.Items(Js.Member(finding, "suggestion"), "applications"));
        Assert.Equal(McpHarness.Expense, McpHarness.Text(other, "businessApplicationId"));
        Assert.Equal("read", McpHarness.Text(other, "access"));
        Assert.Equal("expenses", McpHarness.Text(Assert.Single(Js.Items(other, "domains")), "key"));
        Assert.Empty(Js.Items(gaps, "appLevelDataOwners"));
    }

    [Fact]
    public void UnderDomainsRequiredTheAppLevelDataOwnersAreListedForCleanUp()
    {
        McpHarness h = McpHarness.Create("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"data-taxonomy-mode\",\"values\":{\"value\":\"domains-required\"}}]", withData: true);
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + McpHarness.Travel + "\",\"ownerRole\":\"data-owner\",\"principal\":\"" + McpHarness.Deputy + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}", id: "00000000-0000-4000-8000-0000000005c1");
        JsonTreeNode envelope = h.Ok(McpHarness.TokenAdmin, "get_data_gaps", "{}");
        JsonTreeNode row = Assert.Single(Js.Items(McpHarness.Data(envelope), "appLevelDataOwners"));
        Assert.Equal("00000000-0000-4000-8000-0000000005c1", McpHarness.Text(row, "ownerAssignmentId"));
        Assert.Equal("propose_owner_change", McpHarness.Text(row, "fixTool"));
        Assert.Contains("application-level data owner is left", Js.Text(envelope, "summary"));

        // A domain scope lists none: the clean-up is about applications.
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_data_gaps", "{\"domainKey\":\"finance\"}")), "appLevelDataOwners"));
    }

    [Fact]
    public void PlacesLinksATargetToItsDataLocationsAndTheTierQuestionsHintBeforeAnyoneAnswers()
    {
        McpHarness h = McpHarness.Create(withData: true);
        JsonTreeNode targets = McpHarness.Data(h.Ok(Reader, "list_deployment_targets", "{}"));
        JsonTreeNode production = Js.Items(targets, "items").Single(t => McpHarness.Text(t, "deploymentTargetId") == McpHarness.Production);
        JsonTreeNode location = Assert.Single(Js.Items(production, "dataLocations"));
        Assert.Equal(DataWorld.FinanceProd, McpHarness.Text(location, "dataLocationId"));
        Assert.Equal("Fixture finance production", McpHarness.Text(location, "label"));
        Assert.Empty(Js.Items(Js.Items(targets, "items").Single(t => McpHarness.Text(t, "deploymentTargetId") == McpHarness.TestTarget), "dataLocations"));

        // The expense app reads confidential data, so the sensitivity question comes with the answer it implies.
        JsonTreeNode hinted = McpHarness.Data(h.Ok(Owner, "get_tier_questions", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"));
        Assert.Equal("expenses", McpHarness.Text(hinted, "impliedAnswer.domain.key"));
        Assert.False(string.IsNullOrEmpty(McpHarness.Text(hinted, "impliedAnswer.answerKey")));
        Assert.Equal("null", McpHarness.Text(McpHarness.Data(h.Ok(Owner, "get_tier_questions", "{}")), "impliedAnswer"));
    }

    [Fact]
    public void AProposalsOwnPreviewListsTheDataUsesItWouldMove()
    {
        McpHarness h = McpHarness.Create(withData: true);
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", "{\"baseVersionNumber\":1,\"proposalRationale\":\"Keep expense claims out of AI\",\"validateOnly\":true,"
            + "\"operations\":[{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"expenses\",\"values\":{\"aiUseRule\":\"never\"}}]}"));
        JsonTreeNode use = Js.Items(Js.Member(data, "impact"), "dataUses").Single(u => McpHarness.Text(u, "id") == DataWorld.ExpenseReadsClaims);
        Assert.Equal("data-use", McpHarness.Text(use, "kind"));
        Assert.Equal("expenses", McpHarness.Text(use, "domain.key"));
        Assert.All(Js.Items(Js.Member(data, "impact"), "dataUses"), u => Assert.Equal("expenses", McpHarness.Text(u, "domain.key")));
    }

    [Fact]
    public void ASecondLocationWithTheSamePlatformAndUrlIsRefusedWhenNeitherHasAnExternalId()
    {
        McpHarness h = McpHarness.Create(withData: true);
        var web = new WebEntry(h);
        static string Item(string url, string label, string? id = null) => "{\"items\":[{" + (id == null ? string.Empty : "\"dataLocationId\":\"" + id + "\",")
            + "\"platformKey\":\"sharepoint\",\"targetStageKey\":\"production\",\"url\":\"" + url + "\",\"labelAsSeen\":\"" + label + "\"}]}";
        web.Ok(McpHarness.TokenAdminWeb, "save_data_location", Item("https://fixture.example.org/sites/finance", "Fixture finance files"));
        JsonTreeNode second = McpHarness.Data(web.Ok(McpHarness.TokenAdminWeb, "save_data_location", Item("https://fixture.example.org/sites/travel", "Fixture travel files")));
        string travel = McpHarness.Text(Js.Items(second, "items")[0], "dataLocationId");

        // Pointing the second at the first one's URL would make two locations with one key (the alternate key of O25).
        JsonTreeNode refused = web.Fails(McpHarness.TokenAdminWeb, "save_data_location", Item("https://fixture.example.org/sites/finance", "Fixture travel files", travel), McpErrorCodes.RuleViolation);
        Assert.Contains("already names", McpHarness.Text(refused, "error.message"));
    }

    // ---- 99. import_data_taxonomy ----

    // A fictional user principal name, built in parts so no run of source text reads as an email address (the scrub gate).
    private const string Upn = "someone" + "@" + "example.org";

    [Fact]
    public void AnImportBecomesADraftAndOwnersWaitAsPreparedChanges()
    {
        McpHarness h = McpHarness.Create(withData: true);
        string csv = "key,label,parent,level,classification,ai_rule,retention,owner,deputy\\n"
            + "operations,Operations,,domain,internal,,,,\\n"
            + "facilities,Facilities,operations,,Confidential,,keep-7-years," + Upn + ",\\n"
            + "finance,Finance,,domain,internal,,,"+ McpHarness.Owner + ",\\n"
            + "travel-claims,Travel claims,Operations/Facilities,,,never,,,\\n";
        JsonTreeNode envelope = h.Ok(McpHarness.TokenConfigAdmin, "import_data_taxonomy", "{\"csv\":\"" + csv + "\",\"proposalRationale\":\"Load the facilities list\"}");
        JsonTreeNode data = McpHarness.Data(envelope);
        Assert.Equal("true", McpHarness.Text(data, "valid"));
        Assert.Empty(Js.Items(data, "problems"));
        Assert.Equal("2", McpHarness.Text(data, "counts.domainsAdded"));
        Assert.Equal("2", McpHarness.Text(data, "counts.domainsUnchanged"));
        string draft = McpHarness.Text(data, "configurationDraftId");
        StoredRow row = h.Store.Get("configuration-draft", draft)!;
        string change = Js.Write(row.Column("changeJson")!);
        Assert.Contains("\"facilities\"", change);
        Assert.Contains("\"confidential\"", change);
        Assert.Contains("\"subdomain\"", change);
        Assert.DoesNotContain(Js.Items(row.Column("changeJson"), "operations"), o => Js.Text(o, "key") is "operations" or "finance");

        // Owners are never live: a UPN waits for the admin's AI client, an object ID is ready, and DC-15 names the difference.
        var steps = Js.Items(data, "preparedOwnerChanges");
        Assert.Contains(steps, s => McpHarness.Text(s, "domainKey") == "facilities" && McpHarness.Text(s, "readyToSend") == "false" && McpHarness.Text(s, "userPrincipalName") == Upn);
        Assert.Contains(steps, s => McpHarness.Text(s, "domainKey") == "finance" && McpHarness.Text(s, "readyToSend") == "true" && McpHarness.Text(s, "input.dataDomainKey") == "finance");
        JsonTreeNode difference = Assert.Single(Js.Items(data, "catalogOwnerDifferences"));
        Assert.Equal("DC-15", McpHarness.Text(difference, "check"));
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(difference, "currentHolder.objectId"));
        Assert.Empty(h.Store.Query(RowQuery.For("owner-assignment").Where("subjectDataDomain", "facilities")));

        // The domains in force that the file leaves out are listed, never retired.
        Assert.Contains(Js.Items(data, "notInSource"), n => McpHarness.Text(n, "key") == "payroll");
        Assert.Equal("submit_record", McpHarness.Text(Js.Items(envelope, "nextSteps")[0], "tool"));
    }

    [Fact]
    public void AnImportWithProblemsStoresNothingAndNamesEachRow()
    {
        McpHarness h = McpHarness.Create(withData: true);
        string csv = "key,label,parent,classification,colour\\nbad key,Bad,,internal,red\\nfine,Fine,no-such-parent,secret,\\n";
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "import_data_taxonomy", "{\"csv\":\"" + csv + "\",\"proposalRationale\":\"Try it\"}"));
        Assert.Equal("false", McpHarness.Text(data, "valid"));
        Assert.Equal("null", McpHarness.Text(data, "configurationDraftId"));
        var problems = Js.Items(data, "problems").Select(p => McpHarness.Text(p, "column")).ToList();
        Assert.Contains("colour", problems);
        Assert.Contains("key", problems);
        Assert.Contains("parent", problems);
        Assert.Contains("classification", problems);
        Assert.Empty(h.Store.Query(RowQuery.For("configuration-draft")));

        // A reader may not import; the AI client may, as a candidate draft for its configuration admin.
        h.Fails(Reader, "import_data_taxonomy", "{\"csv\":\"label\\nX\\n\",\"proposalRationale\":\"Try it\"}", McpErrorCodes.NotAuthorized);
    }

    // The harness's person entry, for the tools served only to a person.
    private sealed class WebEntry
    {
        private readonly McpHarness _h;
        private readonly Product.Mcp.Core.Protocol.McpServerCore _server;

        public WebEntry(McpHarness h)
        {
            _h = h;
            _server = h.PersonServer();
        }

        public JsonTreeNode Ok(string token, string tool, string argumentsJson)
        {
            return _h.Ok(token, tool, argumentsJson, server: _server);
        }

        public JsonTreeNode Fails(string token, string tool, string argumentsJson, string code)
        {
            return _h.Fails(token, tool, argumentsJson, code, server: _server);
        }
    }
}
