using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// The shared Tier-3 harness for the live Works AppHost lanes: prerequisite gating, AppHost start/dispose,
/// dev-JWT command submission, and per-aggregate stream observation. Every fact that needs the full topology
/// drives it through here so the lanes cannot drift apart.
/// </summary>
internal static class WorksAppHostSmokeHarness
{
    internal const string DevSigningKey = "DevOnlySigningKey-AtLeast32Chars!";

    /// <summary>The Redis instance a <c>dapr init</c> leaves running.</summary>
    private const int DaprInitRedisPort = 6379;

    /// <summary>How long a control-plane resource may still be held by a previous fact's teardown before it counts as foreign.</summary>
    private static readonly TimeSpan ControlPlaneReleaseWait = TimeSpan.FromSeconds(60);

    /// <summary>How long one non-cancelable asynchronous disposer may delay teardown diagnostics.</summary>
    private static readonly TimeSpan DisposalWait = TimeSpan.FromSeconds(30);

    /// <summary>How long one Docker ownership probe may run before it is terminated and classified.</summary>
    private static readonly TimeSpan DockerProbeWait = TimeSpan.FromSeconds(10);

    /// <summary>How long a terminated Docker probe may take to exit before teardown continues.</summary>
    private static readonly TimeSpan DockerProbeTerminationWait = TimeSpan.FromSeconds(5);

    /// <summary>The persistent Scheduler volume that must have exactly one owner at a time, or null for Platform.</summary>
    private static string? SchedulerVolumeName => WorksAppHostSelection.SchedulerVolumeName;

    /// <summary>The fixed local ports owned by the mTLS Sentry, placement, and scheduler resources.</summary>
    private static (int Port, string Name)[] ControlPlanePorts =>
    [
        (WorksAppHostSelection.ControlPlanePorts.Sentry, "dapr-sentry"),
        (WorksAppHostSelection.ControlPlanePorts.Placement, "dapr-placement-mtls"),
        (WorksAppHostSelection.ControlPlanePorts.Scheduler, "dapr-scheduler-mtls"),
    ];

    /// <summary>Both loopback address families that must be free before a fixed-port topology starts.</summary>
    private static readonly IPAddress[] ControlPlaneLoopbacks = [IPAddress.Loopback, IPAddress.IPv6Loopback];

    private static object[] RuntimeModules(IDistributedApplicationTestingBuilder builder)
    {
        string configuration = builder.AppHostAssembly?.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? throw new InvalidOperationException("The actual AppHost build configuration is missing.");
        return [.. builder.Resources.OfType<ProjectResource>().Where(resource => resource.Name is "eventstore" or "works" or "works-ha" or "eventstore-operations" or "eventstore-admin")
            .Select(resource =>
            {
                string project = resource.GetProjectMetadata().ProjectPath;
                string directory = WorksPlatformRuntimeArtifacts.DirectoryFor(project, configuration);
                string entry = Path.Combine(directory, Path.GetFileNameWithoutExtension(project) + ".dll");
                return (object)new
                {
                    resource = resource.Name, project, configuration, entry,
                    entrySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entry))),
                    sdkAssemblies = Directory.GetFiles(directory, "Hexalith.EventStore.*.dll").Order(StringComparer.Ordinal).Select(path => new
                    {
                        name = Path.GetFileNameWithoutExtension(path), path,
                        version = AssemblyName.GetAssemblyName(path).Version!.ToString(),
                        informationalVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion,
                        sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                    }).ToArray(),
                };
            })];
    }

    private static void IsolateApplicationPorts(IDistributedApplicationTestingBuilder builder, string discovery)
    {
        (int sentryPort, int placementPort, int schedulerPort) = WorksAppHostSelection.ControlPlanePorts;
        if (WorksAppHostSelection.IsPlatform)
        {
            foreach ((string name, int port) in new[] { ("dapr-sentry", sentryPort), ("dapr-placement-mtls", placementPort), ("dapr-scheduler-mtls", schedulerPort) })
                builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == name)
                    .Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == "grpc").Port = port;
            ContainerResource scheduler = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "dapr-scheduler-mtls");
            builder.CreateResourceBuilder(scheduler).WithArgs(context =>
            {
                for (int index = 0; index < context.Args.Count; index++)
                    if (context.Args[index] is string value && value.StartsWith("--override-broadcast-host-port=localhost:", StringComparison.Ordinal))
                        context.Args[index] = "--override-broadcast-host-port=localhost:" + schedulerPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
        }
        // Control-plane endpoints remain fixed and ownership-checked. Application and sidecar
        // proxies are disposable: never route synthetic proof traffic to another local AppHost.
        // Name resolution is isolated too: default mDNS discovery would round-robin this start's
        // app IDs with any unrelated local sidecar that shares them.
        string registry = Path.Combine(discovery, WorksAppHostDiscoveryIsolation.RegistryFileName);
        var isolatedConfigurations = new Dictionary<string, string>(StringComparer.Ordinal);
        string Isolated(string? configuration)
        {
            if (string.IsNullOrWhiteSpace(configuration))
                throw new InvalidOperationException("Every live sidecar needs a Dapr configuration to isolate its name resolution.");
            if (!isolatedConfigurations.TryGetValue(configuration, out string? isolated))
            {
                isolated = Path.Combine(discovery, isolatedConfigurations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "-" + Path.GetFileName(configuration));
                File.WriteAllText(isolated, WorksAppHostDiscoveryIsolation.Render(File.ReadAllText(configuration), registry,
                    WorksAppHostSelection.IsPlatform ? sentryPort : null));
                isolatedConfigurations[configuration] = isolated;
            }

            return isolated;
        }

        foreach (ProjectResource project in builder.Resources.OfType<ProjectResource>())
        {
            foreach (EndpointAnnotation endpoint in project.Annotations.OfType<EndpointAnnotation>())
                endpoint.Port = null;
            DaprSidecarAnnotation? dapr = project.Annotations.OfType<DaprSidecarAnnotation>().SingleOrDefault();
            if (dapr is null) continue;
            DaprSidecarOptionsAnnotation annotation = dapr.Sidecar.Annotations.OfType<DaprSidecarOptionsAnnotation>().Single();
            dapr.Sidecar.Annotations.Remove(annotation);
            dapr.Sidecar.Annotations.Add(new DaprSidecarOptionsAnnotation(annotation.Options with
            {
                AppPort = null, DaprHttpPort = null, DaprGrpcPort = null,
                DaprInternalGrpcPort = null, MetricsPort = null,
                Config = Isolated(annotation.Options.Config),
                RuntimePath = WorksAppHostSelection.IsPlatform
                    ? Environment.GetEnvironmentVariable("WORKS_PLATFORM_DAPRD_RUNTIME_PATH") ?? annotation.Options.RuntimePath : annotation.Options.RuntimePath,
                PlacementHostAddress = WorksAppHostSelection.IsPlatform ? "localhost:" + placementPort.ToString(System.Globalization.CultureInfo.InvariantCulture) : annotation.Options.PlacementHostAddress,
                SchedulerHostAddress = WorksAppHostSelection.IsPlatform ? "localhost:" + schedulerPort.ToString(System.Globalization.CultureInfo.InvariantCulture) : annotation.Options.SchedulerHostAddress,
            }));
            string cliName = dapr.Sidecar.Name + "-cli";
            builder.CreateResourceBuilder(project).WithEnvironment(context =>
            {
                ExecutableResource cli = builder.Resources.OfType<ExecutableResource>().Single(resource => resource.Name == cliName);
                var http = new EndpointReference(cli, "http");
                var grpc = new EndpointReference(cli, "grpc");
                context.EnvironmentVariables["DAPR_HTTP_ENDPOINT"] = http;
                context.EnvironmentVariables["DAPR_GRPC_ENDPOINT"] = grpc;
                context.EnvironmentVariables["DAPR_HTTP_PORT"] = http.Property(EndpointProperty.Port);
                context.EnvironmentVariables["DAPR_GRPC_PORT"] = grpc.Property(EndpointProperty.Port);
                if (project.Name == "eventstore-admin")
                {
                    ExecutableResource gateway = builder.Resources.OfType<ExecutableResource>().Single(resource => resource.Name == "eventstore-dapr-cli");
                    context.EnvironmentVariables["AdminServer__EventStoreDaprHttpEndpoint"] = new EndpointReference(gateway, "http");
                }
            });
        }
    }

    private static Dictionary<string, string> AssignHostingEvidence(IDistributedApplicationTestingBuilder builder, string directory)
    {
        // Each proof host records, from inside its own process, the hosting startups that actually loaded.
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ProjectResource project in builder.Resources.OfType<ProjectResource>()
            .Where(resource => resource.Name is "eventstore" or "works" or "works-ha"))
        {
            string path = Path.Combine(directory, "hosting-" + project.Name + ".json");
            assigned[project.Name] = path;
            builder.CreateResourceBuilder(project).WithEnvironment(WorksSdkProofHostingEvidence.PathVariable, path);
        }

        return assigned;
    }

    private static async Task<Dictionary<string, JsonElement?>> ReadHostingEvidenceAsync(
        IReadOnlyDictionary<string, string> assigned, CancellationToken cancellationToken)
    {
        // Every host has passed its startup probes, so its hosting startups ran long ago. A short bound only
        // covers a slow file flush; a missing record stays null and refuses verification. Each resource has its
        // own bound, so one missing record cannot consume another resource's wait and misattribute the absence.
        var records = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
        foreach ((string resource, string path) in assigned.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            JsonElement? record = null;
            try
            {
                while (record is null)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            record = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(path, deadline.Token).ConfigureAwait(false));
                            break;
                        }
                        catch (JsonException)
                        {
                            // The host may still be writing its record.
                        }
                    }

                    await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Leave the record absent so the verifier refuses the start.
            }

            records[resource] = record;
        }

        return records;
    }

    /// <summary>Reads each runtime module's effective tracked environment from its launched resource snapshot.</summary>
    private static Dictionary<string, Dictionary<string, string?>> EffectiveEnvironment(DistributedApplication app)
    {
        var effective = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        foreach (string resource in new[] { "eventstore", "works", "works-ha", "eventstore-operations", "eventstore-admin" })
        {
            if (!app.ResourceNotifications.TryGetCurrentState(resource, out ResourceEvent? current)) continue;
            effective[resource] = current.Snapshot.EnvironmentVariables
                .Where(variable => WorksPlatformTrackedEnvironment.IsTracked(variable.Name))
                .GroupBy(variable => variable.Name, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (group.FirstOrDefault(variable => variable.IsFromSpec) ?? group.First()).Value, StringComparer.Ordinal);
        }

        return effective;
    }

    /// <summary>
    /// Returns why this lane cannot run, or <see langword="null"/> when every prerequisite is satisfied.
    /// </summary>
    public static async Task<string?> PrerequisiteGapAsync(CancellationToken cancellationToken)
    {
        if (!WorksAppHostSelection.IsPlatform) return "Select the actual Platform host through Platform eng/verify-works-host.sh for live hosting proofs.";
        if (!await IsPortReachableAsync(DaprInitRedisPort, cancellationToken).ConfigureAwait(false))
        {
            string gap = $"the dapr-init Redis on :{DaprInitRedisPort} is not reachable. Start Docker and run `dapr init`";
            if (WorksAppHostSelection.IsPlatform) throw new InvalidOperationException("Platform parity cannot skip: " + gap);
            return gap;
        }

        try
        {
            await WaitForControlPlaneResourcesReleasedAsync("prerequisite", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
        {
            if (WorksAppHostSelection.IsPlatform) throw new InvalidOperationException("Platform parity cannot skip: " + exception.Message, exception);
            return exception.Message;
        }

        return null;
    }

    private static async Task<string> StartupLogsAsync(DistributedApplication app)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var output = new StringBuilder();
        try
        {
            ResourceLoggerService logs = app.Services.GetRequiredService<ResourceLoggerService>();
            foreach (string resource in new[] { "dapr-sentry", "dapr-placement-mtls", "dapr-scheduler-mtls", "eventstore", "works", "works-ha", "eventstore-operations", "eventstore-admin" })
            {
                var lines = new List<LogLine>();
                using var resourceBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                resourceBudget.CancelAfter(TimeSpan.FromMilliseconds(500));
                try
                {
                    string resourceId = app.ResourceNotifications.TryGetCurrentState(resource, out ResourceEvent? current)
                        ? current.ResourceId : resource;
                    await foreach (IReadOnlyList<LogLine> batch in logs.GetAllAsync(resourceId)
                        .WithCancellation(resourceBudget.Token).ConfigureAwait(false)) lines.AddRange(batch);
                }
                catch (OperationCanceledException) when (resourceBudget.IsCancellationRequested)
                {
                    // Capture the backlog already observed; a live stream cannot consume another resource's slice.
                }
                catch (Exception exception)
                {
                    _ = output.AppendLine().Append(resource).Append(" log read failed: ").AppendLine(exception.GetType().Name);
                }
                _ = output.AppendLine().Append(resource).AppendLine(" startup output:");
                foreach (LogLine line in lines.TakeLast(40))
                    _ = output.AppendLine(line.Content.Length > 4000 ? line.Content[..4000] : line.Content);
            }
        }
        catch (Exception exception)
        {
            _ = output.AppendLine().Append("Startup output unavailable: ").Append(exception.GetType().Name);
        }
        return output.ToString();
    }

    /// <summary>Refuses the shared default providers before any live resource can start.</summary>
    internal static void RequireOwnedPlatformFixture(bool platform, bool configured, string? schedulerVolume, string? certificates)
    {
        if (platform && (!configured || schedulerVolume is null || !schedulerVolume.StartsWith("works-sdk416-", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(certificates) || !Path.IsPathFullyQualified(certificates)))
            throw new InvalidOperationException("Platform live proofs require explicit owned SDK configuration, Scheduler volume and certificates.");
    }

    /// <summary>Starts the owned SDK topology, verifies readiness, runs the proof and disposes it for a genuine restart.</summary>
    public static async Task WithAppHostAsync(
        CancellationToken cancellationToken,
        Func<DistributedApplication, HttpClient, HttpClient, CancellationToken, Task> body,
        Action<IDistributedApplicationTestingBuilder>? configure = null,
        string actorTypeName = "WorksSdkReminderActor",
        bool expectWorksReadinessDegraded = false,
        string? schedulerVolumeName = null,
        string? certificateDirectory = null,
        bool useRetainedHost = false)
    {
        ArgumentNullException.ThrowIfNull(body);
        RequireOwnedPlatformFixture(WorksAppHostSelection.IsPlatform, configure is not null, schedulerVolumeName, certificateDirectory);

        // A restart fact calls this method twice after only one prerequisite probe. Recheck immediately before
        // every start so teardown lag from the first run cannot race the second scheduler/placement bind. Once
        // the explicit prerequisite probe has passed, a later occupied resource is a restart-boundary failure: it
        // must never turn the acceptance fact into a successful skip.
        await WaitForControlPlaneResourcesReleasedAsync("startup boundary", cancellationToken, schedulerVolumeName).ConfigureAwait(false);

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // A clean checkout now builds the runtime-only EventStore Operations project during the first live start.
        // Keep that cold-build path inside the readiness gate without borrowing from the acceptance-body budget.
        string? configuredStartupSeconds = Environment.GetEnvironmentVariable("WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS");
        int startupSeconds = configuredStartupSeconds is null ? 600
            : int.TryParse(configuredStartupSeconds, out int seconds) && seconds is > 0 and <= 600 ? seconds
            : throw new InvalidOperationException("WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS must be from 1 through 600.");
        startupCts.CancelAfter(TimeSpan.FromSeconds(startupSeconds));

        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync(useRetainedHost ? WorksAppHostSelection.RetainedEntryPoint() : WorksAppHostSelection.EntryPoint(),
                [
                    "--EnableKeycloak=false",
                    "--environment=Development",
                    $"--Platform:Works:Enabled={(WorksAppHostSelection.IsPlatform && !useRetainedHost).ToString().ToLowerInvariant()}",
                    .. (certificateDirectory is null ? Array.Empty<string>() : new[] { $"--Dapr:Mtls:CertificateDirectory={certificateDirectory}" }),
                    $"--Works:Authentication:DevSigningKey={DevSigningKey}",
                    // These acceptance facts share fixed localhost control-plane addresses and are serialized by
                    // WorksAppHostTestCollection. Preserve those ports instead of the testing builder's default
                    // randomization so daprd reaches the AppHost-owned placement and scheduler proxies.
                    "--DcpPublisher:RandomizePorts=false",
                ],
                startupCts.Token)
            .ConfigureAwait(true);

        // Explicit legacy diagnostic fixture: bound only advisory startup metadata, preserving all
        // production authentication and legacy reminder/process registrations.
        if (configure is null && Environment.GetEnvironmentVariable("WORKS_APPHOST_BOUND_METADATA_STARTUP") == "1")
        {
            string assembly = typeof(WorksSdkProofHostingStartup).Assembly.Location;
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "eventstore"))
                .WithEnvironment("DOTNET_STARTUP_HOOKS", assembly)
                .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", typeof(WorksSdkProofHostingStartup).Assembly.GetName().Name!)
                .WithEnvironment("WORKS_SDK_PROOF", "1").WithEnvironment("WORKS_SDK_PROOF_ROLE", "metadata-only")
                .WithEnvironment("Version", WorksSdkProofProducer.Version);
        }
        // On an actual Platform start, capture Platform's own composition before the fixture configures the builder,
        // and the fixture's additions immediately after, before the harness adds its isolation and evidence wiring.
        Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]>? platformComposition = WorksAppHostSelection.IsPlatform && !useRetainedHost
            ? WorksPlatformCompositionGuard.Capture(builder) : null;
        configure?.Invoke(builder);
        Dictionary<ProjectResource, EnvironmentCallbackAnnotation[]>? fixtureComposition = platformComposition is null
            ? null : WorksPlatformCompositionGuard.Capture(builder);
        WorksAppHostTestReadiness.ConfigureHarnessLogging(builder);
        DistributedApplication? app = null;
        Exception? primaryException = null;
        string? discoveryDirectory = null;
        try
        {
            // Refuses, before any resource is built, a fixture that set a key Platform composed for that resource.
            Dictionary<string, object>? compositionGuard = platformComposition is null ? null
                : await WorksPlatformCompositionGuard.VerifyAsync(builder, platformComposition, fixtureComposition!, startupCts.Token).ConfigureAwait(true);
            if (schedulerVolumeName is not null)
            {
                if (!schedulerVolumeName.StartsWith("works-sdk416-", StringComparison.Ordinal))
                    throw new InvalidOperationException("A proof Scheduler must use an owned synthetic volume.");
                ContainerResource scheduler = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "dapr-scheduler-mtls");
                ContainerMountAnnotation mount = scheduler.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == "/var/lock");
                scheduler.Annotations.Remove(mount);
                builder.CreateResourceBuilder(scheduler).WithVolume(schedulerVolumeName, "/var/lock");
            }

            // Created only inside this try, so the cleanup below always removes it.
            discoveryDirectory = Directory.CreateTempSubdirectory(WorksAppHostDiscoveryIsolation.DirectoryPrefix).FullName;
            IsolateApplicationPorts(builder, discoveryDirectory);
            Dictionary<string, string> hostingEvidence = WorksAppHostSelection.IsPlatform
                ? AssignHostingEvidence(builder, discoveryDirectory) : [];
            if (WorksAppHostSelection.IsPlatform)
            {
                _ = WorksPlatformRuntimeArtifacts.RequireMatchingConfigurations(
                    typeof(WorksAppHostSmokeHarness).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                    builder.AppHostAssembly?.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                    WorksAppHostSelection.RetainedEntryPoint().Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration);
            }
            app = await builder.BuildAsync(startupCts.Token).ConfigureAwait(true);
            try
            {
                await app.StartAsync(startupCts.Token).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "[startup/readiness] The Aspire AppHost could not start the live Works topology. "
                    + WorksAppHostTestReadiness.DescribeResourceStates(
                        app,
                        [
                            "dapr-sentry",
                            "dapr-placement-mtls",
                            "dapr-scheduler-mtls",
                            "eventstore",
                            "works",
                            "eventstore-operations",
                            "eventstore-admin",
                        ]),
                    ex);
            }

            _ = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "dapr-sentry", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
            _ = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "dapr-placement-mtls", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
            _ = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "dapr-scheduler-mtls", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
            _ = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "eventstore", startupCts.Token, cancellationToken)
                .ConfigureAwait(true);
            ResourceEvent worksResource;
            if (expectWorksReadinessDegraded)
            {
                // A fault fixture deliberately disables periodic writers and must assert Degraded itself.
                // Preserve the target readiness annotation; its status is never replaced with liveness.
                await app.ResourceNotifications.WaitForResourceAsync("works", KnownResourceStates.Running, startupCts.Token).ConfigureAwait(true);
                app.ResourceNotifications.TryGetCurrentState("works", out ResourceEvent? running).ShouldBeTrue();
                worksResource = running.ShouldNotBeNull();
            }
            else
            {
                worksResource = await WorksAppHostTestReadiness
                    .WaitForResourceHealthyAsync(app, "works", startupCts.Token, cancellationToken)
                    .ConfigureAwait(true);
            }

            using HttpClient client = app.CreateHttpClient("eventstore");
            client.Timeout = TimeSpan.FromSeconds(60);
            await WorksAppHostTestReadiness
                .WaitForEventStoreCommandRuntimeAsync(client, startupCts.Token)
                .ConfigureAwait(true);
            using HttpClient sidecarClient = WorksAppHostTestReadiness.CreateWorksDaprClient(worksResource);
            await WorksAppHostTestReadiness
                .WaitForWorksActorRuntimeAsync(sidecarClient, startupCts.Token,
                    actorTypeName)
                .ConfigureAwait(true);
            if (WorksAppHostSelection.IsPlatform && Environment.GetEnvironmentVariable("WORKS_PLATFORM_HOST_EVIDENCE") is { Length: > 0 } hostEvidence)
            {
                string hostAssembly = builder.AppHostAssembly?.Location
                    ?? throw new InvalidOperationException("Platform proof did not capture the actual AppHost assembly.");
                await File.AppendAllTextAsync(hostEvidence, JsonSerializer.Serialize(new
                {
                    assembly = hostAssembly,
                    sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(hostAssembly))),
                    directory = builder.AppHostDirectory,
                    role = useRetainedHost ? "retained-rollback" : "platform-target",
                    schedulerVolume = schedulerVolumeName ?? WorksAppHostSelection.SchedulerVolumeName,
                    controlPlanePorts = new { sentry = WorksAppHostSelection.ControlPlanePorts.Sentry,
                        placement = WorksAppHostSelection.ControlPlanePorts.Placement, scheduler = WorksAppHostSelection.ControlPlanePorts.Scheduler },
                    dependencyBuildsSuppressed = Environment.GetEnvironmentVariable("BuildProjectReferences") == "false",
                    daprdRuntimePath = Environment.GetEnvironmentVariable("WORKS_PLATFORM_DAPRD_RUNTIME_PATH"),
                    sidecarLaunches = builder.Resources.OfType<ProjectResource>().SelectMany(project =>
                        project.Annotations.OfType<DaprSidecarAnnotation>().Select(annotation =>
                        {
                            DaprSidecarOptions options = annotation.Sidecar.Annotations.OfType<DaprSidecarOptionsAnnotation>().Single().Options;
                            return new { appId = options.AppId ?? project.Name, config = options.Config,
                                configSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(options.Config!))).ToLowerInvariant() };
                        })).ToArray(),
                    // Written only after every startup probe above passed. An expectWorksReadinessDegraded start
                    // waited for Works to reach Running, not Healthy, so its readiness did not pass; that fixture
                    // records its own Degraded observation.
                    readinessPassed = !expectWorksReadinessDegraded,
                    worksReadinessExpectation = expectWorksReadinessDegraded ? "Degraded" : "Healthy",
                    runtimeModules = RuntimeModules(builder),
                    hostingStartups = await ReadHostingEvidenceAsync(hostingEvidence, cancellationToken).ConfigureAwait(false),
                    // Platform's composed values (evaluated from its own callbacks before the fixture ran), the keys the
                    // fixture wrote, and each module's effective launched environment, for independent comparison.
                    compositionGuard,
                    effectiveEnvironment = EffectiveEnvironment(app),
                    actorType = actorTypeName,
                    // The test runner's loaded SDK assemblies; each host's deployed SDK assemblies are bound per
                    // module in runtimeModules.
                    sdkAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) && assembly.GetName().Name!.StartsWith("Hexalith.EventStore.", StringComparison.Ordinal))
                        .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
                        .Select(assembly => new { name = assembly.GetName().Name, version = assembly.GetName().Version?.ToString(),
                            path = assembly.Location, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))) })
                        .ToArray(),
                }) + Environment.NewLine, startupCts.Token).ConfigureAwait(false);
            }
            try
            {
                // The bounded token is a startup/readiness budget, not a lifetime budget for the acceptance
                // body. Operations is now built as a runtime-only project resource, so consuming build/start time
                // from reminder deadlines can cancel an otherwise healthy future-reminder fact before it fires.
                // Body helpers carry their own bounded polling deadlines and remain linked to the test runner's
                // cancellation token.
                await body(app, client, sidecarClient, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Preserve the body's own phase tag ([registration], [delivery], [submission], …) instead of
                // relabelling every failure as [runtime]: the phase is the first thing a triage read needs.
                throw new InvalidOperationException(
                    $"Live AppHost body failed after startup/readiness probes passed. {DescribePhase(ex)}",
                    ex);
            }
        }
        catch (Exception exception)
        {
            primaryException = app is null || exception is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? exception : new InvalidOperationException(
                exception.Message + await StartupLogsAsync(app).ConfigureAwait(true), exception);
        }

        var cleanupFailures = new List<Exception>();
        if (app is not null)
        {
            try
            {
                await app.DisposeAsync().AsTask().WaitAsync(DisposalWait).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(new InvalidOperationException("[teardown] Distributed application disposal failed.", exception));
            }
        }

        try
        {
            await builder.DisposeAsync().AsTask().WaitAsync(DisposalWait).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(new InvalidOperationException("[teardown] AppHost testing builder disposal failed.", exception));
        }

        // Dispose returning does not guarantee DCP has released its fixed sockets or removed the Scheduler
        // container that owns the persistent volume. Settle both with an independent token so cancellation of
        // the test body cannot prevent cleanup. A clean body must fail when cleanup remains occupied; when the
        // body already failed, retain that primary exception and emit every cleanup failure as diagnostics.
        using (var teardownCts = new CancellationTokenSource(ControlPlaneReleaseWait + TimeSpan.FromSeconds(15)))
        {
            try
            {
                await WaitForControlPlaneResourcesReleasedAsync("teardown boundary", teardownCts.Token, schedulerVolumeName)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(new InvalidOperationException(
                    "[teardown] AppHost control-plane resources did not settle.",
                    exception));
            }
        }

        try
        {
            if (discoveryDirectory is not null) Directory.Delete(discoveryDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            cleanupFailures.Add(new InvalidOperationException("[teardown] The owned Dapr discovery registry could not be removed.", exception));
        }

        if (primaryException is not null)
        {
            foreach (Exception cleanupFailure in cleanupFailures)
            {
                TestContext.Current.SendDiagnosticMessage(
                    "Live AppHost cleanup also failed after the primary acceptance failure: {0}",
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(primaryException).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "[teardown] The live AppHost body passed, but cleanup did not settle completely.",
                cleanupFailures);
        }
    }

    /// <summary>
    /// Create → Assign → Claim → Suspend(DateReached instant): drives the work item to Suspended parked on the
    /// deterministic date await.
    /// </summary>
    public static async Task ParkSuspendedOnDateAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        var tenant = new TenantId(tenantId);
        var workItem = new WorkItemId(workItemId);
        var binding = new ExecutorBinding(new PartyId("recovery-worker"), Channel.Cli, AuthorityLevel.Contribute);

        await SubmitToTerminalAsync(client, tenantId, workItemId, nameof(CreateWorkItem), new CreateWorkItem(tenant, workItem, "Reminder-recovery obligation"), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, tenantId, workItemId, nameof(AssignWorkItem), new AssignWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(client, tenantId, workItemId, nameof(ClaimWorkItem), new ClaimWorkItem(tenant, workItem, binding), cancellationToken).ConfigureAwait(false);
        await SubmitToTerminalAsync(
            client,
            tenantId,
            workItemId,
            nameof(SuspendWorkItem),
            new SuspendWorkItem(tenant, workItem, [AwaitCondition.DateReached(instant)]),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Submits one command and asserts it reaches the Completed terminal status.</summary>
    public static async Task SubmitToTerminalAsync<TCommand>(
        HttpClient client,
        string tenantId,
        string workItemId,
        string commandType,
        TCommand command,
        CancellationToken cancellationToken)
    {
        string correlationId = await SubmitCommandAsync(
            client,
            tenantId,
            workItemId,
            messageId: Guid.NewGuid().ToString(),
            commandType: commandType,
            payload: JsonSerializer.SerializeToElement(command),
            correlationId: null,
            cancellationToken).ConfigureAwait(false);

        string status = await PollToTerminalAsync(client, tenantId, correlationId, cancellationToken).ConfigureAwait(false);
        status.ShouldBe("Completed", $"{commandType} must persist and publish to a Completed terminal status while parking the item.");
    }

    /// <summary>The deterministic pending await for one parked work item.</summary>
    public static PendingDateAwait PendingAwait(string tenantId, string workItemId, DateTimeOffset instant)
        => new(tenantId, workItemId, instant, AwaitCondition.DateReached(instant).CorrelationKey);

    /// <summary>Polls the per-aggregate stream until the accepted resume count is reached, or times out.</summary>
    public static async Task<int> WaitForResumedCountAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        int atLeast,
        CancellationToken cancellationToken,
        DateTimeOffset? deadline = null)
    {
        int count = 0;
        DateTimeOffset effectiveDeadline = deadline ?? DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < effectiveDeadline)
        {
            count = await CountResumedAsync(client, tenantId, workItemId, cancellationToken).ConfigureAwait(false);
            if (count >= atLeast)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"[delivery] Work item '{workItemId}' did not reach {atLeast} accepted {nameof(WorkItemResumed)} "
            + $"event(s) by {effectiveDeadline:O}; observed {count}.");
    }

    /// <summary>Counts accepted <c>WorkItemResumed</c> events in the item's re-readable per-aggregate stream.</summary>
    public static async Task<int> CountResumedAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            tenant = tenantId,
            domain = "work",
            aggregateId = workItemId,
            fromSequence = 0L,
            pageSize = 100,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(tenantId));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "Per-aggregate stream read must return 200 OK for the parked work item.");

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);

        int resumed = 0;
        foreach (JsonElement streamEvent in page.GetProperty("events").EnumerateArray())
        {
            string? typeName = streamEvent.GetProperty("eventTypeName").GetString();
            if (typeName is not null && SimpleTypeName(typeName) == nameof(WorkItemResumed))
            {
                resumed++;
            }
        }

        return resumed;
    }

    /// <summary>Mints a development bearer token scoped to one tenant.</summary>
    public static string MintToken(string tenantId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "works-reminder-recovery-user"),
                new Claim("tenants", JsonSerializer.Serialize(new[] { tenantId })),
                new Claim("domains", JsonSerializer.Serialize(new[] { "work" })),
                new Claim("permissions", JsonSerializer.Serialize(new[] { "command:submit", "command:query", "command:replay" })),
            ]),
            Issuer = "hexalith-dev",
            Audience = "hexalith-eventstore",
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevSigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static async Task<string> SubmitCommandAsync(
        HttpClient client,
        string tenantId,
        string workItemId,
        string messageId,
        string commandType,
        JsonElement payload,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var body = new SubmitCommandRequest(
            MessageId: messageId,
            Tenant: tenantId,
            Domain: "work",
            AggregateId: workItemId,
            CommandType: commandType,
            Payload: payload,
            CorrelationId: correlationId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(tenantId));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string diagnosticBody = responseBody.Length <= 2_000 ? responseBody : responseBody[..2_000] + "…";
        response.StatusCode.ShouldBe(
            HttpStatusCode.Accepted,
            $"[submission] {commandType} must return 202 Accepted. Body={diagnosticBody}");

        JsonElement result = JsonSerializer.Deserialize<JsonElement>(responseBody);
        return result.GetProperty("correlationId").GetString()!;
    }

    private static async Task<string> PollToTerminalAsync(
        HttpClient client,
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        string status = "unknown";
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/commands/status/{correlationId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(tenantId));

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
                status = body.GetProperty("status").GetString() ?? "unknown";
                if (status is "Completed" or "Rejected" or "PublishFailed" or "TimedOut")
                {
                    return status;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return status;
    }

    /// <summary>Extracts the innermost phase tag a live helper attached, or reports that none was carried.</summary>
    private static string DescribePhase(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message;
            int close = message.IndexOf(']');
            if (message.StartsWith('[') && close > 0)
            {
                return $"Phase={message[..(close + 1)]}";
            }
        }

        return "Phase=[runtime] (the failure carried no phase tag).";
    }

    private static string SimpleTypeName(string typeName)
    {
        int lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    /// <summary>
    /// Waits until every fixed control-plane port is free and no container still owns the persistent Scheduler
    /// volume at a start or teardown boundary.
    /// </summary>
    private static async Task WaitForControlPlaneResourcesReleasedAsync(
        string boundary,
        CancellationToken cancellationToken,
        string? schedulerVolumeName = null)
        => await WaitForControlPlaneResourcesReleasedAsync(
            boundary,
            ControlPlaneReleaseWait,
            TimeSpan.FromSeconds(1),
            IsPortAvailableForExclusiveBind,
            token => SchedulerVolumeOwnersAsync(token, schedulerVolumeName),
            static (delay, token) => Task.Delay(delay, token),
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Waits for the fixed control-plane resources using the supplied probes and bounded retry policy.
    /// </summary>
    /// <param name="boundary">The diagnostic name for the start or teardown boundary.</param>
    /// <param name="releaseWait">The total time allowed for resources to settle.</param>
    /// <param name="retryDelay">The delay between observations.</param>
    /// <param name="portAvailable">Returns whether one address and port can be bound exclusively.</param>
    /// <param name="schedulerVolumeOwners">Returns every container owning the persistent Scheduler volume.</param>
    /// <param name="delayAsync">Applies the retry delay.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    internal static async Task WaitForControlPlaneResourcesReleasedAsync(
        string boundary,
        TimeSpan releaseWait,
        TimeSpan retryDelay,
        Func<IPAddress, int, bool> portAvailable,
        Func<CancellationToken, Task<IReadOnlyList<string>>> schedulerVolumeOwners,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        ArgumentOutOfRangeException.ThrowIfLessThan(releaseWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(portAvailable);
        ArgumentNullException.ThrowIfNull(schedulerVolumeOwners);
        ArgumentNullException.ThrowIfNull(delayAsync);

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var occupied = new List<string>();
            foreach ((int port, string name) in ControlPlanePorts)
            {
                foreach (IPAddress address in ControlPlaneLoopbacks)
                {
                    if (!portAvailable(address, port))
                    {
                        occupied.Add($"{name} on {address}:{port}");
                    }
                }
            }

            try
            {
                IReadOnlyList<string> owners = await schedulerVolumeOwners(cancellationToken).ConfigureAwait(false);
                occupied.AddRange(owners.Select(static owner => $"scheduler volume owner {owner}"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SchedulerVolumeProbeCleanupException)
            {
                // Starting another Docker probe after adapter cleanup could not confirm child termination would
                // accumulate children. Surface the actionable cleanup diagnostic immediately.
                throw;
            }
            catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
            {
                occupied.Add($"scheduler volume ownership probe unavailable ({exception.Message})");
            }

            if (occupied.Count == 0)
            {
                return;
            }

            if (elapsed.Elapsed >= releaseWait)
            {
                throw new TimeoutException(
                    $"[{boundary}] Fixed AppHost control-plane resources were still occupied after "
                    + $"{releaseWait.TotalSeconds:0} seconds: {string.Join(", ", occupied)}. "
                    + "Stop leaked DCP controllers/containers or another AppHost using this Works control plane before retrying.");
            }

            await delayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> SchedulerVolumeOwnersAsync(CancellationToken cancellationToken, string? schedulerVolumeName = null)
    {
        // Platform owns no persistent Scheduler volume; without a proof volume there is nothing to own.
        if ((schedulerVolumeName ?? SchedulerVolumeName) is null) return [];
        ProcessStartInfo startInfo = CreateSchedulerVolumeProbeStartInfo(schedulerVolumeName);

        ISchedulerVolumeProbe probe;
        try
        {
            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Docker to inspect the Scheduler volume owner.");
            probe = new ProcessSchedulerVolumeProbe(process);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Could not start Docker to inspect the Scheduler volume owner.",
                exception);
        }

        return await RunAndDisposeSchedulerVolumeProbeAsync(
            probe,
            DockerProbeWait,
            DockerProbeTerminationWait,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs and disposes one Scheduler-volume probe with production exception precedence.</summary>
    internal static async Task<IReadOnlyList<string>> RunAndDisposeSchedulerVolumeProbeAsync(
        ISchedulerVolumeProbe probe,
        TimeSpan probeWait,
        TimeSpan terminationWait,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);

        IReadOnlyList<string>? owners = null;
        Exception? probeFailure = null;
        try
        {
            owners = await RunSchedulerVolumeProbeAsync(
                probe,
                probeWait,
                terminationWait,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            probeFailure = exception;
        }

        Exception? disposalFailure = null;
        try
        {
            probe.Dispose();
        }
        catch (Exception exception)
        {
            disposalFailure = exception;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (disposalFailure is not null)
        {
            Exception classifiedFailure = probeFailure is null
                ? disposalFailure
                : new AggregateException(probeFailure, disposalFailure);
            if (disposalFailure is SchedulerVolumeProbeCleanupException)
            {
                throw new SchedulerVolumeProbeCleanupException(
                    "Docker Scheduler-volume probe disposal could not confirm child termination.",
                    classifiedFailure);
            }

            throw new InvalidOperationException(
                "Docker Scheduler-volume probe disposal encountered a classified cleanup failure.",
                classifiedFailure);
        }

        if (probeFailure is not null)
        {
            ExceptionDispatchInfo.Capture(probeFailure).Throw();
        }

        return owners!;
    }

    /// <summary>Runs one Scheduler-volume ownership probe with bounded execution and termination.</summary>
    /// <param name="probe">The process abstraction to execute.</param>
    /// <param name="probeWait">The maximum probe execution time.</param>
    /// <param name="terminationWait">The maximum wait after requesting termination.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    internal static async Task<IReadOnlyList<string>> RunSchedulerVolumeProbeAsync(
        ISchedulerVolumeProbe probe,
        TimeSpan probeWait,
        TimeSpan terminationWait,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(probeWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(terminationWait, TimeSpan.Zero);

        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(probeWait);
        Task<string> outputTask = StartProbeRead(
            () => probe.ReadStandardOutputAsync(probeCts.Token));
        Task<string> errorTask = StartProbeRead(
            () => probe.ReadStandardErrorAsync(probeCts.Token));
        bool waitingForExit = true;
        string error;
        int exitCode;
        string output;
        try
        {
            await probe.WaitForExitAsync(probeCts.Token).ConfigureAwait(false);
            waitingForExit = false;
            cancellationToken.ThrowIfCancellationRequested();
            string[] redirectedOutput = await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            output = redirectedOutput[0];
            error = redirectedOutput[1];
            exitCode = probe.ExitCode;
        }
        catch (OperationCanceledException exception) when (probeCts.IsCancellationRequested)
        {
            probeCts.Cancel();
            Exception? cleanupFailure = await TerminateAndObserveProbeAsync(
                probe,
                outputTask,
                errorTask,
                terminationWait).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (cleanupFailure is not null)
            {
                throw new InvalidOperationException(
                    "Docker Scheduler-volume probe cleanup failed after the bounded execution window.",
                    cleanupFailure);
            }

            throw new TimeoutException(
                $"Docker did not inspect the Scheduler volume owner within {probeWait.TotalSeconds:0} seconds.",
                exception);
        }
        catch (Exception exception) when (
            waitingForExit
            && exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            probeCts.Cancel();
            Exception? cleanupFailure = await TerminateAndObserveProbeAsync(
                probe,
                outputTask,
                errorTask,
                terminationWait).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Exception classifiedFailure = cleanupFailure is null
                ? exception
                : new AggregateException(exception, cleanupFailure);
            throw new InvalidOperationException(
                $"Docker Scheduler-volume probe process observation failed: {exception.Message}",
                classifiedFailure);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException(
                $"Docker Scheduler-volume probe redirected stream observation failed: {exception.Message}",
                exception);
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException(
                $"Docker Scheduler-volume probe process observation failed: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            string errorDetail = string.IsNullOrWhiteSpace(error) ? "no standard-error detail" : error.Trim();
            throw new InvalidOperationException(
                $"Docker could not inspect the Scheduler volume owner (exit {exitCode}): {errorDetail}.");
        }

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Builds the Docker command that includes running and stopped Scheduler-volume owners.</summary>
    internal static ProcessStartInfo CreateSchedulerVolumeProbeStartInfo(string? schedulerVolumeName = null)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("ps");
        startInfo.ArgumentList.Add("--all");
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add($"volume={schedulerVolumeName ?? SchedulerVolumeName}");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.ID}} {{.Names}}");
        return startInfo;
    }

    /// <summary>Classifies whether the production exclusive-bind probe succeeds for one address and port.</summary>
    /// <param name="address">The loopback address to probe.</param>
    /// <param name="port">The fixed control-plane port to probe.</param>
    /// <returns><see langword="true"/> when the address is bindable or inapplicable on this host.</returns>
    internal static bool IsPortAvailableForExclusiveBind(IPAddress address, int port)
        => IsPortAvailableForExclusiveBind(
            address,
            port,
            static (candidateAddress, candidatePort) =>
                _ = BindPortExclusively(candidateAddress, candidatePort));

    /// <summary>Classifies whether an exclusive bind succeeds or is inapplicable on this host.</summary>
    /// <param name="address">The loopback address to probe.</param>
    /// <param name="port">The fixed control-plane port to probe.</param>
    /// <param name="bindExclusively">Attempts the exclusive bind.</param>
    internal static bool IsPortAvailableForExclusiveBind(
        IPAddress address,
        int port,
        Action<IPAddress, int> bindExclusively)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentNullException.ThrowIfNull(bindExclusively);

        try
        {
            bindExclusively(address, port);
            return true;
        }
        catch (SocketException exception) when (IsUnavailableLoopbackAddress(address, exception.SocketErrorCode))
        {
            // IPv6 can be disabled at the host/kernel level. That makes ::1 inapplicable, not occupied; the
            // IPv4 loopback still proves whether the fixed control-plane port is available on this host.
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Creates, configures, and starts the listener used by the production exclusive-bind probe.</summary>
    /// <param name="address">The loopback address to bind.</param>
    /// <param name="port">The port to bind, or zero to let the operating system select one.</param>
    /// <returns>The socket settings observed on the listener that was started.</returns>
    internal static (bool ExclusiveAddressUse, bool? DualMode) BindPortExclusively(
        IPAddress address,
        int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegative(port);

        var listener = new TcpListener(address, port);
        try
        {
            BindPortExclusively(listener, address);
            return (
                listener.Server.ExclusiveAddressUse,
                address.AddressFamily == AddressFamily.InterNetworkV6
                    ? listener.Server.DualMode
                    : null);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Configures and starts one listener through the production exclusive-bind operation.</summary>
    /// <param name="listener">The listener to configure and start.</param>
    /// <param name="address">The loopback address the listener will probe.</param>
    internal static void BindPortExclusively(TcpListener listener, IPAddress address)
    {
        ConfigureExclusiveBindListener(listener, address);
        listener.Start();
    }

    /// <summary>Applies the production exclusive-bind settings to one control-plane listener.</summary>
    /// <param name="listener">The listener to configure.</param>
    /// <param name="address">The loopback address the listener will probe.</param>
    private static void ConfigureExclusiveBindListener(TcpListener listener, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            listener.Server.DualMode = false;
        }

        listener.Server.ExclusiveAddressUse = true;
    }

    /// <summary>Returns whether a bind failure means the loopback address family is unavailable on this host.</summary>
    /// <param name="address">The loopback address that was probed.</param>
    /// <param name="socketError">The bind failure reported by the socket API.</param>
    internal static bool IsUnavailableLoopbackAddress(IPAddress address, SocketError socketError)
        => address.AddressFamily == AddressFamily.InterNetworkV6
            && socketError is SocketError.AddressFamilyNotSupported
                or SocketError.AddressNotAvailable
                or SocketError.ProtocolNotSupported
                or SocketError.ProtocolFamilyNotSupported;

    private static async Task<Exception?> TerminateAndObserveProbeAsync(
        ISchedulerVolumeProbe probe,
        Task<string> outputTask,
        Task<string> errorTask,
        TimeSpan terminationWait)
    {
        var cleanupFailures = new List<Exception>();
        TimeSpan redirectedReadWait = terminationWait + terminationWait;
        Task<Exception?> outputObservation = ObserveProbeTaskAsync(
            outputTask,
            "standard output",
            redirectedReadWait);
        Task<Exception?> errorObservation = ObserveProbeTaskAsync(
            errorTask,
            "standard error",
            redirectedReadWait);

        bool exited = ProbeHasExited(probe, cleanupFailures);
        if (!exited)
        {
            TryTerminateProbe(probe, cleanupFailures);
            exited = await WaitForProbeExitAsync(probe, terminationWait, cleanupFailures).ConfigureAwait(false);
        }

        if (!exited)
        {
            TryTerminateProbe(probe, cleanupFailures);
            exited = await WaitForProbeExitAsync(probe, terminationWait, cleanupFailures).ConfigureAwait(false);
        }

        if (!exited)
        {
            exited = ProbeHasExited(probe, cleanupFailures);
        }

        Exception?[] redirectedReadFailures = await Task.WhenAll(outputObservation, errorObservation).ConfigureAwait(false);
        cleanupFailures.AddRange(redirectedReadFailures.OfType<Exception>());

        if (!exited)
        {
            cleanupFailures.Add(
                new TimeoutException(
                    $"Docker Scheduler-volume probe did not exit after two bounded termination attempts of "
                    + $"{terminationWait.TotalSeconds:0.###} seconds each."));
        }

        return cleanupFailures.Count switch
        {
            0 => null,
            1 => cleanupFailures[0],
            _ => new AggregateException(cleanupFailures),
        };
    }

    private static bool ProbeHasExited(ISchedulerVolumeProbe probe, List<Exception> cleanupFailures)
    {
        try
        {
            return probe.HasExited;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            cleanupFailures.Add(exception);
            return false;
        }
    }

    private static void TryTerminateProbe(ISchedulerVolumeProbe probe, List<Exception> cleanupFailures)
    {
        try
        {
            probe.Kill();
        }
        catch (InvalidOperationException) when (ProbeHasExited(probe, cleanupFailures))
        {
            // The process exited between the HasExited observation and Kill.
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            cleanupFailures.Add(exception);
        }
    }

    private static async Task<bool> WaitForProbeExitAsync(
        ISchedulerVolumeProbe probe,
        TimeSpan terminationWait,
        List<Exception> cleanupFailures)
    {
        using var terminationCts = new CancellationTokenSource(terminationWait);
        try
        {
            await probe.WaitForExitAsync(terminationCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (terminationCts.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            cleanupFailures.Add(exception);
            return ProbeHasExited(probe, cleanupFailures);
        }
    }

    private static async Task<Exception?> ObserveProbeTaskAsync(
        Task<string> probeTask,
        string streamName,
        TimeSpan observationWait)
    {
        try
        {
            _ = await probeTask.WaitAsync(observationWait).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Cancellation and pipe closure are expected after terminating the timed-out child. Observing both
            // redirected reads prevents either cleanup fault from masking the classified probe timeout.
            return null;
        }
        catch (TimeoutException exception)
        {
            return new TimeoutException(
                $"Docker Scheduler-volume probe {streamName} did not settle within the bounded cleanup window.",
                exception);
        }
        catch (Exception exception)
        {
            return new InvalidOperationException(
                $"Docker Scheduler-volume probe {streamName} failed during cleanup: {exception.Message}",
                exception);
        }
    }

    private static Task<string> StartProbeRead(Func<Task<string>> startRead)
    {
        try
        {
            return startRead()
                ?? Task.FromException<string>(
                    new InvalidOperationException("Docker Scheduler-volume probe returned a null redirected-read task."));
        }
        catch (Exception exception)
        {
            return Task.FromException<string>(exception);
        }
    }

    /// <summary>Checks the TCP prerequisite without converting caller cancellation into an unavailable result.</summary>
    /// <param name="port">The local prerequisite port.</param>
    /// <param name="cancellationToken">Cancels the caller's prerequisite check.</param>
    /// <returns>Whether the local port accepts a connection within the probe budget.</returns>
    internal static async Task<bool> IsPortReachableAsync(int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await IsPortReachableAsync(
            token => client.ConnectAsync("localhost", port, token).AsTask(),
            cancellationToken,
            timeout.Token).ConfigureAwait(false);
    }

    /// <summary>Preserves caller cancellation across connection completion, socket failure and probe timeout races.</summary>
    /// <param name="connectAsync">The connection operation receiving the linked cancellation token.</param>
    /// <param name="cancellationToken">The caller's original cancellation token.</param>
    /// <param name="probeTimeoutToken">The independently controlled probe deadline.</param>
    /// <returns>Whether the connection succeeds before the probe deadline.</returns>
    internal static async Task<bool> IsPortReachableAsync(
        Func<CancellationToken, Task> connectAsync,
        CancellationToken cancellationToken,
        CancellationToken probeTimeoutToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, probeTimeoutToken);
        try
        {
            await connectAsync(probeCts.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return !probeTimeoutToken.IsCancellationRequested;
        }
        catch (SocketException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (probeTimeoutToken.IsCancellationRequested)
            {
                return false;
            }

            throw;
        }
    }
}
