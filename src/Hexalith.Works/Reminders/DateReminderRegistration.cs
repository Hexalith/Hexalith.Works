namespace Hexalith.Works.Reminders;

/// <summary>
/// The serializable, metadata-only payload the reminder scheduler hands to the <see cref="DateReminderActor"/>
/// so a fired reminder can rebuild the deterministic <c>ResumeWorkItem</c>. It carries bounded identifiers
/// and the awaited instant only — no obligation, command body, token, or secret.
/// </summary>
/// <param name="TenantId">The tenant id (raw value).</param>
/// <param name="WorkItemId">The work item id (raw value).</param>
/// <param name="Instant">The awaited UTC instant.</param>
/// <param name="CorrelationKey">The deterministic await correlation key (the round-trip instant string).</param>
/// <param name="DueTimeMilliseconds">The non-negative delay, in milliseconds, until the reminder should fire.</param>
public sealed record DateReminderRegistration(
    string TenantId,
    string WorkItemId,
    DateTimeOffset Instant,
    string CorrelationKey,
    double DueTimeMilliseconds);
