using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Server.Aggregates;

public static class WorkItemAggregate
{
    public static DomainResult Handle(CreateWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        if (string.IsNullOrWhiteSpace(command.Obligation))
        {
            return DomainResult.Rejection([
                new WorkItemCannotBeCreatedWithoutObligation(command.TenantId, command.WorkItemId),
            ]);
        }

        // Sole enforcement point for the cross-tenant parent invariant; events are trusted on replay.
        if (command.Parent is not null && command.Parent.TenantId != command.TenantId)
        {
            return DomainResult.Rejection([
                new WorkItemCannotReferenceParentFromAnotherTenant(command.TenantId, command.WorkItemId, command.Parent),
            ]);
        }

        var created = new WorkItemCreated(
            command.WorkItemId.Value,
            NextSequence(state),
            command.TenantId,
            command.WorkItemId,
            new Obligation(command.Obligation),
            NormalizeInitialEffort(command.InitialEffort),
            command.Schedule,
            command.Parent,
            command.ExecutorBinding,
            command.ConversationCorrelationId);

        return DomainResult.Success([created]);
    }

    public static DomainResult Handle(AssignWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);
        ArgumentNullException.ThrowIfNull(command.Binding);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Assign);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemAssigned(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId, command.Binding)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Assign)),
        };
    }

    public static DomainResult Handle(QueueWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Queue);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemQueued(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Queue)),
        };
    }

    public static DomainResult Handle(ClaimWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);
        ArgumentNullException.ThrowIfNull(command.Binding);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Claim);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemClaimed(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId, command.Binding)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Claim)),
        };
    }

    public static DomainResult Handle(SuspendWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Suspend);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemSuspended(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Suspend)),
        };
    }

    public static DomainResult Handle(ResumeWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Resume);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemResumed(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Resume)),
        };
    }

    public static DomainResult Handle(CompleteWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Complete);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemCompleted(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Complete)),
        };
    }

    public static DomainResult Handle(CancelWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Cancel);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemCancelled(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Cancel)),
        };
    }

    public static DomainResult Handle(RejectWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Reject, command.Requeue);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemRejected(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId, command.Requeue)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Reject)),
        };
    }

    public static DomainResult Handle(ExpireWorkItem command, WorkItemState? state)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TenantId);
        ArgumentNullException.ThrowIfNull(command.WorkItemId);

        WorkItemStatus from = CurrentStatus(state);
        LifecycleOutcome outcome = WorkItemLifecycle.Decide(from, LifecycleAct.Expire);
        return outcome.Decision switch
        {
            LifecycleDecision.Accept => DomainResult.Success([
                new WorkItemExpired(command.WorkItemId.Value, NextSequence(state), command.TenantId, command.WorkItemId)]),
            LifecycleDecision.NoOp => DomainResult.NoOp(),
            _ => Reject(command.TenantId, command.WorkItemId, from, nameof(LifecycleAct.Expire)),
        };
    }

    // "Not created" (null state or the Unknown pre-creation sentinel) rejects every lifecycle command.
    private static WorkItemStatus CurrentStatus(WorkItemState? state)
        => state?.Status ?? WorkItemStatus.Unknown;

    // Monotonic, in-memory sequence assignment: the next event continues the stream. Replaces the
    // create-only "state is null ? 1 : 2" placeholder so multi-event lifecycles stay ordered.
    private static long NextSequence(WorkItemState? state)
        => (state?.Sequence ?? 0) + 1;

    private static DomainResult Reject(TenantId tenantId, WorkItemId workItemId, WorkItemStatus from, string attemptedAct)
        => DomainResult.Rejection([new WorkItemTransitionRejected(tenantId, workItemId, from, attemptedAct)]);

    private static WorkItemEffort? NormalizeInitialEffort(WorkItemEffort? effort)
        => effort is null || effort.Done == 0
            ? effort
            : new WorkItemEffort(effort.Estimated, effort.Unit);
}
