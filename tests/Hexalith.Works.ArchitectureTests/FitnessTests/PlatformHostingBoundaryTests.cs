using System.Text.RegularExpressions;
using System.Xml.Linq;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>Prevents Works-owned hosting and generic runtime plumbing from returning after Platform cutover.</summary>
public sealed class PlatformHostingBoundaryTests
{
    /// <summary>Works owns only its domain-service executable and pure domain libraries.</summary>
    [Fact]
    public void WorksDoesNotOwnTopologyOrServiceDefaults()
    {
        string root = RepositoryRoot.Locate();
        HostingProjects(root).ShouldBeEmpty("Boundary violation: AppHost, Aspire and ServiceDefaults belong to Platform.");
        RootFileHosts(root).ShouldBeEmpty("Boundary violation: file-based Aspire AppHosts belong to Platform.");
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
    [InlineData("apphost.cs", "var builder = DistributedApplication.CreateBuilder(args);")]
    [InlineData("AppHost.cs", "// root hosting entry point")]
    [InlineData("main.cs", "#:sdk Aspire.AppHost.Sdk\nvar builder = DistributedApplication.CreateBuilder(args);")]
    [InlineData("main.cs", "#!/usr/bin/env dotnet\n#:sdk Aspire.AppHost.Sdk@13.6.1\n")]
    public void ReintroducedRootFileHostsAreDetected(string filename, string contents)
    {
        string root = Directory.CreateTempSubdirectory("works-file-host-boundary-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, filename), contents);
            RootFileHosts(root).ShouldHaveSingleItem();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Renamed or newly nested hosting projects are still rejected by the boundary policy.</summary>
    [Theory]
    [InlineData("Hexalith.Works.AppHost")]
    [InlineData("Hexalith.Works.Aspire")]
    [InlineData("Hexalith.Works.ServiceDefaults")]
    [InlineData("Reintroduced.AppHost")]
    public void ReintroducedHostingProjectsAreDetected(string project)
    {
        string root = Directory.CreateTempSubdirectory("works-hosting-boundary-").FullName;
        try
        {
            string directory = Path.Combine(root, "src", "nested", project);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, project + ".csproj"), "<Project />");
            HostingProjects(root).ShouldHaveSingleItem();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Generic actors, periodic recovery and subscription routes are SDK/Platform responsibilities.</summary>
    [Fact]
    public void WorksDoesNotImplementGenericRuntimeMechanics()
    {
        string root = RepositoryRoot.Locate();
        string[] forbidden = ["IRemindable", "RegisterReminderAsync", "UnregisterReminderAsync", "AddDaprClient(",
            "AddActors(", "MapActorsHandlers(", "AddServiceDefaults(", "MapDefaultEndpoints(",
            "BackgroundService", "PeriodicTimer", "MapWorksDomainEvents(", "WorksDomainEventProcessor", "WorkItemProjectionDispatcher"];
        string[] violations = [.. Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !KernelDependencyPolicy.IsBuildOutput(path))
            .SelectMany(path => forbidden.Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => Path.GetRelativePath(root, path) + ": " + token))];
        violations.ShouldBeEmpty("Boundary violation: generic hosting, delivery, reminder and recovery plumbing belongs to EventStore or Platform.");
        string host = File.ReadAllText(Path.Combine(root, "src", "Hexalith.Works", "Runtime", "WorksHost.cs"));
        host.ShouldContain("AddEventStoreDomainService(");
        host.ShouldContain("UseEventStoreDomainService()");
        host.ShouldNotContain("MapPost");
        host.ShouldNotContain("MapActors");
    }

    private static string[] HostingProjects(string root)
        => [.. Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !KernelDependencyPolicy.IsBuildOutput(path))
            .Where(path => Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"(^|\.)(AppHost|Aspire|ServiceDefaults)($|\.)", RegexOptions.IgnoreCase))];

    private static string[] RootFileHosts(string root)
        => [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetFileName(path), "apphost.cs", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(File.ReadAllText(path), @"^\s*#:sdk\s+Aspire\.AppHost\.Sdk(?:@\S+)?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))];
}
