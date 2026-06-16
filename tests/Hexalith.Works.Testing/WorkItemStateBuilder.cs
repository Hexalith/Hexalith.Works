using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Testing;

/// <summary>
/// Pure testing double (the architecture's planned <c>WorkItemBuilder</c>): replays a canonical event
/// sequence to arrange a <see cref="WorkItemState"/> in any of the nine lifecycle statuses, so
/// transition tests can set up their "from" state without duplicating replay plumbing. Contracts-only,
/// no infrastructure — events are applied with a monotonic sequence exactly as the EventStore replay
/// convention would.
/// </summary>
public static class WorkItemStateBuilder
{
    /// <summary>
    /// Builds a <see cref="WorkItemState"/> resting in <paramref name="status"/> by replaying the
    /// shortest legal event path that reaches it. <paramref name="binding"/> is used for the paths that
    /// involve an executor (Assigned / InProgress / Suspended / Completed / Rejected); a default binding
    /// is supplied when none is given.
    /// </summary>
    public static WorkItemState InStatus(
        WorkItemStatus status,
        TenantId tenantId,
        WorkItemId workItemId,
        ExecutorBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(workItemId);

        ExecutorBinding resolvedBinding = binding ?? DefaultBinding();
        string aggregateId = workItemId.Value;
        var state = new WorkItemState();
        long sequence = 0;

        if (status == WorkItemStatus.Unknown)
        {
            return state;
        }

        state.Apply(new WorkItemCreated(aggregateId, ++sequence, tenantId, workItemId, new Obligation("Lifecycle test work item")));

        switch (status)
        {
            case WorkItemStatus.Created:
                break;

            case WorkItemStatus.Assigned:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                break;

            case WorkItemStatus.Queued:
                state.Apply(new WorkItemQueued(aggregateId, ++sequence, tenantId, workItemId));
                break;

            case WorkItemStatus.InProgress:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                state.Apply(new WorkItemClaimed(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                break;

            case WorkItemStatus.Suspended:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                state.Apply(new WorkItemClaimed(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                state.Apply(new WorkItemSuspended(aggregateId, ++sequence, tenantId, workItemId, [DefaultAwaitCondition()]));
                break;

            case WorkItemStatus.Completed:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                state.Apply(new WorkItemClaimed(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                state.Apply(new WorkItemCompleted(aggregateId, ++sequence, tenantId, workItemId));
                break;

            case WorkItemStatus.Cancelled:
                state.Apply(new WorkItemCancelled(aggregateId, ++sequence, tenantId, workItemId));
                break;

            case WorkItemStatus.Rejected:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, tenantId, workItemId, resolvedBinding));
                state.Apply(new WorkItemRejected(aggregateId, ++sequence, tenantId, workItemId, Requeue: false));
                break;

            case WorkItemStatus.Expired:
                state.Apply(new WorkItemExpired(aggregateId, ++sequence, tenantId, workItemId));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported target lifecycle status.");
        }

        return state;
    }

    /// <summary>A known, valid executor binding for arranging states that require one.</summary>
    public static ExecutorBinding DefaultBinding()
        => new(new PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Administer);

    public static AwaitCondition DefaultAwaitCondition()
        => AwaitCondition.ExternalSignal("default-resume-signal");
}
