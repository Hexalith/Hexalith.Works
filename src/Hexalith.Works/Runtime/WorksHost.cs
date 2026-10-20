using Hexalith.EventStore.DomainService;
using Hexalith.Works.Recovery;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hexalith.Works.Runtime;

/// <summary>Builds Works on the canonical EventStore domain-service host.</summary>
internal static class WorksHost
{
    /// <summary>Builds the domain service, with optional executable-test customizations.</summary>
    internal static WebApplication Build(
        string[] args,
        Action<IWebHostBuilder>? configureWebHost = null,
        Action<IServiceCollection>? configureServices = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        configureWebHost?.Invoke(builder.WebHost);
        _ = builder.Services.AddWorksDomainAdapters(builder.Configuration);
        _ = builder.AddEventStoreDomainService(typeof(WorkItemEventStoreAggregate).Assembly);
        configureServices?.Invoke(builder.Services);
        WebApplication app = builder.Build();
        app.MapWorksProcessDisposition();
        return app.UseEventStoreDomainService();
    }
}
