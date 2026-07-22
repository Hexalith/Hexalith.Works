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
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 4.6 AC #1/#3 runtime reminder-recovery proof. This lane starts the full Works AppHost topology under
/// <see cref="Aspire.Hosting.Testing"/>, parks a work item on a past <c>DateReached</c> await, then
/// <em>restarts the AppHost</em> (a fresh <see cref="DistributedApplication"/> against the same external
/// <c>dapr init</c> Redis, so the persisted stream survives the stop) and proves the date resume is reissued
/// and accepted exactly once, idempotently under a second pass.
/// </summary>
/// <remarks>
/// <para>It is Tier-3: it requires Docker, a <c>dapr init</c> Redis, and the Dapr placement/scheduler services.
/// When those prerequisites are absent (e.g. the headless sandbox) the test <see cref="Assert.Skip(string)"/>s
/// with a clear reason rather than failing — mirroring <c>WorksCommandPipelineSmokeTests</c>. The reconciliation
/// <em>decision logic</em> (discover pending due awaits → reissue idempotently, reschedule future awaits) is
/// proven deterministically by <c>DateReminderRecoveryRuntimeTests</c>; this lane proves the end-to-end resume
/// acceptance and exactly-once outcome under a real Aspire restart.</para>
/// <para><b>Substrate limitation (honest, not faked).</b> On restart the Works host's
/// <c>ReminderReconciliationService</c> runs over the forwarded <c>Works:Recovery:Tenants</c> scope, but its
/// <c>StreamReadingPendingDateAwaitSource</c> tenant-wide scan is bounded by the EventStore stream-read gateway,
/// which currently requires a per-aggregate id (the contract allows domain-wide reads, but the
/// <c>StreamsController</c> route rejects a null <c>AggregateId</c> today). So this lane reissues the resume the
/// way the recovery adapter does — through the production <see cref="DateResume.BuildSubmission"/> command
/// factory submitted on the same <c>POST /api/v1/commands</c> path the reminder actor / reconciler use — rather
/// than relying on tenant-wide auto-discovery. "Exactly one accepted <c>WorkItemResumed</c>" is then verified
/// from the re-readable per-aggregate stream (<c>POST /api/v1/streams/read</c>).</para>
/// <para>Auth uses the EventStore EnableKeycloak=false symmetric-key dev path; the signing key matches the
/// EventStore <c>appsettings.Development.json</c> dev key. The dev RBAC validator is permissive when the token
/// carries no <c>eventstore:permission</c> claims, so the same token authorizes command submission and the
/// per-aggregate replay read; only the tenant claim is load-bearing.</para>
/// </remarks>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksReminderRecoveryPipelineSmokeTests
{
    private const string DevSigningKey = "DevOnlySigningKey-AtLeast32Chars!";
    private const string Tenant = "tenant-recovery";

    // A past instant so reconciliation/reissue treats the await as already due; suspend and resume must carry
    // the SAME deterministic DateReached await condition for the aggregate to accept the resume.
    private static readonly DateTimeOffset DueInstant = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Unique per run so a re-run against a persistent dapr-init Redis starts from a clean aggregate stream.
    private static readonly string WorkItem = "work-reminder-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task A_pending_date_resume_survives_an_apphost_restart_and_resumes_exactly_once()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        if (!await PrerequisitesAvailableAsync(ct).ConfigureAwait(true))
        {
            Assert.Skip(
                "Aspire reminder-recovery prerequisites missing (Redis :6379 + Dapr placement :50005 + scheduler :50006). "
                + "Start Docker, run `dapr init`, and start the placement/scheduler services to run this lane.");
            return;
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        // Phase 1 — park the item on a past DateReached await, then stop the AppHost with the resume pending.
        await WithAppHostAsync(ct, async (client, token) =>
        {
            await ParkSuspendedOnDateAsync(client, token).ConfigureAwait(false);
            (await CountResumedAsync(client, token).ConfigureAwait(false))
                .ShouldBe(0, "The parked item must be Suspended (no WorkItemResumed) before recovery runs.");
        }).ConfigureAwait(true);

        // Phase 2/3 — restart the AppHost against the same Redis. The startup reconciler runs; the recovery
        // adapter's deterministic resume is reissued and proven to resume exactly once and to be idempotent.
        await WithAppHostAsync(ct, async (client, token) =>
        {
            string firstStatus = await ReissueDateResumeAsync(client, token).ConfigureAwait(false);
            firstStatus.ShouldBe("Completed", "The reissued date resume must be accepted after restart.");
            (await WaitForResumedCountAsync(client, atLeast: 1, token).ConfigureAwait(false))
                .ShouldBe(1, "Recovery must add exactly one accepted WorkItemResumed.");

            // Second pass — a redelivered firing reissues the SAME deterministic command; the aggregate no-ops
            // it (and the substrate dedups by the deterministic message id), so no second resume is recorded.
            string secondStatus = await ReissueDateResumeAsync(client, token).ConfigureAwait(false);
            secondStatus.ShouldNotBe("Rejected", "A duplicate date resume must resolve idempotently (no-op), never rejected.");
            (await CountResumedAsync(client, token).ConfigureAwait(false))
                .ShouldBe(1, "A second reconciliation pass must not add a duplicate WorkItemResumed.");
        }).ConfigureAwait(true);
    }

    // Starts the full AppHost topology (forwarding the recovery tenant scope so the startup reconciler runs),
    // waits for eventstore + works to be healthy, runs the body against the eventstore gateway client, and
    // disposes both the application and the builder — so the second call is a genuine restart of the topology.
    private static async Task WithAppHostAsync(CancellationToken cancellationToken, Func<HttpClient, CancellationToken, Task> body)
    {
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(TimeSpan.FromMinutes(5));

        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(["--EnableKeycloak=false", $"--Works:Recovery:Tenants={Tenant}"], startupCts.Token)
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

    // Create → Assign → Claim → Suspend(DateReached past): drives the work item to Suspended parked on the
    // deterministic date await the recovery adapter will later reissue against.
    private static async Task ParkSuspendedOnDateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(WorkItem);
        var binding = new ExecutorBinding(new PartyId("recovery-worker"), Channel.Cli, AuthorityLevel.Contribute);

        await SubmitToTerminalAsync(client, nameof(CreateWorkItem), new CreateWorkItem(tenant, workItem, "Reminder-recovery obligation"), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, nameof(AssignWorkItem), new AssignWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, nameof(ClaimWorkItem), new ClaimWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            nameof(SuspendWorkItem),
            new SuspendWorkItem(tenant, workItem, [AwaitCondition.DateReached(DueInstant)]),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task SubmitToTerminalAsync<TCommand>(HttpClient client, string commandType, TCommand command, CancellationToken cancellationToken)
    {
        string correlationId = await SubmitCommandAsync(
            client,
            messageId: Guid.NewGuid().ToString(),
            commandType: commandType,
            payload: JsonSerializer.SerializeToElement(command),
            correlationId: null,
            cancellationToken).ConfigureAwait(false);

        string status = await PollToTerminalAsync(client, correlationId, cancellationToken).ConfigureAwait(false);
        status.ShouldBe("Completed", $"{commandType} must persist and publish to a Completed terminal status while parking the item.");
    }

    // Reissues the resume exactly as the recovery adapter does: the production DateResume factory builds the
    // deterministic ResumeWorkItem + correlation/causation id, and the EventStore submitter carries the
    // causation id as the gateway MessageId (the idempotency key). Returns the resume command's terminal status.
    private static async Task<string> ReissueDateResumeAsync(HttpClient client, CancellationToken cancellationToken)
    {
        WorkCommandSubmission resume = DateResume.BuildSubmission(Tenant, WorkItem, DueInstant);

        string correlationId = await SubmitCommandAsync(
            client,
            messageId: resume.CausationId,
            commandType: resume.CommandType,
            payload: resume.Payload,
            correlationId: resume.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        return await PollToTerminalAsync(client, correlationId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> SubmitCommandAsync(
        HttpClient client,
        string messageId,
        string commandType,
        JsonElement payload,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var body = new SubmitCommandRequest(
            MessageId: messageId,
            Tenant: Tenant,
            Domain: "work",
            AggregateId: WorkItem,
            CommandType: commandType,
            Payload: payload,
            CorrelationId: correlationId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, $"{commandType} submission must return 202 Accepted.");

        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        return result.GetProperty("correlationId").GetString()!;
    }

    private static async Task<string> PollToTerminalAsync(HttpClient client, string correlationId, CancellationToken cancellationToken)
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

    private static async Task<int> WaitForResumedCountAsync(HttpClient client, int atLeast, CancellationToken cancellationToken)
    {
        int count = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            count = await CountResumedAsync(client, cancellationToken).ConfigureAwait(false);
            if (count >= atLeast)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return count;
    }

    // Counts accepted WorkItemResumed events in the parked item's re-readable per-aggregate stream.
    private static async Task<int> CountResumedAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var body = new
        {
            tenant = Tenant,
            domain = "work",
            aggregateId = WorkItem,
            fromSequence = 0L,
            pageSize = 100,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());

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
                new Claim("sub", "works-reminder-recovery-user"),
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
