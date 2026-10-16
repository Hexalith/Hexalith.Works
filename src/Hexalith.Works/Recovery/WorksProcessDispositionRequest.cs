using Hexalith.EventStore.Client.Processes;

namespace Hexalith.Works.Recovery;

/// <summary>Host-edge request to revalidate an existing SDK source under current operator authority.</summary>
internal sealed record WorksProcessDispositionRequest(ProcessSource Source, string Reason);
