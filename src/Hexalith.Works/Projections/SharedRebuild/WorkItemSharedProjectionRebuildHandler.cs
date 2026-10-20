using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Reconciles the complete authoritative Works tenant inventory as one shared roll-up projection.</summary>
public sealed partial class WorkItemSharedProjectionRebuildHandler : IAsyncDomainSharedProjectionPartitionedRebuildHandler
{
    /// <summary>The named internal shared-projection route.</summary>
    public const string ProjectionTypeName = "works-shared-rollup";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private readonly IOptions<WorksProjectionOptions>? _options;
    private readonly bool _useSdkDelivery;

    /// <summary>Creates the rebuild and optional SDK epoch handler.</summary>
    public WorkItemSharedProjectionRebuildHandler(
        IOptions<WorksProjectionOptions>? options = null,
        IConfiguration? configuration = null)
    {
        _options = options;
        // SDK route discovery reads this property while Build maps endpoints. Leave validated options to
        // ValidateOnStart so an invalid projection budget fails at host startup as configured.
        _useSdkDelivery = configuration is null
            ? options?.Value.UseSdkDelivery ?? true
            : configuration.GetValue<bool?>($"{WorksProjectionOptions.SectionName}:UseSdkDelivery") ?? true;
    }

    /// <inheritdoc/>
    public bool EpochEnabled => _useSdkDelivery;

    /// <inheritdoc/>
    public bool OrdinaryDeliveryEnabled => EpochEnabled;

    /// <inheritdoc/>
    public string OrdinaryWriterId => "works-runtime-v3-writer";

    /// <inheritdoc/>
    public string? ControlIndexName => EpochEnabled ? WorksReadModelKeys.RuntimeControlIndex : null;

    /// <inheritdoc/>
    public SharedProjectionScope CreateScope(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return new SharedProjectionScope(
            WorksReadModelKeys.StateStoreName,
            tenantId,
            Domain,
            ProjectionType,
            [OrdinaryWriterId]);
    }

    /// <inheritdoc/>
    public string Domain => "work";

    /// <inheritdoc/>
    public string ProjectionType => EpochEnabled ? WorksReadModelKeys.RuntimeFamily : ProjectionTypeName;

    /// <inheritdoc/>
    public string RebuildStoreName => WorksReadModelKeys.StateStoreName;

    /// <inheritdoc/>
    public Task<DomainSharedProjectionRebuildCandidate> CreateEmptyCandidateAsync(
        DomainSharedProjectionRebuildIdentity identity,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToCandidate(new WorkItemSharedRebuildCandidateState([])));
    }

    /// <inheritdoc/>
    public Task<DomainSharedProjectionRebuildCandidate> AccumulateAsync(
        DomainSharedProjectionRebuildIdentity identity,
        DomainSharedProjectionRebuildCandidate candidate,
        ProjectionRequest aggregateHistory,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(aggregateHistory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(aggregateHistory.TenantId, identity.TenantId, StringComparison.Ordinal)
            || !string.Equals(aggregateHistory.Domain, identity.Domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The shared rebuild history is outside its tenant/domain identity.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateHistory.AggregateId);

        WorkItemSharedRebuildCandidateState state = FromCandidate(candidate);
        var history = new WorkItemSharedRebuildAggregateHistory(
            aggregateHistory.AggregateId,
            [.. aggregateHistory.Events ?? []]);

        // A re-supplied aggregate replaces its earlier history instead of folding the same stream twice: the
        // authoritative inventory names each aggregate once, so a repeat is a redelivery, not a second item.
        List<WorkItemSharedRebuildAggregateHistory> histories =
        [
            .. state.Histories.Where(retained => !string.Equals(retained.AggregateId, history.AggregateId, StringComparison.Ordinal)),
            history,
        ];
        return Task.FromResult(ToCandidate(new WorkItemSharedRebuildCandidateState(histories)));
    }

    /// <inheritdoc/>
    public Task<DomainProjectionRebuildPlan> FinalizeAsync(
        DomainSharedProjectionRebuildIdentity identity,
        DomainSharedProjectionRebuildCandidate candidate,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EpochEnabled
            ? WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity, FromCandidate(candidate), cancellationToken)
            : WorkItemSharedRebuildManifestBuilder.Build(identity, FromCandidate(candidate), cancellationToken));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReadModelBatchOperation>> FoldCatchUpAsync(
        SharedProjectionDelivery delivery,
        long generation,
        SharedProjectionEpochCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(coordinator);
        if (!EpochEnabled)
        {
            throw new InvalidOperationException("The Works SDK projection mode is disabled.");
        }

        ProjectionRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ProjectionRequest>(delivery.CanonicalPayload, s_json);
        }
        catch (JsonException)
        {
            throw FoldFailure(delivery, delivery.SourceStream, delivery.EnvelopePosition);
        }

        if (request is null)
        {
            throw FoldFailure(delivery, delivery.SourceStream, delivery.EnvelopePosition);
        }
        if (!string.Equals(request.Domain, Domain, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.TenantId)
            || string.IsNullOrWhiteSpace(request.AggregateId)
            || !string.Equals(request.AggregateId, delivery.SourceStream, StringComparison.Ordinal)
            || request.Events is not { Length: 1 }
            || request.Events[0] is null
            || request.Events[0].GlobalPosition != delivery.EnvelopePosition)
        {
            throw FoldFailure(delivery, delivery.SourceStream, delivery.EnvelopePosition);
        }

        SharedProjectionScope scope = CreateScope(request.TenantId);
        ReadModelEntry<WorksRuntimeDocument> indexEntry = await coordinator
            .ReadGenerationAsync<WorksRuntimeDocument>(scope, generation, WorksReadModelKeys.RuntimeQueueKey, cancellationToken)
            .ConfigureAwait(false);
        WorksWhatsNextTenantIndex? index = indexEntry.Value?.As<WorksWhatsNextTenantIndex>();
        if (index is not null && !WorksWhatsNextTenantIndexValidation.IsValidCurrent(index))
        {
            throw new InvalidOperationException("The selected Works runtime queue is invalid.");
        }

        if (index?.MemberWorkItemIds.Count > ProjectionDispatchOptions.DefaultMaxSharedRebuildAggregateCount)
        {
            throw new SharedProjectionCapacityExceededException();
        }

        var histories = new List<WorkItemSharedRebuildAggregateHistory>();
        foreach (string member in index?.MemberWorkItemIds ?? [])
        {
            WorkItemSharedRebuildAggregateHistory history = await WorkItemRuntimeHistoryStore.ReadAsync(
                coordinator, scope, generation, member, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(history.AggregateId, member, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected Works runtime history has a foreign identity.");
            }

            histories.Add(history);
        }

        int existingIndex = histories.FindIndex(history => string.Equals(history.AggregateId, request.AggregateId, StringComparison.Ordinal));
        ProjectionEventDto incoming = request.Events[0];
        WorkItemSharedRebuildAggregateHistory nextHistory;
        if (existingIndex < 0)
        {
            nextHistory = new WorkItemSharedRebuildAggregateHistory(request.AggregateId, [incoming]);
            histories.Add(nextHistory);
        }
        else
        {
            WorkItemSharedRebuildAggregateHistory previous = histories[existingIndex];
            ProjectionEventDto? duplicate = previous.Events.FirstOrDefault(item => item.SequenceNumber == incoming.SequenceNumber);
            if (duplicate is not null && !string.Equals(
                    JsonSerializer.Serialize(duplicate, s_json),
                    JsonSerializer.Serialize(incoming, s_json),
                    StringComparison.Ordinal))
            {
                throw FoldFailure(delivery, request.AggregateId, incoming.SequenceNumber);
            }

            nextHistory = duplicate is not null
                ? previous
                : previous with { Events = [.. previous.Events, incoming] };
            histories[existingIndex] = nextHistory;
        }

        if (histories.Count > ProjectionDispatchOptions.DefaultMaxSharedRebuildAggregateCount
            || histories.Sum(history => (long)WorkItemRuntimeHistoryStore.Serialize(history).Length) > WorkItemRuntimeHistoryStore.MaxTenantBytes)
        {
            throw new SharedProjectionCapacityExceededException();
        }

        try
        {
            var identity = new DomainSharedProjectionRebuildIdentity(
                request.TenantId, Domain, ProjectionType, "ordinary-catch-up", "ordinary-catch-up");
            IReadOnlyList<ReadModelBatchOperation> operations = WorkItemSharedRebuildManifestBuilder.BuildEpoch(
                identity,
                new WorkItemSharedRebuildCandidateState(histories),
                cancellationToken).Operations;

            // A complete authoritative rebuild may clear parking. Ordinary delivery only has one
            // envelope, so refolding the family must retain every aggregate's durable recovery disposition.
            var retainedParking = new Dictionary<string, WorksRuntimeDocument>(StringComparer.Ordinal);
            foreach (WorkItemSharedRebuildAggregateHistory history in histories)
            {
                string key = WorksReadModelKeys.RuntimeParkingKey(history.AggregateId);
                ReadModelEntry<WorksRuntimeDocument> parking = await coordinator
                    .ReadGenerationAsync<WorksRuntimeDocument>(scope, generation, key, cancellationToken)
                    .ConfigureAwait(false);
                if (parking.Value is not null)
                {
                    _ = parking.Value.As<WorkItemProjectionParking>();
                    retainedParking.Add(key, parking.Value);
                }
                else if (index?.MemberWorkItemIds.Contains(history.AggregateId, StringComparer.Ordinal) == true)
                {
                    throw new InvalidOperationException("The selected Works runtime generation is missing parking context.");
                }
            }

            var changed = new List<ReadModelBatchOperation>();
            foreach (ReadModelBatchOperation proposed in operations)
            {
                ReadModelBatchOperation operation = retainedParking.TryGetValue(proposed.Key, out WorksRuntimeDocument? parking)
                    ? ReadModelBatchOperation.Write(proposed.Key, parking, ReadModelBatchConcurrency.LastWrite) : proposed;
                WorksRuntimeDocument? previous = (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope,
                    generation, operation.Key, cancellationToken).ConfigureAwait(false)).Value;
                if (previous is null || !ReadModelBatchOperation.Write(operation.Key, previous,
                    ReadModelBatchConcurrency.LastWrite).CanonicalValue.Span.SequenceEqual(operation.CanonicalValue.Span))
                    changed.Add(operation);
            }
            if (changed.Count > 128 || changed.Any(operation => operation.CanonicalValue.Length
                + System.Text.Encoding.UTF8.GetByteCount(operation.Key) + 2048 > 256 * 1024))
                throw new SharedProjectionCapacityExceededException();
            return changed;
        }
        catch (SharedProjectionCapacityExceededException)
        {
            throw;
        }
        catch (OverflowException)
        {
            throw new SharedProjectionCapacityExceededException();
        }
        catch (InvalidOperationException)
        {
            throw FoldFailure(delivery, request.AggregateId, incoming.SequenceNumber);
        }
    }

    /// <inheritdoc/>
    public Task<DomainProjectionHandlerResult> ProjectAsync(
        ProjectionRequest request,
        string dispatchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DomainProjectionHandlerResult.Failed(ProjectionDispatchReasonCodes.UnsupportedCapability));
    }

    private static WorkItemSharedRebuildCandidateState FromCandidate(DomainSharedProjectionRebuildCandidate candidate)
    {
        WorkItemSharedRebuildCandidateState? state;
        try
        {
            state = JsonSerializer.Deserialize<WorkItemSharedRebuildCandidateState>(candidate.State.Span, s_json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Works shared rebuild candidate is malformed.", exception);
        }

        return state is { Histories: not null }
            ? state
            : throw new InvalidOperationException("The Works shared rebuild candidate is malformed.");
    }

    private static DomainSharedProjectionRebuildCandidate ToCandidate(WorkItemSharedRebuildCandidateState state)
        => new(JsonSerializer.SerializeToUtf8Bytes(state, s_json));

    private void ValidateIdentity(DomainSharedProjectionRebuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.Equals(identity.Domain, "work", StringComparison.Ordinal)
            || !string.Equals(identity.ProjectionType, ProjectionType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The shared rebuild identity does not target the Works roll-up route.");
        }
    }

    private SharedProjectionFoldFailureException FoldFailure(SharedProjectionDelivery delivery, string aggregateId, long sourceSequence)
        => new(
            delivery.SourceStream,
            delivery.EnvelopePosition,
            (_options?.Value ?? new WorksProjectionOptions()).MaxUndecodableEventDispatchesBeforeParking,
            failures => [ReadModelBatchOperation.Write(
                WorksReadModelKeys.RuntimeParkingKey(aggregateId),
                WorksRuntimeDocument.From(new WorkItemProjectionParking
                {
                    FailedSequence = sourceSequence,
                    FailureCount = failures,
                    Parked = true,
                }),
                ReadModelBatchConcurrency.LastWrite)]);
}
