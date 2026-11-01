using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>One explicit Development preflight loses a real own-sidecar response while retaining its actual gateway lease.</summary>
internal sealed class WorksRetainedFencePreflightService(IRouteCatalogAuthority authority, IConfiguration configuration,
    IHostEnvironment environment) : BackgroundService
{
    internal const string PathKey = "WORKS_SDK_RETAINED_FENCE_PREFLIGHT";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsDevelopment() || configuration["WORKS_SDK_PROOF"] != "1" || configuration["WORKS_SDK_PROOF_ROLE"] != "eventstore"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] != "eventstore"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] != "eventstore")
            throw new InvalidOperationException("The one-shot fence preflight requires its actual Development gateway.");
        string output = configuration[PathKey]!;
        WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(output)!);
        RouteCatalogGenerationReadback generation;
        while (true)
        {
            try { generation = await authority.ReadbackAsync(stoppingToken).ConfigureAwait(false); if (generation.Serving) break; }
            catch (RouteCatalogUnavailableException) { }
            await Task.Delay(25, stoppingToken).ConfigureAwait(false);
        }
        var lease = await authority.AcquireAsync(stoppingToken).ConfigureAwait(false);
        await using var release = lease.ConfigureAwait(false);
        if (lease.Snapshot.LocalAppId != "eventstore" || lease.Snapshot.RootDigest != generation.RootDigest || lease.Snapshot.Epoch != generation.Epoch)
            throw new InvalidOperationException("The actual preflight sender crossed its committed generation.");
        using var scope = RouteCatalogRemoteCall.Enter(lease);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler);
        string endpoint = configuration["DAPR_HTTP_ENDPOINT"]!;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? address) || !address.IsLoopback || address.Scheme != "http"
            || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0 || address.AbsolutePath != "/")
            throw new InvalidOperationException("The preflight requires its actual local sidecar endpoint.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "/v1.0/metadata"));
        if (configuration["DAPR_API_TOKEN"] is { Length: > 0 } token) request.Headers.Add("dapr-api-token", token);
        var lost = new IOException("Explicit owned response-loss preflight.");
        try
        {
            await lease.InvokeRemoteAsync<int>(async () =>
            {
                using HttpResponseMessage response = await client.SendAsync(request, stoppingToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                _ = await response.Content.ReadAsByteArrayAsync(stoppingToken).ConfigureAwait(false);
                throw lost;
            }, stoppingToken).ConfigureAwait(false);
        }
        catch (IOException exception) when (ReferenceEquals(exception, lost)) { }
        await WorksProofCallerProtocol.WriteNewAsync(output, new { actualRemoteStatus = 200, responseLost = true,
            sender = lease.Snapshot.LocalAppId, generation.RootDigest, generation.Epoch }, stoppingToken).ConfigureAwait(false);
    }
}
