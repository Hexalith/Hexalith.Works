using System.Reflection;
using System.Text.Json;

using Dapr.Actors;
using Dapr.Actors.Runtime;

using Hexalith.EventStore.Client.Aggregates;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Identity;
using Hexalith.EventStore.Contracts.Replay;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.EventStore.Server.Actors;
using Hexalith.EventStore.Server.Commands;
using Hexalith.EventStore.Server.Configuration;
using Hexalith.EventStore.Server.Events;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

using ContractEventEnvelope = Hexalith.EventStore.Contracts.Events.EventEnvelope;
using StoredEventEnvelope = Hexalith.EventStore.Server.Events.EventEnvelope;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Characterizes Works claim conflicts through the real in-process EventStore actor persistence pipeline.
/// </summary>
public sealed class WorkItemClaimPersistenceConflictTests
{
    private const string Domain = "work";
    private const string AggregateType = "work-item";
    private const string DomainServiceVersion = "v1";

    private static readonly PropertyInfo ActorStateManagerProperty =
        typeof(Actor).GetProperty("StateManager", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Dapr Actor.StateManager property was not found.");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly AggregateIdentity Identity = new(Tenant.Value, Domain, Item.Value);
    private static readonly ExecutorBinding LoserBinding =
        new(new PartyId("party-a"), Channel.Mcp, AuthorityLevel.Administer);
    private static readonly ExecutorBinding WinnerBinding =
        new(new PartyId("party-b"), Channel.Cli, AuthorityLevel.Contribute);

    /// <summary>
    /// Proves a persistence conflict reloads the competing winner and persists the loser's existing rejection.
    /// </summary>
    [Fact]
    public async Task RetryingClaimAfterPersistenceConflictCommitsWinnerAndPublishesLoserRejection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (
            InMemoryStateManager inner,
            ClaimWorkItem loserCommand,
            DomainResult loserCandidate,
            ClaimWorkItem winnerCommand,
            DomainResult winnerCandidate) = await SeedQueuedClaimsAsync(cancellationToken).ConfigureAwait(true);

        async Task CommitWinnerAsync(CancellationToken token)
        {
            var persister = CreatePersister(inner);
            _ = await persister.PersistEventsAsync(
                Identity,
                AggregateType,
                CommandFor(winnerCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAY"),
                winnerCandidate,
                DomainServiceVersion,
                token).ConfigureAwait(false);
            await inner.SaveStateAsync(token).ConfigureAwait(false);
        }

        var stateManager = new ConflictInjectingActorStateManager(inner, 1, CommitWinnerAsync);
        (
            AggregateActor actor,
            FakeDomainServiceInvoker invoker,
            FakeEventPublisher publisher,
            FakeDeadLetterPublisher deadLetterPublisher,
            InMemoryCommandStatusStore statusStore) = CreateActor(stateManager, maxPersistenceConflictRetries: 1);
        CommandEnvelope command = CommandFor(loserCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAX");

        CommandProcessingResult result = await actor
            .ProcessCommandAsync(command, cancellationToken)
            .ConfigureAwait(true);

        result.Accepted.ShouldBeFalse();
        result.EventCount.ShouldBe(1);
        result.FailureReason.ShouldBe("DomainRejected");
        result.RejectionEventType.ShouldNotBeNull().ShouldContain(nameof(WorkItemTransitionRejected));
        invoker.Invocations.Count.ShouldBe(2);
        stateManager.EventBatchSaveAttemptCount.ShouldBe(2);
        stateManager.InjectedConflictCount.ShouldBe(1);
        stateManager.ExternalWinnerCommitCount.ShouldBe(1);

        RehydrationResult stream = await ReadCommittedStreamAsync(inner).ConfigureAwait(true);
        stream.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L, 2L, 3L, 4L]);
        AssertOnlyWinnerClaimWasCommitted(stream, WinnerBinding);

        StoredEventEnvelope persistedRejection = stream.Events[3];
        persistedRejection.EventTypeName.ShouldContain(nameof(WorkItemTransitionRejected));
        WorkItemTransitionRejected rejection = JsonSerializer
            .Deserialize<WorkItemTransitionRejected>(persistedRejection.Payload, JsonOptions)
            .ShouldNotBeNull();
        rejection.FromStatus.ShouldBe(WorkItemStatus.InProgress);
        rejection.AttemptedAct.ShouldBe("Claim");

        publisher.TotalEventsPublished.ShouldBe(1);
        FakeEventPublisher.PublishCall publishCall = publisher.PublishCalls.ShouldHaveSingleItem();
        StoredEventEnvelope published = publishCall.Events.ShouldHaveSingleItem();
        published.SequenceNumber.ShouldBe(4);
        published.EventTypeName.ShouldContain(nameof(WorkItemTransitionRejected));
        published.Payload.ShouldBe(persistedRejection.Payload);
        deadLetterPublisher.GetDeadLetterMessages().ShouldBeEmpty();

        CommandStatusRecord status = (await statusStore
            .ReadStatusAsync(Tenant.Value, command.MessageId, cancellationToken)
            .ConfigureAwait(true)).ShouldNotBeNull();
        status.Status.ShouldBe(CommandStatus.Rejected);
        status.EventCount.ShouldBe(1);
        status.RejectionEventType.ShouldNotBeNull().ShouldContain(nameof(WorkItemTransitionRejected));

        AggregateReconstructionResult replay = Replay(stream);
        replay.Status.ShouldBe(AggregateReconstructionStatus.Succeeded);
        replay.LastAppliedSequenceNumber.ShouldBe(4);
        AssertWinnerState(replay, WinnerBinding);

        loserCandidate.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>().Binding.ShouldBe(LoserBinding);
    }

    /// <summary>
    /// Proves a second event-batch conflict exhausts the configured retry without persisting loser effects.
    /// </summary>
    [Fact]
    public async Task ExhaustingClaimPersistenceConflictRetryReturnsConcurrencyConflictWithoutLoserEffects()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (
            InMemoryStateManager inner,
            ClaimWorkItem loserCommand,
            DomainResult loserCandidate,
            ClaimWorkItem winnerCommand,
            DomainResult winnerCandidate) = await SeedQueuedClaimsAsync(cancellationToken).ConfigureAwait(true);

        async Task CommitWinnerAsync(CancellationToken token)
        {
            var persister = CreatePersister(inner);
            _ = await persister.PersistEventsAsync(
                Identity,
                AggregateType,
                CommandFor(winnerCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAY"),
                winnerCandidate,
                DomainServiceVersion,
                token).ConfigureAwait(false);
            await inner.SaveStateAsync(token).ConfigureAwait(false);
        }

        var stateManager = new ConflictInjectingActorStateManager(inner, 2, CommitWinnerAsync);
        (
            AggregateActor actor,
            FakeDomainServiceInvoker invoker,
            FakeEventPublisher publisher,
            FakeDeadLetterPublisher deadLetterPublisher,
            InMemoryCommandStatusStore statusStore) = CreateActor(stateManager, maxPersistenceConflictRetries: 1);
        CommandEnvelope command = CommandFor(loserCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAX");

        CommandProcessingResult result = await actor
            .ProcessCommandAsync(command, cancellationToken)
            .ConfigureAwait(true);

        result.Accepted.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("ConcurrencyConflict");
        result.FailureReason.ShouldBe("ConcurrencyConflict");
        result.EventCount.ShouldBe(0);
        result.RejectionEventType.ShouldBeNull();
        invoker.Invocations.Count.ShouldBe(2);
        stateManager.EventBatchSaveAttemptCount.ShouldBe(2);
        stateManager.InjectedConflictCount.ShouldBe(2);
        stateManager.ExternalWinnerCommitCount.ShouldBe(1);

        RehydrationResult stream = await ReadCommittedStreamAsync(inner).ConfigureAwait(true);
        stream.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L, 2L, 3L]);
        AssertOnlyWinnerClaimWasCommitted(stream, WinnerBinding);
        stream.Events.Any(static envelope =>
            envelope.EventTypeName.Contains(nameof(WorkItemTransitionRejected), StringComparison.Ordinal)).ShouldBeFalse();
        publisher.TotalEventsPublished.ShouldBe(0);
        publisher.PublishCalls.ShouldBeEmpty();
        deadLetterPublisher.GetDeadLetterMessages().ShouldBeEmpty();

        CommandStatusRecord status = (await statusStore
            .ReadStatusAsync(Tenant.Value, command.MessageId, cancellationToken)
            .ConfigureAwait(true)).ShouldNotBeNull();
        status.Status.ShouldBe(CommandStatus.Rejected);
        status.FailureReason.ShouldBe("ConcurrencyConflict");
        status.EventCount.ShouldBeNull();
        status.RejectionEventType.ShouldBeNull();

        AggregateReconstructionResult replay = Replay(stream);
        replay.Status.ShouldBe(AggregateReconstructionStatus.Succeeded);
        replay.LastAppliedSequenceNumber.ShouldBe(3);
        AssertWinnerState(replay, WinnerBinding);

        loserCandidate.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>().Binding.ShouldBe(LoserBinding);
    }

    private static async Task<(
        InMemoryStateManager StateManager,
        ClaimWorkItem LoserCommand,
        DomainResult LoserCandidate,
        ClaimWorkItem WinnerCommand,
        DomainResult WinnerCandidate)> SeedQueuedClaimsAsync(CancellationToken cancellationToken)
    {
        var stateManager = new InMemoryStateManager();
        var aggregate = new WorkItemEventStoreAggregate();

        var create = new CreateWorkItem(Tenant, Item, "Claim conflict runtime proof");
        await ProcessAndCommitAsync(
            stateManager,
            aggregate,
            create,
            "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            currentStream: null,
            cancellationToken).ConfigureAwait(false);

        RehydrationResult afterCreate = await ReadCommittedStreamAsync(stateManager).ConfigureAwait(false);
        var queue = new QueueWorkItem(Tenant, Item);
        await ProcessAndCommitAsync(
            stateManager,
            aggregate,
            queue,
            "01ARZ3NDEKTSV4RRFFQ69G5FAW",
            afterCreate,
            cancellationToken).ConfigureAwait(false);

        RehydrationResult queued = await ReadCommittedStreamAsync(stateManager).ConfigureAwait(false);
        queued.CurrentSequence.ShouldBe(2);
        queued.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L, 2L]);

        var loserCommand = new ClaimWorkItem(Tenant, Item, LoserBinding);
        var winnerCommand = new ClaimWorkItem(Tenant, Item, WinnerBinding);
        DomainServiceCurrentState queuedState = ToDomainServiceCurrentState(queued);
        DomainResult loserCandidate = await aggregate
            .ProcessAsync(CommandFor(loserCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAX"), queuedState)
            .ConfigureAwait(false);
        DomainResult winnerCandidate = await aggregate
            .ProcessAsync(CommandFor(winnerCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAY"), queuedState)
            .ConfigureAwait(false);

        WorkItemClaimed loserClaim = loserCandidate.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        WorkItemClaimed winnerClaim = winnerCandidate.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        loserCandidate.IsSuccess.ShouldBeTrue();
        winnerCandidate.IsSuccess.ShouldBeTrue();
        loserClaim.Sequence.ShouldBe(3);
        winnerClaim.Sequence.ShouldBe(3);
        loserClaim.Sequence.ShouldBe(winnerClaim.Sequence);
        loserClaim.Binding.ShouldBe(LoserBinding);
        winnerClaim.Binding.ShouldBe(WinnerBinding);

        return (stateManager, loserCommand, loserCandidate, winnerCommand, winnerCandidate);
    }

    private static async Task ProcessAndCommitAsync(
        InMemoryStateManager stateManager,
        WorkItemEventStoreAggregate aggregate,
        object command,
        string messageId,
        RehydrationResult? currentStream,
        CancellationToken cancellationToken)
    {
        CommandEnvelope envelope = CommandFor(command, messageId);
        DomainResult result = await aggregate
            .ProcessAsync(
                envelope,
                currentStream is null ? null : ToDomainServiceCurrentState(currentStream))
            .ConfigureAwait(false);
        var persister = CreatePersister(stateManager);
        _ = await persister.PersistEventsAsync(
            Identity,
            AggregateType,
            envelope,
            result,
            DomainServiceVersion,
            cancellationToken).ConfigureAwait(false);
        await stateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static EventPersister CreatePersister(InMemoryStateManager stateManager)
        => new(
            stateManager,
            NullLogger<EventPersister>.Instance,
            new NoOpEventPayloadProtectionService());

    private static (
        AggregateActor Actor,
        FakeDomainServiceInvoker Invoker,
        FakeEventPublisher Publisher,
        FakeDeadLetterPublisher DeadLetterPublisher,
        InMemoryCommandStatusStore StatusStore) CreateActor(
        ConflictInjectingActorStateManager stateManager,
        int maxPersistenceConflictRetries)
    {
        var invoker = new FakeDomainServiceInvoker();
        var aggregate = new WorkItemEventStoreAggregate();
        invoker.SetupHandler(
            typeof(ClaimWorkItem).FullName!,
            (command, currentState) => aggregate.ProcessAsync(command, currentState));

        var publisher = new FakeEventPublisher();
        var deadLetterPublisher = new FakeDeadLetterPublisher();
        var statusStore = new InMemoryCommandStatusStore();
        ICommandAggregateTypeResolver aggregateTypeResolver = Substitute.For<ICommandAggregateTypeResolver>();
        _ = aggregateTypeResolver
            .ResolveAsync(Arg.Any<CommandEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(AggregateType);
        var host = ActorHost.CreateForTest<AggregateActor>(
            new ActorTestOptions { ActorId = new ActorId(Identity.ActorId) });
        var actor = new AggregateActor(
            host,
            NullLogger<AggregateActor>.Instance,
            invoker,
            new FakeSnapshotManager(),
            new NoOpEventPayloadProtectionService(),
            statusStore,
            publisher,
            Options.Create(new EventDrainOptions()),
            Options.Create(new BackpressureOptions()),
            deadLetterPublisher,
            commandAggregateTypeResolver: aggregateTypeResolver,
            concurrencyOptions: Options.Create(new CommandConcurrencyOptions
            {
                MaxPersistenceConflictRetries = maxPersistenceConflictRetries,
            }));
        ActorStateManagerProperty.SetValue(actor, stateManager);
        return (actor, invoker, publisher, deadLetterPublisher, statusStore);
    }

    private static async Task<RehydrationResult> ReadCommittedStreamAsync(InMemoryStateManager stateManager)
    {
        var reader = new EventStreamReader(stateManager, NullLogger<EventStreamReader>.Instance);
        return (await reader.RehydrateAsync(Identity).ConfigureAwait(false)).ShouldNotBeNull();
    }

    private static CommandEnvelope CommandFor(object command, string messageId)
    {
        PropertyInfo tenantProperty = command.GetType()
            .GetProperty(nameof(CreateWorkItem.TenantId))
            .ShouldNotBeNull();
        PropertyInfo workItemProperty = command.GetType()
            .GetProperty(nameof(CreateWorkItem.WorkItemId))
            .ShouldNotBeNull();

        return new CommandEnvelope(
            MessageId: messageId,
            TenantId: tenantProperty.GetValue(command).ShouldBeOfType<TenantId>().Value,
            Domain: Domain,
            AggregateId: workItemProperty.GetValue(command).ShouldBeOfType<WorkItemId>().Value,
            CommandType: command.GetType().FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command, command.GetType()),
            CorrelationId: messageId,
            CausationId: null,
            UserId: "test-user",
            Extensions: null);
    }

    private static DomainServiceCurrentState ToDomainServiceCurrentState(RehydrationResult stream)
        => new(
            SnapshotState: stream.SnapshotState,
            Events: [.. stream.Events.Select(ToContractEnvelope)],
            LastSnapshotSequence: stream.LastSnapshotSequence,
            CurrentSequence: stream.CurrentSequence);

    private static ContractEventEnvelope ToContractEnvelope(StoredEventEnvelope envelope)
        => new(
            new EventMetadata(
                envelope.MessageId,
                envelope.AggregateId,
                envelope.AggregateType,
                envelope.TenantId,
                envelope.Domain,
                envelope.SequenceNumber,
                envelope.GlobalPosition,
                envelope.Timestamp,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.UserId,
                envelope.DomainServiceVersion,
                envelope.EventTypeName,
                envelope.MetadataVersion,
                envelope.SerializationFormat),
            envelope.Payload,
            envelope.Extensions is null ? null : new Dictionary<string, string>(envelope.Extensions));

    private static void AssertOnlyWinnerClaimWasCommitted(
        RehydrationResult stream,
        ExecutorBinding expectedWinner)
    {
        StoredEventEnvelope persistedClaim = stream.Events
            .Where(static envelope => envelope.EventTypeName.Contains(nameof(WorkItemClaimed), StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        WorkItemClaimed claimed = JsonSerializer
            .Deserialize<WorkItemClaimed>(persistedClaim.Payload, JsonOptions)
            .ShouldNotBeNull();
        claimed.Sequence.ShouldBe(3);
        claimed.Binding.ShouldBe(expectedWinner);
        claimed.Binding.ShouldNotBe(LoserBinding);
    }

    private static AggregateReconstructionResult Replay(RehydrationResult stream)
        => AggregateReplayer.Replay<WorkItemState>(new AggregateReconstructionRequest(
            TenantId: Tenant.Value,
            Domain: Domain,
            AggregateType: AggregateType,
            AggregateId: Item.Value,
            UpToSequence: stream.CurrentSequence,
            Events: [.. stream.Events.Select(static envelope => new ReplayEventEnvelope(
                envelope.SequenceNumber,
                envelope.EventTypeName,
                envelope.Payload,
                envelope.SerializationFormat,
                envelope.MetadataVersion,
                envelope.MessageId,
                envelope.CorrelationId,
                envelope.CausationId))],
            IncludeTimeline: true,
            RequestId: "work-item-claim-persistence-conflict"));

    private static void AssertWinnerState(
        AggregateReconstructionResult replay,
        ExecutorBinding expectedWinner)
    {
        using JsonDocument document = JsonDocument.Parse(replay.StateJson.ShouldNotBeNull());
        document.RootElement.GetProperty("status").GetString().ShouldBe(nameof(WorkItemStatus.InProgress));
        document.RootElement.GetProperty("sequence").GetInt64().ShouldBe(3);
        JsonElement binding = document.RootElement.GetProperty("executorBinding");
        binding.GetProperty("partyId").GetProperty("value").GetString().ShouldBe(expectedWinner.PartyId.Value);
        binding.GetProperty("channel").GetString().ShouldBe(expectedWinner.Channel.ToString());
        binding.GetProperty("authorityLevel").GetString().ShouldBe(expectedWinner.AuthorityLevel.ToString());
    }
}
