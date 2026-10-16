using System.Text.Json;

using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;

namespace Hexalith.Works.Reminders;

/// <summary>Refolds committed date awaits and upstream-resolved expiry witnesses for the SDK reminder runtime.</summary>
internal sealed class WorksReminderIntentSource(WorksSourceStreamReader streams) : IReminderIntentSource
{
    private readonly WorksSourceStreamReader _streams = streams ?? throw new ArgumentNullException(nameof(streams));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReminderIntent>> GetCurrentIntentsAsync(ReminderTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Domain != "work") throw new InvalidOperationException("Works reminder target domain is invalid.");
        IReadOnlyList<WorksCommittedEvent> events = await _streams.ReadAsync(target.Tenant, target.Aggregate, cancellationToken).ConfigureAwait(false);
        WorksCommittedEvent? suspension = null;
        WorksCommittedEvent? expirySource = null;
        WorkItemExpirySchedule? expiry = null;
        bool terminal = false;
        foreach (WorksCommittedEvent fact in events)
        {
            switch (fact.Payload)
            {
                case WorkItemCreated created:
                    expiry = created.Expiry;
                    expirySource = fact;
                    break;
                case WorkItemRescheduled rescheduled:
                    expiry = rescheduled.Expiry;
                    expirySource = fact;
                    break;
                case WorkItemSuspended:
                    suspension = fact;
                    break;
                case WorkItemResumed:
                    suspension = null;
                    break;
                case WorkItemCompleted or WorkItemCancelled or WorkItemExpired:
                case WorkItemRejected { Requeue: false }:
                    terminal = true;
                    suspension = null;
                    break;
                case WorkItemRejected:
                    suspension = null;
                    break;
            }
        }

        if (terminal) return [];
        var intents = new List<ReminderIntent>();
        if (suspension?.Payload is WorkItemSuspended suspended)
        {
            AwaitCondition[] dates = [.. suspended.AwaitConditions
                .Where(condition => condition.Kind == AwaitConditionKind.DateReached).Distinct()
                .OrderBy(condition => condition.CorrelationKey, StringComparer.Ordinal)];
            for (int ordinal = 0; ordinal < dates.Length; ordinal++)
            {
                AwaitCondition condition = dates[ordinal];
                intents.Add(new ReminderIntent(target.Tenant, "work", target.Aggregate, condition.Instant!.Value,
                    EffectKindCatalog.DateResume, nameof(ResumeWorkItem),
                    JsonSerializer.SerializeToUtf8Bytes(new ResumeWorkItem(suspended.TenantId, suspended.WorkItemId, condition)),
                    "work", target.Aggregate, suspension.Envelope.SequenceNumber, suspension.Envelope.SequenceNumber)
                { Ordinal = ordinal, SourceMessageId = suspension.Envelope.MessageId });
            }
        }

        if (expiry is not null)
        {
            if (!expiry.IsValidFor(new TenantId(target.Tenant), new WorkItemId(target.Aggregate)))
                throw new InvalidOperationException("The persisted Works expiry witness is invalid.");
            intents.Add(new ReminderIntent(target.Tenant, "work", target.Aggregate, expiry.EffectiveExpiryUtc,
                EffectKindCatalog.Expiry, nameof(ExpireWorkItem),
                JsonSerializer.SerializeToUtf8Bytes(new ExpireWorkItem(new TenantId(target.Tenant), new WorkItemId(target.Aggregate), expiry)),
                "work", target.Aggregate, expirySource!.Envelope.SequenceNumber, expiry.Revision)
            { SourceMessageId = expirySource.Envelope.MessageId });
        }

        return intents;
    }

    /// <inheritdoc/>
    public ReminderCommand TranslateDueIntent(ReminderIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        string expectedType = intent.Kind switch
        {
            EffectKindCatalog.DateResume => nameof(ResumeWorkItem),
            EffectKindCatalog.Expiry => nameof(ExpireWorkItem),
            _ => throw new InvalidOperationException("Unsupported Works reminder kind."),
        };
        if (intent.PayloadType != expectedType || intent.TargetDomain != "work" || intent.SourceDomain != "work"
            || intent.TargetAggregate != intent.SourceAggregate)
            throw new InvalidOperationException("Works reminder command does not match its source witness.");
        return new ReminderCommand(expectedType, [.. intent.Payload]);
    }
}
