using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Reminders;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Diagnostic instrumentation must preserve the actual source and transport semantics.</summary>
public sealed class WorksReminderPhaseTests
{
    [Fact]
    public async Task SourceForwardsTheExactTargetTokenAndResultWithoutRetainingTheirPrivateContents()
    {
        string directory = PrivateDirectory(), path = Path.Combine(directory, "phases.json");
        try
        {
            var source = Substitute.For<IReminderIntentSource>();
            var target = new ReminderTarget("fixture-private-tenant", "work", "private-source");
            IReadOnlyList<ReminderIntent> result = new List<ReminderIntent>();
            CancellationToken token = TestContext.Current.CancellationToken;
            source.GetCurrentIntentsAsync(target, token).Returns(result);
            var wrapper = new WorksProofReminderIntentSource(source, Diagnostics(path));
            (await wrapper.GetCurrentIntentsAsync(target, token).ConfigureAwait(true)).ShouldBeSameAs(result);
            source.ReceivedCalls().Single().GetArguments()[0].ShouldBeSameAs(target);
            var intent = new ReminderIntent(target.Tenant, target.Domain, target.Aggregate, DateTimeOffset.UtcNow,
                "fixture", "FixtureCommand", [], target.Domain, target.Aggregate, 1, 1);
            var translated = new ReminderCommand("FixtureCommand", [1]);
            source.TranslateDueIntent(intent).Returns(translated);
            wrapper.TranslateDueIntent(intent).ShouldBeSameAs(translated);
            string observed = await File.ReadAllTextAsync(path, token).ConfigureAwait(true);
            observed.ShouldContain("intent-read");
            observed.ShouldContain("completed");
            observed.ShouldNotContain("private-");
            source.GetCurrentIntentsAsync(target, token).Returns(Task.FromResult<IReadOnlyList<ReminderIntent>>(null!));
            (await wrapper.GetCurrentIntentsAsync(target, token).ConfigureAwait(true)).ShouldBeNull("diagnostics must not turn unavailable source evidence into an empty successful fold");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SourceRethrowsTheSameFailureAndRecordsOnlyTypesAndCodeMethods()
    {
        string directory = PrivateDirectory(), path = Path.Combine(directory, "phases.json");
        try
        {
            var source = Substitute.For<IReminderIntentSource>();
            var target = new ReminderTarget("fixture", "work", "private-source");
            var failure = new InvalidOperationException("private-bearer-and-payload", new IOException("private-secret"));
            source.GetCurrentIntentsAsync(target, Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<ReminderIntent>>(failure));
            var wrapper = new WorksProofReminderIntentSource(source, Diagnostics(path));
            var actual = await Should.ThrowAsync<InvalidOperationException>(() => wrapper.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken)).ConfigureAwait(true);
            actual.ShouldBeSameAs(failure);
            string observed = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            observed.ShouldContain(nameof(InvalidOperationException));
            observed.ShouldContain(nameof(IOException));
            observed.ShouldNotContain("private-");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("Hexalith.Works.Recovery.ProcessEvidenceUnavailableException", true)]
    [InlineData("private-secret-and-payload", false)]
    public void ActorFailureProjectsOnlyAValidatedRemoteTypeAndNeverItsMessage(string remoteType, bool visible)
    {
        var failure = new Dapr.Actors.ActorInvokeException(remoteType, "private-bearer-payload-canary");
        string shape = JsonSerializer.Serialize(WorksReminderPhaseDiagnostics.ExceptionShape(failure));
        shape.ShouldContain(nameof(Dapr.Actors.ActorInvokeException));
        shape.ShouldNotContain("private-bearer-payload-canary");
        if (visible) shape.ShouldContain(remoteType);
        else shape.ShouldNotContain(remoteType);
        string response = WorksReminderProofRequests.DescribeFailure(Encoding.UTF8.GetBytes(shape));
        response.ShouldNotContain("private-bearer-payload-canary");
        if (visible) response.ShouldContain(remoteType);
        else response.ShouldNotContain(remoteType);
    }

    [Fact]
    public void DiagnosticFirstLastAndOutstandingInventoriesAreBounded()
    {
        string directory = PrivateDirectory(), path = Path.Combine(directory, "phases.json");
        try
        {
            var diagnostics = Diagnostics(path);
            for (int index = 0; index < 40; index++) diagnostics.End(diagnostics.Begin("intent-read", "private-source"), null);
            for (int index = 0; index < 70; index++) _ = diagnostics.Begin("actor-http", "private-source");
            using JsonDocument record = JsonDocument.Parse(File.ReadAllBytes(path));
            record.RootElement.GetProperty("first").GetArrayLength().ShouldBe(8);
            record.RootElement.GetProperty("last").GetArrayLength().ShouldBe(16);
            record.RootElement.GetProperty("pending").GetArrayLength().ShouldBe(64);
            Encoding.UTF8.GetString(File.ReadAllBytes(path)).ShouldNotContain("private-source");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ActorObserverForwardsRequestAndResponseWithoutReadingBodiesOrChangingPolicy()
    {
        string directory = PrivateDirectory(), path = Path.Combine(directory, "phases.json");
        try
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            var filter = new WorksReminderPhaseFilter(Diagnostics(path));
            filter.Configure(builder => builder.Run(context => context.Response.WriteAsync("unchanged-response", context.RequestAborted)))(app);
            var context = new DefaultHttpContext();
            context.Request.Path = "/actors/WorksSdkReminderActor/private-actor/method/converge";
            context.Request.Headers["private-test-input"] = "unchanged";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("private-request"));
            context.Response.Body = new MemoryStream();
            await app.Build()(context).ConfigureAwait(true);
            context.Request.Body.Position.ShouldBe(0);
            context.Request.Headers["private-test-input"].ToString().ShouldBe("unchanged");
            Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()).ShouldBe("unchanged-response");
            string observed = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            observed.ShouldContain("actor-http");
            observed.ShouldNotContain("private-");
            observed.ShouldNotContain("unchanged-response");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ActualHostDependencyInjectionCanConstructTheValidatedObserver()
    {
        string directory = PrivateDirectory(), path = Path.Combine(directory, "phases.json");
        try
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [WorksReminderPhaseDiagnostics.PathKey] = path }).Build();
            using ServiceProvider services = new ServiceCollection().AddSingleton(configuration)
                .AddSingleton<WorksReminderPhaseDiagnostics>().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
            var diagnostics = services.GetRequiredService<WorksReminderPhaseDiagnostics>();
            diagnostics.End(diagnostics.Begin("intent-read", "private-source"), null);
            File.Exists(path).ShouldBeTrue();
            diagnostics.ObservationFailures.ShouldBe(0);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDiagnosticWritesPreserveTheActualSourceResultOrFailure(bool fails)
    {
        string directory = PrivateDirectory(), path = Path.Combine(directory, "phases.json");
        try
        {
            var diagnostics = Diagnostics(path);
            await File.WriteAllBytesAsync(path + ".writing", [], TestContext.Current.CancellationToken).ConfigureAwait(true);
            var source = Substitute.For<IReminderIntentSource>();
            var target = new ReminderTarget("fixture", "work", "private-source");
            var failure = new IOException("private-source-failure");
            IReadOnlyList<ReminderIntent> result = [];
            source.GetCurrentIntentsAsync(target, Arg.Any<CancellationToken>()).Returns(fails
                ? Task.FromException<IReadOnlyList<ReminderIntent>>(failure) : Task.FromResult(result));
            var wrapper = new WorksProofReminderIntentSource(source, diagnostics);
            if (fails)
                (await Should.ThrowAsync<IOException>(() => wrapper.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken)).ConfigureAwait(true)).ShouldBeSameAs(failure);
            else
                (await wrapper.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBeSameAs(result);
            diagnostics.ObservationFailures.ShouldBe(2);
            source.ReceivedCalls().Count().ShouldBe(1);
            File.Exists(path).ShouldBeFalse();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDiagnosticWritesPreserveTheActualActorResponseOrFailure(bool fails)
    {
        string directory = PrivateDirectory(), path = Path.Combine(directory, "phases.json");
        try
        {
            var diagnostics = Diagnostics(path);
            await File.WriteAllBytesAsync(path + ".writing", [], TestContext.Current.CancellationToken).ConfigureAwait(true);
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            var failure = new IOException("private-actor-failure");
            new WorksReminderPhaseFilter(diagnostics).Configure(builder => builder.Run(context => fails
                ? Task.FromException(failure) : context.Response.WriteAsync("unchanged-response", context.RequestAborted)))(app);
            var context = new DefaultHttpContext();
            context.Request.Path = "/actors/WorksSdkReminderActor/private-actor/method/converge";
            context.Response.Body = new MemoryStream();
            if (fails)
                (await Should.ThrowAsync<IOException>(() => app.Build()(context)).ConfigureAwait(true)).ShouldBeSameAs(failure);
            else
            {
                await app.Build()(context).ConfigureAwait(true);
                Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()).ShouldBe("unchanged-response");
            }
            diagnostics.ObservationFailures.ShouldBe(2);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void MissingInitialPrivateDirectoryStillRefusesDiagnosticSetup()
    {
        string directory = PrivateDirectory();
        try { Should.Throw<InvalidOperationException>(() => Diagnostics(Path.Combine(directory, "missing", "phases.json"))); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static WorksReminderPhaseDiagnostics Diagnostics(string path)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WorksReminderPhaseDiagnostics.PathKey] = path }).Build());

    private static string PrivateDirectory()
    {
        string directory = Directory.CreateTempSubdirectory("works-reminder-phase-controls-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }
}
