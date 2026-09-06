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
/// A single tenant's stream-read failure is isolated (logged + skipped) rather than aborting the whole
/// cross-tenant scan: a persistently-unreadable stream in one tenant must not silently starve reminder
/// discovery/recovery for every other tenant. The scan still marks itself incomplete by throwing
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
        Exception? lastFailure = null;

        foreach (string tenant in registry.Tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                pending.AddRange(await ScanTenantAsync(tenant, cancellationToken).ConfigureAwait(false));
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

        if (failedTenantCount > 0)
        {
            throw new PendingDateAwaitScanIncompleteException(pending, failedTenantCount, lastFailure);
        }

        return pending;
    }

    private async Task<IReadOnlyList<PendingDateAwait>> ScanTenantAsync(string tenant, CancellationToken cancellationToken)
    {
        ReadModelEntry<PendingDateAwaitTenantIndex> entry = await _store
            .GetAsync<PendingDateAwaitTenantIndex>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(tenant), cancellationToken)
            .ConfigureAwait(false);
        if (entry.Value is null || entry.Value.Entries.Count == 0)
        {
            return [];
        }

        var pending = new List<PendingDateAwait>();
        foreach (string workItemId in entry.Value.Entries.Keys)
        {
            // The index only tells us which aggregates to inspect; the stream is authoritative. Any failed
            // candidate aborts this tenant's scan and propagates so the caller (GetPendingDateAwaitsAsync)
            // isolates the failure to this tenant, logs it, and keeps scanning the remaining tenants rather
            // than treating one unreadable stream as reason to abandon the whole cross-tenant pass.
            pending.AddRange(await PendingDateAwaitStreamReader
                .RebuildAsync(_gateway, tenant, workItemId, _options.MaxStreamPagesPerTenant, cancellationToken)
                .ConfigureAwait(false));
        }

        return pending;
    }
}
