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
            .Where(path => !IsBuildOutput(path))
            .ToArray();

        string[] projectNames = [.. projectFiles.Select(path => Path.GetFileNameWithoutExtension(path)!)];

        foreach (string requiredProject in RequiredSourceProjects)
        {
            projectNames.ShouldContain(requiredProject, $"Works requires '{requiredProject}' in the bounded-context project set.");
        }

        string[] forbiddenProjects = [.. projectNames.Where(name => ForbiddenProjectFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))];
        forbiddenProjects.ShouldBeEmpty("Works excludes UI, MCP, portal, security, routing, LLM, cost-governance, and production channel-adapter projects.");
    }

    [Fact]
    public void P0_ScaffoldContainsFocusedTestProjectSet()
    {
        string root = RepositoryRoot.Locate();
        string[] projectNames = [.. Directory.GetFiles(Path.Combine(root, "tests"), "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)];

        foreach (string requiredProject in RequiredTestProjectSet())
        {
            projectNames.ShouldContain(requiredProject, $"Works requires focused test project '{requiredProject}'.");
        }
    }

    [Fact]
    public void P0_ScaffoldUsesSlnxAndCentralPackageManagement()
    {
        string root = RepositoryRoot.Locate();

        File.Exists(Path.Combine(root, "Hexalith.Works.slnx")).ShouldBeTrue("Works requires Hexalith.Works.slnx.");
        File.Exists(Path.Combine(root, "Hexalith.Works.sln")).ShouldBeFalse("Use .slnx, not .sln.");
        File.Exists(Path.Combine(root, "Directory.Packages.props")).ShouldBeTrue("Central package management must define package versions outside project files.");

        string[] projectFiles = Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
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
    public void P0_SlnxRegistersWorksProjectsAsBuildableProjects()
    {
        string root = RepositoryRoot.Locate();
        XDocument slnx = XDocument.Load(Path.Combine(root, "Hexalith.Works.slnx"));

        string[] csprojRegisteredAsFile = [.. slnx.Descendants()
            .Where(element => element.Name.LocalName == "File")
            .Select(element => element.Attribute("Path")?.Value)
            .OfType<string>()
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))];

        csprojRegisteredAsFile.ShouldBeEmpty("Every Works .csproj must be registered as <Project> (not a passive <File>) so 'dotnet build Hexalith.Works.slnx' actually compiles it.");
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
    public void P0_WorkItemSliceDoesNotIntroduceDeferredRuntimeBehavior()
    {
        string root = RepositoryRoot.Locate();
        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))];
        string[] deferredDomainTerms =
        [
            "BurnDown",
            "Burndown",
            "RollUp",
            "Suspend",
            "Resume",
            "Reminder",
            "Claim",
            "Queue",
        ];

        string[] filesWithDeferredBehavior = [.. sourceFiles
            .Where(path => !Path.GetFileName(path).EndsWith("Assembly.cs", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(Path.Combine("Hexalith.Works.ServiceDefaults", "Extensions.cs"), StringComparison.Ordinal))
            .Where(path => !path.EndsWith(Path.Combine("Hexalith.Works.AppHost", "Program.cs"), StringComparison.Ordinal))
            .Where(path => deferredDomainTerms.Any(term => File.ReadAllText(path).Contains(term, StringComparison.OrdinalIgnoreCase)))];

        filesWithDeferredBehavior.ShouldBeEmpty("Story 1.2 permits create/replay only and must not introduce lifecycle, burn-down, roll-up, suspend/resume, queueing, claim, reminder, or reactor runtime behavior.");
    }

    [Fact]
    public void P0_WorkItemKernelRemainsPure()
    {
        string root = RepositoryRoot.Locate();
        string serverRoot = Path.Combine(root, "src", "Hexalith.Works.Server");
        string[] bannedSymbols =
        [
            "DateTime.Now",
            "DateTime.UtcNow",
            "DateTimeOffset.Now",
            "DateTimeOffset.UtcNow",
            "Stopwatch",
            "Guid.NewGuid",
            "UniqueIdHelper.Generate",
            "File.",
            "Directory.",
            "HttpClient",
            "Dapr",
        ];

        string[] violations = [.. Directory.GetFiles(serverRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => bannedSymbols
                .Where(symbol => File.ReadAllText(path).Contains(symbol, StringComparison.Ordinal))
                .Select(symbol => $"{Path.GetRelativePath(root, path)} contains {symbol}"))];

        violations.ShouldBeEmpty("Work item command handling and replay must remain deterministic: no clocks, generated IDs, Dapr, EventStore envelope APIs, or I/O in the server kernel.");
    }

    [Fact]
    public void P0_WorkItemServerDependsOnlyOnContracts()
    {
        string root = RepositoryRoot.Locate();
        string projectFile = Path.Combine(root, "src", "Hexalith.Works.Server", "Hexalith.Works.Server.csproj");
        XDocument project = XDocument.Load(projectFile);

        string[] projectReferences = [.. project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(ProjectNameFromReference)];

        projectReferences.ShouldBe(
            ["Hexalith.Works.Contracts"],
            ignoreOrder: true,
            customMessage: "Story 1.2 keeps EventStore.Client out of the Works server kernel until a later command-pipeline/Aspire story owns the Dapr dependency decision.");
    }

    private static IEnumerable<string> RequiredTestProjectSet() => RequiredTestProjects;

    private static string ProjectNameFromReference(string include)
    {
        string normalized = include.Replace('\\', '/');
        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];

        return Path.GetFileNameWithoutExtension(fileName)!;
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
