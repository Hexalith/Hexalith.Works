using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a create or spawn command supplies an initial effort that already carries done progress
/// (<see cref="Done"/> is not zero). Initial effort starts unstarted and progress arrives only through
/// <c>ReportProgress</c>, so the raw act is refused rather than silently coerced to zero. EventStore
/// persists the rejection and assigns its envelope <c>SequenceNumber</c>; the frozen Works payload
/// carries no <c>Sequence</c> because the refusal does not change aggregate state.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemInitialEffortRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    decimal Done) : IRejectionEvent;
