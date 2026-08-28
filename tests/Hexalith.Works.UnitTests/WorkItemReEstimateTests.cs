using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Hexalith.Works.Server.Aggregates;
using Hexalith.Works.Testing;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemReEstimateTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly Unit Hour = new("hour");

    [Fact]
    public void ReEstimate_up_in_same_unit_updates_estimated_and_rederives_remaining()
    {
        WorkItemState state = EstimatedInProgress(estimated: 8m, done: 3m);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 13m, Hour, "scope grew"), state);

        result.IsSuccess.ShouldBeTrue();
        ReEstimated reEstimated = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ReEstimated>();
        reEstimated.AggregateId.ShouldBe(Item.Value);
        reEstimated.Sequence.ShouldBe(sequenceBefore + 1);
        reEstimated.Estimated.ShouldBe(13m);
        reEstimated.Unit.ShouldBe(Hour);
        reEstimated.Note.ShouldBe("scope grew");

        state.Apply(reEstimated);
        state.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(13m);
        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(3m);
        state.Remaining.ShouldBe(10m);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void ReEstimate_with_different_unit_returns_rejection_and_leaves_effort_unchanged()
    {
        WorkItemState state = EstimatedInProgress();
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 13m, new Unit("point")), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemReEstimateRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemReEstimateRejected>();
        rejection.Reason.ShouldBe("Re-estimate unit must match the established effort unit.");
        result.Events.OfType<ReEstimated>().ShouldBeEmpty();
        state.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(8m);
        state.InitialEffort.ShouldNotBeNull().Unit.ShouldBe(Hour);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void ReEstimate_with_negative_estimate_returns_rejection_and_leaves_state_unchanged()
    {
        WorkItemState state = EstimatedInProgress();
        long sequenceBefore = state.Sequence;
        decimal remainingBefore = state.Remaining.ShouldNotBeNull();

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, -1m, Hour), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemReEstimateRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemReEstimateRejected>();
        rejection.Reason.ShouldBe("Estimated effort must be non-negative.");
        state.Remaining.ShouldBe(remainingBefore);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void ReEstimate_below_current_done_clamps_done_to_estimated_without_completing()
    {
        WorkItemState state = EstimatedInProgress(estimated: 8m, done: 6m);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 4m, Hour), state);

        result.IsSuccess.ShouldBeTrue();
        ReEstimated reEstimated = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ReEstimated>();
        reEstimated.Sequence.ShouldBe(sequenceBefore + 1);

        state.Apply(reEstimated);
        state.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(4m);
        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(4m);
        state.Remaining.ShouldBe(0m);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void ReEstimate_on_unestimated_item_establishes_first_estimate_and_unit()
    {
        WorkItemState state = UnestimatedInProgress();
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 5m, Hour), state);

        result.IsSuccess.ShouldBeTrue();
        ReEstimated reEstimated = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ReEstimated>();

        state.Apply(reEstimated);
        state.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(5m);
        state.InitialEffort.ShouldNotBeNull().Unit.ShouldBe(Hour);
        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(0m);
        state.Remaining.ShouldBe(5m);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void ReEstimate_after_establishing_first_estimate_rejects_a_different_unit_and_preserves_it()
    {
        // D2 establishes the Unit on a previously-unestimated item; AC #2 then makes that Unit immutable.
        // This is the "different Unit after the first estimate" rejection reached via the D2 establish path,
        // distinct from the created-with-effort path already covered above.
        WorkItemState state = UnestimatedInProgress();
        state.Apply(WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 5m, Hour), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<ReEstimated>());
        long sequenceAfterEstablish = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 9m, new Unit("point")), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemReEstimateRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemReEstimateRejected>();
        rejection.Reason.ShouldBe("Re-estimate unit must match the established effort unit.");
        result.Events.OfType<ReEstimated>().ShouldBeEmpty();
        state.InitialEffort.ShouldNotBeNull().Unit.ShouldBe(Hour);
        state.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(5m);
        state.Sequence.ShouldBe(sequenceAfterEstablish);
    }

    [Fact]
    public void Mismatched_persisted_reestimate_retains_effort_and_advances_both_replay_sequences()
    {
        var state = new WorkItemState();
        WorkItemRollUpProjection projection = new();
        WorkItemEffort establishedEffort = new(8m, Hour, 3m);
        WorkItemCreated created = new(
            Item.Value,
            1,
            Tenant,
            Item,
            new Obligation("Re-estimate work"),
            establishedEffort);
        ReEstimated mismatched = new(Item.Value, 2, Tenant, Item, 13m, new Unit("point"), "corrupted persisted fact");

        state.Apply(created);
        projection.Project(new WorkItemRollUpEvent(Tenant, Item, created.Sequence, created));
        state.Apply(mismatched);
        projection.Project(new WorkItemRollUpEvent(Tenant, Item, mismatched.Sequence, mismatched));

        WorkItemRollUp rollUp = projection.Get(Tenant, Item).ShouldNotBeNull();
        state.InitialEffort.ShouldBe(establishedEffort);
        rollUp.OwnEffort.ShouldBe(establishedEffort);
        state.Remaining.ShouldBe(establishedEffort.Remaining);
        rollUp.OwnRemaining.ShouldBe(new OwnRemaining(establishedEffort.Remaining, Hour));
        state.Sequence.ShouldBe(mismatched.Sequence);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(mismatched.Sequence);
        rollUp.Degraded.ShouldBeTrue();
        rollUp.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Item, nameof(ReEstimated), mismatched.Sequence),
        ]);
    }

    [Fact]
    public void PersistedNegativeReestimateRetainsEffortAndAdvancesAggregateAndProjectionSequences()
    {
        var state = new WorkItemState();
        WorkItemRollUpProjection projection = new();
        WorkItemEffort establishedEffort = new(8m, Hour, 3m);
        WorkItemCreated created = new(
            Item.Value,
            1,
            Tenant,
            Item,
            new Obligation("Re-estimate work"),
            establishedEffort);
        ReEstimated negative = new(Item.Value, 2, Tenant, Item, -1m, Hour, "corrupted persisted fact");

        state.Apply(created);
        projection.Project(new WorkItemRollUpEvent(Tenant, Item, created.Sequence, created));

        Should.NotThrow(() => state.Apply(negative));
        Should.NotThrow(() => projection.Project(new WorkItemRollUpEvent(Tenant, Item, negative.Sequence, negative)));

        WorkItemRollUp rollUp = projection.Get(Tenant, Item).ShouldNotBeNull();
        state.InitialEffort.ShouldBe(establishedEffort);
        rollUp.OwnEffort.ShouldBe(establishedEffort);
        state.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(8m);
        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(3m);
        state.InitialEffort.ShouldNotBeNull().Unit.ShouldBe(Hour);
        state.Remaining.ShouldBe(5m);
        rollUp.OwnRemaining.ShouldBe(new OwnRemaining(5m, Hour));
        state.Sequence.ShouldBe(negative.Sequence);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(negative.Sequence);
        rollUp.Degraded.ShouldBeTrue();
        rollUp.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Item, nameof(ReEstimated), negative.Sequence),
        ]);
    }

    [Fact]
    public void PersistedNegativeReestimateLeavesUnestimatedAggregateSequenceAligned()
    {
        WorkItemState state = UnestimatedInProgress();
        ReEstimated negative = new(Item.Value, state.Sequence + 1, Tenant, Item, -1m, Hour, "corrupted persisted fact");

        Should.NotThrow(() => state.Apply(negative));

        state.InitialEffort.ShouldBeNull();
        state.Remaining.ShouldBeNull();
        state.Sequence.ShouldBe(negative.Sequence);
    }

    [Fact]
    public void ReEstimate_to_zero_clamps_done_and_remaining_without_completing()
    {
        // Zero is the lower boundary of the AC #1 "non-negative value": accepted, Done clamps to 0, and
        // even though Remaining lands on 0 the act emits ONLY ReEstimated — never WorkItemCompleted (D5).
        WorkItemState state = EstimatedInProgress(estimated: 8m, done: 6m);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 0m, Hour), state);

        result.IsSuccess.ShouldBeTrue();
        ReEstimated reEstimated = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ReEstimated>();
        reEstimated.Estimated.ShouldBe(0m);
        reEstimated.Sequence.ShouldBe(sequenceBefore + 1);

        state.Apply(reEstimated);
        state.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(0m);
        state.InitialEffort.ShouldNotBeNull().Done.ShouldBe(0m);
        state.Remaining.ShouldBe(0m);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Theory]
    [InlineData(WorkItemStatus.Unknown)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void ReEstimate_from_terminal_or_unknown_status_is_rejected_as_illegal_transition(WorkItemStatus status)
    {
        WorkItemState? state = status == WorkItemStatus.Unknown
            ? null
            : WorkItemStateBuilder.InStatus(status, Tenant, Item);
        long sequenceBefore = state?.Sequence ?? 0;

        DomainResult result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 5m, Hour), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe(nameof(ReEstimate));
        (state?.Sequence ?? 0).ShouldBe(sequenceBefore);
    }

    private static WorkItemState EstimatedInProgress(decimal estimated = 8m, decimal done = 0m)
    {
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Re-estimate work"), new WorkItemEffort(estimated, Hour, done)));
        state.Apply(new WorkItemAssigned(Item.Value, 2, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        state.Apply(new WorkItemClaimed(Item.Value, 3, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        return state;
    }

    private static WorkItemState UnestimatedInProgress()
    {
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Re-estimate work")));
        state.Apply(new WorkItemAssigned(Item.Value, 2, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        state.Apply(new WorkItemClaimed(Item.Value, 3, Tenant, Item, WorkItemStateBuilder.DefaultBinding()));
        return state;
    }
}
