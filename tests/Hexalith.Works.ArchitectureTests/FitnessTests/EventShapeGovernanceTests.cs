using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Wire-shape governance for the durable v1 catalog. Every non-rejection domain event must be sealed,
/// carry the <c>AggregateId</c> + <c>Sequence</c> stream-correlation properties, and follow the
/// past-tense naming rule (no "Event" suffix); every command must be sealed and never carry a
/// "Command" suffix. Discovery reflects over the Contracts assembly and keeps only Polymorphic catalog
/// members in the exact target namespace, so the generated <c>*Mapper</c> records (which live in the
/// same namespaces) and the <c>.Rejections</c> sub-namespace are excluded. Each scan pins the expected
/// catalog size so a discovery regression cannot pass vacuously.
/// </summary>
public sealed class EventShapeGovernanceTests
{
    private const int NonRejectionEventCatalogSize = 14;
    private const int CommandCatalogSize = 14;

    [Fact]
    public void P0_NonRejectionDomainEventsAreSealedCarryAggregateIdAndSequenceAndAvoidEventSuffix()
    {
        Type[] eventTypes = CatalogTypesIn(typeof(WorkItemCreated).Namespace!);

        eventTypes.Length.ShouldBe(
            NonRejectionEventCatalogSize,
            "The non-rejection domain event catalog is frozen at 14; a different count is either a discovery regression or an undeclared catalog change.");

        string[] violations = [.. eventTypes.SelectMany(EventShapeViolations)];

        violations.ShouldBeEmpty("Every non-rejection domain event must be sealed, expose public AggregateId and Sequence properties for stream correlation, and use a past-tense name without an 'Event' suffix.");
    }

    [Fact]
    public void P0_CommandsAreSealedAndAvoidCommandSuffix()
    {
        Type[] commandTypes = CatalogTypesIn(typeof(AssignWorkItem).Namespace!);

        commandTypes.Length.ShouldBe(
            CommandCatalogSize,
            "The command catalog is frozen at 14; a different count is either a discovery regression or an undeclared catalog change.");

        string[] violations = [.. commandTypes.SelectMany(CommandShapeViolations)];

        violations.ShouldBeEmpty("Every command must be sealed and use an imperative name without a 'Command' suffix.");
    }

    private static Type[] CatalogTypesIn(string catalogNamespace)
        => [.. typeof(AssignWorkItem).Assembly.GetTypes()
            .Where(type => string.Equals(type.Namespace, catalogNamespace, StringComparison.Ordinal))
            .Where(type => !type.IsAbstract && type != typeof(Polymorphic) && typeof(Polymorphic).IsAssignableFrom(type))];

    private static IEnumerable<string> EventShapeViolations(Type eventType)
    {
        if (!eventType.IsSealed)
        {
            yield return $"{eventType.Name} is not sealed";
        }

        if (eventType.GetProperty("AggregateId") is null)
        {
            yield return $"{eventType.Name} has no public AggregateId property";
        }

        if (eventType.GetProperty("Sequence") is null)
        {
            yield return $"{eventType.Name} has no public Sequence property";
        }

        if (eventType.Name.EndsWith("Event", StringComparison.Ordinal))
        {
            yield return $"{eventType.Name} must not use the 'Event' suffix";
        }
    }

    private static IEnumerable<string> CommandShapeViolations(Type commandType)
    {
        if (!commandType.IsSealed)
        {
            yield return $"{commandType.Name} is not sealed";
        }

        if (commandType.Name.EndsWith("Command", StringComparison.Ordinal))
        {
            yield return $"{commandType.Name} must not use the 'Command' suffix";
        }
    }
}
