using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.Works.Projections;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hexalith.Works.Recovery;

/// <summary>Discovers every retained Works member, including terminal and expiry-only streams.</summary>
internal sealed class WorksProcessSourceCatalog(SharedProjectionEpochCoordinator coordinator,
    ILogger<WorksProcessSourceCatalog>? logger = null) : IProcessSourceCatalog, IReminderTargetCatalog
{
    private readonly SharedProjectionEpochCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly ILogger<WorksProcessSourceCatalog> _logger = logger ?? NullLogger<WorksProcessSourceCatalog>.Instance;

    /// <inheritdoc/>
    public async Task<ProcessSourcePage> ListAsync(string? afterKey, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 256);
        SharedProjectionControlIndexReadResult membershipRead;
        try
        {
            membershipRead = await _coordinator.GetAvailableControlIndexTenantsAsync(
                WorksReadModelKeys.StateStoreName, WorksReadModelKeys.RuntimeControlIndex, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            WorksFailureDiagnostics.DiscoveryIncomplete(_logger, string.Empty, "control-index-" + WorksFailureDiagnostics.Category(exception));
            throw;
        }
        var sources = new SortedDictionary<string, ProcessSource>(StringComparer.Ordinal);
        bool incomplete = membershipRead.IsIncomplete;
        if (incomplete) WorksFailureDiagnostics.DiscoveryIncomplete(_logger, string.Empty, "control-index-unavailable");
        foreach (string[] batch in membershipRead.Tenants.Chunk(16))
        {
            var results = await Task.WhenAll(batch.Select(async tenant =>
            {
                try
                {
                    WorksReadModelKeys.ThrowIfReservedTenantId(tenant);
                    var scope = new SharedProjectionScope(WorksReadModelKeys.StateStoreName, tenant, "work",
                        WorksReadModelKeys.RuntimeFamily, ["works-runtime-v3-writer"]);
                    SharedProjectionReadResult<WorksRuntimeDocument> read = await _coordinator.ReadDiscoveryAsync<WorksRuntimeDocument>(
                        scope, WorksReadModelKeys.RuntimeQueueKey, cancellationToken).ConfigureAwait(false);
                    if (!read.IsAvailable || read.IsDegraded || read.IsStale || read.Value is null)
                    {
                        WorksFailureDiagnostics.DiscoveryIncomplete(_logger, tenant, "projection-unavailable");
                        return (Members: Array.Empty<ProcessSource>(), Incomplete: true);
                    }
                    WorksWhatsNextTenantIndex membership = read.Value.As<WorksWhatsNextTenantIndex>();
                    if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(membership))
                        throw new InvalidOperationException("Works process source membership is invalid.");
                    var memberIds = new HashSet<string>(membership.MemberWorkItemIds, StringComparer.Ordinal);
                    if (membership.Items.Keys.Any(id => !memberIds.Contains(id))
                        || membership.LastSequences.Keys.Any(id => !memberIds.Contains(id)))
                        throw new InvalidOperationException("Works process source membership omits retained evidence.");
                    ProcessSource[] members = [.. membership.MemberWorkItemIds.Select(aggregate => new ProcessSource(tenant, "work", aggregate))];
                    foreach (ProcessSource source in members) source.Validate();
                    if (members.Select(source => source.CatalogKey).Distinct(StringComparer.Ordinal).Count() != members.Length)
                        throw new InvalidOperationException("Works process source membership is duplicated.");
                    return (Members: members, Incomplete: false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    WorksFailureDiagnostics.DiscoveryIncomplete(_logger, tenant, WorksFailureDiagnostics.Category(exception));
                    // No part of an invalid family escapes. SDK discovery retains incompleteness for retry.
                    return (Members: Array.Empty<ProcessSource>(), Incomplete: true);
                }
            })).ConfigureAwait(false);
            foreach (var result in results)
            {
                incomplete |= result.Incomplete;
                foreach (ProcessSource source in result.Members) sources.Add(source.CatalogKey, source);
            }
        }

        ProcessSource[] remaining = [.. sources.Values.Where(source => afterKey is null
            || string.CompareOrdinal(source.CatalogKey, afterKey) > 0).Take(limit + 1)];
        ProcessSource[] page = [.. remaining.Take(limit)];
        return new ProcessSourcePage(page, remaining.Length > limit ? page[^1].CatalogKey : null) { IsIncomplete = incomplete };
    }
}
