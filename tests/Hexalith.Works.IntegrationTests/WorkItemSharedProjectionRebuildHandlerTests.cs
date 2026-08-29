using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises the real EventStore shared lifecycle against the Works reconciliation handler.</summary>
public sealed class WorkItemSharedProjectionRebuildHandlerTests
{
    private const string ChildId = "child";
    private const string ParentId = "parent";
    private const string Tenant = "tenant-alpha";

    private static readonly ExecutorBinding Binding = new(
        new PartyId("party-1"),
        Channel.Mcp,
        AuthorityLevel.Coordinate);
    private static readonly Unit Hour = new("hour");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Legacy_parented_create_rebuild_stages_without_visibility_then_atomically_converges_and_retires_legacy_keys()
    {
        var store = new InMemoryReadModelStore();
        WorkItemRollUp staleParent = RollUp(Tenant, ParentId, 99m);
        var staleParentItem = new WhatsNextItem(
            new TenantId(Tenant),
            new WorkItemId(ParentId),
            WorkItemStatus.Assigned,
            null,
            null,
            Binding,
            new OwnRemaining(99m, Hour),
            new RolledRemaining(99m, Hour),
            [new RolledRemaining(99m, Hour)],
            [],
            1);
        WorksWhatsNextTenantIndex legacyIndex = JsonSerializer.Deserialize<WorksWhatsNextTenantIndex>(
            JsonSerializer.Serialize(new
            {
                items = new Dictionary<string, WhatsNextItem> { [ParentId] = staleParentItem },
                lastSequences = new Dictionary<string, long> { [ParentId] = 1 },
            }, Web),
            Web).ShouldNotBeNull();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyWhatsNextIndexKey(Tenant),
            legacyIndex,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, ParentId),
            staleParent,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, ChildId),
            RollUp(Tenant, ChildId, 88m),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var handler = new WorkItemSharedProjectionRebuildHandler();
        using ServiceProvider provider = BuildProvider(store, handler);
        DomainSharedProjectionRebuildIdentity identity = Identity(Tenant, "rebuild-parented-create");
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(identity)).ConfigureAwait(true);
        current = await DispatchAsync(provider, Accumulate(identity, 0, ChildId, ChildHistory(Tenant))).ConfigureAwait(true);
        DomainSharedProjectionRebuildResponse duplicate = await DispatchAsync(
            provider,
            Accumulate(identity, 0, ChildId, ChildHistory(Tenant))).ConfigureAwait(true);
        duplicate.Status.ShouldBe(ProjectionDispatchStatus.AlreadyCompleted);
        current = await DispatchAsync(provider, Accumulate(identity, 1, ParentId, ParentHistory(Tenant))).ConfigureAwait(true);
        current = await DispatchAsync(provider, Finalize(identity, current)).ConfigureAwait(true);
        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Finalized);

        current = await DispatchAsync(provider, Lifecycle(identity, DomainSharedProjectionRebuildAction.Stage)).ConfigureAwait(true);

        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Prepared);
        (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();
        (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, ParentId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull();
        WorkItemView stagedView = await QueryWorkItemAsync(store, Tenant, ParentId).ConfigureAwait(true);
        stagedView.Found.ShouldBeTrue();
        stagedView.Estimated.ShouldBe(99m);
        IReadOnlyList<WhatsNextItem> stagedQueue = await QueryWhatsNextAsync(store, Tenant).ConfigureAwait(true);
        stagedQueue.ShouldHaveSingleItem().RolledRemaining.ShouldBe(new RolledRemaining(99m, Hour));
        store.HasPendingEnvelope(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant)).ShouldBeTrue();

        current = await DispatchAsync(provider, Lifecycle(identity, DomainSharedProjectionRebuildAction.Commit)).ConfigureAwait(true);

        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
        current.Status.ShouldBe(ProjectionDispatchStatus.Completed);
        WorkItemRollUp parent = await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true);
        parent.OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));
        parent.RolledRemaining.ShouldBe(new RolledRemaining(13m, Hour));
        parent.RolledRemainingByUnit.ShouldBe([new RolledRemaining(13m, Hour)]);
        parent.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
        parent.ExposedChildCount.ShouldBe(1);
        WorkItemRollUp child = await ReadCurrentRollUpAsync(store, Tenant, ChildId).ConfigureAwait(true);
        child.OwnEffort.ShouldBe(new WorkItemEffort(4m, Hour, 1m));
        child.RolledRemaining.ShouldBe(new RolledRemaining(3m, Hour));
        child.Parent.ShouldBe(new ParentWorkItemReference(new TenantId(Tenant), new WorkItemId(ParentId)));

        WorksWhatsNextTenantIndex index = await ReadCurrentIndexAsync(store, Tenant).ConfigureAwait(true);
        index.SchemaVersion.ShouldBe(WorksReadModelKeys.CurrentSchemaVersion);
        index.MemberWorkItemIds.ShouldBe([ChildId, ParentId]);
        index.Items[ParentId].RolledRemaining.ShouldBe(new RolledRemaining(13m, Hour));
        index.Items[ChildId].RolledRemaining.ShouldBe(new RolledRemaining(3m, Hour));
        (await QueryWhatsNextAsync(store, Tenant).ConfigureAwait(true)).Count.ShouldBe(2);
        (await QueryWorkItemAsync(store, Tenant, ParentId).ConfigureAwait(true)).Found.ShouldBeTrue();

        (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, ParentId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();
        (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();
        store.HasPendingEnvelope(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant)).ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_or_malformed_child_keeps_reliable_local_shapes_but_refuses_both_rolled_shapes()
    {
        var store = new InMemoryReadModelStore();
        await RebuildAsync(
            store,
            Tenant,
            "rebuild-missing-child",
            [(ParentId, ParentWithSpawnHistory(Tenant))]).ConfigureAwait(true);

        WorkItemRollUp missing = await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true);
        missing.Status.ShouldBe(WorkItemStatus.Assigned);
        missing.OwnEffort.ShouldBe(new WorkItemEffort(10m, Hour));
        missing.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
        missing.RolledRemaining.ShouldBeNull();
        missing.RolledRemainingByUnit.ShouldBeEmpty();

        var retryStore = new InMemoryReadModelStore();
        ProjectionEventDto malformedCreate = new(
            nameof(WorkItemCreated),
            [0x7B],
            "json",
            1,
            DateTimeOffset.UnixEpoch,
            "corr-1");
        await RebuildAsync(
            retryStore,
            Tenant,
            "rebuild-malformed-child",
            [
                (ChildId, new[] { malformedCreate }),
                (ParentId, ParentWithSpawnHistory(Tenant)),
            ]).ConfigureAwait(true);

        WorkItemRollUp malformedParent = await ReadCurrentRollUpAsync(retryStore, Tenant, ParentId).ConfigureAwait(true);
        malformedParent.OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));
        malformedParent.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
        malformedParent.RolledRemaining.ShouldBeNull();
        malformedParent.RolledRemainingByUnit.ShouldBeEmpty();
        WorkItemRollUp malformedChild = await ReadCurrentRollUpAsync(retryStore, Tenant, ChildId).ConfigureAwait(true);
        malformedChild.Status.ShouldBe(WorkItemStatus.Created);
        malformedChild.OwnEffort.ShouldBe(new WorkItemEffort(4m, Hour));
        malformedChild.RolledRemaining.ShouldBeNull();
        malformedChild.RolledRemainingByUnit.ShouldBeEmpty();
    }

    [Fact]
    public async Task Malformed_nested_child_relationship_keeps_prior_local_fields_and_refuses_rolled_shapes()
    {
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(ParentId);
        ProjectionEventDto malformedRelationship = Dto(
            new ChildSpawned(
                ParentId,
                3,
                tenant,
                parent,
                null!,
                new Obligation("Malformed child"),
                new WorkItemEffort(4m, Hour)),
            3);
        var store = new InMemoryReadModelStore();

        await RebuildAsync(
            store,
            Tenant,
            "rebuild-malformed-relationship",
            [(ParentId,
            [
                Dto(new WorkItemCreated(ParentId, 1, tenant, parent, new Obligation("Parent"), new WorkItemEffort(10m, Hour)), 1),
                Dto(new WorkItemAssigned(ParentId, 2, tenant, parent, Binding), 2),
                malformedRelationship,
            ])]).ConfigureAwait(true);

        WorkItemRollUp result = await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true);
        result.Status.ShouldBe(WorkItemStatus.Assigned);
        result.OwnEffort.ShouldBe(new WorkItemEffort(10m, Hour));
        result.OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));
        result.RolledRemaining.ShouldBeNull();
        result.RolledRemainingByUnit.ShouldBeEmpty();
    }

    [Fact]
    public async Task Foreign_parent_relationship_preserves_child_local_fields_but_refuses_rolled_shapes()
    {
        var tenant = new TenantId(Tenant);
        var child = new WorkItemId(ChildId);
        var store = new InMemoryReadModelStore();
        await RebuildAsync(
            store,
            Tenant,
            "rebuild-foreign-parent",
            [(ChildId,
            [
                Dto(new WorkItemCreated(
                    ChildId,
                    1,
                    tenant,
                    child,
                    new Obligation("Child"),
                    new WorkItemEffort(4m, Hour),
                    Parent: new ParentWorkItemReference(new TenantId("tenant-foreign"), new WorkItemId(ParentId))), 1),
                Dto(new WorkItemAssigned(ChildId, 2, tenant, child, Binding), 2),
            ])]).ConfigureAwait(true);

        WorkItemRollUp result = await ReadCurrentRollUpAsync(store, Tenant, ChildId).ConfigureAwait(true);
        result.Status.ShouldBe(WorkItemStatus.Assigned);
        result.OwnEffort.ShouldBe(new WorkItemEffort(4m, Hour));
        result.Parent.ShouldNotBeNull().TenantId.Value.ShouldBe("tenant-foreign");
        result.RolledRemaining.ShouldBeNull();
        result.RolledRemainingByUnit.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cycle_and_multiple_parent_relationships_conserve_local_fields_and_refuse_rolled_shapes()
    {
        const string firstParentId = "parent-a";
        const string secondParentId = "parent-b";
        const string cycleFirstId = "cycle-a";
        const string cycleSecondId = "cycle-b";
        var tenant = new TenantId(Tenant);
        var store = new InMemoryReadModelStore();

        ProjectionEventDto[] CreatedWithParent(string aggregateId, string parentId, decimal estimate, long sequence = 1)
            =>
            [
                Dto(new WorkItemCreated(
                    aggregateId,
                    sequence,
                    tenant,
                    new WorkItemId(aggregateId),
                    new Obligation(aggregateId),
                    new WorkItemEffort(estimate, Hour),
                    Parent: new ParentWorkItemReference(tenant, new WorkItemId(parentId))), sequence),
            ];

        ProjectionEventDto[] ParentHistoryFor(string aggregateId, decimal estimate)
            =>
            [
                Dto(new WorkItemCreated(
                    aggregateId,
                    1,
                    tenant,
                    new WorkItemId(aggregateId),
                    new Obligation(aggregateId),
                    new WorkItemEffort(estimate, Hour)), 1),
            ];

        ProjectionEventDto[] multipleParentChild =
        [
            .. CreatedWithParent(ChildId, firstParentId, 3m),
            .. CreatedWithParent(ChildId, secondParentId, 3m, sequence: 2),
        ];
        await RebuildAsync(
            store,
            Tenant,
            "rebuild-invalid-graphs",
            [
                (ChildId, multipleParentChild),
                (cycleFirstId, CreatedWithParent(cycleFirstId, cycleSecondId, 5m)),
                (cycleSecondId, CreatedWithParent(cycleSecondId, cycleFirstId, 6m)),
                (firstParentId, ParentHistoryFor(firstParentId, 7m)),
                (secondParentId, ParentHistoryFor(secondParentId, 8m)),
            ]).ConfigureAwait(true);

        foreach ((string id, decimal estimate) in new[]
        {
            (ChildId, 3m),
            (cycleFirstId, 5m),
            (cycleSecondId, 6m),
            (firstParentId, 7m),
            (secondParentId, 8m),
        })
        {
            WorkItemRollUp result = await ReadCurrentRollUpAsync(store, Tenant, id).ConfigureAwait(true);
            result.OwnEffort.ShouldBe(new WorkItemEffort(estimate, Hour));
            result.RolledRemaining.ShouldBeNull();
            result.RolledRemainingByUnit.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Authoritative_membership_makes_unlisted_current_schema_documents_query_unreachable()
    {
        const string orphanId = "orphan";
        var store = new InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentRollUpKey(Tenant, orphanId),
            RollUp(Tenant, orphanId, 77m),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            new WorksWhatsNextTenantIndex
            {
                SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
                MemberWorkItemIds = [orphanId],
                LastSequences = { [orphanId] = 1 },
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        await RebuildAsync(
            store,
            Tenant,
            "rebuild-prunes-membership",
            [(ParentId, ParentHistory(Tenant))]).ConfigureAwait(true);

        WorksWhatsNextTenantIndex index = await ReadCurrentIndexAsync(store, Tenant).ConfigureAwait(true);
        index.MemberWorkItemIds.ShouldBe([ParentId]);
        index.Items.ShouldContainKey(ParentId);
        index.Items.ShouldNotContainKey(orphanId);
        (await QueryWorkItemAsync(store, Tenant, orphanId).ConfigureAwait(true)).Found.ShouldBeFalse();
        (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentRollUpKey(Tenant, orphanId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task Abort_retry_and_colliding_tenant_ids_preserve_atomicity_and_tenant_isolation()
    {
        var store = new InMemoryReadModelStore();
        var handler = new WorkItemSharedProjectionRebuildHandler();
        using ServiceProvider provider = BuildProvider(store, handler);
        DomainSharedProjectionRebuildIdentity abortedIdentity = Identity(Tenant, "rebuild-aborted");
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(abortedIdentity)).ConfigureAwait(true);
        current = await DispatchAsync(provider, Accumulate(abortedIdentity, 0, ParentId, ParentHistory(Tenant))).ConfigureAwait(true);
        current = await DispatchAsync(provider, Finalize(abortedIdentity, current)).ConfigureAwait(true);
        _ = await DispatchAsync(provider, Lifecycle(abortedIdentity, DomainSharedProjectionRebuildAction.Stage)).ConfigureAwait(true);
        current = await DispatchAsync(provider, Lifecycle(abortedIdentity, DomainSharedProjectionRebuildAction.Abort)).ConfigureAwait(true);
        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Aborted);
        (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();

        await RebuildAsync(
            store,
            Tenant,
            "rebuild-tenant-a",
            [(ParentId, ParentHistory(Tenant, estimate: 10m))]).ConfigureAwait(true);
        const string otherTenant = "tenant-beta";
        await RebuildAsync(
            store,
            otherTenant,
            "rebuild-tenant-b",
            [(ParentId, ParentHistory(otherTenant, estimate: 3m))]).ConfigureAwait(true);

        WorkItemRollUp tenantA = await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true);
        WorkItemRollUp tenantB = await ReadCurrentRollUpAsync(store, otherTenant, ParentId).ConfigureAwait(true);
        tenantA.TenantId.Value.ShouldBe(Tenant);
        tenantA.RolledRemaining.ShouldBe(new RolledRemaining(10m, Hour));
        tenantB.TenantId.Value.ShouldBe(otherTenant);
        tenantB.RolledRemaining.ShouldBe(new RolledRemaining(3m, Hour));
        (await QueryWorkItemAsync(store, Tenant, ParentId).ConfigureAwait(true)).Estimated.ShouldBe(10m);
        (await QueryWorkItemAsync(store, otherTenant, ParentId).ConfigureAwait(true)).Estimated.ShouldBe(3m);
    }

    [Fact]
    public async Task Committed_reconciliation_survives_later_ordinary_dispatches_of_the_reconciled_streams()
    {
        var store = new InMemoryReadModelStore();
        await RebuildAsync(
            store,
            Tenant,
            "rebuild-durable-parented-create",
            [
                (ChildId, ChildHistory(Tenant)),
                (ParentId, ParentHistory(Tenant)),
            ]).ConfigureAwait(true);
        (await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true))
            .RolledRemaining.ShouldBe(new RolledRemaining(13m, Hour));

        var dispatcher = new WorkItemProjectionDispatcher(
            store,
            notifier: null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", ParentId, ParentHistory(Tenant)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp parent = await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true);
        parent.RolledRemaining.ShouldBeNull();
        parent.RolledRemainingByUnit.ShouldBeEmpty();
        parent.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
        parent.ExposedChildCount.ShouldBe(1);
        parent.OwnEffort.ShouldBe(new WorkItemEffort(10m, Hour));
        parent.Status.ShouldBe(WorkItemStatus.Assigned);

        // The dispatch writes the current generation, keeps authoritative membership, and stays queryable.
        WorksWhatsNextTenantIndex index = await ReadCurrentIndexAsync(store, Tenant).ConfigureAwait(true);
        index.SchemaVersion.ShouldBe(WorksReadModelKeys.CurrentSchemaVersion);
        index.MemberWorkItemIds.ShouldBe([ChildId, ParentId]);
        index.Items[ParentId].RolledRemaining.ShouldBeNull();
        (await QueryWorkItemAsync(store, Tenant, ParentId).ConfigureAwait(true)).Found.ShouldBeTrue();
        (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, ParentId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();

        // A second dispatch of the same stream must not lose the retained evidence and re-expose a total.
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(Tenant, "work", ParentId, ParentHistory(Tenant)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        WorkItemRollUp repeated = await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true);
        repeated.RolledRemaining.ShouldBeNull();
        repeated.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);
    }

    [Fact]
    public async Task Later_child_spawn_merges_with_reconciled_children_instead_of_replacing_them()
    {
        const string spawnedId = "child-2";
        var store = new InMemoryReadModelStore();
        await RebuildAsync(
            store,
            Tenant,
            "rebuild-before-later-spawn",
            [
                (ChildId, ChildHistory(Tenant)),
                (ParentId, ParentHistory(Tenant)),
            ]).ConfigureAwait(true);
        (await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true))
            .ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId)]);

        // The parent's own stream never carried the reconciled child, and now carries a different one. The
        // replayed and the reconciled evidence are each incomplete, so neither may replace the other.
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId(ParentId);
        var dispatcher = new WorkItemProjectionDispatcher(
            store,
            notifier: null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(
                Tenant,
                "work",
                ParentId,
                [
                    .. ParentHistory(Tenant),
                    Dto(
                        new ChildSpawned(
                            ParentId,
                            3,
                            tenant,
                            parent,
                            new WorkItemId(spawnedId),
                            new Obligation("Later child"),
                            new WorkItemEffort(2m, Hour)),
                        3),
                ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorkItemRollUp merged = await ReadCurrentRollUpAsync(store, Tenant, ParentId).ConfigureAwait(true);
        merged.ChildWorkItemIds.ShouldBe([new WorkItemId(ChildId), new WorkItemId(spawnedId)]);
        merged.ExposedChildCount.ShouldBe(2);
        merged.RolledRemaining.ShouldBeNull();
        merged.RolledRemainingByUnit.ShouldBeEmpty();
        merged.OwnEffort.ShouldBe(new WorkItemEffort(10m, Hour));

        WorksWhatsNextTenantIndex index = await ReadCurrentIndexAsync(store, Tenant).ConfigureAwait(true);
        index.Items[ParentId].RolledRemaining.ShouldBeNull();
    }

    [Fact]
    public async Task Dispatch_after_commit_admits_a_new_aggregate_to_the_current_generation()
    {
        const string newId = "new-work";
        var store = new InMemoryReadModelStore();
        await RebuildAsync(
            store,
            Tenant,
            "rebuild-before-new-aggregate",
            [(ParentId, ParentHistory(Tenant))]).ConfigureAwait(true);

        var tenant = new TenantId(Tenant);
        var workItemId = new WorkItemId(newId);
        var dispatcher = new WorkItemProjectionDispatcher(
            store,
            notifier: null,
            NullLogger<WorkItemProjectionDispatcher>.Instance);
        _ = await dispatcher.DispatchAsync(
            new ProjectionRequest(
                Tenant,
                "work",
                newId,
                [
                    Dto(new WorkItemCreated(newId, 1, tenant, workItemId, new Obligation("New"), new WorkItemEffort(6m, Hour)), 1),
                    Dto(new WorkItemAssigned(newId, 2, tenant, workItemId, Binding), 2),
                ]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        WorksWhatsNextTenantIndex index = await ReadCurrentIndexAsync(store, Tenant).ConfigureAwait(true);
        index.SchemaVersion.ShouldBe(WorksReadModelKeys.CurrentSchemaVersion);
        index.MemberWorkItemIds.ShouldBe([newId, ParentId]);
        WorkItemRollUp created = await ReadCurrentRollUpAsync(store, Tenant, newId).ConfigureAwait(true);
        created.RolledRemaining.ShouldBe(new RolledRemaining(6m, Hour));
        (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, newId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();
        (await QueryWorkItemAsync(store, Tenant, newId).ConfigureAwait(true)).Found.ShouldBeTrue();
        (await QueryWhatsNextAsync(store, Tenant).ConfigureAwait(true))
            .Select(static item => item.WorkItemId.Value)
            .ShouldBe([newId, ParentId], ignoreOrder: true);
    }

    [Fact]
    public async Task Empty_authoritative_inventory_is_refused_instead_of_blanking_the_tenant()
    {
        var store = new InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, ParentId),
            RollUp(Tenant, ParentId, 42m),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var handler = new WorkItemSharedProjectionRebuildHandler();
        DomainSharedProjectionRebuildIdentity identity = Identity(Tenant, "rebuild-empty-inventory");
        DomainSharedProjectionRebuildCandidate empty = await handler
            .CreateEmptyCandidateAsync(identity, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.FinalizeAsync(identity, empty, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        using ServiceProvider provider = BuildProvider(store, handler);
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(identity)).ConfigureAwait(true);
        current = await DispatchAsync(provider, Finalize(identity, current)).ConfigureAwait(true);

        current.Phase.ShouldNotBe(DomainSharedProjectionRebuildPhase.Committed);
        (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldBeNull();
        (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.LegacyRollUpKey(Tenant, ParentId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task Accumulate_replaces_a_redelivered_aggregate_and_refuses_an_unidentified_history()
    {
        var handler = new WorkItemSharedProjectionRebuildHandler();
        DomainSharedProjectionRebuildIdentity identity = Identity(Tenant, "rebuild-redelivery");
        DomainSharedProjectionRebuildCandidate candidate = await handler
            .CreateEmptyCandidateAsync(identity, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        candidate = await handler.AccumulateAsync(
            identity,
            candidate,
            new ProjectionRequest(Tenant, "work", ChildId, ChildHistory(Tenant)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        candidate = await handler.AccumulateAsync(
            identity,
            candidate,
            new ProjectionRequest(Tenant, "work", ChildId, ChildHistory(Tenant)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        candidate = await handler.AccumulateAsync(
            identity,
            candidate,
            new ProjectionRequest(Tenant, "work", ParentId, null!),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        using (JsonDocument document = JsonDocument.Parse(candidate.State))
        {
            document.RootElement.GetProperty("histories").GetArrayLength().ShouldBe(2);
        }

        DomainProjectionRebuildPlan plan = await handler
            .FinalizeAsync(identity, candidate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        plan.Operations.Select(static operation => operation.Key).ShouldBeUnique();

        _ = await Should.ThrowAsync<ArgumentException>(() => handler.AccumulateAsync(
            identity,
            candidate,
            new ProjectionRequest(Tenant, "work", " ", ChildHistory(Tenant)),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    private static DomainSharedProjectionRebuildRequest Accumulate(
        DomainSharedProjectionRebuildIdentity identity,
        long ordinal,
        string aggregateId,
        ProjectionEventDto[] events)
        => new(
            DomainSharedProjectionRebuildProtocol.Version,
            DomainSharedProjectionRebuildAction.Accumulate,
            identity,
            ordinal,
            aggregateId,
            Events: events);

    private static DomainSharedProjectionRebuildRequest Begin(DomainSharedProjectionRebuildIdentity identity)
        => Lifecycle(identity, DomainSharedProjectionRebuildAction.Begin);

    private static ServiceProvider BuildProvider(
        InMemoryReadModelStore store,
        WorkItemSharedProjectionRebuildHandler handler)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton<IReadModelStore>(store);
        _ = services.AddSingleton<IReadModelBatchStagingStore>(store);
        _ = services.AddScoped<IAsyncDomainProjectionHandler>(_ => handler);
        return services.BuildServiceProvider();
    }

    private static ProjectionEventDto[] ChildHistory(string tenantId)
    {
        var tenant = new TenantId(tenantId);
        var child = new WorkItemId(ChildId);
        var parent = new ParentWorkItemReference(tenant, new WorkItemId(ParentId));
        return
        [
            Dto(new WorkItemCreated(ChildId, 1, tenant, child, new Obligation("Child"), new WorkItemEffort(4m, Hour), Parent: parent), 1),
            Dto(new WorkItemAssigned(ChildId, 2, tenant, child, Binding), 2),
            Dto(new ProgressReported(ChildId, 3, tenant, child, 1m, Hour), 3),
        ];
    }

    private static Task<DomainSharedProjectionRebuildResponse> DispatchAsync(
        IServiceProvider provider,
        DomainSharedProjectionRebuildRequest request)
        => DomainSharedProjectionRebuildDispatcher.DispatchAsync(
            provider,
            request,
            new ProjectionDispatchOptions(),
            new DomainProjectionIdentityOptions { AppId = "works-test", ServiceVersion = "v1" },
            TestContext.Current.CancellationToken);

    private static ProjectionEventDto Dto(IEventPayload payload, long sequence)
        => new(
            payload.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            "json",
            sequence,
            DateTimeOffset.UnixEpoch,
            "corr-1");

    private static DomainSharedProjectionRebuildRequest Finalize(
        DomainSharedProjectionRebuildIdentity identity,
        DomainSharedProjectionRebuildResponse response)
        => new(
            DomainSharedProjectionRebuildProtocol.Version,
            DomainSharedProjectionRebuildAction.Finalize,
            identity,
            ExpectedAggregateCount: response.AcceptedAggregateCount,
            ExpectedInventoryFingerprint: response.InventoryFingerprint);

    private static DomainSharedProjectionRebuildIdentity Identity(string tenantId, string operationId)
    {
        string fingerprint = ProjectionRouteCatalogFingerprint.Compute(
            "works-test",
            "v1",
            [new ProjectionDispatchRoute("work", WorkItemSharedProjectionRebuildHandler.ProjectionTypeName)]);
        return new DomainSharedProjectionRebuildIdentity(
            tenantId,
            "work",
            WorkItemSharedProjectionRebuildHandler.ProjectionTypeName,
            operationId,
            fingerprint);
    }

    private static DomainSharedProjectionRebuildRequest Lifecycle(
        DomainSharedProjectionRebuildIdentity identity,
        DomainSharedProjectionRebuildAction action)
        => new(DomainSharedProjectionRebuildProtocol.Version, action, identity);

    private static ProjectionEventDto[] ParentHistory(string tenantId, decimal estimate = 10m)
    {
        var tenant = new TenantId(tenantId);
        var parent = new WorkItemId(ParentId);
        return
        [
            Dto(new WorkItemCreated(ParentId, 1, tenant, parent, new Obligation("Parent"), new WorkItemEffort(estimate, Hour)), 1),
            Dto(new WorkItemAssigned(ParentId, 2, tenant, parent, Binding), 2),
        ];
    }

    private static ProjectionEventDto[] ParentWithSpawnHistory(string tenantId)
    {
        var tenant = new TenantId(tenantId);
        var parent = new WorkItemId(ParentId);
        return
        [
            Dto(new WorkItemCreated(ParentId, 1, tenant, parent, new Obligation("Parent"), new WorkItemEffort(10m, Hour)), 1),
            Dto(new WorkItemAssigned(ParentId, 2, tenant, parent, Binding), 2),
            Dto(new ChildSpawned(ParentId, 3, tenant, parent, new WorkItemId(ChildId), new Obligation("Child"), new WorkItemEffort(4m, Hour)), 3),
        ];
    }

    private static async Task<IReadOnlyList<WhatsNextItem>> QueryWhatsNextAsync(
        IReadModelStore store,
        string tenantId)
    {
        var handler = new WhatsNextQueryHandler(store);
        QueryResult result = await handler.ExecuteAsync(
            new QueryEnvelope(tenantId, "work", ParentId, WhatsNextQueryHandler.WhatsNextQueryType, [], "corr-1", "user-1"),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result.GetPayload().Deserialize<IReadOnlyList<WhatsNextItem>>(Web).ShouldNotBeNull();
    }

    private static async Task<WorkItemView> QueryWorkItemAsync(
        IReadModelStore store,
        string tenantId,
        string aggregateId)
    {
        var handler = new GetWorkItemQueryHandler(store);
        QueryResult result = await handler.ExecuteAsync(
            new QueryEnvelope(tenantId, "work", aggregateId, GetWorkItemQueryHandler.GetWorkItemQueryType, [], "corr-1", "user-1"),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result.GetPayload().Deserialize<WorkItemView>(Web).ShouldNotBeNull();
    }

    private static async Task<WorksWhatsNextTenantIndex> ReadCurrentIndexAsync(
        IReadModelStore store,
        string tenantId)
        => (await store.GetAsync<WorksWhatsNextTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentWhatsNextIndexKey(tenantId),
            TestContext.Current.CancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();

    private static async Task<WorkItemRollUp> ReadCurrentRollUpAsync(
        IReadModelStore store,
        string tenantId,
        string aggregateId)
        => (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.CurrentRollUpKey(tenantId, aggregateId),
            TestContext.Current.CancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();

    private static async Task RebuildAsync(
        InMemoryReadModelStore store,
        string tenantId,
        string operationId,
        IReadOnlyList<(string AggregateId, ProjectionEventDto[] Events)> histories)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler();
        using ServiceProvider provider = BuildProvider(store, handler);
        DomainSharedProjectionRebuildIdentity identity = Identity(tenantId, operationId);
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(identity)).ConfigureAwait(false);
        long ordinal = 0;
        foreach ((string aggregateId, ProjectionEventDto[] events) in histories.OrderBy(static history => history.AggregateId, StringComparer.Ordinal))
        {
            current = await DispatchAsync(provider, Accumulate(identity, ordinal, aggregateId, events)).ConfigureAwait(false);
            ordinal++;
        }

        current = await DispatchAsync(provider, Finalize(identity, current)).ConfigureAwait(false);
        _ = await DispatchAsync(provider, Lifecycle(identity, DomainSharedProjectionRebuildAction.Stage)).ConfigureAwait(false);
        current = await DispatchAsync(provider, Lifecycle(identity, DomainSharedProjectionRebuildAction.Commit)).ConfigureAwait(false);
        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
    }

    private static WorkItemRollUp RollUp(string tenantId, string aggregateId, decimal remaining)
        => new(
            new TenantId(tenantId),
            new WorkItemId(aggregateId),
            WorkItemStatus.Assigned,
            null,
            new OwnRemaining(remaining, Hour),
            new RolledRemaining(remaining, Hour),
            [new RolledRemaining(remaining, Hour)],
            [],
            0,
            1)
        {
            OwnEffort = new WorkItemEffort(remaining, Hour),
        };
}
