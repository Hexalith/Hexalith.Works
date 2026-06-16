namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record Unit
{
    public Unit(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
