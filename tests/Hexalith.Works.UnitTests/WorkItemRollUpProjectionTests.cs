using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemRollUpProjectionTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly TenantId OtherTenant = new("tenant-beta");
    private static readonly Unit Hour = new("hour");
    private static readonly Unit Point = new("point");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId Child = new("child-001");
    private static readonly WorkItemId Grandchild = new("grandchild-001");

    [Fact]
    public void Read_model_contract_distinguishes_own_and_rolled_remaining()
    {
        OwnRemaining own = new(5m, Hour);
        RolledRemaining rolled = new(8m, Hour);

        own.GetType().ShouldNotBe(rolled.GetType());

        WorkItemRollUp model = new(
            Tenant,
            Parent,
            WorkItemStatus.InProgress,
            null,
            own,
            rolled,
            [rolled],
            [Child],
            1,
            2);

        model.OwnRemaining.ShouldBe(own);
        model.RolledRemaining.ShouldBe(rolled);
        model.OwnRemaining.ShouldBeOfType<OwnRemaining>();
        model.RolledRemaining.ShouldBeOfType<RolledRemaining>();
    }

    [Fact]
    public void Projecting_parent_and_child_reports_own_and_recursive_rolled_remaining()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Hour));

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();

        parent.OwnRemaining.ShouldBe(new OwnRemaining(5m, Hour));
        parent.RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
        parent.RolledRemainingByUnit.ShouldBe([new RolledRemaining(8m, Hour)]);
        parent.ChildContributionCount.ShouldBe(1);
        parent.ChildWorkItemIds.ShouldBe([Child]);
        child.RolledRemaining.ShouldBe(new RolledRemaining(3m, Hour));
    }

    [Fact]
    public void Nested_descendant_contribution_propagates_to_every_ancestor()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, Created(Grandchild, 1, 2m, Child));

        projection.Get(Tenant, Grandchild).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(2m, Hour));
        projection.Get(Tenant, Child).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(6m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(11m, Hour));
    }

    [Fact]
    public void Duplicate_child_delivery_does_not_double_count_contribution()
    {
        WorkItemRollUpProjection projection = new();
        ProgressReported progress = new(Child.Value, 2, Tenant, Child, 1m, Hour);

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, progress);
        Project(projection, progress);

        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
    }

    [Fact]
    public void Out_of_order_delivery_rebuilds_node_by_sequence_and_converges()
    {
        WorkItemRollUpProjection outOfOrder = new();
        WorkItemRollUpProjection natural = new();

        WorkItemRollUpEvent[] events =
        [
            Envelope(Created(Parent, 1, 5m)),
            Envelope(Created(Child, 1, 8m, Parent)),
            Envelope(new ProgressReported(Child.Value, 2, Tenant, Child, 4m, Hour)),
            Envelope(new ReEstimated(Child.Value, 3, Tenant, Child, 6m, Hour)),
        ];

        foreach (WorkItemRollUpEvent e in events)
        {
            natural.Project(e);
        }

        foreach (WorkItemRollUpEvent e in events.Reverse())
        {
            outOfOrder.Project(e);
        }

        WorkItemRollUp expected = natural.Get(Tenant, Parent).ShouldNotBeNull();
        WorkItemRollUp actual = outOfOrder.Get(Tenant, Parent).ShouldNotBeNull();
        expected.RolledRemaining.ShouldBe(new RolledRemaining(7m, Hour));
        actual.RolledRemaining.ShouldBe(expected.RolledRemaining);
        actual.RolledRemainingByUnit.ShouldBe(expected.RolledRemainingByUnit);
        actual.ChildWorkItemIds.ShouldBe(expected.ChildWorkItemIds);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("expired")]
    [InlineData("rejected")]
    public void Terminal_child_events_assign_zero_contribution_and_are_replay_safe(string terminal)
    {
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));

        object terminalEvent = terminal switch
        {
            "completed" => new WorkItemCompleted(Child.Value, 2, Tenant, Child),
            "cancelled" => new WorkItemCancelled(Child.Value, 2, Tenant, Child),
            "expired" => new WorkItemExpired(Child.Value, 2, Tenant, Child),
            _ => new WorkItemRejected(Child.Value, 2, Tenant, Child, false),
        };

        Project(projection, terminalEvent);
        Project(projection, terminalEvent);

        projection.Get(Tenant, Child).ShouldNotBeNull().OwnRemaining.ShouldBe(new OwnRemaining(0m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));
    }

    [Fact]
    public void Requeued_rejection_is_not_terminal_and_does_not_zero_contribution()
    {
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));

        Project(projection, new WorkItemRejected(Child.Value, 2, Tenant, Child, true));

        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();
        child.Status.ShouldBe(WorkItemStatus.Queued);
        child.OwnRemaining.ShouldBe(new OwnRemaining(4m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
    }

    [Fact]
    public void Child_spawned_edge_and_child_created_edge_converge_on_same_tree()
    {
        WorkItemRollUpProjection fromSpawn = new();
        WorkItemRollUpProjection fromChildCreate = new();

        Project(fromSpawn, Created(Parent, 1, 5m));
        Project(fromSpawn, new ChildSpawned(
            Parent.Value,
            2,
            Tenant,
            Parent,
            Child,
            new Obligation("child work"),
            new WorkItemEffort(4m, Hour)));

        Project(fromChildCreate, Created(Parent, 1, 5m));
        Project(fromChildCreate, Created(Child, 1, 4m, Parent));

        fromSpawn.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
        fromSpawn.Get(Tenant, Parent).ShouldNotBeNull().ChildWorkItemIds.ShouldBe(fromChildCreate.Get(Tenant, Parent).ShouldNotBeNull().ChildWorkItemIds);
    }

    [Fact]
    public void Cross_tenant_edge_never_leaks_child_totals_to_parent()
    {
        WorkItemRollUpProjection projection = new();
        WorkItemId colliding = new("child-001");

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, new WorkItemCreated(
            colliding.Value,
            1,
            OtherTenant,
            colliding,
            new Obligation("foreign child"),
            new WorkItemEffort(10m, Hour),
            Parent: new ParentWorkItemReference(Tenant, Parent)));

        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));
        projection.Get(OtherTenant, colliding).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(10m, Hour));
    }

    [Fact]
    public void Mixed_units_are_exposed_as_per_unit_values_without_fabricated_single_rollup()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m, unit: Hour));
        Project(projection, Created(Child, 1, 3m, Parent, Point));

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.RolledRemaining.ShouldBeNull();
        parent.RolledRemainingByUnit.ShouldBe([new RolledRemaining(5m, Hour), new RolledRemaining(3m, Point)]);
    }

    [Fact]
    public void Unestimated_child_contributes_after_reestimate_and_terminal_unestimated_child_stays_zero()
    {
        WorkItemRollUpProjection projection = new();
        WorkItemId unestimated = new("child-unestimated");

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(unestimated, 1, null, Parent));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));

        Project(projection, new ReEstimated(unestimated.Value, 2, Tenant, unestimated, 3m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));

        WorkItemId terminalUnestimated = new("child-terminal-unestimated");
        Project(projection, Created(terminalUnestimated, 1, null, Parent));
        Project(projection, new WorkItemCompleted(terminalUnestimated.Value, 2, Tenant, terminalUnestimated));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
    }

    [Fact]
    public void Child_progress_before_parent_edge_converges_when_edge_materializes()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Child, 1, 4m));
        Project(projection, new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Hour));
        Project(projection, Created(Parent, 1, 5m));
        Project(projection, new ChildSpawned(
            Parent.Value,
            2,
            Tenant,
            Parent,
            Child,
            new Obligation("child work"),
            new WorkItemEffort(4m, Hour)));

        projection.Get(Tenant, Child).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(3m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
    }

    [Fact]
    public void Stale_non_terminal_child_event_after_terminal_event_does_not_resurrect_contribution()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, new WorkItemCompleted(Child.Value, 3, Tenant, Child));
        Project(projection, new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Hour));

        projection.Get(Tenant, Child).ShouldNotBeNull().OwnRemaining.ShouldBe(new OwnRemaining(0m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));
    }

    [Fact]
    public void Non_rollup_lifecycle_events_do_not_throw_or_change_remaining_totals()
    {
        WorkItemRollUpProjection projection = new();
        ExecutorBinding binding = new(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Contribute);
        WorkItemRollUpEvent[] lifecycleEvents =
        [
            Envelope(new WorkItemAssigned(Child.Value, 2, Tenant, Child, binding)),
            Envelope(new WorkItemQueued(Child.Value, 3, Tenant, Child)),
            Envelope(new WorkItemClaimed(Child.Value, 4, Tenant, Child, binding)),
            Envelope(new WorkItemSuspended(Child.Value, 5, Tenant, Child)),
            Envelope(new WorkItemResumed(Child.Value, 6, Tenant, Child)),
            Envelope(new WorkItemRescheduled(Child.Value, 7, Tenant, Child, new WorkItemSchedule(Priority.Normal))),
        ];

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));

        foreach (WorkItemRollUpEvent e in lifecycleEvents)
        {
            projection.Project(e);
        }

        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();
        child.Status.ShouldBe(WorkItemStatus.InProgress);
        child.OwnRemaining.ShouldBe(new OwnRemaining(4m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
    }

    [Fact]
    public void Invalid_delivery_facts_do_not_affect_rollup_totals()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        projection.Project(new WorkItemRollUpEvent(Tenant, Child, 0, new ProgressReported(Child.Value, 0, Tenant, Child, 1m, Hour)));
        projection.Project(new WorkItemRollUpEvent(Tenant, Child, 2, new ProgressReported(Child.Value, 2, OtherTenant, Child, 1m, Hour)));

        projection.Get(Tenant, Child).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(4m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
        projection.Get(OtherTenant, Child).ShouldBeNull();
    }

    private static WorkItemCreated Created(
        WorkItemId workItemId,
        long sequence,
        decimal? remaining,
        WorkItemId? parent = null,
        Unit? unit = null)
        => new(
            workItemId.Value,
            sequence,
            Tenant,
            workItemId,
            new Obligation($"obligation-{workItemId.Value}"),
            remaining is null ? null : new WorkItemEffort(remaining.Value, unit ?? Hour),
            Parent: parent is null ? null : new ParentWorkItemReference(Tenant, parent));

    private static void Project(WorkItemRollUpProjection projection, object payload)
        => projection.Project(Envelope(payload));

    private static WorkItemRollUpEvent Envelope(object payload)
        => payload switch
        {
            WorkItemCreated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ChildSpawned e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ProgressReported e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ReEstimated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemCompleted e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemCancelled e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemExpired e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemRejected e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemAssigned e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemQueued e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemClaimed e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemSuspended e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemResumed e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemRescheduled e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            _ => throw new ArgumentOutOfRangeException(nameof(payload)),
        };
}
