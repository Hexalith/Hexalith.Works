using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// Builds tenant-scoped recursive work-item roll-up read models from event deliveries.
/// </summary>
public sealed class WorkItemRollUpProjection
{
    private readonly Dictionary<NodeKey, RollUpNode> _nodes = [];
    private readonly WorkItemRollUpTenantIsolation _tenantIsolation;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemRollUpProjection"/> class with secure tenant isolation.
    /// </summary>
    public WorkItemRollUpProjection()
        : this(new WorkItemRollUpTenantIsolation())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemRollUpProjection"/> class with an isolation policy.
    /// </summary>
    /// <param name="tenantIsolation">The tenant-isolation policy used at every roll-up boundary.</param>
    internal WorkItemRollUpProjection(WorkItemRollUpTenantIsolation tenantIsolation)
    {
        ArgumentNullException.ThrowIfNull(tenantIsolation);
        _tenantIsolation = tenantIsolation;
    }

    public void Project(WorkItemRollUpEvent delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        // Refuse a mismatched or malformed delivery before any node is allocated: a payload whose tenant/id
        // disagrees with the delivery header must not fabricate an empty phantom node in Get()/Snapshot().
        // A corrupted stream is refused, not thrown on -- the exact descriptor and AllowsDelivery own the
        // well-formedness floor (missing payload, missing identities, unsupported payload type) so replay cannot wedge here.
        // The identity comparison itself is policy-governed; the floor is not.
        if (delivery.Sequence <= 0
            || !WorkItemRollUpPayloadDescriptor.TryResolve(delivery.Payload, out WorkItemRollUpPayloadDescriptor? descriptor)
            || !_tenantIsolation.AllowsDelivery(delivery, descriptor))
        {
            return;
        }

        NodeKey key = NodeKey.From(delivery.TenantId, delivery.WorkItemId);
        RollUpNode node = GetOrAdd(key, delivery.TenantId, delivery.WorkItemId);

        if (!node.Accept(delivery.Sequence, descriptor, delivery.Payload))
        {
            return;
        }

        descriptor.ApplyTopology(this, node, delivery.Payload);

        Rebuild(node);
    }

    public WorkItemRollUp? Get(TenantId tenantId, WorkItemId workItemId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(workItemId);

        NodeKey key = NodeKey.From(tenantId, workItemId);
        return _nodes.TryGetValue(key, out RollUpNode? node)
            ? ToReadModel(node, [])
            : null;
    }

    public IReadOnlyList<WorkItemRollUp> Snapshot()
        => [.. _nodes.Values.Select(node => ToReadModel(node, []))];

    private RollUpNode GetOrAdd(NodeKey key, TenantId tenantId, WorkItemId workItemId)
    {
        if (_nodes.TryGetValue(key, out RollUpNode? node))
        {
            return node;
        }

        node = new RollUpNode(tenantId, workItemId);
        _nodes.Add(key, node);
        return node;
    }

    private void AddEdge(NodeKey parentKey, NodeKey childKey)
    {
        // A cross-tenant edge is refused and never materializes graph state. The refusal is not silent:
        // ApplyPayload re-derives a metadata-only diagnostic from the WorkItemCreated fact on every
        // rebuild, so the trace is deterministic and survives replay.
        if (!_tenantIsolation.AllowsEdge(parentKey.TenantId, childKey.TenantId))
        {
            return;
        }

        RollUpNode parent = GetOrAdd(parentKey, new TenantId(parentKey.TenantId), new WorkItemId(parentKey.WorkItemId));
        RollUpNode child = GetOrAdd(childKey, new TenantId(childKey.TenantId), new WorkItemId(childKey.WorkItemId));

        parent.ChildKeys.Add(childKey);
        child.ParentKey = child.ParentKey is null || child.ParentKey == parentKey
            ? parentKey
            : child.ParentKey;
        child.Parent = child.Parent is null || child.Parent.TenantId == parent.TenantId
            ? new ParentWorkItemReference(parent.TenantId, parent.WorkItemId)
            : child.Parent;
    }

    private void Rebuild(RollUpNode node)
    {
        node.ResetProjectionState();

        if (node.HasSpawnFacts && !node.HasCreatedEvent)
        {
            node.Status = WorkItemStatus.Created;
            node.OwnEffort = node.SpawnInitialEffort;
            node.Parent = node.SpawnParent;
            node.LatestAcceptedSourceSequence = Math.Max(node.LatestAcceptedSourceSequence, 1);
        }

        foreach ((long sequence, (WorkItemRollUpPayloadDescriptor Descriptor, IEventPayload Payload) accepted) in node.Events)
        {
            node.LatestAcceptedSourceSequence = Math.Max(node.LatestAcceptedSourceSequence, sequence);
            accepted.Descriptor.ApplyFold(this, node, accepted.Payload);
        }
    }

    /// <summary>
    /// Applies the accepted <see cref="WorkItemCreated"/> topology effect.
    /// </summary>
    /// <param name="node">The accepted delivery's node.</param>
    /// <param name="created">The typed payload.</param>
    internal void ApplyTopology(RollUpNode node, WorkItemCreated created)
    {
        if (created.Parent is not null)
        {
            AddEdge(NodeKey.From(created.Parent.TenantId, created.Parent.WorkItemId), node.Key);
        }
    }

    /// <summary>
    /// Applies the accepted <see cref="ChildSpawned"/> topology effect.
    /// </summary>
    /// <param name="node">The accepted delivery's node.</param>
    /// <param name="spawned">The typed payload.</param>
    internal void ApplyTopology(RollUpNode node, ChildSpawned spawned)
    {
        NodeKey childKey = NodeKey.From(spawned.TenantId, spawned.ChildWorkItemId);
        RollUpNode child = GetOrAdd(childKey, spawned.TenantId, spawned.ChildWorkItemId);
        child.MergeSpawnFacts(spawned);
        AddEdge(node.Key, childKey);
        Rebuild(child);
    }

    /// <summary>
    /// Applies the accepted <see cref="WorkItemCreated"/> sorted-fold effect.
    /// </summary>
    /// <param name="node">The node being rebuilt.</param>
    /// <param name="created">The typed payload.</param>
    internal void ApplyPayload(RollUpNode node, WorkItemCreated created)
    {
        node.Status = WorkItemStatus.Created;
        node.OwnEffort = created.InitialEffort;
        node.Parent = created.Parent;
        node.Terminal = false;
        if (created.Parent is not null && !_tenantIsolation.AllowsEdge(created.Parent.TenantId, node.TenantId))
        {
            // AddEdge refuses the cross-tenant parent edge; surface that refusal as a deterministic
            // metadata-only diagnostic without degrading the node.
            node.Diagnose(nameof(WorkItemCreated), created.Sequence);
        }
    }

    private WorkItemRollUp ToReadModel(RollUpNode node, HashSet<NodeKey> traversal)
    {
        RemainingBuckets buckets = CalculateRolled(node, traversal);
        IReadOnlyList<RolledRemaining> byUnit = buckets.ToRolledRemainingByUnit();
        List<RollUpNode> outputChildren = [];
        foreach (NodeKey childKey in node.ChildKeys)
        {
            if (_nodes.TryGetValue(childKey, out RollUpNode? child)
                && _tenantIsolation.AllowsOutput(node.TenantId, child.TenantId))
            {
                outputChildren.Add(child);
            }
        }

        // Ordinal work item id is the published ordering key. An isolation-ablated output policy can expose two
        // tenants' children whose ids collide, and List<T>.Sort is unstable, so tenant breaks that tie to keep the
        // comparison a total order rather than leaving tied entries in delivery order.
        outputChildren.Sort(static (first, second) =>
        {
            int byWorkItemId = StringComparer.Ordinal.Compare(first.WorkItemId.Value, second.WorkItemId.Value);
            return byWorkItemId != 0
                ? byWorkItemId
                : StringComparer.Ordinal.Compare(first.TenantId.Value, second.TenantId.Value);
        });

        return new WorkItemRollUp(
            node.TenantId,
            node.WorkItemId,
            node.Status,
            node.Parent,
            ToOwnRemaining(node),
            byUnit.Count == 1 ? byUnit[0] : null,
            byUnit,
            [.. outputChildren.Select(child => child.WorkItemId)],
            node.LatestAcceptedSourceSequence)
        {
            Degraded = IsDegraded(node, []),
            ProjectionDiagnostics = CollectDiagnostics(node, []),
            OwnEffort = node.OwnEffort,
        };
    }

    private static OwnRemaining? ToOwnRemaining(RollUpNode node)
    {
        if (node.Terminal)
        {
            return new OwnRemaining(0, node.OwnEffort?.Unit);
        }

        return node.OwnEffort is null
            ? null
            : new OwnRemaining(node.OwnEffort.Remaining, node.OwnEffort.Unit);
    }

    private RemainingBuckets CalculateRolled(RollUpNode node, HashSet<NodeKey> traversal)
    {
        if (!traversal.Add(node.Key))
        {
            return new RemainingBuckets();
        }

        var buckets = new RemainingBuckets();
        OwnRemaining? own = ToOwnRemaining(node);
        if (own is not null && own.Unit is not null)
        {
            buckets.Add(own.Unit, own.Value);
        }

        if (!node.Terminal)
        {
            foreach (NodeKey childKey in node.ChildKeys)
            {
                if (!_nodes.TryGetValue(childKey, out RollUpNode? child)
                    || !_tenantIsolation.AllowsContribution(node.TenantId, child.TenantId))
                {
                    continue;
                }

                RemainingBuckets childBuckets = CalculateRolled(child, traversal);
                buckets.Add(childBuckets);
            }
        }

        traversal.Remove(node.Key);
        return buckets;
    }

    private IReadOnlyList<RollUpProjectionDiagnostic> CollectDiagnostics(RollUpNode node, HashSet<NodeKey> traversal)
    {
        if (!traversal.Add(node.Key))
        {
            return [];
        }

        List<RollUpProjectionDiagnostic> diagnostics = [.. node.ProjectionDiagnostics];
        if (!node.Terminal)
        {
            foreach (NodeKey childKey in node.ChildKeys)
            {
                if (!_nodes.TryGetValue(childKey, out RollUpNode? child)
                    || !_tenantIsolation.AllowsDiagnostic(node.TenantId, child.TenantId))
                {
                    continue;
                }

                diagnostics.AddRange(CollectDiagnostics(child, traversal));
            }
        }

        traversal.Remove(node.Key);
        return [.. diagnostics
            .OrderBy(diagnostic => diagnostic.WorkItemId.Value, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Sequence)
            .ThenBy(diagnostic => diagnostic.EventType, StringComparer.Ordinal)];
    }

    private bool IsDegraded(RollUpNode node, HashSet<NodeKey> traversal)
    {
        if (!traversal.Add(node.Key))
        {
            return false;
        }

        if (node.Degraded)
        {
            traversal.Remove(node.Key);
            return true;
        }

        if (!node.Terminal)
        {
            foreach (NodeKey childKey in node.ChildKeys)
            {
                if (_nodes.TryGetValue(childKey, out RollUpNode? child)
                    && _tenantIsolation.AllowsDegradation(node.TenantId, child.TenantId)
                    && IsDegraded(child, traversal))
                {
                    traversal.Remove(node.Key);
                    return true;
                }
            }
        }

        traversal.Remove(node.Key);
        return false;
    }

    private readonly record struct NodeKey(string TenantId, string WorkItemId)
    {
        public static NodeKey From(TenantId tenantId, WorkItemId workItemId)
            => new(tenantId.Value, workItemId.Value);
    }

    internal sealed class RollUpNode(TenantId tenantId, WorkItemId workItemId)
    {
        private NodeKey Key { get; } = NodeKey.From(tenantId, workItemId);

        public TenantId TenantId { get; } = tenantId;

        public WorkItemId WorkItemId { get; } = workItemId;

        public SortedDictionary<long, (WorkItemRollUpPayloadDescriptor Descriptor, IEventPayload Payload)> Events { get; } = [];

        private HashSet<NodeKey> ChildKeys { get; } = [];

        private NodeKey? ParentKey { get; set; }

        public ParentWorkItemReference? Parent { get; set; }

        public WorkItemStatus Status { get; set; }

        public WorkItemEffort? OwnEffort { get; set; }

        public bool Terminal { get; set; }

        public bool Degraded { get; private set; }

        public List<RollUpProjectionDiagnostic> ProjectionDiagnostics { get; } = [];

        public long LatestAcceptedSourceSequence { get; set; }

        public bool HasSpawnFacts { get; private set; }

        public WorkItemEffort? SpawnInitialEffort { get; private set; }

        public ParentWorkItemReference? SpawnParent { get; private set; }

        public bool HasCreatedEvent => Events.Values.Any(accepted => accepted.Payload is WorkItemCreated);

        public bool Accept(long sequence, WorkItemRollUpPayloadDescriptor descriptor, IEventPayload payload)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(payload);
            return Events.TryAdd(sequence, (descriptor, payload));
        }

        public void MergeSpawnFacts(ChildSpawned spawned)
        {
            ArgumentNullException.ThrowIfNull(spawned);
            if (HasSpawnFacts)
            {
                return;
            }

            HasSpawnFacts = true;
            SpawnInitialEffort = spawned.InitialEffort;
            SpawnParent = new ParentWorkItemReference(spawned.TenantId, spawned.WorkItemId);
        }

        public void ResetProjectionState()
        {
            Parent = null;
            Status = WorkItemStatus.Unknown;
            OwnEffort = null;
            Terminal = false;
            Degraded = false;
            ProjectionDiagnostics.Clear();
            LatestAcceptedSourceSequence = 0;
        }

        // Refuse an incompatible contribution (unit mismatch or corrupted effort value): retain the last
        // valid projected effort, flag the read model as degraded, and record the metadata diagnostic.
        public void Refuse(string eventType, long sequence)
        {
            Degraded = true;
            Diagnose(eventType, sequence);
        }

        // Record a metadata-only diagnostic without degrading the node (for example a refused
        // cross-tenant edge, where the skip itself is by-design isolation, not data loss).
        public void Diagnose(string eventType, long sequence)
            => ProjectionDiagnostics.Add(new RollUpProjectionDiagnostic(TenantId, WorkItemId, eventType, sequence));
    }

    private sealed class RemainingBuckets
    {
        private readonly Dictionary<Unit, decimal> _byUnit = [];

        public void Add(Unit unit, decimal value)
        {
            ArgumentNullException.ThrowIfNull(unit);
            _byUnit[unit] = _byUnit.GetValueOrDefault(unit) + value;
        }

        public void Add(RemainingBuckets other)
        {
            ArgumentNullException.ThrowIfNull(other);
            foreach ((Unit unit, decimal value) in other._byUnit)
            {
                Add(unit, value);
            }
        }

        public IReadOnlyList<RolledRemaining> ToRolledRemainingByUnit()
            => [.. _byUnit
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                .Select(pair => new RolledRemaining(pair.Value, pair.Key))];
    }
}
