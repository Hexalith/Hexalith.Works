namespace Hexalith.Works.IntegrationTests;

/// <summary>Exact persisted fixture effect selected for a Works-owned replay.</summary>
/// <param name="Tenant">The configured synthetic tenant.</param>
/// <param name="Source">The inventoried source aggregate.</param>
/// <param name="Target">The inventoried target aggregate.</param>
/// <param name="EffectId">The original immutable receipt identity.</param>
internal sealed record WorksTrustedEffectProofTarget(string Tenant, string Source, string Target, string EffectId);
