using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Measures actual SDK batch manifests while delegating every write to the real test protocol.</summary>
internal sealed class WorksSdkRebuildBatchObserver(IReadModelBatchStore inner) : IReadModelBatchStore
{
    private static readonly MethodInfo s_canonicalManifest = typeof(ReadModelBatch).Assembly
        .GetType("Hexalith.EventStore.Client.Projections.ReadModelBatchFingerprint", throwOnError: true)!
        .GetMethod("BuildCanonicalManifest", BindingFlags.Public | BindingFlags.Static)!;

    private readonly ConcurrentQueue<(int Operations, int CanonicalBytes, string[] SuccessfulWrites)> _batches = new();

    /// <summary>Gets a consistent snapshot of operation and canonical-byte measurements.</summary>
    internal IReadOnlyList<(int Operations, int CanonicalBytes, string[] SuccessfulWrites)> Batches => _batches.ToArray();

    /// <inheritdoc/>
    public async Task<ReadModelBatchResult> ExecuteAsync(ReadModelBatch batch, CancellationToken cancellationToken = default)
    {
        int canonicalBytes = CanonicalBytes(batch);
        ReadModelBatchResult result = await inner.ExecuteAsync(batch, cancellationToken).ConfigureAwait(false);
        _batches.Enqueue((batch.Operations.Count, canonicalBytes, result.IsSuccess ? WriteDigests(batch.Operations) : []));
        return result;
    }

    /// <summary>Identifies staged write payloads independently of the SDK's generation key encoding.</summary>
    internal static string[] WriteDigests(IEnumerable<ReadModelBatchOperation> operations)
        => [.. operations.Where(operation => operation.Kind == ReadModelBatchOperationKind.Write)
            .Select(operation => Convert.ToHexString(SHA256.HashData(operation.CanonicalValue.Span)))];

    /// <summary>Measures the SDK's exact encoding without copying its serialization contract.</summary>
    internal static int CanonicalBytes(ReadModelBatch batch)
        => ((byte[])s_canonicalManifest.Invoke(null, [batch])!).Length;
}
