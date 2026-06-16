using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

public sealed record WorkItemCompleted(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId) : IEventPayload;
