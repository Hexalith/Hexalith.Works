using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.Works.Projections;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Production <see cref="IPendingDateAwaitSource"/> that discovers pending <c>DateReached</c> awaits from the
/// durable pending-date-await index maintained by <see cref="WorkItemProjectionDispatcher"/> (Story 4.8, AC #2/#3).
/// It enumerates the tenant registry, reads each tenant's index document, and — because the index is
/// <em>discovery</em> and the stream is <em>truth</em> (DD-3) — re-folds every candidate aggregate's per-aggregate
/// stream through the pure <see cref="PendingDateAwaitProjection"/> before returning, so a stale index entry whose
/// stream has since resumed is silently skipped and can never trigger a wrong reissue. This replaces the retired
/// tenant-wide, null-<c>AggregateId</c> scan the gateway 400-rejects, and needs no per-tenant hand configuration:
/// the tenant registry is durable data, not <c>Works:Recovery:Tenants</c>.
/// </summary>
/// <remarks>
/// A candidate-read failure is isolated (logged + skipped) at both levels — one unreadable parking document or
/// aggregate stream never aborts the rest of its own tenant's candidates, and one unreadable tenant never aborts
/// the rest of the cross-tenant scan: a persistently-unreadable candidate must not silently starve reminder
/// discovery/recovery for everything else. Parking-document failures are classified separately and never fall
/// through to a stream read. The scan still marks itself incomplete by throwing
/// <see cref="PendingDateAwaitScanIncompleteException"/> after all eligible tenants are attempted, or after cancellation
/// stops it at a tenant boundary where outer failure evidence already exists. The exception carries the partial results
/// and counts collected so far so a caller (<see cref="DateReminderReconciler"/>) can act on that evidence immediately.
/// Between tenants, caller cancellation preserves failure evidence already incorporated into those cross-tenant counts
/// without counting or logging the cancellation; a clean scan still propagates the caller cancellation unchanged. Within a
/// tenant, the exact-token filters remain authoritative by design: an exact caller cancellation after a prior
/// candidate failure can still leave that tenant's local partial evidence unreported until the next startup pass.
/// A candidate already parked by <see cref="WorkItemProjectionDispatcher"/> is a countable clean skip: its
/// stream cannot be rebuilt, so counting it incomplete would burn startup reconciliation's retry budget on a
/// terminal projection failure, while hiding the skip would make the pass appear complete.
/// </remarks>
internal sealed class IndexedPendingDateAwaitSource(
    IReadModelStore store,
    IEventStoreGatewayClient gateway,
    IOptions<WorksRecoveryOptions> options,
    ILogger<IndexedPendingDateAwaitSource> logger) : IPendingDateAwaitSource
{
    private readonly IEventStoreGatewayClient _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    private readonly ILogger<IndexedPendingDateAwaitSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly IReadModelStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc/>
    public async Task<PendingDateAwaitScanResult> GetPendingDateAwaitsAsync(CancellationToken cancellationToken = default)
    {
        ReadModelEntry<PendingDateAwaitTenantRegistry> entry = await _store
            .GetAsync<PendingDateAwaitTenantRegistry>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, cancellationToken)
            .ConfigureAwait(false);
        PendingDateAwaitTenantRegistry? registry = entry.Value;

        if (registry is null || registry.Tenants.Count == 0)
        {
            return new PendingDateAwaitScanResult([]);
        }

        var pending = new List<PendingDateAwait>();
        int failedTenantCount = 0;
        int failedCandidateCount = 0;
        int skippedParkedCount = 0;
        Exception? lastFailure = null;

        foreach (string tenant in registry.Tenants)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (failedTenantCount > 0 || failedCandidateCount > 0)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                TenantScanResult tenantScan = await ScanTenantAsync(tenant, cancellationToken).ConfigureAwait(false);
                pending.AddRange(tenantScan.Pending);
                failedCandidateCount += tenantScan.FailedCandidateCount;
                skippedParkedCount += tenantScan.SkippedParkedCount;
                lastFailure = tenantScan.LastFailure ?? lastFailure;
            }
            catch (OperationCanceledException ex) when (
                cancellationToken.IsCancellationRequested
                && ex.CancellationToken == cancellationToken)
            {
                if (failedTenantCount > 0 || failedCandidateCount > 0)
                {
                    break;
                }

                throw;
            }
            catch (Exception ex)
            {
                failedTenantCount++;
                lastFailure = ex;
                WorksRecoveryLog.PendingDateAwaitTenantScanFailed(_logger, tenant, ex);
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        if (failedTenantCount > 0 || failedCandidateCount > 0)
        {
            throw new PendingDateAwaitScanIncompleteException(
                pending,
                failedTenantCount,
                failedCandidateCount,
                skippedParkedCount,
                lastFailure);
        }

        return new PendingDateAwaitScanResult(pending, skippedParkedCount);
    }

    private async Task<TenantScanResult> ScanTenantAsync(string tenant, CancellationToken cancellationToken)
    {
        ReadModelEntry<PendingDateAwaitTenantIndex> entry = await _store
            .GetAsync<PendingDateAwaitTenantIndex>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(tenant), cancellationToken)
            .ConfigureAwait(false);
        if (entry.Value is null || entry.Value.Entries.Count == 0)
        {
            return new TenantScanResult([], 0, 0, null);
        }

        var pending = new List<PendingDateAwait>();
        int failedCandidateCount = 0;
        int skippedParkedCount = 0;
        Exception? lastFailure = null;
        foreach (string workItemId in entry.Value.Entries.Keys)
        {
            ReadModelEntry<WorkItemProjectionParking> parking;
            try
            {
                parking = await _store
                    .GetAsync<WorkItemProjectionParking>(
                        WorksReadModelKeys.StateStoreName,
                        WorksReadModelKeys.ProjectionParkingKey(tenant, workItemId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (
                cancellationToken.IsCancellationRequested
                && ex.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCandidateCount++;
                lastFailure = ex;
                WorksRecoveryLog.PendingDateAwaitParkingLookupFailed(_logger, tenant, workItemId, ex);
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            if (parking.Value is { Parked: true })
            {
                // Terminal /project disposition: the stream cannot be folded. Keep the skip observable without
                // making it retryable, and never read the known-unfoldable authoritative stream.
                skippedParkedCount++;
                WorksRecoveryLog.PendingDateAwaitParkedCandidateSkipped(_logger, tenant, workItemId);
                continue;
            }

            // The index only tells us which aggregates to inspect; the stream is authoritative. One unreadable
            // candidate stream is isolated to that candidate: the awaits already collected for this tenant are
            // kept, the remaining candidates are normally still scanned, and the failure is rolled into the
            // pass-level PendingDateAwaitScanIncompleteException so the retry still happens. A foreign
            // cancellation observed alongside caller cancellation stops at this boundary so an exact caller
            // cancellation from the next candidate cannot mask that typed evidence. Recovery must degrade to
            // the items it can read, never collapse for the whole tenant (and, in a single-tenant topology, for
            // the whole system) because of one wedged work item.
            try
            {
                pending.AddRange(await PendingDateAwaitStreamReader
                    .RebuildAsync(_gateway, tenant, workItemId, _options.EffectiveMaxStreamPagesPerAggregate, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException ex) when (
                cancellationToken.IsCancellationRequested
                && ex.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCandidateCount++;
                lastFailure = ex;
                WorksRecoveryLog.PendingDateAwaitCandidateScanFailed(_logger, tenant, workItemId, ex);
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        return new TenantScanResult(pending, failedCandidateCount, skippedParkedCount, lastFailure);
    }

    /// <summary>One tenant's scan outcome: what was discovered, and how much of it could not be read.</summary>
    private sealed record TenantScanResult(
        IReadOnlyList<PendingDateAwait> Pending,
        int FailedCandidateCount,
        int SkippedParkedCount,
        Exception? LastFailure);
}
