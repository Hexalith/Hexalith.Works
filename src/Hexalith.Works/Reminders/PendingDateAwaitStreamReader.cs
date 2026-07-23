using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Rebuilds one work item's currently-pending <c>DateReached</c> awaits from its persisted per-aggregate stream
/// (Story 4.8). Every read carries an <c>AggregateId</c> so it never issues the tenant-wide, null-aggregate read
/// the EventStore gateway 400-rejects; paging advances by <c>FromSequence = LastSequenceReturned + 1</c> with a
/// null continuation token (the gateway fail-closes on any non-null token). Shared by the steady-state suspend
/// handler and the recovery source so both discover truth through the single pure
/// <see cref="PendingDateAwaitProjection"/> fold rather than a second, drift-prone one.
/// </summary>
internal static class PendingDateAwaitStreamReader
{
    private const int PageSize = 200;

    internal static async Task<IReadOnlyList<PendingDateAwait>> RebuildAsync(
        IEventStoreGatewayClient gateway,
        string tenantId,
        string workItemId,
        int maxPages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        var events = new List<(long Sequence, IEventPayload Payload)>();
        long from = 0;
        bool stillTruncated = false;

        for (int page = 0; page < maxPages; page++)
        {
            var request = new StreamReadRequest(
                Tenant: tenantId,
                Domain: WorkCommandSubmission.WorkDomain,
                AggregateId: workItemId,
                FromSequence: from,
                ContinuationToken: null,
                PageSize: PageSize);
            StreamReadPage result = await gateway.ReadStreamAsync(request, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(result.Tenant, tenantId, StringComparison.Ordinal)
                || !string.Equals(result.AggregateId, workItemId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("EventStore returned a stream outside the requested identity.");
            }

            foreach (StreamReadEvent streamEvent in result.Events)
            {
                IEventPayload? payload = WorksEventDecoder.Decode(streamEvent.EventTypeName, streamEvent.Payload);
                if (payload is not null)
                {
                    events.Add((streamEvent.SequenceNumber, payload));
                }
            }

            stillTruncated = result.Metadata.IsTruncated;
            if (!stillTruncated)
            {
                break;
            }

            from = result.Metadata.LastSequenceReturned is { } lastSequence ? lastSequence + 1 : from;
        }

        if (stillTruncated)
        {
            // The stream still has unread pages after the configured page budget: rebuilding pending-await state
            // from what was read would be silently partial. Fail closed instead of risking a wrong reissue.
            throw new InvalidOperationException(
                $"Stream for aggregate '{workItemId}' exceeded the configured {nameof(WorksRecoveryOptions.MaxStreamPagesPerTenant)} per-aggregate page budget while still truncated.");
        }

        IReadOnlyList<IEventPayload> ordered = [.. events.OrderBy(static value => value.Sequence).Select(static value => value.Payload)];
        return PendingDateAwaitProjection.PendingDateAwaits(ordered);
    }
}
