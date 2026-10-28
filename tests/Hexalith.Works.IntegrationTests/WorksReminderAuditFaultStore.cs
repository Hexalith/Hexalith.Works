using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Refuses reminder disposition audit writes while preserving the serialized witness store.</summary>
internal sealed class WorksReminderAuditFaultStore(IReadModelStore inner) : IReadModelStore
{
    /// <summary>Gets or sets a value indicating whether disposition audit writes must fail.</summary>
    internal bool RejectAudit { get; set; }

    /// <summary>Gets the number of refused audit writes.</summary>
    internal int RefusedAuditWrites { get; private set; }

    /// <inheritdoc/>
    public Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
        => inner.GetAsync<T>(storeName, key, cancellationToken);

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        if (RejectAudit && key.Contains(":disposition:", StringComparison.Ordinal))
        {
            RefusedAuditWrites++;
            return Task.FromException(new IOException("Synthetic disposition audit outage."));
        }
        return inner.SaveAsync(storeName, key, value, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
        => inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
}
