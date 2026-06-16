using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Reactor;

/// <summary>
/// Pure, mechanical translation of a parent terminal event into same-kind terminal command intents for
/// caller-supplied, still-active descendants (Story 3.6, FR-10). Mirrors
/// <see cref="ChildCompletionResumeTranslator"/>: explicit inputs in, command intents out — no tree
/// traversal, no EventStore/projection/Dapr/file/clock read, no acceptance decision.
/// <para>
/// A parent <see cref="WorkItemCancelled"/> maps to descendant <see cref="CancelWorkItem"/> intents and a
/// parent <see cref="WorkItemExpired"/> maps to descendant <see cref="ExpireWorkItem"/> intents. Selection
/// fails closed on tenant mismatch (D3) and skips candidates explicitly marked terminal (D2), preserving
/// input order. It deliberately does NOT decide whether a target will accept the command: acceptance,
/// rejection, and idempotent no-op remain owned by <c>WorkItemAggregate.Handle</c> (D1/D4), so duplicate
/// or redelivered intents stay safe because the target terminal commands are idempotent.
/// </para>
/// </summary>
public static class TerminalCascadeTranslator
{
    /// <summary>
    /// Maps a parent <see cref="WorkItemCancelled"/> to <see cref="CancelWorkItem"/> intents for the
    /// same-tenant, still-active descendants supplied by the caller.
    /// </summary>
    public static IReadOnlyList<CancelWorkItem> ToCascadeCommands(
        WorkItemCancelled parentCancelled,
        IReadOnlyList<CascadeDescendant> descendants)
    {
        ArgumentNullException.ThrowIfNull(parentCancelled);
        ArgumentNullException.ThrowIfNull(descendants);

        List<CancelWorkItem> commands = [];
        foreach (CascadeDescendant target in SelectCascadeTargets(parentCancelled.TenantId, descendants))
        {
            commands.Add(new CancelWorkItem(target.TenantId, target.WorkItemId));
        }

        return commands;
    }

    /// <summary>
    /// Maps a parent <see cref="WorkItemExpired"/> to <see cref="ExpireWorkItem"/> intents for the
    /// same-tenant, still-active descendants supplied by the caller.
    /// </summary>
    public static IReadOnlyList<ExpireWorkItem> ToCascadeCommands(
        WorkItemExpired parentExpired,
        IReadOnlyList<CascadeDescendant> descendants)
    {
        ArgumentNullException.ThrowIfNull(parentExpired);
        ArgumentNullException.ThrowIfNull(descendants);

        List<ExpireWorkItem> commands = [];
        foreach (CascadeDescendant target in SelectCascadeTargets(parentExpired.TenantId, descendants))
        {
            commands.Add(new ExpireWorkItem(target.TenantId, target.WorkItemId));
        }

        return commands;
    }

    // Pure selection: fail-closed tenant equality (D3) and explicit terminal skip (D2), in input order.
    // No tree walk, no EventStore/projection read, no acceptance decision — those stay in the aggregate.
    private static IEnumerable<CascadeDescendant> SelectCascadeTargets(
        TenantId parentTenantId,
        IReadOnlyList<CascadeDescendant> descendants)
    {
        foreach (CascadeDescendant descendant in descendants)
        {
            ArgumentNullException.ThrowIfNull(descendant);

            // Skip a candidate the caller already knows to be terminal: the cascade should not even attempt
            // a duplicate terminal command for it. Redelivery of a non-skipped candidate remains safe
            // because the target aggregate treats a duplicate self-terminal command as an idempotent no-op.
            if (descendant.IsTerminal)
            {
                continue;
            }

            // Tenant equality fails closed: a descendant whose tenant differs from the parent terminal
            // event's tenant produces no command, even when work item ids collide across tenants (D3).
            if (descendant.TenantId != parentTenantId)
            {
                continue;
            }

            yield return descendant;
        }
    }
}
