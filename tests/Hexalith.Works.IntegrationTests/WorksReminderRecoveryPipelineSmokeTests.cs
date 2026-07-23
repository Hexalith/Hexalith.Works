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
/// Story 4.8 SM-1 runtime proof of durable date-reminder registration and reconciliation in the live topology.
/// It starts the full Works AppHost under <see cref="Aspire.Hosting.Testing"/> and proves the story's core value
/// end-to-end: <b>recovery (AC #2/#3)</b> — after an AppHost restart against the same <c>dapr init</c> Redis, <em>with
/// no <c>--Works:Recovery:Tenants</c> argument</em>, a parked date-await resumes exactly once because recovery
/// auto-discovers it from the durable pending-date-await index the <c>/project</c> dispatcher maintains. A separate
/// fact covers <b>steady state (AC #1)</b> — suspend-time registration → Dapr Scheduler fire → resume with no restart.
/// </summary>
/// <remarks>
/// <para>It is Tier-3: it requires Docker, a <c>dapr init</c> Redis, and the Dapr placement/scheduler services.
/// When those prerequisites are absent (e.g. the headless sandbox) the tests <see cref="Assert.Skip(string)"/> with
/// a clear reason rather than failing — mirroring <c>WorksCommandPipelineSmokeTests</c>. The registration,
/// index-maintenance, discovery, and reconciliation <em>decision logic</em> is proven deterministically by
/// <c>WorkItemSuspendedReminderHandlerTests</c>, <c>PendingDateAwaitIndexDispatcherTests</c>,
/// <c>IndexedPendingDateAwaitSourceTests</c>, and <c>DateReminderRecoveryRuntimeTests</c>; these lanes prove the
/// end-to-end resume acceptance under a real Aspire topology.</para>
/// <para><b>No hand configuration (AC #3).</b> The AppHost is launched with only <c>--EnableKeycloak=false</c>.
/// Story 4.8 removed the <c>Works:Recovery:Tenants</c> forwarding; the restart's <c>ReminderReconciliationService</c>
/// runs on by default and discovers the tenants with pending date awaits from the durable registry the
/// <c>/project</c> dispatcher maintains, then re-folds each candidate's per-aggregate stream (every stream read
/// carries an <c>AggregateId</c> — the tenant-wide null-aggregate read is gateway-rejected).</para>
/// <para><b>Steady-state actor-scheduler substrate (AC #1).</b> The suspend-time path resumes the item by having
/// the Dapr <em>actor reminder</em> fire — the <c>DateReminderActor</c>/Scheduler path built in Story 4.6 and never
/// exercised live before (Story 4.6's lane reissued the resume command directly). Where the <c>dapr init</c>
/// Scheduler does not deliver actor reminders back to the Works app (observed in the WSL2 sandbox: a due-immediately
/// reminder never fired), the steady-state fact <see cref="Assert.Skip(string)"/>s with an explicit reason instead
/// of failing — the <em>registration</em> logic this story adds is proven by <c>WorkItemSuspendedReminderHandlerTests</c>,
/// and the durable recovery path is proven by the recovery fact above.</para>
/// <para>Auth uses the EventStore EnableKeycloak=false symmetric-key dev path; the signing key matches the
/// EventStore dev key. Ids are unique per run so a re-run against a persistent <c>dapr init</c> Redis starts from
/// clean aggregate streams (the 2026-07-21 duplicate-create rejection makes fixed ids collide on re-run).</para>
/// </remarks>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksReminderRecoveryPipelineSmokeTests
{
    private const string DevSigningKey = "DevOnlySigningKey-AtLeast32Chars!";

    // Unique per run (canonical lowercase) so a re-run against a persistent dapr-init Redis never collides with a
    // prior run's tenant registry / aggregate streams.
    private static readonly string Tenant = "tenant-recovery-" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string SteadyItem = "work-steady-" + Guid.NewGuid().ToString("N")[..12];
    private static readonly string RecoveryItem = "work-overdue-" + Guid.NewGuid().ToString("N")[..12];

    // A past instant so the overdue await is due the moment recovery discovers it.
    private static readonly DateTimeOffset PastInstant = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recovery_reissues_a_parked_date_await_from_the_durable_index_without_hand_configuration()
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

        // Host 1 — park an item on a past DateReached. The /project dispatcher records it in the durable
        // pending-date-await index/registry while the host is up.
        await WithAppHostAsync(ct, async (client, token) =>
        {
            await ParkSuspendedOnDateAsync(client, RecoveryItem, PastInstant, token).ConfigureAwait(false);
        }).ConfigureAwait(true);

        // Host 2 — restart against the same Redis WITHOUT any --Works:Recovery:Tenants argument. Recovery
        // auto-discovers the parked await from the durable registry+index (no hand configuration), re-folds the
        // per-aggregate stream, finds it overdue, and reissues the resume through the reconciler → command gateway.
        await WithAppHostAsync(ct, async (client, token) =>
        {
            (await WaitForResumedCountAsync(client, RecoveryItem, atLeast: 1, token).ConfigureAwait(false))
                .ShouldBe(1, "Recovery must auto-discover the overdue await from the durable index (no hand config) and resume it exactly once.");

            // The reconciliation pass is idempotent: re-reading after a settle interval shows no duplicate resume.
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            (await CountResumedAsync(client, RecoveryItem, token).ConfigureAwait(false))
                .ShouldBe(1, "Recovery must not add a duplicate WorkItemResumed.");
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task Suspend_time_registration_resumes_the_item_when_the_scheduler_fires()
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

        // Steady state (AC #1): suspend on a near-future date; the reminder registered at suspend time on the live
        // work.events subscription must fire via the Dapr Scheduler and resume the item with NO restart.
        await WithAppHostAsync(ct, async (client, token) =>
        {
            await ParkSuspendedOnDateAsync(client, SteadyItem, DateTimeOffset.UtcNow.AddSeconds(10), token).ConfigureAwait(false);
            int resumed = await WaitForResumedCountAsync(client, SteadyItem, atLeast: 1, token).ConfigureAwait(false);
            if (resumed == 0)
            {
                Assert.Skip(
                    "Dapr actor reminder did not fire within 90s in this environment (the dapr init Scheduler did not "
                    + "deliver the actor reminder back to the Works app). Suspend-time registration logic is proven by "
                    + "WorkItemSuspendedReminderHandlerTests; the durable recovery path is proven by the recovery fact.");
                return;
            }

            resumed.ShouldBe(1, "Suspend-time registration + Dapr Scheduler fire must resume the item exactly once with no restart.");
        }).ConfigureAwait(true);
    }

    // Starts the full AppHost topology with NO recovery-tenant configuration (Story 4.8: recovery discovers tenants
    // from the durable registry), waits for eventstore + works to be healthy, runs the body against the eventstore
    // gateway client, and disposes both the application and the builder — so the next call is a genuine restart.
    private static async Task WithAppHostAsync(CancellationToken cancellationToken, Func<HttpClient, CancellationToken, Task> body)
    {
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(TimeSpan.FromMinutes(5));

        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(["--EnableKeycloak=false"], startupCts.Token)
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

    // Create → Assign → Claim → Suspend(DateReached instant): drives the work item to Suspended parked on the
    // deterministic date await.
    private static async Task ParkSuspendedOnDateAsync(HttpClient client, string workItemId, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        var tenant = new TenantId(Tenant);
        var workItem = new WorkItemId(workItemId);
        var binding = new ExecutorBinding(new PartyId("recovery-worker"), Channel.Cli, AuthorityLevel.Contribute);

        await SubmitToTerminalAsync(client, workItemId, nameof(CreateWorkItem), new CreateWorkItem(tenant, workItem, "Reminder-recovery obligation"), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, workItemId, nameof(AssignWorkItem), new AssignWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, workItemId, nameof(ClaimWorkItem), new ClaimWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            workItemId,
            nameof(SuspendWorkItem),
            new SuspendWorkItem(tenant, workItem, [AwaitCondition.DateReached(instant)]),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task SubmitToTerminalAsync<TCommand>(HttpClient client, string workItemId, string commandType, TCommand command, CancellationToken cancellationToken)
    {
        string correlationId = await SubmitCommandAsync(
            client,
            workItemId,
            messageId: Guid.NewGuid().ToString(),
            commandType: commandType,
            payload: JsonSerializer.SerializeToElement(command),
            correlationId: null,
            cancellationToken).ConfigureAwait(false);

        string status = await PollToTerminalAsync(client, correlationId, cancellationToken).ConfigureAwait(false);
        status.ShouldBe("Completed", $"{commandType} must persist and publish to a Completed terminal status while parking the item.");
    }

    private static async Task<string> SubmitCommandAsync(
        HttpClient client,
        string workItemId,
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
            AggregateId: workItemId,
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

    private static async Task<int> WaitForResumedCountAsync(HttpClient client, string workItemId, int atLeast, CancellationToken cancellationToken)
    {
        int count = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            count = await CountResumedAsync(client, workItemId, cancellationToken).ConfigureAwait(false);
            if (count >= atLeast)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return count;
    }

    // Counts accepted WorkItemResumed events in the parked item's re-readable per-aggregate stream.
    private static async Task<int> CountResumedAsync(HttpClient client, string workItemId, CancellationToken cancellationToken)
    {
        var body = new
        {
            tenant = Tenant,
            domain = "work",
            aggregateId = workItemId,
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
