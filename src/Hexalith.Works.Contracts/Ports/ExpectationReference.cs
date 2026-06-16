namespace Hexalith.Works.Contracts.Ports;

/// <summary>
/// A Works-owned, optional <em>reference</em> (a stable pointer) to an expectation that is
/// interpreted on demand by <see cref="IExpectationResolver"/>. This is a reference only — never the
/// interpreted value — so it is safe to carry on coordination facts, events, and replayed state.
/// </summary>
/// <remarks>
/// Mirrors the lightweight, validated, "additive but not silently tolerant" value-object posture of
/// the other Works-owned references (for example <c>PartyId</c>): the pointer is trimmed and a
/// null/blank value is rejected at construction. The resolved <see cref="Expectation"/> is computed
/// on demand and is never stored; only this reference is persisted (FR-2, NFR-11).
/// </remarks>
public sealed record ExpectationReference
{
    public ExpectationReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }
}
