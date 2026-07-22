using System.Text.Json;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Pure construction of the <see cref="ResumeWorkItem"/> submission that a fired date reminder (or a
/// reconciliation pass on an already-due await) issues into the EventStore command path (Story 4.6, AC
/// #1/#3). The aggregate receives the deterministic <c>DateReached</c> await condition and decides
/// acceptance; it never reads a clock. The carried correlation/causation id is deterministic from the
/// reminder identity, so a redelivered firing dedups at the substrate and no-ops at the aggregate after the
/// first <c>WorkItemResumed</c>.
/// </summary>
public static class DateResume
{
    /// <summary>Builds the deterministic <see cref="WorkCommandSubmission"/> for a date-based resume.</summary>
    /// <param name="tenantId">The tenant id (raw value).</param>
    /// <param name="workItemId">The work item id (raw value).</param>
    /// <param name="instant">The awaited instant; normalized to UTC by <see cref="AwaitCondition.DateReached"/>.</param>
    public static WorkCommandSubmission BuildSubmission(string tenantId, string workItemId, DateTimeOffset instant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        var tenant = new TenantId(tenantId);
        var workItem = new WorkItemId(workItemId);
        AwaitCondition condition = AwaitCondition.DateReached(instant);
        var command = new ResumeWorkItem(tenant, workItem, condition);

        string id = $"date-resume-{DateReminderName.For(tenant.Value, workItem.Value, condition.CorrelationKey)}";

        return new WorkCommandSubmission(
            Tenant: tenant.Value,
            AggregateId: workItem.Value,
            CommandType: nameof(ResumeWorkItem),
            Payload: JsonSerializer.SerializeToElement(command),
            CorrelationId: id,
            CausationId: id);
    }
}
