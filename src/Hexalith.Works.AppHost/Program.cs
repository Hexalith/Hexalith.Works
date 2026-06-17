using Hexalith.EventStore.Aspire;
using Hexalith.Works.AppHost;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Resolve local-development Dapr component / access-control paths. builder.AppHostDirectory keeps this working
// under both `dotnet run` and Aspire.Hosting.Testing.
string eventStoreAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.yaml");
string worksAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.works.yaml");
string adminServerAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.eventstore-admin.yaml");
string resiliencyConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "resiliency.yaml");
string stateStoreComponentPath = ResolveDaprConfigPath(builder.AppHostDirectory, "statestore.yaml");

// EventStore command gateway + Admin.Server (cross-repo project metadata; no UI, MCP, chatbot, email, routing,
// cost, or Keycloak realm work is composed for this command/event pipeline proof). The Works domain-service
// mapping routes "work" commands for any tenant at v1 to the "works" app's /process endpoint via the
// Kubernetes-safe sanitized wildcard registration key (wildcard_<domain>_<version>).
IResourceBuilder<ProjectResource> eventStore = builder.AddProject<HexalithEventStore>("eventstore");
_ = eventStore
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__AppId", "works")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__MethodName", "process")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__TenantId", "*")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__Domain", "work")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__Version", "v1");

IResourceBuilder<ProjectResource> adminServer = builder.AddProject<HexalithEventStoreAdminServerHost>("eventstore-admin");

// Shared Dapr topology (Redis-backed actor state store + pub/sub + sidecars + resiliency) via the EventStore
// Aspire helper. Redis is provided by `dapr init` at localhost; the helper owns the sidecar wiring.
HexalithEventStoreResources eventStoreResources = builder.AddHexalithEventStore(
    eventStore,
    adminServer,
    adminUI: null,
    eventStoreDaprConfigPath: eventStoreAccessControlConfigPath,
    adminServerDaprConfigPath: adminServerAccessControlConfigPath,
    resiliencyConfigPath: resiliencyConfigPath,
    stateStoreComponentPath: stateStoreComponentPath);

// The runnable Works domain service. Its Dapr sidecar shares the EventStore state store + pub/sub; it waits for
// EventStore and the shared state store before serving /process, /query, and /project.
//
// Story 4.6 recovery proof: the Works host now also hosts the date-resume reminder actor and the terminal-
// cascade checkpoint store. Dapr actor reminders are persisted by the Dapr Scheduler and their state lives in
// the shared actor-capable state store (statestore.yaml, actorStateStore: "true", scoped to works), so no new
// component is added — only the existing shared topology is reused. The EventStore command gateway endpoint is
// injected so a fired reminder / cascade target reissues its command through the same /api/v1/commands path
// Story 4.5 proved. No Works UI, MCP, chatbot, email, routing, cost, SignalR, or IExecutorRouter surface is
// composed for this recovery proof.
IResourceBuilder<ProjectResource> works = builder.AddProject<HexalithWorks>("works")
    .AddEventStoreDomainModule(eventStoreResources, "works", worksAccessControlConfigPath)
    .WithReference(eventStore)
    .WithEnvironment("EventStore__CommandGateway__BaseAddress", eventStore.GetEndpoint("http"))
    .WaitFor(eventStore)
    .WaitFor(eventStoreResources.StateStore);

// Story 4.6 recovery scope: the date-reminder reconciliation pass is bounded to a known tenant set because
// the EventStore stream-read gateway exposes no tenant-wide enumeration (per-aggregate route only). The scope
// stays empty by default — reconciliation is disabled — so the standard pipeline/topology proofs are
// unchanged. The gated Aspire reminder-recovery lane opts in with --Works:Recovery:Tenants=<comma-separated>,
// which forwards the bounded scope to the Works host so its startup reconciler actually runs.
string? recoveryTenants = builder.Configuration["Works:Recovery:Tenants"];
if (!string.IsNullOrWhiteSpace(recoveryTenants))
{
    string[] tenants = recoveryTenants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (int index = 0; index < tenants.Length; index++)
    {
        works = works.WithEnvironment($"Works__Recovery__Tenants__{index}", tenants[index]);
    }
}

await builder
    .Build()
    .RunAsync()
    .ConfigureAwait(false);

static string ResolveDaprConfigPath(string appHostDirectory, string fileName)
{
    string configPath = Path.Combine(appHostDirectory, "DaprComponents", fileName);
    if (File.Exists(configPath))
    {
        return configPath;
    }

    configPath = Path.Combine(Directory.GetCurrentDirectory(), "DaprComponents", fileName);
    if (File.Exists(configPath))
    {
        return configPath;
    }

    throw new FileNotFoundException(
        $"Dapr configuration '{fileName}' not found. Ensure it exists in the AppHost DaprComponents directory.",
        configPath);
}
