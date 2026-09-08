namespace Hexalith.Works.Runtime;

/// <summary>
/// Host-edge configuration for the Works <c>/project</c> projection adapter. Bound from the
/// <c>Works:Projection</c> configuration section.
/// </summary>
public sealed class WorksProjectionOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Works:Projection";

    /// <summary>The default bounded failure count before a poisoned aggregate is parked.</summary>
    public const int DefaultMaxUndecodableEventDispatchesBeforeParking = 5;

    /// <summary>
    /// How many consecutive dispatches may fail to decode the same state-affecting event before the aggregate is
    /// parked. Decoding stays fail-closed for every attempt up to this bound, so a transient cause still retries;
    /// once reached, the dispatch is acknowledged with a distinct log event and the aggregate is no longer
    /// redispatched.
    /// </summary>
    public int MaxUndecodableEventDispatchesBeforeParking { get; init; } = DefaultMaxUndecodableEventDispatchesBeforeParking;
}
