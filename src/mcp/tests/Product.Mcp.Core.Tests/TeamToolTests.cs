using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Backend;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Tools 95 and 96 (document 24, B24-20): the teams and committees. A team is a group the install knows (a Principal
// Reference of kind group, or a bound group); its kinds are derived on every read; members come from the directory reader
// (Graph as the person, the demo personas, or none), never stored.
public sealed class TeamToolTests
{
    // A fictional support team: a group the product holds a reference to, bound to no role, that supports both fixture
    // applications.
    private const string SupportTeam = "00000000-0000-4000-8000-000000000701";
    private const string SupportOnExpense = "00000000-0000-4000-8000-000000000711";
    private const string SupportDeputyOnTravel = "00000000-0000-4000-8000-000000000712";
    private const string SupportDecision = "00000000-0000-4000-8000-000000000721";
    private const string SupportAsk = "00000000-0000-4000-8000-000000000722";
    private const string ChangeDecision = "00000000-0000-4000-8000-000000000731";
    private const string AnsweredByMember = "00000000-0000-4000-8000-000000000732";
    private const string AnsweredByOther = "00000000-0000-4000-8000-000000000733";

    private static McpHarness World(bool withPorts = true)
    {
        McpHarness h = McpHarness.Create(withPorts: withPorts);
        h.Store.Seed("principal-reference", "{\"principalKind\":\"group\",\"entraObjectId\":\"" + SupportTeam + "\",\"displayLabel\":\"Fixture service desk\",\"isErased\":false,\"directoryState\":\"enabled\"}", id: "00000000-0000-4000-8000-000000000702");
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + McpHarness.Expense + "\",\"ownerRole\":\"support-owner\",\"principal\":\"" + SupportTeam + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}", id: SupportOnExpense);
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + McpHarness.Travel + "\",\"ownerRole\":\"support-owner\",\"principal\":\"" + SupportTeam + "\",\"isDeputy\":true,\"startOn\":\"2026-02-01\"}", id: SupportDeputyOnTravel);
        // An ended assignment makes no team and counts nowhere.
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + McpHarness.Travel + "\",\"ownerRole\":\"technical-owner\",\"principal\":\"" + SupportTeam + "\",\"isDeputy\":false,\"startOn\":\"2025-01-01\",\"endOn\":\"2025-06-30\"}", id: "00000000-0000-4000-8000-000000000713");

        // A go-live decision on the expense app that asks its support owner: it waits on the support team.
        h.Store.Seed("decision", "{\"recordNumber\":\"DEC-0701\",\"decisionType\":\"go-live\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Go live for the finance team\",\"status\":\"decision-proposed\",\"statusMeaning\":\"proposed\"}", id: SupportDecision);
        h.Store.Seed("approval-record", "{\"decision\":\"" + SupportDecision + "\",\"requiredOwnerRole\":\"support-owner\",\"isConsultation\":false,\"requestedOn\":\"2026-11-21T09:00:00Z\",\"expiresOn\":\"2026-11-26T09:00:00Z\","
            + "\"outcome\":\"approval-pending\",\"outcomeMeaning\":\"pending\"}", id: SupportAsk);

        // A change authorization the change managers answered: once by a member of their group (the owner, in the fixture
        // directory), once by someone the directory does not list in it.
        h.Store.Seed("decision", "{\"recordNumber\":\"DEC-0702\",\"decisionType\":\"change-authorization\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Authorize the approval step change\",\"status\":\"decision-accepted\",\"statusMeaning\":\"accepted\"}", id: ChangeDecision);
        h.Store.Seed("approval-record", "{\"decision\":\"" + ChangeDecision + "\",\"requiredRole\":\"change-manager\",\"isConsultation\":false,\"requestedOn\":\"2026-11-18T09:00:00Z\",\"expiresOn\":\"2026-11-23T09:00:00Z\","
            + "\"outcome\":\"approval-approved\",\"outcomeMeaning\":\"approved\",\"approver\":\"" + McpHarness.Owner + "\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-19T10:00:00Z\",\"countsTowardRequirement\":true}", id: AnsweredByMember);
        h.Store.Seed("approval-record", "{\"decision\":\"" + ChangeDecision + "\",\"requiredRole\":\"change-manager\",\"isConsultation\":false,\"requestedOn\":\"2026-11-18T09:00:00Z\",\"expiresOn\":\"2026-11-23T09:00:00Z\","
            + "\"outcome\":\"approval-approved\",\"outcomeMeaning\":\"approved\",\"approver\":\"" + McpHarness.Admin + "\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-20T10:00:00Z\",\"countsTowardRequirement\":true}", id: AnsweredByOther);
        return h;
    }

    private static JsonTreeNode Team(JsonTreeNode list, string objectId)
    {
        return Js.Items(list, "items").Single(t => string.Equals(McpHarness.Text(t, "teamId"), objectId, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Kinds(JsonTreeNode team)
    {
        return Js.Items(team, "kinds").Select(k => k.GetString()).ToArray();
    }

    // ---- 95. list_teams ----

    [Fact]
    public void TheTeamListGivesEveryKnownGroupItsKindsRolesAndCounts()
    {
        McpHarness h = World();
        Scenarios.SubmittedIntake(h);
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"top\":50}"));

        // The eight bound groups, the support team, and the data lead's group (known, bound to nothing without the data world).
        Assert.Equal("10", McpHarness.Text(data, "counts.all"));
        Assert.Equal("10", McpHarness.Text(data, "totalCount"));

        // Committees: the roles an approval rule asks to approve, or a decision right makes accountable (Balanced: the change
        // manager, the platform admin, the configuration admin and the architect among the fixture's bound roles).
        Assert.Equal("4", McpHarness.Text(data, "counts.committee"));
        Assert.Equal(new[] { "committee" }, Kinds(Team(data, McpHarness.ChangeManagerGroup)));
        Assert.Equal(new[] { "committee" }, Kinds(Team(data, McpHarness.ArchitectGroup)));
        Assert.Equal(new[] { "committee" }, Kinds(Team(data, McpHarness.PlatformAdminGroup)));
        Assert.Equal(new[] { "committee" }, Kinds(Team(data, McpHarness.ConfigurationAdminGroup)));
        Assert.Equal("4", McpHarness.Text(data, "counts.roleGroup"));
        Assert.Equal(new[] { "role-group" }, Kinds(Team(data, McpHarness.ReaderGroup)));
        Assert.Equal(new[] { "role-group" }, Kinds(Team(data, McpHarness.OwnerGroup)));
        Assert.Equal("1", McpHarness.Text(data, "counts.supportTeam"));
        Assert.Equal("1", McpHarness.Text(data, "counts.other"));
        Assert.Equal(new[] { "other" }, Kinds(Team(data, McpHarness.DataLeadGroup)));

        // Committees come first, then support teams, role groups and other groups.
        string[] order = Js.Items(data, "items").Select(t => Kinds(t)[0]).ToArray();
        Assert.Equal(order.OrderBy(k => Array.IndexOf(new[] { "committee", "support-team", "role-group", "other" }, k)).ToArray(), order);

        // A support team: its name as the product holds it, its applications and assignments (an ended one does not count),
        // the request that asks its owner role, and the owner gaps on its applications.
        JsonTreeNode support = Team(data, SupportTeam);
        Assert.Equal(new[] { "support-team" }, Kinds(support));
        Assert.Equal("Fixture service desk", McpHarness.Text(support, "team.displayLabel"));
        Assert.Empty(Js.Items(support, "roles"));
        Assert.Equal("2", McpHarness.Text(support, "applicationCount"));
        Assert.Equal("2", McpHarness.Text(support, "assignmentCount"));
        Assert.Equal("1", McpHarness.Text(support, "waitingCount"));
        Assert.Equal("enabled", McpHarness.Text(support, "directoryState"));
        Assert.EndsWith("/records/team/" + SupportTeam, McpHarness.Text(support, "recordUrl"), StringComparison.Ordinal);

        // A committee's waiting count is every pending request that asks its role, as the store holds them, for a reader who
        // may read every intake (the platform admin). The reader may not read another person's intake (intake-visibility
        // own), so the requests on it are counted as not readable, never shown.
        Assert.Contains(Js.Items(h.Ok(McpHarness.TokenReader, "list_teams", "{\"top\":50}"), "couldNotRead"), c => Js.Text(c, "kind") == "approval-record");
        JsonTreeNode all = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_teams", "{\"top\":50}"));
        foreach (JsonTreeNode team in Js.Items(all, "items").Where(t => Js.Items(t, "roles").Count > 0))
        {
            var roles = Js.Items(team, "roles").Select(r => McpHarness.Text(r, "key")).ToHashSet(StringComparer.Ordinal);
            int expected = h.Store.Query(Product.Engine.Runtime.Ports.RowQuery.For("approval-record").Where("outcomeMeaning", "pending"))
                .Count(r => r.Text("askedPrincipal") == null && r.Text("requiredRole") is string role && roles.Contains(role));
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), McpHarness.Text(team, "waitingCount"));
        }

        Assert.True(Js.Items(all, "items").Where(t => Js.Items(t, "roles").Count > 0).Sum(t => int.Parse(McpHarness.Text(t, "waitingCount"), System.Globalization.CultureInfo.InvariantCulture)) > 0, "the submitted intake asks at least one bound role");

        // Members: read live from the fixture directory, for the page's rows.
        Assert.Equal("entra-live", McpHarness.Text(data, "membersReadFrom"));
        Assert.Equal("null", McpHarness.Text(data, "membership"));
        Assert.Equal("1", McpHarness.Text(Team(data, McpHarness.ChangeManagerGroup), "memberCount"));
        Assert.Equal("0", McpHarness.Text(Team(data, McpHarness.ReaderGroup), "memberCount"));
    }

    [Fact]
    public void TheTeamListFiltersByKindTextRoleAndMemberAndKeepsTheCounts()
    {
        McpHarness h = World();

        // The kind filter narrows the rows; the counts stay, so each count opens its own rows.
        JsonTreeNode committees = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"kind\":\"committee\"}"));
        Assert.Equal("4", McpHarness.Text(committees, "totalCount"));
        Assert.Equal("10", McpHarness.Text(committees, "counts.all"));
        Assert.All(Js.Items(committees, "items"), t => Assert.Contains("committee", Kinds(t)));
        JsonTreeNode support = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"kind\":\"support-team\"}"));
        Assert.Equal(SupportTeam, McpHarness.Text(Assert.Single(Js.Items(support, "items")), "teamId"));

        // Text finds the team's name or the role it holds.
        JsonTreeNode byName = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"text\":\"service desk\"}"));
        Assert.Equal(SupportTeam, McpHarness.Text(Assert.Single(Js.Items(byName, "items")), "teamId"));
        Assert.Equal("1", McpHarness.Text(byName, "counts.all"));
        JsonTreeNode byRole = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"text\":\"Change MANAGER\"}"));
        Assert.Equal(McpHarness.ChangeManagerGroup, McpHarness.Text(Assert.Single(Js.Items(byRole, "items")), "teamId"));

        // The role filter: the group a decision page says it waits on.
        JsonTreeNode architects = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"roleKey\":\"architect\"}"));
        Assert.Equal(McpHarness.ArchitectGroup, McpHarness.Text(Assert.Single(Js.Items(architects, "items")), "teamId"));
        h.Fails(McpHarness.TokenReader, "list_teams", "{\"roleKey\":\"no-such-role\"}", McpErrorCodes.Validation);

        // The member filter: the person page's "member of". The fixture directory puts the owner in the change manager and
        // application owner groups.
        JsonTreeNode mine = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"memberObjectId\":\"" + McpHarness.Owner + "\"}"));
        Assert.Equal(new[] { McpHarness.ChangeManagerGroup, McpHarness.OwnerGroup }.OrderBy(g => g, StringComparer.Ordinal), Js.Items(mine, "items").Select(t => McpHarness.Text(t, "teamId")).OrderBy(g => g, StringComparer.Ordinal));

        // Paging keeps the query.
        JsonTreeNode first = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"top\":3}"));
        Assert.Equal(3, Js.Items(first, "items").Count);
        JsonTreeNode second = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"top\":3,\"skipToken\":\"" + McpHarness.Text(first, "skipToken") + "\"}"));
        Assert.Empty(Js.Items(first, "items").Select(t => McpHarness.Text(t, "teamId")).Intersect(Js.Items(second, "items").Select(t => McpHarness.Text(t, "teamId"))));
    }

    // ---- 96. get_team ----

    [Fact]
    public void ACommitteePageSaysWhoIsInItWhatItDecidesAndWhatItsMembersAnswered()
    {
        McpHarness h = World();
        JsonTreeNode envelope = h.Ok(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + McpHarness.ChangeManagerGroup + "\"}");
        JsonTreeNode data = McpHarness.Data(envelope);
        Assert.EndsWith("/records/team/" + McpHarness.ChangeManagerGroup, McpHarness.Text(envelope, "links.recordUrl"), StringComparison.Ordinal);
        Assert.Equal(new[] { "committee" }, Kinds(data));
        Assert.Equal("true", McpHarness.Text(data, "isKnownToProduct"));

        // Its members, read live from the directory.
        Assert.Equal("entra-live", McpHarness.Text(data, "membersReadFrom"));
        JsonTreeNode member = Assert.Single(Js.Items(data, "members"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(member, "objectId"));
        Assert.Equal("1", McpHarness.Text(data, "counts.members"));
        Assert.Contains("never stored", McpHarness.Text(data, "membership"), StringComparison.Ordinal);

        // The role it holds, with the decisions it is accountable for and approves.
        JsonTreeNode role = Assert.Single(Js.Items(data, "roles"));
        Assert.Equal("change-manager", McpHarness.Text(role, "role.key"));
        Assert.Equal("true", McpHarness.Text(role, "answersDecisions"));
        Assert.Equal("change-manager-group", McpHarness.Text(role, "binding.bindingKey"));
        Assert.Contains(Js.Items(role, "decisionRights"), d => McpHarness.Text(d, "decisionType.key") == "change-authorization" && McpHarness.Text(d, "letter") == "accountable");
        Assert.Contains(Js.Items(role, "approvals"), a => McpHarness.Text(a, "decisionType.key") == "change-authorization");
        JsonTreeNode changes = Js.Items(data, "decisionTypes").Single(d => McpHarness.Text(d, "decisionType.key") == "change-authorization");
        Assert.Equal("true", McpHarness.Text(changes, "approves"));
        Assert.Contains(Js.Items(changes, "letters"), l => l.GetString() == "accountable");

        // What its members answered through it: the owner's answer, not the admin's (the directory does not put the admin
        // in this group), with the decision's number and link.
        JsonTreeNode answer = Assert.Single(Js.Items(data, "recentAnswers"));
        Assert.Equal(AnsweredByMember, McpHarness.Text(answer, "approvalRecordId"));
        Assert.Equal("DEC-0702", McpHarness.Text(answer, "decision.recordNumber"));
        Assert.Equal("approved", McpHarness.Text(answer, "outcome.meaning"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(answer, "approver.objectId"));
        Assert.EndsWith("/records/decision/" + ChangeDecision, McpHarness.Text(answer, "recordUrl"), StringComparison.Ordinal);
        Assert.Equal("1", McpHarness.Text(data, "counts.answered"));

        // The committee holds no owner role, so it has no applications and no owner gaps.
        Assert.Empty(Js.Items(data, "applications"));
        Assert.Empty(Js.Items(data, "ownerGaps"));
        Assert.StartsWith(McpHarness.Text(data, "team.displayLabel") + ": a committee.", McpHarness.Text(envelope, "summary"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASupportTeamPageListsItsApplicationsWhatWaitsOnItAndItsOwnerGaps()
    {
        McpHarness h = World();
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + SupportTeam + "\"}"));
        Assert.Equal(new[] { "support-team" }, Kinds(data));
        Assert.Empty(Js.Items(data, "roles"));
        Assert.Empty(Js.Items(data, "decisionTypes"));

        // Its applications by name, each with the owner roles it holds there; the ended assignment is gone.
        JsonTreeNode[] apps = Js.Items(data, "applications").ToArray();
        Assert.Equal(new[] { McpHarness.Expense, McpHarness.Travel }, apps.Select(a => McpHarness.Text(a, "businessApplicationId")));
        JsonTreeNode expense = apps[0];
        JsonTreeNode held = Assert.Single(Js.Items(expense, "assignments"));
        Assert.Equal("support-owner", McpHarness.Text(held, "ownerRole.key"));
        Assert.Equal("false", McpHarness.Text(held, "isDeputy"));
        Assert.Equal("true", McpHarness.Text(Assert.Single(Js.Items(apps[1], "assignments")), "isDeputy"));
        Assert.Equal("2", McpHarness.Text(data, "counts.assignments"));

        // What waits on it: the go-live request that asks the support owner of the expense app.
        JsonTreeNode waiting = Assert.Single(Js.Items(data, "waiting"));
        Assert.Equal(SupportAsk, McpHarness.Text(waiting, "approvalRecordId"));
        Assert.Equal("DEC-0701", McpHarness.Text(waiting, "decision.recordNumber"));
        Assert.Equal("support-owner", McpHarness.Text(waiting, "askedOwnerRole.key"));
        Assert.Equal("null", McpHarness.Text(waiting, "askedRole"));
        Assert.Equal("pending", McpHarness.Text(waiting, "outcome.meaning"));
        Assert.Equal("false", McpHarness.Text(waiting, "isExpired"));
        Assert.Equal("Fixture expense approvals", McpHarness.Text(waiting, "subject.applicationName"));
        Assert.EndsWith("/records/decision/" + SupportDecision, McpHarness.Text(waiting, "recordUrl"), StringComparison.Ordinal);

        // The owner gaps among its applications, counted as the Owners page counts them for the same applications.
        foreach (string app in new[] { McpHarness.Expense, McpHarness.Travel })
        {
            JsonTreeNode owners = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_owner_assignments", "{\"businessApplicationId\":\"" + app + "\",\"state\":\"all\"}"));
            Assert.Equal(McpHarness.Text(owners, "ownerGaps"), Js.Items(data, "ownerGaps").Count(g => McpHarness.Text(g, "businessApplicationId") == app).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(Js.Items(data, "ownerGaps").Count.ToString(System.Globalization.CultureInfo.InvariantCulture), McpHarness.Text(data, "counts.ownerGaps"));

        // A person is not a team, and neither is a group the install does not know.
        h.Fails(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + McpHarness.Owner + "\"}", McpErrorCodes.NotFound);
        h.Fails(McpHarness.TokenReader, "get_team", "{\"teamId\":\"00000000-0000-4000-8000-000000000799\"}", McpErrorCodes.NotFound);
    }

    [Fact]
    public void OnADemoInstallTheMembersAreTheDemoPersonas()
    {
        McpHarness h = World();
        h.Backend.Options.Directory = new DemoPersonaDirectory(
            ("00000000-0000-4000-8000-000000000791", "Fixture persona Ada", new[] { McpHarness.ChangeManagerGroup, SupportTeam }),
            ("00000000-0000-4000-8000-000000000792", "Fixture persona Ben", new[] { SupportTeam }));

        JsonTreeNode list = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"top\":50}"));
        Assert.Equal("demo-personas", McpHarness.Text(list, "membersReadFrom"));
        Assert.Contains("the personas and the estate's other people", McpHarness.Text(list, "membership"), StringComparison.Ordinal);
        Assert.Equal("2", McpHarness.Text(Team(list, SupportTeam), "memberCount"));
        Assert.Equal("1", McpHarness.Text(Team(list, McpHarness.ChangeManagerGroup), "memberCount"));

        JsonTreeNode support = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + SupportTeam + "\"}"));
        Assert.Equal("demo-personas", McpHarness.Text(support, "membersReadFrom"));
        Assert.Equal(new[] { "Fixture persona Ada", "Fixture persona Ben" }, Js.Items(support, "members").Select(m => McpHarness.Text(m, "displayLabel")));
        Assert.StartsWith("The members are the demo's fictional people", McpHarness.Text(support, "membership"), StringComparison.Ordinal);

        // A group no persona is in says so, and keeps the name the product holds for it.
        JsonTreeNode readers = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + McpHarness.ReaderGroup + "\"}"));
        Assert.Empty(Js.Items(readers, "members"));
        Assert.StartsWith("Nobody of the demo is in this fictional group.", McpHarness.Text(readers, "membership"), StringComparison.Ordinal);
        Assert.Equal("Fixture group 107", McpHarness.Text(readers, "team.displayLabel"));

        // The person page's "member of", from the same personas.
        JsonTreeNode ada = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_teams", "{\"memberObjectId\":\"00000000-0000-4000-8000-000000000791\"}"));
        Assert.Equal(new[] { McpHarness.ChangeManagerGroup, SupportTeam }.OrderBy(g => g, StringComparer.Ordinal), Js.Items(ada, "items").Select(t => McpHarness.Text(t, "teamId")).OrderBy(g => g, StringComparer.Ordinal));
    }

    [Fact]
    public void WithTheDirectoryReadsOffTheReadSaysMembershipIsManagedInEntra()
    {
        McpHarness h = World(withPorts: false);
        JsonTreeNode envelope = h.Ok(McpHarness.TokenReader, "list_teams", "{}");
        JsonTreeNode list = McpHarness.Data(envelope);
        Assert.Equal("not-read", McpHarness.Text(list, "membersReadFrom"));
        Assert.StartsWith("Membership is managed in Entra.", McpHarness.Text(list, "membership"), StringComparison.Ordinal);
        Assert.All(Js.Items(list, "items"), t => Assert.Equal("null", McpHarness.Text(t, "memberCount")));
        Assert.Contains("Membership is managed in Entra", McpHarness.Text(envelope, "summary"), StringComparison.Ordinal);

        // The team page names the group to open in Entra, and still shows everything else.
        JsonTreeNode team = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + SupportTeam + "\"}"));
        Assert.Equal("null", McpHarness.Text(team, "members"));
        Assert.Equal("null", McpHarness.Text(team, "counts.members"));
        Assert.Equal("Membership is managed in Entra. This install does not read group members, so open the group Fixture service desk in Entra to see who is in it.", McpHarness.Text(team, "membership"));
        Assert.Single(Js.Items(team, "waiting"));
        Assert.Equal(2, Js.Items(team, "applications").Count);

        // "Member of" cannot be answered without the directory, and the answer says why instead of an empty list alone.
        JsonTreeNode memberOf = h.Ok(McpHarness.TokenReader, "list_teams", "{\"memberObjectId\":\"" + McpHarness.Owner + "\"}");
        Assert.Empty(Js.Items(McpHarness.Data(memberOf), "items"));
        Assert.Contains("does not read group members", McpHarness.Text(memberOf, "summary"), StringComparison.Ordinal);

        // With directory reads on but Entra not answering, the members are not listed, and the read counts what it could not read.
        McpHarness down = World();
        down.Directory.Down = true;
        JsonTreeNode failed = down.Ok(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + McpHarness.ChangeManagerGroup + "\"}");
        Assert.Equal("null", McpHarness.Text(McpHarness.Data(failed), "members"));
        Assert.StartsWith("Entra did not answer", McpHarness.Text(McpHarness.Data(failed), "membership"), StringComparison.Ordinal);
        Assert.Contains(Js.Items(failed, "couldNotRead"), c => Js.Text(c, "kind") == "team-members");
        // Without the members, the answers through the committee's role are listed, whoever gave them.
        Assert.Equal(2, Js.Items(McpHarness.Data(failed), "recentAnswers").Count);
    }

    [Fact]
    public void ToolPolicyRefusesACallerWithNoRoleAndARoleItsRowLeavesOut()
    {
        McpHarness h = World();
        h.Fails(McpHarness.TokenNoRole, "list_teams", "{}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenNoRole, "get_team", "{\"teamId\":\"" + SupportTeam + "\"}", McpErrorCodes.NotAuthorized);

        // Any role reads both, an AI client too, and a person in the web app.
        h.Ok(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + SupportTeam + "\"}");
        h.Ok(McpHarness.TokenReaderWeb, "list_teams", "{}", server: h.PersonServer());
        Assert.Equal("read-only", ContractSet.Embedded.Tools.Single(t => t.Name == "list_teams").AiWriteStates.Single());

        // A Tool Policy row in force that leaves the reader out refuses the reader and hides the tool from it.
        McpHarness tight = McpHarness.Create(PolicyTests.PolicyOperations((tool, values) => tool == "list_teams" || tool == "get_team"
            ? Js.With(values, ("allowedRoleKeys", Js.Strs(new[] { "platform-admin" })))
            : values));
        tight.Fails(McpHarness.TokenReader, "list_teams", "{}", McpErrorCodes.NotAuthorized);
        tight.Fails(McpHarness.TokenReader, "get_team", "{\"teamId\":\"" + McpHarness.ReaderGroup + "\"}", McpErrorCodes.NotAuthorized);
        tight.Ok(McpHarness.TokenAdmin, "list_teams", "{}");
        Assert.DoesNotContain(Js.Items(tight.Rpc(McpHarness.TokenReader, "tools/list", "{}").Result, "tools"), t => Js.Text(t, "name") == "list_teams");
    }

    // A directory that answers from fictional personas, as the Azure host's demo directory does: it marks itself as the
    // demo's (IDemoDirectory), reads no Entra and holds no token.
    private sealed class DemoPersonaDirectory : IDirectoryReader, IDemoDirectory
    {
        private readonly (string Id, string Name, string[] Groups)[] _people;

        public DemoPersonaDirectory(params (string Id, string Name, string[] Groups)[] people)
        {
            _people = people;
        }

        public DirectoryAnswer<IReadOnlyList<DirectoryObject>> GroupMembers(BackendCaller asPerson, string groupObjectId)
        {
            return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Of(_people.Where(p => p.Groups.Contains(groupObjectId, StringComparer.OrdinalIgnoreCase)).Select(p => new DirectoryObject(p.Id, p.Name)).ToList());
        }

        public DirectoryAnswer<DirectoryObject?> Group(BackendCaller asPerson, string groupObjectId)
        {
            return DirectoryAnswer<DirectoryObject?>.Of(_people.Any(p => p.Groups.Contains(groupObjectId, StringComparer.OrdinalIgnoreCase)) ? new DirectoryObject(groupObjectId, "Demo group", "group") : null);
        }

        public DirectoryAnswer<DirectoryUserState> UserState(BackendCaller asPerson, string objectId)
        {
            return DirectoryAnswer<DirectoryUserState>.Unavailable("the demo personas carry no account state");
        }

        public DirectoryAnswer<IReadOnlyList<DirectAssignment>> DirectAssignments(BackendCaller asPerson)
        {
            return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Of(Array.Empty<DirectAssignment>());
        }
    }
}
