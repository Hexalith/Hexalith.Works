using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Raised when a re-estimate is refused by re-estimate-specific invariants (a negative Estimated, or a
/// Unit that differs from the established effort Unit). Status-based failures (terminal status or the
/// pre-creation Unknown sentinel) reuse <c>WorkItemTransitionRejected</c> instead. EventStore persists
/// the rejection and assigns its envelope <c>SequenceNumber</c>; the frozen Works payload carries no
/// <c>Sequence</c> because the refusal does not change aggregate state.
/// </summary>
[PolymorphicSerialization]
public sealed partial record WorkItemReEstimateRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string Reason) : IRejectionEvent;
