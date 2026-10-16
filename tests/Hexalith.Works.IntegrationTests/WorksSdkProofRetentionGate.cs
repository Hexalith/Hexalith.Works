using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Server.Commands;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Explicit synthetic retained-source policy for isolated fixture coordinates only.</summary>
internal sealed class WorksSdkProofRetentionGate : ITrustedEffectRetentionGate
{
    /// <inheritdoc/>
    public Task ValidateAsync(EffectIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!identity.Tenant.StartsWith("sdk415-", StringComparison.Ordinal) || identity.SourceDomain != "work"
            || identity.TargetDomain != "work" || identity.SourceEnvelopeSequence < 1
            || !identity.SourceAggregate.StartsWith("work-sdk415-", StringComparison.Ordinal)
            || !identity.TargetAggregate.StartsWith("work-sdk415-", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Synthetic retained-source policy denied the effect.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CompleteAsync(EffectIdentity identity, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
