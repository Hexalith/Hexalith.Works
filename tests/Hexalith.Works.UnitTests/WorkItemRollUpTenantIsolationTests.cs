using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemRollUpTenantIsolationTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly TenantId OtherTenant = new("tenant-beta");
    private static readonly Unit Hour = new("hour");
    private static readonly Unit Point = new("point");
    private static readonly WorkItemId Parent = new("parent");
    private static readonly WorkItemId LocalChild = new("child-local");
    private static readonly WorkItemId ForeignChild = new("child-foreign");
    private static readonly ExecutorBinding Binding = new(
        new PartyId("party-123"),
        Channel.Mcp,
        AuthorityLevel.Contribute);

    public static TheoryData<IEventPayload> SupportedDeliveryPayloads => new()
    {
        Created(Tenant, LocalChild, 2m, sequence: 2),
        new ChildSpawned(LocalChild.Value, 2, Tenant, LocalChild, new WorkItemId("spawned-child"), new Obligation("spawned child"), new WorkItemEffort(1m, Hour)),
        new ProgressReported(LocalChild.Value, 2, Tenant, LocalChild, 1m, Hour),
        new ReEstimated(LocalChild.Value, 2, Tenant, LocalChild, 3m, Hour),
        new WorkItemCompleted(LocalChild.Value, 2, Tenant, LocalChild),
        new WorkItemCancelled(LocalChild.Value, 2, Tenant, LocalChild),
        new WorkItemExpired(LocalChild.Value, 2, Tenant, LocalChild),
        new WorkItemRejected(LocalChild.Value, 2, Tenant, LocalChild, true),
        new WorkItemAssigned(LocalChild.Value, 2, Tenant, LocalChild, Binding),
        new WorkItemQueued(LocalChild.Value, 2, Tenant, LocalChild),
        new WorkItemClaimed(LocalChild.Value, 2, Tenant, LocalChild, Binding),
        new WorkItemSuspended(LocalChild.Value, 2, Tenant, LocalChild, [AwaitCondition.ExternalSignal("resume")]),
        new WorkItemResumed(LocalChild.Value, 2, Tenant, LocalChild, AwaitCondition.ExternalSignal("resume")),
        new WorkItemRescheduled(LocalChild.Value, 2, Tenant, LocalChild, new WorkItemSchedule(Priority.Normal)),
    };

    [Fact]
    public void Secure_default_policy_enforces_every_boundary()
    {
        // Pins the shipped policy itself. Four of the six defaults are unobservable end to end -- with edge
        // enforcement on, no foreign child ever reaches the output, contribution, diagnostic, or degradation
        // hops -- so only a direct assertion on the defaults stops one of them silently flipping to permissive.
        WorkItemRollUpTenantIsolation policy = new();

        policy.AllowsDelivery(Envelope(Created(Tenant, LocalChild, 5m))).ShouldBeTrue();
        policy.AllowsDelivery(new WorkItemRollUpEvent(Tenant, LocalChild, 1, Created(OtherTenant, LocalChild, 5m)))
            .ShouldBeFalse();

        policy.AllowsEdge(Tenant, Tenant).ShouldBeTrue();
        policy.AllowsEdge(Tenant, OtherTenant).ShouldBeFalse();
        policy.AllowsEdge(Tenant.Value, Tenant.Value).ShouldBeTrue();
        policy.AllowsEdge(Tenant.Value, OtherTenant.Value).ShouldBeFalse();

        policy.AllowsOutput(Tenant, Tenant).ShouldBeTrue();
        policy.AllowsOutput(Tenant, OtherTenant).ShouldBeFalse();

        policy.AllowsContribution(Tenant, Tenant).ShouldBeTrue();
        policy.AllowsContribution(Tenant, OtherTenant).ShouldBeFalse();

        policy.AllowsDiagnostic(Tenant, Tenant).ShouldBeTrue();
        policy.AllowsDiagnostic(Tenant, OtherTenant).ShouldBeFalse();

        policy.AllowsDegradation(Tenant, Tenant).ShouldBeTrue();
        policy.AllowsDegradation(Tenant, OtherTenant).ShouldBeFalse();
    }

    [Fact]
    public void Default_constructed_projection_keeps_the_parent_tenant_closed()
    {
        // Exercises the production call sites through the public parameterless constructor, so the shipped
        // composition -- not just an explicitly configured policy -- is covered by the isolation suite.
        WorkItemRollUpProjection projection = new();

        ProjectParentAndDistinctChildren(projection);
        projection.Project(Envelope(new ReEstimated(ForeignChild.Value, 2, OtherTenant, ForeignChild, 13m, Point)));

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.ChildWorkItemIds.ShouldBe([LocalChild]);
        parent.ChildContributionCount.ShouldBe(1);
        parent.RolledRemaining.ShouldBe(new RolledRemaining(7m, Hour));
        parent.ProjectionDiagnostics.ShouldBeEmpty();
        parent.Degraded.ShouldBeFalse();

        // The refused edge does not erase the child's own declared fact: the cross-tenant parent reference it
        // was created with survives on the child, while the parent side of the edge is never materialized.
        WorkItemRollUp foreignChild = projection.Get(OtherTenant, ForeignChild).ShouldNotBeNull();
        foreignChild.Parent.ShouldBe(new ParentWorkItemReference(Tenant, Parent));
        foreignChild.Degraded.ShouldBeTrue();
        foreignChild.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(OtherTenant, ForeignChild, nameof(WorkItemCreated), 1),
            new RollUpProjectionDiagnostic(OtherTenant, ForeignChild, nameof(ReEstimated), 2),
        ]);
    }

    [Theory]
    [MemberData(nameof(SupportedDeliveryPayloads))]
    public void Delivery_boundary_accepts_every_supported_payload(IEventPayload payload)
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(delivery: true);
        projection.Project(Envelope(Created(Tenant, LocalChild, 5m)));

        projection.Project(new WorkItemRollUpEvent(Tenant, LocalChild, 2, payload));

        projection.Get(Tenant, LocalChild).ShouldNotBeNull().LatestAcceptedSourceSequence.ShouldBe(2);
    }

    [Fact]
    public void Delivery_boundary_refuses_foreign_tenant_wrong_item_and_unknown_payloads()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(delivery: true);
        WorkItemId wrongHeader = new("wrong-header");
        WorkItemId wrongPayload = new("wrong-payload");

        projection.Project(Envelope(Created(Tenant, Parent, 5m)));
        projection.Project(new WorkItemRollUpEvent(
            Tenant,
            LocalChild,
            1,
            Created(OtherTenant, LocalChild, 7m)));
        projection.Project(new WorkItemRollUpEvent(
            Tenant,
            wrongHeader,
            1,
            Created(Tenant, wrongPayload, 7m)));
        projection.Project(new WorkItemRollUpEvent(
            Tenant,
            LocalChild,
            1,
            new UnknownRollUpEventPayload()));

        projection.Get(Tenant, Parent).ShouldNotBeNull();
        projection.Get(Tenant, LocalChild).ShouldBeNull();
        projection.Get(Tenant, wrongHeader).ShouldBeNull();
        projection.Get(OtherTenant, LocalChild).ShouldBeNull();
        projection.Snapshot().ShouldHaveSingleItem();
    }

    [Fact]
    public void Delivery_boundary_uses_ordinal_work_item_identity_and_preserves_rejected_sequence_slot()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(delivery: true);
        WorkItemId caseVariant = new(LocalChild.Value.ToUpperInvariant());
        projection.Project(Envelope(Created(Tenant, LocalChild, 5m)));

        projection.Project(new WorkItemRollUpEvent(
            Tenant,
            LocalChild,
            2,
            new WorkItemAssigned(caseVariant.Value, 2, Tenant, caseVariant, Binding)));

        // Observed before the genuine delivery arrives: were the comparison case-insensitive (or were the
        // work-item half dropped), the case variant would already have taken slot 2 and the assertions
        // after the second delivery would be satisfied either way.
        WorkItemRollUp afterMismatch = projection.Get(Tenant, LocalChild).ShouldNotBeNull();
        afterMismatch.LatestAcceptedSourceSequence.ShouldBe(1);
        afterMismatch.Status.ShouldBe(WorkItemStatus.Created);

        projection.Project(new WorkItemRollUpEvent(
            Tenant,
            LocalChild,
            2,
            new WorkItemAssigned(LocalChild.Value, 2, Tenant, LocalChild, Binding)));

        WorkItemRollUp child = projection.Get(Tenant, LocalChild).ShouldNotBeNull();
        child.LatestAcceptedSourceSequence.ShouldBe(2);
        child.Status.ShouldBe(WorkItemStatus.Assigned);
    }

    [Fact]
    public void Delivery_boundary_refuses_malformed_identities_without_throwing()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(delivery: true);
        projection.Project(Envelope(Created(Tenant, LocalChild, 5m)));
        WorkItemRollUpEvent[] malformedDeliveries =
        [
            new(null!, LocalChild, 2, new ProgressReported(LocalChild.Value, 2, Tenant, LocalChild, 1m, Hour)),
            new(Tenant, null!, 2, new ProgressReported(LocalChild.Value, 2, Tenant, LocalChild, 1m, Hour)),
            new(Tenant, LocalChild, 2, new ProgressReported(LocalChild.Value, 2, null!, LocalChild, 1m, Hour)),
            new(Tenant, LocalChild, 2, new ProgressReported(LocalChild.Value, 2, Tenant, null!, 1m, Hour)),
            new(Tenant, LocalChild, 2, null!),
            new(Tenant, LocalChild, 2, new ChildSpawned(
                LocalChild.Value,
                2,
                Tenant,
                LocalChild,
                null!,
                new Obligation("spawned child"))),
        ];

        foreach (WorkItemRollUpEvent delivery in malformedDeliveries)
        {
            projection.Project(delivery);
        }

        WorkItemRollUp child = projection.Get(Tenant, LocalChild).ShouldNotBeNull();
        child.LatestAcceptedSourceSequence.ShouldBe(1);
        child.OwnRemaining.ShouldBe(new OwnRemaining(5m, Hour));
    }

    [Fact]
    public void Well_formedness_floor_refuses_malformed_deliveries_with_delivery_isolation_off()
    {
        // The floor is documented as ungoverned by the delivery flag. With every hop permissive, a
        // header/payload identity mismatch is admitted -- proving the flag really is off -- while an
        // unsupported payload type, a missing payload, and a missing payload identity are still refused.
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt();
        projection.Project(Envelope(Created(Tenant, LocalChild, 5m)));

        projection.Project(new WorkItemRollUpEvent(Tenant, LocalChild, 2, new UnknownRollUpEventPayload()));
        projection.Project(new WorkItemRollUpEvent(Tenant, LocalChild, 2, null!));
        projection.Project(new WorkItemRollUpEvent(
            Tenant,
            LocalChild,
            2,
            new ProgressReported(LocalChild.Value, 2, null!, LocalChild, 1m, Hour)));

        WorkItemRollUp refused = projection.Get(Tenant, LocalChild).ShouldNotBeNull();
        refused.LatestAcceptedSourceSequence.ShouldBe(1);
        refused.OwnRemaining.ShouldBe(new OwnRemaining(5m, Hour));

        projection.Project(new WorkItemRollUpEvent(
            Tenant,
            LocalChild,
            2,
            new ProgressReported(LocalChild.Value, 2, OtherTenant, LocalChild, 1m, Hour)));

        WorkItemRollUp admitted = projection.Get(Tenant, LocalChild).ShouldNotBeNull();
        admitted.LatestAcceptedSourceSequence.ShouldBe(2);
        admitted.OwnRemaining.ShouldBe(new OwnRemaining(4m, Hour));
    }

    [Fact]
    public void Edge_boundary_admits_local_child_and_refuses_foreign_child_with_local_parent()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(edge: true);

        ProjectParentAndDistinctChildren(projection);

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.ChildWorkItemIds.ShouldBe([LocalChild]);
        parent.ChildContributionCount.ShouldBe(1);
        parent.RolledRemaining.ShouldBe(new RolledRemaining(7m, Hour));
        projection.Get(Tenant, LocalChild).ShouldNotBeNull().ProjectionDiagnostics.ShouldBeEmpty();
        projection.Get(OtherTenant, ForeignChild).ShouldNotBeNull().ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(OtherTenant, ForeignChild, nameof(WorkItemCreated), 1),
        ]);
    }

    [Fact]
    public void Output_boundary_exposes_and_counts_only_local_child_from_permissively_admitted_edges()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(output: true);

        ProjectParentAndDistinctChildren(projection);

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.ChildWorkItemIds.ShouldBe([LocalChild]);
        parent.ChildContributionCount.ShouldBe(1);
        parent.RolledRemaining.ShouldBe(new RolledRemaining(14m, Hour));
        projection.Get(OtherTenant, ForeignChild).ShouldNotBeNull();
    }

    [Fact]
    public void Contribution_boundary_includes_local_effort_and_ignores_foreign_effort_from_permissive_edge()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(contribution: true);

        ProjectParentAndDistinctChildren(projection);

        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.ChildWorkItemIds
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ShouldBe([ForeignChild, LocalChild]);
        parent.ChildContributionCount.ShouldBe(2);
        parent.RolledRemaining.ShouldBe(new RolledRemaining(7m, Hour));
        parent.RolledRemainingByUnit.ShouldBe([new RolledRemaining(7m, Hour)]);
    }

    [Fact]
    public void Diagnostic_boundary_propagates_local_metadata_and_ignores_foreign_metadata_from_permissive_edge()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(diagnostic: true);
        ProjectParentAndDistinctChildren(projection);

        projection.Project(Envelope(new ReEstimated(LocalChild.Value, 2, Tenant, LocalChild, 11m, Point)));
        projection.Project(Envelope(new ReEstimated(ForeignChild.Value, 2, OtherTenant, ForeignChild, 13m, Point)));

        projection.Get(Tenant, LocalChild).ShouldNotBeNull().ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, LocalChild, nameof(ReEstimated), 2),
        ]);
        projection.Get(OtherTenant, ForeignChild).ShouldNotBeNull().ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(OtherTenant, ForeignChild, nameof(ReEstimated), 2),
        ]);
        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.ChildContributionCount.ShouldBe(2);
        parent.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, LocalChild, nameof(ReEstimated), 2),
        ]);
    }

    [Fact]
    public void Degradation_boundary_ignores_foreign_state_but_propagates_local_state_from_permissive_edges()
    {
        WorkItemRollUpProjection projection = ProjectionWithOnlyIsolationAt(degradation: true);
        ProjectParentAndDistinctChildren(projection);

        projection.Project(Envelope(new ReEstimated(ForeignChild.Value, 2, OtherTenant, ForeignChild, 13m, Point)));
        projection.Get(OtherTenant, ForeignChild).ShouldNotBeNull().Degraded.ShouldBeTrue();
        WorkItemRollUp parent = projection.Get(Tenant, Parent).ShouldNotBeNull();
        parent.ChildContributionCount.ShouldBe(2);
        parent.Degraded.ShouldBeFalse();

        projection.Project(Envelope(new ReEstimated(LocalChild.Value, 2, Tenant, LocalChild, 11m, Point)));
        projection.Get(Tenant, LocalChild).ShouldNotBeNull().Degraded.ShouldBeTrue();
        projection.Get(Tenant, Parent).ShouldNotBeNull().Degraded.ShouldBeTrue();
    }

    private static WorkItemRollUpProjection ProjectionWithOnlyIsolationAt(
        bool delivery = false,
        bool edge = false,
        bool output = false,
        bool contribution = false,
        bool diagnostic = false,
        bool degradation = false)
        => new(new WorkItemRollUpTenantIsolation(
            enforceDelivery: delivery,
            enforceEdge: edge,
            enforceOutput: output,
            enforceContribution: contribution,
            enforceDiagnostic: diagnostic,
            enforceDegradation: degradation));

    private static void ProjectParentAndDistinctChildren(WorkItemRollUpProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var parent = new ParentWorkItemReference(Tenant, Parent);

        projection.Project(Envelope(Created(Tenant, Parent, 5m)));
        projection.Project(Envelope(Created(Tenant, LocalChild, 2m, parent)));
        projection.Project(Envelope(Created(OtherTenant, ForeignChild, 7m, parent)));
    }

    private static WorkItemCreated Created(
        TenantId tenantId,
        WorkItemId workItemId,
        decimal remaining,
        ParentWorkItemReference? parent = null,
        long sequence = 1)
        => new(
            workItemId.Value,
            sequence,
            tenantId,
            workItemId,
            new Obligation($"obligation-{tenantId.Value}-{workItemId.Value}"),
            new WorkItemEffort(remaining, Hour),
            Parent: parent);

    private static WorkItemRollUpEvent Envelope(IEventPayload payload)
        => payload switch
        {
            WorkItemCreated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ReEstimated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            _ => throw new ArgumentOutOfRangeException(nameof(payload)),
        };
}
