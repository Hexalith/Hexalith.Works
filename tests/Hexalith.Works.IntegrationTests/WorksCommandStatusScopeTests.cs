using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;

using Hexalith.EventStore.Authentication;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Controllers;
using Hexalith.EventStore.Server.Commands;
using Hexalith.EventStore.ServiceDefaults.Authentication;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Runs the fixture poller against the published SDK controller's actual explicit tenant boundary.</summary>
public sealed class WorksCommandStatusScopeTests
{
    [Fact]
    public async Task PollingSelectsItsAuthorizedTenantWhileMissingAndForeignScopesRemainForbiddenAsync()
    {
        const string tenant = "status-tenant";
        var store = Substitute.For<ICommandStatusStore>();
        store.ReadStatusAsync(tenant, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult<CommandStatusRecord?>(new(CommandStatus.Completed, DateTimeOffset.UtcNow, "item", 1, null, null, null,
                MessageId: call.ArgAt<string>(1), CorrelationId: call.ArgAt<string>(1))));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(store);
        builder.Services.AddControllers().AddApplicationPart(typeof(CommandStatusController).Assembly)
            .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new StatusControllerOnly()));
        builder.Services.AddTransient<IClaimsTransformation, EventStoreClaimsTransformation>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "hexalith-dev", ValidateAudience = true, ValidAudience = "hexalith-eventstore",
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(WorksAppHostSmokeHarness.DevSigningKey)),
                ValidateLifetime = true,
            };
        });
        builder.Services.AddAuthorization(options => options.AddPolicy(EventStoreHttpHostSecurityExtensions.HumanPolicy,
            EventStoreHttpHostSecurityExtensions.CreateHumanPolicy()));
        WebApplication app = builder.Build();
        await using var lifetime = app.ConfigureAwait(true);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapPost("/api/v1/commands", (SubmitCommandRequest command) => Results.Accepted(value: new
            { messageId = command.MessageId, correlationId = command.MessageId }))
            .RequireAuthorization(EventStoreHttpHostSecurityExtensions.HumanPolicy);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await app.StartAsync(deadline.Token).ConfigureAwait(true);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, "item", "FixtureCommand", new { }, deadline.Token).ConfigureAwait(true);
        foreach (string? selected in new[] { tenant, null, "foreign-tenant" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/commands/status/status-message");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(tenant));
            if (selected is not null) request.Headers.Add("X-Hexalith-Tenant", selected);
            using HttpResponseMessage response = await client.SendAsync(request, deadline.Token).ConfigureAwait(true);
            response.StatusCode.ShouldBe(selected == tenant ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
            if (selected == tenant)
                (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(deadline.Token).ConfigureAwait(true))
                    .GetProperty("status").GetString().ShouldBe("Completed");
        }
        store.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ICommandStatusStore.ReadStatusAsync)).ShouldBe(2);
    }


}
