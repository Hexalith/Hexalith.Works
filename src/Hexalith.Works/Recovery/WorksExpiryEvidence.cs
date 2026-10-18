using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Recovery;

/// <summary>Validates persisted witnesses before later history can hide corrupt domain evidence.</summary>
internal sealed class WorksExpiryEvidence
{
    private WorkItemExpirySchedule? _current;
    private bool _created;

    /// <summary>Validates the existing clock-free schedule transition rules in committed order.</summary>
    internal void Apply(IEventPayload payload, string tenant, string aggregate)
    {
        Validate(payload, tenant, aggregate);
        switch (payload)
        {
            case WorkItemCreated created:
                if (_created)
                    throw new InvalidOperationException("The persisted Works source contains repeated creation.");
                _created = true;
                _current = created.Expiry;
                break;
            case WorkItemRescheduled rescheduled:
                if (!WorkItemExpirySchedule.IsValidReplacement(_current, rescheduled.Expiry, new TenantId(tenant), new WorkItemId(aggregate)))
                    throw new InvalidOperationException("The persisted Works expiry replacement is invalid.");
                _current = rescheduled.Expiry;
                break;
            case WorkItemExpired { ExpiryWitness: not null } expired when expired.ExpiryWitness != _current:
                throw new InvalidOperationException("The persisted Works expiry does not match its preceding schedule.");
        }
    }

    /// <summary>Accepts historical absence and validates every non-null persisted expiry against its stream.</summary>
    internal static void Validate(IEventPayload payload, string tenant, string aggregate)
    {
        WorkItemExpirySchedule? expiry = payload switch
        {
            WorkItemCreated created => created.Expiry,
            WorkItemRescheduled rescheduled => rescheduled.Expiry,
            WorkItemExpired expired => expired.ExpiryWitness,
            _ => null,
        };
        if (expiry is not null && !expiry.IsValidFor(new TenantId(tenant), new WorkItemId(aggregate)))
            throw new InvalidOperationException("The persisted Works expiry witness is invalid.");
    }
}
