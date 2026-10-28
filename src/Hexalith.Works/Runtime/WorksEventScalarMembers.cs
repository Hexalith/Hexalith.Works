using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;

namespace Hexalith.Works.Runtime;

/// <summary>Checks required scalar evidence without tightening historical relationship sanitization.</summary>
internal static class WorksEventScalarMembers
{
    /// <summary>Requires an explicit positive domain ordinal on every state-changing event.</summary>
    internal static bool HasPositiveSequence(IEventPayload payload, JsonElement root)
        => payload.GetType().GetProperty(nameof(WorkItemCreated.Sequence)) is not { } sequence
            || (HasProperty(root, nameof(WorkItemCreated.Sequence))
                && sequence.GetValue(payload) is long ordinal && ordinal > 0);

    /// <summary>Distinguishes required scalar values from defaults synthesized for omitted JSON members.</summary>
    internal static bool HasRequiredMembers(IEventPayload payload, JsonElement root)
        => payload switch
        {
            ProgressReported => HasProperty(root, nameof(ProgressReported.DoneDelta)),
            ReEstimated => HasProperty(root, nameof(ReEstimated.Estimated)),
            WorkItemRejected => HasBooleanProperty(root, nameof(WorkItemRejected.Requeue)),
            WorkItemTreeDepthExceeded => HasProperty(root, nameof(WorkItemTreeDepthExceeded.MaxDepth))
                && HasProperty(root, nameof(WorkItemTreeDepthExceeded.ResultingDepth)),
            WorkItemTransitionRejected => HasProperty(root, nameof(WorkItemTransitionRejected.FromStatus)),
            WorkItemInitialEffortRejected => HasProperty(root, nameof(WorkItemInitialEffortRejected.Done)),
            _ => true,
        };

    private static bool HasProperty(JsonElement root, string name)
        => root.EnumerateObject().Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool HasBooleanProperty(JsonElement root, string name)
    {
        JsonValueKind kind = JsonValueKind.Undefined;
        foreach (JsonProperty property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) kind = property.Value.ValueKind;
        return kind is JsonValueKind.True or JsonValueKind.False;
    }
}
