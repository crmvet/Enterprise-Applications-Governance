using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Core.Telemetry;
using Xunit;

namespace Product.Mcp.Core.Tests;

// End-to-end behavior through the server core: AI never approves, accepts or activates; validateOnly and clientRequestId;
// the correlation ID; telemetry without tokens or typed text; a version change seen on the next request; resources.
public sealed class FlowTests
{
    [Fact]
    public void NoToolNameOrAnnotationCanApproveOrDelete()
    {
        string[] verbs = { "approve", "reject", "accept", "authorize", "activate", "assign", "grant", "revoke", "delete", "remove", "set" };
        foreach (ToolContract tool in ContractSet.Embedded.Tools)
        {
            Assert.DoesNotContain(verbs, v => tool.Name.StartsWith(v + "_", StringComparison.Ordinal));
            Assert.NotEqual(true, Js.Flag(tool.Annotations, "destructiveHint"));
        }
    }

    [Fact]
    public void AnAiSubmissionOnlyStartsCardsThatPeopleAnswer()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode proposed = h.Ok(McpHarness.TokenOwner, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Retire the travel planner\",\"context\":\"Replaced\",\"proposedOutcomeKey\":\"retire\",\"reason\":\"A duplicate\"}");
        string decision = McpHarness.Text(McpHarness.Data(proposed), "recordId");
        Assert.Equal("proposed", McpHarness.Text(McpHarness.Data(proposed), "status.meaning"));
        Assert.True(h.Store.Get("decision", decision)!.Flag("isAiCreated"));

        JsonTreeNode submitted = h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        JsonTreeNode data = McpHarness.Data(submitted);
        Assert.Equal("proposed", McpHarness.Text(data, "status.meaning"));
        Assert.NotEmpty(Js.Items(data, "approvalsRequested"));
        Assert.Equal("null", McpHarness.Text(data, "engineAccepted"));
        Assert.Equal("proposed", h.Store.Get("decision", decision)!.Text("statusMeaning"));
        Assert.All(h.Store.Query(RowQuery.For("approval-record").Where("decision", decision)), r => Assert.Equal("pending", r.Text("outcomeMeaning")));

        // The status read shows what is missing; nothing an AI client calls can answer the card.
        JsonTreeNode status = h.Ok(McpHarness.TokenOwner, "get_approval_status", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        Assert.Equal(false, Js.Flag(McpHarness.Data(status), "ready"));
        Assert.NotEmpty(Js.Items(McpHarness.Data(status), "missing"));

        // Only the approval runner writes an answer, and only a person's answer counts; then the engine accepts.
        foreach (StoredRow record in h.Store.Query(RowQuery.For("approval-record").Where("decision", decision)))
        {
            string approver = record.Text("requiredOwnerRole") != null ? McpHarness.Owner : McpHarness.Admin;
            RuntimeOutcome answer = h.Runtime.Write(CallerIdentity.Workload(McpHarness.Runner, clientApp: "approval runner"), WriteCommand.Update("approval-record", record.Id,
                "{\"outcome\":\"approval-approved\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-22T09:00:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"}"));
            Assert.True(answer.Succeeded, answer.ToString());
        }

        JsonTreeNode after = h.Ok(McpHarness.TokenReader, "get_decision_log", "{\"businessApplicationId\":\"" + McpHarness.Travel + "\"}");
        Assert.Equal("accepted", McpHarness.Text(Js.Items(McpHarness.Data(after), "items")[0], "status.meaning"));
        Assert.Equal("engine", h.Store.Get("decision", decision)!.Text("performedByActorKind"));
    }

    // A deployment record is final at insert (class F; document 22, section 8.3). A correction is one new
    // record that carries Corrects Record and Description in its insert; the record it corrects stays as it was, and no
    // caller can rewrite it afterwards.
    [Fact]
    public void ACorrectionIsANewRecordAndTheCorrectedOneNeverChanges()
    {
        McpHarness h = McpHarness.Create();
        string old = Scenarios.Deployment(h);
        string before = CanonicalJson.Write(h.Store.Get("deployment-record", old)!.Columns, CanonicalProfile.Request);

        JsonTreeNode corrected = h.Ok(McpHarness.TokenOwner, "record_deployment_correction", "{\"correctsDeploymentRecordId\":\"" + old + "\",\"idempotencyKey\":\"run-1-fix\",\"reason\":\"The version was wrong\",\"artifactVersion\":\"1.0.1\"}");
        string id = McpHarness.Text(McpHarness.Data(corrected), "deploymentRecordId");
        StoredRow row = h.Store.Get("deployment-record", id)!;
        Assert.Equal(old, row.Text("correctsRecord"));
        Assert.Equal("The version was wrong", row.Text("description"));
        Assert.Equal(old, McpHarness.Text(McpHarness.Data(corrected), "correctsRecordId"));
        Assert.Equal(before, CanonicalJson.Write(h.Store.Get("deployment-record", old)!.Columns, CanonicalProfile.Request));

        foreach (string target in new[] { old, id })
        {
            RuntimeOutcome rewrite = h.Runtime.Write(CallerIdentity.Person(McpHarness.Owner, new[] { McpHarness.OwnerGroup }), WriteCommand.Update("deployment-record", target, "{\"description\":\"rewritten\"}"));
            Assert.False(rewrite.Succeeded, rewrite.ToString());
            Assert.Contains(rewrite.Reasons, r => r.Code == "record.final");
        }
    }

    // WORKING DEFAULT (open design question 8): the engine accepts with no card only for the locked list, here a
    // lowest-tier intake. The AI client's own write still never shows accepted.
    [Fact]
    public void TheEngineAcceptsALowestTierIntakeAndTheAiNeverDoes()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h);
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":[{\"questionKey\":\"data-change\",\"answerKey\":\"reads-only\"},{\"questionKey\":\"sensitivity\",\"answerKey\":\"none\"},{\"questionKey\":\"reach\",\"answerKey\":\"small-team\"},{\"questionKey\":\"failure-impact\",\"answerKey\":\"little\"}]}");
        JsonTreeNode submitted = h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        JsonTreeNode data = McpHarness.Data(submitted);
        Assert.Equal("submitted", McpHarness.Text(data, "status.meaning"));
        Assert.Equal("lowest-tier", McpHarness.Text(data, "engineAccepted.condition"));
        string decision = McpHarness.Text(data, "decisionId");
        StoredRow row = h.Store.Get("decision", decision)!;
        Assert.Equal("accepted", row.Text("statusMeaning"));
        Assert.Equal("engine", row.Text("performedByActorKind"));
        ToolContractTests.AssertNeverApprovedOrAccepted("submit_record", data);

        // Tier answers are a suggestion: the application's tier did not move.
        Assert.Equal("tier-1", h.Store.Get("business-application", McpHarness.Expense)!.Text("tier"));
    }

    [Fact]
    public void AConfigurationChangeFromAnAiIsOnlyACandidateDraft()
    {
        McpHarness h = McpHarness.Create();
        int versions = h.Store.Rows("configuration-set-version").Count;
        JsonTreeNode proposed = h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", Scenarios.ProposalArguments());
        JsonTreeNode data = McpHarness.Data(proposed);
        Assert.Equal(true, Js.Flag(data, "valid"));
        Assert.Equal("draft", McpHarness.Text(data, "status.meaning"));
        Assert.Equal("updated", McpHarness.Text(Js.Items(data, "diffSummary")[0], "op"));
        string draft = McpHarness.Text(data, "configurationDraftId");

        JsonTreeNode submitted = h.Ok(McpHarness.TokenConfigAdmin, "submit_record", "{\"subjectKind\":\"configuration-draft\",\"subjectId\":\"" + draft + "\"}");
        Assert.Equal("submitted", McpHarness.Text(McpHarness.Data(submitted), "status.meaning"));
        Assert.Equal(versions, h.Store.Rows("configuration-set-version").Count);
        Assert.Equal(1, h.Store.ProbeInForceVersion()!.VersionNumber);
        Assert.Equal(100, h.Store.Rows("setting").Where(r => r.Key == "mcp-page-size-max").Select(r => r.Number("value")).Single());

        // Withdrawn: the draft is discarded, its decision withdrawn and its cards cancelled.
        JsonTreeNode withdrawn = h.Ok(McpHarness.TokenConfigAdmin, "withdraw_record", "{\"subjectKind\":\"configuration-draft\",\"subjectId\":\"" + draft + "\",\"reason\":\"Not now\"}");
        Assert.Equal("discarded", McpHarness.Text(McpHarness.Data(withdrawn), "status.meaning"));
        Assert.NotEmpty(Js.Items(McpHarness.Data(withdrawn), "cancelledApprovals"));
    }

    [Fact]
    public void ValidateOnlyWritesNothing()
    {
        McpHarness h = McpHarness.Create();
        string before = h.Store.Dump();
        JsonTreeNode envelope = h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Check only\",\"need\":\"A need\",\"validateOnly\":true}");
        Assert.Equal("null", McpHarness.Text(McpHarness.Data(envelope), "recordId"));
        Assert.Equal(false, Js.Flag(McpHarness.Data(envelope), "created"));
        Assert.Contains(Js.Items(envelope, "warnings"), w => Js.Text(w, "code") == "request.validate-only");
        h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("dry-run").TrimEnd('}') + ",\"validateOnly\":true}");
        h.Ok(McpHarness.TokenOwner, "save_application_draft", "{\"name\":\"Fixture dry run\",\"purpose\":\"x\",\"applicationKindKey\":\"power-platform-app\",\"validateOnly\":true,\"proposedOwners\":[{\"ownerRoleKey\":\"business-owner\",\"objectId\":\"" + McpHarness.Owner + "\"}]}");
        Assert.Equal(before, h.Store.Dump());
    }

    [Fact]
    public void AClientRequestIdReturnsTheFirstResultAndWritesNothingNew()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode first = h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Once\",\"need\":\"A need\",\"clientRequestId\":\"req-7\"}");
        int intakes = h.Store.Rows("intake-request").Count;
        JsonTreeNode again = h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Once\",\"need\":\"A need\",\"clientRequestId\":\"req-7\"}");
        Assert.Equal(McpHarness.Text(McpHarness.Data(first), "recordId"), McpHarness.Text(McpHarness.Data(again), "recordId"));
        Assert.Equal(true, Js.Flag(McpHarness.Data(first), "created"));
        Assert.Equal(false, Js.Flag(McpHarness.Data(again), "created"));
        Assert.Equal(intakes, h.Store.Rows("intake-request").Count);

        // The correlation ID of the first call is the one its Audit Entry holds.
        StoredRow audit = h.Store.Query(RowQuery.For("audit-entry").Where("requestKey", "req-7")).Single();
        Assert.Equal(Js.Text(first, "correlationId"), audit.Text("correlationId"));

        // The record step's own key: the first record comes back with created false.
        JsonTreeNode deployed = h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-9", McpHarness.Production));
        JsonTreeNode replayed = h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-9", McpHarness.Production));
        Assert.Equal(McpHarness.Text(McpHarness.Data(deployed), "deploymentRecordId"), McpHarness.Text(McpHarness.Data(replayed), "deploymentRecordId"));
        Assert.Equal(false, Js.Flag(McpHarness.Data(replayed), "created"));
        Assert.Equal("ai-tool", McpHarness.Text(McpHarness.Data(deployed), "route.meaning"));
        Assert.Equal(true, Js.Flag(Js.Member(McpHarness.Data(deployed), "flags"), "missingApproval"));
    }

    [Fact]
    public void TelemetryHoldsNoTokenAndNoTypedText()
    {
        McpHarness h = McpHarness.Create();
        const string Typed = "Secret-need-text-4711";
        h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"" + Typed + "\",\"need\":\"" + Typed + "\"}");
        h.Fails(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"" + Typed + "\"}", McpErrorCodes.Validation);
        h.Rpc(McpHarness.TokenExpired, "tools/list", "{}");
        Assert.True(h.Telemetry.Events.Count >= 3);
        foreach (TelemetryEvent e in h.Telemetry.Events)
        {
            string all = string.Join("|", e.ToProperties().Select(p => p.Key + "=" + p.Value));
            Assert.DoesNotContain(Typed, all, StringComparison.Ordinal);
            Assert.DoesNotContain("mock-", all, StringComparison.Ordinal);
            Assert.DoesNotContain(McpHarness.Owner, all, StringComparison.OrdinalIgnoreCase);
        }

        TelemetryEvent saved = h.Telemetry.Events.First(e => e.ToolName == "save_intake_draft");
        Assert.Equal("ok", saved.Outcome);
        Assert.Matches("^[0-9a-f]{64}$", saved.CallerHash!);
        Assert.Equal(1, saved.ConfigurationSetVersion);
        Assert.Contains(h.Telemetry.Events, e => e.Outcome == McpErrorCodes.Validation);
        Assert.Contains(h.Telemetry.Events, e => e.Outcome == McpErrorCodes.NotSignedIn);
    }

    // The telemetry salt is a host setting from the host's configuration.
    // Two hosts with different salts give different hashes for the same person, so their telemetry never joins; one host
    // gives the same hash every time, so calls by one person can still be counted; and the hash is the one HashCaller
    // gives for that salt, never the object ID.
    [Fact]
    public void TwoHostsWithDifferentSaltsGiveDifferentCallerHashes()
    {
        McpHarness h = McpHarness.Create();
        const string SaltA = "fixture-host-a-telemetry-salt-01";
        const string SaltB = "fixture-host-b-telemetry-salt-02";
        var telemetryA = new RecordingTelemetry();
        var telemetryB = new RecordingTelemetry();
        McpServerCore hostA = McpHarness.NewServer(h.Backend, telemetryA, h.Clock, SaltA);
        McpServerCore hostB = McpHarness.NewServer(h.Backend, telemetryB, h.Clock, SaltB);

        Whoami(hostA, McpHarness.TokenOwner);
        Whoami(hostA, McpHarness.TokenOwner);
        Whoami(hostA, McpHarness.TokenAdmin);
        Whoami(hostB, McpHarness.TokenOwner);

        string ownerAtA = telemetryA.Events[0].CallerHash!;
        string ownerAtB = telemetryB.Events[0].CallerHash!;
        Assert.Matches("^[0-9a-f]{64}$", ownerAtA);
        Assert.Matches("^[0-9a-f]{64}$", ownerAtB);
        Assert.NotEqual(ownerAtA, ownerAtB);
        Assert.Equal(ownerAtA, telemetryA.Events[1].CallerHash);
        Assert.NotEqual(ownerAtA, telemetryA.Events[2].CallerHash);
        Assert.Equal(TelemetryEvent.HashCaller(McpHarness.Owner, SaltA), ownerAtA);
        Assert.Equal(TelemetryEvent.HashCaller(McpHarness.Owner, SaltB), ownerAtB);
        foreach (TelemetryEvent e in telemetryA.Events.Concat(telemetryB.Events))
        {
            string all = string.Join("|", e.ToProperties().Select(p => p.Key + "=" + p.Value));
            Assert.DoesNotContain(McpHarness.Owner, all, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SaltA, all, StringComparison.Ordinal);
            Assert.DoesNotContain(SaltB, all, StringComparison.Ordinal);
        }
    }

    // A host that sets no salt (or an empty one) sends no caller field at all: the event's CallerHash is null and the
    // property list has no callerHash entry. Everything else is still recorded.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoSaltMeansNoCallerField(string? salt)
    {
        McpHarness h = McpHarness.Create();
        var telemetry = new RecordingTelemetry();
        McpServerCore host = McpHarness.NewServer(h.Backend, telemetry, h.Clock, salt);

        Whoami(host, McpHarness.TokenOwner);
        Whoami(host, McpHarness.TokenAdmin);

        Assert.Equal(2, telemetry.Events.Count);
        Assert.All(telemetry.Events, e =>
        {
            Assert.Null(e.CallerHash);
            Assert.DoesNotContain(e.ToProperties(), p => p.Key == "callerHash");
            Assert.Equal("ok", e.Outcome);
            Assert.Equal("whoami", e.ToolName);
        });
    }

    // The salt is never a constant in code: the option has no default, no production source assigns it a literal, a
    // short guessable salt (such as the old default "product") is refused when the server is built, and HashCaller
    // refuses an empty salt.
    [Fact]
    public void TheSaltIsNeverAConstantInCode()
    {
        Assert.Null(new McpServerOptions().TelemetrySalt);
        McpHarness h = McpHarness.Create();
        ArgumentException refused = Assert.Throws<ArgumentException>(() => McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock, "product"));
        Assert.Contains("telemetry salt", refused.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock, new string('s', McpServerOptions.MinimumTelemetrySaltLength - 1)));
        McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock, new string('s', McpServerOptions.MinimumTelemetrySaltLength));
        Assert.Throws<ArgumentException>(() => TelemetryEvent.HashCaller(McpHarness.Owner, string.Empty));

        string core = System.IO.Path.Combine(McpHarness.Root, "mcp", "Product.Mcp.Core");
        var literal = new System.Text.RegularExpressions.Regex("TelemetrySalt[^;\\n]*=\\s*\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        string[] offenders = System.IO.Directory.GetFiles(core, "*.cs", System.IO.SearchOption.AllDirectories)
            .Where(f => !f.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Any(part => part == "bin" || part == "obj"))
            .Where(f => literal.IsMatch(System.IO.File.ReadAllText(f)))
            .ToArray();
        Assert.Empty(offenders);
    }

    private static void Whoami(McpServerCore server, string token)
    {
        McpResponse response = server.Handle(McpRequest.Call(1, "tools/call", Js.Obj(("name", JsonTreeNode.FromString("whoami")), ("arguments", Js.Parse("{}"))), "Bearer " + token));
        Assert.Equal(200, response.HttpStatus);
    }

    // The method and the tool name come from the client, so text the client chose (a typed
    // sentence, a password, a token-like value) never reaches telemetry. An unknown tool name is "(unknown)", and a
    // method the server does not serve is "(other)", signed in or not.
    [Fact]
    public void TelemetryNeverHoldsAClientChosenMethodOrToolName()
    {
        McpHarness h = McpHarness.Create();
        const string Typed = "my vacation request for Jane Example, password hunter2";
        const string TokenLike = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJwcm9iZSJ9.c2lnbmF0dXJl";
        h.Telemetry.Events.Clear();

        h.Fails(McpHarness.TokenOwner, Typed + " Bearer " + TokenLike, "{}", McpErrorCodes.NotFound);
        h.Rpc(null, Typed + " " + TokenLike, "{}");
        h.Rpc(McpHarness.TokenOwner, Typed, "{}");
        h.Rpc(McpHarness.TokenExpired, Typed, "{}");
        h.Ok(McpHarness.TokenOwner, "whoami", "{}");

        Assert.Equal(5, h.Telemetry.Events.Count);
        foreach (TelemetryEvent e in h.Telemetry.Events)
        {
            string all = string.Join("|", e.ToProperties().Select(p => p.Key + "=" + p.Value));
            Assert.DoesNotContain("vacation", all, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", all, StringComparison.Ordinal);
            Assert.DoesNotContain(TokenLike, all, StringComparison.Ordinal);
        }

        Assert.Equal("(unknown)", h.Telemetry.Events[0].ToolName);
        Assert.Equal("tools/call", h.Telemetry.Events[0].Operation);
        Assert.Equal(McpErrorCodes.NotFound, h.Telemetry.Events[0].Outcome);
        Assert.Equal("(other)", h.Telemetry.Events[1].Operation);
        Assert.Equal(McpErrorCodes.NotSignedIn, h.Telemetry.Events[1].Outcome);
        Assert.Equal("(other)", h.Telemetry.Events[2].Operation);
        Assert.Equal("method-not-found", h.Telemetry.Events[2].Outcome);
        Assert.Equal("(other)", h.Telemetry.Events[3].Operation);
        Assert.Equal("whoami", h.Telemetry.Events[4].ToolName);
        Assert.Equal("tools/call", h.Telemetry.Events[4].Operation);
    }

    [Fact]
    public void UntrustedFieldsNameTheTypedText()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode found = h.Ok(McpHarness.TokenReader, "find_applications", "{}");
        Assert.Contains("items[].purpose", Js.Strings(found, "untrustedFields"));
        Assert.Contains("items[].name", Js.Strings(found, "untrustedFields"));
        JsonTreeNode me = h.Ok(McpHarness.TokenOwner, "whoami");
        Assert.Empty(Js.Strings(me, "untrustedFields"));
    }

    [Fact]
    public void AVersionChangeIsSeenOnTheNextRequest()
    {
        McpHarness h = McpHarness.Create();
        Assert.Contains("find_applications", Tools(h, McpHarness.TokenReader));

        // Another instance activates version 2, which disables find_applications (seeded straight into the store).
        h.Store.SeedVersion(McpHarness.Compile(2, PolicyTests.PolicyOperations((tool, values) => tool == "find_applications" ? Js.With(values, ("isEnabled", JsonTreeNode.False)) : values)));
        Assert.DoesNotContain("find_applications", Tools(h, McpHarness.TokenReader));
        JsonTreeNode me = h.Ok(McpHarness.TokenReader, "whoami");
        Assert.Equal(2, Js.Number(me, "configurationSetVersion"));
        Assert.Equal(2, Js.Number(Js.Member(McpHarness.Data(me), "configurationSet"), "version"));
        h.Fails(McpHarness.TokenReader, "find_applications", "{}", McpErrorCodes.NotFound);
    }

    private static List<string> Tools(McpHarness h, string token)
    {
        return Js.Items(h.Rpc(token, "tools/list", "{}").Result, "tools").Select(t => Js.Text(t, "name")!).ToList();
    }

    [Fact]
    public void PagingCarriesTheQueryNotTheServer()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode first = h.Ok(McpHarness.TokenReader, "find_applications", "{\"top\":1}");
        string token = Js.Text(McpHarness.Data(first), "skipToken")!;
        Assert.Equal(2, Js.Number(McpHarness.Data(first), "totalCount"));

        // Another server core (any instance) serves the next page.
        McpServerCore other = McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock);
        McpResponse second = other.Handle(McpRequest.Call(3, "tools/call", Js.Obj(("name", JsonTreeNode.FromString("find_applications")), ("arguments", Js.Parse("{\"top\":1,\"skipToken\":\"" + token + "\"}"))), "Bearer " + McpHarness.TokenReader));
        JsonTreeNode page = Js.Member(Js.Member(second.Result, "structuredContent"), "data")!;
        Assert.Single(Js.Items(page, "items"));
        Assert.Null(Js.Text(page, "skipToken"));
        Assert.NotEqual(McpHarness.Text(Js.Items(McpHarness.Data(first), "items")[0], "businessApplicationId"), McpHarness.Text(Js.Items(page, "items")[0], "businessApplicationId"));
        h.Fails(McpHarness.TokenReader, "find_applications", "{\"top\":1,\"text\":\"other query\",\"skipToken\":\"" + token + "\"}", McpErrorCodes.Validation);
    }

    // ---- Resources (document 24, section 6.12) ----

    [Fact]
    public void TheThreeR1ResourcesAnswerWithTheirContentSchemas()
    {
        McpHarness h = McpHarness.Create();
        Assert.Equal(new[] { "configuration://in-force" }, Js.Items(h.Rpc(McpHarness.TokenReader, "resources/list", "{}").Result, "resources").Select(r => Js.Text(r, "uri")).ToArray());
        Assert.Equal(new[] { "guideline://G{n}", "playbook://chapter/{n}" }, Js.Items(h.Rpc(McpHarness.TokenReader, "resources/templates/list", "{}").Result, "resourceTemplates").Select(r => Js.Text(r, "uriTemplate")).OrderBy(u => u, StringComparer.Ordinal).ToArray());

        JsonTreeNode inForce = Read(h, McpHarness.TokenReader, "configuration://in-force");
        Assert.Equal(1, Js.Number(inForce, "version"));
        Assert.Equal(h.Store.ProbeInForceVersion()!.ConfigurationSetKey, Js.Text(inForce, "configurationSetKey"));

        JsonTreeNode g2 = Read(h, McpHarness.TokenReader, "guideline://G2");
        Assert.Equal("G2", Js.Text(g2, "number"));
        Assert.Equal(true, Js.Flag(g2, "isActive"));
        Assert.Equal(false, Js.Flag(Read(h, McpHarness.TokenReader, "guideline://G10"), "isActive"));

        JsonTreeNode chapter = Read(h, McpHarness.TokenReader, "playbook://chapter/2");
        Assert.Equal(2, Js.Number(chapter, "chapter"));
        Assert.False(string.IsNullOrEmpty(Js.Text(chapter, "title")));
        Assert.Equal("null", McpHarness.Text(chapter, "url"));

        foreach (string missing in new[] { "guideline://G99", "playbook://chapter/99", "file:///etc/passwd", "configuration://draft" })
        {
            Assert.Equal(JsonRpcCodes.ResourceNotFound, Js.Number(h.Rpc(McpHarness.TokenReader, "resources/read", "{\"uri\":\"" + missing + "\"}").Error, "code"));
        }

        // A person with no role sees no resource; a hidden one looks like an unknown one.
        Assert.Empty(Js.Items(h.Rpc(McpHarness.TokenNoRole, "resources/list", "{}").Result, "resources"));
        Assert.Equal(JsonRpcCodes.ResourceNotFound, Js.Number(h.Rpc(McpHarness.TokenNoRole, "resources/read", "{\"uri\":\"guideline://G2\"}").Error, "code"));
    }

    private static JsonTreeNode Read(McpHarness h, string token, string uri)
    {
        McpResponse response = h.Rpc(token, "resources/read", "{\"uri\":\"" + uri + "\"}");
        Assert.True(response.Error == null, uri + ": " + Js.Write(response.Body!));
        JsonTreeNode content = Js.Items(response.Result, "contents").Single();
        Assert.Equal("application/json", Js.Text(content, "mimeType"));
        return Js.Parse(Js.Text(content, "text")!);
    }
}
