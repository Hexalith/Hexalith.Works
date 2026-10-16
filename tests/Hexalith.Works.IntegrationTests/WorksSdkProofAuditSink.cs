using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Server.Commands;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Persists synthetic privileged attempts before the real SDK admission mutates target state.</summary>
internal sealed class WorksSdkProofAuditSink(IReadModelStore store) : ITrustedEffectAuditSink
{
    /// <inheritdoc/>
    public async Task AppendAsync(TrustedEffectAuditRecord record, CancellationToken cancellationToken = default)
    {
        _ = await ReadModelWritePolicy.UpdateAsync<WorksSdkProofAuditDocument>(store, "statestore",
            "story415:trusted-audit:" + (record.Tenant ?? "denied"),
            prior => new WorksSdkProofAuditDocument([.. prior?.Attempts ?? [], record]),
            maxAttempts: 8, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
