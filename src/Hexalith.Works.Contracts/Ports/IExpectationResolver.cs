namespace Hexalith.Works.Contracts.Ports;

/// <summary>
/// Domain-owned port that interprets an <see cref="ExpectationReference"/> into an
/// <see cref="Expectation"/> on demand, outside the aggregate.
/// </summary>
/// <remarks>
/// v1 ships only a no-LLM, literal implementation; this contracts package must not require an LLM,
/// embedding model, vector store, or any infrastructure backend. The aggregate never calls a resolver:
/// references are carried on coordination facts and interpreted on demand by a caller, so a work item
/// stays valid when no interpreted <see cref="Expectation"/> is produced. This port is the future
/// prompt-injection boundary (NFR-11): interpreted natural language is treated as data, never as
/// trusted input, and the interpreted value is never persisted into events or replayed state.
/// </remarks>
public interface IExpectationResolver
{
    ValueTask<Expectation?> ResolveAsync(ExpectationReference reference, CancellationToken cancellationToken = default);
}
