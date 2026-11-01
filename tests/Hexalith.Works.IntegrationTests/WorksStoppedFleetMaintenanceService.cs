using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Reconciles only a fully stopped test-owned predecessor through the actual prepared successor.</summary>
internal sealed class WorksStoppedFleetMaintenanceService(IConfiguration configuration, IRouteCatalogStateStore store,
    IRouteCatalogAuthority authority, IOptions<RouteCatalogHostOptions> options, WorksStoppedFleetVerifier verifier) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string path = Path.Combine(Path.GetDirectoryName(configuration[WorksFleetQuiescence.OwnerEnvironment]!)!, WorksFleetQuiescence.ProofName);
        if (!File.Exists(path)) return; // Initial fresh isolated composition has no retired fleet.
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (options.Value.Identity is not null)
            {
                try
                {
                    var state = (await store.ReadAsync(stoppingToken).ConfigureAwait(false)).State;
                    if (state.Leases.Count == 0) return; // No proof is needed; bootstrap may already have committed.
                    if (state.Pending is not null)
                    {
                        var verified = await verifier.ReadAsync(stoppingToken).ConfigureAwait(false);
                        // Validate the COMPLETE remaining subset before removing even the first member.
                        foreach (var fence in verified.State.Leases)
                            await authority.ReconcileAsync(new RouteCatalogQuiescenceRequest(fence.LeaseId, fence.RootDigest, fence.Epoch,
                                WorksFleetQuiescence.FenceProof(verified.Owner, verified.Document, fence)), stoppingToken).ConfigureAwait(false);
                        return;
                    }
                }
                catch (RouteCatalogUnavailableException exception) when (exception.Category == RouteCatalogRefusalReason.Backend) { }
            }
            await Task.Delay(50, stoppingToken).ConfigureAwait(false);
        }
    }
}
