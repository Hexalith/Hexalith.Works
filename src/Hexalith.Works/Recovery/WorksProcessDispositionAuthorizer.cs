using System.Security.Claims;

using Hexalith.EventStore.Client.Processes;

namespace Hexalith.Works.Recovery;

/// <summary>Denies disposition until the host supplies current authenticated domain authority.</summary>
internal sealed class WorksProcessDispositionAuthorizer : IProcessDispositionAuthorizer
{
    /// <inheritdoc/>
    public Task<bool> AuthorizeAsync(ClaimsPrincipal principal, ProcessSource source, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
