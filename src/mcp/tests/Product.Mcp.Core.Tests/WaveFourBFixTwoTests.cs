using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Wave 4b, fix round 2 (document 24, B24-25 records each choice, D-41): a configuration change names a role group binding
// by its role, and says "Keep" for a row proposed again as it is, never "Change the role group binding reader group".
public sealed class WaveFourBFixTwoTests
{
    // Passing: the binding proposed again as it is reads "Keep the group binding of <role> as it is.", and the decision
    // statement says the same in lower case.
    [Fact]
    public void ABindingProposedAsItIsIsNamedByItsRoleAndKept()
    {
        McpHarness h = McpHarness.Create();
        ConfigurationSnapshot inForce = InForce(h);
        SnapshotRow binding = Binding(inForce, "reader");
        string role = Js.Text(inForce.TryGetEntity("role", out SnapshotEntity? roles) ? roles!.ActiveRows.First(r => r.KeyPath == "reader").Columns : null, "label")!;

        IReadOnlyList<string> lines = ChangeWords.Describe(inForce, Change(binding.KeyPath, binding.Columns));
        string line = Assert.Single(lines);
        Assert.Equal("Keep the group binding of " + role + " as it is.", line);
        Assert.DoesNotContain("role group binding", line);
    }

    // The other side: a field that differs is named, with the binding still named by its role, and a binding that is
    // added says "Add the group binding of <role>".
    [Fact]
    public void ABindingWithAChangedFieldNamesTheFieldAndAnAddedOneSaysAdd()
    {
        McpHarness h = McpHarness.Create();
        ConfigurationSnapshot inForce = InForce(h);
        SnapshotRow binding = Binding(inForce, "reader");
        string role = Js.Text(inForce.TryGetEntity("role", out SnapshotEntity? roles) ? roles!.ActiveRows.First(r => r.KeyPath == "reader").Columns : null, "label")!;
        JsonTreeNode changed = CanonicalJson.Parse("{\"roleKey\":\"reader\",\"bindingDetail\":\"App role reader for contractors\"}");

        string line = Assert.Single(ChangeWords.Describe(inForce, Change(binding.KeyPath, changed)));
        Assert.Equal("The binding detail of the group binding of " + role + " changes from App role reader to App role reader for contractors.", line);

        string added = Assert.Single(ChangeWords.Describe(inForce, Change("reader-contractors", CanonicalJson.Parse("{\"label\":\"Reader contractors\",\"roleKey\":\"reader\"}"))));
        Assert.Equal("Add the group binding of " + role + ".", added);
    }

    private static ConfigurationSnapshot InForce(McpHarness h)
    {
        return h.Runtime.Cache.Load(h.Store, SnapshotPurpose.Read, h.Clock.UtcNow).Snapshot!.Snapshot;
    }

    private static SnapshotRow Binding(ConfigurationSnapshot inForce, string roleKey)
    {
        Assert.True(inForce.TryGetEntity("role-group-binding", out SnapshotEntity? rows));
        return rows!.ActiveRows.First(r => Js.Text(r.Columns, "roleKey") == roleKey);
    }

    private static JsonTreeNode Change(string key, JsonTreeNode values)
    {
        return CanonicalJson.Parse("{\"operations\":[{\"op\":\"upsert\",\"entity\":\"role-group-binding\",\"key\":\"" + key + "\",\"values\":"
            + CanonicalJson.Write(values, CanonicalProfile.Request) + "}]}");
    }
}
