using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Recovery.ChildCompletion;

/// <summary>
/// Rebuilds a completed child's parent reference and the parent's current await state from two per-aggregate streams.
/// </summary>
internal sealed class StreamReadingChildCompletionAwaitingParentSource(
    IEventStoreGatewayClient gateway,
    IOptions<WorksRecoveryOptions> options,
    ILogger<StreamReadingChildCompletionAwaitingParentSource> logger) : IChildCompletionAwaitingParentSource
{
    private const int PageSize = 200;

    private readonly IEventStoreGatewayClient _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    private readonly ILogger<StreamReadingChildCompletionAwaitingParentSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AwaitingParent>> GetAwaitingParentsAsync(
        WorkItemCompleted childCompleted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(childCompleted);

        try
        {
            IReadOnlyList<IEventPayload> childEvents = await ReadStreamAsync(
                childCompleted.TenantId.Value,
                childCompleted.WorkItemId.Value,
                cancellationToken).ConfigureAwait(false);
            ParentWorkItemReference? parent = childEvents
                .OfType<WorkItemCreated>()
                .Where(value => Matches(value.TenantId, value.WorkItemId, value.AggregateId, childCompleted.TenantId, childCompleted.WorkItemId))
                .Select(static value => value.Parent)
                .FirstOrDefault(static value => value is not null);

            if (parent is null || parent.TenantId != childCompleted.TenantId)
            {
                return [];
            }

            IReadOnlyList<IEventPayload> parentEvents = await ReadStreamAsync(
                parent.TenantId.Value,
                parent.WorkItemId.Value,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AwaitCondition> conditions = RebuildAwaitConditions(parent, parentEvents);
            return conditions.Count == 0
                ? []
                : [new AwaitingParent(parent.TenantId, parent.WorkItemId, conditions)];
        }
        catch (Exception exception)
        {
            WorksRecoveryLog.RecoveryStepFailed(_logger, "read-child-completion-awaiting-parent", exception);
            throw;
        }
    }

    private static IReadOnlyList<AwaitCondition> RebuildAwaitConditions(
        ParentWorkItemReference parent,
        IReadOnlyList<IEventPayload> events)
    {
        IReadOnlyList<AwaitCondition> conditions = [];
        foreach (IEventPayload payload in events)
        {
            switch (payload)
            {
                case WorkItemSuspended suspended
                    when Matches(suspended.TenantId, suspended.WorkItemId, suspended.AggregateId, parent.TenantId, parent.WorkItemId):
                    conditions = [.. suspended.AwaitConditions];
                    break;
                case WorkItemResumed resumed
                    when Matches(resumed.TenantId, resumed.WorkItemId, resumed.AggregateId, parent.TenantId, parent.WorkItemId):
                case WorkItemCancelled cancelled
                    when Matches(cancelled.TenantId, cancelled.WorkItemId, cancelled.AggregateId, parent.TenantId, parent.WorkItemId):
                case WorkItemExpired expired
                    when Matches(expired.TenantId, expired.WorkItemId, expired.AggregateId, parent.TenantId, parent.WorkItemId):
                case WorkItemCompleted completed
                    when Matches(completed.TenantId, completed.WorkItemId, completed.AggregateId, parent.TenantId, parent.WorkItemId):
                case WorkItemRejected rejected
                    when Matches(rejected.TenantId, rejected.WorkItemId, rejected.AggregateId, parent.TenantId, parent.WorkItemId):
                    conditions = [];
                    break;
            }
        }

        return conditions;
    }

    private static bool Matches(
        TenantId actualTenant,
        WorkItemId actualWorkItem,
        string actualAggregateId,
        TenantId expectedTenant,
        WorkItemId expectedWorkItem)
    {
        return actualTenant == expectedTenant
            && actualWorkItem == expectedWorkItem
            && string.Equals(actualAggregateId, expectedWorkItem.Value, StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<IEventPayload>> ReadStreamAsync(
        string tenantId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var events = new List<(long Sequence, IEventPayload Payload)>();
        ReplayContinuationToken? continuation = null;
        long from = 0;

        for (int page = 0; page < _options.MaxStreamPagesPerTenant; page++)
        {
            var request = new StreamReadRequest(
                Tenant: tenantId,
                Domain: WorkCommandSubmission.WorkDomain,
                AggregateId: workItemId,
                FromSequence: from,
                ContinuationToken: continuation,
                PageSize: PageSize);
            StreamReadPage result = await _gateway.ReadStreamAsync(request, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(result.Tenant, tenantId, StringComparison.Ordinal)
                || !string.Equals(result.AggregateId, workItemId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("EventStore returned a stream outside the requested identity.");
            }

            foreach (StreamReadEvent streamEvent in result.Events)
            {
                IEventPayload? payload = WorksEventDecoder.Decode(streamEvent.EventTypeName, streamEvent.Payload);
                if (payload is not null)
                {
                    events.Add((streamEvent.SequenceNumber, payload));
                }
            }

            if (!result.Metadata.IsTruncated)
            {
                break;
            }

            continuation = result.Metadata.NextContinuationToken;
            from = result.Metadata.LastSequenceReturned is { } lastSequence ? lastSequence + 1 : from;
        }

        return [.. events.OrderBy(static value => value.Sequence).Select(static value => value.Payload)];
    }
}
