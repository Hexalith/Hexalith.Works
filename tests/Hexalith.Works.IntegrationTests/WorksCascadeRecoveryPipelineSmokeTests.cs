using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.Testing;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 4.7 live proof: pub/sub resumes a parent after child completion, drives a parent-terminal cascade,
/// and restart replays an interrupted checkpoint.
/// </summary>
/// <remarks>
/// This Tier-3 lane requires the same Redis, Dapr placement, scheduler, and Docker prerequisites as the other
/// Aspire pipeline tests. It first proves the completed-child consumer re-reads the parent await and submits a
/// live resume. It then widens the boundary between two cascade targets with the operational pacing option,
/// stops after the first child reaches terminal state, and restarts against the same durable store to prove both
/// descendants converge with exactly one accepted terminal event each.
/// </remarks>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksCascadeRecoveryPipelineSmokeTests
{
    private const string DevSigningKey = "DevOnlySigningKey-AtLeast32Chars!";
    private const string Tenant = "tenant-cascade-recovery";
    private const int InterruptedTargetIntervalMilliseconds = 20_000;

    private static readonly string s_runId = Guid.NewGuid().ToString("N")[..12];
    private static readonly string s_parent = $"work-cascade-parent-{s_runId}";
    private static readonly string s_firstChild = $"work-cascade-child-a-{s_runId}";
    private static readonly string s_secondChild = $"work-cascade-child-b-{s_runId}";
    private static readonly string s_awaitingParent = $"work-awaiting-parent-{s_runId}";
    private static readonly string s_completedChild = $"work-completed-child-{s_runId}";

    /// <summary>Proves live child-completion resume, cancellation delivery, and durable mid-cascade restart convergence.</summary>
    [Fact]
    public async Task Reactor_translators_run_live_and_interrupted_cascade_converges_after_restart()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        if (!await PrerequisitesAvailableAsync(cancellationToken).ConfigureAwait(true))
        {
            Assert.Skip(
                "Aspire cascade-recovery prerequisites missing (Redis :6379 + Dapr placement :50005 + scheduler :50006). "
                + "Start Docker, run `dapr init`, and start the placement/scheduler services to run this lane.");
            return;
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        // Phase 1: the live parent-terminal subscription dispatches child A, then the pacing interval creates
        // a deterministic stop boundary before child B. Disposing the AppHost simulates the process loss.
        await WithAppHostAsync(cancellationToken, InterruptedTargetIntervalMilliseconds, async (client, token) =>
        {
            await ProveChildCompletionResumeAsync(client, token).ConfigureAwait(false);
            await CreateTreeAsync(client, token).ConfigureAwait(false);
            await SubmitToTerminalAsync(
                client,
                s_parent,
                nameof(CancelWorkItem),
                new CancelWorkItem(new TenantId(Tenant), new WorkItemId(s_parent)),
                token).ConfigureAwait(false);

            (await WaitForEventCountAsync(client, s_firstChild, nameof(WorkItemCancelled), 1, token).ConfigureAwait(false))
                .ShouldBe(1, "the first child proves the parent cancellation reached the live Works subscription");
            (await CountEventsAsync(client, s_secondChild, nameof(WorkItemCancelled), token).ConfigureAwait(false))
                .ShouldBe(0, "the second child must still be outstanding when the first AppHost stops");
        }).ConfigureAwait(true);

        // Phase 2: no tenant list or parent redelivery is needed for discovery. The startup service reads the
        // durable incomplete index, replays the checkpoint, and the target aggregate absorbs any redelivery.
        await WithAppHostAsync(cancellationToken, cascadeTargetIntervalMilliseconds: 0, async (client, token) =>
        {
            (await WaitForEventCountAsync(client, s_secondChild, nameof(WorkItemCancelled), 1, token).ConfigureAwait(false))
                .ShouldBe(1, "startup checkpoint replay must terminate the outstanding child");
            (await CountEventsAsync(client, s_firstChild, nameof(WorkItemCancelled), token).ConfigureAwait(false))
                .ShouldBe(1, "replay must not append a duplicate terminal event to the completed first child");
            (await CountEventsAsync(client, s_secondChild, nameof(WorkItemCancelled), token).ConfigureAwait(false))
                .ShouldBe(1, "redelivery and replay converge to exactly one accepted terminal event");
        }).ConfigureAwait(true);
    }

    private static async Task ProveChildCompletionResumeAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var tenant = new TenantId(Tenant);
        var parentId = new WorkItemId(s_awaitingParent);
        var childId = new WorkItemId(s_completedChild);
        var binding = new ExecutorBinding(new PartyId("story-47-worker"), Channel.Cli, AuthorityLevel.Contribute);

        await SubmitToTerminalAsync(
            client,
            s_awaitingParent,
            nameof(CreateWorkItem),
            new CreateWorkItem(tenant, parentId, "Await child completion"),
            cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            s_awaitingParent,
            nameof(AssignWorkItem),
            new AssignWorkItem(tenant, parentId, binding),
            cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            s_awaitingParent,
            nameof(ClaimWorkItem),
            new ClaimWorkItem(tenant, parentId, binding),
            cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            s_awaitingParent,
            nameof(SuspendWorkItem),
            new SuspendWorkItem(tenant, parentId, [AwaitCondition.ChildCompleted(childId)]),
            cancellationToken).ConfigureAwait(false);

        await SubmitToTerminalAsync(
            client,
            s_completedChild,
            nameof(CreateWorkItem),
            new CreateWorkItem(
                tenant,
                childId,
                "Complete and resume parent",
                Parent: new ParentWorkItemReference(tenant, parentId)),
            cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            s_completedChild,
            nameof(AssignWorkItem),
            new AssignWorkItem(tenant, childId, binding),
            cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            s_completedChild,
            nameof(ClaimWorkItem),
            new ClaimWorkItem(tenant, childId, binding),
            cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            s_completedChild,
            nameof(CompleteWorkItem),
            new CompleteWorkItem(tenant, childId),
            cancellationToken).ConfigureAwait(false);

        (await WaitForEventCountAsync(
            client,
            s_awaitingParent,
            nameof(WorkItemResumed),
            1,
            cancellationToken).ConfigureAwait(false))
            .ShouldBe(1, "the live completed-child subscription must resume the awaiting parent exactly once");
    }

    private static async Task CreateTreeAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var tenant = new TenantId(Tenant);
        var parentId = new WorkItemId(s_parent);

        await SubmitToTerminalAsync(
            client,
            s_parent,
            nameof(CreateWorkItem),
            new CreateWorkItem(tenant, parentId, "Cascade parent"),
            cancellationToken).ConfigureAwait(false);

        foreach (string child in new[] { s_firstChild, s_secondChild })
        {
            var childId = new WorkItemId(child);
            await SubmitToTerminalAsync(
                client,
                child,
                nameof(CreateWorkItem),
                new CreateWorkItem(
                    tenant,
                    childId,
                    "Cascade child",
                    Parent: new ParentWorkItemReference(tenant, parentId)),
                cancellationToken).ConfigureAwait(false);
            await SubmitToTerminalAsync(
                client,
                s_parent,
                nameof(SpawnChild),
                new SpawnChild(tenant, parentId, childId, "Cascade child"),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WithAppHostAsync(
        CancellationToken cancellationToken,
        int cascadeTargetIntervalMilliseconds,
        Func<HttpClient, CancellationToken, Task> body)
    {
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(TimeSpan.FromMinutes(5));

        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(
            [
                "--EnableKeycloak=false",
                $"--Works:Recovery:CascadeTargetIntervalMilliseconds={cascadeTargetIntervalMilliseconds}",
            ],
            startupCts.Token)
            .ConfigureAwait(true);
        WorksAppHostTestReadiness.ConfigureHarnessLogging(builder);
        DistributedApplication app = await builder.BuildAsync(startupCts.Token).ConfigureAwait(true);
        try
        {
            await app.StartAsync(startupCts.Token).ConfigureAwait(true);
            await app.ResourceNotifications.WaitForResourceHealthyAsync("eventstore", startupCts.Token).ConfigureAwait(true);
            await app.ResourceNotifications.WaitForResourceHealthyAsync("works", startupCts.Token).ConfigureAwait(true);

            using HttpClient client = app.CreateHttpClient("eventstore");
            client.Timeout = TimeSpan.FromSeconds(60);
            await WorksAppHostTestReadiness
                .WaitForEventStoreCommandRuntimeAsync(client, startupCts.Token)
                .ConfigureAwait(true);
            await body(client, startupCts.Token).ConfigureAwait(true);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(true);
            await builder.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static async Task SubmitToTerminalAsync<TCommand>(
        HttpClient client,
        string aggregateId,
        string commandType,
        TCommand command,
        CancellationToken cancellationToken)
    {
        var body = new SubmitCommandRequest(
            Guid.NewGuid().ToString(),
            Tenant,
            "work",
            aggregateId,
            commandType,
            JsonSerializer.SerializeToElement(command));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, $"{commandType} submission must return 202 Accepted.");
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        string correlationId = result.GetProperty("correlationId").GetString()!;
        string status = await PollToTerminalAsync(client, correlationId, cancellationToken).ConfigureAwait(false);
        status.ShouldBe("Completed", $"{commandType} must reach Completed before the live cascade advances.");
    }

    private static async Task<string> PollToTerminalAsync(
        HttpClient client,
        string correlationId,
        CancellationToken cancellationToken)
    {
        string status = "unknown";
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/commands/status/{correlationId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
                status = body.GetProperty("status").GetString() ?? "unknown";
                if (status is "Completed" or "Rejected" or "PublishFailed" or "TimedOut")
                {
                    return status;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return status;
    }

    private static async Task<int> WaitForEventCountAsync(
        HttpClient client,
        string aggregateId,
        string eventType,
        int atLeast,
        CancellationToken cancellationToken)
    {
        int count = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            count = await CountEventsAsync(client, aggregateId, eventType, cancellationToken).ConfigureAwait(false);
            if (count >= atLeast)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return count;
    }

    private static async Task<int> CountEventsAsync(
        HttpClient client,
        string aggregateId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            tenant = Tenant,
            domain = "work",
            aggregateId,
            fromSequence = 0L,
            pageSize = 100,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "Per-aggregate stream read must return 200 OK.");

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        return page.GetProperty("events")
            .EnumerateArray()
            .Count(value => string.Equals(
                SimpleTypeName(value.GetProperty("eventTypeName").GetString() ?? string.Empty),
                eventType,
                StringComparison.Ordinal));
    }

    private static string SimpleTypeName(string typeName)
    {
        int lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    private static string MintToken()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "works-cascade-recovery-user"),
                new Claim("tenants", JsonSerializer.Serialize(new[] { Tenant })),
                new Claim("domains", JsonSerializer.Serialize(new[] { "work" })),
                new Claim("permissions", JsonSerializer.Serialize(new[] { "command:submit", "command:query", "command:replay" })),
            ]),
            Issuer = "hexalith-dev",
            Audience = "hexalith-eventstore",
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevSigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static async Task<bool> PrerequisitesAvailableAsync(CancellationToken cancellationToken)
    {
        int placementPort = OperatingSystem.IsWindows() ? 6050 : 50005;
        int schedulerPort = OperatingSystem.IsWindows() ? 6060 : 50006;
        return await IsPortReachableAsync(6379, cancellationToken).ConfigureAwait(false)
            && await IsPortReachableAsync(placementPort, cancellationToken).ConfigureAwait(false)
            && await IsPortReachableAsync(schedulerPort, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsPortReachableAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync("localhost", port, probeCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
