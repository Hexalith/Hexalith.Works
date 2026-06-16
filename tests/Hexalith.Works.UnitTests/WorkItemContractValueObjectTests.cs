using Hexalith.Works.Contracts.ValueObjects;

using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemContractValueObjectTests
{
    [Fact]
    public void TenantId_normalizes_to_aggregate_identity_tenant_component()
    {
        var tenantId = new TenantId("Tenant-Alpha");

        tenantId.Value.ShouldBe("tenant-alpha");
    }

    [Theory]
    [InlineData("tenant:alpha")]
    [InlineData("-tenant-alpha")]
    [InlineData("tenant alpha")]
    public void TenantId_rejects_values_that_cannot_form_canonical_aggregate_identity(string value)
        => Should.Throw<ArgumentException>(() => new TenantId(value));

    [Theory]
    [InlineData("work:001")]
    [InlineData("-work-001")]
    [InlineData("work 001")]
    public void WorkItemId_rejects_values_that_cannot_form_canonical_aggregate_identity(string value)
        => Should.Throw<ArgumentException>(() => new WorkItemId(value));

    [Fact]
    public void Priority_order_matches_documented_queue_precedence()
        => ((int)Priority.Critical).ShouldBeLessThan((int)Priority.High);

    [Fact]
    public void AuthorityLevel_exposes_documented_carried_not_enforced_catalog()
    {
        Enum.GetNames<AuthorityLevel>()
            .ShouldBe(["Unknown", "Read", "Contribute", "Coordinate", "Administer"], ignoreOrder: false);
    }

    [Fact]
    public void ParentWorkItemReference_requires_a_parent_work_item_id()
        => Should.Throw<ArgumentNullException>(() => new ParentWorkItemReference(null!));
}
