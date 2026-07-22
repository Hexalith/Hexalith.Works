namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Durable, tenant-scoped identity of one incomplete parent-terminal cascade checkpoint.
/// </summary>
public sealed record CascadeCheckpointIdentity(
    string TenantId,
    string ParentWorkItemId,
    string ParentTerminalEventType);
