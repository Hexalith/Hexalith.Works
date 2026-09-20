using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
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

    /// <summary>How long a control-plane resource may still be held by a previous fact's teardown before it counts as foreign.</summary>
    private static readonly TimeSpan ControlPlaneReleaseWait = TimeSpan.FromSeconds(60);

    /// <summary>How long one non-cancelable asynchronous disposer may delay teardown diagnostics.</summary>
    private static readonly TimeSpan DisposalWait = TimeSpan.FromSeconds(30);

    /// <summary>How long one Docker ownership probe may run before it is terminated and classified.</summary>
    private static readonly TimeSpan DockerProbeWait = TimeSpan.FromSeconds(10);

    /// <summary>How long a terminated Docker probe may take to exit before teardown continues.</summary>
    private static readonly TimeSpan DockerProbeTerminationWait = TimeSpan.FromSeconds(5);

    /// <summary>The persistent Scheduler volume that must have exactly one owner at a time.</summary>
    private const string SchedulerVolumeName = "hexalith-works-dapr-scheduler";

    /// <summary>The fixed local ports owned by the mTLS Sentry, placement, and scheduler resources.</summary>
    private static readonly (int Port, string Name)[] ControlPlanePorts =
    [
        (50001, "dapr-sentry"),
        (51005, "dapr-placement-mtls"),
        (51006, "dapr-scheduler-mtls"),
    ];

    /// <summary>Both loopback address families that must be free before a fixed-port topology starts.</summary>
    private static readonly IPAddress[] ControlPlaneLoopbacks = [IPAddress.Loopback, IPAddress.IPv6Loopback];

    /// <summary>
    /// Returns why this lane cannot run, or <see langword="null"/> when every prerequisite is satisfied.
    /// </summary>
    public static async Task<string?> PrerequisiteGapAsync(CancellationToken cancellationToken)
    {
        if (!await IsPortReachableAsync(DaprInitRedisPort, cancellationToken).ConfigureAwait(false))
        {
            return $"the dapr-init Redis on :{DaprInitRedisPort} is not reachable. Start Docker and run `dapr init`";
        }

        try
        {
            await WaitForControlPlaneResourcesReleasedAsync("prerequisite", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
        {
            return exception.Message;
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

        // A restart fact calls this method twice after only one prerequisite probe. Recheck immediately before
        // every start so teardown lag from the first run cannot race the second scheduler/placement bind. Once
        // the explicit prerequisite probe has passed, a later occupied resource is a restart-boundary failure: it
        // must never turn the acceptance fact into a successful skip.
        await WaitForControlPlaneResourcesReleasedAsync("startup boundary", cancellationToken).ConfigureAwait(false);

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // A clean checkout now builds the runtime-only EventStore Operations project during the first live start.
        // Keep that cold-build path inside the readiness gate without borrowing from the acceptance-body budget.
        startupCts.CancelAfter(TimeSpan.FromMinutes(10));

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
        DistributedApplication? app = null;
        Exception? primaryException = null;
        try
        {
            app = await builder.BuildAsync(startupCts.Token).ConfigureAwait(true);
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
                .WaitForResourceHealthyAsync(app, "dapr-sentry", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
            _ = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "dapr-placement-mtls", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
            _ = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "dapr-scheduler-mtls", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
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
                // The bounded token is a startup/readiness budget, not a lifetime budget for the acceptance
                // body. Operations is now built as a runtime-only project resource, so consuming build/start time
                // from reminder deadlines can cancel an otherwise healthy future-reminder fact before it fires.
                // Body helpers carry their own bounded polling deadlines and remain linked to the test runner's
                // cancellation token.
                await body(app, client, sidecarClient, cancellationToken).ConfigureAwait(true);
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
        catch (Exception exception)
        {
            primaryException = exception;
        }

        var cleanupFailures = new List<Exception>();
        if (app is not null)
        {
            try
            {
                await app.DisposeAsync().AsTask().WaitAsync(DisposalWait).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(new InvalidOperationException("[teardown] Distributed application disposal failed.", exception));
            }
        }

        try
        {
            await builder.DisposeAsync().AsTask().WaitAsync(DisposalWait).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(new InvalidOperationException("[teardown] AppHost testing builder disposal failed.", exception));
        }

        // Dispose returning does not guarantee DCP has released its fixed sockets or removed the Scheduler
        // container that owns the persistent volume. Settle both with an independent token so cancellation of
        // the test body cannot prevent cleanup. A clean body must fail when cleanup remains occupied; when the
        // body already failed, retain that primary exception and emit every cleanup failure as diagnostics.
        using (var teardownCts = new CancellationTokenSource(ControlPlaneReleaseWait + TimeSpan.FromSeconds(15)))
        {
            try
            {
                await WaitForControlPlaneResourcesReleasedAsync("teardown boundary", teardownCts.Token)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(new InvalidOperationException(
                    "[teardown] AppHost control-plane resources did not settle.",
                    exception));
            }
        }

        if (primaryException is not null)
        {
            foreach (Exception cleanupFailure in cleanupFailures)
            {
                TestContext.Current.SendDiagnosticMessage(
                    "Live AppHost cleanup also failed after the primary acceptance failure: {0}",
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(primaryException).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "[teardown] The live AppHost body passed, but cleanup did not settle completely.",
                cleanupFailures);
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

    /// <summary>
    /// Waits until every fixed control-plane port is free and no container still owns the persistent Scheduler
    /// volume at a start or teardown boundary.
    /// </summary>
    private static async Task WaitForControlPlaneResourcesReleasedAsync(
        string boundary,
        CancellationToken cancellationToken)
        => await WaitForControlPlaneResourcesReleasedAsync(
            boundary,
            ControlPlaneReleaseWait,
            TimeSpan.FromSeconds(1),
            IsPortAvailableForExclusiveBind,
            SchedulerVolumeOwnersAsync,
            static (delay, token) => Task.Delay(delay, token),
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Waits for the fixed control-plane resources using the supplied probes and bounded retry policy.
    /// </summary>
    /// <param name="boundary">The diagnostic name for the start or teardown boundary.</param>
    /// <param name="releaseWait">The total time allowed for resources to settle.</param>
    /// <param name="retryDelay">The delay between observations.</param>
    /// <param name="portAvailable">Returns whether one address and port can be bound exclusively.</param>
    /// <param name="schedulerVolumeOwners">Returns every container owning the persistent Scheduler volume.</param>
    /// <param name="delayAsync">Applies the retry delay.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    internal static async Task WaitForControlPlaneResourcesReleasedAsync(
        string boundary,
        TimeSpan releaseWait,
        TimeSpan retryDelay,
        Func<IPAddress, int, bool> portAvailable,
        Func<CancellationToken, Task<IReadOnlyList<string>>> schedulerVolumeOwners,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        ArgumentOutOfRangeException.ThrowIfLessThan(releaseWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(portAvailable);
        ArgumentNullException.ThrowIfNull(schedulerVolumeOwners);
        ArgumentNullException.ThrowIfNull(delayAsync);

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var occupied = new List<string>();
            foreach ((int port, string name) in ControlPlanePorts)
            {
                foreach (IPAddress address in ControlPlaneLoopbacks)
                {
                    if (!portAvailable(address, port))
                    {
                        occupied.Add($"{name} on {address}:{port}");
                    }
                }
            }

            try
            {
                IReadOnlyList<string> owners = await schedulerVolumeOwners(cancellationToken).ConfigureAwait(false);
                occupied.AddRange(owners.Select(static owner => $"scheduler volume owner {owner}"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SchedulerVolumeProbeCleanupException)
            {
                // Starting another Docker probe after adapter cleanup could not confirm child termination would
                // accumulate children. Surface the actionable cleanup diagnostic immediately.
                throw;
            }
            catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
            {
                occupied.Add($"scheduler volume ownership probe unavailable ({exception.Message})");
            }

            if (occupied.Count == 0)
            {
                return;
            }

            if (elapsed.Elapsed >= releaseWait)
            {
                throw new TimeoutException(
                    $"[{boundary}] Fixed AppHost control-plane resources were still occupied after "
                    + $"{releaseWait.TotalSeconds:0} seconds: {string.Join(", ", occupied)}. "
                    + "Stop leaked DCP controllers/containers or another AppHost using this Works control plane before retrying.");
            }

            await delayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> SchedulerVolumeOwnersAsync(CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateSchedulerVolumeProbeStartInfo();

        ISchedulerVolumeProbe probe;
        try
        {
            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Docker to inspect the Scheduler volume owner.");
            probe = new ProcessSchedulerVolumeProbe(process);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Could not start Docker to inspect the Scheduler volume owner.",
                exception);
        }

        return await RunAndDisposeSchedulerVolumeProbeAsync(
            probe,
            DockerProbeWait,
            DockerProbeTerminationWait,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs and disposes one Scheduler-volume probe with production exception precedence.</summary>
    internal static async Task<IReadOnlyList<string>> RunAndDisposeSchedulerVolumeProbeAsync(
        ISchedulerVolumeProbe probe,
        TimeSpan probeWait,
        TimeSpan terminationWait,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);

        IReadOnlyList<string>? owners = null;
        Exception? probeFailure = null;
        try
        {
            owners = await RunSchedulerVolumeProbeAsync(
                probe,
                probeWait,
                terminationWait,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            probeFailure = exception;
        }

        Exception? disposalFailure = null;
        try
        {
            probe.Dispose();
        }
        catch (Exception exception)
        {
            disposalFailure = exception;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (disposalFailure is not null)
        {
            Exception classifiedFailure = probeFailure is null
                ? disposalFailure
                : new AggregateException(probeFailure, disposalFailure);
            if (disposalFailure is SchedulerVolumeProbeCleanupException)
            {
                throw new SchedulerVolumeProbeCleanupException(
                    "Docker Scheduler-volume probe disposal could not confirm child termination.",
                    classifiedFailure);
            }

            throw new InvalidOperationException(
                "Docker Scheduler-volume probe disposal encountered a classified cleanup failure.",
                classifiedFailure);
        }

        if (probeFailure is not null)
        {
            ExceptionDispatchInfo.Capture(probeFailure).Throw();
        }

        return owners!;
    }

    /// <summary>Runs one Scheduler-volume ownership probe with bounded execution and termination.</summary>
    /// <param name="probe">The process abstraction to execute.</param>
    /// <param name="probeWait">The maximum probe execution time.</param>
    /// <param name="terminationWait">The maximum wait after requesting termination.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    internal static async Task<IReadOnlyList<string>> RunSchedulerVolumeProbeAsync(
        ISchedulerVolumeProbe probe,
        TimeSpan probeWait,
        TimeSpan terminationWait,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(probeWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(terminationWait, TimeSpan.Zero);

        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(probeWait);
        Task<string> outputTask = StartProbeRead(
            () => probe.ReadStandardOutputAsync(probeCts.Token));
        Task<string> errorTask = StartProbeRead(
            () => probe.ReadStandardErrorAsync(probeCts.Token));
        bool waitingForExit = true;
        string error;
        int exitCode;
        string output;
        try
        {
            await probe.WaitForExitAsync(probeCts.Token).ConfigureAwait(false);
            waitingForExit = false;
            cancellationToken.ThrowIfCancellationRequested();
            string[] redirectedOutput = await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            output = redirectedOutput[0];
            error = redirectedOutput[1];
            exitCode = probe.ExitCode;
        }
        catch (OperationCanceledException exception) when (probeCts.IsCancellationRequested)
        {
            probeCts.Cancel();
            Exception? cleanupFailure = await TerminateAndObserveProbeAsync(
                probe,
                outputTask,
                errorTask,
                terminationWait).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (cleanupFailure is not null)
            {
                throw new InvalidOperationException(
                    "Docker Scheduler-volume probe cleanup failed after the bounded execution window.",
                    cleanupFailure);
            }

            throw new TimeoutException(
                $"Docker did not inspect the Scheduler volume owner within {probeWait.TotalSeconds:0} seconds.",
                exception);
        }
        catch (Exception exception) when (
            waitingForExit
            && exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            probeCts.Cancel();
            Exception? cleanupFailure = await TerminateAndObserveProbeAsync(
                probe,
                outputTask,
                errorTask,
                terminationWait).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Exception classifiedFailure = cleanupFailure is null
                ? exception
                : new AggregateException(exception, cleanupFailure);
            throw new InvalidOperationException(
                $"Docker Scheduler-volume probe process observation failed: {exception.Message}",
                classifiedFailure);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException(
                $"Docker Scheduler-volume probe redirected stream observation failed: {exception.Message}",
                exception);
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException(
                $"Docker Scheduler-volume probe process observation failed: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            string errorDetail = string.IsNullOrWhiteSpace(error) ? "no standard-error detail" : error.Trim();
            throw new InvalidOperationException(
                $"Docker could not inspect the Scheduler volume owner (exit {exitCode}): {errorDetail}.");
        }

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Builds the Docker command that includes running and stopped Scheduler-volume owners.</summary>
    internal static ProcessStartInfo CreateSchedulerVolumeProbeStartInfo()
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("ps");
        startInfo.ArgumentList.Add("--all");
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add($"volume={SchedulerVolumeName}");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.ID}} {{.Names}}");
        return startInfo;
    }

    /// <summary>Classifies whether the production exclusive-bind probe succeeds for one address and port.</summary>
    /// <param name="address">The loopback address to probe.</param>
    /// <param name="port">The fixed control-plane port to probe.</param>
    /// <returns><see langword="true"/> when the address is bindable or inapplicable on this host.</returns>
    internal static bool IsPortAvailableForExclusiveBind(IPAddress address, int port)
        => IsPortAvailableForExclusiveBind(
            address,
            port,
            static (candidateAddress, candidatePort) =>
                _ = BindPortExclusively(candidateAddress, candidatePort));

    /// <summary>Classifies whether an exclusive bind succeeds or is inapplicable on this host.</summary>
    /// <param name="address">The loopback address to probe.</param>
    /// <param name="port">The fixed control-plane port to probe.</param>
    /// <param name="bindExclusively">Attempts the exclusive bind.</param>
    internal static bool IsPortAvailableForExclusiveBind(
        IPAddress address,
        int port,
        Action<IPAddress, int> bindExclusively)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentNullException.ThrowIfNull(bindExclusively);

        try
        {
            bindExclusively(address, port);
            return true;
        }
        catch (SocketException exception) when (IsUnavailableLoopbackAddress(address, exception.SocketErrorCode))
        {
            // IPv6 can be disabled at the host/kernel level. That makes ::1 inapplicable, not occupied; the
            // IPv4 loopback still proves whether the fixed control-plane port is available on this host.
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Creates, configures, and starts the listener used by the production exclusive-bind probe.</summary>
    /// <param name="address">The loopback address to bind.</param>
    /// <param name="port">The port to bind, or zero to let the operating system select one.</param>
    /// <returns>The socket settings observed on the listener that was started.</returns>
    internal static (bool ExclusiveAddressUse, bool? DualMode) BindPortExclusively(
        IPAddress address,
        int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegative(port);

        var listener = new TcpListener(address, port);
        try
        {
            BindPortExclusively(listener, address);
            return (
                listener.Server.ExclusiveAddressUse,
                address.AddressFamily == AddressFamily.InterNetworkV6
                    ? listener.Server.DualMode
                    : null);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Configures and starts one listener through the production exclusive-bind operation.</summary>
    /// <param name="listener">The listener to configure and start.</param>
    /// <param name="address">The loopback address the listener will probe.</param>
    internal static void BindPortExclusively(TcpListener listener, IPAddress address)
    {
        ConfigureExclusiveBindListener(listener, address);
        listener.Start();
    }

    /// <summary>Applies the production exclusive-bind settings to one control-plane listener.</summary>
    /// <param name="listener">The listener to configure.</param>
    /// <param name="address">The loopback address the listener will probe.</param>
    private static void ConfigureExclusiveBindListener(TcpListener listener, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            listener.Server.DualMode = false;
        }

        listener.Server.ExclusiveAddressUse = true;
    }

    /// <summary>Returns whether a bind failure means the loopback address family is unavailable on this host.</summary>
    /// <param name="address">The loopback address that was probed.</param>
    /// <param name="socketError">The bind failure reported by the socket API.</param>
    internal static bool IsUnavailableLoopbackAddress(IPAddress address, SocketError socketError)
        => address.AddressFamily == AddressFamily.InterNetworkV6
            && socketError is SocketError.AddressFamilyNotSupported
                or SocketError.AddressNotAvailable
                or SocketError.ProtocolNotSupported
                or SocketError.ProtocolFamilyNotSupported;

    private static async Task<Exception?> TerminateAndObserveProbeAsync(
        ISchedulerVolumeProbe probe,
        Task<string> outputTask,
        Task<string> errorTask,
        TimeSpan terminationWait)
    {
        var cleanupFailures = new List<Exception>();
        TimeSpan redirectedReadWait = terminationWait + terminationWait;
        Task<Exception?> outputObservation = ObserveProbeTaskAsync(
            outputTask,
            "standard output",
            redirectedReadWait);
        Task<Exception?> errorObservation = ObserveProbeTaskAsync(
            errorTask,
            "standard error",
            redirectedReadWait);

        bool exited = ProbeHasExited(probe, cleanupFailures);
        if (!exited)
        {
            TryTerminateProbe(probe, cleanupFailures);
            exited = await WaitForProbeExitAsync(probe, terminationWait, cleanupFailures).ConfigureAwait(false);
        }

        if (!exited)
        {
            TryTerminateProbe(probe, cleanupFailures);
            exited = await WaitForProbeExitAsync(probe, terminationWait, cleanupFailures).ConfigureAwait(false);
        }

        if (!exited)
        {
            exited = ProbeHasExited(probe, cleanupFailures);
        }

        Exception?[] redirectedReadFailures = await Task.WhenAll(outputObservation, errorObservation).ConfigureAwait(false);
        cleanupFailures.AddRange(redirectedReadFailures.OfType<Exception>());

        if (!exited)
        {
            cleanupFailures.Add(
                new TimeoutException(
                    $"Docker Scheduler-volume probe did not exit after two bounded termination attempts of "
                    + $"{terminationWait.TotalSeconds:0.###} seconds each."));
        }

        return cleanupFailures.Count switch
        {
            0 => null,
            1 => cleanupFailures[0],
            _ => new AggregateException(cleanupFailures),
        };
    }

    private static bool ProbeHasExited(ISchedulerVolumeProbe probe, List<Exception> cleanupFailures)
    {
        try
        {
            return probe.HasExited;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            cleanupFailures.Add(exception);
            return false;
        }
    }

    private static void TryTerminateProbe(ISchedulerVolumeProbe probe, List<Exception> cleanupFailures)
    {
        try
        {
            probe.Kill();
        }
        catch (InvalidOperationException) when (ProbeHasExited(probe, cleanupFailures))
        {
            // The process exited between the HasExited observation and Kill.
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            cleanupFailures.Add(exception);
        }
    }

    private static async Task<bool> WaitForProbeExitAsync(
        ISchedulerVolumeProbe probe,
        TimeSpan terminationWait,
        List<Exception> cleanupFailures)
    {
        using var terminationCts = new CancellationTokenSource(terminationWait);
        try
        {
            await probe.WaitForExitAsync(terminationCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (terminationCts.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            cleanupFailures.Add(exception);
            return ProbeHasExited(probe, cleanupFailures);
        }
    }

    private static async Task<Exception?> ObserveProbeTaskAsync(
        Task<string> probeTask,
        string streamName,
        TimeSpan observationWait)
    {
        try
        {
            _ = await probeTask.WaitAsync(observationWait).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Cancellation and pipe closure are expected after terminating the timed-out child. Observing both
            // redirected reads prevents either cleanup fault from masking the classified probe timeout.
            return null;
        }
        catch (TimeoutException exception)
        {
            return new TimeoutException(
                $"Docker Scheduler-volume probe {streamName} did not settle within the bounded cleanup window.",
                exception);
        }
        catch (Exception exception)
        {
            return new InvalidOperationException(
                $"Docker Scheduler-volume probe {streamName} failed during cleanup: {exception.Message}",
                exception);
        }
    }

    private static Task<string> StartProbeRead(Func<Task<string>> startRead)
    {
        try
        {
            return startRead()
                ?? Task.FromException<string>(
                    new InvalidOperationException("Docker Scheduler-volume probe returned a null redirected-read task."));
        }
        catch (Exception exception)
        {
            return Task.FromException<string>(exception);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
