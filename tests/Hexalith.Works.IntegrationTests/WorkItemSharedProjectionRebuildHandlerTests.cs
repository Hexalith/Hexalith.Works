using System.Text.Json;

using Hexalith.EventStore.Client.Discovery;
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
using Hexalith.Works.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises SDK generation rebuilds, retained rollback documents and strict evidence admission.</summary>
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
    public async Task Sdk_rebuild_stages_without_visibility_then_atomically_converges_and_preserves_rollback_keys()
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
            WorksReadModelKeys.WhatsNextIndexKey(Tenant),
            legacyIndex,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, ParentId),
            staleParent,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, ChildId),
            RollUp(Tenant, ChildId, 88m),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        using ServiceProvider provider = BuildProvider(store, handler);
        SharedProjectionEpochCoordinator coordinator = provider.GetRequiredService<SharedProjectionEpochCoordinator>();
        DomainSharedProjectionRebuildIdentity identity = Identity(Tenant, "rebuild-parented-create");
        await RebuildAsync(store, Tenant, "seed-last-committed", [(ParentId, ParentHistory(Tenant, estimate: 99m))]);
        SharedProjectionEpochStatus seeded = await coordinator.GetStatusAsync(handler.CreateScope(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        seeded.IsBuilding.ShouldBeFalse();
        seeded.ActiveGeneration.ShouldBeGreaterThan(0);
        ProjectionEventDto[] parentCapture = [.. ParentHistory(Tenant, estimate: 99m),
            Dto(new ReEstimated(ParentId, 3, new TenantId(Tenant), new WorkItemId(ParentId), 10m, Hour), 3)];
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(identity,
            [(ChildId, ChildHistory(Tenant)), (ParentId, parentCapture)])).ConfigureAwait(true);
        current = await DispatchAsync(provider, Accumulate(identity, 0, ChildId, ChildHistory(Tenant))).ConfigureAwait(true);
        DomainSharedProjectionRebuildResponse duplicate = await DispatchAsync(
            provider,
            Accumulate(identity, 0, ChildId, ChildHistory(Tenant))).ConfigureAwait(true);
        duplicate.Status.ShouldBe(ProjectionDispatchStatus.AlreadyCompleted);
        current = await DispatchAsync(provider, Accumulate(identity, 1, ParentId, parentCapture)).ConfigureAwait(true);
        current = await DispatchAsync(provider, Finalize(identity, current)).ConfigureAwait(true);
        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Finalized);

        current = await DispatchAsync(provider, Lifecycle(identity, DomainSharedProjectionRebuildAction.Stage)).ConfigureAwait(true);

        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Prepared);
        (await ReadCurrentIndexAsync(store, Tenant)).MemberWorkItemIds.ShouldBe([ParentId]);
        await AssertRetainedRollbackDocumentsAsync(store).ConfigureAwait(true);
        WorkItemView stagedView = await QueryWorkItemAsync(store, Tenant, ParentId).ConfigureAwait(true);
        stagedView.Found.ShouldBeTrue();
        stagedView.Estimated.ShouldBe(99m);
        IReadOnlyList<WhatsNextItem> stagedQueue = await QueryWhatsNextAsync(store, Tenant).ConfigureAwait(true);
        stagedQueue.ShouldHaveSingleItem().RolledRemaining.ShouldBe(new RolledRemaining(99m, Hour));
        SharedProjectionEpochStatus staged = await coordinator.GetStatusAsync(handler.CreateScope(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        staged.IsBuilding.ShouldBeTrue();
        staged.ActiveGeneration.ShouldBe(seeded.ActiveGeneration);

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

        // The SDK promotion never rewrites the retained-host rollback documents, and leaves no capture pending.
        await AssertRetainedRollbackDocumentsAsync(store).ConfigureAwait(true);
        SharedProjectionEpochStatus committed = await coordinator.GetStatusAsync(handler.CreateScope(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        committed.IsBuilding.ShouldBeFalse();
        committed.IsAborting.ShouldBeFalse();
        committed.PendingDeliveryCount.ShouldBe(0);
        committed.ActiveGeneration.ShouldBeGreaterThan(seeded.ActiveGeneration);

        async Task AssertRetainedRollbackDocumentsAsync(InMemoryReadModelStore retained)
        {
            (await retained.GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(Tenant, ParentId),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull()
                .RolledRemaining.ShouldBe(new RolledRemaining(99m, Hour));
            (await retained.GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(Tenant, ChildId),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull()
                .RolledRemaining.ShouldBe(new RolledRemaining(88m, Hour));
            WorksWhatsNextTenantIndex retainedIndex = (await retained.GetAsync<WorksWhatsNextTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.WhatsNextIndexKey(Tenant),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull();
            retainedIndex.Items[ParentId].RolledRemaining.ShouldBe(new RolledRemaining(99m, Hour));
            retainedIndex.LastSequences[ParentId].ShouldBe(1);
        }
    }

    [Fact]
    public async Task Missing_child_keeps_reliable_local_shapes_but_refuses_both_rolled_shapes()
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
    }

    [Fact]
    public async Task Malformed_child_refuses_the_rebuild_and_keeps_the_last_committed_generation_served()
    {
        var store = new InMemoryReadModelStore();
        ProjectionEventDto malformedCreate = new(
            nameof(WorkItemCreated),
            [0x7B],
            "json",
            1,
            DateTimeOffset.UnixEpoch,
            "corr-1") { GlobalPosition = 1 };
        await RefuseRebuildAsync(
            store,
            Tenant,
            "rebuild-malformed-child",
            [
                (ChildId, new[] { malformedCreate }),
                (ParentId, ParentWithSpawnHistory(Tenant)),
            ]).ConfigureAwait(true);
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
    public async Task Foreign_payload_identity_refuses_sdk_rebuild_and_keeps_the_last_committed_generation_served()
    {
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(ChildId);
        var store = new InMemoryReadModelStore();
        await RefuseRebuildAsync(
            store,
            Tenant,
            "rebuild-foreign-payload-identity",
            [(ChildId,
            [
                Dto(new WorkItemCreated(
                    ChildId,
                    1,
                    tenant,
                    item,
                    new Obligation("Child"),
                    new WorkItemEffort(4m, Hour)), 1),
                Dto(new WorkItemAssigned(ChildId, 2, tenant, item, Binding), 2),
                Dto(new ProgressReported(
                    "foreign-work",
                    3,
                    new TenantId("tenant-foreign"),
                    new WorkItemId("foreign-work"),
                    1m,
                    Hour), 3),
            ])]).ConfigureAwait(true);
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
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        using ServiceProvider provider = BuildProvider(store, handler);
        DomainSharedProjectionRebuildIdentity abortedIdentity = Identity(Tenant, "rebuild-aborted");
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(abortedIdentity, [(ParentId, ParentHistory(Tenant))])).ConfigureAwait(true);
        current = await DispatchAsync(provider, Accumulate(abortedIdentity, 0, ParentId, ParentHistory(Tenant))).ConfigureAwait(true);
        current = await DispatchAsync(provider, Finalize(abortedIdentity, current)).ConfigureAwait(true);
        _ = await DispatchAsync(provider, Lifecycle(abortedIdentity, DomainSharedProjectionRebuildAction.Stage)).ConfigureAwait(true);
        current = await DispatchAsync(provider, Lifecycle(abortedIdentity, DomainSharedProjectionRebuildAction.Abort)).ConfigureAwait(true);
        current.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Aborted);
        SharedProjectionEpochCoordinator coordinator = provider.GetRequiredService<SharedProjectionEpochCoordinator>();
        SharedProjectionEpochStatus aborted = await coordinator.GetStatusAsync(handler.CreateScope(Tenant),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        aborted.IsBuilding.ShouldBeFalse();
        aborted.ActiveGeneration.ShouldBe(0);
        (await coordinator.ReadAsync<WorksRuntimeDocument>(handler.CreateScope(Tenant), WorksReadModelKeys.RuntimeQueueKey,
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
    public async Task Empty_authoritative_inventory_is_refused_instead_of_blanking_the_tenant()
    {
        var store = new InMemoryReadModelStore();
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, ParentId),
            RollUp(Tenant, ParentId, 42m),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        DomainSharedProjectionRebuildIdentity identity = Identity(Tenant, "rebuild-empty-inventory");
        DomainSharedProjectionRebuildCandidate empty = await handler
            .CreateEmptyCandidateAsync(identity, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.FinalizeAsync(identity, empty, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        // Through the SDK dispatcher, an empty inventory cannot replace the last committed generation.
        await RefuseRebuildAsync(store, Tenant, "rebuild-empty-inventory", []).ConfigureAwait(true);
        (await store.GetAsync<WorkItemRollUp>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(Tenant, ParentId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull()
            .RolledRemaining.ShouldBe(new RolledRemaining(42m, Hour));
    }

    [Fact]
    public async Task Accumulate_replaces_a_redelivered_aggregate_and_refuses_an_unidentified_history()
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
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

    [Theory]
    [InlineData("party", WorksReadModelKeys.RuntimeFamily)]
    [InlineData("work", WorkItemSharedProjectionRebuildHandler.ProjectionTypeName)]
    [InlineData("work", "some-other-projection")]
    [InlineData("party", "some-other-projection")]
    public async Task Every_lifecycle_step_refuses_an_identity_outside_the_works_rollup_route(string domain, string projectionType)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        DomainSharedProjectionRebuildIdentity valid = Identity(Tenant, "rebuild-identity");
        var foreign = new DomainSharedProjectionRebuildIdentity(
            Tenant,
            domain,
            projectionType,
            valid.OperationId,
            valid.CatalogFingerprint);
        DomainSharedProjectionRebuildCandidate candidate = await handler
            .CreateEmptyCandidateAsync(valid, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        // The route guard is what keeps another domain's rebuild from being staged through the Works handler,
        // so it must hold on every entry point, not only the first one a test happens to call.
        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.CreateEmptyCandidateAsync(foreign, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        _ = await Should.ThrowAsync<InvalidOperationException>(() => handler.AccumulateAsync(
            foreign,
            candidate,
            new ProjectionRequest(Tenant, "work", ChildId, ChildHistory(Tenant)),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);
        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.FinalizeAsync(foreign, candidate, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("tenant-beta", "work")]
    [InlineData(Tenant, "party")]
    public async Task Accumulate_refuses_history_from_another_tenant_or_domain(string historyTenant, string historyDomain)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        DomainSharedProjectionRebuildIdentity identity = Identity(Tenant, "rebuild-cross-tenant");
        DomainSharedProjectionRebuildCandidate candidate = await handler
            .CreateEmptyCandidateAsync(identity, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        InvalidOperationException thrown = await Should.ThrowAsync<InvalidOperationException>(() => handler.AccumulateAsync(
            identity,
            candidate,
            new ProjectionRequest(historyTenant, historyDomain, ChildId, ChildHistory(Tenant)),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        thrown.Message.ShouldContain("outside its tenant/domain identity");
    }

    [Fact]
    public async Task Single_aggregate_projection_dispatch_is_refused_as_an_unsupported_capability()
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));

        // Ordinary delivery enters through the SDK epoch dispatcher; the direct single-history method
        // cannot bypass its capture, checkpoint and fencing protocol.
        DomainProjectionHandlerResult result = await handler.ProjectAsync(
            new ProjectionRequest(Tenant, "work", ParentId, ParentHistory(Tenant)),
            "dispatch-1",
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        result.Status.ShouldBe(ProjectionDispatchStatus.Failed);
        result.ReasonCode.ShouldBe(ProjectionDispatchReasonCodes.UnsupportedCapability);
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

    private static DomainSharedProjectionRebuildRequest Begin(DomainSharedProjectionRebuildIdentity identity,
        IReadOnlyList<(string AggregateId, ProjectionEventDto[] Events)> histories)
    {
        string fingerprint = DomainSharedProjectionRebuildFingerprint.EmptyInventory;
        long ordinal = 0;
        foreach (var history in histories.OrderBy(item => item.AggregateId, StringComparer.Ordinal))
            fingerprint = DomainSharedProjectionRebuildFingerprint.AppendInventory(fingerprint, ordinal++,
                DomainSharedProjectionRebuildFingerprint.ComputeHistory(history.AggregateId, false, history.Events));
        return Lifecycle(identity, DomainSharedProjectionRebuildAction.Begin) with
        {
            CaptureInventoryFingerprint = fingerprint,
            SourceHighWatermarks = histories.ToDictionary(item => item.AggregateId,
                item => item.Events.LastOrDefault()?.GlobalPosition ?? 0, StringComparer.Ordinal),
        };
    }

    private static ServiceProvider BuildProvider(
        InMemoryReadModelStore store,
        WorkItemSharedProjectionRebuildHandler handler)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton<IReadModelStore>(store);
        _ = services.AddSingleton<IReadModelBatchStore>(store);
        _ = services.AddSingleton<IReadModelBatchStagingStore>(store);
        _ = services.AddSingleton(new SharedProjectionEpochCoordinator(store, store));
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

    private static async Task<DomainSharedProjectionRebuildResponse> DispatchAsync(
        IServiceProvider provider,
        DomainSharedProjectionRebuildRequest request)
    {
        if (request.Action == DomainSharedProjectionRebuildAction.Begin)
            _ = await provider.GetRequiredService<SharedProjectionEpochCoordinator>().RegisterWriterAsync(
                new WorkItemSharedProjectionRebuildHandler().CreateScope(request.Identity.TenantId),
                "works-runtime-v3-writer", TestContext.Current.CancellationToken).ConfigureAwait(false);
        return await DomainSharedProjectionRebuildDispatcher.DispatchAsync(
            provider,
            request,
            new ProjectionDispatchOptions(),
            new DomainProjectionIdentityOptions { AppId = "works-test", ServiceVersion = "v1" },
            TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static ProjectionEventDto Dto(IEventPayload payload, long sequence)
        => new(
            payload.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            "json",
            sequence,
            DateTimeOffset.UnixEpoch,
            "corr-1") { GlobalPosition = sequence };

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
        // Bind rebuild admission to the SDK's advertised ordinary writer and shared family.
        string fingerprint = AdminOperationalIndexMetadata.Create(new DiscoveryResult([], []), ["work"], [],
            [new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }))], "works-test", "v1", new ProjectionDispatchOptions(), ["work"])
            .CatalogFingerprint!;
        return new DomainSharedProjectionRebuildIdentity(
            tenantId,
            "work",
            WorksReadModelKeys.RuntimeFamily,
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
        var handler = new WhatsNextQueryHandler(policy: CoordinatorPolicy(),
            coordinator: new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store));
        QueryResult result = await handler.ExecuteAsync(
            new QueryEnvelope(tenantId, "work", ParentId, WhatsNextQueryHandler.WhatsNextQueryType, [], "corr-1", "user-1") { AuthenticatedWorkloadId = "sdk-test" },
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result.GetPayload().Deserialize<IReadOnlyList<WhatsNextItem>>(Web).ShouldNotBeNull();
    }

    private static async Task<WorkItemView> QueryWorkItemAsync(
        IReadModelStore store,
        string tenantId,
        string aggregateId)
    {
        var handler = new GetWorkItemQueryHandler(policy: CoordinatorPolicy(),
            coordinator: new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store));
        QueryResult result = await handler.ExecuteAsync(
            new QueryEnvelope(tenantId, "work", aggregateId, GetWorkItemQueryHandler.GetWorkItemQueryType, [], "corr-1", "user-1") { AuthenticatedWorkloadId = "sdk-test" },
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result.GetPayload().Deserialize<WorkItemView>(Web).ShouldNotBeNull();
    }

    private static async Task<WorksWhatsNextTenantIndex> ReadCurrentIndexAsync(
        IReadModelStore store,
        string tenantId)
        => (await new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store).ReadAsync<WorksRuntimeDocument>(
            new WorkItemSharedProjectionRebuildHandler().CreateScope(tenantId), WorksReadModelKeys.RuntimeQueueKey,
            TestContext.Current.CancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>();

    private static async Task<WorkItemRollUp> ReadCurrentRollUpAsync(
        IReadModelStore store,
        string tenantId,
        string aggregateId)
        => (await new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store).ReadAsync<WorksRuntimeDocument>(
            new WorkItemSharedProjectionRebuildHandler().CreateScope(tenantId), WorksReadModelKeys.RuntimeRollUpKey(aggregateId),
            TestContext.Current.CancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull().As<WorkItemRollUp>();

    private static WorksQueryPolicy CoordinatorPolicy()
        => new(NSubstitute.Substitute.For<Hexalith.Parties.Client.Abstractions.IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["sdk-test"] }));

    private static async Task RebuildAsync(
        InMemoryReadModelStore store,
        string tenantId,
        string operationId,
        IReadOnlyList<(string AggregateId, ProjectionEventDto[] Events)> histories)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        using ServiceProvider provider = BuildProvider(store, handler);
        DomainSharedProjectionRebuildIdentity identity = Identity(tenantId, operationId);
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(identity, histories)).ConfigureAwait(false);
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

    /// <summary>
    /// Seeds a committed SDK generation, then drives a refused capture through the SDK rebuild dispatcher and proves
    /// that finalization fails as a handler failure while readers keep the last committed generation.
    /// </summary>
    private static async Task RefuseRebuildAsync(InMemoryReadModelStore store, string tenant, string operation,
        IReadOnlyList<(string AggregateId, ProjectionEventDto[] Events)> histories)
    {
        const string seededId = "seeded-committed";
        var seededItem = new WorkItemId(seededId);
        await RebuildAsync(store, tenant, operation + "-seed", [(seededId,
        [
            Dto(new WorkItemCreated(seededId, 1, new TenantId(tenant), seededItem, new Obligation("Seeded"), new WorkItemEffort(21m, Hour)), 1),
            Dto(new WorkItemAssigned(seededId, 2, new TenantId(tenant), seededItem, Binding), 2),
        ])]).ConfigureAwait(false);

        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        using ServiceProvider provider = BuildProvider(store, handler);
        SharedProjectionEpochCoordinator coordinator = provider.GetRequiredService<SharedProjectionEpochCoordinator>();
        SharedProjectionScope scope = handler.CreateScope(tenant);
        string[] served = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(seededId)];
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> before = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope, served, TestContext.Current.CancellationToken).ConfigureAwait(false);
        before.IsAvailable.ShouldBeTrue();
        long committedGeneration = (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken)
            .ConfigureAwait(false)).ActiveGeneration;
        committedGeneration.ShouldBeGreaterThan(0);

        DomainSharedProjectionRebuildIdentity identity = Identity(tenant, operation);
        DomainSharedProjectionRebuildResponse current = await DispatchAsync(provider, Begin(identity, histories)).ConfigureAwait(false);
        long ordinal = 0;
        foreach ((string aggregateId, ProjectionEventDto[] events) in histories.OrderBy(static history => history.AggregateId, StringComparer.Ordinal))
        {
            current = await DispatchAsync(provider, Accumulate(identity, ordinal, aggregateId, events)).ConfigureAwait(false);
            ordinal++;
        }

        current = await DispatchAsync(provider, Finalize(identity, current)).ConfigureAwait(false);
        current.Phase.ShouldNotBe(DomainSharedProjectionRebuildPhase.Committed);
        current.Status.ShouldBe(ProjectionDispatchStatus.Indeterminate);
        current.ReasonCode.ShouldBe(ProjectionDispatchReasonCodes.HandlerFailure);

        // The refused capture promoted nothing: the active generation and every served document are unchanged.
        (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken).ConfigureAwait(false))
            .ActiveGeneration.ShouldBe(committedGeneration);
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> after = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope, served, TestContext.Current.CancellationToken).ConfigureAwait(false);
        after.IsAvailable.ShouldBeTrue();
        after.Generation.ShouldBe(before.Generation);
        foreach (string key in served)
        {
            after.Value.ShouldNotBeNull()[key].ShouldNotBeNull().Value.GetRawText()
                .ShouldBe(before.Value.ShouldNotBeNull()[key].ShouldNotBeNull().Value.GetRawText());
        }

        WorkItemView view = await QueryWorkItemAsync(store, tenant, seededId).ConfigureAwait(false);
        view.Found.ShouldBeTrue();
        view.Estimated.ShouldBe(21m);
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
            1)
        {
            OwnEffort = new WorkItemEffort(remaining, Hour),
        };
}
