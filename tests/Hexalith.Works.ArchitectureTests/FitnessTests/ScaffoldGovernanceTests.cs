using System.Xml.Linq;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class ScaffoldGovernanceTests
{
    private static readonly string[] RequiredSourceProjects =
    [
        "Hexalith.Works.Contracts",
        "Hexalith.Works.Server",
        "Hexalith.Works.Projections",
        "Hexalith.Works.Reactor",
        "Hexalith.Works.ServiceDefaults",
        "Hexalith.Works.AppHost",
    ];

    private static readonly string[] RequiredTestProjects =
    [
        "Hexalith.Works.Testing",
        "Hexalith.Works.UnitTests",
        "Hexalith.Works.PropertyTests",
        "Hexalith.Works.ArchitectureTests",
        "Hexalith.Works.IntegrationTests",
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
        string root = RepositoryRoot.Locate();
        string[] projectFiles = Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        string[] projectNames = [.. projectFiles.Select(path => Path.GetFileNameWithoutExtension(path)!)];

        foreach (string requiredProject in RequiredSourceProjects)
        {
            projectNames.ShouldContain(requiredProject, $"Story 1.1 requires '{requiredProject}' in the Works scaffold.");
        }

        string[] forbiddenProjects = [.. projectNames.Where(name => ForbiddenProjectFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))];
        forbiddenProjects.ShouldBeEmpty("Story 1.1 excludes UI, MCP, portal, security, routing, LLM, cost-governance, and production channel-adapter projects.");
    }

    [Fact]
    public void P0_ScaffoldContainsFocusedTestProjectSet()
    {
        string root = RepositoryRoot.Locate();
        string[] projectNames = [.. Directory.GetFiles(Path.Combine(root, "tests"), "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path)!)];

        foreach (string requiredProject in RequiredTestProjectSet())
        {
            projectNames.ShouldContain(requiredProject, $"Story 1.1 requires focused test project '{requiredProject}'.");
        }
    }

    [Fact]
    public void P0_ScaffoldUsesSlnxAndCentralPackageManagement()
    {
        string root = RepositoryRoot.Locate();

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

            inlineVersions.ShouldBeEmpty($"Package versions must be managed centrally, not inline in '{projectFile}'.");
        }
    }

    [Fact]
    public void P0_KernelProjectsStayInfrastructureFree()
    {
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
            string path = Path.Combine(root, "src", project, project + ".csproj");
            File.Exists(path).ShouldBeTrue($"Project file not found at '{path}'.");

            string text = File.ReadAllText(path);
            foreach (string forbidden in forbiddenReferences)
            {
                text.ShouldNotContain(forbidden, Case.Insensitive, $"{project} must remain kernel code and not reference adapter, Dapr runtime, UI, MCP, LLM, or OpenAPI packages.");
            }
        }
    }

    [Fact]
    public void P0_NestedSubmodulesRemainUninitialized()
    {
        string root = RepositoryRoot.Locate();
        string[] nestedGitMarkers = Directory.GetFileSystemEntries(root, ".git", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, Path.Combine(root, ".git"), StringComparison.Ordinal))
            .Where(path => path.Split(Path.DirectorySeparatorChar).Count(segment => segment.StartsWith("Hexalith.", StringComparison.Ordinal)) > 1)
            .ToArray();

        nestedGitMarkers.ShouldBeEmpty("Nested submodules inside root submodules must remain uninitialized; never run recursive submodule commands for this repository.");
    }

    [Fact]
    public void P0_StoryElevenRemainsScaffoldOnly()
    {
        string root = RepositoryRoot.Locate();
        string[] sourceFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
        string[] deferredDomainTerms =
        [
            "WorkItem",
            "BurnDown",
            "Burndown",
            "RollUp",
            "Suspend",
            "Resume",
            "ExecutorBinding",
            "Reminder",
        ];

        string[] filesWithDeferredBehavior = [.. sourceFiles
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(Path.Combine("Hexalith.Works.ServiceDefaults", "Extensions.cs"), StringComparison.Ordinal))
            .Where(path => !path.EndsWith(Path.Combine("Hexalith.Works.AppHost", "Program.cs"), StringComparison.Ordinal))
            .Where(path => deferredDomainTerms.Any(term => File.ReadAllText(path).Contains(term, StringComparison.OrdinalIgnoreCase)))];

        filesWithDeferredBehavior.ShouldBeEmpty("Story 1.1 is scaffold-only and must not introduce Work Item lifecycle, burn-down, roll-up, suspend/resume, executor-binding, or reactor runtime behavior.");
    }

    private static IEnumerable<string> RequiredTestProjectSet() => RequiredTestProjects;
}
