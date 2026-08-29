using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Reactor;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Production <see cref="ICascadeDescendantSource"/> that discovers a parent's <em>direct</em> children by
/// reading its persisted stream for <see cref="ChildSpawned"/> events (Story 4.6, AC #4/#5). Only direct
/// children are returned: each child's own terminal event drives the next cascade level, so the subtree
/// converges by event propagation. Each child's terminal flag comes from its persisted per-item roll-up;
/// a missing or unreadable roll-up fails closed as active so the aggregate remains the final authority.
/// This lane runs only under Aspire; the deterministic tests fake the seam.
/// </summary>
public sealed class StreamReadingCascadeDescendantSource(
    IEventStoreGatewayClient gateway,
    IReadModelStore store,
    IOptions<WorksRecoveryOptions> options,
    ILogger<StreamReadingCascadeDescendantSource> logger) : ICascadeDescendantSource
{
    private const int PageSize = 200;

    private readonly IEventStoreGatewayClient _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    private readonly IReadModelStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<StreamReadingCascadeDescendantSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CascadeDescendant>> GetDescendantsAsync(string tenantId, string parentWorkItemId, CancellationToken cancellationToken = default)
    {
        var children = new List<CascadeDescendant>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            ReplayContinuationToken? continuation = null;
            long from = 0;
            bool stillTruncated = false;

            for (int page = 0; page < _options.MaxStreamPagesPerTenant; page++)
            {
                var request = new StreamReadRequest(
                    Tenant: tenantId,
                    Domain: WorkCommandSubmission.WorkDomain,
                    AggregateId: parentWorkItemId,
                    FromSequence: from,
                    ContinuationToken: continuation,
                    PageSize: PageSize);

                StreamReadPage result = await _gateway.ReadStreamAsync(request, cancellationToken).ConfigureAwait(false);

                foreach (StreamReadEvent streamEvent in result.Events)
                {
                    if (WorksEventDecoder.Decode(streamEvent.EventTypeName, streamEvent.Payload) is ChildSpawned childSpawned
                        && seen.Add(childSpawned.ChildWorkItemId.Value))
                    {
                        bool isTerminal = await IsTerminalAsync(
                            tenantId,
                            childSpawned.ChildWorkItemId.Value,
                            cancellationToken).ConfigureAwait(false);
                        children.Add(new CascadeDescendant(childSpawned.TenantId, childSpawned.ChildWorkItemId, isTerminal));
                    }
                }

                stillTruncated = result.Metadata.IsTruncated;
                if (!stillTruncated)
                {
                    break;
                }

                continuation = result.Metadata.NextContinuationToken;

                // Advance past the last event returned so the next page does not re-read the page-boundary
                // event (the gateway paginates by FromSequence = LastSequenceReturned + 1). Re-reading is
                // harmless here — the HashSet dedups child ids — but advancing is clearer and cheaper.
                from = result.Metadata.LastSequenceReturned is { } lastSequence ? lastSequence + 1 : from;
            }

            if (stillTruncated)
            {
                // The parent stream still has unread pages after exhausting the configured page budget: building a
                // cascade checkpoint from a partial descendant set would silently miss targets, and because
                // EnsureCheckpointAsync never re-discovers once a checkpoint exists, those targets would be
                // skipped permanently. Fail closed instead (mirrors StreamReadingChildCompletionAwaitingParentSource).
                throw new InvalidOperationException(
                    $"Stream for aggregate '{parentWorkItemId}' exceeded the configured {nameof(WorksRecoveryOptions.MaxStreamPagesPerTenant)} page budget while still truncated.");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown/timeout cancellation is not a discovery failure: let it propagate without logging a
            // spurious recovery-step-failed (consistent with IsTerminalAsync below).
            throw;
        }
        catch (Exception ex)
        {
            WorksRecoveryLog.RecoveryStepFailed(_logger, "discover-cascade-descendants", ex);
            throw;
        }

        return children;
    }

    private async Task<bool> IsTerminalAsync(string tenantId, string workItemId, CancellationToken cancellationToken)
    {
        try
        {
            ReadModelEntry<WorksWhatsNextTenantIndex> currentIndexEntry = await _store
                .GetAsync<WorksWhatsNextTenantIndex>(
                    WorksReadModelKeys.StateStoreName,
                    WorksReadModelKeys.CurrentWhatsNextIndexKey(tenantId),
                    cancellationToken)
                .ConfigureAwait(false);
            if (currentIndexEntry.Value is not null)
            {
                return await IsCurrentTerminalAsync(
                    tenantId,
                    workItemId,
                    currentIndexEntry.Value,
                    cancellationToken).ConfigureAwait(false);
            }

            ReadModelEntry<WorkItemRollUp> legacyEntry = await _store
                .GetAsync<WorkItemRollUp>(
                    WorksReadModelKeys.StateStoreName,
                    WorksReadModelKeys.RollUpKey(tenantId, workItemId),
                    cancellationToken)
                .ConfigureAwait(false);
            if (MatchesIdentity(legacyEntry.Value, tenantId, workItemId))
            {
                return IsTerminal(legacyEntry.Value!);
            }

            // Close the atomic rebuild commit window between an initial current-manifest miss and the
            // subsequent legacy-roll-up miss. A second miss is the bounded fail-closed result.
            currentIndexEntry = await _store
                .GetAsync<WorksWhatsNextTenantIndex>(
                    WorksReadModelKeys.StateStoreName,
                    WorksReadModelKeys.CurrentWhatsNextIndexKey(tenantId),
                    cancellationToken)
                .ConfigureAwait(false);
            return currentIndexEntry.Value is not null
                && await IsCurrentTerminalAsync(
                    tenantId,
                    workItemId,
                    currentIndexEntry.Value,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown/timeout cancellation is not a read failure: let it propagate instead of being
            // swallowed into the fail-closed "active" result below.
            throw;
        }
        catch (Exception exception)
        {
            WorksRecoveryLog.RecoveryStepFailed(_logger, "read-cascade-descendant-rollup", exception);
            return false;
        }
    }

    private async Task<bool> IsCurrentTerminalAsync(
        string tenantId,
        string workItemId,
        WorksWhatsNextTenantIndex index,
        CancellationToken cancellationToken)
    {
        if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(index)
            || !index.MemberWorkItemIds.Contains(workItemId, StringComparer.Ordinal))
        {
            return false;
        }

        ReadModelEntry<WorkItemRollUp> entry = await _store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.CurrentRollUpKey(tenantId, workItemId),
                cancellationToken)
            .ConfigureAwait(false);
        return MatchesIdentity(entry.Value, tenantId, workItemId) && IsTerminal(entry.Value!);
    }

    private static bool IsTerminal(WorkItemRollUp rollUp)
        => rollUp.Status is WorkItemStatus.Completed
            or WorkItemStatus.Cancelled
            or WorkItemStatus.Rejected
            or WorkItemStatus.Expired;

    private static bool MatchesIdentity(WorkItemRollUp? rollUp, string tenantId, string workItemId)
        => rollUp is not null
            && string.Equals(rollUp.TenantId?.Value, tenantId, StringComparison.Ordinal)
            && string.Equals(rollUp.WorkItemId?.Value, workItemId, StringComparison.Ordinal);
}
