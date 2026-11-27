using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Data taxonomy slice 3, the data tools (D-37, settled by D-41; docs/architecture/data-taxonomy.md). Each tool's ok
// path and one refusal; the AI draft states (an AI client drafts and never confirms or approves; a person confirms on the
// web app, D-36); the never rule refused for an AI client on the MCP endpoint and for a person on the web app's entry
// (D-41, question 7, as the product owner changed it); and the changed tools (docs/architecture/data-taxonomy.md, MCP tools and REST). Every case runs in the data world.
public sealed class DataToolTests
{
    private const string Owner = McpHarness.TokenOwner;
    private const string Admin = McpHarness.TokenAdmin;
    private const string Reader = McpHarness.TokenReader;

    private static McpHarness World()
    {
        return McpHarness.Create(withData: true);
    }

    private static JsonTreeNode Item(JsonTreeNode data, string key, string field = "key")
    {
        return Js.Items(data, "items").Single(i => McpHarness.Text(i, field) == key);
    }

    // ---- 76. find_data_domains ----

    [Fact]
    public void TheTreeShowsEachOwnerOwnOrInheritedAndTheOwnerGaps()
    {
        McpHarness h = World();
        JsonTreeNode all = McpHarness.Data(h.Ok(Reader, "find_data_domains", "{}"));
        Assert.Equal(6, Js.Number(all, "totalCount"));
        Assert.Equal(new[] { "finance", "expenses", "payroll", "bank-details", "operations", "travel-bookings" }, Js.Items(all, "items").Select(i => McpHarness.Text(i, "key")));

        JsonTreeNode payroll = Item(all, "payroll");
        Assert.Equal(McpHarness.Reader, McpHarness.Text(payroll, "owner.principal.objectId"));
        Assert.Equal("null", McpHarness.Text(payroll, "owner.inheritedFrom"));
        Assert.Equal("never", McpHarness.Text(payroll, "rules.ai.value"));
        Assert.Equal("payroll", McpHarness.Text(payroll, "rules.ai.setOn.key"));
        Assert.Contains("data.no-deputy", Js.Strings(payroll, "gaps"));

        JsonTreeNode bank = Item(all, "bank-details");
        Assert.Equal("Finance / Payroll / Bank details", McpHarness.Text(bank, "path"));
        Assert.Equal("payroll", McpHarness.Text(bank, "owner.inheritedFrom.key"));
        Assert.Equal("restricted", McpHarness.Text(bank, "classification.key"));

        JsonTreeNode expenses = Item(all, "expenses");
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(expenses, "owner.principal.objectId"));
        Assert.Equal("finance", McpHarness.Text(expenses, "owner.inheritedFrom.key"));
        Assert.Equal(McpHarness.Architect, McpHarness.Text(expenses, "deputy.objectId"));
        Assert.Equal("2", McpHarness.Text(expenses, "counts.mappings"));
        Assert.Equal("1", McpHarness.Text(expenses, "counts.uses"));

        Assert.Contains("data.no-owner", Js.Strings(Item(all, "operations"), "gaps"));

        JsonTreeNode gaps = McpHarness.Data(h.Ok(Reader, "find_data_domains", "{\"gapsOnly\":true}"));
        Assert.Contains(Js.Items(gaps, "items"), i => McpHarness.Text(i, "key") == "operations");
        Assert.DoesNotContain(Js.Items(gaps, "items"), i => McpHarness.Text(i, "key") == "finance");

        JsonTreeNode readersOwn = McpHarness.Data(h.Ok(Reader, "find_data_domains", "{\"ownerObjectId\":\"" + McpHarness.Reader + "\"}"));
        Assert.Equal(new[] { "payroll", "bank-details" }, Js.Items(readersOwn, "items").Select(i => McpHarness.Text(i, "key")));

        // Refusal: a person with no product role reads no data domain.
        h.Fails(McpHarness.TokenNoRole, "find_data_domains", "{}", McpErrorCodes.NotAuthorized);
    }

    // ---- 77. get_data_domain ----

    [Fact]
    public void TheDomainRecordSaysWhereEachRuleIsSetAndWhoAnswers()
    {
        McpHarness h = World();
        JsonTreeNode data = McpHarness.Data(h.Ok(Reader, "get_data_domain", "{\"domainKey\":\"bank-details\"}"));
        Assert.Equal("Finance / Payroll / Bank details", McpHarness.Text(data, "domain.path"));
        Assert.Equal("never", McpHarness.Text(data, "domain.rules.ai.value"));
        Assert.Equal("payroll", McpHarness.Text(data, "domain.rules.ai.setOn.key"));
        Assert.Equal("payroll", McpHarness.Text(data, "domain.classification.setOn.key"));
        Assert.EndsWith("/records/data-domain/bank-details", McpHarness.Text(data, "domain.recordUrl"));
        JsonTreeNode dataOwner = Js.Items(data, "owners").Single(o => McpHarness.Text(o, "ownerRole.key") == "data-owner");
        Assert.Equal(McpHarness.Reader, McpHarness.Text(dataOwner, "holder.principal.objectId"));
        Assert.Equal("payroll", McpHarness.Text(dataOwner, "inheritedFrom.key"));
        Assert.Single(Js.Items(data, "mappings"));
        Assert.Equal(DataWorld.BankDetailsInLibrary, McpHarness.Text(Js.Items(data, "mappings")[0], "dataMappingId"));

        JsonTreeNode onlyOwners = McpHarness.Data(h.Ok(Reader, "get_data_domain", "{\"domainKey\":\"expenses\",\"sections\":[\"owners\"]}"));
        Assert.Equal("null", McpHarness.Text(onlyOwners, "mappings"));
        Assert.Equal("null", McpHarness.Text(onlyOwners, "uses"));

        h.Fails(Reader, "get_data_domain", "{\"domainKey\":\"no-such-domain\"}", McpErrorCodes.NotFound);
    }

    // ---- 78. find_data_locations ----

    [Fact]
    public void LocationsListWithRestrictedIdsAndAStoreTakesItsInstancesOwnerAndStage()
    {
        McpHarness h = World();
        JsonTreeNode forReader = McpHarness.Data(h.Ok(Reader, "find_data_locations", "{}"));
        Assert.Equal(4, Js.Number(forReader, "totalCount"));
        Assert.All(Js.Items(forReader, "items"), l => Assert.Equal("null", McpHarness.Text(l, "externalId")));

        JsonTreeNode forAdmin = McpHarness.Data(h.Ok(Admin, "find_data_locations", "{}"));
        Assert.Equal("fixture-env-prod", McpHarness.Text(Item(forAdmin, DataWorld.FinanceProd, "dataLocationId"), "externalId"));
        Assert.Equal("true", McpHarness.Text(Item(forAdmin, DataWorld.FinanceTest, "dataLocationId"), "flags.copyOutsideProduction"));

        JsonTreeNode library = Js.Items(McpHarness.Data(h.Ok(Admin, "find_data_locations", "{\"locationId\":\"" + DataWorld.PayrollLibrary + "\"}")), "items").Single();
        Assert.Equal(DataWorld.PayrollHost, McpHarness.Text(library, "parent.dataLocationId"));
        Assert.Equal("production", McpHarness.Text(library, "stage.key"));
        Assert.Equal("true", McpHarness.Text(library, "technicalOwner.fromParent"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(library, "technicalOwner.principal.objectId"));
        Assert.Equal(2, Js.Items(library, "domains").Count);

        h.Fails(Admin, "find_data_locations", "{\"locationId\":\"00000000-0000-4000-8000-000000000999\"}", McpErrorCodes.NotFound);
    }

    // ---- 79. resolve_data ----

    [Fact]
    public void ResolveDataNamesTheDomainItsOwnerAndItsRules()
    {
        McpHarness h = World();
        JsonTreeNode data = McpHarness.Data(h.Ok(Owner, "resolve_data", "{\"externalId\":\"PAYLIB\",\"container\":\"paymast\"}"));
        JsonTreeNode match = Js.Items(data, "matches").Single();
        Assert.Equal("payroll", McpHarness.Text(match, "domain.key"));
        Assert.Equal("true", McpHarness.Text(match, "containerMatched"));
        Assert.Equal(McpHarness.Reader, McpHarness.Text(match, "owner.principal.objectId"));
        Assert.Equal("never", McpHarness.Text(match, "rules.ai.value"));
        Assert.Equal("false", McpHarness.Text(data, "unmapped"));

        JsonTreeNode unknownTable = McpHarness.Data(h.Ok(Owner, "resolve_data", "{\"locationId\":\"" + DataWorld.PayrollLibrary + "\",\"container\":\"NOPE\"}"));
        Assert.Equal("true", McpHarness.Text(unknownTable, "unmapped"));

        JsonTreeNode unknownStore = McpHarness.Data(h.Ok(Owner, "resolve_data", "{\"externalId\":\"no-such-host\"}"));
        Assert.Equal("null", McpHarness.Text(unknownStore, "location"));

        h.Fails(Owner, "resolve_data", "{\"container\":\"PAYMAST\"}", McpErrorCodes.Validation);
    }

    // ---- 80. check_data_use ----

    [Fact]
    public void CheckDataUseAnswersBeforeAnythingIsSaved()
    {
        McpHarness h = World();
        JsonTreeNode never = McpHarness.Data(h.Ok(Owner, "check_data_use", "{\"applicationId\":\"" + McpHarness.Expense + "\",\"domainKey\":\"bank-details\",\"access\":\"read\",\"reachesAi\":true}"));
        Assert.Equal("breaks-rule", McpHarness.Text(never, "state"));
        Assert.Equal("true", McpHarness.Text(never, "refusedWhenSaved"));
        Assert.Equal("ai", McpHarness.Text(never, "governingRule.name"));
        Assert.Equal("payroll", McpHarness.Text(never, "governingRule.setOn.key"));

        JsonTreeNode needed = McpHarness.Data(h.Ok(Owner, "check_data_use", "{\"applicationId\":\"" + McpHarness.Expense + "\",\"domainKey\":\"expenses\",\"access\":\"write\",\"reachesAi\":false}"));
        Assert.Equal("needed", McpHarness.Text(needed, "state"));
        Assert.Equal("1", McpHarness.Text(needed, "approvalsNeeded"));
        JsonTreeNode approver = Js.Items(needed, "approvers").Single(a => McpHarness.Text(a, "isConsultation") == "false");
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(approver, "principal.objectId"));
        Assert.Equal("finance", McpHarness.Text(approver, "inheritedFrom.key"));
        Assert.Equal("restricted", McpHarness.Text(needed, "impliedAnswer.answerKey"));

        JsonTreeNode byTable = McpHarness.Data(h.Ok(Owner, "check_data_use", "{\"applicationId\":\"" + McpHarness.Expense + "\",\"locationId\":\"" + DataWorld.PayrollLibrary + "\",\"container\":\"PAYBANK\",\"access\":\"read\",\"reachesAi\":false}"));
        Assert.Equal("bank-details", McpHarness.Text(byTable, "domain.key"));

        string intake = Scenarios.Intake(h, linked: false);
        JsonTreeNode early = McpHarness.Data(h.Ok(Owner, "check_data_use", "{\"intakeRequestId\":\"" + intake + "\",\"domainKey\":\"travel-bookings\",\"access\":\"read\",\"reachesAi\":true}"));
        Assert.Equal("no-one-to-ask", McpHarness.Text(early, "state"));

        h.Fails(Owner, "check_data_use", "{\"applicationId\":\"" + McpHarness.Expense + "\",\"domainKey\":\"no-such-domain\",\"access\":\"read\",\"reachesAi\":false}", McpErrorCodes.Validation);
    }

    // ---- 81. get_data_gaps ----

    [Fact]
    public void DataGapsNameTheToolThatFixesEach()
    {
        McpHarness h = World();
        JsonTreeNode data = McpHarness.Data(h.Ok(Admin, "get_data_gaps", "{}"));
        Assert.True(Js.Number(Js.Member(data, "counts"), "DC-01") >= 1);
        JsonTreeNode noOwner = Js.Items(data, "items").First(f => McpHarness.Text(f, "check") == "DC-01");
        Assert.Equal("propose_owner_change", McpHarness.Text(noOwner, "fixTool"));
        JsonTreeNode missing = Js.Items(data, "items").Single(f => McpHarness.Text(f, "check") == "DC-05");
        Assert.Equal(DataWorld.ExpenseReadsClaims, McpHarness.Text(missing, "subjectId"));
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(missing, "principal.objectId"));
        Assert.Equal("save_decision_proposal", McpHarness.Text(missing, "fixTool"));
        Assert.Equal("save_data_mapping", McpHarness.Text(Js.Items(data, "items").Single(f => McpHarness.Text(f, "check") == "DC-08"), "fixTool"));

        JsonTreeNode operations = McpHarness.Data(h.Ok(Admin, "get_data_gaps", "{\"domainKey\":\"operations\"}"));
        Assert.All(Js.Items(operations, "items"), f => Assert.Contains(McpHarness.Text(f, "domain.key"), new[] { "operations", "travel-bookings" }));

        h.Fails(Admin, "get_data_gaps", "{\"domainKey\":\"no-such-domain\"}", McpErrorCodes.NotFound);
    }

    // ---- 82. save_data_use: the AI draft states ----

    // An AI client drafts the use and proposes its decision; it never confirms the use, never sends the decision and never
    // answers the card. The person confirms the use on the web app, which sends the card to the data owner by name; the
    // data owner approves on the decision page, and the use is allowed.
    [Fact]
    public void AnAiClientDraftsADataUseAndAPersonConfirmsItSoTheCardGoesToTheDataOwner()
    {
        McpHarness h = World();
        string args = DataWorld.Use("expenses");
        JsonTreeNode drafted = McpHarness.Data(h.Ok(Owner, "save_data_use", args));
        Assert.Equal("draft", McpHarness.Text(drafted, "status.meaning"));
        Assert.Equal("true", McpHarness.Text(drafted, "isAiCreated"));
        Assert.Equal("proposed", McpHarness.Text(drafted, "use.state"));
        Assert.Equal("false", McpHarness.Text(drafted, "decisionSubmitted"));
        Assert.StartsWith("DU-", McpHarness.Text(drafted, "recordNumber"));
        string useId = McpHarness.Text(drafted, "dataUseId");
        string decisionId = McpHarness.Text(drafted, "decisionId");
        Assert.Empty(h.Store.Query(RowQuery.For("approval-record").Where("decision", decisionId)));

        // An AI client never confirms it, and never sends its decision while it is a draft.
        string confirm = DataWorld.Use("expenses", extra: "\"dataUseId\":\"" + useId + "\",\"confirm\":true");
        h.Fails(Owner, "save_data_use", confirm, McpErrorCodes.AiCannotDoThis);
        h.Fails(Owner, "submit_record", "{\"subjectKind\":\"data-use\",\"subjectId\":\"" + useId + "\"}", McpErrorCodes.RuleViolation);
        h.Fails(Owner, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decisionId + "\"}", McpErrorCodes.RuleViolation);

        // The person confirms it in the web app: the card goes to the effective data owner, inherited from Finance.
        McpServerCore web = h.PersonServer();
        JsonTreeNode confirmed = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_data_use", confirm, server: web));
        Assert.Equal("registered", McpHarness.Text(confirmed, "status.meaning"));
        Assert.Equal("true", McpHarness.Text(confirmed, "decisionSubmitted"));
        JsonTreeNode asked = Js.Items(confirmed, "approvalsRequested").Single(a => McpHarness.Text(a, "isConsultation") == "false");
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(asked, "askedPrincipal.objectId"));

        // The data owner's list and the decision page carry the data facts.
        JsonTreeNode mine = McpHarness.Data(h.Ok(McpHarness.TokenDeputyWeb, "list_approvals", "{\"view\":\"mine\"}", server: web));
        JsonTreeNode card = Js.Items(mine, "items").Single(i => McpHarness.Text(i, "decision.decisionId") == decisionId);
        Assert.Equal("expenses", McpHarness.Text(card, "data.domain.key"));
        Assert.Equal("decision", McpHarness.Text(card, "data.rule"));
        JsonTreeNode page = McpHarness.Data(h.Ok(McpHarness.TokenDeputyWeb, "get_decision", "{\"decisionId\":\"" + decisionId + "\"}", server: web));
        Assert.Equal("Finance / Expense claims", McpHarness.Text(page, "data.domain.path"));
        Assert.Equal("confidential", McpHarness.Text(page, "data.governingRule.fromClassification.key"));
        Assert.Equal("false", McpHarness.Text(page, "data.reachesAi"));
        Assert.Equal("1", McpHarness.Text(page, "data.approvalsNeeded"));
        Assert.Equal("0", McpHarness.Text(page, "data.approvalsGiven"));
        Assert.Equal("1", McpHarness.Text(page, "data.configurationSetVersion"));
        JsonTreeNode pageAsked = Js.Items(Js.Member(page, "data"), "asked").Single(a => McpHarness.Text(a, "isConsultation") == "false");
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(pageAsked, "principal.objectId"));
        Assert.Equal("data-use", McpHarness.Text(page, "subject.subjectKind"));

        // AI never approves; the data owner approves on the decision page, and the use is allowed.
        string record = McpHarness.Text(asked, "approvalRecordId");
        h.Fails(Owner, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\",\"reason\":\"Fine\"}", McpErrorCodes.AiCannotDoThis);
        h.Ok(McpHarness.TokenDeputyWeb, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\",\"reason\":\"Approvers need the claims.\"}", server: web);
        JsonTreeNode domain = McpHarness.Data(h.Ok(Reader, "get_data_domain", "{\"domainKey\":\"expenses\",\"sections\":[\"uses\"]}"));
        Assert.Equal("allowed", McpHarness.Text(Js.Items(domain, "uses").Single(u => McpHarness.Text(u, "dataUseId") == useId), "state"));
    }

    // ---- 82. save_data_use: the never rule (G-25) ----

    // D-41, question 7, as the product owner changed it: a use whose governing rule is never is refused when it is saved, for an AI
    // client and a person alike, and the refusal names the rule, the domain and where the rule is set. A copy whose copy
    // rule is never is refused the same way.
    [Fact]
    public void ANeverRuleIsRefusedForAnAiClientOnTheMcpEndpointAndForAPersonOnTheWebApp()
    {
        McpHarness h = World();
        string bankDetailsWithAi = DataWorld.Use("bank-details", reachesAi: true);
        JsonTreeNode ai = h.Fails(Owner, "save_data_use", bankDetailsWithAi, McpErrorCodes.RuleViolation);
        string message = McpHarness.Text(Js.Member(ai, "error")!, "message");
        Assert.Contains("Data rule never", message, StringComparison.Ordinal);
        Assert.Contains("set on Finance / Payroll", message, StringComparison.Ordinal);
        Assert.Contains("Bank details", message, StringComparison.Ordinal);

        JsonTreeNode person = h.Fails(McpHarness.TokenOwnerWeb, "save_data_use", bankDetailsWithAi, McpErrorCodes.RuleViolation, server: h.PersonServer());
        Assert.Contains("Data rule never", McpHarness.Text(Js.Member(person, "error")!, "message"), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Store.Query(RowQuery.For("data-use")), u => u.Text("dataDomain") == "bank-details");

        // A production copy of payroll data outside production: the restricted copy rule is never, for the platform admin too.
        h.Fails(McpHarness.TokenAdminWeb, "save_data_mapping", "{\"items\":[{\"domainKey\":\"payroll\",\"locationId\":\"" + DataWorld.FinanceTest + "\",\"origin\":\"production-copy\"}]}", McpErrorCodes.RuleViolation, server: h.PersonServer());

        // Refusal by role: a reader declares no data use.
        h.Fails(Reader, "save_data_use", DataWorld.Use("expenses"), McpErrorCodes.NotAuthorized);
    }

    // ---- 83. save_data_location and 84. save_data_mapping ----

    [Fact]
    public void LocationsAndMappingsAreDraftsForAnAiClientAndSecretsAreRefused()
    {
        McpHarness h = World();
        McpServerCore web = h.PersonServer();
        string location = "{\"items\":[{\"platformKey\":\"sql-server\",\"targetStageKey\":\"production\",\"externalId\":\"TIMESQL01\",\"labelAsSeen\":\"Fixture timesheet server\"}]}";
        JsonTreeNode drafted = McpHarness.Data(h.Ok(Admin, "save_data_location", location));
        Assert.Equal("draft", McpHarness.Text(drafted, "status.meaning"));
        Assert.Equal("true", McpHarness.Text(drafted, "isAiCreated"));
        string id = McpHarness.Text(Js.Items(drafted, "items")[0], "dataLocationId");

        // Registering it again matches on its platform and external ID; an AI client never confirms it; a person does.
        JsonTreeNode again = McpHarness.Data(h.Ok(Admin, "save_data_location", location));
        Assert.Equal("true", McpHarness.Text(Js.Items(again, "items")[0], "matchedExisting"));
        Assert.Equal(id, McpHarness.Text(Js.Items(again, "items")[0], "dataLocationId"));
        string confirm = "{\"items\":[{\"dataLocationId\":\"" + id + "\",\"confirm\":true,\"seenNow\":true}]}";
        h.Fails(Admin, "save_data_location", confirm, McpErrorCodes.AiCannotDoThis);
        Assert.Equal("registered", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "save_data_location", confirm, server: web)), "status.meaning"));

        // G-22: a connection string marker is refused, for a person too.
        h.Fails(McpHarness.TokenAdminWeb, "save_data_location", "{\"items\":[{\"platformKey\":\"sql-server\",\"targetStageKey\":\"test\",\"externalId\":\"Server=x;Password=secret\",\"labelAsSeen\":\"x\"}]}", McpErrorCodes.RuleViolation, server: web);

        // The technical owner of the payroll host maps data in its library (a store takes its instance's owner) as a
        // draft; the same person may not map data in a location they do not keep.
        JsonTreeNode mapped = McpHarness.Data(h.Ok(Owner, "save_data_mapping", "{\"items\":[{\"domainKey\":\"payroll\",\"locationId\":\"" + DataWorld.PayrollLibrary + "\",\"containers\":[\"PAYHIST\"],\"origin\":\"replica\"}]}"));
        Assert.Equal("draft", McpHarness.Text(Js.Items(mapped, "items")[0], "status.meaning"));
        h.Fails(Owner, "save_data_mapping", "{\"items\":[{\"domainKey\":\"travel-bookings\",\"locationId\":\"" + DataWorld.FinanceProd + "\",\"origin\":\"system-of-record\"}]}", McpErrorCodes.NotAuthorized);

        // The masked copy in test needs a copy decision: the platform admin's save in the web app opens and sends it; an AI
        // client never submits a copy decision.
        JsonTreeNode copy = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "save_data_mapping", "{\"items\":[{\"domainKey\":\"expenses\",\"locationId\":\"" + DataWorld.FinanceTest + "\",\"origin\":\"masked-copy\"}]}", server: web));
        JsonTreeNode item = Js.Items(copy, "items")[0];
        Assert.Equal("true", McpHarness.Text(item, "matchedExisting"));
        Assert.Equal("proposed", McpHarness.Text(item, "copyState"));
        Assert.Equal("true", McpHarness.Text(item, "decisionSubmitted"));
        h.Fails(Admin, "submit_record", "{\"subjectKind\":\"data-mapping\",\"subjectId\":\"" + DataWorld.MaskedExpensesInTest + "\"}", McpErrorCodes.AiCannotDoThis);
        h.Fails(Admin, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + McpHarness.Text(item, "decisionId") + "\"}", McpErrorCodes.AiCannotDoThis);
    }

    // ---- 85. move_data_references ----

    [Fact]
    public void MovingReferencesKeepsEachRowAndItsDecisionsAndAnAiClientOnlyPreviews()
    {
        McpHarness h = World();
        string move = "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"travel-bookings\",\"include\":[\"uses\"],\"reason\":\"Fixture restructuring\"";
        h.Fails(Admin, "move_data_references", move + "}", McpErrorCodes.AiCannotDoThis);
        JsonTreeNode preview = McpHarness.Data(h.Ok(Admin, "move_data_references", move + ",\"validateOnly\":true}"));
        Assert.Contains(DataWorld.ExpenseReadsClaims, Js.Strings(preview, "movedUseIds"));
        Assert.Equal("expenses", h.Store.Get("data-use", DataWorld.ExpenseReadsClaims)!.Text("dataDomain"));

        JsonTreeNode moved = McpHarness.Data(h.Ok(McpHarness.TokenDataLeadWeb, "move_data_references", move + "}", server: h.PersonServer()));
        JsonTreeNode change = Js.Items(moved, "changedUses").Single();
        Assert.Equal("needed", McpHarness.Text(change, "stateBefore"));
        Assert.Equal("not-needed", McpHarness.Text(change, "stateAfter"));
        Assert.Equal("travel-bookings", h.Store.Get("data-use", DataWorld.ExpenseReadsClaims)!.Text("dataDomain"));
        Assert.Equal("person", McpHarness.Text(moved, "performedBy.actorKind"));

        h.Fails(Owner, "move_data_references", move + ",\"validateOnly\":true}", McpErrorCodes.NotAuthorized);
    }

    // Wave 3 verification (major, twice): when the rows move to a domain with another data owner, the new owner is asked,
    // whoever moves them. The data governance lead holds no data-use proposal right and the platform admin does; for both,
    // a decision nobody answered is asked again of the new owner and keeps its identity, a decision someone answered (here
    // the steward's advice) is withdrawn and a new one asks the new owner, a decided use is ended and declared again and
    // its new decision asks the new owner, and a confirmed use that needed a decision and had none gets one. The engine
    // opens each new decision for the person who moved the rows. The AI client's preview says what the person's says.
    [Theory]
    [InlineData(McpHarness.TokenAdmin, McpHarness.TokenAdminWeb, McpHarness.Admin)]
    [InlineData(McpHarness.TokenDataLead, McpHarness.TokenDataLeadWeb, McpHarness.DataLead)]
    public void MovingUsesToADomainWithAnotherDataOwnerAsksTheNewOwner(string moverAi, string mover, string moverId)
    {
        McpHarness h = World();
        McpServerCore web = h.PersonServer();

        // A data steward on Expense claims, so each of its decisions also consults the architect.
        h.Store.Seed("owner-assignment", "{\"subjectKind\":\"data-domain\",\"subjectDataDomain\":\"expenses\",\"ownerRole\":\"data-steward\",\"principal\":\"" + McpHarness.Architect + "\",\"isDeputy\":false,\"startOn\":\"2026-01-01\"}");

        // Unanswered: a use whose decision waits for the data owner of Expense claims.
        JsonTreeNode waiting = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_data_use", DataWorld.Use("expenses", access: "write"), server: web));
        string waitingDecision = McpHarness.Text(waiting, "decisionId");
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(Js.Items(waiting, "approvalsRequested").Single(a => McpHarness.Text(a, "isConsultation") == "false"), "askedPrincipal.objectId"));

        // Answered: the steward gave advice on a second use's decision, which stays proposed.
        JsonTreeNode advised = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_data_use", DataWorld.Use("expenses", extra: "\"locationId\":\"" + DataWorld.PayrollLibrary + "\""), server: web));
        string advisedUse = McpHarness.Text(advised, "dataUseId");
        string advisedDecision = McpHarness.Text(advised, "decisionId");
        string advice = McpHarness.Text(Js.Items(advised, "approvalsRequested").Single(a => McpHarness.Text(a, "isConsultation") == "true"), "approvalRecordId");
        h.Ok(McpHarness.TokenArchitectWeb, "answer_approval", "{\"approvalRecordId\":\"" + advice + "\",\"answer\":\"approve\",\"reason\":\"The library holds the claims.\"}", server: web);
        Assert.Equal("proposed", h.Store.Get("decision", advisedDecision)!.Text("statusMeaning"));

        // Decided: the data owner allowed a third use.
        JsonTreeNode decided = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_data_use", DataWorld.Use("expenses", extra: "\"locationId\":\"" + DataWorld.FinanceTest + "\""), server: web));
        string decidedUse = McpHarness.Text(decided, "dataUseId");
        string card = McpHarness.Text(Js.Items(decided, "approvalsRequested").Single(a => McpHarness.Text(a, "isConsultation") == "false"), "approvalRecordId");
        h.Ok(McpHarness.TokenDeputyWeb, "answer_approval", "{\"approvalRecordId\":\"" + card + "\",\"answer\":\"approve\",\"reason\":\"Approvers need the claims.\"}", server: web);
        Assert.Equal("accepted", h.Store.Get("decision", McpHarness.Text(decided, "decisionId"))!.Text("statusMeaning"));

        // The AI client's preview runs the person's own preview: the same counts and words, its client named, nothing written.
        string move = "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"payroll\",\"include\":[\"uses\"],\"reason\":\"Claims move under payroll\"";
        string before = h.Store.Dump();
        JsonTreeNode aiPreview = h.Ok(moverAi, "move_data_references", move + ",\"validateOnly\":true}");
        JsonTreeNode personPreview = h.Ok(mover, "move_data_references", move + ",\"validateOnly\":true}", server: web);
        Assert.Equal(before, h.Store.Dump());
        Assert.Equal(McpHarness.Text(McpHarness.Data(personPreview), "ownerChange"), McpHarness.Text(McpHarness.Data(aiPreview), "ownerChange"));
        Assert.Equal(McpHarness.Text(McpHarness.Data(personPreview), "movedUseIds"), McpHarness.Text(McpHarness.Data(aiPreview), "movedUseIds"));
        Assert.StartsWith(McpHarness.Text(personPreview, "summary"), McpHarness.Text(aiPreview, "summary"), StringComparison.Ordinal);
        Assert.Contains("would ask", McpHarness.Text(aiPreview, "summary"), StringComparison.Ordinal);
        Assert.NotEqual("null", McpHarness.Text(McpHarness.Data(aiPreview), "performedBy.clientApp"));
        Assert.Equal("ai-client-for-person", McpHarness.Text(McpHarness.Data(aiPreview), "performedBy.actorKind"));

        JsonTreeNode moved = h.Ok(mover, "move_data_references", move + "}", server: web);
        JsonTreeNode change = Js.Member(McpHarness.Data(moved), "ownerChange")!;
        Assert.Equal(McpHarness.Reader, McpHarness.Text(change, "newDataOwner.objectId"));
        Assert.Equal("1", McpHarness.Text(change, "redeclaredCount"));
        Assert.Equal("1", McpHarness.Text(change, "reaskedCount"));
        Assert.Equal("1", McpHarness.Text(change, "withdrawnAnsweredCount"));
        Assert.Equal("0", McpHarness.Text(change, "withdrawnNoOwnerCount"));
        Assert.Equal("3", McpHarness.Text(change, "openedCount"));
        Assert.Equal(McpHarness.Text(McpHarness.Data(personPreview), "ownerChange"), McpHarness.Text(McpHarness.Data(moved), "ownerChange"));
        Assert.DoesNotContain("decision.propose-not-allowed", Js.Write(Js.Member(moved, "warnings") ?? Js.EmptyArray), StringComparison.Ordinal);
        Assert.Contains("now asks", McpHarness.Text(moved, "summary"), StringComparison.Ordinal);

        // Unanswered: the decision keeps its identity and now asks the payroll data owner; the old request is canceled.
        var records = h.Store.Query(RowQuery.For("approval-record").Where("decision", waitingDecision)).Where(r => !r.Flag("isConsultation")).ToList();
        Assert.Contains(records, r => r.Text("outcomeMeaning") == "cancelled" && r.Text("askedPrincipal") == McpHarness.Deputy);
        Assert.Contains(records, r => r.Text("outcomeMeaning") == "pending" && r.Text("askedPrincipal") == McpHarness.Reader);
        Assert.Equal("proposed", h.Store.Get("decision", waitingDecision)!.Text("statusMeaning"));

        // Answered: the engine withdrew the decision, and a new one about the same use asks the payroll data owner.
        StoredRow withdrawn = h.Store.Get("decision", advisedDecision)!;
        Assert.Equal("withdrawn", withdrawn.Text("statusMeaning"));
        Assert.Contains("after someone had answered", withdrawn.Text("reason"), StringComparison.Ordinal);
        AssertAsksTheNewOwner(h, h.Store.Query(RowQuery.For("decision").Where("subjectDataUse", advisedUse)).Single(d => d.Text("statusMeaning") == "proposed"), moverId);

        // Decided: the use is ended and declared again in Payroll; the accepted decision stays with the ended row, and the
        // new row's decision asks the payroll data owner.
        Assert.NotNull(h.Store.Get("data-use", decidedUse)!.Text("endedOn"));
        StoredRow again = h.Store.Query(RowQuery.For("data-use").Where("dataDomain", "payroll")).Single(u => u.Text("dataLocation") == DataWorld.FinanceTest);
        AssertAsksTheNewOwner(h, h.Store.Query(RowQuery.For("decision").Where("subjectDataUse", again.Id)).Single(), moverId);

        // Needed with no decision: the engine opens one for the move.
        AssertAsksTheNewOwner(h, h.Store.Query(RowQuery.For("decision").Where("subjectDataUse", DataWorld.ExpenseReadsClaims)).Single(), moverId);
    }

    // A decision the engine opened for a move: proposed, written by the engine for the person who moved the rows, with a
    // pending request to the payroll data owner.
    private static void AssertAsksTheNewOwner(McpHarness h, StoredRow decision, string moverId)
    {
        Assert.Equal("proposed", decision.Text("statusMeaning"));
        Assert.Equal("engine", decision.Text("performedByActorKind"));
        Assert.Equal(moverId, decision.Text("createdByPrincipal"));
        Assert.Contains(h.Store.Query(RowQuery.For("approval-record").Where("decision", decision.Id)), r => r.Text("outcomeMeaning") == "pending" && r.Text("askedPrincipal") == McpHarness.Reader && !r.Flag("isConsultation"));
    }

    // Wave 3 verification: an AI client's preview of a move runs every rule the person's move runs, so a move the engine
    // refuses for the person (a moved use would duplicate an open use of the new domain) is refused in the preview too,
    // with the same words, and the words fit a move.
    [Fact]
    public void AnAiClientsMovePreviewIsRefusedWhereThePersonsMoveIs()
    {
        McpHarness h = World();
        McpServerCore web = h.PersonServer();

        // The expense app already reads Travel bookings in finance production, where DU-0001 reads Expense claims.
        h.Ok(McpHarness.TokenOwnerWeb, "save_data_use", DataWorld.Use("travel-bookings", extra: "\"locationId\":\"" + DataWorld.FinanceProd + "\""), server: web);
        string move = "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"travel-bookings\",\"include\":[\"uses\"],\"reason\":\"Claims join the trips\",\"validateOnly\":true}";

        JsonTreeNode person = h.Fails(McpHarness.TokenDataLeadWeb, "move_data_references", move, McpErrorCodes.Validation, server: web);
        JsonTreeNode ai = h.Fails(McpHarness.TokenDataLead, "move_data_references", move, McpErrorCodes.Validation);
        Assert.Equal(McpHarness.Text(person, "error.message"), McpHarness.Text(ai, "error.message"));
        Assert.Equal("toDomainKey", McpHarness.Text(ai, "error.field"));
        Assert.Contains("DU-0001 cannot move to Operations / Travel bookings", McpHarness.Text(ai, "error.message"), StringComparison.Ordinal);
        Assert.DoesNotContain("Change that use instead", McpHarness.Text(ai, "error.message"), StringComparison.Ordinal);
        Assert.Equal("expenses", h.Store.Get("data-use", DataWorld.ExpenseReadsClaims)!.Text("dataDomain"));
    }

    // Wave 3 verification (major): a design decision whose approvals are all in stays proposed while the design gate is not
    // ready, and the engine accepts it as soon as the data-use decision the gate waits for is accepted. The page shows the
    // gate before anyone approves, and the answer says the gate holds the decision.
    [Fact]
    public void AHeldDesignDecisionSettlesOnceTheDataUseItsGateWaitsForIsDecided()
    {
        McpHarness h = World();
        McpServerCore web = h.PersonServer();
        string use = "{\"applicationId\":\"" + McpHarness.Travel + "\",\"domainKey\":\"expenses\",\"access\":\"read\",\"purpose\":\"Shows the claims of each trip.\",\"reachesAi\":false}";
        JsonTreeNode declared = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_data_use", use, server: web));
        string useCard = McpHarness.Text(Js.Items(declared, "approvalsRequested").Single(a => McpHarness.Text(a, "isConsultation") == "false"), "approvalRecordId");

        string proposal = "{\"decisionTypeKey\":\"design\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel + "\",\"statement\":\"Book through the travel API.\","
            + "\"context\":\"The design of the booking flow.\",\"proposedOutcomeKey\":\"approve\",\"reason\":\"It is the supported way in.\"}";
        string design = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenArchitectWeb, "save_decision_proposal", proposal, server: web)), "recordId");
        h.Ok(McpHarness.TokenArchitectWeb, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + design + "\"}", server: web);
        string designCard = h.Store.Query(RowQuery.For("approval-record").Where("decision", design)).Single(r => !r.Flag("isConsultation") && r.Text("outcomeMeaning") == "pending").Id;

        JsonTreeNode before = McpHarness.Data(h.Ok(Reader, "get_decision", "{\"decisionId\":\"" + design + "\"}"));
        Assert.Equal("false", McpHarness.Text(before, "gate.ready"));
        JsonTreeNode answered = h.Ok(McpHarness.TokenArchitectWeb, "answer_approval", "{\"approvalRecordId\":\"" + designCard + "\",\"answer\":\"approve\",\"reason\":\"The flow is sound.\"}", server: web);
        Assert.Contains("the gate is not ready yet", McpHarness.Text(answered, "summary"), StringComparison.Ordinal);
        Assert.Equal("proposed", h.Store.Get("decision", design)!.Text("statusMeaning"));

        // The data owner approves the data-use card: the gate turns ready, and the design decision is accepted in that call.
        h.Ok(McpHarness.TokenDeputyWeb, "answer_approval", "{\"approvalRecordId\":\"" + useCard + "\",\"answer\":\"approve\",\"reason\":\"The trips need their claims.\"}", server: web);
        Assert.Equal("accepted", h.Store.Get("decision", design)!.Text("statusMeaning"));
    }

    // ---- 16. propose_owner_change on a data domain or a data location ----

    [Fact]
    public void DomainAndLocationOwnersChangeThroughOwnershipDecisions()
    {
        McpHarness h = World();
        JsonTreeNode top = McpHarness.Data(h.Ok(McpHarness.TokenDataLead, "propose_owner_change", "{\"dataDomainKey\":\"operations\",\"changes\":[{\"ownerRoleKey\":\"data-owner\",\"addObjectId\":\"" + McpHarness.Admin + "\"}],\"reason\":\"Operations needs a data owner.\"}"));
        Assert.Contains(Js.Items(top, "approvalsNeeded"), a => McpHarness.Text(a, "approverRoleKey") == "data-governance-lead");
        Assert.Contains(Js.Items(top, "approvalsNeeded"), a => McpHarness.Text(a, "approverProposedOwners") == "true");
        StoredRow decision = h.Store.Get("decision", McpHarness.Text(top, "decisionId"))!;
        Assert.Equal("data-domain", decision.Text("subjectKind"));
        Assert.Equal("operations", decision.Text("subjectDataDomain"));

        // The technical owner of the payroll host names a deputy for it.
        McpHarness.Data(h.Ok(Owner, "propose_owner_change", "{\"dataLocationId\":\"" + DataWorld.PayrollHost + "\",\"changes\":[{\"ownerRoleKey\":\"technical-owner\",\"addObjectId\":\"" + McpHarness.Deputy + "\",\"isDeputy\":true}],\"reason\":\"A deputy for the host.\"}"));

        // Refusals: no right on the domain; an owner role a domain cannot have; two subjects at once.
        h.Fails(Owner, "propose_owner_change", "{\"dataDomainKey\":\"operations\",\"changes\":[{\"ownerRoleKey\":\"data-owner\",\"addObjectId\":\"" + McpHarness.Owner + "\"}],\"reason\":\"x\"}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenDataLead, "propose_owner_change", "{\"dataDomainKey\":\"operations\",\"changes\":[{\"ownerRoleKey\":\"technical-owner\",\"addObjectId\":\"" + McpHarness.Owner + "\"}],\"reason\":\"x\"}", McpErrorCodes.Validation);
        h.Fails(McpHarness.TokenDataLead, "propose_owner_change", "{\"dataDomainKey\":\"operations\",\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changes\":[{\"ownerRoleKey\":\"data-owner\",\"addObjectId\":\"" + McpHarness.Owner + "\"}],\"reason\":\"x\"}", McpErrorCodes.Validation);

        // The data governance lead's role does not reach the owners of an application (the owner relation still applies).
        h.Fails(McpHarness.TokenDataLead, "propose_owner_change", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changes\":[{\"ownerRoleKey\":\"data-owner\",\"addObjectId\":\"" + McpHarness.Owner + "\"}],\"reason\":\"x\"}", McpErrorCodes.NotAuthorized);
    }

    // ---- The existing tools that gained data (docs/architecture/data-taxonomy.md, MCP tools and REST) ----

    [Fact]
    public void TheApplicationAndPortfolioReadsCarryTheData()
    {
        McpHarness h = World();
        JsonTreeNode owners = McpHarness.Data(h.Ok(Reader, "get_application_owners", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"));
        JsonTreeNode dataOwner = Js.Items(owners, "dataOwners").Single();
        Assert.Equal("expenses", McpHarness.Text(dataOwner, "domain.key"));
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(dataOwner, "principal.objectId"));
        Assert.Equal("finance", McpHarness.Text(dataOwner, "inheritedFrom.key"));

        JsonTreeNode card = McpHarness.Data(h.Ok(Reader, "get_application_card", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"sections\":[\"data\"]}"));
        Assert.Equal("needed", McpHarness.Text(Js.Items(Js.Member(card, "data"), "uses").Single(), "state"));
        Assert.Contains(Js.Items(Js.Member(card, "data"), "findings"), f => McpHarness.Text(f, "check") == "DC-05");
        Assert.Equal("confidential", McpHarness.Text(card, "data.highestClassification.key"));

        JsonTreeNode summary = McpHarness.Data(h.Ok(Reader, "get_portfolio_summary", "{}"));
        Assert.True(Js.Number(Js.Member(summary, "dataGaps"), "total") > 0);

        // Travel is at tier 2, where Balanced's design gate asks for declared and decided data uses.
        JsonTreeNode requirements = McpHarness.Data(h.Ok(Reader, "get_requirements", "{\"businessApplicationId\":\"" + McpHarness.Travel + "\"}"));
        Assert.Contains(Js.Items(requirements, "gates").SelectMany(g => Js.Items(g, "requirements")), r => McpHarness.Text(r, "requirementKind") == "data-uses-declared");

        // The expense app's data implies restricted; an answer of none gets the hint and a DC-09 warning.
        JsonTreeNode tier = h.Ok(Owner, "answer_tier_questions", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        Assert.Equal("restricted", McpHarness.Text(McpHarness.Data(tier), "impliedAnswer.answerKey"));
        Assert.Contains("data.sensitivity-below-data", Js.Write(Js.Member(tier, "warnings")!), StringComparison.Ordinal);

        // The data checks read the answer as answer_tier_questions stored it (questionKey and answerKey), so DC-09 is a
        // finding of the expense app from now on (data taxonomy slice 5).
        JsonTreeNode belowData = McpHarness.Data(h.Ok(Reader, "get_data_gaps", "{\"applicationId\":\"" + McpHarness.Expense + "\",\"checks\":[\"DC-09\"]}"));
        Assert.Equal(1L, Js.Number(Js.Member(belowData, "counts"), "DC-09"));

        JsonTreeNode found = McpHarness.Data(h.Ok(Reader, "find_records", "{\"recordNumber\":\"du 1\"}"));
        Assert.Equal("data-use", McpHarness.Text(Js.Items(found, "items").Single(), "entity"));

        JsonTreeNode trace = McpHarness.Data(h.Ok(Owner, "explain_rules", "{\"subjectKind\":\"data-domain\",\"subjectId\":\"bank-details\"}"));
        JsonTreeNode aspect = Js.Items(trace, "aspects").Single(a => McpHarness.Text(a, "aspect") == "data");
        Assert.Equal("payroll", McpHarness.Text(aspect, "winner.key"));
    }

    [Fact]
    public void QueuesLeavesDecisionsAndWithdrawalsCoverDataOwners()
    {
        McpHarness h = World();

        // The payroll owner has no deputy: a data gap in their queue.
        JsonTreeNode queue = McpHarness.Data(h.Ok(Reader, "get_my_queue", "{\"kinds\":[\"data-gap\"]}"));
        JsonTreeNode gap = Js.Items(queue, "items").First();
        Assert.Equal("data-gap", McpHarness.Text(gap, "kind"));
        Assert.StartsWith("DC-03", McpHarness.Text(gap, "title"));

        // A leave lists the data domains the person owns.
        JsonTreeNode leave = McpHarness.Data(h.Ok(Admin, "record_owner_leave", "{\"principalObjectId\":\"" + McpHarness.Reader + "\",\"leavesOn\":\"2026-12-31\",\"kind\":\"leave\"}"));
        Assert.Equal("payroll", McpHarness.Text(Js.Items(leave, "affectedData").Single(), "dataDomainKey"));

        // A retention decision is about a use that writes; the AI client never submits one.
        h.Fails(Owner, "save_decision_proposal", Retention(DataWorld.ExpenseReadsClaims), McpErrorCodes.Validation);
        string writes = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_data_use", DataWorld.Use("travel-bookings", "write", extra: "\"locationId\":\"" + DataWorld.FinanceProd + "\""), server: h.PersonServer())), "dataUseId");
        string retention = McpHarness.Text(McpHarness.Data(h.Ok(Admin, "save_decision_proposal", Retention(writes))), "recordId");
        h.Fails(Admin, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + retention + "\"}", McpErrorCodes.AiCannotDoThis);
        JsonTreeNode log = McpHarness.Data(h.Ok(Reader, "get_decision_log", "{\"dataDomainKey\":\"operations\"}"));
        Assert.Contains(Js.Items(log, "items"), d => McpHarness.Text(d, "decisionId") == retention);

        // A draft data use an AI client saved is withdrawn by the person it was drafted for.
        string draft = McpHarness.Text(McpHarness.Data(h.Ok(Owner, "save_data_use", DataWorld.Use("travel-bookings"))), "dataUseId");
        JsonTreeNode withdrawn = McpHarness.Data(h.Ok(Owner, "withdraw_record", "{\"subjectKind\":\"data-use\",\"subjectId\":\"" + draft + "\",\"reason\":\"Not needed after all\"}"));
        Assert.Equal("archived", McpHarness.Text(withdrawn, "status.meaning"));
    }

    // get_configuration and propose_configuration_change cover C47 to C50 with no new tool (docs/architecture/data-taxonomy.md, MCP tools and REST): the tree is
    // read, and the configuration admin drafts a new domain, which OP-09 judges.
    [Fact]
    public void TheConfigurationToolsCoverTheTaxonomy()
    {
        McpHarness h = World();
        JsonTreeNode domains = McpHarness.Data(h.Ok(Reader, "get_configuration", "{\"entity\":\"data-domain\"}"));
        Assert.Equal(6, Js.Items(domains, "rows").Count);

        string draft = "{\"baseVersionNumber\":1,\"proposalRationale\":\"Corporate cards sit under Finance\",\"validateOnly\":true,\"operations\":[{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"corporate-cards\","
            + "\"values\":{\"label\":\"Corporate cards\",\"parentKey\":\"finance\",\"levelKey\":\"subdomain\",\"classificationKey\":\"confidential\"}}]}";
        Assert.Equal("true", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", draft)), "valid"));

        // A domain whose parent does not exist breaks V49: OP-09 reports it, and the draft cannot be activated.
        JsonTreeNode broken = McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", draft.Replace("\"parentKey\":\"finance\"", "\"parentKey\":\"no-such-parent\"")));
        Assert.Equal("false", McpHarness.Text(broken, "valid"));
        Assert.Contains(Js.Items(broken, "errors"), e => McpHarness.Text(e, "key") == "corporate-cards");

        // Refusal: a reader proposes no configuration change.
        h.Fails(Reader, "propose_configuration_change", draft, McpErrorCodes.NotAuthorized);
    }

    private static string Retention(string useId)
    {
        return "{\"decisionTypeKey\":\"data-retention\",\"subjectKind\":\"data-use\",\"subjectId\":\"" + useId + "\",\"statement\":\"Archive the data at retirement\",\"context\":\"The app retires\",\"proposedOutcomeKey\":\"archive\",\"reason\":\"Keep seven years\"}";
    }
}
