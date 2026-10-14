using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;
using Hexalith.Works.Runtime;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.ValueObjects;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.Extensions;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Persisted SDK generation and checkpoint proof for ordinary Works deliveries.</summary>
public sealed class WorksSdkProjectionQueryTests
{
    private const string Tenant = "tenant-sdk";
    private const string Aggregate = "work-sdk";
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Sdk_host_maps_one_named_projection_route_despite_legacy_project_conflict_route()
    {
        await using WebApplication app = WorksHost.Build(
            ["--Works:Projection:UseSdkDelivery=true",
                "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999",
                "--Works:Recovery:RunReconciliationOnStartup=false"]);
        RouteEndpoint[] routes = [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()];
        routes.Count(route => route.RoutePattern.RawText == "/project"
            && route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true)
            .ShouldBe(1);
        routes.Count(route => route.RoutePattern.RawText == "/project/v2"
            && route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true)
            .ShouldBe(1);
        using IServiceScope scope = app.Services.CreateScope();
        WorkItemSharedProjectionRebuildHandler handler = scope.ServiceProvider
            .GetServices<IAsyncDomainProjectionHandler>()
            .OfType<WorkItemSharedProjectionRebuildHandler>()
            .ShouldHaveSingleItem();
        handler.EpochEnabled.ShouldBeTrue();
        handler.ProjectionType.ShouldBe(WorksReadModelKeys.RuntimeFamily);
    }

    [Fact]
    public async Task Ordinary_delivery_and_redelivery_persist_one_complete_runtime_family_and_checkpoint()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        scope.Family.ShouldBe(WorksReadModelKeys.RuntimeFamily);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        SharedProjectionDelivery created = Delivery(
            new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Build the SDK view")), 1);
        SharedProjectionDelivery queued = Delivery(new WorkItemQueued(Aggregate, 2, tenant, workItem), 2);

        (await coordinator.JournalAsync(scope, lease, created, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await coordinator.JournalAsync(scope, lease, queued, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);

        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> family = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope,
                [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(Aggregate),
                    WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
                    WorksReadModelKeys.RuntimeHistoryKey(Aggregate), WorksReadModelKeys.RuntimeParkingKey(Aggregate)],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        family.IsAvailable.ShouldBeTrue();
        family.IsStale.ShouldBeFalse();
        family.Value.ShouldNotBeNull().Count.ShouldBe(6);
        WorksWhatsNextTenantIndex queue = family.Value[WorksReadModelKeys.RuntimeQueueKey]!
            .As<WorksWhatsNextTenantIndex>();
        queue.MemberWorkItemIds.ShouldBe([Aggregate]);
        queue.Items[Aggregate].Status.ShouldBe(WorkItemStatus.Queued);
        family.Value[WorksReadModelKeys.RuntimeRollUpKey(Aggregate)]!
            .As<WorkItemRollUp>().Status.ShouldBe(WorkItemStatus.Queued);
        family.Value[WorksReadModelKeys.RuntimePendingDateKey]!
            .As<PendingDateAwaitTenantIndex>().Entries.ShouldBeEmpty();
        family.Value[WorksReadModelKeys.RuntimeTopologyKey]!
            .As<WorksRuntimeTopology>().ChildrenByParent.ShouldBeEmpty();
        family.Value[WorksReadModelKeys.RuntimeParkingKey(Aggregate)]!
            .As<WorkItemProjectionParking>().Parked.ShouldBeFalse();
        (await coordinator.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
        (await coordinator.GetControlIndexTenantsAsync(
            WorksReadModelKeys.StateStoreName, WorksReadModelKeys.RuntimeControlIndex,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldContain(Tenant);

        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        SharedProjectionJournalResult repeated = await coordinator.JournalAsync(scope, lease, queued,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        repeated.ShouldNotBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
    }

    [Fact]
    public async Task Permitted_coordinator_reads_current_queue_while_unlisted_workload_is_denied_before_store_access()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var projectionOptions = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        var handler = new WorkItemSharedProjectionRebuildHandler(projectionOptions);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        foreach (SharedProjectionDelivery delivery in new[]
        {
            Delivery(new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Build the SDK view")), 1),
            Delivery(new WorkItemQueued(Aggregate, 2, tenant, workItem), 2),
        })
        {
            (await coordinator.JournalAsync(scope, lease, delivery, TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
            _ = await coordinator.CatchUpAsync(scope,
                (current, generation, token) => handler.FoldCatchUpAsync(current, generation, coordinator, token),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        var policy = new WorksQueryPolicy(parties,
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        var whatsNext = new WhatsNextQueryHandler(store, projectionOptions, policy, coordinator);
        QueryResult permitted = await whatsNext.ExecuteAsync(CoordinatorQuery("scheduler"),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        permitted.Success.ShouldBeTrue();
        permitted.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        permitted.GetPayload().GetArrayLength().ShouldBe(1);

        QueryResult denied = await new WhatsNextQueryHandler(store, projectionOptions, policy)
            .ExecuteAsync(CoordinatorQuery("unlisted") with { IsGlobalAdmin = true },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        denied.Success.ShouldBeFalse();
        denied.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        await parties.DidNotReceiveWithAnyArgs().ResolvePartyIdentityAsync(
                default!, default!, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Verified_executor_sees_own_assigned_and_tenant_queued_but_not_foreign_assigned()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var projectionOptions = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        var handler = new WorkItemSharedProjectionRebuildHandler(projectionOptions);
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(Tenant);
        foreach ((string aggregate, IEventPayload[] events) in new[]
        {
            ("mine", new IEventPayload[] {
                new WorkItemCreated("mine", 1, tenant, new WorkItemId("mine"), new Obligation("Mine")),
                new WorkItemAssigned("mine", 2, tenant, new WorkItemId("mine"),
                    new ExecutorBinding(new PartyId("party-mine"), Channel.Mcp, AuthorityLevel.Coordinate)),
            }),
            ("foreign", new IEventPayload[] {
                new WorkItemCreated("foreign", 1, tenant, new WorkItemId("foreign"), new Obligation("Foreign")),
                new WorkItemAssigned("foreign", 2, tenant, new WorkItemId("foreign"),
                    new ExecutorBinding(new PartyId("party-other"), Channel.Mcp, AuthorityLevel.Coordinate)),
            }),
            ("queue", new IEventPayload[] {
                new WorkItemCreated("queue", 1, tenant, new WorkItemId("queue"), new Obligation("Available")),
                new WorkItemQueued("queue", 2, tenant, new WorkItemId("queue")),
            }),
        })
        {
            for (int index = 0; index < events.Length; index++)
            {
                SharedProjectionDelivery delivery = Delivery(events[index], index + 1, aggregate);
                (await coordinator.JournalAsync(scope, lease, delivery, TestContext.Current.CancellationToken)
                    .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
                _ = await coordinator.CatchUpAsync(scope,
                    (current, generation, token) => handler.FoldCatchUpAsync(current, generation, coordinator, token),
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
                lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
            }
        }

        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        var observedAt = DateTimeOffset.UtcNow;
        var binding = new HumanActorBindingEvidence(Tenant, "party-mine", "actor-mine", 1, 1,
            observedAt.AddMinutes(-1), null, "source", "provenance",
            new IdentityHistoryCustodyEvidence("policy", "party-actor-history-v1", observedAt.AddDays(1),
                1, "custody", true, true, true));
        parties.ResolvePartyIdentityAsync(Tenant, Arg.Any<Hexalith.Parties.Contracts.Queries.ResolvePartyIdentity>(),
                TestContext.Current.CancellationToken)
            .Returns(Task.FromResult(new PartyIdentityResult(PartyIdentityOutcome.Resolved,
                new PartyIdentityEvidence(1, Tenant, "party-mine", PartyIdentityClassification.Human,
                    true, false, false, 2, observedAt, "observation", binding))));
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        var list = new WhatsNextQueryHandler(store, projectionOptions, policy, coordinator);
        QueryEnvelope query = new(Tenant, "work", "mine", WhatsNextQueryHandler.WhatsNextQueryType,
            JsonSerializer.SerializeToUtf8Bytes(new { partyId = "party-mine" }), "corr-sdk", "actor-mine",
            null, false, null, "actor-mine", "eventstore", true, null, null);
        QueryResult result = await list.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Success.ShouldBeTrue();
        string[] ids = [.. result.GetPayload().EnumerateArray()
            .Select(item => item.GetProperty("workItemId").GetProperty("value").GetString()!)];
        ids.ShouldBe(["mine", "queue"], ignoreOrder: true);
        QueryResult hidden = await new GetWorkItemQueryHandler(store, projectionOptions, policy, coordinator)
            .ExecuteAsync(query with { AggregateId = "foreign", QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        hidden.Success.ShouldBeTrue();
        hidden.GetPayload().GetProperty("found").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Malformed_envelope_gets_durable_terminal_parking_in_sdk_generation()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions
        {
            UseSdkDelivery = true,
            MaxUndecodableEventDispatchesBeforeParking = 1,
        }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var corrupt = new SharedProjectionDelivery(
            Aggregate, 1, [0x7b], JsonSerializer.SerializeToUtf8Bytes(1),
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
        (await coordinator.JournalAsync(scope, lease, corrupt, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        ReadModelEntry<WorksRuntimeDocument> parked = await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(
            scope, 0, WorksReadModelKeys.RuntimeParkingKey(Aggregate), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        parked.Value.ShouldNotBeNull().As<WorkItemProjectionParking>().Parked.ShouldBeTrue();
        (await coordinator.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(1);
    }

    [Fact]
    public async Task Capture_stages_complete_family_and_commit_changes_stale_selection_to_current()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        IEventPayload[] events =
        [
            new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Rebuild")),
            new WorkItemQueued(Aggregate, 2, tenant, workItem),
        ];
        for (int index = 0; index < events.Length; index++)
        {
            (await coordinator.JournalAsync(scope, lease, Delivery(events[index], index + 1),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
            _ = await coordinator.CatchUpAsync(scope,
                (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        const string operation = "capture-sdk";
        _ = await coordinator.BeginAsync(scope, operation, "sealed-inventory",
            new Dictionary<string, long> { [Aggregate] = 2 }, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        SharedProjectionReadResult<WorksRuntimeDocument> stale = await coordinator.ReadAsync<WorksRuntimeDocument>(
            scope, WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken).ConfigureAwait(true);
        stale.IsAvailable.ShouldBeTrue();
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        var whatsNext = new WhatsNextQueryHandler(store,
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }), policy, coordinator);
        QueryResult rebuilding = await whatsNext.ExecuteAsync(CoordinatorQuery("scheduler"),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        rebuilding.Success.ShouldBeTrue();
        rebuilding.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Stale);
        rebuilding.Metadata.IsStale.ShouldBe(true);

        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily,
            operation, "sealed-inventory");
        DomainSharedProjectionRebuildCandidate candidate = await handler.CreateEmptyCandidateAsync(identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        ProjectionEventDto[] history = [Dto(events[0], 1), Dto(events[1], 2)];
        candidate = await handler.AccumulateAsync(identity, candidate,
            new ProjectionRequest(Tenant, "work", Aggregate, history), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        DomainProjectionRebuildPlan plan = await handler.FinalizeAsync(identity, candidate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        plan.Operations.Select(item => item.Key).ShouldContain(WorksReadModelKeys.RuntimeQueueKey);
        plan.Operations.Select(item => item.Key).ShouldContain(WorksReadModelKeys.RuntimePendingDateKey);
        plan.Operations.Select(item => item.Key).ShouldContain(WorksReadModelKeys.RuntimeTopologyKey);
        plan.Operations.Select(item => item.Key).ShouldContain(WorksReadModelKeys.RuntimeParkingKey(Aggregate));
        _ = await coordinator.StageAsync(scope, operation, plan.Operations,
            TestContext.Current.CancellationToken,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(true);
        await coordinator.CommitAsync(scope, operation, TestContext.Current.CancellationToken).ConfigureAwait(true);
        SharedProjectionReadResult<WorksRuntimeDocument> current = await coordinator.ReadAsync<WorksRuntimeDocument>(
            scope, WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken).ConfigureAwait(true);
        current.IsAvailable.ShouldBeTrue();
        current.IsStale.ShouldBeFalse();
        current.Generation.ShouldBeGreaterThan(stale.Generation);
        current.Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>().MemberWorkItemIds.ShouldBe([Aggregate]);
        QueryResult promoted = await whatsNext.ExecuteAsync(CoordinatorQuery("scheduler"),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        promoted.Success.ShouldBeTrue();
        promoted.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
    }

    [Fact]
    public async Task Rebuild_manifest_retains_cleared_date_await_tombstone()
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily,
            "pending-rebuild", "sealed-inventory");
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        DateTimeOffset due = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        AwaitCondition awaited = AwaitCondition.DateReached(due);
        ProjectionEventDto[] activeHistory =
        [
            Dto(new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Wait")), 1),
            Dto(new WorkItemSuspended(Aggregate, 2, tenant, workItem, [awaited]), 2),
        ];
        DomainSharedProjectionRebuildCandidate activeCandidate = await handler.CreateEmptyCandidateAsync(identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        activeCandidate = await handler.AccumulateAsync(identity, activeCandidate,
            new ProjectionRequest(Tenant, "work", Aggregate, activeHistory),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        DomainProjectionRebuildPlan active = await handler.FinalizeAsync(identity, activeCandidate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        PendingDateAwaitTenantIndex activeIndex = PendingIndex(active);
        activeIndex.Entries.ShouldContainKey(Aggregate);

        DomainSharedProjectionRebuildCandidate clearedCandidate = await handler.AccumulateAsync(identity,
            activeCandidate,
            new ProjectionRequest(Tenant, "work", Aggregate,
                [.. activeHistory, Dto(new WorkItemResumed(Aggregate, 3, tenant, workItem, awaited), 3)]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        DomainProjectionRebuildPlan cleared = await handler.FinalizeAsync(identity, clearedCandidate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        PendingDateAwaitTenantIndex clearedIndex = PendingIndex(cleared);
        clearedIndex.Entries.ShouldNotContainKey(Aggregate);
        clearedIndex.LastSequences[Aggregate].ShouldBe(3);
    }

    [Fact]
    public async Task Wrong_actor_tenant_and_origin_are_denied_before_any_Works_read()
    {
        IReadModelStore store = Substitute.For<IReadModelStore>();
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        var observedAt = DateTimeOffset.UtcNow;
        var binding = new HumanActorBindingEvidence(Tenant, "party-mine", "actor-mine", 1, 1,
            observedAt.AddMinutes(-1), null, "source", "provenance",
            new IdentityHistoryCustodyEvidence("policy", "party-actor-history-v1", observedAt.AddDays(1),
                1, "custody", true, true, true));
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<Hexalith.Parties.Contracts.Queries.ResolvePartyIdentity>(),
                TestContext.Current.CancellationToken)
            .Returns(Task.FromResult(new PartyIdentityResult(PartyIdentityOutcome.Resolved,
                new PartyIdentityEvidence(1, Tenant, "party-mine", PartyIdentityClassification.Human,
                    true, false, false, 2, observedAt, "observation", binding))));
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        QueryEnvelope authorizedShape = new(Tenant, "work", Aggregate, WhatsNextQueryHandler.WhatsNextQueryType,
            JsonSerializer.SerializeToUtf8Bytes(new { partyId = "party-mine" }), "corr-sdk", "actor-mine",
            null, false, null, "actor-mine", "eventstore", true, null, null);
        QueryEnvelope[] denied =
        [
            authorizedShape with { UserId = "actor-other" },
            authorizedShape with { TenantId = "tenant-other" },
            authorizedShape with { AuthenticatedWorkloadId = "untrusted-origin" },
            authorizedShape with { IsDelegated = false },
        ];
        foreach (QueryEnvelope query in denied)
        {
            QueryResult list = await new WhatsNextQueryHandler(store, options, policy)
                .ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
            list.Success.ShouldBeFalse();
            list.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
            QueryResult detail = await new GetWorkItemQueryHandler(store, options, policy)
                .ExecuteAsync(query with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
            detail.Success.ShouldBeFalse();
            detail.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        }

        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Restart_recovers_checkpoint_and_rejects_conflicting_redelivery()
    {
        var store = new InMemoryReadModelStore();
        var first = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await first.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        SharedProjectionDelivery created = Delivery(
            new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Restart")), 1);
        (await first.JournalAsync(scope, lease, created, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);

        // A fresh coordinator instance sees the same durable journal and completes it.
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        (await restarted.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, restarted, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(1);
        lease = await restarted.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        SharedProjectionDelivery queued = Delivery(new WorkItemQueued(Aggregate, 2, tenant, workItem), 2);
        (await restarted.JournalAsync(scope, lease, queued, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        SharedProjectionDelivery conflict = Delivery(new WorkItemCreated(
            Aggregate, 2, tenant, workItem, new Obligation("Conflicting identity")), 2);
        (await restarted.JournalAsync(scope, lease, conflict, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.IdentityConflict);
        (await restarted.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, restarted, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
        SharedProjectionReadResult<WorksRuntimeDocument> queue = await restarted.ReadAsync<WorksRuntimeDocument>(
            scope, WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken).ConfigureAwait(true);
        queue.Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>().Items[Aggregate].Status
            .ShouldBe(WorkItemStatus.Queued);
    }

    [Fact]
    public async Task Aborted_concurrent_rebuild_keeps_old_generation_and_allows_live_delivery_afterward()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        SharedProjectionDelivery created = Delivery(
            new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Abort")), 1);
        (await coordinator.JournalAsync(scope, lease, created, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        _ = await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        const string operation = "abort-sdk";
        _ = await coordinator.BeginAsync(scope, operation, "sealed-inventory",
            new Dictionary<string, long> { [Aggregate] = 1 }, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        SharedProjectionDelivery queued = Delivery(new WorkItemQueued(Aggregate, 2, tenant, workItem), 2);
        (await coordinator.JournalAsync(scope, lease, queued, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.StaleLease);
        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await coordinator.JournalAsync(scope, lease, queued, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        await coordinator.AbortAsync(scope, operation,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        SharedProjectionReadResult<WorksRuntimeDocument> restored = await coordinator.ReadAsync<WorksRuntimeDocument>(
            scope, WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken).ConfigureAwait(true);
        restored.IsAvailable.ShouldBeTrue();
        restored.IsStale.ShouldBeFalse();
        restored.Generation.ShouldBe(0);
        restored.Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>().Items[Aggregate].Status
            .ShouldBe(WorkItemStatus.Queued);
        (await coordinator.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
    }

    [Fact]
    public async Task Gapped_capture_is_rejected_before_a_generation_can_be_staged()
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily,
            "gap-rebuild", "sealed-inventory");
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        DomainSharedProjectionRebuildCandidate candidate = await handler.CreateEmptyCandidateAsync(identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        candidate = await handler.AccumulateAsync(identity, candidate,
            new ProjectionRequest(Tenant, "work", Aggregate,
            [
                Dto(new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Gap")), 1),
                Dto(new WorkItemQueued(Aggregate, 3, tenant, workItem), 3),
            ]), TestContext.Current.CancellationToken).ConfigureAwait(true);
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await handler.FinalizeAsync(identity, candidate, TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task Published_sdk_journals_a_gap_but_Works_fold_parks_it_without_advancing_visible_queue()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions
        {
            UseSdkDelivery = true,
            MaxUndecodableEventDispatchesBeforeParking = 1,
        }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(Aggregate);
        (await coordinator.JournalAsync(scope, lease,
            Delivery(new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Gap")), 1),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        _ = await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await coordinator.JournalAsync(scope, lease,
            Delivery(new WorkItemQueued(Aggregate, 3, tenant, workItem), 3),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> read = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope,
                [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeParkingKey(Aggregate)],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        WorksWhatsNextTenantIndex queue = read.Value.ShouldNotBeNull()[WorksReadModelKeys.RuntimeQueueKey]!
            .As<WorksWhatsNextTenantIndex>();
        queue.MemberWorkItemIds.ShouldContain(Aggregate);
        queue.Items.ShouldNotContainKey(Aggregate);
        queue.LastSequences[Aggregate].ShouldBe(1);
        read.Value[WorksReadModelKeys.RuntimeParkingKey(Aggregate)]!
            .As<WorkItemProjectionParking>().Parked.ShouldBeTrue();
        (await coordinator.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(3);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryResult result = await new WhatsNextQueryHandler(store,
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }), policy, coordinator)
            .ExecuteAsync(CoordinatorQuery("scheduler"), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        result.Success.ShouldBeTrue();
        result.GetPayload().GetArrayLength().ShouldBe(0);
        result.Metadata.ShouldNotBeNull().IsDegraded.ShouldBe(true);
    }

    [Fact]
    public async Task ConcurrentNamedDeliveriesForTwoAggregatesSurviveRebuildPromotionRestartAndRedelivery()
    {
        string? endpoint = Environment.GetEnvironmentVariable("WORKS_SDK_DAPR_GRPC_ENDPOINT");
        using DaprClient? client = string.IsNullOrWhiteSpace(endpoint) ? null
            : new DaprClientBuilder().UseGrpcEndpoint(endpoint).Build();
        var memory = new InMemoryReadModelStore();
        var durable = client is null ? null : new DaprReadModelStore(client, Options.Create(new ReadModelBatchOptions()));
        IReadModelStore store = durable is null ? memory : durable;
        IReadModelBatchStore batches = durable is null ? memory : durable;
        var coordinator = new SharedProjectionEpochCoordinator(store, batches);
        string tenantId = client is null ? Tenant : "works-sdk-overlap-" + Guid.NewGuid().ToString("N");
        var tenant = new TenantId(tenantId);
        var parentId = new WorkItemId("parent");
        var childId = new WorkItemId("child");
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        var handler = new WorkItemSharedProjectionRebuildHandler(options);
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        var identity = new DomainProjectionIdentityOptions { AppId = "works-overlap-test", ServiceVersion = "v1" };
        string fingerprint = ProjectionRouteCatalogFingerprint.Compute(identity.AppId, identity.ServiceVersion,
            [new ProjectionDispatchRoute("work", WorksReadModelKeys.RuntimeFamily)]);
        ProjectionRequest[] captured = [
            new(tenantId, "work", "parent", [
                Dto(new WorkItemCreated("parent", 1, tenant, parentId, new Obligation("Parent")), 1),
                Dto(new WorkItemQueued("parent", 2, tenant, parentId), 2)]),
            new(tenantId, "work", "child", [
                Dto(new WorkItemCreated("child", 1, tenant, childId, new Obligation("Child"),
                    Parent: new ParentWorkItemReference(tenant, parentId)), 1),
                Dto(new WorkItemQueued("child", 2, tenant, childId), 2)]),
        ];
        using ServiceProvider provider = NamedProjectionProvider(store, batches, coordinator, handler);
        foreach (ProjectionRequest request in captured)
        {
            ProjectionDispatchResponse response = await DomainProjectionDispatcher.DispatchAsync(provider,
                new ProjectionDispatchRequest(request, [WorksReadModelKeys.RuntimeFamily], $"seed-{request.AggregateId}", fingerprint),
                new ProjectionDispatchOptions(), identity, TestContext.Current.CancellationToken).ConfigureAwait(true);
            response.Outcomes.ShouldHaveSingleItem().Status.ShouldBe(ProjectionDispatchStatus.Completed);
        }

        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryEnvelope query = CoordinatorQuery("scheduler") with { TenantId = tenantId };
        var whatsNext = new WhatsNextQueryHandler(store, options, policy, coordinator);
        const string rebuild = "two-aggregate-rebuild";
        _ = await coordinator.BeginAsync(scope, rebuild, fingerprint,
            new Dictionary<string, long> { ["parent"] = 2, ["child"] = 2 },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        QueryResult duringCapture = await whatsNext.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
        duringCapture.Success.ShouldBeTrue();
        duringCapture.GetPayload().GetArrayLength().ShouldBe(2);
        duringCapture.Metadata.ShouldNotBeNull().IsStale.ShouldBe(true);
        duringCapture.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Stale);
        string? previousGeneration = duringCapture.Metadata.ProjectionVersion;

        DateTimeOffset due = DateTimeOffset.UnixEpoch.AddDays(10);
        ProjectionDispatchRequest[] concurrent = [
            new(new ProjectionRequest(tenantId, "work", "parent", [Dto(new WorkItemAssigned("parent", 3, tenant, parentId,
                new ExecutorBinding(new PartyId("party-parent"), Channel.Mcp, AuthorityLevel.Coordinate)), 3)]),
                [WorksReadModelKeys.RuntimeFamily], "live-parent", fingerprint),
            new(new ProjectionRequest(tenantId, "work", "child", [Dto(new WorkItemSuspended("child", 3, tenant, childId,
                [AwaitCondition.DateReached(due)]), 3)]),
                [WorksReadModelKeys.RuntimeFamily], "live-child", fingerprint),
        ];
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ProjectionDispatchResponse>[] dispatches = [.. concurrent.Select(request => Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            return await DomainProjectionDispatcher.DispatchAsync(provider, request, new ProjectionDispatchOptions(), identity,
                TestContext.Current.CancellationToken).ConfigureAwait(false);
        }, TestContext.Current.CancellationToken))];
        start.SetResult();
        ProjectionDispatchResponse[] admitted = await Task.WhenAll(dispatches).ConfigureAwait(true);
        admitted.ShouldAllBe(response => response.Outcomes.Count == 1
            && response.Outcomes[0].Status == ProjectionDispatchStatus.Completed);
        (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).PendingDeliveryCount.ShouldBe(2);
        foreach (string aggregate in new[] { "parent", "child" })
        {
            (await coordinator.GetCheckpointAsync(scope, aggregate, TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!.Position.ShouldBe(3);
        }

        WorksWhatsNextTenantIndex serving = (await coordinator.ReadAsync<WorksRuntimeDocument>(scope,
            WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken).ConfigureAwait(true))
            .Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>();
        serving.LastSequences["parent"].ShouldBe(2);
        serving.LastSequences["child"].ShouldBe(2);
        QueryResult staleList = await whatsNext.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
        staleList.Success.ShouldBeTrue();
        staleList.GetPayload().GetArrayLength().ShouldBe(2);
        staleList.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Stale);
        staleList.Metadata.ProjectionVersion.ShouldBe(previousGeneration);
        QueryResult staleDetail = await new GetWorkItemQueryHandler(store, options, policy, coordinator)
            .ExecuteAsync(query with { AggregateId = "parent", QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        staleDetail.Success.ShouldBeTrue();
        staleDetail.GetPayload().GetProperty("found").GetBoolean().ShouldBeTrue();
        staleDetail.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Stale);
        staleDetail.Metadata.ProjectionVersion.ShouldBe(previousGeneration);

        var rebuildIdentity = new DomainSharedProjectionRebuildIdentity(tenantId, "work", WorksReadModelKeys.RuntimeFamily,
            rebuild, fingerprint);
        DomainSharedProjectionRebuildCandidate candidate = await handler.CreateEmptyCandidateAsync(rebuildIdentity,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        foreach (ProjectionRequest request in captured)
        {
            candidate = await handler.AccumulateAsync(rebuildIdentity, candidate, request,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        DomainProjectionRebuildPlan plan = await handler.FinalizeAsync(rebuildIdentity, candidate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await coordinator.StageAsync(scope, rebuild, plan.Operations, TestContext.Current.CancellationToken,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(true);
        await coordinator.CommitAsync(scope, rebuild, TestContext.Current.CancellationToken).ConfigureAwait(true);

        using DaprClient? restartedClient = client is null ? null : new DaprClientBuilder().UseGrpcEndpoint(endpoint!).Build();
        var restartedStore = restartedClient is null ? null
            : new DaprReadModelStore(restartedClient, Options.Create(new ReadModelBatchOptions()));
        IReadModelStore currentStore = restartedStore is null ? store : restartedStore;
        IReadModelBatchStore currentBatches = restartedStore is null ? batches : restartedStore;
        var restarted = new SharedProjectionEpochCoordinator(currentStore, currentBatches);
        var currentQuery = new WhatsNextQueryHandler(currentStore, options, policy, restarted);
        QueryResult awaitingCatchUp = await currentQuery.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
        awaitingCatchUp.Success.ShouldBeFalse();
        awaitingCatchUp.ErrorMessage.ShouldBe("projection-unavailable");
        awaitingCatchUp.Metadata.ShouldNotBeNull().IsStale.ShouldBe(true);
        awaitingCatchUp.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        QueryResult unavailableDetail = await new GetWorkItemQueryHandler(currentStore, options, policy, restarted)
            .ExecuteAsync(query with { AggregateId = "parent", QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        unavailableDetail.Success.ShouldBeFalse();
        unavailableDetail.ErrorMessage.ShouldBe("projection-unavailable");
        unavailableDetail.Metadata.ShouldNotBeNull().IsStale.ShouldBe(true);
        unavailableDetail.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        (await restarted.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, restarted, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(2);

        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            .. new[] { "parent", "child" }.SelectMany(aggregate => new[] { WorksReadModelKeys.RuntimeRollUpKey(aggregate),
                WorksReadModelKeys.RuntimeHistoryKey(aggregate), WorksReadModelKeys.RuntimeParkingKey(aggregate) })];
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> family = await restarted
            .ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken).ConfigureAwait(true);
        family.IsAvailable.ShouldBeTrue();
        family.IsStale.ShouldBeFalse();
        family.Generation.ShouldBe(1);
        family.Value.ShouldNotBeNull().Values.ShouldAllBe(document => document != null);
        WorksWhatsNextTenantIndex queue = family.Value[WorksReadModelKeys.RuntimeQueueKey]!.As<WorksWhatsNextTenantIndex>();
        queue.MemberWorkItemIds.ShouldBe(["child", "parent"]);
        queue.Items.ShouldHaveSingleItem().Value.Status.ShouldBe(WorkItemStatus.Assigned);
        queue.Items.ShouldContainKey("parent");
        family.Value[WorksReadModelKeys.RuntimeTopologyKey]!.As<WorksRuntimeTopology>().ChildrenByParent["parent"].ShouldBe(["child"]);
        PendingDateAwaitTenantIndex pending = family.Value[WorksReadModelKeys.RuntimePendingDateKey]!.As<PendingDateAwaitTenantIndex>();
        pending.Entries["child"].ShouldHaveSingleItem().Instant.ShouldBe(due);
        pending.LastSequences["child"].ShouldBe(3);
        foreach ((string aggregate, WorkItemStatus status) in new[] { ("parent", WorkItemStatus.Assigned), ("child", WorkItemStatus.Suspended) })
        {
            queue.LastSequences[aggregate].ShouldBe(3);
            WorkItemRollUp rollUp = family.Value[WorksReadModelKeys.RuntimeRollUpKey(aggregate)]!.As<WorkItemRollUp>();
            rollUp.Status.ShouldBe(status);
            rollUp.LatestAcceptedSourceSequence.ShouldBe(3);
            family.Value[WorksReadModelKeys.RuntimeHistoryKey(aggregate)]!.As<WorkItemSharedRebuildAggregateHistory>().Events
                .Select(item => item.SequenceNumber).ShouldBe([1L, 2L, 3L]);
            family.Value[WorksReadModelKeys.RuntimeParkingKey(aggregate)]!.As<WorkItemProjectionParking>().Parked.ShouldBeFalse();
            (await restarted.GetCheckpointAsync(scope, aggregate, TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!.Position.ShouldBe(3);
        }

        QueryResult current = await currentQuery.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
        current.Success.ShouldBeTrue();
        current.GetPayload().GetArrayLength().ShouldBe(1);
        current.Metadata.ShouldNotBeNull().IsStale.ShouldBe(false);
        current.Metadata.IsDegraded.ShouldBe(false);
        current.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        current.Metadata.ProjectionVersion.ShouldBe("1");
        QueryResult currentDetail = await new GetWorkItemQueryHandler(currentStore, options, policy, restarted)
            .ExecuteAsync(query with { AggregateId = "parent", QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        currentDetail.Success.ShouldBeTrue();
        currentDetail.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        currentDetail.Metadata.ProjectionVersion.ShouldBe("1");
        using ServiceProvider restartedProvider = NamedProjectionProvider(currentStore, currentBatches, restarted, handler);
        foreach (ProjectionDispatchRequest request in concurrent)
        {
            ProjectionDispatchResponse duplicate = await DomainProjectionDispatcher.DispatchAsync(restartedProvider, request,
                new ProjectionDispatchOptions(), identity, TestContext.Current.CancellationToken).ConfigureAwait(true);
            duplicate.Outcomes.ShouldHaveSingleItem().Status.ShouldBe(ProjectionDispatchStatus.AlreadyCompleted);
            (await restarted.GetCheckpointAsync(scope, request.Request.AggregateId, TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!.Position.ShouldBe(3);
        }

        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> repeated = await restarted
            .ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken).ConfigureAwait(true);
        repeated.IsStale.ShouldBeFalse();
        foreach (string key in keys)
        {
            repeated.Value![key]!.Value.GetRawText().ShouldBe(family.Value[key]!.Value.GetRawText());
        }

        (await restarted.GetStatusAsync(scope, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).PendingDeliveryCount.ShouldBe(0);
    }

    [Fact]
    public async Task OrdinaryDeliveryOnAnotherAggregatePreservesTerminalParkingAndDegradedQueries()
    {
        string? endpoint = Environment.GetEnvironmentVariable("WORKS_SDK_DAPR_GRPC_ENDPOINT");
        using DaprClient? client = string.IsNullOrWhiteSpace(endpoint) ? null
            : new DaprClientBuilder().UseGrpcEndpoint(endpoint).Build();
        var memory = new InMemoryReadModelStore();
        var durable = client is null ? null : new DaprReadModelStore(client, Options.Create(new ReadModelBatchOptions()));
        IReadModelStore store = durable is null ? memory : durable;
        IReadModelBatchStore batches = durable is null ? memory : durable;
        var coordinator = new SharedProjectionEpochCoordinator(store, batches);
        string tenantId = client is null ? Tenant : "works-sdk-parking-" + Guid.NewGuid().ToString("N");
        var tenant = new TenantId(tenantId);
        var projectionOptions = Options.Create(new WorksProjectionOptions
        {
            UseSdkDelivery = true,
            MaxUndecodableEventDispatchesBeforeParking = 1,
        });
        var handler = new WorkItemSharedProjectionRebuildHandler(projectionOptions);
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var parkedItem = new WorkItemId(Aggregate);
        var healthyItem = new WorkItemId("healthy");
        foreach ((IEventPayload payload, long sequence, string aggregate) in new[]
        {
            ((IEventPayload)new WorkItemCreated(Aggregate, 1, tenant, parkedItem, new Obligation("Parked")), 1L, Aggregate),
            (new WorkItemQueued(Aggregate, 2, tenant, parkedItem), 2L, Aggregate),
            (new WorkItemQueued(Aggregate, 4, tenant, parkedItem), 4L, Aggregate),
            (new WorkItemCreated("healthy", 1, tenant, healthyItem, new Obligation("Healthy")), 1L, "healthy"),
            (new WorkItemQueued("healthy", 2, tenant, healthyItem), 2L, "healthy"),
        })
        {
            (await coordinator.JournalAsync(scope, lease, Delivery(payload, sequence, aggregate, tenantId),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
            (await coordinator.CatchUpAsync(scope,
                (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
            lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        var restarted = new SharedProjectionEpochCoordinator(store, batches);
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> family = await restarted
            .ReadManyAsync<WorksRuntimeDocument>(scope,
                [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeParkingKey(Aggregate),
                    WorksReadModelKeys.RuntimeParkingKey("healthy")],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        family.IsAvailable.ShouldBeTrue();
        family.IsStale.ShouldBeFalse();
        WorkItemProjectionParking parking = family.Value![WorksReadModelKeys.RuntimeParkingKey(Aggregate)]!
            .As<WorkItemProjectionParking>();
        parking.Parked.ShouldBeTrue();
        parking.FailedSequence.ShouldBe(4);
        parking.FailureCount.ShouldBe(1);
        family.Value[WorksReadModelKeys.RuntimeParkingKey("healthy")]!
            .As<WorkItemProjectionParking>().Parked.ShouldBeFalse();
        WorksWhatsNextTenantIndex queue = family.Value[WorksReadModelKeys.RuntimeQueueKey]!.As<WorksWhatsNextTenantIndex>();
        queue.Items[Aggregate].Status.ShouldBe(WorkItemStatus.Queued);
        queue.LastSequences[Aggregate].ShouldBe(2);
        queue.Items["healthy"].Status.ShouldBe(WorkItemStatus.Queued);
        queue.LastSequences["healthy"].ShouldBe(2);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(4);
        (await restarted.GetCheckpointAsync(scope, "healthy", TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
        (await restarted.GetStatusAsync(scope, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).PendingDeliveryCount.ShouldBe(0);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryResult result = await new WhatsNextQueryHandler(store, projectionOptions, policy, restarted)
            .ExecuteAsync(CoordinatorQuery("scheduler") with { TenantId = tenantId },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Success.ShouldBeTrue();
        result.GetPayload().EnumerateArray().Select(item =>
            item.GetProperty("workItemId").GetProperty("value").GetString()).ShouldBe(["healthy"]);
        result.Metadata.ShouldNotBeNull().IsDegraded.ShouldBe(true);
        result.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Degraded);
        result.Metadata.WarningCodes.ShouldNotBeNull().ShouldContain("works-runtime-parked");
    }

    [Fact]
    public async Task Dapr_Redis_restart_recovers_staged_family_and_concurrent_delivery_when_enabled()
    {
        string? endpoint = Environment.GetEnvironmentVariable("WORKS_SDK_DAPR_GRPC_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        string tenantId = "works-sdk-live-" + Guid.NewGuid().ToString("N");
        var tenant = new TenantId(tenantId);
        var workItem = new WorkItemId(Aggregate);
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        using DaprClient firstClient = new DaprClientBuilder().UseGrpcEndpoint(endpoint).Build();
        var firstStore = new DaprReadModelStore(firstClient, Options.Create(new ReadModelBatchOptions()));
        var first = new SharedProjectionEpochCoordinator(firstStore, firstStore);
        SharedProjectionLease lease = await first.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        IEventPayload created = new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Persisted"));
        (await first.JournalAsync(scope, lease, Delivery(created, 1, tenantId: tenantId),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await first.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, first, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        _ = await first.BeginAsync(scope, "redis-rebuild", "sealed-inventory",
            new Dictionary<string, long> { [Aggregate] = 1 }, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        lease = await first.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await first.JournalAsync(scope, lease, Delivery(new WorkItemQueued(Aggregate, 2, tenant, workItem), 2,
                tenantId: tenantId), TestContext.Current.CancellationToken).ConfigureAwait(true))
            .ShouldBe(SharedProjectionJournalResult.Journaled);

        var identity = new DomainSharedProjectionRebuildIdentity(tenantId, "work", WorksReadModelKeys.RuntimeFamily,
            "redis-rebuild", "sealed-inventory");
        DomainSharedProjectionRebuildCandidate candidate = await handler.CreateEmptyCandidateAsync(identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        candidate = await handler.AccumulateAsync(identity, candidate,
            new ProjectionRequest(tenantId, "work", Aggregate, [Dto(created, 1)]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        DomainProjectionRebuildPlan plan = await handler.FinalizeAsync(identity, candidate,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await first.StageAsync(scope, "redis-rebuild", plan.Operations,
            TestContext.Current.CancellationToken,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(true);
        await first.CommitAsync(scope, "redis-rebuild", TestContext.Current.CancellationToken).ConfigureAwait(true);

        using DaprClient restartedClient = new DaprClientBuilder().UseGrpcEndpoint(endpoint).Build();
        var restartedStore = new DaprReadModelStore(restartedClient, Options.Create(new ReadModelBatchOptions()));
        var restarted = new SharedProjectionEpochCoordinator(restartedStore, restartedStore);
        (await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).IsStale.ShouldBeTrue();
        (await restarted.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, restarted, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> family = await restarted
            .ReadManyAsync<WorksRuntimeDocument>(scope,
                [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(Aggregate),
                    WorksReadModelKeys.RuntimeTopologyKey, WorksReadModelKeys.RuntimePendingDateKey,
                    WorksReadModelKeys.RuntimeParkingKey(Aggregate), WorksReadModelKeys.RuntimeHistoryKey(Aggregate)],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        family.IsAvailable.ShouldBeTrue();
        family.IsStale.ShouldBeFalse();
        family.Generation.ShouldBe(1);
        family.Value.ShouldNotBeNull().Values.ShouldAllBe(value => value != null);
        family.Value[WorksReadModelKeys.RuntimeQueueKey]!.As<WorksWhatsNextTenantIndex>()
            .Items[Aggregate].Status.ShouldBe(WorkItemStatus.Queued);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
    }

    private static ServiceProvider NamedProjectionProvider(IReadModelStore store, IReadModelBatchStore batches,
        SharedProjectionEpochCoordinator coordinator, IAsyncDomainProjectionHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(batches);
        services.AddSingleton(coordinator);
        services.AddSingleton(handler);
        return services.BuildServiceProvider();
    }

    private static QueryEnvelope CoordinatorQuery(string workload)
        => new(
            Tenant,
            "work",
            Aggregate,
            WhatsNextQueryHandler.WhatsNextQueryType,
            [],
            "corr-sdk",
            workload,
            null,
            false,
            null,
            null,
            workload,
            false,
            null,
            null);

    private static SharedProjectionDelivery Delivery(
        IEventPayload payload, long sequence, string aggregate = Aggregate, string tenantId = Tenant)
    {
        ProjectionEventDto dto = Dto(payload, sequence);
        return new SharedProjectionDelivery(
            aggregate,
            sequence,
            JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(tenantId, "work", aggregate, [dto]), s_web),
            JsonSerializer.SerializeToUtf8Bytes(sequence),
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
    }

    private static ProjectionEventDto Dto(IEventPayload payload, long sequence)
        => new(payload.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            "json", sequence, DateTimeOffset.UnixEpoch, "corr-sdk", GlobalPosition: sequence);

    private static PendingDateAwaitTenantIndex PendingIndex(DomainProjectionRebuildPlan plan)
        => JsonSerializer.Deserialize<WorksRuntimeDocument>(plan.Operations.Single(operation =>
                operation.Key == WorksReadModelKeys.RuntimePendingDateKey).CanonicalValue.Span, s_web)
            .ShouldNotBeNull().As<PendingDateAwaitTenantIndex>();
}
