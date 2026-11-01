using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksReminderPassDiagnosticsTests
{
    [Fact]
    public void OnlyActualCategoryEventAndTypedCountsAreRetainedInBoundedTimestampedSamples()
    {
        using var observer = new WorksReminderPassDiagnostics();
        var values = Counts(); values.Add(new("private-source", "private-secret-payload"));
        var logger = observer.CreateLogger(WorksReminderPassDiagnostics.Category);
        observer.CreateLogger("foreign-category").Log(LogLevel.Information, new EventId(200210), values, null, Format);
        logger.Log(LogLevel.Information, new EventId(200211), values, null, Format);
        logger.Log(LogLevel.Warning, new EventId(200210), values, null, Format);
        for (int i = 0; i < 100; i++) logger.Log(LogLevel.Information, new EventId(200210), values, new IOException("private-secret"), Format);
        string json = JsonSerializer.Serialize(observer.Snapshot());
        using var result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("total").GetInt32().ShouldBe(100);
        result.RootElement.GetProperty("first").GetArrayLength().ShouldBe(4);
        result.RootElement.GetProperty("last").GetArrayLength().ShouldBe(16);
        result.RootElement.GetProperty("last")[0].GetProperty("counts").EnumerateObject().Count().ShouldBe(8);
        result.RootElement.GetProperty("last")[0].GetProperty("observedAt").GetDateTimeOffset().ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
        json.ShouldNotContain("private-");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("type")]
    [InlineData("duplicate")]
    public void MalformedCountsCannotBePresentedAsSuccessfulSdkPassEvidence(string fault)
    {
        using var observer = new WorksReminderPassDiagnostics(); var values = Counts();
        if (fault == "missing") values.RemoveAt(0);
        if (fault == "negative") values[0] = new("Tenants", -1);
        if (fault == "type") values[0] = new("Tenants", "private-key");
        if (fault == "duplicate") values.Add(new("Tenants", 1));
        observer.Log(LogLevel.Information, new EventId(200210), values, null, Format);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(observer.Snapshot()));
        result.RootElement.GetProperty("total").GetInt32().ShouldBe(1);
        result.RootElement.GetProperty("malformed").GetInt32().ShouldBe(1);
        result.RootElement.GetProperty("last").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task RetainedSnapshotIsPrivateAndDiagnosticIoFailurePreservesActualHttpStatusAndBody()
    {
        string directory = Directory.CreateTempSubdirectory("works-pass-observer-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string path = Path.Combine(directory, "passes.json");
            using var observer = new WorksReminderPassDiagnostics(path);
            observer.Log(LogLevel.Information, new EventId(200210), Counts(), null, Format);
            if (!OperatingSystem.IsWindows()) File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var prior = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true));
            prior.RootElement.GetProperty("total").GetInt32().ShouldBe(1);
            // The original pass snapshot remains available if an atomic replacement is obstructed.
            await File.WriteAllTextAsync(path + ".writing", "private-obstruction", TestContext.Current.CancellationToken).ConfigureAwait(true);
            var report = new HealthReport(new Dictionary<string, HealthReportEntry>
            {
                ["eventstore-reminders-unresolved"] = new(HealthStatus.Degraded, "Actual pending pass.", TimeSpan.Zero, null,
                    new Dictionary<string, object> { ["passPending"] = true, ["incompleteScans"] = 1, ["secret"] = "private-key" }),
            }, TimeSpan.Zero);
            using var services = new ServiceCollection().AddSingleton(observer).BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = services }; http.Response.StatusCode = 503;
            using var body = new MemoryStream(); http.Response.Body = body;
            await WorksReminderHealthProjection.WriteAsync(http, report).ConfigureAwait(true);
            http.Response.StatusCode.ShouldBe(503);
            string response = Encoding.UTF8.GetString(body.ToArray()); response.ShouldNotContain("private-");
            using var observed = JsonDocument.Parse(response);
            observed.RootElement.GetProperty("status").GetString().ShouldBe("Degraded");
            observed.RootElement.GetProperty("reminderPasses").GetProperty("observationFailures").GetInt32().ShouldBe(1);
            using var retained = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true));
            retained.RootElement.GetProperty("total").GetInt32().ShouldBe(1);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void MissingReminderCheckIsRecordedAsAbsentRatherThanManufacturedHealth()
    {
        using var observer = new WorksReminderPassDiagnostics();
        observer.RecordHealth(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(observer.Snapshot()));
        result.RootElement.GetProperty("health").GetProperty("readiness").GetProperty("reminder").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static List<KeyValuePair<string, object?>> Counts() => [new("Tenants", 1), new("Candidates", 12), new("Armed", 0),
        new("Submitted", 0), new("Cancelled", 0), new("Unresolved", 0), new("Quarantined", 0), new("Incomplete", 0)];
    private static string Format<T>(T state, Exception? exception) => throw new InvalidOperationException("Diagnostic formatter must never read private messages.");
}
