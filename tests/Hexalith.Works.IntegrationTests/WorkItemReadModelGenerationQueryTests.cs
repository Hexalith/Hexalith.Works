using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Verifies fail-closed generation selection and the bounded shared-rebuild commit-race retry.</summary>
public sealed class WorkItemReadModelGenerationQueryTests
{
    private const string Tenant = "tenant-alpha";
    private const string WorkId = "work-1";

    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private static readonly Unit s_hour = new("hour");

    [Fact]
    public async Task Whats_next_rereads_current_manifest_when_commit_removes_legacy_between_reads()
    {
        WorksWhatsNextTenantIndex current = CurrentIndex(WorkId, Item(WorkId));
        var store = new SwitchingReadModelStore((type, key, count) =>
            type == typeof(WorksWhatsNextTenantIndex)
                && string.Equals(key, WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant), StringComparison.Ordinal)
                && count == 2
                    ? current
                    : null);

        IReadOnlyList<WhatsNextItem> items = await QueryQueueAsync(store).ConfigureAwait(true);

        items.ShouldHaveSingleItem().WorkItemId.Value.ShouldBe(WorkId);
        store.ReadCount<WorksWhatsNextTenantIndex>(WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant)).ShouldBe(2);
    }

    [Fact]
    public async Task Get_work_item_rereads_current_manifest_when_commit_removes_legacy_between_reads()
    {
        WorksWhatsNextTenantIndex current = CurrentIndex(WorkId, Item(WorkId));
        WorkItemRollUp rollUp = RollUp(WorkId);
        var store = new SwitchingReadModelStore((type, key, count) =>
        {
            if (type == typeof(WorksWhatsNextTenantIndex)
                && string.Equals(key, WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant), StringComparison.Ordinal)
                && count == 2)
            {
                return current;
            }

            return type == typeof(WorkItemRollUp)
                && string.Equals(key, WorksReadModelKeys.CurrentRollUpKey(Tenant, WorkId), StringComparison.Ordinal)
                    ? rollUp
                    : null;
        });

        WorkItemView view = await QueryItemAsync(store, WorkId).ConfigureAwait(true);

        view.Found.ShouldBeTrue();
        store.ReadCount<WorksWhatsNextTenantIndex>(WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant)).ShouldBe(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task Present_current_manifest_with_missing_or_unsupported_schema_fails_closed_without_downgrade(int schemaVersion)
    {
        var store = new InMemoryReadModelStore();
        await SeedLegacyAsync(store).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex
            {
                SchemaVersion = schemaVersion,
                Items = { [WorkId] = Item(WorkId) },
                LastSequences = { [WorkId] = 1 },
                MemberWorkItemIds = [WorkId],
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        (await QueryQueueAsync(store).ConfigureAwait(true)).ShouldBeEmpty();
        (await QueryItemAsync(store, WorkId).ConfigureAwait(true)).Found.ShouldBeFalse();

        var dispatcher = new WorkItemProjectionDispatcher(
            store,
            notifier: null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            AssignedHistory(),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);
        WorksWhatsNextTenantIndex retained = (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull();
        retained.SchemaVersion.ShouldBe(schemaVersion);
    }

    [Fact]
    public async Task Null_current_collections_fail_closed_and_dispatch_does_not_downgrade_manifest()
    {
        var store = new InMemoryReadModelStore();
        await SeedLegacyAsync(store).ConfigureAwait(true);
        var invalid = new WorksWhatsNextTenantIndex
        {
            SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
            Items = null!,
            LastSequences = null!,
            MemberWorkItemIds = null!,
        };
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            invalid,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        (await QueryQueueAsync(store).ConfigureAwait(true)).ShouldBeEmpty();
        (await QueryItemAsync(store, WorkId).ConfigureAwait(true)).Found.ShouldBeFalse();

        var dispatcher = new WorkItemProjectionDispatcher(
            store,
            notifier: null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            AssignedHistory(),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        WorksWhatsNextTenantIndex retained = (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull();
        retained.SchemaVersion.ShouldBe(WorksReadModelKeys.CurrentSchemaVersion);
        retained.Items.ShouldBeNull();
    }

    [Fact]
    public async Task Null_legacy_collections_fail_closed_and_dispatch_does_not_rewrite_the_legacy_index()
    {
        var store = new InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex
            {
                Items = null!,
                LastSequences = null!,
                MemberWorkItemIds = null!,
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        // No current manifest exists, so the dispatch stays on the legacy generation. A legacy index missing
        // the collections the upsert copies must fail the dispatch, not silently rebuild a truncated index.
        var dispatcher = new WorkItemProjectionDispatcher(
            store,
            notifier: null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            AssignedHistory(),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        WorksWhatsNextTenantIndex retained = (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull();
        retained.Items.ShouldBeNull();
        retained.MemberWorkItemIds.ShouldBeNull();
        (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();
    }

    [Fact]
    public async Task Current_queue_filters_unlisted_items_and_get_refuses_miskeyed_embedded_rollup()
    {
        const string unlistedId = "unlisted";
        var store = new InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex
            {
                SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
                Items =
                {
                    [WorkId] = Item(WorkId),
                    [unlistedId] = Item(unlistedId),
                },
                LastSequences =
                {
                    [WorkId] = 1,
                    [unlistedId] = 1,
                },
                MemberWorkItemIds = [WorkId],
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentRollUpKey(Tenant, WorkId),
            RollUp("different-id"),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<WhatsNextItem> queue = await QueryQueueAsync(store).ConfigureAwait(true);
        queue.ShouldHaveSingleItem().WorkItemId.Value.ShouldBe(WorkId);
        (await QueryItemAsync(store, WorkId).ConfigureAwait(true)).Found.ShouldBeFalse();
    }

    [Fact]
    public async Task Legacy_rollup_with_mismatched_embedded_identity_is_refused_by_readers()
    {
        var store = new InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, WorkId),
            RollUp("different-id"),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        (await QueryItemAsync(store, WorkId).ConfigureAwait(true)).Found.ShouldBeFalse();
        (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();
    }

    private static ProjectionRequest AssignedHistory()
    {
        var tenant = new TenantId(Tenant);
        var workItemId = new WorkItemId(WorkId);
        return new ProjectionRequest(Tenant, "work", WorkId,
        [
            Dto(new WorkItemCreated(WorkId, 1, tenant, workItemId, new Obligation("Work"), new WorkItemEffort(5m, s_hour)), 1),
            Dto(new WorkItemAssigned(WorkId, 2, tenant, workItemId, new ExecutorBinding(new PartyId("party"), Channel.Mcp, AuthorityLevel.Coordinate)), 2),
        ]);
    }

    private static WorksWhatsNextTenantIndex CurrentIndex(string workItemId, WhatsNextItem item)
        => new()
        {
            SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
            Items = { [workItemId] = item },
            LastSequences = { [workItemId] = 1 },
            MemberWorkItemIds = [workItemId],
        };

    private static ProjectionEventDto Dto(IEventPayload payload, long sequence)
        => new(
            payload.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            "json",
            sequence,
            default,
            "corr-1");

    private static WhatsNextItem Item(string workItemId)
        => new(
            new TenantId(Tenant),
            new WorkItemId(workItemId),
            WorkItemStatus.Assigned,
            null,
            null,
            null,
            new OwnRemaining(5m, s_hour),
            new RolledRemaining(5m, s_hour),
            [new RolledRemaining(5m, s_hour)],
            [],
            1);

    private static WorkItemRollUp RollUp(string workItemId)
        => new(
            new TenantId(Tenant),
            new WorkItemId(workItemId),
            WorkItemStatus.Assigned,
            null,
            new OwnRemaining(5m, s_hour),
            new RolledRemaining(5m, s_hour),
            [new RolledRemaining(5m, s_hour)],
            [],
            1);

    private static async Task SeedLegacyAsync(IReadModelStore store)
    {
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex
            {
                Items = { [WorkId] = Item(WorkId) },
                LastSequences = { [WorkId] = 1 },
            },
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, WorkId),
            RollUp(WorkId),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<WhatsNextItem>> QueryQueueAsync(IReadModelStore store)
    {
        var handler = new WhatsNextQueryHandler(store);
        QueryResult result = await handler.ExecuteAsync(
            new QueryEnvelope(Tenant, "work", WorkId, WhatsNextQueryHandler.WhatsNextQueryType, [], "corr-1", "user-1"),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result.GetPayload().Deserialize<IReadOnlyList<WhatsNextItem>>(s_web).ShouldNotBeNull();
    }

    private static async Task<WorkItemView> QueryItemAsync(IReadModelStore store, string workItemId)
    {
        var handler = new GetWorkItemQueryHandler(store);
        QueryResult result = await handler.ExecuteAsync(
            new QueryEnvelope(Tenant, "work", workItemId, GetWorkItemQueryHandler.GetWorkItemQueryType, [], "corr-1", "user-1"),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result.GetPayload().Deserialize<WorkItemView>(s_web).ShouldNotBeNull();
    }

    private sealed class SwitchingReadModelStore(Func<Type, string, int, object?> read) : IReadModelStore
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly Func<Type, string, int, object?> _read = read;

        public Task<ReadModelEntry<TValue>> GetAsync<TValue>(
            string storeName,
            string key,
            CancellationToken cancellationToken = default)
            where TValue : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            string identity = $"{typeof(TValue).FullName}:{key}";
            int count = _counts.TryGetValue(identity, out int existing) ? existing + 1 : 1;
            _counts[identity] = count;
            return Task.FromResult(new ReadModelEntry<TValue>(_read(typeof(TValue), key, count) as TValue, count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        public int ReadCount<TValue>(string key)
            where TValue : class
            => _counts.GetValueOrDefault($"{typeof(TValue).FullName}:{key}");

        public Task SaveAsync<TValue>(string storeName, string key, TValue value, CancellationToken cancellationToken = default)
            where TValue : class
            => throw new NotSupportedException();

        public Task<bool> TrySaveAsync<TValue>(
            string storeName,
            string key,
            TValue value,
            string etag,
            CancellationToken cancellationToken = default)
            where TValue : class
            => throw new NotSupportedException();
    }
}
