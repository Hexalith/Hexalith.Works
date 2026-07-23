using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic Tier-1 proof of Story 4.8's pending-date-await index maintenance in the <c>/project</c> dispatcher
/// (AC #2): a date suspension upserts the tenant index and registers the tenant, a resume/terminal removes the
/// entry, non-date suspensions write nothing, colliding inner ids in two tenants never merge, and re-dispatching
/// the same stream is idempotent. Uses an in-memory <see cref="IReadModelStore"/> — no Docker/Dapr/network.
/// </summary>
public sealed class PendingDateAwaitIndexDispatcherTests
{
    private const string TenantA = "tenant-alpha";
    private const string TenantB = "tenant-beta";
    private const string WorkId = "work-1";

    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset s_future = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Date_suspension_upserts_the_tenant_index_and_registers_the_tenant()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        _ = await dispatcher.DispatchAsync(
            Request(TenantA, WorkId, Created(TenantA, WorkId, 1), SuspendedOnDate(TenantA, WorkId, 2, s_future)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwaitTenantIndex index = await ReadIndexAsync(store, TenantA).ConfigureAwait(true);
        index.Entries.ShouldContainKey(WorkId);
        PendingDateAwait entry = index.Entries[WorkId].ShouldHaveSingleItem();
        entry.TenantId.ShouldBe(TenantA);
        entry.WorkItemId.ShouldBe(WorkId);
        entry.Instant.ShouldBe(s_future);

        PendingDateAwaitTenantRegistry registry = await ReadRegistryAsync(store).ConfigureAwait(true);
        registry.Tenants.ShouldContain(TenantA);
    }

    [Fact]
    public async Task Resume_removes_a_previously_indexed_entry()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        // Dispatch 1: the item is suspended → the entry is written.
        _ = await dispatcher.DispatchAsync(
            Request(TenantA, WorkId, Created(TenantA, WorkId, 1), SuspendedOnDate(TenantA, WorkId, 2, s_future)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await ReadIndexAsync(store, TenantA).ConfigureAwait(true)).Entries.ShouldContainKey(WorkId);

        // Dispatch 2: the full replay now includes the resume → the previously-written entry is removed.
        _ = await dispatcher.DispatchAsync(
            Request(
                TenantA,
                WorkId,
                Created(TenantA, WorkId, 1),
                SuspendedOnDate(TenantA, WorkId, 2, s_future),
                new WorkItemResumed(WorkId, 3, new TenantId(TenantA), new WorkItemId(WorkId), AwaitCondition.DateReached(s_future))),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwaitTenantIndex index = await ReadIndexAsync(store, TenantA).ConfigureAwait(true);
        index.Entries.ShouldNotContainKey(WorkId);
    }

    [Fact]
    public async Task Terminal_event_removes_a_previously_indexed_entry()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        _ = await dispatcher.DispatchAsync(
            Request(TenantA, WorkId, Created(TenantA, WorkId, 1), SuspendedOnDate(TenantA, WorkId, 2, s_future)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await ReadIndexAsync(store, TenantA).ConfigureAwait(true)).Entries.ShouldContainKey(WorkId);

        _ = await dispatcher.DispatchAsync(
            Request(
                TenantA,
                WorkId,
                Created(TenantA, WorkId, 1),
                SuspendedOnDate(TenantA, WorkId, 2, s_future),
                new WorkItemCancelled(WorkId, 3, new TenantId(TenantA), new WorkItemId(WorkId))),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwaitTenantIndex index = await ReadIndexAsync(store, TenantA).ConfigureAwait(true);
        index.Entries.ShouldNotContainKey(WorkId);
    }

    [Fact]
    public async Task Non_date_suspension_writes_nothing_to_the_index_or_registry()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        _ = await dispatcher.DispatchAsync(
            Request(
                TenantA,
                WorkId,
                Created(TenantA, WorkId, 1),
                new WorkItemSuspended(WorkId, 2, new TenantId(TenantA), new WorkItemId(WorkId), [AwaitCondition.ExternalSignal("approval")])),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwaitTenantIndex index = await ReadIndexAsync(store, TenantA).ConfigureAwait(true);
        index.Entries.ShouldBeEmpty();

        ReadModelEntry<PendingDateAwaitTenantRegistry> registry = await store
            .GetAsync<PendingDateAwaitTenantRegistry>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        (registry.Value is null || registry.Value.Tenants.Count == 0).ShouldBeTrue();
    }

    [Fact]
    public async Task Colliding_inner_ids_in_two_tenants_never_merge()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var futureB = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        _ = await dispatcher.DispatchAsync(
            Request(TenantA, WorkId, Created(TenantA, WorkId, 1), SuspendedOnDate(TenantA, WorkId, 2, s_future)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await dispatcher.DispatchAsync(
            Request(TenantB, WorkId, Created(TenantB, WorkId, 1), SuspendedOnDate(TenantB, WorkId, 2, futureB)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwaitTenantIndex indexA = await ReadIndexAsync(store, TenantA).ConfigureAwait(true);
        PendingDateAwaitTenantIndex indexB = await ReadIndexAsync(store, TenantB).ConfigureAwait(true);
        indexA.Entries[WorkId].ShouldHaveSingleItem().Instant.ShouldBe(s_future);
        indexB.Entries[WorkId].ShouldHaveSingleItem().Instant.ShouldBe(futureB);

        PendingDateAwaitTenantRegistry registry = await ReadRegistryAsync(store).ConfigureAwait(true);
        registry.Tenants.ShouldBe([TenantA, TenantB], ignoreOrder: true);
    }

    [Fact]
    public async Task Double_dispatch_of_the_same_stream_is_idempotent()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        ProjectionRequest request = Request(TenantA, WorkId, Created(TenantA, WorkId, 1), SuspendedOnDate(TenantA, WorkId, 2, s_future));

        _ = await dispatcher.DispatchAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await dispatcher.DispatchAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwaitTenantIndex index = await ReadIndexAsync(store, TenantA).ConfigureAwait(true);
        index.Entries.Count.ShouldBe(1);
        index.Entries[WorkId].ShouldHaveSingleItem().Instant.ShouldBe(s_future);

        PendingDateAwaitTenantRegistry registry = await ReadRegistryAsync(store).ConfigureAwait(true);
        registry.Tenants.ShouldHaveSingleItem().ShouldBe(TenantA);
    }

    [Fact]
    public void Index_and_registry_round_trip_through_system_text_json()
    {
        var index = new PendingDateAwaitTenantIndex();
        index.Entries[WorkId] = [new PendingDateAwait(TenantA, WorkId, s_future, AwaitCondition.DateReached(s_future).CorrelationKey)];
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, TenantB } };

        PendingDateAwaitTenantIndex? roundTrippedIndex = JsonSerializer.Deserialize<PendingDateAwaitTenantIndex>(
            JsonSerializer.Serialize(index, s_web), s_web);
        PendingDateAwaitTenantRegistry? roundTrippedRegistry = JsonSerializer.Deserialize<PendingDateAwaitTenantRegistry>(
            JsonSerializer.Serialize(registry, s_web), s_web);

        roundTrippedIndex.ShouldNotBeNull();
        roundTrippedIndex.Entries[WorkId].ShouldHaveSingleItem().Instant.ShouldBe(s_future);
        roundTrippedRegistry.ShouldNotBeNull();
        roundTrippedRegistry.Tenants.ShouldBe([TenantA, TenantB], ignoreOrder: true);
    }

    private static WorkItemProjectionDispatcher NewDispatcher(IReadModelStore store)
        => new(store, notifier: null, NullLogger<WorkItemProjectionDispatcher>.Instance);

    private static WorkItemCreated Created(string tenant, string workId, long sequence)
        => new(workId, sequence, new TenantId(tenant), new WorkItemId(workId), new Obligation("Do the thing"));

    private static WorkItemSuspended SuspendedOnDate(string tenant, string workId, long sequence, DateTimeOffset instant)
        => new(workId, sequence, new TenantId(tenant), new WorkItemId(workId), [AwaitCondition.DateReached(instant)]);

    private static ProjectionRequest Request(string tenant, string workId, params IEventPayload[] events)
        => new(tenant, "work", workId, [.. events.Select((evt, index) => Dto(evt, index + 1))]);

    private static ProjectionEventDto Dto(IEventPayload evt, long sequence)
        => new(
            evt.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType(), s_web),
            "json",
            sequence,
            default,
            "corr-1");

    private static async Task<PendingDateAwaitTenantIndex> ReadIndexAsync(IReadModelStore store, string tenant)
    {
        ReadModelEntry<PendingDateAwaitTenantIndex> entry = await store
            .GetAsync<PendingDateAwaitTenantIndex>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(tenant), TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
        return entry.Value ?? new PendingDateAwaitTenantIndex();
    }

    private static async Task<PendingDateAwaitTenantRegistry> ReadRegistryAsync(IReadModelStore store)
    {
        ReadModelEntry<PendingDateAwaitTenantRegistry> entry = await store
            .GetAsync<PendingDateAwaitTenantRegistry>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
        return entry.Value ?? new PendingDateAwaitTenantRegistry();
    }
}
