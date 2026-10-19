using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.Queries;
using Hexalith.Parties.Contracts.ValueObjects;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Authenticated synthetic current-identity authority for disposable target-host query qualification.</summary>
internal sealed class WorksPlatformIdentityFixture : IAsyncDisposable
{
    /// <summary>The synthetic Executor Party the live lanes bind to their executor-visible work item.</summary>
    internal const string SyntheticExecutorParty = "party-416-executor";

    /// <summary>The synthetic human actor the authority binds to <see cref="SyntheticExecutorParty"/>.</summary>
    internal const string SyntheticExecutorActor = "01ARZ3NDEKTSV4RRFFQ69G5FAV";

    private const string Issuer = "https://story416.invalid/current-identity-fixture";
    private const string Audience = "story416-parties-identity-reader";
    private readonly RSA _key = RSA.Create(2048);
    private WebApplication? _app;
    private int _calls;
    private int _unavailable;

    /// <summary>Gets the owned loopback gateway URI.</summary>
    internal Uri Endpoint { get; private set; } = null!;

    /// <summary>Gets the independent ephemeral service credential installed only in the fixture child.</summary>
    internal string ServiceBearer { get; private set; } = string.Empty;

    /// <summary>Gets how many authenticated canonical requests reached this authority.</summary>
    internal int Calls => Volatile.Read(ref _calls);

    /// <summary>Gets or sets an explicit authority-outage injection.</summary>
    internal bool Unavailable { get => Volatile.Read(ref _unavailable) != 0; set => Volatile.Write(ref _unavailable, value ? 1 : 0); }

    /// <summary>Gets the only tenant this authority resolves.</summary>
    internal string Tenant { get; private set; } = string.Empty;

    /// <summary>Gets the only Executor Party this authority resolves.</summary>
    internal string Party { get; private set; } = string.Empty;

    /// <summary>Gets the only human actor this authority binds to <see cref="Party"/>.</summary>
    internal string Actor { get; private set; } = string.Empty;

    /// <summary>Builds a delegated Executor query exactly as the gateway stamps one for the authenticated actor.</summary>
    /// <param name="aggregateId">The queried work item.</param>
    /// <param name="queryType">The Works query type.</param>
    /// <param name="correlationId">The query correlation.</param>
    /// <returns>A delegated envelope carrying only the Executor Party; no coordinator or role field grants authority.</returns>
    internal QueryEnvelope ExecutorQuery(string aggregateId, string queryType, string correlationId)
        => new(Tenant, "work", aggregateId, queryType, JsonSerializer.SerializeToUtf8Bytes(new { partyId = Party }), correlationId, Actor)
        { OriginalActorId = Actor, AuthenticatedWorkloadId = "eventstore", IsDelegated = true };

    /// <summary>Sends a query through the gateway sidecar with the gateway's own query assertion and returns the SDK result.</summary>
    /// <param name="gatewaySidecar">The <c>eventstore</c> sidecar client.</param>
    /// <param name="query">The envelope to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The protected Works query result.</returns>
    internal static async Task<QueryResult> QueryAsync(HttpClient gatewaySidecar, QueryEnvelope query, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/query", query,
            WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceQuery]), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException("Protected Works query transport refused. HTTP=" + (int)response.StatusCode + "; body="
                + await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return (await response.Content.ReadFromJsonAsync<QueryResult>(cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidOperationException("Protected Works query returned no result.");
    }

    /// <summary>Describes the delegated identity a phase's queries carried, with the resolutions this authority observed.</summary>
    /// <param name="query">One envelope the phase actually sent.</param>
    /// <param name="callsBefore">The authority's call count before the phase's first query.</param>
    /// <returns>The observed delegated identity and authority resolution count.</returns>
    internal object Evidence(QueryEnvelope query, int callsBefore)
    {
        ArgumentNullException.ThrowIfNull(query);
        using JsonDocument payload = JsonDocument.Parse(query.Payload);
        return new
        {
            tenant = query.TenantId, isDelegated = query.IsDelegated, authenticatedWorkloadId = query.AuthenticatedWorkloadId,
            originalActorId = query.OriginalActorId, userId = query.UserId,
            partyId = payload.RootElement.GetProperty("partyId").GetString(),
            resolvedTenant = Tenant, resolvedParty = Party, resolvedActor = Actor,
            identityCalls = Calls - callsBefore,
        };
    }

    /// <summary>Starts a separately authenticated, exact tenant/party/actor synthetic authority.</summary>
    internal static async Task<WorksPlatformIdentityFixture> StartAsync(string tenant, string party, string actor, CancellationToken cancellationToken)
    {
        var fixture = new WorksPlatformIdentityFixture { Tenant = tenant, Party = party, Actor = actor };
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(["--preventHostingStartup=true", "--Logging:LogLevel:Default=Error"]);
            builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true").ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer, ValidAudience = Audience,
                    IssuerSigningKey = new RsaSecurityKey(fixture._key.ExportParameters(false)),
                    ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero,
                };
            });
            builder.Services.AddAuthorization();
            WebApplication app = builder.Build();
            fixture._app = app;
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapPost("/api/v1/queries", (SubmitQueryRequest request, HttpContext context) =>
            {
                if (context.User.FindFirst("sub")?.Value != "works-identity-reader"
                    || context.User.FindFirst("azp")?.Value != "works"
                    || request.Tenant != tenant || request.Domain != "party" || request.AggregateId != party
                    || request.QueryType != typeof(ResolvePartyIdentity).FullName)
                    return (IResult)Results.StatusCode(StatusCodes.Status403Forbidden);
                ResolvePartyIdentity? query = request.Payload?.Deserialize<ResolvePartyIdentity>(JsonSerializerOptions.Web);
                if (query is null || query.TenantId != tenant || query.PartyId != party || query.ExpectedActorId != actor)
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                Interlocked.Increment(ref fixture._calls);
                if (fixture.Unavailable) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var custody = new IdentityHistoryCustodyEvidence("synthetic-416-policy", "party-actor-history-v1", now.AddHours(1),
                    1, "synthetic-416-custody", true, true, true);
                var binding = new HumanActorBindingEvidence(tenant, party, actor, 1, 1,
                    now.AddMinutes(-1), now.AddMinutes(30), "synthetic-416-source", "synthetic-416-provenance", custody);
                var evidence = new PartyIdentityResult(PartyIdentityOutcome.Resolved,
                    new PartyIdentityEvidence(1, tenant, party, PartyIdentityClassification.Human, true, false, false,
                        2, now, "synthetic-416-observation", binding));
                return Results.Ok(new SubmitQueryResponse("synthetic-416-identity", JsonSerializer.SerializeToElement(evidence)));
            }).RequireAuthorization();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            fixture.Endpoint = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            DateTime issued = DateTime.UtcNow;
            fixture.ServiceBearer = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer, Audience = Audience, IssuedAt = issued, NotBefore = issued.AddSeconds(-1), Expires = issued.AddMinutes(30),
                Claims = new Dictionary<string, object> { ["sub"] = "works-identity-reader", ["azp"] = "works" },
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(fixture._key), SecurityAlgorithms.RsaSha256),
            });
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        WebApplication? app = Interlocked.Exchange(ref _app, null);
        try
        {
            if (app is not null)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await app.StopAsync(deadline.Token).ConfigureAwait(false); }
                finally { await app.DisposeAsync().ConfigureAwait(false); }
            }
        }
        finally { _key.Dispose(); }
    }
}
