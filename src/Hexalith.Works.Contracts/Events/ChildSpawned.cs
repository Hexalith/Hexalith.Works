using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

[PolymorphicSerialization]
public sealed partial record ChildSpawned(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemId ChildWorkItemId,
    Obligation Obligation,
    WorkItemEffort? InitialEffort = null,
    WorkItemSchedule? Schedule = null,
    ExecutorBinding? ExecutorBinding = null,
    ConversationCorrelationId? ConversationCorrelationId = null,
    bool SuspendParentUntilChildCompletes = false) : IEventPayload;
