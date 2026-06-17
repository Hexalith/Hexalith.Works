namespace Hexalith.Works.Reminders;

/// <summary>
/// A single still-pending <c>DateReached</c> await for one work item, reconstructed from its event stream.
/// It carries only bounded identifiers — the tenant id, the work item id, the UTC instant, and the
/// deterministic await <c>CorrelationKey</c> (the round-trip instant string) — so the reminder runtime can
/// re-register the deterministic reminder and, when the instant is already due, reissue an idempotent
/// <c>ResumeWorkItem</c> without ever reading a clock inside the kernel.
/// </summary>
public sealed record PendingDateAwait(
    string TenantId,
    string WorkItemId,
    DateTimeOffset Instant,
    string CorrelationKey);
