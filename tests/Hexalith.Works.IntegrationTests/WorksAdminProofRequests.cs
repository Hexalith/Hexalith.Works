using System.Text.Json;

using Aspire.Hosting.ApplicationModel;

using Hexalith.EventStore.Client.Processes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exchanges only finite disposition probes with the actual Admin, without exporting its credentials.</summary>
internal static class WorksAdminProofRequests
{
    internal static async Task<HttpResponseMessage> PostAsync(ResourceEvent admin, ProcessSource source, string kind, string nonce, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string Value(string name) => admin.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
        string directory = Value(WorksAdminProofProtocol.DirectoryKey), session = Value(WorksAdminProofProtocol.SessionKey);
        WorksProofCallerProtocol.RequireDirectory(directory);
        var request = new WorksAdminProofRequest(nonce, session, kind, source, DateTimeOffset.UtcNow.AddSeconds(55));
        WorksAdminProofProtocol.Validate(request, nonce, session, DateTimeOffset.UtcNow, [source]);
        string path = Path.Combine(directory, nonce);
        await WorksProofCallerProtocol.WriteNewAsync(path + ".request.json", request, token).ConfigureAwait(false);
        bool received = false;
        try
        {
            while (!File.Exists(path + ".response.json")) await Task.Delay(25, token).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<WorksProofCallerResponse>(await WorksProofCallerProtocol.ReadAsync(path + ".response.json", token).ConfigureAwait(false))!;
            received = true;
            if (result.Nonce != nonce || result.Session != session || result.Failure is not null
                || result.Generation is not { Serving: true, Profile: "Development", Epoch: > 0 } generation
                || generation.RequiredHosts.Count < 4 || !generation.RequiredHosts.Any(host => host.HostId == "eventstore-admin" && host.AppId == "eventstore-admin"))
                throw new InvalidOperationException("The actual Admin caller refused its committed proof: " + result.Failure);
            var response = new HttpResponseMessage((System.Net.HttpStatusCode)result.Status) { Content = new ByteArrayContent(result.Body) };
            if (result.ContentType is not null) response.Content.Headers.TryAddWithoutValidation("Content-Type", result.ContentType);
            return response;
        }
        finally
        {
            if (!received)
            {
                await WorksProofCallerProtocol.WriteNewAsync(path + ".cancel", new { nonce }, CancellationToken.None).ConfigureAwait(false);
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (!File.Exists(path + ".response.json") && elapsed.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
                received = File.Exists(path + ".response.json");
            }
            if (received) foreach (string suffix in new[] { ".request.json", ".response.json", ".cancel" }) File.Delete(path + suffix);
        }
    }
}
