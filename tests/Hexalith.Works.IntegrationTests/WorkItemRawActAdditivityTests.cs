using System.Text.Json;

using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Extensions;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// AC #1/#2/#3 regression guard: registering the catalog with
/// <see cref="Hexalith.PolymorphicSerializations"/> must be purely <em>additive</em>. EventStore
/// persists event <em>concrete</em> CLR types with options-free <see cref="System.Text.Json"/> (keyed by
/// <see cref="Type.FullName"/>), while Works command builders use the same case-preserving concrete writer.
/// Because the generated <see cref="Polymorphic"/> base is an empty <c>[DataContract] record</c>, deriving from
/// it must add nothing to either concrete shape. These tests prove no <c>$type</c> discriminator or EventStore
/// envelope field leaks into the concrete form, and a persisted event still round-trips through the shared
/// case-insensitive reader and replays. If any of these fail, the additivity assumption is wrong (STOP and
/// escalate — see the story Critical Decision).
/// </summary>
public sealed class WorkItemRawActAdditivityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    static WorkItemRawActAdditivityTests()
        // The discriminator must be ABSENT from concrete serialization even when the polymorphic
        // registry is populated; register first so this test proves additivity, not non-registration.
        => HexalithWorksContractsSerialization.RegisterPolymorphicMappers();

    [Fact]
    public void Concrete_type_serialization_emits_no_polymorphic_discriminator()
    {
        WorkItemV1Catalog.All.Count.ShouldBe(WorkItemV1Catalog.Count); // Guard: never iterate an empty catalog.

        foreach (Polymorphic payload in WorkItemV1Catalog.All)
        {
            // Serialize the CONCRETE runtime type (payload.GetType()), not the Polymorphic base. This mirrors
            // EventPersister for events and the Works options-free command builders for commands.
            string concreteJson = JsonSerializer.Serialize(payload, payload.GetType());

            concreteJson.ShouldNotContain(
                PolymorphicHelper.Discriminator,
                Case.Sensitive,
                $"{payload.GetType().Name} concrete serialization must not emit a polymorphic discriminator.");
        }
    }

    [Fact]
    public void Concrete_type_serialization_carries_no_eventstore_envelope_fields()
    {
        WorkItemV1Catalog.All.Count.ShouldBe(WorkItemV1Catalog.Count); // Guard: never iterate an empty catalog.

        foreach (Polymorphic payload in WorkItemV1Catalog.All)
        {
            string concreteJson = JsonSerializer.Serialize(payload, payload.GetType());

            // Assert top-level PROPERTY absence rather than substring containment: an envelope field
            // would be a top-level sibling of the payload, and a substring test would false-positive on
            // legitimate fields (e.g. "correlationId" is a substring of "conversationCorrelationId").
            using JsonDocument document = JsonDocument.Parse(concreteJson);
            JsonElement root = document.RootElement;

            foreach (string envelopeField in WorkItemV1Catalog.EnvelopeFields)
            {
                root.EnumerateObject().Any(property => string.Equals(property.Name, envelopeField, StringComparison.OrdinalIgnoreCase))
                    .ShouldBeFalse($"{payload.GetType().Name} must return payload only; EventStore owns envelope metadata ({envelopeField}).");
            }
        }
    }

    [Fact]
    public void WorkItemCreated_concrete_round_trip_still_replays_to_created_state()
    {
        var created = new WorkItemCreated(
            "work-001",
            1,
            new TenantId("tenant-alpha"),
            new WorkItemId("work-001"),
            new Obligation("Prepare the first tenant-scoped work item"));

        // Options-free concrete write → case-insensitive reader → replay, unaffected by the Polymorphic base.
        string json = JsonSerializer.Serialize(created, created.GetType());
        json.ShouldNotContain(PolymorphicHelper.Discriminator, Case.Sensitive);

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions).ShouldNotBeNull();
        roundTripped.ShouldBe(created);

        var state = new WorkItemState();
        state.Apply(roundTripped);

        state.Status.ShouldBe(WorkItemStatus.Created);
        state.AggregateIdentity.ShouldNotBeNull().ToString().ShouldBe("tenant-alpha:work:work-001");
    }
}
