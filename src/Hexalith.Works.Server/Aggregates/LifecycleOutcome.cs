using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Server.Aggregates;

/// <summary>The kind of outcome a lifecycle transition decision produces.</summary>
internal enum LifecycleDecision
{
    /// <summary>The transition is legal; the item moves to the paired target status.</summary>
    Accept,

    /// <summary>The transition is illegal from the current status; refuse with a rejection event.</summary>
    Reject,

    /// <summary>An idempotent duplicate of the terminal command; acknowledge with no state change.</summary>
    NoOp,
}

/// <summary>
/// The decision returned by <see cref="WorkItemLifecycle.Decide"/>: an outcome kind and, for
/// <see cref="LifecycleDecision.Accept"/>, the resulting <see cref="WorkItemStatus"/>.
/// </summary>
internal readonly record struct LifecycleOutcome(LifecycleDecision Decision, WorkItemStatus Target)
{
    /// <summary>An accepted transition to <paramref name="target"/>.</summary>
    public static LifecycleOutcome Accept(WorkItemStatus target) => new(LifecycleDecision.Accept, target);

    /// <summary>A rejected (illegal) transition.</summary>
    public static LifecycleOutcome Reject { get; } = new(LifecycleDecision.Reject, WorkItemStatus.Unknown);

    /// <summary>An idempotent no-op (duplicate terminal command).</summary>
    public static LifecycleOutcome NoOp { get; } = new(LifecycleDecision.NoOp, WorkItemStatus.Unknown);
}
