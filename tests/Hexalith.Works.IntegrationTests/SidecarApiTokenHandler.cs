
namespace Hexalith.Works.IntegrationTests;

internal sealed class SidecarApiTokenHandler(Uri sidecar, string? apiToken) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } address || address.Scheme != sidecar.Scheme
            || address.Authority != sidecar.Authority || address.UserInfo.Length != 0)
            throw new InvalidOperationException("A selected sidecar credential cannot be sent to another endpoint.");
        if (!string.IsNullOrEmpty(apiToken) && !request.Headers.Contains("dapr-api-token"))
            request.Headers.Add("dapr-api-token", apiToken);
        return base.SendAsync(request, cancellationToken);
    }
}
