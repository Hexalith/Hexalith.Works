using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>A Platform start refuses fixture configuration that sets, removes or changes any key Platform composed, even to an equal value.</summary>
public sealed class WorksPlatformCompositionGuardTests
{
    private const string PlatformHook = "/platform/Hexalith.Platform.Works.Runtime.dll";

    /// <summary>Equal, different and removed Platform values are refused; unrelated fixture keys are not.</summary>
    [Fact]
    public async Task FixtureWriteOfAPlatformComposedKeyRefusesEvenWithAnEqualValue()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        foreach ((string key, string value) in new[]
        {
            ("DOTNET_ENVIRONMENT", "Development"),
            ("Works__Recovery__UseSdkRuntime", "true"),
            ("Authentication__Workload__AllowedCallers__1", "eventstore-admin"),
            ("EventStore__RateLimiting__PermitLimit", "100000"),
        })
        {
            IDistributedApplicationBuilder builder = Composed(out IResourceBuilder<ProjectResource> works);
            Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> platform = WorksPlatformCompositionGuard.Capture(builder);
            works.WithEnvironment("Works__Query__PartiesGatewayBaseAddress", "http://127.0.0.1:9/").WithEnvironment(key, value);
            InvalidOperationException refused = await Should.ThrowAsync<InvalidOperationException>(() =>
                WorksPlatformCompositionGuard.VerifyAsync(builder, platform, WorksPlatformCompositionGuard.Capture(builder), ct));
            refused.Message.ShouldContain("works: " + key);
            refused.Message.ShouldNotContain("PartiesGatewayBaseAddress");
        }

        IDistributedApplicationBuilder removed = Composed(out IResourceBuilder<ProjectResource> host);
        Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> composed = WorksPlatformCompositionGuard.Capture(removed);
        host.Resource.Annotations.Remove(host.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Last());
        (await Should.ThrowAsync<InvalidOperationException>(() =>
            WorksPlatformCompositionGuard.VerifyAsync(removed, composed, WorksPlatformCompositionGuard.Capture(removed), ct)))
            .Message.ShouldContain("removed a Platform-composed environment callback");
    }

    /// <summary>
    /// A fixture callback that removes a Platform key, or rewrites one only where Platform composed it, writes nothing on an
    /// empty environment; the combined evaluation still refuses it. A list extension applied only to a composed value stays
    /// permitted and is recorded as a fixture key.
    /// </summary>
    [Fact]
    public async Task RemovalAndConditionalOverwriteOfAPlatformComposedKeyAreRefused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        foreach ((string key, string refusal, Action<IDictionary<string, object>> callback) in new (string, string, Action<IDictionary<string, object>>)[]
        {
            ("Authentication__Workload__AllowedCallers__1", "works: Authentication__Workload__AllowedCallers__1 was removed",
                environment => environment.Remove("Authentication__Workload__AllowedCallers__1")),
            ("EventStore__RateLimiting__PermitLimit", "works: EventStore__RateLimiting__PermitLimit was changed where Platform composed it",
                environment =>
                {
                    if (environment.ContainsKey("EventStore__RateLimiting__PermitLimit")) environment["EventStore__RateLimiting__PermitLimit"] = "100000";
                }),
            ("DOTNET_STARTUP_HOOKS", "works: DOTNET_STARTUP_HOOKS does not extend Platform's composed list",
                environment => environment.Remove("DOTNET_STARTUP_HOOKS")),
            (WorksPlatformCompositionGuard.AppChannelToken, "works: APP_API_TOKEN is not the captured synthetic app-channel secret",
                environment => environment.Remove(WorksPlatformCompositionGuard.AppChannelToken)),
        })
        {
            IDistributedApplicationBuilder builder = Composed(out IResourceBuilder<ProjectResource> works);
            Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> platform = WorksPlatformCompositionGuard.Capture(builder);
            works.WithEnvironment("WORKS_SDK_PROOF", "1").WithEnvironment(context => callback(context.EnvironmentVariables));
            InvalidOperationException refused = await Should.ThrowAsync<InvalidOperationException>(() =>
                WorksPlatformCompositionGuard.VerifyAsync(builder, platform, WorksPlatformCompositionGuard.Capture(builder), ct));
            refused.Message.ShouldContain(refusal, Case.Sensitive, key);
            refused.Message.ShouldNotContain("WORKS_SDK_PROOF", Case.Sensitive, key);
        }

        IDistributedApplicationBuilder extended = Composed(out IResourceBuilder<ProjectResource> host);
        Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> composed = WorksPlatformCompositionGuard.Capture(extended);
        host.WithEnvironment(context =>
        {
            if (context.EnvironmentVariables.TryGetValue("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", out object? value))
                context.EnvironmentVariables["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = value + ";Hexalith.Works.IntegrationTests";
        });
        Dictionary<string, object> evidence = await WorksPlatformCompositionGuard.VerifyAsync(extended, composed, WorksPlatformCompositionGuard.Capture(extended), ct);
        string json = System.Text.Json.JsonSerializer.Serialize(evidence["works"]);
        json.ShouldContain("\"fixtureKeys\":[\"ASPNETCORE_HOSTINGSTARTUPASSEMBLIES\"]");
        json.ShouldContain("\"key\":\"ASPNETCORE_HOSTINGSTARTUPASSEMBLIES\",\"rule\":\"extends-composed-list\"");
    }

    /// <summary>The proof extends Platform's hook lists and substitutes the captured synthetic channel secret; both are recorded.</summary>
    [Fact]
    public async Task ListExtensionAndCapturedChannelSecretArePermittedAndCompositionIsRecorded()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDistributedApplicationBuilder builder = Composed(out IResourceBuilder<ProjectResource> works);
        Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> platform = WorksPlatformCompositionGuard.Capture(builder);
        IResourceBuilder<ParameterResource> token = builder.AddParameter("proof-works-app-api-token", () => new string('a', 64), secret: true);
        works.WithEnvironment(context => WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(context.EnvironmentVariables,
                "DOTNET_STARTUP_HOOKS", "/consumer/Hexalith.Works.IntegrationTests.dll", Path.PathSeparator))
            .WithEnvironment(context => WorksSdkRecoveryPipelineSmokeTests.ExtendEnvironmentList(context.EnvironmentVariables,
                "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "Hexalith.Works.IntegrationTests", ';'))
            .WithEnvironment(WorksPlatformCompositionGuard.AppChannelToken, token)
            .WithEnvironment("WORKS_SDK_PROOF", "1");
        Dictionary<string, object> evidence = await WorksPlatformCompositionGuard.VerifyAsync(builder, platform, WorksPlatformCompositionGuard.Capture(builder), ct);
        string json = System.Text.Json.JsonSerializer.Serialize(evidence["works"]);
        json.ShouldContain("\"key\":\"DOTNET_STARTUP_HOOKS\",\"rule\":\"extends-composed-list\"");
        json.ShouldContain("\"key\":\"ASPNETCORE_HOSTINGSTARTUPASSEMBLIES\",\"rule\":\"extends-composed-list\"");
        json.ShouldContain("\"key\":\"APP_API_TOKEN\",\"rule\":\"captured-restorable-synthetic-secret\"");
        json.ShouldContain("\"Authentication__Workload__AllowedCallers__0\":\"eventstore\"");
        json.ShouldContain("\"EventStore__RateLimiting__PermitLimit\":\"8192\"");
        json.ShouldContain("\"DOTNET_ENVIRONMENT\":\"Development\"");
    }

    /// <summary>Replacing a composed list, or substituting a non-secret or foreign channel token, is refused.</summary>
    [Fact]
    public void ReplacedListsAndForeignChannelSecretsAreRefused()
    {
        var platform = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["DOTNET_STARTUP_HOOKS"] = PlatformHook,
            [WorksPlatformCompositionGuard.AppChannelToken] = new ParameterResource("works-app-api-token", _ => "a", secret: true),
        };
        foreach ((string name, object fixtureValue, object combined) in new (string, object, object)[]
        {
            ("replaced hook", "/consumer/proof.dll", "/consumer/proof.dll"),
            ("prefix only", PlatformHook + "x", PlatformHook + "x"),
            ("empty extension", PlatformHook + Path.PathSeparator, PlatformHook + Path.PathSeparator),
        })
        {
            var refusals = new List<string>();
            WorksPlatformCompositionGuard.Overlaps("works", platform, new Dictionary<string, object> { ["DOTNET_STARTUP_HOOKS"] = fixtureValue },
                Combined(platform, new Dictionary<string, object> { ["DOTNET_STARTUP_HOOKS"] = combined }), refusals).ShouldBeEmpty(name);
            refusals.ShouldHaveSingleItem(name).ShouldContain("does not extend");
        }

        foreach (ParameterResource token in new[]
        {
            new ParameterResource("proof-works-app-api-token", _ => "a", secret: false),
            new ParameterResource("proof-eventstore-app-api-token", _ => "a", secret: true),
        })
        {
            var refusals = new List<string>();
            var write = new Dictionary<string, object> { [WorksPlatformCompositionGuard.AppChannelToken] = token };
            WorksPlatformCompositionGuard.Overlaps("works", platform, write, Combined(platform, write), refusals).ShouldBeEmpty();
            refusals.ShouldHaveSingleItem().ShouldContain("captured synthetic app-channel secret");
        }
    }

    /// <summary>Only a complete redirect to an owned loopback HTTP collector may replace the orchestrator's OTLP export.</summary>
    [Fact]
    public void OnlyAnOwnedLoopbackCollectorMayReplaceTheComposedOtlpExport()
    {
        var platform = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:18889", ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc", ["OTEL_METRIC_EXPORT_INTERVAL"] = "1000",
        };
        var owned = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:41234", ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf", ["OTEL_METRIC_EXPORT_INTERVAL"] = "1000",
        };
        var accepted = new List<string>();
        WorksPlatformCompositionGuard.Overlaps("works", platform, owned, owned, accepted).Select(item => item.Rule).Distinct()
            .ShouldBe(["redirects-otlp-export-to-owned-loopback-collector"]);
        accepted.ShouldBeEmpty();
        foreach ((string key, string value) in new[]
        {
            ("OTEL_EXPORTER_OTLP_ENDPOINT", "http://collector.example:4318"),
            ("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc"),
            ("OTEL_METRIC_EXPORT_INTERVAL", "never"),
        })
        {
            var changed = new Dictionary<string, object>(owned, StringComparer.Ordinal) { [key] = value };
            var refusals = new List<string>();
            _ = WorksPlatformCompositionGuard.Overlaps("works", platform, changed, changed, refusals);
            refusals.ShouldNotBeEmpty(key);
        }

        var partial = new Dictionary<string, object>(StringComparer.Ordinal) { ["OTEL_METRIC_EXPORT_INTERVAL"] = "1000" };
        var partialRefusals = new List<string>();
        _ = WorksPlatformCompositionGuard.Overlaps("works", platform, partial, Combined(platform, partial), partialRefusals);
        partialRefusals.ShouldHaveSingleItem().ShouldContain("OTEL_METRIC_EXPORT_INTERVAL");
    }

    /// <summary>The effective redirect must remain loopback HTTP/protobuf even when a fixture behaves differently over Platform values.</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("endpoint")]
    [InlineData("protocol")]
    [InlineData("both")]
    [InlineData("remove-endpoint")]
    [InlineData("remove-protocol")]
    public async Task ConditionalTelemetryRedirectMustValidateTheCombinedEnvironment(string mutation)
    {
        IDistributedApplicationBuilder builder = Composed(out IResourceBuilder<ProjectResource> works);
        works.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:18889")
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
        Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> platform = WorksPlatformCompositionGuard.Capture(builder);
        works.WithEnvironment(context =>
        {
            bool composed = context.EnvironmentVariables.ContainsKey("DOTNET_ENVIRONMENT");
            context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = composed && (mutation is "endpoint" or "both")
                ? "https://foreign.example:4318" : "http://127.0.0.1:41234";
            context.EnvironmentVariables["OTEL_EXPORTER_OTLP_PROTOCOL"] = composed && (mutation is "protocol" or "both") ? "grpc" : "http/protobuf";
            if (composed && mutation == "remove-endpoint") context.EnvironmentVariables.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
            if (composed && mutation == "remove-protocol") context.EnvironmentVariables.Remove("OTEL_EXPORTER_OTLP_PROTOCOL");
        });

        if (mutation == "valid")
        {
            Dictionary<string, object> evidence = await WorksPlatformCompositionGuard.VerifyAsync(
                builder, platform, WorksPlatformCompositionGuard.Capture(builder), TestContext.Current.CancellationToken);
            System.Text.Json.JsonSerializer.Serialize(evidence["works"]).ShouldContain("redirects-otlp-export-to-owned-loopback-collector");
        }
        else
        {
            InvalidOperationException refused = await Should.ThrowAsync<InvalidOperationException>(() => WorksPlatformCompositionGuard.VerifyAsync(
                builder, platform, WorksPlatformCompositionGuard.Capture(builder), TestContext.Current.CancellationToken));
            refused.Message.ShouldContain("does not redirect OTLP export to an owned loopback collector");
        }
    }

    /// <summary>Unchanged Platform telemetry cannot legitimize invalid fixture declarations or a different effective collector.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EffectiveRedirectMustMatchTheFixtureOwnedCollector(bool differentLoopback)
    {
        IDistributedApplicationBuilder builder = Composed(out IResourceBuilder<ProjectResource> works);
        works.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:18889")
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]> platform = WorksPlatformCompositionGuard.Capture(builder);
        works.WithEnvironment(context =>
        {
            if (!context.EnvironmentVariables.ContainsKey("DOTNET_ENVIRONMENT"))
            {
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = differentLoopback ? "http://127.0.0.1:41234" : "dummy";
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_PROTOCOL"] = differentLoopback ? "http/protobuf" : "dummy";
            }
        });

        (await Should.ThrowAsync<InvalidOperationException>(() => WorksPlatformCompositionGuard.VerifyAsync(
            builder, platform, WorksPlatformCompositionGuard.Capture(builder), TestContext.Current.CancellationToken)))
            .Message.ShouldContain("does not redirect OTLP export to an owned loopback collector");
    }

    // The fixture's writes applied over Platform's values, as the guard's combined evaluation produces them.
    private static Dictionary<string, object> Combined(IReadOnlyDictionary<string, object> platform, IReadOnlyDictionary<string, object> writes)
    {
        var combined = new Dictionary<string, object>(platform, StringComparer.Ordinal);
        foreach ((string key, object value) in writes) combined[key] = value;
        return combined;
    }

    private static IDistributedApplicationBuilder Composed(out IResourceBuilder<ProjectResource> works)
    {
        // Composition only: the guard evaluates environment callbacks and nothing is built or launched.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = ["--environment=Development"], DisableDashboard = true,
            AssemblyName = typeof(WorksPlatformCompositionGuardTests).Assembly.GetName().Name,
        });
        IResourceBuilder<ParameterResource> generated = builder.AddParameter("works-app-api-token", () => new string('b', 64), secret: true);
        // A synthetic project file: the guard evaluates composed environment callbacks and never builds or launches it.
        string project = Path.Combine(Directory.CreateTempSubdirectory("story416-composition-guard-").FullName, "Hexalith.Works.csproj");
        File.WriteAllText(project, "<Project />");
        works = builder.AddProject("works", project)
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
            .WithEnvironment("Works__Recovery__UseSdkRuntime", "true")
            .WithEnvironment("Authentication__Workload__AllowedCallers__0", "eventstore")
            .WithEnvironment("Authentication__Workload__AllowedCallers__1", "eventstore-admin")
            .WithEnvironment("EventStore__RateLimiting__PermitLimit", "8192")
            .WithEnvironment("DOTNET_STARTUP_HOOKS", PlatformHook)
            .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "Hexalith.Platform.Works.Runtime")
            .WithEnvironment(WorksPlatformCompositionGuard.AppChannelToken, generated);
        return builder;
    }
}
