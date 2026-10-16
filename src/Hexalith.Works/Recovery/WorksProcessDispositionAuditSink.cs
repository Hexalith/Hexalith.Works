using System.Security.Claims;

using Hexalith.EventStore.Client.Processes;

namespace Hexalith.Works.Recovery;

/// <summary>Refuses disposition without a host-provided durable audit sink.</summary>
internal sealed class WorksProcessDispositionAuditSink : IProcessDispositionAuditSink
{
    /// <inheritdoc/>
    public Task RecordAsync(ClaimsPrincipal principal, ProcessSource source, string reason, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Works process disposition has no configured durable audit authority.");
}
