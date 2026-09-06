using System.Reflection;

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

        if (!string.Equals(payloadTenant.Value, tenantId, StringComparison.Ordinal)
            || !string.Equals(payloadWorkItem.Value, aggregateId, StringComparison.Ordinal))
        {
            return false;
        }

        PropertyInfo? aggregateIdProperty = type.GetProperty("AggregateId");
        if (aggregateIdProperty is null)
        {
            // Every rejection event (IRejectionEvent) is documented to carry no AggregateId at all — its
            // TenantId/WorkItemId pair is its whole identity contract, already checked above. Any other
            // payload shape missing AggregateId fails closed instead of being silently treated as a match:
            // every known non-rejection Works lifecycle event carries the property, so its absence there
            // signals an unexpected shape this identity check must not vouch for.
            return payload is IRejectionEvent;
        }

        return string.Equals(aggregateIdProperty.GetValue(payload) as string, aggregateId, StringComparison.Ordinal);
    }
}
