using System.Security.Cryptography;
#if WORKS_PLATFORM_PARITY
using System.Text.Json;
using Hexalith.EventStore.Client.Processes;
using Hexalith.Platform.Works.Runtime;
#endif

using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Server.Commands;
using Hexalith.EventStore.Server.DomainServices;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        _ = WorksDeclaredProofRuntime.Capture();
        builder.ConfigureServices((context, services) =>
        {
            if (context.HostingEnvironment.EnvironmentName != "Development")
                throw new InvalidOperationException("Synthetic Works proof authority is restricted to Development fixtures.");
            // Hosting startups run in their listed order, so Platform's telemetry is already registered here
            // exactly when its own hosting startup executed before this proof.
            bool platformTelemetryRegisteredBeforeProof = WorksSdkProofHostingEvidence.PlatformTelemetryRegistered(services);
            ConfigureProofServices(context, services, Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_ROLE")!,
                Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_KEY"));
            WorksSdkProofHostingEvidence.Write(services, platformTelemetryRegisteredBeforeProof);
        });
    }

    internal static void ConfigureProofServices(WebHostBuilderContext context, IServiceCollection services, string role, string? privateKey)
    {
        if (context.Configuration[WorksCatalogFirstChanceObserver.PathKey] is not null)
            services.AddHostedService<WorksCatalogFirstChanceObserver>();
        if (role == "eventstore-admin")
        {
            WorksAdminProofService.RegisterIssuer(services, context.Configuration);
            services.AddHostedService<WorksAdminProofService>();
            if (privateKey is { Length: > 0 })
                ConfigureProofJwtBearer(services, privateKey);
            return;
        }
        if (role == "metadata-only")
        {
            ConfigureMetadata(services);
            return;
        }
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey!), out _);
        var key = new RsaSecurityKey(rsa) { KeyId = "story415-ephemeral" };
        services.AddSingleton(key);
        // Every catalog participant must validate the same proof issuer, audience, and key. Rewriting
        // only EventStore leaves AuthenticationFingerprint mismatched and process evidence unavailable.
        ConfigureProofJwtBearer(services, rsa, key);
        services.AddSingleton<IWorkloadAssertionIssuer>(provider => new WorksSdkProofWorkloadAssertionIssuer(
            provider.GetRequiredService<RsaSecurityKey>(),
            provider.GetService<IOptions<WorkloadAssertionIssuerOptions>>()?.Value.Workload));
        services.AddSingleton<WorksSdkProofIntentRegistry>();
        if (role is "operations")
            return;
        if (role == "works")
        {
            InstallProofProcessOptions(services);
            // Keep the actual Works health result, exposing only the same bounded diagnostic response used by the gateway.
            services.Configure<Hexalith.EventStore.ServiceDefaults.EventStoreServiceDefaultsOptions>(options =>
                options.DevelopmentHealthResponseWriter = WorksReminderHealthProjection.WriteAsync);
            services.AddSingleton(_ => new WorksReminderPassDiagnostics(context.Configuration[WorksReminderPhaseDiagnostics.PathKey] is { Length: > 0 } phasePath
                ? phasePath + ".passes.json" : null));
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(provider => provider.GetRequiredService<WorksReminderPassDiagnostics>());
            if (context.Configuration[WorksReminderPhaseDiagnostics.PathKey] is not null)
            {
                services.AddSingleton<WorksReminderPhaseDiagnostics>();
                services.AddSingleton(_ => new WorksProcessTranslationDiagnostics(context.Configuration[WorksReminderPhaseDiagnostics.PathKey] + ".translations.json"));
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(provider => provider.GetRequiredService<WorksProcessTranslationDiagnostics>());
                services.AddSingleton<Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter, WorksGatewayStreamPhaseFilter>();
                services.AddScoped<Hexalith.Works.Reminders.WorksReminderIntentSource>();
                services.AddScoped<IReminderIntentSource>(provider => new WorksProofReminderIntentSource(
                    provider.GetRequiredService<Hexalith.Works.Reminders.WorksReminderIntentSource>(), provider.GetRequiredService<WorksReminderPhaseDiagnostics>()));
                services.AddSingleton<IStartupFilter, WorksReminderPhaseFilter>();
            }
            if (context.Configuration[WorksReminderProofProtocol.DirectoryKey] is not null)
                services.AddHostedService<WorksReminderProofService>();
            if (context.Configuration[WorksTrustedEffectProofProtocol.DirectoryKey] is not null)
                services.AddHostedService<WorksTrustedEffectProofService>();
            if (context.Configuration[WorksProofRuntimeInventoryService.PathKey] is not null)
                services.AddHostedService<WorksProofRuntimeInventoryService>();
            if (Environment.GetEnvironmentVariable(WorksPlatformReminderCallbackProbe.ActorEnvironment) is { Length: > 0 })
                services.AddSingleton<IStartupFilter, WorksPlatformReminderCallbackProbe>();
#if WORKS_PLATFORM_PARITY
            if (Environment.GetEnvironmentVariable("WORKS_PLATFORM_PARITY") == "1")
            {
                ProcessSource[] grants = JsonSerializer.Deserialize<ProcessSource[]>(
                    Environment.GetEnvironmentVariable("WORKS_PLATFORM_RECOVERY_GRANTS") ?? "[]")!;
                services.AddWorksSyntheticRecoveryRuntime(context.HostingEnvironment, grants, "eventstore-admin", "statestore");
                services.AddSingleton<IStartupFilter>(_ => new WorksPlatformDispositionProbe(grants));
                services.AddSingleton<Hexalith.EventStore.Client.Processes.IProcessDispositionAuditSink>(provider =>
                    new WorksSyntheticDispositionAuditSink(new WorksPlatformAuditFaultStore(provider.GetRequiredService<Hexalith.EventStore.Client.Projections.IReadModelStore>()),
                        provider.GetRequiredService<WorksSyntheticDispositionAuthorizer>(), "statestore", provider.GetRequiredService<TimeProvider>()));
            }
#endif
            services.AddSingleton<IReminderDelegationTokenProvider, WorksSdkProofDelegationProvider>();
            services.AddSingleton<Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter, WorksSdkProofResponseFilter>();
            if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_CALLBACKS") != "1")
                services.AddSingleton<IStartupFilter, WorksSdkProofWakeupFilter>();
        }
        else if (role == "eventstore")
        {
            if (context.Configuration[WorksRetainedFencePreflightService.PathKey] is not null)
                services.AddHostedService<WorksRetainedFencePreflightService>();
            if (context.Configuration[WorksFleetQuiescence.OwnerEnvironment] is not null)
            {
                services.AddSingleton<WorksStoppedFleetVerifier>();
                services.AddSingleton<IRouteCatalogQuiescenceVerifier>(provider => provider.GetRequiredService<WorksStoppedFleetVerifier>());
                services.AddHostedService<WorksStoppedFleetMaintenanceService>();
            }
            if (context.Configuration[WorksProofCallerProtocol.DirectoryKey] is not null)
                services.AddHostedService<WorksProofCallerService>();
            ConfigureMetadata(services);
            services.AddSingleton<IIdempotencyIntentAdapterRegistry>(provider => provider.GetRequiredService<WorksSdkProofIntentRegistry>());
            services.AddSingleton<ITrustedEffectRetentionGate, WorksSdkProofRetentionGate>();
            services.AddSingleton<ITrustedEffectAuditSink, WorksSdkProofAuditSink>();
            services.Configure<TrustedEffectAuthorityOptions>(options => options.Rules = [
                new("works", "works.date-resume", "work", "ResumeWorkItem"),
                new("works", "works.expiry", "work", "ExpireWorkItem"),
                new("works", "works.cascade-cancel", "work", "CancelWorkItem"),
                new("works", "works.cascade-expire", "work", "ExpireWorkItem"),
                new("works", "works.child-completion-resume", "work", "ResumeWorkItem"),
            ]);
        }
    }

    private static void ConfigureProofJwtBearer(IServiceCollection services, string pkcs8PrivateKey)
    {
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(pkcs8PrivateKey), out _);
        var key = new RsaSecurityKey(rsa) { KeyId = "story415-ephemeral" };
        services.AddSingleton(key);
        ConfigureProofJwtBearer(services, rsa, key);
        services.AddSingleton<IWorkloadAssertionIssuer>(provider => new WorksSdkProofWorkloadAssertionIssuer(
            provider.GetRequiredService<RsaSecurityKey>(),
            provider.GetService<IOptions<WorkloadAssertionIssuerOptions>>()?.Value.Workload));
    }

    private static void ConfigureProofJwtBearer(IServiceCollection services, RSA rsa, RsaSecurityKey key)
    {
        var signingKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false)) { KeyId = key.KeyId };
        // Gateway stream reads authenticate on DaprInternal, not the human Bearer scheme. Install the
        // proof authority on every JWT scheme while preserving each scheme's own audience.
        services.PostConfigureAll<JwtBearerOptions>(options => InstallProofAuthority(options, signingKey));
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var audiences = new HashSet<string>(StringComparer.Ordinal);
            if (options.TokenValidationParameters.ValidAudience is { Length: > 0 } audience)
                audiences.Add(audience);
            if (options.TokenValidationParameters.ValidAudiences is not null)
                foreach (string valid in options.TokenValidationParameters.ValidAudiences)
                    if (valid is { Length: > 0 }) audiences.Add(valid);
            audiences.Add("hexalith-eventstore");
            options.TokenValidationParameters.ValidAudience = "hexalith-eventstore";
            options.TokenValidationParameters.ValidAudiences = audiences;
        });
    }

    private static void InstallProofAuthority(JwtBearerOptions options, RsaSecurityKey signingKey)
    {
        var metadata = new OpenIdConnectConfiguration { Issuer = WorksSdkProofDelegationProvider.Issuer };
        metadata.SigningKeys.Add(signingKey);
        options.Authority = string.Empty;
        options.MetadataAddress = string.Empty;
        options.RequireHttpsMetadata = false;
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
        var issuers = new HashSet<string>(StringComparer.Ordinal) { WorksSdkProofDelegationProvider.Issuer };
        if (options.TokenValidationParameters.ValidIssuer is { Length: > 0 } issuer)
            issuers.Add(issuer);
        if (options.TokenValidationParameters.ValidIssuers is not null)
            foreach (string valid in options.TokenValidationParameters.ValidIssuers)
                if (valid is { Length: > 0 }) issuers.Add(valid);
        options.TokenValidationParameters.ValidIssuer = WorksSdkProofDelegationProvider.Issuer;
        options.TokenValidationParameters.ValidIssuers = issuers;
        options.TokenValidationParameters.IssuerSigningKey = signingKey;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        var algorithms = options.TokenValidationParameters.ValidAlgorithms is { } existing
            ? existing.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        _ = algorithms.Add(SecurityAlgorithms.RsaSha256);
        options.TokenValidationParameters.ValidAlgorithms = algorithms;
    }

    private static void InstallProofProcessOptions(IServiceCollection services)
        => services.Replace(ServiceDescriptor.Transient<IOptionsFactory<EventStoreProcessOptions>, WorksSdkProofProcessOptionsFactory>());

    private static void ConfigureMetadata(IServiceCollection services)
    {
        // Use the SDK's explicit Development response-writer seam to expose the actual
        // aggregate and individual readiness states required by the fixture's probes.
        services.Configure<Hexalith.EventStore.ServiceDefaults.EventStoreServiceDefaultsOptions>(options =>
            options.DevelopmentHealthResponseWriter = WorksReminderHealthProjection.WriteAsync);
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
