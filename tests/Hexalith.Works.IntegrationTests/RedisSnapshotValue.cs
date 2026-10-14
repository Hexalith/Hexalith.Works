namespace Hexalith.Works.IntegrationTests;

/// <summary>The serialized contents of one isolated Redis snapshot key.</summary>
/// <param name="Dump">The Redis serialization used to restore the key.</param>
/// <param name="Expiry">The remaining key lifetime, when one is set.</param>
/// <param name="Canonical">The stable representation used to verify restored contents.</param>
internal sealed record RedisSnapshotValue(byte[] Dump, TimeSpan? Expiry, string Canonical);
