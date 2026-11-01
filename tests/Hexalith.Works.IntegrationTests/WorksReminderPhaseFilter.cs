using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Read-only timing around the actual SDK reminder actor receiver, without reading request or response bodies.</summary>
internal sealed class WorksReminderPhaseFilter(WorksReminderPhaseDiagnostics diagnostics) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            string[] parts = context.Request.Path.Value?.Split('/') ?? [];
            if (parts.Length < 5 || parts[1] != "actors" || parts[2] != "WorksSdkReminderActor")
            {
                await continuation(context).ConfigureAwait(false);
                return;
            }
            long sequence = diagnostics.Begin("actor-http", parts[3]);
            try
            {
                await continuation(context).ConfigureAwait(false);
                diagnostics.End(sequence, null, status: context.Response.StatusCode);
            }
            catch (Exception exception)
            {
                diagnostics.End(sequence, exception, status: context.Response.StatusCode);
                throw;
            }
        });
        next(app);
    };
}
