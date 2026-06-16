namespace Hexalith.Works.Contracts.Ports;

/// <summary>
/// The interpreted-on-demand result returned by <see cref="IExpectationResolver"/> for a given
/// <see cref="ExpectationReference"/>.
/// </summary>
/// <remarks>
/// An <see cref="Expectation"/> is resolved on demand and is <strong>never</strong> stored in an
/// event, command, or replayed state (FR-2, NFR-11). Only the stable <see cref="ExpectationReference"/>
/// is carried on coordination facts; the interpreted value is recomputed outside the aggregate when a
/// caller actually needs it. Keeping the interpreted value out of persisted state preserves the
/// natural-language-is-data boundary: stored coordination facts never embed model-interpreted text.
/// </remarks>
public sealed record Expectation
{
    public Expectation(string interpretedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interpretedValue);
        InterpretedValue = interpretedValue;
    }

    public string InterpretedValue { get; }
}
