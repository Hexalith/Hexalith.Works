using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Reactor;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Host-edge at-least-once dispatcher for terminal cascades with checkpoint persistence and restart replay
/// (Story 4.6, AC #4/#5/#6). It owns delivery, checkpointing, and replay only — never a domain decision. On a
/// parent terminal event it discovers direct descendants, runs the pure <see cref="TerminalCascadeTranslator"/>
/// to get mechanical terminal command intents, records a re-readable <see cref="CascadeCheckpoint"/>, then
/// dispatches each target through <see cref="IWorkCommandSubmitter"/> so every target command round-trips
/// through <c>WorkItemAggregate.Handle</c>. The checkpoint is persisted after each target attempt, so a
/// mid-cascade restart re-reads the outstanding targets from the checkpoint (not an in-memory list),
/// skips the targets already dispatched, and converges — duplicate terminal commands are safe because the
/// aggregate no-ops an exact duplicate terminal.
/// </summary>
public sealed class CascadeDispatcher(
    ICascadeCheckpointStore checkpointStore,
    ICascadeDescendantSource descendantSource,
    IWorkCommandSubmitter submitter,
    ILogger<CascadeDispatcher> logger,
    IOptions<WorksRecoveryOptions>? options = null)
{
    private readonly ICascadeCheckpointStore _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
    private readonly ICascadeDescendantSource _descendantSource = descendantSource ?? throw new ArgumentNullException(nameof(descendantSource));
    private readonly IWorkCommandSubmitter _submitter = submitter ?? throw new ArgumentNullException(nameof(submitter));
    private readonly ILogger<CascadeDispatcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeSpan _targetInterval = ComputeTargetInterval(options, logger);

    /// <summary>
    /// Upper bound for <see cref="WorksRecoveryOptions.CascadeTargetIntervalMilliseconds"/>: a misconfigured
    /// very large value must not be able to block a dispatch (and hold the Dapr message/marker in-progress)
    /// indefinitely.
    /// </summary>
    private const int MaxTargetIntervalMilliseconds = 60_000;

    /// <summary>Dispatches the cancel cascade for a parent <see cref="WorkItemCancelled"/>.</summary>
    public async Task DispatchAsync(WorkItemCancelled parentCancelled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentCancelled);

        CascadeCheckpoint checkpoint = await EnsureCheckpointAsync(
            parentCancelled.TenantId.Value,
            parentCancelled.WorkItemId.Value,
            nameof(WorkItemCancelled),
            parentCancelled.Sequence,
            CascadeCheckpoint.CancelKind,
            descendants => [.. TerminalCascadeTranslator.ToCascadeCommands(parentCancelled, descendants).Select(static c => c.WorkItemId.Value)],
            cancellationToken).ConfigureAwait(false);

        await DriveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Dispatches the expire cascade for a parent <see cref="WorkItemExpired"/>.</summary>
    public async Task DispatchAsync(WorkItemExpired parentExpired, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentExpired);

        CascadeCheckpoint checkpoint = await EnsureCheckpointAsync(
            parentExpired.TenantId.Value,
            parentExpired.WorkItemId.Value,
            nameof(WorkItemExpired),
            parentExpired.Sequence,
            CascadeCheckpoint.ExpireKind,
            descendants => [.. TerminalCascadeTranslator.ToCascadeCommands(parentExpired, descendants).Select(static c => c.WorkItemId.Value)],
            cancellationToken).ConfigureAwait(false);

        await DriveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replays an existing checkpoint after a restart: re-reads the outstanding targets from the persisted
    /// checkpoint and dispatches the ones not yet completed. Returns <see langword="false"/> when no checkpoint
    /// exists for the supplied parent-terminal identity.
    /// </summary>
    public async Task<bool> ReplayAsync(string tenantId, string parentWorkItemId, string parentTerminalEventType, CancellationToken cancellationToken = default)
    {
        CascadeCheckpoint? checkpoint = await _checkpointStore
            .GetAsync(tenantId, parentWorkItemId, parentTerminalEventType, cancellationToken)
            .ConfigureAwait(false);

        if (checkpoint is null)
        {
            return false;
        }

        await DriveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static TimeSpan ComputeTargetInterval(IOptions<WorksRecoveryOptions>? options, ILogger<CascadeDispatcher> logger)
    {
        int configured = options?.Value.CascadeTargetIntervalMilliseconds ?? 0;
        int clamped = Math.Clamp(configured, 0, MaxTargetIntervalMilliseconds);
        if (clamped != configured)
        {
            WorksRecoveryLog.CascadeTargetIntervalClamped(logger, configured, clamped);
        }

        return TimeSpan.FromMilliseconds(clamped);
    }

    private async Task<CascadeCheckpoint> EnsureCheckpointAsync(
        string tenantId,
        string parentWorkItemId,
        string parentTerminalEventType,
        long parentTerminalSequence,
        string kind,
        Func<IReadOnlyList<CascadeDescendant>, IReadOnlyList<string>> translate,
        CancellationToken cancellationToken)
    {
        CascadeCheckpoint? existing = await _checkpointStore
            .GetAsync(tenantId, parentWorkItemId, parentTerminalEventType, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Duplicate/redelivered parent terminal event: reuse the durable checkpoint, never re-discover.
            return existing;
        }

        IReadOnlyList<CascadeDescendant> descendants = await _descendantSource
            .GetDescendantsAsync(tenantId, parentWorkItemId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<string> targetIds = translate(descendants);
        IReadOnlyList<CascadeTargetCheckpoint> targets =
        [
            .. targetIds.Select(id => new CascadeTargetCheckpoint(
                id,
                kind,
                CascadeTargetStatus.Pending,
                CascadeCommands.CorrelationId(tenantId, parentWorkItemId, parentTerminalSequence, id, kind))),
        ];

        var checkpoint = new CascadeCheckpoint(
            tenantId,
            parentWorkItemId,
            parentTerminalEventType,
            parentTerminalSequence,
            targets,
            Completed: targets.Count == 0);

        await _checkpointStore.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        WorksRecoveryLog.CascadeCheckpointed(_logger, tenantId, parentWorkItemId, targets.Count);
        return checkpoint;
    }

    private async Task DriveAsync(CascadeCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        if (checkpoint.Completed)
        {
            WorksRecoveryLog.CascadeReplayResumed(_logger, checkpoint.TenantId, checkpoint.ParentWorkItemId, 0);
            return;
        }

        var targets = checkpoint.Targets.ToList();
        int outstanding = targets.Count(static t => t.Status != CascadeTargetStatus.Completed);
        WorksRecoveryLog.CascadeReplayResumed(_logger, checkpoint.TenantId, checkpoint.ParentWorkItemId, outstanding);

        for (int i = 0; i < targets.Count; i++)
        {
            CascadeTargetCheckpoint target = targets[i];
            if (target.Status == CascadeTargetStatus.Completed)
            {
                // Already dispatched in a prior pass: skip so an already-terminal descendant is not re-terminated.
                continue;
            }

            // Persist the in-flight attempt before submitting (the documented safe boundary): if the process
            // stops after the submit but before the completion write, replay re-submits the same idempotent
            // terminal command, which the aggregate no-ops.
            targets[i] = target with { Status = CascadeTargetStatus.Attempted };
            checkpoint = checkpoint with { Targets = [.. targets] };
            await _checkpointStore.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);

            WorkCommandSubmission submission = CascadeCommands.BuildSubmission(
                checkpoint.TenantId,
                target.DescendantWorkItemId,
                target.Kind,
                target.CorrelationId);
            await _submitter.SubmitAsync(submission, cancellationToken).ConfigureAwait(false);
            WorksRecoveryLog.CascadeTargetDispatched(_logger, checkpoint.TenantId, checkpoint.ParentWorkItemId, target.DescendantWorkItemId, target.Kind);

            targets[i] = targets[i] with { Status = CascadeTargetStatus.Completed };
            checkpoint = checkpoint with { Targets = [.. targets] };
            await _checkpointStore.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);

            if (_targetInterval > TimeSpan.Zero
                && targets.Skip(i + 1).Any(static value => value.Status != CascadeTargetStatus.Completed))
            {
                await Task.Delay(_targetInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        checkpoint = checkpoint with { Completed = true };
        await _checkpointStore.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
    }
}
