using System.Net;

using Google.Protobuf;

using Hexalith.EventStore.DomainService;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Disposable loopback OTLP receiver; retains only allowlisted recovery gauges and reason labels.</summary>
/// <remarks>Wire fields follow the official opentelemetry-proto metrics/v1 and common/v1 schemas.
/// https://github.com/open-telemetry/opentelemetry-proto/blob/main/opentelemetry/proto/metrics/v1/metrics.proto
/// This is a bounded test receiver, not a general telemetry service.</remarks>
internal sealed class WorksPlatformTelemetryCollector : IAsyncDisposable
{
    private const int PayloadLimit = 4 * 1024 * 1024;
    private const int PointLimit = 32768;
    private const string Prefix = "eventstore.domain.recovery.";
    private static readonly HashSet<string> _names = ["unresolved", "pendingDispositions", "pendingPublications", "pendingTransitions", "pendingObservations", "retainedCandidates", "complete", "capacityExceeded", "stateAvailable", "ready", "alert", "reason"];
    private static readonly HashSet<string> _reasons = ["candidate-offset-invalid", "candidate-page-missing", "candidate-page-unreadable", "candidate-tail-unlinked", "candidate-tail-unreadable", "capacity-exceeded", "catalog-unavailable", "hosted-pass-failed", "read-worker-exhausted", "reconciler-stopped", "reconciliation-disabled", "source-incomplete", "source-state-unavailable", "sweep-record-budget", "tenant-offset-invalid", "tenant-page-missing", "tenant-page-unreadable", "tenant-tail-unlinked", "tenant-tail-unreadable", "other"];
    private readonly object _gate = new();
    private readonly Queue<WorksPlatformMetricPoint> _points = [];
    private readonly Queue<string> _failures = [];
    private IHost? _host;
    private Uri? _endpoint;

    /// <summary>Gets the base loopback endpoint; HTTP/protobuf exporters append /v1/metrics.</summary>
    internal Uri Endpoint => _endpoint ?? throw new InvalidOperationException("Collector has not started.");

    /// <summary>Gets an immutable receipt-order snapshot of bounded transport observations.</summary>
    internal IReadOnlyList<WorksPlatformMetricPoint> Points { get { lock (_gate) return _points.ToArray(); } }

    /// <summary>Gets bounded refusal codes; no raw telemetry request is retained.</summary>
    internal IReadOnlyList<string> Failures { get { lock (_gate) return _failures.ToArray(); } }

    /// <summary>Starts Kestrel on an OS-assigned loopback port with no hosting startup injection.</summary>
    /// <param name="cancellationToken">Startup cancellation.</param>
    /// <returns>The owned disposable collector.</returns>
    internal static async Task<WorksPlatformTelemetryCollector> StartAsync(CancellationToken cancellationToken)
    {
        var collector = new WorksPlatformTelemetryCollector();
        IHost host = Host.CreateDefaultBuilder([])
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(web => web
                .UseSetting(WebHostDefaults.PreventHostingStartupKey, "true")
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 0);
                    options.Limits.MaxRequestBodySize = null;
                })
                .Configure(application => application.Run(collector.ReceiveAsync)))
            .Build();
        collector._host = host;
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            string address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            collector._endpoint = new Uri(address.TrimEnd('/') + "/", UriKind.Absolute);
            return collector;
        }
        catch
        {
            await collector.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        IHost? host = Interlocked.Exchange(ref _host, null);
        if (host is null) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await host.StopAsync(deadline.Token).ConfigureAwait(false); }
        finally
        {
            if (host is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
            else host.Dispose();
        }
    }

    private async Task ReceiveAsync(HttpContext context)
    {
        if (context.Request.Path != "/v1/metrics" || !HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (context.Request.Headers.ContentEncoding.Count != 0
            || context.Request.ContentType?.Split(';')[0] is not ("application/x-protobuf" or "application/protobuf"))
        {
            Refuse("unsupported-content", StatusCodes.Status415UnsupportedMediaType, context);
            return;
        }
        if (context.Request.ContentLength > PayloadLimit)
        {
            Refuse("payload-limit", StatusCodes.Status413PayloadTooLarge, context);
            return;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using MemoryStream body = new();
            byte[] buffer = new byte[16384];
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (body.Length + count > PayloadLimit)
                {
                    Refuse("payload-limit", StatusCodes.Status413PayloadTooLarge, context);
                    return;
                }
                await body.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
            }
            List<WorksPlatformMetricPoint> points = Decode(body.ToArray(), DateTimeOffset.UtcNow);
            lock (_gate)
            {
                foreach (WorksPlatformMetricPoint point in points)
                {
                    if (_points.Count >= PointLimit) _points.Dequeue();
                    _points.Enqueue(point);
                }
            }
            // Empty protobuf ExportMetricsServiceResponse is a successful OTLP acknowledgement.
            context.Response.ContentType = "application/x-protobuf";
            context.Response.ContentLength = 0;
            context.Response.StatusCode = StatusCodes.Status200OK;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OverflowException)
        {
            Refuse("invalid-recovery-otlp", StatusCodes.Status400BadRequest, context);
        }
        catch (OperationCanceledException)
        {
            Refuse("request-deadline", StatusCodes.Status408RequestTimeout, context);
        }
    }

    private void Refuse(string reason, int status, HttpContext context)
    {
        lock (_gate)
        {
            if (_failures.Count >= 16) _failures.Dequeue();
            _failures.Enqueue(reason);
        }
        context.Response.StatusCode = status;
    }

    private static List<WorksPlatformMetricPoint> Decode(byte[] bytes, DateTimeOffset at)
    {
        List<WorksPlatformMetricPoint> points = [];
        Fields(bytes, (tag, input) =>
        {
            if (tag != 10) { input.SkipLastField(); return; }
            Fields(input.ReadBytes().ToByteArray(), (resourceTag, resource) =>
            {
                if (resourceTag == 18) DecodeScope(resource.ReadBytes().ToByteArray(), at, points);
                else resource.SkipLastField();
            });
        });
        return points;
    }

    private static void DecodeScope(byte[] bytes, DateTimeOffset at, List<WorksPlatformMetricPoint> points)
    {
        string? scope = null;
        List<byte[]> metrics = [];
        Fields(bytes, (tag, input) =>
        {
            if (tag == 10)
                Fields(input.ReadBytes().ToByteArray(), (scopeTag, description) =>
                {
                    if (scopeTag == 10) scope = description.ReadString();
                    else description.SkipLastField();
                });
            else if (tag == 18)
            {
                if (metrics.Count >= 1024) throw new InvalidDataException("Metric count limit.");
                metrics.Add(input.ReadBytes().ToByteArray());
            }
            else input.SkipLastField();
        });
        if (scope != EventStoreDomainTelemetry.MeterName("work")) return;
        foreach (byte[] metric in metrics) DecodeMetric(metric, at, points);
    }

    private static void DecodeMetric(byte[] bytes, DateTimeOffset at, List<WorksPlatformMetricPoint> points)
    {
        string? name = null;
        byte[]? gauge = null;
        Fields(bytes, (tag, input) =>
        {
            if (tag == 10) name = input.ReadString();
            else if (tag == 42) gauge = input.ReadBytes().ToByteArray();
            else input.SkipLastField();
        });
        if (name is null || !name.StartsWith(Prefix, StringComparison.Ordinal) || !_names.Contains(name[Prefix.Length..])) return;
        if (gauge is null) throw new InvalidDataException("Recovery instrument is not a gauge.");
        Fields(gauge, (tag, input) =>
        {
            if (tag != 10) { input.SkipLastField(); return; }
            if (points.Count >= 4096) throw new InvalidDataException("Point count limit.");
            points.Add(DecodePoint(name, input.ReadBytes().ToByteArray(), at));
        });
    }

    private static WorksPlatformMetricPoint DecodePoint(string name, byte[] bytes, DateTimeOffset at)
    {
        double? value = null;
        string? reason = null;
        ulong timestamp = 0;
        uint flags = 0;
        Fields(bytes, (tag, input) =>
        {
            switch (tag)
            {
                case 25: timestamp = input.ReadFixed64(); break;
                case 33: value = input.ReadDouble(); break;
                case 49: value = input.ReadSFixed64(); break;
                case 64: flags = input.ReadUInt32(); break;
                case 58:
                    (string? key, string? attributeValue) = DecodeAttribute(input.ReadBytes().ToByteArray());
                    if (key != "reason" || reason is not null || attributeValue is null || !_reasons.Contains(attributeValue))
                        throw new InvalidDataException("Unexpected or unbounded recovery label.");
                    reason = attributeValue;
                    break;
                default: input.SkipLastField(); break;
            }
        });
        if (value is null || !double.IsFinite(value.Value) || value < 0 || timestamp == 0 || (flags & 1) != 0
            || (name == Prefix + "reason") != (reason is not null))
            throw new InvalidDataException("Invalid gauge observation.");
        return new WorksPlatformMetricPoint(name, value.Value, reason, timestamp, at);
    }

    private static (string? Key, string? Value) DecodeAttribute(byte[] bytes)
    {
        string? key = null;
        string? value = null;
        Fields(bytes, (tag, input) =>
        {
            if (tag == 10) key = input.ReadString();
            else if (tag == 18)
                Fields(input.ReadBytes().ToByteArray(), (valueTag, nested) =>
                {
                    if (valueTag == 10) value = nested.ReadString();
                    else nested.SkipLastField();
                });
            else input.SkipLastField();
        });
        return (key, value);
    }

    private static void Fields(byte[] bytes, Action<uint, CodedInputStream> field)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using CodedInputStream input = CodedInputStream.CreateWithLimits(stream, PayloadLimit, 8);
        uint tag;
        while ((tag = input.ReadTag()) != 0) field(tag, input);
    }
}
