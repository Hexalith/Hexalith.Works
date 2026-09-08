using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

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
    public async Task Older_replay_cannot_resurrect_an_await_cleared_by_a_newer_replay()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        // The item genuinely held a date await, so the index carries an entry for it...
        _ = await dispatcher.DispatchAsync(
            Request(TenantA, WorkId, Created(TenantA, WorkId, 1), SuspendedOnDate(TenantA, WorkId, 2, s_future)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        // ...a newer replay clears it and leaves the tombstone watermark...
        _ = await dispatcher.DispatchAsync(
            Request(
                TenantA,
                WorkId,
                Created(TenantA, WorkId, 1),
                SuspendedOnDate(TenantA, WorkId, 2, s_future),
                new WorkItemResumed(WorkId, 3, new TenantId(TenantA), new WorkItemId(WorkId), AwaitCondition.DateReached(s_future))),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        // ...and an older replay redelivered afterwards cannot resurrect the cleared await.
        _ = await dispatcher.DispatchAsync(
            Request(TenantA, WorkId, Created(TenantA, WorkId, 1), SuspendedOnDate(TenantA, WorkId, 2, s_future)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwaitTenantIndex index = await ReadIndexAsync(store, TenantA).ConfigureAwait(true);
        index.Entries.ShouldNotContainKey(WorkId);
        index.LastSequences[WorkId].ShouldBe(3);
    }

    [Fact]
    public async Task An_item_that_never_held_a_date_await_writes_no_index_document_at_all()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        _ = await dispatcher.DispatchAsync(
            Request(TenantA, WorkId, Created(TenantA, WorkId, 1)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Writing a LastSequences tombstone for every work item ever dispatched would make every /project
        // dispatch in the tenant contend on this one singleton key and grow it without bound.
        store.GetSuccessfulWriteCount(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA)).ShouldBe(0);
        ReadModelEntry<PendingDateAwaitTenantIndex> entry = await store
            .GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA),
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        entry.Value.ShouldBeNull();
    }

    [Fact]
    public async Task A_projection_request_for_another_domain_is_refused()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        // Without this guard a foreign aggregate id would be admitted into the tenant's authoritative
        // MemberWorkItemIds manifest.
        InvalidOperationException thrown = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            new ProjectionRequest(
                TenantA,
                "party",
                WorkId,
                [Dto(Created(TenantA, WorkId, 1), 1)]),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        thrown.Message.ShouldContain("domain");
        store.SuccessfulWriteKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_reserved_tenant_id_is_refused_at_the_project_host_edge()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        InvalidOperationException thrown = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            Request(WorksReadModelKeys.ReservedTenantId, WorkId, Created(WorksReadModelKeys.ReservedTenantId, WorkId, 1)),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        // Its index key would be byte-identical to the well-known registry key and would silently disable
        // date-reminder recovery for every tenant.
        thrown.Message.ShouldContain(WorksReadModelKeys.ReservedTenantId);
        thrown.Message.ShouldContain("registry");
    }

    [Fact]
    public async Task Malformed_state_affecting_event_fails_before_the_pending_index_is_updated()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var malformed = new ProjectionEventDto(
            nameof(WorkItemSuspended),
            "{"u8.ToArray(),
            "json",
            1,
            default,
            "corr-1");

        await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            new ProjectionRequest(TenantA, "work", WorkId, [malformed]),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        (await ReadIndexAsync(store, TenantA).ConfigureAwait(true)).Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Undecodable_state_affecting_event_parks_the_aggregate_after_the_configured_failure_budget()
    {
        var store = new Story47InMemoryReadModelStore();
        var options = new WorksProjectionOptions { MaxUndecodableEventDispatchesBeforeParking = 3 };
        var dispatcher = new WorkItemProjectionDispatcher(store, notifier: null, NullLogger<WorkItemProjectionDispatcher>.Instance, options);
        var request = new ProjectionRequest(
            TenantA,
            "work",
            WorkId,
            [new ProjectionEventDto(nameof(WorkItemSuspended), "{"u8.ToArray(), "json", 7, default, "corr-1")]);

        // Fail closed for every attempt inside the budget: a transient cause must still be retried.
        for (int attempt = 1; attempt < options.MaxUndecodableEventDispatchesBeforeParking; attempt++)
        {
            _ = await Should.ThrowAsync<InvalidOperationException>(
                () => dispatcher.DispatchAsync(request, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        }

        // The budget is spent: acknowledge instead of 500ing forever, so the projection poller stops
        // redispatching this one aggregate rather than looping on it for the life of the deployment.
        _ = await dispatcher.DispatchAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await dispatcher.DispatchAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);

        ReadModelEntry<WorkItemProjectionParking> parking = await store
            .GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkId),
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        parking.Value.ShouldNotBeNull().Parked.ShouldBeTrue();
        parking.Value.FailedSequence.ShouldBe(7);
        parking.Value.FailureCount.ShouldBe(options.MaxUndecodableEventDispatchesBeforeParking);

        // Nothing about the poisoned aggregate was ever projected.
        (await ReadIndexAsync(store, TenantA).ConfigureAwait(true)).Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_at_a_new_sequence_restarts_the_parking_budget()
    {
        var store = new Story47InMemoryReadModelStore();
        var options = new WorksProjectionOptions { MaxUndecodableEventDispatchesBeforeParking = 3 };
        var dispatcher = new WorkItemProjectionDispatcher(store, notifier: null, NullLogger<WorkItemProjectionDispatcher>.Instance, options);

        _ = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            new ProjectionRequest(TenantA, "work", WorkId, [new ProjectionEventDto(nameof(WorkItemSuspended), "{"u8.ToArray(), "json", 7, default, "corr-1")]),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);
        _ = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            new ProjectionRequest(TenantA, "work", WorkId, [new ProjectionEventDto(nameof(WorkItemSuspended), "{"u8.ToArray(), "json", 9, default, "corr-1")]),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        // The budget counts consecutive failures on the same poisoned event, so an unrelated later failure
        // cannot inherit an old count and park an aggregate that has only failed once.
        ReadModelEntry<WorkItemProjectionParking> parking = await store
            .GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkId),
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        parking.Value.ShouldNotBeNull().FailedSequence.ShouldBe(9);
        parking.Value.FailureCount.ShouldBe(1);
        parking.Value.Parked.ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_tenants_are_both_retained_in_the_registry_after_an_etag_conflict()
    {
        var store = new Story47InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitRegistryKey,
            new PendingDateAwaitTenantRegistry(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.CoordinateFirstTrySaveConflict(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitRegistryKey);
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        await Task.WhenAll(
            dispatcher.DispatchAsync(
                Request(TenantA, "work-a", Created(TenantA, "work-a", 1), SuspendedOnDate(TenantA, "work-a", 2, s_future)),
                TestContext.Current.CancellationToken),
            dispatcher.DispatchAsync(
                Request(TenantB, "work-b", Created(TenantB, "work-b", 1), SuspendedOnDate(TenantB, "work-b", 2, s_future)),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        (await ReadRegistryAsync(store).ConfigureAwait(true)).Tenants.ShouldBe([TenantA, TenantB], ignoreOrder: true);
    }

    [Fact]
    public async Task Concurrent_aggregates_are_both_retained_in_one_tenant_index_after_an_etag_conflict()
    {
        var store = new Story47InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitRegistryKey,
            new PendingDateAwaitTenantRegistry { Tenants = { TenantA } },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA),
            new PendingDateAwaitTenantIndex(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.CoordinateFirstTrySaveConflict(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA));
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        await Task.WhenAll(
            dispatcher.DispatchAsync(
                Request(TenantA, "work-a", Created(TenantA, "work-a", 1), SuspendedOnDate(TenantA, "work-a", 2, s_future)),
                TestContext.Current.CancellationToken),
            dispatcher.DispatchAsync(
                Request(TenantA, "work-b", Created(TenantA, "work-b", 1), SuspendedOnDate(TenantA, "work-b", 2, s_future)),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        (await ReadIndexAsync(store, TenantA).ConfigureAwait(true)).Entries.Keys.ShouldBe(["work-a", "work-b"], ignoreOrder: true);
    }

    [Fact]
    public void Index_and_registry_round_trip_through_system_text_json()
    {
        var index = new PendingDateAwaitTenantIndex();
        index.Entries[WorkId] = [new PendingDateAwait(TenantA, WorkId, s_future, AwaitCondition.DateReached(s_future).CorrelationKey)];
        index.LastSequences[WorkId] = 2;
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, TenantB } };

        PendingDateAwaitTenantIndex? roundTrippedIndex = JsonSerializer.Deserialize<PendingDateAwaitTenantIndex>(
            JsonSerializer.Serialize(index, s_web), s_web);
        PendingDateAwaitTenantRegistry? roundTrippedRegistry = JsonSerializer.Deserialize<PendingDateAwaitTenantRegistry>(
            JsonSerializer.Serialize(registry, s_web), s_web);

        roundTrippedIndex.ShouldNotBeNull();
        roundTrippedIndex.Entries[WorkId].ShouldHaveSingleItem().Instant.ShouldBe(s_future);
        roundTrippedIndex.LastSequences[WorkId].ShouldBe(2);
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
