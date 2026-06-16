namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record Obligation
{
    public Obligation(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description.Trim();
    }

    public string Description { get; }
}
