using Hexalith.EventStore.Aspire;
using Hexalith.Works.AppHost;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Resolve local-development Dapr component / access-control paths. builder.AppHostDirectory keeps this working
// under both `dotnet run` and Aspire.Hosting.Testing.
string eventStoreAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.yaml");
string worksAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.works.yaml");
string adminServerAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.eventstore-admin.yaml");
string resiliencyConfigPath = ResolveDaprConfigPath(
    builder.AppHostDirectory,
    Path.Combine("resiliency", "resiliency.yaml"));
string stateStoreComponentPath = ResolveDaprConfigPath(builder.AppHostDirectory, "statestore.yaml");

// Model the resiliency CRD as a local Dapr resource so every sidecar that must enforce the committed policy
// receives its directory on --resources-path explicitly, instead of picking the file up incidentally because
// it happened to sit beside statestore.yaml.
IResourceBuilder<IDaprComponentResource> resiliency = builder.AddDaprComponent(
    "resiliency",
    "resiliency",
    new DaprComponentOptions { LocalPath = resiliencyConfigPath });

// Local security service for JWT/OIDC authentication. The EventStore Aspire helper owns the Keycloak resource
// and exposes it under the shared "security" resource name. Set EnableKeycloak=false to keep the symmetric-key
// development fallback used by the AppHost topology smoke tests.
HexalithEventStoreSecurityResources? security = builder.AddHexalithEventStoreSecurity(
    new HexalithEventStoreSecurityOptions
    {
        RealmImportPath = ProjectMetadataPaths.GetProjectPath(
            "references",
            "Hexalith.EventStore",
            "src",
            "Hexalith.EventStore.AppHost",
            "KeycloakRealms"),
    });

// EventStore command gateway + Admin.Server (cross-repo project metadata; no UI, MCP, chatbot, email, routing,
// cost, or production security-hardening surface is composed for this command/event pipeline proof). The Works
// domain-service mapping routes "work" commands for any tenant at v1 to the "works" app's /process endpoint via
// the Kubernetes-safe sanitized wildcard registration key (wildcard_<domain>_<version>).
IResourceBuilder<ProjectResource> eventStore = builder.AddProject<HexalithEventStore>("eventstore")
    .WithHttpHealthCheck("/alive");
_ = eventStore
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__AppId", "works")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__MethodName", "process")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__TenantId", "*")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__Domain", "work")
    .WithEnvironment("EventStore__DomainServices__Registrations__wildcard_work_v1__Version", "v1")
    .WithEnvironment("EventStore__Publisher__TopicOverrides__work", "work.events")
    .WithEnvironment("Authentication__DaprInternal__AllowedCallers__0", "works");

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
// stateful component is added — the existing shared topology is reused. The EventStore command gateway endpoint is
// injected so a fired reminder / cascade target reissues its command through the same /api/v1/commands path
// Story 4.5 proved. No Works UI, MCP, chatbot, email, routing, cost, SignalR, or IExecutorRouter surface is
// composed for this recovery proof.
IResourceBuilder<ProjectResource> works = builder.AddProject<HexalithWorks>("works")
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/alive")
    .AddEventStoreDomainModule(eventStoreResources, "works", worksAccessControlConfigPath)
    .WithEnvironment("EventStore__CommandGateway__BaseAddress", eventStore.GetEndpoint("http"))
    .WaitFor(eventStoreResources.StateStore);

// The resiliency CRD carries policies for both ends of the pipeline: pubsubRetryInbound/subscriberTimeout for
// the Works subscriber (the bounded retry budget that ends in deadletter.work.events) and
// pubsubRetryOutbound/apps.eventstore/components.statestore for the publisher and admin reader. Reference it
// from every composed sidecar so no end silently falls back to Dapr defaults. The set is derived from the
// composed model rather than an enumerated list, so a sidecar added later cannot silently miss the policy.
foreach (IDaprSidecarResource sidecar in builder.Resources
    .OfType<ProjectResource>()
    .Select(SidecarOf)
    .OfType<IDaprSidecarResource>()
    .Distinct())
{
    _ = builder.CreateResourceBuilder(sidecar).WithReference(resiliency);
}

if (security is not null)
{
    _ = eventStore.WithJwtBearerSecurity(security);
    _ = adminServer.WithJwtBearerSecurity(security);
    _ = works
        .WithJwtBearerSecurity(security)
        .WithEventStoreClientCredentials(security);
}

// Story 4.8 removed the hand-configured Works:Recovery:Tenants forwarding: the date-reminder reconciliation
// pass now discovers tenants with pending date awaits from the durable pending-date-await registry the
// /project dispatcher maintains, so recovery runs on by default (Works:Recovery:RunReconciliationOnStartup)
// with no per-tenant configuration. The cascade pacing knob below is the only remaining recovery forward.
string? cascadeTargetInterval = builder.Configuration["Works:Recovery:CascadeTargetIntervalMilliseconds"];
if (!string.IsNullOrWhiteSpace(cascadeTargetInterval))
{
    if (!int.TryParse(cascadeTargetInterval, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int cascadeTargetIntervalMilliseconds)
        || cascadeTargetIntervalMilliseconds < 0)
    {
        throw new InvalidOperationException(
            $"Configuration value 'Works:Recovery:CascadeTargetIntervalMilliseconds' must be a non-negative integer; got '{cascadeTargetInterval}'.");
    }

    works = works.WithEnvironment(
        "Works__Recovery__CascadeTargetIntervalMilliseconds",
        cascadeTargetIntervalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

await builder
    .Build()
    .RunAsync()
    .ConfigureAwait(false);

// Resolve a composed project's Dapr sidecar from its own annotation rather than the toolkit's "<appId>-dapr"
// naming convention, so a sidecar rename cannot silently drop the resiliency reference. Projects composed
// without a sidecar yield null; a project carrying more than one is a composition error worth naming, because
// the silent alternative is a sidecar that enforces no policy.
static IDaprSidecarResource? SidecarOf(ProjectResource project)
{
    DaprSidecarAnnotation[] annotations = [.. project.Annotations.OfType<DaprSidecarAnnotation>()];
    return annotations.Length switch
    {
        0 => null,
        1 => annotations[0].Sidecar,
        _ => throw new InvalidOperationException(
            $"Project resource '{project.Name}' carries {annotations.Length} Dapr sidecar annotations; expected at most one."),
    };
}

// relativePath is resolved under the AppHost's DaprComponents directory and may name a subdirectory
// (e.g. "resiliency/resiliency.yaml") when a component needs an isolated --resources-path.
static string ResolveDaprConfigPath(string appHostDirectory, string relativePath)
{
    string configPath = Path.Combine(appHostDirectory, "DaprComponents", relativePath);
    if (File.Exists(configPath))
    {
        return configPath;
    }

    configPath = Path.Combine(Directory.GetCurrentDirectory(), "DaprComponents", relativePath);
    if (File.Exists(configPath))
    {
        return configPath;
    }

    throw new FileNotFoundException(
        $"Dapr configuration '{relativePath}' not found. Ensure it exists in the AppHost DaprComponents directory.",
        configPath);
}
