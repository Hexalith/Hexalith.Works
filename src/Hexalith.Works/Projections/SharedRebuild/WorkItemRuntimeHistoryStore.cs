using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Pages bounded source context behind logical SDK mutations without owning a physical writer.</summary>
internal static class WorkItemRuntimeHistoryStore
{
    /// <summary>Maximum serialized history bytes represented by one logical page.</summary>
    internal const int PageBytes = 64 * 1024;
    /// <summary>Maximum retained source context for one aggregate.</summary>
    internal const int MaxItemBytes = 4 * 1024 * 1024;
    /// <summary>Maximum retained source context for one tenant family.</summary>
    internal const int MaxTenantBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>Captures one history in bounded documents, independent of the rebuild candidate transport budget.</summary>
    internal static IReadOnlyList<ReadModelBatchOperation> CreateOperations(WorkItemSharedRebuildAggregateHistory history)
    {
        byte[] bytes = Serialize(history);
        int count = (bytes.Length + PageBytes - 1) / PageBytes;
        var operations = new List<ReadModelBatchOperation>(count + 1)
        {
            ReadModelBatchOperation.Write(WorksReadModelKeys.RuntimeHistoryKey(history.AggregateId),
                WorksRuntimeDocument.From(new WorksRuntimeHistoryManifest(history.AggregateId, count, bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)))), ReadModelBatchConcurrency.LastWrite),
        };
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            operations.Add(ReadModelBatchOperation.Write(WorksReadModelKeys.RuntimeHistoryPageKey(history.AggregateId, ordinal),
                WorksRuntimeDocument.From(new WorksRuntimeHistoryPage(bytes.AsSpan(ordinal * PageBytes,
                    Math.Min(PageBytes, bytes.Length - ordinal * PageBytes)).ToArray())), ReadModelBatchConcurrency.LastWrite));
        }
        return operations;
    }

    /// <summary>Reads the selected generation's complete bounded context, including the earlier inline representation.</summary>
    internal static async Task<WorkItemSharedRebuildAggregateHistory> ReadAsync(SharedProjectionEpochCoordinator coordinator,
        SharedProjectionScope scope, long generation, string aggregateId, CancellationToken cancellationToken)
    {
        WorksRuntimeDocument document = (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, generation,
            WorksReadModelKeys.RuntimeHistoryKey(aggregateId), cancellationToken).ConfigureAwait(false)).Value
            ?? throw new InvalidOperationException("The selected Works generation is missing source context.");
        if (document.Kind == typeof(WorkItemSharedRebuildAggregateHistory).FullName)
            return document.As<WorkItemSharedRebuildAggregateHistory>();
        WorksRuntimeHistoryManifest manifest = document.As<WorksRuntimeHistoryManifest>();
        if (manifest.AggregateId != aggregateId || manifest.ByteCount is < 1 or > MaxItemBytes
            || manifest.PageCount != (manifest.ByteCount + PageBytes - 1) / PageBytes
            || manifest.Digest is not { Length: 64 } || !manifest.Digest.All(Uri.IsHexDigit))
            throw new InvalidOperationException("The selected Works source context manifest is invalid.");
        byte[] bytes = new byte[manifest.ByteCount];
        for (int ordinal = 0; ordinal < manifest.PageCount; ordinal++)
        {
            WorksRuntimeDocument pageDocument = (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, generation,
                WorksReadModelKeys.RuntimeHistoryPageKey(aggregateId, ordinal), cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("The selected Works source context page is missing.");
            byte[] part = pageDocument.As<WorksRuntimeHistoryPage>().Data;
            int expected = Math.Min(PageBytes, bytes.Length - ordinal * PageBytes);
            if (part is null || part.Length != expected)
                throw new InvalidOperationException("The selected Works source context page is invalid.");
            part.CopyTo(bytes, ordinal * PageBytes);
        }
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), manifest.Digest, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected Works source context digest is invalid.");
        return JsonSerializer.Deserialize<WorkItemSharedRebuildAggregateHistory>(bytes, s_json)
            ?? throw new InvalidOperationException("The selected Works source context is empty.");
    }

    /// <summary>Enforces an explicit operational capacity without converting healthy source growth to poison.</summary>
    internal static byte[] Serialize(WorkItemSharedRebuildAggregateHistory history)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(history, s_json);
        if (bytes.Length > MaxItemBytes) throw new SharedProjectionCapacityExceededException();
        return bytes;
    }
}
