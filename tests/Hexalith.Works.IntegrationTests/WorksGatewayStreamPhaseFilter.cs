using Hexalith.EventStore.Client.Gateway;

using Microsoft.Extensions.Http;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Observes actual wire-attempt timing inside the existing SDK gateway client and resilience handlers.</summary>
internal sealed class WorksGatewayStreamPhaseFilter(WorksReminderPhaseDiagnostics diagnostics) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name == typeof(IEventStoreGatewayClient).FullName || builder.Name == nameof(IEventStoreGatewayClient))
            builder.AdditionalHandlers.Add(new WorksGatewayStreamPhaseHandler(diagnostics));
    };
}
