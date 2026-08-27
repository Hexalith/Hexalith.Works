using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a lifecycle command is refused because the transition is illegal from the work item's
/// current status — the command produced no state change. Like other rejection events this carries
/// context (the <see cref="FromStatus"/> and the <see cref="AttemptedAct"/>). EventStore persists it
/// and assigns its envelope <c>SequenceNumber</c>; the frozen Works payload carries no <c>Sequence</c>
/// because the refusal does not change aggregate state. This is distinct from the state-changing
/// <c>WorkItemRejected</c> event emitted by <c>RejectWorkItem</c>, which may reach terminal
/// <c>Rejected</c> status.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemTransitionRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemStatus FromStatus,
    string AttemptedAct) : IRejectionEvent;
