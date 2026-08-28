using Aspire.Hosting.ApplicationModel;

using Projects;

namespace Hexalith.Works.AppHost;

/// <summary>Cross-repo project metadata for the reusable EventStore operations workload.</summary>
public sealed class HexalithEventStoreOperations : IProjectMetadata
{
    /// <inheritdoc/>
    public string ProjectPath => ProjectMetadataPaths.GetProjectPath(
        "references",
        "Hexalith.EventStore",
        "src",
        "Hexalith.EventStore.Operations",
        "Hexalith.EventStore.Operations.csproj");

    /// <inheritdoc/>
    public bool SuppressBuild => true;
}
