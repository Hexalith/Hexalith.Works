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


}
