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

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Measures authorized queue reads while only the retained terminal population grows.</summary>
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
                elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4.9), "a refusal qualifies only as an observed deadline");
                elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(7), "the Works handler must return close to the five-second budget");
                var diagnostic = logger.Entries.ShouldHaveSingleItem();
                diagnostic.EventId.Id.ShouldBe(4510);
                diagnostic.Properties["FailureCategory"].ShouldBe("timeout");
                result.ErrorMessage.ShouldBe("projection-unavailable");
                result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
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

    private static string Owner(int ordinal) => FormattableString.Invariant($"owner-{ordinal:D4}");

    private static async Task<SharedProjectionScope> SeedAsync(SharedProjectionEpochCoordinator coordinator, string tenant,
        int activeOwners, int terminalOwners, CancellationToken ct)
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
            ProjectionEventDto final = ordinal < activeOwners
                ? Event(new WorkItemQueued(owner, 2, tenantId, item), 2)
                : Event(new WorkItemCancelled(owner, 2, tenantId, item), 2);
            histories.Add(new WorkItemSharedRebuildAggregateHistory(owner, [created, final]));
        }
        var identity = new DomainSharedProjectionRebuildIdentity(tenant, "work", WorksReadModelKeys.RuntimeFamily, "queue-cost", "queue-inventory");
        var plan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, new WorkItemSharedRebuildCandidateState(histories), ct);
        _ = await coordinator.BeginAsync(scope, identity.OperationId, identity.CatalogFingerprint,
            histories.ToDictionary(history => history.AggregateId, _ => 2L), ct).ConfigureAwait(false);
        _ = await coordinator.StageAsync(scope, identity.OperationId, plan.Operations, ct,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(false);
        await coordinator.CommitAsync(scope, identity.OperationId, ct).ConfigureAwait(false);
        return scope;

        static ProjectionEventDto Event(IEventPayload payload, long sequence)
            => new(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
                "json", sequence, DateTimeOffset.UnixEpoch, "queue-fixture", GlobalPosition: sequence);
    }
}
