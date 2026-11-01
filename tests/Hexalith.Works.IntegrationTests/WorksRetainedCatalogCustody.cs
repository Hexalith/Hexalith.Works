using System.Security.Cryptography;
using System.Text.Json;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.EventStore.Contracts.Routing;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Checks the exact fixture-owned backend and stopped-fleet proof without removing any retained fence.</summary>
internal static class WorksRetainedCatalogCustody
{
    /// <summary>This disposable proof supports only its exact nondefault, unauthenticated, database-zero Redis.</summary>
    internal static void RequireOwnedBackend(byte[] bytes, string component, string endpoint, bool business = false)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var entries = root.GetProperty("spec").GetProperty("metadata").EnumerateArray().ToArray();
        string[] allowed = business ? ["actorStateStore", "redisHost", "redisPassword"] : ["keyPrefix", "redisHost", "redisPassword"];
        if (!endpoint.StartsWith("127.0.0.1:", StringComparison.Ordinal) || !int.TryParse(endpoint[10..], out int port) || port is < 1 or > 65535 || port == 6379
            || root.GetProperty("kind").GetString() != "Component" || root.GetProperty("metadata").GetProperty("name").GetString() != component
            || root.GetProperty("spec").GetProperty("type").GetString() != "state.redis"
            || entries.Length != 3 || !entries.Select(entry => entry.GetProperty("name").GetString()).Order(StringComparer.Ordinal).SequenceEqual(allowed, StringComparer.Ordinal)
            || entries.Any(entry => entry.TryGetProperty("secretKeyRef", out _)))
            throw new InvalidOperationException("The retained authority is not the exact isolated Redis backend.");
        var metadata = entries.ToDictionary(entry => entry.GetProperty("name").GetString()!, entry => entry.GetProperty("value").GetString(), StringComparer.Ordinal);
        if (metadata["redisHost"] != endpoint || metadata["redisPassword"] != "" || metadata[business ? "actorStateStore" : "keyPrefix"] != (business ? "true" : "name"))
            throw new InvalidOperationException("The retained authority backend coordinates or key prefix differ from their owned fixture.");
    }

    /// <summary>Every independently retained host receipt binds the same exact committed generation and complete roster.</summary>
    internal static void RequireReceipts(RouteCatalogActivation activation, RouteCatalogEnvelope catalog, long epoch,
        RouteCatalogHostIdentity host, RouteCatalogHostAttestation ready, RouteCatalogGenerationReadback committed)
    {
        if (ready.AttemptId != activation.AttemptId || ready.BindingDigest != activation.TrustProof || ready.Host != host
            || ready.Generation != catalog.Generation || ready.RootDigest != activation.RootDigest
            || ready.RouteFacetDigest != catalog.RouteFacetDigest || ready.IdempotencyFacetDigest != catalog.IdempotencyFacetDigest || ready.Profile != catalog.Profile
            || !committed.Serving || committed.Epoch != epoch || committed.Generation != catalog.Generation || committed.RootDigest != activation.RootDigest
            || committed.RouteFacetDigest != catalog.RouteFacetDigest || committed.IdempotencyFacetDigest != catalog.IdempotencyFacetDigest
            || committed.Profile != catalog.Profile || committed.RequiredHosts.Count != activation.RequiredHosts.Count
            || committed.RequiredHosts.Distinct().Count() != committed.RequiredHosts.Count
            || activation.RequiredHosts.Any(required => !committed.RequiredHosts.Contains(required)))
            throw new InvalidOperationException("The retained physical host receipts do not bind the actual complete committed generation.");
    }

    /// <summary>Captures a genuine fence only with its signed complete old fleet and fresh independently observed quiescence.</summary>
    internal static void RequireStoppedFences(WorksFleetOwner owner, WorksStoppedFleetDocument proof, RouteCatalogActivationState state,
        byte[] backend, string endpoint, string profilePath, string boot, string pidNamespace, IReadOnlyDictionary<int, WorksFleetProcess> processes)
    {
        if (owner.Backend != endpoint || Path.GetFullPath(owner.ProfilePath) != Path.GetFullPath(profilePath)
            || proof.BackendDigest != Convert.ToHexStringLower(SHA256.HashData(backend)))
            throw new InvalidOperationException("The retained fence proof belongs to different backend custody.");
        WorksFleetQuiescence.ValidateDocument(owner, proof, state);
        WorksFleetQuiescence.RequireStopped(proof.Scope, boot, pidNamespace, processes);
    }
}
