using Hexalith.EventStore.ServiceDefaults.Authentication;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Issues fixture RSA workload assertions the proof JWT authority can validate without Keycloak.</summary>
internal sealed class WorksSdkProofWorkloadAssertionIssuer(RsaSecurityKey key, string? workload) : IWorkloadAssertionIssuer
{
    /// <inheritdoc/>
    public bool CanBindResources => true;

    /// <inheritdoc/>
    public ValueTask<string?> IssueAsync(WorkloadAssertionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation);
        cancellationToken.ThrowIfCancellationRequested();
        string caller = workload is { Length: > 0 }
            ? workload
            : Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_ROLE") switch
            {
                "eventstore" => "eventstore",
                "eventstore-admin" => "eventstore-admin",
                "operations" => "eventstore-operations",
                _ => "works",
            };
        DateTime now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [EventStoreWorkloadAuthenticationDefaults.CallerClaimType] = caller,
            [EventStoreWorkloadAuthenticationDefaults.OperationClaimType] = request.Operation,
            [RouteCatalogSurfacePolicy.ClaimType] = "service",
        };
        if (request.Bindings is not null)
        {
            foreach (KeyValuePair<string, string> binding in request.Bindings)
            {
                claims[binding.Key] = binding.Value;
            }
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = WorksSdkProofDelegationProvider.Issuer,
            Audience = request.Audience.Trim(),
            IssuedAt = now,
            NotBefore = now.AddSeconds(-1),
            Expires = now.AddMinutes(2),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = claims,
        };
        return ValueTask.FromResult<string?>(
            new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor));
    }
}
