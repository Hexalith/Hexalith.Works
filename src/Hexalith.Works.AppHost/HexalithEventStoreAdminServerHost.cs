using Aspire.Hosting.ApplicationModel;

using Projects;

namespace Hexalith.Works.AppHost;

/// <summary>Cross-repo project metadata for the EventStore Admin.Server host (root submodule).</summary>
public sealed class HexalithEventStoreAdminServerHost : IProjectMetadata
{
    /// <inheritdoc/>
    public string ProjectPath => ProjectMetadataPaths.GetProjectPath(
        "Hexalith.EventStore",
        "src",
        "Hexalith.EventStore.Admin.Server.Host",
        "Hexalith.EventStore.Admin.Server.Host.csproj");

    /// <inheritdoc/>
    public bool SuppressBuild => true;
}
