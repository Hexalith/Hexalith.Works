using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Reminders;

namespace Hexalith.Works.Reminders;

/// <summary>Converges SDK reminders by refolding the authoritative target after a committed event wakeup.</summary>
internal sealed class WorksReminderEventHandler(IReminderRegistrar registrar)
{
    private readonly IReminderRegistrar _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));

    internal Task<ReminderConvergenceResult> HandleAsync(EventStoreDomainEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Domain != "work") throw new InvalidOperationException("Works reminder wakeup domain is invalid.");
        return _registrar.ConvergeAsync(new ReminderTarget(envelope.TenantId, "work", envelope.AggregateId), cancellationToken);
    }
}
