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
/// adapter the same options-free PascalCase concrete event form EventPersister writes, and asserts the
/// adapter's persisted end state and query representation: eligible items land in the tenant "what's next"
/// index, terminal items fall out, locally complete leaf totals remain available, and child-dependent parent
/// totals are refused because separate aggregate dispatches cannot reconcile them. Missing indexes and
/// cross-tenant lookups fail closed.
/// </summary>
public sealed class WorkItemProjectionQueryAdapterTests
{
    private const string Tenant = "tenant-alpha";
    private const string OtherTenant = "tenant-beta";
    private const string WorkId = "work-1";
    private const string ChildId = "work-1-child";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly ExecutorBinding Binding = new(new PartyId("party-1"), Channel.Mcp, AuthorityLevel.Coordinate);
    private static readonly Unit Hour = new("hour");
    private static readonly Unit Point = new("point");

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
    public async Task CamelCaseWebCompatibilityPayloadIsAcceptedByTheProjectionReader()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                WebDto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                WebDto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

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
    public async Task Parent_progress_persists_reliable_local_evidence_but_refuses_child_dependent_totals()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        ProjectionResponse response = await dispatcher
            .DispatchAsync(ParentWithChild(progress: 2m), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        WorkItemRollUp parent = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        parent.TenantId.Value.ShouldBe(Tenant);
        parent.WorkItemId.Value.ShouldBe(WorkId);
        parent.Status.ShouldBe(WorkItemStatus.Assigned);
        parent.OwnEffort.ShouldNotBeNull().ShouldBe(new WorkItemEffort(10m, Hour, 2m));
        parent.OwnRemaining.ShouldBe(new OwnRemaining(8m, Hour));
        parent.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
        parent.ExposedChildCount.ShouldBe(1);
        parent.LatestAcceptedSourceSequence.ShouldBe(4);
        parent.RolledRemaining.ShouldBeNull();
        parent.RolledRemainingByUnit.ShouldBeEmpty();
        parent.Degraded.ShouldBeFalse();
        parent.ProjectionDiagnostics.ShouldBeEmpty();

        WhatsNextItem returned = response.State.Deserialize<WhatsNextItem>(Web).ShouldNotBeNull();
        returned.OwnRemaining.ShouldBe(new OwnRemaining(8m, Hour));
        returned.RolledRemaining.ShouldBeNull();
        returned.RolledRemainingByUnit.ShouldBeEmpty();

        JsonElement queryItem = (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
        queryItem.GetProperty("tenantId").GetProperty("value").GetString().ShouldBe(Tenant);
        queryItem.GetProperty("status").GetString().ShouldBe(nameof(WorkItemStatus.Assigned));
        queryItem.GetProperty("ownRemaining").GetProperty("value").GetDecimal().ShouldBe(8m);
        queryItem.GetProperty("rolledRemaining").ValueKind.ShouldBe(JsonValueKind.Null);
        queryItem.GetProperty("rolledRemainingByUnit").EnumerateArray().ShouldBeEmpty();
        queryItem.GetProperty("latestAcceptedSourceSequence").GetInt64().ShouldBe(4);
    }

    [Fact]
    public async Task Undecodable_child_spawn_event_type_conservatively_refuses_incomplete_local_totals()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(WorkId);

        ProjectionResponse response = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, parent, new Obligation("Do the thing"), new WorkItemEffort(10m, Hour)), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, parent, Binding), 2),
                new ProjectionEventDto(nameof(ChildSpawned), [0x7B], "json", 3, default, "corr-1"),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp persisted = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        persisted.ExposedChildCount.ShouldBe(0);
        persisted.ChildWorkItemIds.ShouldBeEmpty();
        persisted.OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));
        persisted.RolledRemaining.ShouldBeNull();
        persisted.RolledRemainingByUnit.ShouldBeEmpty();
        persisted.Degraded.ShouldBeFalse();
        persisted.ProjectionDiagnostics.ShouldBeEmpty();

        WhatsNextItem returned = response.State.Deserialize<WhatsNextItem>(Web).ShouldNotBeNull();
        returned.RolledRemaining.ShouldBeNull();
        returned.RolledRemainingByUnit.ShouldBeEmpty();

        JsonElement queryItem = (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
        queryItem.GetProperty("rolledRemaining").ValueKind.ShouldBe(JsonValueKind.Null);
        queryItem.GetProperty("rolledRemainingByUnit").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Mixed_unit_parent_and_child_refuse_scalar_and_per_unit_totals_across_adapter_outputs()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(WorkId);

        ProjectionResponse response = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, parent, new Obligation("Do the thing"), new WorkItemEffort(10m, Hour)), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, parent, Binding), 2),
                Dto(new ChildSpawned(WorkId, 3, tenant, parent, new WorkItemId(ChildId), new Obligation("Do the child work"), new WorkItemEffort(4m, Point)), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp persisted = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        persisted.ExposedChildCount.ShouldBe(1);
        persisted.OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));
        persisted.RolledRemaining.ShouldBeNull();
        persisted.RolledRemainingByUnit.ShouldBeEmpty();

        WhatsNextItem returned = response.State.Deserialize<WhatsNextItem>(Web).ShouldNotBeNull();
        returned.RolledRemaining.ShouldBeNull();
        returned.RolledRemainingByUnit.ShouldBeEmpty();

        JsonElement queryItem = (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
        queryItem.GetProperty("rolledRemaining").ValueKind.ShouldBe(JsonValueKind.Null);
        queryItem.GetProperty("rolledRemainingByUnit").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Initially_effortless_child_still_triggers_refusal_by_contribution_count()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(WorkId);

        ProjectionResponse response = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, parent, new Obligation("Do the thing"), new WorkItemEffort(10m, Hour)), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, parent, Binding), 2),
                Dto(new ChildSpawned(WorkId, 3, tenant, parent, new WorkItemId(ChildId), new Obligation("Do the child work")), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp persisted = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        persisted.ExposedChildCount.ShouldBe(1);
        persisted.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
        persisted.OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));
        persisted.RolledRemaining.ShouldBeNull();
        persisted.RolledRemainingByUnit.ShouldBeEmpty();

        WhatsNextItem returned = response.State.Deserialize<WhatsNextItem>(Web).ShouldNotBeNull();
        returned.RolledRemaining.ShouldBeNull();
        returned.RolledRemainingByUnit.ShouldBeEmpty();

        JsonElement queryItem = (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
        queryItem.GetProperty("rolledRemaining").ValueKind.ShouldBe(JsonValueKind.Null);
        queryItem.GetProperty("rolledRemainingByUnit").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Separately_dispatched_child_progress_preserves_child_state_without_restoring_parent_spawn_total()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        _ = await dispatcher.DispatchAsync(ParentWithChild(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        var tenant = new TenantId(Tenant);
        var child = new WorkItemId(ChildId);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", ChildId,
            [
                Dto(new WorkItemCreated(
                    ChildId,
                    1,
                    tenant,
                    child,
                    new Obligation("Do the child work"),
                    new WorkItemEffort(4m, Hour),
                    Parent: new ParentWorkItemReference(tenant, new WorkItemId(WorkId))), 1),
                Dto(new WorkItemAssigned(ChildId, 2, tenant, child, Binding), 2),
                Dto(new ProgressReported(ChildId, 3, tenant, child, 1m, Hour), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp parent = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        parent.RolledRemaining.ShouldBeNull();
        parent.RolledRemainingByUnit.ShouldBeEmpty();
        parent.OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));

        WorkItemRollUp childRollUp = await ReadRollUpAsync(store, Tenant, ChildId).ConfigureAwait(true);
        childRollUp.Status.ShouldBe(WorkItemStatus.Assigned);
        childRollUp.Parent.ShouldBe(new ParentWorkItemReference(tenant, new WorkItemId(WorkId)));
        childRollUp.OwnEffort.ShouldBe(new WorkItemEffort(4m, Hour, 1m));
        childRollUp.OwnRemaining.ShouldBe(new OwnRemaining(3m, Hour));
        childRollUp.RolledRemaining.ShouldBe(new RolledRemaining(3m, Hour));
        childRollUp.RolledRemainingByUnit.ShouldBe([new RolledRemaining(3m, Hour)]);

        IReadOnlyList<JsonElement> items = await QueryWhatsNextAsync(store).ConfigureAwait(true);
        JsonElement childItem = items.Single(item => item.GetProperty("workItemId").GetProperty("value").GetString() == ChildId);
        childItem.GetProperty("ownRemaining").GetProperty("value").GetDecimal().ShouldBe(3m);
        childItem.GetProperty("rolledRemaining").GetProperty("value").GetDecimal().ShouldBe(3m);
        JsonElement parentItem = items.Single(item => item.GetProperty("workItemId").GetProperty("value").GetString() == WorkId);
        parentItem.GetProperty("rolledRemaining").ValueKind.ShouldBe(JsonValueKind.Null);
        parentItem.GetProperty("rolledRemainingByUnit").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaf_replay_keeps_locally_complete_rolled_totals_available()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing"), new WorkItemEffort(8m, Hour)), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
                Dto(new ProgressReported(WorkId, 3, tenant, item, 2m, Hour), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp leaf = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        leaf.ExposedChildCount.ShouldBe(0);
        leaf.OwnRemaining.ShouldBe(new OwnRemaining(6m, Hour));
        leaf.RolledRemaining.ShouldBe(new RolledRemaining(6m, Hour));
        leaf.RolledRemainingByUnit.ShouldBe([new RolledRemaining(6m, Hour)]);

        JsonElement queryItem = (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
        queryItem.GetProperty("rolledRemaining").GetProperty("value").GetDecimal().ShouldBe(6m);
        queryItem.GetProperty("rolledRemainingByUnit").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Terminal_parent_preserves_status_and_structure_while_rolled_totals_remain_unavailable()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, parent, new Obligation("Do the thing"), new WorkItemEffort(10m, Hour)), 1),
                Dto(new ChildSpawned(WorkId, 2, tenant, parent, new WorkItemId(ChildId), new Obligation("Do the child work"), new WorkItemEffort(4m, Hour)), 2),
                Dto(new WorkItemCompleted(WorkId, 3, tenant, parent), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp terminal = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        terminal.Status.ShouldBe(WorkItemStatus.Completed);
        terminal.OwnEffort.ShouldBe(new WorkItemEffort(10m, Hour));
        terminal.OwnRemaining.ShouldBe(new OwnRemaining(0m, Hour));
        terminal.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
        terminal.ExposedChildCount.ShouldBe(1);
        terminal.LatestAcceptedSourceSequence.ShouldBe(3);
        terminal.RolledRemaining.ShouldBeNull();
        terminal.RolledRemainingByUnit.ShouldBeEmpty();
        (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Colliding_work_item_ids_remain_isolated_by_tenant()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        await DispatchAssignedLeafAsync(dispatcher, Tenant, estimate: 8m).ConfigureAwait(true);
        await DispatchAssignedLeafAsync(dispatcher, OtherTenant, estimate: 3m).ConfigureAwait(true);

        WorkItemRollUp first = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        WorkItemRollUp second = await ReadRollUpAsync(store, OtherTenant, WorkId).ConfigureAwait(true);
        first.TenantId.Value.ShouldBe(Tenant);
        first.RolledRemaining.ShouldBe(new RolledRemaining(8m, Hour));
        second.TenantId.Value.ShouldBe(OtherTenant);
        second.RolledRemaining.ShouldBe(new RolledRemaining(3m, Hour));

        JsonElement firstQuery = (await QueryWhatsNextAsync(store, Tenant).ConfigureAwait(true)).ShouldHaveSingleItem();
        JsonElement secondQuery = (await QueryWhatsNextAsync(store, OtherTenant).ConfigureAwait(true)).ShouldHaveSingleItem();
        firstQuery.GetProperty("tenantId").GetProperty("value").GetString().ShouldBe(Tenant);
        firstQuery.GetProperty("rolledRemaining").GetProperty("value").GetDecimal().ShouldBe(8m);
        secondQuery.GetProperty("tenantId").GetProperty("value").GetString().ShouldBe(OtherTenant);
        secondQuery.GetProperty("rolledRemaining").GetProperty("value").GetDecimal().ShouldBe(3m);
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

    private static ProjectionRequest ParentWithChild(decimal progress = 0m)
    {
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(WorkId);
        List<ProjectionEventDto> events =
        [
            Dto(new WorkItemCreated(WorkId, 1, tenant, parent, new Obligation("Do the thing"), new WorkItemEffort(10m, Hour)), 1),
            Dto(new WorkItemAssigned(WorkId, 2, tenant, parent, Binding), 2),
            Dto(new ChildSpawned(WorkId, 3, tenant, parent, new WorkItemId(ChildId), new Obligation("Do the child work"), new WorkItemEffort(4m, Hour)), 3),
        ];
        if (progress > 0)
        {
            events.Add(Dto(new ProgressReported(WorkId, 4, tenant, parent, progress, Hour), 4));
        }

        return new ProjectionRequest(Tenant, "work", WorkId, [.. events]);
    }

    private static async Task DispatchAssignedLeafAsync(WorkItemProjectionDispatcher dispatcher, string tenantId, decimal estimate)
    {
        var tenant = new TenantId(tenantId);
        var item = new WorkItemId(WorkId);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(tenantId, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing"), new WorkItemEffort(estimate, Hour)), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static ProjectionEventDto Dto(IEventPayload evt, long sequence)
        => new(
            evt.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType()),
            "json",
            sequence,
            default,
            "corr-1");

    private static ProjectionEventDto WebDto(IEventPayload evt, long sequence)
        => new(
            evt.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType(), Web),
            "json",
            sequence,
            default,
            "corr-1");

    private static async Task<WorkItemRollUp> ReadRollUpAsync(IReadModelStore store, string tenantId, string workItemId)
    {
        ReadModelEntry<WorkItemRollUp> entry = await store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(tenantId, workItemId),
                TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
        return entry.Value.ShouldNotBeNull();
    }

    private static async Task<IReadOnlyList<JsonElement>> QueryWhatsNextAsync(IReadModelStore store, string tenantId = Tenant)
    {
        var handler = new WhatsNextQueryHandler(store);
        var envelope = new QueryEnvelope(
            tenantId,
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
