using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Projections;

namespace Hexalith.Works.Runtime;

/// <summary>Maps committed Works lifecycle facts to SDK-owned process and reminder discovery.</summary>
/// <typeparam name="TEvent">The declared Works lifecycle fact.</typeparam>
internal sealed class WorksRecoveryWakeupHandler<TEvent>(ProcessCheckpointStore processes, IReminderRegistrar reminders)
    : IEventStoreDomainEventHandler<TEvent> where TEvent : IEventPayload
{
    /// <inheritdoc/>
    public async Task HandleAsync(TEvent @event, EventStoreDomainEventContext context, CancellationToken cancellationToken = default)
    {
        if (context.Domain != "work" || !WorksEventIdentity.Matches(@event, context.TenantId, context.AggregateId))
            throw new InvalidOperationException("Works recovery wakeup does not match the committed stream identity.");
        WorksReadModelKeys.ThrowIfReservedTenantId(context.TenantId);
        var source = new ProcessSource(context.TenantId, "work", context.AggregateId);
        source.Validate();
        await processes.EnsureCandidateAsync(source, cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await reminders.ConvergeAsync(new ReminderTarget(context.TenantId, "work", context.AggregateId), cancellationToken).ConfigureAwait(false);
    }
}
