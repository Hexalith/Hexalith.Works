using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Hexalith.EventStore.Client.Processes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Requests a fixed replay from the actual Works process without exporting or supplying its credentials.</summary>
internal static class WorksTrustedEffectProofRequests
{
    /// <summary>Returns the real SDK receipt response or its actual HTTP refusal, bound to this private request and generation.</summary>
    internal static async Task<HttpResponseMessage> PostAsync(ResourceEvent works, WorksTrustedEffectProofTarget target, bool conflict, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string Value(string name) => works.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
        string directory = Value(WorksTrustedEffectProofProtocol.DirectoryKey), session = Value(WorksTrustedEffectProofProtocol.SessionKey);
        WorksProofCallerProtocol.RequireDirectory(directory);
        string nonce = Guid.NewGuid().ToString("N"), path = Path.Combine(directory, nonce);
        var request = new WorksProofCallerRequest(nonce, session, conflict ? "conflict-child-completion-resume" : "replay-child-completion-resume",
            JsonSerializer.SerializeToElement(target), DateTimeOffset.UtcNow.AddSeconds(55));
        WorksTrustedEffectProofProtocol.Validate(request, nonce, session, DateTimeOffset.UtcNow,
            JsonSerializer.Deserialize<ProcessSource[]>(Value("WORKS_PLATFORM_RECOVERY_GRANTS"))!);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        await WorksProofCallerProtocol.WriteNewAsync(path + ".request.json", request, deadline.Token).ConfigureAwait(false);
        bool received = false;
        try
        {
            while (!File.Exists(path + ".response.json")) await Task.Delay(25, deadline.Token).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<WorksProofCallerResponse>(await WorksProofCallerProtocol.ReadAsync(path + ".response.json", deadline.Token).ConfigureAwait(false))!;
            received = true;
            if (response.Nonce != nonce || response.Session != session)
                throw new InvalidOperationException("The effect driver response does not bind this private request.");
            if (response.Failure is not null || response.Status is < 100 or > 599
                || response.Generation is not { Serving: true, Profile: "Development", Epoch: > 0 } generation
                || !generation.RequiredHosts.Any(host => host.HostId == "works" && host.AppId == "works"))
                throw new InvalidOperationException("The actual Works effect driver refused its committed proof: " + response.Failure
                    + "; bounded type/method evidence=" + WorksReminderProofRequests.DescribeFailure(response.Body));
            return new HttpResponseMessage((System.Net.HttpStatusCode)response.Status) { Content = new ByteArrayContent(response.Body) };
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
