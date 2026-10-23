using System.Collections.Concurrent;
using System.Reflection;

using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Measures actual SDK batch manifests while delegating every write to the real test protocol.</summary>
internal sealed class WorksSdkRebuildBatchObserver(IReadModelBatchStore inner) : IReadModelBatchStore
{
    private static readonly MethodInfo s_canonicalManifest = typeof(ReadModelBatch).Assembly
        .GetType("Hexalith.EventStore.Client.Projections.ReadModelBatchFingerprint", throwOnError: true)!
        .GetMethod("BuildCanonicalManifest", BindingFlags.Public | BindingFlags.Static)!;

    private readonly ConcurrentQueue<(int Operations, int CanonicalBytes)> _batches = new();

    /// <summary>Gets a consistent snapshot of operation and canonical-byte measurements.</summary>
    internal IReadOnlyList<(int Operations, int CanonicalBytes)> Batches => _batches.ToArray();

    /// <inheritdoc/>
    public Task<ReadModelBatchResult> ExecuteAsync(ReadModelBatch batch, CancellationToken cancellationToken = default)
    {
        byte[] canonical = (byte[])s_canonicalManifest.Invoke(null, [batch])!;
        _batches.Enqueue((batch.Operations.Count, canonical.Length));
        return inner.ExecuteAsync(batch, cancellationToken);
    }

    /// <summary>Measures the SDK's exact encoding without copying its serialization contract.</summary>
    internal static int CanonicalBytes(ReadModelBatch batch)
        => ((byte[])s_canonicalManifest.Invoke(null, [batch])!).Length;
}
