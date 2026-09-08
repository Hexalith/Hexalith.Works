namespace Hexalith.Works.Projections;

/// <summary>
/// The persisted bounded-failure record for one work aggregate whose <c>/project</c> dispatch cannot decode a
/// state-affecting event. Decoding stays fail-closed, but after
/// <see cref="Runtime.WorksProjectionOptions.MaxUndecodableEventDispatchesBeforeParking"/> consecutive failures
/// on the <em>same</em> sequence the aggregate is parked: its dispatch is acknowledged rather than 500'd, so the
/// EventStore projection poller stops redispatching it forever and the blast radius is one visible aggregate
/// instead of a permanent poller loop.
/// </summary>
/// <remarks>Plain host-edge <c>System.Text.Json</c> read model; not a durable polymorphic catalog type.</remarks>
public sealed class WorkItemProjectionParking
{
    /// <summary>The source sequence of the event that could not be decoded.</summary>
    public long FailedSequence { get; init; }

    /// <summary>The number of consecutive dispatches that failed to decode <see cref="FailedSequence"/>.</summary>
    public int FailureCount { get; init; }

    /// <summary>Whether this aggregate is parked and must no longer be redispatched.</summary>
    public bool Parked { get; init; }
}
