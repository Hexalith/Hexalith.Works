using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Replays every cascade checkpoint discovered through the durable incomplete index.
/// </summary>
public sealed class CascadeRecoveryReconciler(
    ICascadeCheckpointIndex index,
    CascadeDispatcher dispatcher,
    ILogger<CascadeRecoveryReconciler> logger)
{
    private readonly CascadeDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    private readonly ICascadeCheckpointIndex _index = index ?? throw new ArgumentNullException(nameof(index));
    private readonly ILogger<CascadeRecoveryReconciler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Replays the current index snapshot and returns the number of checkpoints completed.</summary>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CascadeCheckpointIdentity> entries = await _index
            .GetIncompleteAsync(cancellationToken)
            .ConfigureAwait(false);
        int completed = 0;
        foreach (CascadeCheckpointIdentity identity in entries)
        {
            try
            {
                bool replayed = await _dispatcher
                    .ReplayAsync(
                        identity.TenantId,
                        identity.ParentWorkItemId,
                        identity.ParentTerminalEventType,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (replayed)
                {
                    await _index.RemoveIncompleteAsync(identity, cancellationToken).ConfigureAwait(false);
                    completed++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                WorksRecoveryLog.RecoveryStepFailed(_logger, "startup-cascade-replay", exception);
            }
        }

        return completed;
    }
}
