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
    {
        string repositoryRoot = GetRepositoryRoot();
        string nestedPath = Path.Combine(repositoryRoot, Path.Combine(path));
        if (path.Length < 2
            || !string.Equals(path[0], "references", StringComparison.Ordinal)
            || File.Exists(Path.Combine(repositoryRoot, path[0], path[1], ".git"))
            || Directory.Exists(Path.Combine(repositoryRoot, path[0], path[1], ".git")))
        {
            return nestedPath;
        }

        string siblingRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "..", path[1]));
        return File.Exists(Path.Combine(siblingRoot, ".git"))
            || Directory.Exists(Path.Combine(siblingRoot, ".git"))
            ? Path.Combine([siblingRoot, .. path.Skip(2)])
            : nestedPath;
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
