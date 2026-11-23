using System;
using System.Linq;
using System.Reflection;
using Product.Engine.Core.Contract;
using Xunit;

namespace Product.Engine.Core.Tests;

// The operations catalog (document 25, section 5.2) and the engine's own facts.
public sealed class OperationCatalogTests
{
    [Fact]
    public void ThereAreFourteenOperationsInIdOrder()
    {
        Assert.Equal(14, OperationCatalog.All.Count);
        for (int i = 0; i < OperationCatalog.All.Count; i++)
        {
            OperationInfo info = OperationCatalog.All[i];
            Assert.Equal("OP-" + (i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture), info.Id);
            Assert.Same(info, OperationCatalog.Get(info.Operation));
            Assert.NotEmpty(info.Outcomes);
            Assert.All(info.Outcomes, outcome => Assert.True(WireFormat.IsKebabCase(outcome), outcome));
        }
    }

    [Fact]
    public void OnlyTheGuardIsAWrite()
    {
        Assert.Equal(new[] { EngineOperation.GuardWrite }, OperationCatalog.All.Where(info => info.IsWrite).Select(info => info.Operation).ToArray());
    }

    [Fact]
    public void OutcomesMatchTheDesign()
    {
        Assert.Equal(new[] { "required", "engine-accept" }, OperationCatalog.Get(EngineOperation.ResolveRequiredApprovals).Outcomes);
        Assert.Equal(EngineContract.ApprovalOveralls.WireValues, OperationCatalog.Get(EngineOperation.EvaluateApprovalStatus).Outcomes);
        Assert.Equal(EngineContract.GateVerdicts.WireValues, OperationCatalog.Get(EngineOperation.EvaluateGateDecision).Outcomes);
        Assert.Equal(new[] { "qualifies", "does-not-qualify" }, OperationCatalog.Get(EngineOperation.EvaluateStandardChange).Outcomes);
        Assert.Equal(new[] { "authorized", "not-authorized", "not-applicable" }, OperationCatalog.Get(EngineOperation.EvaluateLinkedAuthorization).Outcomes);
    }

    [Fact]
    public void ProposedOutcomesAreMarked()
    {
        EngineOperation[] proposed = OperationCatalog.All.Where(info => info.OutcomesProposed).Select(info => info.Operation).ToArray();

        Assert.Equal(
            new[] { EngineOperation.GuardWrite, EngineOperation.ValidateConfigurationSet, EngineOperation.WalkImpact, EngineOperation.ComputeDueDates },
            proposed);
    }

    [Fact]
    public void KnownOutcomesAreOrdinal()
    {
        OperationInfo tier = OperationCatalog.Get(EngineOperation.EvaluateTier);

        Assert.True(tier.IsKnownOutcome("computed"));
        Assert.False(tier.IsKnownOutcome("Computed"));
        Assert.False(tier.IsKnownOutcome("engine-accept"));
        Assert.False(tier.IsKnownOutcome(null));
    }

    [Fact]
    public void AnUnknownOperationIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OperationCatalog.Get((EngineOperation)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OperationCatalog.Get((EngineOperation)15));
    }

    [Fact]
    public void EngineVersionMatchesTheAssemblyVersion()
    {
        // EngineInfo.Version must stay equal to VersionPrefix in Directory.Build.props.
        Version? assemblyVersion = typeof(EngineInfo).Assembly.GetName().Version;

        Assert.NotNull(assemblyVersion);
        Assert.Equal(EngineInfo.Version, assemblyVersion!.ToString(3));
    }

    [Fact]
    public void TheEngineReadsSnapshotSchemaOne()
    {
        Assert.Equal(1, EngineInfo.SupportedSnapshotSchemaMajor);
    }

    [Fact]
    public void CoreTypesAreImmutable()
    {
        // Document 25, section 4.2: immutable classes with get-only properties. No public setters anywhere in the core.
        PropertyInfo[] settable = typeof(EngineInfo).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(property => property.SetMethod != null && property.SetMethod.IsPublic)
            .ToArray();

        Assert.Empty(settable.Select(property => property.DeclaringType!.Name + "." + property.Name));
    }
}
