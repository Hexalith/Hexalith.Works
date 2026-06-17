using System.Text.Json;

namespace Hexalith.Works.Runtime;

/// <summary>
/// Host-edge seam that submits a single Works domain command into the EventStore command pipeline that
/// Story 4.5 proved (the public command gateway <c>POST /api/v1/commands</c> → EventStore → the Works
/// <c>/process</c> domain service → <c>WorkItemAggregate.Handle</c>). The reminder and cascade runtimes
/// depend only on this abstraction so their at-least-once dispatch logic stays deterministically testable
/// without Dapr or a live gateway.
/// </summary>
/// <remarks>
/// The runtime never decides domain acceptance: every submission round-trips through the aggregate, which
/// owns accept / reject / idempotent no-op. A redelivered submission is safe because the carried
/// <see cref="WorkCommandSubmission.CorrelationId"/> / <see cref="WorkCommandSubmission.CausationId"/> are
/// deterministic, so the EventStore substrate can dedup the duplicate and the aggregate no-ops an exact
/// duplicate terminal/resume command (Story 3.6 / Story 2.x idempotency).
/// </remarks>
public interface IWorkCommandSubmitter
{
    /// <summary>Submits one Works command into the EventStore command gateway path.</summary>
    /// <param name="submission">The deterministic, metadata-only command submission.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task SubmitAsync(WorkCommandSubmission submission, CancellationToken cancellationToken = default);
}

/// <summary>
/// A metadata-only Works command submission. It carries identity, the command type discriminator, the
/// pre-serialized command payload, and deterministic correlation/causation ids for at-least-once dedup.
/// No clock, secret, token, or obligation text is part of this contract.
/// </summary>
/// <param name="Tenant">The tenant id (raw <c>TenantId.Value</c>).</param>
/// <param name="AggregateId">The work item aggregate id (raw <c>WorkItemId.Value</c>).</param>
/// <param name="CommandType">The command type discriminator (e.g. <c>nameof(ResumeWorkItem)</c>).</param>
/// <param name="Payload">The command serialized with <c>JsonSerializerDefaults.Web</c>.</param>
/// <param name="CorrelationId">A deterministic correlation id so a redelivery reuses the same id.</param>
/// <param name="CausationId">A deterministic causation id so the substrate can dedup a redelivery.</param>
public sealed record WorkCommandSubmission(
    string Tenant,
    string AggregateId,
    string CommandType,
    JsonElement Payload,
    string CorrelationId,
    string CausationId)
{
    /// <summary>The Works domain name the EventStore gateway routes on.</summary>
    public const string WorkDomain = "work";
}
