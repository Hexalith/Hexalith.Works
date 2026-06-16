using System.Xml.Linq;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void P0_SourceProjectReferencesFollowStoryElevenArchitectureDirection()
    {
        string root = RepositoryRoot.Locate();

        ProjectReferenceNames(root, "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj")
            .ShouldBe(
                ["Hexalith.EventStore.Contracts"],
                ignoreOrder: true,
                customMessage: "Contracts may reference only EventStore.Contracts in Story 1.1.");

        ProjectReferenceNames(root, "src/Hexalith.Works.Server/Hexalith.Works.Server.csproj")
            .ShouldBe(
                ["Hexalith.Works.Contracts"],
                ignoreOrder: true,
                customMessage: "Server must reference inward to Contracts only in Story 1.1.");

        ProjectReferenceNames(root, "src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj")
            .ShouldBe(
                ["Hexalith.Works.Contracts"],
                ignoreOrder: true,
                customMessage: "Projections must reference Contracts only in Story 1.1.");

        ProjectReferenceNames(root, "src/Hexalith.Works.Reactor/Hexalith.Works.Reactor.csproj")
            .ShouldBe(
                ["Hexalith.Works.Contracts"],
                ignoreOrder: true,
                customMessage: "Reactor is an adapter-ring project and must reference inward to Contracts only in Story 1.1.");
    }

    [Fact]
    public void P0_AppHostReferencesOnlyStoryElevenTopologyProjects()
    {
        string root = RepositoryRoot.Locate();

        ProjectReferenceNames(root, "src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj")
            .ShouldBe(
                [
                    "Hexalith.Works.Contracts",
                    "Hexalith.Works.Projections",
                    "Hexalith.Works.Reactor",
                    "Hexalith.Works.Server",
                    "Hexalith.Works.ServiceDefaults",
                    "Hexalith.EventStore.Aspire",
                ],
                ignoreOrder: true,
                customMessage: "AppHost should wire only the Story 1.1 Works topology and EventStore Aspire support.");
    }

    private static string[] ProjectReferenceNames(string root, string relativeProjectPath)
    {
        string projectPath = Path.Combine(root, relativeProjectPath);
        XDocument project = XDocument.Load(projectPath);

        return [.. project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(ProjectNameFromReference)];
    }

    private static string ProjectNameFromReference(string include)
    {
        string normalized = include.Replace('\\', '/');
        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];

        return Path.GetFileNameWithoutExtension(fileName);
    }
}
