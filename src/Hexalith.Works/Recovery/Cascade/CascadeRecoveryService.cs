using Hexalith.Works.Runtime;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Runs one durable-index cascade recovery pass when the Works host starts.
/// </summary>
public sealed class CascadeRecoveryService(
    CascadeRecoveryReconciler reconciler,
    ILogger<CascadeRecoveryService> logger) : BackgroundService
{
    private readonly CascadeRecoveryReconciler _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    private readonly ILogger<CascadeRecoveryService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _ = await _reconciler.RecoverAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown is a safe replay boundary; the durable index drives the next startup pass.
        }
        catch (Exception exception)
        {
            WorksRecoveryLog.RecoveryStepFailed(_logger, "startup-cascade-recovery", exception);
        }
    }
}
