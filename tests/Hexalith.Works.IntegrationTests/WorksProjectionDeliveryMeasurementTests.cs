using System.Diagnostics;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Standalone real-store measurements for ordinary Works projection delivery.</summary>
public sealed class WorksProjectionDeliveryMeasurementTests
{
    /// <summary>Measures real state-store delivery cost across representative admitted history volumes and populations.</summary>
    [Theory(Explicit = true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(512, false)]
    public async Task OrdinaryDeliveryMeasuresRealStoreCostNearHistoryLimits(int ownerCount, bool largeHistories)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using DaprClient client = WorksProjectionMeasurementEndpoint.CreateClient();
        var batchOptions = Options.Create(new ReadModelBatchOptions
        {
            MaxOperations = WorksRebuildOperationBound.MaxOperations,
            MaxCanonicalManifestBytes = 8 * ReadModelBatchOptions.DefaultMaxCanonicalManifestBytes,
        });
        var store = new DaprReadModelStore(client, batchOptions);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        string tenant = "delivery-cost-" + Guid.NewGuid().ToString("N");
        var tenantId = new TenantId(tenant);
        var scope = handler.CreateScope(tenant);
        _ = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        var histories = new List<WorkItemSharedRebuildAggregateHistory>();
        for (int ordinal = 0; ordinal < ownerCount; ordinal++)
        {
            string owner = FormattableString.Invariant($"owner-{ordinal:D2}");
            var workItem = new WorkItemId(owner);
            var events = new List<ProjectionEventDto>
            {
                WorksSdkProjectionQueryTests.Dto(new WorkItemCreated(owner, 1, tenantId, workItem, new Obligation("Delivery measurement")), 1),
            };
            if (largeHistories && ordinal < 4)
            {
                for (int sequence = 2; sequence <= 140; sequence++)
                {
                    events.Add(WorksSdkProjectionQueryTests.Dto(new WorkItemRescheduled(owner, sequence, tenantId, workItem, new WorkItemSchedule(),
                        new string('x', 20 * 1024)), sequence));
                }
            }
            histories.Add(new WorkItemSharedRebuildAggregateHistory(owner, [.. events]));
        }

        long historyBytes = histories.Sum(history => (long)WorkItemRuntimeHistoryStore.Serialize(history).Length);
        int largestHistory = histories.Max(history => WorkItemRuntimeHistoryStore.Serialize(history).Length);
        historyBytes.ShouldBeLessThan(WorkItemRuntimeHistoryStore.MaxTenantBytes);
        largestHistory.ShouldBeLessThan(WorkItemRuntimeHistoryStore.MaxItemBytes);
        if (largeHistories)
        {
            historyBytes.ShouldBeGreaterThan(9 * WorkItemRuntimeHistoryStore.MaxTenantBytes / 10);
            largestHistory.ShouldBeGreaterThan(9 * WorkItemRuntimeHistoryStore.MaxItemBytes / 10);
        }

        var identity = new DomainSharedProjectionRebuildIdentity(tenant, "work", WorksReadModelKeys.RuntimeFamily,
            "delivery-measurement", "delivery-inventory");
        var plan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, new WorkItemSharedRebuildCandidateState(histories), ct);
        _ = await coordinator.BeginAsync(scope, identity.OperationId, identity.CatalogFingerprint,
            histories.ToDictionary(history => history.AggregateId, history => (long)history.Events.Length), ct);
        _ = await coordinator.StageAsync(scope, identity.OperationId, plan.Operations, ct,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
        await coordinator.CommitAsync(scope, identity.OperationId, ct);
        const string changedOwner = "owner-00";
        int retainedEventCount = histories[0].Events.Length;
        await coordinator.SeedCapturedOwnerSequenceAsync(scope, identity.OperationId, changedOwner, retainedEventCount, ct);
        var domain = new DomainProjectionIdentityOptions { AppId = "works-measurement", ServiceVersion = "v1" };
        var dispatchOptions = new ProjectionDispatchOptions();
        string fingerprint = ProjectionRouteCatalogFingerprint.Compute(domain.AppId, domain.ServiceVersion,
            [new ProjectionDispatchRoute("work", WorksReadModelKeys.RuntimeFamily)]);
        string unaffectedKey = WorksReadModelKeys.RuntimeParkingKey("owner-31");
        long generation = (await coordinator.GetStatusAsync(scope, ct)).ActiveGeneration;
        ReadModelEntry<WorksRuntimeDocument> unrelated = await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, generation, unaffectedKey, ct);
        var samples = new List<double>();
        const int deliveries = 5;
        for (long sequence = retainedEventCount + 1; sequence <= retainedEventCount + deliveries; sequence++)
        {
            var update = new WorkItemRescheduled(changedOwner, sequence, tenantId, new WorkItemId(changedOwner),
                new WorkItemSchedule(), "Measured ordinary event");
            var request = new ProjectionDispatchRequest(new ProjectionRequest(tenant, "work", changedOwner, [WorksSdkProjectionQueryTests.Dto(update, sequence)]),
                [WorksReadModelKeys.RuntimeFamily], FormattableString.Invariant($"measured-delivery-{sequence}"), fingerprint);
            using ServiceProvider provider = WorksSdkProjectionQueryTests.NamedProjectionProvider(store, store, coordinator, handler);
            long started = Stopwatch.GetTimestamp();
            ProjectionDispatchResponse response = await DomainProjectionDispatcher.DispatchAsync(provider, request, dispatchOptions, domain, ct);
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            ProjectionDispatchOutcome outcome = response.Outcomes.ShouldHaveSingleItem();
            outcome.Status.ShouldBe(ProjectionDispatchStatus.Completed, outcome.ReasonCode);
            coordinator = new SharedProjectionEpochCoordinator(store, store);
            (await coordinator.GetCheckpointAsync(scope, changedOwner, ct)).ShouldNotBeNull().OwnerSequenceNumber.ShouldBe(sequence);
        }
        ReadModelEntry<WorksRuntimeDocument> retained = await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, generation, unaffectedKey, ct);
        retained.Value!.Value.GetRawText().ShouldBe(unrelated.Value!.Value.GetRawText());
        retained.ETag.ShouldBe(unrelated.ETag);
        var status = await coordinator.GetStatusAsync(scope, ct);
        status.ActiveGeneration.ShouldBe(generation);
        status.PendingDeliveryCount.ShouldBe(0);
        (await WorkItemRuntimeHistoryStore.ReadAsync(coordinator, scope, generation, changedOwner, ct))
            .Events.Select(item => item.SequenceNumber).ShouldBe(Enumerable.Range(1, retainedEventCount + deliveries).Select(sequence => (long)sequence));
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { measurement = "ordinary-delivery", largeHistories, ownerCount,
            historyBytes, largestHistory, samplesMilliseconds = samples, deliveriesPerSecond = deliveries * 1_000 / samples.Sum(),
            samplesExceedingFiveSeconds = samples.Count(sample => sample > 5_000),
            runtimeImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_RUNTIME"),
            redisImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_REDIS") }));
    }
}
