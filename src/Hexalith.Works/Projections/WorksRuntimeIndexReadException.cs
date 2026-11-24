namespace Hexalith.Works.Projections;

/// <summary>Preserves SDK selection metadata when the selected runtime indexes fail validation.</summary>
/// <param name="generation">The selected generation.</param>
/// <param name="isStale">Whether the SDK marked the selected generation stale.</param>
/// <param name="cause">The underlying index validation failure.</param>
internal sealed class WorksRuntimeIndexReadException(long generation, bool isStale, Exception cause)
    : InvalidOperationException("The selected Works runtime index could not be read.", cause)
{
    /// <summary>Gets the selected SDK generation.</summary>
    internal long Generation { get; } = generation;

    /// <summary>Gets whether the SDK marked the selected generation stale.</summary>
    internal bool IsStale { get; } = isStale;
}
