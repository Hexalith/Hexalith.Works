using Hexalith.EventStore.Contracts.Identity;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.State;

public sealed class WorkItemState
{
    private const string Domain = "work";
    private readonly List<WorkItemId> _spawnedChildWorkItemIds = [];
    private readonly List<AwaitCondition> _awaitConditions = [];

    public TenantId? TenantId { get; private set; }

    public WorkItemId? WorkItemId { get; private set; }

    public Obligation? Obligation { get; private set; }

    public WorkItemStatus Status { get; private set; }

    /// <summary>
    /// The sequence number of the last event applied to this state. Starts at 0 (no events) and is
    /// set to each success event's <c>Sequence</c> on replay, giving the writer a monotonic basis for
    /// assigning the next event's sequence. Rejection events do not advance it.
    /// </summary>
    public long Sequence { get; private set; }

    public WorkItemEffort? InitialEffort { get; private set; }

    public decimal? Remaining => InitialEffort?.Remaining;

    public bool IsCompletedByRemaining => Remaining == 0;

    public WorkItemSchedule? Schedule { get; private set; }

    public ParentWorkItemReference? Parent { get; private set; }

    public ExecutorBinding? ExecutorBinding { get; private set; }

    public ConversationCorrelationId? ConversationCorrelationId { get; private set; }

    public IReadOnlyList<WorkItemId> SpawnedChildWorkItemIds => _spawnedChildWorkItemIds;

    public IReadOnlyList<AwaitCondition> AwaitConditions => _awaitConditions;

    public AwaitCondition? LastConsumedAwaitCondition { get; private set; }

    public AggregateIdentity? AggregateIdentity => TenantId is null || WorkItemId is null
        ? null
        : new AggregateIdentity(TenantId.Value, Domain, WorkItemId.Value);

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemCannotBeCreatedWithoutObligation e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemCannotReferenceParentFromAnotherTenant e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemCannotReferenceSecondParent e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemTreeCycleRejected e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemTreeDepthExceeded e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemProgressRejected e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemReEstimateRejected e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemInitialEffortRejected e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822

    // Trust boundary: WorkItemAggregate.Handle is the sole writer of WorkItemCreated and enforces
    // the cross-tenant parent invariant before the event is emitted. Replay therefore trusts the
    // stored event and applies Parent verbatim — a persisted foreign-tenant parent is preserved as a
    // distinct reference, never coerced to this work item's tenant.
    public void Apply(WorkItemCreated e)
    {
        ArgumentNullException.ThrowIfNull(e);
        TenantId = e.TenantId;
        WorkItemId = e.WorkItemId;
        Obligation = e.Obligation;
        Status = WorkItemStatus.Created;
        Sequence = e.Sequence;
        InitialEffort = e.InitialEffort;
        Schedule = e.Schedule;
        Parent = e.Parent;
        ExecutorBinding = e.ExecutorBinding;
        ConversationCorrelationId = e.ConversationCorrelationId;
    }

    // Lifecycle replay: each success event is trusted (WorkItemAggregate.Handle is the sole writer and
    // enforces the transition table before emitting) and applied by setting the target status and the
    // monotonic sequence. Only the minimal fields owned by current stories are touched: own effort,
    // child-completion awaits, and lightweight child references. Roll-up remains deferred.
    public void Apply(WorkItemAssigned e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.Assigned;
        Sequence = e.Sequence;
        ExecutorBinding = e.Binding;
    }

    public void Apply(WorkItemQueued e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.Queued;
        Sequence = e.Sequence;
    }

    public void Apply(WorkItemClaimed e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.InProgress;
        Sequence = e.Sequence;
        ExecutorBinding = e.Binding;
    }

    public void Apply(WorkItemSuspended e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.Suspended;
        Sequence = e.Sequence;
        _awaitConditions.Clear();
        _awaitConditions.AddRange(e.AwaitConditions);
        LastConsumedAwaitCondition = null;
    }

    public void Apply(WorkItemResumed e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.InProgress;
        Sequence = e.Sequence;
        _awaitConditions.Clear();
        LastConsumedAwaitCondition = e.ConsumedAwaitCondition;
    }

    public void Apply(ChildSpawned e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_spawnedChildWorkItemIds.Contains(e.ChildWorkItemId))
        {
            _spawnedChildWorkItemIds.Add(e.ChildWorkItemId);
        }

        Sequence = e.Sequence;
    }

    public void Apply(ProgressReported e)
    {
        ArgumentNullException.ThrowIfNull(e);
        InitialEffort = InitialEffort!.Report(e.DoneDelta);
        Sequence = e.Sequence;
    }

    public void Apply(ReEstimated e)
    {
        ArgumentNullException.ThrowIfNull(e);
        InitialEffort = InitialEffort is null
            ? new WorkItemEffort(e.Estimated, e.Unit)
            : InitialEffort.ReEstimate(e.Estimated);
        Sequence = e.Sequence;
    }

    public void Apply(WorkItemRescheduled e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Schedule = e.Schedule;
        Sequence = e.Sequence;
    }

    public void Apply(WorkItemCompleted e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.Completed;
        Sequence = e.Sequence;
    }

    public void Apply(WorkItemCancelled e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.Cancelled;
        Sequence = e.Sequence;
    }

    // The requeue flag carried by the event determines the resting status: a requeued rejection rests
    // at Queued (raw-act evidence only), a non-requeue rejection reaches the terminal Rejected status.
    public void Apply(WorkItemRejected e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = e.Requeue ? WorkItemStatus.Queued : WorkItemStatus.Rejected;
        Sequence = e.Sequence;
    }

    public void Apply(WorkItemExpired e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Status = WorkItemStatus.Expired;
        Sequence = e.Sequence;
    }

#pragma warning disable CA1822 // EventStore replay convention requires an Apply overload for rejection events.
    public void Apply(WorkItemTransitionRejected e)
        => ArgumentNullException.ThrowIfNull(e);
#pragma warning restore CA1822
}
