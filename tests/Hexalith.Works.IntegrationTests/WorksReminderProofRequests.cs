using System.Text.Json;

using Hexalith.EventStore.Contracts.Reminders;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Bounded private IPC to the actual Works host, never a synthetic Dapr sender.</summary>
internal static class WorksReminderProofRequests
{
    internal static async Task<WorksReminderProofResult> ConvergeAsync(string directory, string session, ReminderTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        WorksProofCallerProtocol.RequireDirectory(directory);
        string nonce = Guid.NewGuid().ToString("N"), path = Path.Combine(directory, nonce);
        var request = new WorksProofCallerRequest(nonce, session, "converge-reminder-twice", JsonSerializer.SerializeToElement(target), DateTimeOffset.UtcNow.AddSeconds(55));
        await WorksProofCallerProtocol.WriteNewAsync(path + ".request.json", request, token).ConfigureAwait(false);
        bool received = false;
        try
        {
            while (!File.Exists(path + ".response.json")) await Task.Delay(25, token).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<WorksProofCallerResponse>(await WorksProofCallerProtocol.ReadAsync(path + ".response.json", token).ConfigureAwait(false))!;
            received = true;
            if (response.Nonce != nonce || response.Session != session)
                throw new InvalidOperationException("The reminder driver response does not bind this private request.");
            if (response.Status != 200 || response.Failure is not null)
                throw new InvalidOperationException("The actual Works reminder driver refused: " + response.Failure
                    + "; bounded type/method evidence=" + DescribeFailure(response.Body));
            var result = JsonSerializer.Deserialize<WorksReminderProofResult>(response.Body)!;
            if (result.Outcomes.Length != 2 || result.Generation is not { Serving: true, Profile: "Development", Epoch: > 0 }
                || result.Generation.RequiredHosts.Count < 4 || result.Generation.RootDigest != response.Generation?.RootDigest
                || result.Generation.Epoch != response.Generation?.Epoch)
                throw new InvalidOperationException("The reminder proof lacks its complete matching committed cohort.");
            return result;
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

    internal static string DescribeFailure(byte[] body)
    {
        try
        {
            if (body.Length > 32 * 1024) return "unavailable";
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 4) return "unavailable";
            var shape = document.RootElement.EnumerateArray().Select(item => new
            {
                type = item.GetProperty("type").GetString(),
                remoteType = item.TryGetProperty("remoteType", out var remote) && remote.ValueKind == JsonValueKind.String
                    && remote.GetString() is { Length: > 0 and <= 512 } remoteType
                    && remoteType.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '`') ? remoteType : null,
                methods = item.GetProperty("methods").EnumerateArray().Select(method => method.GetString()).ToArray(),
            }).ToArray();
            if (shape.Any(item => item.type is not { Length: > 0 and <= 512 } || item.type.Any(char.IsControl)
                || item.methods.Length > 6 || item.methods.Any(method => method is not { Length: > 0 and <= 768 } || method.Any(char.IsControl)))) return "unavailable";
            return JsonSerializer.Serialize(shape);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { return "unavailable"; }
    }

}
