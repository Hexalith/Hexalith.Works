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
/// persists/replays the <em>concrete</em> CLR type with plain <see cref="System.Text.Json"/> (keyed by
/// <see cref="Type.FullName"/>); because the generated <see cref="Polymorphic"/> base is an empty
/// <c>[DataContract] record</c>, deriving from it must add nothing to concrete-type serialization. These
/// tests prove the persisted shape is unchanged: no <c>$type</c> discriminator and no EventStore envelope
/// fields leak into the concrete form, and a concrete event still round-trips and replays. If any of
/// these fail, the additivity assumption is wrong (STOP and escalate — see the story Critical Decision).
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
            // Mirror EventStore's persist call: serialize the CONCRETE runtime type (payload.GetType()),
            // not the Polymorphic base. The empty base contributes no members and no discriminator.
            string concreteJson = JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);

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
            string concreteJson = JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);

            // Assert top-level PROPERTY absence rather than substring containment: an envelope field
            // would be a top-level sibling of the payload, and a substring test would false-positive on
            // legitimate fields (e.g. "correlationId" is a substring of "conversationCorrelationId").
            using JsonDocument document = JsonDocument.Parse(concreteJson);
            JsonElement root = document.RootElement;

            foreach (string envelopeField in WorkItemV1Catalog.EnvelopeFields)
            {
                root.TryGetProperty(envelopeField, out _)
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

        // Concrete-type write → replay loop (EventStore transport), unaffected by the Polymorphic base.
        string json = JsonSerializer.Serialize(created, created.GetType(), JsonOptions);
        json.ShouldNotContain(PolymorphicHelper.Discriminator, Case.Sensitive);

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions).ShouldNotBeNull();
        roundTripped.ShouldBe(created);

        var state = new WorkItemState();
        state.Apply(roundTripped);

        state.Status.ShouldBe(WorkItemStatus.Created);
        state.AggregateIdentity.ShouldNotBeNull().ToString().ShouldBe("tenant-alpha:work:work-001");
    }
}
