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

public sealed class WorkItemRescheduleTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");

    [Fact]
    public void Reschedule_with_priority_and_due_date_replaces_schedule_without_changing_status()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item);
        long sequenceBefore = state.Sequence;
        var schedule = new WorkItemSchedule(Priority.High, new DateOnly(2026, 7, 15));

        DomainResult result = WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, schedule, "deadline moved"), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemRescheduled rescheduled = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRescheduled>();
        rescheduled.AggregateId.ShouldBe(Item.Value);
        rescheduled.Sequence.ShouldBe(sequenceBefore + 1);
        rescheduled.Schedule.ShouldBe(schedule);
        rescheduled.Note.ShouldBe("deadline moved");

        state.Apply(rescheduled);
        state.Schedule.ShouldBe(schedule);
        state.Status.ShouldBe(WorkItemStatus.Assigned);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void Reschedule_with_empty_schedule_is_accepted_as_sorts_last_fact()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item);
        var schedule = new WorkItemSchedule();

        DomainResult result = WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, schedule), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemRescheduled rescheduled = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRescheduled>();

        state.Apply(rescheduled);
        state.Schedule.ShouldNotBeNull().Priority.ShouldBeNull();
        state.Schedule.ShouldNotBeNull().DueDate.ShouldBeNull();
        state.Status.ShouldBe(WorkItemStatus.Created);
    }

    [Fact]
    public void Reschedule_with_empty_schedule_clears_a_previously_set_schedule_whole()
    {
        // D3: reschedule carries the desired end-state and Apply does `Schedule = e.Schedule`. Replacing a
        // fully-populated schedule with an empty one must clear BOTH fields — proving whole-replacement
        // rather than the rejected per-field-patch alternative (where null would mean "leave unchanged").
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(
            Item.Value,
            1,
            Tenant,
            Item,
            new Obligation("Reschedule clear"),
            null,
            new WorkItemSchedule(Priority.Critical, new DateOnly(2026, 8, 1))));
        state.Schedule.ShouldNotBeNull().Priority.ShouldBe(Priority.Critical);

        DomainResult result = WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, new WorkItemSchedule()), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemRescheduled rescheduled = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRescheduled>();

        state.Apply(rescheduled);
        state.Schedule.ShouldNotBeNull().Priority.ShouldBeNull();
        state.Schedule.ShouldNotBeNull().DueDate.ShouldBeNull();
        state.Status.ShouldBe(WorkItemStatus.Created);
    }

    [Fact]
    public void Reschedule_with_only_a_due_date_is_accepted_and_replayed()
    {
        // AC #3/#4 partial schedule: a due date without a priority is valid (the inverse partial — a
        // priority with no due date — is already exercised by the live-status theory). Both fields stay
        // independently nullable; a missing priority is never coerced into a default band.
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Item);
        long sequenceBefore = state.Sequence;
        var schedule = new WorkItemSchedule(DueDate: new DateOnly(2026, 9, 30));

        DomainResult result = WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, schedule), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemRescheduled rescheduled = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRescheduled>();

        state.Apply(rescheduled);
        state.Schedule.ShouldNotBeNull().Priority.ShouldBeNull();
        state.Schedule.ShouldNotBeNull().DueDate.ShouldBe(new DateOnly(2026, 9, 30));
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    public void Reschedule_from_each_live_status_is_accepted_without_changing_status(WorkItemStatus status)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state.Sequence;
        var schedule = new WorkItemSchedule(Priority.Low);

        DomainResult result = WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, schedule), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemRescheduled rescheduled = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRescheduled>();
        state.Apply(rescheduled);
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore + 1);
        state.Schedule.ShouldBe(schedule);
    }

    [Theory]
    [InlineData(WorkItemStatus.Unknown)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Reschedule_from_terminal_or_unknown_status_is_rejected_as_illegal_transition(WorkItemStatus status)
    {
        WorkItemState? state = status == WorkItemStatus.Unknown
            ? null
            : WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state?.Sequence ?? 0;

        DomainResult result = WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, new WorkItemSchedule(Priority.Normal)), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe(nameof(RescheduleWorkItem));
        (state?.Sequence ?? 0).ShouldBe(sequenceBefore);
    }
}
