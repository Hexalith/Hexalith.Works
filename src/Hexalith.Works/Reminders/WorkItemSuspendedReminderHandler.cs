using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Steady-state trigger for durable date-reminder registration (Story 4.8, AC #1). Consumes the live
/// <c>work.events</c> subscription's <see cref="WorkItemSuspended"/> deliveries (Story 4.7's surface) and registers
/// a self-targeted durable Dapr reminder for each currently-pending <c>DateReached</c> await, so a date-suspended
/// item resumes when the date fires without any host restart or reconciliation pass.
/// </summary>
/// <remarks>
/// <para>Registration is derived from the folded <em>current</em> pending set — rebuilt from the aggregate's
/// per-aggregate stream through the pure <see cref="PendingDateAwaitProjection"/> (DD-1) — never from the raw
/// suspend event in isolation: a suspend redelivered after the item already resumed re-folds to an empty set and
/// registers nothing. Registration is idempotent (deterministic <see cref="DateReminderName"/>; same-name
/// re-registration overwrites in place), so the subscription's at-least-once redelivery is safe. Failures
/// propagate so the processor releases its marker and redelivers, retrying the idempotent registration.</para>
/// </remarks>
internal sealed class WorkItemSuspendedReminderHandler(
    IEventStoreGatewayClient gateway,
    IDateReminderScheduler scheduler,
    TimeProvider timeProvider,
    IOptions<WorksRecoveryOptions> options,
    ILogger<WorkItemSuspendedReminderHandler> logger) : IEventStoreDomainEventHandler<WorkItemSuspended>
{
    private readonly IEventStoreGatewayClient _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    private readonly ILogger<WorkItemSuspendedReminderHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly IDateReminderScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc/>
    public async Task HandleAsync(
        WorkItemSuspended @event,
        EventStoreDomainEventContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<PendingDateAwait> pending = await PendingDateAwaitStreamReader
            .RebuildAsync(_gateway, @event.TenantId.Value, @event.WorkItemId.Value, _options.MaxStreamPagesPerTenant, cancellationToken)
            .ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (PendingDateAwait pendingAwait in pending)
        {
            TimeSpan dueTime = pendingAwait.Instant > now ? pendingAwait.Instant - now : TimeSpan.Zero;
            await _scheduler.ScheduleResumeReminderAsync(pendingAwait, dueTime, cancellationToken).ConfigureAwait(false);
            WorksRecoveryLog.DateReminderScheduled(
                _logger,
                pendingAwait.TenantId,
                pendingAwait.WorkItemId,
                DateReminderName.For(pendingAwait.TenantId, pendingAwait.WorkItemId, pendingAwait.CorrelationKey));
        }
    }
}
