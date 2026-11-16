using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Hexalith.EventStore.Aspire;
using Hexalith.Platform.Composition;
using Hexalith.Works.Runtime;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises the published SDK with the real prebuilt proof and its separately qualified host body.</summary>
public sealed class WorksSdkProofPreparationTests
{
    /// <summary>The original fixture is declared before preparation and remains frozen at the actual start boundary.</summary>
    [Fact(Timeout = 180000)]
    public async Task PublishedSdkPreparesTheRealDeclaredProofAndGuardsItsStartAsync()
    {
        string scratch = Directory.CreateTempSubdirectory("works-proof-prepare-").FullName;
        try
        {
            string works = SourceRoot();
            string project = Path.Combine(works, "src/Hexalith.Works/Hexalith.Works.csproj");
            string proof = typeof(WorksSdkProofPreparationTests).Assembly.Location;
            string consumer = Path.GetDirectoryName(proof)!;
            string targets = Path.Combine(works, "tests/Hexalith.Works.IntegrationTests/WorksPlatformProofRuntime.targets");
            string manifest = Path.Combine(scratch, "proof.json");
            string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                schema = "hexalith.works-proof-runtime.v1", targetsPath = targets, targetsSha256 = Digest(targets),
                assemblyPath = proof, assemblySha256 = Digest(proof),
                files = Directory.EnumerateFiles(consumer, "*", SearchOption.AllDirectories).ToDictionary(path => path, Digest, StringComparer.Ordinal),
            }));
            var context = WorksProofRuntimeContext.Load(manifest, works).ShouldNotBeNull();
            var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
            builder.Environment.EnvironmentName = "Development";
            var selected = builder.AddProject("works", project);
#pragma warning disable ASPIREEXTENSION001
            var launch = (ProjectLaunchConfiguration)await selected.Resource.CreateLaunchConfigurationAsync(ExecutableLaunchMode.NoDebug,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            launch.BuildConfiguration = "Release";
            string output = Path.Combine(scratch, "artifacts");
            launch.BuildEnvironment = new Dictionary<string, string>
            {
                ["ArtifactsPath"] = output, ["UseArtifactsOutput"] = "true",
                ["UseHexalithProjectReferences"] = "false", ["UseNuGetDeps"] = "true",
                ["HexalithEventStoreFromSource"] = "false", ["HexalithPolymorphicSerializationsFromSource"] = "false",
                ["PathMap"] = output + "=/_/hexalith-works-platform-build",
            };
            foreach (string key in new[] { "Version", "MinVerVersionOverride", "HexalithBuildsPublishedToolRoot", "HexalithBuildsRoot", "PlatformWorksRuntimeRoot" })
                if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value) launch.BuildEnvironment[key] = value;
            context.Configure(launch);
            selected.WithDebugSupport(_ => Task.FromResult(launch), "project");
#pragma warning restore ASPIREEXTENSION001
            context.ObserveStarts(builder, [selected.Resource]);
            context.Verify();
            var prepared = await selected.PrepareEventStoreDevelopmentOwnerAsync(TimeSpan.FromSeconds(120), TestContext.Current.CancellationToken).ConfigureAwait(true);
            await using var preparationLifetime = prepared.ConfigureAwait(true);
            context.Verify();
            using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(prepared.Directory, "preparation.json")));
            string entry = receipt.RootElement.GetProperty("targetPath").GetString()!;
            string deployedProof = Path.Combine(Path.GetDirectoryName(entry)!, Path.GetFileName(proof));
            context.MatchesProofAssembly(deployedProof).ShouldBeTrue();
            WorksDeclaredProofRuntime.Capture(manifest, Path.ChangeExtension(entry, ".deps.json"), deployedProof)["declared"]!.GetValue<bool>().ShouldBeTrue();
            Digest(entry).ShouldBe(receipt.RootElement.GetProperty("targetSha256").GetString());
            string runtimeManifest = Path.Combine(scratch, "runtime.json");
            File.WriteAllText(runtimeManifest, JsonSerializer.Serialize(new
            {
                schema = "hexalith.works-qualified-runtime.v1", projectPath = project, entryPath = entry, entrySha256 = Digest(entry),
                files = Directory.EnumerateFiles(Path.GetDirectoryName(entry)!, "*", SearchOption.AllDirectories).ToDictionary(path => path, Digest, StringComparer.Ordinal),
                sourceFiles = new Dictionary<string, string> { [project] = Digest(project) },
            }));
            using var qualified = new WorksQualifiedRuntimeContext(runtimeManifest, project, Path.GetDirectoryName(entry)!);
            Assembly actualOwner = qualified.ReadOwner();
            actualOwner.ManifestModule.ModuleVersionId.ShouldBe(prepared.Owner.Contracts.ManifestModule.ModuleVersionId);
            actualOwner.Location.ShouldBe(entry);
            Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.Read([actualOwner]).Count.ShouldBeGreaterThan(0);
            Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.ReadOperations([actualOwner]).Count.ShouldBeGreaterThan(0);
            Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.ReadOperations([actualOwner]).Count
                .ShouldBe(Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.ReadOperations([prepared.Owner.Contracts]).Count);
            WorksRetainedCatalogEnrollment.RequireQualifiedDeployment(project, project, Path.GetDirectoryName(entry)!, consumer,
                Path.ChangeExtension(proof, ".deps.json"), context, qualified);
            qualified.ObserveStarts(builder, selected.Resource);
            using var application = builder.Build();
            await builder.Eventing.PublishAsync(new BeforeResourceStartedEvent(selected.Resource, application.Services), TestContext.Current.CancellationToken);
            File.AppendAllText(manifest, " ");
            (await Should.ThrowAsync<InvalidOperationException>(() => builder.Eventing.PublishAsync(
                new BeforeResourceStartedEvent(selected.Resource, application.Services), TestContext.Current.CancellationToken))).Message.ShouldContain("changed");
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static string SourceRoot([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
