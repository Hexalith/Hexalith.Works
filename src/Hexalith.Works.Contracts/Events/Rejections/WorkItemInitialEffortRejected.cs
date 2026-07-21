using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a create or spawn command supplies an initial effort that already carries done progress
/// (<see cref="Done"/> is not zero). Initial effort starts unstarted and progress arrives only through
/// <c>ReportProgress</c>, so the raw act is refused rather than silently coerced to zero. Rejections
/// are returned to the caller and are not appended to the event stream, so they carry no sequence.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemInitialEffortRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    decimal Done) : IRejectionEvent;
