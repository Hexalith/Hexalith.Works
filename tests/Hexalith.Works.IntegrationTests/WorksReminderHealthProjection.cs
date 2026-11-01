using System.Net.Http.Json;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.Testing;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exports only known reminder booleans/counts while preserving the actual health status and description.</summary>
internal static class WorksReminderHealthProjection
{
    private const string Reminder = "eventstore-reminders-unresolved";
    private static readonly string[] BooleanFields = ["passCompleted", "passPending", "callbackTokenConfigured"];
    private static readonly string[] CountFields = ["incompleteScans", "unresolvedItems", "unresolved", "quarantined"];

    internal static Task WriteAsync(HttpContext http, HealthReport report)
    {
        var diagnostics = http.RequestServices.GetService<WorksReminderPassDiagnostics>();
        diagnostics?.RecordHealth(report);
        return http.Response.WriteAsJsonAsync(Project(report, diagnostics?.Snapshot()));
    }

    internal static object ProjectReminder(HealthReport report)
    {
        bool present = report.Entries.TryGetValue(Reminder, out HealthReportEntry reminder);
        return new { status = report.Status.ToString(), reminder = !present ? null : new { status = reminder.Status.ToString(),
            description = reminder.Description, data = reminder.Data.Where(pair =>
                BooleanFields.Contains(pair.Key, StringComparer.Ordinal) && pair.Value is bool
                || CountFields.Contains(pair.Key, StringComparer.Ordinal) && pair.Value is int count && count >= 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value) } };
    }

    internal static object Project(HealthReport report, object? reminderPasses = null) => new
    {
        status = report.Status.ToString(),
        reminderPasses,
        results = report.Entries.ToDictionary(entry => entry.Key, entry => new
        {
            status = entry.Value.Status.ToString(), description = entry.Value.Description,
            data = entry.Key == Reminder ? entry.Value.Data.Where(pair =>
                BooleanFields.Contains(pair.Key, StringComparer.Ordinal) && pair.Value is bool
                || CountFields.Contains(pair.Key, StringComparer.Ordinal) && pair.Value is int count && count >= 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value) : [],
        }),
    };

    internal static async Task<string> CaptureStartupAsync(DistributedApplication app, string resource, CancellationToken token)
    {
        using HttpClient client = app.CreateHttpClient(resource);
        client.MaxResponseContentBufferSize = 64 * 1024;
        using HttpResponseMessage response = await client.GetAsync("/ready", token).ConfigureAwait(false);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { observedAt = DateTimeOffset.UtcNow, httpStatus = (int)response.StatusCode,
            status = body.GetProperty("status"), reminder = body.GetProperty("results").GetProperty(Reminder),
            reminderPasses = body.GetProperty("reminderPasses") });
    }

    internal static async Task CapturePostRepairFailureAsync(DistributedApplication app, CancellationToken original)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(original);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            using HttpClient client = app.CreateHttpClient("works");
            client.MaxResponseContentBufferSize = 64 * 1024;
            using HttpResponseMessage response = await client.GetAsync("/ready", deadline.Token).ConfigureAwait(false);
            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(deadline.Token).ConfigureAwait(false);
            if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } path)
            {
                var state = new { httpStatus = (int)response.StatusCode, status = body.GetProperty("status"), reminder = body.GetProperty("results").GetProperty(Reminder),
                    reminderPasses = body.GetProperty("reminderPasses") };
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new { phase = "post-repair-actual-reminder-readiness", observedAt = DateTimeOffset.UtcNow, state })
                    + Environment.NewLine, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* Observation must preserve the original bounded health failure. */ }
    }
}
