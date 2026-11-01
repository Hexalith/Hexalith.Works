using System.Text.Json;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Private test IPC, never an authority or a remote HTTP receiver.</summary>
internal sealed class WorksProofCallerTransport(string directory, string session, Uri sidecar) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _raw = new(new SocketsHttpHandler { AllowAutoRedirect = false });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (!request.Options.TryGetValue(WorksProofCallerProtocol.UseActualSender, out bool actual) || !actual)
            return await _raw.SendAsync(request, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        WorksProofCallerProtocol.RequireDirectory(directory);
        const string prefix = "/v1.0/invoke/works/method/";
        if (request.Method != HttpMethod.Post || request.RequestUri is not { IsAbsoluteUri: true } uri
            || uri.Scheme != sidecar.Scheme || uri.Authority != sidecar.Authority || uri.UserInfo.Length != 0 || uri.Query.Length != 0
            || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            || uri.OriginalString.Contains('%', StringComparison.Ordinal) || uri.OriginalString.Contains('\\', StringComparison.Ordinal)
            || uri.OriginalString.Split('/').Any(segment => segment is "." or "..")
            || request.Content is null)
            throw new InvalidOperationException("A private proof cannot choose another resource or transport.");
        string command = WorksProofCallerProtocol.Command(uri.AbsolutePath[prefix.Length..]);
        if (request.Headers.Any(header => !string.Equals(header.Key,
            Hexalith.EventStore.ServiceDefaults.Authentication.EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The private SDK proof caller accepts no forwarded headers or credentials.");
        byte[] body = await request.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        if (body.Length > WorksProofCallerProtocol.MaximumBytes) throw new InvalidOperationException("The private proof payload exceeds its bound.");
        using var payload = JsonDocument.Parse(body);
        string nonce = Guid.NewGuid().ToString("N");
        string path = Path.Combine(directory, nonce);
        var message = new WorksProofCallerRequest(nonce, session, command, payload.RootElement.Clone(), DateTimeOffset.UtcNow.AddSeconds(90));
        await WorksProofCallerProtocol.WriteNewAsync(path + ".request.json", message, token).ConfigureAwait(false);
        bool received = false;
        try
        {
            while (!File.Exists(path + ".response.json")) await Task.Delay(25, token).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<WorksProofCallerResponse>(await WorksProofCallerProtocol.ReadAsync(path + ".response.json", token).ConfigureAwait(false))!;
            received = true;
            if (result.Nonce != nonce || result.Session != session || result.Failure is not null || result.Generation is not { Serving: true, Profile: "Development", Epoch: > 0 }
                || result.Generation.RequiredHosts.Count < 4)
                throw new InvalidOperationException("The actual SDK caller refused or did not prove its current committed cohort: " + result.Failure);
            var response = new HttpResponseMessage((System.Net.HttpStatusCode)result.Status) { Content = new ByteArrayContent(result.Body), RequestMessage = request };
            if (result.ContentType is not null) response.Content.Headers.TryAddWithoutValidation("Content-Type", result.ContentType);
            response.Headers.TryAddWithoutValidation("X-Works-Proof-Caller-Root", result.Generation.RootDigest);
            response.Headers.TryAddWithoutValidation("X-Works-Proof-Caller-Epoch", result.Generation.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
            // An unacknowledged request stays private for diagnosis; a completed exchange can be removed.
            if (received) foreach (string suffix in new[] { ".request.json", ".response.json", ".cancel" }) File.Delete(path + suffix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _raw.Dispose();
        base.Dispose(disposing);
    }
}
