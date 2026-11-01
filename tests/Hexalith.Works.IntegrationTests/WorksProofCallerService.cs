using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Test code running in the actual gateway, using its actual SDK client/issuer/authority.</summary>
internal sealed class WorksProofCallerService(IHttpClientFactory clients, DaprClient dapr, IRouteCatalogAuthority authority,
    IConfiguration configuration, IHostEnvironment environment) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string directory = configuration[WorksProofCallerProtocol.DirectoryKey]!;
        string session = configuration[WorksProofCallerProtocol.SessionKey]!;
        if (!environment.IsDevelopment() || configuration["WORKS_SDK_PROOF"] != "1" || configuration["WORKS_SDK_PROOF_ROLE"] != "eventstore"
            || configuration["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] != "eventstore" || !Guid.TryParseExact(session, "N", out _))
            throw new InvalidOperationException("The private proof caller must run inside the selected Development gateway.");
        WorksProofCallerProtocol.RequireDirectory(directory);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.request.json"))
            {
                string nonce = Path.GetFileName(file)[..^".request.json".Length];
                if (!Guid.TryParseExact(nonce, "N", out _)) throw new InvalidOperationException("An unknown proof file cannot select a path.");
                string cancel = Path.Combine(directory, nonce + ".cancel");
                WorksProofCallerResponse response;
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                Task watcher = WatchCancellationAsync(cancel, requestCancellation, stoppingToken);
                try
                {
                    var request = JsonSerializer.Deserialize<WorksProofCallerRequest>(await WorksProofCallerProtocol.ReadAsync(file, stoppingToken).ConfigureAwait(false))!;
                    WorksProofCallerProtocol.Validate(request, nonce, session, DateTimeOffset.UtcNow);
                    requestCancellation.CancelAfter(request.ExpiresAt - DateTimeOffset.UtcNow);
                    if (File.Exists(cancel)) requestCancellation.Cancel();
                    requestCancellation.Token.ThrowIfCancellationRequested();
                    // No caller supplies a URL, target, verb, credentials, generation or headers.
                    using HttpRequestMessage outgoing = dapr.CreateInvokeMethodRequest("works", WorksProofCallerProtocol.Method(request.Command), request.Payload);
                    if (request.Command == "query")
                    {
                        var query = request.Payload.Deserialize<QueryEnvelope>(JsonSerializerOptions.Web)!;
                        if (query.CallerAuthority is not null) outgoing.Options.Set(QueryCallerAuthorityBinding.RequestBindings, QueryCallerAuthorityBinding.Bind(query));
                    }
                    using HttpClient client = clients.CreateClient("domain-service-invocation");
                    using HttpResponseMessage result = await client.SendAsync(outgoing, requestCancellation.Token).ConfigureAwait(false);
                    byte[] body = await result.Content.ReadAsByteArrayAsync(requestCancellation.Token).ConfigureAwait(false);
                    var generation = await authority.ReadbackAsync(requestCancellation.Token).ConfigureAwait(false);
                    response = new(nonce, session, (int)result.StatusCode, body, result.Content.Headers.ContentType?.ToString(), generation, null);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    response = new(nonce, session, 0, [], null, null, exception is OperationCanceledException ? "cancelled" : "caller-refused");
                }
                finally
                {
                    requestCancellation.Cancel();
                    await watcher.ConfigureAwait(false);
                }
                await WorksProofCallerProtocol.WriteNewAsync(Path.Combine(directory, nonce + ".response.json"), response, stoppingToken).ConfigureAwait(false);
                File.Delete(file);
            }
            await Task.Delay(25, stoppingToken).ConfigureAwait(false);
        }
    }

    private static async Task WatchCancellationAsync(string path, CancellationTokenSource request, CancellationToken stopping)
    {
        try
        {
            while (!request.IsCancellationRequested)
            {
                if (File.Exists(path)) { request.Cancel(); return; }
                await Task.Delay(25, request.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested || stopping.IsCancellationRequested) { }
    }
}
