using Hexalith.EventStore.Contracts.Reminders;

namespace Hexalith.Works.Contracts.ValueObjects;

/// <summary>An upstream-resolved UTC expiry and deterministic schedule witness; contains no expiry policy.</summary>
/// <param name="EffectiveExpiryUtc">The resolved zero-offset expiry instant.</param>
/// <param name="ScheduleToken">The SDK token binding tenant, work item, instant, and revision.</param>
/// <param name="Revision">The increasing schedule revision.</param>
public sealed record WorkItemExpirySchedule(DateTimeOffset EffectiveExpiryUtc, string ScheduleToken, long Revision)
{
    /// <summary>Checks the same increasing, non-dropping replacement rule for commands and committed evidence.</summary>
    public static bool IsValidReplacement(WorkItemExpirySchedule? current, WorkItemExpirySchedule? replacement, TenantId tenant, WorkItemId item)
        => replacement is null ? current is null
            : replacement.IsValidFor(tenant, item) && replacement.Revision > (current?.Revision ?? 0);

    /// <summary>Validates the supplied witness against its addressed domain identity without reading a clock.</summary>
    public bool IsValidFor(TenantId tenant, WorkItemId item)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(item);
        return EffectiveExpiryUtc.Offset == TimeSpan.Zero && Revision > 0
            && string.Equals(ScheduleToken, ReminderIdentityCodec.ComputeScheduleToken(
                tenant.Value.ToLowerInvariant(), item.Value, EffectiveExpiryUtc, Revision), StringComparison.Ordinal);
    }
}
