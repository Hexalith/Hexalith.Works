using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;

namespace Hexalith.Works.Recovery;

/// <summary>Obtains a fresh delegation from the host authority for each exact process submission.</summary>
internal sealed class WorksProcessDelegationProvider(IReminderDelegationTokenProvider? authority = null) : IProcessDelegationProvider
{
    /// <inheritdoc/>
    public async Task<TrustedEffectContext> CreateAsync(TrustedEffectSubmission submission, string causationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(causationId);
        string purpose = WorksEffectPurposes.ForKind(submission.Identity.EffectKind);
        if (authority is null) throw new InvalidOperationException("Works delegated effect authority is not configured.");
        string? token = await authority.GetDelegationTokenAsync(new ReminderDelegationRequest(
            submission, WorksEffectPurposes.Workload, purpose, causationId), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Works delegated effect authority is unavailable.");
        return new TrustedEffectContext(WorksEffectPurposes.Workload, purpose, causationId, token);
    }
}
