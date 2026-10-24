using System.Text.Json;

using Hexalith.EventStore.Client.Discovery;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Recovery;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Qualifies date occurrence identity, normalized discovery and complete-replay repair.</summary>
public sealed class WorksDateReminderCorrectnessTests
{
    private const string Tenant = "tenant-date-correctness";
    private const string Item = "date-item";
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private static readonly AwaitCondition s_date = AwaitCondition.DateReached(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Same-date re-suspension changes both scheduling and command identities; redelivery changes neither.</summary>
    [Fact]
    public async Task RepeatedDateOccurrencesRetainRedeliveryIdentityAndSeparateLaterSuspensions()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IEventPayload[] facts = History([s_date]);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Story48Streams.Page(Tenant, Item, facts));
        var target = new ReminderTarget(Tenant, "work", Item);
        var source = Source(gateway);
        ReminderIntent first = (await source.GetCurrentIntentsAsync(target, ct)).ShouldHaveSingleItem();
        ReminderIntent replay = (await Source(gateway).GetCurrentIntentsAsync(target, ct)).ShouldHaveSingleItem();
        ReminderIdentityCodec.ComputeReminderName(replay).ShouldBe(ReminderIdentityCodec.ComputeReminderName(first));
        MessageId(replay).ShouldBe(MessageId(first));
        replay.Payload.ShouldBe(first.Payload);
        replay.SourceMessageId.ShouldBe(first.SourceMessageId);
        first.SourceSequence.ShouldBe(2);
        first.ScheduleRevision.ShouldBe(2);

        facts = [.. facts, new WorkItemResumed(Item, 3, new TenantId(Tenant), new WorkItemId(Item), s_date),
            new WorkItemSuspended(Item, 4, new TenantId(Tenant), new WorkItemId(Item), [s_date])];
        ReminderIntent later = (await Source(gateway).GetCurrentIntentsAsync(target, ct)).ShouldHaveSingleItem();
        later.SourceSequence.ShouldBe(4);
        later.ScheduleRevision.ShouldBe(4);
        later.SourceMessageId.ShouldNotBe(first.SourceMessageId);
        later.DueUtc.ShouldBe(first.DueUtc);
        later.Ordinal.ShouldBe(first.Ordinal);
        later.Payload.ShouldBe(first.Payload);
        ReminderIdentityCodec.ComputeReminderName(later).ShouldNotBe(ReminderIdentityCodec.ComputeReminderName(first));
        MessageId(later).ShouldNotBe(MessageId(first));
        ReminderCommand command = source.TranslateDueIntent(later);
        command.CommandType.ShouldBe(nameof(ResumeWorkItem));
        JsonSerializer.Deserialize<ResumeWorkItem>(command.Payload).ShouldBe(new ResumeWorkItem(new TenantId(Tenant), new WorkItemId(Item), s_date));
        ReminderIntent laterReplay = (await Source(gateway).GetCurrentIntentsAsync(target, ct)).ShouldHaveSingleItem();
        MessageId(laterReplay).ShouldBe(MessageId(later));
        ReminderIdentityCodec.ComputeReminderName(laterReplay).ShouldBe(ReminderIdentityCodec.ComputeReminderName(later));
    }

    /// <summary>Both SDK write paths persist one entry per normalized date and agree with the authoritative reminder fold.</summary>
    /// <param name="rebuild">Whether the initial generation is published through complete replay.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateDatesNormalizeAcrossSdkCommitRestartAndRedelivery(bool rebuild)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        AwaitCondition later = AwaitCondition.DateReached(s_date.Instant!.Value.AddHours(1));
        AwaitCondition sameInstant = AwaitCondition.DateReached(s_date.Instant.Value.ToOffset(TimeSpan.FromHours(2)));
        AwaitCondition[] conditions = [later, s_date, sameInstant, AwaitCondition.ExternalSignal("signal"),
            AwaitCondition.ChildCompleted(new WorkItemId("child")), later];
        IEventPayload[] facts = History(conditions);
        byte[] sourceBefore = JsonSerializer.SerializeToUtf8Bytes(facts[1], facts[1].GetType());
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = Handler();
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        if (rebuild)
        {
            await RebuildAsync(store, "initial", facts, ct);
        }
        else
        {
            foreach ((IEventPayload fact, int index) in facts.Select((fact, index) => (fact, index)))
            {
                SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
                (await coordinator.JournalAsync(scope, lease, Delivery(fact, index + 1), ct)).ShouldBe(SharedProjectionJournalResult.Journaled);
                (await coordinator.CatchUpAsync(scope, (delivery, generation, token) =>
                    handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct)).ShouldBe(1);
            }
        }

        var restarted = new SharedProjectionEpochCoordinator(store, store);
        var accepted = await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct);
        PendingDateAwaitTenantIndex pending = accepted.Value.ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
        pending.Entries[Item].ShouldBe([
            new PendingDateAwait(Tenant, Item, later.Instant!.Value, later.CorrelationKey),
            new PendingDateAwait(Tenant, Item, s_date.Instant.Value, s_date.CorrelationKey)]);
        pending.LastSequences[Item].ShouldBe(2);
        JsonSerializer.SerializeToUtf8Bytes(facts[1], facts[1].GetType()).ShouldBe(sourceBefore);
        conditions.ShouldBe([later, s_date, sameInstant, AwaitCondition.ExternalSignal("signal"),
            AwaitCondition.ChildCompleted(new WorkItemId("child")), later]);
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Story48Streams.Page(Tenant, Item, facts));
        IReadOnlyList<ReminderIntent> intents = await Source(gateway).GetCurrentIntentsAsync(new(Tenant, "work", Item), ct);
        intents.Select(intent => intent.DueUtc).Order().ShouldBe(pending.Entries[Item].Select(entry => entry.Instant).Order());
        intents.Select(MessageId).Distinct().Count().ShouldBe(2);
        foreach (ReminderIntent intent in intents)
            JsonSerializer.Deserialize<ResumeWorkItem>(intent.Payload).ShouldBe(new ResumeWorkItem(new TenantId(Tenant),
                new WorkItemId(Item), AwaitCondition.DateReached(intent.DueUtc)));

        string before = accepted.Value.Value.GetRawText();
        IReadOnlyList<ReadModelBatchOperation> repeated = await handler.FoldCatchUpAsync(Delivery(facts[1], 2),
            accepted.Generation, restarted, ct);
        repeated.ShouldBeEmpty();
        (await new SharedProjectionEpochCoordinator(store, store).ReadAsync<WorksRuntimeDocument>(scope,
            WorksReadModelKeys.RuntimePendingDateKey, ct)).Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before);
    }

    /// <summary>A full rebuild repairs a legacy skipped-event watermark without changing the authoritative evidence.</summary>
    /// <param name="parked">Whether a previous decoder also left terminal parking evidence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteReplayRepairsLegacyPendingWatermarkFromUnchangedRecognizedEvidence(bool parked)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IEventPayload[] facts = History([s_date]);
        var store = new InMemoryReadModelStore();
        var handler = Handler();
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        DomainProjectionRebuildPlan plan = await PlanAsync(handler, "legacy", facts, ct);
        // Model the historical decoder's persisted outcome; this does not execute an older decoder.
        // The exact same captured envelopes below are understood by the current decoder.
        var legacy = new PendingDateAwaitTenantIndex { LastSequences = new(StringComparer.Ordinal) { [Item] = 2 } };
        ReadModelBatchOperation[] oldOperations = [.. plan.Operations.Select(operation => operation.Key switch
        {
            WorksReadModelKeys.RuntimePendingDateKey => ReadModelBatchOperation.Write(operation.Key,
                WorksRuntimeDocument.From(legacy), ReadModelBatchConcurrency.LastWrite),
            var key when key == WorksReadModelKeys.RuntimeParkingKey(Item) => ReadModelBatchOperation.Write(key,
                WorksRuntimeDocument.From(new WorkItemProjectionParking { FailedSequence = parked ? 2 : 0,
                    FailureCount = parked ? 5 : 0, Parked = parked }), ReadModelBatchConcurrency.LastWrite),
            _ => operation,
        })];
        await CommitAsync(coordinator, scope, "legacy", oldOperations, ct);
        var before = await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct);
        before.Value.ShouldNotBeNull().As<PendingDateAwaitTenantIndex>().Entries.ShouldBeEmpty();
        byte[] sourceBytes = WorkItemRuntimeHistoryStore.Serialize(await WorkItemRuntimeHistoryStore.ReadAsync(
            coordinator, scope, before.Generation, Item, ct));

        await RebuildAsync(store, "repair", facts, ct, async stagedCoordinator =>
        {
            var staged = await stagedCoordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct)
                .ConfigureAwait(false);
            staged.Generation.ShouldBe(before.Generation);
            staged.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before.Value.Value.GetRawText());
        });
        var afterRestart = new SharedProjectionEpochCoordinator(store, store);
        var repaired = await afterRestart.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimePendingDateKey, ct);
        repaired.IsAvailable.ShouldBeTrue();
        repaired.IsStale.ShouldBeFalse();
        repaired.Generation.ShouldBeGreaterThan(before.Generation);
        var pending = repaired.Value.ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
        pending.LastSequences[Item].ShouldBe(2);
        pending.Entries[Item].ShouldHaveSingleItem().ShouldBe(new PendingDateAwait(Tenant, Item,
            s_date.Instant!.Value, s_date.CorrelationKey));
        (await afterRestart.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeParkingKey(Item), ct))
            .Value.ShouldNotBeNull().As<WorkItemProjectionParking>().Parked.ShouldBeFalse();
        WorkItemRuntimeHistoryStore.Serialize(await WorkItemRuntimeHistoryStore.ReadAsync(afterRestart, scope,
            repaired.Generation, Item, ct)).ShouldBe(sourceBytes);
    }

    private static async Task RebuildAsync(InMemoryReadModelStore store, string operation, IEventPayload[] facts,
        CancellationToken cancellationToken, Func<SharedProjectionEpochCoordinator, Task>? beforeCommit = null)
    {
        var handler = Handler();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var services = new ServiceCollection();
        services.AddSingleton<IReadModelStore>(store);
        services.AddSingleton<IReadModelBatchStore>(store);
        services.AddSingleton<IReadModelBatchStagingStore>(store);
        services.AddSingleton(coordinator);
        services.AddScoped<IAsyncDomainProjectionHandler>(_ => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        var dispatchOptions = new ProjectionDispatchOptions();
        var hostIdentity = new DomainProjectionIdentityOptions { AppId = "works-test", ServiceVersion = "v1" };
        string catalog = AdminOperationalIndexMetadata.Create(new DiscoveryResult([], []), ["work"], [], [handler],
            hostIdentity.AppId, hostIdentity.ServiceVersion, dispatchOptions, ["work"]).CatalogFingerprint!;
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily,
            operation, catalog);
        ProjectionEventDto[] history = [.. facts.Select((fact, index) => Dto(fact, index + 1))];
        _ = await coordinator.RegisterWriterAsync(handler.CreateScope(Tenant), handler.OrdinaryWriterId, cancellationToken)
            .ConfigureAwait(false);
        async Task<DomainSharedProjectionRebuildResponse> DispatchAsync(DomainSharedProjectionRebuildRequest request)
        {
            DomainSharedProjectionRebuildResponse response = await DomainSharedProjectionRebuildDispatcher.DispatchAsync(
                provider, request, dispatchOptions, hostIdentity, cancellationToken).ConfigureAwait(false);
            response.Status.ShouldNotBe(ProjectionDispatchStatus.Failed);
            return response;
        }
        _ = await DispatchAsync(new(DomainSharedProjectionRebuildProtocol.Version,
            DomainSharedProjectionRebuildAction.Begin, identity,
            SourceHighWatermarks: new Dictionary<string, long> { [Item] = 2 },
            CaptureInventoryFingerprint: DomainSharedProjectionRebuildFingerprint.AppendInventory(
                DomainSharedProjectionRebuildFingerprint.EmptyInventory, 0,
                DomainSharedProjectionRebuildFingerprint.ComputeHistory(Item, false, history)))).ConfigureAwait(false);
        DomainSharedProjectionRebuildResponse accumulated = await DispatchAsync(new(DomainSharedProjectionRebuildProtocol.Version,
            DomainSharedProjectionRebuildAction.Accumulate, identity, 0, Item, Events: history)).ConfigureAwait(false);
        accumulated.AcceptedAggregateCount.ShouldBe(1);
        DomainSharedProjectionRebuildResponse finalized = await DispatchAsync(new(DomainSharedProjectionRebuildProtocol.Version,
            DomainSharedProjectionRebuildAction.Finalize, identity, ExpectedAggregateCount: accumulated.AcceptedAggregateCount,
            ExpectedInventoryFingerprint: accumulated.InventoryFingerprint)).ConfigureAwait(false);
        finalized.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Finalized);
        DomainSharedProjectionRebuildResponse staged = await DispatchAsync(new(DomainSharedProjectionRebuildProtocol.Version,
            DomainSharedProjectionRebuildAction.Stage, identity)).ConfigureAwait(false);
        staged.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Prepared);
        if (beforeCommit is not null) await beforeCommit(coordinator).ConfigureAwait(false);
        DomainSharedProjectionRebuildResponse committed = await DispatchAsync(new(DomainSharedProjectionRebuildProtocol.Version,
            DomainSharedProjectionRebuildAction.Commit, identity)).ConfigureAwait(false);
        committed.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
    }

    private static IEventPayload[] History(IReadOnlyList<AwaitCondition> dates)
        => [new WorkItemCreated(Item, 1, new TenantId(Tenant), new WorkItemId(Item), new Obligation("dates")),
            new WorkItemSuspended(Item, 2, new TenantId(Tenant), new WorkItemId(Item), dates)];

    private static WorksReminderIntentSource Source(IEventStoreGatewayClient gateway)
        => new(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())));

    private static string MessageId(ReminderIntent intent)
        => EffectIdentityCodec.ComputeMessageId(new(intent.Tenant, intent.SourceDomain, intent.SourceAggregate,
            intent.SourceSequence, intent.Kind, intent.TargetDomain, intent.TargetAggregate, intent.Ordinal));

    private static WorkItemSharedProjectionRebuildHandler Handler()
        => new(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));

    private static ProjectionEventDto Dto(IEventPayload fact, long sequence)
        => new(fact.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(fact, fact.GetType()), "json", sequence,
            DateTimeOffset.UnixEpoch, "date-correctness", GlobalPosition: sequence);

    private static SharedProjectionDelivery Delivery(IEventPayload fact, long sequence)
        => new(Item, sequence, JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(Tenant, "work", Item,
            [Dto(fact, sequence)]), s_web), JsonSerializer.SerializeToUtf8Bytes(sequence),
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));

    private static async Task<DomainProjectionRebuildPlan> PlanAsync(WorkItemSharedProjectionRebuildHandler handler,
        string operation, IEventPayload[] facts, CancellationToken cancellationToken)
    {
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily,
            operation, "capture-" + operation);
        var candidate = await handler.CreateEmptyCandidateAsync(identity, cancellationToken).ConfigureAwait(false);
        candidate = await handler.AccumulateAsync(identity, candidate, new ProjectionRequest(Tenant, "work", Item,
            [.. facts.Select((fact, index) => Dto(fact, index + 1))]), cancellationToken).ConfigureAwait(false);
        return await handler.FinalizeAsync(identity, candidate, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CommitAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope,
        string operation, IReadOnlyList<ReadModelBatchOperation> operations, CancellationToken cancellationToken)
    {
        _ = await coordinator.RegisterWriterAsync(scope, Handler().OrdinaryWriterId, cancellationToken).ConfigureAwait(false);
        _ = await coordinator.BeginAsync(scope, operation, "capture-" + operation, new Dictionary<string, long> { [Item] = 2 }, cancellationToken).ConfigureAwait(false);
        _ = await coordinator.StageAsync(scope, operation, operations, cancellationToken,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(false);
        await coordinator.CommitAsync(scope, operation, cancellationToken).ConfigureAwait(false);
    }
}
