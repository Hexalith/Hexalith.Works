using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a lifecycle command is refused because the transition is illegal from the work item's
/// current status — the command produced no state change. Like other rejection events this carries
/// context (the <see cref="FromStatus"/> and the <see cref="AttemptedAct"/>) and is returned to the
/// caller rather than appended to the stream, so it has no sequence. This is distinct from the
/// terminal <c>Rejected</c> status reached by <c>RejectWorkItem(Requeue: false)</c>.
/// </summary>
public sealed record WorkItemTransitionRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemStatus FromStatus,
    string AttemptedAct) : IRejectionEvent;
