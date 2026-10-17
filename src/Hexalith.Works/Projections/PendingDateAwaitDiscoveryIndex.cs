using Hexalith.EventStore.Client.Projections;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Projections;

/// <summary>
/// Writes the tenant registry and pending-date-await index that recovery uses to discover parked date awaits.
/// </summary>
internal static class PendingDateAwaitDiscoveryIndex
{
    /// <summary>
    /// Records one aggregate's current pending date awaits when the sequence is newer than the stored watermark.
    /// </summary>
    public static async Task RecordAsync(
        IReadModelStore store,
        ILogger logger,
        string tenantId,
        string aggregateId,
        long sequence,
        IReadOnlyList<PendingDateAwait> pending,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentNullException.ThrowIfNull(pending);
        if (sequence <= 0 || pending.Count == 0)
        {
            return;
        }

        WorksReadModelKeys.ThrowIfReservedTenantId(tenantId);
        await EnsureTenantRegisteredAsync(store, logger, tenantId, cancellationToken).ConfigureAwait(false);
        await UpsertIndexAsync(store, logger, tenantId, aggregateId, sequence, pending, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureTenantRegisteredAsync(
        IReadModelStore store,
        ILogger logger,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ReadModelEntry<PendingDateAwaitTenantRegistry> current = await store
            .GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (current.Value is not null && current.Value.Tenants.Contains(tenantId))
        {
            return;
        }

        ReadModelWriteContext context = new(
            Category: "works pending-date-await tenant registry",
            ProjectionType: WorksReadModelKeys.PendingDateAwaitRegistryProjectionType);
        _ = await ReadModelWritePolicy.UpdateAsync<PendingDateAwaitTenantRegistry>(
            store,
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitRegistryKey,
            existing =>
            {
                var tenants = existing is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(existing.Tenants, StringComparer.Ordinal);
                _ = tenants.Add(tenantId);
                return new PendingDateAwaitTenantRegistry { Tenants = tenants };
            },
            context,
            logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertIndexAsync(
        IReadModelStore store,
        ILogger logger,
        string tenantId,
        string aggregateId,
        long sequence,
        IReadOnlyList<PendingDateAwait> pending,
        CancellationToken cancellationToken)
    {
        ReadModelWriteContext context = new(
            Category: "works pending-date-await index",
            ProjectionType: WorksReadModelKeys.PendingDateAwaitIndexProjectionType);
        _ = await ReadModelWritePolicy.UpdateAsync<PendingDateAwaitTenantIndex>(
            store,
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(tenantId),
            current =>
            {
                PendingDateAwaitTenantIndex index = current ?? new PendingDateAwaitTenantIndex();
                var lastSequences = new Dictionary<string, long>(index.LastSequences, StringComparer.Ordinal);
                var entries = new Dictionary<string, IReadOnlyList<PendingDateAwait>>(index.Entries, StringComparer.Ordinal);
                if (lastSequences.TryGetValue(aggregateId, out long storedLastSequence)
                    && storedLastSequence >= sequence)
                {
                    return new PendingDateAwaitTenantIndex { Entries = entries, LastSequences = lastSequences };
                }

                lastSequences[aggregateId] = sequence;
                entries[aggregateId] = pending;
                return new PendingDateAwaitTenantIndex { Entries = entries, LastSequences = lastSequences };
            },
            context,
            logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
