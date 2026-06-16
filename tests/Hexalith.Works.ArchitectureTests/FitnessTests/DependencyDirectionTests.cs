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
            .Where(element => !IsConditionallyExcluded(element))
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(ProjectNameFromReference)];
    }

    // The fitness test must reflect the *realized* default build graph, not the raw XML. A reference
    // gated by a Condition (on the element itself or an ancestor ItemGroup) is excluded from the
    // unconditional build, so it must not be counted as present. This prevents a disabled (decorative)
    // reference from making the dependency-direction assertion pass while the real build omits it.
    private static bool IsConditionallyExcluded(XElement element)
    {
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            if (current.Attribute("Condition") is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static string ProjectNameFromReference(string include)
    {
        string normalized = include.Replace('\\', '/');
        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];

        return Path.GetFileNameWithoutExtension(fileName);
    }
}
