using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Ports;

/// <summary>
/// Executor-selection seam: selects which <see cref="ExecutorBinding"/> should handle a work item.
/// </summary>
/// <remarks>
/// Abstraction only. No implementation, selection engine, scoring model, or escalation policy ships in
/// v1 — this seam is deferred to a later theme. The contracts package must not require any selection
/// engine, LLM, or cost-governance backend, and an architecture-fitness test asserts that no concrete
/// type in the Works kernel implements this port in v1.
/// </remarks>
public interface IExecutorRouter
{
    ValueTask<ExecutorBinding?> SelectExecutorAsync(
        TenantId tenantId,
        WorkItemId workItemId,
        CancellationToken cancellationToken = default);
}
