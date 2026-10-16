using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Faults real audit persistence while retaining the real SDK state store for all other work.</summary>
internal sealed class WorksPlatformAuditFaultStore(IReadModelStore inner) : IReadModelStore
{
    internal const string FaultKey = "story416:synthetic-audit-outage";

    /// <inheritdoc/>
    public async Task<ReadModelEntry<TValue>> GetAsync<TValue>(string storeName, string key, CancellationToken cancellationToken = default) where TValue : class
    {
        await CheckAsync(storeName, key, cancellationToken).ConfigureAwait(false);
        return await inner.GetAsync<TValue>(storeName, key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveAsync<TValue>(string storeName, string key, TValue value, CancellationToken cancellationToken = default) where TValue : class
    {
        await CheckAsync(storeName, key, cancellationToken).ConfigureAwait(false);
        await inner.SaveAsync(storeName, key, value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> TrySaveAsync<TValue>(string storeName, string key, TValue value, string etag, CancellationToken cancellationToken = default) where TValue : class
    {
        await CheckAsync(storeName, key, cancellationToken).ConfigureAwait(false);
        return await inner.TrySaveAsync(storeName, key, value, etag, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckAsync(string storeName, string key, CancellationToken cancellationToken)
    {
        if (key.StartsWith("platform:works:process-disposition-audit:", StringComparison.Ordinal)
            && (await inner.GetAsync<WorksPlatformAuditFault>(storeName, FaultKey, cancellationToken).ConfigureAwait(false)).Value?.Unavailable == true)
            throw new IOException("Synthetic durable audit provider outage.");
    }
}
