using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The wave 2 leftovers in the tools (document 24, B24-17; document 25, B25-18): a platform admin runs the due-state
// refresh now; the first AI client and person of a draft stay after a person edits it; the intake reads carry the
// portfolio and filter by it; one owner gap count across Overview, Owners and Portfolios; the requirements read lists only
// the decision types about an application; one AI activity row per deployment; and record links that follow the
// request's own base address.
public sealed class LeftoverToolTests
{
    // ---- 86. run_due_state_refresh ----

    [Fact]
    public void APlatformAdminRunsTheRefreshNowAndASecondRunChangesNothing()
    {
        McpHarness h = McpHarness.Create();
        string done = h.Store.Seed("review", "{\"businessApplication\":\"" + McpHarness.Expense + "\",\"reviewType\":\"owner-check\",\"status\":\"review-done\",\"statusMeaning\":\"done\","
            + "\"dueOn\":\"2025-11-20\",\"doneOn\":\"2025-11-22\",\"reviewer\":\"" + McpHarness.Owner + "\",\"nextDueOn\":\"2026-11-22\",\"recordNumber\":\"REV-0901\"}", id: "00000000-0000-4000-8000-000000000a91");
        McpServerCore web = h.PersonServer();

        JsonTreeNode dry = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "run_due_state_refresh", "{\"validateOnly\":true}", server: web));
        Assert.Equal("true", McpHarness.Text(dry, "dryRun"));
        Assert.Equal("1", McpHarness.Text(dry, "counts.reviewsOpened"));
        Assert.Single(h.Store.Rows("review"));

        JsonTreeNode envelope = h.Ok(McpHarness.TokenAdminWeb, "run_due_state_refresh", "{}", server: web);
        JsonTreeNode data = McpHarness.Data(envelope);
        Assert.Equal("person", McpHarness.Text(data, "trigger"));
        JsonTreeNode opened = Assert.Single(Js.Items(data, "reviewsOpened"));
        Assert.Equal(done, McpHarness.Text(opened, "fromReviewId"));
        Assert.Equal("2026-11-22", McpHarness.Text(opened, "dueOn"));
        Assert.StartsWith("REV-", McpHarness.Text(opened, "recordNumber"));
        Assert.Contains("1 review opened", Js.Text(envelope, "summary"));
        StoredRow review = h.Store.Get("review", McpHarness.Text(opened, "reviewId"))!;
        Assert.Equal("engine", review.Text("performedByActorKind"));
        Assert.Equal(McpHarness.Admin, review.Text("onBehalfOf"));

        // The second run sends the new review's reminder for today; the third changes and sends nothing.
        h.Ok(McpHarness.TokenAdminWeb, "run_due_state_refresh", "{}", server: web);
        JsonTreeNode third = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "run_due_state_refresh", "{}", server: web));
        Assert.Equal("0", McpHarness.Text(third, "counts.reviewsOpened"));
        Assert.Empty(Js.Items(third, "notices"));
        Assert.Equal(2, h.Store.Rows("review").Count);

        // Only a platform admin on the web app runs it: an AI client is never served it, and the owner holds no role for it.
        h.Fails(McpHarness.TokenAdmin, "run_due_state_refresh", "{}", McpErrorCodes.AiCannotDoThis);
        h.Fails(McpHarness.TokenOwnerWeb, "run_due_state_refresh", "{}", McpErrorCodes.NotAuthorized, server: web);
    }

    // ---- The drafted by pair (document 22, B22-15) ----

    [Fact]
    public void ThePersonEditsAnAiDraftAndItStillSaysWhichAiClientDraftedItForWhom()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h, "Fixture crew overtime tracker", linked: false);
        string aiClient = h.Store.Get("intake-request", intake)!.Text("clientApp")!;
        h.Ok(McpHarness.TokenOwnerWeb, "save_intake_draft", "{\"intakeRequestId\":\"" + intake + "\",\"title\":\"Fixture crew overtime tracker\",\"need\":\"Track crew overtime by week\",\"proposedKindKey\":\"power-platform-app\"}", server: h.PersonServer());

        StoredRow row = h.Store.Get("intake-request", intake)!;
        Assert.Equal("person", row.Text("performedByActorKind"));
        Assert.Equal(aiClient, row.Text("draftedByClientApp"));
        Assert.Equal(McpHarness.Owner, row.Text("draftedForPrincipal"));

        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}"));
        Assert.Equal("person", McpHarness.Text(read, "intake.performedBy.actorKind"));
        Assert.Equal(aiClient, McpHarness.Text(read, "intake.performedBy.draftedBy.clientAppLabel"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(read, "intake.performedBy.draftedBy.forPrincipal.objectId"));

        JsonTreeNode activity = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_ai_activity", "{\"view\":\"drafts\"}"));
        JsonTreeNode item = Assert.Single(Js.Items(activity, "items"), i => McpHarness.Text(i, "record.recordId") == intake);
        Assert.Equal(aiClient, McpHarness.Text(item, "clientApp"));

        // A person's own row has no AI mark.
        string own = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_intake_draft", "{\"title\":\"Fixture person draft\",\"need\":\"Typed by a person\",\"proposedKindKey\":\"power-platform-app\"}", server: h.PersonServer())), "recordId");
        Assert.Equal("null", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + own + "\"}")), "intake.performedBy.draftedBy"));
    }

    // ---- The intake's portfolio ----

    [Fact]
    public void TheIntakeReadsCarryThePortfolioAndTheListFiltersByIt()
    {
        McpHarness h = McpHarness.Create();
        string placed = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Fixture placed request\",\"need\":\"A placed need\",\"proposedKindKey\":\"power-platform-app\",\"portfolioId\":\"" + McpHarness.Portfolio + "\"}")), "recordId");
        string linked = Scenarios.Intake(h, "Fixture linked request", linked: true);
        string loose = Scenarios.Intake(h, "Fixture loose request", linked: false);

        JsonTreeNode one = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + placed + "\"}"));
        Assert.Equal(McpHarness.Portfolio, McpHarness.Text(one, "intake.portfolio.portfolioId"));
        Assert.Equal("Fixture finance", McpHarness.Text(one, "intake.portfolio.name"));
        Assert.Equal("false", McpHarness.Text(one, "intake.portfolio.fromApplication"));
        Assert.Equal("true", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + linked + "\"}")), "intake.portfolio.fromApplication"));

        JsonTreeNode list = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "list_intake_requests", "{\"portfolioId\":\"" + McpHarness.Portfolio + "\"}"));
        var ids = Js.Items(list, "items").Select(i => McpHarness.Text(i, "intakeRequestId")).ToList();
        Assert.Contains(placed, ids);
        Assert.Contains(linked, ids);
        Assert.DoesNotContain(loose, ids);
        Assert.All(Js.Items(list, "items"), i => Assert.Equal(McpHarness.Portfolio, McpHarness.Text(i, "portfolio.portfolioId")));
        Assert.Equal("null", McpHarness.Text(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "list_intake_requests", "{}")), "items").Single(i => McpHarness.Text(i, "intakeRequestId") == loose), "portfolio"));
    }

    // ---- One owner gap count ----

    [Fact]
    public void OverviewOwnersAndPortfoliosCountTheSameOwnerGaps()
    {
        McpHarness h = McpHarness.Create();
        // A holder whose account the owner check found disabled counts once, beside the roles with no holder or no deputy.
        StoredRow owner = h.Store.Query(RowQuery.For("principal-reference").Where("entraObjectId", McpHarness.Owner)).Single();
        h.Store.Tamper("principal-reference", owner.Id, Js.With(owner.Columns, ("directoryState", JsonTreeNode.FromString("disabled"))));

        string scope = "\"portfolioId\":\"" + McpHarness.Portfolio + "\"";
        JsonTreeNode summary = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_portfolio_summary", "{" + scope + ",\"includeOwnerGaps\":true}"));
        JsonTreeNode owners = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_owner_assignments", "{" + scope + ",\"state\":\"current\"}"));
        JsonTreeNode portfolios = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_portfolios", "{}"));
        JsonTreeNode portfolio = Js.Items(portfolios, "items").Single(p => McpHarness.Text(p, "portfolioId") == McpHarness.Portfolio);

        long gaps = Js.Number(summary, "ownerGaps") ?? -1;
        Assert.True(gaps > 0);
        Assert.Equal(gaps, Js.Number(owners, "ownerGaps"));
        Assert.Equal(gaps, Js.Number(Js.Member(portfolio, "counts"), "ownerGaps"));
        Assert.Equal(gaps, Js.Items(owners, "coverage").Sum(c => Js.Number(c, "gaps") ?? 0));
        Assert.Equal(Js.Number(summary, "applicationsWithOwnerGap"), Js.Number(Js.Member(portfolio, "counts"), "applicationsWithOwnerGap"));
        Assert.True(Js.Items(summary, "ownerGapList").Count < gaps);
    }

    // ---- What a tier needs ----

    [Fact]
    public void WhatATierNeedsListsOnlyTheDecisionTypesAboutAnApplication()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode needs = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_requirements", "{\"scope\":{\"tierKey\":\"tier-2\",\"applicationKindKey\":\"power-platform-app\"}}"));
        var types = Js.Items(needs, "approvals").Select(a => McpHarness.Text(a, "decisionTypeKey")).Distinct().ToList();
        Assert.NotEmpty(types);
        Assert.DoesNotContain("configuration-change", types);
        Assert.DoesNotContain("gate-policy-change", types);
        Assert.Contains("go-live", types);

        // A data-use decision is opened about each use, so its approvals are those of its domain's data owner, never the
        // application's (wave 3 verification), and the owner subject says where the owner role is held.
        var dataUse = Js.Items(needs, "approvals").Where(a => McpHarness.Text(a, "decisionTypeKey") == "data-use").ToList();
        Assert.NotEmpty(dataUse);
        Assert.All(dataUse.Where(a => McpHarness.Text(a, "approverOwnerRoleKey") != null), a => Assert.Equal("data-domain", McpHarness.Text(a, "ownerSubject")));
    }

    // ---- One AI activity row per deployment ----

    [Fact]
    public void ADeploymentAnAiClientRecordedWithAnAiOnlyCodeApprovalIsOneActivityRow()
    {
        McpHarness h = McpHarness.Create();
        string arguments = Scenarios.DeploymentArguments("run-ai-9", McpHarness.Production).TrimEnd('}') + ",\"codeApprovalKind\":\"ai-only\"}";
        string record = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment", arguments)), "deploymentRecordId");

        JsonTreeNode all = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"view\":\"all\"}"));
        JsonTreeNode item = Assert.Single(Js.Items(all, "items"), i => McpHarness.Text(i, "record.recordId") == record);
        Assert.Equal("code-approval", McpHarness.Text(item, "kind"));
        Assert.Equal("recorded", McpHarness.Text(item, "state"));
        Assert.Equal(h.Store.Get("deployment-record", record)!.Text("clientApp"), McpHarness.Text(item, "clientApp"));
        Assert.Equal("1", McpHarness.Text(all, "counts.codeApprovals"));
        Assert.Contains(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"view\":\"drafts\"}")), "items"), i => McpHarness.Text(i, "record.recordId") == record);
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"view\":\"code-approvals\"}")), "items"), i => McpHarness.Text(i, "record.recordId") == record);
    }

    // ---- Record links on a local run ----

    [Fact]
    public void RecordLinksFollowTheRequestsOwnBaseWhenTheHostSetsIt()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore local = McpHarness.NewServer(h.Backend, h.Telemetry, h.Clock, configure: o => o.RecordUrlBase = "http://127.0.0.1:5081/app/records");
        JsonTreeNode envelope = h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + Scenarios.Intake(h) + "\"}", server: local);
        Assert.StartsWith("http://127.0.0.1:5081/app/records/intake-request/", McpHarness.Text(envelope, "links.recordUrl"));
        Assert.StartsWith("http://127.0.0.1:5081/app/records/", McpHarness.Text(McpHarness.Data(envelope), "intake.recordUrl"));

        // The harness's own server keeps the configured base.
        JsonTreeNode configured = h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + Scenarios.Intake(h, "Fixture second") + "\"}");
        Assert.StartsWith("https://product.example.org/records/", McpHarness.Text(configured, "links.recordUrl"));
    }
}
