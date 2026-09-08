using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reminders;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// The shared Tier-3 harness for the live Works AppHost lanes: prerequisite gating, AppHost start/dispose,
/// dev-JWT command submission, and per-aggregate stream observation. Every fact that needs the full topology
/// drives it through here so the lanes cannot drift apart.
/// </summary>
internal static class WorksAppHostSmokeHarness
{
    private const string DevSigningKey = "DevOnlySigningKey-AtLeast32Chars!";

    /// <summary>The Redis instance a <c>dapr init</c> leaves running.</summary>
    private const int DaprInitRedisPort = 6379;

    /// <summary>
    /// The fixed local ports the AppHost-owned mTLS control plane binds. Every live fact runs with
    /// <c>--DcpPublisher:RandomizePorts=false</c>, so a foreign listener on any of them makes the AppHost hang
    /// in <c>StartAsync</c> until the startup budget expires instead of failing usefully — the gate skips
    /// instead, naming the port.
    /// </summary>
    /// <summary>How long a control-plane port may still be held by a previous fact's teardown before it counts as foreign.</summary>
    private static readonly TimeSpan ControlPlanePortWait = TimeSpan.FromSeconds(60);

    private static readonly (int Port, string Name)[] ControlPlanePorts =
    [
        (50001, "dapr-sentry"),
        (51005, "dapr-placement-mtls"),
        (51006, "dapr-scheduler-mtls"),
    ];

    /// <summary>
    /// Returns why this lane cannot run, or <see langword="null"/> when every prerequisite is satisfied.
    /// </summary>
    public static async Task<string?> PrerequisiteGapAsync(CancellationToken cancellationToken)
    {
        if (!await IsPortReachableAsync(DaprInitRedisPort, cancellationToken).ConfigureAwait(false))
        {
            return $"the dapr-init Redis on :{DaprInitRedisPort} is not reachable. Start Docker and run `dapr init`";
        }

        foreach ((int port, string name) in ControlPlanePorts)
        {
            // Give an ordinary teardown lag time to release the port before deciding it is foreign-held: the
            // previous fact's session containers and DCP proxies can outlive the app's dispose by a few seconds.
            if (!await WaitForPortToFreeAsync(port, cancellationToken).ConfigureAwait(false))
            {
                return $"local port {port} is still in use after {ControlPlanePortWait.TotalSeconds:0} seconds, so the "
                    + $"AppHost-owned '{name}' resource cannot bind it. Stop the process holding it (a leaked DCP "
                    + "controller or container from a previous AppHost run, or a `dapr init` control plane) before running this lane";
            }
        }

        return null;
    }

    /// <summary>
    /// Starts the full AppHost topology with NO recovery-tenant configuration (Story 4.8: recovery discovers
    /// tenants from the durable registry), waits for eventstore + works to be healthy, runs the body against the
    /// eventstore gateway client, and disposes both the application and the builder — so the next call is a
    /// genuine restart.
    /// </summary>
    public static async Task WithAppHostAsync(
        CancellationToken cancellationToken,
        Func<DistributedApplication, HttpClient, HttpClient, CancellationToken, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(TimeSpan.FromMinutes(5));

        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(
                [
                    "--EnableKeycloak=false",
                    "--environment=Development",
                    // These acceptance facts share fixed localhost control-plane addresses and are serialized by
                    // WorksAppHostTestCollection. Preserve those ports instead of the testing builder's default
                    // randomization so daprd reaches the AppHost-owned placement and scheduler proxies.
                    "--DcpPublisher:RandomizePorts=false",
                ],
                startupCts.Token)
            .ConfigureAwait(true);

        WorksAppHostTestReadiness.ConfigureHarnessLogging(builder);
        DistributedApplication app = await builder.BuildAsync(startupCts.Token).ConfigureAwait(true);
        try
        {
            try
            {
                await app.StartAsync(startupCts.Token).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "[startup/readiness] The Aspire AppHost could not start the live Works topology. "
                    + WorksAppHostTestReadiness.DescribeResourceStates(
                        app,
                        [
                            "dapr-sentry",
                            "dapr-placement-mtls",
                            "dapr-scheduler-mtls",
                            "eventstore",
                            "works",
                            "eventstore-operations",
                            "eventstore-admin",
                        ]),
                    ex);
            }

            _ = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "eventstore", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
            ResourceEvent worksResource = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "works", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);

            using HttpClient client = app.CreateHttpClient("eventstore");
            client.Timeout = TimeSpan.FromSeconds(60);
            await WorksAppHostTestReadiness
                .WaitForEventStoreCommandRuntimeAsync(client, startupCts.Token)
                .ConfigureAwait(true);
            using HttpClient sidecarClient = WorksAppHostTestReadiness.CreateWorksDaprClient(worksResource);
            await WorksAppHostTestReadiness
                .WaitForWorksActorRuntimeAsync(sidecarClient, startupCts.Token)
                .ConfigureAwait(true);
            try
            {
                await body(app, client, sidecarClient, startupCts.Token).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Preserve the body's own phase tag ([registration], [delivery], [submission], …) instead of
                // relabelling every failure as [runtime]: the phase is the first thing a triage read needs.
                throw new InvalidOperationException(
                    $"Live AppHost body failed after startup/readiness probes passed. {DescribePhase(ex)}",
                    ex);
            }
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(true);
            await builder.DisposeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Create → Assign → Claim → Suspend(DateReached instant): drives the work item to Suspended parked on the
    /// deterministic date await.
    /// </summary>
    public static async Task ParkSuspendedOnDateAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        var tenant = new TenantId(tenantId);
        var workItem = new WorkItemId(workItemId);
        var binding = new ExecutorBinding(new PartyId("recovery-worker"), Channel.Cli, AuthorityLevel.Contribute);

        await SubmitToTerminalAsync(client, tenantId, workItemId, nameof(CreateWorkItem), new CreateWorkItem(tenant, workItem, "Reminder-recovery obligation"), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, tenantId, workItemId, nameof(AssignWorkItem), new AssignWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, tenantId, workItemId, nameof(ClaimWorkItem), new ClaimWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            tenantId,
            workItemId,
            nameof(SuspendWorkItem),
            new SuspendWorkItem(tenant, workItem, [AwaitCondition.DateReached(instant)]),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Submits one command and asserts it reaches the Completed terminal status.</summary>
    public static async Task SubmitToTerminalAsync<TCommand>(
        HttpClient client,
        string tenantId,
        string workItemId,
        string commandType,
        TCommand command,
        CancellationToken cancellationToken)
    {
        string correlationId = await SubmitCommandAsync(
            client,
            tenantId,
            workItemId,
            messageId: Guid.NewGuid().ToString(),
            commandType: commandType,
            payload: JsonSerializer.SerializeToElement(command),
            correlationId: null,
            cancellationToken).ConfigureAwait(false);

        string status = await PollToTerminalAsync(client, tenantId, correlationId, cancellationToken).ConfigureAwait(false);
        status.ShouldBe("Completed", $"{commandType} must persist and publish to a Completed terminal status while parking the item.");
    }

    /// <summary>The deterministic pending await for one parked work item.</summary>
    public static PendingDateAwait PendingAwait(string tenantId, string workItemId, DateTimeOffset instant)
        => new(tenantId, workItemId, instant, AwaitCondition.DateReached(instant).CorrelationKey);

    /// <summary>Polls the per-aggregate stream until the accepted resume count is reached, or times out.</summary>
    public static async Task<int> WaitForResumedCountAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        int atLeast,
        CancellationToken cancellationToken,
        DateTimeOffset? deadline = null)
    {
        int count = 0;
        DateTimeOffset effectiveDeadline = deadline ?? DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < effectiveDeadline)
        {
            count = await CountResumedAsync(client, tenantId, workItemId, cancellationToken).ConfigureAwait(false);
            if (count >= atLeast)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"[delivery] Work item '{workItemId}' did not reach {atLeast} accepted {nameof(WorkItemResumed)} "
            + $"event(s) by {effectiveDeadline:O}; observed {count}.");
    }

    /// <summary>Counts accepted <c>WorkItemResumed</c> events in the item's re-readable per-aggregate stream.</summary>
    public static async Task<int> CountResumedAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            tenant = tenantId,
            domain = "work",
            aggregateId = workItemId,
            fromSequence = 0L,
            pageSize = 100,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(tenantId));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "Per-aggregate stream read must return 200 OK for the parked work item.");

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);

        int resumed = 0;
        foreach (JsonElement streamEvent in page.GetProperty("events").EnumerateArray())
        {
            string? typeName = streamEvent.GetProperty("eventTypeName").GetString();
            if (typeName is not null && SimpleTypeName(typeName) == nameof(WorkItemResumed))
            {
                resumed++;
            }
        }

        return resumed;
    }

    /// <summary>Mints a development bearer token scoped to one tenant.</summary>
    public static string MintToken(string tenantId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "works-reminder-recovery-user"),
                new Claim("tenants", JsonSerializer.Serialize(new[] { tenantId })),
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

    private static async Task<string> SubmitCommandAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        string messageId,
        string commandType,
        JsonElement payload,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var body = new SubmitCommandRequest(
            MessageId: messageId,
            Tenant: tenantId,
            Domain: "work",
            AggregateId: workItemId,
            CommandType: commandType,
            Payload: payload,
            CorrelationId: correlationId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(tenantId));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string diagnosticBody = responseBody.Length <= 2_000 ? responseBody : responseBody[..2_000] + "…";
        response.StatusCode.ShouldBe(
            HttpStatusCode.Accepted,
            $"[submission] {commandType} must return 202 Accepted. Body={diagnosticBody}");

        JsonElement result = JsonSerializer.Deserialize<JsonElement>(responseBody);
        return result.GetProperty("correlationId").GetString()!;
    }

    private static async Task<string> PollToTerminalAsync(
        HttpClient client,
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        string status = "unknown";
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/commands/status/{correlationId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(tenantId));

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

    /// <summary>Extracts the innermost phase tag a live helper attached, or reports that none was carried.</summary>
    private static string DescribePhase(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message;
            int close = message.IndexOf(']');
            if (message.StartsWith('[') && close > 0)
            {
                return $"Phase={message[..(close + 1)]}";
            }
        }

        return "Phase=[runtime] (the failure carried no phase tag).";
    }

    private static string SimpleTypeName(string typeName)
    {
        int lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    /// <summary>Polls one port until nothing is listening on it, or the bounded wait is spent.</summary>
    private static async Task<bool> WaitForPortToFreeAsync(int port, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + ControlPlanePortWait;
        while (true)
        {
            if (!await IsPortReachableAsync(port, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
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
