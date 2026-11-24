using System.Diagnostics;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Recovery;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Measures complete Works discovery through the real Dapr state store.</summary>
public sealed class WorksRecoveryCatalogMeasurementTests
{
    /// <summary>Exercises both the default population and a valid multi-page Works process profile.</summary>
    [Fact(Explicit = true)]
    public async Task CatalogSweepMeasuresRealStoreCost()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using DaprClient client = WorksProjectionMeasurementEndpoint.CreateClient();
        var store = new DaprReadModelStore(client);
        string scopeId = "catalog-measurement-" + Guid.NewGuid().ToString("N");
        await MeasurePopulationAsync(store, scopeId, multiPage: false, ct);
        await MeasurePopulationAsync(store, scopeId, multiPage: true, ct);
    }

    private static async Task MeasurePopulationAsync(DaprReadModelStore store, string scopeId, bool multiPage, CancellationToken ct)
    {
        var options = new EventStoreProcessOptions { MaxEffectsPerEnvelope = 5, SourceServiceQuantum = TimeSpan.FromSeconds(15) };
        if (multiPage)
        {
            options.SupportedCandidateCount = 45;
            options.DiscoveryPageSize = 16;
            options.ReconciliationInterval = TimeSpan.FromSeconds(1);
            options.ReadinessMaxAge = TimeSpan.FromMinutes(30);
        }
        options.Validate().ShouldBeEmpty();

        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var expected = new List<string>();
        for (int tenantOrdinal = 0; tenantOrdinal < 3; tenantOrdinal++)
        {
            string tenant = FormattableString.Invariant($"{scopeId}-{tenantOrdinal}");
            int ownerCount = options.SupportedCandidateCount / 3;
            expected.AddRange(await SeedAsync(coordinator, tenant, ownerCount, ct).ConfigureAwait(false));
        }
        expected.Sort(StringComparer.Ordinal);

        int expectedPages = (expected.Count + options.DiscoveryPageSize - 1) / options.DiscoveryPageSize;
        var catalog = new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(store, store));
        var samples = new List<double>();
        for (int sweep = 0; sweep < 5; sweep++)
        {
            var observed = new List<string>();
            string? after = null;
            for (int pageOrdinal = 0; pageOrdinal < expectedPages; pageOrdinal++)
            {
                long started = Stopwatch.GetTimestamp();
                ProcessSourcePage page = await catalog.ListAsync(after, options.DiscoveryPageSize, ct).ConfigureAwait(false);
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                page.IsIncomplete.ShouldBeFalse();
                page.Sources.Count.ShouldBe(Math.Min(options.DiscoveryPageSize, expected.Count - observed.Count));
                observed.AddRange(page.Sources.Select(source => source.CatalogKey));
                after = page.NextKey;
                if (pageOrdinal + 1 < expectedPages)
                    after.ShouldBe(page.Sources.Last().CatalogKey);
            }
            after.ShouldBeNull();
            observed.ShouldBe(expected);
        }
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { measurement = "recovery-catalog", multiPage,
            candidates = expected.Count, options.DiscoveryPageSize, pagesPerSweep = expectedPages,
            operationBudgetMilliseconds = options.OperationTimeout.TotalMilliseconds, samplesMilliseconds = samples,
            pagesOverBudget = samples.Count(sampleTime => sampleTime > options.OperationTimeout.TotalMilliseconds),
            runtimeImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_RUNTIME"),
            redisImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_REDIS") }));
    }

    private static async Task<IEnumerable<string>> SeedAsync(SharedProjectionEpochCoordinator coordinator,
        string tenant, int ownerCount, CancellationToken ct)
    {
        const string writer = "works-runtime-v3-writer";
        var scope = new SharedProjectionScope(WorksReadModelKeys.StateStoreName, tenant, "work", WorksReadModelKeys.RuntimeFamily, [writer]);
        _ = await coordinator.RegisterWriterAsync(scope, writer, ct).ConfigureAwait(false);
        var histories = new List<WorkItemSharedRebuildAggregateHistory>();
        var tenantId = new TenantId(tenant);
        for (int ordinal = 0; ordinal < ownerCount; ordinal++)
        {
            string owner = FormattableString.Invariant($"owner-{ordinal:D2}");
            var workItem = new WorkItemId(owner);
            ProjectionEventDto first = Event(new WorkItemCreated(owner, 1, tenantId, workItem, new Obligation("Retained catalog member")), 1);
            if (ordinal == 0)
            {
                var binding = new ExecutorBinding(new PartyId("catalog-owner"), Channel.Cli, AuthorityLevel.Contribute);
                ProjectionEventDto assigned = Event(new WorkItemAssigned(owner, 2, tenantId, workItem, binding), 2);
                ProjectionEventDto claimed = Event(new WorkItemClaimed(owner, 3, tenantId, workItem, binding), 3);
                ProjectionEventDto waiting = Event(new WorkItemSuspended(owner, 4, tenantId, workItem,
                    [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddDays(1))]), 4);
                histories.Add(new WorkItemSharedRebuildAggregateHistory(owner, [first, assigned, claimed, waiting]));
            }
            else
            {
                ProjectionEventDto terminal = Event(new WorkItemCancelled(owner, 2, tenantId, workItem), 2);
                histories.Add(new WorkItemSharedRebuildAggregateHistory(owner, ordinal % 2 == 0 ? [first, terminal] : [first]));
            }

        }

        var identity = new DomainSharedProjectionRebuildIdentity(tenant, "work", WorksReadModelKeys.RuntimeFamily,
            FormattableString.Invariant($"catalog-cost-{ownerCount}"), "catalog-inventory");
        var plan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, new WorkItemSharedRebuildCandidateState(histories), ct);
        _ = await coordinator.BeginAsync(scope, identity.OperationId, identity.CatalogFingerprint,
            histories.ToDictionary(history => history.AggregateId, history => (long)history.Events.Length), ct).ConfigureAwait(false);
        _ = await coordinator.StageAsync(scope, identity.OperationId, plan.Operations, ct,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(false);
        await coordinator.CommitAsync(scope, identity.OperationId, ct).ConfigureAwait(false);
        return histories.Select(history => new ProcessSource(tenant, "work", history.AggregateId).CatalogKey);

        static ProjectionEventDto Event(IEventPayload payload, long sequence)
            => new(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
                "json", sequence, DateTimeOffset.UnixEpoch, "seed-catalog", GlobalPosition: sequence);
    }
}
