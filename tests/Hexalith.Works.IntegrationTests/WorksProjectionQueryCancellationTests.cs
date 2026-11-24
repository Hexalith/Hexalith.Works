using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Client.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Verifies query deadlines at the admitted provider and actual Dapr client transport boundaries.</summary>
[Collection("SDK discovery admission")]
public sealed class WorksProjectionQueryCancellationTests
{
    private const string Tenant = "projection-deadline";
    private const string WorkItem = "work-projection-deadline";
    private const string Workload = "scheduler";

    /// <summary>An unavailable result returns while the admitted provider still withholds its response.</summary>
    [Theory]
    [InlineData(WhatsNextQueryHandler.WhatsNextQueryType, false)]
    [InlineData(WhatsNextQueryHandler.WhatsNextQueryType, true)]
    [InlineData(GetWorkItemQueryHandler.GetWorkItemQueryType, false)]
    [InlineData(GetWorkItemQueryHandler.GetWorkItemQueryType, true)]
    public async Task NonCooperativeProviderCannotHoldAnAuthorizedQuery(string queryType, bool blockRelatedRead)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var inner = new InMemoryReadModelStore();
        var scope = new SharedProjectionScope(WorksReadModelKeys.StateStoreName, Tenant, "work",
            WorksReadModelKeys.RuntimeFamily, ["works-runtime-v3-writer"]);
        var seeded = new SharedProjectionEpochCoordinator(inner, inner);
        _ = await seeded.RegisterWriterAsync(scope, "works-runtime-v3-writer", ct);
        string key = "epoch";
        if (blockRelatedRead)
        {
            var identity = new DomainSharedProjectionRebuildIdentity(Tenant, "work", WorksReadModelKeys.RuntimeFamily,
                "seed-query-deadline", "seed-catalog");
            var created = new WorkItemCreated(WorkItem, 1, new TenantId(Tenant), new WorkItemId(WorkItem),
                new Obligation("Projection read deadline"));
            var dto = new ProjectionEventDto(nameof(WorkItemCreated), JsonSerializer.SerializeToUtf8Bytes(created),
                "json", 1, DateTimeOffset.UnixEpoch, "deadline-seed", GlobalPosition: 1);
            // The what's-next query reads parking only for eligible candidates, so the blocked item is queued.
            var queued = new WorkItemQueued(WorkItem, 2, new TenantId(Tenant), new WorkItemId(WorkItem));
            var queuedDto = new ProjectionEventDto(nameof(WorkItemQueued), JsonSerializer.SerializeToUtf8Bytes(queued),
                "json", 2, DateTimeOffset.UnixEpoch, "deadline-seed", GlobalPosition: 2);
            var candidate = new WorkItemSharedRebuildCandidateState([new WorkItemSharedRebuildAggregateHistory(WorkItem, [dto, queuedDto])]);
            var plan = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, candidate, ct);
            _ = await seeded.BeginAsync(scope, identity.OperationId, "seed-catalog", new Dictionary<string, long> { [WorkItem] = 2 }, ct);
            _ = await seeded.StageAsync(scope, identity.OperationId, plan.Operations, ct);
            await seeded.CommitAsync(scope, identity.OperationId, ct);
            long generation = (await seeded.GetStatusAsync(scope, ct)).ActiveGeneration;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WorksReadModelKeys.RuntimeParkingKey(WorkItem))));
            key = FormattableString.Invariant($"shared-generation:{scope.ComputeHash()}:{generation}:{hash}");
        }
        int persistedBefore = inner.Count;

        var blocked = new DiscoveryBlockedReadModelStore(inner, Tenant, scope.ComputeHash(), key);
        var coordinator = new SharedProjectionEpochCoordinator(blocked, inner);
        Task<QueryResult> request = Handler(queryType, coordinator).ExecuteAsync(Query(queryType), ct);
        try
        {
            await blocked.Started.WaitAsync(TimeSpan.FromSeconds(8), ct);
            QueryResult result = await request.WaitAsync(TimeSpan.FromSeconds(6), ct);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldBe("projection-unavailable");
            result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
            blocked.Invocations.ShouldBe(1);
            inner.Count.ShouldBe(persistedBefore);
            if (!blockRelatedRead && queryType == WhatsNextQueryHandler.WhatsNextQueryType)
                await AssertProviderSlotRetainedAsync(ct);
        }
        finally
        {
            blocked.Release();
            await WorksQueryPolicyDeadlineTests.DrainProviderSlotsAsync();
        }
    }

    /// <summary>The actual Dapr client's gRPC state request observes the caller's cancellation.</summary>
    [Theory]
    [InlineData(WhatsNextQueryHandler.WhatsNextQueryType, true)]
    [InlineData(WhatsNextQueryHandler.WhatsNextQueryType, false)]
    [InlineData(GetWorkItemQueryHandler.GetWorkItemQueryType, true)]
    [InlineData(GetWorkItemQueryHandler.GetWorkItemQueryType, false)]
    public async Task DaprTransportObservesCallerCancellationOrReadDeadline(string queryType, bool cancelCaller)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            endpoint => endpoint.Protocols = HttpProtocols.Http2));
        await using var sidecar = builder.Build();
        sidecar.MapPost("/dapr.proto.runtime.v1.Dapr/GetState", async (HttpContext context) =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted).ConfigureAwait(false);
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                cancelled.TrySetResult();
            }
        });
        await sidecar.StartAsync(ct);
        using DaprClient client = new DaprClientBuilder().UseGrpcEndpoint(sidecar.Urls.Single()).Build();
        var store = new DaprReadModelStore(client);
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<QueryResult> request = Handler(queryType, coordinator).ExecuteAsync(Query(queryType), caller.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
        if (cancelCaller)
        {
            await caller.CancelAsync();
            var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(6), ct));
            failure.CancellationToken.ShouldBe(caller.Token);
        }
        else
        {
            QueryResult result = await request.WaitAsync(TimeSpan.FromSeconds(6), ct);
            result.ErrorMessage.ShouldBe("projection-unavailable");
            result.Metadata.ShouldNotBeNull().Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
            caller.IsCancellationRequested.ShouldBeFalse();
        }
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
    }

    /// <summary>Time already spent in authorization cannot restart the projection budget.</summary>
    [Fact]
    public async Task BudgetExpiresBeforeAdmittingAProviderWhenAuthorizationConsumedIt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        long startedAt = TimeProvider.System.GetTimestamp() - (5 * TimeProvider.System.TimestampFrequency);
        int invocations = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorksProjectionReadBudget.ReadAsync(_ =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult(1);
        }, startedAt, ct));
        invocations.ShouldBe(0);
    }

    private static async Task AssertProviderSlotRetainedAsync(CancellationToken ct)
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new AuthoritativeStreamReadDeadline(TimeSpan.FromSeconds(10), TimeProvider.System, ct,
            TimeProvider.System.GetTimestamp());
        var probes = new List<Task<int>>();
        try
        {
            for (int index = 0; index < 15; index++)
                probes.Add(deadline.ReadWithAdmissionAsync(_ => release.Task));
            await Should.ThrowAsync<AuthoritativeStreamReadAdmissionException>(
                () => deadline.ReadWithAdmissionAsync(_ => Task.FromResult(-1))).ConfigureAwait(false);
        }
        finally
        {
            release.TrySetResult(0);
            (await Task.WhenAll(probes).ConfigureAwait(false)).ShouldAllBe(result => result == 0);
        }
    }

    private static IDomainQueryHandler Handler(string queryType, SharedProjectionEpochCoordinator coordinator)
    {
        var policy = new WorksQueryPolicy(Substitute.For<IPartiesIdentityClient>(),
            Options.Create(new WorksQueryOptions { PermittedCoordinatorWorkloads = [Workload] }));
        return queryType == WhatsNextQueryHandler.WhatsNextQueryType
            ? new WhatsNextQueryHandler(policy, coordinator)
            : new GetWorkItemQueryHandler(policy, coordinator);
    }

    private static QueryEnvelope Query(string queryType)
        => new(Tenant, "work", WorkItem, queryType, [], "deadline-test", Workload, null, false, null, null,
            Workload, false, null, null);
}
