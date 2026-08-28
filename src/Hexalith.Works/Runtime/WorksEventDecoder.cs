using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;

namespace Hexalith.Works.Runtime;

/// <summary>
/// Decodes a concrete Works event keyed by its short type name. Its case-insensitive Web reader accepts both
/// EventPersister's options-free PascalCase bytes and camelCase compatibility inputs; neither form carries a
/// polymorphic <c>$type</c> discriminator. Shared by the recovery runtime so a re-readable stream of
/// <c>work</c> events can be turned back into the pure <see cref="IEventPayload"/> instances the
/// reminder/cascade projections consume.
/// </summary>
internal static class WorksEventDecoder
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, Type> s_eventTypesByName = typeof(WorkItemCreated).Assembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false } && typeof(IEventPayload).IsAssignableFrom(type))
        .ToDictionary(type => type.Name, StringComparer.Ordinal);

    /// <summary>Decodes one event, or returns <see langword="null"/> for an unknown type or malformed payload.</summary>
    public static IEventPayload? Decode(string eventTypeName, byte[] payload)
    {
        if (string.IsNullOrEmpty(eventTypeName) || payload is null)
        {
            return null;
        }

        if (!s_eventTypesByName.TryGetValue(SimpleTypeName(eventTypeName), out Type? eventType))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(payload, eventType, s_web) as IEventPayload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Returns whether the event type name identifies a known Works event.</summary>
    public static bool IsKnownEventType(string eventTypeName)
        => !string.IsNullOrEmpty(eventTypeName) && s_eventTypesByName.ContainsKey(SimpleTypeName(eventTypeName));

    private static string SimpleTypeName(string eventTypeName)
    {
        int lastDot = eventTypeName.LastIndexOf('.');
        return lastDot >= 0 ? eventTypeName[(lastDot + 1)..] : eventTypeName;
    }
}
