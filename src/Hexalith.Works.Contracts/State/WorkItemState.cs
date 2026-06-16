using Hexalith.EventStore.Contracts.Identity;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.State;

public sealed class WorkItemState
{
    private const string Domain = "work";

    public TenantId? TenantId { get; private set; }

    public WorkItemId? WorkItemId { get; private set; }

    public Obligation? Obligation { get; private set; }

    public WorkItemStatus Status { get; private set; }

    public WorkItemEffort? InitialEffort { get; private set; }

    public decimal? Remaining => InitialEffort?.Remaining;

    public bool IsCompletedByRemaining => Remaining == 0;

    public WorkItemSchedule? Schedule { get; private set; }

    public ParentWorkItemReference? Parent { get; private set; }

    public ExecutorBinding? ExecutorBinding { get; private set; }

    public ConversationCorrelationId? ConversationCorrelationId { get; private set; }

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
        InitialEffort = e.InitialEffort;
        Schedule = e.Schedule;
        Parent = e.Parent;
        ExecutorBinding = e.ExecutorBinding;
        ConversationCorrelationId = e.ConversationCorrelationId;
    }
}
