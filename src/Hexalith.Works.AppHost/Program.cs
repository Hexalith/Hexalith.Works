using Hexalith.EventStore.Aspire;
using Hexalith.Works.AppHost;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

string? configuredPlacementHostAddress =
    builder.Configuration[AspireDaprLocalServiceEndpoints.PlacementHostAddressKey];
string? configuredSchedulerHostAddress =
    builder.Configuration[AspireDaprLocalServiceEndpoints.SchedulerHostAddressKey];
bool hasConfiguredPlacement = !string.IsNullOrWhiteSpace(configuredPlacementHostAddress);
bool hasConfiguredScheduler = !string.IsNullOrWhiteSpace(configuredSchedulerHostAddress);
if (hasConfiguredPlacement != hasConfiguredScheduler)
{
    throw new InvalidOperationException(
        $"Configure both '{AspireDaprLocalServiceEndpoints.PlacementHostAddressKey}' and "
        + $"'{AspireDaprLocalServiceEndpoints.SchedulerHostAddressKey}', or configure neither so the AppHost "
        + "can compose its mTLS-enabled local control plane.");
}

// Resolve local-development Dapr component / access-control paths. builder.AppHostDirectory keeps this working
// under both `dotnet run` and Aspire.Hosting.Testing.
string eventStoreAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.yaml");
string worksAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.works.yaml");
string adminServerAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.eventstore-admin.yaml");
string operationsAccessControlConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "accesscontrol.eventstore-operations.yaml");
string resiliencyConfigPath = ResolveDaprConfigPath(
    builder.AppHostDirectory,
    Path.Combine("resiliency", "resiliency.yaml"));
string stateStoreComponentPath = ResolveDaprConfigPath(builder.AppHostDirectory, "statestore.yaml");
string pubSubComponentPath = ResolveDaprConfigPath(builder.AppHostDirectory, "pubsub.yaml");
string sentryConfigPath = ResolveDaprConfigPath(builder.AppHostDirectory, "sentry.yaml");

// Self-hosted Dapr does not compose Sentry automatically. Keep its generated issuer material in the external
// user Dapr directory, then make every sidecar wait for the healthy Sentry resource before reading that bundle.
(IResourceBuilder<ContainerResource> sentry, string sentryCertificateDirectory) =
    DaprSelfHostedMtls.AddSentry(builder, sentryConfigPath);

// A sidecar with mTLS enabled also requires TLS-enabled actor control-plane services. The containers created by
// `dapr init` are plaintext, so use an AppHost-owned placement/scheduler pair by default and persist Scheduler's
// reminder database in its own named volume. Explicit endpoints remain available for an externally managed,
// mTLS-compatible pair and are required as a complete placement/scheduler tuple.
IResourceBuilder<ContainerResource>? daprPlacement = null;
IResourceBuilder<ContainerResource>? daprScheduler = null;
string? daprPlacementHostAddress;
string? daprSchedulerHostAddress;
if (hasConfiguredPlacement)
{
    (daprPlacementHostAddress, daprSchedulerHostAddress) = AspireDaprLocalServiceEndpoints.Resolve(
        configuredPlacementHostAddress,
        configuredSchedulerHostAddress);
}
else
{
    (daprPlacement, daprScheduler) = DaprSelfHostedMtls.AddControlPlane(
        builder,
        sentry,
        sentryCertificateDirectory);
    daprPlacementHostAddress = DaprSelfHostedMtls.PlacementHostAddress;
    daprSchedulerHostAddress = DaprSelfHostedMtls.SchedulerHostAddress;
}

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

    // Command dead letters must not collide with the subscriber DLQ. EventPublisherOptions derives the command
    // dead-letter topic as "{prefix}.{GetPubSubTopic(identity)}", and the override above resolves the work
    // domain's topic to work.events -- so the default "deadletter" prefix produces the literal string
    // deadletter.work.events, which is the topic the operations workload drains. A distinct prefix keeps the two
    // queues separate, as the operator runbook states, and matches the eventstore publish grant in pubsub.yaml.
    .WithEnvironment("EventStore__Publisher__DeadLetterTopicPrefix", "commanddeadletter")
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
    stateStoreComponentPath: stateStoreComponentPath,
    pubSubComponentPath: pubSubComponentPath,
    daprPlacementHostAddress: daprPlacementHostAddress,
    daprSchedulerHostAddress: daprSchedulerHostAddress);

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
    .AddEventStoreDomainModule(
        eventStoreResources,
        "works",
        worksAccessControlConfigPath,
        daprPlacementHostAddress: daprPlacementHostAddress,
        daprSchedulerHostAddress: daprSchedulerHostAddress)
    .WithEnvironment("EventStore__CommandGateway__BaseAddress", eventStore.GetEndpoint("http"))
    .WaitFor(eventStoreResources.StateStore);

// Reusable EventStore-owned operations workload. Its actor is the durable serialization point for the Works
// subscriber DLQ. It has state access, subscribes only to deadletter.work.events, and has no publish grant.
// Replay reaches Works only through the narrow /work/events service-invocation policy.
IResourceBuilder<ProjectResource> operations = builder.AddProject<HexalithEventStoreOperations>("eventstore-operations")
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/alive")
    // Project resources do not implicitly inherit the AppHost environment under Aspire.Hosting.Testing. Keep
    // the operations host on the same environment so its Development-only token fallback matches the rest of
    // this composed topology; outside Development its existing fail-closed token validation is preserved.
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("EventStoreOperations__PubSubName", "pubsub")
    .WithEnvironment("EventStoreOperations__TopicName", "deadletter.work.events")
    .WithEnvironment("EventStoreOperations__CaptureRoute", "/dead-letters/work/events")
    .WithEnvironment("EventStoreOperations__AdminCallerAppId", "eventstore-admin")
    .WithEnvironment("EventStoreOperations__ReplayAppId", "works")
    .WithEnvironment("EventStoreOperations__ReplayMethodName", "work/events")
    .WithReference(works)
    .WaitFor(works)
    .WaitFor(eventStoreResources.StateStore)
    .WithDaprSidecar(sidecar => sidecar
        .WithOptions(new DaprSidecarOptions
        {
            AppId = "eventstore-operations",
            Config = operationsAccessControlConfigPath,
            EnableAppHealthCheck = true,
            AppHealthCheckPath = "/alive",
            PlacementHostAddress = daprPlacementHostAddress,
            SchedulerHostAddress = daprSchedulerHostAddress,
        })
        .WithReference(eventStoreResources.StateStore)
        .WithReference(eventStoreResources.PubSub));

_ = adminServer
    .WithEnvironment("AdminServer__OperationsAppId", "eventstore-operations")
    .WithReference(operations)
    .WaitFor(operations);

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

// Dapr's service-invocation ACL obtains app id, namespace, and trust domain from the caller's Sentry-issued
// SPIFFE certificate. Configure every composed sidecar, including non-invoking receivers, so all Dapr traffic
// remains mutually authenticated and a future invocation cannot silently fall back to an identity-less caller.
foreach (ProjectResource project in builder.Resources
    .OfType<ProjectResource>()
    .Where(static project => SidecarOf(project) is not null))
{
    DaprSelfHostedMtls.ConfigureSidecar(
        builder.CreateResourceBuilder(project),
        SidecarOf(project)!,
        sentry,
        sentryCertificateDirectory,
        daprPlacement,
        daprScheduler);
}

if (security is not null)
{
    _ = eventStore.WithJwtBearerSecurity(security);
    _ = adminServer.WithJwtBearerSecurity(security);
    // The EventStore Aspire security helper now requires an explicit OIDC client id and per-run user-name /
    // password parameters for client-credential token acquisition. Values come from the same
    // LocalAuthentication:* configuration keys the EventStore AppHost reads, falling back to a per-run random
    // password so no credential is ever checked in. Only the Keycloak-enabled path reaches this branch; the
    // live smoke lanes run with --EnableKeycloak=false and leave `security` null.
    IResourceBuilder<ParameterResource> worksClientUsername = builder.AddParameter(
        "works-client-username",
        () => builder.Configuration["LocalAuthentication:TenantAUsername"] is { Length: > 0 } configuredUsername
            ? configuredUsername
            : "tenant-a-user");
    IResourceBuilder<ParameterResource> worksClientPassword = builder.AddParameter(
        "works-client-password",
        () => builder.Configuration["LocalAuthentication:TenantAPassword"] is { Length: > 0 } configuredPassword
            ? configuredPassword
            : Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)),
        secret: true);

    _ = works
        .WithJwtBearerSecurity(security)
        .WithEventStoreClientCredentials(
            security,
            HexalithEventStoreSecurityOptions.DefaultEventStoreClientId,
            worksClientUsername,
            worksClientPassword);
}
else
{
    // Development symmetric-key validation for the `--EnableKeycloak=false` topology the Tier-3 live lanes use.
    // The EventStore host used to carry these values in its own appsettings.Development.json; the submodule bump
    // at superproject HEAD 52a56c6 (EventStore 910fda6a → 8745b14b) removed that block, and the host's
    // ValidateEventStoreAuthenticationOptions then fails at startup with "requires either 'Authority' … or
    // 'SigningKey'", taking every live lane down before any Works code runs. Composing them here mirrors the
    // EventStore AppHost's own ConfigureLocalSymmetricValidation and matches the dev token the smoke lanes mint
    // (issuer hexalith-dev, audience hexalith-eventstore). The key is a development-only literal, overridable
    // through Works:Authentication:DevSigningKey; the host still refuses symmetric keys outside Development.
    string devSigningKey = builder.Configuration["Works:Authentication:DevSigningKey"] is { Length: > 0 } configuredKey
        ? configuredKey
        : "DevOnlySigningKey-AtLeast32Chars!";
    foreach (IResourceBuilder<ProjectResource> jwtValidator in new[] { eventStore, adminServer })
    {
        _ = jwtValidator
            // Project resources do not implicitly inherit the AppHost environment under
            // Aspire.Hosting.Testing, and the symmetric-key path is Development-only by contract.
            .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
            .WithEnvironment("Authentication__JwtBearer__Authority", string.Empty)
            .WithEnvironment("Authentication__JwtBearer__Issuer", "hexalith-dev")
            .WithEnvironment("Authentication__JwtBearer__Audience", HexalithEventStoreSecurityOptions.DefaultAudience)
            .WithEnvironment("Authentication__JwtBearer__ValidAudiences__0", HexalithEventStoreSecurityOptions.DefaultAudience)
            .WithEnvironment("Authentication__JwtBearer__SigningKey", devSigningKey)
            .WithEnvironment("Authentication__JwtBearer__RequireHttpsMetadata", "false");
    }
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
