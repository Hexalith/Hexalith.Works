using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Hexalith.Works.Testing;
using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>The non-reject lifecycle acts, used as the column axis of the data-driven matrix tests.</summary>
public enum Act
{
    Assign,
    Queue,
    Claim,
    Suspend,
    Resume,
    Complete,
    Cancel,
    Expire,
}

/// <summary>The expected outcome of a single transition-matrix cell.</summary>
public enum Expect
{
    Accept,
    Reject,
    NoOp,
}

public sealed class WorkItemLifecycleTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly ExecutorBinding Binding = WorkItemStateBuilder.DefaultBinding();
    private static readonly AwaitCondition ResumeSignal = WorkItemStateBuilder.DefaultAwaitCondition();

    // Every (status, non-reject-command) cell of docs/lifecycle-transition-matrix.md. The Reject act is
    // exercised separately because it is the only flag-dependent column.
    [Theory]
    // From Created
    [InlineData(WorkItemStatus.Created, Act.Assign, Expect.Accept, WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Created, Act.Queue, Expect.Accept, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Created, Act.Claim, Expect.Reject, WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Created, Act.Suspend, Expect.Reject, WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Created, Act.Resume, Expect.Reject, WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Created, Act.Complete, Expect.Reject, WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Created, Act.Cancel, Expect.Accept, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Created, Act.Expire, Expect.Accept, WorkItemStatus.Expired)]
    // From Assigned
    [InlineData(WorkItemStatus.Assigned, Act.Assign, Expect.Accept, WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Assigned, Act.Queue, Expect.Accept, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Assigned, Act.Claim, Expect.Accept, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Assigned, Act.Suspend, Expect.Reject, WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Assigned, Act.Resume, Expect.Reject, WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Assigned, Act.Complete, Expect.Reject, WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Assigned, Act.Cancel, Expect.Accept, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Assigned, Act.Expire, Expect.Accept, WorkItemStatus.Expired)]
    // From Queued
    [InlineData(WorkItemStatus.Queued, Act.Assign, Expect.Accept, WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued, Act.Queue, Expect.Reject, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Queued, Act.Claim, Expect.Accept, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Queued, Act.Suspend, Expect.Reject, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Queued, Act.Resume, Expect.Reject, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Queued, Act.Complete, Expect.Reject, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Queued, Act.Cancel, Expect.Accept, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Queued, Act.Expire, Expect.Accept, WorkItemStatus.Expired)]
    // From InProgress
    [InlineData(WorkItemStatus.InProgress, Act.Assign, Expect.Reject, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.InProgress, Act.Queue, Expect.Reject, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.InProgress, Act.Claim, Expect.Reject, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.InProgress, Act.Suspend, Expect.Accept, WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.InProgress, Act.Resume, Expect.Reject, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.InProgress, Act.Complete, Expect.Accept, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.InProgress, Act.Cancel, Expect.Accept, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.InProgress, Act.Expire, Expect.Accept, WorkItemStatus.Expired)]
    // From Suspended
    [InlineData(WorkItemStatus.Suspended, Act.Assign, Expect.Reject, WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Suspended, Act.Queue, Expect.Reject, WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Suspended, Act.Claim, Expect.Reject, WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Suspended, Act.Suspend, Expect.Reject, WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Suspended, Act.Resume, Expect.Accept, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended, Act.Complete, Expect.Accept, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Suspended, Act.Cancel, Expect.Accept, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Suspended, Act.Expire, Expect.Accept, WorkItemStatus.Expired)]
    // From Completed (terminal): only the duplicate Complete is a no-op
    [InlineData(WorkItemStatus.Completed, Act.Assign, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, Act.Queue, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, Act.Claim, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, Act.Suspend, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, Act.Resume, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, Act.Complete, Expect.NoOp, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, Act.Cancel, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, Act.Expire, Expect.Reject, WorkItemStatus.Completed)]
    // From Cancelled (terminal): only the duplicate Cancel is a no-op
    [InlineData(WorkItemStatus.Cancelled, Act.Assign, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, Act.Queue, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, Act.Claim, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, Act.Suspend, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, Act.Resume, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, Act.Complete, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, Act.Cancel, Expect.NoOp, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, Act.Expire, Expect.Reject, WorkItemStatus.Cancelled)]
    // From Rejected (terminal): every non-reject act is rejected (the Reject-act no-op is tested below)
    [InlineData(WorkItemStatus.Rejected, Act.Assign, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, Act.Queue, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, Act.Claim, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, Act.Suspend, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, Act.Resume, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, Act.Complete, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, Act.Cancel, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, Act.Expire, Expect.Reject, WorkItemStatus.Rejected)]
    // From Expired (terminal): only the duplicate Expire is a no-op
    [InlineData(WorkItemStatus.Expired, Act.Assign, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, Act.Queue, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, Act.Claim, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, Act.Suspend, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, Act.Resume, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, Act.Complete, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, Act.Cancel, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, Act.Expire, Expect.NoOp, WorkItemStatus.Expired)]
    public void Transition_cell_matches_matrix(WorkItemStatus from, Act act, Expect expect, WorkItemStatus target)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(from, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = Invoke(act, state);

        AssertOutcome(result, expect, from, target, state, sequenceBefore);
    }

    // The Reject act across all nine statuses, for both requeue values (AC #5 + terminal idempotency).
    [Theory]
    [InlineData(WorkItemStatus.Created, true, Expect.Reject, WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Created, false, Expect.Reject, WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned, true, Expect.Accept, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Assigned, false, Expect.Accept, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Queued, true, Expect.Reject, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Queued, false, Expect.Reject, WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress, true, Expect.Reject, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.InProgress, false, Expect.Reject, WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended, true, Expect.Reject, WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Suspended, false, Expect.Reject, WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Completed, true, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Completed, false, Expect.Reject, WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled, true, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Cancelled, false, Expect.Reject, WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected, true, Expect.Reject, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Rejected, false, Expect.NoOp, WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired, true, Expect.Reject, WorkItemStatus.Expired)]
    [InlineData(WorkItemStatus.Expired, false, Expect.Reject, WorkItemStatus.Expired)]
    public void Reject_cell_matches_matrix(WorkItemStatus from, bool requeue, Expect expect, WorkItemStatus target)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(from, Tenant, Item);
        long sequenceBefore = state.Sequence;

        var result = WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item, requeue), state);

        switch (expect)
        {
            case Expect.Accept:
                result.IsSuccess.ShouldBeTrue();
                WorkItemRejected rejected = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRejected>();
                rejected.Requeue.ShouldBe(requeue);
                rejected.Sequence.ShouldBe(sequenceBefore + 1);
                state.Apply(rejected);
                state.Status.ShouldBe(target);
                state.Sequence.ShouldBe(sequenceBefore + 1);
                break;

            case Expect.Reject:
                result.IsRejection.ShouldBeTrue();
                WorkItemTransitionRejected transitionRejected = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
                transitionRejected.FromStatus.ShouldBe(from);
                transitionRejected.AttemptedAct.ShouldBe("Reject");
                state.Status.ShouldBe(from);
                state.Sequence.ShouldBe(sequenceBefore);
                break;

            case Expect.NoOp:
                result.IsNoOp.ShouldBeTrue();
                result.Events.ShouldBeEmpty();
                state.Status.ShouldBe(from);
                state.Sequence.ShouldBe(sequenceBefore);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(expect));
        }
    }

    [Theory]
    [InlineData(Act.Assign)]
    [InlineData(Act.Queue)]
    [InlineData(Act.Claim)]
    [InlineData(Act.Suspend)]
    [InlineData(Act.Resume)]
    [InlineData(Act.Complete)]
    [InlineData(Act.Cancel)]
    [InlineData(Act.Expire)]
    public void Lifecycle_command_against_uncreated_state_is_rejected(Act act)
    {
        DomainResult fromNull = Invoke(act, null);
        fromNull.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = fromNull.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.Unknown);

        DomainResult fromUnknown = Invoke(act, new WorkItemState());
        fromUnknown.IsRejection.ShouldBeTrue();
        fromUnknown.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
    }

    [Fact]
    public void Reject_against_uncreated_state_is_rejected()
    {
        var result = WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item), null);
        result.IsRejection.ShouldBeTrue();
        result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
    }

    // AC #1 — Created accepts assign/queue; an unsupported act from Created is rejected.
    [Fact]
    public void Created_accepts_assign_and_queue_and_rejects_unsupported_acts()
    {
        WorkItemState assigned = Replay(WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item),
            WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, Binding), Created()));
        assigned.Status.ShouldBe(WorkItemStatus.Assigned);

        WorkItemState queued = Replay(Created(),
            WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), Created()));
        queued.Status.ShouldBe(WorkItemStatus.Queued);

        WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), Created()).IsRejection.ShouldBeTrue();
    }

    // AC #2 — Assigned <-> Queued requeue/direct-assign, and both reach InProgress via Claim.
    [Fact]
    public void Assigned_and_queued_interchange_and_both_claim_into_progress()
    {
        Replay(Assigned(), WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), Assigned()))
            .Status.ShouldBe(WorkItemStatus.Queued);

        Replay(Queued(), WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, Binding), Queued()))
            .Status.ShouldBe(WorkItemStatus.Assigned);

        Replay(Assigned(), WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), Assigned()))
            .Status.ShouldBe(WorkItemStatus.InProgress);

        Replay(Queued(), WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), Queued()))
            .Status.ShouldBe(WorkItemStatus.InProgress);
    }

    // AC #3 — InProgress -> Suspended -> InProgress (resume), with no resting "Resumed" status anywhere.
    [Fact]
    public void Suspend_then_resume_returns_to_in_progress_with_no_resumed_status()
    {
        WorkItemState inProgress = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Item);

        WorkItemState suspended = Replay(inProgress, WorkItemAggregate.Handle(SuspendCommand(), inProgress));
        suspended.Status.ShouldBe(WorkItemStatus.Suspended);

        WorkItemState resumed = Replay(suspended, WorkItemAggregate.Handle(ResumeCommand(), suspended));
        resumed.Status.ShouldBe(WorkItemStatus.InProgress);

        Enum.GetNames<WorkItemStatus>().ShouldNotContain("Resumed");
    }

    // AC #4 — from a terminal status every command is rejected except the listed idempotent duplicate.
    [Fact]
    public void Terminal_status_rejects_other_commands_but_no_ops_the_listed_duplicate()
    {
        WorkItemState completed = WorkItemStateBuilder.InStatus(WorkItemStatus.Completed, Tenant, Item);
        WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Item), completed).IsRejection.ShouldBeTrue();
        WorkItemAggregate.Handle(new CompleteWorkItem(Tenant, Item), completed).IsNoOp.ShouldBeTrue();

        WorkItemState cancelled = WorkItemStateBuilder.InStatus(WorkItemStatus.Cancelled, Tenant, Item);
        WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Item), cancelled).IsNoOp.ShouldBeTrue();

        WorkItemState expired = WorkItemStateBuilder.InStatus(WorkItemStatus.Expired, Tenant, Item);
        WorkItemAggregate.Handle(new ExpireWorkItem(Tenant, Item), expired).IsNoOp.ShouldBeTrue();

        WorkItemState rejected = WorkItemStateBuilder.InStatus(WorkItemStatus.Rejected, Tenant, Item);
        WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item, Requeue: false), rejected).IsNoOp.ShouldBeTrue();
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    public void Cancel_from_each_non_terminal_status_emits_cancelled_and_replay_rests_terminal(WorkItemStatus status)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Item), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemCancelled cancelled = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCancelled>();
        cancelled.Sequence.ShouldBe(sequenceBefore + 1);
        state.Apply(cancelled);
        state.Status.ShouldBe(WorkItemStatus.Cancelled);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    public void Expire_from_each_non_terminal_status_emits_expired_and_replay_rests_terminal(WorkItemStatus status)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ExpireWorkItem(Tenant, Item), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemExpired expired = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemExpired>();
        expired.Sequence.ShouldBe(sequenceBefore + 1);
        state.Apply(expired);
        state.Status.ShouldBe(WorkItemStatus.Expired);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Theory]
    [InlineData(nameof(ReportProgress))]
    [InlineData(nameof(RescheduleWorkItem))]
    [InlineData(nameof(AssignWorkItem))]
    [InlineData(nameof(QueueWorkItem))]
    [InlineData(nameof(ClaimWorkItem))]
    [InlineData(nameof(SuspendWorkItem))]
    [InlineData(nameof(ResumeWorkItem))]
    [InlineData(nameof(CompleteWorkItem))]
    [InlineData(nameof(RejectWorkItem))]
    [InlineData(nameof(ExpireWorkItem))]
    public void Commands_after_cancel_are_rejected_and_leave_state_unchanged(string commandName)
    {
        WorkItemState state = RichCancelledState();
        long sequenceBefore = state.Sequence;
        WorkItemEffort effortBefore = state.InitialEffort.ShouldNotBeNull();
        WorkItemSchedule scheduleBefore = state.Schedule.ShouldNotBeNull();
        ExecutorBinding bindingBefore = state.ExecutorBinding.ShouldNotBeNull();

        DomainResult result = InvokeNamed(commandName, state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.Cancelled);
        state.Status.ShouldBe(WorkItemStatus.Cancelled);
        state.InitialEffort.ShouldBe(effortBefore);
        state.Schedule.ShouldBe(scheduleBefore);
        state.ExecutorBinding.ShouldBe(bindingBefore);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Theory]
    [InlineData(WorkItemStatus.Completed, nameof(ReEstimate))]
    [InlineData(WorkItemStatus.Completed, nameof(RescheduleWorkItem))]
    [InlineData(WorkItemStatus.Cancelled, nameof(ReEstimate))]
    [InlineData(WorkItemStatus.Cancelled, nameof(RescheduleWorkItem))]
    [InlineData(WorkItemStatus.Rejected, nameof(ReEstimate))]
    [InlineData(WorkItemStatus.Rejected, nameof(RescheduleWorkItem))]
    [InlineData(WorkItemStatus.Expired, nameof(ReEstimate))]
    [InlineData(WorkItemStatus.Expired, nameof(RescheduleWorkItem))]
    public void Planning_acts_from_terminal_statuses_are_transition_rejections(WorkItemStatus status, string commandName)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = InvokeNamed(commandName, state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe(commandName);
        rejection.GetType().GetProperty("Sequence").ShouldBeNull();
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Noop_results_have_no_events_and_rejections_never_mix_success_payloads()
    {
        WorkItemState completed = WorkItemStateBuilder.InStatus(WorkItemStatus.Completed, Tenant, Item);

        DomainResult noop = WorkItemAggregate.Handle(new CompleteWorkItem(Tenant, Item), completed);
        noop.IsNoOp.ShouldBeTrue();
        noop.Events.ShouldBeEmpty();

        DomainResult rejection = WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Item), completed);
        rejection.IsRejection.ShouldBeTrue();
        rejection.IsSuccess.ShouldBeFalse();
        rejection.Events.ShouldHaveSingleItem().ShouldBeAssignableTo<IRejectionEvent>();
        rejection.Events.Any(e => e is WorkItemCompleted or WorkItemCancelled or WorkItemRejected or WorkItemExpired).ShouldBeFalse();
        completed.Status.ShouldBe(WorkItemStatus.Completed);
    }

    // AC #5 — reject-with-requeue rests at Queued; reject-without-requeue reaches terminal Rejected.
    [Fact]
    public void Reject_requeue_rests_at_queued_and_non_requeue_reaches_terminal_rejected()
    {
        WorkItemState requeued = Assigned();
        WorkItemRejected requeueEvent = WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item, Requeue: true), requeued)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRejected>();
        requeueEvent.Requeue.ShouldBeTrue();
        requeued.Apply(requeueEvent);
        requeued.Status.ShouldBe(WorkItemStatus.Queued);

        WorkItemState terminal = Assigned();
        WorkItemRejected terminalEvent = WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item, Requeue: false), terminal)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRejected>();
        terminalEvent.Requeue.ShouldBeFalse();
        terminal.Apply(terminalEvent);
        terminal.Status.ShouldBe(WorkItemStatus.Rejected);
    }

    [Fact]
    public void Reject_without_explicit_requeue_uses_default_requeue_and_rests_at_queued()
    {
        WorkItemState state = Assigned();
        long sequenceBefore = state.Sequence;

        WorkItemRejected rejected = WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRejected>();

        rejected.Requeue.ShouldBeTrue();
        rejected.Sequence.ShouldBe(sequenceBefore + 1);
        state.Apply(rejected);
        state.Status.ShouldBe(WorkItemStatus.Queued);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    // AC #4/#5 — Reject is legal only from Assigned. The data-driven matrix Theory deliberately excludes
    // the flag-dependent Reject act, so this is the only place that proves a default (requeue) reject from
    // every other status is a WorkItemTransitionRejected — never a WorkItemRejected and never a reopen.
    // In particular, a requeue reject of an already-Rejected item must NOT no-op or reopen the terminal item
    // (matrix: "Rejected + Reject(requeue) = R"); the closed no-op list only covers Reject(requeue: false).
    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Default_reject_from_any_non_assigned_status_is_a_transition_rejection_and_never_reopens(WorkItemStatus status)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state.Sequence;

        // RejectWorkItem(Tenant, Item) defaults Requeue = true.
        DomainResult result = WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item), state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.IsNoOp.ShouldBeFalse();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        result.Events.Any(e => e is WorkItemRejected).ShouldBeFalse();
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    // Sequence tracking: a multi-event lifecycle assigns monotonically increasing sequence numbers.
    [Fact]
    public void Multi_event_lifecycle_assigns_monotonic_sequences()
    {
        var state = new WorkItemState();

        WorkItemCreated created = WorkItemAggregate.Handle(CreateCommand(), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCreated>();
        created.Sequence.ShouldBe(1);
        state.Apply(created);
        state.Sequence.ShouldBe(1);

        WorkItemAssigned assigned = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, Binding), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        assigned.Sequence.ShouldBe(2);
        state.Apply(assigned);

        WorkItemClaimed claimed = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        claimed.Sequence.ShouldBe(3);
        state.Apply(claimed);

        WorkItemSuspended suspended = WorkItemAggregate.Handle(SuspendCommand(), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemSuspended>();
        suspended.Sequence.ShouldBe(4);
        state.Apply(suspended);
        state.Sequence.ShouldBe(4);
    }

    [Fact]
    public void Rejection_does_not_advance_the_sequence()
    {
        WorkItemState created = Created();
        long before = created.Sequence;

        // Resume is illegal from Created -> rejection, which must not advance the stream sequence.
        WorkItemAggregate.Handle(ResumeCommand(), created).IsRejection.ShouldBeTrue();
        created.Sequence.ShouldBe(before);

        // The next legal command therefore still claims sequence before + 1.
        WorkItemAssigned assigned = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, Binding), created)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        assigned.Sequence.ShouldBe(before + 1);
    }

    private static WorkItemState Created() => WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item);

    private static WorkItemState Assigned() => WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item);

    private static WorkItemState Queued() => WorkItemStateBuilder.InStatus(WorkItemStatus.Queued, Tenant, Item);

    private static CreateWorkItem CreateCommand() => new(Tenant, Item, "Prepare the lifecycle work item");

    private static SuspendWorkItem SuspendCommand() => new(Tenant, Item, [ResumeSignal]);

    private static ResumeWorkItem ResumeCommand() => new(Tenant, Item, ResumeSignal);

    private static WorkItemState RichCancelledState()
    {
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(
            Item.Value,
            1,
            Tenant,
            Item,
            new Obligation("Cancelled work item"),
            new WorkItemEffort(8m, new Unit("hour"), 2m),
            new WorkItemSchedule(Priority.High, new DateOnly(2026, 7, 15)),
            null,
            Binding));
        state.Apply(new WorkItemCancelled(Item.Value, 2, Tenant, Item));
        return state;
    }

    private static DomainResult Invoke(Act act, WorkItemState? state)
        => act switch
        {
            Act.Assign => WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, Binding), state),
            Act.Queue => WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), state),
            Act.Claim => WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), state),
            Act.Suspend => WorkItemAggregate.Handle(SuspendCommand(), state),
            Act.Resume => WorkItemAggregate.Handle(ResumeCommand(), state),
            Act.Complete => WorkItemAggregate.Handle(new CompleteWorkItem(Tenant, Item), state),
            Act.Cancel => WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Item), state),
            Act.Expire => WorkItemAggregate.Handle(new ExpireWorkItem(Tenant, Item), state),
            _ => throw new ArgumentOutOfRangeException(nameof(act)),
        };

    private static DomainResult InvokeNamed(string commandName, WorkItemState state)
        => commandName switch
        {
            nameof(ReportProgress) => WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 1m, new Unit("hour")), state),
            nameof(ReEstimate) => WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 5m, new Unit("hour")), state),
            nameof(RescheduleWorkItem) => WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, new WorkItemSchedule(Priority.Normal)), state),
            nameof(AssignWorkItem) => WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, Binding), state),
            nameof(QueueWorkItem) => WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), state),
            nameof(ClaimWorkItem) => WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), state),
            nameof(SuspendWorkItem) => WorkItemAggregate.Handle(SuspendCommand(), state),
            nameof(ResumeWorkItem) => WorkItemAggregate.Handle(ResumeCommand(), state),
            nameof(CompleteWorkItem) => WorkItemAggregate.Handle(new CompleteWorkItem(Tenant, Item), state),
            nameof(RejectWorkItem) => WorkItemAggregate.Handle(new RejectWorkItem(Tenant, Item), state),
            nameof(ExpireWorkItem) => WorkItemAggregate.Handle(new ExpireWorkItem(Tenant, Item), state),
            _ => throw new ArgumentOutOfRangeException(nameof(commandName), commandName, "Unhandled test command."),
        };

    private static void AssertOutcome(DomainResult result, Expect expect, WorkItemStatus from, WorkItemStatus target, WorkItemState state, long sequenceBefore)
    {
        switch (expect)
        {
            case Expect.Accept:
                result.IsSuccess.ShouldBeTrue($"{from} should accept the act");
                IEventPayload e = result.Events.ShouldHaveSingleItem();
                ApplyEvent(state, e);
                state.Status.ShouldBe(target);
                state.Sequence.ShouldBe(sequenceBefore + 1);
                break;

            case Expect.Reject:
                result.IsRejection.ShouldBeTrue($"{from} should reject the act");
                WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
                rejection.FromStatus.ShouldBe(from);
                rejection.TenantId.ShouldBe(Tenant);
                rejection.WorkItemId.ShouldBe(Item);
                state.Status.ShouldBe(from);
                state.Sequence.ShouldBe(sequenceBefore);
                break;

            case Expect.NoOp:
                result.IsNoOp.ShouldBeTrue($"{from} should no-op the act");
                result.Events.ShouldBeEmpty();
                state.Status.ShouldBe(from);
                state.Sequence.ShouldBe(sequenceBefore);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(expect));
        }
    }

    private static WorkItemState Replay(WorkItemState state, DomainResult result)
    {
        foreach (IEventPayload e in result.Events)
        {
            ApplyEvent(state, e);
        }

        return state;
    }

    private static void ApplyEvent(WorkItemState state, IEventPayload e)
    {
        switch (e)
        {
            case WorkItemCreated x: state.Apply(x); break;
            case WorkItemAssigned x: state.Apply(x); break;
            case WorkItemQueued x: state.Apply(x); break;
            case WorkItemClaimed x: state.Apply(x); break;
            case WorkItemSuspended x: state.Apply(x); break;
            case WorkItemResumed x: state.Apply(x); break;
            case WorkItemCompleted x: state.Apply(x); break;
            case WorkItemCancelled x: state.Apply(x); break;
            case WorkItemRejected x: state.Apply(x); break;
            case WorkItemExpired x: state.Apply(x); break;
            case WorkItemTransitionRejected x: state.Apply(x); break;
            default: throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "Unexpected event type.");
        }
    }
}
