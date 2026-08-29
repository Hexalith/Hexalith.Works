using Hexalith.EventStore.Contracts.Events;

namespace Hexalith.Works.Projections;

/// <summary>Describes one bounded Works projection-event decode attempt.</summary>
/// <param name="Payload">The decoded payload, when available.</param>
/// <param name="KnownEventType">Whether the event name belongs to the Works event catalog.</param>
/// <param name="Malformed">Whether a known Works event could not be decoded.</param>
internal sealed record WorkItemProjectionEventDecodeResult(
    IEventPayload? Payload,
    bool KnownEventType,
    bool Malformed);
