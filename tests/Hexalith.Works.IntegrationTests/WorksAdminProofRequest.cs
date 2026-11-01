using Hexalith.EventStore.Client.Processes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>A closed Admin-owned fixture request; it cannot choose a URL, token, operation or generation.</summary>
internal sealed record WorksAdminProofRequest(string Nonce, string Session, string Kind, ProcessSource Source, DateTimeOffset ExpiresAt);
