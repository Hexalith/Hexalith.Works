using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.Recovery;

/// <summary>Checks the closed committed Works shape without changing historical legacy readers.</summary>
internal static class WorksRecoveryEventDecoder
{
    /// <summary>Decodes required contract members before any recovery no-op or domain translation.</summary>
    internal static IEventPayload Decode(string eventType, byte[] bytes)
    {
        IEventPayload payload = WorksEventDecoder.Decode(eventType, bytes)
            ?? throw new InvalidOperationException("Works source evidence is unknown or undecodable.");
        if (!string.Equals(eventType, payload.GetType().Name, StringComparison.Ordinal)
            && !string.Equals(eventType, payload.GetType().FullName, StringComparison.Ordinal))
            throw new InvalidOperationException("Works source evidence has a foreign event type.");
        // State-changing payloads declare Sequence; command-rejection payloads do not.
        using JsonDocument document = JsonDocument.Parse(bytes);
        if (!WorksEventScalarMembers.HasPositiveSequence(payload, document.RootElement))
            throw new InvalidOperationException("Works source evidence requires a positive domain sequence.");
        if (!WorksEventScalarMembers.HasRequiredMembers(payload, document.RootElement))
            throw new InvalidOperationException("Works source evidence has missing required members.");
        bool valid = payload switch
        {
            WorkItemCreated fact => fact.Obligation is not null,
            ChildSpawned fact => fact.ChildWorkItemId is not null && fact.Obligation is not null,
            WorkItemRescheduled fact => fact.Schedule is not null,
            WorkItemAssigned fact => fact.Binding is not null,
            WorkItemClaimed fact => fact.Binding is not null,
            ProgressReported fact => fact.Unit is not null,
            ReEstimated fact => fact.Unit is not null,
            ConversationLinked fact => fact.ConversationCorrelationId is not null,
            WorkItemSuspended fact => fact.AwaitConditions is { Count: > 0 },
            WorkItemInitialEffortRejected fact => fact.Done != 0,
            WorkItemCannotReferenceParentFromAnotherTenant fact => fact.Parent is not null,
            WorkItemCannotReferenceSecondParent fact => fact.ExistingParent is not null && fact.ProposedParent is not null,
            WorkItemConversationLinkRejected fact => fact.ExistingConversationCorrelationId is not null && fact.ProposedConversationCorrelationId is not null,
            WorkItemTreeCycleRejected fact => fact.ProposedParent is not null && fact.CycleWorkItemId is not null,
            WorkItemTreeDepthExceeded fact => fact.ProposedParent is not null && fact.ResultingDepth > fact.MaxDepth,
            WorkItemProgressRejected fact => fact.Reason is not null,
            WorkItemReEstimateRejected fact => fact.Reason is not null,
            WorkItemTransitionRejected fact => fact.AttemptedAct is not null && Enum.IsDefined(fact.FromStatus),
            _ => true,
        };
        return valid ? payload : throw new InvalidOperationException("Works source evidence has missing required members.");
    }
}
