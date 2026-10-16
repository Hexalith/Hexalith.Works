namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises the actual SDK HTTP response handling with controlled canonical gateway responses.</summary>
internal sealed class WorksSdkHttpEvidenceHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => handler(request, cancellationToken);
}
