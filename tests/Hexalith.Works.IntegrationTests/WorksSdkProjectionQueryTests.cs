using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Discovery;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public async Task HostDiscoveryPreservesSharedRebuildAndAdvertisesOnlySelectedOrdinaryWriter(bool? sdkMode)
    {
        if (sdkMode == false)
        {
            Should.Throw<InvalidOperationException>(() => WorksHost.Build(["--Works:Projection:UseSdkDelivery=false"]));
            return;
        }
        await using WebApplication app = WorksHost.Build([
            .. sdkMode.HasValue ? new[] { $"--Works:Projection:UseSdkDelivery={sdkMode}" } : Array.Empty<string>(),
            "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999",
            "--Works:Recovery:RunReconciliationOnStartup=false",
        ]);
        using IServiceScope scope = app.Services.CreateScope();
        IAsyncDomainProjectionHandler[] handlers = [.. scope.ServiceProvider.GetServices<IAsyncDomainProjectionHandler>()];
        WorkItemSharedProjectionRebuildHandler shared = handlers.OfType<WorkItemSharedProjectionRebuildHandler>().ShouldHaveSingleItem();
        shared.ShouldBeAssignableTo<IAsyncDomainSharedProjectionRebuildHandler>();
        shared.OrdinaryDeliveryEnabled.ShouldBeTrue();
        shared.EpochEnabled.ShouldBeTrue();
        shared.ProjectionType.ShouldBe(WorksReadModelKeys.RuntimeFamily);
        AdminOperationalIndexMetadata.Response metadata = AdminOperationalIndexMetadata.Create(
            app.Services.GetRequiredService<DiscoveryResult>(), ["work"], [], handlers, "works", "v1", new ProjectionDispatchOptions());
        AdminOperationalIndexMetadata.DomainMetadata domain = metadata.Domains.Single();
        domain.NamedProjectionTypes.ShouldNotBeNull().ShouldBe([shared.ProjectionType]);
        domain.OrdinaryProjectionTypes.ShouldNotBeNull().ShouldBe([WorksReadModelKeys.RuntimeFamily]);
        ProjectionDispatchRoute route = new("work", shared.ProjectionType);
        metadata.CatalogFingerprint.ShouldBe(ProjectionRouteCatalogFingerprint.Compute("works", "v1", [route],
            [route]));
        RouteEndpoint[] routes = [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()];
        routes.ShouldContain(route => route.RoutePattern.RawText == "/project/rebuild/shared/v1");
    }

    [Fact]
    public async Task Sdk_host_maps_one_named_projection_route_and_the_canonical_legacy_fallback()
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
        scope.ServiceProvider.GetServices<IDomainProjectionHandler>().ShouldBeEmpty();
        ProjectionResponse? legacy = DomainProjectionDispatcher.Project(scope.ServiceProvider,
            new ProjectionRequest(Tenant, "work", Aggregate,
                [Dto(new WorkItemCreated(Aggregate, 1, new TenantId(Tenant), new WorkItemId(Aggregate),
                    new Obligation("legacy replay")), 1)]), TestContext.Current.CancellationToken);
        legacy.ShouldBeNull("The canonical /project fallback must have no legacy Works handler capable of echoing computed state.");
    }

    /// <summary>Stale named redelivery cannot replace a newer view or resurrect a terminal queue entry.</summary>
    /// <param name="terminal">Whether a terminal event removed the work item from the queue.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleNamedRedeliveryReturnsExplicitOutcomeAndPreservesAuthoritativeQueryState(bool terminal)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        var scope = handler.CreateScope(Tenant);
        var identity = new DomainProjectionIdentityOptions { AppId = "works-stale-replay-test", ServiceVersion = "v1" };
        string fingerprint = ProjectionRouteCatalogFingerprint.Compute(identity.AppId, identity.ServiceVersion,
            [new ProjectionDispatchRoute("work", WorksReadModelKeys.RuntimeFamily)]);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(Aggregate);
        ProjectionRequest created = new(Tenant, "work", Aggregate,
            [Dto(new WorkItemCreated(Aggregate, 1, tenant, item, new Obligation("authoritative query")), 1)]);
        ProjectionRequest queued = new(Tenant, "work", Aggregate,
            [Dto(new WorkItemQueued(Aggregate, 2, tenant, item), 2)]);
        ProjectionRequest cancelled = new(Tenant, "work", Aggregate,
            [Dto(new WorkItemCancelled(Aggregate, 3, tenant, item), 3)]);
        WorkItemStatus expectedStatus = terminal ? WorkItemStatus.Cancelled : WorkItemStatus.Queued;
        long expectedSequence = terminal ? 3 : 2;
        using (ServiceProvider provider = NamedProjectionProvider(store, store, coordinator, handler))
        {
            foreach (ProjectionRequest request in terminal ? new[] { created, queued, cancelled } : new[] { created, queued })
            {
                ProjectionDispatchResponse response = await DomainProjectionDispatcher.DispatchAsync(provider,
                    new ProjectionDispatchRequest(request, [WorksReadModelKeys.RuntimeFamily], $"seed-{request.Events[0].SequenceNumber}", fingerprint),
                    new ProjectionDispatchOptions(), identity, ct);
                ProjectionDispatchOutcome outcome = response.Outcomes.ShouldHaveSingleItem();
                outcome.Status.ShouldBe(ProjectionDispatchStatus.Completed);
                outcome.State.ShouldBeNull();
            }
        }
        var before = await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey(Aggregate), ct);
        before.Value.ShouldNotBeNull().As<WorkItemRollUp>().Status.ShouldBe(expectedStatus);
        string persisted = before.Value.Value.GetRawText();
        var queueBefore = await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct);
        string persistedQueue = queueBefore.Value.ShouldNotBeNull().Value.GetRawText();
        var historyBefore = await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeHistoryKey(Aggregate), ct);
        int pageCount = historyBefore.Value.ShouldNotBeNull().As<WorksRuntimeHistoryManifest>().PageCount;
        pageCount.ShouldBeGreaterThan(0);
        string[] familyKeys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(Aggregate),
            WorksReadModelKeys.RuntimeTopologyKey, WorksReadModelKeys.RuntimePendingDateKey,
            WorksReadModelKeys.RuntimeParkingKey(Aggregate), WorksReadModelKeys.RuntimeHistoryKey(Aggregate),
            .. Enumerable.Range(0, pageCount).Select(page => WorksReadModelKeys.RuntimeHistoryPageKey(Aggregate, page))];
        var familyBefore = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, familyKeys, ct);
        familyBefore.IsAvailable.ShouldBeTrue();
        familyBefore.Value.ShouldNotBeNull().Values.ShouldAllBe(document => document != null);

        var restarted = new SharedProjectionEpochCoordinator(store, store);
        var restartedHandler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        using ServiceProvider restartedProvider = NamedProjectionProvider(store, store, restarted, restartedHandler);
        ProjectionDispatchResponse replay = await DomainProjectionDispatcher.DispatchAsync(restartedProvider,
            new ProjectionDispatchRequest(terminal ? queued : created, [WorksReadModelKeys.RuntimeFamily], "older-history-after-restart", fingerprint),
            new ProjectionDispatchOptions(), identity, ct);
        ProjectionDispatchOutcome duplicate = replay.Outcomes.ShouldHaveSingleItem();
        duplicate.Status.ShouldBe(ProjectionDispatchStatus.AlreadyCompleted);
        duplicate.State.ShouldBeNull();
        var after = await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey(Aggregate), ct);
        after.Generation.ShouldBe(before.Generation);
        after.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(persisted);
        (await restarted.GetCheckpointAsync(scope, Aggregate, ct)).ShouldNotBeNull().Position.ShouldBe(expectedSequence);
        var queueAfter = await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct);
        queueAfter.Generation.ShouldBe(queueBefore.Generation);
        queueAfter.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(persistedQueue);
        WorksWhatsNextTenantIndex index = queueAfter.Value.As<WorksWhatsNextTenantIndex>();
        index.Items.ContainsKey(Aggregate).ShouldBe(!terminal);
        index.LastSequences[Aggregate].ShouldBe(expectedSequence);
        index.MemberWorkItemIds.ShouldContain(Aggregate);
        var familyAfter = await restarted.ReadManyAsync<WorksRuntimeDocument>(scope, familyKeys, ct);
        familyAfter.IsAvailable.ShouldBeTrue();
        familyAfter.Generation.ShouldBe(familyBefore.Generation);
        foreach (string key in familyKeys)
        {
            familyAfter.Value.ShouldNotBeNull()[key].ShouldNotBeNull().Value.GetRawText()
                .ShouldBe(familyBefore.Value[key]!.Value.GetRawText());
        }
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryResult queue = await new WhatsNextQueryHandler(policy, restarted).ExecuteAsync(CoordinatorQuery("scheduler"), ct);
        queue.Success.ShouldBeTrue();
        queue.GetPayload().GetArrayLength().ShouldBe(terminal ? 0 : 1);
        QueryResult result = await new GetWorkItemQueryHandler(policy, restarted).ExecuteAsync(
            CoordinatorQuery("scheduler") with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType }, ct);
        result.Success.ShouldBeTrue();
        WorkItemView view = result.GetPayload().Deserialize<WorkItemView>(s_web).ShouldNotBeNull();
        view.Found.ShouldBeTrue();
        view.Status.ShouldBe(expectedStatus);
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
        var whatsNext = new WhatsNextQueryHandler(policy, coordinator);
        QueryResult permitted = await whatsNext.ExecuteAsync(CoordinatorQuery("scheduler"),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        permitted.Success.ShouldBeTrue();
        permitted.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        permitted.GetPayload().GetArrayLength().ShouldBe(1);

        QueryResult denied = await new WhatsNextQueryHandler(policy)
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
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PartyIdentityResult(PartyIdentityOutcome.Resolved,
                new PartyIdentityEvidence(1, Tenant, "party-mine", PartyIdentityClassification.Human,
                    true, false, false, 2, observedAt, "observation", binding))));
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        var list = new WhatsNextQueryHandler(policy, coordinator);
        QueryEnvelope query = new(Tenant, "work", "mine", WhatsNextQueryHandler.WhatsNextQueryType,
            JsonSerializer.SerializeToUtf8Bytes(new { partyId = "party-mine" }), "corr-sdk", "actor-mine",
            null, false, null, "actor-mine", "eventstore", true, null, null);
        QueryResult result = await list.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Success.ShouldBeTrue();
        string[] ids = [.. result.GetPayload().EnumerateArray()
            .Select(item => item.GetProperty("workItemId").GetProperty("value").GetString()!)];
        ids.ShouldBe(["mine", "queue"], ignoreOrder: true);
        QueryResult hidden = await new GetWorkItemQueryHandler(policy, coordinator)
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
        var whatsNext = new WhatsNextQueryHandler(policy, coordinator);
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
                Arg.Any<CancellationToken>())
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
            QueryResult list = await new WhatsNextQueryHandler(policy)
                .ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
            list.Success.ShouldBeFalse();
            list.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
            QueryResult detail = await new GetWorkItemQueryHandler(policy)
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
        // Durable restart against a real Dapr store is proved by the Platform live projection and recovery lanes;
        // this deterministic fact keeps the same coordinator/handler semantics over one persisted in-memory store.
        var memory = new InMemoryReadModelStore();
        IReadModelStore store = memory;
        IReadModelBatchStore batches = memory;
        string tenantId = Tenant;
        var first = new SharedProjectionEpochCoordinator(store, batches);
        var handler = new WorkItemSharedProjectionRebuildHandler(
            Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        SharedProjectionLease lease = await first.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(tenantId);
        var workItem = new WorkItemId(Aggregate);
        SharedProjectionDelivery created = Delivery(
            new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Restart")), 1, tenantId: tenantId);
        (await first.JournalAsync(scope, lease, created, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);

        // A fresh coordinator instance sees the same durable journal and completes it.
        var restarted = new SharedProjectionEpochCoordinator(store, batches);
        (await restarted.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, restarted, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(1);
        lease = await restarted.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        SharedProjectionDelivery queued = Delivery(new WorkItemQueued(Aggregate, 2, tenant, workItem), 2, tenantId: tenantId);
        (await restarted.JournalAsync(scope, lease, queued, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        SharedProjectionDelivery conflict = Delivery(new WorkItemCreated(
            Aggregate, 2, tenant, workItem, new Obligation("Conflicting identity")), 2, tenantId: tenantId);
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

        // Conflict readiness is SDK-owned; no domain parking writer is involved.
        var afterRestart = new SharedProjectionEpochCoordinator(store, batches);
        SharedProjectionEpochStatus status = await afterRestart.GetStatusAsync(scope,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        status.PendingDeliveryCount.ShouldBe(0);
        status.IsBuilding.ShouldBeFalse();
        status.IsDegraded.ShouldBeTrue();
        status.DeliveryConflicts.Count.ShouldBe(1);
        status.DeliveryConflicts.Single().SourceStream.ShouldBe(Aggregate);
        status.DeliveryConflicts.Single().EnvelopePosition.ShouldBe(2);
        queue.IsDegraded.ShouldBeTrue();
        (await afterRestart.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeParkingKey(Aggregate),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull()
            .As<WorkItemProjectionParking>().Parked.ShouldBeFalse();
        lease = await afterRestart.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        (await afterRestart.JournalAsync(scope, lease, conflict, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.IdentityConflict);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryEnvelope coordinatorQuery = CoordinatorQuery("scheduler") with { TenantId = tenantId };
        QueryResult query = await new WhatsNextQueryHandler(policy, afterRestart)
            .ExecuteAsync(coordinatorQuery, TestContext.Current.CancellationToken).ConfigureAwait(true);
        query.Success.ShouldBeFalse();
        query.Metadata.ShouldNotBeNull().IsDegraded.ShouldBe(true);
        query.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        QueryResult detail = await new GetWorkItemQueryHandler(policy, afterRestart)
            .ExecuteAsync(coordinatorQuery with { AggregateId = Aggregate, QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        detail.Success.ShouldBeFalse();
        detail.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
    }

    [Fact]
    public async Task NamedOwnerSequenceGapKeepsDurableReadinessDegradedUntilMissingDeliveryConverges()
    {
        // Durable restart against a real Dapr store is proved by the Platform live projection and recovery lanes;
        // this deterministic fact keeps the same coordinator/handler semantics over one persisted in-memory store.
        var memory = new InMemoryReadModelStore();
        IReadModelStore store = memory;
        IReadModelBatchStore batches = memory;
        var coordinator = new SharedProjectionEpochCoordinator(store, batches);
        string tenantId = Tenant;
        var tenant = new TenantId(tenantId);
        var item = new WorkItemId(Aggregate);
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        var handler = new WorkItemSharedProjectionRebuildHandler(options);
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        var identity = new DomainProjectionIdentityOptions { AppId = "works-gap-test", ServiceVersion = "v1" };
        string fingerprint = ProjectionRouteCatalogFingerprint.Compute(identity.AppId, identity.ServiceVersion,
            [new ProjectionDispatchRoute("work", WorksReadModelKeys.RuntimeFamily)]);
        ProjectionDispatchRequest Request(IEventPayload payload, long sequence) => new(
            new ProjectionRequest(tenantId, "work", Aggregate, [Dto(payload, sequence)]),
            [WorksReadModelKeys.RuntimeFamily], $"gap-{sequence}", fingerprint);
        using ServiceProvider provider = NamedProjectionProvider(store, batches, coordinator, handler);
        var first = Request(new WorkItemCreated(Aggregate, 1, tenant, item, new Obligation("Gap")), 1);
        (await DomainProjectionDispatcher.DispatchAsync(provider, first, new ProjectionDispatchOptions(), identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Outcomes.Single().Status.ShouldBe(ProjectionDispatchStatus.Completed);
        var future = Request(new WorkItemAssigned(Aggregate, 3, tenant, item,
            new ExecutorBinding(new PartyId("party-mine"), Channel.Mcp, AuthorityLevel.Coordinate)), 3);
        (await DomainProjectionDispatcher.DispatchAsync(provider, future, new ProjectionDispatchOptions(), identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Outcomes.Single().Status.ShouldBe(ProjectionDispatchStatus.Retryable);
        // A restarted coordinator instance re-reads the same persisted store.
        IReadModelStore currentStore = store;
        IReadModelBatchStore currentBatches = batches;
        var restarted = new SharedProjectionEpochCoordinator(currentStore, currentBatches);
        using ServiceProvider currentProvider = NamedProjectionProvider(currentStore, currentBatches, restarted, handler);
        var status = await restarted.GetStatusAsync(scope, TestContext.Current.CancellationToken).ConfigureAwait(true);
        status.IsDegraded.ShouldBeTrue();
        status.PendingDeliveryCount.ShouldBe(0);
        status.DeliveryConflicts.ShouldHaveSingleItem().RequiredOwnerSequenceNumber.ShouldBe(3);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken).ConfigureAwait(true))!
            .OwnerSequenceNumber.ShouldBe(1);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryEnvelope query = CoordinatorQuery("scheduler") with { TenantId = tenantId, AggregateId = Aggregate };
        var list = new WhatsNextQueryHandler(policy, restarted);
        var detail = new GetWorkItemQueryHandler(policy, restarted);
        (await list.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true))
            .Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        (await detail.ExecuteAsync(query with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        var missing = Request(new WorkItemQueued(Aggregate, 2, tenant, item), 2);
        (await DomainProjectionDispatcher.DispatchAsync(currentProvider, missing, new ProjectionDispatchOptions(), identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Outcomes.Single().Status.ShouldBe(ProjectionDispatchStatus.Completed);
        (await restarted.GetStatusAsync(scope, TestContext.Current.CancellationToken).ConfigureAwait(true)).IsDegraded.ShouldBeTrue();
        (await DomainProjectionDispatcher.DispatchAsync(currentProvider, future, new ProjectionDispatchOptions(), identity,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Outcomes.Single().Status.ShouldBe(ProjectionDispatchStatus.Completed);
        var resolved = await restarted.GetStatusAsync(scope, TestContext.Current.CancellationToken).ConfigureAwait(true);
        resolved.IsDegraded.ShouldBeFalse();
        resolved.DeliveryConflicts.Single().ResolvedByOwnerSequenceNumber.ShouldBe(3);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken).ConfigureAwait(true))!
            .OwnerSequenceNumber.ShouldBe(3);
        (await list.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true))
            .Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        (await detail.ExecuteAsync(query with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
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

    /// <summary>Successful immutable replay removes its failed attempt and duplicate receipts never refold or revive it.</summary>
    [Fact]
    public async Task SuccessfulReplayClearsDeliveryFailureAndNewFailuresStartIndependently()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(Aggregate);
        SharedProjectionDelivery created = Delivery(new WorkItemCreated(Aggregate, 1, tenant, item, new Obligation("Replay")), 1);
        (await coordinator.JournalAsync(scope, lease, created, ct)).ShouldBe(SharedProjectionJournalResult.Journaled);
        // Controlled transient fold failure at the SDK seam; the domain payload remains byte-identical throughout.
        Task<IReadOnlyList<ReadModelBatchOperation>> FailAsync(SharedProjectionDelivery delivery, long _, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            throw new SharedProjectionFoldFailureException(delivery.SourceStream, delivery.EnvelopePosition, 5,
                _ => throw new InvalidOperationException("A retryable attempt must not request parking mutations."));
        }
        SharedProjectionRetryableFoldException failed = await Should.ThrowAsync<SharedProjectionRetryableFoldException>(() =>
            coordinator.CatchUpAsync(scope, FailAsync, ct));
        failed.Failure.FailureCount.ShouldBe(1);
        coordinator = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionEpochStatus pending = await coordinator.GetStatusAsync(scope, ct);
        pending.PendingFoldFailures.ShouldHaveSingleItem().FailureCount.ShouldBe(1);
        pending.PendingDeliveryCount.ShouldBe(1);
        pending.ParkingEvidence.ShouldBeEmpty();
        int folds = 0;
        async Task<IReadOnlyList<ReadModelBatchOperation>> FoldAsync(SharedProjectionDelivery delivery, long generation, CancellationToken token)
        {
            folds++;
            return await handler.FoldCatchUpAsync(delivery, generation, coordinator, token).ConfigureAwait(true);
        }
        (await coordinator.CatchUpAsync(scope, FoldAsync, ct)).ShouldBe(1);
        folds.ShouldBe(1);
        coordinator = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionEpochStatus complete = await coordinator.GetStatusAsync(scope, ct);
        complete.PendingFoldFailures.ShouldBeEmpty();
        complete.PendingDeliveryCount.ShouldBe(0);
        complete.ParkingEvidence.ShouldBeEmpty();
        complete.IsDegraded.ShouldBeFalse();
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeHistoryKey(Aggregate),
            WorksReadModelKeys.RuntimeRollUpKey(Aggregate), WorksReadModelKeys.RuntimeParkingKey(Aggregate),
            WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey];
        var before = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, ct);
        before.IsAvailable.ShouldBeTrue();
        before.Value.ShouldNotBeNull()[WorksReadModelKeys.RuntimeParkingKey(Aggregate)]!.As<WorkItemProjectionParking>()
            .FailureCount.ShouldBe(0);
        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct);
        (await coordinator.JournalAsync(scope, lease, created, ct)).ShouldBe(SharedProjectionJournalResult.AlreadyJournaled);
        (await coordinator.CatchUpAsync(scope, FoldAsync, ct)).ShouldBe(0);
        folds.ShouldBe(1);
        var after = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, ct);
        after.IsAvailable.ShouldBeTrue();
        after.Generation.ShouldBe(before.Generation);
        foreach (string key in keys)
            after.Value.ShouldNotBeNull()[key]!.Value.GetRawText().ShouldBe(before.Value[key]!.Value.GetRawText());
        (await coordinator.GetStatusAsync(scope, ct)).PendingFoldFailures.ShouldBeEmpty();
        (await coordinator.GetCheckpointAsync(scope, Aggregate, ct)).ShouldNotBeNull().Position.ShouldBe(1);
        (await coordinator.JournalAsync(scope, lease, Delivery(new WorkItemQueued(Aggregate, 2, tenant, item), 2), ct))
            .ShouldBe(SharedProjectionJournalResult.Journaled);
        _ = await Should.ThrowAsync<SharedProjectionRetryableFoldException>(() => coordinator.CatchUpAsync(scope, FailAsync, ct));
        coordinator = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionFailureStatus next = (await coordinator.GetStatusAsync(scope, ct)).PendingFoldFailures.ShouldHaveSingleItem();
        next.SourcePosition.ShouldBe(2);
        next.FailureCount.ShouldBe(1);
        SharedProjectionEpochStatus retryable = await coordinator.GetStatusAsync(scope, ct);
        retryable.ActiveGeneration.ShouldBe(after.Generation);
        retryable.ParkingEvidence.ShouldBeEmpty();
        foreach (string key in keys)
            (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, after.Generation, key, ct)).Value
                .ShouldNotBeNull().Value.GetRawText().ShouldBe(after.Value![key]!.Value.GetRawText());
        (await coordinator.CatchUpAsync(scope, FoldAsync, ct)).ShouldBe(1);
        coordinator = new SharedProjectionEpochCoordinator(store, store);
        (await coordinator.GetStatusAsync(scope, ct)).PendingFoldFailures.ShouldBeEmpty();
        (await coordinator.GetStatusAsync(scope, ct)).ParkingEvidence.ShouldBeEmpty();
        (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct))
            .Value.ShouldNotBeNull().As<WorksWhatsNextTenantIndex>().Items[Aggregate].Status.ShouldBe(WorkItemStatus.Queued);
    }

    /// <summary>Unknown and gapped ordinary deliveries park without publishing partial state or source watermarks.</summary>
    /// <param name="unknown">Whether to inject an unknown event instead of a missing source sequence.</param>
    /// <param name="defaultPolicy">Whether to qualify every retry of the default parking policy.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvalidOrdinaryEvidenceParksWithoutAdvancingVisibleState(bool unknown, bool defaultPolicy)
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var projectionOptions = defaultPolicy ? new WorksProjectionOptions() : new WorksProjectionOptions
        {
            UseSdkDelivery = true,
            MaxUndecodableEventDispatchesBeforeParking = 1,
        };
        int parkingLimit = projectionOptions.MaxUndecodableEventDispatchesBeforeParking;
        if (defaultPolicy) parkingLimit.ShouldBe(5);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(projectionOptions));
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
            Delivery(new WorkItemSuspended(Aggregate, 2, tenant, workItem,
                [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddDays(1))]), 2),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(Aggregate),
            WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            WorksReadModelKeys.RuntimeHistoryKey(Aggregate)];
        var before = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken);
        byte[] historyBefore = WorkItemRuntimeHistoryStore.Serialize(await WorkItemRuntimeHistoryStore.ReadAsync(
            coordinator, scope, before.Generation, Aggregate, TestContext.Current.CancellationToken));
        before.Value.ShouldNotBeNull()[WorksReadModelKeys.RuntimePendingDateKey].ShouldNotBeNull()
            .As<PendingDateAwaitTenantIndex>().Entries[Aggregate].ShouldHaveSingleItem();
        long sequence = unknown ? 3 : 4;
        ProjectionEventDto dto = Dto(new WorkItemQueued(Aggregate, sequence, tenant, workItem), sequence);
        if (unknown) dto = dto with { EventTypeName = "FutureWorksEvent" };
        var invalid = new SharedProjectionDelivery(Aggregate, sequence,
            JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(Tenant, "work", Aggregate, [dto]), s_web),
            JsonSerializer.SerializeToUtf8Bytes(sequence), new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
        (await coordinator.JournalAsync(scope, lease, invalid,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        long admittedCheckpoint = (await coordinator.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken))!
            .Position;
        admittedCheckpoint.ShouldBe(sequence);
        for (int attempt = 1; attempt < parkingLimit; attempt++)
        {
            await Should.ThrowAsync<SharedProjectionRetryableFoldException>(() => coordinator.CatchUpAsync(scope,
                (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);
            coordinator = new SharedProjectionEpochCoordinator(store, store);
            SharedProjectionEpochStatus retry = await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken);
            retry.PendingDeliveryCount.ShouldBe(1);
            retry.IsDegraded.ShouldBeTrue();
            retry.ParkingEvidence.ShouldBeEmpty();
            SharedProjectionFailureStatus failure = retry.PendingFoldFailures.ShouldHaveSingleItem();
            failure.SourceStream.ShouldBe(Aggregate);
            failure.SourcePosition.ShouldBe(sequence);
            failure.FailureCount.ShouldBe(attempt);
            (await coordinator.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken))!
                .Position.ShouldBe(admittedCheckpoint);
            var retained = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken);
            retained.Generation.ShouldBe(before.Generation);
            retained.IsAvailable.ShouldBeFalse();
            retained.Value.ShouldBeNull();
            foreach (string key in keys)
                (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, before.Generation, key,
                    TestContext.Current.CancellationToken)).Value.ShouldNotBeNull().Value.GetRawText()
                    .ShouldBe(before.Value.ShouldNotBeNull()[key].ShouldNotBeNull().Value.GetRawText());
            WorkItemRuntimeHistoryStore.Serialize(await WorkItemRuntimeHistoryStore.ReadAsync(coordinator, scope,
                retained.Generation, Aggregate, TestContext.Current.CancellationToken)).ShouldBe(historyBefore);
            var retryPolicy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
                Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
            QueryResult retryQueue = await new WhatsNextQueryHandler(retryPolicy, coordinator).ExecuteAsync(
                CoordinatorQuery("scheduler"), TestContext.Current.CancellationToken);
            QueryResult retryDetail = await new GetWorkItemQueryHandler(retryPolicy, coordinator).ExecuteAsync(
                CoordinatorQuery("scheduler") with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken);
            foreach (QueryResult refused in new[] { retryQueue, retryDetail })
            {
                refused.Success.ShouldBeFalse();
                refused.ErrorMessage.ShouldBe("projection-unavailable");
                refused.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
                refused.Metadata.IsDegraded.ShouldBe(true);
            }
        }
        (await coordinator.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> read = await restarted
            .ReadManyAsync<WorksRuntimeDocument>(scope,
                [.. keys, WorksReadModelKeys.RuntimeParkingKey(Aggregate)],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        read.Generation.ShouldBe(before.Generation);
        WorkItemRuntimeHistoryStore.Serialize(await WorkItemRuntimeHistoryStore.ReadAsync(restarted, scope,
            read.Generation, Aggregate, TestContext.Current.CancellationToken)).ShouldBe(historyBefore);
        foreach (string key in keys)
            read.Value.ShouldNotBeNull()[key].ShouldNotBeNull().Value.GetRawText()
                .ShouldBe(before.Value.ShouldNotBeNull()[key].ShouldNotBeNull().Value.GetRawText());
        WorksWhatsNextTenantIndex queue = read.Value.ShouldNotBeNull()[WorksReadModelKeys.RuntimeQueueKey]!
            .As<WorksWhatsNextTenantIndex>();
        queue.MemberWorkItemIds.ShouldContain(Aggregate);
        queue.Items.ShouldNotContainKey(Aggregate);
        queue.LastSequences[Aggregate].ShouldBe(2);
        read.Value[WorksReadModelKeys.RuntimePendingDateKey].ShouldNotBeNull()
            .As<PendingDateAwaitTenantIndex>().LastSequences[Aggregate].ShouldBe(2);
        WorkItemProjectionParking parking = read.Value[WorksReadModelKeys.RuntimeParkingKey(Aggregate)]!
            .As<WorkItemProjectionParking>();
        parking.Parked.ShouldBeTrue();
        parking.FailedSequence.ShouldBe(sequence);
        parking.FailureCount.ShouldBe(parkingLimit);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(sequence);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryResult result = await new WhatsNextQueryHandler(policy, restarted)
            .ExecuteAsync(CoordinatorQuery("scheduler"), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("projection-unavailable");
        result.Metadata.ShouldNotBeNull().IsDegraded.ShouldBe(true);
        result.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        result.Metadata.WarningCodes.ShouldNotBeNull().ShouldContain("works-runtime-unavailable");
        QueryResult detail = await new GetWorkItemQueryHandler(policy, restarted).ExecuteAsync(
            CoordinatorQuery("scheduler") with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        detail.Success.ShouldBeFalse();
        detail.ErrorMessage.ShouldBe("projection-unavailable");
        detail.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
    }

    [Fact]
    public async Task ConcurrentNamedDeliveriesForTwoAggregatesSurviveRebuildPromotionRestartAndRedelivery()
    {
        // Durable restart against a real Dapr store is proved by the Platform live projection and recovery lanes;
        // this deterministic fact keeps the same coordinator/handler semantics over one persisted in-memory store.
        var memory = new InMemoryReadModelStore();
        IReadModelStore store = memory;
        IReadModelBatchStore batches = memory;
        var coordinator = new SharedProjectionEpochCoordinator(store, batches);
        string tenantId = Tenant;
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
        var whatsNext = new WhatsNextQueryHandler(policy, coordinator);
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
            && response.Outcomes[0].Status == ProjectionDispatchStatus.Retryable);
        (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).PendingDeliveryCount.ShouldBe(0);
        foreach (string aggregate in new[] { "parent", "child" })
        {
            (await coordinator.GetCheckpointAsync(scope, aggregate, TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!.Position.ShouldBe(2);
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
        QueryResult staleDetail = await new GetWorkItemQueryHandler(policy, coordinator)
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
        await coordinator.CommitAsync(scope, rebuild, TestContext.Current.CancellationToken,
            requireOwnerSequenceHandoff: true).ConfigureAwait(true);

        // A restarted coordinator instance re-reads the same persisted store.
        IReadModelStore currentStore = store;
        IReadModelBatchStore currentBatches = batches;
        var restarted = new SharedProjectionEpochCoordinator(currentStore, currentBatches);
        var currentQuery = new WhatsNextQueryHandler(policy, restarted);
        QueryResult awaitingCatchUp = await currentQuery.ExecuteAsync(query, TestContext.Current.CancellationToken).ConfigureAwait(true);
        awaitingCatchUp.Success.ShouldBeFalse();
        awaitingCatchUp.ErrorMessage.ShouldBe("projection-unavailable");
        awaitingCatchUp.Metadata.ShouldNotBeNull().IsStale.ShouldBe(true);
        awaitingCatchUp.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        QueryResult unavailableDetail = await new GetWorkItemQueryHandler(policy, restarted)
            .ExecuteAsync(query with { AggregateId = "parent", QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        unavailableDetail.Success.ShouldBeFalse();
        unavailableDetail.ErrorMessage.ShouldBe("projection-unavailable");
        unavailableDetail.Metadata.ShouldNotBeNull().IsStale.ShouldBe(true);
        unavailableDetail.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        // The corrected SDK fences owner-sequenced delivery during capture. No receipt or checkpoint
        // advances until promotion hands the captured owner positions to the new generation.
        foreach (string aggregate in new[] { "parent", "child" })
            await restarted.SeedCapturedOwnerSequenceAsync(scope, rebuild, aggregate, 2,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        await restarted.CompleteOwnerSequenceHandoffAsync(scope, rebuild, TestContext.Current.CancellationToken).ConfigureAwait(true);
        using ServiceProvider currentProvider = NamedProjectionProvider(currentStore, currentBatches, restarted, handler);
        foreach (ProjectionDispatchRequest request in concurrent)
        {
            ProjectionDispatchResponse retry = await DomainProjectionDispatcher.DispatchAsync(currentProvider, request,
                new ProjectionDispatchOptions(), identity, TestContext.Current.CancellationToken).ConfigureAwait(true);
            retry.Outcomes.ShouldHaveSingleItem().Status.ShouldBe(ProjectionDispatchStatus.Completed);
        }
        (await restarted.CatchUpAsync(scope,
            (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, restarted, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(0);

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
            (await WorkItemRuntimeHistoryStore.ReadAsync(restarted, scope, family.Generation, aggregate,
                TestContext.Current.CancellationToken)).Events.Select(item => item.SequenceNumber).ShouldBe([1L, 2L, 3L]);
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
        QueryResult currentDetail = await new GetWorkItemQueryHandler(policy, restarted)
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
        // Durable restart against a real Dapr store is proved by the Platform live projection and recovery lanes;
        // this deterministic fact keeps the same coordinator/handler semantics over one persisted in-memory store.
        var memory = new InMemoryReadModelStore();
        IReadModelStore store = memory;
        IReadModelBatchStore batches = memory;
        var coordinator = new SharedProjectionEpochCoordinator(store, batches);
        string tenantId = Tenant;
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
        QueryResult result = await new WhatsNextQueryHandler(policy, restarted)
            .ExecuteAsync(CoordinatorQuery("scheduler") with { TenantId = tenantId },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("projection-unavailable");
        result.Metadata.ShouldNotBeNull().IsDegraded.ShouldBe(true);
        result.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        result.Metadata.WarningCodes.ShouldNotBeNull().ShouldContain("works-runtime-unavailable");
    }

    [Fact]
    public async Task ConflictingSourceSequenceAtNewEnvelopePositionIsDurablyParkedAcrossRestart()
    {
        // Durable restart against a real Dapr store is proved by the Platform live projection and recovery lanes;
        // this deterministic fact keeps the same coordinator/handler semantics over one persisted in-memory store.
        var memory = new InMemoryReadModelStore();
        IReadModelStore store = memory;
        IReadModelBatchStore batches = memory;
        var coordinator = new SharedProjectionEpochCoordinator(store, batches);
        string tenantId = Tenant;
        var tenant = new TenantId(tenantId);
        var workItem = new WorkItemId(Aggregate);
        var options = Options.Create(new WorksProjectionOptions
        {
            UseSdkDelivery = true,
            MaxUndecodableEventDispatchesBeforeParking = 1,
        });
        var handler = new WorkItemSharedProjectionRebuildHandler(options);
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        foreach ((IEventPayload payload, long sequence) in new[]
        {
            ((IEventPayload)new WorkItemCreated(Aggregate, 1, tenant, workItem, new Obligation("Source conflict")), 1L),
            (new WorkItemQueued(Aggregate, 2, tenant, workItem), 2L),
        })
        {
            (await coordinator.JournalAsync(scope, lease, Delivery(payload, sequence, tenantId: tenantId),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
            (await coordinator.CatchUpAsync(scope,
                (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token),
                TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
            lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(Aggregate),
            WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            WorksReadModelKeys.RuntimeHistoryKey(Aggregate), WorksReadModelKeys.RuntimeParkingKey(Aggregate)];
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> before = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken).ConfigureAwait(true);
        before.IsAvailable.ShouldBeTrue();

        // A new SDK envelope carries conflicting evidence for the already folded source sequence.
        // Unlike an SDK journal identity collision, this reaches the Works fold and its parking policy.
        ProjectionEventDto conflicting = Dto(new WorkItemCreated(Aggregate, 2, tenant, workItem,
            new Obligation("Conflicting source evidence")), 2) with
        { GlobalPosition = 3 };
        var delivery = new SharedProjectionDelivery(Aggregate, 3,
            JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(tenantId, "work", Aggregate, [conflicting]), s_web),
            JsonSerializer.SerializeToUtf8Bytes(3), new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
        (await coordinator.JournalAsync(scope, lease, delivery, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
        (await coordinator.CatchUpAsync(scope,
            (current, generation, token) => handler.FoldCatchUpAsync(current, generation, coordinator, token),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);

        // A restarted coordinator instance re-reads the same persisted store.
        IReadModelStore currentStore = store;
        IReadModelBatchStore currentBatches = batches;
        var restarted = new SharedProjectionEpochCoordinator(currentStore, currentBatches);
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> after = await restarted
            .ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken).ConfigureAwait(true);
        after.IsAvailable.ShouldBeTrue();
        after.Generation.ShouldBe(before.Generation);
        foreach (string key in keys.Where(key => key != WorksReadModelKeys.RuntimeParkingKey(Aggregate)))
        {
            after.Value![key]!.Value.GetRawText().ShouldBe(before.Value![key]!.Value.GetRawText());
        }

        WorkItemProjectionParking parking = after.Value![WorksReadModelKeys.RuntimeParkingKey(Aggregate)]!
            .As<WorkItemProjectionParking>();
        parking.Parked.ShouldBeTrue();
        parking.FailedSequence.ShouldBe(2);
        parking.FailureCount.ShouldBe(1);
        (await restarted.GetCheckpointAsync(scope, Aggregate, TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(3);
        (await restarted.GetStatusAsync(scope, TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).PendingDeliveryCount.ShouldBe(0);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        QueryResult result = await new WhatsNextQueryHandler(policy, restarted)
            .ExecuteAsync(CoordinatorQuery("scheduler") with { TenantId = tenantId },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("projection-unavailable");
        result.Metadata.ShouldNotBeNull().IsDegraded.ShouldBe(true);
        result.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        result.Metadata.WarningCodes.ShouldNotBeNull().ShouldContain("works-runtime-unavailable");
    }

    [Fact]
    public async Task Healthy_tenant_growth_and_large_source_context_converge_with_bounded_delta_writes_after_restart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        long historyBytes = 0;
        async Task DeliverAsync(IEventPayload payload, long sequence, string id)
        {
            _ = await coordinator.JournalAsync(scope, lease, Delivery(payload, sequence, id) with { OwnerSequenceNumber = sequence }, ct).ConfigureAwait(false);
            _ = await coordinator.CatchUpAsync(scope, async (delivery, generation, token) =>
            {
                var operations = await handler.FoldCatchUpAsync(delivery, generation, coordinator, token).ConfigureAwait(false);
                operations.Count.ShouldBeLessThanOrEqualTo(128);
                operations.ShouldAllBe(operation => operation.CanonicalValue.Length < 256 * 1024);
                return operations;
            }, ct).ConfigureAwait(false);
            coordinator = new SharedProjectionEpochCoordinator(store, store);
            (await coordinator.GetCheckpointAsync(scope, id, ct).ConfigureAwait(false))!.OwnerSequenceNumber.ShouldBe(sequence);
        }
        for (int index = 0; index < 64; index++)
        {
            string id = "capacity-" + index;
            await DeliverAsync(new WorkItemCreated(id, 1, new TenantId(Tenant), new WorkItemId(id), new Obligation("bounded current item")), 1, id);
            // Historical notes are valid source evidence; the current queue and roll-up remain bounded.
            await DeliverAsync(new WorkItemRescheduled(id, 2, new TenantId(Tenant), new WorkItemId(id), new WorkItemSchedule(), new string('x', 20 * 1024)), 2, id);
            historyBytes += WorkItemRuntimeHistoryStore.Serialize(await WorkItemRuntimeHistoryStore.ReadAsync(coordinator, scope, 0, id, ct)).Length;
        }
        historyBytes.ShouldBeGreaterThan(1024 * 1024);
        const string large = "large-history";
        await DeliverAsync(new WorkItemCreated(large, 1, new TenantId(Tenant), new WorkItemId(large), new Obligation("bounded current item")), 1, large);
        for (long sequence = 2; sequence <= 18; sequence++)
            await DeliverAsync(new WorkItemRescheduled(large, sequence, new TenantId(Tenant), new WorkItemId(large), new WorkItemSchedule(), new string('y', 20 * 1024)), sequence, large);
        var retained = await WorkItemRuntimeHistoryStore.ReadAsync(coordinator, scope, 0, large, ct);
        WorkItemRuntimeHistoryStore.Serialize(retained).Length.ShouldBeGreaterThan(256 * 1024);
        var queued = Delivery(new WorkItemQueued(large, 19, new TenantId(Tenant), new WorkItemId(large)), 19, large) with { OwnerSequenceNumber = 19 };
        _ = await coordinator.JournalAsync(scope, lease, queued, ct);
        _ = await coordinator.CatchUpAsync(scope, (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct);
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        var read = await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct);
        read.IsAvailable.ShouldBeTrue(); read.IsDegraded.ShouldBeFalse(); read.IsStale.ShouldBeFalse();
        read.Value!.As<WorksWhatsNextTenantIndex>().MemberWorkItemIds.Count.ShouldBe(65);
        read.Value.As<WorksWhatsNextTenantIndex>().Items[large].Status.ShouldBe(WorkItemStatus.Queued);
        (await WorkItemRuntimeHistoryStore.ReadAsync(restarted, scope, 0, large, ct))
            .Events.Select(item => item.SequenceNumber).ShouldBe(Enumerable.Range(1, 19).Select(number => (long)number));
        (await restarted.GetCheckpointAsync(scope, large, ct))!.OwnerSequenceNumber.ShouldBe(19);
        (await restarted.JournalAsync(scope, lease, queued, ct)).ShouldBe(SharedProjectionJournalResult.AlreadyJournaled);
        (await restarted.GetStatusAsync(scope, ct)).PendingDeliveryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Earlier_inline_history_remains_readable_and_is_paged_by_the_next_ordinary_delivery()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        var scope = handler.CreateScope(Tenant);
        _ = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        var created = new WorkItemCreated(Aggregate, 1, new TenantId(Tenant), new WorkItemId(Aggregate), new Obligation("inline compatibility"));
        var history = new WorkItemSharedRebuildAggregateHistory(Aggregate, [Dto(created, 1)]);
        var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily, "inline-compat", "inventory");
        _ = await coordinator.BeginAsync(scope, identity.OperationId, identity.CatalogFingerprint,
            new Dictionary<string, long> { [Aggregate] = 1 }, ct);
        var candidate = await handler.CreateEmptyCandidateAsync(identity, ct);
        candidate = await handler.AccumulateAsync(identity, candidate, new ProjectionRequest(Tenant, "work", Aggregate, history.Events), ct);
        var plan = await handler.FinalizeAsync(identity, candidate, ct);
        IReadOnlyList<ReadModelBatchOperation> inline = [.. plan.Operations.Where(operation =>
            operation.Key != WorksReadModelKeys.RuntimeHistoryKey(Aggregate)
            && operation.Key != WorksReadModelKeys.RuntimeHistoryPageKey(Aggregate, 0)),
            ReadModelBatchOperation.Write(WorksReadModelKeys.RuntimeHistoryKey(Aggregate), WorksRuntimeDocument.From(history), ReadModelBatchConcurrency.LastWrite)];
        _ = await coordinator.StageAsync(scope, identity.OperationId, inline, ct);
        await coordinator.CommitAsync(scope, identity.OperationId, ct);
        await coordinator.SeedCapturedOwnerSequenceAsync(scope, identity.OperationId, Aggregate, 1, ct);
        coordinator = new SharedProjectionEpochCoordinator(store, store);
        var lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct);
        (await coordinator.JournalAsync(scope, lease, Delivery(new WorkItemQueued(Aggregate, 2, new TenantId(Tenant), new WorkItemId(Aggregate)), 2)
            with
        { OwnerSequenceNumber = 2 }, ct)).ShouldBe(SharedProjectionJournalResult.Journaled);
        _ = await coordinator.CatchUpAsync(scope, (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct);
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        (await WorkItemRuntimeHistoryStore.ReadAsync(restarted, scope, 1, Aggregate, ct)).Events.Select(item => item.SequenceNumber).ShouldBe([1L, 2L]);
        (await restarted.ReadGenerationAsync<WorksRuntimeDocument>(scope, 1, WorksReadModelKeys.RuntimeHistoryKey(Aggregate), ct))
            .Value!.Kind.ShouldBe(typeof(WorksRuntimeHistoryManifest).FullName);
        (await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct)).IsDegraded.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_context_capacity_is_retryable_degraded_and_preserves_accepted_family_without_parking(bool currentDocument)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions
        { UseSdkDelivery = true, MaxUndecodableEventDispatchesBeforeParking = 1 }));
        var scope = handler.CreateScope(Tenant);
        var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        _ = await coordinator.JournalAsync(scope, lease, Delivery(new WorkItemCreated(Aggregate, 1,
            new TenantId(Tenant), new WorkItemId(Aggregate), new Obligation("accepted")), 1), ct);
        _ = await coordinator.CatchUpAsync(scope, (d, g, token) => handler.FoldCatchUpAsync(d, g, coordinator, token), ct);
        const string large = "capacity-boundary";
        IEventPayload rejected = currentDocument
            ? new WorkItemSuspended(Aggregate, 2, new TenantId(Tenant), new WorkItemId(Aggregate),
                [.. Enumerable.Range(0, 3000).Select(index => AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddMinutes(index)))])
            : new WorkItemCreated(large, 1, new TenantId(Tenant), new WorkItemId(large),
                new Obligation(new string('x', WorkItemRuntimeHistoryStore.MaxItemBytes)));
        _ = await coordinator.JournalAsync(scope, lease, Delivery(rejected, currentDocument ? 2 : 1,
            currentDocument ? Aggregate : large), ct);
        for (int attempt = 0; attempt < 2; attempt++)
            _ = await Should.ThrowAsync<SharedProjectionRetryableFoldException>(() => coordinator.CatchUpAsync(scope,
                (d, g, token) => handler.FoldCatchUpAsync(d, g, coordinator, token), ct));
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        var status = await restarted.GetStatusAsync(scope, ct);
        status.PendingDeliveryCount.ShouldBe(1); status.IsDegraded.ShouldBeTrue();
        status.PendingFoldFailures.ShouldHaveSingleItem().ReasonCode.ShouldBe("capacity");
        status.ParkingEvidence.ShouldBeEmpty();
        (await restarted.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, ct)).IsDegraded.ShouldBeTrue();
        (await restarted.ReadManyAsync<WorksRuntimeDocument>(scope, [WorksReadModelKeys.RuntimeQueueKey], ct)).IsAvailable.ShouldBeFalse();
        (await restarted.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, WorksReadModelKeys.RuntimeQueueKey, ct))
            .Value!.As<WorksWhatsNextTenantIndex>().MemberWorkItemIds.ShouldBe([Aggregate]);
        (await restarted.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, WorksReadModelKeys.RuntimeParkingKey(large), ct)).Value.ShouldBeNull();
        (await restarted.GetCheckpointAsync(scope, Aggregate, ct))!.Position.ShouldBe(currentDocument ? 2 : 1);
    }

    [Fact]
    public async Task First_delivery_parking_remains_discoverable_and_queries_unavailable_after_unrelated_delivery_and_restart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true, MaxUndecodableEventDispatchesBeforeParking = 1 });
        var handler = new WorkItemSharedProjectionRebuildHandler(options);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var scope = handler.CreateScope(Tenant);
        var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        const string bad = "bad-first";
        _ = await coordinator.JournalAsync(scope, lease, new(bad, 1, [0x7b], [1],
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)), ct);
        _ = await coordinator.CatchUpAsync(scope, (d, g, token) => handler.FoldCatchUpAsync(d, g, coordinator, token), ct);
        var evidence = (await coordinator.GetStatusAsync(scope, ct)).ParkingEvidence.ShouldHaveSingleItem();
        evidence.SourceStream.ShouldBe(bad);
        (await coordinator.GetControlIndexTenantsAsync(scope.StoreName, WorksReadModelKeys.RuntimeControlIndex, ct)).ShouldContain(Tenant);
        const string healthy = "unrelated";
        _ = await coordinator.JournalAsync(scope, lease, Delivery(new WorkItemCreated(healthy, 1,
            new TenantId(Tenant), new WorkItemId(healthy), new Obligation("healthy")), 1, healthy), ct);
        _ = await coordinator.CatchUpAsync(scope, (d, g, token) => handler.FoldCatchUpAsync(d, g, coordinator, token), ct);
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        (await restarted.GetStatusAsync(scope, ct)).ParkingEvidence.ShouldHaveSingleItem().ShouldBe(evidence);
        (await restarted.GetStatusAsync(scope, ct)).IsDegraded.ShouldBeTrue();
        (await restarted.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, WorksReadModelKeys.RuntimeParkingKey(bad), ct))
            .Value!.As<WorkItemProjectionParking>().Parked.ShouldBeTrue();
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        var query = CoordinatorQuery("scheduler") with { AggregateId = bad };
        var list = await new WhatsNextQueryHandler(policy, restarted).ExecuteAsync(query, TestContext.Current.CancellationToken);
        var detail = await new GetWorkItemQueryHandler(policy, restarted)
            .ExecuteAsync(query with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType }, TestContext.Current.CancellationToken);
        list.Metadata!.IsDegraded.ShouldBe(true); detail.Metadata!.IsDegraded.ShouldBe(true);
        list.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable); detail.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
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

    [Fact]
    public async Task Wrong_unit_progress_retains_accepted_values_and_marks_both_pinned_queries_degraded()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        var handler = new WorkItemSharedProjectionRebuildHandler(options);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var scope = handler.CreateScope(Tenant);
        var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        IEventPayload[] events = [new WorkItemCreated(Aggregate, 1, new TenantId(Tenant), new WorkItemId(Aggregate),
            new Obligation("effort"), new WorkItemEffort(5m, new Unit("hours"))),
            new WorkItemQueued(Aggregate, 2, new TenantId(Tenant), new WorkItemId(Aggregate)),
            new ProgressReported(Aggregate, 3, new TenantId(Tenant), new WorkItemId(Aggregate), 1m, new Unit("days"))];
        for (int index = 0; index < events.Length; index++)
        {
            _ = await coordinator.JournalAsync(scope, lease, Delivery(events[index], index + 1), ct);
            _ = await coordinator.CatchUpAsync(scope, (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct);
        }
        coordinator = new SharedProjectionEpochCoordinator(store, store);
        WorkItemRollUp rollUp = (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey(Aggregate), ct))
            .Value!.As<WorkItemRollUp>();
        rollUp.Degraded.ShouldBeTrue(); rollUp.OwnEffort!.Done.ShouldBe(0); rollUp.LatestAcceptedSourceSequence.ShouldBe(3);
        (await coordinator.GetCheckpointAsync(scope, Aggregate, ct))!.Position.ShouldBe(3);
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(), Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        var query = CoordinatorQuery("scheduler");
        QueryResult list = await new WhatsNextQueryHandler(policy, coordinator).ExecuteAsync(query, ct);
        QueryResult detail = await new GetWorkItemQueryHandler(policy, coordinator)
            .ExecuteAsync(query with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType }, ct);
        foreach (QueryResult result in new[] { list, detail })
        {
            result.Success.ShouldBeTrue(); result.Metadata!.IsDegraded.ShouldBe(true);
            result.Metadata.IsStale.ShouldBe(false); result.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Degraded);
            result.Metadata.WarningCodes.ShouldNotBeNull().ShouldContain("works-rollup-refused:" + rollUp.ProjectionDiagnostics.Single().EventType);
        }
        list.GetPayload().GetArrayLength().ShouldBe(1);
        detail.GetPayload().GetProperty("done").GetDecimal().ShouldBe(0);
    }

    [Fact]
    public async Task Decimal_rollup_overflow_retains_accepted_family_and_pending_non_poison_capacity_across_restart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true, MaxUndecodableEventDispatchesBeforeParking = 1 });
        var handler = new WorkItemSharedProjectionRebuildHandler(options);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var scope = handler.CreateScope(Tenant);
        var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        var parent = new WorkItemCreated(Aggregate, 1, new TenantId(Tenant), new WorkItemId(Aggregate), new Obligation("parent"), new WorkItemEffort(decimal.MaxValue, new Unit("hours")));
        _ = await coordinator.JournalAsync(scope, lease, Delivery(parent, 1), ct);
        _ = await coordinator.CatchUpAsync(scope, (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct);
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(Aggregate), WorksReadModelKeys.RuntimeHistoryKey(Aggregate)];
        var before = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, ct);
        const string childId = "overflow-child";
        var child = new WorkItemCreated(childId, 1, new TenantId(Tenant), new WorkItemId(childId), new Obligation("child"),
            new WorkItemEffort(1m, new Unit("hours")), Parent: new ParentWorkItemReference(new TenantId(Tenant), new WorkItemId(Aggregate)));
        _ = await coordinator.JournalAsync(scope, lease, Delivery(child, 1, childId), ct);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var failure = await Should.ThrowAsync<SharedProjectionRetryableFoldException>(() => coordinator.CatchUpAsync(scope,
                (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct));
            failure.Failure.ReasonCode.ShouldBe("capacity");
            coordinator = new SharedProjectionEpochCoordinator(store, store);
        }
        var status = await coordinator.GetStatusAsync(scope, ct);
        status.IsDegraded.ShouldBeTrue(); status.PendingDeliveryCount.ShouldBe(1); status.ParkingEvidence.ShouldBeEmpty();
        foreach (string key in keys)
            (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, key, ct)).Value!.Value.GetRawText()
                .ShouldBe(before.Value![key]!.Value.GetRawText());
        (await coordinator.GetCheckpointAsync(scope, Aggregate, ct))!.Position.ShouldBe(1);
        (await coordinator.GetCheckpointAsync(scope, childId, ct))!.Position.ShouldBe(1); // Journal accepted; child family never applied.
        (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, WorksReadModelKeys.RuntimeRollUpKey(childId), ct)).Value.ShouldBeNull();
        (await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, ct)).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Same_length_valid_json_history_page_corruption_rejects_next_delivery_without_rewriting_applied_family()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var scope = handler.CreateScope(Tenant);
        var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct);
        _ = await coordinator.JournalAsync(scope, lease, Delivery(new WorkItemCreated(Aggregate, 1, new TenantId(Tenant), new WorkItemId(Aggregate), new Obligation("accepted")), 1), ct);
        _ = await coordinator.CatchUpAsync(scope, (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct);
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey(Aggregate), WorksReadModelKeys.RuntimeHistoryKey(Aggregate)];
        var before = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, ct);
        string pageKey = WorksReadModelKeys.RuntimeHistoryPageKey(Aggregate, 0);
        var page = (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, pageKey, ct)).Value!.As<WorksRuntimeHistoryPage>();
        byte[] corrupted = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(page.Data).Replace("corr-sdk", "corr-bad", StringComparison.Ordinal));
        corrupted.Length.ShouldBe(page.Data.Length); corrupted.SequenceEqual(page.Data).ShouldBeFalse();
        using JsonDocument validJson = JsonDocument.Parse(corrupted);
        // Fault injection persists a corrupted logical page through the SDK writer; the manifest remains intact.
        _ = await coordinator.JournalAsync(scope, lease, new SharedProjectionDelivery("page-fault", 1, [1], [1]), ct);
        _ = await coordinator.CatchUpAsync(scope, (_, _, _) => Task.FromResult<IReadOnlyList<ReadModelBatchOperation>>([
            ReadModelBatchOperation.Write(pageKey, WorksRuntimeDocument.From(new WorksRuntimeHistoryPage(corrupted)), ReadModelBatchConcurrency.LastWrite)]), ct);
        _ = await coordinator.JournalAsync(scope, lease, Delivery(new WorkItemQueued(Aggregate, 2, new TenantId(Tenant), new WorkItemId(Aggregate)), 2), ct);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => coordinator.CatchUpAsync(scope,
                (delivery, generation, token) => handler.FoldCatchUpAsync(delivery, generation, coordinator, token), ct));
            error.Message.ShouldContain("digest");
            coordinator = new SharedProjectionEpochCoordinator(store, store);
        }
        foreach (string key in keys)
            (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, key, ct)).Value!.Value.GetRawText()
                .ShouldBe(before.Value![key]!.Value.GetRawText());
        (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, WorksReadModelKeys.RuntimeRollUpKey(Aggregate), ct))
            .Value!.As<WorkItemRollUp>().LatestAcceptedSourceSequence.ShouldBe(1);
        (await coordinator.GetCheckpointAsync(scope, Aggregate, ct))!.Position.ShouldBe(2); // Accepted journal head, not an applied model.
        (await coordinator.GetStatusAsync(scope, ct)).PendingDeliveryCount.ShouldBe(1);
        (await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, ct)).IsAvailable.ShouldBeFalse();
    }

    /// <summary>Both query adapters retain safe unavailable responses and bounded diagnostics for corrupt persisted documents.</summary>
    [Theory]
    [InlineData(false, "document")]
    [InlineData(true, "document")]
    [InlineData(false, "null-members")]
    [InlineData(true, "null-members")]
    [InlineData(false, "omitted-item")]
    [InlineData(true, "omitted-item")]
    [InlineData(false, "omitted-sequence")]
    [InlineData(true, "omitted-sequence")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    [InlineData(false, "blank")]
    [InlineData(true, "blank")]
    public async Task QueryFailureDiagnosticsAreBoundedAndExcludePrivateEvidence(bool detail, string failure)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var projection = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        var scope = projection.CreateScope(Tenant);
        var lease = await coordinator.RegisterWriterAsync(scope, projection.OrdinaryWriterId, ct);
        const string secret = "private-document-and-bearer-secret";
        bool invalidMembership = failure != "document";
        var omittedItem = new WhatsNextItem(new TenantId(Tenant), new WorkItemId(Aggregate), WorkItemStatus.Queued,
            null, null, null, null, null, [], [], 1);
        var corrupt = invalidMembership
            ? WorksRuntimeDocument.From(new WorksWhatsNextTenantIndex
            {
                SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
                MemberWorkItemIds = failure switch
                {
                    "null-members" => null!,
                    "duplicate" => [Aggregate, Aggregate],
                    "blank" => [" "],
                    _ => [],
                },
                Items = failure == "omitted-item" ? new() { [Aggregate] = omittedItem } : new(),
                LastSequences = failure == "omitted-sequence" ? new() { [Aggregate] = 1 } : new(),
            })
            : new WorksRuntimeDocument(typeof(WorksWhatsNextTenantIndex).FullName!, JsonSerializer.SerializeToElement(secret));
        _ = await coordinator.JournalAsync(scope, lease, new SharedProjectionDelivery("diagnostic-fault", 1, [1], [1]), ct);
        _ = await coordinator.CatchUpAsync(scope, (_, _, _) => Task.FromResult<IReadOnlyList<ReadModelBatchOperation>>([
            ReadModelBatchOperation.Write(WorksReadModelKeys.RuntimeQueueKey, corrupt, ReadModelBatchConcurrency.LastWrite)]), ct);
        string persistedBefore = (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(
            scope, 0, WorksReadModelKeys.RuntimeQueueKey, ct)).Value!.Value.GetRawText();
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        var detailLogger = new Story48RecordingLogger<GetWorkItemQueryHandler>();
        var queueLogger = new Story48RecordingLogger<WhatsNextQueryHandler>();
        QueryEnvelope query = CoordinatorQuery("scheduler") with
        {
            CorrelationId = "correlation\n\u2028" + new string('c', 200),
            Payload = System.Text.Encoding.UTF8.GetBytes(secret),
            QueryType = secret,
        };
        IDomainQueryHandler handler = detail
            ? new GetWorkItemQueryHandler(policy, coordinator, detailLogger)
            : new WhatsNextQueryHandler(policy, coordinator, queueLogger);

        QueryResult result = await handler.ExecuteAsync(query, ct);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("projection-unavailable");
        result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        var entry = (detail ? detailLogger.Entries : queueLogger.Entries).ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(4510);
        entry.Level.ShouldBe(Microsoft.Extensions.Logging.LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Properties["FailureCategory"].ShouldBe(invalidMembership ? "invalid-membership" : "invalid-document");
        entry.Properties["TenantId"].ShouldBe(Tenant);
        entry.Properties["QueryType"].ShouldBe(detail ? GetWorkItemQueryHandler.GetWorkItemQueryType : WhatsNextQueryHandler.WhatsNextQueryType);
        ((string)entry.Properties["CorrelationId"]!).Length.ShouldBe(128);
        entry.Message.ShouldNotContain(secret);
        entry.Message.ShouldNotContain("\n");
        entry.Message.ShouldNotContain("\u2028");
        (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, 0, WorksReadModelKeys.RuntimeQueueKey, ct))
            .Value!.Value.GetRawText().ShouldBe(persistedBefore);

        IDomainQueryHandler throwing = detail
            ? new GetWorkItemQueryHandler(policy, coordinator, new ThrowingDiagnosticLogger<GetWorkItemQueryHandler>())
            : new WhatsNextQueryHandler(policy, coordinator, new ThrowingDiagnosticLogger<WhatsNextQueryHandler>());
        (await throwing.ExecuteAsync(query, ct)).ErrorMessage.ShouldBe("projection-unavailable");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => handler.ExecuteAsync(query, cancelled.Token));
        (detail ? detailLogger.Entries : queueLogger.Entries).Count.ShouldBe(1);
        var refused = await handler.ExecuteAsync(query with { AuthenticatedWorkloadId = "unlisted" }, ct);
        refused.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        (detail ? detailLogger.Entries : queueLogger.Entries).Count.ShouldBe(1);
    }

    /// <summary>SDK-wrapped and ordinary store failures retain useful finite categories in both adapters.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task QueryDependencyFailuresUseSafeCategoriesThroughTheSdk(bool detail, bool timeout)
    {
        Exception failure = timeout ? new TimeoutException("private-provider-secret") : new IOException("private-provider-secret");
        var store = new DiagnosticUnavailableReadModelStore(failure);
        var coordinator = new SharedProjectionEpochCoordinator(store, new InMemoryReadModelStore());
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = ["scheduler"] }));
        var detailLogger = new Story48RecordingLogger<GetWorkItemQueryHandler>();
        var queueLogger = new Story48RecordingLogger<WhatsNextQueryHandler>();
        IDomainQueryHandler handler = detail ? new GetWorkItemQueryHandler(policy, coordinator, detailLogger)
            : new WhatsNextQueryHandler(policy, coordinator, queueLogger);
        QueryResult result = await handler.ExecuteAsync(CoordinatorQuery("scheduler"), TestContext.Current.CancellationToken);

        result.ErrorMessage.ShouldBe("projection-unavailable");
        var entry = (detail ? detailLogger.Entries : queueLogger.Entries).ShouldHaveSingleItem();
        entry.Properties["FailureCategory"].ShouldBe(timeout ? "timeout" : "dependency-unavailable");
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain("private-provider-secret");
        store.ReadCount.ShouldBeGreaterThan(0);
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
