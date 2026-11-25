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
        EventStoreProcessOptions options = BindProcess(settings);
        options.SupportedCandidateCount.ShouldBe(12);
        options.MaxEffectsPerEnvelope.ShouldBe(5);
        options.OperationTimeout.ShouldBe(WorksAppHostSelection.IsPlatform ? TimeSpan.FromSeconds(4) : TimeSpan.FromMilliseconds(1970));
        options.SourceServiceQuantum.ShouldBe(TimeSpan.FromTicks(29 * options.OperationTimeout.Ticks));
        options.ReadinessMaxAge.ShouldBe(TimeSpan.FromMinutes(30));
        options.ReconciliationInterval.ShouldBe(TimeSpan.FromSeconds(10));
        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(30));
        options.RetryLimit.ShouldBe(10);
        IReadOnlyList<string> processFailures = options.Validate();
        if (WorksAppHostSelection.IsPlatform)
            processFailures.ShouldBe([processFailures.Single(failure => failure.StartsWith("SupportedCandidateCount cannot fit", StringComparison.Ordinal))]);
        else
        {
            processFailures.ShouldBeEmpty();
            services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value.Validate().ShouldBeEmpty();
            // Whole-cycle work is 12 * (2 * 57.13 + 17 * 1.97) + 10 = 1783 s, below 1790 s.
            options.ReadinessMaxAge.Subtract(options.ReconciliationInterval)
                .ShouldBeGreaterThan(TimeSpan.FromSeconds(1783));
        }

        settings["EventStore:Reminders:ReconciliationInterval"].ShouldBe("00:00:20");
        settings["Works:Recovery:ContextReadTimeout"].ShouldBe("00:00:02");
        EventStoreReminderOptions reminders = services.GetRequiredService<IOptions<EventStoreReminderOptions>>().Value;
        reminders.ReconciliationInterval.ShouldBe(TimeSpan.FromSeconds(20));
        reminders.CatalogOperationTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        reminders.CatalogServiceBudget.ShouldBe(TimeSpan.FromSeconds(30));
        reminders.Validate().ShouldBeEmpty();
        services.GetRequiredService<IOptions<WorksRecoveryOptions>>().Value.ContextReadTimeout.ShouldBe(TimeSpan.FromSeconds(2));
        // ConfigureRecoveryProfile is shared by the selected and retained topology's Works
        // resources; the HA replica receives the same real emitted and SDK-bound options.
        reminders.CatalogServiceBudget.ShouldBeGreaterThan(TimeSpan.FromSeconds(options.SupportedCandidateCount * 2));
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
        EventStoreProcessOptions composed = BindProcess(settings);
        settings["EventStore:Processes:SourceServiceQuantum"] = TimeSpan.FromTicks(29 * composed.OperationTimeout.Ticks).ToString();
        EventStoreProcessOptions options = BindProcess(settings);
        options.MaxEffectsPerEnvelope.ShouldBe(5);
        options.SourceServiceQuantum.ShouldBe(TimeSpan.FromTicks(29 * options.OperationTimeout.Ticks));
        IReadOnlyList<string> failures = options.Validate();
        if (WorksAppHostSelection.IsPlatform)
            failures.ShouldBe([failures.Single(failure => failure.StartsWith("SupportedCandidateCount cannot fit", StringComparison.Ordinal))]);
        else
            failures.ShouldBeEmpty();
    }

    /// <summary>Five-effect work must fit the source quantum and an extra source must fit freshness before admission.</summary>
    /// <param name="field">The profile setting to change.</param>
    /// <param name="value">The unsafe replacement.</param>
    /// <param name="failurePrefix">The independently expected SDK validation boundary.</param>
    [Theory]
    [InlineData("SourceServiceQuantum", "00:00:57.1299999", "SourceServiceQuantum must fit")]
    [InlineData("SupportedCandidateCount", "13", "SupportedCandidateCount cannot fit")]
    public async Task UnsafeEffectOrPopulationBudgetIsRefused(string field, string value, string failurePrefix)
    {
        Dictionary<string, string?> settings = await ComposedSettingsAsync("works");
        settings["EventStore:Processes:" + field] = value;
        using ServiceProvider services = Services(settings);
        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value)
            .Failures.ShouldContain(failure => failure.StartsWith(failurePrefix, StringComparison.Ordinal));
    }

    /// <summary>Fixture calibration does not modify either production default or the SDK's hard upper bound.</summary>
    [Fact]
    public void FixtureCalibrationPreservesProductionDefaults()
    {
        new EventStoreReminderOptions().CatalogOperationTimeout.ShouldBe(TimeSpan.FromSeconds(2));
        new EventStoreReminderOptions().CatalogServiceBudget.ShouldBe(TimeSpan.FromSeconds(2));
        new WorksRecoveryOptions().ContextReadTimeout.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>The finite fixture cannot silently exceed the actual SDK validation boundary.</summary>
    /// <param name="field">The reminder option to exceed.</param>
    [Theory]
    [InlineData("CatalogOperationTimeout")]
    [InlineData("CatalogServiceBudget")]
    public async Task ReminderProfileCannotExceedSdkHardLimits(string field)
    {
        Dictionary<string, string?> settings = await ComposedSettingsAsync("works");
        settings["EventStore:Reminders:" + field] = "00:00:30.0000001";
        using ServiceProvider services = Services(settings);
        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IOptions<EventStoreReminderOptions>>().Value);
    }

    private static EventStoreProcessOptions BindProcess(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new EventStoreProcessOptions();
        configuration.GetSection(EventStoreProcessOptions.SectionName).Bind(options);
        if (configuration["EventStore:Processes:MaxEffectsPerEnvelope"] is null) options.MaxEffectsPerEnvelope = 5;
        if (configuration["EventStore:Processes:SourceServiceQuantum"] is null) options.SourceServiceQuantum = TimeSpan.FromSeconds(15);
        return options;
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
        environment.Count.ShouldBe(13);
        environment["Logging__LogLevel__Hexalith.EventStore.DomainService.ReminderReconciler"].ShouldBe("Information");
        return environment.ToDictionary(pair => pair.Key.Replace("__", ":", StringComparison.Ordinal), pair => (string?)pair.Value);
    }
}
