using System.Text;

using Hexalith.EventStore.ErrorHandling;
using Hexalith.EventStore.Extensions;
using Hexalith.EventStore.Server.Commands;
using Hexalith.EventStore.Server.Pipeline;
using Hexalith.EventStore.Server.Pipeline.Commands;
using Hexalith.EventStore.Server.Pipeline.Queries;
using Hexalith.EventStore.Server.Projections;
using Hexalith.EventStore.Server.Queries;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.EventStore.Testing.Fakes;

using MediatR;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

using NSubstitute;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Real SDK controllers, JWT and MediatR authorization/routing over the strict isolated Works receiver catalog.</summary>
internal static class WorksGatewayHttpFixture
{
    internal const string Audience = "works-ui-gateway";

    internal static WebApplication Build(IRouteCatalogAuthority authority, IQueryRouter queries, ICommandRouter commands,
        InMemoryCommandStatusStore statuses, InMemoryCommandArchiveStore archives, string issuer, string signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            Args = ["--Logging:LogLevel:Default=Error"],
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:JwtBearer:Issuer"] = issuer, ["Authentication:JwtBearer:Audience"] = Audience,
            ["Authentication:JwtBearer:SigningKey"] = signingKey,
        });
        builder.Services.AddEventStore();
        // This component fixture exercises HTTP admission and the real request terminals.
        // The separate canonical cohort owns distributed workers and activation.
        builder.Services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        builder.Services.AddSingleton(authority);
        builder.Services.RemoveAll<ICommandStatusStore>();
        builder.Services.AddSingleton<ICommandStatusStore>(statuses);
        builder.Services.RemoveAll<ICommandArchiveStore>();
        builder.Services.AddSingleton<ICommandArchiveStore>(archives);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(Substitute.For<IETagService>());
        builder.Services.RemoveAll<IRequestHandler<SubmitCommand, SubmitCommandResult>>();
        builder.Services.AddSingleton<IRequestHandler<SubmitCommand, SubmitCommandResult>>(new SubmitCommandHandler(authority,
            statuses, archives, commands, null, null, new NoOpProjectionUpdateOrchestrator(), NullLogger<SubmitCommandHandler>.Instance));
        builder.Services.RemoveAll<IRequestHandler<SubmitQuery, SubmitQueryResult>>();
        builder.Services.AddSingleton<IRequestHandler<SubmitQuery, SubmitQueryResult>>(new SubmitQueryHandler(queries, NullLogger<SubmitQueryHandler>.Instance));
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new() { ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true,
                ValidAudience = Audience, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), ValidAlgorithms = [SecurityAlgorithms.HmacSha256] };
        });
        builder.Services.AddAuthorization(options => options.AddPolicy(EventStoreHttpHostSecurityExtensions.HumanPolicy, policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddControllers().AddApplicationPart(typeof(Hexalith.EventStore.Controllers.CommandsController).Assembly);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        return app;
    }
}
