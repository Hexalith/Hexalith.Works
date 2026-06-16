namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record WorkItemEffort
{
    public WorkItemEffort(decimal estimated, Unit unit, decimal done = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(estimated);
        ArgumentOutOfRangeException.ThrowIfNegative(done);
        ArgumentNullException.ThrowIfNull(unit);
        if (done > estimated)
        {
            throw new ArgumentOutOfRangeException(nameof(done), done, "Done effort cannot exceed estimated effort.");
        }

        Estimated = estimated;
        Unit = unit;
        Done = done;
    }

    public decimal Estimated { get; }

    public Unit Unit { get; }

    public decimal Done { get; }

    public decimal Remaining => Estimated - Done;

    public WorkItemEffort Report(decimal doneDelta)
    {
        if (doneDelta <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doneDelta), doneDelta, "Done delta must be positive.");
        }

        return new WorkItemEffort(Estimated, Unit, Math.Min(Estimated, Done + doneDelta));
    }

    // Re-estimate to a new absolute estimate while preserving the established (immutable) Unit. Done is
    // clamped down to the new estimate so the derived Remaining is never negative — re-estimating below
    // current Done lands Remaining on zero without re-deriving or storing it.
    public WorkItemEffort ReEstimate(decimal newEstimated)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newEstimated);

        return new WorkItemEffort(newEstimated, Unit, Math.Min(Done, newEstimated));
    }
}
