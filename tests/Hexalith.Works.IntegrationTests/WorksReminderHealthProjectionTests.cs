using System.Security.Cryptography;
using System.Text;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.ServiceDefaults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text.Json;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksReminderHealthProjectionTests
{
    [Fact]
    public async Task ActualWorksProofRegistrationWritesTheActualStatusAndBoundedPassCountsWithoutGatewayMetadataChanges()
    {
        using RSA rsa = RSA.Create(2048);
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration)
            .AddSingleton(Substitute.For<IReadModelStore>());
        var context = new WebHostBuilderContext { Configuration = configuration, HostingEnvironment = Substitute.For<IWebHostEnvironment>() };
        WorksSdkProofHostingStartup.ConfigureProofServices(context, services, "works", Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var writer = provider.GetRequiredService<IOptions<EventStoreServiceDefaultsOptions>>().Value.DevelopmentHealthResponseWriter;
        writer.ShouldNotBeNull();
        services.ShouldNotContain(descriptor => descriptor.ImplementationType == typeof(WorksSdkProofMetadataFilter));
        var pass = provider.GetServices<ILoggerProvider>().OfType<WorksReminderPassDiagnostics>().ShouldHaveSingleItem();
        pass.CreateLogger(WorksReminderPassDiagnostics.Category).LogInformation(new EventId(200210),
            "Tenants={Tenants}, Candidates={Candidates}, Armed={Armed}, Submitted={Submitted}, Cancelled={Cancelled}, Unresolved={Unresolved}, Quarantined={Quarantined}, Incomplete={Incomplete}",
            1, 12, 0, 0, 0, 0, 0, 0);
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["eventstore-reminders-unresolved"] = new(HealthStatus.Degraded, "Actual pending pass.", TimeSpan.Zero, null,
                new Dictionary<string, object> { ["passPending"] = true, ["passCompleted"] = true, ["incompleteScans"] = 1 }),
        }, TimeSpan.Zero);
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        using var body = new MemoryStream(); http.Response.Body = body;
        await writer(http, report).ConfigureAwait(true);
        http.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        using var observed = JsonDocument.Parse(Encoding.UTF8.GetString(body.ToArray()));
        observed.RootElement.GetProperty("status").GetString().ShouldBe("Degraded");
        observed.RootElement.GetProperty("results").GetProperty("eventstore-reminders-unresolved").GetProperty("data").GetProperty("passPending").GetBoolean().ShouldBeTrue();
        observed.RootElement.GetProperty("reminderPasses").GetProperty("total").GetInt32().ShouldBe(1);
        observed.RootElement.GetProperty("reminderPasses").GetProperty("last")[0].GetProperty("counts").GetProperty("Incomplete").GetInt32().ShouldBe(0);
    }

    [Fact]
    public void ProjectionPreservesActualStatusAndDescriptionAndOnlyKnownTypedReminderCounters()
    {
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["eventstore-reminders-unresolved"] = new(HealthStatus.Degraded, "Actual pending pass.", TimeSpan.Zero, exception: null, data: new Dictionary<string, object>
            {
                ["passCompleted"] = true, ["passPending"] = true, ["incompleteScans"] = 1,
                ["unresolvedItems"] = 2, ["unresolved"] = 3, ["quarantined"] = 0, ["callbackTokenConfigured"] = true,
                ["secret"] = "private-key", ["lastCursor"] = "private-source", ["extra"] = new { payload = "private-payload" },
            }),
            ["other-sdk-check"] = new(HealthStatus.Healthy, "Other unchanged description.", TimeSpan.Zero, exception: null, data: new Dictionary<string, object>
            { ["passCompleted"] = "private-unrelated-data", ["secret"] = "private-key" }),
        }, TimeSpan.Zero);
        string json = JsonSerializer.Serialize(WorksReminderHealthProjection.Project(report));
        using var actual = JsonDocument.Parse(json);
        actual.RootElement.GetProperty("status").GetString().ShouldBe("Degraded");
        var reminder = actual.RootElement.GetProperty("results").GetProperty("eventstore-reminders-unresolved");
        reminder.GetProperty("status").GetString().ShouldBe("Degraded");
        reminder.GetProperty("description").GetString().ShouldBe("Actual pending pass.");
        reminder.GetProperty("data").GetProperty("passPending").GetBoolean().ShouldBeTrue();
        reminder.GetProperty("data").GetProperty("unresolved").GetInt32().ShouldBe(3);
        reminder.GetProperty("data").EnumerateObject().Count().ShouldBe(7);
        actual.RootElement.GetProperty("results").GetProperty("other-sdk-check").GetProperty("data").EnumerateObject().ShouldBeEmpty();
        json.ShouldNotContain("private-"); json.ShouldNotContain("lastCursor");
    }

    [Fact]
    public void UnknownValueShapesCannotSmuggleDataThroughKnownFieldNames()
    {
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["eventstore-reminders-unresolved"] = new(HealthStatus.Degraded, "Unchanged.", TimeSpan.Zero, exception: null, data: new Dictionary<string, object>
            { ["passPending"] = "private-key", ["unresolved"] = new { secret = "private-key" }, ["incompleteScans"] = -1 }),
        }, TimeSpan.Zero);
        string json = JsonSerializer.Serialize(WorksReminderHealthProjection.Project(report));
        using var actual = JsonDocument.Parse(json);
        actual.RootElement.GetProperty("results").GetProperty("eventstore-reminders-unresolved").GetProperty("data").EnumerateObject().ShouldBeEmpty();
        json.ShouldNotContain("private-");
    }

    [Fact]
    public void ProcessProjectionRetainsActualRetryReasonsAndCountsWithoutExposingPrivateState()
    {
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["eventstore-processes-unresolved"] = new(HealthStatus.Degraded, "Actual retained process failure.", TimeSpan.Zero, null,
                new Dictionary<string, object>
                {
                    ["complete"] = true, ["unresolved"] = 1, ["cycle"] = 28L, ["retainedCandidates"] = 12L,
                    ["pendingDispositions"] = 0, ["pendingPublications"] = 0, ["pendingTransitions"] = 1, ["pendingObservations"] = 0,
                    ["reasonCounts"] = new Dictionary<string, int> { ["effect-unknown"] = 1, ["private-reason"] = 2 },
                    ["secret"] = "private-key", ["source"] = new { payload = "private-payload" },
                }),
        }, TimeSpan.Zero);
        string json = JsonSerializer.Serialize(WorksReminderHealthProjection.Project(report));
        using var document = JsonDocument.Parse(json);
        JsonElement process = document.RootElement.GetProperty("results").GetProperty("eventstore-processes-unresolved");
        process.GetProperty("status").GetString().ShouldBe("Degraded");
        process.GetProperty("description").GetString().ShouldBe("Actual retained process failure.");
        JsonElement data = process.GetProperty("data");
        data.GetProperty("complete").GetBoolean().ShouldBeTrue();
        data.GetProperty("cycle").GetInt64().ShouldBe(28);
        data.GetProperty("unresolved").GetInt32().ShouldBe(1);
        data.GetProperty("pendingTransitions").GetInt32().ShouldBe(1);
        data.GetProperty("reasonCounts").GetProperty("effect-unknown").GetInt32().ShouldBe(1);
        data.GetProperty("reasonCounts").EnumerateObject().Count().ShouldBe(2);
        json.ShouldNotContain("private-");
    }

    [Fact]
    public void ProcessProjectionRejectsInvalidCounterAndReasonShapes()
    {
        var data = new Dictionary<string, object>
        {
            ["complete"] = "private-state", ["unresolved"] = -1, ["cycle"] = "private-cursor",
            ["reasonCounts"] = new Dictionary<string, object> { ["effect-unknown"] = "private-secret" },
        };
        WorksProcessFailureEvidence.ProjectHealthData(data).ShouldBeEmpty();
    }


    [Fact]
    public void CappedProcessReasonsAreDeterministicAndReportOmissions()
    {
        var reasons = Enumerable.Range(0, 20).ToDictionary(index => "private-reason-" + index.ToString("D2"), index => index);
        var reversed = reasons.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        string Project(Dictionary<string, int> selected) => JsonSerializer.Serialize(WorksProcessFailureEvidence.ProjectHealthData(
            new Dictionary<string, object> { ["reasonCounts"] = selected }));
        string first = Project(reasons);
        first.ShouldBe(Project(reversed));
        using var result = JsonDocument.Parse(first);
        result.RootElement.GetProperty("reasonCounts").EnumerateObject().Count().ShouldBe(16);
        result.RootElement.GetProperty("omittedReasonCount").GetInt32().ShouldBe(4);
        first.ShouldNotContain("private-reason");
    }

}
