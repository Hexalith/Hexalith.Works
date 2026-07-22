using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Reactor;

namespace Hexalith.Works.Recovery.ChildCompletion;

/// <summary>
/// Re-reads the parent relationship and current parent await state for a completed child.
/// </summary>
public interface IChildCompletionAwaitingParentSource
{
    /// <summary>Returns the same-tenant parent when its current stream state still carries awaits.</summary>
    Task<IReadOnlyList<AwaitingParent>> GetAwaitingParentsAsync(
        WorkItemCompleted childCompleted,
        CancellationToken cancellationToken = default);
}
