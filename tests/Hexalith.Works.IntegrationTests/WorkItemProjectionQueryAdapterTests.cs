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

using NSubstitute;

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
    public async Task Undecodable_non_relationship_event_refuses_rolled_shapes_on_the_ordinary_path()
    {
        var store = new InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var work = new WorkItemId(WorkId);

        // A childless leaf whose known, non-state-affecting event cannot be decoded: skipping it silently and
        // publishing the surviving events' total would expose a number this stream cannot prove. The refusal
        // must not depend on the event happening to be a ChildSpawned.
        ProjectionResponse response = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, work, new Obligation("Do the thing"), new WorkItemEffort(10m, Hour)), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, work, Binding), 2),
                new ProjectionEventDto(nameof(ProgressReported), [0x7B], "json", 3, default, "corr-1"),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp persisted = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        persisted.ExposedChildCount.ShouldBe(0);
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
    public async Task Older_ineligible_replay_cannot_delete_newer_eligible_state()
    {
        var store = new Story47InMemoryReadModelStore();
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

        store.SuccessfulWriteKeys.Take(2).ShouldBe(
        [
            $"{WorksReadModelKeys.StateStoreName}:{WorksReadModelKeys.RollUpKey(Tenant, WorkId)}",
            $"{WorksReadModelKeys.StateStoreName}:{WorksReadModelKeys.WhatsNextIndexKey(Tenant)}",
        ]);
        store.ResetSuccessfulWriteObservation();

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Stale obligation")), 1),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp rollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        rollUp.Status.ShouldBe(WorkItemStatus.Assigned);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(3);
        rollUp.OwnRemaining.ShouldBe(new OwnRemaining(6m, Hour));

        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items[WorkId].Status.ShouldBe(WorkItemStatus.Assigned);
        index.Items[WorkId].LatestAcceptedSourceSequence.ShouldBe(3);
        index.LastSequences[WorkId].ShouldBe(3);
        store.GetSuccessfulWriteCount(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant)).ShouldBe(0);
    }

    [Fact]
    public async Task Older_eligible_replay_cannot_resurrect_terminal_index_state()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
                Dto(new WorkItemCompleted(WorkId, 3, tenant, item), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp rollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        rollUp.Status.ShouldBe(WorkItemStatus.Completed);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(3);

        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items.ShouldNotContainKey(WorkId);
        index.LastSequences[WorkId].ShouldBe(3);
    }

    [Fact]
    public async Task Tombstone_only_index_refuses_older_eligible_replay_and_does_not_notify()
    {
        var store = new Story47InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex { LastSequences = { [WorkId] = 3 } },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        IProjectionChangeNotifier notifier = Substitute.For<IProjectionChangeNotifier>();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store, notifier);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp rollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        rollUp.Status.ShouldBe(WorkItemStatus.Assigned);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(2);
        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items.ShouldNotContainKey(WorkId);
        index.LastSequences[WorkId].ShouldBe(3);
        (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldBeEmpty();
        await notifier
            .DidNotReceiveWithAnyArgs()
            .NotifyProjectionChangedAsync(default!, default!, default, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Older_terminal_replay_cannot_delete_newer_eligible_index_entry()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
                Dto(new ProgressReported(WorkId, 3, tenant, item, 2m, Hour), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.ResetSuccessfulWriteObservation();

        // The stale replay ends terminal, so accepting it would remove the index entry: the deletion half of
        // the ordering guard, which a stale *eligible* replay can never exercise.
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                Dto(new WorkItemCompleted(WorkId, 2, tenant, item), 2),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp rollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        rollUp.Status.ShouldBe(WorkItemStatus.Assigned);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(3);

        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items[WorkId].Status.ShouldBe(WorkItemStatus.Assigned);
        index.Items[WorkId].LatestAcceptedSourceSequence.ShouldBe(3);
        index.LastSequences[WorkId].ShouldBe(3);
        (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
        store.GetSuccessfulWriteCount(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant)).ShouldBe(0);
    }

    [Fact]
    public async Task Accepted_replay_notifies_once_and_a_later_stale_replay_does_not()
    {
        var store = new Story47InMemoryReadModelStore();
        IProjectionChangeNotifier notifier = Substitute.For<IProjectionChangeNotifier>();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store, notifier);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Pins the accepting edge of the indexAccepted notification gate: without it, a permanently false
        // gate would silence every subscriber while leaving all persisted-state assertions green.
        await notifier
            .Received(1)
            .NotifyProjectionChangedAsync(
                WorksReadModelKeys.WhatsNextProjectionType,
                Tenant,
                null,
                Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
        notifier.ClearReceivedCalls();

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Stale obligation")), 1),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        await notifier
            .DidNotReceiveWithAnyArgs()
            .NotifyProjectionChangedAsync(default!, default!, default, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Concurrent_distinct_items_merge_after_a_tenant_index_etag_conflict()
    {
        var store = new Story47InMemoryReadModelStore();
        string indexKey = WorksReadModelKeys.WhatsNextIndexKey(Tenant);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            indexKey,
            new WorksWhatsNextTenantIndex(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.ResetSuccessfulWriteObservation();
        store.CoordinateFirstTrySaveConflict(WorksReadModelKeys.StateStoreName, indexKey);
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        await Task.WhenAll(
            dispatcher.DispatchAsync(CreateThenAssign(Tenant, WorkId), TestContext.Current.CancellationToken),
            dispatcher.DispatchAsync(CreateThenAssign(Tenant, ChildId), TestContext.Current.CancellationToken)).ConfigureAwait(true);

        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items.Keys.ShouldBe([WorkId, ChildId], ignoreOrder: true);
        index.LastSequences.Keys.ShouldBe([WorkId, ChildId], ignoreOrder: true);
        index.LastSequences[WorkId].ShouldBe(2);
        index.LastSequences[ChildId].ShouldBe(2);
        store.GetSuccessfulWriteCount(WorksReadModelKeys.StateStoreName, indexKey).ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_older_and_newer_replays_converge_after_a_roll_up_etag_conflict()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        string rollUpKey = WorksReadModelKeys.RollUpKey(Tenant, WorkId);
        store.FailNextSaves(WorksReadModelKeys.StateStoreName, rollUpKey);
        store.CoordinateFirstTrySaveConflict(
            WorksReadModelKeys.StateStoreName,
            rollUpKey);

        await Task.WhenAll(
            dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken),
            dispatcher.DispatchAsync(
                new ProjectionRequest(Tenant, "work", WorkId,
                [
                    Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                    Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
                    Dto(new WorkItemCompleted(WorkId, 3, tenant, item), 3),
                ]),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        WorkItemRollUp rollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        rollUp.Status.ShouldBe(WorkItemStatus.Completed);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(3);
        store.GetSuccessfulWriteCount(WorksReadModelKeys.StateStoreName, rollUpKey).ShouldBe(2);

        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items.ShouldNotContainKey(WorkId);
        index.LastSequences[WorkId].ShouldBe(3);
    }

    [Fact]
    public async Task Empty_and_rejection_only_replays_do_not_mutate_authoritative_projection_models()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.ResetSuccessfulWriteObservation();

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId, []),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemTransitionRejected(tenant, item, WorkItemStatus.Assigned, "Claim"), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        store.GetSuccessfulWriteCount(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, WorkId)).ShouldBe(0);
        store.GetSuccessfulWriteCount(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant)).ShouldBe(0);

        (await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true))
            .LatestAcceptedSourceSequence.ShouldBe(2);
        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items[WorkId].LatestAcceptedSourceSequence.ShouldBe(2);
        index.LastSequences[WorkId].ShouldBe(2);
    }

    [Fact]
    public async Task Legacy_eligible_item_watermark_refuses_an_older_replay_without_a_tombstone_map_entry()
    {
        var store = new Story47InMemoryReadModelStore();
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        var legacyItem = new WhatsNextItem(
            tenant,
            item,
            WorkItemStatus.Assigned,
            null,
            null,
            Binding,
            null,
            null,
            [],
            [],
            3);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex { Items = { [WorkId] = legacyItem } },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Stale obligation")), 1),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items[WorkId].ShouldBe(legacyItem);
        index.LastSequences.ShouldNotContainKey(WorkId);
    }

    [Fact]
    public async Task Stored_item_ahead_of_its_retained_sequence_does_not_freeze_the_index()
    {
        // The two watermarks come from different projections: the retained sequence is the roll-up's, while
        // Items[id].LatestAcceptedSourceSequence is the what's-next projection's. Their accept filters can
        // disagree (a ChildSpawned with no child id is accepted by what's-next and refused by the roll-up's
        // identity registry), so a stored item can sit permanently ahead. The item watermark must therefore be
        // a fallback for a missing retained entry, never a competing maximum — maximising over both would
        // refuse this item's every later replay and freeze its index entry and notifications.
        var store = new Story47InMemoryReadModelStore();
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        var aheadItem = new WhatsNextItem(
            tenant,
            item,
            WorkItemStatus.Queued,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            5);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex
            {
                Items = { [WorkId] = aheadItem },
                LastSequences = { [WorkId] = 2 },
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        IProjectionChangeNotifier notifier = Substitute.For<IProjectionChangeNotifier>();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store, notifier);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items[WorkId].Status.ShouldBe(WorkItemStatus.Assigned);
        index.Items[WorkId].ExecutorBinding.ShouldBe(Binding);
        index.Items[WorkId].LatestAcceptedSourceSequence.ShouldBe(2);
        index.LastSequences[WorkId].ShouldBe(2);
        await notifier
            .Received(1)
            .NotifyProjectionChangedAsync(
                WorksReadModelKeys.WhatsNextProjectionType,
                Tenant,
                null,
                Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Newer_replay_over_a_retained_tombstone_restores_eligibility()
    {
        // The forward edge of the tombstone: retention must refuse only *older* replays. An item that leaves
        // the eligible set and later re-enters it (suspend, resume, requeue) has to reappear in the index at
        // its newer sequence, or the guard would evict it from the tenant's queue permanently.
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
                Dto(new WorkItemSuspended(WorkId, 3, tenant, item, [AwaitCondition.ExternalSignal("resume")]), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorksWhatsNextTenantIndex suspended = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        suspended.Items.ShouldNotContainKey(WorkId);
        suspended.LastSequences[WorkId].ShouldBe(3);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenant, item, new Obligation("Do the thing")), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenant, item, Binding), 2),
                Dto(new WorkItemSuspended(WorkId, 3, tenant, item, [AwaitCondition.ExternalSignal("resume")]), 3),
                Dto(new WorkItemResumed(WorkId, 4, tenant, item, AwaitCondition.ExternalSignal("resume")), 4),
                Dto(new WorkItemQueued(WorkId, 5, tenant, item), 5),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorksWhatsNextTenantIndex requeued = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        requeued.Items[WorkId].Status.ShouldBe(WorkItemStatus.Queued);
        requeued.Items[WorkId].LatestAcceptedSourceSequence.ShouldBe(5);
        requeued.LastSequences[WorkId].ShouldBe(5);
        (await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true))
            .LatestAcceptedSourceSequence.ShouldBe(5);
        (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
    }

    [Fact]
    public void Whats_next_index_sequence_tombstones_round_trip_and_legacy_json_gets_a_usable_empty_map()
    {
        var index = new WorksWhatsNextTenantIndex { LastSequences = { [WorkId] = 7 } };

        WorksWhatsNextTenantIndex roundTripped = JsonSerializer
            .Deserialize<WorksWhatsNextTenantIndex>(JsonSerializer.Serialize(index, Web), Web)
            .ShouldNotBeNull();
        roundTripped.LastSequences[WorkId].ShouldBe(7);

        WorksWhatsNextTenantIndex legacy = JsonSerializer
            .Deserialize<WorksWhatsNextTenantIndex>("""{"items":{}}""", Web)
            .ShouldNotBeNull();
        legacy.SchemaVersion.ShouldBe(0);
        legacy.LastSequences.ShouldBeEmpty();
        legacy.LastSequences[WorkId] = 1;
        legacy.LastSequences[WorkId].ShouldBe(1);
    }

    [Fact]
    public async Task Equal_sequence_redispatch_refreshes_deterministic_roll_up_and_index_documents()
    {
        var store = new Story47InMemoryReadModelStore();
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(WorkId);
        var staleRollUp = new WorkItemRollUp(
            tenant,
            item,
            WorkItemStatus.Queued,
            null,
            null,
            null,
            [],
            [],
            0,
            2);
        var staleItem = new WhatsNextItem(
            tenant,
            item,
            WorkItemStatus.Queued,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            2);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, WorkId),
            staleRollUp,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex
            {
                Items = { [WorkId] = staleItem },
                LastSequences = { [WorkId] = 2 },
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp rollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        rollUp.Status.ShouldBe(WorkItemStatus.Assigned);
        rollUp.LatestAcceptedSourceSequence.ShouldBe(2);
        WorksWhatsNextTenantIndex index = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items[WorkId].Status.ShouldBe(WorkItemStatus.Assigned);
        index.Items[WorkId].ExecutorBinding.ShouldBe(Binding);
        index.LastSequences[WorkId].ShouldBe(2);
    }

    [Fact]
    public async Task Roll_up_retry_exhaustion_propagates_without_an_unconditional_fallback()
    {
        var store = new Story47InMemoryReadModelStore();
        string rollUpKey = WorksReadModelKeys.RollUpKey(Tenant, WorkId);
        store.RejectNextTrySaves(
            WorksReadModelKeys.StateStoreName,
            rollUpKey,
            ReadModelWritePolicy.DefaultMaxAttempts);
        store.FailNextSaves(WorksReadModelKeys.StateStoreName, rollUpKey);
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            CreateThenAssign(),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        exception.Message.ShouldContain(rollUpKey);
        exception.Message.ShouldContain("exceeded the optimistic-concurrency retry limit");
        store.GetSuccessfulWriteCount(WorksReadModelKeys.StateStoreName, rollUpKey).ShouldBe(0);
        store.GetSuccessfulWriteCount(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(Tenant)).ShouldBe(0);
    }

    [Fact]
    public async Task Equal_sequence_redispatch_repairs_index_after_non_atomic_index_retry_exhaustion()
    {
        var store = new Story47InMemoryReadModelStore();
        string indexKey = WorksReadModelKeys.WhatsNextIndexKey(Tenant);
        store.RejectNextTrySaves(
            WorksReadModelKeys.StateStoreName,
            indexKey,
            ReadModelWritePolicy.DefaultMaxAttempts);
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            CreateThenAssign(),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        exception.Message.ShouldContain(indexKey);
        WorkItemRollUp firstRollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        firstRollUp.Status.ShouldBe(WorkItemStatus.Assigned);
        firstRollUp.LatestAcceptedSourceSequence.ShouldBe(2);
        (await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true)).Items.ShouldBeEmpty();

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp repairedRollUp = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        repairedRollUp.Status.ShouldBe(WorkItemStatus.Assigned);
        repairedRollUp.LatestAcceptedSourceSequence.ShouldBe(2);
        WorksWhatsNextTenantIndex repairedIndex = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        repairedIndex.Items[WorkId].Status.ShouldBe(WorkItemStatus.Assigned);
        repairedIndex.Items[WorkId].LatestAcceptedSourceSequence.ShouldBe(2);
        repairedIndex.LastSequences[WorkId].ShouldBe(2);
        (await QueryWhatsNextAsync(store).ConfigureAwait(true)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Ordering_guards_keep_colliding_inner_ids_isolated_by_tenant()
    {
        var store = new Story47InMemoryReadModelStore();
        WorkItemProjectionDispatcher dispatcher = NewDispatcher(store);
        var tenantA = new TenantId(Tenant);
        var tenantB = new TenantId(OtherTenant);
        var item = new WorkItemId(WorkId);

        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenantA, item, new Obligation("Tenant A")), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenantA, item, Binding), 2),
                Dto(new WorkItemCompleted(WorkId, 3, tenantA, item), 3),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(OtherTenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenantB, item, new Obligation("Tenant B")), 1),
                Dto(new WorkItemAssigned(WorkId, 2, tenantB, item, Binding), 2),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await dispatcher.DispatchAsync(CreateThenAssign(), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(OtherTenant, "work", WorkId,
            [
                Dto(new WorkItemCreated(WorkId, 1, tenantB, item, new Obligation("Stale tenant B")), 1),
            ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp rollUpA = await ReadRollUpAsync(store, Tenant, WorkId).ConfigureAwait(true);
        WorkItemRollUp rollUpB = await ReadRollUpAsync(store, OtherTenant, WorkId).ConfigureAwait(true);
        rollUpA.Status.ShouldBe(WorkItemStatus.Completed);
        rollUpA.LatestAcceptedSourceSequence.ShouldBe(3);
        rollUpB.Status.ShouldBe(WorkItemStatus.Assigned);
        rollUpB.LatestAcceptedSourceSequence.ShouldBe(2);

        WorksWhatsNextTenantIndex indexA = await ReadWhatsNextIndexAsync(store, Tenant).ConfigureAwait(true);
        WorksWhatsNextTenantIndex indexB = await ReadWhatsNextIndexAsync(store, OtherTenant).ConfigureAwait(true);
        indexA.Items.ShouldNotContainKey(WorkId);
        indexA.LastSequences[WorkId].ShouldBe(3);
        indexB.Items[WorkId].Status.ShouldBe(WorkItemStatus.Assigned);
        indexB.LastSequences[WorkId].ShouldBe(2);
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

    private static WorkItemProjectionDispatcher NewDispatcher(
        IReadModelStore store,
        IProjectionChangeNotifier? notifier = null)
        => new(store, notifier, NullLogger<WorkItemProjectionDispatcher>.Instance);

    private static ProjectionRequest CreateThenAssign(string tenantId = Tenant, string workItemId = WorkId)
    {
        var tenant = new TenantId(tenantId);
        var item = new WorkItemId(workItemId);
        return new ProjectionRequest(tenantId, "work", workItemId,
        [
            Dto(new WorkItemCreated(workItemId, 1, tenant, item, new Obligation("Do the thing")), 1),
            Dto(new WorkItemAssigned(workItemId, 2, tenant, item, Binding), 2),
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

    private static async Task<WorksWhatsNextTenantIndex> ReadWhatsNextIndexAsync(IReadModelStore store, string tenantId)
    {
        ReadModelEntry<WorksWhatsNextTenantIndex> entry = await store
            .GetAsync<WorksWhatsNextTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.WhatsNextIndexKey(tenantId),
                TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
        return entry.Value ?? new WorksWhatsNextTenantIndex();
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
