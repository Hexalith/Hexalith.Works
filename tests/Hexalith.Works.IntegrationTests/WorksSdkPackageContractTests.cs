using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Works.Projections.SharedRebuild;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Compile-time consumer check for the published SDK and Parties seams.</summary>
public sealed class WorksSdkPackageContractTests
{
#if DEBUG
    /// <summary>Debug resolves every SDK library through its source project, including Contracts.</summary>
    [Fact]
    public void DebugConsumerLoadsSdkContractsAndRuntimeFromSource()
    {
        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Hexalith.Works.IntegrationTests.deps.json")));
        JsonElement libraries = dependencies.RootElement.GetProperty("libraries");
        JsonProperty[] sdk = [.. libraries.EnumerateObject().Where(library => library.Name.StartsWith("Hexalith.EventStore.", StringComparison.Ordinal))];
        sdk.Length.ShouldBeGreaterThanOrEqualTo(9);
        sdk.ShouldContain(library => library.Name.StartsWith("Hexalith.EventStore.Contracts/", StringComparison.Ordinal));
        foreach (JsonProperty library in sdk) library.Value.GetProperty("type").GetString().ShouldBe("project");
    }
#else
    [Fact]
    public void ReleaseConsumerLoadsPinnedCandidateAndPublishedPartiesContracts()
    {
        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Hexalith.Works.IntegrationTests.deps.json")));
        JsonElement libraries = dependencies.RootElement.GetProperty("libraries");
        foreach (string dependency in new[] { "Hexalith.EventStore.DomainService/3.122.0-story415.20261016.9",
            "Hexalith.EventStore.Client/3.122.0-story415.20261016.9", "Hexalith.Parties.Client/1.2.1",
            "Hexalith.Platform.Identity/1.0.0-story414.20261014.5" })
        {
            libraries.TryGetProperty(dependency, out JsonElement package).ShouldBeTrue();
            package.GetProperty("type").GetString().ShouldBe("package");
        }
    }
#endif

    [Fact]
    public void Works_epoch_handler_implements_the_published_named_delivery_contract()
    {
        typeof(IAsyncDomainSharedProjectionEpochHandler).IsAssignableFrom(
            typeof(WorkItemSharedProjectionRebuildHandler)).ShouldBeTrue();
        typeof(SharedProjectionEpochCoordinator).Assembly.GetName().Name.ShouldBe("Hexalith.EventStore.Client");
        typeof(IPartiesIdentityClient).Assembly.GetName().Name.ShouldBe("Hexalith.Parties.Client");
    }
}
