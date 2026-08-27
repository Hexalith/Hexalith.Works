using Hexalith.EventStore.Contracts.Events;

namespace Hexalith.Works.UnitTests;

/// <summary>
/// Represents an event payload unknown to the roll-up projection's delivery allowlist.
/// </summary>
internal sealed record UnknownRollUpEventPayload : IEventPayload;
