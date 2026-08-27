using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Minimal ETag-aware read-model store for Story 4.7 checkpoint-index integration tests.
/// </summary>
internal sealed class Story47InMemoryReadModelStore : IReadModelStore
{
    // A coordinated conflict must never outlive the test that armed it: if fewer writers than declared ever
    // reach the coordinated key, an unbounded wait would hang the whole unattended run instead of failing it.
    private static readonly TimeSpan s_coordinationTimeout = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly Dictionary<string, (object Value, int Version)> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<object>> _successfulWrites = new(StringComparer.Ordinal);
    private readonly List<string> _successfulWriteKeys = [];
    private readonly Dictionary<string, int> _saveFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _trySaveRejections = new(StringComparer.Ordinal);
    private int _coordinatedArrivals;
    private int _coordinatedParticipants;
    private TaskCompletionSource<bool>? _coordinatedRelease;
    private string? _coordinatedScopedKey;

    /// <summary>Gets the successful-write keys in persisted order.</summary>
    public IReadOnlyList<string> SuccessfulWriteKeys
    {
        get
        {
            lock (_sync)
            {
                return [.. _successfulWriteKeys];
            }
        }
    }

    /// <summary>Returns the number of successful writes observed for one persisted key.</summary>
    public int GetSuccessfulWriteCount(string storeName, string key)
    {
        lock (_sync)
        {
            return _successfulWrites.TryGetValue(ScopedKey(storeName, key), out List<object>? writes)
                ? writes.Count
                : 0;
        }
    }

    /// <summary>Returns the successfully persisted values for one key in write order.</summary>
    public IReadOnlyList<TValue> GetSuccessfulWrites<TValue>(string storeName, string key)
        where TValue : class
    {
        lock (_sync)
        {
            return _successfulWrites.TryGetValue(ScopedKey(storeName, key), out List<object>? writes)
                ? [.. writes.Cast<TValue>()]
                : [];
        }
    }

    /// <summary>Clears successful-write observations without changing persisted values or ETags.</summary>
    public void ResetSuccessfulWriteObservation()
    {
        lock (_sync)
        {
            _successfulWrites.Clear();
            _successfulWriteKeys.Clear();
        }
    }

    /// <summary>
    /// Holds the first optimistic write attempts for a key until all participants have read the same ETag,
    /// deterministically forcing all but one participant through the retry path.
    /// </summary>
    public void CoordinateFirstTrySaveConflict(string storeName, string key, int participants = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(participants, 2);
        lock (_sync)
        {
            if (_coordinatedScopedKey is not null)
            {
                throw new InvalidOperationException("A first-write conflict is already coordinated.");
            }

            _coordinatedScopedKey = ScopedKey(storeName, key);
            _coordinatedParticipants = participants;
            _coordinatedArrivals = 0;
            _coordinatedRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Rejects the next optimistic writes for a key without changing its persisted state.</summary>
    public void RejectNextTrySaves(string storeName, string key, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        lock (_sync)
        {
            _trySaveRejections[ScopedKey(storeName, key)] = count;
        }
    }

    /// <summary>Fails the next unconditional writes for a key without changing its persisted state.</summary>
    public void FailNextSaves(string storeName, string key, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        lock (_sync)
        {
            _saveFailures[ScopedKey(storeName, key)] = count;
        }
    }

    /// <inheritdoc/>
    public Task<ReadModelEntry<TValue>> GetAsync<TValue>(
        string storeName,
        string key,
        CancellationToken cancellationToken = default)
        where TValue : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult(_entries.TryGetValue(ScopedKey(storeName, key), out (object Value, int Version) entry)
                ? new ReadModelEntry<TValue>((TValue)entry.Value, entry.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : new ReadModelEntry<TValue>(null, null));
        }
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
        lock (_sync)
        {
            if (Consume(_saveFailures, scopedKey))
            {
                throw new InvalidOperationException($"Injected unconditional write failure for '{key}'.");
            }

            int version = _entries.TryGetValue(scopedKey, out (object Value, int Version) current) ? current.Version + 1 : 1;
            _entries[scopedKey] = (value, version);
            RecordSuccessfulWrite(scopedKey, value);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<bool> TrySaveAsync<TValue>(
        string storeName,
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
        where TValue : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        string scopedKey = ScopedKey(storeName, key);
        Task? coordinatedWait = null;
        lock (_sync)
        {
            if (string.Equals(_coordinatedScopedKey, scopedKey, StringComparison.Ordinal))
            {
                _coordinatedArrivals++;
                coordinatedWait = _coordinatedRelease!.Task;
                if (_coordinatedArrivals == _coordinatedParticipants)
                {
                    _coordinatedScopedKey = null;
                    _coordinatedRelease.SetResult(true);
                }
            }
        }

        if (coordinatedWait is not null)
        {
            await coordinatedWait.WaitAsync(s_coordinationTimeout, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            if (Consume(_trySaveRejections, scopedKey))
            {
                return false;
            }

            if (_entries.TryGetValue(scopedKey, out (object Value, int Version) current))
            {
                if (!string.Equals(etag, current.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    return false;
                }

                _entries[scopedKey] = (value, current.Version + 1);
                RecordSuccessfulWrite(scopedKey, value);
                return true;
            }

            if (!string.IsNullOrEmpty(etag))
            {
                return false;
            }

            _entries[scopedKey] = (value, 1);
            RecordSuccessfulWrite(scopedKey, value);
            return true;
        }
    }

    private static bool Consume(Dictionary<string, int> remaining, string scopedKey)
    {
        if (!remaining.TryGetValue(scopedKey, out int count))
        {
            return false;
        }

        if (count == 1)
        {
            _ = remaining.Remove(scopedKey);
        }
        else
        {
            remaining[scopedKey] = count - 1;
        }

        return true;
    }

    private void RecordSuccessfulWrite<TValue>(string scopedKey, TValue value)
        where TValue : class
    {
        if (!_successfulWrites.TryGetValue(scopedKey, out List<object>? writes))
        {
            writes = [];
            _successfulWrites[scopedKey] = writes;
        }

        writes.Add(value);
        _successfulWriteKeys.Add(scopedKey);
    }

    private static string ScopedKey(string storeName, string key) => $"{storeName}:{key}";
}
