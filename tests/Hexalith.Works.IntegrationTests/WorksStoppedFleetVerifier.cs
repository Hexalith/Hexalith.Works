using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Platform.Composition;

using Microsoft.Extensions.Configuration;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Explicit test-owner verifier; each authority CAS retry independently rechecks actual quiescence.</summary>
internal sealed class WorksStoppedFleetVerifier(IConfiguration configuration, IRouteCatalogStateStore store) : IRouteCatalogQuiescenceVerifier
{
    internal async Task<(WorksFleetOwner Owner, WorksStoppedFleetDocument Document, RouteCatalogActivationState State)> ReadAsync(CancellationToken token)
    {
        string path = configuration[WorksFleetQuiescence.OwnerEnvironment]!;
        var owner = await WorksFleetQuiescence.ReadOwnerAsync(path, token).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<WorksStoppedFleetDocument>(await WorksProofCallerProtocol.ReadAsync(
            Path.Combine(Path.GetDirectoryName(path)!, WorksFleetQuiescence.ProofName), token).ConfigureAwait(false))!;
        var state = (await store.ReadAsync(token).ConfigureAwait(false)).State;
        WorksFleetQuiescence.ValidateDocument(owner, document, state);
        WorksFleetQuiescence.RequireStopped(document.Scope, WorksFleetQuiescence.Boot(), WorksFleetQuiescence.PidNamespace(), WorksFleetQuiescence.Processes());
        var profile = JsonSerializer.Deserialize<WorksDevelopmentCatalogProfile>(await WorksProofCallerProtocol.ReadAsync(owner.ProfilePath, token).ConfigureAwait(false))!;
        if (store.Coordinates is not { } coordinates || coordinates.StateStore != profile.Catalog.AuthorityComponentName
            || coordinates.StateKey != profile.Catalog.AuthorityStateKey)
            throw new RouteCatalogUnavailableException("The verifier is not attached to the retained owner's actual authority cell.");
        string directory = configuration["EventStore:RouteCatalog:DevelopmentBootstrap:Directory"]!;
        WorksProofCallerProtocol.RequireDirectory(directory);
        byte[] actualBackend = await WorksProofCallerProtocol.ReadAsync(Path.Combine(directory, "authority.backend.json"), token).ConfigureAwait(false);
        if (Convert.ToHexStringLower(SHA256.HashData(actualBackend)) != document.BackendDigest)
            throw new RouteCatalogUnavailableException("The successor's actual authority backend differs from the stopped owner.");
        return (owner, document, state);
    }

    public async Task VerifyAsync(RouteCatalogQuiescenceRequest request, RouteCatalogDispatchFence fence, CancellationToken cancellationToken)
    {
        var verified = await ReadAsync(cancellationToken).ConfigureAwait(false);
        string expected = WorksFleetQuiescence.FenceProof(verified.Owner, verified.Document, fence);
        if (request.LeaseId != fence.LeaseId || request.RootDigest != fence.RootDigest || request.Epoch != fence.Epoch
            || request.Proof.Length != 64 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(request.Proof)))
            throw new RouteCatalogUnavailableException("The owner proof is not for this exact retained dispatch fence.");
    }
}
