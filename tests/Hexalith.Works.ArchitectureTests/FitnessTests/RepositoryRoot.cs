namespace Hexalith.Works.ArchitectureTests.FitnessTests;

internal static class RepositoryRoot
{
    public static string Locate()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && File.Exists(Path.Combine(directory.FullName, ".gitmodules")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Hexalith.Works repository root above '{AppContext.BaseDirectory}'.");
    }

    public static string PathFromRoot(params string[] segments)
        => Path.Combine([Locate(), .. segments]);

    public static string DependencyRoot(string repositoryName)
    {
        string nestedRoot = PathFromRoot("references", repositoryName);
        if (File.Exists(Path.Combine(nestedRoot, ".git"))
            || Directory.Exists(Path.Combine(nestedRoot, ".git")))
        {
            return nestedRoot;
        }

        string siblingRoot = Path.GetFullPath(Path.Combine(Locate(), "..", repositoryName));
        return File.Exists(Path.Combine(siblingRoot, ".git"))
            || Directory.Exists(Path.Combine(siblingRoot, ".git"))
            ? siblingRoot
            : nestedRoot;
    }
}
