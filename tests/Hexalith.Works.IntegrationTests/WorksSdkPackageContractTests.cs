using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Works.Projections.SharedRebuild;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Compile-time consumer check for the published SDK and Parties seams.</summary>
public sealed class WorksSdkPackageContractTests
{
    [Fact]
    public void Works_epoch_handler_implements_the_published_named_delivery_contract()
    {
        typeof(IAsyncDomainSharedProjectionEpochHandler).IsAssignableFrom(
            typeof(WorkItemSharedProjectionRebuildHandler)).ShouldBeTrue();
        typeof(SharedProjectionEpochCoordinator).Assembly.GetName().Name.ShouldBe("Hexalith.EventStore.Client");
        typeof(IPartiesIdentityClient).Assembly.GetName().Name.ShouldBe("Hexalith.Parties.Client");
    }
}
