using Hexalith.Works.Contracts.Ports;

namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record Obligation
{
    public Obligation(string description, ExpectationReference? reference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description.Trim();
        Reference = reference;
    }

    public string Description { get; }

    /// <summary>
    /// Optional, reference-only pointer to an expectation that is interpreted on demand by
    /// <see cref="IExpectationResolver"/>. This is never the interpreted value: the created event and
    /// replayed state carry only this reference, so replay is deterministic whether the reference is
    /// absent or present-but-uninterpreted. The field is additive and nullable, so an obligation
    /// serialized before this field existed deserializes with a null reference.
    /// </summary>
    public ExpectationReference? Reference { get; }
}
