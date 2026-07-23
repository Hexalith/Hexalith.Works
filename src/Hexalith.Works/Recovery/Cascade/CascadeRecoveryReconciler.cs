using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Replays every cascade checkpoint discovered through the durable incomplete index.
/// </summary>
public sealed class CascadeRecoveryReconciler(
    ICascadeCheckpointIndex index,
    CascadeDispatcher dispatcher,
    TimeProvider timeProvider,
    IOptions<WorksRecoveryOptions> options,
    ILogger<CascadeRecoveryReconciler> logger)
{
    private readonly CascadeDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    private readonly ICascadeCheckpointIndex _index = index ?? throw new ArgumentNullException(nameof(index));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<CascadeRecoveryReconciler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // TimeSpan.FromHours overflows above ~2.56e8 hours; clamp well below that so a misconfigured huge value reads
    // as "effectively never prune" instead of throwing OverflowException and aborting the entire startup pass.
    private const int MaxStaleAfterHours = 24 * 365 * 1000;

    /// <summary>Replays the current index snapshot and returns the number of checkpoints completed.</summary>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CascadeCheckpointIndexEntry> entries = await _index
            .GetIncompleteAsync(cancellationToken)
            .ConfigureAwait(false);
        int completed = 0;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan staleAfter = TimeSpan.FromHours(Math.Clamp(_options.CascadeCheckpointIndexStaleAfterHours, 0, MaxStaleAfterHours));
        foreach (CascadeCheckpointIndexEntry entry in entries)
        {
            try
            {
                bool replayed = await _dispatcher
                    .ReplayAsync(
                        entry.Identity.TenantId,
                        entry.Identity.ParentWorkItemId,
                        entry.Identity.ParentTerminalEventType,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (replayed)
                {
                    await _index.RemoveIncompleteAsync(entry.Identity, cancellationToken).ConfigureAwait(false);
                    completed++;
                }
                else if (now - entry.AddedAt > staleAfter)
                {
                    // No checkpoint was ever written for this identity (the documented crash window between
                    // index-add and checkpoint-write, ReadModelCascadeCheckpointStore.SaveAsync), and it has
                    // been stale long enough that a still-in-flight dispatch is implausible: prune it instead
                    // of retrying and no-oping on every future startup forever.
                    await _index.RemoveIncompleteAsync(entry.Identity, cancellationToken).ConfigureAwait(false);
                    WorksRecoveryLog.CascadeIndexEntryPruned(_logger, entry.Identity.TenantId, entry.Identity.ParentWorkItemId);
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
