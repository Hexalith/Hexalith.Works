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
/// A stream-read failure is isolated (logged + skipped) at both levels — one unreadable candidate aggregate
/// never aborts the rest of its own tenant's candidates, and one unreadable tenant never aborts the rest of the
/// cross-tenant scan: a persistently-unreadable stream must not silently starve reminder discovery/recovery for
/// everything else. The scan still marks itself incomplete by throwing
/// <see cref="PendingDateAwaitScanIncompleteException"/> after every tenant has been attempted, carrying the
/// partial results collected from the tenants that scanned cleanly so a caller (<see cref="DateReminderReconciler"/>)
/// can act on that partial evidence immediately and let only the failed tenant(s) be retried.
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
    public async Task<IReadOnlyList<PendingDateAwait>> GetPendingDateAwaitsAsync(CancellationToken cancellationToken = default)
    {
        ReadModelEntry<PendingDateAwaitTenantRegistry> entry = await _store
            .GetAsync<PendingDateAwaitTenantRegistry>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, cancellationToken)
            .ConfigureAwait(false);
        PendingDateAwaitTenantRegistry? registry = entry.Value;

        if (registry is null || registry.Tenants.Count == 0)
        {
            return [];
        }

        var pending = new List<PendingDateAwait>();
        int failedTenantCount = 0;
        int failedCandidateCount = 0;
        Exception? lastFailure = null;

        foreach (string tenant in registry.Tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                TenantScanResult tenantScan = await ScanTenantAsync(tenant, cancellationToken).ConfigureAwait(false);
                pending.AddRange(tenantScan.Pending);
                failedCandidateCount += tenantScan.FailedCandidateCount;
                lastFailure = tenantScan.LastFailure ?? lastFailure;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedTenantCount++;
                lastFailure = ex;
                WorksRecoveryLog.PendingDateAwaitTenantScanFailed(_logger, tenant, ex);
            }
        }

        if (failedTenantCount > 0 || failedCandidateCount > 0)
        {
            throw new PendingDateAwaitScanIncompleteException(pending, failedTenantCount, failedCandidateCount, lastFailure);
        }

        return pending;
    }

    private async Task<TenantScanResult> ScanTenantAsync(string tenant, CancellationToken cancellationToken)
    {
        ReadModelEntry<PendingDateAwaitTenantIndex> entry = await _store
            .GetAsync<PendingDateAwaitTenantIndex>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(tenant), cancellationToken)
            .ConfigureAwait(false);
        if (entry.Value is null || entry.Value.Entries.Count == 0)
        {
            return new TenantScanResult([], 0, null);
        }

        var pending = new List<PendingDateAwait>();
        int failedCandidateCount = 0;
        Exception? lastFailure = null;
        foreach (string workItemId in entry.Value.Entries.Keys)
        {
            // The index only tells us which aggregates to inspect; the stream is authoritative. One unreadable
            // candidate stream is isolated to that candidate: the awaits already collected for this tenant are
            // kept, the remaining candidates are still scanned, and the failure is rolled into the pass-level
            // PendingDateAwaitScanIncompleteException so the retry still happens. Recovery must degrade to the
            // items it can read, never collapse for the whole tenant (and, in a single-tenant topology, for the
            // whole system) because of one wedged work item.
            try
            {
                pending.AddRange(await PendingDateAwaitStreamReader
                    .RebuildAsync(_gateway, tenant, workItemId, _options.EffectiveMaxStreamPagesPerAggregate, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCandidateCount++;
                lastFailure = ex;
                WorksRecoveryLog.PendingDateAwaitCandidateScanFailed(_logger, tenant, workItemId, ex);
            }
        }

        return new TenantScanResult(pending, failedCandidateCount, lastFailure);
    }

    /// <summary>One tenant's scan outcome: what was discovered, and how much of it could not be read.</summary>
    private sealed record TenantScanResult(
        IReadOnlyList<PendingDateAwait> Pending,
        int FailedCandidateCount,
        Exception? LastFailure);
}
