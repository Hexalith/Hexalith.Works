using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

[PolymorphicSerialization]
public sealed partial record ProgressReported(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId,
    decimal DoneDelta,
    Unit Unit,
    string? Note = null) : IEventPayload;
