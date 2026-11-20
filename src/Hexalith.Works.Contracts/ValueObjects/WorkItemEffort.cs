namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record WorkItemEffort
{
    public WorkItemEffort(decimal estimated, Unit unit, decimal done = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(estimated);
        ArgumentOutOfRangeException.ThrowIfNegative(done);
        ArgumentNullException.ThrowIfNull(unit);

        Estimated = estimated;
        Unit = unit;
        Done = done;
    }

    public decimal Estimated { get; }

    public Unit Unit { get; }

    public decimal Done { get; }

    public decimal Remaining => Math.Max(0m, Estimated - Done);

    public WorkItemEffort Report(decimal doneDelta)
    {
        if (doneDelta <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doneDelta), doneDelta, "Done delta must be positive.");
        }

        decimal nextDone = doneDelta >= Remaining ? Math.Max(Done, Estimated) : Done + doneDelta;
        return new WorkItemEffort(Estimated, Unit, nextDone);
    }

    public WorkItemEffort ReEstimate(decimal newEstimated)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newEstimated);

        return new WorkItemEffort(newEstimated, Unit, Done);
    }
}
