using System.Security.Cryptography;
using System.Text;

using Hexalith.EventStore.Client.Projections;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Reminders;

namespace Hexalith.Works.Projections;

/// <summary>Builds and validates the versioned queue and pending-date indexes inside a Works runtime generation.</summary>
internal static class WorksRuntimeIndexShards
{
    /// <summary>The current layout version of both runtime index inventories.</summary>
    internal const int Version = 1;
    private const int BucketCount = 64;
    private const int MaxMembers = ProjectionDispatchOptions.DefaultMaxSharedRebuildAggregateCount;
    private const int MaxMutationBytes = 256 * 1024;

    /// <summary>Creates a flat compatibility write or a complete validated shard inventory and its writes.</summary>
    internal static IReadOnlyList<ReadModelBatchOperation> CreateOperations(
        WorksWhatsNextTenantIndex queue,
        PendingDateAwaitTenantIndex pendingDate,
        bool useShardedIndexes,
        CancellationToken cancellationToken)
    {
        ValidateIndexes(queue, pendingDate, useShardedIndexes);
        if (!useShardedIndexes)
        {
            return
            [
                Write(WorksReadModelKeys.RuntimeQueueKey, WorksRuntimeDocument.From(queue)),
                Write(WorksReadModelKeys.RuntimePendingDateKey, WorksRuntimeDocument.From(pendingDate)),
            ];
        }

        return
        [
            .. CreateShards(WorksReadModelKeys.RuntimeQueueKey, queue.MemberWorkItemIds, members =>
                WorksRuntimeDocument.From(new WorksWhatsNextTenantIndex
                {
                    SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
                    MemberWorkItemIds = members.ToArray(),
                    Items = SelectMembers(queue.Items, members),
                    LastSequences = SelectMembers(queue.LastSequences, members),
                }), cancellationToken),
            .. CreateShards(WorksReadModelKeys.RuntimePendingDateKey, pendingDate.LastSequences.Keys, members =>
                WorksRuntimeDocument.From(new PendingDateAwaitTenantIndex
                {
                    Entries = SelectMembers(pendingDate.Entries, members),
                    LastSequences = SelectMembers(pendingDate.LastSequences, members),
                }), cancellationToken),
        ];
    }

    /// <summary>Derives readable shard keys from the two validated roots without enumerating storage.</summary>
    internal static IReadOnlyList<string> ShardKeys(WorksRuntimeDocument queue, WorksRuntimeDocument pendingDate)
    {
        if (IsFlat(queue, pendingDate))
            return [];

        return [.. ShardKeys(WorksReadModelKeys.RuntimeQueueKey, Manifest(WorksReadModelKeys.RuntimeQueueKey, queue)),
            .. ShardKeys(WorksReadModelKeys.RuntimePendingDateKey, Manifest(WorksReadModelKeys.RuntimePendingDateKey, pendingDate))];
    }

    /// <summary>Reconstructs the indexes and checks shard routing, byte limits, coverage and integrity.</summary>
    internal static WorksRuntimeIndexes Reconstruct(
        WorksRuntimeDocument queue,
        WorksRuntimeDocument pendingDate,
        IReadOnlyDictionary<string, WorksRuntimeDocument?> documents)
    {
        if (IsFlat(queue, pendingDate))
        {
            var flat = new WorksRuntimeIndexes(queue.As<WorksWhatsNextTenantIndex>(),
                pendingDate.As<PendingDateAwaitTenantIndex>(), false);
            ValidateIndexes(flat.Queue, flat.PendingDate, false);
            return flat;
        }

        var items = new Dictionary<string, WhatsNextItem>(StringComparer.Ordinal);
        var queueSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        var entries = new Dictionary<string, IReadOnlyList<PendingDateAwait>>(StringComparer.Ordinal);
        var pendingSequences = new Dictionary<string, long>(StringComparer.Ordinal);

        ReadShards(WorksReadModelKeys.RuntimeQueueKey, Manifest(WorksReadModelKeys.RuntimeQueueKey, queue), documents, shard =>
        {
            WorksWhatsNextTenantIndex part = shard.As<WorksWhatsNextTenantIndex>();
            ValidateQueue(part, true);
            AppendMembers(items, part.Items);
            AppendMembers(queueSequences, part.LastSequences);
            return part.MemberWorkItemIds;
        });
        ReadShards(WorksReadModelKeys.RuntimePendingDateKey, Manifest(WorksReadModelKeys.RuntimePendingDateKey, pendingDate), documents, shard =>
        {
            PendingDateAwaitTenantIndex part = shard.As<PendingDateAwaitTenantIndex>();
            ValidatePending(part);
            AppendMembers(entries, part.Entries);
            AppendMembers(pendingSequences, part.LastSequences);
            return part.LastSequences.Keys;
        });

        var indexes = new WorksRuntimeIndexes(
            new WorksWhatsNextTenantIndex
            {
                SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
                MemberWorkItemIds = [.. queueSequences.Keys.Order(StringComparer.Ordinal)],
                Items = items,
                LastSequences = queueSequences,
            },
            new PendingDateAwaitTenantIndex { Entries = entries, LastSequences = pendingSequences },
            true);
        ValidateIndexes(indexes.Queue, indexes.PendingDate, true);
        return indexes;
    }

    /// <summary>Returns whether the canonical mutation plus its logical key and overhead fit the existing budget.</summary>
    internal static bool FitsMutation(ReadModelBatchOperation operation)
        => operation.CanonicalValue.Length + Encoding.UTF8.GetByteCount(operation.Key) + 2048 <= MaxMutationBytes;

    private static bool IsFlat(WorksRuntimeDocument queue, WorksRuntimeDocument pendingDate)
        => queue.Kind == typeof(WorksWhatsNextTenantIndex).FullName && pendingDate.Kind == typeof(PendingDateAwaitTenantIndex).FullName;

    private static WorksRuntimeIndexManifest Manifest(string rootKey, WorksRuntimeDocument document)
    {
        WorksRuntimeIndexManifest manifest = document.As<WorksRuntimeIndexManifest>();
        if (manifest.RootKey != rootKey || manifest.Version != Version
            || manifest.MemberCount is < 0 or > MaxMembers
            || manifest.PageCounts is not { Length: BucketCount }
            || manifest.PageCounts.Any(count => count < 0 || count > manifest.MemberCount)
            || manifest.PageCounts.Sum() > manifest.MemberCount
            || manifest.Digest is not { Length: 64 }
            || !manifest.Digest.All(Uri.IsHexDigit))
        {
            throw InvalidIndex();
        }

        if (rootKey == WorksReadModelKeys.RuntimeQueueKey && manifest.MemberCount == 0)
            throw new WorksRuntimeIndexMembershipException();

        return manifest;
    }

    private static IEnumerable<string> ShardKeys(string rootKey, WorksRuntimeIndexManifest manifest)
    {
        for (int bucket = 0; bucket < BucketCount; bucket++)
        {
            for (int page = 0; page < manifest.PageCounts[bucket]; page++)
                yield return WorksReadModelKeys.RuntimeIndexShardKey(rootKey, manifest.Version, bucket, page);
        }
    }

    private static IReadOnlyList<ReadModelBatchOperation> CreateShards(
        string rootKey,
        IEnumerable<string> memberIds,
        Func<ArraySegment<string>, WorksRuntimeDocument> document,
        CancellationToken cancellationToken)
    {
        var operations = new List<ReadModelBatchOperation>();
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var pageCounts = new int[BucketCount];
        int memberCount = 0;
        foreach (IGrouping<int, string> group in memberIds.Order(StringComparer.Ordinal).GroupBy(Bucket).OrderBy(group => group.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] members = group.ToArray();
            AddPages(members, group.Key);
            memberCount += members.Length;
        }

        var manifest = new WorksRuntimeIndexManifest(rootKey, Version, memberCount, pageCounts, Convert.ToHexString(digest.GetHashAndReset()));
        ReadModelBatchOperation root = Write(rootKey, WorksRuntimeDocument.From(manifest));
        if (!FitsMutation(root))
            throw new SharedProjectionCapacityExceededException();
        operations.Add(root);
        return operations;

        void AddPages(ArraySegment<string> members, int bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = WorksReadModelKeys.RuntimeIndexShardKey(rootKey, Version, bucket, pageCounts[bucket]);
            ReadModelBatchOperation operation = Write(key, document(members));
            if (FitsMutation(operation))
            {
                operations.Add(operation);
                digest.AppendData(SHA256.HashData(operation.CanonicalValue.Span));
                pageCounts[bucket]++;
                return;
            }

            if (members.Count == 1)
                throw new SharedProjectionCapacityExceededException();

            // Bisect overflowing buckets. A change to one bucket never moves a member in another bucket.
            int half = members.Count / 2;
            AddPages(members.Slice(0, half), bucket);
            AddPages(members.Slice(half), bucket);
        }
    }

    private static void ReadShards(
        string rootKey,
        WorksRuntimeIndexManifest manifest,
        IReadOnlyDictionary<string, WorksRuntimeDocument?> documents,
        Func<WorksRuntimeDocument, IEnumerable<string>> readMembers)
    {
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var members = new HashSet<string>(StringComparer.Ordinal);
        for (int bucket = 0; bucket < BucketCount; bucket++)
        {
            for (int page = 0; page < manifest.PageCounts[bucket]; page++)
            {
                string key = WorksReadModelKeys.RuntimeIndexShardKey(rootKey, manifest.Version, bucket, page);
                if (!documents.TryGetValue(key, out WorksRuntimeDocument? document) || document is null)
                    throw InvalidIndex();

                ReadModelBatchOperation operation = Write(key, document);
                if (!FitsMutation(operation))
                    throw InvalidIndex();

                digest.AppendData(SHA256.HashData(operation.CanonicalValue.Span));
                int before = members.Count;
                foreach (string member in readMembers(document))
                {
                    if (Bucket(member) != bucket || !members.Add(member) || members.Count > manifest.MemberCount)
                        throw InvalidIndex();
                }

                if (members.Count == before)
                    throw InvalidIndex();
            }
        }

        if (members.Count != manifest.MemberCount
            || !string.Equals(manifest.Digest, Convert.ToHexString(digest.GetHashAndReset()), StringComparison.Ordinal))
        {
            throw InvalidIndex();
        }
    }

    private static void ValidateIndexes(WorksWhatsNextTenantIndex queue, PendingDateAwaitTenantIndex pending, bool sharded)
    {
        ValidateQueue(queue, sharded);
        if (!sharded)
        {
            // The old queue contract allows eligible items to supply a missing legacy sequence.
            if (pending.Entries is null || pending.LastSequences is null)
                throw InvalidIndex();
            return;
        }

        ValidatePending(pending);
        if (pending.LastSequences.Keys.Any(id => !queue.LastSequences.ContainsKey(id)))
            throw InvalidIndex();
    }

    private static void ValidateQueue(WorksWhatsNextTenantIndex queue, bool sharded)
    {
        if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(queue)
            || (sharded && (queue.MemberWorkItemIds.Count is < 1 or > MaxMembers
                || queue.LastSequences.Count != queue.MemberWorkItemIds.Count
                || queue.LastSequences.Values.Any(sequence => sequence < 0))))
        {
            throw new WorksRuntimeIndexMembershipException();
        }
    }

    private static void ValidatePending(PendingDateAwaitTenantIndex pending)
    {
        if (pending.Entries is null || pending.LastSequences is null
            || pending.LastSequences.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 1)
            || pending.Entries.Any(pair => pair.Value is not { Count: > 0 } || !pending.LastSequences.ContainsKey(pair.Key)))
        {
            throw InvalidIndex();
        }
    }

    private static int Bucket(string member)
        => SHA256.HashData(Encoding.UTF8.GetBytes(member))[0] % BucketCount;

    private static Dictionary<string, TValue> SelectMembers<TValue>(
        Dictionary<string, TValue> source, IEnumerable<string> members)
    {
        var selected = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (string member in members)
        {
            if (source.TryGetValue(member, out TValue? value))
                selected.Add(member, value);
        }

        return selected;
    }

    private static void AppendMembers<TValue>(Dictionary<string, TValue> all, Dictionary<string, TValue> part)
    {
        foreach ((string member, TValue value) in part)
        {
            if (!all.TryAdd(member, value))
                throw InvalidIndex();
        }
    }

    private static ReadModelBatchOperation Write(string key, WorksRuntimeDocument document)
        => ReadModelBatchOperation.Write(key, document, ReadModelBatchConcurrency.LastWrite);

    private static InvalidOperationException InvalidIndex()
        => new("The selected Works runtime index inventory is missing or invalid.");
}
