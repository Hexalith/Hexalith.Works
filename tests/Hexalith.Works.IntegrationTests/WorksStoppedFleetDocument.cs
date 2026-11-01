using Hexalith.EventStore.ServiceDefaults.Routing;

namespace Hexalith.Works.IntegrationTests;

internal sealed record WorksStoppedFleetDocument(string Retirement, string Owner, WorksFleetScope Scope,
    RouteCatalogDispatchFence[] Fences, string BackendDigest, string Signature);
