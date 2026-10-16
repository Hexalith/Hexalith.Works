using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;

namespace Hexalith.Works.Projections.SharedRebuild;

public sealed partial class WorkItemSharedProjectionRebuildHandler
{
    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>> PreparePartitionAsync(
        DomainSharedProjectionRebuildIdentity identity,
        ProjectionRequest aggregateHistory,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(aggregateHistory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!EpochEnabled
            || !string.Equals(aggregateHistory.TenantId, identity.TenantId, StringComparison.Ordinal)
            || !string.Equals(aggregateHistory.Domain, identity.Domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The partition history is outside the selected Works SDK rebuild.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateHistory.AggregateId);
        ArgumentNullException.ThrowIfNull(aggregateHistory.Events);
        ReadOnlyMemory<byte> bytes = WorkItemRuntimeHistoryStore.Serialize(new WorkItemSharedRebuildAggregateHistory(
            aggregateHistory.AggregateId, aggregateHistory.Events));
        return Task.FromResult(bytes);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReadOnlyMemory<byte>>> PreparePartitionChunksAsync(
        DomainSharedProjectionRebuildIdentity identity,
        ProjectionRequest aggregateHistory,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> bytes = await PreparePartitionAsync(identity, aggregateHistory, cancellationToken).ConfigureAwait(false);
        var chunks = new List<ReadOnlyMemory<byte>>();
        for (int offset = 0; offset < bytes.Length; offset += WorkItemRuntimeHistoryStore.PageBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunks.Add(bytes.Slice(offset, Math.Min(WorkItemRuntimeHistoryStore.PageBytes, bytes.Length - offset)));
        }
        return chunks;
    }

    /// <inheritdoc/>
    public Task<DomainProjectionRebuildPlan> FinalizePartitionsAsync(
        DomainSharedProjectionRebuildIdentity identity,
        IReadOnlyList<DomainSharedProjectionRebuildPartition> partitions,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(partitions);
        if (!EpochEnabled) throw new InvalidOperationException("The Works SDK projection mode is disabled.");
        var histories = new List<WorkItemSharedRebuildAggregateHistory>();
        long tenantBytes = 0;
        long previousOrdinal = -1;
        string? previousAggregate = null;
        for (int index = 0; index < partitions.Count;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DomainSharedProjectionRebuildPartition first = partitions[index];
            if (first.Ordinal <= previousOrdinal || string.IsNullOrWhiteSpace(first.AggregateId)
                || previousAggregate is not null && string.CompareOrdinal(first.AggregateId, previousAggregate) <= 0)
                throw new InvalidOperationException("The Works rebuild partition inventory is not ordered and unique.");
            previousOrdinal = first.Ordinal;
            previousAggregate = first.AggregateId;
            using var bytes = new MemoryStream();
            int chunkOrdinal = 0;
            while (index < partitions.Count && partitions[index].Ordinal == first.Ordinal)
            {
                DomainSharedProjectionRebuildPartition chunk = partitions[index++];
                if (chunk.ChunkOrdinal != chunkOrdinal++ || !string.Equals(chunk.AggregateId, first.AggregateId, StringComparison.Ordinal)
                    || chunk.State.Length is < 1 or > WorkItemRuntimeHistoryStore.PageBytes)
                    throw new InvalidOperationException("The Works rebuild owner chunks are invalid or incomplete.");
                tenantBytes += chunk.State.Length;
                if (bytes.Length + chunk.State.Length > WorkItemRuntimeHistoryStore.MaxItemBytes
                    || tenantBytes > WorkItemRuntimeHistoryStore.MaxTenantBytes)
                    throw new SharedProjectionCapacityExceededException();
                bytes.Write(chunk.State.Span);
            }
            WorkItemSharedRebuildAggregateHistory history;
            try
            {
                history = JsonSerializer.Deserialize<WorkItemSharedRebuildAggregateHistory>(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), s_json)
                    ?? throw new InvalidOperationException("The Works rebuild owner partition is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("The Works rebuild owner partition is malformed.", exception);
            }
            if (!string.Equals(history.AggregateId, first.AggregateId, StringComparison.Ordinal) || history.Events is null)
                throw new InvalidOperationException("The Works rebuild owner partition has a foreign identity or missing history.");
            histories.Add(history);
            if (histories.Count > ProjectionDispatchOptions.DefaultMaxSharedRebuildAggregateCount)
                throw new SharedProjectionCapacityExceededException();
        }
        return Task.FromResult(WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity,
            new WorkItemSharedRebuildCandidateState(histories), cancellationToken));
    }
}
