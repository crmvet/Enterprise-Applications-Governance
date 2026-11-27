using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Runtime.Callers;

// How a call reached the engine. The actor kind comes from the entry point and the token type, never from a field the
// caller sends (document 24, section 4.5; document 25, section 11.2).
public enum CallerEntry
{
    // The product's page or app, with a delegated user token: a person.
    Person,

    // The product's MCP endpoint with a delegated user token: an AI client acting for the signed-in person.
    AiClient,

    // An app-only token: a pipeline's workload identity or the approval runner (document 26, RA-07).
    Workload,

    // The engine's own scheduled work inside the host, with no token: the daily due-state refresh (document 23, S2).
    // Only the host builds such a caller, in process; no token and no request maps to it. It resolves
    // to the actor kind engine, holds no role, and every write it makes is an engine-nested write (document 22, section
    // 2.4: "The engine itself (flags, computed dates, reminders, derived links)").
    Engine,
}

// What the adapter knows about the caller before the engine resolves roles: the object ID and kind from the token or the
// execution context, the entry point, and the caller's Entra group memberships (group team membership on Dataverse,
// group claims on Azure). It never carries a role: the engine resolves roles itself (document 26, RA-01).
public sealed class CallerIdentity
{
    public CallerIdentity(
        string principalId,
        PrincipalKind principalKind,
        CallerEntry entry,
        IEnumerable<string>? groupObjectIds = null,
        string? clientApp = null,
        bool clientAppVerified = false)
    {
        if (string.IsNullOrEmpty(principalId))
        {
            throw new ArgumentException("A caller has an object ID.", nameof(principalId));
        }

        bool workloadKind = principalKind == PrincipalKind.ServicePrincipal || principalKind == PrincipalKind.ManagedIdentity;
        if (entry == CallerEntry.Workload && !workloadKind && principalKind != PrincipalKind.AgentIdentity)
        {
            throw new ArgumentException("An app-only token comes from a service principal, a managed identity or an agent identity.", nameof(principalKind));
        }

        if (entry == CallerEntry.Engine && !workloadKind)
        {
            throw new ArgumentException("The engine's own caller is the host's service principal or managed identity.", nameof(principalKind));
        }

        if (entry != CallerEntry.Workload && entry != CallerEntry.Engine && (workloadKind || principalKind == PrincipalKind.Group))
        {
            throw new ArgumentException("A delegated token names a user, an agent user or an agent identity.", nameof(principalKind));
        }

        PrincipalId = principalId;
        PrincipalKind = principalKind;
        Entry = entry;
        GroupObjectIds = new ReadOnlyCollection<string>((groupObjectIds ?? Enumerable.Empty<string>()).Where(g => !string.IsNullOrEmpty(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        ClientApp = string.IsNullOrEmpty(clientApp) ? null : clientApp;
        ClientAppVerified = clientAppVerified;
    }

    public string PrincipalId { get; }

    public PrincipalKind PrincipalKind { get; }

    public CallerEntry Entry { get; }

    public IReadOnlyList<string> GroupObjectIds { get; }

    // The client application, such as "Claude Code". On the MCP route it comes from the token's client claim.
    public string? ClientApp { get; }

    // True only when ClientApp came from a validated token claim (document 22, section 2.4, Client App Verified).
    public bool ClientAppVerified { get; }

    public static CallerIdentity Person(string principalId, IEnumerable<string>? groups = null, string? clientApp = null)
    {
        return new CallerIdentity(principalId, PrincipalKind.User, CallerEntry.Person, groups, clientApp);
    }

    public static CallerIdentity AiClient(string principalId, IEnumerable<string>? groups = null, string? clientApp = null, bool clientAppVerified = false)
    {
        return new CallerIdentity(principalId, PrincipalKind.User, CallerEntry.AiClient, groups, clientApp, clientAppVerified);
    }

    public static CallerIdentity Workload(string principalId, PrincipalKind kind = PrincipalKind.ServicePrincipal, string? clientApp = null)
    {
        return new CallerIdentity(principalId, kind, CallerEntry.Workload, null, clientApp);
    }

    // The engine's own scheduled work (the due-state refresh), as the host runs it in process. principalId defaults to
    // EngineKeys.EnginePrincipalId, the engine's fixed identity in stamps and audit entries.
    public static CallerIdentity Engine(string? principalId = null, string? clientApp = null)
    {
        return new CallerIdentity(string.IsNullOrEmpty(principalId) ? EngineKeys.EnginePrincipalId : principalId!, PrincipalKind.ManagedIdentity, CallerEntry.Engine, null, clientApp ?? EngineKeys.EngineClientApp);
    }
}

// The resolved actor, or the refusal of an app-only caller with no current grant.
public sealed class CallerResolution
{
    internal CallerResolution(ActorInfo? actor, Reason? refusal, IEnumerable<RuleTrace> trace)
    {
        Actor = actor;
        Refusal = refusal;
        Trace = new ReadOnlyCollection<RuleTrace>(trace.ToList());
    }

    public ActorInfo? Actor { get; }

    public Reason? Refusal { get; }

    // The Role Group Binding and Workload Grant rows that gave the roles.
    public IReadOnlyList<RuleTrace> Trace { get; }

    public bool IsResolved => Actor != null;
}

// Resolves the caller's product roles from the configuration in force on every call (document 26, RA-01: the engine
// never trusts a role named in the request).
//
// - A person, or an AI client acting for that person, holds the human roles of the active C30 Role Group Bindings whose
//   group the person is in (D-11). The AI client acts with the person's roles and is stamped as an AI client.
// - An app-only caller holds only the workload roles of its current C42 Workload Grants (RA-07). With none, it is
//   refused (actor.workload-grant-missing). A grant is current until its Expires On day starts (UTC): "from Expires On
//   the grant admits no app-only token" (document 26, section 3.4).
// - A workload holding approval-writer is the approval runner; any other workload is a pipeline (document 25, section
//   11.2: actor kind from the workload role).
// - An agent identity or agent user never holds a role or a grant (document 26, RA-06). It resolves to the actor kind
//   agent identity, and OP-08 refuses its writes.
//
// WORKING DEFAULT (open design question 1): approval-writer and deployment-recorder are fixed keys.
// WORKING DEFAULT (open design question 12): workload identities hold their role directly through a grant.
public static class CallerResolver
{
    public static CallerResolution Resolve(ConfigurationSnapshot snapshot, CallerIdentity caller, DateTime asOfUtc)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        if (caller.PrincipalKind == PrincipalKind.AgentIdentity || caller.PrincipalKind == PrincipalKind.AgentUser)
        {
            return Agent(caller);
        }

        if (caller.Entry == CallerEntry.Engine)
        {
            // The engine's own scheduled work: no role, no grant, the actor kind engine. Only the host builds this caller.
            return new CallerResolution(new ActorInfo(caller.PrincipalId, caller.PrincipalKind, ActorKindMeaning.Engine), null, Enumerable.Empty<RuleTrace>());
        }

        var trace = new List<RuleTrace>();
        if (caller.Entry == CallerEntry.Workload)
        {
            var roles = new SortedSet<string>(StringComparer.Ordinal);
            foreach (SnapshotRow grant in Active(snapshot, "workload-grant"))
            {
                string? role = Text(grant, "workloadRoleKey");
                if (!string.Equals(Text(grant, "principalObjectId"), caller.PrincipalId, StringComparison.OrdinalIgnoreCase) || role == null || !IsCurrent(grant, asOfUtc))
                {
                    continue;
                }

                if (snapshot.TryGetRow("role", role, out SnapshotRow? roleRow) && roleRow.IsActive && Flag(roleRow, "isWorkloadRole"))
                {
                    roles.Add(role);
                    trace.Add(new RuleTrace("Workload Grant", grant.KeyPath, RuleTraceRole.Matched));
                }
            }

            if (roles.Count == 0)
            {
                return new CallerResolution(
                    null,
                    new Reason(
                        ReasonCodes.ActorWorkloadGrantMissing,
                        ReasonSeverity.Error,
                        "The app-only caller holds no current workload grant in the configuration in force, so it is refused. Record or renew its Workload Grant through a configuration change.",
                        "Workload Grant",
                        null),
                    trace);
            }

            ActorKindMeaning kind = roles.Contains(EngineKeys.ApprovalWriterRole) ? ActorKindMeaning.ApprovalRunner : ActorKindMeaning.Pipeline;
            return new CallerResolution(new ActorInfo(caller.PrincipalId, caller.PrincipalKind, kind, roles), null, trace);
        }

        var human = new SortedSet<string>(StringComparer.Ordinal);
        foreach (SnapshotRow binding in Active(snapshot, "role-group-binding"))
        {
            string? group = Text(binding, "entraGroupObjectId");
            string? role = Text(binding, "roleKey");
            if (group == null || role == null || !caller.GroupObjectIds.Contains(group, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // A workload role is never bound to a group (V35); a binding that names one gives nothing.
            if (snapshot.TryGetRow("role", role, out SnapshotRow? roleRow) && roleRow.IsActive && !Flag(roleRow, "isWorkloadRole"))
            {
                human.Add(role);
                trace.Add(new RuleTrace("Role Group Binding", binding.KeyPath, RuleTraceRole.Matched));
            }
        }

        return new CallerResolution(Person(caller, human), null, trace);
    }

    // Before version 1 no role is bound and no grant exists (document 21, section 4.8, step 3). A person resolves with
    // no roles, which the bootstrap rule allows; an app-only caller is refused.
    public static CallerResolution ResolveWithoutConfiguration(CallerIdentity caller)
    {
        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        if (caller.PrincipalKind == PrincipalKind.AgentIdentity || caller.PrincipalKind == PrincipalKind.AgentUser)
        {
            return Agent(caller);
        }

        if (caller.Entry == CallerEntry.Workload || caller.Entry == CallerEntry.Engine)
        {
            return new CallerResolution(
                null,
                new Reason(ReasonCodes.ActorWorkloadGrantMissing, ReasonSeverity.Error, "No configuration version exists yet, so no workload grant exists and the app-only caller is refused.", "Workload Grant", null),
                Enumerable.Empty<RuleTrace>());
        }

        return new CallerResolution(Person(caller, Enumerable.Empty<string>()), null, Enumerable.Empty<RuleTrace>());
    }

    private static ActorInfo Person(CallerIdentity caller, IEnumerable<string> roles)
    {
        return caller.Entry == CallerEntry.AiClient
            ? new ActorInfo(caller.PrincipalId, caller.PrincipalKind, ActorKindMeaning.AiClientForPerson, roles, caller.PrincipalId)
            : new ActorInfo(caller.PrincipalId, caller.PrincipalKind, ActorKindMeaning.Person, roles);
    }

    private static CallerResolution Agent(CallerIdentity caller)
    {
        return new CallerResolution(new ActorInfo(caller.PrincipalId, caller.PrincipalKind, ActorKindMeaning.AgentIdentity), null, Enumerable.Empty<RuleTrace>());
    }

    private static bool IsCurrent(SnapshotRow grant, DateTime asOfUtc)
    {
        DateTime? expires = RuntimeJson.Utc(grant.Columns, "expiresOn");
        return expires.HasValue && asOfUtc < expires.Value;
    }

    private static IEnumerable<SnapshotRow> Active(ConfigurationSnapshot snapshot, string entity)
    {
        return snapshot.TryGetEntity(entity, out SnapshotEntity? rows) ? rows.ActiveRows : Enumerable.Empty<SnapshotRow>();
    }

    private static string? Text(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String && value.GetString().Length > 0 ? value.GetString() : null;
    }

    private static bool Flag(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
    }
}
