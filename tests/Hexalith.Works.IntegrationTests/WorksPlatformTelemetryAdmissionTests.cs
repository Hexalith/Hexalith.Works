using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Hexalith.Platform.Composition;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Qualifies telemetry admission against the supported Platform preview composition.</summary>
public sealed class WorksPlatformTelemetryAdmissionTests
{
    /// <summary>The supported preview composes both exporter keys through Aspire and refuses foreign fixture redirects.</summary>
    [Fact]
    public async Task ActualPreviewComposesExporterKeysAndRefusesForeignFixtureTelemetry()
    {
        string worksRoot = Path.GetFullPath(Path.Combine(SchemaEvolution.GoldenCorpusFiles.SourceDirectory("Golden"), "../../../.."));
        string platformRoot = Path.Combine(worksRoot, "references", "Hexalith.Platform");
        // Composition never launches a process; use private owner inputs with an inert isolated endpoint.
        string ownerDirectory = Directory.CreateTempSubdirectory("works-telemetry-profile-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ownerDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string OwnerComponent(string name)
            {
                string source = Path.Combine(platformRoot, "DaprComponents", name + ".yaml");
                string target = Path.Combine(ownerDirectory, name + ".yaml");
                File.WriteAllText(target, File.ReadAllText(source).Replace("localhost:6379", "127.0.0.1:27123", StringComparison.Ordinal));
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return target;
            }
            var catalog = new Hexalith.EventStore.Aspire.DevelopmentRouteCatalogCompositionOptions(
                Path.Combine(ownerDirectory, "catalog-custody"), "routing-telemetry", "works-telemetry", "telemetry",
                new Dictionary<string, string> { ["telemetry"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }, "telemetry");
            var profile = new WorksDevelopmentCatalogProfile(OwnerComponent("statestore"), catalog, 1,
                OwnerComponent("pubsub"), OwnerComponent("configstore"));
            string profilePath = Path.Combine(ownerDirectory, "profile.json");
            File.WriteAllBytes(profilePath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(profile));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(profilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
            {
                Args = ["--environment=Development"], ProjectDirectory = platformRoot,
                AssemblyName = typeof(PlatformAppModel).Assembly.GetName().Name, DisableDashboard = true, AllowUnsecuredTransport = true,
            });
            builder.Configuration["Platform:Works:SourceRoot"] = worksRoot;
            builder.Configuration["Platform:Works:EventStoreSourceRoot"] = Path.Combine(worksRoot, "references", "Hexalith.EventStore");
            builder.Configuration["Platform:Works:DevelopmentCatalogProfile"] = profilePath;
            WorksPreviewModel.Compose(builder);
            // Own only the workspace leases registered by this composition. Resolving Aspire's other
            // hosted services would require DCP tooling even though this test launches no resources.
            IServiceCollection cleanupServices = new ServiceCollection();
            foreach (ServiceDescriptor descriptor in builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationFactory?.Method.DeclaringType?.DeclaringType == typeof(WorksPreviewModel)))
                cleanupServices.Add(descriptor);
            cleanupServices.Count.ShouldBeGreaterThan(0, "Platform must register its composition workspace cleanup.");
            using ServiceProvider cleanup = cleanupServices.BuildServiceProvider();
            _ = cleanup.GetServices<IHostedService>().ToArray();
            Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> platform = WorksPlatformCompositionGuard.Capture(builder);
            Dictionary<string, object> baseline = await WorksPlatformCompositionGuard.VerifyAsync(
                builder, platform, platform, TestContext.Current.CancellationToken);
            var keys = System.Text.Json.JsonSerializer.SerializeToElement(baseline["works"]).GetProperty("platformComposedKeys")
                .EnumerateArray().Select(item => item.GetString()).ToArray();
            keys.ShouldContain("OTEL_EXPORTER_OTLP_ENDPOINT");
            keys.ShouldContain("OTEL_EXPORTER_OTLP_PROTOCOL");
            ProjectResource works = platform.Keys.Single(resource => resource.Name == "works");
            builder.CreateResourceBuilder(works).WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "https://foreign.example:4318")
                .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
            (await Should.ThrowAsync<InvalidOperationException>(() => WorksPlatformCompositionGuard.VerifyAsync(
                builder, platform, WorksPlatformCompositionGuard.Capture(builder), TestContext.Current.CancellationToken)))
                .Message.ShouldContain("does not redirect OTLP export to an owned loopback collector");
        }
        finally { Directory.Delete(ownerDirectory, recursive: true); }
    }
}
