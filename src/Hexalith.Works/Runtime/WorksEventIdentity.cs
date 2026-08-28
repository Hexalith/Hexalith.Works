using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Runtime;

/// <summary>Validates the common stream identity carried by Works success and rejection events.</summary>
internal static class WorksEventIdentity
{
    public static bool Matches(IEventPayload payload, string tenantId, string aggregateId)
    {
        Type type = payload.GetType();
        if (type.GetProperty(nameof(TenantId))?.GetValue(payload) is not TenantId payloadTenant
            || type.GetProperty(nameof(WorkItemId))?.GetValue(payload) is not WorkItemId payloadWorkItem)
        {
            return false;
        }

        object? payloadAggregate = type.GetProperty("AggregateId")?.GetValue(payload);
        return string.Equals(payloadTenant.Value, tenantId, StringComparison.Ordinal)
            && string.Equals(payloadWorkItem.Value, aggregateId, StringComparison.Ordinal)
            && (payloadAggregate is null || string.Equals(payloadAggregate as string, aggregateId, StringComparison.Ordinal));
    }
}
