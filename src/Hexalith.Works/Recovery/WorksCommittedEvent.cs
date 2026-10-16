using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;

namespace Hexalith.Works.Recovery;

/// <summary>A validated committed envelope and its fully decoded Works payload.</summary>
/// <param name="Envelope">The authoritative EventStore envelope, including its committed position.</param>
/// <param name="Payload">The decoded domain fact.</param>
internal sealed record WorksCommittedEvent(StreamReadEvent Envelope, IEventPayload Payload);
