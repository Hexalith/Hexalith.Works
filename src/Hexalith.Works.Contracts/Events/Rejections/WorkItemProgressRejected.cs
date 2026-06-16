using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a progress report is refused by progress-specific invariants. Rejections are returned to
/// the caller and are not appended to the event stream, so they carry no sequence.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemProgressRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string Reason) : IRejectionEvent;
