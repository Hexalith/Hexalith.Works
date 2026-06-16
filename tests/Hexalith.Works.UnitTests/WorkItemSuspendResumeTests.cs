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

public sealed class WorkItemSuspendResumeTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly WorkItemId Child = new("child-001");
    private static readonly Unit Hour = new("hour");
    private static readonly DateTimeOffset ResumeInstant = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Suspend_from_in_progress_records_all_await_conditions_and_replays_to_suspended()
    {
        WorkItemState state = InProgress(estimated: 8m);
        AwaitCondition[] conditions =
        [
            AwaitCondition.ChildCompleted(Child),
            AwaitCondition.DateReached(ResumeInstant),
            AwaitCondition.ExternalSignal("external-approval-001"),
        ];
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new SuspendWorkItem(Tenant, Item, conditions), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemSuspended suspended = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemSuspended>();
        suspended.Sequence.ShouldBe(sequenceBefore + 1);
        suspended.AwaitConditions.ShouldBe(conditions);

        state.Apply(suspended);
        state.Status.ShouldBe(WorkItemStatus.Suspended);
        state.AwaitConditions.ShouldBe(conditions);
        state.Remaining.ShouldBe(8m);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Suspend_requires_in_progress_and_does_not_burn_sequence_when_rejected(WorkItemStatus status)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(Suspend(AwaitCondition.ExternalSignal("resume-signal")), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe("Suspend");
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Suspend_without_any_condition_is_rejected_and_leaves_in_progress_state_unchanged()
    {
        WorkItemState state = InProgress();
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new SuspendWorkItem(Tenant, Item), state);

        result.IsRejection.ShouldBeTrue();
        result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Progress_from_suspended_is_rejected_and_keeps_remaining_effort()
    {
        WorkItemState state = InProgress(estimated: 8m);
        WorkItemSuspended suspended = WorkItemAggregate.Handle(Suspend(AwaitCondition.ExternalSignal("resume-signal")), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemSuspended>();
        state.Apply(suspended);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 2m, Hour), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.Suspended);
        rejection.AttemptedAct.ShouldBe(nameof(ReportProgress));
        state.Remaining.ShouldBe(8m);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Matching_resume_consumes_one_condition_clears_all_conditions_and_replays_to_in_progress()
    {
        AwaitCondition consumed = AwaitCondition.ExternalSignal("external-approval-001");
        AwaitCondition other = AwaitCondition.DateReached(ResumeInstant);
        WorkItemState state = Suspended(consumed, other);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ResumeWorkItem(Tenant, Item, consumed), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemResumed resumed = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemResumed>();
        resumed.Sequence.ShouldBe(sequenceBefore + 1);
        resumed.ConsumedAwaitCondition.ShouldBe(consumed);

        state.Apply(resumed);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.AwaitConditions.ShouldBeEmpty();
        state.LastConsumedAwaitCondition.ShouldBe(consumed);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void Non_matching_resume_while_suspended_rejects_and_preserves_await_set()
    {
        AwaitCondition current = AwaitCondition.ChildCompleted(Child);
        WorkItemState state = Suspended(current, AwaitCondition.ExternalSignal("external-approval-001"));
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(
            new ResumeWorkItem(Tenant, Item, AwaitCondition.ExternalSignal("different-signal")),
            state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.Suspended);
        state.Status.ShouldBe(WorkItemStatus.Suspended);
        state.AwaitConditions.ShouldBe([current, AwaitCondition.ExternalSignal("external-approval-001")]);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Duplicate_consumed_key_after_resume_is_noop_but_different_key_is_rejected()
    {
        AwaitCondition consumed = AwaitCondition.ExternalSignal("external-approval-001");
        WorkItemState state = Suspended(consumed);
        WorkItemResumed resumed = WorkItemAggregate.Handle(new ResumeWorkItem(Tenant, Item, consumed), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemResumed>();
        state.Apply(resumed);
        long sequenceAfterResume = state.Sequence;

        DomainResult duplicate = WorkItemAggregate.Handle(new ResumeWorkItem(Tenant, Item, consumed), state);
        DomainResult different = WorkItemAggregate.Handle(new ResumeWorkItem(Tenant, Item, AwaitCondition.ExternalSignal("other-signal")), state);

        duplicate.IsNoOp.ShouldBeTrue();
        duplicate.Events.ShouldBeEmpty();
        different.IsRejection.ShouldBeTrue();
        different.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>().FromStatus.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceAfterResume);
    }

    [Fact]
    public void Await_condition_matching_is_kind_aware_and_rejects_malformed_keys_at_construction()
    {
        AwaitCondition.ChildCompleted(Child).Kind.ShouldBe(AwaitConditionKind.ChildCompleted);
        AwaitCondition.DateReached(ResumeInstant).Kind.ShouldBe(AwaitConditionKind.DateReached);
        AwaitCondition.ExternalSignal("child-001").ShouldNotBe(AwaitCondition.ChildCompleted(Child));

        Should.Throw<ArgumentNullException>(() => new AwaitCondition(null!));
        Should.Throw<ArgumentException>(() => AwaitCondition.ExternalSignal(""));
        Should.Throw<ArgumentException>(() => new AwaitCondition(
            AwaitConditionKind.ChildCompleted,
            correlationKey: "other-child",
            childWorkItemId: Child));
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Resume_from_any_non_suspended_status_is_rejected_and_burns_no_sequence(WorkItemStatus status)
    {
        // Resume is only legal out of Suspended. From a never-suspended InProgress item the supplied key
        // cannot equal a (still null) last-consumed key, so it is a transition rejection — not a no-op.
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(
            new ResumeWorkItem(Tenant, Item, AwaitCondition.ExternalSignal("resume-signal")),
            state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe("Resume");
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Resume_while_suspended_with_no_supplied_condition_is_rejected_and_preserves_await_set()
    {
        AwaitCondition[] conditions = [AwaitCondition.ChildCompleted(Child), AwaitCondition.ExternalSignal("external-approval-001")];
        WorkItemState state = Suspended(conditions);
        long sequenceBefore = state.Sequence;

        // A keyless resume cannot match any current condition, so the item stays parked.
        DomainResult result = WorkItemAggregate.Handle(new ResumeWorkItem(Tenant, Item), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.Suspended);
        state.Status.ShouldBe(WorkItemStatus.Suspended);
        state.AwaitConditions.ShouldBe(conditions);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Date_reached_resume_matches_the_same_instant_in_a_different_offset_and_replays_to_in_progress()
    {
        // D3/D5: a DateReached condition is a value, not a clock read. The same absolute instant expressed
        // in a different UTC offset normalizes to the same condition, so a resume carrying it is accepted.
        AwaitCondition suspended = AwaitCondition.DateReached(ResumeInstant);
        var sameInstantOtherOffset = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2));
        AwaitCondition resumeKey = AwaitCondition.DateReached(sameInstantOtherOffset);

        resumeKey.ShouldBe(suspended);
        resumeKey.CorrelationKey.ShouldBe(suspended.CorrelationKey);

        WorkItemState state = Suspended(suspended);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ResumeWorkItem(Tenant, Item, resumeKey), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemResumed resumed = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemResumed>();
        resumed.ConsumedAwaitCondition.ShouldBe(suspended);

        state.Apply(resumed);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.AwaitConditions.ShouldBeEmpty();
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void Date_reached_resume_one_second_off_does_not_match_and_keeps_the_item_suspended()
    {
        AwaitCondition current = AwaitCondition.DateReached(ResumeInstant);
        WorkItemState state = Suspended(current);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(
            new ResumeWorkItem(Tenant, Item, AwaitCondition.DateReached(ResumeInstant.AddSeconds(1))),
            state);

        result.IsRejection.ShouldBeTrue();
        result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>()
            .FromStatus.ShouldBe(WorkItemStatus.Suspended);
        state.Status.ShouldBe(WorkItemStatus.Suspended);
        state.AwaitConditions.ShouldBe([current]);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Matching_resume_can_consume_the_child_completion_condition_from_a_mixed_suspension()
    {
        // First-match is not limited to external signals: a multi-kind suspension can be released by the
        // child-completion key, which still clears the whole set (D1).
        AwaitCondition childCondition = AwaitCondition.ChildCompleted(Child);
        WorkItemState state = Suspended(AwaitCondition.DateReached(ResumeInstant), childCondition);
        long sequenceBefore = state.Sequence;

        WorkItemResumed resumed = WorkItemAggregate.Handle(new ResumeWorkItem(Tenant, Item, childCondition), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemResumed>();
        resumed.ConsumedAwaitCondition.ShouldBe(childCondition);

        state.Apply(resumed);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.AwaitConditions.ShouldBeEmpty();
        state.LastConsumedAwaitCondition.ShouldBe(childCondition);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    private static SuspendWorkItem Suspend(params AwaitCondition[] conditions) => new(Tenant, Item, conditions);

    private static WorkItemState Suspended(params AwaitCondition[] conditions)
    {
        WorkItemState state = InProgress();
        WorkItemSuspended suspended = WorkItemAggregate.Handle(Suspend(conditions), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemSuspended>();
        state.Apply(suspended);
        return state;
    }

    private static WorkItemState InProgress(decimal? estimated = null)
    {
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(
            Item.Value,
            1,
            Tenant,
            Item,
            new Obligation("Suspend/resume work item"),
            estimated is null ? null : new WorkItemEffort(estimated.Value, Hour)));
        state.Apply(new WorkItemAssigned(Item.Value, 2, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        state.Apply(new WorkItemClaimed(Item.Value, 3, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        return state;
    }
}
