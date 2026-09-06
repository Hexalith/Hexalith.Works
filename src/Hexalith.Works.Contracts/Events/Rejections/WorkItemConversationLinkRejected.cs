using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

/// <summary>
/// Refuses replacement of a work item's authoritative Conversation correlation.
/// </summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="WorkItemId">The work item whose correlation is retained.</param>
/// <param name="ExistingConversationCorrelationId">The authoritative correlation already stored.</param>
/// <param name="ProposedConversationCorrelationId">The conflicting correlation proposed by the command.</param>
[PolymorphicSerialization]
public sealed partial record WorkItemConversationLinkRejected(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ConversationCorrelationId ExistingConversationCorrelationId,
    ConversationCorrelationId ProposedConversationCorrelationId) : IRejectionEvent;
