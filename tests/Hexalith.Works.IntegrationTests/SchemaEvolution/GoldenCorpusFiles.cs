using System.IO;

namespace Hexalith.Works.IntegrationTests.SchemaEvolution;

/// <summary>Checks corpus membership against source so incremental output cannot conceal deleted fixtures.</summary>
internal static class GoldenCorpusFiles
{
    /// <summary>Lists source fixture paths relative to the named corpus, independently of copied output.</summary>
    internal static string[] SourceFileNames(string corpusName)
    {
        string directory = SourceDirectory(corpusName);
        return Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string SourceDirectory(string corpusName)
    {
        // The working directory also supports runs whose custom artifacts directory is outside the repository.
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
            {
                string project = Path.Combine(current.FullName, "tests", "Hexalith.Works.IntegrationTests");
                if (File.Exists(Path.Combine(current.FullName, "Hexalith.Works.slnx")) &&
                    File.Exists(Path.Combine(project, "Hexalith.Works.IntegrationTests.csproj")))
                {
                    return Path.Combine(project, "SchemaEvolution", corpusName);
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Works source corpus; copied output cannot prove source membership.");
    }
}
