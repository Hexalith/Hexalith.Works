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
        return ResolveProjectPath(GetRepositoryRoot(),
            Environment.GetEnvironmentVariable("WORKS_PLATFORM_SDK_SOURCE_ROOT"), path);
    }

    /// <summary>Resolves an explicit compatible SDK fleet before Aspire materializes project defaults.</summary>
    internal static string ResolveProjectPath(string repositoryRoot, string? eventStoreSourceRoot, params string[] path)
    {
        if (path.Length >= 2 && path[0] == "references" && path[1] == "Hexalith.EventStore"
            && eventStoreSourceRoot is not null)
        {
            string selectedRoot = RequireExplicitRoot(eventStoreSourceRoot);
            string selected = Path.Combine([selectedRoot, .. path.Skip(2)]);
            if (!File.Exists(selected) && !Directory.Exists(selected))
                throw new InvalidOperationException("The selected SDK deployment metadata path does not exist: " + selected);
            return selected;
        }
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
        => Environment.GetEnvironmentVariable("WORKS_PLATFORM_WORKS_ROOT") is { } selected
            ? RequireExplicitRoot(selected)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string RequireExplicitRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            throw new InvalidOperationException("An explicit fleet source root must be an existing absolute directory.");
        return Path.GetFullPath(path);
    }
}
