using System.Net;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Observes real synchronous and asynchronous OTLP HTTP responses without changing the request or response.</summary>
/// <param name="observe">Receives the status and synthetic request bytes after the real transport completes.</param>
internal sealed class WorksPlatformTelemetryEvidenceHandler(Action<HttpStatusCode, byte[]> observe) : DelegatingHandler(new HttpClientHandler())
{
    /// <inheritdoc/>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
        HttpResponseMessage response = base.Send(request, cancellationToken);
        observe(response.StatusCode, body);
        return response;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        observe(response.StatusCode, body);
        return response;
    }
}
