using Hexalith.EventStore.Contracts.Authorization;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.Parties.Contracts;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.Queries;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Test-owned metadata for the fixture's existing identity reader over the public Parties DTOs.
/// Parties 2.0 packages do not ship owner catalog metadata; this is not production Parties enrollment.</summary>
internal static class WorksIdentityReaderCatalog
{
    internal static RouteContractDeclaration Declaration { get; } = new("party-query-identity", "query", "party",
        typeof(ResolvePartyIdentity).FullName!, null, "v1", null, "workload", EventStoreOperationIdentifiers.DomainServiceQuery,
        null, [], new(typeof(ResolvePartyIdentity).FullName!, "exact", [typeof(ResolvePartyIdentity).FullName!], "domain-service", "computed", []));

    internal static RouteOperationContract Operation { get; } = RouteOperationSchema.Create(Declaration,
        typeof(ResolvePartyIdentity), typeof(PartyIdentityResult), "internal", "UI-only", PartiesJsonOptions.Default);
}
