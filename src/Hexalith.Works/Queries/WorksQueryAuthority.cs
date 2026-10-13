namespace Hexalith.Works.Queries;

/// <summary>Verified scope for one Works SDK query.</summary>
/// <param name="Allowed">Whether the request has current authority.</param>
/// <param name="AllWork">Whether the trusted workload may read all tenant work.</param>
/// <param name="PartyId">The currently bound Executor Party, when applicable.</param>
internal sealed record WorksQueryAuthority(bool Allowed, bool AllWork, string? PartyId);
