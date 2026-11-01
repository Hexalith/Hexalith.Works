using System.Net;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Processes;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Failure evidence preserves state and excludes domain contents and caller coordinates.</summary>
public sealed class WorksProcessFailureEvidenceTests
{
    [Fact]
    public async Task ProjectionKeepsRetryAndPlanFactsWithoutPrivateSourcePayloadOrCursors()
    {
        var source = new ProcessSource("private-tenant", "work", "private-parent");
        var state = new ProcessSourceState(1, source, 6, ProcessSourceStatus.Parked, 10, 7, "translation-timeout",
            "private-owner", DateTimeOffset.UtcNow.AddSeconds(30), 1, 1, DateTimeOffset.UtcNow)
        {
            PendingPlan = new(7, new string('0', 64),
                [new ProcessEffect(EffectKindCatalog.CascadeCancel, "work", "private-child", EffectKindCatalog.PrimaryTargetOrdinal,
                    "private-command-type", Encoding.UTF8.GetBytes("private-command-payload"))]),
        };
        var options = Options.Create(new EventStoreProcessOptions());
        var sweep = await new ProcessSweepStore(new InMemoryReadModelStore(), options, TimeProvider.System)
            .GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        sweep = sweep with { Cycle = 1, Generation = 1, CycleStartedAt = DateTimeOffset.UtcNow, CatalogAfterKey = "private-cursor",
            Reasons = new Dictionary<string, int> { ["private-reason"] = 1 }, Unresolved = 1 };
        var fault = new WorksSdkProofFaultState(true, "private-effect-id") { CausationId = "private-causation", EffectKind = "private-kind" };
        string projection = JsonSerializer.Serialize(WorksProcessFailureEvidence.Project(state, fault, sweep));
        projection.ShouldContain("translation-timeout");
        projection.ShouldContain("Parked");
        projection.ShouldContain("\"attempts\":10");
        projection.ShouldContain("\"plannedEffects\":1");
        projection.ShouldContain("effectIdsSha256");
        projection.ShouldNotContain("private-");

        var store = Substitute.For<IReadModelStore>();
        store.GetAsync<ProcessSourceState>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ReadModelEntry<ProcessSourceState>(state, "1"));
        store.GetAsync<WorksSdkProofFaultState>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ReadModelEntry<WorksSdkProofFaultState>(fault, "1"));
        store.GetAsync<ProcessSweepState>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ReadModelEntry<ProcessSweepState>(sweep, "1"));
        string captured = JsonSerializer.Serialize(await WorksProcessFailureEvidence.CaptureAsync(store, source, TestContext.Current.CancellationToken).ConfigureAwait(true));
        captured.ShouldContain("translation-timeout");
        captured.ShouldNotContain("private-");
        store.ReceivedCalls().ShouldAllBe(call => call.GetMethodInfo().Name == "GetAsync");
    }

    [Fact]
    public async Task HttpTimingForwardsTheSameRequestResponseAndTokenWithoutReadingContent()
    {
        string directory = Directory.CreateTempSubdirectory("works-stream-phase-controls-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string path = Path.Combine(directory, "phases.json");
        try
        {
            var diagnostics = new WorksReminderPhaseDiagnostics(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [WorksReminderPhaseDiagnostics.PathKey] = path }).Build());
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1/api/v1/streams/read/workload")
            { Content = new StringContent("private-request") };
            request.Headers.Add("private-header", "private-credential");
            var result = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("private-response") };
            CancellationToken token = TestContext.Current.CancellationToken;
            using var actual = new WorksSdkHttpEvidenceHandler((forwarded, observedToken) =>
            {
                forwarded.ShouldBeSameAs(request);
                observedToken.ShouldBe(token);
                return Task.FromResult(result);
            });
            using var handler = new WorksGatewayStreamPhaseHandler(diagnostics) { InnerHandler = actual };
            using var invoker = new HttpMessageInvoker(handler);
            using HttpResponseMessage response = await invoker.SendAsync(request, token).ConfigureAwait(true);
            response.ShouldBeSameAs(result);
            (await response.Content.ReadAsStringAsync(token).ConfigureAwait(true)).ShouldBe("private-response");
            string evidence = await File.ReadAllTextAsync(path, token).ConfigureAwait(true);
            evidence.ShouldContain("503");
            evidence.ShouldNotContain("private-");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
