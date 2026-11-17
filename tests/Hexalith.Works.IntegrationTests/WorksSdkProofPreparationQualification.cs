using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Hexalith.EventStore.Aspire;
using Hexalith.Platform.Composition;
using Hexalith.Works.Runtime;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Runs real public SDK preparation in an ordinary console process with a clean assembly inventory.</summary>
public static class WorksSdkProofPreparationQualification
{
    /// <summary>Requires the real receipt, runtime dependency closure, metadata inventory and start guards.</summary>
    public static async Task<System.Text.Json.Nodes.JsonObject> RunAsync(string works, string proof, string runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(works);
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(runId);
        string launcherProof = proof;
        string scratch = Directory.CreateTempSubdirectory("works-proof-prepare-").FullName;
        try
        {
            string consumerOutput = Path.Combine(scratch, "consumer");
            await BuildConsumerAsync(works, consumerOutput, cancellationToken).ConfigureAwait(false);
            proof = Path.Combine(consumerOutput, "bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests.dll");
            string project = Path.Combine(works, "src/Hexalith.Works/Hexalith.Works.csproj");
            string consumer = Path.GetDirectoryName(proof)!;
            string targets = Path.Combine(works, "tests/Hexalith.Works.IntegrationTests/WorksPlatformProofRuntime.targets");
            using var assets = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(consumerOutput, "obj/Hexalith.Works.IntegrationTests/project.assets.json")));
            using var dependencies = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(proof, ".deps.json")));
            foreach (var dependency in System.Xml.Linq.XDocument.Load(targets).Descendants("PackageVersion").Where(item => item.Attribute("AssemblyVersion") is not null))
            {
                string name = dependency.Attribute("Include")!.Value, version = dependency.Attribute("Version")!.Value;
                string package = name + "/" + version;
                assets.RootElement.GetProperty("libraries").GetProperty(package).GetProperty("type").GetString().ShouldBe("package");
                dependencies.RootElement.GetProperty("libraries").GetProperty(package).GetProperty("type").GetString().ShouldBe("package");
                AssemblyName.GetAssemblyName(Path.Combine(consumer, name + ".dll")).Version!.ToString()
                    .ShouldBe(dependency.Attribute("AssemblyVersion")!.Value);
            }
            string manifest = Path.Combine(scratch, "proof.json");
            string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                schema = "hexalith.works-proof-runtime.v1", targetsPath = targets, targetsSha256 = Digest(targets),
                assemblyPath = proof, assemblySha256 = Digest(proof),
                files = Directory.EnumerateFiles(consumer, "*", SearchOption.AllDirectories).ToDictionary(path => path, Digest, StringComparer.Ordinal),
            }));
            string frozenConsumerManifest = Digest(manifest);
            int consumerFiles = Directory.EnumerateFiles(consumer, "*", SearchOption.AllDirectories).Count();
            var context = WorksProofRuntimeContext.Load(manifest, works).ShouldNotBeNull();
            var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
            builder.Environment.EnvironmentName = "Development";
            var selected = builder.AddProject("works", project);
#pragma warning disable ASPIREEXTENSION001
            var launch = (ProjectLaunchConfiguration)await selected.Resource.CreateLaunchConfigurationAsync(ExecutableLaunchMode.NoDebug,
                cancellationToken).ConfigureAwait(true);
            launch.BuildConfiguration = "Release";
            string output = Path.Combine(scratch, "artifacts");
            launch.BuildEnvironment = new Dictionary<string, string>
            {
                ["ArtifactsPath"] = output, ["UseArtifactsOutput"] = "true",
                ["UseHexalithProjectReferences"] = "false", ["UseNuGetDeps"] = "true",
                ["HexalithEventStoreFromSource"] = "false", ["HexalithPolymorphicSerializationsFromSource"] = "false",
                ["PathMap"] = output + "=/_/hexalith-works-platform-build",
                ["MSBuildWarningsAsErrors"] = "MSB3277", ["TreatWarningsAsErrors"] = "true",
                ["NuGetAudit"] = "true", ["NuGetAuditMode"] = "all", ["NuGetAuditLevel"] = "low",
            };
            foreach (string key in new[] { "Version", "MinVerVersionOverride", "HexalithBuildsPublishedToolRoot", "HexalithBuildsRoot", "PlatformWorksRuntimeRoot" })
                if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value) launch.BuildEnvironment[key] = value;
            context.Configure(launch);
            selected.WithDebugSupport(_ => Task.FromResult(launch), "project");
#pragma warning restore ASPIREEXTENSION001
            context.ObserveStarts(builder, [selected.Resource]);
            context.Verify();
            var prepared = await selected.PrepareEventStoreDevelopmentOwnerAsync(TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(true);
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
            int declarations = Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.Read([actualOwner]).Count;
            declarations.ShouldBeGreaterThan(0);
            int operations = Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.ReadOperations([actualOwner]).Count;
            operations.ShouldBeGreaterThan(0);
            Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.ReadOperations([actualOwner]).Count
                .ShouldBe(Hexalith.EventStore.ServiceDefaults.Routing.CompiledRouteCatalogDeclarations.ReadOperations([prepared.Owner.Contracts]).Count);
            WorksRetainedCatalogEnrollment.RequireQualifiedDeployment(project, project, Path.GetDirectoryName(entry)!, consumer,
                Path.ChangeExtension(proof, ".deps.json"), context, qualified);
            qualified.ObserveStarts(builder, selected.Resource);
            using var application = builder.Build();
            await builder.Eventing.PublishAsync(new BeforeResourceStartedEvent(selected.Resource, application.Services), cancellationToken).ConfigureAwait(false);
            File.AppendAllText(manifest, " ");
            (await Should.ThrowAsync<InvalidOperationException>(() => builder.Eventing.PublishAsync(
                new BeforeResourceStartedEvent(selected.Resource, application.Services), cancellationToken)).ConfigureAwait(false)).Message.ShouldContain("changed");
            return new System.Text.Json.Nodes.JsonObject
            {
                ["schema"] = "hexalith.works-sdk-proof-qualification.v1", ["runId"] = runId,
                ["processId"] = Environment.ProcessId, ["launcherProofPath"] = launcherProof, ["launcherProofSha256"] = Digest(launcherProof),
                ["proofPath"] = proof, ["proofSha256"] = Digest(proof),
                ["consumerManifestSha256"] = frozenConsumerManifest, ["consumerFileCount"] = consumerFiles,
                ["runtimeEntrySha256"] = Digest(entry), ["moduleVersionId"] = actualOwner.ManifestModule.ModuleVersionId.ToString(),
                ["declarations"] = declarations, ["operations"] = operations,
                ["beforeStartVerified"] = true, ["changedOriginalRefused"] = true,
            };
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
    private static async Task BuildConsumerAsync(string works, string output, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        { WorkingDirectory = works, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "build", Path.Combine(works, "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj"),
            "-c", "Release", "-p:MSBuildWarningsAsErrors=MSB3277", "-p:TreatWarningsAsErrors=true",
            "-p:NuGetAudit=true", "-p:NuGetAuditMode=all", "-p:NuGetAuditLevel=low", "--artifacts-path", output, "--disable-build-servers", "-m:1", "-nr:false",
            "-p:PathMap=" + output + "=/_/hexalith-works-platform-build", "-p:CustomAfterMicrosoftCommonTargets=", "-p:HexalithWorksProofAssembly=",
            "-p:UseHexalithProjectReferences=false", "-p:UseNuGetDeps=true", "-p:HexalithEventStoreFromSource=false",
            "-p:HexalithPolymorphicSerializationsFromSource=false", "-p:WorksAllowLocalEventStoreCandidate=false" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The isolated public consumer qualification did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
        }
        string diagnostics = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        process.ExitCode.ShouldBe(0, diagnostics);
    }

}
