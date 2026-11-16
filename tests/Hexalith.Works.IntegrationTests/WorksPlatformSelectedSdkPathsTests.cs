using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Retained host metadata selects an explicit compatible fleet before Aspire applies immutable defaults.</summary>
public sealed class WorksPlatformSelectedSdkPathsTests
{
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


    /// <summary>Current and frozen project targets select their own output folders through actual MSBuild evaluation.</summary>
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net11.0")]
    public void SelectedProjectFrameworkDeterminesItsRuntimeOutput(string framework)
    {
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
}
