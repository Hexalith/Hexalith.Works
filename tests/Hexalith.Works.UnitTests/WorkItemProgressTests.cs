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

public sealed class WorkItemProgressTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly Unit Hour = new("hour");

    [Fact]
    public void ReportProgress_with_positive_delta_reduces_remaining_and_advances_sequence()
    {
        WorkItemState state = EstimatedInProgress();
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 3m, Hour, "first pass"), state);

        result.IsSuccess.ShouldBeTrue();
        ProgressReported reported = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ProgressReported>();
        reported.AggregateId.ShouldBe(Item.Value);
        reported.Sequence.ShouldBe(sequenceBefore + 1);
        reported.DoneDelta.ShouldBe(3m);
        reported.Unit.ShouldBe(Hour);
        reported.Note.ShouldBe("first pass");

        state.Apply(reported);
        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(3m);
        state.Remaining.ShouldBe(5m);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void ReportProgress_over_progress_clamps_remaining_to_zero_and_completes_in_order()
    {
        WorkItemState state = EstimatedInProgress(estimated: 8m, done: 6m);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 7m, Hour), state);

        result.IsSuccess.ShouldBeTrue();
        result.Events.Count.ShouldBe(2);
        ProgressReported reported = result.Events[0].ShouldBeOfType<ProgressReported>();
        WorkItemCompleted completed = result.Events[1].ShouldBeOfType<WorkItemCompleted>();
        reported.Sequence.ShouldBe(sequenceBefore + 1);
        completed.Sequence.ShouldBe(sequenceBefore + 2);

        state.Apply(reported);
        state.Apply(completed);

        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(8m);
        state.Remaining.ShouldBe(0m);
        state.Status.ShouldBe(WorkItemStatus.Completed);
        state.Sequence.ShouldBe(sequenceBefore + 2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ReportProgress_with_non_positive_delta_returns_progress_rejection_and_leaves_state_unchanged(int delta)
    {
        WorkItemState state = EstimatedInProgress();
        long sequenceBefore = state.Sequence;
        decimal remainingBefore = state.Remaining.ShouldNotBeNull();

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, delta, Hour), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemProgressRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemProgressRejected>();
        rejection.Reason.ShouldBe("Done delta must be positive.");
        state.Sequence.ShouldBe(sequenceBefore);
        state.Remaining.ShouldBe(remainingBefore);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
    }

    [Fact]
    public void ReportProgress_with_unit_mismatch_returns_progress_rejection_and_leaves_state_unchanged()
    {
        WorkItemState state = EstimatedInProgress();
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 1m, new Unit("point")), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemProgressRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemProgressRejected>();
        rejection.Reason.ShouldBe("Progress unit must match the established effort unit.");
        state.Sequence.ShouldBe(sequenceBefore);
        state.Remaining.ShouldBe(8m);
    }

    [Fact]
    public void ReportProgress_without_estimated_effort_is_rejected_and_does_not_complete()
    {
        WorkItemState state = UnestimatedInProgress();
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 8m, Hour), state);

        result.IsRejection.ShouldBeTrue();
        result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemProgressRejected>();
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Remaining.ShouldBeNull();
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Theory]
    [InlineData(WorkItemStatus.Unknown)]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void ReportProgress_outside_in_progress_is_rejected_as_illegal_transition(WorkItemStatus status)
    {
        WorkItemState? state = status == WorkItemStatus.Unknown
            ? null
            : WorkItemStateBuilder.InStatus(status, Tenant, Item);

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 1m, Hour), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe(nameof(ReportProgress));
    }

    [Fact]
    public void Explicit_complete_still_completes_unestimated_in_progress_and_suspended_items()
    {
        WorkItemState inProgress = UnestimatedInProgress();
        WorkItemCompleted inProgressCompleted = WorkItemAggregate.Handle(new CompleteWorkItem(Tenant, Item), inProgress)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCompleted>();
        inProgress.Apply(inProgressCompleted);
        inProgress.Status.ShouldBe(WorkItemStatus.Completed);
        inProgress.Remaining.ShouldBeNull();

        WorkItemState suspended = WorkItemStateBuilder.InStatus(WorkItemStatus.Suspended, Tenant, Item);
        WorkItemCompleted suspendedCompleted = WorkItemAggregate.Handle(new CompleteWorkItem(Tenant, Item), suspended)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCompleted>();
        suspended.Apply(suspendedCompleted);
        suspended.Status.ShouldBe(WorkItemStatus.Completed);
        suspended.Remaining.ShouldBeNull();
    }

    [Fact]
    public void WorkItemEffort_report_clamps_done_to_estimated()
    {
        var effort = new WorkItemEffort(8m, Hour, 7m);

        WorkItemEffort reported = effort.Report(5m);

        reported.Done.ShouldBe(8m);
        reported.Remaining.ShouldBe(0m);
    }

    [Fact]
    public void ReportProgress_applied_repeatedly_accumulates_done_and_burns_down_remaining()
    {
        // AC #2: progress "burns down as a fact" — each accepted report appends a ProgressReported at the
        // next sequence, and the replayed Done/Remaining accumulate across reports without completing early.
        WorkItemState state = EstimatedInProgress(estimated: 8m);
        long sequenceBefore = state.Sequence;

        ProgressReported first = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 3m, Hour, "first pass"), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<ProgressReported>();
        first.Sequence.ShouldBe(sequenceBefore + 1);
        state.Apply(first);
        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(3m);
        state.Remaining.ShouldBe(5m);

        // A second report on the burned-down state must continue the stream, not restart it, and must not
        // auto-complete while Remaining stays positive (single event, no WorkItemCompleted).
        DomainResult secondResult = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 2m, Hour), state);
        ProgressReported second = secondResult.Events.ShouldHaveSingleItem().ShouldBeOfType<ProgressReported>();
        second.Sequence.ShouldBe(sequenceBefore + 2);
        second.DoneDelta.ShouldBe(2m);
        second.Note.ShouldBeNull();
        state.Apply(second);

        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(5m);
        state.Remaining.ShouldBe(3m);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore + 2);
    }

    [Fact]
    public void ReportProgress_with_delta_exactly_equal_to_remaining_completes_in_order()
    {
        // AC #3 boundary: a delta that lands Remaining exactly on zero (not over) still completes
        // synchronously, emitting ProgressReported then WorkItemCompleted in order.
        WorkItemState state = EstimatedInProgress(estimated: 8m, done: 5m);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 3m, Hour), state);

        result.IsSuccess.ShouldBeTrue();
        result.Events.Count.ShouldBe(2);
        ProgressReported reported = result.Events[0].ShouldBeOfType<ProgressReported>();
        WorkItemCompleted completed = result.Events[1].ShouldBeOfType<WorkItemCompleted>();
        reported.DoneDelta.ShouldBe(3m);
        reported.Sequence.ShouldBe(sequenceBefore + 1);
        completed.Sequence.ShouldBe(sequenceBefore + 2);

        state.Apply(reported);
        state.Apply(completed);

        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(8m);
        state.Remaining.ShouldBe(0m);
        state.Status.ShouldBe(WorkItemStatus.Completed);
        state.Sequence.ShouldBe(sequenceBefore + 2);
    }

    private static WorkItemState EstimatedInProgress(decimal estimated = 8m, decimal done = 0m)
    {
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Report progress"), new WorkItemEffort(estimated, Hour, done)));
        state.Apply(new WorkItemAssigned(Item.Value, 2, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        state.Apply(new WorkItemClaimed(Item.Value, 3, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        return state;
    }

    private static WorkItemState UnestimatedInProgress()
    {
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Report progress")));
        state.Apply(new WorkItemAssigned(Item.Value, 2, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        state.Apply(new WorkItemClaimed(Item.Value, 3, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        return state;
    }
}
