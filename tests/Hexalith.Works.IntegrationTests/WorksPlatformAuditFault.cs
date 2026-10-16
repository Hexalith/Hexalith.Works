namespace Hexalith.Works.IntegrationTests;

/// <summary>Fixture-owned outage of only the durable audit adapter.</summary>
/// <param name="Unavailable">Whether the next audit read/write must fail.</param>
internal sealed record WorksPlatformAuditFault(bool Unavailable);
