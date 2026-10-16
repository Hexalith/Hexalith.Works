using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Deliberately loses synthetic initial wakeups so SDK catalog recovery must discover committed work.</summary>
internal sealed class WorksSdkProofWakeupFilter : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path == "/work/events")
                {
                    context.Request.EnableBuffering();
                    using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                    context.Request.Body.Position = 0;
                    JsonElement envelope = body.RootElement.TryGetProperty("data", out JsonElement data) ? data : body.RootElement;
                    string? tenant = envelope.TryGetProperty("tenantId", out JsonElement tenantId) ? tenantId.GetString()
                        : envelope.TryGetProperty("TenantId", out tenantId) ? tenantId.GetString() : null;
                    if (tenant?.StartsWith("sdk415-", StringComparison.Ordinal) == true)
                    {
                        await context.Response.WriteAsJsonAsync(new { status = "SUCCESS" }, context.RequestAborted).ConfigureAwait(false);
                        return;
                    }
                }
                await continuation(context).ConfigureAwait(false);
            });
            next(app);
        };
}
