using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Projections.Models;

/// <summary>
/// Carries a persisted work-item payload to the pure projections with its EventStore delivery
/// coordinates. Projections may filter unsupported or non-state-changing payloads before acceptance.
/// </summary>
/// <param name="TenantId">The tenant from the EventStore projection delivery.</param>
/// <param name="WorkItemId">The work-item identity from the EventStore projection delivery.</param>
/// <param name="Sequence">
/// The canonical EventStore envelope <c>SequenceNumber</c>. For accepted state-changing deliveries it
/// keys ordering, deduplication, and freshness; it is not the Works payload <c>Sequence</c> ordinal.
/// </param>
/// <param name="Payload">The decoded Works payload. Rejection payloads may be filtered as no state change.</param>
public sealed record WorkItemRollUpEvent(
    TenantId TenantId,
    WorkItemId WorkItemId,
    long Sequence,
    IEventPayload Payload);
