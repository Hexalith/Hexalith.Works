using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Pure, deterministic derivation of a single work item's still-pending <c>DateReached</c> awaits from its
/// ordered event stream (Story 4.6, AC #3). This is the smallest host-edge re-readable source the
/// reconciliation-on-recovery scan needs: a suspended item exposes its await set, a resume or any terminal
/// event clears it, so the currently-pending date awaits are exactly the <c>DateReached</c> conditions of
/// the most recent <see cref="WorkItemSuspended"/> that no later <see cref="WorkItemResumed"/> or terminal
/// event has cleared. It reads no clock, store, Dapr, or projection — only the events it is handed.
/// </summary>
/// <remarks>
/// Mirrors the kernel replay semantics (<c>WorkItemState.Apply</c>): <c>WorkItemSuspended</c> sets the await
/// set; <c>WorkItemResumed</c> and the four terminal events (<c>Cancelled</c>/<c>Expired</c>/<c>Completed</c>/
/// <c>Rejected</c>) clear it. Events are consumed in sequence order, so the last suspend/resume/terminal wins
/// and a re-scan of the same stream is idempotent. Non-date awaits (child-completion, external-signal) are
/// not the reminder runtime's concern and are filtered out.
/// </remarks>
public static class PendingDateAwaitProjection
{
    /// <summary>
    /// Reconstructs the still-pending <c>DateReached</c> awaits for one work item from its <paramref name="events"/>,
    /// supplied in ascending sequence order.
    /// </summary>
    public static IReadOnlyList<PendingDateAwait> PendingDateAwaits(IReadOnlyList<IEventPayload> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        TenantId? tenantId = null;
        WorkItemId? workItemId = null;
        IReadOnlyList<AwaitCondition> currentDateAwaits = [];

        foreach (IEventPayload payload in events)
        {
            switch (payload)
            {
                case WorkItemSuspended suspended:
                    tenantId = suspended.TenantId;
                    workItemId = suspended.WorkItemId;
                    currentDateAwaits = [.. suspended.AwaitConditions.Where(static condition => condition.Kind == AwaitConditionKind.DateReached)];
                    break;

                // Resume consumes the await and the kernel clears the whole set; any terminal event closes
                // the item. Either way no date await remains pending.
                case WorkItemResumed:
                case WorkItemCancelled:
                case WorkItemExpired:
                case WorkItemCompleted:
                case WorkItemRejected:
                    currentDateAwaits = [];
                    break;

                default:
                    break;
            }
        }

        if (tenantId is null || workItemId is null || currentDateAwaits.Count == 0)
        {
            return [];
        }

        return [.. currentDateAwaits
            .Where(static condition => condition.Instant is not null)
            .Select(condition => new PendingDateAwait(
                tenantId.Value,
                workItemId.Value,
                condition.Instant!.Value,
                condition.CorrelationKey))];
    }
}
