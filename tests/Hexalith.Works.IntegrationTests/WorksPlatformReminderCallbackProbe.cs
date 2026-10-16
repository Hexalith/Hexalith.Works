using System.Text.Json.Nodes;

using Hexalith.EventStore.Client.Projections;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Records successful real Scheduler callbacks for the one explicitly selected HA actor.</summary>
internal sealed class WorksPlatformReminderCallbackProbe : IStartupFilter
{
    internal const string ActorEnvironment = "WORKS_PLATFORM_HA_CALLBACK_ACTOR";

    internal static string Key(string actor) => "story416:ha:successful-reminder-callback:" + actor;

    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            string actor = Environment.GetEnvironmentVariable(ActorEnvironment)!;
            // Dapr's app callback route: https://docs.dapr.io/reference/api/actors_api/#invoke-reminder
            string prefix = "/actors/WorksSdkReminderActor/" + actor + "/method/remind/";
            app.Use(async (context, continuation) =>
            {
                await continuation(context).ConfigureAwait(false);
                if (HttpMethods.IsPut(context.Request.Method) && context.Response.StatusCode == StatusCodes.Status200OK
                    && context.Request.Path.Value is { } path && path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string reminder = path[prefix.Length..];
                    IReadModelStore store = context.RequestServices.GetRequiredService<IReadModelStore>();
                    _ = await ReadModelWritePolicy.UpdateAsync<JsonObject>(store, "statestore", Key(actor), prior => new JsonObject
                    {
                        ["calls"] = (prior?["calls"]?.GetValue<int>() ?? 0) + 1,
                        ["reminder"] = reminder,
                    }, maxAttempts: 8, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                }
            });
            next(app);
        };
}
