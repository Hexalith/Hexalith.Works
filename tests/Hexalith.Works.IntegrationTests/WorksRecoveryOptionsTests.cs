using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic coverage of the <c>Works:Recovery</c> options contract: the fail-fast validation chain
/// registered by <see cref="WorksRecoveryExtensions.AddWorksReminderAndCascadeRecovery"/>, and the deprecated
/// <c>MaxStreamPagesPerTenant</c> configuration alias kept for the renamed per-aggregate page budget.
/// </summary>
/// <remarks>
/// Without these facts the fail-fast guard could be weakened silently, turning a misconfiguration into a
/// permanently and silently disabled recovery pass, and the renamed budget could stop honouring configuration
/// that still uses the old key.
/// </remarks>
public sealed class WorksRecoveryOptionsTests
{
    [Theory]
    [InlineData("MaxStreamPagesPerAggregate", "0")]
    [InlineData("MaxStreamPagesPerAggregate", "-1")]
    [InlineData("MaxStreamPagesPerTenant", "0")]
    [InlineData("ReminderReconciliationMaxAttempts", "0")]
    [InlineData("ReminderReconciliationRetryDelayMilliseconds", "-1")]
    public void Invalid_recovery_configuration_fails_the_validation_chain(string key, string value)
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:{key}"] = value,
        });

        OptionsValidationException thrown = Should.Throw<OptionsValidationException>(() => options.Value);
        thrown.Failures.ShouldNotBeEmpty();
    }

    [Fact]
    public void Valid_recovery_configuration_binds_and_passes_validation()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerAggregate"] = "7",
        });

        options.Value.EffectiveMaxStreamPagesPerAggregate.ShouldBe(7);
    }

    [Fact]
    public void Deprecated_per_tenant_key_still_binds_the_per_aggregate_page_budget()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerTenant"] = "3",
        });

        // The budget was always applied per aggregate; only the configuration key's name was wrong.
        options.Value.EffectiveMaxStreamPagesPerAggregate.ShouldBe(3);
        options.Value.MaxStreamPagesPerTenant.ShouldBe(3);
    }

    [Fact]
    public void Deprecated_alias_wins_over_the_renamed_key_so_existing_configuration_keeps_its_meaning()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerAggregate"] = "11",
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerTenant"] = "4",
        });

        options.Value.EffectiveMaxStreamPagesPerAggregate.ShouldBe(4);
    }

    [Fact]
    public void Unconfigured_recovery_options_use_the_documented_defaults()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal));

        options.Value.RunReconciliationOnStartup.ShouldBeTrue();
        options.Value.MaxStreamPagesPerTenant.ShouldBeNull();
        options.Value.EffectiveMaxStreamPagesPerAggregate
            .ShouldBe(WorksRecoveryOptions.DefaultMaxStreamPagesPerAggregate);
    }

    private static IOptions<WorksRecoveryOptions> BuildOptions(Dictionary<string, string?> configurationValues)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddWorksReminderAndCascadeRecovery(configuration);
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<WorksRecoveryOptions>>();
    }
}
