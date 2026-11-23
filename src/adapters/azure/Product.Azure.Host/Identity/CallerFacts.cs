using System;
using System.Collections.Generic;
using System.Linq;
using Product.Azure.Stores;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Policy;

namespace Product.Azure.Host;

/// <summary>
/// Turns a mapped caller into the engine's caller identity and the store call context. The actor kind comes from the
/// entry point and the token type, never from a field the caller sends: a delegated token is an AI client acting for the
/// signed-in person on every route of this release, an app-only token is a workload (a pipeline, or the approval runner
/// when it holds the approval writer grant). Product roles come only from the configuration in force.
/// </summary>
public sealed class CallerFacts
{
    private readonly IRecordStore _records;
    private readonly IConfigurationStore _configuration;
    private readonly EngineRuntime _runtime;

    /// <summary>Creates the service.</summary>
    public CallerFacts(IRecordStore records, IConfigurationStore configuration, EngineRuntime runtime)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>
    /// The engine principal kind of a delegated caller: <see cref="PrincipalKind.AgentUser"/> or
    /// <see cref="PrincipalKind.AgentIdentity"/> when its Principal Reference (or, for a local development persona, the
    /// persona) says so, else <see cref="PrincipalKind.User"/>.
    /// </summary>
    public PrincipalKind DelegatedKindOf(CallerClaims? caller, string objectId)
    {
        if (caller != null && caller.IsDevIdentity && caller.DeclaredPrincipalKind != null
            && EngineContract.PrincipalKinds.TryParse(caller.DeclaredPrincipalKind, out PrincipalKind declared)
            && (declared == PrincipalKind.AgentUser || declared == PrincipalKind.AgentIdentity))
        {
            return declared;
        }

        PrincipalKind? stored = StoredKind(objectId);
        return stored == PrincipalKind.AgentUser || stored == PrincipalKind.AgentIdentity ? stored.Value : PrincipalKind.User;
    }

    /// <summary>
    /// The engine principal kind of an app-only caller: <see cref="PrincipalKind.ManagedIdentity"/> or
    /// <see cref="PrincipalKind.AgentIdentity"/> when its Principal Reference says so, else
    /// <see cref="PrincipalKind.ServicePrincipal"/>.
    /// </summary>
    public PrincipalKind WorkloadKindOf(string objectId)
    {
        PrincipalKind? stored = StoredKind(objectId);
        return stored == PrincipalKind.ManagedIdentity || stored == PrincipalKind.AgentIdentity ? stored.Value : PrincipalKind.ServicePrincipal;
    }

    /// <summary>The display label of a Principal Reference, or null.</summary>
    public string? DisplayLabel(string objectId)
    {
        return Principal(objectId)?.Text("displayLabel");
    }

    /// <summary>The principal kind a Principal Reference records for an Entra object ID, as the contract writes it, or null.</summary>
    public string? StoredPrincipalKind(string objectId)
    {
        return Principal(objectId)?.Text("principalKind");
    }

    /// <summary>The engine caller identity of a mapped caller on this release's entry points.</summary>
    public CallerIdentity Identity(CallerClaims caller, string? clientAppLabel = null)
    {
        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        if (caller.IsAppOnly)
        {
            return CallerIdentity.Workload(caller.ObjectId, WorkloadKindOf(caller.ObjectId), clientAppLabel ?? DisplayLabel(caller.ObjectId) ?? caller.ClientAppId);
        }

        PrincipalKind kind = DelegatedKindOf(caller, caller.ObjectId);
        string clientApp = clientAppLabel ?? caller.ClientAppId;
        return kind == PrincipalKind.User
            ? CallerIdentity.AiClient(caller.ObjectId, caller.GroupObjectIds, clientApp, clientAppVerified: true)
            : new CallerIdentity(caller.ObjectId, kind, CallerEntry.AiClient, caller.GroupObjectIds, clientApp, true);
    }

    /// <summary>
    /// The store call context for a request: the caller, the entry, the actor kind and roles the engine resolves from the
    /// configuration in force (none before version 1), the client app label and the correlation ID.
    /// </summary>
    public StoreCallContext CallContext(CallerClaims caller, string correlationId)
    {
        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        ActorKindMeaning? actorKind = null;
        IReadOnlyList<string> roles = Array.Empty<string>();
        string? onBehalfOf = null;
        string? clientLabel = null;
        CallerEntry entry = caller.IsAppOnly ? CallerEntry.Workload : CallerEntry.AiClient;
        try
        {
            SnapshotLoad load = _runtime.Cache.Load(_configuration, SnapshotPurpose.Read, Utc(_runtime.Clock.UtcNow));
            CompiledSnapshot? snapshot = load.Snapshot;
            if (snapshot != null)
            {
                if (!caller.IsAppOnly)
                {
                    KnownClientResult known = KnownClientCheck.From(snapshot.Snapshot).Check(caller.ClientAppId);
                    clientLabel = known.IsAllowed ? known.Label : null;
                }

                CallerResolution resolution = CallerResolver.Resolve(snapshot.Snapshot, Identity(caller, clientLabel), Utc(_runtime.Clock.UtcNow));
                if (resolution.Actor != null)
                {
                    actorKind = resolution.Actor.ActorKind;
                    roles = resolution.Actor.RoleKeys;
                    onBehalfOf = resolution.Actor.OnBehalfOfPrincipalId;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The context is best effort: a store that cannot answer here fails the request's own engine call, which
            // reports it. The context then carries the caller without an actor kind.
        }

        string? clientApp = caller.IsAppOnly ? DisplayLabelSafe(caller.ObjectId) ?? caller.ClientAppId : clientLabel ?? caller.ClientAppId;
        return new StoreCallContext(caller.ObjectId, entry, actorKind, correlationId, roles, onBehalfOf, string.IsNullOrEmpty(clientApp) ? null : clientApp, clientAppVerified: !string.IsNullOrEmpty(caller.ClientAppId));
    }

    private string? DisplayLabelSafe(string objectId)
    {
        try
        {
            return DisplayLabel(objectId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private PrincipalKind? StoredKind(string objectId)
    {
        string? text = Principal(objectId)?.Text("principalKind");
        return text != null && EngineContract.PrincipalKinds.TryParse(text, out PrincipalKind kind) ? kind : null;
    }

    private StoredRow? Principal(string objectId)
    {
        if (string.IsNullOrEmpty(objectId))
        {
            return null;
        }

        StoredRow? row = _records.Query(RowQuery.For("principal-reference").Where("entraObjectId", objectId)).FirstOrDefault();
        string lower = objectId.ToLowerInvariant();
        if (row == null && !string.Equals(lower, objectId, StringComparison.Ordinal))
        {
            row = _records.Query(RowQuery.For("principal-reference").Where("entraObjectId", lower)).FirstOrDefault();
        }

        return row;
    }

    internal static DateTime Utc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }
}
