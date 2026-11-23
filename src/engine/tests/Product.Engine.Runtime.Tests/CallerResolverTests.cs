using System;
using Product.Engine.Core;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// Roles from the configuration in force on every call (document 26, RA-01), workload roles only through a current grant
// (RA-07), and agent identities with no role at all (RA-06).
public sealed class CallerResolverTests
{
    private static readonly Lazy<Configuration.Snapshots.CompiledSnapshot> Version1 = new(() => Harness.Compile(1));

    [Fact]
    public void APersonHoldsTheRolesBoundToTheirGroups()
    {
        CallerResolution r = CallerResolver.Resolve(Version1.Value.Snapshot, Harness.PersonOne, Harness.AsOf);
        Assert.NotNull(r.Actor);
        Assert.Equal(ActorKindMeaning.Person, r.Actor!.ActorKind);
        Assert.Equal(new[] { "configuration-admin", "requester" }, r.Actor.RoleKeys);
        Assert.Null(r.Actor.OnBehalfOfPrincipalId);
        Assert.Equal(2, r.Trace.Count);
    }

    [Fact]
    public void APersonInNoBoundGroupHoldsNoRole()
    {
        CallerResolution r = CallerResolver.Resolve(Version1.Value.Snapshot, CallerIdentity.Person(Harness.Person3, new[] { "00000000-0000-4000-8000-000000000999" }), Harness.AsOf);
        Assert.Empty(r.Actor!.RoleKeys);
    }

    [Fact]
    public void AnAiClientActsWithThePersonsRolesAndIsStampedAsAnAiClient()
    {
        CallerResolution r = CallerResolver.Resolve(Version1.Value.Snapshot, Harness.AiClientOf1, Harness.AsOf);
        Assert.Equal(ActorKindMeaning.AiClientForPerson, r.Actor!.ActorKind);
        Assert.Equal(Harness.Person1, r.Actor.OnBehalfOfPrincipalId);
        Assert.Contains("configuration-admin", r.Actor.RoleKeys);
    }

    [Fact]
    public void AWorkloadHoldsOnlyItsCurrentGrantsRoleAndItsActorKindFollowsTheRole()
    {
        CallerResolution pipeline = CallerResolver.Resolve(Version1.Value.Snapshot, Harness.BuildPipeline, Harness.AsOf);
        Assert.Equal(ActorKindMeaning.Pipeline, pipeline.Actor!.ActorKind);
        Assert.Equal(new[] { EngineKeys.DeploymentRecorderRole }, pipeline.Actor.RoleKeys);

        CallerResolution runner = CallerResolver.Resolve(Version1.Value.Snapshot, Harness.ApprovalRunner, Harness.AsOf);
        Assert.Equal(ActorKindMeaning.ApprovalRunner, runner.Actor!.ActorKind);
        Assert.Equal(new[] { EngineKeys.ApprovalWriterRole }, runner.Actor.RoleKeys);
    }

    // From Expires On the grant admits no app-only token (document 26, section 3.4).
    [Fact]
    public void AnExpiredOrMissingGrantRefusesTheWorkload()
    {
        DateTime lastMoment = new DateTime(2027, 6, 29, 23, 59, 59, DateTimeKind.Utc);
        DateTime expiry = new DateTime(2027, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        Assert.NotNull(CallerResolver.Resolve(Version1.Value.Snapshot, Harness.BuildPipeline, lastMoment).Actor);

        CallerResolution expired = CallerResolver.Resolve(Version1.Value.Snapshot, Harness.BuildPipeline, expiry);
        Assert.Null(expired.Actor);
        Assert.Equal(ReasonCodes.ActorWorkloadGrantMissing, expired.Refusal!.Code);

        CallerResolution none = CallerResolver.Resolve(Version1.Value.Snapshot, Harness.ReleasePipeline, Harness.AsOf);
        Assert.Null(none.Actor);
        Assert.Equal(ReasonCodes.ActorWorkloadGrantMissing, none.Refusal!.Code);
    }

    [Fact]
    public void AnAgentIdentityHoldsNoRoleWhateverItsEntry()
    {
        CallerResolution workload = CallerResolver.Resolve(Version1.Value.Snapshot, CallerIdentity.Workload(Harness.AgentIdentity, PrincipalKind.AgentIdentity), Harness.AsOf);
        Assert.Equal(ActorKindMeaning.AgentIdentity, workload.Actor!.ActorKind);
        Assert.Empty(workload.Actor.RoleKeys);

        CallerResolution delegated = CallerResolver.Resolve(Version1.Value.Snapshot, new CallerIdentity(Harness.AgentIdentity, PrincipalKind.AgentUser, CallerEntry.AiClient, new[] { Harness.ConfigurationAdminGroup }), Harness.AsOf);
        Assert.Equal(ActorKindMeaning.AgentIdentity, delegated.Actor!.ActorKind);
        Assert.Empty(delegated.Actor.RoleKeys);
    }

    [Fact]
    public void BeforeVersionOneAPersonHasNoRoleAndAWorkloadIsRefused()
    {
        Assert.Empty(CallerResolver.ResolveWithoutConfiguration(Harness.PersonOne).Actor!.RoleKeys);
        Assert.Equal(ReasonCodes.ActorWorkloadGrantMissing, CallerResolver.ResolveWithoutConfiguration(Harness.BuildPipeline).Refusal!.Code);
    }

    [Fact]
    public void AnAppOnlyEntryMustBeAWorkloadPrincipal()
    {
        Assert.Throws<ArgumentException>(() => new CallerIdentity(Harness.Person1, PrincipalKind.User, CallerEntry.Workload));
        Assert.Throws<ArgumentException>(() => new CallerIdentity(Harness.Runner, PrincipalKind.ServicePrincipal, CallerEntry.AiClient));
    }
}
