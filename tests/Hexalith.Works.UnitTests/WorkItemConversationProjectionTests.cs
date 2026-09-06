using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;

using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemConversationProjectionTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly ConversationCorrelationId Conversation = new("conversation-456");
    private static readonly ConversationCorrelationId ConflictingConversation = new("conversation-789");
    private static readonly ExecutorBinding Binding = new(new PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Coordinate);

    [Fact]
    public void Roll_up_converges_to_the_link_and_latest_watermark_for_duplicate_out_of_order_delivery()
    {
        var projection = new WorkItemRollUpProjection();
        var created = new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link projection work"));
        var assigned = new WorkItemAssigned(Item.Value, 2, Tenant, Item, Binding);
        var linked = new ConversationLinked(Item.Value, 3, Tenant, Item, Conversation);

        projection.Project(Delivery(3, linked));
        projection.Project(Delivery(2, assigned));
        projection.Project(Delivery(1, created));
        projection.Project(Delivery(3, linked));

        WorkItemRollUp model = projection.Get(Tenant, Item).ShouldNotBeNull();
        model.Status.ShouldBe(WorkItemStatus.Assigned);
        model.ConversationCorrelationId.ShouldBe(Conversation);
        model.LatestAcceptedSourceSequence.ShouldBe(3);
    }

    [Fact]
    public void Initial_conversation_on_a_spawned_child_is_retained_by_the_roll_up()
    {
        var projection = new WorkItemRollUpProjection();
        var child = new WorkItemId("child-001");
        var spawned = new ChildSpawned(
            Item.Value,
            1,
            Tenant,
            Item,
            child,
            new Obligation("Child work"),
            ConversationCorrelationId: Conversation);

        projection.Project(Delivery(1, spawned));

        WorkItemRollUp childModel = projection.Get(Tenant, child).ShouldNotBeNull();
        childModel.ConversationCorrelationId.ShouldBe(Conversation);
    }

    [Fact]
    public void Create_time_conversation_remains_authoritative_and_conflict_records_a_deterministic_diagnostic()
    {
        var projection = new WorkItemRollUpProjection();
        var created = new WorkItemCreated(
            Item.Value,
            1,
            Tenant,
            Item,
            new Obligation("Link projection work"),
            ConversationCorrelationId: Conversation);
        var conflicting = new ConversationLinked(Item.Value, 2, Tenant, Item, ConflictingConversation);

        projection.Project(Delivery(1, created));
        projection.Project(Delivery(2, conflicting));
        projection.Project(Delivery(2, conflicting));

        WorkItemRollUp model = projection.Get(Tenant, Item).ShouldNotBeNull();
        model.ConversationCorrelationId.ShouldBe(Conversation);
        model.LatestAcceptedSourceSequence.ShouldBe(2);
        model.Degraded.ShouldBeTrue();
        model.ProjectionDiagnostics.ShouldBe([
            new RollUpProjectionDiagnostic(Tenant, Item, nameof(ConversationLinked), 2),
        ]);
    }

    [Fact]
    public void Null_conversation_link_is_refused_before_it_can_clear_the_reference_or_advance_the_watermark()
    {
        var projection = new WorkItemRollUpProjection();
        var created = new WorkItemCreated(
            Item.Value,
            1,
            Tenant,
            Item,
            new Obligation("Link projection work"),
            ConversationCorrelationId: Conversation);

        projection.Project(Delivery(1, created));
        projection.Project(Delivery(2, new ConversationLinked(Item.Value, 2, Tenant, Item, null!)));

        WorkItemRollUp model = projection.Get(Tenant, Item).ShouldNotBeNull();
        model.ConversationCorrelationId.ShouldBe(Conversation);
        model.LatestAcceptedSourceSequence.ShouldBe(1);
        model.ProjectionDiagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Conversation_link_is_a_whats_next_noop_that_advances_the_accepted_watermark()
    {
        var projection = new WhatsNextQueueProjection();
        var created = new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link projection work"));
        var assigned = new WorkItemAssigned(Item.Value, 2, Tenant, Item, Binding);
        var linked = new ConversationLinked(Item.Value, 3, Tenant, Item, Conversation);

        projection.Project(Delivery(1, created)).Changed.ShouldBeFalse();
        projection.Project(Delivery(2, assigned)).Changed.ShouldBeTrue();
        projection.Project(Delivery(3, linked)).Changed.ShouldBeFalse();
        projection.Project(Delivery(3, linked)).Changed.ShouldBeFalse();

        WhatsNextItem item = projection.WhatsNext(Tenant).ShouldHaveSingleItem();
        item.Status.ShouldBe(WorkItemStatus.Assigned);
        item.LatestAcceptedSourceSequence.ShouldBe(3);
    }

    private static WorkItemRollUpEvent Delivery(long sequence, Hexalith.EventStore.Contracts.Events.IEventPayload payload)
        => new(Tenant, Item, sequence, payload);
}
