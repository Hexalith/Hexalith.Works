using System.Xml.Linq;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void P0_SourceProjectReferencesFollowWorksArchitectureDirection()
    {
        string root = RepositoryRoot.Locate();

        ProjectReferenceNames(root, "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj")
            .ShouldBe(
                ["Hexalith.EventStore.Contracts"],
                ignoreOrder: true,
                customMessage: "Contracts may reference only EventStore.Contracts.");

        ProjectReferenceNames(root, "src/Hexalith.Works.Server/Hexalith.Works.Server.csproj")
            .ShouldBe(
                ["Hexalith.Works.Contracts"],
                ignoreOrder: true,
                customMessage: "Server must reference inward to Contracts only.");

        ProjectReferenceNames(root, "src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj")
            .ShouldBe(
                ["Hexalith.Works.Contracts"],
                ignoreOrder: true,
                customMessage: "Projections must reference Contracts only.");

        ProjectReferenceNames(root, "src/Hexalith.Works.Reactor/Hexalith.Works.Reactor.csproj")
            .ShouldBe(
                ["Hexalith.Works.Contracts"],
                ignoreOrder: true,
                customMessage: "Reactor is an adapter-ring project and must reference inward to Contracts only.");
    }

    [Fact]
    public void P0_AppHostReferencesOnlyWorksTopologyProjects()
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
                customMessage: "AppHost should wire only the Works topology and EventStore Aspire support.");
    }

    [Fact]
    public void P0_ContractsDoesNotReferenceSiblingImplementationProjects()
    {
        string root = RepositoryRoot.Locate();
        string[] forbiddenSiblingProjects =
        [
            "Hexalith.Parties.Client",
            "Hexalith.Parties.Server",
            "Hexalith.Conversations.Client",
            "Hexalith.Conversations.Server",
            "Hexalith.Tenants.Server",
            "Hexalith.EventStore.Client",
            "Hexalith.EventStore.Server",
            "Hexalith.EventStore.Aspire",
        ];

        string[] references = ProjectReferenceNames(root, "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj");

        string[] violations = [.. references
            .Where(reference => forbiddenSiblingProjects.Any(forbidden => reference.StartsWith(forbidden, StringComparison.Ordinal)))];

        violations.ShouldBeEmpty("Works contracts may expose only reference IDs and must not depend on sibling client, server, adapter, or runtime projects.");
    }

    [Fact]
    public void P0_HexalithDependenciesUseProjectReferencesNotPackageReferences()
    {
        string root = RepositoryRoot.Locate();
        string[] projectFiles = [.. Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))];

        // Guard against a vacuous pass: if discovery returns nothing (wrong root, packaged output dir),
        // the violation scan below would trivially pass while enforcing almost nothing.
        projectFiles.ShouldNotBeEmpty("Expected to discover Hexalith.Works project files to govern.");
        projectFiles.ShouldContain(
            path => Path.GetFileName(path) == "Hexalith.Works.Contracts.csproj",
            "Hexalith.Works.Contracts.csproj must be discovered for this fitness guard to be meaningful.");

        string[] packageFiles = [Path.Combine(root, "Directory.Packages.props"), .. projectFiles];

        string[] violations = [.. packageFiles
            .SelectMany(file => PackageReferenceNames(file)
                .Where(name => name.StartsWith("Hexalith.", StringComparison.Ordinal))
                .Select(name => $"{Path.GetRelativePath(root, file)} contains PackageReference/PackageVersion {name}"))];

        violations.ShouldBeEmpty("Hexalith libraries must be consumed from checked-out sibling source with ProjectReference, never NuGet PackageReference or Directory.Packages.props entries.");
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

    private static string[] PackageReferenceNames(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);

        return [.. project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "PackageVersion")
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))];
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

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
