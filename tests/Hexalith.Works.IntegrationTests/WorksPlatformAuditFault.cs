namespace Hexalith.Works.IntegrationTests;

/// <summary>Fixture-owned outage of only the durable audit adapter.</summary>
/// <param name="Unavailable">Whether the next audit read/write must fail.</param>
/// <param name="ObservedOutages">Bounded durable witness that a real audit attempt reached the fault adapter.</param>
internal sealed record WorksPlatformAuditFault(bool Unavailable, int ObservedOutages = 0);
