using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Client.Processes;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Starts the exact archived topology before the expensive complete-fleet rollback proof.</summary>
public sealed class WorksPlatformRetainedHostPreflightTests
{
    /// <summary>The historical topology must resolve all paths and reach real SDK readiness with run-owned providers.</summary>
    [Fact(Explicit = true)]
    public async Task ArchivedTopologyReachesHealthyReadiness()
    {
        if (!WorksAppHostSelection.IsPlatform)
            throw new InvalidOperationException("Retained topology preflight requires the canonical Platform verifier selection.");
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        using RSA rsa = RSA.Create(2048);
        string key = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await isolation.CaptureAuthorityAsync(key, digestKey, ct).ConfigureAwait(true);
        await isolation.CaptureDevelopmentCatalogProfileAsync(digestKey, ct, enableQuiescence: true).ConfigureAwait(true);
        string fenceEvidence = Path.Combine(Path.GetDirectoryName(isolation.ComponentPath)!, "owned-fence.json");
        long oldEpoch = 0;
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, _gateway, _sidecar, token) =>
        {
            using HttpClient works = app.CreateHttpClient("works");
            JsonElement readiness = await works.GetFromJsonAsync<JsonElement>("/ready", token).ConfigureAwait(false);
            readiness.GetProperty("status").GetString().ShouldBe("Healthy");
            app.ResourceNotifications.TryGetCurrentState("works", out var current).ShouldBeTrue();
            string inventoryPath = current!.Snapshot.EnvironmentVariables.Single(variable => variable.Name == WorksProofRuntimeInventoryService.PathKey).Value!;
            using var observed = CancellationTokenSource.CreateLinkedTokenSource(token); observed.CancelAfter(TimeSpan.FromSeconds(5));
            while (!File.Exists(inventoryPath)) await Task.Delay(25, observed.Token).ConfigureAwait(false);
            var inventory = JsonSerializer.Deserialize<WorksProofRuntimeInventory>(await WorksProofCallerProtocol.ReadAsync(inventoryPath, observed.Token).ConfigureAwait(false))!;
            inventory.HostId.ShouldBe("works"); inventory.Generation.Serving.ShouldBeTrue();
            inventory.Generation.Profile.ShouldBe("Development"); inventory.Generation.RequiredHosts.Count.ShouldBe(4);
            inventory.Generation.RequiredHosts.Select(host => (host.HostId, host.AppId)).OrderBy(pair => pair.HostId)
                .ShouldBe(new[] { "eventstore", "eventstore-admin", "eventstore-operations", "works" }.Select(name => (name, name)));
            current.Snapshot.EnvironmentVariables.Single(variable => variable.Name == WorksTrustedEffectProofProtocol.DirectoryKey).Value.ShouldNotBeNullOrWhiteSpace();
            oldEpoch = inventory.Generation.Epoch;
            using var fenceBudget = CancellationTokenSource.CreateLinkedTokenSource(token); fenceBudget.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(fenceEvidence)) await Task.Delay(25, fenceBudget.Token).ConfigureAwait(false);
            using var fence = JsonDocument.Parse(await WorksProofCallerProtocol.ReadAsync(fenceEvidence, fenceBudget.Token).ConfigureAwait(false));
            fence.RootElement.GetProperty("actualRemoteStatus").GetInt32().ShouldBe(200);
            fence.RootElement.GetProperty("responseLost").GetBoolean().ShouldBeTrue();
        }, builder =>
        {
            WorksSdkRecoveryPipelineSmokeTests.Configure(builder, key, digestKey, isolation.ComponentPath, isolation.RedisEndpoint,
                [new ProcessSource("tenant-retained-preflight", "work", "item-retained-preflight")]);
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(project => project.Name == "eventstore"))
                .WithEnvironment(WorksRetainedFencePreflightService.PathKey, fenceEvidence);
        }, "WorksSdkReminderActor",
            schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory,
            useRetainedHost: true).ConfigureAwait(true);
        // The full replay previously reached these capture paths only after seventeen live
        // phases. Exercise the same actual selected filesystem/custody paths cheaply here.
        var owner = await WorksFleetQuiescence.ReadOwnerAsync(Path.Combine(Path.GetDirectoryName(isolation.ComponentPath)!, WorksFleetQuiescence.OwnerName), ct).ConfigureAwait(true);
        using (var stoppedRedis = await WorksFleetQuiescence.ConnectAsync(owner, ct).ConfigureAwait(true))
        {
            var profile = JsonSerializer.Deserialize<Hexalith.Platform.Composition.WorksDevelopmentCatalogProfile>(await WorksProofCallerProtocol.ReadAsync(isolation.DevelopmentCatalogProfilePath, ct).ConfigureAwait(true))!;
            var stopped = await WorksFleetQuiescence.ReadStateAsync(stoppedRedis, profile, ct).ConfigureAwait(true);
            stopped.Leases.ShouldHaveSingleItem().HostId.ShouldBe("eventstore"); stopped.Epoch.ShouldBe(oldEpoch);
            await EvidenceAsync("retained-stopped-owned-fence", new { stopped.Epoch, root = stopped.Committed!.RootDigest,
                leaseCount = stopped.Leases.Count, sender = stopped.Leases[0].HostId }, ct).ConfigureAwait(true);
        }
        IReadOnlyDictionary<string, string> beforeBinaries = WorksPlatformRollbackChecks.RuntimeBinaryHashes();
        using (WorksPlatformBinarySnapshot binaries = WorksPlatformBinarySnapshot.Capture(beforeBinaries)) binaries.VerifyCopies();
        IReadOnlyDictionary<string, string> capturedFleet = await isolation.CaptureFleetAsync(ct).ConfigureAwait(true);
        await EvidenceAsync("retained-complete-fleet-captured", new { files = capturedFleet.Count, runtimeFiles = beforeBinaries.Count }, ct).ConfigureAwait(true);
        await isolation.RestoreFleetAsync(ct).ConfigureAwait(true);
        IReadOnlyDictionary<string, string> observedFleet = await isolation.ObserveFleetAsync(ct).ConfigureAwait(true);
        observedFleet.OrderBy(pair => pair.Key).ShouldBe(capturedFleet.Where(pair => pair.Key != WorksSdkProofRedis.DumpInventoryKey).OrderBy(pair => pair.Key));
        var restoredAuthority = await isolation.ReadRestoredAuthorityAsync(ct).ConfigureAwait(true);
        restoredAuthority.SigningKey.ShouldBe(key); restoredAuthority.DigestKey.ShouldBe(digestKey);
        (await isolation.VerifyRestoredConfigurationAsync(ct).ConfigureAwait(true)).ShouldNotBeEmpty();
        WorksPlatformRollbackChecks.RuntimeBinaryHashes().OrderBy(pair => pair.Key).ShouldBe(beforeBinaries.OrderBy(pair => pair.Key));
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, _gateway, _sidecar, token) =>
        {
            using HttpClient works = app.CreateHttpClient("works");
            JsonElement readiness = await works.GetFromJsonAsync<JsonElement>("/ready", token).ConfigureAwait(false);
            readiness.GetProperty("status").GetString().ShouldBe("Healthy");
            using var restoredRedis = await WorksFleetQuiescence.ConnectAsync(owner, token).ConfigureAwait(false);
            var profile = JsonSerializer.Deserialize<Hexalith.Platform.Composition.WorksDevelopmentCatalogProfile>(await WorksProofCallerProtocol.ReadAsync(isolation.DevelopmentCatalogProfilePath, token).ConfigureAwait(false))!;
            var actual = await WorksFleetQuiescence.ReadStateAsync(restoredRedis, profile, token).ConfigureAwait(false);
            actual.Pending.ShouldBeNull(); actual.Leases.ShouldBeEmpty(); actual.Epoch.ShouldBe(oldEpoch + 1);
            actual.Ready.ShouldBeEmpty(); actual.Committed!.RequiredHosts.Count.ShouldBe(4);
            await EvidenceAsync("retained-successor-committed", new { actual.Epoch, root = actual.Committed.RootDigest,
                predecessor = actual.Previous!.RootDigest, leaseCount = actual.Leases.Count, hostCount = actual.Committed.RequiredHosts.Count }, token).ConfigureAwait(false);
            (await isolation.VerifyRestoredConfigurationAsync(token, sdkRunning: true).ConfigureAwait(false)).ShouldNotBeEmpty();
            WorksPlatformRollbackChecks.RuntimeBinaryHashes().OrderBy(pair => pair.Key).ShouldBe(beforeBinaries.OrderBy(pair => pair.Key));
        }, builder => WorksSdkRecoveryPipelineSmokeTests.Configure(builder, restoredAuthority.SigningKey, restoredAuthority.DigestKey,
            isolation.ComponentPath, isolation.RedisEndpoint, [new ProcessSource("tenant-retained-preflight", "work", "item-retained-preflight")]), "WorksSdkReminderActor",
            schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory,
            useRetainedHost: true).ConfigureAwait(true);
        await isolation.VerifySuccessorCustodyAsync(ct).ConfigureAwait(true);
        await EvidenceAsync("retained-successor-custody-verified", new { oldEpoch, newEpoch = oldEpoch + 1, runtimeFiles = beforeBinaries.Count }, ct).ConfigureAwait(true);
    }
    private static async Task EvidenceAsync(string phase, object state, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } path)
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new { phase, observedAt = DateTimeOffset.UtcNow, state }) + "\n", token).ConfigureAwait(false);
    }
}
