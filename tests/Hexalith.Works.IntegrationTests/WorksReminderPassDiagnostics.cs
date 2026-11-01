using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Retains only bounded actual SDK pass-completion counts and timestamps, never log messages or source identities.</summary>
internal sealed class WorksReminderPassDiagnostics : ILoggerProvider, ILogger
{
    internal const string Category = "Hexalith.EventStore.DomainService.ReminderReconciler";
    private static readonly string[] Fields = ["Tenants", "Candidates", "Armed", "Submitted", "Cancelled", "Unresolved", "Quarantined", "Incomplete"];
    private readonly object _gate = new();
    private readonly string? _path;
    private object? _health;

    internal WorksReminderPassDiagnostics(string? path = null)
    {
        if (path is not null) WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(path)!);
        _path = path;
    }
    private readonly List<object> _first = [];
    private readonly Queue<object> _last = [];
    private long _total;
    private long _malformed;
    private long _failures;

    public ILogger CreateLogger(string categoryName) => categoryName == Category ? this : NullLogger.Instance;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Information;
    public void Dispose() { }

    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (level != LogLevel.Information || eventId.Id != 200210) return;
        try
        {
            lock (_gate)
            {
                _total++;
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                    foreach (var pair in values.Take(32))
                        if (Fields.Contains(pair.Key, StringComparer.Ordinal) && pair.Value is int count && count >= 0)
                            if (!counts.TryAdd(pair.Key, count)) { _malformed++; Persist(); return; }
                if (counts.Count != Fields.Length) { _malformed++; Persist(); return; }
                var sample = new { observedAt = DateTimeOffset.UtcNow, counts };
                if (_first.Count < 4) _first.Add(sample);
                _last.Enqueue(sample);
                while (_last.Count > 16) _last.Dequeue();
                Persist();
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        { Interlocked.Increment(ref _failures); }
    }

    internal object Snapshot()
    {
        lock (_gate) return SnapshotCore();
    }

    internal void RecordHealth(HealthReport report)
    {
        try
        {
            lock (_gate)
            {
                _health = new { observedAt = DateTimeOffset.UtcNow, readiness = WorksReminderHealthProjection.ProjectReminder(report) };
                Persist();
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) { Interlocked.Increment(ref _failures); }
    }

    private object SnapshotCore() => new { processId = Environment.ProcessId, observedAt = DateTimeOffset.UtcNow,
        total = _total, malformed = _malformed, observationFailures = Interlocked.Read(ref _failures),
        first = _first.ToArray(), last = _last.ToArray(), health = _health };

    private void Persist()
    {
        if (_path is null) return;
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(SnapshotCore());
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(_path + ".writing", options)) stream.Write(bytes);
            File.Move(_path + ".writing", _path, overwrite: true);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) { Interlocked.Increment(ref _failures); }
    }
}
