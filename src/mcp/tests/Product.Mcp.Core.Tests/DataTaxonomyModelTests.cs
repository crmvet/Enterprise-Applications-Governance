using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Data taxonomy slice 1, the model (D-37, settled by D-41; docs/architecture/data-taxonomy.md). The configuration
// the model adds reaches the tools that read configuration, and an owner role held only on data domains is never offered
// or taken as an owner role of an application. The data tools themselves come with slice 3.
public sealed class DataTaxonomyModelTests
{
    // The data steward is held on data domains only (C28 Subject Kinds), so it is not an application's owner role. The data
    // owner is held on both, so an application may still name one, as before the data taxonomy.
    [Fact]
    public void AnOwnerRoleHeldOnlyOnDataDomainsIsNotAnApplicationsOwnerRole()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode refused = h.Fails(McpHarness.TokenOwner, "propose_owner_change", Change("data-steward"), McpErrorCodes.Validation);
        Assert.Contains("data-steward", Js.Write(refused));
        Assert.Contains("data-domain", Js.Write(refused));

        JsonTreeNode proposed = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "propose_owner_change", Change("data-owner")));
        Assert.False(string.IsNullOrEmpty(McpHarness.Text(proposed, "decisionId")));
    }

    // The owner coverage is per application, so it leaves out the roles an application cannot have.
    [Fact]
    public void TheOwnerCoverageListsOnlyTheRolesAnApplicationCanHave()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_owner_assignments", "{}"));
        string[] roles = Js.Items(data, "coverage").Select(c => McpHarness.Text(c, "ownerRoleKey")).ToArray();
        Assert.Contains("data-owner", roles);
        Assert.Contains("technical-owner", roles);
        Assert.DoesNotContain("data-steward", roles);
    }

    // get_configuration reads the four new configuration entities and the new rows of the old ones.
    [Fact]
    public void TheConfigurationReadServesTheDataTaxonomy()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode classes = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration", "{\"entity\":\"data-classification\"}"));
        Assert.Equal(new[] { "confidential", "internal", "public", "restricted" }, Js.Items(classes, "rows").Select(r => McpHarness.Text(r, "key")).OrderBy(k => k, System.StringComparer.Ordinal));

        JsonTreeNode levels = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration", "{\"entity\":\"data-domain-level\"}"));
        Assert.Equal(3, Js.Items(levels, "rows").Count);

        JsonTreeNode platforms = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration", "{\"entity\":\"data-platform\",\"top\":100}"));
        Assert.Equal(20, Js.Items(platforms, "rows").Count);

        JsonTreeNode setting = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration", "{\"entity\":\"setting\",\"keys\":[\"data-taxonomy-mode\"]}"));
        Assert.Contains("domains-optional", Js.Write(setting));

        JsonTreeNode roles = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration", "{\"entity\":\"role\",\"keys\":[\"data-governance-lead\"]}"));
        Assert.Single(Js.Items(roles, "rows"));
    }

    // The data governance lead (D-41, question 4) reads what any role reads, and holds the tool rights the Tool Policy gives
    // it: proposing an owner change, and proposing, submitting and withdrawing a decision.
    [Fact]
    public void TheDataGovernanceLeadIsAHumanRoleInTheToolPolicy()
    {
        ContractSet contracts = ContractSet.Embedded;
        Assert.Contains("data-governance-lead", contracts.HumanRoleKeys);
        Assert.Equal(15, contracts.HumanRoleKeys.Count);
    }

    private static string Change(string ownerRole)
    {
        return "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changes\":[{\"ownerRoleKey\":\"" + ownerRole + "\",\"addObjectId\":\"" + McpHarness.Deputy + "\"}],\"reason\":\"Name the owner of the data.\"}";
    }
}
