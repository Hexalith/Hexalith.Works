using System.Runtime.Serialization;

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
[DataContract]
public sealed record DateReminderRegistration(
    [property: DataMember(Name = "TenantId", Order = 1, IsRequired = true)] string TenantId,
    [property: DataMember(Name = "WorkItemId", Order = 2, IsRequired = true)] string WorkItemId,
    [property: DataMember(Name = "Instant", Order = 3, IsRequired = true)] DateTimeOffset Instant,
    [property: DataMember(Name = "CorrelationKey", Order = 4, IsRequired = true)] string CorrelationKey,
    [property: DataMember(Name = "DueTimeMilliseconds", Order = 5, IsRequired = true)] double DueTimeMilliseconds);
