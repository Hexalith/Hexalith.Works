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
            WorkItemRejected => HasBooleanProperty(bytes, nameof(WorkItemRejected.Requeue)),
            WorkItemCancelled => HasProperty(bytes, nameof(WorkItemCancelled.Sequence)),
            WorkItemInitialEffortRejected fact => fact.Done != 0,
            WorkItemCannotReferenceParentFromAnotherTenant fact => fact.Parent is not null,
            WorkItemCannotReferenceSecondParent fact => fact.ExistingParent is not null && fact.ProposedParent is not null,
            WorkItemConversationLinkRejected fact => fact.ExistingConversationCorrelationId is not null && fact.ProposedConversationCorrelationId is not null,
            WorkItemTreeCycleRejected fact => fact.ProposedParent is not null && fact.CycleWorkItemId is not null,
            WorkItemTreeDepthExceeded fact => fact.ProposedParent is not null && fact.ResultingDepth > fact.MaxDepth,
            WorkItemProgressRejected fact => fact.Reason is not null,
            WorkItemReEstimateRejected fact => fact.Reason is not null,
            WorkItemTransitionRejected fact => fact.AttemptedAct is not null && Enum.IsDefined(fact.FromStatus)
                && HasProperty(bytes, nameof(WorkItemTransitionRejected.FromStatus)),
            _ => true,
        };
        return valid ? payload : throw new InvalidOperationException("Works source evidence has missing required members.");
    }

    private static bool HasProperty(byte[] bytes, string name)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        return document.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasBooleanProperty(byte[] bytes, string name)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonValueKind kind = JsonValueKind.Undefined;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) kind = property.Value.ValueKind;
        return kind is JsonValueKind.True or JsonValueKind.False;
    }
}
