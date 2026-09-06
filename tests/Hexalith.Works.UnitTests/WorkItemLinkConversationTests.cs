using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;

using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemLinkConversationTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly ConversationCorrelationId ExistingConversation = new("conversation-456");
    private static readonly ConversationCorrelationId ProposedConversation = new("conversation-789");
    private static readonly ExecutorBinding Binding = new(new PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Coordinate);

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    public void First_link_from_each_live_status_emits_one_event_and_preserves_status(WorkItemStatus status)
    {
        WorkItemState state = InStatus(status);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new LinkConversation(Tenant, Item, ProposedConversation), state);

        result.IsSuccess.ShouldBeTrue();
        ConversationLinked linked = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ConversationLinked>();
        linked.AggregateId.ShouldBe(Item.Value);
        linked.Sequence.ShouldBe(sequenceBefore + 1);
        linked.TenantId.ShouldBe(Tenant);
        linked.WorkItemId.ShouldBe(Item);
        linked.ConversationCorrelationId.ShouldBe(ProposedConversation);

        state.Apply(linked);
        state.ConversationCorrelationId.ShouldBe(ProposedConversation);
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Exact_retry_is_a_noop_in_every_established_status(WorkItemStatus status)
    {
        WorkItemState state = InStatus(status, ExistingConversation);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new LinkConversation(Tenant, Item, ExistingConversation), state);

        result.IsNoOp.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        state.ConversationCorrelationId.ShouldBe(ExistingConversation);
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Conflicting_relink_is_rejected_before_status_rules_and_preserves_authority(WorkItemStatus status)
    {
        WorkItemState state = InStatus(status, ExistingConversation);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new LinkConversation(Tenant, Item, ProposedConversation), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemConversationLinkRejected rejection = result.Events
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkItemConversationLinkRejected>();
        rejection.TenantId.ShouldBe(Tenant);
        rejection.WorkItemId.ShouldBe(Item);
        rejection.ExistingConversationCorrelationId.ShouldBe(ExistingConversation);
        rejection.ProposedConversationCorrelationId.ShouldBe(ProposedConversation);
        state.ConversationCorrelationId.ShouldBe(ExistingConversation);
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Theory]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void First_link_from_an_unlinked_terminal_status_is_a_transition_rejection(WorkItemStatus status)
    {
        WorkItemState state = InStatus(status);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new LinkConversation(Tenant, Item, ProposedConversation), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe(nameof(LinkConversation));
        state.ConversationCorrelationId.ShouldBeNull();
        state.Status.ShouldBe(status);
        state.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Missing_and_unknown_work_are_rejected_without_creating_state()
    {
        DomainResult missing = WorkItemAggregate.Handle(new LinkConversation(Tenant, Item, ProposedConversation), null);
        WorkItemTransitionRejected missingRejection = missing.Events
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkItemTransitionRejected>();
        missingRejection.FromStatus.ShouldBe(WorkItemStatus.Unknown);
        missingRejection.AttemptedAct.ShouldBe(nameof(LinkConversation));

        var unknown = new WorkItemState();
        DomainResult unknownResult = WorkItemAggregate.Handle(new LinkConversation(Tenant, Item, ProposedConversation), unknown);
        WorkItemTransitionRejected unknownRejection = unknownResult.Events
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkItemTransitionRejected>();
        unknownRejection.FromStatus.ShouldBe(WorkItemStatus.Unknown);
        unknownRejection.AttemptedAct.ShouldBe(nameof(LinkConversation));
        unknown.ConversationCorrelationId.ShouldBeNull();
        unknown.Sequence.ShouldBe(0);
        unknown.AggregateIdentity.ShouldBeNull();
    }

    [Fact]
    public void Conversation_link_replay_changes_only_reference_and_sequence()
    {
        WorkItemState state = InStatus(WorkItemStatus.Suspended);
        WorkItemStatus statusBefore = state.Status;
        WorkItemEffort? effortBefore = state.InitialEffort;
        WorkItemSchedule? scheduleBefore = state.Schedule;
        ExecutorBinding? bindingBefore = state.ExecutorBinding;
        ParentWorkItemReference? parentBefore = state.Parent;
        IReadOnlyList<AwaitCondition> awaitConditionsBefore = [.. state.AwaitConditions];
        var linked = new ConversationLinked(Item.Value, state.Sequence + 1, Tenant, Item, ProposedConversation);

        state.Apply(linked);

        state.ConversationCorrelationId.ShouldBe(ProposedConversation);
        state.Sequence.ShouldBe(linked.Sequence);
        state.Status.ShouldBe(statusBefore);
        state.InitialEffort.ShouldBe(effortBefore);
        state.Schedule.ShouldBe(scheduleBefore);
        state.ExecutorBinding.ShouldBe(bindingBefore);
        state.Parent.ShouldBe(parentBefore);
        state.AwaitConditions.ShouldBe(awaitConditionsBefore);
    }

    [Fact]
    public void Conversation_link_replay_preserves_the_first_reference_while_consuming_later_ordinals()
    {
        WorkItemState state = InStatus(WorkItemStatus.Created, ExistingConversation);

        state.Apply(new ConversationLinked(Item.Value, 2, Tenant, Item, ExistingConversation));
        state.Apply(new ConversationLinked(Item.Value, 3, Tenant, Item, ProposedConversation));

        state.ConversationCorrelationId.ShouldBe(ExistingConversation);
        state.Status.ShouldBe(WorkItemStatus.Created);
        state.Sequence.ShouldBe(3);
    }

    [Fact]
    public void Null_conversation_link_replay_is_malformed_and_does_not_consume_its_ordinal()
    {
        WorkItemState state = InStatus(WorkItemStatus.Created, ExistingConversation);

        Should.Throw<ArgumentNullException>(() => state.Apply(
            new ConversationLinked(Item.Value, 2, Tenant, Item, null!)));

        state.ConversationCorrelationId.ShouldBe(ExistingConversation);
        state.Status.ShouldBe(WorkItemStatus.Created);
        state.Sequence.ShouldBe(1);
    }

    [Fact]
    public void Established_state_identity_mismatch_fails_closed_before_link_precedence()
    {
        WorkItemState state = InStatus(WorkItemStatus.Created, ExistingConversation);
        LinkConversation[] mismatches =
        [
            new LinkConversation(new TenantId("tenant-beta"), Item, ExistingConversation),
            new LinkConversation(Tenant, new WorkItemId("work-002"), ExistingConversation),
        ];

        foreach (LinkConversation mismatch in mismatches)
        {
            Should.Throw<InvalidOperationException>(() => WorkItemAggregate.Handle(mismatch, state));
        }

        state.ConversationCorrelationId.ShouldBe(ExistingConversation);
        state.Status.ShouldBe(WorkItemStatus.Created);
        state.Sequence.ShouldBe(1);
    }

    private static WorkItemState InStatus(
        WorkItemStatus status,
        ConversationCorrelationId? conversationCorrelationId = null)
    {
        var state = new WorkItemState();
        if (status == WorkItemStatus.Unknown)
        {
            return state;
        }

        long sequence = 0;
        state.Apply(new WorkItemCreated(
            Item.Value,
            ++sequence,
            Tenant,
            Item,
            new Obligation("Link the work conversation"),
            new WorkItemEffort(8m, new Unit("hour")),
            new WorkItemSchedule(Priority.Normal),
            null,
            Binding,
            conversationCorrelationId));

        switch (status)
        {
            case WorkItemStatus.Created:
                break;
            case WorkItemStatus.Assigned:
                state.Apply(new WorkItemAssigned(Item.Value, ++sequence, Tenant, Item, Binding));
                break;
            case WorkItemStatus.Queued:
                state.Apply(new WorkItemQueued(Item.Value, ++sequence, Tenant, Item));
                break;
            case WorkItemStatus.InProgress:
                state.Apply(new WorkItemAssigned(Item.Value, ++sequence, Tenant, Item, Binding));
                state.Apply(new WorkItemClaimed(Item.Value, ++sequence, Tenant, Item, Binding));
                break;
            case WorkItemStatus.Suspended:
                state.Apply(new WorkItemAssigned(Item.Value, ++sequence, Tenant, Item, Binding));
                state.Apply(new WorkItemClaimed(Item.Value, ++sequence, Tenant, Item, Binding));
                state.Apply(new WorkItemSuspended(
                    Item.Value,
                    ++sequence,
                    Tenant,
                    Item,
                    [AwaitCondition.ExternalSignal("conversation-link-test")]));
                break;
            case WorkItemStatus.Completed:
                state.Apply(new WorkItemAssigned(Item.Value, ++sequence, Tenant, Item, Binding));
                state.Apply(new WorkItemClaimed(Item.Value, ++sequence, Tenant, Item, Binding));
                state.Apply(new WorkItemCompleted(Item.Value, ++sequence, Tenant, Item));
                break;
            case WorkItemStatus.Cancelled:
                state.Apply(new WorkItemCancelled(Item.Value, ++sequence, Tenant, Item));
                break;
            case WorkItemStatus.Rejected:
                state.Apply(new WorkItemAssigned(Item.Value, ++sequence, Tenant, Item, Binding));
                state.Apply(new WorkItemRejected(Item.Value, ++sequence, Tenant, Item, Requeue: false));
                break;
            case WorkItemStatus.Expired:
                state.Apply(new WorkItemExpired(Item.Value, ++sequence, Tenant, Item));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported status.");
        }

        return state;
    }
}
