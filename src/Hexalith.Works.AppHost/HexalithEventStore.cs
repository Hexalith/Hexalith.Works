using Aspire.Hosting.ApplicationModel;

using Projects;

namespace Hexalith.Works.AppHost;

/// <summary>Cross-repo project metadata for the EventStore command-gateway web host (root submodule).</summary>
public sealed class HexalithEventStore : IProjectMetadata
{
    /// <inheritdoc/>
    public string ProjectPath => ProjectMetadataPaths.GetProjectPath(
        "references",
        "Hexalith.EventStore",
        "src",
        "Hexalith.EventStore",
        "Hexalith.EventStore.csproj");

    /// <inheritdoc/>
    public bool SuppressBuild => true;
}
