using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Runtime;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic proof of <see cref="WorksEventIdentity.Matches"/>'s fail-closed posture (Story 4.8 code-review
/// remediation): a non-rejection payload shape with no <c>AggregateId</c> property must not be silently treated
/// as matching — every known non-rejection Works lifecycle event carries the property, so its absence there
/// signals an unexpected shape the identity check must not vouch for. Rejection events
/// (<see cref="IRejectionEvent"/>) are the documented, deliberate exception — every one of them carries no
/// <c>AggregateId</c> at all, so their tenant/work-item pair alone is their whole identity contract; this must
/// keep matching so the projection dispatcher does not start rejecting every known rejection event.
/// </summary>
public sealed class WorksEventIdentityTests
{
    private const string Tenant = "tenant-alpha";
    private const string WorkItem = "work-001";

    [Fact]
    public void Matches_a_payload_whose_tenant_work_item_and_aggregate_id_all_agree()
    {
        var payload = new PayloadWithAggregateId(new TenantId(Tenant), new WorkItemId(WorkItem), WorkItem);

        WorksEventIdentity.Matches(payload, Tenant, WorkItem).ShouldBeTrue();
    }

    [Fact]
    public void Fails_closed_when_a_non_rejection_payload_type_has_no_aggregate_id_property()
    {
        var payload = new PayloadWithoutAggregateId(new TenantId(Tenant), new WorkItemId(WorkItem));

        WorksEventIdentity.Matches(payload, Tenant, WorkItem).ShouldBeFalse();
    }

    [Fact]
    public void Matches_a_rejection_event_with_no_aggregate_id_property_by_tenant_and_work_item_alone()
    {
        var payload = new RejectionPayloadWithoutAggregateId(new TenantId(Tenant), new WorkItemId(WorkItem));

        WorksEventIdentity.Matches(payload, Tenant, WorkItem).ShouldBeTrue();
    }

    [Fact]
    public void Fails_closed_when_the_aggregate_id_property_value_disagrees()
    {
        var payload = new PayloadWithAggregateId(new TenantId(Tenant), new WorkItemId(WorkItem), "different-aggregate");

        WorksEventIdentity.Matches(payload, Tenant, WorkItem).ShouldBeFalse();
    }

    [Fact]
    public void Fails_closed_when_the_aggregate_id_property_value_is_null()
    {
        var payload = new PayloadWithAggregateId(new TenantId(Tenant), new WorkItemId(WorkItem), null);

        WorksEventIdentity.Matches(payload, Tenant, WorkItem).ShouldBeFalse();
    }

    private sealed record PayloadWithoutAggregateId(TenantId TenantId, WorkItemId WorkItemId) : IEventPayload;

    private sealed record RejectionPayloadWithoutAggregateId(TenantId TenantId, WorkItemId WorkItemId) : IRejectionEvent;

    private sealed record PayloadWithAggregateId(TenantId TenantId, WorkItemId WorkItemId, string? AggregateId) : IEventPayload;
}
