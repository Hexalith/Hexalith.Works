using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Hexalith.EventStore.Aspire;
using Hexalith.Platform.Composition;
using Hexalith.Works.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Enrols the actual archived topology's selected qualified deployment through the SDK without rebuilding the frozen host.</summary>
internal sealed class WorksRetainedCatalogEnrollment(DevelopmentRouteCatalogComposition catalog) : IHostedService, IAsyncDisposable
{
    /// <summary>Registers real observed receivers and the retained owner's real CAS/key custody.</summary>
    internal static void Configure(IDistributedApplicationTestingBuilder builder, string profilePath, string ownedRedisEndpoint)
    {
        if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Retained fleet enrollment requires Development.");
        builder.Configuration["Platform:Works:DevelopmentCatalogProfile"] = Path.GetFullPath(profilePath);
        string owner = Path.GetDirectoryName(Path.GetFullPath(profilePath))!;
        WorksProofCallerProtocol.RequireDirectory(owner);
        if (new FileInfo(profilePath).Length > 1024 * 1024) throw new InvalidOperationException("The retained owner profile exceeds its bound.");
        var profile = JsonSerializer.Deserialize<WorksDevelopmentCatalogProfile>(WorksProofCallerProtocol.ReadAsync(profilePath, CancellationToken.None).GetAwaiter().GetResult())
            ?? throw new InvalidOperationException("The retained owner profile is absent.");
        if (profile.Replicas != 1 || !Path.GetFullPath(profile.Catalog.CustodyDirectory).StartsWith(owner + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("The archived topology requires its exact four-host private owner profile.");
        foreach (string? component in new[] { profile.StateStoreComponentPath, profile.PubSubComponentPath, profile.ConfigStoreComponentPath })
        {
            if (component is null || !Path.IsPathFullyQualified(component) || !Path.GetFullPath(component).StartsWith(owner + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("The retained component is outside its captured owner profile.");
            WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(component)!);
            _ = WorksProofCallerProtocol.ReadAsync(component, CancellationToken.None).GetAwaiter().GetResult();
        }
        IResourceBuilder<ProjectResource> Project(string name) => builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == name));
        var works = Project("works"); var gateway = Project("eventstore"); var admin = Project("eventstore-admin"); var operations = Project("eventstore-operations");
        string selectedProject = Path.GetFullPath(works.Resource.GetProjectMetadata().ProjectPath);
        string expectedRoot = Environment.GetEnvironmentVariable("WORKS_PLATFORM_WORKS_ROOT")
            ?? throw new InvalidOperationException("The actual qualified Works deployment must be explicit.");
        Assembly assembly = typeof(WorksHost).Assembly;
        RequireQualifiedDeployment(selectedProject, Path.Combine(expectedRoot, "src/Hexalith.Works/Hexalith.Works.csproj"),
            WorksPlatformRuntimeArtifacts.DirectoryFor(selectedProject, assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration),
            Path.GetDirectoryName(assembly.Location)!, Path.ChangeExtension(typeof(WorksAppHostSmokeHarness).Assembly.Location, ".deps.json"));
        string statePath = RequireAuthorityComponentPath(builder.Resources.OfType<IDaprComponentResource>().Single(resource => resource.Name == "statestore"),
            profile.StateStoreComponentPath, ownedRedisEndpoint);
        admin.WithEnvironment("EventStore__RouteCatalog__DevelopmentBootstrap__Role", "admin-receiver");
        operations.WithEnvironment("EventStore__RouteCatalog__DevelopmentBootstrap__Role", "operations-receiver");
        // The archived host predates mandatory Operations JWT validation. Reuse its actual
        // private local issuer parameter; no credential or permission is created for this receiver.
        ConfigureOperationsAuthentication(operations, builder.CreateResourceBuilder(builder.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == "works-local-jwt-signing-key")));
        // SuppressBuild remains the archived model's actual launch metadata. The complete selected
        // deployment was independently qualified above and is independently observed in each child.
        var composition = builder.AddEventStoreDevelopmentRouteCatalog(gateway, [new(works, assembly, assembly)],
            [gateway.CaptureEventStoreDevelopmentReceiver(works, admin), works.CaptureEventStoreDevelopmentReceiver(gateway, operations, admin),
                admin.CaptureEventStoreDevelopmentReceiver(), operations.CaptureEventStoreDevelopmentReceiver(admin)], statePath, options: profile.Catalog);
        builder.Services.AddSingleton<IHostedService>(_ => new WorksRetainedCatalogEnrollment(composition));
    }

    /// <summary>Uses the actual captured business component, rather than the archived SDK's original backend annotation.</summary>
    internal static string RequireAuthorityComponentPath(IDaprComponentResource state, string capturedPath, string ownedRedisEndpoint)
    {
        string owner = Path.GetDirectoryName(Path.GetFullPath(capturedPath))!;
        if (state.Name != "statestore" || state.Type != "state.redis" || state.Options?.LocalPath is not { } selected
            || !Path.GetFullPath(selected).StartsWith(owner + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("The archived authority requires the actual owner-captured Redis component.");
        WorksProofCallerProtocol.RequireDirectory(owner);
        WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(Path.GetFullPath(selected))!);
        byte[] captured = WorksProofCallerProtocol.ReadAsync(capturedPath, CancellationToken.None).GetAwaiter().GetResult();
        byte[] actual = WorksProofCallerProtocol.ReadAsync(selected, CancellationToken.None).GetAwaiter().GetResult();
        if (!actual.AsSpan().SequenceEqual(captured))
            throw new InvalidOperationException("The actual business Redis component differs from its retained owner profile.");
        object decoded = new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(System.Text.Encoding.UTF8.GetString(actual));
        byte[] json = System.Text.Encoding.UTF8.GetBytes(new YamlDotNet.Serialization.SerializerBuilder().JsonCompatible().Build().Serialize(decoded));
        WorksRetainedCatalogCustody.RequireOwnedBackend(json, "statestore", ownedRedisEndpoint, business: true);
        return Path.GetFullPath(selected);
    }

    /// <summary>Completes the archived Development receiver with the same existing issuer as its actual peers.</summary>
    internal static void ConfigureOperationsAuthentication(IResourceBuilder<ProjectResource> operations, IResourceBuilder<ParameterResource> signingKey)
    {
        if (operations.Resource.Name != "eventstore-operations" || signingKey.Resource.Name != "works-local-jwt-signing-key")
            throw new InvalidOperationException("The retained receiver requires its exact existing local issuer parameter.");
        operations.WithEnvironment("Authentication__JwtBearer__Authority", string.Empty)
            .WithEnvironment("Authentication__JwtBearer__Issuer", "hexalith-dev")
            .WithEnvironment("Authentication__JwtBearer__Audience", HexalithEventStoreSecurityOptions.DefaultAudience)
            .WithEnvironment("Authentication__JwtBearer__ValidAudiences__0", HexalithEventStoreSecurityOptions.DefaultAudience)
            .WithEnvironment("Authentication__JwtBearer__AllowedAlgorithms__0", "HS256")
            .WithEnvironment("Authentication__JwtBearer__SigningKey", signingKey)
            .WithEnvironment("Authentication__JwtBearer__RequireHttpsMetadata", "false")
            .WithEnvironment("Authentication__WorkloadIssuer__Workload", operations.Resource.Name);
    }

    /// <summary>Requires actual project identity and every selected Works/SDK deployment DLL to match the qualified consumer closure.</summary>
    internal static void RequireQualifiedDeployment(string selectedProject, string expectedProject, string runtimeDirectory, string consumerDirectory, string consumerManifest)
    {
        if (Path.GetFullPath(selectedProject) != Path.GetFullPath(expectedProject) || !File.Exists(selectedProject))
            throw new InvalidOperationException("The archived model selected a foreign Works executable.");
        string[] files = Directory.EnumerateFiles(runtimeDirectory, "*.dll").Where(path => Owned(Path.GetFileName(path))).ToArray();
        var expectedNames = QualifiedClosure(consumerManifest);
        if (!expectedNames.Contains("Hexalith.Works.dll") || !expectedNames.Contains("Hexalith.EventStore.ServiceDefaults.dll")
            || !expectedNames.SetEquals(files.Select(Path.GetFileName)!))
            throw new InvalidOperationException("The actual deployment's SDK/Works name set differs from the qualified executable dependency closure.");
        foreach (string runtime in files)
        {
            byte[] actual = SHA256.HashData(File.ReadAllBytes(runtime));
            byte[] expected = SHA256.HashData(File.ReadAllBytes(Path.Combine(consumerDirectory, Path.GetFileName(runtime))));
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException("The archived model deployment differs from its actual qualified consumer closure.");
        }
    }

    private static bool Owned(string name) => name.StartsWith("Hexalith.EventStore.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Hexalith.Works.", StringComparison.OrdinalIgnoreCase) || name.Equals("Hexalith.Works.dll", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> QualifiedClosure(string manifest)
    {
        if (new FileInfo(manifest).Length > 16 * 1024 * 1024) throw new InvalidOperationException("The qualified dependency manifest exceeds its bound.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var target = document.RootElement.GetProperty("targets").GetProperty(document.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!);
        string root = target.EnumerateObject().Single(node => node.Name.Split('/')[0] == "Hexalith.Works").Name;
        var pending = new Queue<string>(); pending.Enqueue(root);
        var visited = new HashSet<string>(StringComparer.Ordinal); var names = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out string? key))
        {
            if (!visited.Add(key)) continue;
            var node = target.GetProperty(key);
            if (node.TryGetProperty("runtime", out var assets))
                foreach (var asset in assets.EnumerateObject())
                    if (Owned(Path.GetFileName(asset.Name))) names.Add(Path.GetFileName(asset.Name));
            if (node.TryGetProperty("dependencies", out var dependencies))
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    string entry = dependency.Name + "/" + dependency.Value.GetString();
                    if (target.TryGetProperty(entry, out _)) pending.Enqueue(entry);
                    else if (Owned(dependency.Name + ".dll")) throw new InvalidOperationException("A qualified owned dependency has no runtime node.");
                }
        }
        return names;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc/>
    public ValueTask DisposeAsync() => catalog.DisposeAsync();
}
