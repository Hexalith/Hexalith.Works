using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.IntegrationTests.SchemaEvolution;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Projection admission preserves historical encodings while refusing synthesized scalar evidence.</summary>
public sealed class WorkItemProjectionEventAdmissionTests
{
    /// <summary>Every catalog payload supports both retained casings and exact short/full discriminator forms.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HistoricalCatalogPayloadsRemainReadable(bool web, bool fullName)
    {
        foreach (IEventPayload payload in WorkItemV1Catalog.All.OfType<IEventPayload>())
        {
            var json = JsonSerializer.SerializeToNode(payload, payload.GetType(), Options(web))!.AsObject();
            json["futureOptionalMember"] = "additive";
            var dto = new ProjectionEventDto(fullName ? payload.GetType().FullName! : payload.GetType().Name,
                JsonSerializer.SerializeToUtf8Bytes(json), "json", 1, DateTimeOffset.UnixEpoch, "correlation");
            var decoded = Decode(dto);
            decoded.KnownEventType.ShouldBeTrue();
            decoded.Malformed.ShouldBeFalse();
            decoded.Payload.ShouldBe(payload);
        }
    }

    /// <summary>The changed decoder admits the exact checked-in compatibility and persisted-writer bytes.</summary>
    [Theory]
    [InlineData("Golden")]
    [InlineData("EventPersisterGolden")]
    public void FrozenHistoricalCorpusRemainsReadable(string corpus)
    {
        foreach (IEventPayload sample in WorkItemV1Catalog.All.OfType<IEventPayload>())
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(GoldenCorpusFiles.SourceDirectory(corpus), sample.GetType().Name + ".v1.json"));
            var dto = new ProjectionEventDto(sample.GetType().FullName!, bytes, "json", 1, DateTimeOffset.UnixEpoch, "correlation");
            var decoded = Decode(dto);
            decoded.KnownEventType.ShouldBeTrue();
            decoded.Malformed.ShouldBeFalse();
            decoded.Payload.ShouldBe(JsonSerializer.Deserialize(bytes, sample.GetType(), Options(true)));
        }
    }

    /// <summary>Explicit nonpositive ordinals are corrupt under both supported JSON casings.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public void NonpositiveSequencesAreMalformed(long sequence, bool web)
    {
        foreach (IEventPayload payload in WorkItemV1Catalog.All.OfType<IEventPayload>()
            .Where(payload => payload.GetType().GetProperty("Sequence") is not null))
        {
            var json = JsonSerializer.SerializeToNode(payload, payload.GetType(), Options(web))!.AsObject();
            json[web ? "sequence" : "Sequence"] = sequence;
            var dto = new ProjectionEventDto(payload.GetType().FullName!, JsonSerializer.SerializeToUtf8Bytes(json),
                "json", 1, DateTimeOffset.UnixEpoch, "correlation");
            var decoded = Decode(dto);
            decoded.KnownEventType.ShouldBeTrue();
            decoded.Malformed.ShouldBeTrue();
            decoded.Payload.ShouldBeNull();
        }
    }

    /// <summary>Omitted required values cannot silently become zero, false, or the first enum member.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingScalarMembersAreMalformed(bool web)
    {
        foreach (IEventPayload payload in WorkItemV1Catalog.All.OfType<IEventPayload>())
        {
            string[] members = payload switch
            {
                ProgressReported => ["Sequence", "DoneDelta"],
                ReEstimated => ["Sequence", "Estimated"],
                WorkItemRejected => ["Sequence", "Requeue"],
                WorkItemTreeDepthExceeded => ["MaxDepth", "ResultingDepth"],
                WorkItemTransitionRejected => ["FromStatus"],
                WorkItemInitialEffortRejected => ["Done"],
                _ => payload.GetType().GetProperty("Sequence") is null ? [] : ["Sequence"],
            };
            foreach (string member in members)
            {
                var json = JsonSerializer.SerializeToNode(payload, payload.GetType(), Options(web))!.AsObject();
                json.Remove(web ? JsonNamingPolicy.CamelCase.ConvertName(member) : member).ShouldBeTrue();
                var dto = new ProjectionEventDto(payload.GetType().FullName!, JsonSerializer.SerializeToUtf8Bytes(json),
                    "json", 1, DateTimeOffset.UnixEpoch, "correlation");
                var decoded = Decode(dto);
                decoded.KnownEventType.ShouldBeTrue();
                decoded.Malformed.ShouldBeTrue();
                decoded.Payload.ShouldBeNull();
            }
        }
    }

    /// <summary>Namespace spoofing cannot turn a foreign event into a Works state change.</summary>
    [Fact]
    public void ForeignNamespaceAliasesAreUnknown()
    {
        foreach (IEventPayload payload in WorkItemV1Catalog.All.OfType<IEventPayload>())
        {
            var dto = new ProjectionEventDto("Foreign.Namespace." + payload.GetType().Name,
                JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()), "json", 1, DateTimeOffset.UnixEpoch, "correlation");
            var decoded = Decode(dto);
            decoded.KnownEventType.ShouldBeFalse();
            decoded.Payload.ShouldBeNull();
        }
    }

    /// <summary>Explicit zero and false values remain distinct from missing persisted members.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitZeroAndFalseRemainReadable(bool web)
    {
        var tenant = WorkItemV1Catalog.Tenant;
        var item = WorkItemV1Catalog.Item;
        IEventPayload[] payloads =
        [
            new ProgressReported(item.Value, 2, tenant, item, 0m, new Unit("hour")),
            new ReEstimated(item.Value, 2, tenant, item, 0m, new Unit("hour")),
            new WorkItemRejected(item.Value, 2, tenant, item, false),
        ];
        foreach (IEventPayload payload in payloads)
        {
            var dto = new ProjectionEventDto(payload.GetType().FullName!,
                JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), Options(web)), "json", 2, DateTimeOffset.UnixEpoch, "correlation");
            var decoded = Decode(dto);
            decoded.Malformed.ShouldBeFalse();
            decoded.Payload.ShouldBe(payload);
        }
    }

    private static JsonSerializerOptions Options(bool web)
        => web ? new(JsonSerializerDefaults.Web) : new();

    private static WorkItemProjectionEventDecodeResult Decode(ProjectionEventDto dto)
        => WorkItemProjectionEventDecoder.Decode(dto, new TenantId("tenant-alpha"), new WorkItemId("work-001"), "correlation", null);
}
