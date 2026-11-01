using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksProcessTranslationDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualTranslatorKeepsSuccessfulOutcomeWhenDiagnosticsCannotWrite(bool obstruct)
    {
        string directory = Directory.CreateTempSubdirectory("works-translation-diagnostics-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string path = Path.Combine(directory, "translation.json");
            if (obstruct) await File.WriteAllTextAsync(path + ".writing", "private-canary", TestContext.Current.CancellationToken);
            using var diagnostics = new WorksProcessTranslationDiagnostics(path);
            using var loggers = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(diagnostics));
            var gateway = Substitute.For<IEventStoreGatewayClient>();
            var page = Story48Streams.Page("private-tenant", "private-item", new WorkItemCreated("private-item", 1,
                new TenantId("private-tenant"), new WorkItemId("private-item"), new Obligation("private-obligation")));
            var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())), loggers.CreateLogger<WorksProcessTranslator>());
            (await translator.TranslateAsync(new("private-tenant", "work", "private-item"), page.Events[0], TestContext.Current.CancellationToken)).ShouldBeEmpty();
            gateway.ReceivedCalls().ShouldBeEmpty();
            using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(diagnostics.Snapshot()));
            snapshot.RootElement.GetProperty("last")[0].GetProperty("outcome").GetString().ShouldBe("completed");
            snapshot.RootElement.GetProperty("last")[0].GetProperty("effects").GetInt32().ShouldBe(0);
            JsonSerializer.Serialize(diagnostics.Snapshot()).ShouldNotContain("private-");
            if (obstruct) snapshot.RootElement.GetProperty("observationFailures").GetInt64().ShouldBeGreaterThan(0);
            else if (!OperatingSystem.IsWindows()) File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ActualSourceFailureRemainsTheSameExceptionAndNeverBecomesDiagnosticContent()
    {
        using var diagnostics = new WorksProcessTranslationDiagnostics();
        using var loggers = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(diagnostics));
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        var failure = new TimeoutException("private-credential-payload-canary");
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<StreamReadPage>(failure));
        var page = Story48Streams.Page("private-tenant", "private-item", new WorkItemCreated("private-item", 1,
            new TenantId("private-tenant"), new WorkItemId("private-item"), new Obligation("private-obligation")),
            new WorkItemCancelled("private-item", 2, new TenantId("private-tenant"), new WorkItemId("private-item")));
        var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())), loggers.CreateLogger<WorksProcessTranslator>());
        var actual = await Should.ThrowAsync<TimeoutException>(async () => await translator.TranslateAsync(new("private-tenant", "work", "private-item"), page.Events[1], TestContext.Current.CancellationToken).ConfigureAwait(false));
        actual.ShouldBeSameAs(failure);
        string snapshot = JsonSerializer.Serialize(diagnostics.Snapshot()); snapshot.ShouldNotContain("private-");
        using var captured = JsonDocument.Parse(snapshot);
        captured.RootElement.GetProperty("last")[0].GetProperty("kind").GetString().ShouldBe(nameof(WorkItemCancelled));
        captured.RootElement.GetProperty("last")[0].GetProperty("outcome").GetString().ShouldBe("failed");
    }

    [Fact]
    public void ActualAdapterDescriptorRemainsTheExactSdkRegisteredConcreteType()
    {
        var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(new WorksProcessTranslationDiagnostics()));
        services.AddWorksReminderAndCascadeRecovery(new ConfigurationBuilder().Build());
        // This DI composition control supplies no transport; HTTP/auth/catalogue execution
        // remains qualified by the separate actual-host lanes.
        services.AddSingleton(Substitute.For<IEventStoreGatewayClient>());
        var descriptor = services.Single(service => service.ServiceType == typeof(IProcessTranslator));
        descriptor.ImplementationType.ShouldBe(typeof(WorksProcessTranslator));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IProcessTranslator>().ShouldBeOfType<WorksProcessTranslator>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingLoggerCannotChangeTheActualAdapterOutcome(bool failDuringEnablement)
    {
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        var page = Story48Streams.Page("private-tenant", "private-item", new WorkItemCreated("private-item", 1,
            new TenantId("private-tenant"), new WorkItemId("private-item"), new Obligation("private-obligation")));
        var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())), new ThrowingLogger(failDuringEnablement));
        (await translator.TranslateAsync(new("private-tenant", "work", "private-item"), page.Events[0], TestContext.Current.CancellationToken)).ShouldBeEmpty();
        var actual = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await translator.TranslateAsync(new("private-tenant", "foreign-domain", "private-item"), page.Events[0], TestContext.Current.CancellationToken).ConfigureAwait(false));
        actual.Message.ShouldBe("Works process evidence has invalid scope or encoding.");
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellationWithoutDispatchOrSyntheticSuccess()
    {
        using var diagnostics = new WorksProcessTranslationDiagnostics();
        using var loggers = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(diagnostics));
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        var page = Story48Streams.Page("private-tenant", "private-item", new WorkItemCreated("private-item", 1,
            new TenantId("private-tenant"), new WorkItemId("private-item"), new Obligation("private-obligation")),
            new WorkItemCancelled("private-item", 2, new TenantId("private-tenant"), new WorkItemId("private-item")));
        var translator = new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions())), loggers.CreateLogger<WorksProcessTranslator>());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await translator.TranslateAsync(new("private-tenant", "work", "private-item"), page.Events[1], cancellation.Token).ConfigureAwait(false));
        gateway.ReceivedCalls().ShouldBeEmpty();
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(diagnostics.Snapshot()));
        snapshot.RootElement.GetProperty("last")[0].GetProperty("outcome").GetString().ShouldBe("cancelled");
        snapshot.RootElement.GetProperty("failures").GetArrayLength().ShouldBe(1);
    }

    private sealed class ThrowingLogger(bool failDuringEnablement) : ILogger<WorksProcessTranslator>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => failDuringEnablement ? throw new IOException("private-log-failure") : true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new IOException("private-log-failure");
    }

    [Fact]
    public void OnlyActualTypedEventsAreObservedWithoutCallingFormattersOrRetainingUnknownFields()
    {
        using var diagnostics = new WorksProcessTranslationDiagnostics();
        var logger = diagnostics.CreateLogger(WorksProcessTranslationDiagnostics.Category);
        for (int i = 1; i <= 100; i++)
        {
            var start = new List<KeyValuePair<string, object?>> { new("Observation", (long)i), new("Kind", "WorkItemCancelled"), new("private-canary", "private-secret") };
            logger.Log(LogLevel.Debug, new EventId(290101), start, null, ForbiddenFormatter);
            var ended = new List<KeyValuePair<string, object?>>(start) { new("Milliseconds", 12.5), new("Outcome", "completed"), new("Effects", 5) };
            logger.Log(LogLevel.Debug, new EventId(290102), ended, new IOException("private-secret"), ForbiddenFormatter);
        }
        diagnostics.CreateLogger("foreign").Log(LogLevel.Debug, new EventId(290101), new[] { new KeyValuePair<string, object?>("Observation", 1L) }, null, ForbiddenFormatter);
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(diagnostics.Snapshot()));
        snapshot.RootElement.GetProperty("total").GetInt64().ShouldBe(100);
        snapshot.RootElement.GetProperty("first").GetArrayLength().ShouldBe(8);
        snapshot.RootElement.GetProperty("last").GetArrayLength().ShouldBe(32);
        snapshot.RootElement.GetProperty("pending").GetArrayLength().ShouldBe(0);
        snapshot.RootElement.GetProperty("malformed").GetInt64().ShouldBe(0);
        snapshot.RootElement.GetRawText().ShouldNotContain("private-");
    }

    private static string ForbiddenFormatter<T>(T state, Exception? exception) => throw new InvalidOperationException("The diagnostic observer must not format messages.");
}
