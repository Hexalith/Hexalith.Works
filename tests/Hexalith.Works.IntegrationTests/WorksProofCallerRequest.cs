using System.Text.Json;

namespace Hexalith.Works.IntegrationTests;

internal sealed record WorksProofCallerRequest(string Nonce, string Session, string Command, JsonElement Payload, DateTimeOffset ExpiresAt);
