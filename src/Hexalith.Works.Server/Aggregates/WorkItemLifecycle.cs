using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Server.Aggregates;

/// <summary>
/// The single, pure source of truth for the work-item lifecycle state machine. Given a current
/// <see cref="WorkItemStatus"/> and a lifecycle <see cref="LifecycleAct"/>, it decides whether the
/// transition is accepted (and to which target status), rejected, or an idempotent no-op. This table
/// is mirrored 1:1 by docs/lifecycle-transition-matrix.md; later lifecycle stories consult it rather
/// than redefining transitions locally. The table reads no clock, RNG, or I/O.
/// </summary>
internal static class WorkItemLifecycle
{
    /// <summary>
    /// Decides the outcome of applying <paramref name="act"/> to an item currently in
    /// <paramref name="from"/>. <paramref name="requeue"/> is only consulted for
    /// <see cref="LifecycleAct.Reject"/>: from <c>Assigned</c> it selects the resting status
    /// (<c>Queued</c> when requeue, terminal <c>Rejected</c> otherwise); from the terminal
    /// <c>Rejected</c> status only a non-requeue reject is the idempotent duplicate (no-op).
    /// </summary>
    public static LifecycleOutcome Decide(WorkItemStatus from, LifecycleAct act, bool requeue = true)
        => from switch
        {
            WorkItemStatus.Created => act switch
            {
                LifecycleAct.Assign => LifecycleOutcome.Accept(WorkItemStatus.Assigned),
                LifecycleAct.Queue => LifecycleOutcome.Accept(WorkItemStatus.Queued),
                LifecycleAct.Cancel => LifecycleOutcome.Accept(WorkItemStatus.Cancelled),
                LifecycleAct.Expire => LifecycleOutcome.Accept(WorkItemStatus.Expired),
                _ => LifecycleOutcome.Reject,
            },
            WorkItemStatus.Assigned => act switch
            {
                LifecycleAct.Assign => LifecycleOutcome.Accept(WorkItemStatus.Assigned),
                LifecycleAct.Queue => LifecycleOutcome.Accept(WorkItemStatus.Queued),
                LifecycleAct.Claim => LifecycleOutcome.Accept(WorkItemStatus.InProgress),
                LifecycleAct.Cancel => LifecycleOutcome.Accept(WorkItemStatus.Cancelled),
                LifecycleAct.Reject => LifecycleOutcome.Accept(requeue ? WorkItemStatus.Queued : WorkItemStatus.Rejected),
                LifecycleAct.Expire => LifecycleOutcome.Accept(WorkItemStatus.Expired),
                _ => LifecycleOutcome.Reject,
            },
            WorkItemStatus.Queued => act switch
            {
                LifecycleAct.Assign => LifecycleOutcome.Accept(WorkItemStatus.Assigned),
                LifecycleAct.Claim => LifecycleOutcome.Accept(WorkItemStatus.InProgress),
                LifecycleAct.Cancel => LifecycleOutcome.Accept(WorkItemStatus.Cancelled),
                LifecycleAct.Expire => LifecycleOutcome.Accept(WorkItemStatus.Expired),
                _ => LifecycleOutcome.Reject,
            },
            WorkItemStatus.InProgress => act switch
            {
                LifecycleAct.Suspend => LifecycleOutcome.Accept(WorkItemStatus.Suspended),
                LifecycleAct.Complete => LifecycleOutcome.Accept(WorkItemStatus.Completed),
                LifecycleAct.Cancel => LifecycleOutcome.Accept(WorkItemStatus.Cancelled),
                LifecycleAct.Expire => LifecycleOutcome.Accept(WorkItemStatus.Expired),
                _ => LifecycleOutcome.Reject,
            },
            WorkItemStatus.Suspended => act switch
            {
                LifecycleAct.Resume => LifecycleOutcome.Accept(WorkItemStatus.InProgress),
                LifecycleAct.Complete => LifecycleOutcome.Accept(WorkItemStatus.Completed),
                LifecycleAct.Cancel => LifecycleOutcome.Accept(WorkItemStatus.Cancelled),
                LifecycleAct.Expire => LifecycleOutcome.Accept(WorkItemStatus.Expired),
                _ => LifecycleOutcome.Reject,
            },

            // Terminal states: every act is rejected except the exact-duplicate terminal command,
            // which is the idempotent no-op (AC #4). Already-terminal items are unaffected by
            // cancel/expire — the basis for the idempotent reactor cascade (Story 3.6).
            WorkItemStatus.Completed => act == LifecycleAct.Complete ? LifecycleOutcome.NoOp : LifecycleOutcome.Reject,
            WorkItemStatus.Cancelled => act == LifecycleAct.Cancel ? LifecycleOutcome.NoOp : LifecycleOutcome.Reject,
            WorkItemStatus.Rejected => act == LifecycleAct.Reject && !requeue ? LifecycleOutcome.NoOp : LifecycleOutcome.Reject,
            WorkItemStatus.Expired => act == LifecycleAct.Expire ? LifecycleOutcome.NoOp : LifecycleOutcome.Reject,

            // Unknown / null state = "not created": every lifecycle command is rejected.
            _ => LifecycleOutcome.Reject,
        };
}
