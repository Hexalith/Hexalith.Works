using System.Text.RegularExpressions;
using System.Xml.Linq;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>Prevents Works-owned hosting and generic runtime plumbing from returning after Platform cutover.</summary>
public sealed class PlatformHostingBoundaryTests
{
    private static readonly string[] _plumbingTokens = ["IRemindable", "RegisterReminderAsync", "UnregisterReminderAsync", "AddDaprClient(",
        "AddActors(", "MapActorsHandlers(", "AddServiceDefaults(", "MapDefaultEndpoints(",
        "BackgroundService", "IHostedService", "IHostedLifecycleService", "AddHostedService", "PeriodicTimer", "System.Threading.Timer",
        "System.Timers", "new Timer(", "MapWorksDomainEvents(", "WorksDomainEventProcessor", "WorkItemProjectionDispatcher"];

    /// <summary>Works owns only its domain-service executable and pure domain libraries.</summary>
    [Fact]
    public void WorksDoesNotOwnTopologyOrServiceDefaults()
    {
        string root = RepositoryRoot.Locate();
        HostingProjects(root).ShouldBeEmpty("Boundary violation: AppHost, Aspire and ServiceDefaults belong to Platform.");
        FileHosts(root).ShouldBeEmpty("Boundary violation: file-based Aspire AppHosts belong to Platform.");
        File.Exists(Path.Combine(root, "aspire.config.json")).ShouldBeFalse("Platform owns Aspire configuration.");
        string solution = File.ReadAllText(Path.Combine(root, "Hexalith.Works.slnx"));
        solution.ShouldNotContain(".AppHost");
        solution.ShouldNotContain(".ServiceDefaults");
        XDocument integration = XDocument.Load(Path.Combine(root, "tests", "Hexalith.Works.IntegrationTests", "Hexalith.Works.IntegrationTests.csproj"));
        integration.Descendants("ProjectReference").Select(item => item.Attribute("Include")?.Value ?? string.Empty)
            .ShouldNotContain(path => Regex.IsMatch(path, @"Hexalith\.Works\.(AppHost|Aspire|ServiceDefaults)", RegexOptions.IgnoreCase));
    }

    /// <summary>Root file hosts cannot bypass the source-project classifier, even under another filename.</summary>
    [Theory]
    [InlineData("eng/apphost.cs", "// nested host")]
    [InlineData("src/main.cs", "#:sdk Aspire.AppHost.Sdk@13.6.1\n")]
    [InlineData("apphost.cs", "var builder = DistributedApplication.CreateBuilder(args);")]
    [InlineData("AppHost.cs", "// root hosting entry point")]
    [InlineData("main.cs", "#:sdk Aspire.AppHost.Sdk\nvar builder = DistributedApplication.CreateBuilder(args);")]
    [InlineData("main.cs", "#!/usr/bin/env dotnet\n#:sdk Aspire.AppHost.Sdk@13.6.1\n")]
    public void ReintroducedFileHostsAreDetected(string filename, string contents)
    {
        string root = Directory.CreateTempSubdirectory("works-file-host-boundary-").FullName;
        try
        {
            string path = Path.Combine(root, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            FileHosts(root).ShouldHaveSingleItem();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Renamed or newly nested hosting projects are still rejected by the boundary policy, by name or by declared hosting SDK/package.</summary>
    [Theory]
    [InlineData("tests/Hexalith.Works.AppHost", "<Project />")]
    [InlineData("samples/Hexalith.Works.Aspire", "<Project />")]
    [InlineData("Hexalith.Works.AppHost", "<Project />")]
    [InlineData("Hexalith.Works.Aspire", "<Project />")]
    [InlineData("Hexalith.Works.ServiceDefaults", "<Project />")]
    [InlineData("src/nested/Reintroduced.AppHost", "<Project />")]
    [InlineData("tests/LocalTopology", "<Project Sdk=\"Aspire.AppHost.Sdk/13.6.1\" />")]
    [InlineData("samples/Orchestration", "<Project Sdk=\"Microsoft.NET.Sdk\"><Sdk Name=\"Aspire.AppHost.Sdk\" Version=\"13.6.1\" /></Project>")]
    [InlineData("eng/Topology", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Aspire.Hosting.AppHost\" /></ItemGroup></Project>")]
    [InlineData("src/x/artifacts/LocalTopology", "<Project Sdk=\"Aspire.AppHost.Sdk/13.6.1\" />")]
    [InlineData("tests/references/Example.AppHost", "<Project />")]
    public void ReintroducedHostingProjectsAreDetected(string project, string contents)
    {
        string root = Directory.CreateTempSubdirectory("works-hosting-boundary-").FullName;
        try
        {
            string directory = Path.Combine(root, project);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, Path.GetFileName(project) + ".csproj"), contents);
            HostingProjects(root).ShouldHaveSingleItem();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>An ordinary project outside the source tree is not mistaken for a hosting project.</summary>
    [Fact]
    public void OrdinaryProjectsOutsideSourceAreNotHostingProjects()
    {
        string root = Directory.CreateTempSubdirectory("works-hosting-control-").FullName;
        try
        {
            string directory = Directory.CreateDirectory(Path.Combine(root, "tests", "LocalTopology")).FullName;
            File.WriteAllText(Path.Combine(directory, "LocalTopology.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Aspire.Hosting.Testing\" /></ItemGroup></Project>");
            HostingProjects(root).ShouldBeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Generic actors, periodic recovery and subscription routes are SDK/Platform responsibilities.</summary>
    [Fact]
    public void WorksDoesNotImplementGenericRuntimeMechanics()
    {
        string root = RepositoryRoot.Locate();
        Directory.Exists(Path.Combine(root, "samples")).ShouldBeTrue("The plumbing scan must cover the samples included in the solution.");
        PlumbingViolations(root).ShouldBeEmpty("Boundary violation: generic hosting, delivery, reminder and recovery plumbing belongs to EventStore or Platform.");
        string host = File.ReadAllText(Path.Combine(root, "src", "Hexalith.Works", "Runtime", "WorksHost.cs"));
        host.ShouldContain("AddEventStoreDomainService(");
        host.ShouldContain("UseEventStoreDomainService()");
        host.ShouldNotContain("MapPost");
        host.ShouldNotContain("MapActors");
    }

    /// <summary>The idiomatic forms of hosted-service and timer plumbing are detected in source and samples.</summary>
    /// <param name="relativePath">The owned source file that reintroduces the plumbing.</param>
    /// <param name="contents">The reintroduced source text.</param>
    [Theory]
    [InlineData("src/Hexalith.Works/Runtime/Worker.cs", "builder.Services.AddHostedService<Worker>();")]
    [InlineData("samples/Sample/Program.cs", "services.AddHostedService(provider => new Worker());")]
    [InlineData("src/Hexalith.Works/Runtime/Poller.cs", "private readonly Timer _timer = new Timer(_ => Poll(), null, 0, 1000);")]
    [InlineData("samples/Sample/Poller.cs", "private readonly System.Timers.Timer _timer = new(1000);")]
    [InlineData("src/Hexalith.Works/Runtime/Lifecycle.cs", "internal sealed class Lifecycle : IHostedLifecycleService { }")]
    [InlineData("samples/Sample/Background.cs", "internal sealed class Background : BackgroundService { }")]
    public void ReintroducedGenericPlumbingIsDetected(string relativePath, string contents)
    {
        string root = Directory.CreateTempSubdirectory("works-plumbing-boundary-").FullName;
        try
        {
            string path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            PlumbingViolations(root).ShouldNotBeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Plumbing tokens in build outputs or outside source and samples are not Works-owned source.</summary>
    [Fact]
    public void PlumbingScanIgnoresBuildOutputsAndNonSourceTrees()
    {
        string root = Directory.CreateTempSubdirectory("works-plumbing-control-").FullName;
        try
        {
            foreach (string relativePath in new[] { "src/Hexalith.Works/obj/Generated.cs", "samples/Sample/bin/Generated.cs", "tests/Fixture.cs" })
            {
                string path = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "services.AddHostedService<Worker>();");
            }

            string source = Path.Combine(root, "src", "Hexalith.Works", "Clean.cs");
            File.WriteAllText(source, "internal sealed class Clean { }");
            PlumbingViolations(root).ShouldBeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string[] PlumbingViolations(string root)
        => [.. new[] { "src", "samples" }
            .Select(tree => Path.Combine(root, tree))
            .Where(Directory.Exists)
            .SelectMany(tree => Directory.EnumerateFiles(tree, "*.cs", SearchOption.AllDirectories))
            .Where(path => !KernelDependencyPolicy.IsBuildOutput(path))
            .SelectMany(path => _plumbingTokens.Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => Path.GetRelativePath(root, path) + ": " + token))];

    private static string[] HostingProjects(string root)
        => [.. OwnedFiles(root, "*.csproj")
            .Where(path => Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"(^|\.)(AppHost|Aspire|ServiceDefaults)($|\.)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(File.ReadAllText(path), @"Aspire\.AppHost\.Sdk|Aspire\.Hosting\.AppHost", RegexOptions.IgnoreCase))];

    private static string[] FileHosts(string root)
        => [.. OwnedFiles(root, "*.cs")
            .Where(path => string.Equals(Path.GetFileName(path), "apphost.cs", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(File.ReadAllText(path), @"^\s*#:sdk\s+Aspire\.AppHost\.Sdk(?:@\S+)?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))];

    /// <summary>Root dependency checkouts and generated outputs at any depth are outside the Works source boundary.</summary>
    /// <param name="directory">The excluded directory, relative to the repository root.</param>
    [Theory]
    [InlineData("references")]
    [InlineData("artifacts")]
    [InlineData(".artifacts")]
    [InlineData(".git")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("src/Hexalith.Works/bin")]
    [InlineData("tests/Hexalith.Works.IntegrationTests/obj")]
    public void DependencyAndBuildOutputHostsAreExcluded(string directory)
    {
        string root = Directory.CreateTempSubdirectory("works-hosting-exclusion-").FullName;
        try
        {
            string excluded = Directory.CreateDirectory(Path.Combine(root, directory)).FullName;
            File.WriteAllText(Path.Combine(excluded, "apphost.cs"), "#:sdk Aspire.AppHost.Sdk");
            File.WriteAllText(Path.Combine(excluded, "Example.AppHost.csproj"), "<Project Sdk=\"Aspire.AppHost.Sdk/13.6.1\" />");
            FileHosts(root).ShouldBeEmpty();
            HostingProjects(root).ShouldBeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Dependency and artifact folder names are excluded only at the repository root, never below an owned tree.</summary>
    /// <param name="directory">The nested owned directory that reuses an excluded root name.</param>
    [Theory]
    [InlineData("src/x/artifacts")]
    [InlineData("src/x/.artifacts")]
    [InlineData("tests/references")]
    [InlineData("samples/Sample/.git")]
    public void NestedExcludedNamesRemainOwned(string directory)
    {
        string root = Directory.CreateTempSubdirectory("works-hosting-nested-").FullName;
        try
        {
            string nested = Directory.CreateDirectory(Path.Combine(root, directory)).FullName;
            File.WriteAllText(Path.Combine(nested, "apphost.cs"), "#:sdk Aspire.AppHost.Sdk");
            File.WriteAllText(Path.Combine(nested, "Topology.csproj"), "<Project Sdk=\"Aspire.AppHost.Sdk/13.6.1\" />");
            FileHosts(root).ShouldHaveSingleItem();
            HostingProjects(root).ShouldHaveSingleItem();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static IEnumerable<string> OwnedFiles(string root, string pattern)
        => OwnedFiles(root, pattern, isRepositoryRoot: true);

    private static IEnumerable<string> OwnedFiles(string directory, string pattern, bool isRepositoryRoot)
    {
        foreach (string file in Directory.EnumerateFiles(directory, pattern)) yield return file;
        foreach (string child in Directory.EnumerateDirectories(directory).Where(path =>
            Path.GetFileName(path) is not ("bin" or "obj")
            && !(isRepositoryRoot && Path.GetFileName(path) is "references" or "artifacts" or ".artifacts" or ".git")
            && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))
        {
            foreach (string file in OwnedFiles(child, pattern, isRepositoryRoot: false)) yield return file;
        }
    }
}
