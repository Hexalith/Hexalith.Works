using Hexalith.EventStore.Contracts.Routing;

namespace Hexalith.Works.IntegrationTests;

internal sealed record WorksProofRuntimeInventory(int ProcessId, string HostId, string AssemblySha256, string ObservationSha256,
    string[] LegacyHandlers, string[] NamedHandlers, RouteCatalogEntry[] Targets, RouteCatalogGenerationReadback Generation);
