using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// The pure, in-memory tenant "what's next" projection (FR-20). It consumes the same per-aggregate
/// <see cref="WorkItemRollUpEvent"/> delivery envelope the roll-up projection uses (DC6) and answers
/// <see cref="WhatsNext(TenantId, Func{TenantId, WorkItemId, WorkItemRollUp?}?)"/>: the tenant's
/// eligible (<see cref="WorkItemStatus.Queued"/> or <see cref="WorkItemStatus.Assigned"/>) items ordered
/// by <see cref="WhatsNextOrdering"/> (Priority → Due Date → identity; neither sorts last).
/// <para>
/// Each item keeps its accepted events in a per-item <see cref="SortedDictionary{TKey, TValue}"/> keyed
/// by EventStore envelope position and re-derives status / schedule / binding / own-remaining / await
/// conditions on every change. Unsupported and rejection payloads are filtered before acceptance, so
/// the freshness watermark is the latest accepted state-changing envelope position rather than the full
/// stream high-watermark. Replay, duplicate, and out-of-order delivery converge to the same read model
/// and the same ordering (idempotent + order-tolerant — DC5/NFR-4/NFR-9/B2). Tenant isolation is a
/// per-item <c>(tenant, id)</c> key plus an ordinal tenant-equality check on read, so items from
/// different tenants with colliding inner ids stay distinct and never cross (AC #5). It holds no
/// authoritative state, performs no I/O, and never logs (NFR-5/NFR-6).
/// </para>
/// </summary>
public sealed class WhatsNextQueueProjection
{
    /// <summary>
    /// The stable kebab-case projection token the deferred runtime adapter passes to
    /// <c>IProjectionChangeNotifier.NotifyProjectionChangedAsync</c> (SignalR group
    /// <c>{projectionType}:{tenantId}</c>). v1 ships the token, not the live wiring (DC1/AC #4).
    /// </summary>
    public const string ProjectionType = "works-whats-next";

    private readonly Dictionary<ItemKey, ItemNode> _items = [];

    /// <summary>
    /// Applies a delivery and reports whether it changed the tenant's what's-next eligibility set or
    /// ordering. The deferred runtime adapter notifies the SignalR seam only when
    /// <see cref="WhatsNextProjectionChange.Changed"/> is set; a binding- or remaining-only update on an
    /// already-eligible item is not a change (AC #4). Unsupported or rejection payloads,
    /// tenant/id-mismatched payloads, and duplicate envelope positions are ignored and report no change.
    /// </summary>
    public WhatsNextProjectionChange Project(WorkItemRollUpEvent delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(delivery.Payload);

        if (delivery.Sequence <= 0
            || !WhatsNextPayloadDescriptor.TryResolve(delivery.Payload, out WhatsNextPayloadDescriptor? descriptor)
            || !descriptor.MatchesIdentity(delivery))
        {
            return new WhatsNextProjectionChange(false, delivery.TenantId);
        }

        ItemKey key = ItemKey.From(delivery.TenantId, delivery.WorkItemId);
        ItemNode node = GetOrAdd(key, delivery.TenantId, delivery.WorkItemId);

        // Short-circuit a duplicate/out-of-sequence fact before paying for the order signature. Accept only
        // appends to the per-item event map; it does not re-derive projected state (that is Rebuild's job),
        // so the post-Accept signature still reflects the pre-delivery eligibility/order — capturing `before`
        // here is equivalent to capturing it earlier, while skipping the O(n log n) rebuild on the expected
        // at-least-once duplicate-delivery path (NFR-9/B2).
        if (!node.Accept(delivery.Sequence, descriptor, delivery.Payload))
        {
            return new WhatsNextProjectionChange(false, delivery.TenantId);
        }

        IReadOnlyList<OrderSignatureEntry> before = OrderSignature(delivery.TenantId);

        Rebuild(node);

        IReadOnlyList<OrderSignatureEntry> after = OrderSignature(delivery.TenantId);
        return new WhatsNextProjectionChange(!before.SequenceEqual(after), delivery.TenantId);
    }

    /// <summary>
    /// Returns the tenant's eligible items ordered by <see cref="WhatsNextOrdering"/>. Each item's own
    /// status / schedule / binding / own-remaining / await-conditions are derived from its own events;
    /// rolled-remaining is composed only where the optional <paramref name="rollUpLookup"/> supplies a
    /// co-available <see cref="WorkItemRollUp"/> (DC7 — the tree is never rebuilt here), and is left
    /// null / empty otherwise.
    /// </summary>
    public IReadOnlyList<WhatsNextItem> WhatsNext(
        TenantId tenantId,
        Func<TenantId, WorkItemId, WorkItemRollUp?>? rollUpLookup = null)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        return BuildEligibleItems(tenantId, rollUpLookup);
    }

    private static bool IsEligible(WorkItemStatus status)
        => status is WorkItemStatus.Queued or WorkItemStatus.Assigned;

    private void Rebuild(ItemNode node)
    {
        node.ResetProjectionState();
        foreach ((long sequence, (WhatsNextPayloadDescriptor Descriptor, IEventPayload Payload) accepted) in node.Events)
        {
            node.LatestAcceptedSourceSequence = Math.Max(node.LatestAcceptedSourceSequence, sequence);
            accepted.Descriptor.ApplyFold(this, node, accepted.Payload);
        }
    }

    private static OwnRemaining? ToOwnRemaining(ItemNode node)
    {
        if (node.Terminal)
        {
            return new OwnRemaining(0, node.OwnEffort?.Unit);
        }

        return node.OwnEffort is null
            ? null
            : new OwnRemaining(node.OwnEffort.Remaining, node.OwnEffort.Unit);
    }

    private static WhatsNextItem ToReadModel(ItemNode node, WorkItemRollUp? rollUp)
        => new(
            node.TenantId,
            node.WorkItemId,
            node.Status,
            node.Schedule?.Priority,
            node.Schedule?.DueDate,
            node.ExecutorBinding,
            ToOwnRemaining(node),
            rollUp?.RolledRemaining,
            rollUp?.RolledRemainingByUnit ?? [],
            [.. node.AwaitConditions],
            node.LatestAcceptedSourceSequence);

    private IReadOnlyList<WhatsNextItem> BuildEligibleItems(
        TenantId tenantId,
        Func<TenantId, WorkItemId, WorkItemRollUp?>? rollUpLookup)
    {
        List<WhatsNextItem> items = [];
        foreach (ItemNode node in _items.Values)
        {
            if (!string.Equals(node.TenantId.Value, tenantId.Value, StringComparison.Ordinal)
                || !IsEligible(node.Status))
            {
                continue;
            }

            items.Add(ToReadModel(node, rollUpLookup?.Invoke(node.TenantId, node.WorkItemId)));
        }

        items.Sort(WhatsNextOrdering.Instance);
        return items;
    }

    // The change signature is the ordered list of each eligible item's ordering key (id + priority rank
    // + due-date key). It flips when eligibility changes (membership) or when an ordering input changes
    // (priority / due date), and stays stable for binding- or remaining-only updates — exactly AC #4's
    // "change queue eligibility or ordering". It deliberately does not depend on the roll-up lookup.
    private IReadOnlyList<OrderSignatureEntry> OrderSignature(TenantId tenantId)
        => [.. BuildEligibleItems(tenantId, null)
            .Select(item => new OrderSignatureEntry(
                item.WorkItemId.Value,
                WhatsNextOrdering.PriorityRank(item.Priority),
                WhatsNextOrdering.DueDateKey(item.DueDate).DayNumber))];

    private ItemNode GetOrAdd(ItemKey key, TenantId tenantId, WorkItemId workItemId)
    {
        if (_items.TryGetValue(key, out ItemNode? node))
        {
            return node;
        }

        node = new ItemNode(tenantId, workItemId);
        _items.Add(key, node);
        return node;
    }

    private readonly record struct OrderSignatureEntry(string WorkItemId, int PriorityRank, int DueDate);

    private readonly record struct ItemKey(string TenantId, string WorkItemId)
    {
        public static ItemKey From(TenantId tenantId, WorkItemId workItemId)
            => new(tenantId.Value, workItemId.Value);
    }

    internal sealed class ItemNode(TenantId tenantId, WorkItemId workItemId)
    {
        public TenantId TenantId { get; } = tenantId;

        public WorkItemId WorkItemId { get; } = workItemId;

        public SortedDictionary<long, (WhatsNextPayloadDescriptor Descriptor, IEventPayload Payload)> Events { get; } = [];

        public WorkItemStatus Status { get; set; }

        public WorkItemSchedule? Schedule { get; set; }

        public ExecutorBinding? ExecutorBinding { get; set; }

        public WorkItemEffort? OwnEffort { get; set; }

        public List<AwaitCondition> AwaitConditions { get; } = [];

        public bool Terminal { get; set; }

        public long LatestAcceptedSourceSequence { get; set; }

        public bool Accept(long sequence, WhatsNextPayloadDescriptor descriptor, IEventPayload payload)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(payload);
            return Events.TryAdd(sequence, (descriptor, payload));
        }

        public void ResetProjectionState()
        {
            Status = WorkItemStatus.Unknown;
            Schedule = null;
            ExecutorBinding = null;
            OwnEffort = null;
            AwaitConditions.Clear();
            Terminal = false;
            LatestAcceptedSourceSequence = 0;
        }
    }
}
