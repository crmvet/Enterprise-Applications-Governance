using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Xunit;

namespace Product.Engine.Core.Tests;

// The snapshot model of document 25, section 6.2. Fictional rows only.
public sealed class SnapshotTests
{
    private const string Hash = "8622543ce66b398c4922d3d34eb895c30ece337675c39bbfd0f0533c4d715065";

    private static readonly JsonTreeNode NoColumns = JsonTreeNode.CreateObject();

    private static SnapshotRow Row(string key, bool isActive = true, string? parentKey = null, string? meaning = null)
    {
        return new SnapshotRow(key, isActive, Origin.Shipped, NoColumns, meaning, parentKey);
    }

    [Theory]
    [InlineData("1.0", 1, 0, true)]
    [InlineData("1.7", 1, 7, true)]
    [InlineData("2.0", 2, 0, false)]
    [InlineData("0.9", 0, 9, false)]
    [InlineData("10.12", 10, 12, false)]
    public void SchemaVersionsParse(string text, int major, int minor, bool supported)
    {
        Assert.True(SnapshotSchemaVersion.TryParse(text, out SnapshotSchemaVersion? version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(supported, version.IsSupported);
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.0.0")]
    [InlineData("01.0")]
    [InlineData("1.00")]
    [InlineData("1.a")]
    [InlineData(" 1.0")]
    [InlineData("-1.0")]
    [InlineData("1.")]
    public void BadSchemaVersionsDoNotParse(string? text)
    {
        Assert.False(SnapshotSchemaVersion.TryParse(text, out _));
    }

    [Fact]
    public void RowsAreSortedByKeyPathAndFoundByIt()
    {
        var entity = new SnapshotEntity("tier-answer-option", new[]
        {
            Row("writes", parentKey: "data-change"),
            Row("reads-only", parentKey: "data-change"),
            Row("low", parentKey: "audience"),
        });

        Assert.Equal(new[] { "audience/low", "data-change/reads-only", "data-change/writes" }, entity.Rows.Select(row => row.KeyPath).ToArray());
        Assert.True(entity.TryGetRow("data-change/reads-only", out SnapshotRow? row));
        Assert.Equal("reads-only", row.Key);
        Assert.Equal("data-change", row.ParentKey);
        Assert.False(entity.TryGetRow("reads-only", out _));
    }

    [Fact]
    public void ActiveRowsLeaveOutRetiredRows()
    {
        var entity = new SnapshotEntity("tier", new[] { Row("tier-1"), Row("tier-0", isActive: false), Row("tier-2") });

        Assert.Equal(new[] { "tier-1", "tier-2" }, entity.ActiveRows.Select(row => row.Key).ToArray());
        Assert.Equal(3, entity.Rows.Count);
    }

    [Fact]
    public void ARepeatedKeyIsABrokenSnapshot()
    {
        Assert.Throws<ArgumentException>(() => new SnapshotEntity("tier", new[] { Row("tier-1"), Row("tier-1", isActive: false) }));
    }

    [Fact]
    public void RowsCheckTheirParts()
    {
        Assert.Throws<ArgumentException>(() => Row("Tier 1"));
        Assert.Throws<ArgumentException>(() => Row("tier-1", parentKey: "Parent"));
        Assert.Throws<ArgumentException>(() => Row("tier-1", meaning: "In progress"));
        Assert.Throws<ArgumentException>(() => new SnapshotRow("tier-1", true, Origin.Local, JsonTreeNode.CreateArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotRow("tier-1", true, (Origin)5, NoColumns));
    }

    [Fact]
    public void RowsExposeTheirColumns()
    {
        JsonTreeNode columns = CanonicalJson.Parse("{\"label\":\"Tier 2\",\"rank\":2}");
        var row = new SnapshotRow("tier-2", true, Origin.Local, columns, "live");

        Assert.True(row.TryGetColumn("rank", out JsonTreeNode? rank));
        Assert.True(rank.TryGetInt64(out long value));
        Assert.Equal(2, value);
        Assert.False(row.TryGetColumn("missing", out _));
        Assert.Equal("live", row.EngineMeaning);
        Assert.Equal(Origin.Local, row.Origin);
    }

    [Fact]
    public void ASnapshotIndexesItsEntities()
    {
        var snapshot = new ConfigurationSnapshot(
            new SnapshotSchemaVersion(1, 0),
            "working",
            7,
            Hash,
            new[]
            {
                new SnapshotEntity("tier", new[] { Row("tier-1"), Row("tier-2") }),
                new SnapshotEntity("gate-policy", new[] { Row("tier-1-normal-production") }),
            });

        Assert.Equal(new[] { "gate-policy", "tier" }, snapshot.Entities.Select(entity => entity.Name).ToArray());
        Assert.True(snapshot.TryGetRow("tier", "tier-2", out SnapshotRow? row));
        Assert.Equal("tier-2", row.Key);
        Assert.False(snapshot.TryGetRow("tier", "tier-9", out _));
        Assert.False(snapshot.TryGetRow("tier-question", "tier-1", out _));
        Assert.Equal(7, snapshot.VersionNumber);
        Assert.Equal("working", snapshot.ConfigurationSetKey);
    }

    [Fact]
    public void ASnapshotRefusesWhatTheEngineCannotRead()
    {
        SnapshotEntity[] none = Array.Empty<SnapshotEntity>();

        Assert.Throws<ArgumentException>(() => new ConfigurationSnapshot(new SnapshotSchemaVersion(2, 0), "working", 1, Hash, none));
        Assert.Throws<ArgumentException>(() => new ConfigurationSnapshot(new SnapshotSchemaVersion(1, 0), "working", 1, Hash.ToUpperInvariant(), none));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigurationSnapshot(new SnapshotSchemaVersion(1, 0), "working", 0, Hash, none));
        Assert.Throws<ArgumentException>(() => new ConfigurationSnapshot(new SnapshotSchemaVersion(1, 0), "Working Set", 1, Hash, none));
        Assert.Throws<ArgumentException>(() => new ConfigurationSnapshot(
            new SnapshotSchemaVersion(1, 0),
            "working",
            1,
            Hash,
            new[] { new SnapshotEntity("tier", new[] { Row("tier-1") }), new SnapshotEntity("tier", new[] { Row("tier-2") }) }));
    }
}
