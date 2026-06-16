using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

public sealed record WorkItemSuspended(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId) : IEventPayload;
