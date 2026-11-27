using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Wave 4b: the backend items of the verify round and the product review (document 24 records each choice, D-41).
public sealed class WaveFourBTests
{
    private const string Tier3App = "00000000-0000-4000-8000-000000000517";

    // ---- list_approvals mine counts only what the person may answer; get_my_queue leaves the rest out ----

    // Passing: the business owner is asked and may answer, so the request counts and is work in the queue.
    [Fact]
    public void AnAnswerableRequestIsCountedAndQueued()
    {
        McpHarness h = McpHarness.Create();
        string decision = Tier3GoLive(h);
        McpServerCore web = h.PersonServer();

        JsonTreeNode mine = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "list_approvals", "{\"view\":\"mine\",\"decisionId\":\"" + decision + "\"}", server: web));
        Assert.Equal("1", McpHarness.Text(mine, "answerableCount"));
        Assert.Equal("1", McpHarness.Text(mine, "totalCount"));
        JsonTreeNode queue = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "get_my_queue", "{\"kinds\":[\"approval\"]}", server: web));
        Assert.Contains(Js.Items(queue, "items"), i => McpHarness.Text(i, "subjectId") == decision);
    }

    // Refusing: the deputy proposed the tier 3 go-live, so the separation rule keeps them from answering. The request stays
    // in mine (the decision page reads why from it) but counts as none, the summary says why, and the queue leaves it out,
    // on the web app and for an AI client acting for the deputy alike.
    [Fact]
    public void ARequestTheSeparationRuleBlocksIsListedButNotCountedOrQueued()
    {
        McpHarness h = McpHarness.Create();
        string decision = Tier3GoLive(h);
        McpServerCore web = h.PersonServer();

        JsonTreeNode envelope = h.Ok(McpHarness.TokenDeputyWeb, "list_approvals", "{\"view\":\"mine\",\"decisionId\":\"" + decision + "\"}", server: web);
        JsonTreeNode mine = McpHarness.Data(envelope);
        Assert.Equal("0", McpHarness.Text(mine, "answerableCount"));
        Assert.Equal("1", McpHarness.Text(mine, "totalCount"));
        Assert.Equal("false", McpHarness.Text(Js.Items(mine, "items").Single(), "canAnswer"));
        Assert.Equal("No approval request waits for your answer. 1 more asks you, but a separation rule keeps you from answering.", McpHarness.Text(envelope, "summary"));

        JsonTreeNode queue = McpHarness.Data(h.Ok(McpHarness.TokenDeputyWeb, "get_my_queue", "{\"kinds\":[\"approval\"]}", server: web));
        Assert.DoesNotContain(Js.Items(queue, "items"), i => McpHarness.Text(i, "subjectId") == decision);

        JsonTreeNode ai = McpHarness.Data(h.Ok(McpHarness.TokenDeputy, "list_approvals", "{\"view\":\"mine\",\"decisionId\":\"" + decision + "\"}"));
        Assert.Equal("0", McpHarness.Text(ai, "answerableCount"));
        JsonTreeNode aiQueue = McpHarness.Data(h.Ok(McpHarness.TokenDeputy, "get_my_queue", "{\"kinds\":[\"approval\"]}"));
        Assert.DoesNotContain(Js.Items(aiQueue, "items"), i => McpHarness.Text(i, "subjectId") == decision);

        // The other views carry no answerable count.
        Assert.Equal("null", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenDeputyWeb, "list_approvals", "{\"view\":\"open\"}", server: web)), "answerableCount"));
    }

    // ---- version 1's "in force since" and review date do not move with the clock ----

    // A store seeded before the seed wrote Published On: the oldest audit entry stands in, so two reads a day apart give
    // the same activation and review dates.
    [Fact]
    public void ASeededVersionWithoutPublishedOnKeepsItsDatesAcrossDays()
    {
        // The day before the harness date (its tokens run out 8 hours after it), so the clock can move a whole day.
        McpHarness h = McpHarness.Create();
        h.Clock.UtcNow = McpHarness.AsOf.AddDays(-1);
        Tier3GoLive(h);
        JsonTreeNode first = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_configuration_health"));
        h.Clock.Advance(TimeSpan.FromDays(1));
        JsonTreeNode second = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_configuration_health"));

        Assert.Equal(McpHarness.Text(first, "active.activatedOn"), McpHarness.Text(second, "active.activatedOn"));
        Assert.Equal(McpHarness.Text(first, "reviewDueOn"), McpHarness.Text(second, "reviewDueOn"));
        Assert.Equal("2026-11-21T09:00:00Z", McpHarness.Text(second, "active.activatedOn"));
        Assert.Equal("2027-05-20", McpHarness.Text(second, "reviewDueOn"));
        Assert.Equal("2026-11-21T09:00:00Z", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "whoami")), "configurationSet.publishedOn"));
    }

    // A version row with Published On (what the seed writes now) gives that moment on every page.
    [Fact]
    public void ASeededVersionWithPublishedOnGivesItsSeedMoment()
    {
        McpHarness h = McpHarness.Create();
        StoredRow version = h.Store.Query(RowQuery.For("configuration-set-version")).Single();
        h.Store.Tamper("configuration-set-version", version.Id, Js.With(version.Columns, ("publishedOn", JsonTreeNode.FromString("2026-11-01T08:00:00Z"))));
        h.Clock.Advance(TimeSpan.FromHours(6));

        JsonTreeNode health = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_configuration_health"));
        Assert.Equal("2026-11-01T08:00:00Z", McpHarness.Text(health, "active.activatedOn"));
        Assert.Equal("2027-04-30", McpHarness.Text(health, "reviewDueOn"));
        Assert.Equal("2026-11-01T08:00:00Z", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_configuration")), "configurationSet.publishedOn"));
    }

    // ---- list_approvals and get_ai_activity take a portfolio ----

    private const string OtherPortfolio = "00000000-0000-4000-8000-000000000502";
    private const string UnknownPortfolio = "00000000-0000-4000-8000-0000000005ff";

    // Passing: the retirement of the travel planner is in the fixture portfolio and not in another one. Refusing: a
    // portfolio that does not exist.
    [Fact]
    public void TheApprovalsListKeepsThePortfolioScopeAndRefusesAnUnknownPortfolio()
    {
        McpHarness h = McpHarness.Create();
        string record = Scenarios.PendingRetirementApproval(h);
        h.Store.Seed("portfolio", "{\"name\":\"Fixture other portfolio\",\"level\":\"department\",\"isActive\":true}", id: OtherPortfolio);

        JsonTreeNode inside = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_approvals", "{\"view\":\"open\",\"portfolioId\":\"" + McpHarness.Portfolio + "\"}"));
        Assert.Contains(Js.Items(inside, "items"), i => McpHarness.Text(i, "approvalRecordId") == record);
        JsonTreeNode outside = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_approvals", "{\"view\":\"open\",\"portfolioId\":\"" + OtherPortfolio + "\"}"));
        Assert.Equal("0", McpHarness.Text(outside, "totalCount"));
        h.Fails(McpHarness.TokenAdmin, "list_approvals", "{\"view\":\"open\",\"portfolioId\":\"" + UnknownPortfolio + "\"}", McpErrorCodes.NotFound);
    }

    [Fact]
    public void TheAiActivityKeepsThePortfolioScopeAndRefusesAnUnknownPortfolio()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.SubmittedRetirement(h);
        h.Store.Seed("portfolio", "{\"name\":\"Fixture other portfolio\",\"level\":\"department\",\"isActive\":true}", id: OtherPortfolio);

        JsonTreeNode inside = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"portfolioId\":\"" + McpHarness.Portfolio + "\"}"));
        Assert.NotEqual("0", McpHarness.Text(inside, "counts.everything"));
        JsonTreeNode outside = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"portfolioId\":\"" + OtherPortfolio + "\"}"));
        Assert.Equal("0", McpHarness.Text(outside, "counts.everything"));
        Assert.Equal("0", McpHarness.Text(outside, "totalCount"));
        h.Fails(McpHarness.TokenAdmin, "get_ai_activity", "{\"portfolioId\":\"" + UnknownPortfolio + "\"}", McpErrorCodes.NotFound);
    }

    // ---- the AI activity counts add up, and the Admin page counts the same rows ----

    [Fact]
    public void EverythingCountsEachActivityOnceAndAdminCountsTheSameDraftsAndRefusals()
    {
        McpHarness h = McpHarness.Create();
        string record = Scenarios.PendingRetirementApproval(h);
        Scenarios.Deployment(h);

        // A call the product refuses an AI client: it may not ask a request again.
        h.Fails(McpHarness.TokenOwner, "renew_approval_request", "{\"approvalRecordId\":\"" + record + "\"}", McpErrorCodes.AiCannotDoThis);

        JsonTreeNode all = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"view\":\"all\",\"top\":100}"));
        Assert.Equal(McpHarness.Text(all, "totalCount"), McpHarness.Text(all, "counts.everything"));

        JsonTreeNode window = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"from\":\"2026-11-15\"}"));
        JsonTreeNode admin = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_admin_status", "{\"days\":7}"));
        JsonTreeNode[] clients = Js.Items(window, "clients").ToArray();
        Assert.NotEmpty(clients);
        foreach (JsonTreeNode client in clients)
        {
            JsonTreeNode row = Js.Items(admin, "clients").Single(c => McpHarness.Text(c, "label") == McpHarness.Text(client, "clientApp"));
            Assert.Equal(McpHarness.Text(client, "drafts"), McpHarness.Text(row, "drafts"));
            Assert.Equal(McpHarness.Text(client, "refused"), McpHarness.Text(row, "refused"));
        }
    }

    // ---- the gap check run and the Notices tile count from one definition ----

    [Fact]
    public void ARunCountsItsOpenUnregisteredAndVanishedClaimsApart()
    {
        McpHarness h = McpHarness.Create();
        h.Ok(McpHarness.TokenAdmin, "compare_inventory_list", "{\"estateKey\":\"power-platform-inventory\",\"isFinalPage\":true,\"sourceNote\":\"An export from the admin's own tool\",\"items\":["
            + "{\"externalId\":\"fixture-flow-1\",\"componentKindKey\":\"cloud-flow\",\"labelAsSeen\":\"Fixture flow\"}]}");

        JsonTreeNode run = Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_gap_check_runs", "{}")), "items").Single();
        Assert.Equal("1", McpHarness.Text(run, "newClaims"));
        Assert.Equal("1", McpHarness.Text(run, "openClaimsLastSeen"));
        Assert.Equal(McpHarness.Text(run, "vanishedClaims"), McpHarness.Text(run, "openVanishedLastSeen"));
        Assert.NotEqual("0", McpHarness.Text(run, "openVanishedLastSeen"));
    }

    [Fact]
    public void AnExpiredApprovalNoticeCountsOnceInTheRunAndInTheTile()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.ExpiringRetirementApproval(h, -1);
        McpServerCore web = h.PersonServer();
        h.Ok(McpHarness.TokenAdminWeb, "run_due_state_refresh", "{}", server: web);

        JsonTreeNode notices = Js.Member(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_admin_status", "{\"days\":7}")), "notices")!;
        Assert.Contains(Js.Items(notices, "items"), n => McpHarness.Text(n, "notice") == "expired");
        long tile = long.Parse(McpHarness.Text(notices, "reminders")) + long.Parse(McpHarness.Text(notices, "escalations")) + long.Parse(McpHarness.Text(notices, "overdue")) + long.Parse(McpHarness.Text(notices, "flagNotices"));
        Assert.Equal(Js.Items(notices, "items").Count, (int)tile);
        Assert.Equal(tile.ToString(System.Globalization.CultureInfo.InvariantCulture), McpHarness.Text(Js.Items(notices, "recentRuns")[0], "notices"));
    }

    // ---- a review requested with no date falls due in 14 days ----

    [Fact]
    public void AReviewRequestedWithoutADateIsDueInFourteenDays()
    {
        McpHarness h = McpHarness.Create();
        string review = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "request_architecture_review", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"reason\":\"A new integration with the ledger\"}")), "reviewId");
        Assert.Equal("2026-12-06", h.Store.Get("review", review)!.Text("dueOn")?.Substring(0, 10));
    }

    // ---- the separation refusal names the rule by its label ----

    [Fact]
    public void TheSeparationRefusalNamesTheRuleByItsLabelWithNoBacktick()
    {
        McpHarness h = McpHarness.Create();
        string decision = Tier3GoLive(h);
        string record = h.Store.Query(RowQuery.For("approval-record").Where("decision", decision)).Single().Id;
        JsonTreeNode refused = h.Fails(McpHarness.TokenDeputyWeb, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\",\"reason\":\"Fixture reason\"}", McpErrorCodes.RuleViolation, server: h.PersonServer());
        Assert.StartsWith("The separation rule No self-approval at tier 3 keeps you from answering", McpHarness.Text(refused, "error.message"), StringComparison.Ordinal);
        Assert.DoesNotContain("`", McpHarness.Text(refused, "error.message"));
    }

    // ---- only the newest record of an ask is asked again ----

    [Fact]
    public void OnlyTheNewestRecordOfAnAskIsAskedAgain()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        string first = Scenarios.ExpiringRetirementApproval(h, -1);
        string decision = h.Store.Get("approval-record", first)!.Text("decision")!;
        string second = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + first + "\"}", server: web)), "request.approvalRecordId");

        // The second request lapses too. Asking the first again is refused and writes nothing: ask the second.
        StoredRow row = h.Store.Get("approval-record", second)!;
        h.Store.Tamper("approval-record", second, Js.With(row.Columns, ("expiresOn", JsonTreeNode.FromString("2026-11-22T08:00:00Z"))));
        string before = h.Store.DumpWithout("audit-entry");
        JsonTreeNode refused = h.Fails(McpHarness.TokenOwnerWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + first + "\"}", McpErrorCodes.RuleViolation, server: web);
        Assert.Contains("was asked again as " + second + "; ask that one again.", McpHarness.Text(refused, "error.message"));
        Assert.Equal(before, h.Store.DumpWithout("audit-entry"));

        // Asking the newest lapsed record again opens one new request.
        JsonTreeNode renewed = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "renew_approval_request", "{\"approvalRecordId\":\"" + second + "\"}", server: web));
        Assert.Equal("true", McpHarness.Text(renewed, "renewed"));
        Assert.Single(h.Store.Query(RowQuery.For("approval-record").Where("decision", decision).Where("outcomeMeaning", "pending")));
    }

    // ---- a retired application owes no document; a move into a looser domain warns ----

    [Fact]
    public void ARetiredApplicationListsNoDocumentToFix()
    {
        McpHarness h = McpHarness.Create();
        StoredRow link = h.Store.Get("document-link", McpHarness.WikiLink)!;
        h.Store.Tamper("document-link", link.Id, Js.With(link.Columns, ("statusMeaning", JsonTreeNode.FromString("current")), ("lastCheckedOn", JsonTreeNode.FromString("2024-01-01"))));
        string app = link.Text("businessApplication")!;
        JsonTreeNode live = Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_documentation_status", "{\"businessApplicationId\":\"" + app + "\"}")), "applications").Single();
        Assert.Contains(McpHarness.WikiLink, Js.Strings(live, "stale"));

        StoredRow application = h.Store.Get("business-application", app)!;
        h.Store.Tamper("business-application", app, Js.With(application.Columns, ("lifecycleStage", JsonTreeNode.FromString("retire"))));
        JsonTreeNode retired = Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_documentation_status", "{\"businessApplicationId\":\"" + app + "\"}")), "applications").Single();
        Assert.Empty(Js.Strings(retired, "stale"));
        Assert.Empty(Js.Strings(retired, "missing"));
        Assert.Empty(Js.Items(retired, "required"));
        Assert.Contains(Js.Items(retired, "documents"), d => McpHarness.Text(d, "documentLinkId") == McpHarness.WikiLink);
    }

    // Wave 4b (found by the demo's S-23.15): a person who owns a registered application marks that it holds no business
    // data, and the answer keeps the status registered within the tool's output contract (it said draft only, so the
    // harness's output check and the server answered an internal error). An AI client is still refused.
    [Fact]
    public void MarkingARegisteredApplicationAsHoldingNoBusinessDataAnswersWithinItsContract()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        StoredRow app = h.Store.Get("business-application", McpHarness.Expense)!;
        Assert.Equal("registered", app.Text("statusMeaning"));
        string input = "{\"businessApplicationId\":\"" + app.Id + "\",\"name\":\"" + app.Text("name") + "\",\"purpose\":\"" + app.Text("purpose") + "\",\"applicationKindKey\":\"" + app.Text("applicationKind") + "\",\"holdsNoBusinessData\":true}";
        JsonTreeNode marked = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "save_application_draft", input, server: web));
        Assert.Equal("registered", McpHarness.Text(marked, "status.meaning"));
        Assert.True(h.Store.Get("business-application", app.Id)!.Flag("holdsNoBusinessData"));
        h.Fails(McpHarness.TokenAdmin, "save_application_draft", input, McpErrorCodes.AiCannotDoThis);
    }

    // Wave 4b (found by the demo's S-22.19): withdrawing a draft application withdraws its ownership decision and cancels
    // the requests it opened, so nobody stays asked to confirm the owners of an application that was withdrawn.
    [Fact]
    public void WithdrawingADraftApplicationWithdrawsItsOwnershipDecision()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        JsonTreeNode draft = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_application_draft", "{\"name\":\"Fixture key register\",\"purpose\":\"Which keys each crew member holds.\",\"applicationKindKey\":\"power-platform-app\",\"proposedOwners\":[{\"ownerRoleKey\":\"business-owner\",\"objectId\":\"" + McpHarness.Owner + "\"}]}", server: web));
        string app = McpHarness.Text(draft, "recordId");
        StoredRow ownership = h.Store.Get("decision", McpHarness.Text(draft, "ownershipDecisionId"))!;
        Assert.Equal(app, ownership.Text("subjectApplication"));
        h.Ok(McpHarness.TokenOwnerWeb, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + ownership.Id + "\"}", server: web);
        Assert.Contains(h.Store.Query(RowQuery.For("approval-record").Where("decision", ownership.Id)), r => r.Text("outcomeMeaning") == "pending");

        JsonTreeNode withdrawn = h.Ok(McpHarness.TokenOwnerWeb, "withdraw_record", "{\"subjectKind\":\"application-draft\",\"subjectId\":\"" + app + "\",\"reason\":\"Not needed after all.\"}", server: web);
        Assert.StartsWith("Withdrawn. ", McpHarness.Text(withdrawn, "summary"), StringComparison.Ordinal);
        Assert.DoesNotContain("No approval was waiting", McpHarness.Text(withdrawn, "summary"), StringComparison.Ordinal);
        Assert.Equal(ownership.Id, McpHarness.Text(McpHarness.Data(withdrawn), "withdrawnDecisionId"));
        Assert.Equal("withdrawn", h.Store.Get("decision", ownership.Id)!.Text("statusMeaning"));
        Assert.DoesNotContain(h.Store.Query(RowQuery.For("approval-record").Where("decision", ownership.Id)), r => r.Text("outcomeMeaning") == "pending");
    }

    // Wave 4b (found by the demo's S-22.14): a mapping whose new domain is mapped at the same location with the same origin
    // is refused in the preview and the write, with the field toDomainKey and nothing written, instead of meeting the
    // store's key and answering an internal error. A move of the uses alone still goes ahead.
    [Fact]
    public void AMappingThatWouldCollideInTheNewDomainIsRefusedInThePreviewAndTheWrite()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        h.Store.Seed("data-mapping", "{\"dataDomain\":\"travel-bookings\",\"dataLocation\":\"" + DataWorld.FinanceProd + "\",\"containers\":[\"fx_trip\"],\"dataOrigin\":\"system-of-record\",\"status\":\"data-mapping-registered\",\"statusMeaning\":\"registered\",\"startOn\":\"2026-01-01\"}");
        string move = "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"travel-bookings\",\"include\":[\"mappings\",\"uses\"],\"reason\":\"Fixture restructuring\"";
        string before = h.Store.Dump();
        foreach (string input in new[] { move + ",\"validateOnly\":true}", move + "}" })
        {
            JsonTreeNode refused = h.Fails(McpHarness.TokenDataLeadWeb, "move_data_references", input, McpErrorCodes.Validation, server: web);
            Assert.Equal("toDomainKey", McpHarness.Text(refused, "error.field"));
            Assert.Contains("is mapped there already, as the system of record", McpHarness.Text(refused, "error.message"), StringComparison.Ordinal);
        }

        Assert.Equal(before, h.Store.Dump());
        Assert.Equal("expenses", h.Store.Get("data-mapping", DataWorld.ExpensesInProd)!.Text("dataDomain"));

        // The uses alone move, as before.
        h.Ok(McpHarness.TokenDataLeadWeb, "move_data_references", "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"travel-bookings\",\"include\":[\"uses\"],\"reason\":\"Fixture restructuring\",\"validateOnly\":true}", server: web);
    }

    [Fact]
    public void AMoveIntoALooserDomainWarnsInThePreviewAndTheAnswer()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        string move = "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"travel-bookings\",\"include\":[\"uses\"],\"reason\":\"Fixture restructuring\"";
        foreach (JsonTreeNode envelope in new[]
        {
            h.Ok(McpHarness.TokenDataLeadWeb, "move_data_references", move + ",\"validateOnly\":true}", server: web),
            h.Ok(McpHarness.TokenDataLeadWeb, "move_data_references", move + "}", server: web),
        })
        {
            JsonTreeNode warning = Js.Items(envelope, "warnings").Single(w => Js.Text(w, "code") == "data-move.looser-target");
            Assert.Contains("is less strict than", Js.Text(warning, "message"));
            Assert.Contains("against Confidential", Js.Text(warning, "message"));
        }

        // A move into a stricter domain says nothing of the kind.
        JsonTreeNode back = h.Ok(McpHarness.TokenDataLeadWeb, "move_data_references", "{\"fromDomainKey\":\"travel-bookings\",\"toDomainKey\":\"expenses\",\"include\":[\"uses\"],\"reason\":\"Fixture restructuring\",\"validateOnly\":true}", server: web);
        Assert.DoesNotContain(Js.Items(back, "warnings"), w => Js.Text(w, "code") == "data-move.looser-target");
    }

    // ---- one application's data findings come from that application's facts, and are the same ----

    [Fact]
    public void OneApplicationsDataFindingsAreTheEstateFindingsAboutIt()
    {
        McpHarness h = McpHarness.Create(withData: true);
        foreach (string app in new[] { McpHarness.Expense, McpHarness.Travel })
        {
            string estate = Js.Write(Arr(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_data_gaps", "{\"top\":100}")), "items").Where(f => McpHarness.Text(f, "businessApplicationId") == app)));
            string scoped = Js.Write(Arr(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_data_gaps", "{\"applicationId\":\"" + app + "\",\"top\":100}")), "items")));
            Assert.Equal(estate, scoped);
        }

        Assert.NotEqual("[]", Js.Write(Arr(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_data_gaps", "{\"applicationId\":\"" + McpHarness.Expense + "\",\"top\":100}")), "items"))));
    }

    private static JsonTreeNode Arr(System.Collections.Generic.IEnumerable<JsonTreeNode> items)
    {
        return JsonTreeNode.CreateArray(items);
    }

    // ---- the gap check signal counts every claim opened and settled in the window ----

    [Fact]
    public void TheGapCheckSignalCountsTheClaimsOpenedAndSettledInTheWindow()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        // The whole list in one final page, the registered expense app with it, so the run finishes and nothing vanished.
        JsonTreeNode run = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "compare_inventory_list", "{\"estateKey\":\"power-platform-inventory\",\"isFinalPage\":true,\"sourceNote\":\"An export from the admin's own tool\",\"items\":["
            + "{\"externalId\":\"00000000-0000-0000-0000-000000000101\",\"componentKindKey\":\"canvas-app\",\"labelAsSeen\":\"Expense app\"},"
            + "{\"externalId\":\"fixture-flow-1\",\"componentKindKey\":\"cloud-flow\",\"labelAsSeen\":\"Fixture flow one\",\"suggestedClaimantObjectId\":\"" + McpHarness.Owner + "\"},"
            + "{\"externalId\":\"fixture-flow-2\",\"componentKindKey\":\"cloud-flow\",\"labelAsSeen\":\"Fixture flow two\"},"
            + "{\"externalId\":\"fixture-flow-3\",\"componentKindKey\":\"cloud-flow\",\"labelAsSeen\":\"Fixture flow three\"},"
            + "{\"externalId\":\"fixture-flow-4\",\"componentKindKey\":\"cloud-flow\",\"labelAsSeen\":\"Fixture flow four\"}]}"));
        string[] claims = Js.Items(run, "newClaims").Select(c => McpHarness.Text(c, "claimId")).ToArray();
        Assert.Equal(4, claims.Length);
        h.Ok(McpHarness.TokenOwnerWeb, "claim_resource", "{\"claimId\":\"" + claims[0] + "\",\"businessApplicationId\":\"" + McpHarness.Expense + "\"}", server: web);
        h.Ok(McpHarness.TokenAdminWeb, "dismiss_claim", "{\"claimId\":\"" + claims[1] + "\",\"reason\":\"A test copy nobody uses\"}", server: web);

        JsonTreeNode signal = Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_measures", "{}")), "signals").Single(g => McpHarness.Text(g, "key") == "gap-check-stale");
        Assert.Contains("4 claims opened and 2 settled in the window.", McpHarness.Text(signal, "note"));
    }

    // A tier 3 go-live the deputy business owner proposed and submitted: one request, to the business owner role.
    internal static string Tier3GoLive(McpHarness h)
    {
        SeedTier3(h);
        string decision = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenDeputy, "save_decision_proposal", "{\"decisionTypeKey\":\"go-live\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + Tier3App
            + "\",\"statement\":\"Go live\",\"context\":\"Fixture context\",\"proposedOutcomeKey\":\"go\",\"reason\":\"Fixture reason\"}")), "recordId");
        h.Ok(McpHarness.TokenDeputy, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        return decision;
    }

    internal static void SeedTier3(McpHarness h)
    {
        h.Store.Seed("business-application", "{\"name\":\"Fixture claims triage\",\"recordNumber\":\"APP-0007\",\"purpose\":\"Triage insurance claims\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + McpHarness.Portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-3\",\"lifecycleStage\":\"build\","
            + "\"stageSince\":\"2026-09-01T00:00:00Z\",\"configurationSetVersion\":1}", id: Tier3App);
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Tier3App + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-09-01\"}",
            id: "00000000-0000-4000-8000-000000000571");
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Tier3App + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Deputy + "\",\"isDeputy\":true,\"deputyFor\":\"00000000-0000-4000-8000-000000000571\",\"startOn\":\"2026-09-01\"}",
            id: "00000000-0000-4000-8000-000000000572");
    }
}
