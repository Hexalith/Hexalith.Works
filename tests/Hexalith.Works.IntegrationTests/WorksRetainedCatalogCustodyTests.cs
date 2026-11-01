using System.Text.Json;
using Shouldly;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Works.Routing;

namespace Hexalith.Works.IntegrationTests;

/// <summary>The actual retained descriptor must point to the one owned Redis namespace before capture.</summary>
public sealed class WorksRetainedCatalogCustodyTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("other-host")]
    [InlineData("other-attempt")]
    [InlineData("wrong-binding")]
    [InlineData("missing-replica")]
    [InlineData("copied-replica")]
    [InlineData("stale-epoch")]
    [InlineData("not-serving")]
    [InlineData("other-facet")]
    public void PhysicalReceiptsBindTheWholeActualCommittedGeneration(string fault)
    {
        var declaration = WorksCatalogDeclarations.Routes.First(route => route.Kind == "query");
        var operation = WorksCatalogDeclarations.Operations.Single(item => item.RouteId == declaration.RouteId);
        var catalog = RouteCatalogCodec.Seal(new(3, 1, "Development", false, [declaration],
            [new(declaration.RouteId, 1, RouteCatalogCodec.ComputeDeclarationDigest(declaration), "works", "query", "v1",
                declaration.CredentialKind, declaration.Operation, false, "fixture", operation.Digest)], [], "", "", [operation], []));
        string root = RouteCatalogCodec.ComputeRootDigest(catalog), digest = new('a', 64);
        var host = new RouteCatalogHostIdentity("works", "works", digest, digest, "policy");
        var peer = host with { HostId = "works-ha" };
        var activation = new RouteCatalogActivation("attempt", RouteCatalogCodec.Encode(catalog), root, null, [host, peer], "owner", "binding");
        var ready = new RouteCatalogHostAttestation("attempt", "binding", 1, root, catalog.RouteFacetDigest, catalog.IdempotencyFacetDigest, "Development", host);
        var committed = new RouteCatalogGenerationReadback(2, 1, root, catalog.RouteFacetDigest, catalog.IdempotencyFacetDigest, "Development", [host, peer], true);
        if (fault == "other-host") ready = ready with { Host = peer };
        if (fault == "other-attempt") ready = ready with { AttemptId = "foreign" };
        if (fault == "wrong-binding") ready = ready with { BindingDigest = "foreign" };
        if (fault == "missing-replica") committed = committed with { RequiredHosts = [host] };
        if (fault == "copied-replica") committed = committed with { RequiredHosts = [host, host] };
        if (fault == "stale-epoch") committed = committed with { Epoch = 1 };
        if (fault == "not-serving") committed = committed with { Serving = false };
        if (fault == "other-facet") committed = committed with { RouteFacetDigest = new string('b', 64) };
        void Verify() => WorksRetainedCatalogCustody.RequireReceipts(activation, catalog, 2, host, ready, committed);
        if (fault == "valid") Verify(); else Should.Throw<InvalidOperationException>(Verify);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("default-host")]
    [InlineData("foreign-host")]
    [InlineData("foreign-component")]
    [InlineData("different-prefix")]
    [InlineData("password")]
    [InlineData("other-database")]
    [InlineData("secret-reference")]
    public void AuthorityBackendCannotBorrowDefaultOrDifferentCoordinates(string fault)
    {
        var metadata = new List<Dictionary<string, string>>
        {
            new() { ["name"] = "redisHost", ["value"] = fault == "default-host" ? "localhost:6379" : fault == "foreign-host" ? "127.0.0.1:27124" : "127.0.0.1:27123" },
            new() { ["name"] = "redisPassword", ["value"] = fault == "password" ? "foreign" : "" },
            new() { ["name"] = "keyPrefix", ["value"] = fault == "different-prefix" ? "appid" : "name" },
        };
        if (fault == "other-database") metadata.Add(new() { ["name"] = "redisDB", ["value"] = "1" });
        if (fault == "secret-reference") metadata[0].Add("secretKeyRef", "foreign");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "Component", metadata = new { name = fault == "foreign-component" ? "other" : "routing-owned" },
            spec = new { type = "state.redis", metadata } });
        void Verify() => WorksRetainedCatalogCustody.RequireOwnedBackend(bytes, "routing-owned", "127.0.0.1:27123");
        if (fault == "valid") Verify(); else Should.Throw<InvalidOperationException>(Verify);
    }
}
