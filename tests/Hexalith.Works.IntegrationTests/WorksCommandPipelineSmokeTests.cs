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
using Hexalith.Works.Contracts.ValueObjects;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 4.5 AC #2/#3 runtime command-pipeline proof. This lane starts the full Works AppHost topology under
/// <see cref="Aspire.Hosting.Testing"/> and submits an authenticated command through the EventStore command
/// gateway (<c>POST /api/v1/commands</c>), polling <c>/api/v1/commands/status/{correlationId}</c> to a terminal
/// status — proving the kernel → adapter aggregate → EventStore persist-then-publish path end-to-end through
/// the runnable Works domain service.
/// </summary>
/// <remarks>
/// <para>It is Tier-3: it requires Docker, a `dapr init` Redis, and the Dapr placement/scheduler services. When
/// those prerequisites are absent (e.g. the headless sandbox) the test <see cref="Assert.Skip(string)"/>s with a
/// clear reason rather than failing — a miswired topology still fails via the model-inspection lane
/// (<c>WorksAppHostTopologyTests</c>), and the deterministic adapter convergence is proven by
/// <c>WorkItemProjectionQueryAdapterTests</c>.</para>
/// <para>Auth uses the EventStore EnableKeycloak=false symmetric-key dev path; the signing key matches the
/// EventStore <c>appsettings.Development.json</c> dev key.</para>
/// </remarks>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksCommandPipelineSmokeTests
{
    private const string DevSigningKey = "DevOnlySigningKey-AtLeast32Chars!";
    private const string Tenant = "tenant-alpha";
    // Unique per run so persistent dapr-init Redis cannot turn a rerun into duplicate-create rejection.
    private static readonly string WorkItem = "work-smoke-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task CreateWorkItem_command_persists_then_publishes_to_a_terminal_status()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        if (!await PrerequisitesAvailableAsync(ct).ConfigureAwait(true))
        {
            Assert.Skip(
                "Aspire command-pipeline prerequisites missing (Redis :6379 + Dapr placement :50005 + scheduler :50006). "
                + "Start Docker, run `dapr init`, and start the placement/scheduler services to run this lane.");
            return;
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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

            string correlationId = await SubmitCreateWorkItemAsync(client, startupCts.Token).ConfigureAwait(true);
            string status = await PollToTerminalAsync(client, correlationId, startupCts.Token).ConfigureAwait(true);

            status.ShouldBe("Completed", "A valid CreateWorkItem must persist and publish to a Completed terminal status.");
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(true);
            await builder.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static async Task<string> SubmitCreateWorkItemAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var command = new CreateWorkItem(new TenantId(Tenant), new WorkItemId(WorkItem), "Smoke-test obligation");
        var body = new SubmitCommandRequest(
            MessageId: Guid.NewGuid().ToString(),
            Tenant: Tenant,
            Domain: "work",
            AggregateId: WorkItem,
            CommandType: nameof(CreateWorkItem),
            Payload: JsonSerializer.SerializeToElement(command));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, "Command submission must return 202 Accepted.");

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

    private static string MintToken()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "works-smoke-user"),
                new Claim("tenants", JsonSerializer.Serialize(new[] { Tenant })),
                new Claim("domains", JsonSerializer.Serialize(new[] { "work" })),
                new Claim("permissions", JsonSerializer.Serialize(new[] { "command:submit", "command:query" })),
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
