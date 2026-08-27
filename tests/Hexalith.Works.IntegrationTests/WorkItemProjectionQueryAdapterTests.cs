using System.Globalization;
using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic Tier-1 proof of the Story 4.5 runtime projection + query adapter, with an in-memory
/// <see cref="IReadModelStore"/> standing in for Dapr (no Docker/Dapr/containers/network). It feeds the
/// adapter the same concrete (Web-JSON, no <c>$type</c>) event form EventStore persists, and asserts the
/// pipeline AC #2 convergence at the adapter edge: an <c>Assigned</c>/<c>Queued</c> item lands in the tenant
/// "what's next" index and is returned by the query, a <c>Completed</c> item falls out of the eligible set,
/// and the query fails closed to an empty result when no index exists. The full create → progress → spawn
/// child → suspend → resume → complete persist-then-publish proof under Aspire is the skipped runtime lane.
/// </summary>
public sealed class WorkItemProjectionQueryAdapterTests
{
    private const string Tenant = "tenant-alpha";
    private const string WorkId = "work-1";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly ExecutorBinding Binding = new(new PartyId("party-1"), Channel.Mcp, AuthorityLevel.Coordinate);

    [Fact]
    public async Task Assigned_item_is_projected_into_the_index_and_returned_by_the_query()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        ProjectionResponse response = await dispatcher
            .DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        response.ProjectionType.ShouldBe("works-whats-next");

        IReadOnlyList<JsonElement> items = await QueryWhatsNextAsync(store).ConfigureAwait(true);
        items.Count.ShouldBe(1);
        items[0].GetProperty("workItemId").GetProperty("value").GetString().ShouldBe(WorkId);
    }

    [Fact]
    public async Task Completed_item_falls_out_of_the_whats_next_eligible_set()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await QueryWhatsNextAsync(store).ConfigureAwait(true)).Count.ShouldBe(1);

        // EventStore replays the full stream each /project call; through completion the item leaves {Queued,Assigned}.
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
                Dto(new WorkItemQueued(WorkId, 3, tenant, item), 3),
                Dto(new WorkItemClaimed(WorkId, 4, tenant, item, Binding), 4),
                Dto(new WorkItemCompleted(WorkId, 5, tenant, item), 5),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        (await QueryWhatsNextAsync(store).ConfigureAwait(true)).Count.ShouldBe(0);
    }

    [Fact]
    public async Task AcceptedFreshnessUsesEnvelopePositionAndFilteredRejectionsDoNotAdvanceIt()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                // Persisted rejection at envelope 1 is filtered as no projection state change.
                Dto(new WorkItemCannotBeCreatedWithoutObligation(tenant, item), 1),
                // State-changing payload ordinal 1 intentionally diverges from envelope position 2.
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 2),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 3),
                // A later filtered rejection proves the accepted watermark is not the full stream high-watermark.
                Dto(new WorkItemTransitionRejected(tenant, item, WorkItemStatus.Assigned, "Claim"), 4),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<JsonElement> items = await QueryWhatsNextAsync(store).ConfigureAwait(true);
        items.Count.ShouldBe(1);
        items[0].GetProperty("latestAcceptedSourceSequence").GetInt64().ShouldBe(3);

        // The roll-up read model written by the same dispatch carries the same accepted-envelope watermark:
        // envelope 3 (the last accepted state-changing delivery), not envelope 4 (the filtered rejection).
        ReadModelEntry<WorkItemRollUp> rollUp = await store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(Tenant, WorkId),
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        rollUp.Value.ShouldNotBeNull().LatestAcceptedSourceSequence.ShouldBe(3);
    }

    [Fact]
    public async Task Query_fails_closed_to_empty_for_a_tenant_with_no_index()
        => (await QueryWhatsNextAsync(new InMemoryReadModelStore()).ConfigureAwait(true)).Count.ShouldBe(0);

    private static WorkItemProjectionDispatcher NewDispatcher(IReadModelStore store)
        => new(store, notifier: null, NullLogger<WorkItemProjectionDispatcher>.Instance);

    private static ProjectionRequest CreateThenAssign()
    {
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        return new ProjectionRequest(Tenant, "work", WorkId,
        [
            Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
            Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
        ]);
    }

    private static ProjectionEventDto Dto(IEventPayload evt, long sequence)
        => new(
            evt.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType(), Web),
            "json",
            sequence,
            default,
            "corr-1");

    private static async Task<IReadOnlyList<JsonElement>> QueryWhatsNextAsync(IReadModelStore store)
    {
        var handler = new WhatsNextQueryHandler(store);
        var envelope = new QueryEnvelope(
            Tenant,
            "work",
            WorkId,
            WhatsNextQueryHandler.WhatsNextQueryType,
            [],
            "corr-1",
            "user-1");

        QueryResult result = await handler.ExecuteAsync(envelope, TestContext.Current.CancellationToken).ConfigureAwait(false);
        result.Success.ShouldBeTrue();
        return [.. result.GetPayload().EnumerateArray()];
    }

    /// <summary>An in-memory optimistic-concurrency read-model store for deterministic adapter tests.</summary>
    private sealed class InMemoryReadModelStore : IReadModelStore
    {
        private readonly Dictionary<string, (object Value, long ETag)> _entries = new(StringComparer.Ordinal);

        public Task<ReadModelEntry<TValue>> GetAsync<TValue>(string storeName, string key, CancellationToken cancellationToken = default)
            where TValue : class
            => Task.FromResult(_entries.TryGetValue(Composite(storeName, key), out (object Value, long ETag) entry)
                ? new ReadModelEntry<TValue>((TValue)entry.Value, entry.ETag.ToString(CultureInfo.InvariantCulture))
                : new ReadModelEntry<TValue>(null, null));

        public Task SaveAsync<TValue>(string storeName, string key, TValue value, CancellationToken cancellationToken = default)
            where TValue : class
        {
            string composite = Composite(storeName, key);
            long etag = _entries.TryGetValue(composite, out (object Value, long ETag) entry) ? entry.ETag + 1 : 1;
            _entries[composite] = (value, etag);
            return Task.CompletedTask;
        }

        public Task<bool> TrySaveAsync<TValue>(string storeName, string key, TValue value, string etag, CancellationToken cancellationToken = default)
            where TValue : class
        {
            string composite = Composite(storeName, key);
            bool exists = _entries.TryGetValue(composite, out (object Value, long ETag) entry);
            bool matches = exists
                ? string.Equals(entry.ETag.ToString(CultureInfo.InvariantCulture), etag, StringComparison.Ordinal)
                : string.IsNullOrEmpty(etag);

            if (!matches)
            {
                return Task.FromResult(false);
            }

            _entries[composite] = (value, exists ? entry.ETag + 1 : 1);
            return Task.FromResult(true);
        }

        private static string Composite(string storeName, string key) => $"{storeName}::{key}";
    }
}
