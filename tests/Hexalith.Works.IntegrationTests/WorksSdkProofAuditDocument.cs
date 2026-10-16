using Hexalith.EventStore.Server.Commands;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Payload-free synthetic audit attempts persisted through the SDK store.</summary>
/// <param name="Attempts">The immutable appended attempt sequence.</param>
internal sealed record WorksSdkProofAuditDocument(IReadOnlyList<TrustedEffectAuditRecord> Attempts);
