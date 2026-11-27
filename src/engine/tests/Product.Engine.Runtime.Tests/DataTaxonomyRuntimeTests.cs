using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The data taxonomy in the runtime (docs/architecture/data-taxonomy.md; data taxonomy slice 2):
// a data-use card goes to the effective owner of the domain by name, restricted data to AI also asks the security
// reviewer, a use that breaks a never rule is refused when saved for a person and an AI client alike (D-41, question 7),
// the data-use state is computed when read, the design gate reads the data uses, an ownership decision about a domain
// writes a domain owner, and the data checks find the gaps.
public sealed class DataTaxonomyRuntimeTests
{
    // The worked tree of the data fixtures, on Balanced.
    private const string Tree = "["
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"people\",\"values\":{\"label\":\"People\",\"levelKey\":\"domain\",\"classificationKey\":\"confidential\"}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"payroll\",\"values\":{\"label\":\"Payroll\",\"parentKey\":\"people\",\"levelKey\":\"subdomain\",\"classificationKey\":\"restricted\",\"aiUseRule\":\"never\"}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"bank-details\",\"values\":{\"label\":\"Bank details\",\"parentKey\":\"payroll\",\"levelKey\":\"data-set\"}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"timesheets\",\"values\":{\"label\":\"Timesheets\",\"parentKey\":\"people\",\"levelKey\":\"subdomain\"}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"field-operations\",\"values\":{\"label\":\"Field operations\",\"levelKey\":\"domain\",\"classificationKey\":\"internal\"}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"safety-findings\",\"values\":{\"label\":\"Safety findings\",\"parentKey\":\"field-operations\",\"levelKey\":\"subdomain\",\"classificationKey\":\"restricted\"}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"parts-catalog\",\"values\":{\"label\":\"Parts catalog\",\"parentKey\":\"field-operations\",\"levelKey\":\"subdomain\"}}"
        + "]";

    private static long Number(JsonTreeNode? payload, string member)
    {
        JsonTreeNode? value = RuntimeJsonTestHelpers.Member(payload, member);
        return value != null && value.TryGetInt64(out long n) ? n : -1;
    }

    private static long Count(JsonTreeNode? payload, string check)
    {
        return Number(RuntimeJsonTestHelpers.Member(payload, "counts"), check);
    }

    private static string DomainOwner(string domain, string principal)
    {
        return "{\"subjectKind\":\"data-domain\",\"subjectDataDomain\":\"" + domain + "\",\"ownerRole\":\"data-owner\",\"principal\":\"" + principal + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}";
    }

    private static string Use(Harness h, string domain, bool ai, string status = "data-use-registered", string meaning = "registered")
    {
        return "{\"businessApplication\":\"" + h.ApplicationId + "\",\"dataDomain\":\"" + domain + "\",\"access\":\"read\",\"purpose\":\"Fixture purpose.\",\"reachesAi\":" + (ai ? "true" : "false")
            + ",\"isDeidentified\":false,\"status\":\"" + status + "\",\"statusMeaning\":\"" + meaning + "\",\"startOn\":\"2026-11-22\"}";
    }

    private static string ProposeUseDecision(Harness h, string useId)
    {
        return Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"data-use\",\"subjectKind\":\"data-use\",\"subjectDataUse\":\"" + useId + "\",\"subjectApplication\":\"" + h.ApplicationId
            + "\",\"statement\":\"The application may send safety findings to its model.\",\"outcome\":\"allow\",\"status\":\"decision-proposed\"}"))).RowId!;
    }

    // Restricted data to AI (Approval rules): the data owner of the domain is asked by name, and the security reviewer through
    // the role. The steward consultation resolves to nobody, so no card goes out for it.
    [Fact]
    public void ARestrictedAiUseAsksTheDomainOwnerByNameAndTheSecurityReviewer()
    {
        Harness h = Harness.Create(Tree);
        h.Store.Seed("owner-assignment", DomainOwner("safety-findings", Harness.Person2));
        string use = h.Store.Seed("data-use", Use(h, "safety-findings", ai: true));
        string decision = ProposeUseDecision(h, use);

        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision));
        StoredRow[] records = RuntimeJsonTestHelpers.Strings(request.Payload, "approvalRecordIds").Select(id => h.Row("approval-record", id)).ToArray();

        Assert.Equal(2, records.Length);
        StoredRow owner = Assert.Single(records, r => r.Text("requiredOwnerRole") == "data-owner");
        Assert.Equal(Harness.Person2, owner.Text("askedPrincipal"));
        Assert.Equal("data-use-restricted-ai-owner", owner.Text("approvalRule"));
        Assert.Single(records, r => r.Text("requiredRole") == "security-reviewer" && r.Text("approvalRule") == "data-use-restricted-ai-security");

        // The state is computed when read: proposed, 0 of 2 approvals.
        RuntimeOutcome state = Harness.Ok(h.Runtime.EvaluateDataUse(Harness.PersonOne, use));
        Assert.Equal("proposed", state.Outcome);
        Assert.Equal(2, Number(state.Payload, "approvalsNeeded"));
        Assert.Equal(0, Number(state.Payload, "approvalsGiven"));
    }

    // The data owner inherits down: a use of Parts catalog that reaches AI asks the owner of Field operations.
    [Fact]
    public void AnInheritedOwnerAnswersForTheDomainsBelow()
    {
        Harness h = Harness.Create(Tree);
        h.Store.Seed("owner-assignment", DomainOwner("field-operations", Harness.Person3));
        string use = h.Store.Seed("data-use", Use(h, "parts-catalog", ai: true));

        RuntimeOutcome needed = Harness.Ok(h.Runtime.EvaluateDataUse(Harness.PersonOne, use));
        Assert.Equal("needed", needed.Outcome);

        string decision = ProposeUseDecision(h, use);
        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision));
        StoredRow record = h.Row("approval-record", h.CountingRecord(request.Payload));
        Assert.Equal(Harness.Person3, record.Text("askedPrincipal"));
        Assert.Equal("data-use-domain", record.Text("approvalRule"));
    }

    // D-41, question 7, as the product owner changed it: a use that breaks a never rule is refused when it is saved, for a person and an
    // AI client alike, and the refusal names the rule, the domain and where the rule is set. Nothing is written.
    [Fact]
    public void AUseThatBreaksANeverRuleIsRefusedForAPersonAndAnAiClient()
    {
        Harness h = Harness.Create(Tree);
        string before = h.Store.Dump();
        foreach (CallerIdentity caller in new[] { Harness.PersonOne, Harness.AiClientOf1 })
        {
            RuntimeOutcome refused = h.Runtime.Write(caller, WriteCommand.Create("data-use", Use(h, "bank-details", ai: true).Replace(",\"statusMeaning\":\"registered\"", string.Empty)));
            Assert.False(refused.Succeeded);
            Assert.True(refused.HasReason(ReasonCodes.DataRuleNever), refused.ToString());
            Assert.Contains("Payroll", refused.ToString(), System.StringComparison.Ordinal);
            Assert.Contains("never", refused.ToString(), System.StringComparison.Ordinal);
        }

        Assert.Equal(before, h.Store.Dump());

        // Without AI the same data is only a decision away, so the save goes through.
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("data-use", Use(h, "bank-details", ai: false).Replace(",\"statusMeaning\":\"registered\"", string.Empty))));
    }

    // Gate requirements: at tier 2 the design gate needs the data uses declared and decided. A use of internal data that only
    // reads needs no decision, so declaring it makes the gate ready.
    [Fact]
    public void TheDesignGateReadsTheDataUses()
    {
        Harness h = Harness.Create(Tree);
        h.Store.Tamper("business-application", h.ApplicationId, RuntimeJsonTestHelpers.With(h.Row("business-application", h.ApplicationId).Columns, "tier", JsonTreeNode.FromString("tier-2")));

        RuntimeOutcome before = Harness.Ok(h.Runtime.GetGateReadiness(Harness.PersonOne, h.ApplicationId, "design"));
        Assert.Equal("not-ready", before.Outcome);
        Assert.True(before.HasReason(ReasonCodes.GateRequirementDataUsesUndeclared), before.ToString());

        h.Store.Seed("data-use", Use(h, "parts-catalog", ai: false));
        RuntimeOutcome after = Harness.Ok(h.Runtime.GetGateReadiness(Harness.PersonOne, h.ApplicationId, "design"));
        Assert.Equal("ready", after.Outcome);

        // A use of restricted data needs a decision, so the gate waits for it.
        h.Store.Seed("owner-assignment", DomainOwner("safety-findings", Harness.Person2));
        h.Store.Seed("data-use", Use(h, "safety-findings", ai: false));
        RuntimeOutcome waiting = Harness.Ok(h.Runtime.GetGateReadiness(Harness.PersonOne, h.ApplicationId, "design"));
        Assert.Equal("not-ready", waiting.Outcome);
        Assert.True(waiting.HasReason(ReasonCodes.GateRequirementDataUsesUndecided), waiting.ToString());
    }

    // The wave 3 fix pass left one gap: a plain write, outside a composed call, never
    // settled a held gate decision, so it waited for the next accepted decision or the daily refresh. A design decision
    // whose approval is in stays proposed while the design gate waits for the data uses; the plain write that declares a
    // use needing no decision makes the gate ready, and the decision is accepted in that same call.
    [Fact]
    public void APlainWriteSettlesAHeldGateDecision()
    {
        Harness h = Harness.Create(Tree);
        h.Store.Tamper("business-application", h.ApplicationId, RuntimeJsonTestHelpers.With(h.Row("business-application", h.ApplicationId).Columns, "tier", JsonTreeNode.FromString("tier-2")));
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"design\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId
            + "\",\"statement\":\"Read the parts catalog through its API.\",\"outcome\":\"approve\",\"status\":\"decision-proposed\"}"))).RowId!;
        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision));
        string[] cards = RuntimeJsonTestHelpers.Strings(request.Payload, "approvalRecordIds").Where(id => !h.Row("approval-record", id).Flag("isConsultation")).ToArray();
        Assert.NotEmpty(cards);
        foreach (string card in cards)
        {
            Harness.Ok(h.Answer(card, "approval-approved", Harness.Person2, typedReason: "The API is the supported way in."));
        }

        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        RuntimeOutcome declared = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("data-use", Use(h, "parts-catalog", ai: false).Replace(",\"statusMeaning\":\"registered\"", string.Empty))));

        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Contains(declared.Writes!, w => w.Entity == "decision" && w.RowId == decision);
    }

    // Appointing owners: an ownership decision about a lower domain goes to the parent domain's owner, by name, and to the
    // proposed owner; once both approve, the accepted decision writes the owner assignment with the domain as its subject.
    [Fact]
    public void AnOwnershipDecisionAboutADomainWritesADomainOwner()
    {
        Harness h = Harness.Create(Tree);
        h.Store.Seed("owner-assignment", DomainOwner("people", Harness.Person2));
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonTwo, WriteCommand.Create("decision",
            "{\"decisionType\":\"ownership\",\"subjectKind\":\"data-domain\",\"subjectDataDomain\":\"timesheets\",\"statement\":\"Person 3 owns timesheets\",\"outcome\":\"assign\","
            + "\"proposedChangesJson\":{\"owners\":[{\"ownerRoleKey\":\"data-owner\",\"addObjectId\":\"" + Harness.Person3 + "\"}]},\"status\":\"decision-proposed\"}"))).RowId!;

        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonTwo, decision));
        StoredRow[] records = RuntimeJsonTestHelpers.Strings(request.Payload, "approvalRecordIds").Select(id => h.Row("approval-record", id)).ToArray();
        StoredRow parent = Assert.Single(records, r => r.Text("approvalRule") == "ownership-domain-parent-owner");
        StoredRow successor = Assert.Single(records, r => r.Text("approvalRule") == "ownership-domain-proposed-owners");
        Assert.Equal(Harness.Person2, parent.Text("askedPrincipal"));
        Assert.Equal(Harness.Person3, successor.Text("askedPrincipal"));

        Harness.Ok(h.Runtime.AnswerApproval(CallerIdentity.Person(Harness.Person2, null, "Product page"), new ApprovalAnswerCommand(parent.Id, approve: true, reason: "Agreed")));
        Harness.Ok(h.Runtime.AnswerApproval(CallerIdentity.Person(Harness.Person3, null, "Product page"), new ApprovalAnswerCommand(successor.Id, approve: true, reason: "I take it on")));

        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        StoredRow owner = Assert.Single(h.Store.Rows("owner-assignment"), o => o.Text("subjectDataDomain") == "timesheets");
        Assert.Equal("data-domain", owner.Text("subjectKind"));
        Assert.Equal(Harness.Person3, owner.Text("principal"));
        Assert.Equal("data-owner", owner.Text("ownerRole"));
        Assert.Null(owner.Text("businessApplication"));
        Assert.Equal(decision, owner.Text("ownershipDecision"));

        // The new owner answers for timesheets now.
        RuntimeOutcome resolved = Harness.Ok(h.Runtime.ResolveDataDomain(Harness.PersonOne, "timesheets"));
        Assert.Equal("resolved", resolved.Outcome);
        Assert.Contains(Harness.Person3, resolved.Payload!.ToString(), System.StringComparison.Ordinal);
    }

    // Data checks: the data checks read the store and find the gaps: domains with no owner (DC-01) and a draft use that
    // breaks a rule (DC-07). The person scope keeps the findings about that person's data.
    [Fact]
    public void TheDataChecksFindTheGaps()
    {
        Harness h = Harness.Create(Tree);
        h.Store.Seed("owner-assignment", DomainOwner("field-operations", Harness.Person3));
        h.Store.Seed("data-use", Use(h, "bank-details", ai: true, status: "data-use-draft", meaning: "draft"));
        h.Store.Seed("data-use", Use(h, "parts-catalog", ai: true));

        RuntimeOutcome all = Harness.Ok(h.Runtime.RunDataChecks(Harness.PersonOne));
        Assert.Equal("checked", all.Outcome);
        Assert.Equal("4", Count(all.Payload, "DC-01").ToString());
        Assert.Equal("1", Count(all.Payload, "DC-07").ToString());
        Assert.Equal("1", Count(all.Payload, "DC-05").ToString());

        RuntimeOutcome mine = Harness.Ok(h.Runtime.RunDataChecks(Harness.PersonOne, CanonicalJson.Parse("{\"principalId\":\"" + Harness.Person3 + "\"}")));
        Assert.Equal("1", Count(mine.Payload, "DC-05").ToString());
        Assert.Equal("0", Count(mine.Payload, "DC-01").ToString());
    }
}
