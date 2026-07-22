using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Minimal ETag-aware read-model store for Story 4.7 checkpoint-index integration tests.
/// </summary>
internal sealed class Story47InMemoryReadModelStore : IReadModelStore
{
    private readonly Dictionary<string, (object Value, int Version)> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<ReadModelEntry<TValue>> GetAsync<TValue>(
        string storeName,
        string key,
        CancellationToken cancellationToken = default)
        where TValue : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.TryGetValue(ScopedKey(storeName, key), out (object Value, int Version) entry)
            ? new ReadModelEntry<TValue>((TValue)entry.Value, entry.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : new ReadModelEntry<TValue>(null, null));
    }

    /// <inheritdoc/>
    public Task SaveAsync<TValue>(
        string storeName,
        string key,
        TValue value,
        CancellationToken cancellationToken = default)
        where TValue : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        string scopedKey = ScopedKey(storeName, key);
        int version = _entries.TryGetValue(scopedKey, out (object Value, int Version) current) ? current.Version + 1 : 1;
        _entries[scopedKey] = (value, version);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<TValue>(
        string storeName,
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
        where TValue : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        string scopedKey = ScopedKey(storeName, key);
        if (_entries.TryGetValue(scopedKey, out (object Value, int Version) current))
        {
            if (!string.Equals(etag, current.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _entries[scopedKey] = (value, current.Version + 1);
            return Task.FromResult(true);
        }

        if (!string.IsNullOrEmpty(etag))
        {
            return Task.FromResult(false);
        }

        _entries[scopedKey] = (value, 1);
        return Task.FromResult(true);
    }

    private static string ScopedKey(string storeName, string key) => $"{storeName}:{key}";
}
