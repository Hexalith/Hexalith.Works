using System.Net.Http.Headers;

namespace Hexalith.Works.Queries;

/// <summary>Requires the independently provisioned service bearer on every authoritative Parties query.</summary>
/// <remarks>
/// The inbound request's own <c>Authorization</c> header is never forwarded: on SDK query ingress it is the
/// gateway workload assertion minted for Works, not a credential for the Parties gateway.
/// </remarks>
public sealed class WorksPartiesGatewayAuthorizationHandler : DelegatingHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthenticationHeaderValue? header = request.Headers.Authorization;
        if (header is null || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            throw new InvalidOperationException("Authoritative Parties query transport has no configured service bearer.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
