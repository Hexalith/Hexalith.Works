using Hexalith.Works.Reactor;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Supplies the direct descendant candidates of a parent work item for a terminal cascade (Story 4.6). The
/// pure <see cref="TerminalCascadeTranslator"/> takes caller-supplied descendants; this seam is that caller.
/// Only <em>direct</em> children are needed: when a child is terminated, its own terminal event drives the
/// next cascade level, so the subtree converges by event propagation rather than an in-runtime tree walk.
/// The deterministic test lane fakes this seam; production reads the parent stream under Aspire.
/// </summary>
public interface ICascadeDescendantSource
{
    /// <summary>Returns the direct descendant candidates of <paramref name="parentWorkItemId"/> in <paramref name="tenantId"/>.</summary>
    Task<IReadOnlyList<CascadeDescendant>> GetDescendantsAsync(string tenantId, string parentWorkItemId, CancellationToken cancellationToken = default);
}
