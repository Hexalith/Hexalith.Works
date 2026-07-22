using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.Works.Contracts.Events;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Mechanically forwards a consumed parent cancellation to the existing durable cascade dispatcher.
/// </summary>
internal sealed class WorkItemCancelledCascadeHandler(CascadeDispatcher dispatcher)
    : IEventStoreDomainEventHandler<WorkItemCancelled>
{
    private readonly CascadeDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc/>
    public Task HandleAsync(
        WorkItemCancelled @event,
        EventStoreDomainEventContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(context);
        return _dispatcher.DispatchAsync(@event, cancellationToken);
    }
}
