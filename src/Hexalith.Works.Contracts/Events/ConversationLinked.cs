using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

/// <summary>
/// Records the first authoritative opaque Conversation correlation for a work item.
/// </summary>
/// <param name="AggregateId">The EventStore aggregate identifier.</param>
/// <param name="Sequence">The state-changing payload ordinal.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="WorkItemId">The linked work item.</param>
/// <param name="ConversationCorrelationId">The externally-owned Conversation correlation.</param>
[PolymorphicSerialization]
public sealed partial record ConversationLinked(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId,
    ConversationCorrelationId ConversationCorrelationId) : IEventPayload;
