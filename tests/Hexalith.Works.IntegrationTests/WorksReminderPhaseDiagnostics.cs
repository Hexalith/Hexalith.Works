using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Bounded observation of actual fixture work; never used for authorization or recovery decisions.</summary>
internal sealed class WorksReminderPhaseDiagnostics
{
    internal const string PathKey = "WORKS_SDK_PROOF_REMINDER_PHASES";
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<long, Dictionary<string, object?>> _pending = [];
    private readonly List<Dictionary<string, object?>> _first = [];
    private readonly Queue<Dictionary<string, object?>> _last = [];
    private readonly Queue<Dictionary<string, object?>> _failures = [];
    private long _sequence;
    private long _overflow;
    private long _observationFailures;

    /// <summary>Validates the owned diagnostic directory before any real work starts.</summary>
    public WorksReminderPhaseDiagnostics(IConfiguration configuration)
    {
        _path = configuration[PathKey] ?? throw new InvalidOperationException("The private reminder diagnostic path is missing.");
        WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(_path)!);
    }

    internal long ObservationFailures => Interlocked.Read(ref _observationFailures);

    internal long Begin(string phase, string identity)
    {
        lock (_gate)
        {
            long sequence = ++_sequence;
            if (_pending.Count >= 64) { _overflow++; TryWrite(); return 0; }
            _pending.Add(sequence, new()
            {
                ["sequence"] = sequence, ["phase"] = phase,
                ["identitySha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),
                ["startedAt"] = DateTimeOffset.UtcNow, ["startedTimestamp"] = Stopwatch.GetTimestamp(),
            });
            TryWrite();
            return sequence;
        }
    }

    internal void End(long sequence, Exception? exception, int? resultCount = null, int? status = null)
    {
        lock (_gate)
        {
            if (!_pending.Remove(sequence, out var record)) return;
            long started = (long)record["startedTimestamp"]!;
            record.Remove("startedTimestamp");
            record["milliseconds"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            record["outcome"] = exception is null ? "completed" : exception is OperationCanceledException ? "cancelled" : "failed";
            record["resultCount"] = resultCount;
            record["httpStatus"] = status;
            record["failure"] = ExceptionShape(exception);
            if (_first.Count < 8) _first.Add(record);
            _last.Enqueue(record);
            while (_last.Count > 16) _last.Dequeue();
            if (exception is not null) { _failures.Enqueue(record); while (_failures.Count > 16) _failures.Dequeue(); }
            TryWrite();
        }
    }

    internal static object[] ExceptionShape(Exception? exception)
    {
        var result = new List<object>();
        for (int depth = 0; exception is not null && depth < 4; depth++, exception = exception.InnerException)
            result.Add(new
            {
                type = exception.GetType().FullName,
                remoteType = exception is Dapr.Actors.ActorInvokeException remote
                    && remote.ActualExceptionType is { Length: > 0 and <= 512 } type
                    && type.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '`')
                    ? remote.ActualExceptionType : null,
                methods = new StackTrace(exception).GetFrames().Take(6).Select(frame => frame.GetMethod())
                    .Select(method => method?.DeclaringType?.FullName + "." + method?.Name).ToArray(),
            });
        return [.. result];
    }

    private void TryWrite()
    {
        try { Write(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Once initial private-path validation succeeds, observation failures cannot replace real work outcomes.
            Interlocked.Increment(ref _observationFailures);
        }
    }

    private void Write()
    {
        string path = _path;
        WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(path)!);
        var pending = _pending.Values.Select(record => record.Where(pair => pair.Key != "startedTimestamp")
            .Append(new("milliseconds", Stopwatch.GetElapsedTime((long)record["startedTimestamp"]!).TotalMilliseconds))
            .ToDictionary(pair => pair.Key, pair => pair.Value)).ToArray();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { processId = Environment.ProcessId, observedAt = DateTimeOffset.UtcNow,
            total = _sequence, overflow = _overflow, observationFailures = ObservationFailures, pending, first = _first, last = _last, failures = _failures });
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        string temporary = path + ".writing";
        using (var stream = new FileStream(temporary, options)) stream.Write(bytes);
        File.Move(temporary, path, overwrite: true);
    }
}
