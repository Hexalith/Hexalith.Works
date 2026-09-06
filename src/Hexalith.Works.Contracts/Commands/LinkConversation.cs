using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Establishes the first opaque Conversation correlation for an existing non-terminal work item.
/// </summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="WorkItemId">The work item to link.</param>
/// <param name="ConversationCorrelationId">The externally-owned Conversation correlation.</param>
[PolymorphicSerialization]
public sealed partial record LinkConversation(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ConversationCorrelationId ConversationCorrelationId);
