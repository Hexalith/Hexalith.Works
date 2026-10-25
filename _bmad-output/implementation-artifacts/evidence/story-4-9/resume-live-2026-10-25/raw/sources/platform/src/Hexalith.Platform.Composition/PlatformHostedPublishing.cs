namespace Hexalith.Platform.Composition;

using System.Text.Json;
using Aspire.Hosting;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Hexalith.Builds.Tooling.Workspace;
using Microsoft.Extensions.Configuration;

/// <summary>Hosted selection and the native Aspire probe, independent of the Development profile.</summary>
public static class PlatformHostedPublishing
{
    /// <summary>Returns canonical chart inputs without binding local credentials, paths or providers.</summary>
    public static JsonElement Describe(PlatformApplicationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return JsonSerializer.SerializeToElement(new
        {
            schema = "hexalith.platform.application-graph.v1",
            workloads = graph.Workloads.Select(w => new
            {
                identity = w.Identity, resourceId = w.ResourceId, appId = w.AppId, readiness = w.ReadinessEndpoint,
                environment = graph.Dapr.Environment(PlatformDeclarationSet.Id(w.Module.Declaration), w.ServerId),
                inputs = w.Module.Declaration.GetProperty("runtime").GetProperty("extensions").EnumerateArray()
                    .SelectMany(e => e.GetProperty("configurationInputs").EnumerateArray()).Select(i => new
                    {
                        key = i.GetProperty("key").GetString(), source = i.GetProperty("source").GetString(), reference = i.GetProperty("reference").GetString(),
                    }).ToArray(),
                publicRoutes = w.Module.Declaration.GetProperty("surfaces").GetProperty("interfaces").EnumerateArray()
                    .Where(i => i.GetProperty("server").GetString() == w.ServerId && i.GetProperty("exposure").GetString() == "public")
                    .Select(i => i.GetProperty("routePrefix").GetString()).Distinct().ToArray(),
                // Namespace and trust are parameters in the chart. ACL operations come only from this helper.
                accessControl = graph.Dapr.AccessControl(w.AppId, "__APPLICATION_NAMESPACE__", "__TRUST_DOMAIN__"),
            }).ToArray(),
            capabilities = graph.Dapr.Capabilities, subscriptions = graph.Dapr.Subscriptions,
        });
    }

    /// <summary>Selects the same four-owner reference composition, without calling the local binder.</summary>
    public static PlatformCompositionOptions ReferenceOptions(string root) => new()
    {
        PlatformRoot = root,
        Mapping = new SourceMapping(WorkspaceMode.Source, root, "Hexalith.Platform", new[] { "EventStore", "Tenants", "Memories", "Parties" }
            .Select(name => new SourceMappingEntry("Hexalith." + name, "source", Path.Combine(root, "references", "Hexalith." + name))).ToArray(), false),
        ActiveModule = "parties",
        DeclarationPaths = [Path.Combine(root, "references/Hexalith.Parties/module/hexalith.module-manifest.v2.json")],
    };

    /// <summary>Probes the pinned native integration. No publish/deploy operation is triggered here.</summary>
    public static void ComposeNativeProbe(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ExecutionContext.IsRunMode) throw new InvalidOperationException("Hosted qualification is publish-only; select an explicit publish operation.");
        if (builder.Configuration.GetValue<bool>("Platform:Timesheets:Enabled") || builder.Configuration.GetValue<bool>("Platform:Works:Enabled") || builder.Configuration.GetValue<bool>("Platform:Agents:Enabled"))
            throw new InvalidDataException("Hosted reference publishing cannot combine local composition selections.");
        PlatformApplicationGraph graph = PlatformApplicationGraph.Resolve(ReferenceOptions(builder.AppHostDirectory));
        string graphPath = builder.Configuration["Platform:Hosted:GraphPath"] ?? throw new InvalidDataException("Platform:Hosted:GraphPath is required for the native probe.");
        File.WriteAllText(Path.GetFullPath(graphPath), Describe(graph).GetRawText() + Environment.NewLine);
        builder.AddKubernetesEnvironment("qualification").WithDashboard(false);
        foreach (PlatformWorkload workload in graph.Workloads)
        {
            // Deliberately use the native Dapr annotation rather than a second Kubernetes Dapr model.
            builder.AddContainer(workload.ResourceId, "qualification.invalid/" + workload.ResourceId + "@sha256:" + new string('a', 64))
                .WithHttpEndpoint(targetPort: 8080, name: "http")
                .WithDaprSidecar(new DaprSidecarOptions { AppId = workload.AppId, AppPort = 8080 });
        }
    }
}
