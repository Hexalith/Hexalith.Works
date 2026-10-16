using Projects;

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

    /// <summary>The three SDK entry points resolve into the chosen archive while Works retains its own source.</summary>
    [Theory]
    [InlineData("Hexalith.EventStore")]
    [InlineData("Hexalith.EventStore.Operations")]
    [InlineData("Hexalith.EventStore.Admin.Server.Host")]
    public void ExplicitArchiveRootSelectsTheMatchingSdkEntryPoint(string project)
    {
        string directory = Path.Combine(Path.GetTempPath(), "works-sdk416-paths-" + Guid.NewGuid().ToString("N"));
        string works = Path.Combine(directory, "works");
        string sdk = Path.Combine(directory, "published-sdk");
        string expected = Path.Combine(sdk, "src", project, project + ".csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        Directory.CreateDirectory(works);
        try
        {
            File.WriteAllText(expected, "<Project />");
            ProjectMetadataPaths.ResolveProjectPath(works, sdk, "references", "Hexalith.EventStore", "src", project, project + ".csproj")
                .ShouldBe(expected);
            ProjectMetadataPaths.ResolveProjectPath(works, sdk, "src", "Hexalith.Works", "Hexalith.Works.csproj")
                .ShouldBe(Path.Combine(works, "src", "Hexalith.Works", "Hexalith.Works.csproj"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>An invalid selected root or missing entry refuses instead of silently launching another SDK.</summary>
    [Theory]
    [InlineData("relative")]
    [InlineData("missing-root")]
    [InlineData("missing-project")]
    public void InvalidExplicitSelectionRefusesFallback(string scenario)
    {
        string directory = Path.Combine(Path.GetTempPath(), "works-sdk416-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string sdk = scenario switch
            {
                "relative" => "relative-sdk",
                "missing-root" => Path.Combine(directory, "absent"),
                _ => directory,
            };
            Should.Throw<InvalidOperationException>(() => ProjectMetadataPaths.ResolveProjectPath(directory, sdk,
                "references", "Hexalith.EventStore", "src", "Hexalith.EventStore", "Hexalith.EventStore.csproj"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Without an explicit SDK root the existing declared-submodule resolution is preserved.</summary>
    [Fact]
    public void DefaultDeclaredSdkPathIsPreserved()
    {
        string directory = Path.Combine(Path.GetTempPath(), "works-sdk416-paths-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(directory, "references", "Hexalith.EventStore");
        Directory.CreateDirectory(sdk);
        try
        {
            File.WriteAllText(Path.Combine(sdk, ".git"), "gitdir: synthetic-metadata-only");
            ProjectMetadataPaths.ResolveProjectPath(directory, null, "references", "Hexalith.EventStore", "src", "Hexalith.EventStore", "Hexalith.EventStore.csproj")
                .ShouldBe(Path.Combine(sdk, "src", "Hexalith.EventStore", "Hexalith.EventStore.csproj"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
