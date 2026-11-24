using System.Diagnostics;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;
using Hexalith.Works.Recovery;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Measures query and recovery reads over synthetic sharded Works projections.</summary>
public sealed class WorksQueueMeasurementTests
{
    private const string Workload = "scheduler";
    private const int ActiveOwners = 8;

    /// <summary>Compares successful or deadline-bounded reads without increasing the authorized result size.</summary>
    [Theory(Explicit = true)]
    [InlineData(0)]
    [InlineData(1_024)]
    [InlineData(4_096)]
    public async Task WhatsNextMeasuresRetainedTerminalMembershipCost(int terminalOwners)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using DaprClient client = WorksProjectionMeasurementEndpoint.CreateClient();
        var store = new DaprReadModelStore(client, Options.Create(new ReadModelBatchOptions
        {
            MaxOperations = WorksRebuildOperationBound.MaxOperations,
            MaxCanonicalManifestBytes = 8 * ReadModelBatchOptions.DefaultMaxCanonicalManifestBytes,
        }));
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        string tenant = "queue-measurement-" + Guid.NewGuid().ToString("N");
        var scope = await SeedAsync(coordinator, tenant, ActiveOwners, terminalOwners, ct);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = [Workload] }));
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        var index = await WorksRuntimeIndexReader.ReadAsync(restarted, scope, ct);
        index.IsAvailable.ShouldBeTrue();
        WorksRuntimeIndexes indexes = index.Value.ShouldNotBeNull().Indexes;
        indexes.Queue.MemberWorkItemIds.ShouldBe(Enumerable.Range(0, ActiveOwners + terminalOwners).Select(Owner));
        indexes.Queue.Items.Keys.Order(StringComparer.Ordinal).ShouldBe(Enumerable.Range(0, ActiveOwners).Select(Owner));
        if (terminalOwners > 0)
        {
            string last = Owner(ActiveOwners + terminalOwners - 1);
            var rollup = await restarted.ReadGenerationAsync<WorksRuntimeDocument>(scope, index.Generation,
                WorksReadModelKeys.RuntimeRollUpKey(last), ct);
            rollup.Value.ShouldNotBeNull().As<WorkItemRollUp>().Status.ShouldBe(WorkItemStatus.Cancelled);
        }
        var query = new QueryEnvelope(tenant, "work", "owner-0000", WhatsNextQueryHandler.WhatsNextQueryType,
            [], "query-measurement", Workload, null, false, null, null, Workload, false, null, null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string[] expected = [.. Enumerable.Range(0, ActiveOwners).Select(Owner)];
        var samples = new List<double>();
        int successful = 0;
        for (int iteration = 0; iteration < 3; iteration++)
        {
            var logger = new Story48RecordingLogger<WhatsNextQueryHandler>();
            var handler = new WhatsNextQueryHandler(policy, restarted, logger);
            long start = Stopwatch.GetTimestamp();
            QueryResult result = await handler.ExecuteAsync(query, ct);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            samples.Add(elapsed.TotalMilliseconds);
            if (result.Success)
            {
                result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
                result.GetPayload().Deserialize<WhatsNextItem[]>(options).ShouldNotBeNull()
                    .Select(item => item.WorkItemId.Value).ShouldBe(expected);
                successful++;
            }
            else
            {
                terminalOwners.ShouldBeGreaterThan(0);
                RequireTimeout(result, elapsed, logger);
            }
        }
        var current = await coordinator.GetStatusAsync(scope, ct);
        current.PendingDeliveryCount.ShouldBe(0);
        current.IsDegraded.ShouldBeFalse();
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { measurement = "terminal-queue",
            terminalOwners, activeOwners = ActiveOwners, successful, samplesMilliseconds = samples,
            runtimeImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_RUNTIME"),
            redisImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_REDIS") }));
    }

    /// <summary>Measures the sharded index path for a selected detail and a real recovery catalog page.</summary>
    [Fact(Explicit = true)]
    public async Task ShardedProjectionReadsMeasureDetailAndRecoveryDeadlines()
    {
        const int owners = 2_048;
        CancellationToken ct = TestContext.Current.CancellationToken;
        using DaprClient client = WorksProjectionMeasurementEndpoint.CreateClient();
        var store = new DaprReadModelStore(client, Options.Create(new ReadModelBatchOptions
        {
            MaxOperations = WorksRebuildOperationBound.MaxOperations,
            MaxCanonicalManifestBytes = 8 * ReadModelBatchOptions.DefaultMaxCanonicalManifestBytes,
        }));
        var writer = new SharedProjectionEpochCoordinator(store, store);
        string tenant = "sharded-read-cost-" + Guid.NewGuid().ToString("N");
        var scope = await SeedAsync(writer, tenant, 1, owners - 1, ct, includeDateAwaits: true);
        var cold = new SharedProjectionEpochCoordinator(store, store);
        var served = await WorksRuntimeIndexReader.ReadAsync(cold, scope, ct);
        served.IsAvailable.ShouldBeTrue();
        var indexes = served.Value.ShouldNotBeNull().Indexes;
        indexes.IsSharded.ShouldBeTrue();
        indexes.Queue.MemberWorkItemIds.Count.ShouldBe(owners);
        indexes.PendingDate.Entries.Count.ShouldBeGreaterThan(1);
        int shardCount = served.Value.Documents.Keys.Count(key => key.Contains(":shards:", StringComparison.Ordinal));
        shardCount.ShouldBeGreaterThan(64);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = [Workload] }));
        var query = new QueryEnvelope(tenant, "work", Owner(0), GetWorkItemQueryHandler.GetWorkItemQueryType,
            [], "sharded-read-measurement", Workload, null, false, null, null, Workload, false, null, null);
        var catalog = new WorksProcessSourceCatalog(cold);
        var detail = new List<double>();
        var discovery = new List<double>();
        var boundedDiscovery = new List<double>();
        int cancelledDiscovery = 0;
        int timedOutDetails = 0;
        var process = new EventStoreProcessOptions();
        for (int iteration = 0; iteration < 5; iteration++)
        {
            var logger = new Story48RecordingLogger<GetWorkItemQueryHandler>();
            var handler = new GetWorkItemQueryHandler(policy, cold, logger);
            long started = Stopwatch.GetTimestamp();
            QueryResult result = await handler.ExecuteAsync(query, ct);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            detail.Add(elapsed.TotalMilliseconds);
            if (result.Success)
            {
                result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
                var payload = result.GetPayload().Deserialize<WorkItemView>(JsonSerializerOptions.Web).ShouldNotBeNull();
                payload.Found.ShouldBeTrue();
                payload.WorkItemId.Value.ShouldBe(Owner(0));
                payload.TenantId.Value.ShouldBe(tenant);
                payload.Status.ShouldBe(WorkItemStatus.Queued);
            }
            else
            {
                RequireTimeout(result, elapsed, logger);
                timedOutDetails++;
            }

            started = Stopwatch.GetTimestamp();
            var page = await catalog.ListAsync(null, process.DiscoveryPageSize, ct);
            discovery.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            page.IsIncomplete.ShouldBeFalse();
            page.Sources.ShouldAllBe(source => source.Tenant == tenant && source.Domain == "work");
            page.Sources.Select(source => source.Aggregate).ShouldBe(Enumerable.Range(0, process.DiscoveryPageSize).Select(Owner));
            page.NextKey.ShouldBe(page.Sources[^1].CatalogKey);

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(process.OperationTimeout);
            started = Stopwatch.GetTimestamp();
            try
            {
                var bounded = await catalog.ListAsync(null, process.DiscoveryPageSize, budget.Token);
                bounded.IsIncomplete.ShouldBeFalse();
                bounded.Sources.ShouldBe(page.Sources);
                bounded.NextKey.ShouldBe(page.NextKey);
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                cancelledDiscovery++;
            }
            boundedDiscovery.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { measurement = "sharded-reads", owners, shardCount,
            samplesMilliseconds = detail, discoveryMilliseconds = discovery, boundedDiscoveryMilliseconds = boundedDiscovery,
            cancelledDiscovery, timedOutDetails,
            processBudgetMilliseconds = process.OperationTimeout.TotalMilliseconds,
            runtimeImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_RUNTIME"),
            redisImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_REDIS") }));
    }

    private static void RequireTimeout<T>(QueryResult result, TimeSpan elapsed, Story48RecordingLogger<T> logger)
    {
        elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4.9), "a refusal qualifies only as an observed deadline");
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(7), "the Works handler must return close to the five-second budget");
        var diagnostic = logger.Entries.ShouldHaveSingleItem();
        diagnostic.EventId.Id.ShouldBe(4510);
        diagnostic.Properties["FailureCategory"].ShouldBe("timeout");
        result.ErrorMessage.ShouldBe("projection-unavailable");
        result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
    }

    private static string Owner(int ordinal) => FormattableString.Invariant($"owner-{ordinal:D4}");

    private static async Task<SharedProjectionScope> SeedAsync(SharedProjectionEpochCoordinator coordinator, string tenant,
        int activeOwners, int terminalOwners, CancellationToken ct, bool includeDateAwaits = false)
    {
        const string writer = "works-runtime-v3-writer";
        var scope = new SharedProjectionScope(WorksReadModelKeys.StateStoreName, tenant, "work", WorksReadModelKeys.RuntimeFamily, [writer]);
        _ = await coordinator.RegisterWriterAsync(scope, writer, ct).ConfigureAwait(false);
        var tenantId = new TenantId(tenant);
        var histories = new List<WorkItemSharedRebuildAggregateHistory>();
        for (int ordinal = 0; ordinal < activeOwners + terminalOwners; ordinal++)
        {
            string owner = Owner(ordinal);
            var item = new WorkItemId(owner);
            ProjectionEventDto created = Event(new WorkItemCreated(owner, 1, tenantId, item, new Obligation("Queue measurement")), 1);
            if (includeDateAwaits && ordinal % 64 == 1)
            {
                var binding = new ExecutorBinding(new PartyId("sharded-read-owner"), Channel.Cli, AuthorityLevel.Contribute);
                histories.Add(new WorkItemSharedRebuildAggregateHistory(owner,
                [
                    created,
                    Event(new WorkItemAssigned(owner, 2, tenantId, item, binding), 2),
                    Event(new WorkItemClaimed(owner, 3, tenantId, item, binding), 3),
                    Event(new WorkItemSuspended(owner, 4, tenantId, item,
                        [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddDays(1))]), 4),
                ]));
                continue;
            }
            ProjectionEventDto final = ordinal < activeOwners
                ? Event(new WorkItemQueued(owner, 2, tenantId, item), 2)
                : Event(new WorkItemCancelled(owner, 2, tenantId, item), 2);
            histories.Add(new WorkItemSharedRebuildAggregateHistory(owner, [created, final]));
        }
        var identity = new DomainSharedProjectionRebuildIdentity(tenant, "work", WorksReadModelKeys.RuntimeFamily, "queue-cost", "queue-inventory");
        var plan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, new WorkItemSharedRebuildCandidateState(histories), ct);
        _ = await coordinator.BeginAsync(scope, identity.OperationId, identity.CatalogFingerprint,
            histories.ToDictionary(history => history.AggregateId, history => history.Events[^1].GlobalPosition), ct).ConfigureAwait(false);
        _ = await coordinator.StageAsync(scope, identity.OperationId, plan.Operations, ct,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(false);
        await coordinator.CommitAsync(scope, identity.OperationId, ct).ConfigureAwait(false);
        return scope;

        static ProjectionEventDto Event(IEventPayload payload, long sequence)
            => new(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
                "json", sequence, DateTimeOffset.UnixEpoch, "queue-fixture", GlobalPosition: sequence);
    }
}
