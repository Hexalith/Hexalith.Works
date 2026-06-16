using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Projections.Models;

public sealed record WorkItemRollUpEvent(
    TenantId TenantId,
    WorkItemId WorkItemId,
    long Sequence,
    IEventPayload Payload);
