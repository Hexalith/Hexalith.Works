using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Hexalith.EventStore.DomainService;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Qualifies the live fixture's actual composed profile against SDK capacity and effect deadlines.</summary>
public sealed class WorksSdkRecoveryProfileTests
{
    /// <summary>The environment emitted for either Works host binds to the validated twelve-source profile.</summary>
    /// <param name="resourceName">The fixture host receiving the same profile.</param>
    [Theory]
    [InlineData("works")]
    [InlineData("works-ha")]
    public async Task ComposedProfileFitsTwelveSourcesAndFiveEffects(string resourceName)
    {
        Dictionary<string, string?> settings = await ComposedSettingsAsync(resourceName);
        using ServiceProvider services = Services(settings);
        EventStoreProcessOptions options = services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value;
        options.SupportedCandidateCount.ShouldBe(12);
        options.MaxEffectsPerEnvelope.ShouldBe(5);
        options.OperationTimeout.ShouldBe(TimeSpan.FromMilliseconds(1900));
        options.SourceServiceQuantum.ShouldBe(TimeSpan.FromSeconds(56));
        options.ReadinessMaxAge.ShouldBe(TimeSpan.FromMinutes(30));
        options.ReconciliationInterval.ShouldBe(TimeSpan.FromSeconds(10));
        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(30));
        options.RetryLimit.ShouldBe(10);
        options.Validate().ShouldBeEmpty();
        settings["EventStore:Reminders:ReconciliationInterval"].ShouldBe("00:00:02");
        settings["Works:Recovery:ContextReadTimeout"].ShouldBe("00:00:02");

        // The registered five-effect contract needs 29 operation deadlines per envelope (55.1 s).
        // Whole-cycle work is 12 * (2 * 56 + 17 * 1.9) + 10 = 1741.6 s, below 1790 s.
        options.SourceServiceQuantum.ShouldBeGreaterThan(TimeSpan.FromSeconds(55.1));
        options.ReadinessMaxAge.Subtract(options.ReconciliationInterval)
            .ShouldBeGreaterThan(TimeSpan.FromSeconds(1741.6));
    }

    /// <summary>The superseded profile fits eleven sources but cannot silently admit the executor-visible twelfth.</summary>
    /// <param name="sources">The number of retained sources.</param>
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public async Task OldProfileCannotAdmitTheTwelfthSource(int sources)
    {
        Dictionary<string, string?> settings = await ComposedSettingsAsync("works");
        settings["EventStore:Processes:OperationTimeout"] = "00:00:02";
        settings["EventStore:Processes:SourceServiceQuantum"] = "00:01:00";
        settings["EventStore:Processes:SupportedCandidateCount"] = sources.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using ServiceProvider services = Services(settings);
        if (sources == 11)
            services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value.Validate().ShouldBeEmpty();
        else
            Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value)
                .Failures.ShouldContain(failure => failure.StartsWith("SupportedCandidateCount cannot fit", StringComparison.Ordinal));
    }

    /// <summary>The exact five-effect deadline is accepted, keeping the lower quantum boundary inclusive.</summary>
    [Fact]
    public async Task ExactFiveEffectQuantumIsAccepted()
    {
        Dictionary<string, string?> settings = await ComposedSettingsAsync("works");
        settings["EventStore:Processes:SourceServiceQuantum"] = "00:00:55.1000000";
        using ServiceProvider services = Services(settings);
        EventStoreProcessOptions options = services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value;
        options.MaxEffectsPerEnvelope.ShouldBe(5);
        options.SourceServiceQuantum.ShouldBe(TimeSpan.FromMilliseconds(55100));
        options.Validate().ShouldBeEmpty();
    }

    /// <summary>Five-effect work must fit the source quantum and an extra source must fit freshness before admission.</summary>
    /// <param name="field">The profile setting to change.</param>
    /// <param name="value">The unsafe replacement.</param>
    /// <param name="failurePrefix">The independently expected SDK validation boundary.</param>
    [Theory]
    [InlineData("SourceServiceQuantum", "00:00:55.0999999", "SourceServiceQuantum must fit")]
    [InlineData("SupportedCandidateCount", "13", "SupportedCandidateCount cannot fit")]
    public async Task UnsafeEffectOrPopulationBudgetIsRefused(string field, string value, string failurePrefix)
    {
        Dictionary<string, string?> settings = await ComposedSettingsAsync("works");
        settings["EventStore:Processes:" + field] = value;
        using ServiceProvider services = Services(settings);
        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value)
            .Failures.ShouldContain(failure => failure.StartsWith(failurePrefix, StringComparison.Ordinal));
    }

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(configuration);
        services.AddWorksReminderAndCascadeRecovery(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<Dictionary<string, string?>> ComposedSettingsAsync(string name)
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        { Args = [], DisableDashboard = true });
        var resource = new ProjectResource(name);
        WorksSdkRecoveryPipelineSmokeTests.ConfigureRecoveryProfile(builder.CreateResourceBuilder(resource));
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment, TestContext.Current.CancellationToken);
        foreach (EnvironmentCallbackAnnotation callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await callback.Callback(context).ConfigureAwait(true);
        environment.Count.ShouldBe(9);
        return environment.ToDictionary(pair => pair.Key.Replace("__", ":", StringComparison.Ordinal), pair => (string?)pair.Value);
    }
}
