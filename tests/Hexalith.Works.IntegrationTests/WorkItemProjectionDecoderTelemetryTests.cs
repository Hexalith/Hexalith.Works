using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;

using Microsoft.Extensions.Logging;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Qualifies the bounded skip diagnostics shared by projection decoding paths.</summary>
public sealed class WorkItemProjectionDecoderTelemetryTests
{
    /// <summary>Unknown, empty, null and malformed evidence retain classification and bounded structured fields.</summary>
    /// <param name="failure">The decoder refusal branch to execute.</param>
    [Theory]
    [InlineData("unknown")]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("malformed")]
    [InlineData("identity")]
    public void SkippedEvidenceHasBoundedSingleLineDiagnostics(string failure)
    {
        const string secret = "private-payload-marker";
        string type = failure switch
        {
            "unknown" => "External.Namespace.Unknown\r\n\u2028\u2029\0" + new string('E', 512),
            "empty" => string.Empty,
            _ => typeof(WorkItemCreated).FullName!,
        };
        byte[] payload = Encoding.UTF8.GetBytes(failure == "null" ? "null" : "{" + secret);
        if (failure == "identity")
            payload = JsonSerializer.SerializeToUtf8Bytes(new WorkItemCreated("item", 1,
                new TenantId("foreign"), new WorkItemId("item"), new Obligation(secret)));
        var dto = new ProjectionEventDto(type, payload, "json", 1, DateTimeOffset.UnixEpoch, "unused");
        var logger = new Story48RecordingLogger<WorkItemProjectionDecoderTelemetryTests>();

        WorkItemProjectionEventDecodeResult result = WorkItemProjectionEventDecoder.Decode(dto,
            new TenantId("tenant"), new WorkItemId("item"),
            "correlation\r\n\u2028\u2029\0" + new string('C', 512), logger);

        result.Payload.ShouldBeNull();
        result.KnownEventType.ShouldBe(failure is "null" or "malformed" or "identity");
        result.Malformed.ShouldBe(failure is "null" or "malformed" or "identity");
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(failure == "identity" ? 4505 : 4504);
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Properties.Keys.Order(StringComparer.Ordinal).ShouldBe(
            new[] { "CorrelationId", "EventType", "TenantId", "WorkItemId", "{OriginalFormat}" });
        entry.Properties["EventType"].ShouldBe(failure switch
        {
            "unknown" => "Unknown?????" + new string('E', 116),
            "empty" => string.Empty,
            _ => nameof(WorkItemCreated),
        });
        entry.Properties["CorrelationId"].ShouldBe("correlation?????" + new string('C', 112));
        entry.Properties["TenantId"].ShouldBe("tenant");
        entry.Properties["WorkItemId"].ShouldBe("item");
        entry.Message.ShouldNotContain(secret);
        entry.Message.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029').ShouldBeFalse();
    }
}
