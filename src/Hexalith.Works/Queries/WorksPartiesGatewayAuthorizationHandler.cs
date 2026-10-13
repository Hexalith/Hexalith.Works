using System.Net.Http.Headers;

namespace Hexalith.Works.Queries;

/// <summary>Forwards only the inbound authenticated bearer to authoritative Parties queries.</summary>
public sealed class WorksPartiesGatewayAuthorizationHandler(IHttpContextAccessor contexts) : DelegatingHandler
{
    private readonly IHttpContextAccessor _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? authorization = _contexts.HttpContext?.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            throw new InvalidOperationException("Authoritative Parties query transport has no authenticated bearer.");
        }

        request.Headers.Authorization = header;
        return base.SendAsync(request, cancellationToken);
    }
}
