using Microsoft.Extensions.Http;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Bounds advisory metadata probes in the explicitly enabled synthetic topology.</summary>
internal sealed class WorksSdkProofMetadataFilter : IHttpMessageHandlerBuilderFilter
{
    /// <inheritdoc/>
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        => builder =>
        {
            next(builder);
            if (builder.Name?.StartsWith("eventstore-domain-metadata:", StringComparison.Ordinal) == true)
                builder.AdditionalHandlers.Insert(0, new WorksSdkProofMetadataHandler());
        };
}
