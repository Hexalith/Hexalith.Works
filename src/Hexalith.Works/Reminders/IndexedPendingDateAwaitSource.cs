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
        foreach (string tenant in registry.Tenants)
        {
            pending.AddRange(await ScanTenantAsync(tenant, cancellationToken).ConfigureAwait(false));
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
            // candidate makes the scan incomplete and is propagated so startup reconciliation retries the whole,
            // idempotent pass instead of treating a partial result as success.
            pending.AddRange(await PendingDateAwaitStreamReader
                .RebuildAsync(_gateway, tenant, workItemId, _options.MaxStreamPagesPerTenant, cancellationToken)
                .ConfigureAwait(false));
        }

        return pending;
    }
}
