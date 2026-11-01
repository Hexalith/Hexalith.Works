using System.Text.Json.Serialization;

namespace Hexalith.Works.Routing;

/// <summary>Existing inner query payload; caller identity and delegation remain authenticated gateway context.</summary>
/// <param name="PartyId">The existing optional Executor Party selector; configured coordinators keep their nondelegated domain policy.</param>
public sealed record WorksExecutorQueryParameters([property: JsonPropertyName("partyId")] string? PartyId = null);
