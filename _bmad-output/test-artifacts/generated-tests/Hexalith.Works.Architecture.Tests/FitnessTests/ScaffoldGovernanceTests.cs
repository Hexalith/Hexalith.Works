using System.Xml.Linq;

using Shouldly;

using Xunit;

namespace Hexalith.Works.Architecture.Tests.FitnessTests;

public sealed class ScaffoldGovernanceTests
{
    private static readonly string[] RequiredProjects =
    [
        "Hexalith.Works.Contracts",
        "Hexalith.Works.Server",
        "Hexalith.Works.Projections",
        "Hexalith.Works.Reactor",
        "Hexalith.Works.ServiceDefaults",
        "Hexalith.Works.AppHost",
        "Hexalith.Works.Testing",
    ];

    private static readonly string[] ForbiddenProjectFragments =
    [
        ".UI",
        ".Mcp",
        ".AdminPortal",
        ".ConsumerPortal",
        ".Security",
        ".Routing",
        ".Llm",
        ".LLM",
        ".CostGovernance",
        ".Email",
        ".Channel",
    ];

    [Fact]
    public void P0_ScaffoldContainsOnlyTheV1ProjectSet()
    {
        // Given the Works scaffold has been created from the starter template.
        string root = RepositoryRoot.Locate();

        // When root-owned Works project files are inspected.
        string[] projectFiles = Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        string[] projectNames = [.. projectFiles.Select(path => Path.GetFileNameWithoutExtension(path))];

        // Then only the v1 architecture project set is present.
        foreach (string requiredProject in RequiredProjects)
        {
            projectNames.ShouldContain(requiredProject, $"Story 1.1 requires '{requiredProject}' in the Works scaffold.");
        }

        string[] forbiddenProjects = [.. projectNames.Where(name => ForbiddenProjectFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))];
        forbiddenProjects.ShouldBeEmpty("Story 1.1 excludes UI, MCP, portal, security, routing, LLM, cost-governance, and production channel-adapter projects.");
    }

    [Fact]
    public void P0_ScaffoldUsesSlnxAndCentralPackageManagement()
    {
        // Given the Works scaffold has solution and package-management files.
        string root = RepositoryRoot.Locate();

        // When the scaffold metadata is inspected.
        File.Exists(Path.Combine(root, "Hexalith.Works.slnx")).ShouldBeTrue("Story 1.1 requires Hexalith.Works.slnx.");
        File.Exists(Path.Combine(root, "Hexalith.Works.sln")).ShouldBeFalse("Use .slnx, not .sln.");
        File.Exists(Path.Combine(root, "Directory.Packages.props")).ShouldBeTrue("Central package management must define package versions outside project files.");

        string[] projectFiles = Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        foreach (string projectFile in projectFiles)
        {
            XDocument project = XDocument.Load(projectFile);
            string[] inlineVersions = [.. project.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Where(element => element.Attribute("Version") is not null || element.Element("Version") is not null)
                .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? projectFile)];

            // Then package versions are not declared inline.
            inlineVersions.ShouldBeEmpty($"Package versions must be managed centrally, not inline in '{projectFile}'.");
        }
    }

    [Fact]
    public void P0_KernelProjectsStayInfrastructureFree()
    {
        // Given the Works kernel projects exist.
        string root = RepositoryRoot.Locate();
        string[] kernelProjects =
        [
            "Hexalith.Works.Contracts",
            "Hexalith.Works.Server",
            "Hexalith.Works.Projections",
        ];

        string[] forbiddenReferences =
        [
            "Dapr.Actors.AspNetCore",
            "Dapr.Client",
            "ModelContextProtocol",
            "Microsoft.AspNetCore.Components",
            "Microsoft.AspNetCore.OpenApi",
            "Swashbuckle",
            "OpenAI",
            "SemanticKernel",
        ];

        foreach (string project in kernelProjects)
        {
            // When each kernel project file is inspected.
            string path = Path.Combine(root, "src", project, project + ".csproj");
            File.Exists(path).ShouldBeTrue($"Project file not found at '{path}'.");

            string text = File.ReadAllText(path);
            foreach (string forbidden in forbiddenReferences)
            {
                // Then adapter, runtime, UI, MCP, LLM, and OpenAPI dependencies stay outside the kernel.
                text.ShouldNotContain(forbidden, Case.Insensitive, $"{project} must remain kernel code and not reference adapter, Dapr runtime, UI, MCP, LLM, or OpenAPI packages.");
            }
        }
    }

    [Fact]
    public void P0_NestedSubmodulesRemainUninitialized()
    {
        // Given this repository uses root-level Hexalith submodules only.
        string root = RepositoryRoot.Locate();

        // When .git markers are searched below root submodules.
        string[] nestedGitMarkers = Directory.GetFileSystemEntries(root, ".git", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, Path.Combine(root, ".git"), StringComparison.Ordinal))
            .Where(path => path.Split(Path.DirectorySeparatorChar).Count(segment => segment.StartsWith("Hexalith.", StringComparison.Ordinal)) > 1)
            .ToArray();

        // Then nested submodules remain uninitialized.
        nestedGitMarkers.ShouldBeEmpty("Nested submodules inside root submodules must remain uninitialized; never run recursive submodule commands for this repository.");
    }
}
