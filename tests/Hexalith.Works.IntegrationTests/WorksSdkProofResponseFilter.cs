using Hexalith.EventStore.Client.Projections;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Adds a one-shot synthetic lost-response fault outside the real protected trusted-effect transport.</summary>
internal sealed class WorksSdkProofResponseFilter(IServiceProvider services) : IHttpMessageHandlerBuilderFilter
{
    /// <inheritdoc/>
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        => builder =>
        {
            next(builder);
            if (builder.Name?.Contains("ITrustedEffectSubmitter", StringComparison.Ordinal) == true)
                builder.AdditionalHandlers.Insert(0, new WorksSdkProofResponseHandler(services.GetRequiredService<IReadModelStore>()));
        };
}
