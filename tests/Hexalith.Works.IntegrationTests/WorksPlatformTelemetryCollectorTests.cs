using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;

using Hexalith.EventStore.DomainService;

using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Validates the receiver with actual OTLP exporter bytes and bounded invalid HTTP input.</summary>
public sealed class WorksPlatformTelemetryCollectorTests
{
    /// <summary>Actual canonical SDK meter exports are decoded with timestamps and bounded reason labels.</summary>
    [Fact]
    public async Task RealOtlpExporterPreservesAlertTransitionsAndReasonLabels()
    {
        WorksPlatformTelemetryCollector collector = await WorksPlatformTelemetryCollector.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await using var cleanup = collector.ConfigureAwait(true);
        using var diagnostics = new EventStoreDomainDiagnostics("work");
        long alert = 1;
        _ = diagnostics.Meter.CreateObservableGauge("eventstore.domain.recovery.alert", () => Interlocked.Read(ref alert));
        _ = diagnostics.Meter.CreateObservableGauge("eventstore.domain.recovery.reason", () =>
            new System.Diagnostics.Metrics.Measurement<long>(2, new KeyValuePair<string, object?>("reason", "source-incomplete")));
        using MeterProvider provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(EventStoreDomainTelemetry.MeterName("work"))
            .AddOtlpExporter(options =>
            {
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Endpoint = new Uri(collector.Endpoint, "v1/metrics");
            }).Build();
        provider.ForceFlush(10000).ShouldBeTrue();
        collector.Points.ShouldContain(point => point.Name == "eventstore.domain.recovery.alert" && point.Value == 1 && point.Reason == null && point.UnixTimeNanoseconds > 0);
        Interlocked.Exchange(ref alert, 0);
        provider.ForceFlush(10000).ShouldBeTrue();
        collector.Points.ShouldContain(point => point.Name == "eventstore.domain.recovery.alert" && point.Value == 0);
        collector.Points.ShouldContain(point => point.Name == "eventstore.domain.recovery.reason" && point.Value == 2 && point.Reason == "source-incomplete");
        collector.Failures.ShouldBeEmpty();
    }

    /// <summary>Mixed exporter requests with forbidden labels or instrument shapes receive HTTP refusal without retaining any points.</summary>
    [Theory]
    [InlineData("forbidden-attribute")]
    [InlineData("unknown-reason")]
    [InlineData("reason-on-alert")]
    [InlineData("unlabeled-reason")]
    [InlineData("counter")]
    public async Task RealOtlpExporterCannotAdmitForbiddenRecoveryEvidence(string scenario)
    {
        WorksPlatformTelemetryCollector collector = await WorksPlatformTelemetryCollector.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await using var cleanup = collector.ConfigureAwait(true);
        using var diagnostics = new EventStoreDomainDiagnostics("work");
        string canary = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        // Create the valid gauge first, then verify its wire position precedes the invalid instrument.
        const string validName = "eventstore.domain.recovery.ready";
        _ = diagnostics.Meter.CreateObservableGauge(validName, () => 1L);
        var observed = new TaskCompletionSource<(HttpStatusCode Status, byte[] Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var exporterClient = new HttpClient(new WorksPlatformTelemetryEvidenceHandler(
            (status, body) => observed.TrySetResult((status, body))));
        KeyValuePair<string, object?>[] labels = scenario switch
        {
            "forbidden-attribute" => [new("tenantId", canary)],
            "unknown-reason" => [new("reason", canary)],
            "reason-on-alert" => [new("reason", "source-incomplete")],
            _ => [],
        };
        string name = "eventstore.domain.recovery." + (scenario is "unknown-reason" or "unlabeled-reason" ? "reason" : "alert");
        Counter<long>? counter = scenario == "counter" ? diagnostics.Meter.CreateCounter<long>(name) : null;
        if (counter is null) _ = diagnostics.Meter.CreateObservableGauge(name, () => new Measurement<long>(1, labels));
        using MeterProvider provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(EventStoreDomainTelemetry.MeterName("work"))
            .AddOtlpExporter(options =>
            {
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Endpoint = new Uri(collector.Endpoint, "v1/metrics");
                options.HttpClientFactory = () => exporterClient;
            }).Build();
        counter?.Add(1);

        _ = provider.ForceFlush(10000);
        (HttpStatusCode status, byte[] body) = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        status.ShouldBe(HttpStatusCode.BadRequest);
        int validPosition = body.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(validName));
        int invalidPosition = body.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(name));
        validPosition.ShouldBeGreaterThanOrEqualTo(0);
        invalidPosition.ShouldBeGreaterThan(validPosition);
        collector.Failures.ShouldContain("invalid-recovery-otlp");
        collector.Points.ShouldBeEmpty();
        collector.Failures.ShouldNotContain(value => value.Contains(canary, StringComparison.Ordinal));
    }

    /// <summary>Oversized, invalid protobuf and nonmetrics routes cannot become observations.</summary>
    [Fact]
    public async Task OversizedOrMalformedRequestsDoNotProduceEvidence()
    {
        WorksPlatformTelemetryCollector collector = await WorksPlatformTelemetryCollector.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await using var cleanup = collector.ConfigureAwait(true);
        using HttpClient client = new() { BaseAddress = collector.Endpoint };
        using var huge = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]);
        huge.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        using HttpResponseMessage oversized = await client.PostAsync("v1/metrics", huge, TestContext.Current.CancellationToken);
        oversized.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        using var invalid = new ByteArrayContent([10, 255]);
        invalid.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        using HttpResponseMessage malformed = await client.PostAsync("v1/metrics", invalid, TestContext.Current.CancellationToken);
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using HttpResponseMessage other = await client.GetAsync("v1/traces", TestContext.Current.CancellationToken);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        collector.Points.ShouldBeEmpty();
        collector.Failures.ShouldContain("payload-limit");
        collector.Failures.ShouldContain("invalid-recovery-otlp");
    }
}
