using System.Security.Cryptography;

using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Server.Commands;
using Hexalith.EventStore.Server.DomainServices;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

[assembly: HostingStartup(typeof(Hexalith.Works.IntegrationTests.WorksSdkProofHostingStartup))]

namespace Hexalith.Works.IntegrationTests;

/// <summary>Explicit fixture-only asymmetric authority; preserves the production workload/channel protections.</summary>
public sealed class WorksSdkProofHostingStartup : IHostingStartup
{
    /// <inheritdoc/>
    public void Configure(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF") != "1") return;
        builder.ConfigureServices((context, services) =>
        {
            if (context.HostingEnvironment.EnvironmentName != "Development")
                throw new InvalidOperationException("Synthetic Works proof authority is restricted to Development fixtures.");
            string role = Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_ROLE")!;
            if (role == "metadata-only")
            {
                ConfigureMetadata(services);
                return;
            }
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_KEY")!), out _);
            var key = new RsaSecurityKey(rsa) { KeyId = "story415-ephemeral" };
            services.AddSingleton(key);
            services.AddSingleton<WorksSdkProofIntentRegistry>();
            if (role == "works")
            {
                services.AddSingleton<IReminderDelegationTokenProvider, WorksSdkProofDelegationProvider>();
                services.AddSingleton<Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter, WorksSdkProofResponseFilter>();
                if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_CALLBACKS") != "1")
                    services.AddSingleton<IStartupFilter, WorksSdkProofWakeupFilter>();
            }
            else if (role == "eventstore")
            {
                ConfigureMetadata(services);
                services.AddSingleton<IIdempotencyIntentAdapterRegistry>(provider => provider.GetRequiredService<WorksSdkProofIntentRegistry>());
                services.AddSingleton<ITrustedEffectRetentionGate, WorksSdkProofRetentionGate>();
                services.AddSingleton<ITrustedEffectAuditSink, WorksSdkProofAuditSink>();
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = WorksSdkProofDelegationProvider.Issuer };
                    metadata.SigningKeys.Add(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = key.KeyId });
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                    options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256, SecurityAlgorithms.RsaSha256];
                });
                services.Configure<TrustedEffectAuthorityOptions>(options => options.Rules = [
                    new("works", "works.date-resume", "work", "ResumeWorkItem"),
                    new("works", "works.expiry", "work", "ExpireWorkItem"),
                    new("works", "works.cascade-cancel", "work", "CancelWorkItem"),
                    new("works", "works.cascade-expire", "work", "ExpireWorkItem"),
                    new("works", "works.child-completion-resume", "work", "ResumeWorkItem"),
                ]);
            }
        });
    }
    private static void ConfigureMetadata(IServiceCollection services)
    {
        // These development sample apps are absent from the Works topology. Preserve the real
        // Works binding and named projection discovery while bounding advisory startup probes.
        services.PostConfigure<DomainServiceOptions>(options =>
        {
            foreach (string registration in options.Registrations
                .Where(pair => pair.Value.AppId is "sample" or "tenants").Select(pair => pair.Key).ToArray())
                options.Registrations.Remove(registration);
        });
        services.AddSingleton<Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter, WorksSdkProofMetadataFilter>();
    }

}
