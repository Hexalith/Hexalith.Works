using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Extensions;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// AC #5: proves the v1 event and command catalog is registered with
/// <see cref="Hexalith.PolymorphicSerializations"/> and that the library can <em>resolve</em> every
/// payload type. Each registered type is serialized through the empty <see cref="Polymorphic"/> base
/// (the resolution surface), asserted to carry the <c>$type</c> discriminator equal to its v1 type
/// name (no version suffix — NFR-12), and round-tripped back to the original concrete record. This is
/// the polymorphic <em>capability</em>; concrete-type (EventStore transport) serialization is proven
/// unchanged separately in <see cref="WorkItemRawActAdditivityTests"/>.
/// </summary>
public sealed class WorkItemSerializationRegistrationTests
{
    static WorkItemSerializationRegistrationTests()
        // Idempotent; safe to call from every test class that exercises the catalog.
        => HexalithWorksContractsSerialization.RegisterPolymorphicMappers();

    [Fact]
    public void RegisterPolymorphicMappers_populates_the_default_resolver_with_the_whole_v1_catalog()
    {
        // Vacuous-pass guard #1: the sample catalog under test is itself non-empty and the expected size.
        WorkItemV1Catalog.All.Count.ShouldBe(WorkItemV1Catalog.Count);

        // Vacuous-pass guard #2: registration actually populated the static resolver registry. A no-op
        // RegisterPolymorphicMappers would leave zero derived types on the Polymorphic base, and the
        // per-type loop below would then fail-closed — but we assert the registry directly so the
        // emptiness is reported as the root cause, not as a downstream serialization failure.
        var probeOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonTypeInfo baseTypeInfo = new PolymorphicSerializationResolver().GetTypeInfo(typeof(Polymorphic), probeOptions);

        baseTypeInfo.PolymorphismOptions.ShouldNotBeNull();
        baseTypeInfo.PolymorphismOptions!.DerivedTypes.Count.ShouldBeGreaterThanOrEqualTo(WorkItemV1Catalog.Count);
    }

    [Fact]
    public void Every_registered_event_and_command_resolves_through_the_polymorphic_base()
    {
        WorkItemV1Catalog.All.Count.ShouldBe(WorkItemV1Catalog.Count); // Guard: never iterate an empty catalog.

        foreach (Polymorphic payload in WorkItemV1Catalog.All)
        {
            Type concreteType = payload.GetType();

            // Serialize THROUGH the base: this is the path that exercises PolymorphicSerializations'
            // resolver and emits the discriminator.
            string json = JsonSerializer.Serialize<Polymorphic>(payload, PolymorphicHelper.DefaultJsonSerializerOptions);

            using JsonDocument document = JsonDocument.Parse(json);
            document.RootElement.TryGetProperty(PolymorphicHelper.Discriminator, out JsonElement typeElement)
                .ShouldBeTrue($"{concreteType.Name} must serialize a \"{PolymorphicHelper.Discriminator}\" discriminator through the Polymorphic base.");

            // v1 discriminator == type name (NFR-12: no "Vn" suffix below version 2).
            typeElement.GetString().ShouldBe(concreteType.Name);

            // Deserialize THROUGH the base resolves the discriminator back to the concrete type.
            Polymorphic roundTripped = JsonSerializer.Deserialize<Polymorphic>(json, PolymorphicHelper.DefaultJsonSerializerOptions)
                .ShouldNotBeNull();

            roundTripped.GetType().ShouldBe(concreteType);
            roundTripped.ShouldBe(payload); // record value-equality over every reported field.
        }
    }
}
