using System.Text.Json;

using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Passive sampling leaves the actual SDK failure and authority untouched.</summary>
public sealed class WorksCatalogFirstChanceObserverTests
{
    [Fact]
    public async Task DiagnosticWriteFailureCannotReplaceTheRealSdkRefusal()
    {
        string directory = Directory.CreateTempSubdirectory("works-first-chance-io-controls-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string path = Path.Combine(directory, "refusals.json");
        try
        {
            File.WriteAllText(path + ".writing", "occupied private diagnostic slot");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WorksCatalogFirstChanceObserver.PathKey] = path }).Build();
            using var observer = new WorksCatalogFirstChanceObserver(configuration);
            var actualSdk = new RouteCatalogAuthority(Substitute.For<IRouteCatalogStateStore>(), Substitute.For<IRouteCatalogTrustValidator>(), Options.Create(new RouteCatalogHostOptions()));
            await observer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            await Should.ThrowAsync<RouteCatalogUnavailableException>(() => actualSdk.AcquireAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
            observer.ObservationFailures.ShouldBeGreaterThan(0);
            await observer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ActualSdkRefusalIsObservedWithoutMessagesAndUnsubscribedAtStop()
    {
        string directory = Directory.CreateTempSubdirectory("works-first-chance-controls-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string path = Path.Combine(directory, "refusals.json");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WorksCatalogFirstChanceObserver.PathKey] = path }).Build();
            using var observer = new WorksCatalogFirstChanceObserver(configuration);
            var store = Substitute.For<IRouteCatalogStateStore>();
            var trust = Substitute.For<IRouteCatalogTrustValidator>();
            var actualSdk = new RouteCatalogAuthority(store, trust, Options.Create(new RouteCatalogHostOptions()));
            await observer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            RouteCatalogUnavailableException failure = await Should.ThrowAsync<RouteCatalogUnavailableException>(() => actualSdk.AcquireAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
            string observation = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            observation.ShouldContain("RouteCatalogAuthority");
            observation.ShouldContain("RequireLocalIdentity");
            observation.ShouldNotContain(failure.Message);
            using JsonDocument parsed = JsonDocument.Parse(observation);
            parsed.RootElement.GetProperty("sites").EnumerateObject().Count().ShouldBeLessThanOrEqualTo(64);
            parsed.RootElement.GetProperty("first").GetArrayLength().ShouldBeLessThanOrEqualTo(8);
            parsed.RootElement.GetProperty("last").GetArrayLength().ShouldBeLessThanOrEqualTo(16);
            store.ReceivedCalls().ShouldBeEmpty("the genuine SDK refuses missing local identity before store access");
            await observer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            string stopped = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await Should.ThrowAsync<RouteCatalogUnavailableException>(() => actualSdk.AcquireAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
            (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(stopped);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
