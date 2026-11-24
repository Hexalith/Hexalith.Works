using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Client.Discovery;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
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
        var queue = await WorksRuntimeIndexReader.ReadAsync(cold, scope, ct);
        queue.IsAvailable.ShouldBeTrue();
        queue.Value.ShouldNotBeNull().Indexes.Queue.MemberWorkItemIds.ShouldBe(["owner-b"]);
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
        var histories = new List<ProjectionRequest>();
        var heads = new Dictionary<string, long>(StringComparer.Ordinal);
        for (int owner = 0; owner < owners; owner++)
        {
            ProjectionRequest history = History($"owner-{owner:D5}".PadRight(64, 'x'), owners == 10000 && owner >= 16 ? 0 : historyBytes);
            if (owner < 2)
                history = WithDateLifecycle(history, clear: owner == 0);
            histories.Add(history);
            heads.Add(history.AggregateId, history.Events[^1].GlobalPosition);
            IReadOnlyList<ReadOnlyMemory<byte>> chunks = await handler.PreparePartitionChunksAsync(identity, history, ct);
            partitions.AddRange(chunks.Select((chunk, index) => new DomainSharedProjectionRebuildPartition(owner, history.AggregateId, chunk, index)));
        }
        DomainProjectionRebuildPlan plan = await handler.FinalizePartitionsAsync(identity, partitions, ct);
        List<ReadModelBatchOperation> shardOperations = [.. plan.Operations.Where(operation =>
            operation.Key.StartsWith(WorksReadModelKeys.RuntimeQueueKey + ":shards:", StringComparison.Ordinal)
            || operation.Key.StartsWith(WorksReadModelKeys.RuntimePendingDateKey + ":shards:", StringComparison.Ordinal))];
        // The plan includes three fixed documents, a manifest/parking/roll-up per owner, history pages and index shards.
        plan.Operations.Count.ShouldBe(3 + (owners * 3) + partitions.Count + shardOperations.Count);
        shardOperations.Count.ShouldBeGreaterThan(0);
        plan.Operations.Where(operation => shardOperations.Contains(operation)
            || operation.Key is WorksReadModelKeys.RuntimeQueueKey or WorksReadModelKeys.RuntimePendingDateKey)
            .ShouldAllBe(operation => operation.CanonicalValue.Length + Encoding.UTF8.GetByteCount(operation.Key)
                + Encoding.UTF8.GetByteCount(operation.ValueTypeName) + 1024 <= 256 * 1024);
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
            var store = new InMemoryReadModelStore();
            store.BatchOptions.MaxOperations = configured.MaxOperations;
            store.BatchOptions.MaxCanonicalManifestBytes = configured.MaxCanonicalManifestBytes;
            using ServiceProvider provider = Provider(store, handler, store);
            await RebuildAsync(provider, handler, "seed-large-queue", [histories[0]]);
            var coordinator = provider.GetRequiredService<SharedProjectionEpochCoordinator>();
            SharedProjectionScope scope = handler.CreateScope(Tenant);
            long priorGeneration = (await coordinator.GetStatusAsync(scope, ct)).ActiveGeneration;
            SharedProjectionCheckpoint priorCheckpoint = (await coordinator.GetCheckpointAsync(scope, histories[0].AggregateId, ct))
                .ShouldNotBeNull();
            priorCheckpoint.Position.ShouldBe(heads[histories[0].AggregateId]);
            _ = await coordinator.BeginAsync(scope, "large-queue", "counted-inventory", heads, ct);
            _ = await coordinator.StageAsync(scope, "large-queue", plan.Operations, ct,
                new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
            var staged = await WorksRuntimeIndexReader.ReadAsync(coordinator, scope, ct);
            staged.IsAvailable.ShouldBeTrue();
            staged.IsStale.ShouldBeTrue();
            staged.Generation.ShouldBe(priorGeneration);
            staged.Value.ShouldNotBeNull().Indexes.Queue.MemberWorkItemIds.ShouldBe([histories[0].AggregateId]);
            await coordinator.CommitAsync(scope, "large-queue", ct);

            var cold = new SharedProjectionEpochCoordinator(store, store);
            var promoted = await WorksRuntimeIndexReader.ReadAsync(cold, scope, ct);
            promoted.Generation.ShouldBeGreaterThan(priorGeneration);
            promoted.IsAvailable.ShouldBeTrue();
            promoted.IsStale.ShouldBeFalse();
            WorksRuntimeIndexes indexes = promoted.Value.ShouldNotBeNull().Indexes;
            indexes.IsSharded.ShouldBeTrue();
            indexes.Queue.MemberWorkItemIds.ShouldBe(histories.Select(history => history.AggregateId));
            indexes.Queue.LastSequences.Count.ShouldBe(owners);
            indexes.PendingDate.LastSequences[histories[0].AggregateId].ShouldBe(5);
            indexes.PendingDate.Entries.ShouldNotContainKey(histories[0].AggregateId);
            indexes.PendingDate.LastSequences[histories[1].AggregateId].ShouldBe(4);
            indexes.PendingDate.Entries[histories[1].AggregateId].ShouldHaveSingleItem().Instant.ShouldBe(DateTimeOffset.UnixEpoch.AddDays(1));
            SharedProjectionCheckpoint survivingCheckpoint = (await cold.GetCheckpointAsync(scope, histories[0].AggregateId, ct))
                .ShouldNotBeNull();
            survivingCheckpoint.Position.ShouldBe(priorCheckpoint.Position);
            survivingCheckpoint.OwnerSequenceNumber.ShouldBe(priorCheckpoint.OwnerSequenceNumber);
            survivingCheckpoint.Token.ShouldBe(priorCheckpoint.Token);
            foreach (ProjectionRequest history in histories)
            {
                indexes.Queue.LastSequences[history.AggregateId].ShouldBe(history.Events[^1].SequenceNumber);
                WorkItemSharedRebuildAggregateHistory restored = await WorkItemRuntimeHistoryStore.ReadAsync(
                    cold, scope, promoted.Generation, history.AggregateId, ct);
                JsonSerializer.SerializeToUtf8Bytes(restored, s_json).ShouldBe(JsonSerializer.SerializeToUtf8Bytes(
                    new WorkItemSharedRebuildAggregateHistory(history.AggregateId, history.Events), s_json));
            }
            TestContext.Current.TestOutputHelper!.WriteLine($"shards={shardOperations.Count}; largestShardBytes={shardOperations.Max(operation => operation.CanonicalValue.Length)}; generation={promoted.Generation}");
        }
    }

    /// <summary>Staging splits a single skewed pending-date bucket at its real mutation byte limit.</summary>
    [Fact]
    public async Task SkewedPendingDateBucketSplitsAndReopensEveryPage()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var handler = new WorkItemSharedProjectionRebuildHandler();
        ProjectionRequest[] histories = [.. Enumerable.Range(0, 5000)
            .Select(id => $"skewed-{id:D4}".PadRight(64, 'x'))
            .Where(id => SHA256.HashData(Encoding.UTF8.GetBytes(id))[0] % 64 == 0)
            .Take(5)
            .Select((id, ordinal) => WithDateLifecycle(History(id, 140000), clear: ordinal == 0, dateCount: 512))];
        histories.Length.ShouldBe(5);
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "skewed-bucket", Catalog(handler));
        var candidate = new WorkItemSharedRebuildCandidateState(
            [.. histories.Select(history => new WorkItemSharedRebuildAggregateHistory(history.AggregateId, history.Events))]);
        DomainProjectionRebuildPlan unsplit = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, candidate, ct, useShardedIndexes: false);
        unsplit.Operations.Single(operation => operation.Key == WorksReadModelKeys.RuntimePendingDateKey)
            .CanonicalValue.Length.ShouldBeGreaterThan(256 * 1024);

        using ServiceProvider provider = Provider(store, handler, store);
        await RebuildAsync(provider, handler, "skewed-bucket", histories);
        var cold = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var read = await WorksRuntimeIndexReader.ReadAsync(cold, scope, ct);
        read.IsAvailable.ShouldBeTrue();
        WorksRuntimeIndexView view = read.Value.ShouldNotBeNull();
        WorksRuntimeIndexManifest pendingRoot = view.Documents[WorksReadModelKeys.RuntimePendingDateKey]
            .ShouldNotBeNull().As<WorksRuntimeIndexManifest>();
        int shardCount = pendingRoot.PageCounts.Where(count => count > 0).ShouldHaveSingleItem();
        shardCount.ShouldBeGreaterThan(1);
        for (int page = 0; page < shardCount; page++)
        {
            string key = WorksReadModelKeys.RuntimeIndexShardKey(WorksReadModelKeys.RuntimePendingDateKey, pendingRoot.Version, 0, page);
            var mutation = ReadModelBatchOperation.Write(key, view.Documents[key].ShouldNotBeNull(), ReadModelBatchConcurrency.LastWrite);
            (mutation.CanonicalValue.Length + Encoding.UTF8.GetByteCount(mutation.Key)
                + Encoding.UTF8.GetByteCount(mutation.ValueTypeName) + 1024).ShouldBeLessThanOrEqualTo(256 * 1024);
        }

        view.Indexes.Queue.MemberWorkItemIds.ShouldBe(histories.Select(history => history.AggregateId));
        view.Indexes.PendingDate.Entries.ShouldNotContainKey(histories[0].AggregateId);
        foreach (ProjectionRequest history in histories)
        {
            string owner = history.AggregateId;
            long lastSequence = history.Events[^1].SequenceNumber;
            view.Indexes.Queue.LastSequences[owner].ShouldBe(lastSequence);
            view.Indexes.PendingDate.LastSequences[owner].ShouldBe(lastSequence);
            if (lastSequence == 4)
                view.Indexes.PendingDate.Entries[owner].Select(entry => entry.Instant).ShouldBe(
                    Enumerable.Range(0, 512).Select(day => DateTimeOffset.UnixEpoch.AddDays(day + 1)));

            WorkItemSharedRebuildAggregateHistory restored = await WorkItemRuntimeHistoryStore.ReadAsync(cold, scope, read.Generation, owner, ct);
            JsonSerializer.SerializeToUtf8Bytes(restored, s_json).ShouldBe(
                JsonSerializer.SerializeToUtf8Bytes(new WorkItemSharedRebuildAggregateHistory(owner, history.Events), s_json));
            SharedProjectionCheckpoint checkpoint = (await cold.GetCheckpointAsync(scope, owner, ct)).ShouldNotBeNull();
            checkpoint.OwnerSequenceNumber.ShouldBe(lastSequence);
            checkpoint.Position.ShouldBe(history.Events[^1].GlobalPosition);
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
        await RebuildAsync(provider, handler, "tenant-byte-ceiling", histories,
            stageProof: (observed, WorksSdkRebuildBatchObserver.WriteDigests(logicalPlan.Operations)));

        observed.Batches.Count.ShouldBeGreaterThan(4);
        observed.Batches.ShouldAllBe(batch => batch.Operations <= configured.MaxOperations && batch.CanonicalBytes <= configured.MaxCanonicalManifestBytes);
        var cold = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var queue = await WorksRuntimeIndexReader.ReadAsync(cold, scope, ct);
        queue.IsAvailable.ShouldBeTrue();
        queue.IsStale.ShouldBeFalse();
        queue.Value.ShouldNotBeNull().Indexes.Queue.MemberWorkItemIds.ShouldBe(histories.Select(history => history.AggregateId));
        foreach (ReadModelBatchOperation operation in logicalPlan.Operations)
        {
            operation.Kind.ShouldBe(ReadModelBatchOperationKind.Write);
            var expected = JsonSerializer.Deserialize<WorksRuntimeDocument>(operation.CanonicalValue.Span, s_json).ShouldNotBeNull();
            var persisted = (await cold.ReadGenerationAsync<WorksRuntimeDocument>(scope, queue.Generation, operation.Key, ct))
                .Value.ShouldNotBeNull(operation.Key);
            persisted.Kind.ShouldBe(expected.Kind, operation.Key);
            JsonElement.DeepEquals(persisted.Value, expected.Value).ShouldBeTrue(operation.Key);
        }
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

    private static ProjectionRequest WithDateLifecycle(ProjectionRequest history, bool clear, int dateCount = 1)
    {
        string aggregate = history.AggregateId;
        var tenantId = new TenantId(Tenant);
        var workItemId = new WorkItemId(aggregate);
        var queued = new WorkItemQueued(aggregate, 2, tenantId, workItemId);
        var claimed = new WorkItemClaimed(aggregate, 3, tenantId, workItemId,
            new ExecutorBinding(new PartyId("rebuild-executor"), Channel.Mcp, AuthorityLevel.Coordinate));
        var suspended = new WorkItemSuspended(aggregate, 4, tenantId, workItemId,
            [.. Enumerable.Range(0, dateCount).Select(day => AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddDays(day + 1)))]);
        ProjectionEventDto[] pending = [.. history.Events, Envelope(queued, 2), Envelope(claimed, 3), Envelope(suspended, 4)];
        if (!clear)
            return history with { Events = pending };

        var cancelled = new WorkItemCancelled(aggregate, 5, tenantId, workItemId);
        return history with { Events = [.. pending, Envelope(cancelled, 5)] };

        static ProjectionEventDto Envelope(IEventPayload payload, long sequence)
            => new(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
                "json", sequence, DateTimeOffset.UnixEpoch, "", GlobalPosition: 9 + sequence);
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
        IReadOnlyList<ProjectionRequest> histories, bool expectRefusal = false,
        (WorksSdkRebuildBatchObserver Observer, string[] ExpectedWrites)? stageProof = null)
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
        int batchesBeforeStage = stageProof?.Observer.Batches.Count ?? 0;
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Stage)).ConfigureAwait(false)).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Prepared);
        if (stageProof is { } proof)
        {
            // Reconcile the successful writes from Stage with the finalized plan before Commit runs.
            string[][] stagedWrites = [.. proof.Observer.Batches.Skip(batchesBeforeStage)
                .Select(batch => batch.SuccessfulWrites).Where(writes => writes.Length > 0)];
            stagedWrites.Length.ShouldBeGreaterThan(1);
            stagedWrites.SelectMany(writes => writes).Order(StringComparer.Ordinal)
                .ShouldBe(proof.ExpectedWrites.Order(StringComparer.Ordinal));
        }
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Commit)).ConfigureAwait(false)).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
        (await Dispatch(Request(DomainSharedProjectionRebuildAction.Verify)).ConfigureAwait(false)).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
    }
}
