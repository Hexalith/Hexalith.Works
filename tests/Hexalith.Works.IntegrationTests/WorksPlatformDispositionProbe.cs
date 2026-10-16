using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.ServiceDefaults.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Records the exact receiver refusal behind preserved Dapr transport policies in synthetic fixtures.</summary>
internal sealed class WorksPlatformDispositionProbe(IReadOnlyCollection<ProcessSource> grants) : IStartupFilter
{
    internal const string NonceHeader = "x-works-platform-disposition-nonce";
    internal const string KindHeader = "x-works-platform-disposition-kind";
    internal const string Route = "/work/processes/revalidate";
    private readonly HashSet<ProcessSource> _grants = grants.Count is > 0 and <= 64 ? grants.ToHashSet()
        : throw new ArgumentException("Disposition probes require a bounded explicit source set.", nameof(grants));

    internal static string Key(ProcessSource source, string kind)
        => "story416:disposition-response:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.CatalogKey))) + ":" + kind;

    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            IServiceScopeFactory scopes = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>();
            app.Use(async (context, continuation) =>
            {
                string nonce = context.Request.Headers[NonceHeader].ToString();
                string kind = context.Request.Headers[KindHeader].ToString();
                ProcessSource? source = null;
                if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == Route
                    && context.Request.ContentLength is > 0 and <= 4096
                    && nonce.Length == 32 && nonce.All(char.IsAsciiHexDigit)
                    && kind is "wrong-purpose" or "wrong-tenant" or "wrong-aggregate" or "audit-outage")
                {
                    context.Request.EnableBuffering(4096, 4096);
                    try
                    {
                        using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                        if (body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("source", out JsonElement element))
                            source = element.Deserialize<ProcessSource>(JsonSerializerOptions.Web);
                        if (source is not null && !_grants.Contains(source)) source = null;
                    }
                    catch (JsonException) { source = null; }
                    catch (KeyNotFoundException) { source = null; }
                    finally { context.Request.Body.Position = 0; }
                }
                context.Response.OnCompleted(async () =>
                {
                    int expected = kind == "audit-outage" ? StatusCodes.Status500InternalServerError : StatusCodes.Status403Forbidden;
                    if (source is null || context.Response.StatusCode != expected || context.User.Identity?.IsAuthenticated != true) return;
                    string? Claim(string name)
                    {
                        string[] values = context.User.FindAll(name).Select(claim => claim.Value).ToArray();
                        return values.Length == 1 ? values[0] : null;
                    }
                    string? tenant = Claim(EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType);
                    string? aggregate = Claim("eventstore:bound-aggregate");
                    string? purpose = Claim("eventstore:bound-purpose");
                    if (context.User.Identities.Count() != 1 || Claim(ClaimTypes.NameIdentifier) != "workload:eventstore-admin"
                        || Claim(EventStoreWorkloadAuthenticationDefaults.ActorBindingClaimType) != "synthetic-operator-416"
                        || Claim(EventStoreWorkloadAuthenticationDefaults.WorkloadClaimType) != "eventstore-admin"
                        || Claim(EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType) != source.Domain) return;
                    bool bound = kind switch
                    {
                        "wrong-purpose" => tenant == source.Tenant && aggregate == source.Aggregate && purpose is { Length: > 0 } && purpose != "works.process-disposition",
                        "wrong-tenant" => tenant is { Length: > 0 } && tenant != source.Tenant && aggregate == source.Aggregate && purpose == "works.process-disposition",
                        "wrong-aggregate" => tenant == source.Tenant && aggregate is { Length: > 0 } && aggregate != source.Aggregate && purpose == "works.process-disposition",
                        "audit-outage" => tenant == source.Tenant && aggregate == source.Aggregate && purpose == "works.process-disposition",
                        _ => false,
                    };
                    if (!bound) return;
                    AsyncServiceScope observationScope = scopes.CreateAsyncScope();
                    await using var observationDisposal = observationScope.ConfigureAwait(false);
                    IReadModelStore store = observationScope.ServiceProvider.GetRequiredService<IReadModelStore>();
                    using var observationDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    _ = await ReadModelWritePolicy.UpdateAsync<JsonObject>(store, "statestore", Key(source, kind), prior => new JsonObject
                    {
                        ["count"] = Math.Min((prior?["count"]?.GetValue<int>() ?? 0) + 1, 1024),
                        ["nonce"] = nonce, ["status"] = expected, ["kind"] = kind, ["route"] = Route,
                        ["tenant"] = source.Tenant, ["domain"] = source.Domain, ["aggregate"] = source.Aggregate,
                        ["workload"] = "eventstore-admin",
                    }, maxAttempts: 8, cancellationToken: observationDeadline.Token).WaitAsync(observationDeadline.Token).ConfigureAwait(false);
                });
                await continuation(context).ConfigureAwait(false);
            });
            next(app);
        };
}
