namespace Hexalith.Works.Projections;

/// <summary>Identifies every shard and records its canonical contents within one runtime index.</summary>
/// <param name="RootKey">The logical index root owns the shard inventory.</param>
/// <param name="Version">The version of the shard layout and key scheme.</param>
/// <param name="MemberCount">The expected number of retained source watermarks across shards.</param>
/// <param name="PageCounts">The shard counts in deterministic bucket order.</param>
/// <param name="Digest">The SHA-256 digest of the ordered shard digests.</param>
internal sealed record WorksRuntimeIndexManifest(string RootKey, int Version, int MemberCount, int[] PageCounts, string Digest);
