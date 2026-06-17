using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Commands;

namespace Hexalith.Works.Runtime;

/// <summary>
/// Production <see cref="IWorkCommandSubmitter"/> that submits a Works command through the EventStore command
/// gateway (<c>POST /api/v1/commands</c>) — the same public command surface Story 4.5 proved end-to-end. The
/// deterministic <see cref="WorkCommandSubmission.CausationId"/> is carried as the command's
/// <see cref="SubmitCommandRequest.MessageId"/> so the substrate can dedup an exact at-least-once redelivery;
/// the aggregate's own idempotency (duplicate resume/terminal → no-op) is the primary safety net and is
/// proven deterministically without this gateway.
/// </summary>
public sealed class EventStoreGatewayWorkCommandSubmitter(IEventStoreGatewayClient gateway) : IWorkCommandSubmitter
{
    private readonly IEventStoreGatewayClient _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

    /// <inheritdoc/>
    public async Task SubmitAsync(WorkCommandSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var request = new SubmitCommandRequest(
            MessageId: submission.CausationId,
            Tenant: submission.Tenant,
            Domain: WorkCommandSubmission.WorkDomain,
            AggregateId: submission.AggregateId,
            CommandType: submission.CommandType,
            Payload: submission.Payload,
            CorrelationId: submission.CorrelationId);

        _ = await _gateway.SubmitCommandAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
