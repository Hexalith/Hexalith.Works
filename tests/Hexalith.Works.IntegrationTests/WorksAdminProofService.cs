using System.Net.Http.Headers;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Runs only inside the actual Admin, using its own issuer, SDK transport and committed dispatch scope.</summary>
internal sealed class WorksAdminProofService(IHttpClientFactory clients, DaprClient dapr, IRouteCatalogAuthority authority,
    IWorkloadAssertionIssuer issuer, IConfiguration configuration, IHostEnvironment environment) : BackgroundService
{
    internal static void RegisterIssuer(IServiceCollection services, IConfiguration configuration)
        => services.AddEventStoreWorkloadAssertionIssuer(configuration, options =>
        {
            if (!string.IsNullOrWhiteSpace(options.Workload) && options.Workload != "eventstore-admin")
                throw new InvalidOperationException("The Admin proof cannot borrow another issuer identity.");
            options.Workload = "eventstore-admin";
        });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RequireOwner();
        string directory = configuration[WorksAdminProofProtocol.DirectoryKey]!;
        string session = configuration[WorksAdminProofProtocol.SessionKey]!;
        WorksProofCallerProtocol.RequireDirectory(directory);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.request.json"))
            {
                string nonce = Path.GetFileName(file)[..^".request.json".Length];
                if (!Guid.TryParseExact(nonce, "N", out _)) throw new InvalidOperationException("An unknown proof file cannot select a path.");
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                Task watcher = WatchCancellationAsync(Path.Combine(directory, nonce + ".cancel"), requestCancellation);
                WorksProofCallerResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<WorksAdminProofRequest>(await WorksProofCallerProtocol.ReadAsync(file, stoppingToken).ConfigureAwait(false))!;
                    if (File.Exists(Path.Combine(directory, nonce + ".cancel"))) requestCancellation.Cancel();
                    response = await ExecuteRequestAsync(request, nonce, session, requestCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    response = new(nonce, session, 0, [], null, null, exception is OperationCanceledException ? "cancelled" : "caller-refused");
                }
                finally { requestCancellation.Cancel(); await watcher.ConfigureAwait(false); }
                // Retire this request before publishing its response: a correlated retry may reuse the nonce.
                File.Delete(file);
                await WorksProofCallerProtocol.WriteNewAsync(Path.Combine(directory, nonce + ".response.json"), response, stoppingToken).ConfigureAwait(false);
            }
            await Task.Delay(25, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<WorksProofCallerResponse> ExecuteRequestAsync(WorksAdminProofRequest request, string nonce, string session, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireOwner();
        if (session != configuration[WorksAdminProofProtocol.SessionKey]) throw new InvalidOperationException("The Admin proof session is foreign.");
        ProcessSource[] grants = JsonSerializer.Deserialize<ProcessSource[]>(configuration["WORKS_PLATFORM_RECOVERY_GRANTS"] ?? "[]")!;
        if (grants.Length is < 1 or > 64 || grants.Distinct().Count() != grants.Length || grants.Any(source => source.Domain != "work"))
            throw new InvalidOperationException("The Admin proof requires its exact finite Works source inventory.");
        foreach (ProcessSource source in grants) source.Validate();
        WorksAdminProofProtocol.Validate(request, nonce, session, DateTimeOffset.UtcNow, grants);
        if (!issuer.CanBindResources) throw new InvalidOperationException("The actual Admin issuer cannot bind this request.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(request.ExpiresAt - DateTimeOffset.UtcNow);
        var lease = await authority.AcquireAsync(deadline.Token).ConfigureAwait(false);
        await using var release = lease.ConfigureAwait(false);
        using var scope = RouteCatalogRemoteCall.Enter(lease);
        string assertion = await issuer.IssueAsync(new("works", WorksAdminProofProtocol.Operation, WorksAdminProofProtocol.Bindings(request)), deadline.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual Admin issuer refused its bound request.");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { source = request.Source, reason = "Synthetic Platform parity revalidation" }, JsonSerializerOptions.Web);
        if (payload.Length > 4096) throw new InvalidOperationException("The finite disposition proof exceeds its receiver observation bound.");
        using var outgoing = dapr.CreateInvokeMethodRequest(HttpMethod.Post, "works", WorksAdminProofProtocol.Route);
        outgoing.Content = new ByteArrayContent(payload);
        outgoing.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        outgoing.Headers.Add(EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName, assertion);
        if (request.Kind != "accepted")
        {
            outgoing.Headers.Add(WorksPlatformDispositionProbe.NonceHeader, nonce);
            outgoing.Headers.Add(WorksPlatformDispositionProbe.KindHeader, request.Kind);
        }
        using HttpClient client = clients.CreateClient("works-admin-disposition-proof");
        client.MaxResponseContentBufferSize = WorksProofCallerProtocol.MaximumBytes;
        using HttpResponseMessage result = await lease.InvokeRemoteAsync(() => client.SendAsync(outgoing, deadline.Token), deadline.Token).ConfigureAwait(false);
        byte[] body = await result.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
        var generation = await authority.ReadbackAsync(deadline.Token).ConfigureAwait(false);
        if (!generation.Serving || generation.RootDigest != lease.Snapshot.RootDigest || generation.Epoch != lease.Snapshot.Epoch)
            throw new InvalidOperationException("The Admin proof did not finish in its admitted generation.");
        return new(nonce, session, (int)result.StatusCode, body, result.Content.Headers.ContentType?.ToString(), generation, null);
    }

    private void RequireOwner()
    {
        if (!environment.IsDevelopment() || configuration["WORKS_SDK_PROOF"] != "1" || configuration["WORKS_SDK_PROOF_ROLE"] != "eventstore-admin"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] != "eventstore-admin"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] != "eventstore-admin"
            || !Guid.TryParseExact(configuration[WorksAdminProofProtocol.SessionKey], "N", out _))
            throw new InvalidOperationException("The finite Admin proof requires the actual selected Development Admin host.");
    }

    private static async Task WatchCancellationAsync(string path, CancellationTokenSource request)
    {
        try
        {
            while (!request.IsCancellationRequested)
            {
                if (File.Exists(path)) { request.Cancel(); return; }
                await Task.Delay(25, request.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
    }
}
