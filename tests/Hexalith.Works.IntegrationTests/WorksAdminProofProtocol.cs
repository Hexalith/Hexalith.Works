using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.ServiceDefaults.Authentication;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Finite qualification vocabulary for the actual Admin operator and its reviewed source inventory.</summary>
internal static class WorksAdminProofProtocol
{
    internal const string DirectoryKey = "WORKS_SDK_PROOF_ADMIN_DIRECTORY";
    internal const string SessionKey = "WORKS_SDK_PROOF_ADMIN_SESSION";
    internal const string Route = "work/processes/revalidate";
    internal const string Operation = "domain-service:process process:dispose";

    internal static void Validate(WorksAdminProofRequest request, string nonce, string session, DateTimeOffset now,
        IReadOnlyCollection<ProcessSource> grants)
    {
        if (!Guid.TryParseExact(nonce, "N", out _) || request.Nonce != nonce || request.Session != session
            || request.ExpiresAt <= now || request.ExpiresAt > now.AddMinutes(1)
            || request.Kind is not ("accepted" or "wrong-purpose" or "wrong-tenant" or "wrong-aggregate" or "audit-outage")
            || request.Source is null || !grants.Contains(request.Source))
            throw new InvalidOperationException("The finite Admin proof is stale, foreign or malformed.");
        request.Source.Validate();
    }

    internal static IReadOnlyDictionary<string, string> Bindings(WorksAdminProofRequest request) => new Dictionary<string, string>
    {
        [EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType] = request.Source.Tenant + (request.Kind == "wrong-tenant" ? "-foreign" : string.Empty),
        [EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType] = request.Source.Domain,
        [EventStoreWorkloadAuthenticationDefaults.BindingClaimPrefix + "aggregate"] = request.Source.Aggregate + (request.Kind == "wrong-aggregate" ? "-foreign" : string.Empty),
        [EventStoreWorkloadAuthenticationDefaults.ActorBindingClaimType] = "synthetic-operator-416",
        [EventStoreWorkloadAuthenticationDefaults.BindingClaimPrefix + "purpose"] = request.Kind == "wrong-purpose" ? "wrong-purpose" : "works.process-disposition",
    };
}
