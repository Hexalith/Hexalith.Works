using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

/// <summary>
/// Raw-act evidence that a bound executor declined an assignment. <see cref="Requeue"/> distinguishes
/// the resting status: <c>true</c> rests at <c>Queued</c>, <c>false</c> reaches terminal
/// <c>Rejected</c>. This is distinct from a <c>WorkItemTransitionRejected</c> rejection event.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemRejected(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId,
    bool Requeue) : IEventPayload;
