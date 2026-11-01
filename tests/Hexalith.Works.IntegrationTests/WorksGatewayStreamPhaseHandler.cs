namespace Hexalith.Works.IntegrationTests;

/// <summary>Forwards the same request, response and cancellation; records no headers, URLs or bodies.</summary>
internal sealed class WorksGatewayStreamPhaseHandler(WorksReminderPhaseDiagnostics diagnostics) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath.EndsWith("/api/v1/streams/read/workload", StringComparison.Ordinal) != true)
            return await base.SendAsync(request, token).ConfigureAwait(false);
        long sequence = diagnostics.Begin("gateway-stream-http", "workload-stream-read");
        try
        {
            var response = await base.SendAsync(request, token).ConfigureAwait(false);
            diagnostics.End(sequence, null, status: (int)response.StatusCode);
            return response;
        }
        catch (Exception exception)
        {
            diagnostics.End(sequence, exception);
            throw;
        }
    }
}
