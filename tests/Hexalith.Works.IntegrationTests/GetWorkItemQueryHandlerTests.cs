using System.Globalization;
using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic Tier-1 proof of the <c>get-work-item</c> query adapter with an in-memory
/// <see cref="IReadModelStore"/> standing in for Dapr (no Docker/Dapr/containers/network). It feeds the projection
/// dispatcher the same concrete (Web-JSON, no <c>$type</c>) event form EventStore persists, then queries the
/// handler and asserts: an existing work item resolves with its planned effort (estimated/done/remaining/unit) for
/// planned-vs-actual consumers, an unknown id fails closed to a not-found view, and the read is tenant-scoped.
/// </summary>
public sealed class GetWorkItemQueryHandlerTests
{
    private const string Tenant = "tenant-alpha";
    private const string WorkId = "work-1";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Unit Hour = new("hour");

    [Fact]
    public async Task Returns_found_view_with_planned_effort_for_an_existing_work_item()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Build the thing"), new WorkItemEffort(8m, Hour)), 1),
                Dto(new ProgressReported(WorkId, 2, tenant, item, 3m, Hour), 2),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemView view = await QueryGetWorkItemAsync(store, WorkId).ConfigureAwait(true);

        view.Found.ShouldBeTrue();
        view.TenantId.Value.ShouldBe(Tenant);
        view.WorkItemId.Value.ShouldBe(WorkId);
        view.Status.ShouldBe(WorkItemStatus.Created);
        view.Estimated.ShouldBe(8m);
        view.Done.ShouldBe(3m);
        view.Remaining.ShouldBe(5m);
        view.Unit.ShouldNotBeNull().Value.ShouldBe("hour");
    }

    [Fact]
    public async Task Fails_closed_to_not_found_for_an_unknown_work_item()
    {
        var store = new InMemoryReadModelStore();

        WorkItemView view = await QueryGetWorkItemAsync(store, "missing").ConfigureAwait(true);

        view.Found.ShouldBeFalse();
        view.WorkItemId.Value.ShouldBe("missing");
        view.Status.ShouldBe(WorkItemStatus.Unknown);
        view.Estimated.ShouldBeNull();
        view.Unit.ShouldBeNull();
    }

    [Fact]
    public async Task Read_is_tenant_scoped_so_another_tenants_id_is_not_found()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Build the thing"), new WorkItemEffort(8m, Hour)), 1),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Same work-item inner id, different tenant: the tenant-scoped roll-up key cannot resolve the other
        // tenant's item, so the cross-tenant read fails closed.
        WorkItemView view = await QueryGetWorkItemAsync(store, WorkId, tenantId: "tenant-beta").ConfigureAwait(true);

        view.Found.ShouldBeFalse();
    }

    private static WorkItemProjectionDispatcher NewDispatcher(IReadModelStore store)
        => new(store, notifier: null, NullLogger<WorkItemProjectionDispatcher>.Instance);

    private static ProjectionEventDto Dto(IEventPayload evt, long sequence)
        => new(
            evt.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType(), Web),
            "json",
            sequence,
            default,
            "corr-1");

    private static async Task<WorkItemView> QueryGetWorkItemAsync(IReadModelStore store, string workItemId, string tenantId = Tenant)
    {
        var handler = new GetWorkItemQueryHandler(store);
        var envelope = new QueryEnvelope(
            tenantId,
            "work",
            workItemId,
            GetWorkItemQueryHandler.GetWorkItemQueryType,
            [],
            "corr-1",
            "user-1");

        QueryResult result = await handler.ExecuteAsync(envelope, TestContext.Current.CancellationToken).ConfigureAwait(false);
        result.Success.ShouldBeTrue();
        return result.GetPayload().Deserialize<WorkItemView>(Web).ShouldNotBeNull();
    }

    /// <summary>An in-memory read-model store for deterministic adapter tests (mirrors the WhatsNext adapter test).</summary>
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
