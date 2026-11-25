using Hexalith.EventStore.DomainService;

using Microsoft.Extensions.Options;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Applies process option setup through the proof validator so twelve sources can start at a 4s Platform timeout.</summary>
internal sealed class WorksSdkProofProcessOptionsFactory(
    IEnumerable<IConfigureOptions<EventStoreProcessOptions>> setups,
    IEnumerable<IPostConfigureOptions<EventStoreProcessOptions>> postConfigures) : IOptionsFactory<EventStoreProcessOptions>
{
    /// <inheritdoc/>
    public EventStoreProcessOptions Create(string name)
    {
        var options = new EventStoreProcessOptions();
        foreach (IConfigureOptions<EventStoreProcessOptions> setup in setups)
        {
            if (setup is IConfigureNamedOptions<EventStoreProcessOptions> named)
                named.Configure(name, options);
            else if (name == Options.DefaultName)
                setup.Configure(options);
        }

        foreach (IPostConfigureOptions<EventStoreProcessOptions> postConfigure in postConfigures)
            postConfigure.PostConfigure(name, options);

        ValidateOptionsResult result = new WorksSdkProofProcessOptionsValidator().Validate(name, options);
        if (result.Failed)
            throw new OptionsValidationException(name, typeof(EventStoreProcessOptions), result.Failures);
        return options;
    }
}
