using System.Text.Json;

using Hexalith.EventStore.Client.Discovery;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Qualifies deferred partition admission and capacity claims against the actual SDK lifecycle.</summary>
public sealed class WorksSdkRebuildAdmissionTests
{
    private const string Tenant = "rebuild-admission";
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>Erased inventory members leave valid gaps in the partitions supplied by the SDK.</summary>
    [Fact]
    public async Task ErasedOwnerOrdinalGapsPromoteOnlyTheSurvivingAuthoritativeHistory()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var handler = new WorkItemSharedProjectionRebuildHandler();
        ProjectionRequest survivor = History("owner-b");
        using ServiceProvider provider = Provider(store, handler, store);
        await RebuildAsync(provider, handler, "erased-ordinal", [new ProjectionRequest(Tenant, "work", "owner-a", []), survivor]);

        var cold = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var queue = await cold.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct);
        queue.IsAvailable.ShouldBeTrue();
        queue.Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>().MemberWorkItemIds.ShouldBe(["owner-b"]);
        (await cold.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("owner-a"), ct)).Value.ShouldBeNull();
        (await cold.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("owner-b"), ct))
            .Value.ShouldNotBeNull().As<WorkItemRollUp>().Status.ShouldBe(WorkItemStatus.Created);
        (await WorkItemRuntimeHistoryStore.ReadAsync(cold, scope, queue.Generation, "owner-b", ct)).Events
            .Select(item => Convert.ToBase64String(item.Payload)).ShouldBe(survivor.Events.Select(item => Convert.ToBase64String(item.Payload)));
    }

    /// <summary>An empty replacement cannot discard the previously committed tenant generation.</summary>
    [Fact]
    public async Task EmptyPartitionFinalizationPreservesTheCommittedGenerationAfterReconstruction()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var handler = new WorkItemSharedProjectionRebuildHandler();
        using ServiceProvider provider = Provider(store, handler, store);
        await RebuildAsync(provider, handler, "seed-nonempty", [History("owner-a")]);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var before = await new SharedProjectionEpochCoordinator(store, store)
            .ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("owner-a"), ct);
        byte[] beforeHistory = JsonSerializer.SerializeToUtf8Bytes(await WorkItemRuntimeHistoryStore.ReadAsync(
            new SharedProjectionEpochCoordinator(store, store), scope, before.Generation, "owner-a", ct), s_json);
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "refuse-empty", Catalog(handler));
        _ = await Should.ThrowAsync<InvalidOperationException>(() => handler.FinalizePartitionsAsync(identity, [], ct));

        await RebuildAsync(provider, handler, "refuse-empty", [], expectRefusal: true);
        var cold = new SharedProjectionEpochCoordinator(store, store);
        (await cold.GetStatusAsync(scope, ct)).ActiveGeneration.ShouldBe(before.Generation);
        var after = await cold.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("owner-a"), ct);
        after.IsAvailable.ShouldBeTrue();
        after.Generation.ShouldBe(before.Generation);
        after.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before.Value.ShouldNotBeNull().Value.GetRawText());
        JsonSerializer.SerializeToUtf8Bytes(await WorkItemRuntimeHistoryStore.ReadAsync(cold, scope, after.Generation, "owner-a", ct), s_json)
            .ShouldBe(beforeHistory);
    }

    /// <summary>Both preparation APIs admit the exact byte ceiling and refuse one byte beyond it.</summary>
    [Fact]
    public async Task OwnerHistoryLimitIsEnforcedDuringBothPartitionPreparationApis()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var handler = new WorkItemSharedProjectionRebuildHandler();
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "owner-limit", Catalog(handler));
        ProjectionRequest admitted = History("owner-a", WorkItemRuntimeHistoryStore.MaxItemBytes);
        (await handler.PreparePartitionAsync(identity, admitted, ct)).Length.ShouldBe(WorkItemRuntimeHistoryStore.MaxItemBytes);
        IReadOnlyList<ReadOnlyMemory<byte>> chunks = await handler.PreparePartitionChunksAsync(identity, admitted, ct);
        chunks.Count.ShouldBe(64);
        chunks.ShouldAllBe(chunk => chunk.Length == WorkItemRuntimeHistoryStore.PageBytes);

        ProjectionRequest oversized = admitted with { Events = [admitted.Events[0] with { CorrelationId = admitted.Events[0].CorrelationId + "x" }] };
        _ = await Should.ThrowAsync<SharedProjectionCapacityExceededException>(() => handler.PreparePartitionAsync(identity, oversized, ct));
        _ = await Should.ThrowAsync<SharedProjectionCapacityExceededException>(() => handler.PreparePartitionChunksAsync(identity, oversized, ct));
    }

    /// <summary>Invalid owner ordinals and oversized histories cannot advance persisted SDK inventory.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusedSdkAccumulationPreservesInventoryAndThePriorReadableGeneration(bool oversized)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var handler = new WorkItemSharedProjectionRebuildHandler();
        using ServiceProvider provider = Provider(store, handler, store);
        await RebuildAsync(provider, handler, "seed-refused-accumulation", [History("seeded-owner")]);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var coordinator = provider.GetRequiredService<SharedProjectionEpochCoordinator>();
        var before = await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("seeded-owner"), ct);
        ProjectionRequest history = History("owner-a", oversized ? WorkItemRuntimeHistoryStore.MaxItemBytes + 1 : 0);
        string fingerprint = DomainSharedProjectionRebuildFingerprint.AppendInventory(DomainSharedProjectionRebuildFingerprint.EmptyInventory,
            0, DomainSharedProjectionRebuildFingerprint.ComputeHistory(history.AggregateId, false, history.Events));
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "refused-accumulation", Catalog(handler));
        DomainSharedProjectionRebuildRequest Request(DomainSharedProjectionRebuildAction action) => new(DomainSharedProjectionRebuildProtocol.Version, action, identity);
        Task<DomainSharedProjectionRebuildResponse> Dispatch(DomainSharedProjectionRebuildRequest request) => DomainSharedProjectionRebuildDispatcher.DispatchAsync(
            provider, request, new ProjectionDispatchOptions(), new DomainProjectionIdentityOptions { AppId = "works-test", ServiceVersion = "v1" }, ct);
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Begin) with
        {
            CaptureInventoryFingerprint = fingerprint,
            SourceHighWatermarks = new Dictionary<string, long> { [history.AggregateId] = 10 },
        })).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Accumulating);
        int keysBeforeAccumulation = store.Count;
        DomainSharedProjectionRebuildResponse refused = await Dispatch(Request(DomainSharedProjectionRebuildAction.Accumulate) with
        {
            AggregateOrdinal = oversized ? 0 : 1,
            AggregateId = history.AggregateId,
            Events = history.Events,
        });
        refused.Status.ShouldBe(oversized ? ProjectionDispatchStatus.Indeterminate : ProjectionDispatchStatus.Retryable);
        refused.ReasonCode.ShouldBe(oversized ? ProjectionDispatchReasonCodes.HandlerFailure : ProjectionDispatchReasonCodes.DeliveryGap);
        store.Count.ShouldBe(keysBeforeAccumulation);
        DomainSharedProjectionRebuildResponse inspected = await Dispatch(Request(DomainSharedProjectionRebuildAction.Inspect) with { CaptureInventoryFingerprint = fingerprint });
        inspected.Status.ShouldBe(ProjectionDispatchStatus.Completed);
        inspected.AcceptedAggregateCount.ShouldBe(0);
        inspected.InventoryFingerprint.ShouldBe(DomainSharedProjectionRebuildFingerprint.EmptyInventory);
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Finalize) with { ExpectedAggregateCount = 1, ExpectedInventoryFingerprint = fingerprint }))
            .Phase.ShouldNotBe(DomainSharedProjectionRebuildPhase.Committed);
        var cold = new SharedProjectionEpochCoordinator(store, store);
        (await cold.GetStatusAsync(scope, ct)).ActiveGeneration.ShouldBe(before.Generation);
        var after = await cold.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("seeded-owner"), ct);
        after.IsAvailable.ShouldBeTrue();
        after.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before.Value.ShouldNotBeNull().Value.GetRawText());
    }

    /// <summary>Actual plans cover multi-page owners and the supported aggregate-count ceiling.</summary>
    [Theory]
    [InlineData(3, 140000)]
    [InlineData(10000, 140000)]
    public async Task ActualRebuildPlansFitTheRegisteredOperationCeiling(int owners, int historyBytes)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var handler = new WorkItemSharedProjectionRebuildHandler();
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "operation-count", Catalog(handler));
        var partitions = new List<DomainSharedProjectionRebuildPartition>();
        var heads = new Dictionary<string, long>(StringComparer.Ordinal);
        for (int owner = 0; owner < owners; owner++)
        {
            ProjectionRequest history = History($"owner-{owner:D5}".PadRight(64, 'x'), owners == 10000 && owner >= 16 ? 0 : historyBytes);
            heads.Add(history.AggregateId, 10);
            IReadOnlyList<ReadOnlyMemory<byte>> chunks = await handler.PreparePartitionChunksAsync(identity, history, ct);
            partitions.AddRange(chunks.Select((chunk, index) => new DomainSharedProjectionRebuildPartition(owner, history.AggregateId, chunk, index)));
        }
        DomainProjectionRebuildPlan plan = await handler.FinalizePartitionsAsync(identity, partitions, ct);
        // Count the actual history pages plus history manifests, parking and roll-ups, and the three family indexes.
        plan.Operations.Count.ShouldBe(3 + (owners * 3) + partitions.Count);
        plan.Operations.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count().ShouldBe(plan.Operations.Count);
        ReadModelBatchOptions configured = ProductionBatchOptions();
        plan.Operations.Count.ShouldBeLessThanOrEqualTo(configured.MaxOperations);
        configured.MaxOperations.ShouldBe(WorksRebuildOperationBound.MaxOperations);
        if (historyBytes > 0) partitions.Count.ShouldBeGreaterThan(owners);
        long tenantBytes = partitions.Sum(partition => (long)partition.State.Length);
        tenantBytes.ShouldBeLessThanOrEqualTo(WorkItemRuntimeHistoryStore.MaxTenantBytes);
        TestContext.Current.TestOutputHelper!.WriteLine($"owners={owners}; pages={partitions.Count}; operations={plan.Operations.Count}; tenantBytes={tenantBytes}; limit={configured.MaxOperations}");
        if (owners == 10000)
        {
            plan.Operations.Count.ShouldBeGreaterThan(40003);
            // Operation-count admission does not override the SDK's independent per-document bound.
            var store = new InMemoryReadModelStore();
            store.BatchOptions.MaxOperations = configured.MaxOperations;
            store.BatchOptions.MaxCanonicalManifestBytes = configured.MaxCanonicalManifestBytes;
            using ServiceProvider provider = Provider(store, handler, store);
            await RebuildAsync(provider, handler, "seed-large-queue", [History("seeded-owner")]);
            var coordinator = provider.GetRequiredService<SharedProjectionEpochCoordinator>();
            SharedProjectionScope scope = handler.CreateScope(Tenant);
            var before = await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("seeded-owner"), ct);
            _ = await coordinator.BeginAsync(scope, "large-queue", "counted-inventory", heads, ct);
            int keysBeforeStage = store.Count;
            (await Should.ThrowAsync<InvalidOperationException>(() => coordinator.StageAsync(scope, "large-queue", plan.Operations, ct)))
                .Message.ShouldContain("mutation exceeds the bounded batch limit");
            store.Count.ShouldBe(keysBeforeStage);
            var cold = new SharedProjectionEpochCoordinator(store, store);
            (await cold.GetStatusAsync(scope, ct)).ActiveGeneration.ShouldBe(before.Generation);
            var after = await cold.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("seeded-owner"), ct);
            after.IsAvailable.ShouldBeTrue();
            after.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before.Value.ShouldNotBeNull().Value.GetRawText());
            TestContext.Current.TestOutputHelper!.WriteLine($"largeQueueCanonicalValueBytes={plan.Operations.Single(operation => operation.Key == WorksReadModelKeys.RuntimeQueueKey).CanonicalValue.Length}; stage=refused-before-writes; priorGeneration={before.Generation}");
        }
    }

    /// <summary>The full tenant-byte ceiling commits through bounded batches with exact persisted context.</summary>
    [Fact]
    public async Task TenantByteCeilingRebuildUsesBoundedRealBatchesAndRetainsEveryHistoryAfterRestart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        ReadModelBatchOptions configured = ProductionBatchOptions();
        store.BatchOptions.MaxOperations = configured.MaxOperations;
        store.BatchOptions.MaxCanonicalManifestBytes = configured.MaxCanonicalManifestBytes;
        var observed = new WorksSdkRebuildBatchObserver(store);
        var handler = new WorkItemSharedProjectionRebuildHandler();
        ProjectionRequest[] histories = [.. Enumerable.Range(0, 4).Select(owner => History($"owner-{owner:D5}".PadRight(64, 'x'), WorkItemRuntimeHistoryStore.MaxItemBytes))];
        histories.Sum(history => (long)JsonSerializer.SerializeToUtf8Bytes(new WorkItemSharedRebuildAggregateHistory(history.AggregateId, history.Events), s_json).Length)
            .ShouldBe(WorkItemRuntimeHistoryStore.MaxTenantBytes);
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "tenant-byte-ceiling", Catalog(handler));
        DomainProjectionRebuildPlan logicalPlan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity,
            new WorkItemSharedRebuildCandidateState([.. histories.Select(history => new WorkItemSharedRebuildAggregateHistory(history.AggregateId, history.Events))]), ct);
        var logicalBatch = new ReadModelBatch(
            new ReadModelBatchScope(WorksReadModelKeys.StateStoreName, Tenant, "work", "family", handler.ProjectionType, "logical-only"), logicalPlan.Operations);
        int logicalBytes = WorksSdkRebuildBatchObserver.CanonicalBytes(logicalBatch);
        // This full logical plan would exceed one batch; the real SDK must stage it as bounded batches.
        logicalBytes.ShouldBeGreaterThan(configured.MaxCanonicalManifestBytes);
        (await Should.ThrowAsync<ArgumentException>(() => store.ExecuteAsync(logicalBatch, ct))).Message.ShouldContain("canonical manifest");
        store.Count.ShouldBe(0);
        using ServiceProvider provider = Provider(store, handler, observed);
        await RebuildAsync(provider, handler, "tenant-byte-ceiling", histories);

        observed.Batches.Count.ShouldBeGreaterThan(4);
        observed.Batches.ShouldAllBe(batch => batch.Operations <= configured.MaxOperations && batch.CanonicalBytes <= configured.MaxCanonicalManifestBytes);
        var cold = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var queue = await cold.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct);
        queue.IsAvailable.ShouldBeTrue();
        queue.IsStale.ShouldBeFalse();
        queue.Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>().MemberWorkItemIds.ShouldBe(histories.Select(history => history.AggregateId));
        foreach (ProjectionRequest history in histories)
        {
            WorkItemSharedRebuildAggregateHistory restored = await WorkItemRuntimeHistoryStore.ReadAsync(cold, scope, queue.Generation, history.AggregateId, ct);
            JsonSerializer.SerializeToUtf8Bytes(restored, s_json).ShouldBe(JsonSerializer.SerializeToUtf8Bytes(new WorkItemSharedRebuildAggregateHistory(history.AggregateId, history.Events), s_json));
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"tenantBytes={WorkItemRuntimeHistoryStore.MaxTenantBytes}; logicalCanonicalBytes={logicalBytes}; batches={observed.Batches.Count}; maxOperations={observed.Batches.Max(batch => batch.Operations)}; maxCanonicalBytes={observed.Batches.Max(batch => batch.CanonicalBytes)}; manifestLimit={configured.MaxCanonicalManifestBytes}");
    }

    private static ReadModelBatchOptions ProductionBatchOptions()
    {
        var services = new ServiceCollection();
        services.AddWorksDomainAdapters(new ConfigurationBuilder().Build());
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ReadModelBatchOptions>>().Value;
    }

    private static ProjectionRequest History(string aggregate, int targetBytes = 0)
    {
        ProjectionEventDto Event(string description) => new(nameof(WorkItemCreated),
            JsonSerializer.SerializeToUtf8Bytes(new WorkItemCreated(aggregate, 1, new TenantId(Tenant), new WorkItemId(aggregate), new Obligation(description))),
            "json", 1, DateTimeOffset.UnixEpoch, "", GlobalPosition: 10);
        ProjectionEventDto dto = Event("x");
        int Size(ProjectionEventDto item) => JsonSerializer.SerializeToUtf8Bytes(new WorkItemSharedRebuildAggregateHistory(aggregate, [item]), s_json).Length;
        if (targetBytes > 0)
        {
            int addedPayloadBytes = Math.Max(0, ((targetBytes - Size(dto)) / 4 * 3) - 3);
            dto = Event(new string('x', addedPayloadBytes + 1));
            dto = dto with { CorrelationId = new string('p', targetBytes - Size(dto)) };
            Size(dto).ShouldBe(targetBytes);
        }
        return new ProjectionRequest(Tenant, "work", aggregate, [dto]);
    }

    private static string Catalog(WorkItemSharedProjectionRebuildHandler handler)
        => AdminOperationalIndexMetadata.Create(new DiscoveryResult([], []), ["work"], [], [handler], "works-test", "v1", new ProjectionDispatchOptions(), ["work"]).CatalogFingerprint!;

    private static ServiceProvider Provider(InMemoryReadModelStore store, WorkItemSharedProjectionRebuildHandler handler, IReadModelBatchStore batches)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReadModelStore>(store);
        services.AddSingleton(batches);
        services.AddSingleton<IReadModelBatchStagingStore>(store);
        services.AddSingleton(new SharedProjectionEpochCoordinator(store, batches));
        services.AddSingleton<IAsyncDomainProjectionHandler>(handler);
        return services.BuildServiceProvider();
    }

    private static async Task RebuildAsync(ServiceProvider provider, WorkItemSharedProjectionRebuildHandler handler, string operation,
        IReadOnlyList<ProjectionRequest> histories, bool expectRefusal = false)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, operation, Catalog(handler));
        var coordinator = provider.GetRequiredService<SharedProjectionEpochCoordinator>();
        _ = await coordinator.RegisterWriterAsync(handler.CreateScope(Tenant), handler.OrdinaryWriterId, ct).ConfigureAwait(false);
        string fingerprint = DomainSharedProjectionRebuildFingerprint.EmptyInventory;
        for (int ordinal = 0; ordinal < histories.Count; ordinal++)
        {
            ProjectionRequest history = histories[ordinal];
            fingerprint = DomainSharedProjectionRebuildFingerprint.AppendInventory(fingerprint, ordinal,
                DomainSharedProjectionRebuildFingerprint.ComputeHistory(history.AggregateId, history.Events.Length == 0, history.Events));
        }
        DomainSharedProjectionRebuildRequest Request(DomainSharedProjectionRebuildAction action) => new(DomainSharedProjectionRebuildProtocol.Version, action, identity);
        Task<DomainSharedProjectionRebuildResponse> Dispatch(DomainSharedProjectionRebuildRequest request) => DomainSharedProjectionRebuildDispatcher.DispatchAsync(
            provider, request, new ProjectionDispatchOptions(), new DomainProjectionIdentityOptions { AppId = "works-test", ServiceVersion = "v1" }, ct);
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Begin) with
        {
            CaptureInventoryFingerprint = fingerprint,
            SourceHighWatermarks = histories.ToDictionary(history => history.AggregateId, history => history.Events.LastOrDefault()?.GlobalPosition ?? 10),
            ErasedOwnerIds = [.. histories.Where(history => history.Events.Length == 0).Select(history => history.AggregateId)],
        }).ConfigureAwait(false)).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Accumulating);
        for (int ordinal = 0; ordinal < histories.Count; ordinal++)
        {
            ProjectionRequest history = histories[ordinal];
            (await Dispatch(Request(DomainSharedProjectionRebuildAction.Accumulate) with
            {
                AggregateOrdinal = ordinal,
                AggregateId = history.AggregateId,
                IsErased = history.Events.Length == 0,
                Events = history.Events,
            }).ConfigureAwait(false)).Status.ShouldBe(ProjectionDispatchStatus.Completed);
        }
        DomainSharedProjectionRebuildResponse finalized = await Dispatch(Request(DomainSharedProjectionRebuildAction.Finalize) with
        {
            ExpectedAggregateCount = histories.Count,
            ExpectedInventoryFingerprint = fingerprint,
        }).ConfigureAwait(false);
        if (expectRefusal)
        {
            finalized.Status.ShouldBe(ProjectionDispatchStatus.Indeterminate);
            finalized.ReasonCode.ShouldBe(ProjectionDispatchReasonCodes.HandlerFailure);
            return;
        }
        finalized.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Finalized);
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Stage)).ConfigureAwait(false)).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Prepared);
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Commit)).ConfigureAwait(false)).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Verify)).ConfigureAwait(false)).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
    }
}
