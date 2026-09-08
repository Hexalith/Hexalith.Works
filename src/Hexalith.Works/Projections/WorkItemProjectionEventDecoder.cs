using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Projections;

/// <summary>Decodes Works projection events and enforces their requested stream identity.</summary>
internal static partial class WorkItemProjectionEventDecoder
{
    private static readonly JsonSerializerOptions s_webOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, Type> s_eventTypesByName = typeof(WorkItemCreated).Assembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false } && typeof(IEventPayload).IsAssignableFrom(type))
        .ToDictionary(type => type.Name, StringComparer.Ordinal);

    /// <summary>Decodes an event and refuses any payload outside the supplied tenant/aggregate identity.</summary>
    public static WorkItemProjectionEventDecodeResult Decode(
        ProjectionEventDto dto,
        TenantId tenantId,
        WorkItemId workItemId,
        string correlationId,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(workItemId);

        string simpleName = SimpleTypeName(dto.EventTypeName);
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            // A nameless event cannot be resolved against the catalog: fail closed as an unknown type
            // instead of faulting the dispatch inside the type lookup.
            LogSkipped(logger, simpleName ?? string.Empty, workItemId.Value, tenantId.Value, correlationId);
            return new WorkItemProjectionEventDecodeResult(null, false, false);
        }

        if (!s_eventTypesByName.TryGetValue(simpleName, out Type? eventType))
        {
            LogSkipped(logger, dto.EventTypeName, workItemId.Value, tenantId.Value, correlationId);
            return new WorkItemProjectionEventDecodeResult(null, false, false);
        }

        try
        {
            IEventPayload? payload = JsonSerializer.Deserialize(dto.Payload, eventType, s_webOptions) as IEventPayload;
            if (payload is null)
            {
                LogSkipped(logger, dto.EventTypeName, workItemId.Value, tenantId.Value, correlationId);
                return new WorkItemProjectionEventDecodeResult(null, true, true);
            }

            if (payload is ConversationLinked { ConversationCorrelationId: null })
            {
                LogSkipped(logger, dto.EventTypeName, workItemId.Value, tenantId.Value, correlationId);
                return new WorkItemProjectionEventDecodeResult(null, true, true);
            }

            if (!WorksEventIdentity.Matches(payload, tenantId.Value, workItemId.Value))
            {
                // A foreign identity is malformed evidence, not an unknown type: return Malformed so
                // /project can park after the bounded budget instead of throwing before ParkOrRetryAsync
                // and 500ing the poller forever.
                return new WorkItemProjectionEventDecodeResult(null, true, true);
            }

            return new WorkItemProjectionEventDecodeResult(payload, true, false);
        }
        catch (Exception exception) when (IsHandledDecodeFailure(exception))
        {
            LogSkipped(logger, dto.EventTypeName, workItemId.Value, tenantId.Value, correlationId);
            return new WorkItemProjectionEventDecodeResult(null, true, true);
        }
    }

    /// <summary>
    /// Returns whether a serializer failure is malformed evidence rather than an unclassified escape.
    /// Mirrors <see cref="WorksEventDecoder"/> so <see cref="NotSupportedException"/> parks instead of
    /// 500ing <c>/project</c> forever.
    /// </summary>
    /// <param name="exception">The exception thrown while decoding a known Works event.</param>
    internal static bool IsHandledDecodeFailure(Exception exception)
        => exception is JsonException or ArgumentException or NotSupportedException;

    /// <summary>Returns the first non-blank correlation id in a projection history.</summary>
    public static string CorrelationIdOf(IReadOnlyList<ProjectionEventDto>? events)
        => events?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e?.CorrelationId))?.CorrelationId ?? string.Empty;

    /// <summary>Returns the unqualified type name from a projection event name.</summary>
    public static string SimpleTypeName(string eventTypeName)
    {
        if (string.IsNullOrEmpty(eventTypeName))
        {
            return eventTypeName;
        }

        int lastDot = eventTypeName.LastIndexOf('.');
        return lastDot >= 0 ? eventTypeName[(lastDot + 1)..] : eventTypeName;
    }

    private static void LogSkipped(
        ILogger? logger,
        string eventType,
        string workItemId,
        string tenantId,
        string correlationId)
    {
        if (logger is not null)
        {
            SkippedEvent(logger, eventType, workItemId, tenantId, correlationId);
        }
    }

    [LoggerMessage(
        EventId = 4504,
        Level = LogLevel.Warning,
        Message = "Skipped undecodable projection event {EventType} for work item {WorkItemId} (tenant {TenantId}, correlation {CorrelationId}).")]
    private static partial void SkippedEvent(
        ILogger logger,
        string eventType,
        string workItemId,
        string tenantId,
        string correlationId);
}
