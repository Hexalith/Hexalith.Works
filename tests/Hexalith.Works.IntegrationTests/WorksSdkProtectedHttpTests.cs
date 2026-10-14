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
        QueryResult list = await new WhatsNextQueryHandler(store, options, policy, coordinator)
            .ExecuteAsync(ExecutorQuery(), TestContext.Current.CancellationToken).ConfigureAwait(true);
        list.Success.ShouldBeTrue();
        list.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        list.GetPayload().EnumerateArray().Select(item =>
            item.GetProperty("workItemId").GetProperty("value").GetString()).ShouldBe(["mine", "queue"]);

        foreach ((string aggregate, bool expected) in new[] { ("mine", true), ("queue", true), ("foreign", false) })
        {
            QueryResult detail = await new GetWorkItemQueryHandler(store, options, policy, coordinator)
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
            ["mine"] = 2, ["foreign"] = 2, ["queue"] = 2,
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        ReadModelBatchOperation[] operations = [.. family.Value.Select(pair => ReadModelBatchOperation.Write(
            pair.Key, pair.Key == WorksReadModelKeys.RuntimeRollUpKey("mine")
                ? WorksRuntimeDocument.From(rollUp) : pair.Value!, ReadModelBatchConcurrency.LastWrite))];
        _ = await coordinator.StageAsync(scope, "contaminated", operations,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        await coordinator.CommitAsync(scope, "contaminated", TestContext.Current.CancellationToken).ConfigureAwait(true);

        using HttpClient client = PartiesClient(Address(parties), HumanBearer);
        var policy = new WorksQueryPolicy(new HttpPartiesIdentityClient(client), Options.Create(new WorksQueryOptions()));
        QueryResult result = await new GetWorkItemQueryHandler(store, options, policy, coordinator)
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
                ? await new WhatsNextQueryHandler(store, options, policy).ExecuteAsync(query,
                    TestContext.Current.CancellationToken).ConfigureAwait(true)
                : await new GetWorkItemQueryHandler(store, options, policy).ExecuteAsync(query,
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        }

        identityRequests.ShouldBe(failure == "missing-bearer" ? 0 : 2);
        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfiguredServiceBearerAuthorizesProtectedExecutorQueriesWithSyntheticCurrentAuthority()
    {
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
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await PopulateRuntimeAsync(coordinator).ConfigureAwait(true);
        await using WebApplication app = BuildProtectedHost(store, Address(parties), coordinator,
            [$"--Works:Query:PartiesGatewayBearerToken={ServiceBearer()}"]);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };

        // The protected SDK receiver sees only the gateway assertion. Its published Parties client
        // authenticates independently; neither payload flags nor the actor's bearer supply service authority.
        using HttpResponseMessage listResponse = await client.SendAsync(Request("/query", ExecutorQuery(),
            channel: true, assertion: Assertion("domain-service:query")),
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
            using HttpResponseMessage detailResponse = await client.SendAsync(Request("/query", ExecutorQuery() with
            {
                AggregateId = aggregate,
                QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType,
            }, channel: true, assertion: Assertion("domain-service:query")),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            QueryResult detail = (await detailResponse.Content.ReadFromJsonAsync<QueryResult>(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
            detail.Success.ShouldBeTrue();
            detail.GetPayload().GetProperty("found").GetBoolean().ShouldBe(expected);
            detail.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Current);
        }

        identityRequests.ShouldBe(4);
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
        IReadModelStore store = Substitute.For<IReadModelStore>();
        string bearer = failure == "expired" ? ServiceBearer(expired: true) : "rejected-service-credential";
        await using WebApplication app = BuildProtectedHost(store, Address(parties), settings:
            [$"--Works:Query:PartiesGatewayBearerToken={bearer}"]);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        store.ClearReceivedCalls();
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
        store.ReceivedCalls().ShouldBeEmpty();
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
            ["--Works:Query:GatewayWorkloadId=designated-gateway", "--Works:Query:PermittedCoordinatorWorkloads:0=scheduler"]);
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

        foreach (string route in new[] { "/query", "/project/v2" })
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

        using HttpResponseMessage legacyConflict = await client.SendAsync(Request("/project", query,
            channel: true, assertion: Assertion("domain-service:project")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        legacyConflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using HttpResponseMessage foreignOrigin = await client.SendAsync(Request("/query", query,
            channel: true, assertion: Assertion("domain-service:query", "other-caller")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        foreignOrigin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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

    private static WebApplication BuildProtectedHost(IReadModelStore store, string partiesAddress,
        SharedProjectionEpochCoordinator? coordinator = null, string[]? settings = null)
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
        ], webHost => webHost.UseUrls("http://127.0.0.1:0"), services =>
        {
            services.RemoveAll<IReadModelStore>();
            services.AddSingleton(store);
            if (store is IReadModelBatchStore batches)
            {
                services.RemoveAll<IReadModelBatchStore>();
                services.AddSingleton(batches);
            }
            if (coordinator is not null)
            {
                services.RemoveAll<SharedProjectionEpochCoordinator>();
                services.AddSingleton(coordinator);
            }
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

    private static string ServiceBearer(bool expired = false)
    {
        DateTime now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "parties-identity-reader",
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "works-identity-reader", ["azp"] = "works",
                ["eventstore:tenant"] = Tenant, ["eventstore:domain"] = "party", ["eventstore:permission"] = "query:read",
            },
            IssuedAt = now.AddMinutes(-2),
            NotBefore = now.AddMinutes(-2),
            Expires = expired ? now.AddMinutes(-1) : now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    private static HttpClient PartiesClient(string address, string? bearer)
    {
        var context = new DefaultHttpContext();
        if (bearer is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {bearer}";
        }

        var handler = new WorksPartiesGatewayAuthorizationHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = new HttpClientHandler(),
        };
        return new HttpClient(handler) { BaseAddress = new Uri(address) };
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

    private static string Assertion(string operation, string caller = "eventstore")
    {
        DateTime now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = AppId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(2),
            Claims = new Dictionary<string, object>
            {
                [EventStoreWorkloadAuthenticationDefaults.CallerClaimType] = caller,
                [EventStoreWorkloadAuthenticationDefaults.OperationClaimType] = new[] { operation },
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
