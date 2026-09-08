using System.Reflection;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.EventStore.Contracts.Serialization;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises LinkConversation through EventStore's reflection dispatch and state rehydration.</summary>
public sealed class LinkConversationRuntimeAdapterTests
{
    private const string Domain = "work";

    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly ConversationCorrelationId Conversation = new("conversation-456");
    private static readonly ConversationCorrelationId ConflictingConversation = new("conversation-789");

    [Fact]
    public async Task ProcessAsync_reflection_dispatch_links_rehydrated_unlinked_work()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        DomainServiceCurrentState current = CurrentState(
            new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link runtime work")));

        DomainResult result = await aggregate.ProcessAsync(CommandFor(command), current).ConfigureAwait(true);

        ConversationLinked linked = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ConversationLinked>();
        result.IsSuccess.ShouldBeTrue();
        linked.AggregateId.ShouldBe(Item.Value);
        linked.Sequence.ShouldBe(2);
        linked.ConversationCorrelationId.ShouldBe(Conversation);
    }

    [Fact]
    public async Task ProcessAsync_rehydrates_the_authoritative_link_for_retry_and_conflict_precedence()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        DomainServiceCurrentState current = CurrentState(
            new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link runtime work")),
            new ConversationLinked(Item.Value, 2, Tenant, Item, Conversation));

        DomainResult retry = await aggregate.ProcessAsync(
            CommandFor(new LinkConversation(Tenant, Item, Conversation)),
            current).ConfigureAwait(true);
        DomainResult conflict = await aggregate.ProcessAsync(
            CommandFor(new LinkConversation(Tenant, Item, ConflictingConversation)),
            current).ConfigureAwait(true);

        retry.IsNoOp.ShouldBeTrue();
        retry.Events.ShouldBeEmpty();
        WorkItemConversationLinkRejected rejected = conflict.Events
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkItemConversationLinkRejected>();
        conflict.IsRejection.ShouldBeTrue();
        rejected.ExistingConversationCorrelationId.ShouldBe(Conversation);
        rejected.ProposedConversationCorrelationId.ShouldBe(ConflictingConversation);
    }

    [Fact]
    public async Task ProcessAsync_fails_closed_when_envelope_and_payload_identities_differ()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        CommandEnvelope mismatched = CommandFor(command) with { AggregateId = "work-002" };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(mismatched, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("does not match its command envelope", Case.Sensitive);
    }

    [Fact]
    public async Task ProcessAsync_fails_closed_when_envelope_domain_differs()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        CommandEnvelope mismatched = CommandFor(command) with { Domain = "party" };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(mismatched, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("does not match its command envelope", Case.Sensitive);
    }

    [Fact]
    public async Task ProcessAsync_fails_closed_when_envelope_tenant_differs()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new LinkConversation(Tenant, Item, Conversation);
        CommandEnvelope mismatched = CommandFor(command) with { TenantId = "tenant-beta" };

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(mismatched, currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("does not match its command envelope", Case.Sensitive);
    }

    [Fact]
    public async Task ProcessAsync_fails_closed_when_conversation_correlation_is_omitted()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new { TenantId = new { Value = Tenant.Value }, WorkItemId = new { Value = Item.Value } },
            EventStorePayloadSerialization.Options);
        CommandEnvelope envelope = CommandFor(new LinkConversation(Tenant, Item, Conversation)) with
        {
            Payload = payload,
        };
        DomainServiceCurrentState current = CurrentState(
            new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Link runtime work")));

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(envelope, current));

        exception.InnerException.ShouldBeOfType<ArgumentNullException>();
    }

    [Fact]
    public async Task ProcessAsync_refuses_reserved_tenant_create_before_persist()
    {
        var aggregate = new WorkItemEventStoreAggregate();
        var command = new CreateWorkItem(new TenantId(WorksReadModelKeys.ReservedTenantId), Item, "Must not persist");

        TargetInvocationException exception = await Should.ThrowAsync<TargetInvocationException>(
            () => aggregate.ProcessAsync(CommandForCreate(command), currentState: null));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain(WorksReadModelKeys.ReservedTenantId, Case.Sensitive);
        exception.InnerException.Message.ShouldContain("registry", Case.Sensitive);
    }

    private static CommandEnvelope CommandForCreate(CreateWorkItem command)
        => new(
            MessageId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            TenantId: command.TenantId.Value,
            Domain: Domain,
            AggregateId: command.WorkItemId.Value,
            CommandType: typeof(CreateWorkItem).FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command),
            CorrelationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            CausationId: null,
            UserId: "test-user",
            Extensions: null);

    private static CommandEnvelope CommandFor(LinkConversation command)
        => new(
            MessageId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            TenantId: command.TenantId.Value,
            Domain: Domain,
            AggregateId: command.WorkItemId.Value,
            CommandType: typeof(LinkConversation).FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command),
            CorrelationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            CausationId: null,
            UserId: "test-user",
            Extensions: null);

    private static DomainServiceCurrentState CurrentState(params IEventPayload[] events)
        => new(
            SnapshotState: null,
            Events: [.. events.Select((payload, index) => Event(payload, index + 1))],
            LastSnapshotSequence: 0,
            CurrentSequence: events.Length);

    private static EventEnvelope Event(IEventPayload payload, int position)
        => new(
            new EventMetadata(
                MessageId: $"event-{position}",
                AggregateId: Item.Value,
                AggregateType: "work-item",
                TenantId: Tenant.Value,
                Domain: Domain,
                SequenceNumber: position,
                GlobalPosition: position,
                Timestamp: new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero),
                CorrelationId: "corr-1",
                CausationId: "cause-1",
                UserId: "test-user",
                DomainServiceVersion: "v1",
                EventTypeName: payload.GetType().FullName!,
                MetadataVersion: 1,
                SerializationFormat: "json"),
            JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            null);
}
