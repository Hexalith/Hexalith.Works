using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a re-estimate is refused by re-estimate-specific invariants (a negative Estimated, or a
/// Unit that differs from the established effort Unit). Status-based failures (terminal status or the
/// pre-creation Unknown sentinel) reuse <c>WorkItemTransitionRejected</c> instead. Rejections are
/// returned to the caller and are not appended to the event stream, so they carry no sequence.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemReEstimateRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string Reason) : IRejectionEvent;
