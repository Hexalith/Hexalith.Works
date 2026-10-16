using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Works.Projections.SharedRebuild;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Consumer checks for the selected SDK release, source mode and published Parties seams.</summary>
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
    public void ReleaseConsumerLoadsPublishedSdkAndPartiesContracts()
    {
        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Hexalith.Works.IntegrationTests.deps.json")));
        JsonElement libraries = dependencies.RootElement.GetProperty("libraries");
        JsonProperty[] sdk = [.. libraries.EnumerateObject().Where(library => library.Name.StartsWith("Hexalith.EventStore.", StringComparison.Ordinal))];
        sdk.Length.ShouldBeGreaterThanOrEqualTo(9);
        sdk.ShouldContain(library => library.Name.StartsWith("Hexalith.EventStore.Contracts/", StringComparison.Ordinal));
        foreach (JsonProperty library in sdk)
        {
            library.Name.ShouldEndWith("/3.124.0");
            library.Value.GetProperty("type").GetString().ShouldBe("package");
        }

        libraries.TryGetProperty("Hexalith.Parties.Client/1.4.0", out JsonElement parties).ShouldBeTrue();
        parties.GetProperty("type").GetString().ShouldBe("package");

        JsonProperty platform = libraries.EnumerateObject().Where(
            library => library.Name.StartsWith("Hexalith.Platform.Identity/", StringComparison.Ordinal)).ShouldHaveSingleItem();
        platform.Value.GetProperty("type").GetString().ShouldBe("project");
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
