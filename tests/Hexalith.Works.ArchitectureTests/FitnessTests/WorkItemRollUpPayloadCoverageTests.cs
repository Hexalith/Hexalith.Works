using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Projections.Strategies;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class WorkItemRollUpPayloadCoverageTests
{
    [Fact]
    public void Contracts_event_payloads_are_supported_or_explicitly_rejected()
    {
        Type[] contractPayloadTypes = [.. typeof(WorkItemCreated).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(typeof(IEventPayload).IsAssignableFrom)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
        Type[] expectedSupportedTypes = [.. contractPayloadTypes
            .Where(type => !typeof(IRejectionEvent).IsAssignableFrom(type))];
        Type[] actualSupportedTypes = [.. WorkItemRollUpTenantIsolation.SupportedPayloadTypes
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

        contractPayloadTypes.ShouldNotBeEmpty("The Contracts-derived payload universe must not pass vacuously.");
        actualSupportedTypes.ShouldBe(
            expectedSupportedTypes,
            "Every concrete non-rejection Contracts payload must be accepted by the exact-type runtime identity registry.");

        Type[] intentionallyExcludedTypes = [.. contractPayloadTypes.Except(actualSupportedTypes)];
        intentionallyExcludedTypes.ShouldNotBeEmpty("Works Contracts must expose explicit rejection payloads.");
        intentionallyExcludedTypes.ShouldAllBe(
            type => typeof(IRejectionEvent).IsAssignableFrom(type),
            "Only the explicit IRejectionEvent category may be excluded from roll-up state.");
    }
}
