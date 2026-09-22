using Hexalith.Works.Runtime;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Runs one durable-index cascade recovery pass when the Works host starts.
/// </summary>
/// <remarks>
/// A pass that throws is logged as <c>startup-cascade-recovery</c> and is not retried in-process.
/// The durable incomplete index is replayed on the next process start. A single checkpoint that fails
/// inside <see cref="CascadeRecoveryReconciler"/> is logged as <c>startup-cascade-replay</c> and left
/// on that index; the rest of the pass continues.
/// </remarks>
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
