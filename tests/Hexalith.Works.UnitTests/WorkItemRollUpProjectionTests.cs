using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Hexalith.Works.Server.Aggregates;
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

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));
        parent.Degraded.ShouldBeFalse();
        parent.ProjectionDiagnostics.ShouldBeEmpty();

        // The refused cross-tenant edge is not silent: the child that declared the foreign parent carries
        // a deterministic metadata-only diagnostic, without being degraded — tenant isolation is by-design
        // behavior, not a stale retained value.
        WorkItemRollUp foreignChild = projection.Get(OtherTenant, colliding).ShouldNotBeNull();
        foreignChild.RolledRemaining.ShouldBe(new RolledRemaining(10m, Hour));
        foreignChild.Degraded.ShouldBeFalse();
        foreignChild.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(OtherTenant, colliding, nameof(WorkItemCreated), 1),
        ]);
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
    public void Single_unit_subtree_exposes_single_labeled_subtotal()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m, unit: Hour));
        Project(projection, Created(Child, 1, 3m, Parent, Hour));

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
        parent.RolledRemainingByUnit.ShouldBe([new RolledRemaining(8m, Hour)]);
        foreach (RolledRemaining entry in parent.RolledRemainingByUnit)
        {
            entry.Unit.ShouldNotBeNull();
        }
    }

    [Fact]
    public void Deeper_mixed_unit_tree_exposes_each_unit_without_coerced_total()
    {
        WorkItemRollUpProjection projection = new();
        Unit day = new("day");

        Project(projection, Created(Parent, 1, 5m, unit: Hour));
        Project(projection, Created(Child, 1, 3m, Parent, Point));
        Project(projection, Created(Grandchild, 1, 2m, Child, day));

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.RolledRemaining.ShouldBeNull();
        parent.RolledRemainingByUnit.ShouldBe([
            new RolledRemaining(2m, day),
            new RolledRemaining(5m, Hour),
            new RolledRemaining(3m, Point),
        ]);
        foreach (RolledRemaining entry in parent.RolledRemainingByUnit)
        {
            entry.Unit.ShouldNotBeNull();
        }
    }

    [Fact]
    public void Same_unit_progress_and_reestimate_update_only_matching_unit_subtotal()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m, unit: Hour));
        Project(projection, Created(Child, 1, 3m, Parent, Point));
        Project(projection, new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Point));
        Project(projection, new ReEstimated(Child.Value, 3, Tenant, Child, 5m, Point));

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.RolledRemaining.ShouldBeNull();
        parent.RolledRemainingByUnit.ShouldBe([new RolledRemaining(5m, Hour), new RolledRemaining(4m, Point)]);
    }

    [Fact]
    public void Same_unit_children_sum_within_bucket_while_a_different_unit_child_stays_separate()
    {
        // AC #2: summation is legitimate *within* a Unit (two hour children fold into one hour subtotal)
        // but must never happen *across* Units (the point child is never blended into the hour bucket and
        // no coerced single value appears). This is the contrast the per-Unit map exists to guarantee.
        WorkItemRollUpProjection projection = new();
        WorkItemId hourChild = new("child-hour");
        WorkItemId pointChild = new("child-point");

        Project(projection, Created(Parent, 1, 5m, unit: Hour));
        Project(projection, Created(hourChild, 1, 3m, Parent, Hour));
        Project(projection, Created(pointChild, 1, 4m, Parent, Point));

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.RolledRemaining.ShouldBeNull();
        parent.RolledRemainingByUnit.ShouldBe([new RolledRemaining(8m, Hour), new RolledRemaining(4m, Point)]);
    }

    [Fact]
    public void Degraded_node_refuses_only_the_bad_event_then_applies_a_later_matching_unit_event()
    {
        // AC #5: fail-closed refuses *only* the unit-incompatible event. A later matching-unit progress
        // still burns down from the last valid value, and the node stays degraded because the bad event
        // remains in the ordered log and re-derives the same single diagnostic on every rebuild.
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Hour));   // 4 -> 3 (valid)
        Project(projection, new ProgressReported(Child.Value, 3, Tenant, Child, 2m, Point));  // refused + degraded
        Project(projection, new ProgressReported(Child.Value, 4, Tenant, Child, 1m, Hour));   // 3 -> 2 (still applied)

        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();
        child.OwnRemaining.ShouldBe(new OwnRemaining(2m, Hour));
        child.Degraded.ShouldBeTrue();
        child.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Child, nameof(ProgressReported), 3),
        ]);
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(7m, Hour));
    }

    [Fact]
    public void Rejected_unit_mismatched_command_emits_no_event_so_projection_stays_fresh_and_not_degraded()
    {
        // AC #4 end-to-end: the write-side guard rejects a unit-incompatible ReportProgress before any
        // ProgressReported is emitted, so nothing is ever delivered to the projection. Unlike AC #5 (a
        // persisted bad event reaching the read side and degrading it), the projection here never even sees
        // the invalid act, so the roll-up stays fresh — unchanged value, not degraded, no diagnostics.
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m, unit: Hour));
        Project(projection, Created(Child, 1, 4m, Parent, Hour));
        WorkItemRollUp before = projection.Get(Tenant, Child).ShouldNotBeNull();

        WorkItemState child = EstablishedInProgressChild();
        DomainResult rejected = WorkItemAggregate.Handle(new ReportProgress(Tenant, Child, 1m, Point), child);

        rejected.IsRejection.ShouldBeTrue();
        rejected.Events.OfType<ProgressReported>().ShouldBeEmpty();

        // No ProgressReported emitted => no roll-up delivery fact => read model is byte-for-byte unchanged.
        WorkItemRollUp after = projection.Get(Tenant, Child).ShouldNotBeNull();
        after.OwnRemaining.ShouldBe(before.OwnRemaining);
        after.RolledRemaining.ShouldBe(before.RolledRemaining);
        after.Degraded.ShouldBeFalse();
        after.ProjectionDiagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Unit_mismatched_progress_retains_last_valid_value_and_marks_metadata_diagnostic()
    {
        WorkItemRollUpProjection natural = new();
        WorkItemRollUpProjection duplicatedAndOutOfOrder = new();
        WorkItemRollUpEvent[] events =
        [
            Envelope(Created(Parent, 1, 5m)),
            Envelope(Created(Child, 1, 4m, Parent)),
            Envelope(new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Hour)),
            Envelope(new ProgressReported(Child.Value, 3, Tenant, Child, 2m, Point, "bad payload note")),
        ];

        foreach (WorkItemRollUpEvent e in events)
        {
            natural.Project(e);
        }

        foreach (WorkItemRollUpEvent e in events.Reverse().Concat([events[3], events[3]]))
        {
            duplicatedAndOutOfOrder.Project(e);
        }

        WorkItemRollUp expected = natural.Get(Tenant, Parent).ShouldNotBeNull();
        WorkItemRollUp actual = duplicatedAndOutOfOrder.Get(Tenant, Parent).ShouldNotBeNull();
        expected.RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
        expected.Degraded.ShouldBeTrue();
        expected.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Child, nameof(ProgressReported), 3),
        ]);
        actual.RolledRemaining.ShouldBe(expected.RolledRemaining);
        actual.RolledRemainingByUnit.ShouldBe(expected.RolledRemainingByUnit);
        actual.Degraded.ShouldBe(expected.Degraded);
        actual.ProjectionDiagnostics.ShouldBe(expected.ProjectionDiagnostics);
    }

    [Fact]
    public void Unit_mismatched_reestimate_retains_last_valid_value_and_marks_metadata_diagnostic()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, new ReEstimated(Child.Value, 2, Tenant, Child, 9m, Point, "bad payload note"));

        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();
        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        child.OwnRemaining.ShouldBe(new OwnRemaining(4m, Hour));
        child.Degraded.ShouldBeTrue();
        child.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Child, nameof(ReEstimated), 2),
        ]);
        parent.RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
        parent.Degraded.ShouldBeTrue();
        parent.ProjectionDiagnostics.ShouldBe(child.ProjectionDiagnostics);
    }

    [Fact]
    public void First_estimate_establishes_unit_and_later_mismatch_degrades()
    {
        WorkItemRollUpProjection projection = new();
        WorkItemId unestimated = new("child-unestimated");

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(unestimated, 1, null, Parent));
        Project(projection, new ReEstimated(unestimated.Value, 2, Tenant, unestimated, 3m, Point));
        Project(projection, new ProgressReported(unestimated.Value, 3, Tenant, unestimated, 1m, Point));
        projection.Get(Tenant, unestimated).ShouldNotBeNull().Degraded.ShouldBeFalse();

        Project(projection, new ReEstimated(unestimated.Value, 4, Tenant, unestimated, 8m, Hour));

        WorkItemRollUp child = projection.Get(Tenant, unestimated).ShouldNotBeNull();
        child.OwnRemaining.ShouldBe(new OwnRemaining(2m, Point));
        child.Degraded.ShouldBeTrue();
        child.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, unestimated, nameof(ReEstimated), 4),
        ]);
    }

    [Fact]
    public void Poisoned_non_positive_done_delta_refuses_degrades_and_does_not_wedge_the_projection()
    {
        // Read-side defense: the write side validates DoneDelta > 0, but a corrupted persisted fact must
        // refuse-and-diagnose instead of throwing inside WorkItemEffort.Report and wedging every rebuild.
        // Redelivery of the poisoned fact converges, and a later valid fact still applies.
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Hour));   // 4 -> 3 (valid)

        ProgressReported poisoned = new(Child.Value, 3, Tenant, Child, 0m, Hour);             // corrupted stream
        Project(projection, poisoned);
        Project(projection, poisoned);                                                        // idempotent redelivery
        Project(projection, new ProgressReported(Child.Value, 4, Tenant, Child, 1m, Hour));   // 3 -> 2 (still applied)

        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();
        child.OwnRemaining.ShouldBe(new OwnRemaining(2m, Hour));
        child.Degraded.ShouldBeTrue();
        child.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Child, nameof(ProgressReported), 3),
        ]);
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(7m, Hour));
    }

    [Fact]
    public void Poisoned_negative_estimate_refuses_degrades_and_retains_last_valid_effort()
    {
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));

        Project(projection, new ReEstimated(Child.Value, 2, Tenant, Child, -1m, Hour));       // corrupted stream
        Project(projection, new ProgressReported(Child.Value, 3, Tenant, Child, 1m, Hour));   // 4 -> 3 (still applied)

        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();
        child.OwnRemaining.ShouldBe(new OwnRemaining(3m, Hour));
        child.Degraded.ShouldBeTrue();
        child.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Child, nameof(ReEstimated), 2),
        ]);
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
    }

    [Fact]
    public void Terminal_degraded_child_contributes_zero()
    {
        WorkItemRollUpProjection projection = new();

        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));
        Project(projection, new ProgressReported(Child.Value, 2, Tenant, Child, 1m, Point));
        Project(projection, new WorkItemCompleted(Child.Value, 3, Tenant, Child));

        WorkItemRollUp child = projection.Get(Tenant, Child).ShouldNotBeNull();
        child.OwnRemaining.ShouldBe(new OwnRemaining(0m, Hour));
        child.Degraded.ShouldBeTrue();
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));
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
            Envelope(new WorkItemSuspended(Child.Value, 5, Tenant, Child, [AwaitCondition.ExternalSignal("rollup-resume")])),
            Envelope(new WorkItemResumed(Child.Value, 6, Tenant, Child, AwaitCondition.ExternalSignal("rollup-resume"))),
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

    [Fact]
    public void Mismatched_delivery_leaves_no_phantom_node_in_get_or_snapshot()
    {
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m));

        // The envelope header (Tenant, Child) disagrees with the payload tenant/id — refused before any
        // node is allocated, so no empty phantom (Tenant, Child) node appears in Get or Snapshot.
        projection.Project(new WorkItemRollUpEvent(Tenant, Child, 1, new ProgressReported(Child.Value, 1, OtherTenant, Child, 1m, Hour)));
        projection.Project(new WorkItemRollUpEvent(Tenant, Child, 1, new ProgressReported(Grandchild.Value, 1, Tenant, Grandchild, 1m, Hour)));

        projection.Get(Tenant, Child).ShouldBeNull();
        projection.Snapshot().ShouldHaveSingleItem().WorkItemId.ShouldBe(Parent);
    }

    [Fact]
    public void Suspended_child_keeps_contributing_remaining_and_resume_only_flips_status()
    {
        // AC #2: parking a child on an await-condition is status-only. While Suspended the child still
        // contributes its current Remaining to the parent roll-up, and a later resume changes only the
        // status back to InProgress — it never alters Remaining.
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));

        Project(projection, new WorkItemSuspended(Child.Value, 2, Tenant, Child, [AwaitCondition.ExternalSignal("await-approval")]));

        WorkItemRollUp suspendedChild = projection.Get(Tenant, Child).ShouldNotBeNull();
        suspendedChild.Status.ShouldBe(WorkItemStatus.Suspended);
        suspendedChild.OwnRemaining.ShouldBe(new OwnRemaining(4m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));

        Project(projection, new WorkItemResumed(Child.Value, 3, Tenant, Child, AwaitCondition.ExternalSignal("await-approval")));

        WorkItemRollUp resumedChild = projection.Get(Tenant, Child).ShouldNotBeNull();
        resumedChild.Status.ShouldBe(WorkItemStatus.InProgress);
        resumedChild.OwnRemaining.ShouldBe(new OwnRemaining(4m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
    }

    [Fact]
    public void Cascade_terminal_descendants_zero_their_contribution_and_drop_the_open_subtree_from_an_active_ancestor()
    {
        // Story 3.6 AC #1: a parent terminating cascades same-kind terminal commands into still-active
        // descendants; each descendant applies its own terminal transition, the resulting terminal events
        // zero its contribution, and the whole open subtree drops out of a still-active ancestor's rolled
        // Remaining. The roll-up consumes the same WorkItemCancelled events the cascade command handling
        // produces — it adds no cascade-specific subtraction path.
        WorkItemRollUpProjection projection = new();
        WorkItemId root = new("root-001");

        Project(projection, Created(root, 1, 5m));
        Project(projection, Created(Parent, 1, 4m, root));
        Project(projection, Created(Child, 1, 3m, Parent));
        projection.Get(Tenant, root).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(12m, Hour));

        // Parent is the cascade trigger; the still-active child applies its own cascade-driven cancel.
        Project(projection, new WorkItemCancelled(Parent.Value, 2, Tenant, Parent));
        Project(projection, new WorkItemCancelled(Child.Value, 2, Tenant, Child));

        projection.Get(Tenant, Child).ShouldNotBeNull().OwnRemaining.ShouldBe(new OwnRemaining(0m, Hour));
        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.Status.ShouldBe(WorkItemStatus.Cancelled);
        parent.OwnRemaining.ShouldBe(new OwnRemaining(0m, Hour));

        // The root stays active but its rolled Remaining no longer includes the cancelled parent/child subtree.
        WorkItemRollUp rootRollUp = projection.Get(Tenant, root).ShouldNotBeNull();
        rootRollUp.Status.ShouldBe(WorkItemStatus.Created);
        rootRollUp.RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));
    }

    [Fact]
    public void Cascade_expire_descendants_are_replay_safe_and_redelivery_keeps_zero_contribution()
    {
        // Story 3.6 AC #2/#3 at the read side: a cascade may deliver the descendant ExpireWorkItem more
        // than once, so the produced WorkItemExpired may arrive duplicated/out of order. Terminal nodes
        // converge to zero contribution regardless, so the parent roll never resurrects the subtree.
        WorkItemRollUpProjection projection = new();
        Project(projection, Created(Parent, 1, 5m));
        Project(projection, Created(Child, 1, 4m, Parent));

        WorkItemExpired childExpired = new(Child.Value, 2, Tenant, Child);
        Project(projection, childExpired);
        Project(projection, childExpired); // duplicate cascade delivery
        Project(projection, new ProgressReported(Child.Value, 3, Tenant, Child, 1m, Hour)); // stale, post-terminal

        projection.Get(Tenant, Child).ShouldNotBeNull().OwnRemaining.ShouldBe(new OwnRemaining(0m, Hour));
        projection.Get(Tenant, Parent).ShouldNotBeNull().RolledRemaining.ShouldBe(new RolledRemaining(5m, Hour));
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

    private static WorkItemState EstablishedInProgressChild()
    {
        ExecutorBinding binding = new(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Contribute);
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(Child.Value, 1, Tenant, Child, new Obligation("child work"), new WorkItemEffort(4m, Hour)));
        state.Apply(new WorkItemAssigned(Child.Value, 2, Tenant, Child, binding));
        state.Apply(new WorkItemClaimed(Child.Value, 3, Tenant, Child, binding));
        return state;
    }

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
