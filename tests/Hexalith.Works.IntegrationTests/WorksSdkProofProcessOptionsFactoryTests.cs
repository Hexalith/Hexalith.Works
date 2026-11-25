using Hexalith.EventStore.DomainService;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksSdkProofProcessOptionsFactoryTests
{
    [Fact]
    public void ProofFactoryAdmitsTwelveSourcesAtFourSecondPlatformTimeout()
    {
        using ServiceProvider services = Services(options =>
        {
            options.MaxEffectsPerEnvelope = 5;
            options.SupportedCandidateCount = 12;
            options.OperationTimeout = TimeSpan.FromSeconds(4);
            options.SourceServiceQuantum = TimeSpan.FromSeconds(116);
            options.LeaseDuration = TimeSpan.FromSeconds(30);
            options.RetryLimit = 10;
            options.ReconciliationInterval = TimeSpan.FromSeconds(10);
            options.ReadinessMaxAge = TimeSpan.FromMinutes(30);
        });
        EventStoreProcessOptions options = services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value;
        options.OperationTimeout.ShouldBe(TimeSpan.FromSeconds(4));
        options.SupportedCandidateCount.ShouldBe(12);
        options.Validate().ShouldContain(failure => failure.StartsWith("SupportedCandidateCount cannot fit", StringComparison.Ordinal));
    }

    [Fact]
    public void ProofFactoryStillRefusesAnUndersizedFiveEffectQuantum()
    {
        using ServiceProvider services = Services(options =>
        {
            options.MaxEffectsPerEnvelope = 5;
            options.SupportedCandidateCount = 12;
            options.OperationTimeout = TimeSpan.FromSeconds(4);
            options.SourceServiceQuantum = TimeSpan.FromSeconds(115);
            options.LeaseDuration = TimeSpan.FromSeconds(30);
            options.RetryLimit = 10;
            options.ReconciliationInterval = TimeSpan.FromSeconds(10);
            options.ReadinessMaxAge = TimeSpan.FromMinutes(30);
        });
        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value)
            .Failures.ShouldContain(failure => failure.StartsWith("SourceServiceQuantum must fit", StringComparison.Ordinal));
    }

    private static ServiceProvider Services(Action<EventStoreProcessOptions> configure)
    {
        var services = new ServiceCollection();
        _ = services.AddOptions<EventStoreProcessOptions>().Configure(configure);
        services.Replace(ServiceDescriptor.Transient<IOptionsFactory<EventStoreProcessOptions>, WorksSdkProofProcessOptionsFactory>());
        return services.BuildServiceProvider();
    }
}
