using System.Net;
using System.Text.Json;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Fixture-local bounded replay of transient ordinary-command admission with unchanged identity, bytes and authentication.</summary>
internal sealed class WorksSdkProofOrdinaryRetryHandler(HttpClient client, Func<CancellationToken, Task>? onUnavailable = null) : HttpMessageHandler
{
    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? bytes = request.Content is null ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        bool canRetry = false;
        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/v1/commands" && bytes is not null)
        {
            using JsonDocument body = JsonDocument.Parse(bytes);
            if (body.RootElement.TryGetProperty("tenant", out JsonElement tenant))
                canRetry = tenant.GetString()?.StartsWith("sdk415-", StringComparison.Ordinal) == true;
        }
        for (int attempt = 0; ; attempt++)
        {
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
                Content = bytes is null ? null : new ByteArrayContent(bytes),
            };
            foreach (var header in request.Headers)
                forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (forwarded.Content is not null && request.Content is not null)
                foreach (var header in request.Content.Headers)
                    forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            HttpResponseMessage response = await client.SendAsync(forwarded, cancellationToken).ConfigureAwait(false);
            if (canRetry && response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt == 0 && onUnavailable is not null)
                await onUnavailable(cancellationToken).ConfigureAwait(false);
            if (!canRetry || response.StatusCode != HttpStatusCode.ServiceUnavailable || attempt >= 3)
                return response;
            TimeSpan delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            if (delay < TimeSpan.Zero) delay = TimeSpan.FromSeconds(1);
            if (delay > TimeSpan.FromSeconds(30)) delay = TimeSpan.FromSeconds(30);
            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
