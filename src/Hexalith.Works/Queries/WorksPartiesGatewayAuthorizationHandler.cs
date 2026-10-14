using System.Net.Http.Headers;

namespace Hexalith.Works.Queries;

/// <summary>Uses configured service authority or the inbound authenticated bearer for Parties queries.</summary>
public sealed class WorksPartiesGatewayAuthorizationHandler(IHttpContextAccessor contexts) : DelegatingHandler
{
    private readonly IHttpContextAccessor _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthenticationHeaderValue? header = request.Headers.Authorization;
        if (header is null)
        {
            _ = AuthenticationHeaderValue.TryParse(_contexts.HttpContext?.Request.Headers.Authorization.ToString(), out header);
        }

        if (header is null || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            throw new InvalidOperationException("Authoritative Parties query transport has no authenticated bearer.");
        }

        request.Headers.Authorization = header;
        return base.SendAsync(request, cancellationToken);
    }
}
