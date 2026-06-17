using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Production <see cref="IPendingDateAwaitSource"/> that rebuilds the still-pending <c>DateReached</c> awaits
/// by reading the persisted <c>work</c> streams for the configured tenant scope and applying the pure
/// <see cref="PendingDateAwaitProjection"/> per aggregate (Story 4.6, AC #3). Because neither the EventStore
/// stream-read surface nor the Dapr state store exposes cross-tenant enumeration, the scan is bounded to
/// <see cref="WorksRecoveryOptions.Tenants"/> — the documented substrate limitation. The deterministic
/// adapter lane proves the reconciliation logic with a fake source; this lane runs only under Aspire.
/// </summary>
public sealed class StreamReadingPendingDateAwaitSource(
    IEventStoreGatewayClient gateway,
    IOptions<WorksRecoveryOptions> options,
    ILogger<StreamReadingPendingDateAwaitSource> logger) : IPendingDateAwaitSource
{
    private const int PageSize = 200;

    private readonly IEventStoreGatewayClient _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<StreamReadingPendingDateAwaitSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PendingDateAwait>> GetPendingDateAwaitsAsync(CancellationToken cancellationToken = default)
    {
        var pending = new List<PendingDateAwait>();
        foreach (string tenant in _options.Tenants)
        {
            try
            {
                pending.AddRange(await ScanTenantAsync(tenant, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                WorksRecoveryLog.RecoveryStepFailed(_logger, "scan-tenant-date-awaits", ex);
            }
        }

        return pending;
    }

    private async Task<IReadOnlyList<PendingDateAwait>> ScanTenantAsync(string tenant, CancellationToken cancellationToken)
    {
        var byAggregate = new Dictionary<string, List<(long Sequence, IEventPayload Payload)>>(StringComparer.Ordinal);

        ReplayContinuationToken? continuation = null;
        long from = 0;

        for (int page = 0; page < _options.MaxStreamPagesPerTenant; page++)
        {
            var request = new StreamReadRequest(
                Tenant: tenant,
                Domain: WorkCommandSubmission.WorkDomain,
                AggregateId: null,
                FromSequence: from,
                ContinuationToken: continuation,
                PageSize: PageSize);

            StreamReadPage result = await _gateway.ReadStreamAsync(request, cancellationToken).ConfigureAwait(false);

            foreach (StreamReadEvent streamEvent in result.Events)
            {
                IEventPayload? payload = WorksEventDecoder.Decode(streamEvent.EventTypeName, streamEvent.Payload);
                string? aggregateId = AggregateIdOf(payload);
                if (aggregateId is null)
                {
                    continue;
                }

                if (!byAggregate.TryGetValue(aggregateId, out List<(long, IEventPayload)>? events))
                {
                    events = [];
                    byAggregate[aggregateId] = events;
                }

                events.Add((streamEvent.SequenceNumber, payload!));
            }

            if (!result.Metadata.IsTruncated)
            {
                break;
            }

            continuation = result.Metadata.NextContinuationToken;

            // Advance past the last event returned so the next page does not re-read the page-boundary
            // event (the gateway paginates by FromSequence = LastSequenceReturned + 1). Re-reading is
            // harmless here — the projection is idempotent — but advancing is clearer and cheaper.
            from = result.Metadata.LastSequenceReturned is { } lastSequence ? lastSequence + 1 : from;
        }

        var pending = new List<PendingDateAwait>();
        foreach (List<(long Sequence, IEventPayload Payload)> events in byAggregate.Values)
        {
            IReadOnlyList<IEventPayload> ordered = [.. events.OrderBy(static e => e.Sequence).Select(static e => e.Payload)];
            pending.AddRange(PendingDateAwaitProjection.PendingDateAwaits(ordered));
        }

        return pending;
    }

    // Only the suspend/resume/terminal events drive pending date-await state; everything else is irrelevant.
    private static string? AggregateIdOf(IEventPayload? payload) => payload switch
    {
        WorkItemSuspended suspended => suspended.AggregateId,
        WorkItemResumed resumed => resumed.AggregateId,
        WorkItemCancelled cancelled => cancelled.AggregateId,
        WorkItemExpired expired => expired.AggregateId,
        WorkItemCompleted completed => completed.AggregateId,
        WorkItemRejected rejected => rejected.AggregateId,
        _ => null,
    };
}
