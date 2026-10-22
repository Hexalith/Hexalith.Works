using System.IO;

using Shouldly;

namespace Hexalith.Works.IntegrationTests.SchemaEvolution;

/// <summary>Checks corpus membership against source so incremental output cannot conceal deleted fixtures.</summary>
internal static class GoldenCorpusFiles
{
    /// <summary>Requires source and copied fixture membership to equal the catalog independently.</summary>
    internal static void AssertMembership(IEnumerable<string> catalogFileNames, string sourceDirectory, string outputDirectory)
    {
        string[] expected = [.. catalogFileNames.Order(StringComparer.Ordinal)];
        FileNames(sourceDirectory).ShouldBe(expected, ignoreOrder: false,
            customMessage: "Corpus source filenames must match the catalog even when stale copied fixtures remain.");
        FileNames(outputDirectory).ShouldBe(expected, ignoreOrder: false,
            customMessage: "Copied corpus filenames must match the catalog in both directions.");
    }

    /// <summary>Locates one unambiguous checkout above the output or working directory.</summary>
    /// <param name="corpusName">The corpus subdirectory name.</param>
    /// <param name="searchRoots">Optional search roots for isolated discovery regression tests.</param>
    /// <returns>The corpus directory in the uniquely identified checkout.</returns>
    internal static string SourceDirectory(string corpusName, params string[] searchRoots)
    {
        // The working directory also supports runs whose custom artifacts directory is outside the repository.
        string[] starts = searchRoots.Length == 0 ? [AppContext.BaseDirectory, Directory.GetCurrentDirectory()] : searchRoots;
        var candidates = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string start in starts)
        {
            for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
            {
                string project = Path.Combine(current.FullName, "tests", "Hexalith.Works.IntegrationTests");
                if (File.Exists(Path.Combine(current.FullName, "Hexalith.Works.slnx")) &&
                    File.Exists(Path.Combine(project, "Hexalith.Works.IntegrationTests.csproj")))
                {
                    candidates.Add(Path.Combine(project, "SchemaEvolution", corpusName));
                    break;
                }
            }
        }

        return candidates.Count switch
        {
            0 => throw new DirectoryNotFoundException("Could not locate the Works source corpus; copied output cannot prove source membership."),
            1 => candidates.Single(),
            _ => throw new InvalidOperationException("Conflicting Works source checkouts were found above the output and working directories."),
        };
    }

    private static string[] FileNames(string directory)
        => Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
