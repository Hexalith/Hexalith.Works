using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Projections.Strategies;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class ProjectionPayloadCoverageTests
{
    [Fact]
    public void Projection_catalogs_cover_every_non_rejection_contract_payload_exactly_once()
    {
        Type[] contractPayloadTypes = [.. typeof(WorkItemCreated).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(typeof(IEventPayload).IsAssignableFrom)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

        contractPayloadTypes.ShouldNotBeEmpty("The Contracts-derived payload universe must not pass vacuously.");

        VerifyCatalog(
            "roll-up",
            contractPayloadTypes,
            [.. WorkItemRollUpPayloadDescriptor.Catalog.Select(
                descriptor => (descriptor.PayloadType, descriptor.EffectDisposition))],
            [typeof(WorkItemRescheduled)]);
        VerifyCatalog(
            "what's-next",
            contractPayloadTypes,
            [.. WhatsNextPayloadDescriptor.Catalog.Select(
                descriptor => (descriptor.PayloadType, descriptor.EffectDisposition))],
            [typeof(ChildSpawned)]);
    }

    private static void VerifyCatalog(
        string projectionName,
        IReadOnlyCollection<Type> contractPayloadTypes,
        IReadOnlyCollection<(Type PayloadType, ProjectionPayloadEffectDisposition EffectDisposition)> catalog,
        IReadOnlyCollection<Type> expectedIntentionalNoOps)
    {
        Type[] expectedPayloadTypes = [.. contractPayloadTypes
            .Where(type => !typeof(IRejectionEvent).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
        Type[] actualPayloadTypes = [.. catalog
            .Select(entry => entry.PayloadType)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

        actualPayloadTypes.Distinct().Count().ShouldBe(
            actualPayloadTypes.Length,
            $"Every exact payload type must appear only once in the {projectionName} catalog.");
        actualPayloadTypes.ShouldBe(
            expectedPayloadTypes,
            $"Every concrete non-rejection Contracts payload must have one {projectionName} descriptor.");
        catalog.ShouldAllBe(
            entry => entry.EffectDisposition != ProjectionPayloadEffectDisposition.Unspecified,
            $"Every {projectionName} descriptor must declare its projection effect.");

        Type[] intentionallyExcludedTypes = [.. contractPayloadTypes.Except(actualPayloadTypes)];
        intentionallyExcludedTypes.ShouldNotBeEmpty("Works Contracts must expose explicit rejection payloads.");
        intentionallyExcludedTypes.ShouldAllBe(
            type => typeof(IRejectionEvent).IsAssignableFrom(type),
            $"Only IRejectionEvent payloads may be excluded from the {projectionName} catalog.");

        Type[] actualIntentionalNoOps = [.. catalog
            .Where(entry => entry.EffectDisposition == ProjectionPayloadEffectDisposition.IntentionalNoOp)
            .Select(entry => entry.PayloadType)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
        Type[] sortedExpectedIntentionalNoOps = [.. expectedIntentionalNoOps
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
        actualIntentionalNoOps.ShouldBe(
            sortedExpectedIntentionalNoOps,
            $"The {projectionName} intentional no-op set is part of the projection contract.");
    }
}
