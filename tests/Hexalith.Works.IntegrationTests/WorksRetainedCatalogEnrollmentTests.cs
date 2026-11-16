using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.EventStore.Aspire;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>The archived model may consume only its exact prequalified deployment, never borrowed metadata or missing source bytes.</summary>
public sealed class WorksRetainedCatalogEnrollmentTests
{
    /// <summary>The actual environment reuses its secret parameter, retains workload identity and passes SDK JWT validation.</summary>
    [Fact]
    public async Task ArchivedOperationsUsesItsExistingIssuerWithoutChangingCallerPolicy()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        builder.Environment.EnvironmentName = Microsoft.Extensions.Hosting.Environments.Development;
        var operations = builder.CreateResourceBuilder(new ProjectResource("eventstore-operations"));
        operations.WithEnvironment("Authentication__Workload__AllowedCallers__0", "eventstore-admin");
        var key = builder.AddParameter("works-local-jwt-signing-key", () => "ExistingPrivateSyntheticKey-AtLeast32Chars", secret: true);
        WorksRetainedCatalogEnrollment.ConfigureOperationsAuthentication(operations, key);
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, operations.Resource, environment, TestContext.Current.CancellationToken);
        foreach (var callback in operations.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>()) await callback.Callback(context);
        environment["Authentication__JwtBearer__SigningKey"].ShouldBeSameAs(key.Resource);
        environment["Authentication__WorkloadIssuer__Workload"].ShouldBe("eventstore-operations");
        environment["Authentication__Workload__AllowedCallers__0"].ShouldBe("eventstore-admin");
        var settings = new Dictionary<string, string?>();
        foreach (var pair in environment)
            settings[pair.Key.Replace("__", ":", StringComparison.Ordinal)] = pair.Value is IValueProvider provider
                ? await provider.GetValueAsync(TestContext.Current.CancellationToken) : pair.Value.ToString();
        var options = new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
            .GetSection("Authentication:JwtBearer").Get<JwtBearerAuthenticationOptions>()!;
        JwtBearerAuthenticationContract.Validate(options, builder.Environment, "Authentication:JwtBearer").Succeeded.ShouldBeTrue();
    }

    /// <summary>The selected component file must match the captured owned backend, independently of archived defaults.</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("different-backend")]
    [InlineData("missing-selected")]
    [InlineData("missing-captured")]
    [InlineData("foreign-path")]
    [InlineData("model-default")]
    public void AuthorityUsesTheActualCapturedBusinessComponent(string fault)
    {
        string owner = Directory.CreateTempSubdirectory("works-retained-backend-").FullName;
        string foreign = Directory.CreateTempSubdirectory("works-retained-foreign-").FullName;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(owner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(foreign, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            string captured = Path.Combine(owner, "captured.yaml");
            string selected = Path.Combine(fault == "foreign-path" ? foreign : owner, "statestore.yaml");
            const string component = "kind: Component\nmetadata:\n  name: statestore\nspec:\n  type: state.redis\n  metadata:\n    - name: redisHost\n      value: \"127.0.0.1:27123\"\n    - name: redisPassword\n      value: \"\"\n    - name: actorStateStore\n      value: \"true\"\n";
            File.WriteAllText(captured, component);
            File.WriteAllText(selected, fault == "different-backend" ? component.Replace("127.0.0.1:27123", "localhost:6379", StringComparison.Ordinal) : File.ReadAllText(captured));
            if (!OperatingSystem.IsWindows())
                foreach (string file in new[] { captured, selected }) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (fault == "missing-selected") File.Delete(selected);
            if (fault == "missing-captured") File.Delete(captured);
            var state = new DaprComponentResource("statestore", "state.redis") { Options = fault == "model-default" ? null : new DaprComponentOptions { LocalPath = selected } };
            if (fault == "valid") WorksRetainedCatalogEnrollment.RequireAuthorityComponentPath(state, captured, "127.0.0.1:27123").ShouldBe(selected);
            else Should.Throw<InvalidOperationException>(() => WorksRetainedCatalogEnrollment.RequireAuthorityComponentPath(state, captured, "127.0.0.1:27123"));
        }
        finally { Directory.Delete(owner, true); Directory.Delete(foreign, true); }
    }

    /// <summary>A real SDK model's original default annotation cannot override the actual owner-selected component.</summary>
    [Fact]
    public void OriginalSdkBackendModelCannotOverrideTheCapturedLocalComponent()
    {
        string owner = Directory.CreateTempSubdirectory("works-retained-model-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(owner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string captured = Path.Combine(owner, "captured.yaml"), actual = Path.Combine(owner, "statestore.yaml");
            const string bytes = "kind: Component\nmetadata:\n  name: statestore\nspec:\n  type: state.redis\n  metadata:\n    - name: redisHost\n      value: \"127.0.0.1:27123\"\n    - name: redisPassword\n      value: \"\"\n    - name: actorStateStore\n      value: \"true\"\n";
            File.WriteAllText(captured, bytes); File.WriteAllText(actual, bytes);
            if (!OperatingSystem.IsWindows()) foreach (string file in new[] { captured, actual }) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
            var gateway = builder.CreateResourceBuilder(new ProjectResource("eventstore"));
            var topology = builder.AddHexalithEventStore(gateway, adminServer: null);
            topology.StateStore.Resource.Options.ShouldBeNull();
            topology.StateStore.Resource.Annotations.ShouldContain(annotation => annotation.GetType().Name == "EventStoreRedisBackendAnnotation");
            typeof(DaprComponentResource).GetProperty(nameof(DaprComponentResource.Options))!.SetValue(topology.StateStore.Resource, new DaprComponentOptions { LocalPath = actual });
            WorksRetainedCatalogEnrollment.RequireAuthorityComponentPath(topology.StateStore.Resource, captured, "127.0.0.1:27123").ShouldBe(actual);
            File.WriteAllText(actual, bytes.Replace("127.0.0.1:27123", "localhost:6379", StringComparison.Ordinal));
            Should.Throw<InvalidOperationException>(() => WorksRetainedCatalogEnrollment.RequireAuthorityComponentPath(topology.StateStore.Resource, captured, "127.0.0.1:27123"));
        }
        finally { Directory.Delete(owner, true); }
    }

    /// <summary>Every SDK/Works runtime DLL is required, including files absent from the caller closure.</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("foreign-project")]
    [InlineData("changed-sdk")]
    [InlineData("missing-consumer-sdk")]
    [InlineData("missing-runtime-sdk")]
    [InlineData("missing-runtime-client")]
    [InlineData("foreign-runtime-sdk")]
    [InlineData("proof-valid")]
    [InlineData("proof-foreign-body")]
    [InlineData("proof-missing-transitive")]
    [InlineData("proof-extra-transitive")]
    [InlineData("proof-changed-transitive")]
    public void DeploymentIdentityRequiresTheActualProjectAndCompleteEqualOwnedDlls(string fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        string directory = Directory.CreateTempSubdirectory("works-retained-closure-").FullName;
        try
        {
            string runtime = Path.Combine(directory, "runtime"), consumer = Path.Combine(directory, "consumer");
            Directory.CreateDirectory(runtime); Directory.CreateDirectory(consumer);
            string project = Path.Combine(directory, "Hexalith.Works.csproj"), other = Path.Combine(directory, "Other.csproj");
            File.WriteAllText(project, "<Project />"); File.WriteAllText(other, "<Project />");
            foreach (string name in new[] { "Hexalith.Works.dll", "Hexalith.Works.Contracts.dll", "Hexalith.EventStore.ServiceDefaults.dll", "Hexalith.EventStore.Client.dll" })
            { File.WriteAllBytes(Path.Combine(runtime, name), [1, 2, 3]); File.WriteAllBytes(Path.Combine(consumer, name), [1, 2, 3]); }
            string manifest = Path.Combine(consumer, "Hexalith.Works.IntegrationTests.deps.json");
            var nodes = new Dictionary<string, object>
            {
                ["Hexalith.Works/1"] = new { dependencies = new Dictionary<string, string> { ["Hexalith.Works.Contracts"] = "1", ["Hexalith.EventStore.ServiceDefaults"] = "1" }, runtime = new Dictionary<string, object> { ["Hexalith.Works.dll"] = new { } } },
                ["Hexalith.Works.Contracts/1"] = new { dependencies = new Dictionary<string, string> { ["Hexalith.EventStore.Client"] = "1" }, runtime = new Dictionary<string, object> { ["Hexalith.Works.Contracts.dll"] = new { } } },
                ["Hexalith.EventStore.Client/1"] = new { runtime = new Dictionary<string, object> { ["Hexalith.EventStore.Client.dll"] = new { } } },
                ["Hexalith.EventStore.ServiceDefaults/1"] = new { runtime = new Dictionary<string, object> { ["Hexalith.EventStore.ServiceDefaults.dll"] = new { } } },
                // A test-only consumer node is not part of the actual Works executable dependency graph.
                ["Hexalith.EventStore.Testing/1"] = new { runtime = new Dictionary<string, object> { ["Hexalith.EventStore.Testing.dll"] = new { } } },
            };
            bool declaredProof = fault.StartsWith("proof-", StringComparison.Ordinal);
            if (declaredProof)
                nodes["Hexalith.Works.IntegrationTests/1"] = new { dependencies = new Dictionary<string, string> { ["Hexalith.Works"] = "1", ["Hexalith.EventStore.Testing"] = "1" },
                    runtime = new Dictionary<string, object> { ["Hexalith.Works.IntegrationTests.dll"] = new { } } };
            File.WriteAllText(manifest, System.Text.Json.JsonSerializer.Serialize(new { runtimeTarget = new { name = "runtime" }, targets = new Dictionary<string, object> { ["runtime"] = nodes } }));
            File.WriteAllBytes(Path.Combine(consumer, "Hexalith.EventStore.Testing.dll"), [4]);
            if (fault == "missing-runtime-client") File.Delete(Path.Combine(runtime, "Hexalith.EventStore.Client.dll"));
            if (fault == "foreign-runtime-sdk") File.WriteAllBytes(Path.Combine(runtime, "Hexalith.EventStore.Foreign.dll"), [4]);
            if (fault == "changed-sdk") File.WriteAllBytes(Path.Combine(runtime, "Hexalith.EventStore.ServiceDefaults.dll"), [9]);
            if (fault == "missing-consumer-sdk") File.Delete(Path.Combine(consumer, "Hexalith.EventStore.ServiceDefaults.dll"));
            if (fault == "missing-runtime-sdk") File.Delete(Path.Combine(runtime, "Hexalith.EventStore.ServiceDefaults.dll"));
            Hexalith.Platform.Composition.WorksProofRuntimeContext? proofRuntime = null;
            WorksQualifiedRuntimeContext? qualifiedRuntime = null;
            if (declaredProof)
            {
                string proof = Path.Combine(consumer, "Hexalith.Works.IntegrationTests.dll");
                File.WriteAllBytes(proof, [5]);
                File.WriteAllText(Path.ChangeExtension(proof, ".runtimeconfig.json"), "{}");
                File.Copy(proof, Path.Combine(runtime, Path.GetFileName(proof)));
                File.Copy(Path.Combine(consumer, "Hexalith.EventStore.Testing.dll"), Path.Combine(runtime, "Hexalith.EventStore.Testing.dll"));
                string targets = Path.Combine(directory, "tests/Hexalith.Works.IntegrationTests/WorksPlatformProofRuntime.targets");
                Directory.CreateDirectory(Path.GetDirectoryName(targets)!); File.WriteAllText(targets, "<Project />");
                string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
                string binding = Path.Combine(directory, "manifest.json");
                File.WriteAllText(binding, System.Text.Json.JsonSerializer.Serialize(new
                {
                    schema = "hexalith.works-proof-runtime.v1", targetsPath = targets, targetsSha256 = Hash(targets),
                    assemblyPath = proof, assemblySha256 = Hash(proof),
                    files = Directory.EnumerateFiles(consumer).ToDictionary(path => path, Hash, StringComparer.Ordinal),
                }));
                proofRuntime = Hexalith.Platform.Composition.WorksProofRuntimeContext.Load(binding, directory);
                string qualifiedBinding = Path.Combine(directory, "qualified-runtime.json");
                string entry = Path.Combine(runtime, "Hexalith.Works.dll");
                File.WriteAllText(qualifiedBinding, System.Text.Json.JsonSerializer.Serialize(new
                {
                    schema = "hexalith.works-qualified-runtime.v1", projectPath = project, entryPath = entry, entrySha256 = Hash(entry),
                    files = Directory.EnumerateFiles(runtime).ToDictionary(path => path, Hash, StringComparer.Ordinal),
                    sourceFiles = new Dictionary<string, string> { [project] = Hash(project) },
                }));
                qualifiedRuntime = new WorksQualifiedRuntimeContext(qualifiedBinding, project, runtime);
                if (fault == "proof-foreign-body") File.WriteAllBytes(Path.Combine(runtime, Path.GetFileName(proof)), [9]);
                if (fault == "proof-missing-transitive") File.Delete(Path.Combine(runtime, "Hexalith.EventStore.Testing.dll"));
                if (fault == "proof-extra-transitive") File.WriteAllBytes(Path.Combine(runtime, "Hexalith.Works.Foreign.dll"), [9]);
                if (fault == "proof-changed-transitive") File.WriteAllBytes(Path.Combine(runtime, "Hexalith.EventStore.Testing.dll"), [9]);
            }
            void Verify() => WorksRetainedCatalogEnrollment.RequireQualifiedDeployment(fault == "foreign-project" ? other : project, project, runtime, consumer, manifest, proofRuntime, qualifiedRuntime);
            if (fault is "valid" or "proof-valid") Verify();
            else Should.Throw<Exception>(Verify);
            if (fault == "proof-valid") qualifiedRuntime?.Dispose();
            else qualifiedRuntime?.Unload();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
