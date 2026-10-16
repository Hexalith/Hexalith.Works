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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises durable owner identity handoff through the released SDK partitioned lifecycle.</summary>
public sealed class WorksSdkPartitionedRebuildTests
{
    private const string Tenant = "partition-tenant";
    private const string Aggregate = "partition-owner";
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Two_rebuilds_seed_captured_identities_and_accept_the_next_full_prefix_after_restart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        _ = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        var localIdentity = new DomainProjectionIdentityOptions { AppId = "works-test", ServiceVersion = "v1" };
        string catalog = AdminOperationalIndexMetadata.Create(new DiscoveryResult([], []), ["work"], [], [handler],
            localIdentity.AppId, localIdentity.ServiceVersion, new ProjectionDispatchOptions(), ["work"]).CatalogFingerprint!;
        ProjectionEventDto[] captured = [Dto(new WorkItemCreated(Aggregate, 1, new TenantId(Tenant), new WorkItemId(Aggregate), new Obligation("captured")), 1)];
        ServiceProvider Provider(SharedProjectionEpochCoordinator current)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IReadModelStore>(store);
            services.AddSingleton<IReadModelBatchStore>(store);
            services.AddSingleton<IReadModelBatchStagingStore>(store);
            services.AddSingleton(current);
            services.AddSingleton<IAsyncDomainProjectionHandler>(handler);
            return services.BuildServiceProvider();
        }
        ProjectionDispatchRequest Delivery(ProjectionEventDto[] events, string dispatch) => new(
            new ProjectionRequest(Tenant, "work", Aggregate, events), [handler.ProjectionType], dispatch, catalog);
        using (ServiceProvider provider = Provider(coordinator))
        {
            (await DomainProjectionDispatcher.DispatchAsync(provider, Delivery(captured, "initial"), new ProjectionDispatchOptions(), localIdentity, ct))
                .Outcomes.ShouldHaveSingleItem().Status.ShouldBe(ProjectionDispatchStatus.Completed);
        }
        for (int epoch = 1; epoch <= 2; epoch++)
        {
            coordinator = new SharedProjectionEpochCoordinator(store, store);
            using ServiceProvider provider = Provider(coordinator);
            var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "partition-rebuild-" + epoch, catalog);
            DomainSharedProjectionRebuildRequest Request(DomainSharedProjectionRebuildAction action) => new(DomainSharedProjectionRebuildProtocol.Version, action, identity);
            Task<DomainSharedProjectionRebuildResponse> Dispatch(DomainSharedProjectionRebuildRequest request) =>
                DomainSharedProjectionRebuildDispatcher.DispatchAsync(provider, request, new ProjectionDispatchOptions(), localIdentity, ct);
            string inventory = DomainSharedProjectionRebuildFingerprint.AppendInventory(DomainSharedProjectionRebuildFingerprint.EmptyInventory, 0,
                DomainSharedProjectionRebuildFingerprint.ComputeHistory(Aggregate, false, captured));
            (await Dispatch(Request(DomainSharedProjectionRebuildAction.Begin) with
            {
                CaptureInventoryFingerprint = inventory,
                SourceHighWatermarks = new Dictionary<string, long> { [Aggregate] = captured[^1].GlobalPosition },
            })).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Accumulating);
            (await Dispatch(Request(DomainSharedProjectionRebuildAction.Accumulate) with
            {
                AggregateOrdinal = 0, AggregateId = Aggregate, Events = captured,
            })).AcceptedAggregateCount.ShouldBe(1);
            (await Dispatch(Request(DomainSharedProjectionRebuildAction.Finalize) with
            {
                ExpectedAggregateCount = 1, ExpectedInventoryFingerprint = inventory,
            })).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Finalized);
            (await Dispatch(Request(DomainSharedProjectionRebuildAction.Stage))).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Prepared);
            (await Dispatch(Request(DomainSharedProjectionRebuildAction.Commit))).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
            (await Dispatch(Request(DomainSharedProjectionRebuildAction.Verify))).Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
            var cold = new SharedProjectionEpochCoordinator(store, store);
            SharedProjectionCheckpoint checkpoint = (await cold.GetCheckpointAsync(scope, Aggregate, ct)).ShouldNotBeNull();
            checkpoint.OwnerSequenceNumber.ShouldBe(1);
            checkpoint.CapturedEventDigests.ShouldNotBeNull().Length.ShouldBe(32);
            (await cold.GetStatusAsync(scope, ct)).ActiveGeneration.ShouldBe(epoch);
        }
        coordinator = new SharedProjectionEpochCoordinator(store, store);
        using (ServiceProvider provider = Provider(coordinator))
        {
            ProjectionEventDto[] next = [.. captured, Dto(new WorkItemQueued(Aggregate, 2, new TenantId(Tenant), new WorkItemId(Aggregate)), 2)];
            (await DomainProjectionDispatcher.DispatchAsync(provider, Delivery(next, "after-two-commits"), new ProjectionDispatchOptions(), localIdentity, ct))
                .Outcomes.ShouldHaveSingleItem().Status.ShouldBe(ProjectionDispatchStatus.Completed);
            (await DomainProjectionDispatcher.DispatchAsync(provider, Delivery(next, "same-prefix-redelivery"), new ProjectionDispatchOptions(), localIdentity, ct))
                .Outcomes.ShouldHaveSingleItem().Status.ShouldBeOneOf(ProjectionDispatchStatus.Completed, ProjectionDispatchStatus.AlreadyCompleted);
        }
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        (await restarted.GetCheckpointAsync(scope, Aggregate, ct))!.OwnerSequenceNumber.ShouldBe(2);
        (await restarted.GetStatusAsync(scope, ct)).PendingDeliveryCount.ShouldBe(0);
        var read = await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey(Aggregate), ct);
        read.IsAvailable.ShouldBeTrue(); read.IsDegraded.ShouldBeFalse(); read.IsStale.ShouldBeFalse();
        read.Value!.As<WorkItemRollUp>().Status.ShouldBe(WorkItemStatus.Queued);
        (await WorkItemRuntimeHistoryStore.ReadAsync(restarted, scope, read.Generation, Aggregate, ct)).Events.Length.ShouldBe(2);
    }

    [Fact]
    public async Task Large_owner_is_chunked_and_reassembles_the_identical_complete_plan()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "large-partition", "catalog");
        ProjectionEventDto[] events = [Dto(new WorkItemCreated(Aggregate, 1, new TenantId(Tenant), new WorkItemId(Aggregate), new Obligation("large")), 1),
            .. Enumerable.Range(2, 10).Select(sequence => Dto(new WorkItemRescheduled(Aggregate, sequence, new TenantId(Tenant), new WorkItemId(Aggregate),
                new WorkItemSchedule(), new string('x', 20 * 1024)), sequence))];
        var request = new ProjectionRequest(Tenant, "work", Aggregate, events);
        IReadOnlyList<ReadOnlyMemory<byte>> chunks = await handler.PreparePartitionChunksAsync(identity, request, ct);
        chunks.Count.ShouldBeGreaterThan(4);
        chunks.ShouldAllBe(chunk => chunk.Length > 0 && chunk.Length <= WorkItemRuntimeHistoryStore.PageBytes);
        DomainSharedProjectionRebuildPartition[] partitions = [.. chunks.Select((chunk, index) => new DomainSharedProjectionRebuildPartition(0, Aggregate, chunk, index))];
        DomainProjectionRebuildPlan plan = await handler.FinalizePartitionsAsync(identity, partitions, ct);
        DomainProjectionRebuildPlan expected = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity,
            new WorkItemSharedRebuildCandidateState([new WorkItemSharedRebuildAggregateHistory(Aggregate, events)]), ct);
        plan.Operations.Select(operation => (operation.Key, Convert.ToBase64String(operation.CanonicalValue.Span)))
            .ShouldBe(expected.Operations.Select(operation => (operation.Key, Convert.ToBase64String(operation.CanonicalValue.Span))));
        _ = await Should.ThrowAsync<InvalidOperationException>(() => handler.FinalizePartitionsAsync(identity,
            [partitions[0], .. partitions.Skip(2)], ct));
        _ = await Should.ThrowAsync<InvalidOperationException>(() => handler.FinalizePartitionsAsync(identity,
            [partitions[0] with { AggregateId = "foreign-owner" }, .. partitions.Skip(1)], ct));
        _ = await Should.ThrowAsync<InvalidOperationException>(() => handler.PreparePartitionChunksAsync(identity, request with { TenantId = "foreign-tenant" }, ct));
    }

    [Fact]
    public async Task Owner_and_tenant_partition_capacity_refuse_before_a_replacement_plan_is_built()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", handler.ProjectionType, "partition-capacity", "catalog");
        ReadOnlyMemory<byte> page = new byte[WorkItemRuntimeHistoryStore.PageBytes];
        DomainSharedProjectionRebuildPartition[] oversizedOwner = [.. Enumerable.Range(0, 65)
            .Select(index => new DomainSharedProjectionRebuildPartition(0, Aggregate, page, index))];
        _ = await Should.ThrowAsync<SharedProjectionCapacityExceededException>(() => handler.FinalizePartitionsAsync(identity, oversizedOwner, ct));
        var partitions = new List<DomainSharedProjectionRebuildPartition>();
        for (int owner = 0; owner < 7; owner++)
        {
            string aggregate = "capacity-owner-" + owner;
            ProjectionEventDto[] events = [Dto(new WorkItemCreated(aggregate, 1, new TenantId(Tenant), new WorkItemId(aggregate), new Obligation("bounded")), 1),
                Dto(new WorkItemRescheduled(aggregate, 2, new TenantId(Tenant), new WorkItemId(aggregate), new WorkItemSchedule(), new string('x', 2 * 1024 * 1024)), 2)];
            IReadOnlyList<ReadOnlyMemory<byte>> chunks = await handler.PreparePartitionChunksAsync(identity,
                new ProjectionRequest(Tenant, "work", aggregate, events), ct);
            partitions.AddRange(chunks.Select((chunk, index) => new DomainSharedProjectionRebuildPartition(owner, aggregate, chunk, index)));
        }
        partitions.Sum(partition => (long)partition.State.Length).ShouldBeGreaterThan(WorkItemRuntimeHistoryStore.MaxTenantBytes);
        _ = await Should.ThrowAsync<SharedProjectionCapacityExceededException>(() => handler.FinalizePartitionsAsync(identity, partitions, ct));
    }

    private static ProjectionEventDto Dto(IEventPayload payload, long sequence) => new(payload.GetType().Name,
        JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()), "json", sequence, DateTimeOffset.UnixEpoch, "partition-proof", GlobalPosition: sequence * 10);
}
