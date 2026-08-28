namespace Hexalith.Works.Projections;

/// <summary>
/// The single well-known durable document listing every tenant that has (or has had) pending <c>DateReached</c>
/// awaits. The registry is append-only, so recovery can discover every tenant without configuration.
/// </summary>
/// <remarks>Plain host-edge <c>System.Text.Json</c> read model; not a durable polymorphic catalog type.</remarks>
public sealed class PendingDateAwaitTenantRegistry
{
    /// <summary>The tenant ids known to have (or have had) pending date awaits.</summary>
    public HashSet<string> Tenants { get; init; } = new(StringComparer.Ordinal);
}
