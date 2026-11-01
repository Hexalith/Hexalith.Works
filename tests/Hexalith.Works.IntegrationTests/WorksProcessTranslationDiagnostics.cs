using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Retains only the actual translator's structured timing fields; never formats messages or changes dispatch.</summary>
internal sealed class WorksProcessTranslationDiagnostics : ILoggerProvider
{
    internal const string Category = "Hexalith.Works.Recovery.WorksProcessTranslator";
    private readonly object _gate = new();
    private readonly string? _path;
    private readonly Dictionary<long, (string Kind, DateTimeOffset Started)> _pending = [];
    private readonly List<object> _first = [];
    private readonly Queue<object> _last = [];
    private readonly Queue<object> _failures = [];
    private long _total, _malformed, _overflow, _observationFailures;

    internal WorksProcessTranslationDiagnostics(string? path = null)
    {
        _path = path;
        if (path is not null) WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName == Category);
    public void Dispose() { }

    internal object Snapshot()
    {
        lock (_gate) return new { processId = Environment.ProcessId, observedAt = DateTimeOffset.UtcNow,
            total = _total, malformed = _malformed, overflow = _overflow, observationFailures = _observationFailures,
            pending = _pending.Select(pair => new { observation = pair.Key, kind = pair.Value.Kind, startedAt = pair.Value.Started }).ToArray(), first = _first.ToArray(), last = _last.ToArray(), failures = _failures.ToArray() };
    }

    private void Record(EventId id, object? state)
    {
        lock (_gate)
        {
            try
            {
                if (state is not IEnumerable<KeyValuePair<string, object?>> fields) { _malformed++; return; }
                var values = fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                if (!values.TryGetValue("Observation", out var number) || number is not long observation || observation <= 0
                    || !values.TryGetValue("Kind", out var observedKind) || observedKind is not string kind
                    || kind is not ("WorkItemCancelled" or "WorkItemExpired" or "WorkItemCompleted" or "WorkItemRescheduled" or "other"))
                { _malformed++; return; }
                if (id.Id == 290101)
                {
                    _total++;
                    if (_pending.Count >= 64) { _overflow++; return; }
                    if (!_pending.TryAdd(observation, (kind, DateTimeOffset.UtcNow))) _malformed++;
                }
                else if (id.Id == 290102)
                {
                    if (!values.TryGetValue("Milliseconds", out var duration) || duration is not double milliseconds || !double.IsFinite(milliseconds) || milliseconds < 0
                        || !values.TryGetValue("Outcome", out var outcomeValue) || outcomeValue is not string outcome || outcome is not ("completed" or "failed" or "cancelled")
                        || !values.TryGetValue("Effects", out var effects) || effects is not null && effects is not (int and >= 0)
                        || outcome == "completed" && effects is null || outcome != "completed" && effects is not null
                        || !_pending.Remove(observation, out var started) || started.Kind != kind)
                    { _malformed++; return; }
                    object row = new { observation, kind, startedAt = started.Started, endedAt = DateTimeOffset.UtcNow, milliseconds, outcome, effects };
                    if (_first.Count < 8) _first.Add(row);
                    _last.Enqueue(row); while (_last.Count > 32) _last.Dequeue();
                    if (outcome != "completed") { _failures.Enqueue(row); while (_failures.Count > 16) _failures.Dequeue(); }
                }
                TryWrite();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { _observationFailures++; }
        }
    }

    private void TryWrite()
    {
        if (_path is null) return;
        try
        {
            WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(_path)!);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(_path + ".writing", options)) stream.Write(JsonSerializer.SerializeToUtf8Bytes(Snapshot()));
            File.Move(_path + ".writing", _path, overwrite: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _observationFailures++; }
    }

    private sealed class Logger(WorksProcessTranslationDiagnostics owner, bool selected) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => selected && logLevel == LogLevel.Debug;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && eventId.Id is 290101 or 290102) owner.Record(eventId, state);
        }
    }
}
