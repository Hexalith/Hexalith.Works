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

    /// <summary>Renamed or newly nested hosting projects are still rejected by the boundary policy.</summary>
    [Theory]
    [InlineData("tests/Hexalith.Works.AppHost")]
    [InlineData("samples/Hexalith.Works.Aspire")]
    [InlineData("Hexalith.Works.AppHost")]
    [InlineData("Hexalith.Works.Aspire")]
    [InlineData("Hexalith.Works.ServiceDefaults")]
    [InlineData("src/nested/Reintroduced.AppHost")]
    public void ReintroducedHostingProjectsAreDetected(string project)
    {
        string root = Directory.CreateTempSubdirectory("works-hosting-boundary-").FullName;
        try
        {
            string directory = Path.Combine(root, project);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, Path.GetFileName(project) + ".csproj"), "<Project />");
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
            "BackgroundService", "IHostedService", "AddHostedService(", "PeriodicTimer", "System.Threading.Timer", "MapWorksDomainEvents(", "WorksDomainEventProcessor", "WorkItemProjectionDispatcher"];
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
        => [.. OwnedFiles(root, "*.csproj")
            .Where(path => Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"(^|\.)(AppHost|Aspire|ServiceDefaults)($|\.)", RegexOptions.IgnoreCase))];

    private static string[] FileHosts(string root)
        => [.. OwnedFiles(root, "*.cs")
            .Where(path => string.Equals(Path.GetFileName(path), "apphost.cs", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(File.ReadAllText(path), @"^\s*#:sdk\s+Aspire\.AppHost\.Sdk(?:@\S+)?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))];

    /// <summary>Dependency checkouts and generated outputs are outside the Works source boundary.</summary>
    [Theory]
    [InlineData("references")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".artifacts")]
    public void DependencyAndBuildOutputHostsAreExcluded(string directory)
    {
        string root = Directory.CreateTempSubdirectory("works-hosting-exclusion-").FullName;
        try
        {
            string excluded = Directory.CreateDirectory(Path.Combine(root, directory)).FullName;
            File.WriteAllText(Path.Combine(excluded, "apphost.cs"), "#:sdk Aspire.AppHost.Sdk");
            File.WriteAllText(Path.Combine(excluded, "Example.AppHost.csproj"), "<Project />");
            FileHosts(root).ShouldBeEmpty();
            HostingProjects(root).ShouldBeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static IEnumerable<string> OwnedFiles(string root, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(root, pattern)) yield return file;
        foreach (string directory in Directory.EnumerateDirectories(root).Where(path =>
            Path.GetFileName(path) is not ("references" or "bin" or "obj" or ".artifacts" or "artifacts" or ".git")
            && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))
        {
            foreach (string file in OwnedFiles(directory, pattern)) yield return file;
        }
    }
}
