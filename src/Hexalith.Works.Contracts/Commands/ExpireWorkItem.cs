using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Terminal expiry: accepted from any non-terminal status (→ <c>Expired</c>). The command is the
/// adapter-fired signal — handling reads no clock (deadlines are advisory-until-fired). TTL/date
/// sourcing and the scheduled signal that fires this command are out of scope here (Story 4.6).
/// </summary>
public sealed record ExpireWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId);
