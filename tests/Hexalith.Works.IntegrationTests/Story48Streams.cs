using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Builds per-aggregate <see cref="StreamReadPage"/> fakes for Story 4.8 deterministic source/handler tests.</summary>
internal static class Story48Streams
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    /// <summary>A single, non-truncated per-aggregate page carrying the given events in order.</summary>
    public static StreamReadPage Page(string tenant, string aggregateId, params IEventPayload[] events)
        => PageAt(tenant, aggregateId, 1, isTruncated: false, events);

    /// <summary>A per-aggregate page with explicit sequence and truncation metadata.</summary>
    public static StreamReadPage PageAt(
        string tenant,
        string aggregateId,
        long firstSequence,
        bool isTruncated,
        params IEventPayload[] events)
    {
        StreamReadEvent[] streamEvents =
        [
            .. events.Select((value, index) => new StreamReadEvent(
                firstSequence + index,
                value.GetType().FullName!,
                JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), s_web),
                "json",
                1,
                $"message-{aggregateId}-{index}",
                $"correlation-{aggregateId}-{index}",
                null,
                new DateTimeOffset(2026, 7, 22, 8, 0, index, TimeSpan.Zero),
                null)),
        ];

        return new StreamReadPage(
            tenant,
            WorkCommandSubmission.WorkDomain,
            aggregateId,
            streamEvents,
            new StreamReadMetadata(
                firstSequence - 1,
                null,
                streamEvents.Length == 0 ? null : firstSequence + streamEvents.Length - 1,
                streamEvents.Length == 0 ? firstSequence - 1 : firstSequence + streamEvents.Length - 1,
                streamEvents.Length,
                IsTruncated: isTruncated,
                NextContinuationToken: null));
    }
}
