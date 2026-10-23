using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Recovery;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Proves the current SDK replacements for deferred legacy reminder delivery and discovery gaps.</summary>
public sealed class WorksSdkReminderDeliveryTests
{
    private const string Tenant = "tenant-reminder-delivery";
    private const string Item = "suspended-item";
    private static readonly AwaitCondition s_date = AwaitCondition.DateReached(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Discovery and registrar failures release delivery ownership; only successful redelivery completes deduplication.</summary>
    /// <param name="failure">The publication or reminder boundary that fails on the first delivery.</param>
    [Theory]
    [InlineData("registrar")]
    [InlineData("sweep")]
    [InlineData("candidate")]
    [InlineData("tenant-registry")]
    public async Task SuspensionFailureRemainsRedeliverableUntilDiscoveryAndConvergenceSucceed(string failure)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var markers = Markers(store);
        var source = new ProcessSource(Tenant, "work", Item);
        IReminderRegistrar reminders = Substitute.For<IReminderRegistrar>();
        bool unavailable = true;
        int convergenceCalls = 0;
        reminders.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            convergenceCalls++;
            call.Arg<ReminderTarget>().ShouldBe(new ReminderTarget(Tenant, "work", Item));
            // A scheduling failure must already have durable discovery, even without projection delivery.
            (await Checkpoints(store).ContainsCandidateAsync(source, ct).ConfigureAwait(false)).ShouldBeTrue();
            if (unavailable) throw new IOException("synthetic reminder convergence outage");
            return new ReminderConvergenceResult(1, 0, 0, 0, 0);
        });
        if (failure != "registrar")
            store.ConcurrentWriteBeforeTrySave = () =>
            {
                bool candidatePage = store.Snapshot<ProcessIndexPage<ProcessSource>>("statestore", ProcessStateKeys.Candidates(Tenant, 0)) is not null;
                bool candidateMarker = store.Snapshot<ProcessCandidateMarker>("statestore", ProcessStateKeys.CandidateMarker(source)) is not null;
                if (failure == "sweep" || (failure == "candidate" && candidatePage) || (failure == "tenant-registry" && candidateMarker))
                    throw new IOException("synthetic discovery outage");
            };
        using var services = Services(store, reminders);
        var processor = Processor(services, markers);
        EventStoreDomainEventEnvelope envelope = SuspensionEnvelope();

        await Should.ThrowAsync<IOException>(() => processor.ProcessAsync(envelope, ct));
        convergenceCalls.ShouldBe(failure == "registrar" ? 1 : 0);
        (await Checkpoints(store).ContainsCandidateAsync(source, ct)).ShouldBe(failure == "registrar");
        if (failure is "candidate" or "tenant-registry")
        {
            store.Snapshot<ProcessIndexPage<ProcessSource>>("statestore", ProcessStateKeys.Candidates(Tenant, 0))
                .ShouldNotBeNull().Entries.ShouldBe([source]);
            (store.Snapshot<ProcessCandidateMarker>("statestore", ProcessStateKeys.CandidateMarker(source)) is not null)
                .ShouldBe(failure == "tenant-registry");
            store.Snapshot<ProcessIndexPage<string>>("statestore", ProcessStateKeys.Tenants(0)).ShouldBeNull();
            (await new ProcessSweepStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System).GetAsync(ct))
                .PendingPublications.ShouldContainKey(ProcessStateKeys.SourceId(source));
        }
        (await markers.TryAcquireAsync(envelope.MessageId, ct)).ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.Acquired);
        await markers.ReleaseAsync(envelope.MessageId, ct);

        unavailable = false;
        store.ConcurrentWriteBeforeTrySave = null;
        // Reconstruct the SDK processor, Works handler, Dapr marker adapter and client over retained storage.
        using var restarted = Services(store, reminders);
        (await Processor(restarted, Markers(store)).ProcessAsync(envelope, ct)).ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        (await Checkpoints(store).ContainsCandidateAsync(source, ct)).ShouldBeTrue();
        (await markers.TryAcquireAsync(envelope.MessageId, ct)).ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.Completed);
        (await Processor(restarted, Markers(store)).ProcessAsync(envelope, ct)).ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
        convergenceCalls.ShouldBe(failure == "registrar" ? 2 : 1);
        (await Checkpoints(store).ReadCandidatePageAsync(Tenant, 0, ct)).ShouldNotBeNull().Entries.ShouldBe([source]);
    }

    /// <summary>Retained SDK work permits acknowledgement after discovery, while a thrown convergence remains retryable.</summary>
    [Fact]
    public async Task RetainedConvergenceIsAcknowledgedWithoutRepeatingCompletedDelivery()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var markers = Markers(store);
        IReminderRegistrar reminders = Substitute.For<IReminderRegistrar>();
        reminders.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>())
            .Returns(new ReminderConvergenceResult(0, 0, 0, 1, 0));
        using var services = Services(store, reminders);
        var processor = Processor(services, markers);
        EventStoreDomainEventEnvelope envelope = SuspensionEnvelope();

        (await processor.ProcessAsync(envelope, ct)).ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        (await Checkpoints(store).ContainsCandidateAsync(new(Tenant, "work", Item), ct)).ShouldBeTrue();
        (await markers.TryAcquireAsync(envelope.MessageId, ct)).ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.Completed);
        using var restarted = Services(store, reminders);
        (await Processor(restarted, Markers(store)).ProcessAsync(envelope, ct)).ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
        _ = await reminders.Received(1).ConvergeAsync(new ReminderTarget(Tenant, "work", Item), Arg.Any<CancellationToken>());
    }

    /// <summary>A winning tenant publication between registry read and CAS survives the losing handler's retry and restart.</summary>
    [Fact]
    public async Task SuspensionDiscoveryPreservesOtherTenantsAcrossRegistryConflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        IReminderRegistrar reminders = Substitute.For<IReminderRegistrar>();
        reminders.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>())
            .Returns(new ReminderConvergenceResult(1, 0, 0, 0, 0));
        using var services = Services(store, reminders);
        var processor = Processor(services, new InMemoryEventStoreDomainEventMarkerStore());
        var first = new ProcessSource("tenant-a", "work", Item);
        var second = new ProcessSource("tenant-b", "work", Item);
        var winner = new ProcessSource("tenant-c", "work", Item);
        (await processor.ProcessAsync(SuspensionEnvelope(first.Tenant), ct)).ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        bool contested = false;
        store.ConcurrentWriteBeforeTrySave = () =>
        {
            if (store.Snapshot<ProcessCandidateMarker>("statestore", ProcessStateKeys.CandidateMarker(second)) is null)
                return;
            // The second candidate is durable; its next write appends to the shared tenant registry.
            store.ConcurrentWriteBeforeTrySave = null;
            contested = true;
            Task.Run(async () => (await processor.ProcessAsync(SuspensionEnvelope(winner.Tenant), ct).ConfigureAwait(false))
                .ShouldBe(EventStoreDomainEventProcessingResult.Processed), ct).GetAwaiter().GetResult();
        };

        (await processor.ProcessAsync(SuspensionEnvelope(second.Tenant), ct)).ShouldBe(EventStoreDomainEventProcessingResult.Processed);

        contested.ShouldBeTrue();
        var restarted = Checkpoints(store);
        (await restarted.ReadTenantPageAsync(0, ct)).ShouldNotBeNull().Entries
            .Order(StringComparer.Ordinal).ShouldBe([first.Tenant, second.Tenant, winner.Tenant]);
        foreach (ProcessSource expected in new[] { first, second, winner })
        {
            (await restarted.ContainsCandidateAsync(expected, ct)).ShouldBeTrue();
            (await restarted.ReadCandidatePageAsync(expected.Tenant, 0, ct)).ShouldNotBeNull().Entries.ShouldBe([expected]);
        }
        (await new ProcessSweepStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System)
            .GetAsync(ct)).PendingPublications.ShouldBeEmpty();
    }

    /// <summary>An empty pending-date fold persists its ordering fence and cannot be undone by an older suspension after restart.</summary>
    /// <param name="terminal">Whether cancellation rather than resume clears the suspension.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearedDateDiscoveryKeepsSequenceFenceAcrossStaleDeliveryAndRestart(bool terminal)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        var scope = handler.CreateScope(Tenant);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(Item);
        IEventPayload[] facts = [new WorkItemCreated(Item, 1, tenant, item, new Obligation("date discovery")),
            new WorkItemSuspended(Item, 2, tenant, item, [s_date]),
            terminal ? new WorkItemCancelled(Item, 3, tenant, item) : new WorkItemResumed(Item, 3, tenant, item, s_date)];
        IEventPayload[] committed = [];
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<Hexalith.EventStore.Contracts.Streams.StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Story48Streams.Page(Tenant, Item, committed));
        var intents = new WorksReminderIntentSource(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));
        var target = new ReminderTarget(Tenant, "work", Item);
        for (int index = 0; index < facts.Length; index++)
        {
            committed = facts[..(index + 1)];
            var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
            (await coordinator.JournalAsync(scope, lease, Delivery(facts[index], index + 1), ct))
                .ShouldBe(SharedProjectionJournalResult.Journaled);
            (await coordinator.CatchUpAsync(scope, (delivery, generation, token) =>
                handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct)).ShouldBe(1);
            var pending = (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct))
                .Value.ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
            if (index > 0) pending.LastSequences[Item].ShouldBe(index + 1);
            pending.Entries.ContainsKey(Item).ShouldBe(index == 1);
            IReadOnlyList<ReminderIntent> current = await intents.GetCurrentIntentsAsync(target, ct);
            current.Count.ShouldBe(index == 1 ? 1 : 0);
            if (index == 1) current.ShouldHaveSingleItem().DueUtc.ShouldBe(s_date.Instant!.Value);
        }

        var restarted = new SharedProjectionEpochCoordinator(store, store);
        var restartedHandler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        var refreshed = await restarted.RefreshLeaseAsync(scope, restartedHandler.OrdinaryWriterId, ct);
        (await restarted.JournalAsync(scope, refreshed, Delivery(facts[1], 2), ct))
            .ShouldBe(SharedProjectionJournalResult.AlreadyJournaled);
        (await restarted.CatchUpAsync(scope, (delivery, generation, token) =>
            restartedHandler.FoldCatchUpAsync(delivery, generation, restarted, token), ct)).ShouldBe(0);
        var active = await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct);
        // Bypass journal deduplication and exercise the domain fold's retained-history comparison too.
        (await restartedHandler.FoldCatchUpAsync(Delivery(facts[1], 2), active.Generation, restarted, ct)).ShouldBeEmpty();
        (await intents.GetCurrentIntentsAsync(target, ct)).ShouldBeEmpty();
        var retained = (await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct))
            .Value.ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
        retained.Entries.ShouldBeEmpty();
        retained.LastSequences[Item].ShouldBe(3);
        (await restarted.GetCheckpointAsync(scope, Item, ct)).ShouldNotBeNull().Position.ShouldBe(3);
    }

    /// <summary>A completion outage propagates after dispatch and retries only the retained marker after reconstruction.</summary>
    [Fact]
    public async Task CompletionMarkerFailureRemainsRetryableWithoutRepeatingReminderConvergence()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        IReminderRegistrar reminders = Substitute.For<IReminderRegistrar>();
        reminders.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>())
            .Returns(new ReminderConvergenceResult(1, 0, 0, 0, 0));
        EventStoreDomainEventEnvelope envelope = SuspensionEnvelope();
        using var services = Services(store, reminders);

        await Should.ThrowAsync<IOException>(() => Processor(services, Markers(store, failCompletion: true))
            .ProcessAsync(envelope, ct));

        var retainedMarkers = Markers(store);
        (await retainedMarkers.TryAcquireAsync(envelope.MessageId, ct))
            .ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.CompletionPending);
        (await Checkpoints(store).ContainsCandidateAsync(new(Tenant, "work", Item), ct)).ShouldBeTrue();
        // A second failed attempt must still refuse acknowledgement without invoking the handler again.
        using var failedRestart = Services(store, reminders);
        await Should.ThrowAsync<IOException>(() => Processor(failedRestart, Markers(store, failCompletion: true))
            .ProcessAsync(envelope, ct));
        (await retainedMarkers.TryAcquireAsync(envelope.MessageId, ct))
            .ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.CompletionPending);

        using var restarted = Services(store, reminders);
        (await Processor(restarted, Markers(store)).ProcessAsync(envelope, ct))
            .ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
        (await Markers(store).TryAcquireAsync(envelope.MessageId, ct))
            .ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.Completed);
        (await Processor(restarted, Markers(store)).ProcessAsync(envelope, ct))
            .ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
        _ = await reminders.Received(1).ConvergeAsync(new ReminderTarget(Tenant, "work", Item), Arg.Any<CancellationToken>());
        (await Checkpoints(store).ReadCandidatePageAsync(Tenant, 0, ct)).ShouldNotBeNull().Entries
            .ShouldBe([new ProcessSource(Tenant, "work", Item)]);
    }

    /// <summary>An uncommitted resume fold cannot change the accepted pending-await document or its watermark.</summary>
    [Fact]
    public async Task UncommittedPendingDateFoldLeavesAcceptedStateUntouchedUntilSdkCommit()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        var scope = handler.CreateScope(Tenant);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(Item);
        IEventPayload[] facts = [new WorkItemCreated(Item, 1, tenant, item, new Obligation("pending fold")),
            new WorkItemSuspended(Item, 2, tenant, item, [s_date])];
        for (int index = 0; index < facts.Length; index++)
        {
            var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
            (await coordinator.JournalAsync(scope, lease, Delivery(facts[index], index + 1), ct))
                .ShouldBe(SharedProjectionJournalResult.Journaled);
            (await coordinator.CatchUpAsync(scope, (delivery, generation, token) =>
                handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct)).ShouldBe(1);
        }
        var accepted = await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct);
        var loaded = accepted.Value.ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
        string before = accepted.Value.Value.GetRawText();
        SharedProjectionDelivery resumed = Delivery(new WorkItemResumed(Item, 3, tenant, item, s_date), 3);

        IReadOnlyList<ReadModelBatchOperation> proposed = await handler.FoldCatchUpAsync(resumed, accepted.Generation, coordinator, ct);

        var proposedPending = JsonSerializer.Deserialize<WorksRuntimeDocument>(proposed.Single(operation =>
                operation.Key == WorksReadModelKeys.RuntimePendingDateKey).CanonicalValue.Span,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
        proposedPending.Entries.ShouldBeEmpty();
        proposedPending.LastSequences[Item].ShouldBe(3);
        loaded.Entries[Item].ShouldHaveSingleItem().Instant.ShouldBe(s_date.Instant!.Value);
        loaded.LastSequences[Item].ShouldBe(2);
        var unchanged = await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, accepted.Generation,
            WorksReadModelKeys.RuntimePendingDateKey, ct);
        unchanged.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before);
        (await coordinator.GetCheckpointAsync(scope, Item, ct)).ShouldNotBeNull().Position.ShouldBe(2);

        // Discard the proposed operations, reconstruct over retained storage, and commit through the SDK.
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        var leaseAfterRestart = await restarted.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct);
        (await restarted.JournalAsync(scope, leaseAfterRestart, resumed, ct)).ShouldBe(SharedProjectionJournalResult.Journaled);
        // The admission checkpoint records journal durability; pending delivery proves the fold is not applied yet.
        (await restarted.GetCheckpointAsync(scope, Item, ct)).ShouldNotBeNull().Position.ShouldBe(3);
        (await restarted.GetStatusAsync(scope, ct)).PendingDeliveryCount.ShouldBe(1);
        bool rejected = false;
        store.BatchFaultHook = (phase, _, _) =>
        {
            if (phase == ReadModelBatchPhase.BeforeDispatch)
            {
                rejected = true;
                throw new IOException("synthetic pre-commit batch outage");
            }
            return Task.CompletedTask;
        };
        await Should.ThrowAsync<SharedProjectionWriteUnavailableException>(() => restarted.CatchUpAsync(scope, (delivery, generation, token) =>
            handler.FoldCatchUpAsync(delivery, generation, restarted, token), ct));
        rejected.ShouldBeTrue();
        store.BatchFaultHook = null;
        var afterRejection = new SharedProjectionEpochCoordinator(store, store);
        (await afterRejection.ReadGenerationAsync<WorksRuntimeDocument>(scope, accepted.Generation,
            WorksReadModelKeys.RuntimePendingDateKey, ct)).Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before);
        (await afterRejection.GetCheckpointAsync(scope, Item, ct)).ShouldNotBeNull().Position.ShouldBe(3);
        (await afterRejection.GetStatusAsync(scope, ct)).PendingDeliveryCount.ShouldBe(1);
        (await afterRejection.CatchUpAsync(scope, (delivery, generation, token) =>
            handler.FoldCatchUpAsync(delivery, generation, afterRejection, token), ct)).ShouldBe(1);
        var committed = (await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct))
            .Value.ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
        committed.Entries.ShouldBeEmpty();
        committed.LastSequences[Item].ShouldBe(3);
        (await afterRejection.GetStatusAsync(scope, ct)).PendingDeliveryCount.ShouldBe(0);
        (await restarted.GetCheckpointAsync(scope, Item, ct)).ShouldNotBeNull().Position.ShouldBe(3);
        loaded.Entries.ShouldContainKey(Item);
        loaded.LastSequences[Item].ShouldBe(2);
    }

    private static ProcessCheckpointStore Checkpoints(InMemoryReadModelStore store)
        => new(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System);

    private static ServiceProvider Services(InMemoryReadModelStore store, IReminderRegistrar reminders)
        => new ServiceCollection().AddSingleton(Checkpoints(store)).AddSingleton(reminders)
            .AddScoped<IEventStoreDomainEventHandler<WorkItemSuspended>, WorksRecoveryWakeupHandler<WorkItemSuspended>>()
            .BuildServiceProvider();

    private static EventStoreDomainEventProcessor Processor(ServiceProvider services, IEventStoreDomainEventMarkerStore markers)
        => new(services.GetRequiredService<IServiceScopeFactory>(),
            new Dictionary<string, Type> { [typeof(WorkItemSuspended).FullName!] = typeof(WorkItemSuspended) },
            markers, NullLogger<EventStoreDomainEventProcessor>.Instance);

    private static DaprEventStoreDomainEventMarkerStore Markers(InMemoryReadModelStore store, bool failCompletion = false)
    {
        var client = Substitute.For<DaprClient>();
        client.GetStateAsync<EventStoreDomainEventMarkerRecord?>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConsistencyMode?>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>()).Returns(async call =>
                (await store.GetAsync<EventStoreDomainEventMarkerRecord>(call.ArgAt<string>(0), call.ArgAt<string>(1),
                    call.Arg<CancellationToken>()).ConfigureAwait(false)).Value);
        client.GetStateAndETagAsync<EventStoreDomainEventMarkerRecord?>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConsistencyMode?>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var entry = await store.GetAsync<EventStoreDomainEventMarkerRecord>(call.ArgAt<string>(0), call.ArgAt<string>(1),
                    call.Arg<CancellationToken>()).ConfigureAwait(false);
                return (entry.Value, entry.ETag ?? string.Empty);
            });
        client.TrySaveStateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<EventStoreDomainEventMarkerRecord>(), Arg.Any<string>(),
            Arg.Any<StateOptions>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                EventStoreDomainEventMarkerRecord record = call.Arg<EventStoreDomainEventMarkerRecord>();
                if (failCompletion && record.State == EventStoreDomainEventMarkerState.Completed)
                    throw new IOException("synthetic completion marker outage");
                return store.TrySaveAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), record,
                    call.ArgAt<string>(3), call.Arg<CancellationToken>());
            });
        return new(client, Options.Create(new EventStoreDomainEventsOptions { TopicName = "work.events", SubscriptionRoute = "/work/events" }));
    }

    private static EventStoreDomainEventEnvelope SuspensionEnvelope(string tenant = Tenant)
        => new(tenant switch
        {
            "tenant-a" => "01ARZ3NDEKTSV4RRFFQ69G5FAX",
            "tenant-b" => "01ARZ3NDEKTSV4RRFFQ69G5FAY",
            "tenant-c" => "01ARZ3NDEKTSV4RRFFQ69G5FAZ",
            _ => "01ARZ3NDEKTSV4RRFFQ69G5FAV",
        }, Item, tenant, typeof(WorkItemSuspended).FullName!, 2,
            DateTimeOffset.UnixEpoch, "reminder-delivery", "json",
            JsonSerializer.SerializeToUtf8Bytes(new WorkItemSuspended(Item, 2, new TenantId(tenant), new WorkItemId(Item), [s_date])))
        { Domain = "work" };

    private static SharedProjectionDelivery Delivery(IEventPayload fact, long sequence)
    {
        var dto = new ProjectionEventDto(fact.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(fact, fact.GetType()),
            "json", sequence, DateTimeOffset.UnixEpoch, "reminder-delivery", GlobalPosition: sequence);
        return new(Item, sequence, JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(Tenant, "work", Item, [dto]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), JsonSerializer.SerializeToUtf8Bytes(sequence),
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
    }
}
