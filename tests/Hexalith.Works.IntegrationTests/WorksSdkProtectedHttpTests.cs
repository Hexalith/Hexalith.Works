using System.Security.Cryptography;
using System.Security.Claims;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Recovery;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Parties.Client;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.ValueObjects;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;
using Hexalith.Works.Runtime;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises the real SDK-mode HTTP authentication pipeline.</summary>
public sealed class WorksSdkProtectedHttpTests
{
    private const string AppId = "works-sdk-http-test";
    private const string Issuer = "works-sdk-http-issuer";
    private const string ChannelToken = "works-sdk-http-channel-token";
    private const string SigningKey = "works-sdk-http-signing-key-with-32-bytes-minimum";
    private const string Tenant = "tenant-sdk";
    private const string Actor = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string HumanBearer = "works-sdk-synthetic-human-bearer";

    /// <summary>Every SDK store interface uses the request-scoped counter that protects denial assertions.</summary>
    [Fact]
    public async Task QueryObservationCountsSingleBulkAndBatchStoresThroughHostBindings()
    {
        var inner = new InMemoryReadModelStore();
        var store = new QueryWindowStore(inner);
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999");
        IReadModelStore single = app.Services.GetRequiredService<IReadModelStore>();
        IReadModelBulkStore bulk = app.Services.GetRequiredService<IReadModelBulkStore>();
        IReadModelBatchStore batch = app.Services.GetRequiredService<IReadModelBatchStore>();
        single.ShouldBeSameAs(store);
        bulk.ShouldBeSameAs(store);
        batch.ShouldBeSameAs(store);
        CancellationToken ct = TestContext.Current.CancellationToken;
        int round = 0;
        async Task ExerciseAllAsync(bool counted)
        {
            string key = "probe-" + ++round;
            int expected = store.Operations;
            void AssertCount()
            {
                expected += counted ? 1 : 0;
                store.Operations.ShouldBe(expected);
            }

            await single.GetAsync<string>("statestore", key, ct).ConfigureAwait(true);
            AssertCount();
            await single.SaveAsync("statestore", key, "saved", ct).ConfigureAwait(true);
            AssertCount();
            (await single.TrySaveAsync("statestore", key + "-conditional", "conditional", string.Empty, ct)
                .ConfigureAwait(true)).ShouldBeTrue();
            AssertCount();
            await bulk.GetManyAsync<string>("statestore", [key], 1, ct).ConfigureAwait(true);
            AssertCount();
            var manifest = new ReadModelBatch(
                new ReadModelBatchScope("statestore", Tenant, "work", "probe", "observation", key),
                [ReadModelBatchOperation.Write(key, "batched", ReadModelBatchConcurrency.LastWrite)]);
            (await batch.ExecuteAsync(manifest, ct).ConfigureAwait(true)).IsSuccess.ShouldBeTrue();
            AssertCount();
            inner.Snapshot<string>("statestore", key).ShouldBe("batched");
            inner.Snapshot<string>("statestore", key + "-conditional").ShouldBe("conditional");
        }

        await ExerciseAllAsync(counted: false).ConfigureAwait(true);
        store.Observe = true;
        await ExerciseAllAsync(counted: false).ConfigureAwait(true);
        using (QueryWindowStore.EnterRequest())
        {
            await ExerciseAllAsync(counted: true).ConfigureAwait(true);
            store.Observe = false;
            await ExerciseAllAsync(counted: false).ConfigureAwait(true);
        }
        store.Observe = true;
        await ExerciseAllAsync(counted: false).ConfigureAwait(true);
        store.Operations.ShouldBe(5);
    }

    [Fact]
    public async Task RejectedNamedProjectionIngressPreservesPersistedFamilyAndCheckpoint()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", coordinator);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId("mine");
        ProjectionDispatchRequest created = NamedDelivery(new WorkItemCreated("mine", 1, tenant, workItem,
            new Obligation("Protected delivery")), 1);
        using HttpResponseMessage accepted = await client.SendAsync(Request("/project/v2", created,
            channel: true, assertion: Assertion("domain-service:project")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimeRollUpKey("mine"),
            WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            WorksReadModelKeys.RuntimeHistoryKey("mine"), WorksReadModelKeys.RuntimeParkingKey("mine")];
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> before = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken).ConfigureAwait(true);
        before.IsAvailable.ShouldBeTrue();
        before.Value.ShouldNotBeNull().Values.ShouldAllBe(value => value != null);
        (await coordinator.GetCheckpointAsync(scope, "mine", TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(1);

        ProjectionDispatchRequest queued = NamedDelivery(new WorkItemQueued("mine", 2, tenant, workItem), 2);
        foreach ((bool channel, string? assertion, HttpStatusCode expected) in new[]
        {
            (false, Assertion("domain-service:project"), HttpStatusCode.Unauthorized),
            (true, (string?)null, HttpStatusCode.Unauthorized),
            (true, Assertion("domain-service:query"), HttpStatusCode.Forbidden),
            (true, Assertion("domain-service:project", "other-caller"), HttpStatusCode.Unauthorized),
        })
        {
            using HttpResponseMessage denied = await client.SendAsync(Request("/project/v2", queued, channel, assertion),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            denied.StatusCode.ShouldBe(expected);
            (await coordinator.GetCheckpointAsync(scope, "mine", TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!.Position.ShouldBe(1);
            SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> after = await coordinator
                .ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken).ConfigureAwait(true);
            after.IsAvailable.ShouldBeTrue();
            after.Generation.ShouldBe(before.Generation);
            foreach (string key in keys)
            {
                after.Value![key]!.Kind.ShouldBe(before.Value[key]!.Kind);
                after.Value[key]!.Value.GetRawText().ShouldBe(before.Value[key]!.Value.GetRawText());
            }

            (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).PendingDeliveryCount.ShouldBe(0);
        }
    }

    /// <summary>Invalid wakeups remain retryable and never commit discovery, reminders or completion markers.</summary>
    [Theory]
    [InlineData("foreign-domain")]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-aggregate")]
    [InlineData("reserved-tenant")]
    public async Task RefusedWakeupReturnsRetryableFailureWithoutStateWrites(string scenario)
    {
        var store = new InMemoryReadModelStore();
        var markers = new InMemoryEventStoreDomainEventMarkerStore();
        IReminderRegistrar reminders = Substitute.For<IReminderRegistrar>();
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", settings:
            ["--EventStore:Reminders:ReconciliationEnabled=false", "--EventStore:Processes:ReconciliationEnabled=false"], configure: services =>
        {
            services.RemoveAll<IEventStoreDomainEventMarkerStore>();
            services.AddSingleton<IEventStoreDomainEventMarkerStore>(markers);
            services.RemoveAll<IReminderRegistrar>();
            services.AddSingleton(reminders);
        });
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        string tenant = scenario == "reserved-tenant" ? "tenants" : Tenant;
        var created = new WorkItemCreated("wakeup", 1, new TenantId(tenant), new WorkItemId("wakeup"), new Obligation("synthetic"));
        var envelope = new EventStoreDomainEventEnvelope("01ARZ3NDEKTSV4RRFFQ69G5FAV",
            scenario == "foreign-aggregate" ? "foreign-item" : "wakeup", scenario == "foreign-tenant" ? "foreign-tenant" : tenant,
            typeof(WorkItemCreated).FullName!, 1, DateTimeOffset.UnixEpoch, "wakeup-proof", "json",
            JsonSerializer.SerializeToUtf8Bytes(created))
        { Domain = scenario == "foreign-domain" ? "foreign" : "work" };
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage response = await client.SendAsync(Request("/work/events", envelope, channel: true, assertion: null),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            store.Count.ShouldBe(0);
            reminders.ReceivedCalls().ShouldBeEmpty();
        }
        (await markers.TryAcquireAsync(envelope.MessageId, TestContext.Current.CancellationToken)).ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.Acquired);

        // Positive control on the same host: a matching committed fact is accepted and commits its discovery effect.
        var valid = new WorkItemCreated("wakeup", 1, new TenantId(Tenant), new WorkItemId("wakeup"), new Obligation("synthetic"));
        var validEnvelope = new EventStoreDomainEventEnvelope("01ARZ3NDEKTSV4RRFFQ69G5FAW", "wakeup", Tenant,
            typeof(WorkItemCreated).FullName!, 1, DateTimeOffset.UnixEpoch, "wakeup-control", "json",
            JsonSerializer.SerializeToUtf8Bytes(valid))
        { Domain = "work" };
        using (HttpResponseMessage accepted = await client.SendAsync(Request("/work/events", validEnvelope, channel: true, assertion: null),
            TestContext.Current.CancellationToken).ConfigureAwait(true))
        {
            accepted.IsSuccessStatusCode.ShouldBeTrue($"A matching wakeup must be accepted; got {(int)accepted.StatusCode}.");
        }

        using IServiceScope scope = app.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ProcessCheckpointStore>()
            .ContainsCandidateAsync(new ProcessSource(Tenant, "work", "wakeup"), TestContext.Current.CancellationToken)
            .ConfigureAwait(true)).ShouldBeTrue();
        store.Count.ShouldBeGreaterThan(0);
        _ = await reminders.Received(1).ConvergeAsync(new ReminderTarget(Tenant, "work", "wakeup"), Arg.Any<CancellationToken>()).ConfigureAwait(true);
        (await markers.TryAcquireAsync(validEnvelope.MessageId, TestContext.Current.CancellationToken)).ShouldBe(EventStoreDomainEventMarkerAcquisitionResult.Completed);
    }

    [Fact]
    public async Task PublishedPartiesTransportAuthorizesPersistedExecutorWorkWithSyntheticCurrentAuthority()
    {
        int identityRequests = 0;
        await using WebApplication parties = BuildPartiesGateway((request, context) =>
        {
            identityRequests++;
            context.Request.Headers.Authorization.ToString().ShouldBe($"Bearer {HumanBearer}");
            AssertIdentityRequest(request);
            return Results.Ok(new SubmitQueryResponse("identity", JsonSerializer.SerializeToElement(CurrentIdentity())));
        });
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await PopulateRuntimeAsync(coordinator).ConfigureAwait(true);
        using HttpClient client = PartiesClient(Address(parties), HumanBearer);
        var policy = new WorksQueryPolicy(new HttpPartiesIdentityClient(client), Options.Create(new WorksQueryOptions()));
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        QueryResult list = await new WhatsNextQueryHandler(policy, coordinator)
            .ExecuteAsync(ExecutorQuery(), TestContext.Current.CancellationToken).ConfigureAwait(true);
        list.Success.ShouldBeTrue();
        list.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        list.GetPayload().EnumerateArray().Select(item =>
            item.GetProperty("workItemId").GetProperty("value").GetString()).ShouldBe(["mine", "queue"]);

        foreach ((string aggregate, bool expected) in new[] { ("mine", true), ("queue", true), ("foreign", false) })
        {
            QueryResult detail = await new GetWorkItemQueryHandler(policy, coordinator)
                .ExecuteAsync(ExecutorQuery() with
                {
                    AggregateId = aggregate,
                    QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType,
                }, TestContext.Current.CancellationToken).ConfigureAwait(true);
            detail.Success.ShouldBeTrue();
            detail.GetPayload().GetProperty("found").GetBoolean().ShouldBe(expected);
        }

        identityRequests.ShouldBe(4);
        var projection = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = projection.CreateScope(Tenant);
        (await coordinator.GetCheckpointAsync(scope, "mine", TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
        (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull()
            .As<WorksWhatsNextTenantIndex>().MemberWorkItemIds.ShouldBe(["foreign", "mine", "queue"]);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("watermark")]
    [InlineData("tenant")]
    [InlineData("aggregate")]
    public async Task ExecutorDetailRefusesContaminatedRollupWithTruthfulUnavailableMetadata(string contamination)
    {
        await using WebApplication parties = BuildPartiesGateway((request, context) =>
        {
            context.Request.Headers.Authorization.ToString().ShouldBe($"Bearer {HumanBearer}");
            AssertIdentityRequest(request);
            return Results.Ok(new SubmitQueryResponse("identity", JsonSerializer.SerializeToElement(CurrentIdentity())));
        });
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await PopulateRuntimeAsync(coordinator).ConfigureAwait(true);
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        var projection = new WorkItemSharedProjectionRebuildHandler(options);
        SharedProjectionScope scope = projection.CreateScope(Tenant);
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey,
            WorksReadModelKeys.RuntimeTopologyKey, .. new[] { "mine", "foreign", "queue" }.SelectMany(aggregate => new[] {
                WorksReadModelKeys.RuntimeRollUpKey(aggregate), WorksReadModelKeys.RuntimeParkingKey(aggregate),
                WorksReadModelKeys.RuntimeHistoryKey(aggregate) })];
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> family = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope, keys, TestContext.Current.CancellationToken).ConfigureAwait(true);
        family.IsAvailable.ShouldBeTrue();
        WorkItemRollUp rollUp = family.Value![WorksReadModelKeys.RuntimeRollUpKey("mine")]!.As<WorkItemRollUp>();
        rollUp = contamination switch
        {
            "status" => rollUp with { Status = WorkItemStatus.Completed },
            "watermark" => rollUp with { LatestAcceptedSourceSequence = rollUp.LatestAcceptedSourceSequence + 1 },
            "tenant" => rollUp with { TenantId = new TenantId("tenant-other") },
            _ => rollUp with { WorkItemId = new WorkItemId("foreign") },
        };
        _ = await coordinator.BeginAsync(scope, "contaminated", "sealed", new Dictionary<string, long>
        {
            ["mine"] = 2,
            ["foreign"] = 2,
            ["queue"] = 2,
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        ReadModelBatchOperation[] operations = [.. family.Value.Select(pair => ReadModelBatchOperation.Write(
            pair.Key, pair.Key == WorksReadModelKeys.RuntimeRollUpKey("mine")
                ? WorksRuntimeDocument.From(rollUp) : pair.Value!, ReadModelBatchConcurrency.LastWrite))];
        _ = await coordinator.StageAsync(scope, "contaminated", operations,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await coordinator.CommitAsync(scope, "contaminated", TestContext.Current.CancellationToken).ConfigureAwait(true);

        using HttpClient client = PartiesClient(Address(parties), HumanBearer);
        var policy = new WorksQueryPolicy(new HttpPartiesIdentityClient(client), Options.Create(new WorksQueryOptions()));
        QueryResult result = await new GetWorkItemQueryHandler(policy, coordinator)
            .ExecuteAsync(ExecutorQuery() with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("projection-unavailable");
        result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        result.Metadata.IsDegraded.ShouldBe(true);
        WorkItemRollUp persisted = (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey("mine"),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull().As<WorkItemRollUp>();
        persisted.TenantId.ShouldBe(rollUp.TenantId);
        persisted.WorkItemId.ShouldBe(rollUp.WorkItemId);
        persisted.Status.ShouldBe(rollUp.Status);
        persisted.LatestAcceptedSourceSequence.ShouldBe(rollUp.LatestAcceptedSourceSequence);
    }

    [Theory]
    [InlineData("missing-bearer")]
    [InlineData("wrong-bearer")]
    [InlineData("expired-binding")]
    [InlineData("future-binding")]
    [InlineData("expired-custody")]
    [InlineData("unsafe-custody")]
    [InlineData("wrong-actor")]
    [InlineData("wrong-party")]
    [InlineData("wrong-tenant")]
    [InlineData("inactive")]
    [InlineData("restricted")]
    [InlineData("erased")]
    [InlineData("stale")]
    [InlineData("degraded")]
    public async Task PublishedIdentityTransportDeniesUnavailableAuthorityBeforeWorksStateAccess(string failure)
    {
        int identityRequests = 0;
        PartyIdentityResult identity = CurrentIdentity();
        PartyIdentityEvidence evidence = identity.Evidence!;
        HumanActorBindingEvidence binding = evidence.HumanBinding!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        evidence = failure switch
        {
            "expired-binding" => evidence with { HumanBinding = binding with { ValidUntil = now.AddMinutes(-1) } },
            "future-binding" => evidence with { HumanBinding = binding with { ValidFrom = now.AddMinutes(1) } },
            "expired-custody" => evidence with { HumanBinding = binding with { Custody = binding.Custody with { ExpiresAt = now.AddMinutes(-1) } } },
            "unsafe-custody" => evidence with { HumanBinding = binding with { Custody = binding.Custody with { RestoreSafe = false } } },
            "wrong-actor" => evidence with { HumanBinding = binding with { ActorId = "01ARZ3NDEKTSV4RRFFQ69G5FAA" } },
            "wrong-party" => evidence with { PartyId = "party-other" },
            "wrong-tenant" => evidence with { TenantId = "tenant-other" },
            "inactive" => evidence with { IsActive = false },
            "restricted" => evidence with { IsRestricted = true },
            "erased" => evidence with { IsErasingOrErased = true },
            _ => evidence,
        };
        await using WebApplication parties = BuildPartiesGateway((request, context) =>
        {
            identityRequests++;
            if (context.Request.Headers.Authorization.ToString() != $"Bearer {HumanBearer}")
            {
                return Results.Unauthorized();
            }

            AssertIdentityRequest(request);
            return Results.Ok(new SubmitQueryResponse("identity", JsonSerializer.SerializeToElement(identity with { Evidence = evidence }),
                Metadata: new QueryResponseMetadata(IsStale: failure == "stale", IsDegraded: failure == "degraded")));
        });
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        IReadModelStore store = Substitute.For<IReadModelStore>();
        using HttpClient client = PartiesClient(Address(parties),
            failure == "missing-bearer" ? null : failure == "wrong-bearer" ? "untrusted" : HumanBearer);
        var policy = new WorksQueryPolicy(new HttpPartiesIdentityClient(client), Options.Create(new WorksQueryOptions()));
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        foreach (string queryType in new[] { WhatsNextQueryHandler.WhatsNextQueryType, GetWorkItemQueryHandler.GetWorkItemQueryType })
        {
            QueryEnvelope query = ExecutorQuery() with { QueryType = queryType };
            QueryResult result = queryType == WhatsNextQueryHandler.WhatsNextQueryType
                ? await new WhatsNextQueryHandler(policy).ExecuteAsync(query,
                    TestContext.Current.CancellationToken).ConfigureAwait(true)
                : await new GetWorkItemQueryHandler(policy).ExecuteAsync(query,
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        }

        identityRequests.ShouldBe(failure == "missing-bearer" ? 0 : 2);
        store.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Default and custom gateways bind exact caller authority before reading persisted Executor work.</summary>
    /// <param name="gatewayOverride">A paired gateway override, or null to exercise both defaults.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("designated-gateway")]
    public async Task ConfiguredServiceBearerAuthorizesProtectedExecutorQueriesWithSyntheticCurrentAuthority(string? gatewayOverride)
    {
        string gateway = gatewayOverride ?? "eventstore";
        int identityRequests = 0;
        await using WebApplication parties = BuildAuthenticatedPartiesGateway((request, context) =>
        {
            identityRequests++;
            context.User.Identity.ShouldNotBeNull().IsAuthenticated.ShouldBeTrue();
            context.User.FindFirst("sub").ShouldNotBeNull().Value.ShouldBe("works-identity-reader");
            AssertIdentityRequest(request);
            return Results.Ok(new SubmitQueryResponse("identity", JsonSerializer.SerializeToElement(CurrentIdentity())));
        });
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        var store = new QueryWindowStore(new InMemoryReadModelStore());
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await PopulateRuntimeAsync(coordinator).ConfigureAwait(true);
        store.Observe = true;
        List<string> settings = [$"--Works:Query:PartiesGatewayBearerToken={ServiceBearer()}"];
        if (gatewayOverride is not null)
        {
            settings.Add($"--Works:Query:GatewayWorkloadId={gatewayOverride}");
            settings.Add($"--Authentication:QueryAuthority:GatewayWorkloadId={gatewayOverride}");
            settings.Add($"--Authentication:Workload:AllowedCallers:0={gatewayOverride}");
        }
        await using WebApplication app = BuildProtectedHost(store, Address(parties), coordinator, [.. settings]);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };

        // The protected SDK receiver sees only the gateway assertion. Its published Parties client
        // authenticates independently; neither payload flags nor the actor's bearer supply service authority.
        QueryEnvelope listQuery = ExecutorQuery() with
        {
            AuthenticatedWorkloadId = gateway,
            CallerAuthority = new QueryCallerAuthority("authenticated-caller", [], []),
        };
        using HttpResponseMessage listResponse = await client.SendAsync(Request("/query", listQuery,
            channel: true, assertion: Assertion("domain-service:query", gateway, listQuery)),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        QueryResult list = (await listResponse.Content.ReadFromJsonAsync<QueryResult>(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
        list.Success.ShouldBeTrue();
        list.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        list.GetPayload().EnumerateArray().Select(item =>
            item.GetProperty("workItemId").GetProperty("value").GetString()).ShouldBe(["mine", "queue"]);

        foreach ((string aggregate, bool expected) in new[] { ("mine", true), ("queue", true), ("foreign", false) })
        {
            QueryEnvelope detailQuery = listQuery with
            {
                AggregateId = aggregate,
                QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType,
            };
            using HttpResponseMessage detailResponse = await client.SendAsync(Request("/query", detailQuery,
                channel: true, assertion: Assertion("domain-service:query", gateway, detailQuery)),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult detail = (await detailResponse.Content.ReadFromJsonAsync<QueryResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
            detail.Success.ShouldBeTrue();
            detail.GetPayload().GetProperty("found").GetBoolean().ShouldBe(expected);
            detail.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        }

        store.Operations.ShouldBeGreaterThan(0, "The observation must detect authorized state access.");
        int authorizedOperations = store.Operations;
        using (HttpResponseMessage deniedCaller = await client.SendAsync(Request("/query", listQuery,
            channel: true, assertion: Assertion("domain-service:query", "unapproved-gateway", listQuery)),
            TestContext.Current.CancellationToken).ConfigureAwait(true))
        {
            deniedCaller.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        using (HttpResponseMessage missingDigest = await client.SendAsync(Request("/query", listQuery,
            channel: true, assertion: Assertion("domain-service:query", gateway)),
            TestContext.Current.CancellationToken).ConfigureAwait(true))
        {
            missingDigest.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult denied = (await missingDigest.Content.ReadFromJsonAsync<QueryResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
            denied.Success.ShouldBeFalse();
            denied.ErrorMessage.ShouldBe("safe-denial");
        }
        using (HttpResponseMessage deniedDigest = await client.SendAsync(Request("/query",
            listQuery with { CorrelationId = "tampered-correlation" }, channel: true,
            assertion: Assertion("domain-service:query", gateway, listQuery)),
            TestContext.Current.CancellationToken).ConfigureAwait(true))
        {
            deniedDigest.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult denied = (await deniedDigest.Content.ReadFromJsonAsync<QueryResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
            denied.Success.ShouldBeFalse();
            denied.ErrorMessage.ShouldBe("safe-denial");
        }

        identityRequests.ShouldBe(4, "Rejected caller and query bindings must not reach Parties identity resolution.");
        store.Operations.ShouldBe(authorizedOperations, "Rejected authority must not access single, bulk or batch state stores.");
        var projection = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = projection.CreateScope(Tenant);
        foreach (string aggregate in new[] { "mine", "queue", "foreign" })
        {
            (await coordinator.GetCheckpointAsync(scope, aggregate, TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!.Position.ShouldBe(2);
        }

        (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).Value.ShouldNotBeNull()
            .As<WorksWhatsNextTenantIndex>().MemberWorkItemIds.ShouldBe(["foreign", "mine", "queue"]);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("expired")]
    public async Task RejectedServiceBearerDeniesProtectedExecutorBeforeWorksStateAccess(string failure)
    {
        int identityRequests = 0;
        await using WebApplication parties = BuildAuthenticatedPartiesGateway((_, _) =>
        {
            identityRequests++;
            return Results.Ok(new SubmitQueryResponse("identity", JsonSerializer.SerializeToElement(CurrentIdentity())));
        });
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        var store = new QueryWindowStore(new InMemoryReadModelStore());
        string bearer = failure == "expired" ? ServiceBearer(expired: true) : "rejected-service-credential";
        await using WebApplication app = BuildProtectedHost(store, Address(parties), settings:
            [$"--Works:Query:PartiesGatewayBearerToken={bearer}"]);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.Observe = true;
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        foreach (string queryType in new[] { WhatsNextQueryHandler.WhatsNextQueryType, GetWorkItemQueryHandler.GetWorkItemQueryType })
        {
            using HttpResponseMessage response = await client.SendAsync(Request("/query", ExecutorQuery() with { QueryType = queryType },
                channel: true, assertion: Assertion("domain-service:query")),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult result = (await response.Content.ReadFromJsonAsync<QueryResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        }

        identityRequests.ShouldBe(0);
        store.Operations.ShouldBe(0, "A rejected or expired service bearer must not read or write Works state.");
    }

    /// <summary>Captured authority changes only when a host is replaced, even after a secret reload.</summary>
    [Fact]
    public async Task ServiceBearerRotationRequiresReplacementHostAndPreservesProjectionState()
    {
        string original = ServiceBearer(credentialId: "original");
        string replacement = ServiceBearer(credentialId: "replacement");
        using var authorityConfiguration = new ConfigurationManager();
        authorityConfiguration["Works:Query:PartiesGatewayBearerToken"] = original;
        string accepted = original;
        int identityRequests = 0;
        await using WebApplication parties = BuildAuthenticatedPartiesGateway((request, context) =>
        {
            identityRequests++;
            AssertIdentityRequest(request);
            return string.Equals(context.Request.Headers.Authorization, "Bearer " + accepted, StringComparison.Ordinal)
                ? Results.Ok(new SubmitQueryResponse("identity", JsonSerializer.SerializeToElement(CurrentIdentity())))
                : Results.Unauthorized();
        });
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        var store = new QueryWindowStore(new InMemoryReadModelStore());
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await PopulateRuntimeAsync(coordinator).ConfigureAwait(true);
        store.Observe = true;
        var projection = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = projection.CreateScope(Tenant);
        SharedProjectionReadResult<WorksRuntimeDocument> before = await coordinator.ReadAsync<WorksRuntimeDocument>(scope,
            WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken).ConfigureAwait(true);
        before.IsAvailable.ShouldBeTrue();

        await using (WebApplication originalHost = BuildProtectedHost(store, Address(parties), coordinator,
            queryConfiguration: authorityConfiguration))
        {
            await originalHost.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            await RequireQueryAsync(originalHost, true).ConfigureAwait(true);
            accepted = replacement;
            await RequireQueryAsync(originalHost, false).ConfigureAwait(true);

            // Reload an existing secret provider; a running host does not acquire new configuration sources.
            authorityConfiguration["Works:Query:PartiesGatewayBearerToken"] = replacement;
            ((IConfigurationRoot)authorityConfiguration).Reload();
            originalHost.Configuration["Works:Query:PartiesGatewayBearerToken"].ShouldBe(replacement);
            originalHost.Services.GetRequiredService<IOptions<WorksQueryOptions>>().Value
                .PartiesGatewayBearerToken.ShouldBe(original);
            await RequireQueryAsync(originalHost, false).ConfigureAwait(true);
            await originalHost.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        await using (WebApplication replacementHost = BuildProtectedHost(store, Address(parties), coordinator,
            [$"--Works:Query:PartiesGatewayBearerToken={replacement}"]))
        {
            await replacementHost.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            await RequireQueryAsync(replacementHost, true).ConfigureAwait(true);
        }

        identityRequests.ShouldBe(4);
        SharedProjectionReadResult<WorksRuntimeDocument> after = await coordinator.ReadAsync<WorksRuntimeDocument>(scope,
            WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken).ConfigureAwait(true);
        after.IsAvailable.ShouldBeTrue();
        after.Generation.ShouldBe(before.Generation);
        after.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(before.Value.ShouldNotBeNull().Value.GetRawText());
        foreach (string aggregate in new[] { "mine", "queue", "foreign" })
        {
            (await coordinator.GetCheckpointAsync(scope, aggregate, TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!.Position.ShouldBe(2);
        }

        async Task RequireQueryAsync(WebApplication host, bool allowed)
        {
            int beforeOperations = store.Operations;
            QueryEnvelope query = ExecutorQuery() with { CallerAuthority = new QueryCallerAuthority("authenticated-caller", [], []) };
            using var client = new HttpClient { BaseAddress = new Uri(Address(host)) };
            using HttpResponseMessage response = await client.SendAsync(Request("/query", query, channel: true,
                assertion: Assertion("domain-service:query", query: query)), TestContext.Current.CancellationToken).ConfigureAwait(true);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult result = (await response.Content.ReadFromJsonAsync<QueryResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
            result.Success.ShouldBe(allowed);
            if (allowed)
            {
                store.Operations.ShouldBeGreaterThan(beforeOperations, "The observation must detect authorized state access.");
                result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
                result.GetPayload().EnumerateArray().Select(item =>
                    item.GetProperty("workItemId").GetProperty("value").GetString()).ShouldBe(["mine", "queue"]);
            }
            else
            {
                result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
                store.Operations.ShouldBe(beforeOperations, "Revoked authority must deny before any single, bulk or batch state access.");
            }
        }
    }

    [Fact]
    public async Task ProtectedExecutorQueryWithoutForwardedBearerDeniesBeforeIdentityOrWorksStateAccess()
    {
        int identityRequests = 0;
        await using WebApplication parties = BuildPartiesGateway((request, _) =>
        {
            identityRequests++;
            AssertIdentityRequest(request);
            return Results.Ok(new SubmitQueryResponse("identity", JsonSerializer.SerializeToElement(CurrentIdentity())));
        });
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        IReadModelStore store = Substitute.For<IReadModelStore>();
        await using WebApplication app = BuildProtectedHost(store, Address(parties));
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.ClearReceivedCalls();
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };

        // Canonical EventStore invocation strips the human bearer. The actual Works typed client
        // registration cannot acquire authority from the workload assertion alone.
        foreach (string queryType in new[] { WhatsNextQueryHandler.WhatsNextQueryType, GetWorkItemQueryHandler.GetWorkItemQueryType })
        {
            using HttpResponseMessage response = await client.SendAsync(Request("/query",
                ExecutorQuery() with { QueryType = queryType },
                channel: true, assertion: Assertion("domain-service:query")),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult result = (await response.Content.ReadFromJsonAsync<QueryResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        }

        identityRequests.ShouldBe(0);
        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task FrozenSdkRejectsHumanBearerOnAuthenticatedQueryIngressBeforeWorksStateAccess()
    {
        IReadModelStore store = Substitute.For<IReadModelStore>();
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999");
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.ClearReceivedCalls();
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        using HttpResponseMessage response = await client.SendAsync(Request("/query", ExecutorQuery(),
            channel: true, assertion: Assertion("domain-service:query"), bearer: HumanBearer),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/query")]
    [InlineData("/query/")]
    [InlineData("/QUERY")]
    public async Task GatewayOnlyIngressAppliesToEveryQueryRouteSpelling(string route)
    {
        IReadModelStore store = Substitute.For<IReadModelStore>();
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", settings:
            ["--Works:Query:GatewayWorkloadId=designated-gateway", "--Authentication:QueryAuthority:GatewayWorkloadId=designated-gateway",
                "--Works:Query:PermittedCoordinatorWorkloads:0=scheduler"]);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.ClearReceivedCalls();
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        QueryEnvelope query = ExecutorQuery() with
        {
            IsDelegated = false,
            OriginalActorId = null,
            AuthenticatedWorkloadId = "scheduler",
        };
        using HttpResponseMessage response = await client.SendAsync(Request(route, query,
            channel: true, assertion: Assertion("domain-service:query")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_and_named_projection_require_channel_and_exact_operation_assertions()
    {
        await using WebApplication app = WorksHost.Build(
            [
                "--environment=Development",
                "--Logging:LogLevel:Default=Error",
                "--Works:Projection:UseSdkDelivery=true",
                "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999",
                "--Works:Recovery:RunReconciliationOnStartup=false",
                $"--{DaprAppChannelToken.ConfigurationKey}={ChannelToken}",
                $"--EventStore:DomainService:AppId={AppId}",
                $"--Authentication:JwtBearer:Issuer={Issuer}",
                "--Authentication:JwtBearer:Audience=hexalith-eventstore",
                $"--Authentication:JwtBearer:SigningKey={SigningKey}",
                $"--Authentication:JwtBearer:AllowedAlgorithms:0={SecurityAlgorithms.HmacSha256}",
                "--Authentication:JwtBearer:RequireHttpsMetadata=false",
            ], webHost => webHost.UseUrls("http://127.0.0.1:0"));
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        string address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>().ShouldNotBeNull().Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        QueryEnvelope query = new("tenant-sdk", "work", "work-sdk", WhatsNextQueryHandler.WhatsNextQueryType,
            [], "corr-http", "untrusted-user", null, false, null, null, "eventstore", false, null, null);

        foreach (string route in new[] { "/query", "/project/v2", "/project" })
        {
            using HttpResponseMessage missing = await client.SendAsync(Request(route, query),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            missing.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            using HttpResponseMessage channelOnly = await client.SendAsync(Request(route, query, channel: true),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            channelOnly.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            using HttpResponseMessage wrongOperation = await client.SendAsync(Request(route, query,
                channel: true, assertion: Assertion("domain-service:process")),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            wrongOperation.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using HttpResponseMessage validQuery = await client.SendAsync(Request("/query", query,
            channel: true, assertion: Assertion("domain-service:query")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        validQuery.StatusCode.ShouldBe(HttpStatusCode.OK);
        QueryResult? denied = await validQuery.Content.ReadFromJsonAsync<QueryResult>(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        denied.ShouldNotBeNull().Success.ShouldBeFalse();
        denied.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);

        using HttpResponseMessage validNamed = await client.SendAsync(Request("/project/v2", query,
            channel: true, assertion: Assertion("domain-service:project")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        validNamed.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        validNamed.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);

        // The canonical SDK has no legacy handler in named-delivery mode. Its 404 closes the
        // gateway's legacy intent, so subsequent commands can advance the named projection.
        using HttpResponseMessage legacyUnavailable = await client.SendAsync(Request("/project",
            new ProjectionRequest("tenant-sdk", "work", "work-sdk", []),
            channel: true, assertion: Assertion("domain-service:project")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        legacyUnavailable.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using HttpResponseMessage foreignOrigin = await client.SendAsync(Request("/query", query,
            channel: true, assertion: Assertion("domain-service:query", "other-caller")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        foreignOrigin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Event_delivery_and_actor_callbacks_require_the_sidecar_channel()
    {
        await using WebApplication app = WorksHost.Build(
            [
                "--environment=Development",
                "--Logging:LogLevel:Default=Error",
                "--Works:Recovery:RunReconciliationOnStartup=false",
                "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999",
                $"--{DaprAppChannelToken.ConfigurationKey}={ChannelToken}",
                $"--EventStore:DomainService:AppId={AppId}",
                $"--Authentication:JwtBearer:Issuer={Issuer}",
                "--Authentication:JwtBearer:Audience=hexalith-eventstore",
                $"--Authentication:JwtBearer:SigningKey={SigningKey}",
                $"--Authentication:JwtBearer:AllowedAlgorithms:0={SecurityAlgorithms.HmacSha256}",
                "--Authentication:JwtBearer:RequireHttpsMetadata=false",
            ], webHost => webHost.UseUrls("http://127.0.0.1:0"));
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        string address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>().ShouldNotBeNull().Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        foreach ((HttpMethod method, string route) in new[] { (HttpMethod.Post, "/work/events"), (HttpMethod.Get, "/dapr/config") })
        {
            using var missing = new HttpRequestMessage(method, route);
            using HttpResponseMessage missingResponse = await client.SendAsync(missing,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            missingResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route);

            using var wrong = new HttpRequestMessage(method, route);
            wrong.Headers.Add(DaprAppChannelToken.HeaderName, "not-the-channel-token");
            using HttpResponseMessage wrongResponse = await client.SendAsync(wrong,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            wrongResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route);

            using var valid = new HttpRequestMessage(method, route);
            valid.Headers.Add(DaprAppChannelToken.HeaderName, ChannelToken);
            using HttpResponseMessage validResponse = await client.SendAsync(valid,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            validResponse.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, route);
            validResponse.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, route);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("disabled-reader")]
    [InlineData("untrusted-reader")]
    [InlineData("inactive-actor")]
    [InlineData("expired")]
    [InlineData("missing")]
    [InlineData("binding-actor")]
    [InlineData("expired-binding")]
    [InlineData("unsafe-custody")]
    [InlineData("unavailable-custody")]
    public async Task ProtectedExecutorUsesCanonicalGatewayAndRealPlatformReaderAdmission(string? failure)
    {
        var authority = new WorksSdkPlatformAuthorityFixture(Tenant, Actor, Issuer, SigningKey, CurrentIdentity);
        await using WebApplication parties = authority.Build(failure);
        await parties.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        var persisted = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(persisted, persisted);
        await PopulateRuntimeAsync(coordinator).ConfigureAwait(true);
        IReadModelStore forbiddenStore = Substitute.For<IReadModelStore>();
        IReadModelStore store = failure is null ? persisted : forbiddenStore;
        string[] settings = failure == "missing" ? [] : [$"--Works:Query:PartiesGatewayBearerToken={ServiceBearer(failure == "expired")}"];
        await using WebApplication app = BuildProtectedHost(store, Address(parties), failure is null ? coordinator : null, settings);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        forbiddenStore.ClearReceivedCalls();
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        foreach (string queryType in new[] { WhatsNextQueryHandler.WhatsNextQueryType, GetWorkItemQueryHandler.GetWorkItemQueryType })
        {
            using HttpResponseMessage response = await client.SendAsync(Request("/query", ExecutorQuery() with { QueryType = queryType },
                channel: true, assertion: Assertion("domain-service:query")), TestContext.Current.CancellationToken).ConfigureAwait(true);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult result = (await response.Content.ReadFromJsonAsync<QueryResult>(cancellationToken: TestContext.Current.CancellationToken)
                .ConfigureAwait(true)).ShouldNotBeNull();
            result.Success.ShouldBe(failure is null);
            if (failure is null)
            {
                result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
                if (queryType == WhatsNextQueryHandler.WhatsNextQueryType)
                    result.GetPayload().EnumerateArray().Select(item => item.GetProperty("workItemId").GetProperty("value").GetString())
                        .ShouldBe(["mine", "queue"]);
                else result.GetPayload().GetProperty("found").GetBoolean().ShouldBeTrue();
            }
            else result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        }
        authority.VerifiedQueries.ShouldBe(failure is null or "binding-actor" or "expired-binding" or "unsafe-custody" or "unavailable-custody" ? 2 : 0);
        forbiddenStore.ReceivedCalls().ShouldBeEmpty();
        var projection = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        (await coordinator.GetCheckpointAsync(projection.CreateScope(Tenant), "mine", TestContext.Current.CancellationToken)
            .ConfigureAwait(true))!.Position.ShouldBe(2);
    }

    private static HttpRequestMessage Request(string route, object query, bool channel = false, string? assertion = null, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(query, options: JsonSerializerOptions.Web),
        };
        if (channel)
        {
            request.Headers.Add(DaprAppChannelToken.HeaderName, ChannelToken);
        }

        if (assertion is not null)
        {
            request.Headers.Add(EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName, assertion);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    [Theory]
    [InlineData("scheduled")]
    [InlineData("cascade")]
    [InlineData("stale")]
    [InlineData("missing-origin")]
    [InlineData("audit-outage")]
    [InlineData("purpose")]
    [InlineData("workload")]
    [InlineData("kind")]
    [InlineData("digest")]
    [InlineData("target")]
    [InlineData("caller")]
    public async Task ExpiryRequiresAuthenticatedBoundOriginAndCurrentWitness(string scenario)
    {
        var store = new InMemoryReadModelStore();
        await using WebApplication app = BuildProtectedHost(scenario == "audit-outage" ? new OriginAuditUnavailableReadModelStore(store) : store, "http://127.0.0.1:8999", settings:
            ["--Works:Recovery:UseSdkRuntime=true", "--EventStore:Processes:ReconciliationEnabled=false", "--EventStore:Reminders:ReconciliationEnabled=false"]);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId("expiry-origin");
        DateTimeOffset due = DateTimeOffset.UnixEpoch.AddDays(1);
        WorkItemExpirySchedule Witness(long revision) => new(due,
            ReminderIdentityCodec.ComputeScheduleToken(Tenant, item.Value, due, revision), revision);
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(item.Value, 1, tenant, item, new Obligation("origin proof"), Expiry: Witness(2)));
        var command = new ExpireWorkItem(tenant, item, scenario == "cascade" ? null : Witness(scenario == "stale" ? 1 : 2));
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(command);
        string kind = scenario == "cascade" ? EffectKindCatalog.CascadeExpire : scenario == "kind" ? EffectKindCatalog.DateResume : EffectKindCatalog.Expiry;
        var identity = new EffectIdentity(Tenant, "work", "source-origin", 9, kind, "work", item.Value, 0);
        string message = EffectIdentityCodec.ComputeMessageId(identity);
        string workload = scenario == "workload" ? "other-worker" : "works";
        var envelope = new CommandEnvelope(message, Tenant, "work", item.Value, nameof(ExpireWorkItem),
            payload, message, "committed-source", workload, new() { ["trusted-purpose"] = "works.cascade-expire" });
        var request = new DomainServiceRequest(envelope, state)
        {
            TrustedEffectOrigin = scenario == "missing-origin" ? null : new TrustedEffectOrigin(identity, workload,
                scenario is "purpose" or "audit-outage" ? "wrong-purpose" : WorksEffectPurposes.ForKind(kind), "committed-source", nameof(ExpireWorkItem),
                EffectIdentityCodec.RenderDigest(SHA256.HashData(payload))),
        };
        if (scenario == "digest") request = request with { Command = envelope with { Payload = [123] } };
        if (scenario == "target") request = request with { Command = envelope with { AggregateId = "foreign-target" } };
        using HttpResponseMessage response = await client.SendAsync(Request("/process", request, channel: true,
            assertion: Assertion("domain-service:process", scenario == "caller" ? "foreign-caller" : "eventstore")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        if (scenario is "scheduled" or "cascade" or "stale")
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            DomainServiceWireResult result = (await response.Content.ReadFromJsonAsync<DomainServiceWireResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true))!;
            result.IsRejection.ShouldBe(scenario == "stale");
            result.Events.Any(value => value.EventTypeName.EndsWith(nameof(WorkItemExpired), StringComparison.Ordinal)).ShouldBe(scenario != "stale");
        }
        else if (scenario == "audit-outage")
        {
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(1));
            (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
            store.Snapshot<object>("statestore", "eventstore:trusted-effect-origin:v1:denials").ShouldBeNull();
        }
        else
        {
            response.StatusCode.ShouldBe(scenario == "caller" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
            if (scenario == "caller")
                store.Snapshot<object>("statestore", "eventstore:trusted-effect-origin:v1:denials").ShouldBeNull();
            else
            {
                (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
                Dictionary<string, JsonElement> audit = store.Snapshot<Dictionary<string, JsonElement>>("statestore", "eventstore:trusted-effect-origin:v1:denials")!;
                audit.ShouldNotBeNull()["count"].GetInt64().ShouldBe(1);
                audit["reasonCode"].GetString().ShouldBe(scenario is "purpose" or "workload" or "kind" or "missing-origin"
                    ? "adapter-authorization-denied" : "origin-binding-denied");
                string safe = JsonSerializer.Serialize(audit);
                safe.ShouldNotContain("committed-source");
                safe.ShouldNotContain("expiry-origin");
                safe.ShouldNotContain(SigningKey);
            }
        }
    }

    [Theory]
    [InlineData(nameof(CancelWorkItem), EffectKindCatalog.CascadeCancel, false, HttpStatusCode.OK, false, Tenant)]
    [InlineData(nameof(CancelWorkItem), EffectKindCatalog.DateResume, false, HttpStatusCode.Forbidden, false, Tenant)]
    [InlineData(nameof(ResumeWorkItem), EffectKindCatalog.DateResume, false, HttpStatusCode.OK, false, Tenant)]
    [InlineData(nameof(ResumeWorkItem), EffectKindCatalog.ChildCompletionResume, false, HttpStatusCode.OK, false, Tenant)]
    [InlineData(nameof(ResumeWorkItem), EffectKindCatalog.Expiry, false, HttpStatusCode.Forbidden, false, Tenant)]
    [InlineData(nameof(ResumeWorkItem), EffectKindCatalog.CascadeCancel, false, HttpStatusCode.Forbidden, false, Tenant)]
    [InlineData(nameof(ExpireWorkItem), EffectKindCatalog.CascadeExpire, true, HttpStatusCode.OK, true, Tenant)]
    [InlineData(nameof(ExpireWorkItem), EffectKindCatalog.Expiry, false, HttpStatusCode.Forbidden, false, Tenant)]
    [InlineData(nameof(CancelWorkItem), EffectKindCatalog.CascadeCancel, false, HttpStatusCode.Forbidden, false, "TENANT-SDK")]
    [InlineData(nameof(CancelWorkItem), EffectKindCatalog.CascadeCancel, false, HttpStatusCode.Forbidden, false, "Tenant-sdk")]
    [InlineData(nameof(ResumeWorkItem), EffectKindCatalog.DateResume, false, HttpStatusCode.Forbidden, false, "TENANT-SDK")]
    [InlineData(nameof(ResumeWorkItem), EffectKindCatalog.DateResume, false, HttpStatusCode.Forbidden, false, "Tenant-sdk")]
    [InlineData(nameof(ExpireWorkItem), EffectKindCatalog.CascadeExpire, true, HttpStatusCode.Forbidden, false, "TENANT-SDK")]
    [InlineData(nameof(ExpireWorkItem), EffectKindCatalog.CascadeExpire, true, HttpStatusCode.Forbidden, false, "Tenant-sdk")]
    public async Task DelegatedOriginMatrixAllowsOnlyItsPurposeCommandPairs(
        string commandType, string kind, bool withWitness, HttpStatusCode expectedStatus, bool expectedRejection, string envelopeTenant)
    {
        var store = new InMemoryReadModelStore();
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", settings:
            ["--Works:Recovery:UseSdkRuntime=true", "--EventStore:Processes:ReconciliationEnabled=false", "--EventStore:Reminders:ReconciliationEnabled=false"]);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId("origin-matrix");
        DateTimeOffset due = DateTimeOffset.UnixEpoch.AddDays(1);
        var witness = new WorkItemExpirySchedule(due, ReminderIdentityCodec.ComputeScheduleToken(Tenant, item.Value, due, 2), 2);
        var state = new WorkItemState();
        var created = new WorkItemCreated(item.Value, 1, tenant, item, new Obligation("matrix proof"));
        state.Apply(created);
        AwaitCondition condition = kind == EffectKindCatalog.ChildCompletionResume
            ? AwaitCondition.ChildCompleted(new WorkItemId("completed-child")) : AwaitCondition.DateReached(due);
        object currentState = commandType == nameof(ResumeWorkItem)
            ? Story48Streams.Page(Tenant, item.Value, created, new WorkItemSuspended(item.Value, 2, tenant, item, [condition])).Events
            : state;
        byte[] payload = commandType switch
        {
            nameof(CancelWorkItem) => JsonSerializer.SerializeToUtf8Bytes(new CancelWorkItem(tenant, item)),
            nameof(ResumeWorkItem) => JsonSerializer.SerializeToUtf8Bytes(new ResumeWorkItem(tenant, item, condition)),
            _ => JsonSerializer.SerializeToUtf8Bytes(new ExpireWorkItem(tenant, item, withWitness ? witness : null)),
        };
        var identity = new EffectIdentity(Tenant, "work", "source-matrix", 9, kind, "work", item.Value, 0);
        string message = EffectIdentityCodec.ComputeMessageId(identity);
        var envelope = new CommandEnvelope(message, envelopeTenant, "work", item.Value, commandType,
            payload, message, "committed-source", "works", new() { ["trusted-purpose"] = WorksEffectPurposes.ForKind(kind) });
        var request = new DomainServiceRequest(envelope, currentState)
        {
            TrustedEffectOrigin = new TrustedEffectOrigin(identity, "works", WorksEffectPurposes.ForKind(kind), "committed-source",
                commandType, EffectIdentityCodec.RenderDigest(SHA256.HashData(payload))),
        };
        using HttpResponseMessage response = await client.SendAsync(Request("/process", request, channel: true,
            assertion: Assertion("domain-service:process", "eventstore")), TestContext.Current.CancellationToken).ConfigureAwait(true);
        response.StatusCode.ShouldBe(expectedStatus);
        if (expectedStatus == HttpStatusCode.OK)
        {
            DomainServiceWireResult result = (await response.Content.ReadFromJsonAsync<DomainServiceWireResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true))!;
            result.IsRejection.ShouldBe(expectedRejection);
            result.Events.Any(value => value.EventTypeName.EndsWith(nameof(WorkItemCancelled), StringComparison.Ordinal))
                .ShouldBe(commandType == nameof(CancelWorkItem));
            result.Events.Any(value => value.EventTypeName.EndsWith(nameof(WorkItemResumed), StringComparison.Ordinal))
                .ShouldBe(commandType == nameof(ResumeWorkItem));
        }
        else
        {
            (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        }
    }

    /// <summary>Protected command ingress refuses every reserved spelling without mutating persisted projections.</summary>
    [Theory]
    [InlineData("TENANTS", false)]
    [InlineData("TeNaNtS", false)]
    [InlineData("TENANTS", true)]
    [InlineData("TeNaNtS", true)]
    public async Task ReservedTenantHttpCommandPreservesPersistedState(string reserved, bool payloadReserved)
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await PopulateRuntimeAsync(coordinator);
        var projection = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = projection.CreateScope(Tenant);
        SharedProjectionReadResult<WorksRuntimeDocument> before = await coordinator.ReadAsync<WorksRuntimeDocument>(scope,
            WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken);
        string beforeBytes = before.Value.ShouldNotBeNull().Value.GetRawText();
        int beforeCount = store.Count;
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", coordinator,
            ["--EventStore:Processes:ReconciliationEnabled=false", "--EventStore:Reminders:ReconciliationEnabled=false"]);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        var command = new CreateWorkItem(new TenantId(payloadReserved ? reserved : Tenant), new WorkItemId("reserved-http"), "refused");
        var envelope = new CommandEnvelope("01ARZ3NDEKTSV4RRFFQ69G5FAV", payloadReserved ? Tenant : reserved,
            "work", "reserved-http", nameof(CreateWorkItem), JsonSerializer.SerializeToUtf8Bytes(command),
            "01ARZ3NDEKTSV4RRFFQ69G5FAV", null, "actor-reserved", null);

        using HttpResponseMessage response = await client.SendAsync(Request("/process", new DomainServiceRequest(envelope, null),
            channel: true, assertion: Assertion("domain-service:process")), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        store.Count.ShouldBe(beforeCount);
        SharedProjectionReadResult<WorksRuntimeDocument> after = await coordinator.ReadAsync<WorksRuntimeDocument>(scope,
            WorksReadModelKeys.RuntimeQueueKey, TestContext.Current.CancellationToken);
        after.Value.ShouldNotBeNull().Value.GetRawText().ShouldBe(beforeBytes);
        after.Generation.ShouldBe(before.Generation);
        (await coordinator.GetCheckpointAsync(scope, "mine", TestContext.Current.CancellationToken)).ShouldNotBeNull().Position.ShouldBe(2);
        store.Snapshot<object>("statestore", WorksReadModelKeys.PendingDateAwaitRegistryKey).ShouldBeNull();
    }

    /// <summary>An expiry-bearing create or reschedule must address the stream its envelope names.</summary>
    [Theory]
    [InlineData(nameof(CreateWorkItem), "tenant")]
    [InlineData(nameof(CreateWorkItem), "domain")]
    [InlineData(nameof(CreateWorkItem), "aggregate")]
    [InlineData(nameof(RescheduleWorkItem), "tenant")]
    [InlineData(nameof(RescheduleWorkItem), "domain")]
    [InlineData(nameof(RescheduleWorkItem), "aggregate")]
    public void WitnessedCreateAndRescheduleRejectForeignEnvelopeAddress(string commandType, string foreign)
    {
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId("address-matrix");
        DateTimeOffset due = DateTimeOffset.UnixEpoch.AddDays(1);
        var witness = new WorkItemExpirySchedule(due, ReminderIdentityCodec.ComputeScheduleToken(Tenant, item.Value, due, 1), 1);
        CommandEnvelope envelope = new("01ARZ3NDEKTSV4RRFFQ69G5FAV", foreign == "tenant" ? "foreign-tenant" : Tenant,
            foreign == "domain" ? "foreign" : "work", foreign == "aggregate" ? "foreign-item" : item.Value, commandType,
            [123], "01ARZ3NDEKTSV4RRFFQ69G5FAV", null, "test-user", null);
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(item.Value, 1, tenant, item, new Obligation("address proof")));

        _ = Should.Throw<InvalidOperationException>(() => commandType == nameof(CreateWorkItem)
            ? WorkItemEventStoreAggregate.Handle(new CreateWorkItem(tenant, item, "address proof", Expiry: witness), null, envelope)
            : WorkItemEventStoreAggregate.Handle(new RescheduleWorkItem(tenant, item, new WorkItemSchedule(), Expiry: witness), state, envelope));
    }

    [Fact]
    public async Task ProcessDispositionIngressRequiresProtectedTransportAndCurrentAuthority()
    {
        var store = new InMemoryReadModelStore();
        bool authenticatedPrincipalObserved = false;
        var defaultAuthorizer = new WorksProcessDispositionAuthorizer();
        var observedAuthorizer = Substitute.For<IProcessDispositionAuthorizer>();
        observedAuthorizer.AuthorizeAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), Arg.Any<ProcessSource>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var principal = call.Arg<System.Security.Claims.ClaimsPrincipal>();
            authenticatedPrincipalObserved = principal.Identity?.IsAuthenticated == true;
            return defaultAuthorizer.AuthorizeAsync(principal, call.Arg<ProcessSource>(), call.Arg<CancellationToken>());
        });
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", settings:
            ["--Works:Recovery:UseSdkRuntime=true", "--EventStore:Processes:ReconciliationEnabled=false", "--EventStore:Reminders:ReconciliationEnabled=false"],
            configure: services =>
            {
                services.RemoveAll<IProcessDispositionAuthorizer>();
                services.AddSingleton(observedAuthorizer);
            });
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        var source = new ProcessSource(Tenant, "work", "existing-source");
        ProcessCheckpointStore checkpoints = app.Services.GetRequiredService<ProcessCheckpointStore>();
        app.Services.GetRequiredService<IProcessDispositionAuditSink>().ShouldBeOfType<WorksProcessDispositionAuditSink>();
        await checkpoints.EnsureCandidateAsync(source, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        ProcessSourceState owned = (await checkpoints.AcquireAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true))!;
        owned = await checkpoints.UpdateOwnedAsync(owned, state => state with
        {
            Checkpoint = 1,
            Status = ProcessSourceStatus.Quarantined,
            FailureSequence = 2,
            ReasonCode = "invalid-evidence",
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await checkpoints.ReleaseAsync(owned, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var before = await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), TestContext.Current.CancellationToken).ConfigureAwait(true);
        string bearer = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "hexalith-eventstore",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = "synthetic-operator" },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
        });
        foreach ((bool channel, string? assertion, string? credential, HttpStatusCode status) in new[]
        {
            (false, (string?)null, (string?)null, HttpStatusCode.Unauthorized),
            (true, Assertion("domain-service:query"), (string?)null, HttpStatusCode.Forbidden),
            (true, Assertion("domain-service:process"), (string?)null, HttpStatusCode.Forbidden),
            (true, Assertion("domain-service:process"), bearer, HttpStatusCode.Unauthorized),
        })
        {
            using HttpResponseMessage denied = await client.SendAsync(Request("/work/processes/revalidate",
                new WorksProcessDispositionRequest(source, "synthetic revalidation"), channel, assertion, credential),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            denied.StatusCode.ShouldBe(status);
            (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(before);
        }
        authenticatedPrincipalObserved.ShouldBeTrue();
        await observedAuthorizer.Received(1).AuthorizeAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), source, Arg.Any<CancellationToken>());
    }

    /// <summary>Malformed requests and unavailable audit authority fail before disposition or audit state changes.</summary>
    [Theory]
    [InlineData("null-source", HttpStatusCode.BadRequest)]
    [InlineData("blank-reason", HttpStatusCode.BadRequest)]
    [InlineData("long-reason", HttpStatusCode.BadRequest)]
    [InlineData("invalid-source", HttpStatusCode.BadRequest)]
    [InlineData("audit-unavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task InvalidOrUnauditableProcessDispositionPreservesDurableState(string scenario, HttpStatusCode expected)
    {
        var store = new InMemoryReadModelStore();
        var source = new ProcessSource(Tenant, "work", "invalid-disposition");
        var authorizer = Substitute.For<IProcessDispositionAuthorizer>();
        authorizer.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ProcessSource>(), Arg.Any<CancellationToken>()).Returns(true);
        var audit = Substitute.For<IProcessDispositionAuditSink>();
        audit.RecordAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ProcessSource>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new WorksProcessDispositionAuditSink().RecordAsync(call.Arg<ClaimsPrincipal>(),
                call.Arg<ProcessSource>(), call.Arg<string>(), call.Arg<CancellationToken>()));
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", settings:
            ["--Works:Recovery:UseSdkRuntime=true", "--EventStore:Processes:ReconciliationEnabled=false", "--EventStore:Reminders:ReconciliationEnabled=false"],
            configure: services =>
            {
                services.RemoveAll<IProcessDispositionAuthorizer>();
                services.AddSingleton(authorizer);
                services.RemoveAll<IProcessDispositionAuditSink>();
                services.AddSingleton(audit);
            });
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        ProcessCheckpointStore checkpoints = app.Services.GetRequiredService<ProcessCheckpointStore>();
        await checkpoints.EnsureCandidateAsync(source, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        ProcessSourceState owned = (await checkpoints.AcquireAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true))!;
        owned = await checkpoints.UpdateOwnedAsync(owned, state => state with
        { Checkpoint = 1, Status = ProcessSourceStatus.Quarantined, FailureSequence = 2, ReasonCode = "invalid-evidence" },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await checkpoints.ReleaseAsync(owned, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var before = await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        string beforeSweep = JsonSerializer.Serialize(await store.GetAsync<ProcessSweepState>("statestore", ProcessStateKeys.Sweep,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        int beforeCount = store.Count;
        var request = scenario switch
        {
            "null-source" => new WorksProcessDispositionRequest(null!, "synthetic repair"),
            "blank-reason" => new WorksProcessDispositionRequest(source, " "),
            "long-reason" => new WorksProcessDispositionRequest(source, new string('x', 257)),
            "invalid-source" => new WorksProcessDispositionRequest(source with { Aggregate = " " }, "synthetic repair"),
            _ => new WorksProcessDispositionRequest(source, "synthetic repair"),
        };
        using HttpResponseMessage response = await client.SendAsync(Request("/work/processes/revalidate", request,
            channel: true, assertion: Assertion("domain-service:process")), TestContext.Current.CancellationToken).ConfigureAwait(true);
        response.StatusCode.ShouldBe(expected);
        (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(before);
        JsonSerializer.Serialize(await store.GetAsync<ProcessSweepState>("statestore", ProcessStateKeys.Sweep,
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(beforeSweep);
        store.Count.ShouldBe(beforeCount, "denial must persist no audit or disposition writes");
        await audit.Received(scenario == "audit-unavailable" ? 1 : 0).RecordAsync(Arg.Any<ClaimsPrincipal>(),
            Arg.Any<ProcessSource>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Authorized HTTP disposition durably audits before mutation, preserves the checkpoint, and reuses SDK evidence validation.</summary>
    [Theory]
    [InlineData(false, HttpStatusCode.NoContent)]
    [InlineData(true, HttpStatusCode.Accepted)]
    public async Task AuthorizedProcessDispositionAuditsBeforeImmediateOrDeferredRevalidation(bool retainOwner, HttpStatusCode expected)
    {
        const string auditKey = "story415:http-disposition-audit";
        const string reason = "synthetic evidence repair";
        var store = new InMemoryReadModelStore();
        var source = new ProcessSource(Tenant, "work", "authorized-disposition");
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(source.Aggregate);
        StreamReadPage committed = Story48Streams.Page(Tenant, source.Aggregate,
            new WorkItemCreated(source.Aggregate, 1, tenant, item, new Obligation("HTTP recovery")),
            new WorkItemCancelled(source.Aggregate, 2, tenant, item));
        bool repaired = false;
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.Arg<StreamReadRequest>();
            StreamReadPage page = repaired ? committed : committed with
            { Events = [committed.Events[0], committed.Events[1] with { EventTypeName = "UnknownWorksFact" }] };
            StreamReadEvent[] tail = [.. page.Events.Where(fact => fact.SequenceNumber > request.FromSequence)];
            return page with
            {
                Events = tail,
                Metadata = page.Metadata with
                { FromSequence = request.FromSequence, EventCount = tail.Length, LastSequenceReturned = tail.LastOrDefault()?.SequenceNumber }
            };
        });
        var authorizer = Substitute.For<IProcessDispositionAuthorizer>();
        authorizer.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), source, Arg.Any<CancellationToken>()).Returns(call =>
        {
            var principal = call.Arg<ClaimsPrincipal>();
            principal.Identity.ShouldNotBeNull().IsAuthenticated.ShouldBeTrue();
            principal.FindFirst(ClaimTypes.NameIdentifier).ShouldNotBeNull().Value.ShouldBe("workload:eventstore");
            return true;
        });
        string expectedSource = "";
        string? expectedSweepETag = null;
        int auditCount = 0;
        int auditedWrites = 0;
        var audit = Substitute.For<IProcessDispositionAuditSink>();
        audit.RecordAsync(Arg.Any<ClaimsPrincipal>(), source, reason, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            CancellationToken cancellationToken = call.Arg<CancellationToken>();
            JsonSerializer.Serialize((await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source),
                cancellationToken).ConfigureAwait(true)).Value).ShouldBe(expectedSource);
            (await store.GetAsync<ProcessSweepState>("statestore", ProcessStateKeys.Sweep, cancellationToken)
                .ConfigureAwait(true)).ETag.ShouldBe(expectedSweepETag);
            string actor = call.Arg<ClaimsPrincipal>().FindFirst(ClaimTypes.NameIdentifier).ShouldNotBeNull().Value;
            _ = await ReadModelWritePolicy.UpdateAsync<List<Dictionary<string, string>>>(store, "statestore", auditKey,
                prior => [.. prior ?? [], new() { ["actor"] = actor, ["tenant"] = source.Tenant,
                    ["domain"] = source.Domain, ["aggregate"] = source.Aggregate, ["reason"] = reason }],
                cancellationToken: cancellationToken).ConfigureAwait(true);
            auditCount++;
            store.ConcurrentWriteBeforeTrySave = () =>
            {
                List<Dictionary<string, string>> durable = store.Snapshot<List<Dictionary<string, string>>>("statestore", auditKey)!;
                durable.Count.ShouldBe(auditCount);
                durable[^1]["actor"].ShouldBe("workload:eventstore");
                durable[^1]["aggregate"].ShouldBe(source.Aggregate);
                durable[^1]["reason"].ShouldBe(reason);
                auditedWrites++;
            };
        });
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        await using WebApplication app = BuildProtectedHost(store, "http://127.0.0.1:8999", settings:
            ["--Works:Recovery:UseSdkRuntime=true", "--EventStore:Processes:ReconciliationEnabled=false", "--EventStore:Reminders:ReconciliationEnabled=false", "--EventStore:Processes:SourcesPerPass=1", "--EventStore:Processes:SupportedCandidateCount=1"],
            configure: services =>
            {
                services.RemoveAll<IProcessDispositionAuthorizer>();
                services.AddSingleton(authorizer);
                services.RemoveAll<IProcessDispositionAuditSink>();
                services.AddSingleton(audit);
                services.RemoveAll<IEventStoreGatewayClient>();
                services.AddSingleton(gateway);
                services.RemoveAll<ITrustedEffectSubmitter>();
                services.AddSingleton(submitter);
            });
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
        ProcessCheckpointStore checkpoints = app.Services.GetRequiredService<ProcessCheckpointStore>();
        await checkpoints.EnsureCandidateAsync(source, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        ProcessSourceState owned = (await checkpoints.AcquireAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true))!;
        owned = await checkpoints.UpdateOwnedAsync(owned, state => state with
        { Checkpoint = 1, Status = ProcessSourceStatus.Quarantined, Attempts = 2, FailureSequence = 2, ReasonCode = "invalid-evidence" },
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        if (!retainOwner) await checkpoints.ReleaseAsync(owned, TestContext.Current.CancellationToken).ConfigureAwait(true);
        ProcessSourceState before = await checkpoints.GetAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true);
        async Task<HttpResponseMessage> RevalidateAsync()
        {
            store.ConcurrentWriteBeforeTrySave = null;
            expectedSource = JsonSerializer.Serialize(await checkpoints.GetAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true));
            expectedSweepETag = (await store.GetAsync<ProcessSweepState>("statestore", ProcessStateKeys.Sweep,
                TestContext.Current.CancellationToken).ConfigureAwait(true)).ETag;
            return await client.SendAsync(Request("/work/processes/revalidate", new WorksProcessDispositionRequest(source, reason),
                channel: true, assertion: Assertion("domain-service:process")), TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        using HttpResponseMessage response = await RevalidateAsync().ConfigureAwait(true);
        response.StatusCode.ShouldBe(expected);
        ProcessSourceState after = await checkpoints.GetAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true);
        after.Checkpoint.ShouldBe(1);
        ProcessPendingDisposition pending = (await app.Services.GetRequiredService<ProcessSweepStore>()
            .GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true)).Pending[ProcessStateKeys.SourceId(source)];
        pending.Applied.ShouldBe(!retainOwner);
        if (retainOwner)
        {
            after.ShouldBe(before);
            pending.ClaimToken.ShouldBeNull();
            await checkpoints.ReleaseAsync(owned, TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await app.Services.GetRequiredService<ProcessReconciler>().RunPassAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            ProcessPendingDisposition applied = (await app.Services.GetRequiredService<ProcessSweepStore>()
                .GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true)).Pending[ProcessStateKeys.SourceId(source)];
            applied.Applied.ShouldBeTrue();
            after = await checkpoints.GetAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true);
            after.Status.ShouldBe(ProcessSourceStatus.Revalidating);
            after.LastDispositionGeneration.ShouldBe(pending.PreparedGeneration);
            after.LastDispositionGeneration.ShouldBeGreaterThan(before.LastDispositionGeneration);
            after.Checkpoint.ShouldBe(before.Checkpoint);
        }
        else
        {
            after.Status.ShouldBe(ProcessSourceStatus.Revalidating);
            after.FailureSequence.ShouldBeNull();
            after.ReasonCode.ShouldBeNull();
            after.Attempts.ShouldBe(0);
        }
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        CheckpointedProcessRunner runner = scope.ServiceProvider.GetRequiredService<CheckpointedProcessRunner>();
        ProcessRunResult invalid = await runner.RunAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true);
        invalid.Status.ShouldBe(ProcessSourceStatus.Quarantined);
        invalid.Checkpoint.ShouldBe(1);
        (await checkpoints.GetAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true)).ReasonCode.ShouldBe("invalid-evidence");
        repaired = true;
        (await runner.RunAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true)).Status.ShouldBe(ProcessSourceStatus.Quarantined);
        using HttpResponseMessage retry = await RevalidateAsync().ConfigureAwait(true);
        retry.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await checkpoints.GetAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true)).Checkpoint.ShouldBe(1);
        ProcessRunResult recovered = await runner.RunAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true);
        recovered.Status.ShouldBe(ProcessSourceStatus.Active);
        recovered.Checkpoint.ShouldBe(2);
        ProcessSourceState persisted = await checkpoints.GetAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true);
        persisted.Checkpoint.ShouldBe(2);
        persisted.ReasonCode.ShouldBeNull();
        store.Snapshot<List<Dictionary<string, string>>>("statestore", auditKey).ShouldNotBeNull().Count.ShouldBe(2);
        auditedWrites.ShouldBeGreaterThan(0);
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Counts store operations for the in-flight HTTP request after <see cref="Observe"/> is enabled.
    /// Startup and background recovery traffic stay outside that request and are not counted.
    /// </summary>
    private sealed class QueryWindowStore(InMemoryReadModelStore inner) : IReadModelStore, IReadModelBulkStore, IReadModelBatchStore
    {
        private static readonly AsyncLocal<int> RequestDepth = new();
        private int _operations;

        public bool Observe { get; set; }

        public int Operations => _operations;

        internal static IDisposable EnterRequest()
        {
            RequestDepth.Value++;
            return new RequestScope();
        }

        public Task<ReadModelEntry<TValue>> GetAsync<TValue>(string storeName, string key, CancellationToken cancellationToken = default)
            where TValue : class
        {
            Note();
            return inner.GetAsync<TValue>(storeName, key, cancellationToken);
        }

        public Task SaveAsync<TValue>(string storeName, string key, TValue value, CancellationToken cancellationToken = default)
            where TValue : class
        {
            Note();
            return inner.SaveAsync(storeName, key, value, cancellationToken);
        }

        public Task<bool> TrySaveAsync<TValue>(string storeName, string key, TValue value, string etag, CancellationToken cancellationToken = default)
            where TValue : class
        {
            Note();
            return inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
        }

        public Task<IReadOnlyList<ReadModelBulkEntry<TValue>>> GetManyAsync<TValue>(string storeName,
            IReadOnlyList<string> keys, int parallelism, CancellationToken cancellationToken = default)
            where TValue : class
        {
            Note();
            return inner.GetManyAsync<TValue>(storeName, keys, parallelism, cancellationToken);
        }

        public Task<ReadModelBatchResult> ExecuteAsync(ReadModelBatch batch, CancellationToken cancellationToken = default)
        {
            Note();
            return inner.ExecuteAsync(batch, cancellationToken);
        }

        private void Note()
        {
            if (Observe && RequestDepth.Value > 0)
            {
                _ = Interlocked.Increment(ref _operations);
            }
        }

        private sealed class RequestScope : IDisposable
        {
            public void Dispose() => RequestDepth.Value--;
        }
    }

    /// <summary>Marks the HTTP pipeline so only that request's store access is attributed to the denial check.</summary>
    private sealed class QueryRequestObservationFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (context, continuation) =>
                {
                    using (QueryWindowStore.EnterRequest())
                        await continuation(context).ConfigureAwait(false);
                });
                next(app);
            };
    }

    private static WebApplication BuildProtectedHost(IReadModelStore store, string partiesAddress,
        SharedProjectionEpochCoordinator? coordinator = null, string[]? settings = null, Action<IServiceCollection>? configure = null,
        IConfiguration? queryConfiguration = null)
        => WorksHost.Build([
            "--environment=Development",
            "--Logging:LogLevel:Default=Error",
            "--Works:Projection:UseSdkDelivery=true",
            $"--Works:Query:PartiesGatewayBaseAddress={partiesAddress}",
            "--Works:Recovery:RunReconciliationOnStartup=false",
            $"--{DaprAppChannelToken.ConfigurationKey}={ChannelToken}",
            $"--EventStore:DomainService:AppId={AppId}",
            $"--Authentication:JwtBearer:Issuer={Issuer}",
            "--Authentication:JwtBearer:Audience=hexalith-eventstore",
            $"--Authentication:JwtBearer:SigningKey={SigningKey}",
            $"--Authentication:JwtBearer:AllowedAlgorithms:0={SecurityAlgorithms.HmacSha256}",
            "--Authentication:JwtBearer:RequireHttpsMetadata=false",
            .. settings ?? [],
        ], webHost =>
        {
            webHost.UseUrls("http://127.0.0.1:0");
            if (queryConfiguration is not null)
                webHost.ConfigureAppConfiguration((_, configuration) => configuration.AddConfiguration(queryConfiguration));
        }, services =>
        {
            services.RemoveAll<IReadModelStore>();
            services.AddSingleton(store);
            if (store is QueryWindowStore)
                services.AddSingleton<IStartupFilter, QueryRequestObservationFilter>();
            if (store is IReadModelBatchStore batches)
            {
                services.RemoveAll<IReadModelBatchStore>();
                services.AddSingleton(batches);
            }
            if (store is IReadModelBulkStore bulk)
            {
                services.RemoveAll<IReadModelBulkStore>();
                services.AddSingleton(bulk);
            }
            if (coordinator is not null)
            {
                services.RemoveAll<SharedProjectionEpochCoordinator>();
                services.AddSingleton(coordinator);
            }
            configure?.Invoke(services);
        });

    private static WebApplication BuildPartiesGateway(Func<SubmitQueryRequest, HttpContext, IResult> query)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(["--Logging:LogLevel:Default=Error"]);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        app.MapPost("/api/v1/queries", query);
        return app;
    }

    private static WebApplication BuildAuthenticatedPartiesGateway(Func<SubmitQueryRequest, HttpContext, IResult> query)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(["--Logging:LogLevel:Default=Error"]);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = "parties-identity-reader",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.Zero,
            };
        });
        builder.Services.AddAuthorization();
        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/api/v1/queries", query).RequireAuthorization();
        return app;
    }

    private static string ServiceBearer(bool expired = false, string credentialId = "service-authority")
    {
        DateTime now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "parties-identity-reader",
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "works-identity-reader",
                ["azp"] = "works",
                ["jti"] = credentialId,
                ["eventstore:tenant"] = Tenant,
                ["eventstore:domain"] = "party",
                ["eventstore:permission"] = "query:read",
            },
            IssuedAt = expired ? now.AddHours(-3) : now.AddMinutes(-2),
            NotBefore = expired ? now.AddHours(-3) : now.AddMinutes(-2),
            Expires = expired ? now.AddHours(-2) : now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Without a configured service bearer the Parties transport fails closed before any request is sent.</summary>
    [Fact]
    public async Task Parties_gateway_handler_rejects_requests_without_a_service_bearer()
    {
        var inner = new CountingHandler();
        using var client = new HttpClient(new WorksPartiesGatewayAuthorizationHandler { InnerHandler = inner })
        {
            BaseAddress = new Uri("http://parties.invalid"),
        };

        _ = await Should.ThrowAsync<InvalidOperationException>(() => client.GetAsync("/probe"));
        inner.Calls.ShouldBe(0);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static HttpClient PartiesClient(string address, string? bearer)
    {
        var handler = new WorksPartiesGatewayAuthorizationHandler { InnerHandler = new HttpClientHandler() };
        var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        if (bearer is not null)
        {
            // Mirrors WorksHost: the configured service bearer is the only credential ever sent to Parties.
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return client;
    }

    private static string Address(WebApplication app)
        => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            .ShouldNotBeNull().Addresses.Single();

    private static QueryEnvelope ExecutorQuery()
        => new(Tenant, "work", "mine", WhatsNextQueryHandler.WhatsNextQueryType,
            JsonSerializer.SerializeToUtf8Bytes(new { partyId = "party-mine", allWork = true, role = "coordinator" }),
            "corr-http", Actor, null, true, null, Actor, "eventstore", true, null, null);

    private static ProjectionDispatchRequest NamedDelivery(IEventPayload payload, long sequence)
    {
        var dto = new ProjectionEventDto(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
            "json", sequence, DateTimeOffset.UnixEpoch, "corr-http", GlobalPosition: sequence);
        return new ProjectionDispatchRequest(new ProjectionRequest(Tenant, "work", "mine", [dto]),
            [WorksReadModelKeys.RuntimeFamily], $"http-delivery-{sequence}", ProjectionRouteCatalogFingerprint.Compute(
                AppId, "v1", [new ProjectionDispatchRoute("work", WorksReadModelKeys.RuntimeFamily)]));
    }

    private static PartyIdentityResult CurrentIdentity()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var custody = new IdentityHistoryCustodyEvidence("policy", "party-actor-history-v1", now.AddDays(1),
            1, "custody", true, true, true);
        var binding = new HumanActorBindingEvidence(Tenant, "party-mine", Actor, 1, 1,
            now.AddMinutes(-1), now.AddHours(1), "source", "provenance", custody);
        return new PartyIdentityResult(PartyIdentityOutcome.Resolved,
            new PartyIdentityEvidence(1, Tenant, "party-mine", PartyIdentityClassification.Human,
                true, false, false, 2, now, "observation", binding));
    }

    private static void AssertIdentityRequest(SubmitQueryRequest request)
    {
        request.Tenant.ShouldBe(Tenant);
        request.Domain.ShouldBe("party");
        request.AggregateId.ShouldBe("party-mine");
        request.QueryType.ShouldBe(typeof(Hexalith.Parties.Contracts.Queries.ResolvePartyIdentity).FullName);
        var query = request.Payload.ShouldNotBeNull().Deserialize<Hexalith.Parties.Contracts.Queries.ResolvePartyIdentity>(JsonSerializerOptions.Web)
            .ShouldNotBeNull();
        query.TenantId.ShouldBe(Tenant);
        query.PartyId.ShouldBe("party-mine");
        query.ExpectedActorId.ShouldBe(Actor);
    }

    private static async Task PopulateRuntimeAsync(SharedProjectionEpochCoordinator coordinator)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(Tenant);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        var tenant = new TenantId(Tenant);
        foreach (string aggregate in new[] { "mine", "foreign", "queue" })
        {
            var workItem = new WorkItemId(aggregate);
            IEventPayload[] events = [
                new WorkItemCreated(aggregate, 1, tenant, workItem, new Obligation(aggregate)),
                aggregate == "queue" ? new WorkItemQueued(aggregate, 2, tenant, workItem)
                    : new WorkItemAssigned(aggregate, 2, tenant, workItem, new ExecutorBinding(
                        new PartyId(aggregate == "mine" ? "party-mine" : "party-other"), Channel.Mcp, AuthorityLevel.Coordinate)),
            ];
            for (int index = 0; index < events.Length; index++)
            {
                IEventPayload payload = events[index];
                var dto = new ProjectionEventDto(payload.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
                    "json", index + 1, DateTimeOffset.UnixEpoch, "corr-http", GlobalPosition: index + 1);
                var delivery = new SharedProjectionDelivery(aggregate, index + 1,
                    JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(Tenant, "work", aggregate, [dto]), JsonSerializerOptions.Web),
                    JsonSerializer.SerializeToUtf8Bytes(index + 1));
                (await coordinator.JournalAsync(scope, lease, delivery, TestContext.Current.CancellationToken)
                    .ConfigureAwait(true)).ShouldBe(SharedProjectionJournalResult.Journaled);
                (await coordinator.CatchUpAsync(scope,
                    (current, generation, token) => handler.FoldCatchUpAsync(current, generation, coordinator, token),
                    TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(1);
                lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
            }
        }
    }

    private static string Assertion(string operation, string caller = "eventstore", QueryEnvelope? query = null)
    {
        DateTime now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            [EventStoreWorkloadAuthenticationDefaults.CallerClaimType] = caller,
            [EventStoreWorkloadAuthenticationDefaults.OperationClaimType] = new[] { operation },
        };
        if (query is not null)
        {
            foreach ((string key, string value) in QueryCallerAuthorityBinding.Bind(query))
                claims.Add(key, value);
        }
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = AppId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(2),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
