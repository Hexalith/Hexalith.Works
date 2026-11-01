using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;

using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Passively observes caught SDK refusals; never substitutes an authority or reads exception messages.</summary>
internal sealed class WorksCatalogFirstChanceObserver(IConfiguration configuration) : IHostedService, IDisposable
{
    internal const string PathKey = "WORKS_SDK_PROOF_CATALOG_EXCEPTIONS";
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _sites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastWritten = new(StringComparer.Ordinal);
    private readonly List<object> _first = [];
    private readonly Queue<object> _last = [];
    private long _total;
    private long _overflow;
    private int _active;
    private long _failures;

    internal long ObservationFailures => Interlocked.Read(ref _failures);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(configuration[PathKey])!);
        if (Interlocked.Exchange(ref _active, 1) != 0) throw new InvalidOperationException("The passive observer is already active.");
        AppDomain.CurrentDomain.FirstChanceException += Observe;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _active, 0) != 0) AppDomain.CurrentDomain.FirstChanceException -= Observe;
        lock (_gate) { } // Complete any in-flight passive callback before releasing the host lifetime.
    }

    private void Observe(object? sender, FirstChanceExceptionEventArgs args)
    {
        try { ObserveCore(args); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Diagnostic I/O must never replace the genuine SDK refusal in a first-chance callback.
            Interlocked.Increment(ref _failures);
        }
    }

    private void ObserveCore(FirstChanceExceptionEventArgs args)
    {
        if (Volatile.Read(ref _active) == 0 || args.Exception is not RouteCatalogUnavailableException refusal) return;
        string[] methods = Frames(new StackTrace(refusal));
        if (methods.Length == 0) methods = Frames(new StackTrace());
        if (methods.Length == 0) return;
        string site = methods[0];
        lock (_gate)
        {
            if (Volatile.Read(ref _active) == 0) return;
            _total++;
            if (!_sites.ContainsKey(site) && _sites.Count >= 64) { _overflow++; return; }
            _sites[site] = _sites.GetValueOrDefault(site) + 1;
            long now = Stopwatch.GetTimestamp();
            if (_lastWritten.TryGetValue(site, out long prior) && Stopwatch.GetElapsedTime(prior) < TimeSpan.FromSeconds(1)) return;
            _lastWritten[site] = now;
            var sample = new { observedAt = DateTimeOffset.UtcNow, type = refusal.GetType().FullName,
                category = refusal.Category.ToString(), methods };
            if (_first.Count < 8) _first.Add(sample);
            _last.Enqueue(sample);
            while (_last.Count > 16) _last.Dequeue();
            Write();
        }
    }

    private static string[] Frames(StackTrace trace) => [.. trace.GetFrames()
        .Where(frame => frame.GetMethod()?.DeclaringType?.FullName?.StartsWith("Hexalith.EventStore.ServiceDefaults.Routing.", StringComparison.Ordinal) == true)
        .Take(8).Select(frame => frame.GetMethod()!.DeclaringType!.FullName + "." + frame.GetMethod()!.Name + "@il:" + frame.GetILOffset())];

    private void Write()
    {
        string path = configuration[PathKey]!;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { processId = Environment.ProcessId, observedAt = DateTimeOffset.UtcNow,
            total = _total, overflow = _overflow, sites = _sites, first = _first, last = _last });
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        string temporary = path + ".writing";
        using (var stream = new FileStream(temporary, options)) stream.Write(bytes);
        File.Move(temporary, path, overwrite: true);
    }
}
