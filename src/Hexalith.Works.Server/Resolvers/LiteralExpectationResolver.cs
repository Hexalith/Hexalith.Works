using Hexalith.Works.Contracts.Ports;

namespace Hexalith.Works.Server.Resolvers;

/// <summary>
/// No-LLM v1 implementation of <see cref="IExpectationResolver"/>.
/// </summary>
/// <remarks>
/// This implementation performs no interpretation: it is a literal, verbatim passthrough that echoes
/// the reference value as the <see cref="Expectation"/>. It calls no LLM, clock, random source,
/// network, or I/O, so the server kernel stays deterministic and replay-safe. The future
/// prompt-injection boundary (NFR-11) lives at the port; v1 deliberately interprets nothing.
/// </remarks>
public sealed class LiteralExpectationResolver : IExpectationResolver
{
    public ValueTask<Expectation?> ResolveAsync(ExpectationReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new ValueTask<Expectation?>(new Expectation(reference.Value));
    }
}
