using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;

namespace Hexalith.Works.Projections.Strategies;

public sealed class WorkItemRollUpProjection
{
    private readonly Dictionary<NodeKey, RollUpNode> _nodes = [];

    public void Project(WorkItemRollUpEvent delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(delivery.Payload);

        // Refuse a mismatched delivery before any node is allocated: a payload whose tenant/id disagrees
        // with the delivery header must not fabricate an empty phantom node in Get()/Snapshot().
        if (delivery.Sequence <= 0 || !EventMatchesDelivery(delivery))
        {
            return;
        }

        NodeKey key = NodeKey.From(delivery.TenantId, delivery.WorkItemId);
        RollUpNode node = GetOrAdd(key, delivery.TenantId, delivery.WorkItemId);

        if (!node.Accept(delivery.Sequence, delivery.Payload))
        {
            return;
        }

        switch (delivery.Payload)
        {
            case WorkItemCreated created when created.Parent is not null:
                AddEdge(NodeKey.From(created.Parent.TenantId, created.Parent.WorkItemId), key);
                break;
            case ChildSpawned spawned:
                NodeKey childKey = NodeKey.From(spawned.TenantId, spawned.ChildWorkItemId);
                RollUpNode child = GetOrAdd(childKey, spawned.TenantId, spawned.ChildWorkItemId);
                child.MergeSpawnFacts(spawned);
                AddEdge(key, childKey);
                Rebuild(child);
                break;
        }

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

    private static bool EventMatchesDelivery(WorkItemRollUpEvent delivery)
        => delivery.Payload switch
        {
            WorkItemCreated e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            ChildSpawned e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            ProgressReported e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            ReEstimated e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemCompleted e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemCancelled e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemExpired e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemRejected e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemAssigned e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemQueued e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemClaimed e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemSuspended e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemResumed e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,
            WorkItemRescheduled e => e.TenantId == delivery.TenantId && e.WorkItemId == delivery.WorkItemId,

            // Fail closed: an event type this projection does not know cannot prove its payload agrees
            // with the delivery header, so it must never be accepted into a sequence slot.
            _ => false,
        };

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
        if (!string.Equals(parentKey.TenantId, childKey.TenantId, StringComparison.Ordinal))
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

    private static void Rebuild(RollUpNode node)
    {
        node.ResetProjectionState();

        if (node.HasSpawnFacts && !node.HasCreatedEvent)
        {
            node.Status = WorkItemStatus.Created;
            node.OwnEffort = node.SpawnInitialEffort;
            node.Parent = node.SpawnParent;
            node.LatestAcceptedSourceSequence = Math.Max(node.LatestAcceptedSourceSequence, 1);
        }

        foreach ((long sequence, IEventPayload payload) in node.Events)
        {
            node.LatestAcceptedSourceSequence = Math.Max(node.LatestAcceptedSourceSequence, sequence);
            ApplyPayload(node, payload);
        }
    }

    private static void ApplyPayload(RollUpNode node, IEventPayload payload)
    {
        switch (payload)
        {
            case WorkItemCreated created:
                node.Status = WorkItemStatus.Created;
                node.OwnEffort = created.InitialEffort;
                node.Parent = created.Parent;
                node.Terminal = false;
                if (created.Parent is not null && created.Parent.TenantId != node.TenantId)
                {
                    // AddEdge refuses the cross-tenant parent edge; surface that refusal as a
                    // deterministic metadata-only diagnostic without degrading the node — tenant
                    // isolation is by-design behavior, not a stale retained value.
                    node.Diagnose(nameof(WorkItemCreated), created.Sequence);
                }

                break;
            case ProgressReported progress when !node.Terminal && node.OwnEffort is not null:
                if (node.OwnEffort.Unit != progress.Unit)
                {
                    node.Refuse(nameof(ProgressReported), progress.Sequence);
                    break;
                }

                // Read-side defense against a corrupted stream: the write side rejects non-positive
                // deltas, but a persisted one must refuse-and-diagnose instead of throwing inside
                // WorkItemEffort.Report and wedging every rebuild of this aggregate.
                if (progress.DoneDelta <= 0)
                {
                    node.Refuse(nameof(ProgressReported), progress.Sequence);
                    break;
                }

                node.OwnEffort = node.OwnEffort.Report(progress.DoneDelta);
                break;
            case ReEstimated reEstimated when !node.Terminal:
                if (node.OwnEffort is not null && node.OwnEffort.Unit != reEstimated.Unit)
                {
                    node.Refuse(nameof(ReEstimated), reEstimated.Sequence);
                    break;
                }

                // Read-side defense against a corrupted stream: a persisted negative estimate would
                // throw inside WorkItemEffort and wedge every rebuild of this aggregate.
                if (reEstimated.Estimated < 0)
                {
                    node.Refuse(nameof(ReEstimated), reEstimated.Sequence);
                    break;
                }

                node.OwnEffort = node.OwnEffort is null
                    ? new WorkItemEffort(reEstimated.Estimated, reEstimated.Unit)
                    : node.OwnEffort.ReEstimate(reEstimated.Estimated);
                break;
            case WorkItemAssigned assigned when !node.Terminal:
                node.Status = WorkItemStatus.Assigned;
                break;
            case WorkItemQueued queued when !node.Terminal:
                node.Status = WorkItemStatus.Queued;
                break;
            case WorkItemClaimed claimed when !node.Terminal:
                node.Status = WorkItemStatus.InProgress;
                break;
            case WorkItemSuspended suspended when !node.Terminal:
                node.Status = WorkItemStatus.Suspended;
                break;
            case WorkItemResumed resumed when !node.Terminal:
                node.Status = WorkItemStatus.InProgress;
                break;
            case WorkItemRejected rejected when rejected.Requeue && !node.Terminal:
                node.Status = WorkItemStatus.Queued;
                break;
            case WorkItemCompleted:
                SetTerminal(node, WorkItemStatus.Completed);
                break;
            case WorkItemCancelled:
                SetTerminal(node, WorkItemStatus.Cancelled);
                break;
            case WorkItemExpired:
                SetTerminal(node, WorkItemStatus.Expired);
                break;
            case WorkItemRejected rejected when !rejected.Requeue:
                SetTerminal(node, WorkItemStatus.Rejected);
                break;
        }
    }

    private static void SetTerminal(RollUpNode node, WorkItemStatus status)
    {
        node.Status = status;
        node.Terminal = true;
    }

    private WorkItemRollUp ToReadModel(RollUpNode node, HashSet<NodeKey> traversal)
    {
        RemainingBuckets buckets = CalculateRolled(node, traversal);
        IReadOnlyList<RolledRemaining> byUnit = buckets.ToRolledRemainingByUnit();

        return new WorkItemRollUp(
            node.TenantId,
            node.WorkItemId,
            node.Status,
            node.Parent,
            ToOwnRemaining(node),
            byUnit.Count == 1 ? byUnit[0] : null,
            byUnit,
            [.. node.ChildKeys
                .Where(key => string.Equals(key.TenantId, node.TenantId.Value, StringComparison.Ordinal))
                .Select(key => _nodes[key].WorkItemId)],
            node.ChildKeys.Count(key => string.Equals(key.TenantId, node.TenantId.Value, StringComparison.Ordinal)),
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
                if (!string.Equals(childKey.TenantId, node.TenantId.Value, StringComparison.Ordinal)
                    || !_nodes.TryGetValue(childKey, out RollUpNode? child))
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
                if (!string.Equals(childKey.TenantId, node.TenantId.Value, StringComparison.Ordinal)
                    || !_nodes.TryGetValue(childKey, out RollUpNode? child))
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
                if (string.Equals(childKey.TenantId, node.TenantId.Value, StringComparison.Ordinal)
                    && _nodes.TryGetValue(childKey, out RollUpNode? child)
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

    private sealed class RollUpNode(TenantId tenantId, WorkItemId workItemId)
    {
        public NodeKey Key { get; } = NodeKey.From(tenantId, workItemId);

        public TenantId TenantId { get; } = tenantId;

        public WorkItemId WorkItemId { get; } = workItemId;

        public SortedDictionary<long, IEventPayload> Events { get; } = [];

        public HashSet<NodeKey> ChildKeys { get; } = [];

        public NodeKey? ParentKey { get; set; }

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

        public bool HasCreatedEvent => Events.Values.OfType<WorkItemCreated>().Any();

        public bool Accept(long sequence, IEventPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return Events.TryAdd(sequence, payload);
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
