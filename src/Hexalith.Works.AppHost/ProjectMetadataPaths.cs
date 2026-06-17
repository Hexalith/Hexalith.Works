namespace Projects;

/// <summary>
/// Resolves cross-repo project paths from the AppHost output directory back to the Works repository root, so
/// the Works AppHost can reference the EventStore web host / Admin.Server.Host (root submodule) and the
/// runnable Works domain service via <see cref="Aspire.Hosting.ApplicationModel.IProjectMetadata"/> without a
/// build-time <c>ProjectReference</c> (mirrors the Tenants AppHost pattern).
/// </summary>
internal static class ProjectMetadataPaths
{
    /// <summary>Combines <paramref name="path"/> segments onto the repository root.</summary>
    public static string GetProjectPath(params string[] path)
        => Path.Combine(GetRepositoryRoot(), Path.Combine(path));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
