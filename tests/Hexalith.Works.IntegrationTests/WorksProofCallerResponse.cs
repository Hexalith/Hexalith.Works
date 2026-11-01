using Hexalith.EventStore.Contracts.Routing;

namespace Hexalith.Works.IntegrationTests;

internal sealed record WorksProofCallerResponse(string Nonce, string Session, int Status, byte[] Body, string? ContentType,
    RouteCatalogGenerationReadback? Generation, string? Failure);
