using System.Reflection;
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
    /// <summary>Release consumes the selected public SDK and Parties packages.</summary>
    [Fact]
    public void ReleaseConsumerLoadsSelectedSdkPackagesAndPartiesContracts()
    {
        AssemblyMetadataAttribute[] metadata = [.. typeof(WorksSdkPackageContractTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()];
        string selectedVersion = metadata.Single(attribute => attribute.Key == "WorksSelectedEventStoreVersion").Value!;
        string pinnedVersion = metadata.Single(attribute => attribute.Key == "WorksPinnedEventStoreVersion").Value!;
        string selectedPartiesVersion = metadata.Single(attribute => attribute.Key == "WorksSelectedPartiesVersion").Value!;
        bool allowLocalCandidate = string.Equals(metadata.Single(attribute => attribute.Key == "WorksAllowLocalEventStoreCandidate").Value,
            "true", StringComparison.OrdinalIgnoreCase);
        selectedVersion.ShouldNotBeNullOrWhiteSpace();
        pinnedVersion.ShouldNotBeNullOrWhiteSpace();
        if (allowLocalCandidate) selectedVersion.ShouldContain("-story415.");
        else selectedVersion.ShouldBe(pinnedVersion, "Release uses the static public pin unless WorksAllowLocalEventStoreCandidate=true explicitly selects a local proof.");
        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Hexalith.Works.IntegrationTests.deps.json")));
        JsonElement libraries = dependencies.RootElement.GetProperty("libraries");
        JsonProperty[] sdk = [.. libraries.EnumerateObject().Where(library => library.Name.StartsWith("Hexalith.EventStore.", StringComparison.Ordinal))];
        sdk.Length.ShouldBeGreaterThanOrEqualTo(9);
        sdk.ShouldContain(library => library.Name.StartsWith("Hexalith.EventStore.Contracts/", StringComparison.Ordinal));
        foreach (JsonProperty library in sdk)
        {
            library.Name.ShouldEndWith("/" + selectedVersion);
            library.Value.GetProperty("type").GetString().ShouldBe("package");
        }

        selectedPartiesVersion.ShouldNotBeNullOrWhiteSpace();
        foreach (string package in new[] { "Hexalith.Parties.Client", "Hexalith.Parties.Contracts" })
        {
            libraries.TryGetProperty(package + "/" + selectedPartiesVersion, out JsonElement parties).ShouldBeTrue();
            parties.GetProperty("type").GetString().ShouldBe("package");
        }

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
