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
#if !DEBUG
    [Fact]
    public void ReleaseConsumerLoadsTheFrozenPublishedContracts()
    {
        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Hexalith.Works.IntegrationTests.deps.json")));
        JsonElement libraries = dependencies.RootElement.GetProperty("libraries");
        foreach (string dependency in new[] { "Hexalith.EventStore.DomainService/3.120.0",
            "Hexalith.EventStore.Client/3.120.0", "Hexalith.Parties.Client/1.2.1" })
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
