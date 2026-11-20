using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Retained host metadata selects an explicit compatible fleet before Aspire applies immutable defaults.</summary>
public sealed class WorksPlatformSelectedSdkPathsTests
{
    /// <summary>Current Platform launch uses the serial-prebuilt runtime outputs, not a second artifacts-path.</summary>
    [Fact]
    public void CurrentPlatformLaunchBindsTheQualifiedRuntimeArtifactsPath()
    {
        string directory = Directory.CreateTempSubdirectory("works-runtime-artifacts-").FullName;
        string? previous = Environment.GetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS");
        try
        {
            var options = new DistributedApplicationOptions();
            Environment.SetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS", directory);
            WorksPlatformRuntimeArtifacts.BindLaunchArtifacts(options);
            WorksPlatformRuntimeArtifacts.ReadBoundLaunchArtifacts(options).ShouldBe(directory);
            Environment.SetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS", "runtime");
            Should.Throw<InvalidOperationException>(() => WorksPlatformRuntimeArtifacts.BindLaunchArtifacts(options));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS", previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Current Platform child launches keep the serial-prebuilt outputs closed.</summary>
    [Fact]
    public void CurrentPlatformLaunchDisablesRebuildOfQualifiedRuntimeOutputs()
    {
        string directory = Directory.CreateTempSubdirectory("works-runtime-prebuilt-").FullName;
        string? previous = Environment.GetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS");
        try
        {
            IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder([]);
            string project = Path.Combine(directory, "Probe.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            ProjectResource owned = builder.AddProject("works", project).Resource;
            int before = owned.Annotations.OfType<CommandLineArgsCallbackAnnotation>().Count();
            WorksPlatformRuntimeArtifacts.BindPrebuiltLaunch(builder);
            owned.Annotations.OfType<CommandLineArgsCallbackAnnotation>().Count().ShouldBe(before);
            Environment.SetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS", directory);
            WorksPlatformRuntimeArtifacts.BindPrebuiltLaunch(builder);
            owned.Annotations.OfType<CommandLineArgsCallbackAnnotation>().Count().ShouldBe(before + 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS", previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Both supported configurations bind the running fleet and the restored fleet to one inventory.</summary>
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void MatchingFleetConfigurationsAreQualified(string configuration)
        => WorksPlatformRuntimeArtifacts.RequireMatchingConfigurations(configuration, configuration, configuration)
            .ShouldBe(configuration);

    /// <summary>A different host or rollback configuration cannot qualify hashes from the consumer's output tree.</summary>
    [Theory]
    [InlineData("Debug", "Release", "Debug")]
    [InlineData("Debug", "Debug", "Release")]
    [InlineData("Release", "Debug", "Release")]
    [InlineData(null, "Debug", "Debug")]
    [InlineData("Debug", null, "Debug")]
    [InlineData("Debug", "Debug", null)]
    [InlineData("Custom", "Custom", "Custom")]
    public void MismatchedOrMissingFleetConfigurationsRefuse(string? consumer, string? host, string? retainedHost)
        => Should.Throw<InvalidOperationException>(() =>
            WorksPlatformRuntimeArtifacts.RequireMatchingConfigurations(consumer, host, retainedHost));


    /// <summary>The .NET 11 project target selects its output folder through MSBuild evaluation.</summary>
    [Fact]
    public void SelectedProjectFrameworkDeterminesItsRuntimeOutput()
    {
        const string framework = "net11.0";
        string directory = Directory.CreateTempSubdirectory("works-selected-framework-").FullName;
        try
        {
            string project = Path.Combine(directory, "Host.proj");
            File.WriteAllText(project, $"<Project><PropertyGroup><TargetFramework>{framework}</TargetFramework></PropertyGroup></Project>");
            WorksPlatformRuntimeArtifacts.DefaultDirectoryFor(project, "Release")
                .ShouldBe(Path.Combine(directory, "bin", "Release", framework));
            File.WriteAllText(project, "<Project><PropertyGroup><TargetFramework>net10.0;net11.0</TargetFramework></PropertyGroup></Project>");
            Should.Throw<InvalidOperationException>(() => WorksPlatformRuntimeArtifacts.DefaultDirectoryFor(project, "Release"));
            File.WriteAllText(project, "<Project>");
            Should.Throw<InvalidOperationException>(() => WorksPlatformRuntimeArtifacts.DefaultDirectoryFor(project, "Release"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>An unavailable SDK selected by the project cannot fall back to the caller's SDK.</summary>
    [Fact]
    public void SelectedProjectSdkContextRefusesAnUnavailableSdk()
    {
        string directory = Directory.CreateTempSubdirectory("works-selected-sdk-").FullName;
        try
        {
            string project = Path.Combine(directory, "Host.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>");
            WorksPlatformRuntimeArtifacts.DefaultDirectoryFor(project, "Release")
                .ShouldBe(Path.Combine(directory, "bin", "Release", "net11.0"));
            File.WriteAllText(Path.Combine(directory, "global.json"), "{\"sdk\":{\"version\":\"99.0.100\",\"rollForward\":\"disable\"}}");
            Should.Throw<InvalidOperationException>(() => WorksPlatformRuntimeArtifacts.DefaultDirectoryFor(project, "Release"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RelativeProjectInputUsesItsNormalizedSelectedDirectory()
    {
        string directory = Directory.CreateTempSubdirectory("works-relative-sdk-").FullName;
        try
        {
            string project = Path.Combine(directory, "Host.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>");
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, project);
            WorksPlatformRuntimeArtifacts.DefaultDirectoryFor(relative, "Release").ShouldBe(Path.Combine(directory, "bin", "Release", "net11.0"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ExitedLauncherCannotLeaveInheritedOutputPipesPastTheDeadlineAsync()
    {
        using WorksIsolatedFixtureProcess fixture = await WorksIsolatedFixtureProcess.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        var start = fixture.StartInfo("pipes", Path.Combine(fixture.DirectoryPath, "descendant.pid"));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Should.Throw<InvalidOperationException>(() => WorksPlatformRuntimeArtifacts.InspectFramework(start, TimeSpan.FromSeconds(5)))
            .Message.ShouldContain("deadline");
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8));
        File.Exists(Path.Combine(fixture.DirectoryPath, "descendant.pid")).ShouldBeTrue();
    }

}
