using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Reactor;

/// <summary>
/// Caller-supplied descendant candidate for a terminal cascade (Story 3.6). It carries only the minimal
/// facts the pure <see cref="TerminalCascadeTranslator"/> needs to select a target: the descendant
/// <see cref="TenantId"/> (for fail-closed tenant equality, D3), its <see cref="WorkItemId"/>, and
/// whether it is already terminal so an explicitly-terminal candidate can be skipped (D2). It carries no
/// EventStore envelope, Dapr metadata, checkpoint state, parent status decision, roll-up total, Party
/// data, or adapter detail — tenant-safe descendant discovery is performed by the caller, not here (D4).
/// </summary>
public sealed record CascadeDescendant(
    TenantId TenantId,
    WorkItemId WorkItemId,
    bool IsTerminal);
