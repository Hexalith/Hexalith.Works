using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a progress report is refused by progress-specific invariants. EventStore persists the
/// rejection and assigns its envelope <c>SequenceNumber</c>; the frozen Works payload carries no
/// <c>Sequence</c> because the refusal does not change aggregate state.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemProgressRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string Reason) : IRejectionEvent;
