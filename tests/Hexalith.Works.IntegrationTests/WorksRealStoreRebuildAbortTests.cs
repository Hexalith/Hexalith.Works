using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Proves the shared-rebuild abort branch under concurrent delivery on a real Dapr state store.</summary>
public sealed class WorksRealStoreRebuildAbortTests
{
    private const string First = "owner-a";
    private const string Second = "owner-b";
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset s_due = new(2027, 1, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// An abort after staging, run with the dispatcher's coordinator sequence (deferred open, then completion), keeps the
    /// committed generation served. Deliveries journaled during the capture and while the abort runs converge on it, a
    /// later rebuild and live delivery succeed, and every observed state matches the in-memory reference store exactly.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task AbortedRebuildUnderConcurrentDeliveryKeepsTheServedGenerationOnTheRealStore()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using DaprClient client = WorksProjectionMeasurementEndpoint.CreateClient();
        var real = new DaprReadModelStore(client, Options.Create(new ReadModelBatchOptions
        {
            MaxOperations = WorksRebuildOperationBound.MaxOperations,
            MaxCanonicalManifestBytes = 8 * ReadModelBatchOptions.DefaultMaxCanonicalManifestBytes,
        }));
        var reference = new InMemoryReadModelStore();
        string tenant = "abort-proof-" + Guid.NewGuid().ToString("N");

        AbortRun observed = await RunAsync(real, real, tenant, ct);
        AbortRun expected = await RunAsync(reference, reference, tenant, ct);

        observed.ServedGeneration.ShouldBe(1);
        observed.ServedGeneration.ShouldBe(expected.ServedGeneration);
        observed.RebuiltGeneration.ShouldBe(expected.RebuiltGeneration);
        observed.States.Count.ShouldBe(4);
        observed.States.ShouldBe(expected.States, "every observed real-store state must equal the in-memory reference");
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { measurement = "aborted-rebuild",
            servedGeneration = observed.ServedGeneration, rebuiltGeneration = observed.RebuiltGeneration, realDigest = Digest(observed.States), referenceDigest = Digest(expected.States),
            samplesMilliseconds = new[] { observed.AbortMilliseconds },
            runtimeImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_RUNTIME"),
            redisImage = Environment.GetEnvironmentVariable("WORKS_PROJECTION_MEASUREMENT_REDIS") }));
    }

    private static async Task<AbortRun> RunAsync(IReadModelStore store, IReadModelBatchStore batches, string tenant, CancellationToken ct)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        var coordinator = new SharedProjectionEpochCoordinator(store, batches);
        SharedProjectionScope scope = handler.CreateScope(tenant);
        var tenantId = new TenantId(tenant);
        var binding = new ExecutorBinding(new PartyId("abort-proof-executor"), Channel.Cli, AuthorityLevel.Contribute);
        var histories = new SortedDictionary<string, List<ProjectionEventDto>>(StringComparer.Ordinal)
        {
            [First] = [Dto(new WorkItemCreated(First, 1, tenantId, new WorkItemId(First), new Obligation("Abort proof A")), 1)],
            [Second] = [Dto(new WorkItemCreated(Second, 1, tenantId, new WorkItemId(Second), new Obligation("Abort proof B")), 1)],
        };
        var states = new List<string>();
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct).ConfigureAwait(false);
        foreach ((string owner, List<ProjectionEventDto> events) in histories)
            (await coordinator.JournalAsync(scope, lease, Delivery(tenant, owner, events[0]), ct).ConfigureAwait(false)).ShouldBe(SharedProjectionJournalResult.Journaled);
        _ = await CatchUpAsync(coordinator, handler, scope, ct).ConfigureAwait(false);

        // One committed authoritative rebuild moves the served generation above the live-only generation zero.
        await RebuildAsync(coordinator, scope, tenant, "abort-proof-commit", histories, commit: true, ct).ConfigureAwait(false);
        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct).ConfigureAwait(false);
        Snapshot served = await SnapshotAsync(coordinator, scope, stale: false, ct).ConfigureAwait(false);
        states.Add(served.Canonical);

        // A second rebuild captures and stages; its Begin fences the writer lease.
        await RebuildAsync(coordinator, scope, tenant, "abort-proof-abort", histories, commit: false, ct).ConfigureAwait(false);
        SharedProjectionDelivery queuedFirst = Delivery(tenant, First, Append(histories, First, new WorkItemQueued(First, 2, tenantId, new WorkItemId(First))));
        (await coordinator.JournalAsync(scope, lease, queuedFirst, ct).ConfigureAwait(false)).ShouldBe(SharedProjectionJournalResult.StaleLease);
        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct).ConfigureAwait(false);
        (await coordinator.JournalAsync(scope, lease, queuedFirst, ct).ConfigureAwait(false)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.JournalAsync(scope, lease, Delivery(tenant, Second,
            Append(histories, Second, new WorkItemQueued(Second, 2, tenantId, new WorkItemId(Second)))), ct).ConfigureAwait(false))
            .ShouldBe(SharedProjectionJournalResult.Journaled);
        Snapshot capturing = await SnapshotAsync(coordinator, scope, stale: true, ct).ConfigureAwait(false);
        capturing.Documents.ShouldBe(served.Documents, "the stale served generation must not show deliveries journaled during the capture");
        capturing.Checkpoints.ShouldBe(new Dictionary<string, long> { [First] = 2, [Second] = 2 }, "both deliveries are journaled");

        // The production abort (deferred open, then completion) races further deliveries, including a dated await.
        ProjectionEventDto[] racing =
        [
            Append(histories, Second, new WorkItemAssigned(Second, 3, tenantId, new WorkItemId(Second), binding)),
            Append(histories, Second, new WorkItemClaimed(Second, 4, tenantId, new WorkItemId(Second), binding)),
            Append(histories, Second, new WorkItemSuspended(Second, 5, tenantId, new WorkItemId(Second), [AwaitCondition.DateReached(s_due)])),
        ];
        long started = Stopwatch.GetTimestamp();
        Task abort = AbortAsync(coordinator, handler, scope, "abort-proof-abort", ct);
        foreach (ProjectionEventDto dto in racing)
            lease = await JournalEventuallyAsync(coordinator, handler, scope, lease, Delivery(tenant, Second, dto), ct).ConfigureAwait(false);
        await abort.ConfigureAwait(false);
        double abortMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _ = await CatchUpAsync(coordinator, handler, scope, ct).ConfigureAwait(false);

        // A restarted reader serves the original generation with every journaled delivery folded in.
        var restarted = new SharedProjectionEpochCoordinator(store, batches);
        SharedProjectionEpochStatus aborted = await restarted.GetStatusAsync(scope, ct).ConfigureAwait(false);
        aborted.ActiveGeneration.ShouldBe(served.Generation);
        aborted.IsBuilding.ShouldBeFalse();
        aborted.IsDegraded.ShouldBeFalse();
        aborted.PendingDeliveryCount.ShouldBe(0);
        Snapshot afterAbort = await SnapshotAsync(restarted, scope, stale: false, ct).ConfigureAwait(false);
        afterAbort.Generation.ShouldBe(served.Generation);
        afterAbort.Checkpoints.ShouldBe(new Dictionary<string, long> { [First] = 2, [Second] = 5 });
        afterAbort.Statuses.ShouldBe(new Dictionary<string, string> { [First] = "Queued" });
        afterAbort.PendingDateOwners.ShouldBe([Second]);
        states.Add(afterAbort.Canonical);

        // The aborted epoch leaves nothing that collides with the next authoritative rebuild of the same histories.
        await RebuildAsync(coordinator, scope, tenant, "abort-proof-after", histories, commit: true, ct).ConfigureAwait(false);
        Snapshot rebuilt = await SnapshotAsync(new SharedProjectionEpochCoordinator(store, batches), scope, stale: false, ct).ConfigureAwait(false);
        // The aborted epoch's number is consumed, so the next promotion selects a later generation.
        rebuilt.Generation.ShouldBeGreaterThan(served.Generation);
        rebuilt.Canonical.ShouldBe(afterAbort.Canonical, "an authoritative rebuild must reproduce the converged family");
        states.Add(rebuilt.Canonical);

        // Live delivery keeps converging after the rebuild.
        lease = await restarted.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct).ConfigureAwait(false);
        lease = await JournalEventuallyAsync(restarted, handler, scope, lease, Delivery(tenant, First,
            Append(histories, First, new WorkItemAssigned(First, 3, tenantId, new WorkItemId(First), binding))), ct).ConfigureAwait(false);
        _ = await CatchUpAsync(restarted, handler, scope, ct).ConfigureAwait(false);
        Snapshot live = await SnapshotAsync(new SharedProjectionEpochCoordinator(store, batches), scope, stale: false, ct).ConfigureAwait(false);
        live.Checkpoints[First].ShouldBe(3);
        live.Statuses[First].ShouldBe("Assigned");
        states.Add(live.Canonical);
        return new AbortRun(served.Generation, rebuilt.Generation, states, abortMilliseconds);
    }

    private static async Task<SharedProjectionLease> JournalEventuallyAsync(SharedProjectionEpochCoordinator coordinator,
        WorkItemSharedProjectionRebuildHandler handler, SharedProjectionScope scope, SharedProjectionLease lease,
        SharedProjectionDelivery delivery, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            SharedProjectionJournalResult result = await coordinator.JournalAsync(scope, lease, delivery, ct).ConfigureAwait(false);
            if (result is SharedProjectionJournalResult.Journaled or SharedProjectionJournalResult.AlreadyJournaled) return lease;
            if (result == SharedProjectionJournalResult.StaleLease)
                lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct).ConfigureAwait(false);
            else if (result == SharedProjectionJournalResult.Backpressure)
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            else
                throw new InvalidOperationException("Unexpected journal result " + result + ".");
        }

        throw new TimeoutException("The delivery was not journaled.");
    }

    private static Task<int> CatchUpAsync(SharedProjectionEpochCoordinator coordinator, WorkItemSharedProjectionRebuildHandler handler,
        SharedProjectionScope scope, CancellationToken ct)
        => coordinator.CatchUpAsync(scope, (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct);

    private static async Task AbortAsync(SharedProjectionEpochCoordinator coordinator, WorkItemSharedProjectionRebuildHandler handler,
        SharedProjectionScope scope, string operation, CancellationToken ct)
    {
        // DomainSharedProjectionRebuildDispatcher aborts with a deferred open, then completes the abort separately.
        await coordinator.AbortAsync(scope, operation,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct, deferOpen: true).ConfigureAwait(false);
        await coordinator.CompleteAbortAsync(scope, operation, ct).ConfigureAwait(false);
    }

    private static async Task RebuildAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope, string tenant, string operation,
        SortedDictionary<string, List<ProjectionEventDto>> histories, bool commit, CancellationToken ct)
    {
        WorkItemSharedRebuildAggregateHistory[] captured = [.. histories.Select(pair => new WorkItemSharedRebuildAggregateHistory(pair.Key, [.. pair.Value]))];
        var identity = new DomainSharedProjectionRebuildIdentity(tenant, "work", WorksReadModelKeys.RuntimeFamily, operation, "abort-proof-inventory");
        var plan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, new WorkItemSharedRebuildCandidateState(captured), ct);
        _ = await coordinator.BeginAsync(scope, identity.OperationId, identity.CatalogFingerprint,
            captured.ToDictionary(history => history.AggregateId, history => history.Events[^1].GlobalPosition), ct).ConfigureAwait(false);
        _ = await coordinator.StageAsync(scope, identity.OperationId, plan.Operations, ct,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(false);
        if (commit)
        {
            await coordinator.CommitAsync(scope, identity.OperationId, ct).ConfigureAwait(false);
        }
    }

    private static async Task<Snapshot> SnapshotAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope, bool stale, CancellationToken ct)
    {
        var read = await WorksRuntimeIndexReader.ReadAsync(coordinator, scope, ct).ConfigureAwait(false);
        read.IsAvailable.ShouldBeTrue();
        read.IsStale.ShouldBe(stale);
        var indexes = read.Value.ShouldNotBeNull().Indexes;
        var checkpoints = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string owner in new[] { First, Second })
        {
            SharedProjectionCheckpoint? checkpoint = await coordinator.GetCheckpointAsync(scope, owner, ct).ConfigureAwait(false);
            checkpoints[owner] = checkpoint.ShouldNotBeNull().Position;
        }

        Dictionary<string, string> statuses = indexes.Queue.Items.Values.ToDictionary(item => item!.WorkItemId.Value, item => item!.Status.ToString(), StringComparer.Ordinal);
        string[] pendingDateOwners = [.. indexes.PendingDate.Entries.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key).Order(StringComparer.Ordinal)];
        // The generation is reported separately so a later rebuild can be compared with the converged family.
        string documents = JsonSerializer.Serialize(new
        {
            members = indexes.Queue.MemberWorkItemIds,
            items = indexes.Queue.Items.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value),
            pendingDates = indexes.PendingDate.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal),
        }, s_web);
        string canonical = documents + JsonSerializer.Serialize(checkpoints.OrderBy(pair => pair.Key, StringComparer.Ordinal), s_web);
        return new Snapshot(read.Generation, checkpoints, statuses, pendingDateOwners, documents, canonical);
    }

    private static ProjectionEventDto Append(SortedDictionary<string, List<ProjectionEventDto>> histories, string owner, IEventPayload payload)
    {
        List<ProjectionEventDto> events = histories[owner];
        ProjectionEventDto dto = Dto(payload, events.Count + 1);
        events.Add(dto);
        return dto;
    }

    private static SharedProjectionDelivery Delivery(string tenant, string owner, ProjectionEventDto dto)
        => new(owner, dto.SequenceNumber,
            JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(tenant, "work", owner, [dto]), s_web),
            JsonSerializer.SerializeToUtf8Bytes(dto.SequenceNumber),
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));

    private static ProjectionEventDto Dto(IEventPayload payload, long sequence) => WorksSdkProjectionQueryTests.Dto(payload, sequence);

    private static string Digest(IEnumerable<string> states)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', states))));

    private sealed record AbortRun(long ServedGeneration, long RebuiltGeneration, IReadOnlyList<string> States, double AbortMilliseconds);

    private sealed record Snapshot(long Generation, Dictionary<string, long> Checkpoints, Dictionary<string, string> Statuses,
        string[] PendingDateOwners, string Documents, string Canonical);
}
