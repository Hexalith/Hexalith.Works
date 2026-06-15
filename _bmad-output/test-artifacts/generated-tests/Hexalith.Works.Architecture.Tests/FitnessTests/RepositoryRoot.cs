namespace Hexalith.Works.Architecture.Tests.FitnessTests;

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
}
