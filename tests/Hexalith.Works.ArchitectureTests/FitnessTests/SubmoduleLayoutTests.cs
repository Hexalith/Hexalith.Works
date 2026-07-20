using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class SubmoduleLayoutTests
{
    [Fact]
    public void RootSubmodulesShouldLiveUnderReferences()
    {
        string root = RepositoryRoot.Locate();
        string gitModulesPath = Path.Combine(root, ".gitmodules");
        string[] submodulePaths = [.. File
            .ReadLines(gitModulesPath)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("path = ", StringComparison.Ordinal))
            .Select(line => line["path = ".Length..])
            .Order(StringComparer.Ordinal)];

        string[] expectedPaths =
        [
            "references/Hexalith.AI.Tools",
            "references/Hexalith.Builds",
            "references/Hexalith.Chatbot",
            "references/Hexalith.Commons",
            "references/Hexalith.Conversations",
            "references/Hexalith.EventStore",
            "references/Hexalith.FrontComposer",
            "references/Hexalith.Parties",
            "references/Hexalith.PolymorphicSerializations",
            "references/Hexalith.Projects",
            "references/Hexalith.Tenants",
        ];

        submodulePaths.ShouldBe(expectedPaths);
        foreach (string submodulePath in submodulePaths)
        {
            Directory.Exists(Path.Combine(root, submodulePath)).ShouldBeTrue(submodulePath);
        }

        Directory.GetDirectories(root, "Hexalith.*", SearchOption.TopDirectoryOnly).ShouldBeEmpty();
    }
}
