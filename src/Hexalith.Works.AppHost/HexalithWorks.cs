using Aspire.Hosting.ApplicationModel;

using Projects;

namespace Hexalith.Works.AppHost;

/// <summary>Project metadata for the runnable Works domain-service host (<c>src/Hexalith.Works</c>).</summary>
public sealed class HexalithWorks : IProjectMetadata
{
    /// <inheritdoc/>
    public string ProjectPath => ProjectMetadataPaths.GetProjectPath(
        "src",
        "Hexalith.Works",
        "Hexalith.Works.csproj");

    /// <inheritdoc/>
    public bool SuppressBuild => true;
}
