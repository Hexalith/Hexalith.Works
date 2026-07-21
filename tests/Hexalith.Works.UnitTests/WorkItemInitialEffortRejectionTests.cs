using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Hexalith.Works.Testing;
using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>
/// Raw-act refuse-don't-coerce posture for command-supplied initial effort: an effort that already
/// carries done progress is rejected with <see cref="WorkItemInitialEffortRejected"/> on both creation
/// paths (<see cref="CreateWorkItem"/> and <see cref="SpawnChild"/>) rather than silently reset to
/// zero. Progress arrives only through <c>ReportProgress</c>.
/// </summary>
public sealed class WorkItemInitialEffortRejectionTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId Child = new("child-001");
    private static readonly Unit Hour = new("hour");

    [Fact]
    public void Create_with_initial_effort_already_carrying_done_progress_is_rejected_not_coerced_to_zero()
    {
        var command = new CreateWorkItem(
            Tenant,
            Item,
            "Prepare the first tenant-scoped work item",
            new WorkItemEffort(8m, Hour, 3m));

        DomainResult result = WorkItemAggregate.Handle(command, null);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Count.ShouldBe(1);
        WorkItemInitialEffortRejected rejection = result.Events.Single().ShouldBeOfType<WorkItemInitialEffortRejected>();
        rejection.TenantId.ShouldBe(Tenant);
        rejection.WorkItemId.ShouldBe(Item);
        rejection.Done.ShouldBe(3m);

        var state = new WorkItemState();
        state.Apply(rejection);

        state.Sequence.ShouldBe(0);
        state.Status.ShouldBe(WorkItemStatus.Unknown);
        state.AggregateIdentity.ShouldBeNull();
    }

    [Fact]
    public void SpawnChild_with_initial_effort_already_carrying_done_progress_is_rejected_against_the_child_id()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Parent);
        long sequenceBefore = state.Sequence;
        var command = new SpawnChild(
            Tenant,
            Parent,
            Child,
            "Break out child work",
            new WorkItemEffort(8m, Hour, 3m));

        DomainResult result = WorkItemAggregate.Handle(command, state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Count.ShouldBe(1);
        WorkItemInitialEffortRejected rejection = result.Events.Single().ShouldBeOfType<WorkItemInitialEffortRejected>();
        rejection.TenantId.ShouldBe(Tenant);
        // Child creation follows CreateWorkItem semantics: the rejection targets the caller-supplied
        // child id, never the parent.
        rejection.WorkItemId.ShouldBe(Child);
        rejection.Done.ShouldBe(3m);

        state.Apply(rejection);

        state.Sequence.ShouldBe(sequenceBefore);
        state.Status.ShouldBe(WorkItemStatus.Created);
        state.SpawnedChildWorkItemIds.ShouldBeEmpty();
    }
}
