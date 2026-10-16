using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Security;
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

    /// <summary>Starts a separately authenticated, exact tenant/party/actor synthetic authority.</summary>
    internal static async Task<WorksPlatformIdentityFixture> StartAsync(string tenant, string party, string actor, CancellationToken cancellationToken)
    {
        var fixture = new WorksPlatformIdentityFixture();
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
