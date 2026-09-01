namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// Describes how an accepted payload affects one projection.
/// </summary>
internal enum ProjectionPayloadEffectDisposition
{
    /// <summary>
    /// No effect contract has been assigned.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The payload participates in the projection's sorted fold.
    /// </summary>
    Fold = 1,

    /// <summary>
    /// The payload applies a one-shot topology effect after sequence acceptance.
    /// </summary>
    Topology = 2,

    /// <summary>
    /// The payload applies both a one-shot topology effect and a sorted fold effect.
    /// </summary>
    TopologyAndFold = 3,

    /// <summary>
    /// The payload intentionally changes no visible projection state while retaining its accepted sequence.
    /// </summary>
    IntentionalNoOp = 4,
}
