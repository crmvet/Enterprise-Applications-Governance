using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One Dependency row (O04), fetched by the adapter: an application depends on exactly one application or component.
public sealed class DependencyEdge
{
    public DependencyEdge(string fromId, string? toApplicationId, string? toComponentId, string dependencyTypeKey)
    {
        if (string.IsNullOrEmpty(fromId))
        {
            throw new ArgumentException("A dependency names the application that depends.", nameof(fromId));
        }

        if (string.IsNullOrEmpty(toApplicationId) == string.IsNullOrEmpty(toComponentId))
        {
            throw new ArgumentException("A dependency points to exactly one of an application and a component (O04).", nameof(toApplicationId));
        }

        if (!ConfigurationKey.IsValid(dependencyTypeKey))
        {
            throw new ArgumentException("A dependency type key must use the configuration key format.", nameof(dependencyTypeKey));
        }

        FromId = fromId;
        ToApplicationId = string.IsNullOrEmpty(toApplicationId) ? null : toApplicationId;
        ToComponentId = string.IsNullOrEmpty(toComponentId) ? null : toComponentId;
        DependencyTypeKey = dependencyTypeKey;
    }

    // The application that depends (O04 From Application).
    public string FromId { get; }

    public string? ToApplicationId { get; }

    public string? ToComponentId { get; }

    public string DependencyTypeKey { get; }

    internal ImpactNodeId From => ImpactNodeId.Application(FromId);

    internal ImpactNodeId To => ToApplicationId != null ? ImpactNodeId.Application(ToApplicationId) : ImpactNodeId.Component(ToComponentId!);
}

// OP-10 input (document 25, section 5.12).
public sealed class ImpactRequest
{
    public ImpactRequest(string? startApplicationId, string? startComponentId, ImpactDirection direction, int maxDepth, IEnumerable<DependencyEdge> edges)
    {
        if (string.IsNullOrEmpty(startApplicationId) == string.IsNullOrEmpty(startComponentId))
        {
            throw new ArgumentException("The walk starts from exactly one of an application and a component.", nameof(startApplicationId));
        }

        if (!Enum.IsDefined(typeof(ImpactDirection), direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (maxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "A walk takes at least one hop.");
        }

        StartApplicationId = string.IsNullOrEmpty(startApplicationId) ? null : startApplicationId;
        StartComponentId = string.IsNullOrEmpty(startComponentId) ? null : startComponentId;
        Direction = direction;
        MaxDepth = maxDepth;
        Edges = new ReadOnlyCollection<DependencyEdge>((edges ?? throw new ArgumentNullException(nameof(edges))).ToList());
    }

    // The application to start from.
    public string? StartApplicationId { get; }

    // The component to start from, instead of an application. A proposal of this build that follows document 24,
    // tool 7 (get_dependency_impact takes one of an application and a component). A component depends on nothing,
    // so only its dependants can be walked.
    public string? StartComponentId { get; }

    // Dependants (who depends on the start), dependencies (what the start depends on), or both, as two walks.
    public ImpactDirection Direction { get; }

    // Hops to walk; capped by the impact-max-depth setting.
    public int MaxDepth { get; }

    // The Dependency rows (O04), fetched by the adapter.
    public IReadOnlyList<DependencyEdge> Edges { get; }

    internal ImpactNodeId Start => StartApplicationId != null ? ImpactNodeId.Application(StartApplicationId) : ImpactNodeId.Component(StartComponentId!);

    public static ImpactRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "startApplicationId", "startComponentId", "direction", "maxDepth", "edges");
        string? application = wire.OptionalString("startApplicationId");
        string? component = wire.OptionalString("startComponentId");
        if ((application == null) == (component == null))
        {
            throw OperationStop.Input(
                application == null ? ReasonCodes.InputRequired : ReasonCodes.InputMalformed,
                "input needs exactly one of startApplicationId and startComponentId.");
        }

        ImpactDirection direction = wire.Contract("direction", EngineContract.ImpactDirections);
        int maxDepth = wire.OptionalInt("maxDepth") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "input.maxDepth is required.");
        if (maxDepth < 1)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, "input.maxDepth must be at least 1.");
        }

        var edges = new List<DependencyEdge>();
        foreach (WireObject edge in wire.Objects("edges", required: false, "fromId", "toApplicationId", "toComponentId", "dependencyTypeKey"))
        {
            string from = edge.RequiredString("fromId");
            string? toApplication = edge.OptionalString("toApplicationId");
            string? toComponent = edge.OptionalString("toComponentId");
            if ((toApplication == null) == (toComponent == null))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, edge.Where + " must name exactly one of toApplicationId and toComponentId (O04).");
            }

            edges.Add(new DependencyEdge(from, toApplication, toComponent, edge.RequiredKey("dependencyTypeKey")));
        }

        return new ImpactRequest(application, component, direction, maxDepth, edges);
    }
}

// One application or component the walk reached.
public sealed class ImpactNode
{
    public ImpactNode(string? applicationId, string? componentId, ImpactDirection direction, int depth, IEnumerable<string> dependencyTypeKeys, IEnumerable<string> path)
    {
        ApplicationId = applicationId;
        ComponentId = componentId;
        Direction = direction;
        Depth = depth;
        DependencyTypeKeys = new ReadOnlyCollection<string>(dependencyTypeKeys.ToList());
        Path = new ReadOnlyCollection<string>(path.ToList());
    }

    public string? ApplicationId { get; }

    // Set instead of ApplicationId for a component, which the dependencies walk reaches as a leaf.
    public string? ComponentId { get; }

    // The walk that reached it: dependants or dependencies (a proposal; lets one call draw both sides of a map).
    public ImpactDirection Direction { get; }

    // Hops from the start, at least 1. The shortest way there.
    public int Depth { get; }

    // The counted dependency type keys on the path, distinct and sorted.
    public IReadOnlyList<string> DependencyTypeKeys { get; }

    // The IDs from the start to this node, both included.
    public IReadOnlyList<string> Path { get; }

    internal string Id => ApplicationId ?? ComponentId!;

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("applicationId", ApplicationId)
            .Add("componentId", ComponentId)
            .Add("direction", EngineContract.ImpactDirections.ToWire(Direction))
            .Add("depth", Depth)
            .Add("dependencyTypeKeys", DependencyTypeKeys)
            .Add("path", Path)
            .Build();
    }
}

// OP-10 output (document 25, section 5.12).
public sealed class ImpactResult : IOperationPayload
{
    public ImpactResult(string? startApplicationId, string? startComponentId, ImpactDirection direction, int maxDepthApplied, IEnumerable<ImpactNode> reached, bool cycleDetected, bool truncatedAtDepth)
    {
        StartApplicationId = startApplicationId;
        StartComponentId = startComponentId;
        Direction = direction;
        MaxDepthApplied = maxDepthApplied;
        Reached = new ReadOnlyCollection<ImpactNode>(reached.ToList());
        CycleDetected = cycleDetected;
        TruncatedAtDepth = truncatedAtDepth;
    }

    public string? StartApplicationId { get; }

    public string? StartComponentId { get; }

    public ImpactDirection Direction { get; }

    // The requested depth, or the impact-max-depth setting when that is lower.
    public int MaxDepthApplied { get; }

    // Sorted by depth, then ID, then direction. The start itself is never listed.
    public IReadOnlyList<ImpactNode> Reached { get; }

    // True when the reached applications and the start depend on each other in a loop.
    public bool CycleDetected { get; }

    // True when the walk stopped at MaxDepthApplied with more to reach (document 24, tool 7 truncatedAtDepth).
    public bool TruncatedAtDepth { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("startApplicationId", StartApplicationId)
            .Add("startComponentId", StartComponentId)
            .Add("direction", EngineContract.ImpactDirections.ToWire(Direction))
            .Add("maxDepthApplied", MaxDepthApplied)
            .Add("reached", WireWriter.Array(Reached.Select(node => node.ToJson())))
            .Add("cycleDetected", CycleDetected)
            .Add("truncatedAtDepth", TruncatedAtDepth)
            .Build();
    }
}

// A node of the dependency graph: an application or a component, by ID. Applications and components are different
// tables, so the kind is part of the identity.
internal readonly struct ImpactNodeId : IEquatable<ImpactNodeId>
{
    private ImpactNodeId(string id, bool isComponent)
    {
        Id = id;
        IsComponent = isComponent;
    }

    public string Id { get; }

    public bool IsComponent { get; }

    public static ImpactNodeId Application(string id) => new(id, false);

    public static ImpactNodeId Component(string id) => new(id, true);

    public bool Equals(ImpactNodeId other) => IsComponent == other.IsComponent && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ImpactNodeId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id) * 2 + (IsComponent ? 1 : 0);

    // Ordinal by ID, applications before components: the order every list and every tie uses.
    public static int Compare(ImpactNodeId a, ImpactNodeId b)
    {
        int byId = string.CompareOrdinal(a.Id, b.Id);
        return byId != 0 ? byId : a.IsComponent.CompareTo(b.IsComponent);
    }
}

// OP-10 Walk impact ("what breaks", D-01; document 25, section 5.12).
// Only dependency types with Counts For Impact yes are followed. The walk goes breadth first, so each node is listed
// once per direction at its shortest depth; ties are broken by ID, never by input order. Both directions are two
// separate walks: the dependants of the start, and what the start depends on. One call, because the walk is recursive
// and agents do better with one tool (design brief, section 5).
public static class ImpactOperation
{
    private const string DependencyType = "dependency-type";

    public static EvaluationResult<ImpactResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, ImpactRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, request));
    }

    public static EvaluationResult<ImpactResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, ImpactRequest.FromJson(input)));
    }

    private static OperationAnswer<ImpactResult> Answer(ConfigurationSnapshot snapshot, ImpactRequest request)
    {
        var trace = new List<RuleTrace>();
        int cap = MaxDepthSetting(snapshot);
        trace.Add(new RuleTrace("Setting", EngineKeys.ImpactMaxDepth, RuleTraceRole.Matched));
        int depth = Math.Min(request.MaxDepth, cap);

        // Which edges count. A retired dependency type keeps its flag: its Dependency rows still exist, and the walk
        // asks what breaks, so it follows them (a proposal of this build).
        var counts = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        for (int i = 0; i < request.Edges.Count; i++)
        {
            string key = request.Edges[i].DependencyTypeKey;
            if (!counts.ContainsKey(key))
            {
                SnapshotRow type = SnapshotRead.RequireExisting(snapshot, DependencyType, key, "input.edges[" + i.ToString(CultureInfo.InvariantCulture) + "].dependencyTypeKey");
                counts[key] = SnapshotRead.Flag(type, "countsForImpact");
            }
        }

        foreach (KeyValuePair<string, bool> pair in counts)
        {
            trace.Add(new RuleTrace("Dependency Type", pair.Key, pair.Value ? RuleTraceRole.Matched : RuleTraceRole.Skipped));
        }

        List<DependencyEdge> counted = request.Edges.Where(edge => counts[edge.DependencyTypeKey]).ToList();
        var reached = new List<ImpactNode>();
        bool truncated = false;
        var cycleNodes = new List<ImpactNodeId>();
        foreach (ImpactDirection walk in Walks(request.Direction))
        {
            Walk result = Run(request.Start, walk, depth, counted);
            reached.AddRange(result.Reached);
            truncated |= result.Truncated;
            cycleNodes.AddRange(result.CycleNodes);
        }

        reached = reached
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ThenBy(node => node.ComponentId != null)
            .ThenBy(node => (int)node.Direction)
            .ToList();

        var reasons = new List<Reason>();
        if (cycleNodes.Count > 0)
        {
            ImpactNodeId first = cycleNodes.OrderBy(node => node, Comparer<ImpactNodeId>.Create(ImpactNodeId.Compare)).First();
            reasons.Add(new Reason(
                ReasonCodes.ImpactCycleDetected,
                ReasonCatalog.Get(ReasonCodes.ImpactCycleDetected).DefaultSeverity,
                "The dependency walk met application " + SnapshotRead.Quote(first.Id) + " twice, because the applications depend on each other in a loop.",
                "Dependency",
                first.Id));
        }

        if (truncated)
        {
            string why = depth < request.MaxDepth ? "the impact-max-depth setting caps the " + request.MaxDepth.ToString(CultureInfo.InvariantCulture) + " hops asked for" : "that is the depth asked for";
            reasons.Add(new Reason(
                ReasonCodes.ImpactDepthCapped,
                ReasonCatalog.Get(ReasonCodes.ImpactDepthCapped).DefaultSeverity,
                "The dependency walk stopped at depth " + depth.ToString(CultureInfo.InvariantCulture) + " with more to reach, because " + why + ".",
                depth < request.MaxDepth ? "Setting" : null,
                depth < request.MaxDepth ? EngineKeys.ImpactMaxDepth : null));
        }

        var payload = new ImpactResult(request.StartApplicationId, request.StartComponentId, request.Direction, depth, reached, cycleNodes.Count > 0, truncated);
        return new OperationAnswer<ImpactResult>("walked", payload, reasons, trace);
    }

    private static IEnumerable<ImpactDirection> Walks(ImpactDirection direction)
    {
        if (direction != ImpactDirection.Dependencies)
        {
            yield return ImpactDirection.Dependants;
        }

        if (direction != ImpactDirection.Dependants)
        {
            yield return ImpactDirection.Dependencies;
        }
    }

    // The impact-max-depth setting: a whole number of at least 1 (document 21, section 3.6).
    private static int MaxDepthSetting(ConfigurationSnapshot snapshot)
    {
        JsonTreeNode value = SnapshotRead.RequireSetting(
            snapshot,
            EngineKeys.ImpactMaxDepth,
            node => node.TryGetInt64(out long number) && number >= 1 && number <= int.MaxValue,
            "a whole number of at least 1");
        value.TryGetInt64(out long cap);
        return (int)cap;
    }

    // One breadth-first walk in one direction. Dependants follow edges backwards (who points at this node);
    // dependencies follow them forwards (what this node points at).
    private static Walk Run(ImpactNodeId start, ImpactDirection direction, int maxDepth, List<DependencyEdge> edges)
    {
        var next = new Dictionary<ImpactNodeId, SortedDictionary<ImpactNodeId, SortedSet<string>>>();
        foreach (DependencyEdge edge in edges)
        {
            ImpactNodeId from = direction == ImpactDirection.Dependants ? edge.To : edge.From;
            ImpactNodeId to = direction == ImpactDirection.Dependants ? edge.From : edge.To;
            if (!next.TryGetValue(from, out SortedDictionary<ImpactNodeId, SortedSet<string>>? targets))
            {
                targets = new SortedDictionary<ImpactNodeId, SortedSet<string>>(Comparer<ImpactNodeId>.Create(ImpactNodeId.Compare));
                next[from] = targets;
            }

            if (!targets.TryGetValue(to, out SortedSet<string>? types))
            {
                types = new SortedSet<string>(StringComparer.Ordinal);
                targets[to] = types;
            }

            types.Add(edge.DependencyTypeKey);
        }

        var seen = new Dictionary<ImpactNodeId, Visit> { { start, new Visit(0, null, Array.Empty<string>()) } };
        var order = new List<ImpactNodeId>();
        var frontier = new List<ImpactNodeId> { start };
        bool truncated = false;
        for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var reachedNow = new List<ImpactNodeId>();
            foreach (ImpactNodeId node in frontier)
            {
                if (!next.TryGetValue(node, out SortedDictionary<ImpactNodeId, SortedSet<string>>? targets))
                {
                    continue;
                }

                foreach (KeyValuePair<ImpactNodeId, SortedSet<string>> target in targets)
                {
                    if (!seen.ContainsKey(target.Key))
                    {
                        seen[target.Key] = new Visit(depth + 1, node, target.Value.ToArray());
                        reachedNow.Add(target.Key);
                    }
                }
            }

            reachedNow.Sort(ImpactNodeId.Compare);
            order.AddRange(reachedNow);
            frontier = reachedNow;
        }

        // More to reach beyond the last depth walked: a node there points at a node the walk has not seen.
        foreach (ImpactNodeId node in frontier)
        {
            if (next.TryGetValue(node, out SortedDictionary<ImpactNodeId, SortedSet<string>>? targets) && targets.Keys.Any(target => !seen.ContainsKey(target)))
            {
                truncated = true;
                break;
            }
        }

        var reached = new List<ImpactNode>();
        foreach (ImpactNodeId node in order)
        {
            var path = new List<string>();
            var types = new SortedSet<string>(StringComparer.Ordinal);
            ImpactNodeId? step = node;
            while (step.HasValue)
            {
                Visit visit = seen[step.Value];
                path.Add(step.Value.Id);
                types.UnionWith(visit.HopTypes);
                step = visit.Parent;
            }

            path.Reverse();
            reached.Add(new ImpactNode(node.IsComponent ? null : node.Id, node.IsComponent ? node.Id : null, direction, seen[node].Depth, types, path));
        }

        return new Walk(reached, truncated, CycleNodes(seen.Keys, next));
    }

    // The nodes on a loop among the nodes the walk saw (the start included), following only the counted edges between
    // them. A diamond, where two paths meet again, is no loop. Depth first, with the usual three colors.
    private static List<ImpactNodeId> CycleNodes(IEnumerable<ImpactNodeId> nodes, Dictionary<ImpactNodeId, SortedDictionary<ImpactNodeId, SortedSet<string>>> next)
    {
        var inSet = new HashSet<ImpactNodeId>(nodes);
        var colour = new Dictionary<ImpactNodeId, int>();
        var onLoop = new List<ImpactNodeId>();
        foreach (ImpactNodeId root in inSet.OrderBy(node => node, Comparer<ImpactNodeId>.Create(ImpactNodeId.Compare)))
        {
            if (colour.ContainsKey(root))
            {
                continue;
            }

            var stack = new List<KeyValuePair<ImpactNodeId, IEnumerator<ImpactNodeId>>>();
            colour[root] = 1;
            stack.Add(new KeyValuePair<ImpactNodeId, IEnumerator<ImpactNodeId>>(root, Targets(root, next, inSet).GetEnumerator()));
            while (stack.Count > 0)
            {
                KeyValuePair<ImpactNodeId, IEnumerator<ImpactNodeId>> top = stack[stack.Count - 1];
                if (!top.Value.MoveNext())
                {
                    colour[top.Key] = 2;
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                ImpactNodeId target = top.Value.Current;
                if (!colour.TryGetValue(target, out int state))
                {
                    colour[target] = 1;
                    stack.Add(new KeyValuePair<ImpactNodeId, IEnumerator<ImpactNodeId>>(target, Targets(target, next, inSet).GetEnumerator()));
                }
                else if (state == 1)
                {
                    // A back edge: every node on the stack from the target up is on the loop.
                    int from = stack.FindIndex(entry => entry.Key.Equals(target));
                    for (int i = from; i < stack.Count; i++)
                    {
                        onLoop.Add(stack[i].Key);
                    }
                }
            }
        }

        return onLoop.Distinct().ToList();
    }

    private static IEnumerable<ImpactNodeId> Targets(ImpactNodeId node, Dictionary<ImpactNodeId, SortedDictionary<ImpactNodeId, SortedSet<string>>> next, HashSet<ImpactNodeId> inSet)
    {
        return next.TryGetValue(node, out SortedDictionary<ImpactNodeId, SortedSet<string>>? targets)
            ? targets.Keys.Where(inSet.Contains).ToList()
            : new List<ImpactNodeId>();
    }

    private sealed class Visit
    {
        public Visit(int depth, ImpactNodeId? parent, string[] hopTypes)
        {
            Depth = depth;
            Parent = parent;
            HopTypes = hopTypes;
        }

        public int Depth { get; }

        public ImpactNodeId? Parent { get; }

        // The counted types of the edges from the parent to this node.
        public string[] HopTypes { get; }
    }

    private sealed class Walk
    {
        public Walk(List<ImpactNode> reached, bool truncated, List<ImpactNodeId> cycleNodes)
        {
            Reached = reached;
            Truncated = truncated;
            CycleNodes = cycleNodes;
        }

        public List<ImpactNode> Reached { get; }

        public bool Truncated { get; }

        public List<ImpactNodeId> CycleNodes { get; }
    }
}
