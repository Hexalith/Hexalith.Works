using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Dapr.Client;

using Hexalith.EventStore.Aspire;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Actual independent Works hosts share SDK recovery, mTLS placement and durable receipts.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksPlatformHaPipelineSmokeTests
{
    /// <summary>One Works host fails after a lost firing; its surviving peer recovers and rejects a stale firing.</summary>
    [Fact(Explicit = true)]
    public async Task TwoWorksHostsRecoverLostFiringAfterPrimaryStopsWithoutDuplicateEffect()
    {
        if (!WorksAppHostSelection.IsPlatform)
            Assert.Skip("Runs only in the Platform verifier lane, which binds the actual Platform AppHost artifact.");
        CancellationToken ct = TestContext.Current.CancellationToken;
        (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true)).ShouldBeNull();
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        string suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));
        string tenant = "sdk415-ha-" + suffix;
        string item = "work-sdk415-ha-" + suffix;
        using RSA authority = RSA.Create(2048);
        string key = Convert.ToBase64String(authority.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, gateway, sidecar, token) =>
        {
            object trust = await WorksPlatformTrustChecks.VerifyAsync(app, "works-dapr-cli", "works", "eventstore", token).ConfigureAwait(false);
            ResourceEvent primary = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", token, token).ConfigureAwait(false);
            ResourceEvent secondary = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works-ha", token, token).ConfigureAwait(false);
            primary.ResourceId.ShouldNotBe(secondary.ResourceId);
            string Endpoint(ResourceEvent resource, string name) => resource.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
            Endpoint(primary, "DAPR_HTTP_ENDPOINT").ShouldNotBe(Endpoint(secondary, "DAPR_HTTP_ENDPOINT"));
            using DaprClient dapr = new DaprClientBuilder().UseHttpEndpoint(Endpoint(primary, "DAPR_HTTP_ENDPOINT"))
                .UseGrpcEndpoint(Endpoint(primary, "DAPR_GRPC_ENDPOINT")).Build();
            var store = new DaprReadModelStore(dapr);
            DateTimeOffset due = DateTimeOffset.UtcNow.AddSeconds(60);
            await WorksAppHostSmokeHarness.ParkSuspendedOnDateAsync(gateway, tenant, item, due, token).ConfigureAwait(false);
            string actor = ReminderIdentityCodec.ComputeActorId(tenant, item);
            string reminderName = await WaitWitnessAsync(store, "eventstore:reminders:v1:WorksSdkReminderActor:item:" + actor, token).ConfigureAwait(false);
            using (HttpResponseMessage deleted = await sidecar.DeleteAsync($"v1.0/actors/WorksSdkReminderActor/{actor}/reminders/{reminderName}", token).ConfigureAwait(false))
                deleted.EnsureSuccessStatusCode();
            ResourceCommandService commands = app.Services.GetRequiredService<ResourceCommandService>();
            ExecuteCommandResult stopped = await commands.ExecuteCommandAsync(primary.ResourceId, KnownResourceCommands.StopCommand, token).ConfigureAwait(false);
            stopped.Success.ShouldBeTrue(stopped.Message);
            ResourceEvent primarySidecar = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works-dapr-cli", token, token).ConfigureAwait(false);
            ExecuteCommandResult sidecarStopped = await commands.ExecuteCommandAsync(primarySidecar.ResourceId, KnownResourceCommands.StopCommand, token).ConfigureAwait(false);
            sidecarStopped.Success.ShouldBeTrue(sidecarStopped.Message);
            using (var stopDeadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                stopDeadline.CancelAfter(TimeSpan.FromSeconds(30));
                await app.ResourceNotifications.WaitForResourceAsync("works",
                    [KnownResourceStates.Exited, KnownResourceStates.Finished], stopDeadline.Token).ConfigureAwait(false);
                await app.ResourceNotifications.WaitForResourceAsync("works-dapr-cli",
                    [KnownResourceStates.Exited, KnownResourceStates.Finished], stopDeadline.Token).ConfigureAwait(false);
            }
            DateTimeOffset primaryStoppedAt = DateTimeOffset.UtcNow;
            bool primaryStoppedBeforeDue = primaryStoppedAt < due;
            primaryStoppedBeforeDue.ShouldBeTrue("The primary must be stopped before the scheduled date can fire.");
            int resumeCountAtPrimaryStop = await WorksAppHostSmokeHarness.CountResumedAsync(gateway, tenant, item, token).ConfigureAwait(false);
            resumeCountAtPrimaryStop.ShouldBe(0, "Recovery must occur only after the primary has stopped.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            int resumed;
            while ((resumed = await WorksAppHostSmokeHarness.CountResumedAsync(gateway, tenant, item, deadline.Token).ConfigureAwait(false)) != 1)
                await Task.Delay(200, deadline.Token).ConfigureAwait(false);
            using HttpClient survivor = WorksAppHostTestReadiness.CreateDaprClient(secondary, "Surviving Works");
            using DaprClient survivorDapr = new DaprClientBuilder().UseHttpEndpoint(Endpoint(secondary, "DAPR_HTTP_ENDPOINT"))
                .UseGrpcEndpoint(Endpoint(secondary, "DAPR_GRPC_ENDPOINT")).Build();
            var survivorStore = new DaprReadModelStore(survivorDapr);
            string callbackKey = WorksPlatformReminderCallbackProbe.Key(actor);
            int beforeCallbacks = (await survivorStore.GetAsync<System.Text.Json.Nodes.JsonObject>("statestore", callbackKey, token).ConfigureAwait(false)).Value?["calls"]?.GetValue<int>() ?? 0;
            int afterCallbacks = beforeCallbacks;
            bool retiredReminderRemovedAfterFiring = false;
            string? firedReminder = null;
            // A stale witness is fired by the real Scheduler after the logical effect committed.
            using (HttpResponseMessage stale = await survivor.PutAsJsonAsync($"v1.0/actors/WorksSdkReminderActor/{actor}/reminders/{reminderName}",
                new { dueTime = "1s", period = "0s", data = (object?)null }, token).ConfigureAwait(false)) stale.EnsureSuccessStatusCode();
            using (var staleDeadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                staleDeadline.CancelAfter(TimeSpan.FromSeconds(30));
                while (true)
                {
                    using HttpResponseMessage reminder = await survivor.GetAsync(
                        $"v1.0/actors/WorksSdkReminderActor/{actor}/reminders/{reminderName}", staleDeadline.Token).ConfigureAwait(false);
                    System.Text.Json.Nodes.JsonObject? observation = (await survivorStore.GetAsync<System.Text.Json.Nodes.JsonObject>(
                        "statestore", callbackKey, staleDeadline.Token).ConfigureAwait(false)).Value;
                    afterCallbacks = observation?["calls"]?.GetValue<int>() ?? 0;
                    if (reminder.StatusCode == System.Net.HttpStatusCode.NotFound && afterCallbacks > beforeCallbacks)
                    {
                        firedReminder = observation!["reminder"]!.GetValue<string>();
                        firedReminder.ShouldBe(reminderName);
                        retiredReminderRemovedAfterFiring = true;
                        break;
                    }
                    if (reminder.StatusCode != System.Net.HttpStatusCode.NotFound) reminder.EnsureSuccessStatusCode();
                    await Task.Delay(100, staleDeadline.Token).ConfigureAwait(false);
                }
            }
            // Re-observed after the stale firing: the committed resume count stays one.
            int staleSchedulerFireResumed = await WorksAppHostSmokeHarness.CountResumedAsync(gateway, tenant, item, token).ConfigureAwait(false);
            staleSchedulerFireResumed.ShouldBe(1);
            bool actualStaleCallbackObserved = afterCallbacks > beforeCallbacks && firedReminder == reminderName;
            if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } path)
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new
                {
                    phase = "platform-two-host-ha-lost-firing-stale-callback-recovery",
                    state = new { primary = primary.ResourceId, secondary = secondary.ResourceId,
                        primarySidecar = Endpoint(primary, "DAPR_HTTP_ENDPOINT"), secondarySidecar = Endpoint(secondary, "DAPR_HTTP_ENDPOINT"),
                        actor, reminderName, firedReminder, trust, stoppedPrimary = stopped.Success, stoppedPrimarySidecar = sidecarStopped.Success, resumed,
                        due, primaryStoppedAt, primaryStoppedBeforeDue, resumeCountAtPrimaryStop,
                        retiredReminderRemovedAfterFiring, beforeCallbacks, afterCallbacks, actualStaleCallbackObserved, staleSchedulerFireResumed },
                }) + Environment.NewLine, token).ConfigureAwait(false);
        }, builder =>
        {
            AddIndependentReplica(builder);
            WorksSdkRecoveryPipelineSmokeTests.Configure(builder, key, digestKey, isolation.ComponentPath, isolation.RedisEndpoint,
                [new ProcessSource(tenant, "work", item)]);
            foreach (ProjectResource host in builder.Resources.OfType<ProjectResource>().Where(resource => resource.Name is "works" or "works-ha"))
                builder.CreateResourceBuilder(host).WithEnvironment(WorksPlatformReminderCallbackProbe.ActorEnvironment, ReminderIdentityCodec.ComputeActorId(tenant, item));
        }, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);
    }

    private static void AddIndependentReplica(IDistributedApplicationTestingBuilder builder)
    {
        ProjectResource primary = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "works");
        string platform = Environment.GetEnvironmentVariable("WORKS_PLATFORM_ROOT")!;
        string works = Environment.GetEnvironmentVariable("WORKS_PLATFORM_WORKS_ROOT") ?? Path.GetFullPath(Path.Combine(platform, "..", "works"));
        IResourceBuilder<ProjectResource> replica = builder.AddProject("works-ha", Path.Combine(works, "src", "Hexalith.Works", "Hexalith.Works.csproj")).WithHttpEndpoint();
        // Each resource owns its environment annotations. Re-registering another project's callbacks would
        // also share their captured state and endpoint bindings. Instead, evaluate only the callbacks Platform
        // composed for its works host, into a scratch dictionary, and copy the named string values. The replica
        // therefore runs Platform's own runtime hook, hosting startup, SDK modes and workload callers.
        EnvironmentCallbackAnnotation[] platformComposition = [.. primary.Annotations.OfType<EnvironmentCallbackAnnotation>()];
        replica.WithEnvironment(async context =>
        {
            var composed = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (EnvironmentCallbackAnnotation callback in platformComposition)
                await callback.Callback(new EnvironmentCallbackContext(context.ExecutionContext, primary, composed, context.CancellationToken)).ConfigureAwait(false);
            foreach ((string name, string value) in PlatformComposedValues(composed))
                context.EnvironmentVariables[name] = value;
        });
        var resources = new HexalithEventStoreResources(
            builder.CreateResourceBuilder((IDaprComponentResource)builder.Resources.Single(resource => resource.Name == "statestore")),
            builder.CreateResourceBuilder((IDaprComponentResource)builder.Resources.Single(resource => resource.Name == "pubsub")),
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "eventstore")),
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "eventstore-admin")));
        // Platform composes the command gateway on its own works host. The replica receives the same endpoint
        // reference; the fixture never re-sets a key Platform composed on the primary.
        replica.WithEnvironment("EventStore__CommandGateway__BaseAddress", resources.EventStore.GetEndpoint("http"));
        replica.WithEnvironment("ASPNETCORE_URLS", ReferenceExpression.Create($"http://localhost:{replica.GetEndpoint("http").Property(EndpointProperty.TargetPort)}"));
        replica.AddEventStoreDomainModule(resources, "works", Path.Combine(platform, "DaprComponents", "accesscontrol.works.yaml"),
            daprPlacementHostAddress: "localhost:" + WorksAppHostSelection.ControlPlanePorts.Placement.ToString(System.Globalization.CultureInfo.InvariantCulture),
            daprSchedulerHostAddress: "localhost:" + WorksAppHostSelection.ControlPlanePorts.Scheduler.ToString(System.Globalization.CultureInfo.InvariantCulture));
        IDaprSidecarResource replicaSidecar = replica.Resource.Annotations.OfType<DaprSidecarAnnotation>().Single().Sidecar;
        DaprSidecarOptionsAnnotation replicaOptions = replicaSidecar.Annotations.OfType<DaprSidecarOptionsAnnotation>().Single();
        DaprSidecarOptions primaryOptions = primary.Annotations.OfType<DaprSidecarAnnotation>().Single().Sidecar.Annotations.OfType<DaprSidecarOptionsAnnotation>().Single().Options;
        replicaSidecar.Annotations.Remove(replicaOptions);
        replicaSidecar.Annotations.Add(new DaprSidecarOptionsAnnotation(replicaOptions.Options with { ResourcesPaths = primaryOptions.ResourcesPaths }));
        DaprSelfHostedMtls.ConfigureSidecar(replica,
            replica.Resource.Annotations.OfType<DaprSidecarAnnotation>().Single().Sidecar,
            builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "dapr-sentry")),
            WorksPlatformTrustChecks.CredentialDirectory(builder.Resources),
            builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "dapr-placement-mtls")),
            builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "dapr-scheduler-mtls")));
        replica.WaitFor(resources.StateStore).WithHttpHealthCheck("/ready");
        foreach (WaitAnnotation wait in replica.Resource.Annotations.OfType<WaitAnnotation>()
            .Where(wait => ReferenceEquals(wait.Resource, resources.EventStore.Resource)).ToArray()) replica.Resource.Annotations.Remove(wait);
    }

    /// <summary>Selects the Platform-composed works values a replica must share, refusing any that Platform did not compose.</summary>
    /// <param name="composed">The environment Platform composed for its works host.</param>
    /// <returns>The mirrored names and string values.</returns>
    internal static IReadOnlyList<(string Name, string Value)> PlatformComposedValues(IReadOnlyDictionary<string, object> composed)
    {
        // DOTNET_ENVIRONMENT too: on an actual Platform start the fixture no longer sets it, so the replica takes Platform's value.
        string[] required = ["DOTNET_ENVIRONMENT", "WORKS_PLATFORM_RUNTIME", "DOTNET_STARTUP_HOOKS", "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES",
            "Dapr__DeclarativeSubscriptions", "Works__Projection__UseSdkDelivery", "Works__Recovery__UseSdkRuntime", "Works__Query__GatewayWorkloadId",
            "Authentication__Workload__Audience", "Authentication__WorkloadIssuer__Workload", "Authentication__Workload__AllowedCallers__0"];
        string[] prefixed = [.. composed.Keys.Where(name => name.StartsWith("Authentication__Workload__AllowedCallers__", StringComparison.Ordinal)
            || name.StartsWith("Authentication__JwtBearer__", StringComparison.Ordinal))];
        List<(string Name, string Value)> values = [];
        foreach (string name in required.Union(prefixed, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!composed.TryGetValue(name, out object? value) || value is not string text
                || (text.Length == 0 && name != "Authentication__JwtBearer__Authority"))
                throw new InvalidOperationException("Platform did not compose a string " + name + " value for its works host.");
            values.Add((name, text));
        }

        return values;
    }

    private static async Task<string> WaitWitnessAsync(IReadModelStore store, string key, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (true)
        {
            var state = (await store.GetAsync<System.Text.Json.Nodes.JsonObject>("statestore", key, deadline.Token).ConfigureAwait(false)).Value;
            if (state?["entries"]?.AsArray().FirstOrDefault()?["reminderName"]?.GetValue<string>() is { } name)
                return name;
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }

}
