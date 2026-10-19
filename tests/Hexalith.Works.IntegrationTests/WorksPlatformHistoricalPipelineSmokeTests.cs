using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Dapr.Client;

using Hexalith.Commons.UniqueIds;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Queries;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Historical lifecycle, tree, claim and filtered query scenarios on the real Platform composition.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksPlatformHistoricalPipelineSmokeTests
{
    private const string Actor = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string Party = "party-416-executor";

    /// <summary>Persists historical semantics, refuses unauthorized traffic and preserves views across actual target restart.</summary>
    [Fact]
    public async Task PlatformLifecycleTreeClaimFilteringAndSecuritySurviveRestart()
    {
        if (!WorksAppHostSelection.IsPlatform)
        {
            // Ordinary consumer tests have no Platform deployment artifact. The required Platform verifier
            // selects this fact only with a hash-bound target; it never counts this path as parity.
            Assert.Skip("Runs only in the Platform verifier lane, which binds the actual Platform AppHost artifact.");
        }
        CancellationToken ct = TestContext.Current.CancellationToken;
        (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true)).ShouldBeNull();
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        string suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));
        string tenant = "sdk415-history-" + suffix;
        string[] ids = ["mine-" + suffix, "foreign-" + suffix, "queued-" + suffix, "parent-" + suffix, "child-" + suffix, "race-" + suffix];
        string mine = ids[0], foreign = ids[1], queued = ids[2], parent = ids[3], child = ids[4], race = ids[5];
        var tenantId = new TenantId(tenant);
        var ownBinding = new ExecutorBinding(new PartyId(Party), Channel.Cli, AuthorityLevel.Contribute);
        var otherBinding = new ExecutorBinding(new PartyId("party-416-other"), Channel.Mcp, AuthorityLevel.Administer);
        using RSA authority = RSA.Create(2048);
        string key = Convert.ToBase64String(authority.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        WorksPlatformIdentityFixture identity = await WorksPlatformIdentityFixture.StartAsync(tenant, Party, Actor, ct).ConfigureAwait(true);
        await using var identityCleanup = identity.ConfigureAwait(false);
        ProcessSource[] grants = [.. ids.Select(id => new ProcessSource(tenant, "work", id))];
        Action<IDistributedApplicationTestingBuilder> configure = builder =>
        {
            WorksSdkRecoveryPipelineSmokeTests.Configure(builder, key, digestKey, isolation.ComponentPath, isolation.RedisEndpoint, grants, identity: identity);
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "eventstore"))
                .WithEnvironment("Logging__LogLevel__Hexalith.EventStore.Server.Projections.NamedProjectionDispatchCoordinator", "Information");
        };
        string? persistedDigest = null;

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, gateway, _sidecar, token) =>
        {
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new DaprReadModelStore(dapr);
            var coordinator = new SharedProjectionEpochCoordinator(store, store);
            var scope = new SharedProjectionScope("statestore", tenant, "work", WorksReadModelKeys.RuntimeFamily, ["works-runtime-v3-writer"]);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, mine, nameof(CreateWorkItem),
                new CreateWorkItem(tenantId, new WorkItemId(mine), "Historical effort lifecycle", new WorkItemEffort(10, new Unit("hour")), ExecutorBinding: ownBinding), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, mine, nameof(AssignWorkItem), new AssignWorkItem(tenantId, new WorkItemId(mine), ownBinding), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, foreign, nameof(CreateWorkItem), new CreateWorkItem(tenantId, new WorkItemId(foreign), "Other executor", ExecutorBinding: otherBinding), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, foreign, nameof(AssignWorkItem), new AssignWorkItem(tenantId, new WorkItemId(foreign), otherBinding), token).ConfigureAwait(false);
            foreach (string id in new[] { queued, parent, race })
                await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, id, nameof(CreateWorkItem), new CreateWorkItem(tenantId, new WorkItemId(id), "Historical " + id), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, parent, nameof(SpawnChild),
                new SpawnChild(tenantId, new WorkItemId(parent), new WorkItemId(child), "Reciprocal tree child"), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, child, nameof(CreateWorkItem),
                new CreateWorkItem(tenantId, new WorkItemId(child), "Tree child", Parent: new ParentWorkItemReference(tenantId, new WorkItemId(parent))), token).ConfigureAwait(false);
            foreach (string id in new[] { queued, race })
                await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, id, nameof(QueueWorkItem), new QueueWorkItem(tenantId, new WorkItemId(id)), token).ConfigureAwait(false);
            try
            {
                await WaitAsync(async () => (await ReadRollUpAsync(coordinator, scope, mine, token).ConfigureAwait(false))?.Status == WorkItemStatus.Assigned
                    && (await ReadRollUpAsync(coordinator, scope, foreign, token).ConfigureAwait(false))?.Status == WorkItemStatus.Assigned
                    && (await ReadRollUpAsync(coordinator, scope, queued, token).ConfigureAwait(false))?.Status == WorkItemStatus.Queued
                    && (await ReadRollUpAsync(coordinator, scope, child, token).ConfigureAwait(false))?.Parent?.WorkItemId.Value == parent, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
            {
                using var diagnosticDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                diagnosticDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                var cold = new SharedProjectionEpochCoordinator(store, store);
                SharedProjectionEpochStatus status = await cold.GetStatusAsync(scope, diagnosticDeadline.Token).ConfigureAwait(false);
                var checkpoints = new Dictionary<string, SharedProjectionCheckpoint?>(StringComparer.Ordinal);
                var rollUps = new Dictionary<string, WorkItemRollUp?>(StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    checkpoints[id] = await cold.GetCheckpointAsync(scope, id, diagnosticDeadline.Token).ConfigureAwait(false);
                    rollUps[id] = await ReadRollUpAsync(cold, scope, id, diagnosticDeadline.Token).ConfigureAwait(false);
                }
                throw new InvalidOperationException("[historical-initial-projection] " + JsonSerializer.Serialize(new
                {
                    status, checkpoints, rollUpStatuses = rollUps.ToDictionary(pair => pair.Key, pair => pair.Value?.Status),
                    childParent = rollUps[child]?.Parent?.WorkItemId.Value,
                }), exception);
            }
            await AwaitProjectionSourceHeadsAsync(coordinator, scope, gateway, ids, token).ConfigureAwait(false);
            ResourceEvent gatewayResource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore", token, token).ConfigureAwait(false);
            ResourceEvent worksResource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", token, token).ConfigureAwait(false);
            using HttpClient gatewaySidecar = WorksAppHostTestReadiness.CreateDaprClient(gatewayResource, "EventStore");
            using HttpClient worksSidecar = WorksAppHostTestReadiness.CreateDaprClient(worksResource, "Works");
            int identityCallsBefore = identity.Calls;
            QueryEnvelope executorQuery = ExecutorQuery(tenant, mine);
            QueryResult list = await QueryAsync(gatewaySidecar, executorQuery, token).ConfigureAwait(false);
            list.Success.ShouldBeTrue(list.ErrorMessage);
            string[] visible = list.GetPayload().EnumerateArray().Select(item => item.Deserialize<WhatsNextItem>(JsonSerializerOptions.Web)!.WorkItemId.Value).ToArray();
            visible.ShouldContain(mine);
            visible.ShouldContain(queued);
            visible.ShouldNotContain(foreign);
            QueryResult ownDetail = await QueryAsync(gatewaySidecar, executorQuery with { QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType }, token).ConfigureAwait(false);
            ownDetail.Success.ShouldBeTrue(ownDetail.ErrorMessage);
            ownDetail.GetPayload().Deserialize<WorkItemView>(JsonSerializerOptions.Web)!.Found.ShouldBeTrue();
            QueryResult hidden = await QueryAsync(gatewaySidecar, executorQuery with { AggregateId = foreign, QueryType = GetWorkItemQueryHandler.GetWorkItemQueryType }, token).ConfigureAwait(false);
            hidden.Success.ShouldBeTrue(hidden.ErrorMessage);
            hidden.GetPayload().Deserialize<WorkItemView>(JsonSerializerOptions.Web)!.Found.ShouldBeFalse();
            identity.Calls.ShouldBeGreaterThanOrEqualTo(3);
            string unchanged = await FamilyDigestAsync(coordinator, scope, ids, token).ConfigureAwait(false);
            QueryResult wrongActor = await QueryAsync(gatewaySidecar, executorQuery with { OriginalActorId = "01ARZ3NDEKTSV4RRFFQ69G5FAX", UserId = "01ARZ3NDEKTSV4RRFFQ69G5FAX" }, token).ConfigureAwait(false);
            wrongActor.Success.ShouldBeFalse();
            wrongActor.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
            QueryResult wrongTenant = await QueryAsync(gatewaySidecar, executorQuery with { TenantId = tenant + "-foreign" }, token).ConfigureAwait(false);
            wrongTenant.Success.ShouldBeFalse();
            wrongTenant.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
            identity.Unavailable = true;
            try
            {
                QueryResult unavailable = await QueryAsync(gatewaySidecar, executorQuery, token).ConfigureAwait(false);
                unavailable.Success.ShouldBeFalse();
                unavailable.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
            }
            finally { identity.Unavailable = false; }
            using HttpResponseMessage wrongCaller = await WorksPlatformProofRequests.PostAsync(worksSidecar, "works", "/query", executorQuery,
                WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceQuery]), token).ConfigureAwait(false);
            await RequireStatusAsync(wrongCaller, "historical-wrong-caller", token, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden).ConfigureAwait(false);
            using HttpClient direct = app.CreateHttpClient("works");
            using var directRequest = new HttpRequestMessage(HttpMethod.Post, "/query") { Content = JsonContent.Create(executorQuery) };
            directRequest.Headers.TryAddWithoutValidation(EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName,
                WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceQuery]));
            using HttpResponseMessage wrongPort = await direct.SendAsync(directRequest, token).ConfigureAwait(false);
            await RequireStatusAsync(wrongPort, "historical-direct-port", token, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden).ConfigureAwait(false);
            (await wrongPort.Content.ReadAsStringAsync(token).ConfigureAwait(false)).ShouldNotContain(mine);
            using HttpResponseMessage forbiddenPublish = await worksSidecar.PostAsJsonAsync("v1.0/publish/pubsub/work.events", new { probe = "forbidden-publisher" }, token).ConfigureAwait(false);
            await RequireStatusAsync(forbiddenPublish, "historical-forbidden-publish", token, HttpStatusCode.Forbidden).ConfigureAwait(false);
            (await FamilyDigestAsync(coordinator, scope, ids, token).ConfigureAwait(false)).ShouldBe(unchanged);
            await EvidenceAsync("platform-executor-filtering-and-origin-tenant-publish-refusals", new
            {
                tenant, visible, own = ownDetail.GetPayload(), hidden = hidden.GetPayload(), wrongActor = wrongActor.ErrorMessage,
                wrongTenant = wrongTenant.ErrorMessage, identityCalls = identity.Calls, wrongCaller = (int)wrongCaller.StatusCode,
                wrongPort = (int)wrongPort.StatusCode, forbiddenPublish = (int)forbiddenPublish.StatusCode, unchangedFamily = unchanged,
                queryIdentity = identity.Evidence(executorQuery, identityCallsBefore),
                authority = "independent ephemeral RSA authenticated synthetic current-identity fixture; published Parties client",
            }, token).ConfigureAwait(false);

            string[] claimOutcomes = await Task.WhenAll(
                SubmitAsync(gateway, tenant, race, new ClaimWorkItem(tenantId, new WorkItemId(race), ownBinding), token),
                SubmitAsync(gateway, tenant, race, new ClaimWorkItem(tenantId, new WorkItemId(race), otherBinding), token)).ConfigureAwait(false);
            claimOutcomes.Order(StringComparer.Ordinal).ShouldBe(["Completed", "Rejected"]);
            StreamReadPage raceStream = await StreamAsync(gateway, tenant, race, token).ConfigureAwait(false);
            raceStream.Events.Count(item => Simple(item.EventTypeName) == nameof(WorkItemClaimed)).ShouldBe(1);
            raceStream.Events.Count(item => Simple(item.EventTypeName) == nameof(WorkItemTransitionRejected)).ShouldBe(1);
            WorkItemState raceReplay = Replay(raceStream);
            raceReplay.Status.ShouldBe(WorkItemStatus.InProgress);
            raceReplay.ExecutorBinding.ShouldBeOneOf(ownBinding, otherBinding);
            await WaitAsync(async () => (await ReadRollUpAsync(coordinator, scope, race, token).ConfigureAwait(false))?.Status == WorkItemStatus.InProgress, token).ConfigureAwait(false);
            (await SubmitAsync(gateway, tenant, parent, new SpawnChild(tenantId, new WorkItemId(parent), new WorkItemId(parent), "Refuse cycle"), token).ConfigureAwait(false)).ShouldBe("Rejected");
            StreamReadPage parentStream = await StreamAsync(gateway, tenant, parent, token).ConfigureAwait(false);
            parentStream.Events.Count(item => Simple(item.EventTypeName) == nameof(WorkItemTreeCycleRejected)).ShouldBe(1);
            Replay(parentStream).Status.ShouldBe(WorkItemStatus.Created);
            var topology = (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeTopologyKey, token).ConfigureAwait(false)).Value!.As<WorksRuntimeTopology>();
            topology.ChildrenByParent[parent].ShouldContain(child);
            topology.ChildrenByParent[parent].ShouldNotContain(parent);

            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, mine, nameof(ClaimWorkItem), new ClaimWorkItem(tenantId, new WorkItemId(mine), ownBinding), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, mine, nameof(ReportProgress), new ReportProgress(tenantId, new WorkItemId(mine), 2, new Unit("hour")), token).ConfigureAwait(false);
            AwaitCondition signal = AwaitCondition.ExternalSignal("synthetic-history-416");
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, mine, nameof(SuspendWorkItem), new SuspendWorkItem(tenantId, new WorkItemId(mine), [signal]), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, mine, nameof(ResumeWorkItem), new ResumeWorkItem(tenantId, new WorkItemId(mine), signal), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, mine, nameof(CompleteWorkItem), new CompleteWorkItem(tenantId, new WorkItemId(mine)), token).ConfigureAwait(false);
            StreamReadPage lifecycle = await StreamAsync(gateway, tenant, mine, token).ConfigureAwait(false);
            WorkItemState replay = Replay(lifecycle);
            replay.Status.ShouldBe(WorkItemStatus.Completed);
            replay.InitialEffort!.Done.ShouldBe(2);
            replay.Remaining.ShouldBe(8);
            replay.ExecutorBinding.ShouldBe(ownBinding);
            lifecycle.Events.Select(item => Simple(item.EventTypeName)).ShouldBe([nameof(WorkItemCreated), nameof(WorkItemAssigned), nameof(WorkItemClaimed),
                nameof(ProgressReported), nameof(WorkItemSuspended), nameof(WorkItemResumed), nameof(WorkItemCompleted)]);
            await WaitAsync(async () => (await ReadRollUpAsync(coordinator, scope, mine, token).ConfigureAwait(false))?.Status == WorkItemStatus.Completed, token).ConfigureAwait(false);
            await AwaitProjectionSourceHeadsAsync(coordinator, scope, gateway, ids, token).ConfigureAwait(false);
            persistedDigest = await FamilyDigestAsync(coordinator, scope, ids, token).ConfigureAwait(false);
            await EvidenceAsync("platform-historical-lifecycle-tree-concurrent-claim-persisted", new
            {
                tenant, lifecycle, raceStream, parentStream, topology, familyDigest = persistedDigest,
                claimOutcomes, replay = new { replay.Status, replay.InitialEffort, replay.Remaining, replay.ExecutorBinding },
            }, token).ConfigureAwait(false);
        }, configure, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, gateway, _sidecar, token) =>
        {
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new DaprReadModelStore(dapr);
            var coordinator = new SharedProjectionEpochCoordinator(store, store);
            var scope = new SharedProjectionScope("statestore", tenant, "work", WorksReadModelKeys.RuntimeFamily, ["works-runtime-v3-writer"]);
            // Every recorded value is re-observed after the restart; the persisted digest is only the comparison oracle.
            string restartedDigest = await FamilyDigestAsync(coordinator, scope, ids, token).ConfigureAwait(false);
            restartedDigest.ShouldBe(persistedDigest);
            WorkItemStatus? rolledUp = (await ReadRollUpAsync(coordinator, scope, mine, token).ConfigureAwait(false))?.Status;
            WorkItemStatus replayed = Replay(await StreamAsync(gateway, tenant, mine, token).ConfigureAwait(false)).Status;
            rolledUp.ShouldBe(WorkItemStatus.Completed);
            replayed.ShouldBe(WorkItemStatus.Completed);
            StreamReadPage raceStream = await StreamAsync(gateway, tenant, race, token).ConfigureAwait(false);
            int claimCount = raceStream.Events.Count(item => Simple(item.EventTypeName) == nameof(WorkItemClaimed));
            claimCount.ShouldBe(1);
            await EvidenceAsync("platform-historical-restart-replay-and-family", new
            {
                tenant, familyDigest = restartedDigest, claimCount, completed = rolledUp == WorkItemStatus.Completed && replayed == WorkItemStatus.Completed,
            }, token).ConfigureAwait(false);
        }, configure, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);
    }

    private static QueryEnvelope ExecutorQuery(string tenant, string owner)
        => new(tenant, "work", owner, WhatsNextQueryHandler.WhatsNextQueryType,
            JsonSerializer.SerializeToUtf8Bytes(new { partyId = Party, allWork = true, role = "coordinator" }), "platform-executor-query", Actor)
        { OriginalActorId = Actor, AuthenticatedWorkloadId = "eventstore", IsDelegated = true };

    private static async Task<QueryResult> QueryAsync(HttpClient sidecar, QueryEnvelope query, CancellationToken token)
    {
        using HttpResponseMessage response = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/query", query,
            WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceQuery]), token).ConfigureAwait(false);
        await RequireStatusAsync(response, "historical-query", token, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<QueryResult>(token).ConfigureAwait(false)).ShouldNotBeNull();
    }

    private static async Task<string> SubmitAsync<TCommand>(HttpClient client, string tenant, string owner, TCommand command, CancellationToken token)
    {
        string message = UniqueIdHelper.GenerateSortableUniqueStringId();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands")
        {
            Content = JsonContent.Create(new SubmitCommandRequest(message, tenant, "work", owner, typeof(TCommand).Name, JsonSerializer.SerializeToElement(command))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(tenant));
        using HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
        string responseBody = await ReadBoundedBodyAsync(response, token).ConfigureAwait(false);
        bool rejected = response.StatusCode == HttpStatusCode.UnprocessableEntity;
        if (rejected)
        {
            // The released synchronous gateway maps a persisted domain rejection to typed HTTP422.
            // Keep the exact losing rejection and subsequently require its durable terminal status.
            Type? expected = typeof(TCommand) == typeof(ClaimWorkItem) ? typeof(WorkItemTransitionRejected)
                : typeof(TCommand) == typeof(SpawnChild) ? typeof(WorkItemTreeCycleRejected) : null;
            expected.ShouldNotBeNull($"[historical-submit] Unexpected domain rejection for {typeof(TCommand).Name}: {responseBody}");
            using JsonDocument problem = JsonDocument.Parse(responseBody);
            problem.RootElement.GetProperty("rejectionType").GetString()!.Split(',')[0].ShouldBe(expected.FullName);
            problem.RootElement.GetProperty("tenantId").GetString().ShouldBe(tenant);
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted,
                $"[historical-submit] {typeof(TCommand).Name} expected202, got{(int)response.StatusCode}; body={responseBody}");
            SubmitCommandResponse accepted = JsonSerializer.Deserialize<SubmitCommandResponse>(responseBody, JsonSerializerOptions.Web).ShouldNotBeNull();
            (accepted.MessageId ?? message).ShouldBe(message);
        }
        string? terminal = null;
        await WaitAsync(async () =>
        {
            using var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/commands/status/" + Uri.EscapeDataString(message));
            statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(tenant));
            using HttpResponseMessage status = await client.SendAsync(statusRequest, token).ConfigureAwait(false);
            if (status.StatusCode == HttpStatusCode.NotFound) return false;
            status.EnsureSuccessStatusCode();
            JsonElement body = await status.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
            terminal = body.GetProperty("status").GetString();
            return terminal is "Completed" or "Rejected" or "PublishFailed" or "TimedOut";
        }, token).ConfigureAwait(false);
        if (rejected) terminal.ShouldBe("Rejected");
        return terminal!;
    }

    private static async Task RequireStatusAsync(HttpResponseMessage response, string phase, CancellationToken token, params HttpStatusCode[] expected)
    {
        if (expected.Contains(response.StatusCode)) return;
        string body = await ReadBoundedBodyAsync(response, token).ConfigureAwait(false);
        throw new InvalidOperationException($"[{phase}] Expected {string.Join(',', expected.Select(value => (int)value))}, got{(int)response.StatusCode}; body={body}");
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        await using var cleanup = stream.ConfigureAwait(false);
        byte[] bytes = new byte[16385];
        int length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, deadline.Token).ConfigureAwait(false);
        if (length == bytes.Length) throw new InvalidOperationException("[historical-response] Response exceeded the 16KiB diagnostic bound.");
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static async Task<StreamReadPage> StreamAsync(HttpClient gateway, string tenant, string owner, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
        { Content = JsonContent.Create(new { tenant, domain = "work", aggregateId = owner, fromSequence = 0L, pageSize = 100 }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(tenant));
        using HttpResponseMessage response = await gateway.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        StreamReadPage page = (await response.Content.ReadFromJsonAsync<StreamReadPage>(token).ConfigureAwait(false)).ShouldNotBeNull();
        page.Tenant.ShouldBe(tenant);
        page.AggregateId.ShouldBe(owner);
        page.Metadata.IsTruncated.ShouldBeFalse();
        page.Events.Count.ShouldBe(page.Metadata.EventCount);
        page.Events.Select(item => item.SequenceNumber).ShouldBe(Enumerable.Range(1, page.Events.Count).Select(position => (long)position));
        return page;
    }

    private static WorkItemState Replay(StreamReadPage page)
    {
        WorkItemState state = new();
        Type[] types = typeof(WorkItemCreated).Assembly.GetTypes().Where(type => typeof(IEventPayload).IsAssignableFrom(type)).ToArray();
        foreach (StreamReadEvent item in page.Events)
        {
            Type type = types.Single(type => type.Name == Simple(item.EventTypeName));
            object payload = JsonSerializer.Deserialize(item.Payload, type, JsonSerializerOptions.Web).ShouldNotBeNull();
            MethodInfo apply = typeof(WorkItemState).GetMethod("Apply", [type]).ShouldNotBeNull();
            apply.Invoke(state, [payload]);
        }
        return state;
    }

    private static string Simple(string type) => type.Split('.').Last();

    private static async Task<WorkItemRollUp?> ReadRollUpAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope, string owner, CancellationToken token)
        => (await coordinator.ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeRollUpKey(owner), token).ConfigureAwait(false)).Value?.As<WorkItemRollUp>();

    private static async Task AwaitProjectionSourceHeadsAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope,
        HttpClient gateway, string[] owners, CancellationToken token)
    {
        foreach (string owner in owners)
        {
            StreamReadPage stream = await StreamAsync(gateway, scope.TenantId, owner, token).ConfigureAwait(false);
            StreamReadEvent last = stream.Events[^1];
            await WaitAsync(async () =>
            {
                SharedProjectionCheckpoint? checkpoint = await coordinator.GetCheckpointAsync(scope, owner, token).ConfigureAwait(false);
                return checkpoint?.OwnerSequenceNumber == last.SequenceNumber && checkpoint.Position == last.GlobalPosition;
            }, token).ConfigureAwait(false);
        }
    }

    private static async Task<string> FamilyDigestAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope, string[] owners, CancellationToken token)
    {
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            .. owners.SelectMany(owner => new[] { WorksReadModelKeys.RuntimeRollUpKey(owner), WorksReadModelKeys.RuntimeHistoryKey(owner), WorksReadModelKeys.RuntimeParkingKey(owner) })];
        var family = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, token).ConfigureAwait(false);
        family.IsAvailable.ShouldBeTrue();
        family.IsStale.ShouldBeFalse();
        family.IsDegraded.ShouldBeFalse();
        family.Value.ShouldNotBeNull().Values.ShouldAllBe(value => value != null);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(family.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static async Task<DaprClient> DaprAsync(DistributedApplication app, CancellationToken token)
    {
        ResourceEvent resource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", token, token).ConfigureAwait(false);
        string Endpoint(string key) => resource.Snapshot.EnvironmentVariables.Single(variable => variable.Name == key).Value!;
        return new DaprClientBuilder().UseHttpEndpoint(Endpoint("DAPR_HTTP_ENDPOINT")).UseGrpcEndpoint(Endpoint("DAPR_GRPC_ENDPOINT")).Build();
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (!await condition().ConfigureAwait(false)) await Task.Delay(200, deadline.Token).ConfigureAwait(false);
    }

    private static async Task EvidenceAsync(string phase, object state, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } path)
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new { phase, state }) + Environment.NewLine, token).ConfigureAwait(false);
    }
}
