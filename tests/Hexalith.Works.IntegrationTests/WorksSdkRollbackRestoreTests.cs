using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using StackExchange.Redis;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Isolated Redis keyspace restore and legacy Works replay proof.</summary>
public sealed class WorksSdkRollbackRestoreTests
{
    [Fact]
    public async Task Complete_isolated_keyspace_restore_removes_sdk_epoch_then_legacy_replay_recovers_queue()
    {
        string? grpcEndpoint = Environment.GetEnvironmentVariable("WORKS_SDK_ROLLBACK_DAPR_GRPC_ENDPOINT");
        string? redisEndpoint = Environment.GetEnvironmentVariable("WORKS_SDK_ROLLBACK_REDIS_ENDPOINT");
        if (string.IsNullOrWhiteSpace(grpcEndpoint) || string.IsNullOrWhiteSpace(redisEndpoint))
        {
            return;
        }

        if (!string.Equals(Environment.GetEnvironmentVariable("WORKS_SDK_ROLLBACK_ALLOW_ISOLATED_FLUSH"),
                "true", StringComparison.Ordinal)
            || !redisEndpoint.StartsWith("127.0.0.1:", StringComparison.Ordinal)
            || redisEndpoint.EndsWith(":6379", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The rollback fixture requires an explicitly isolated local Redis port.");
        }

        using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(
            redisEndpoint + ",allowAdmin=true").ConfigureAwait(true);
        IDatabase database = redis.GetDatabase();
        IServer server = redis.GetServer(redisEndpoint);
        await server.FlushDatabaseAsync().ConfigureAwait(true);

        string tenantId = "works-rollback-" + Guid.NewGuid().ToString("N");
        const string aggregateId = "rollback-work";
        var tenant = new TenantId(tenantId);
        var workItem = new WorkItemId(aggregateId);
        IEventPayload created = new WorkItemCreated(aggregateId, 1, tenant, workItem,
            new Obligation("Restore and replay"));
        IEventPayload queued = new WorkItemQueued(aggregateId, 2, tenant, workItem);
        ProjectionEventDto first = Dto(created, 1);
        ProjectionEventDto second = Dto(queued, 2);
        var retainedSource = new ProjectionRequest(tenantId, "work", aggregateId, [first, second]);

        using DaprClient preCutoverClient = new DaprClientBuilder().UseGrpcEndpoint(grpcEndpoint).Build();
        var preCutoverStore = new DaprReadModelStore(preCutoverClient, Options.Create(new ReadModelBatchOptions()));
        var legacyWriter = new WorkItemProjectionDispatcher(preCutoverStore, null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await legacyWriter.DispatchAsync(new ProjectionRequest(tenantId, "work", aggregateId, [first]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        const string marker = "rollback-fixture:activation-marker";
        const string writerProtocolMarker = "projection-delivery-writer-protocol";
        await database.StringSetAsync(marker, "pre-cutover-v1").ConfigureAwait(true);

        // The isolated Redis database is the complete state-store snapshot: every key and serialized value.
        IReadOnlyDictionary<string, RedisSnapshotValue> preCutover = await CaptureAsync(server, database)
            .ConfigureAwait(true);
        preCutover.Count.ShouldBeGreaterThan(1);

        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        var coordinator = new SharedProjectionEpochCoordinator(preCutoverStore, preCutoverStore);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        foreach (ProjectionEventDto eventDto in retainedSource.Events)
        {
            var delivery = new SharedProjectionDelivery(aggregateId, eventDto.GlobalPosition,
                JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(tenantId, "work", aggregateId,
                    [eventDto]), JsonSerializerOptions.Web),
                JsonSerializer.SerializeToUtf8Bytes(eventDto.SequenceNumber),
                new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
            (await coordinator.JournalAsync(scope, lease, delivery, TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
            _ = await coordinator.CatchUpAsync(scope,
                (current, generation, token) => handler.FoldCatchUpAsync(current, generation, coordinator, token),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        await database.StringSetAsync(marker, "sdk-v2-activated").ConfigureAwait(true);
        await preCutoverClient.SaveStateAsync(WorksReadModelKeys.StateStoreName, writerProtocolMarker,
            new { schemaVersion = 1, writerProtocolVersion = 2, cutoverCommit = "rollback-fixture",
                activatedAt = DateTimeOffset.UtcNow }, cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        (await coordinator.GetCheckpointAsync(scope, aggregateId, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
        IReadOnlyDictionary<string, RedisSnapshotValue> activated = await CaptureAsync(server, database)
            .ConfigureAwait(true);
        activated.Count.ShouldBeGreaterThan(preCutover.Count);
        activated.Keys.ShouldContain(key => key.EndsWith("||" + writerProtocolMarker, StringComparison.Ordinal));

        // Restore the entire pre-cutover keyspace, including its marker, before selecting the legacy fleet.
        await server.FlushDatabaseAsync().ConfigureAwait(true);
        foreach (KeyValuePair<string, RedisSnapshotValue> entry in preCutover)
        {
            await database.KeyRestoreAsync(entry.Key, entry.Value.Dump, entry.Value.Expiry)
                .ConfigureAwait(true);
        }

        IReadOnlyDictionary<string, RedisSnapshotValue> restored = await CaptureAsync(server, database)
            .ConfigureAwait(true);
        restored.Keys.ShouldBe(preCutover.Keys, ignoreOrder: true);
        restored.Keys.ShouldNotContain(key => key.EndsWith("||" + writerProtocolMarker, StringComparison.Ordinal));
        foreach (KeyValuePair<string, RedisSnapshotValue> entry in preCutover)
        {
            restored[entry.Key].Canonical.ShouldBe(entry.Value.Canonical);
        }

        ((string?)await database.StringGetAsync(marker).ConfigureAwait(true)).ShouldBe("pre-cutover-v1");
        using DaprClient legacyFleetClient = new DaprClientBuilder().UseGrpcEndpoint(grpcEndpoint).Build();
        var legacyFleetStore = new DaprReadModelStore(legacyFleetClient, Options.Create(new ReadModelBatchOptions()));
        var legacyFleetWriter = new WorkItemProjectionDispatcher(legacyFleetStore, null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await legacyFleetWriter.DispatchAsync(retainedSource, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        QueryResult result = await new WhatsNextQueryHandler(legacyFleetStore,
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = false }))
            .ExecuteAsync(new QueryEnvelope(tenantId, "work", aggregateId,
                WhatsNextQueryHandler.WhatsNextQueryType, [], "corr-rollback", "legacy-reader"),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Success.ShouldBeTrue();
        result.GetPayload().GetArrayLength().ShouldBe(1);
        result.GetPayload()[0].GetProperty("workItemId").GetProperty("value").GetString()
            .ShouldBe(aggregateId);
    }

    private static async Task<IReadOnlyDictionary<string, RedisSnapshotValue>> CaptureAsync(
        IServer server, IDatabase database)
    {
        var snapshot = new Dictionary<string, RedisSnapshotValue>(StringComparer.Ordinal);
        foreach (RedisKey key in server.Keys())
        {
            byte[] dump = (await database.KeyDumpAsync(key).ConfigureAwait(true)).ShouldNotBeNull();
            TimeSpan? expiry = await database.KeyTimeToLiveAsync(key).ConfigureAwait(true);
            RedisType type = await database.KeyTypeAsync(key).ConfigureAwait(true);
            string canonical = type switch
            {
                RedisType.String => "string:" + (await database.StringGetAsync(key).ConfigureAwait(true)).ToString(),
                RedisType.Hash => "hash:" + string.Join("|", (await database.HashGetAllAsync(key)
                        .ConfigureAwait(true)).OrderBy(entry => entry.Name.ToString(), StringComparer.Ordinal)
                    .Select(entry => entry.Name + "=" + entry.Value)),
                _ => throw new InvalidOperationException("The isolated rollback fixture found an unsupported Redis value type."),
            };
            snapshot.Add(key.ToString(), new RedisSnapshotValue(dump, expiry, canonical));
        }

        return snapshot;
    }

    private static ProjectionEventDto Dto(IEventPayload payload, long sequence)
        => new(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            "json", sequence, DateTimeOffset.UnixEpoch, "corr-rollback", GlobalPosition: sequence);

}
