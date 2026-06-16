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
    public void Channel_exposes_documented_closed_v1_catalog()
    {
        // Channel is a closed v1 catalog: an unknown channel string fails deserialization rather than
        // degrading to Unknown, and Unknown is a deserialization sentinel rejected by ExecutorBinding.
        Enum.GetNames<Channel>()
            .ShouldBe(["Unknown", "Mcp", "Cli", "Chatbot", "Email"], ignoreOrder: false);
    }

    [Fact]
    public void PartyId_preserves_the_parties_aggregate_identity_component()
    {
        var partyId = new PartyId("Party-123");

        partyId.Value.ShouldBe("Party-123");
    }

    [Fact]
    public void PartyId_accepts_the_maximum_length_identity_component()
        => new PartyId(new string('a', 256)).Value.Length.ShouldBe(256);

    [Fact]
    public void PartyId_rejects_an_identity_component_that_exceeds_the_maximum_length()
        => Should.Throw<ArgumentException>(() => new PartyId(new string('a', 257)));

    [Fact]
    public void PartyId_is_case_sensitive_unlike_the_lowercased_tenant_id()
        => new PartyId("Party-1").ShouldNotBe(new PartyId("party-1"));

    [Theory]
    [InlineData("party:123")]
    [InlineData("-party-123")]
    [InlineData("party 123")]
    [InlineData("party-")]
    [InlineData("party.")]
    [InlineData("pärty")]
    public void PartyId_rejects_values_that_cannot_form_canonical_party_identity(string value)
        => Should.Throw<ArgumentException>(() => new PartyId(value));

    [Fact]
    public void ExecutorBinding_requires_a_party_id()
        => Should.Throw<ArgumentNullException>(() => new ExecutorBinding(null!, Channel.Mcp, AuthorityLevel.Contribute));

    [Fact]
    public void ExecutorBinding_rejects_the_unknown_channel_sentinel()
        => Should.Throw<ArgumentException>(() => new ExecutorBinding(new PartyId("party-123"), Channel.Unknown, AuthorityLevel.Contribute));

    [Fact]
    public void ExecutorBinding_rejects_an_undefined_channel_value()
        => Should.Throw<ArgumentException>(() => new ExecutorBinding(new PartyId("party-123"), (Channel)999, AuthorityLevel.Contribute));

    [Fact]
    public void ParentWorkItemReference_requires_a_parent_tenant_id()
        => Should.Throw<ArgumentNullException>(() => new ParentWorkItemReference(null!, new WorkItemId("parent-001")));

    [Fact]
    public void ParentWorkItemReference_requires_a_parent_work_item_id()
        => Should.Throw<ArgumentNullException>(() => new ParentWorkItemReference(new TenantId("tenant-alpha"), null!));
}
