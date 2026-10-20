using System.Security.Cryptography;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Dapr.Client;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Focused real-target rebuild diagnostics; full recovery and restore remain independently required.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksPlatformProjectionPipelineSmokeTests
{
    /// <summary>Promotes all eleven sources and catches a genuinely new event committed during capture.</summary>
    [Fact(Explicit = true)]
    public async Task ActualPlatformSharedRebuildAndConcurrentCatchUpPreserveCompleteFamily()
    {
        if (!WorksAppHostSelection.IsPlatform)
            Assert.Skip("Runs only in the Platform verifier lane, which binds the actual Platform AppHost artifact.");
        CancellationToken ct = TestContext.Current.CancellationToken;
        (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true)).ShouldBeNull();
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        string suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));
        string tenant = "sdk415-projection-" + suffix;
        ProcessSource[] sources = [.. Enumerable.Range(0, 11).Select(index => new ProcessSource(tenant, "work", "work-sdk415-projection-" + suffix + "-" + index))];
        string owner = sources[0].Aggregate;
        using RSA authority = RSA.Create(2048);
        string key = Convert.ToBase64String(authority.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        // Test-only Parties identity for the delegated Executor queries; neither host composes coordinator authority.
        WorksPlatformIdentityFixture identity = await WorksPlatformIdentityFixture.StartAsync(tenant,
            WorksPlatformIdentityFixture.SyntheticExecutorParty, WorksPlatformIdentityFixture.SyntheticExecutorActor, ct).ConfigureAwait(true);
        await using var identityCleanup = identity.ConfigureAwait(false);
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, gateway, _sidecar, token) =>
        {
            var tenantId = new TenantId(tenant);
            // The owner keeps its three-event history (created, assigned, reassigned) and rests Assigned to the
            // synthetic Executor, so the delegated Executor view can read it before and after the rebuilds.
            var initial = new ExecutorBinding(new PartyId("synthetic-projection-worker"), Channel.Cli, AuthorityLevel.Contribute);
            var executor = new ExecutorBinding(new PartyId(identity.Party), Channel.Cli, AuthorityLevel.Contribute);
            foreach (ProcessSource source in sources)
            {
                await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, source.Aggregate, nameof(CreateWorkItem),
                    new CreateWorkItem(tenantId, new WorkItemId(source.Aggregate), "Focused eleven-source projection"), token).ConfigureAwait(false);
                if (source.Aggregate == owner)
                {
                    await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, owner, nameof(AssignWorkItem),
                        new AssignWorkItem(tenantId, new WorkItemId(owner), initial), token).ConfigureAwait(false);
                    await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, tenant, owner, nameof(AssignWorkItem),
                        new AssignWorkItem(tenantId, new WorkItemId(owner), executor), token).ConfigureAwait(false);
                }
            }
            ResourceEvent resource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", token, token).ConfigureAwait(false);
            string Endpoint(string name) => resource.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
            using DaprClient dapr = new DaprClientBuilder().UseHttpEndpoint(Endpoint("DAPR_HTTP_ENDPOINT")).UseGrpcEndpoint(Endpoint("DAPR_GRPC_ENDPOINT")).Build();
            var store = new DaprReadModelStore(dapr);
            var coordinator = new SharedProjectionEpochCoordinator(store, store);
            var scope = new SharedProjectionScope("statestore", tenant, "work", WorksReadModelKeys.RuntimeFamily, ["works-runtime-v3-writer"]);
            var checkpoints = new ProcessCheckpointStore(store, Microsoft.Extensions.Options.Options.Create(new Hexalith.EventStore.DomainService.EventStoreProcessOptions()), TimeProvider.System);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                while ((await checkpoints.ReadCandidatePageAsync(tenant, 0, deadline.Token).ConfigureAwait(false))?.Entries.Count != 11)
                    await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                foreach (ProcessSource source in sources)
                    while ((await coordinator.GetCheckpointAsync(scope, source.Aggregate, deadline.Token).ConfigureAwait(false))?.OwnerSequenceNumber != (source.Aggregate == owner ? 3 : 1))
                        await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
            object proof = await WorksPlatformProjectionChecks.VerifyAsync(app, gateway, store, tenant, owner, identity, token,
                initialConvergencePhase: "platform-focused-initial-projection-convergence", expectedSources: sources.Length).ConfigureAwait(false);
            if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } path)
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new { phase = "platform-focused-projection-rebuild", state = proof }) + Environment.NewLine, token).ConfigureAwait(false);
        }, builder =>
        {
            WorksSdkRecoveryPipelineSmokeTests.Configure(builder, key, digestKey, isolation.ComponentPath, isolation.RedisEndpoint, sources, identity: identity);
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "works"))
                .WithEnvironment("WORKS_SDK_PROOF_CALLBACKS", "1");
        }, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);
    }
}
