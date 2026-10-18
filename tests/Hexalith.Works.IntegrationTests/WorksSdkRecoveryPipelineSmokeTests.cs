using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Dapr.Client;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Identity;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Real Redis/Dapr Works SDK recovery with an explicitly synthetic asymmetric authority.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksSdkRecoveryPipelineSmokeTests
{
    /// <summary>A store that ignores cancellation cannot hold the retained-plan helper after its linked deadline is cancelled.</summary>
    [Fact]
    public async Task RetainedPlanWaitBoundsNonCooperativeStoreAcquisition()
    {
        var pending = new TaskCompletionSource<ReadModelEntry<ProcessSourceState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = Substitute.For<IReadModelStore>();
        CancellationToken acquisitionToken = default;
        store.GetAsync<ProcessSourceState>("statestore", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            acquisitionToken = call.Arg<CancellationToken>();
            entered.SetResult();
            return pending.Task;
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task waiting = WaitForRetainedPlanAsync(store, new ProcessSource("tenant-415", "work", "retained-plan"), 1, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        acquisitionToken.ShouldNotBe(cancellation.Token);
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(true);
        acquisitionToken.IsCancellationRequested.ShouldBeTrue();
        pending.Task.IsCompleted.ShouldBeFalse();
    }

    /// <summary>The recovery retry wrapper preserves coordinated projection writes and idempotent batch replay.</summary>
    [Fact]
    public async Task RetryStorePreservesCoordinatedBatchCapability()
    {
        var persisted = new Hexalith.EventStore.Testing.Fakes.InMemoryReadModelStore();
        IReadModelStore store = new OpenCircuitRetryStore(persisted);
        var batch = new ReadModelBatch(new ReadModelBatchScope("statestore", "tenant-415", "work", "batch-item", "proof", "batch-415"),
            [ReadModelBatchOperation.Write("detail", new Dictionary<string, string> { ["value"] = "committed" }, ReadModelBatchConcurrency.CreateOnly),
             ReadModelBatchOperation.Write("index", new Dictionary<string, string> { ["item"] = "batch-item" }, ReadModelBatchConcurrency.CreateOnly)]);
        IReadModelBatchStore batches = store.ShouldBeAssignableTo<IReadModelBatchStore>();
        (await batches.ExecuteAsync(batch, TestContext.Current.CancellationToken).ConfigureAwait(true)).Status.ShouldBe(ReadModelBatchStatus.Completed);
        (await batches.ExecuteAsync(batch, TestContext.Current.CancellationToken).ConfigureAwait(true)).Status.ShouldBe(ReadModelBatchStatus.AlreadyCompleted);
        persisted.Snapshot<Dictionary<string, string>>("statestore", "detail").ShouldNotBeNull()["value"].ShouldBe("committed");
        persisted.Snapshot<Dictionary<string, string>>("statestore", "index").ShouldNotBeNull()["item"].ShouldBe("batch-item");
    }

    /// <summary>Real protected event callbacks persist reminder witnesses and process membership with both periodic writers disabled.</summary>
    [Fact]
    public async Task SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        if (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true) is { } gap)
        {
            Assert.Skip("SDK Redis/Dapr lane cannot run: " + gap);
            return;
        }
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: WorksAppHostSelection.IsPlatform).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        string suffix = Guid.NewGuid().ToString("N")[..10];
        string tenant = "sdk415-callback-" + suffix;
        string item = "work-sdk415-callback-" + suffix;
        using RSA rsa = RSA.Create(2048);
        string key = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        WorksPlatformTelemetryCollector telemetry = WorksAppHostSelection.IsPlatform
            ? await WorksPlatformTelemetryCollector.StartAsync(ct).ConfigureAwait(true) : new WorksPlatformTelemetryCollector();
        await using var telemetryCleanup = telemetry.ConfigureAwait(false);
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            if (WorksAppHostSelection.IsPlatform)
            {
                using HttpClient worksReadiness = app.CreateHttpClient("works");
                JsonElement health = await worksReadiness.GetFromJsonAsync<JsonElement>("/ready", token).ConfigureAwait(false);
                health.GetProperty("status").GetString().ShouldBe("Degraded", "Disabled periodic recovery must remain readiness-degraded.");
                await EvidenceAsync("periodic-writers-disabled-readiness-degraded", health, token).ConfigureAwait(false);
            }
            using DaprClient dapr = await DaprAsync(app, token, allowDegraded: WorksAppHostSelection.IsPlatform).ConfigureAwait(false);
            var store = new OpenCircuitRetryStore(new DaprReadModelStore(dapr));
            using DaprClient gatewayDapr = await DaprAsync(app, token, "eventstore").ConfigureAwait(false);
            await gatewayDapr.SaveStateAsync("statestore", "projection-delivery-writer-protocol",
                new { schemaVersion = 1, writerProtocolVersion = 2, cutoverCommit = "story415-isolated-callback-fixture", activatedAt = DateTimeOffset.UtcNow },
                cancellationToken: token).ConfigureAwait(false);
            DateTimeOffset due = DateTimeOffset.UtcNow.AddHours(1);
            var expiry = new WorkItemExpirySchedule(due, ReminderIdentityCodec.ComputeScheduleToken(tenant, item, due, 1), 1);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, item, nameof(CreateWorkItem),
                new CreateWorkItem(new TenantId(tenant), new WorkItemId(item), "Callback witness", Expiry: expiry), token).ConfigureAwait(false);
            string keyName = "eventstore:reminders:v1:WorksSdkReminderActor:item:" + ReminderIdentityCodec.ComputeActorId(tenant, item);
            JsonObject? witness = null;
            await WaitAsync(async () =>
            {
                witness = (await store.GetAsync<JsonObject>("statestore", keyName, token).ConfigureAwait(false)).Value;
                return witness?["entries"]?.AsArray().Count == 1;
            }, token).ConfigureAwait(false);
            var source = new ProcessSource(tenant, "work", item);
            var checkpoints = new ProcessCheckpointStore(store, Microsoft.Extensions.Options.Options.Create(new EventStoreProcessOptions()), TimeProvider.System);
            (await checkpoints.ContainsCandidateAsync(source, token).ConfigureAwait(false)).ShouldBeTrue();
            (await checkpoints.GetAsync(source, token).ConfigureAwait(false)).Checkpoint.ShouldBe(0);
            await EvidenceAsync("successful-callback-no-periodic-recovery", new { source, witness, processCandidate = true }, token).ConfigureAwait(false);
            if (WorksAppHostSelection.IsPlatform)
            {
                await WaitAsync(() => Task.FromResult(telemetry.Points.Any(point => point.Name == "eventstore.domain.recovery.alert" && point.Value == 1)), token).ConfigureAwait(false);
                telemetry.Failures.ShouldBeEmpty();
                await EvidenceAsync("disabled-recovery-otlp-alert-export", telemetry.Points, token).ConfigureAwait(false);
#if WORKS_PLATFORM_PARITY
                // Persist the already published source through the SDK before testing protected HTTP disposition.
                ProcessSourceState owner = (await checkpoints.AcquireAsync(source, token).ConfigureAwait(false)).ShouldNotBeNull();
                await checkpoints.ReleaseAsync(owner, token).ConfigureAwait(false);
                await EvidenceAsync("callback-protected-scoped-disposition-audit-before-mutation",
                    await WorksPlatformOperationsChecks.VerifyAsync(app, store, source, token).ConfigureAwait(false), token).ConfigureAwait(false);
#endif
                await EvidenceAsync("platform-named-conflict-durable-quarantine-with-unchanged-family",
                    await WorksPlatformProjectionChecks.VerifyQuarantineAsync(app, client, store, tenant, item, token).ConfigureAwait(false), token).ConfigureAwait(false);
            }
        }, builder =>
        {
            Configure(builder, key, digestKey, isolation.ComponentPath, isolation.RedisEndpoint, [new ProcessSource(tenant, "work", item)], WorksAppHostSelection.IsPlatform ? telemetry.Endpoint : null);
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "works"))
                .WithEnvironment("WORKS_SDK_PROOF_CALLBACKS", "1")
                .WithEnvironment("EventStore__Reminders__ReconciliationEnabled", "false")
                .WithEnvironment("EventStore__Processes__ReconciliationEnabled", "false");
        }, "WorksSdkReminderActor", expectWorksReadinessDegraded: WorksAppHostSelection.IsPlatform,
            schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);
    }

    /// <summary>Lost wakeups and scheduler firing recover; target-commit response loss replays its receipt across restart.</summary>
    [Fact]
    public async Task SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        if (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true) is { } gap)
        {
            Assert.Skip("SDK Redis/Dapr lane cannot run: " + gap);
            return;
        }
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: WorksAppHostSelection.IsPlatform).ConfigureAwait(true);
        await using System.Runtime.CompilerServices.ConfiguredAsyncDisposable cleanup = isolation.ConfigureAwait(false);
        string suffix = Guid.NewGuid().ToString("N")[..10];
        string tenant = "sdk415-" + suffix;
        string dateItem = "work-sdk415-date-" + suffix;
        string expiryItem = "work-sdk415-expiry-" + suffix;
        string parentId = "work-sdk415-parent-" + suffix;
        string childId = "work-sdk415-child-" + suffix;
        using RSA rsa = RSA.Create(2048);
        string key = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        WorksPlatformTelemetryCollector telemetry = WorksAppHostSelection.IsPlatform
            ? await WorksPlatformTelemetryCollector.StartAsync(ct).ConfigureAwait(true) : new WorksPlatformTelemetryCollector();
        await using var telemetryCleanup = telemetry.ConfigureAwait(false);
        if (WorksAppHostSelection.IsPlatform)
            await isolation.CaptureAuthorityAsync(key, digestKey, ct).ConfigureAwait(true);
        string? committedEffect = null;
        string? resumedEffect = null;
        string resumeParent = "work-sdk415-await-parent-" + suffix;
        string resumeChild = "work-sdk415-completed-child-" + suffix;
        string rescheduled = "work-sdk415-rescheduled-" + suffix;
        ProcessSource[] recoveryGrants = [.. new[] { rescheduled, expiryItem, dateItem, parentId, childId,
            childId + "-1", childId + "-2", childId + "-3", childId + "-4", resumeParent, resumeChild }
            .Select(id => new ProcessSource(tenant, "work", id))];
        Action<IDistributedApplicationTestingBuilder> configure = builder => Configure(builder, key, digestKey, isolation.ComponentPath, isolation.RedisEndpoint, recoveryGrants, WorksAppHostSelection.IsPlatform ? telemetry.Endpoint : null);
        string? expiryCascadeEffect = null;
        WorkItemExpirySchedule? childScheduledExpiry = null;

        // Reuse the existing two expiry sources, preserving the eleven-source bounded fixture profile.
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new OpenCircuitRetryStore(new DaprReadModelStore(dapr));
            using DaprClient gatewayDapr = await DaprAsync(app, token, "eventstore").ConfigureAwait(false);
            await gatewayDapr.SaveStateAsync("statestore", "projection-delivery-writer-protocol",
                new { schemaVersion = 1, writerProtocolVersion = 2, cutoverCommit = "story415-isolated-fixture", activatedAt = DateTimeOffset.UtcNow },
                cancellationToken: token).ConfigureAwait(false);
            var tenantId = new TenantId(tenant);
            var expiryParent = new WorkItemId(rescheduled);
            var expiryChild = new WorkItemId(expiryItem);
            DateTimeOffset oldDue = DateTimeOffset.UtcNow.AddSeconds(12);
            DateTimeOffset currentDue = oldDue.AddSeconds(30);
            WorkItemExpirySchedule Schedule(DateTimeOffset instant, long revision) => new(instant,
                ReminderIdentityCodec.ComputeScheduleToken(tenant, rescheduled, instant, revision), revision);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, rescheduled, nameof(CreateWorkItem),
                new CreateWorkItem(tenantId, expiryParent, "Rescheduled expiry parent", Expiry: Schedule(oldDue, 1)), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, rescheduled, nameof(RescheduleWorkItem),
                new RescheduleWorkItem(tenantId, expiryParent, new WorkItemSchedule(), Expiry: Schedule(currentDue, 2)), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, rescheduled, nameof(SpawnChild),
                new SpawnChild(tenantId, expiryParent, expiryChild, "Cascade expiry child"), token).ConfigureAwait(false);
            DateTimeOffset childDue = currentDue.AddHours(1);
            childScheduledExpiry = new WorkItemExpirySchedule(childDue,
                ReminderIdentityCodec.ComputeScheduleToken(tenant, expiryItem, childDue, 1), 1);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, expiryItem, nameof(CreateWorkItem),
                new CreateWorkItem(tenantId, expiryChild, "Witnessed child", Parent: new ParentWorkItemReference(tenantId, expiryParent), Expiry: childScheduledExpiry), token).ConfigureAwait(false);
            _ = await ReadModelWritePolicy.UpdateAsync<WorksSdkProofFaultState>(store, "statestore", "story415:fail-after-commit:" + tenant,
                _ => new WorksSdkProofFaultState(true) { EffectKind = EffectKindCatalog.CascadeExpire }, maxAttempts: 8, cancellationToken: token).ConfigureAwait(false);
            TimeSpan staleDelay = oldDue.AddSeconds(1) - DateTimeOffset.UtcNow;
            if (staleDelay > TimeSpan.Zero) await Task.Delay(staleDelay, token).ConfigureAwait(false);
            (await CountAsync(client, tenant, rescheduled, nameof(WorkItemExpired), token).ConfigureAwait(false)).ShouldBe(0);
            (await CountAsync(client, tenant, expiryItem, nameof(WorkItemExpired), token).ConfigureAwait(false)).ShouldBe(0);
            await WaitAsync(async () =>
            {
                expiryCascadeEffect = (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + tenant, token)
                    .ConfigureAwait(false)).Value?.CommittedEffectId;
                return expiryCascadeEffect is not null;
            }, token).ConfigureAwait(false);
            expiryCascadeEffect.ShouldBe(EffectIdentityCodec.ComputeEffectId(new EffectIdentity(tenant, "work", rescheduled, 4,
                EffectKindCatalog.CascadeExpire, "work", expiryItem, 0)));
            (await CountAsync(client, tenant, expiryItem, nameof(WorkItemExpired), token).ConfigureAwait(false)).ShouldBe(1);
            await WaitAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
                { Content = JsonContent.Create(new { tenant, domain = "work", aggregateId = expiryItem, fromSequence = 0L, pageSize = 100 }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(tenant));
                using HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
                if ((int)response.StatusCode == 503) return false;
                response.EnsureSuccessStatusCode();
                JsonElement stream = await response.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
                var facts = stream.GetProperty("events").EnumerateArray().ToArray();
                var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var created = JsonSerializer.Deserialize<WorkItemCreated>(facts.Single(fact => fact.GetProperty("eventTypeName").GetString()!.EndsWith(nameof(WorkItemCreated), StringComparison.Ordinal))
                    .GetProperty("payload").GetBytesFromBase64(), json)!;
                created.Expiry.ShouldBe(childScheduledExpiry);
                var expired = JsonSerializer.Deserialize<WorkItemExpired>(facts.Single(fact => fact.GetProperty("eventTypeName").GetString()!.EndsWith(nameof(WorkItemExpired), StringComparison.Ordinal))
                    .GetProperty("payload").GetBytesFromBase64(), json)!;
                expired.ExpiryWitness.ShouldBeNull();
                return true;
            }, token).ConfigureAwait(false);
            await WaitAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
                { Content = JsonContent.Create(new { tenant, domain = "work", aggregateId = rescheduled, fromSequence = 0L, pageSize = 100 }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(tenant));
                using HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
                if ((int)response.StatusCode == 503) return false;
                response.EnsureSuccessStatusCode();
                JsonElement stream = await response.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
                var expired = JsonSerializer.Deserialize<WorkItemExpired>(stream.GetProperty("events").EnumerateArray()
                    .Single(fact => fact.GetProperty("eventTypeName").GetString()!.EndsWith(nameof(WorkItemExpired), StringComparison.Ordinal))
                    .GetProperty("payload").GetBytesFromBase64(), JsonSerializerOptions.Web)!;
                expired.ExpiryWitness.ShouldBe(Schedule(currentDue, 2));
                await EvidenceAsync("rescheduled-expiry-committed-current-witness", expired, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
            var checkpoint = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(new ProcessSource(tenant, "work", rescheduled)), token).ConfigureAwait(false)).Value!;
            checkpoint.Checkpoint.ShouldBe(3);
            var effect = checkpoint.PendingPlan!.Effects.ShouldHaveSingleItem();
            effect.Kind.ShouldBe(EffectKindCatalog.CascadeExpire);
            JsonSerializer.Deserialize<ExpireWorkItem>(effect.CommandPayload)!.ExpiryWitness.ShouldBeNull();
            await EvidenceAsync("cascade-expiry-target-commit-before-checkpoint", new { expiryCascadeEffect, childScheduledExpiry, checkpoint, childExpired = 1 }, token).ConfigureAwait(false);
        }, configure, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new OpenCircuitRetryStore(new DaprReadModelStore(dapr));
            await WaitAsync(async () => (await store.GetAsync<ProcessSourceState>("statestore",
                ProcessStateKeys.Source(new ProcessSource(tenant, "work", rescheduled)), token).ConfigureAwait(false)).Value?.Checkpoint == 4,
                token).ConfigureAwait(false);
            WorksSdkProofFaultState replay = (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + tenant, token).ConfigureAwait(false)).Value!;
            replay.ReplayObserved.ShouldBeTrue();
            (await CountAsync(client, tenant, expiryItem, nameof(WorkItemExpired), token).ConfigureAwait(false)).ShouldBe(1);
            ResourceEvent gateway = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore", token, token).ConfigureAwait(false);
            using HttpClient actorState = WorksAppHostTestReadiness.CreateDaprClient(gateway, "EventStore");
            string expiryActor = new AggregateIdentity(tenant, "work", expiryItem).ActorId;
            using HttpResponseMessage persisted = await actorState.GetAsync($"v1.0/actors/AggregateActor/{Uri.EscapeDataString(expiryActor)}/state/effect_receipt_{expiryCascadeEffect}", token).ConfigureAwait(false);
            persisted.EnsureSuccessStatusCode();
            JsonElement expiryReceipt = await persisted.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
            expiryReceipt.GetProperty("purpose").GetString().ShouldBe("works.cascade-expire");
            expiryReceipt.GetProperty("disposition").GetInt32().ShouldBe((int)TrustedEffectDisposition.Success);
            var expiryCheckpoint = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(new ProcessSource(tenant, "work", rescheduled)), token).ConfigureAwait(false)).Value!;
            expiryCheckpoint.PendingPlan.ShouldBeNull();
            await EvidenceAsync("cascade-expiry-restart-receipt-and-checkpoint", new { expiryCascadeEffect, childScheduledExpiry, expiryReceipt, replay, expiryCheckpoint, childExpired = 1 }, token).ConfigureAwait(false);
            var tenantId = new TenantId(tenant);

            // Leave time for four committed commands and periodic discovery before the witness can fire.
            DateTimeOffset dateDue = DateTimeOffset.UtcNow.AddSeconds(90);
            await WorksAppHostSmokeHarness.ParkSuspendedOnDateAsync(client, tenant, dateItem, dateDue, token).ConfigureAwait(false);
            string actor = ReminderIdentityCodec.ComputeActorId(tenant, dateItem);
            string stateKey = "eventstore:reminders:v1:WorksSdkReminderActor:item:" + actor;
            JsonObject? witness = null;
            try
            {
                await WaitAsync(async () =>
                {
                    witness = (await store.GetAsync<JsonObject>("statestore", stateKey, token).ConfigureAwait(false)).Value;
                    return witness?["entries"]?.AsArray().Count > 0;
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                await EvidenceAsync("date-registration-timeout", new { dateDue, witness,
                    resumed = await CountAsync(client, tenant, dateItem, nameof(WorkItemResumed), token).ConfigureAwait(false) }, token).ConfigureAwait(false);
                await WorksSdkProofDiagnostics.CaptureAsync(app, token).ConfigureAwait(false);
                throw;
            }
            dateDue.ShouldBeGreaterThan(DateTimeOffset.UtcNow, "the future reminder must be deleted before it can fire");
            string name = witness!["entries"]![0]!["reminderName"]!.GetValue<string>();
            using (HttpResponseMessage deleted = await sidecar.DeleteAsync(
                $"v1.0/actors/WorksSdkReminderActor/{actor}/reminders/{name}", token).ConfigureAwait(false))
                deleted.EnsureSuccessStatusCode();
            if (WorksAppHostSelection.IsPlatform)
                await EvidenceAsync("concurrent-sdk-reminder-reissue-after-lost-scheduler-wakeup",
                    await WorksPlatformRecoveryChecks.ReissueConcurrentlyAsync(app, sidecar, tenant, dateItem, name, token).ConfigureAwait(false), token).ConfigureAwait(false);
            // The bounded state poll treats SDK not-ready/unavailable reads as unresolved until a complete stream is available.
            await WaitAsync(async () => await CountAsync(client, tenant, dateItem, nameof(WorkItemResumed), token).ConfigureAwait(false) == 1,
                token).ConfigureAwait(false);

            var parent = new WorkItemId(parentId);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, parentId, nameof(CreateWorkItem),
                new CreateWorkItem(tenantId, parent, "Synthetic cascade parent"), token).ConfigureAwait(false);
            for (int ordinal = 0; ordinal < 5; ordinal++)
            {
                var target = new WorkItemId(ordinal == 0 ? childId : childId + "-" + ordinal);
                await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, parentId, nameof(SpawnChild),
                    new SpawnChild(tenantId, parent, target, "Synthetic five-child cascade"), token).ConfigureAwait(false);
                if (ordinal != 0)
                    await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, target.Value, nameof(CreateWorkItem),
                        new CreateWorkItem(tenantId, target, "Synthetic five-child cascade", Parent: new ParentWorkItemReference(tenantId, parent)), token).ConfigureAwait(false);
            }
            _ = await ReadModelWritePolicy.UpdateAsync<WorksSdkProofFaultState>(store, "statestore", "story415:fail-after-commit:" + tenant,
                _ => new WorksSdkProofFaultState(true), maxAttempts: 8, cancellationToken: token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, parentId, nameof(CancelWorkItem),
                new CancelWorkItem(tenantId, parent), token).ConfigureAwait(false);
            ProcessSourceState? creationGap = null;
            try
            {
                await WaitAsync(async () =>
                {
                    creationGap = (await store.GetAsync<ProcessSourceState>("statestore",
                        ProcessStateKeys.Source(new ProcessSource(tenant, "work", parentId)), token).ConfigureAwait(false)).Value;
                    return creationGap?.ReasonCode == "evidence-unavailable";
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
            {
                await EvidenceAsync("canonical-related-child-creation-gap-timeout", creationGap!, token).ConfigureAwait(false);
                throw new TimeoutException($"Delayed-child evidence was not observed. Checkpoint={creationGap?.Checkpoint} Status={creationGap?.Status} Reason={creationGap?.ReasonCode} FailureSequence={creationGap?.FailureSequence}.", exception);
            }
            creationGap!.Checkpoint.ShouldBe(6);
            creationGap.FailureSequence.ShouldBe(7);
            creationGap.PendingPlan.ShouldBeNull();
            for (int ordinal = 1; ordinal < 5; ordinal++)
                (await CountAsync(client, tenant, childId + "-" + ordinal, nameof(WorkItemCancelled), token).ConfigureAwait(false)).ShouldBe(0);
            var gapOptions = Microsoft.Extensions.Options.Options.Create(new EventStoreProcessOptions { SupportedCandidateCount = 11 });
            ProcessSweepState? gapSweep = null;
            await WaitAsync(async () =>
            {
                gapSweep = await new ProcessSweepStore(store, gapOptions, TimeProvider.System).GetAsync(token).ConfigureAwait(false);
                return gapSweep.Unresolved > 0 && gapSweep.Reasons.ContainsKey("evidence-unavailable");
            }, token).ConfigureAwait(false);
            var gapHealth = await new ProcessReadinessHealthCheck(new ProcessSweepStore(store, gapOptions, TimeProvider.System), gapOptions, TimeProvider.System)
                .CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext(), token).ConfigureAwait(false);
            gapHealth.Status.ShouldBe(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
            if (WorksAppHostSelection.IsPlatform)
                await WaitAsync(() => Task.FromResult(telemetry.Points.Any(point => point.Name == "eventstore.domain.recovery.alert" && point.Value == 1)), token).ConfigureAwait(false);
            await EvidenceAsync("canonical-related-child-creation-gap", new { creationGap, gapSweep, readiness = gapHealth.Status.ToString(), existingChildCancellations = 0 }, token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, childId, nameof(CreateWorkItem),
                new CreateWorkItem(tenantId, new WorkItemId(childId), "Synthetic delayed child creation", Parent: new ParentWorkItemReference(tenantId, parent)), token).ConfigureAwait(false);
            await WaitAsync(async () =>
            {
                committedEffect = (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + tenant, token)
                    .ConfigureAwait(false)).Value?.CommittedEffectId;
                return committedEffect is not null;
            }, token).ConfigureAwait(false);
            (await CountAsync(client, tenant, childId, nameof(WorkItemCancelled), token).ConfigureAwait(false)).ShouldBe(1);
            ProcessSourceState checkpoint = (await store.GetAsync<ProcessSourceState>("statestore",
                ProcessStateKeys.Source(new ProcessSource(tenant, "work", parentId)), token).ConfigureAwait(false)).Value!;
            checkpoint.Checkpoint.ShouldBeLessThan(7);
            checkpoint.PendingPlan!.Effects.Count.ShouldBe(5);
            await EvidenceAsync("cascade-target-commit-before-checkpoint", new { committedEffect, checkpoint, childCancelled = 1 }, token).ConfigureAwait(false);
        }, configure, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new OpenCircuitRetryStore(new DaprReadModelStore(dapr));
            await WaitAsync(async () => (await store.GetAsync<ProcessSourceState>("statestore",
                ProcessStateKeys.Source(new ProcessSource(tenant, "work", parentId)), token).ConfigureAwait(false)).Value?.Checkpoint >= 7,
                token).ConfigureAwait(false);
            for (int ordinal = 0; ordinal < 5; ordinal++)
            {
                string target = ordinal == 0 ? childId : childId + "-" + ordinal;
                (await CountAsync(client, tenant, target, nameof(WorkItemCancelled), token).ConfigureAwait(false)).ShouldBe(1);
            }
            (await CountAsync(client, tenant, expiryItem, nameof(WorkItemExpired), token).ConfigureAwait(false)).ShouldBe(1);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, dateItem, token).ConfigureAwait(false)).ShouldBe(1);
            ResourceEvent gateway = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore", token, token).ConfigureAwait(false);
            using HttpClient actorState = WorksAppHostTestReadiness.CreateDaprClient(gateway, "EventStore");
            string actorId = new AggregateIdentity(tenant, "work", childId).ActorId;
            using HttpResponseMessage receipt = await actorState.GetAsync(
                $"v1.0/actors/AggregateActor/{Uri.EscapeDataString(actorId)}/state/effect_receipt_{committedEffect}", token).ConfigureAwait(false);
            receipt.EnsureSuccessStatusCode();
            string receiptBody = await receipt.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            receiptBody.ShouldContain(committedEffect!);
            var cascadeReceipts = new List<object>();
            for (int ordinal = 0; ordinal < 5; ordinal++)
            {
                string target = ordinal == 0 ? childId : childId + "-" + ordinal;
                string effectId = EffectIdentityCodec.ComputeEffectId(new EffectIdentity(tenant, "work", parentId,
                    7, EffectKindCatalog.CascadeCancel, "work", target, 0));
                string targetActor = new AggregateIdentity(tenant, "work", target).ActorId;
                using HttpResponseMessage persistedReceipt = await actorState.GetAsync(
                    $"v1.0/actors/AggregateActor/{Uri.EscapeDataString(targetActor)}/state/effect_receipt_{effectId}", token).ConfigureAwait(false);
                persistedReceipt.EnsureSuccessStatusCode();
                string persistedBody = await persistedReceipt.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                persistedBody.ShouldContain(effectId);
                cascadeReceipts.Add(new { target, effectId, receipt = JsonSerializer.Deserialize<JsonElement>(persistedBody), cancelled = 1 });
            }
            await EvidenceAsync("five-child-persisted-target-receipts", cascadeReceipts, token).ConfigureAwait(false);
            await WaitAsync(async () => await CountAsync(client, tenant, rescheduled, nameof(WorkItemExpired), token).ConfigureAwait(false) == 1, token).ConfigureAwait(false);
            await EvidenceAsync("cascade-restart-receipt-and-checkpoint", new { committedEffect, receipt = JsonSerializer.Deserialize<JsonElement>(receiptBody),
                checkpoint = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(new ProcessSource(tenant, "work", parentId)), token).ConfigureAwait(false)).Value }, token).ConfigureAwait(false);

            var tenantId = new TenantId(tenant);
            var parent = new WorkItemId(resumeParent);
            var child = new WorkItemId(resumeChild);
            var binding = new ExecutorBinding(new PartyId("synthetic-worker"), Channel.Cli, AuthorityLevel.Contribute);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, resumeParent, nameof(CreateWorkItem), new CreateWorkItem(tenantId, parent, "Awaiting parent"), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, resumeParent, nameof(SpawnChild), new SpawnChild(tenantId, parent, child, "Completing child"), token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, resumeChild, nameof(CreateWorkItem), new CreateWorkItem(tenantId, child, "Completing child", Parent: new ParentWorkItemReference(tenantId, parent)), token).ConfigureAwait(false);
            foreach ((string item, WorkItemId id) in new[] { (resumeParent, parent), (resumeChild, child) })
            {
                await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, item, nameof(AssignWorkItem), new AssignWorkItem(tenantId, id, binding), token).ConfigureAwait(false);
                await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, item, nameof(ClaimWorkItem), new ClaimWorkItem(tenantId, id, binding), token).ConfigureAwait(false);
            }
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, resumeParent, nameof(SuspendWorkItem), new SuspendWorkItem(tenantId, parent, [AwaitCondition.ChildCompleted(child)]), token).ConfigureAwait(false);
            _ = await ReadModelWritePolicy.UpdateAsync<WorksSdkProofFaultState>(store, "statestore", "story415:fail-after-commit:" + tenant,
                _ => new WorksSdkProofFaultState(false) { HoldDelegation = true }, maxAttempts: 8, cancellationToken: token).ConfigureAwait(false);
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, resumeChild, nameof(CompleteWorkItem), new CompleteWorkItem(tenantId, child), token).ConfigureAwait(false);
            // The completion is discovered from projection membership on a later sweep cycle. A busy
            // proof host can spend the ordinary two-minute poll inside earlier sources, so this wait
            // allows one extra cycle and fails immediately once the completion is checkpointed without a plan.
            await WaitForRetainedPlanAsync(store, new ProcessSource(tenant, "work", resumeChild), expectedSequence: 4, token).ConfigureAwait(false);
            // Consume the await through an ordinary command after the SDK retained its exact plan.
            // The trusted effect must then commit a no-op receipt without another domain event.
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(client, tenant, resumeParent, nameof(ResumeWorkItem),
                new ResumeWorkItem(tenantId, parent, AwaitCondition.ChildCompleted(child)), token).ConfigureAwait(false);
            _ = await ReadModelWritePolicy.UpdateAsync<WorksSdkProofFaultState>(store, "statestore", "story415:fail-after-commit:" + tenant,
                _ => new WorksSdkProofFaultState(true), maxAttempts: 8, cancellationToken: token).ConfigureAwait(false);
            await WaitAsync(async () =>
            {
                resumedEffect = (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + tenant, token).ConfigureAwait(false)).Value?.CommittedEffectId;
                return resumedEffect is not null;
            }, token).ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, resumeParent, token).ConfigureAwait(false)).ShouldBe(1);
            ProcessSourceState beforeResumeCheckpoint = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(new ProcessSource(tenant, "work", resumeChild)), token).ConfigureAwait(false)).Value!;
            beforeResumeCheckpoint.Checkpoint.ShouldBeLessThan(4);
            beforeResumeCheckpoint.PendingPlan!.Effects.Count.ShouldBe(1);
            var lostNoOp = (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + tenant, token).ConfigureAwait(false)).Value!;
            lostNoOp.Disposition.ShouldBe(TrustedEffectDisposition.NoOp);
            await EvidenceAsync("child-resume-noop-receipt-before-checkpoint", new { resumedEffect, checkpoint = beforeResumeCheckpoint, lostNoOp, parentResumed = 1 }, token).ConfigureAwait(false);
            if (WorksAppHostSelection.IsPlatform)
                await EvidenceAsync("paged-process-candidates-pre-restart",
                    await WorksPlatformRecoveryChecks.RepackCandidatePagesAsync(store, tenant, recoveryGrants, token).ConfigureAwait(false), token).ConfigureAwait(false);
        }, configure, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new OpenCircuitRetryStore(new DaprReadModelStore(dapr));
            var source = new ProcessSource(tenant, "work", resumeChild);
            await WaitAsync(async () => (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), token).ConfigureAwait(false)).Value?.Checkpoint >= 4, token).ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, resumeParent, token).ConfigureAwait(false)).ShouldBe(1);
            ResourceEvent gateway = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore", token, token).ConfigureAwait(false);
            using HttpClient actorState = WorksAppHostTestReadiness.CreateDaprClient(gateway, "EventStore");
            using HttpResponseMessage receipt = await actorState.GetAsync($"v1.0/actors/AggregateActor/{Uri.EscapeDataString(new AggregateIdentity(tenant, "work", resumeParent).ActorId)}/state/effect_receipt_{resumedEffect}", token).ConfigureAwait(false);
            receipt.EnsureSuccessStatusCode();
            string body = await receipt.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            body.ShouldContain(resumedEffect!);
            var replay = (await store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + tenant, token).ConfigureAwait(false)).Value!;
            replay.Disposition.ShouldBe(TrustedEffectDisposition.NoOp);
            replay.ReplayObserved.ShouldBeTrue();
            ProcessSweepState? completedSweep = null;
            await WaitAsync(async () =>
            {
                completedSweep = (await store.GetAsync<ProcessSweepState>("statestore", ProcessStateKeys.Sweep, token).ConfigureAwait(false)).Value;
                return completedSweep?.Complete == true && completedSweep.Unresolved == 0;
            }, token).ConfigureAwait(false);
            completedSweep!.RetainedCandidates.ShouldBe(11);
            await EvidenceAsync("complete-supported-process-coverage", completedSweep, token).ConfigureAwait(false);
            await EvidenceAsync("child-resume-noop-restart-receipt-and-checkpoint", new { resumedEffect, replay, receipt = JsonSerializer.Deserialize<JsonElement>(body),
                checkpoint = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), token).ConfigureAwait(false)).Value }, token).ConfigureAwait(false);
            if (WorksAppHostSelection.IsPlatform)
            {
#if WORKS_PLATFORM_PARITY
                await EvidenceAsync("paged-process-restart-and-shared-store-owner-fencing",
                    await WorksPlatformRecoveryChecks.VerifyPagesAndOwnershipAsync(store, tenant, recoveryGrants, token).ConfigureAwait(false), token).ConfigureAwait(false);
                await EvidenceAsync("protected-scoped-disposition-audit-before-mutation",
                    await WorksPlatformOperationsChecks.VerifyAsync(app, store, source, token).ConfigureAwait(false), token).ConfigureAwait(false);
                await EvidenceAsync("platform-named-redelivery-capture-through-commit-complete-family",
                    await WorksPlatformProjectionChecks.VerifyAsync(app, client, store, tenant, resumeParent, token).ConfigureAwait(false), token).ConfigureAwait(false);
#endif
                await EvidenceAsync("platform-query-and-explicit-receipt-replay",
                    await WorksPlatformRollbackChecks.VerifyAsync(app, client, rsa, tenant, resumeParent, resumedEffect!, token).ConfigureAwait(false), token).ConfigureAwait(false);
                DateTimeOffset settledAt = DateTimeOffset.UtcNow;
                await WaitAsync(() => Task.FromResult(telemetry!.Points.Any(point => point.Name == "eventstore.domain.recovery.alert" && point.Value == 0 && point.ReceivedAt > settledAt)), token).ConfigureAwait(false);
                telemetry!.Failures.ShouldBeEmpty();
                await EvidenceAsync("durable-recovery-alert-export-and-clear", telemetry.Points, token).ConfigureAwait(false);
            }
        }, configure, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);

        if (WorksAppHostSelection.IsPlatform)
        {
            using WorksPlatformBinarySnapshot binaries = WorksPlatformBinarySnapshot.Capture(WorksPlatformRollbackChecks.RuntimeBinaryHashes());
            IReadOnlyDictionary<string, string> runtimeHashes = binaries.Hashes;
            IReadOnlyDictionary<string, string> fleet = await isolation.CaptureFleetAsync(ct).ConfigureAwait(true);
            await EvidenceAsync("complete-platform-fleet-snapshot", new { fleet, runtimeHashes, schedulerVolume = isolation.SchedulerVolumeName }, ct).ConfigureAwait(true);
            key = string.Empty;
            digestKey = string.Empty;
            await isolation.RestoreFleetAsync(ct).ConfigureAwait(true);
            (key, digestKey) = await isolation.ReadRestoredAuthorityAsync(ct).ConfigureAwait(true);
            rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(key), out _);
            binaries.Restore();
            WorksPlatformRollbackChecks.RuntimeBinaryHashes().OrderBy(pair => pair.Key).ShouldBe(runtimeHashes.OrderBy(pair => pair.Key));
            await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
            {
                WorksPlatformRollbackChecks.RuntimeBinaryHashes().OrderBy(pair => pair.Key).ShouldBe(runtimeHashes.OrderBy(pair => pair.Key));
                await isolation.VerifyRestoredConfigurationAsync(token).ConfigureAwait(false);
                object convergence = await WorksPlatformRollbackChecks.VerifyAsync(app, client, rsa, tenant, resumeParent, resumedEffect!, token, restored: true).ConfigureAwait(false);
                (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, dateItem, token).ConfigureAwait(false)).ShouldBe(1);
                (await CountAsync(client, tenant, childId, nameof(WorkItemCancelled), token).ConfigureAwait(false)).ShouldBe(1);
                (await CountAsync(client, tenant, expiryItem, nameof(WorkItemExpired), token).ConfigureAwait(false)).ShouldBe(1);
                await EvidenceAsync("complete-restore-retained-host-query-and-receipt-replay", new
                {
                    convergence, fleet, runtimeHashes, childCancelled = 1, childExpired = 1,
                    restoredConfiguration = "captured compatible SDK configuration and receiver policy; retained Works AppHost binary",
                }, token).ConfigureAwait(false);
            }, configure, "WorksSdkReminderActor", schedulerVolumeName: isolation.SchedulerVolumeName,
                certificateDirectory: isolation.CertificateDirectory, useRetainedHost: true).ConfigureAwait(true);
        }
    }

    private static async Task EvidenceAsync(string phase, object state, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } path)
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new { phase, state }) + Environment.NewLine, token).ConfigureAwait(false);
    }

    /// <summary>Accepts unchanged policy lines or the exact optional component-watch opt-out without normalizing YAML indentation.</summary>
    /// <param name="committed">The committed Dapr configuration lines.</param>
    /// <param name="copy">The candidate fixture configuration lines.</param>
    /// <returns>Whether the copy preserves every committed line and changes only the permitted feature block.</returns>
    internal static bool IsProtectedDaprConfigCopy(string[] committed, string[] copy)
    {
        if (copy.SequenceEqual(committed, StringComparer.Ordinal)) return true;
        int spec = Array.IndexOf(committed, "spec:");
        if (spec < 0) return false;
        string[] permitted = [.. committed.Take(spec + 1), "  features:", "    - name: HotReload", "      enabled: false", .. committed.Skip(spec + 1)];
        return copy.SequenceEqual(permitted, StringComparer.Ordinal);
    }

    internal static void Configure(IDistributedApplicationTestingBuilder builder, string key, string digestKey, string componentPath, string redisEndpoint, IReadOnlyList<ProcessSource> recoveryGrants, Uri? telemetryEndpoint = null)
    {
        if (WorksAppHostSelection.IsPlatform && Environment.GetEnvironmentVariable("WORKS_PLATFORM_ROOT") is { Length: > 0 } platformRoot
            && Path.GetFullPath(builder.AppHostDirectory) != Path.GetFullPath(platformRoot))
        {
            // A deliberate rollback restores the captured compatible receiver policy beside the retained
            // host binary. It never mutates the historical AppHost or its checked-in policy files.
            foreach (IDaprSidecarResource sidecar in builder.Resources.OfType<ProjectResource>()
                .SelectMany(project => project.Annotations.OfType<DaprSidecarAnnotation>().Select(annotation => annotation.Sidecar)))
            {
                DaprSidecarOptionsAnnotation original = sidecar.Annotations.OfType<DaprSidecarOptionsAnnotation>().Single();
                string restoredPolicy = Path.Combine(platformRoot, "DaprComponents", Path.GetFileName(original.Options.Config!));
                File.Exists(restoredPolicy).ShouldBeTrue();
                sidecar.Annotations.Remove(original);
                sidecar.Annotations.Add(new DaprSidecarOptionsAnnotation(original.Options with { Config = restoredPolicy }));
            }
            if (!builder.Resources.Any(resource => resource.Name == "configstore"))
            {
                IResourceBuilder<IDaprComponentResource> configStore = builder.AddDaprComponent("configstore", "configuration.redis",
                    new DaprComponentOptions { LocalPath = Path.Combine(platformRoot, "DaprComponents", "configstore.yaml") });
                ProjectResource gateway = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "eventstore");
                builder.CreateResourceBuilder(gateway.Annotations.OfType<DaprSidecarAnnotation>().Single().Sidecar).WithReference(configStore);
            }
        }
        if (WorksAppHostSelection.IsPlatform)
        {
            WorksPlatformAppChannelTokens.Configure(builder, componentPath);
            string policyDirectory = Path.Combine(Path.GetDirectoryName(componentPath)!, "receiver-policies");
            Directory.CreateDirectory(policyDirectory);
            foreach (IDaprSidecarResource sidecar in builder.Resources.OfType<ProjectResource>()
                .SelectMany(project => project.Annotations.OfType<DaprSidecarAnnotation>().Select(annotation => annotation.Sidecar)))
            {
                DaprSidecarOptionsAnnotation annotation = sidecar.Annotations.OfType<DaprSidecarOptionsAnnotation>().Single();
                string policy = Path.Combine(policyDirectory, Path.GetFileName(annotation.Options.Config!));
                if (!File.Exists(policy)) File.Copy(annotation.Options.Config!, policy);
                string[] resourcePaths = [.. annotation.Options.ResourcesPaths.Select(original =>
                {
                    string destination = Path.Combine(policyDirectory, Path.GetFileName(original));
                    if (!Directory.Exists(destination))
                    {
                        Directory.CreateDirectory(destination);
                        foreach (string file in Directory.EnumerateFiles(original)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
                    }
                    return destination;
                })];
                sidecar.Annotations.Remove(annotation);
                sidecar.Annotations.Add(new DaprSidecarOptionsAnnotation(annotation.Options with { Config = policy, ResourcesPaths = resourcePaths.ToImmutableHashSet(StringComparer.Ordinal) }));
            }
        }
        // An opt-in process-local configuration copy can disable component file watching when the shared
        // host has exhausted inotify. The fixture never changes its committed ACL, mTLS, or Dapr resources.
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_DAPR_CONFIG_DIRECTORY") is { Length: > 0 } configDirectory)
        {
            configDirectory = Path.GetFullPath(configDirectory);
            IDaprSidecarResource[] sidecars = [.. builder.Resources.OfType<ProjectResource>()
                .SelectMany(project => project.Annotations.OfType<DaprSidecarAnnotation>().Select(annotation => annotation.Sidecar))];
            sidecars.ShouldNotBeEmpty();
            foreach (IDaprSidecarResource sidecar in sidecars)
            {
                sidecar.TryGetLastAnnotation<DaprSidecarOptionsAnnotation>(out var annotation).ShouldBeTrue();
                DaprSidecarOptions options = annotation.ShouldNotBeNull().Options;
                string committed = options.Config.ShouldNotBeNull("The sidecar has no committed Dapr configuration to replace.");
                string config = Path.Combine(configDirectory, Path.GetFileName(committed));
                File.Exists(config).ShouldBeTrue($"Missing Dapr configuration copy '{config}'.");
                // YAML indentation determines whether the ACL remains under spec. Preserve every original
                // line exactly and admit only the correctly indented HotReload block immediately after spec.
                IsProtectedDaprConfigCopy(File.ReadAllLines(committed), File.ReadAllLines(config)).ShouldBeTrue(
                    $"'{config}' differs from the committed '{committed}' beyond the HotReload opt-out.");
                sidecar.Annotations.Remove(annotation);
                sidecar.Annotations.Add(new DaprSidecarOptionsAnnotation(options with { Config = config }));
            }
        }
        foreach (DaprComponentResource component in builder.Resources.OfType<DaprComponentResource>())
        {
            if (component.Options?.LocalPath is not { } original || component.Name is not ("statestore" or "pubsub" or "configstore")) continue;
            string replacement = component.Name == "statestore" ? componentPath : Path.Combine(Path.GetDirectoryName(componentPath)!, component.Name + ".yaml");
            if (component.Name != "statestore")
            {
                CaptureComponentConfiguration(original, replacement, redisEndpoint, WorksAppHostSelection.IsPlatform);
            }
            // Aspire constructs this init-only option before the testing callback; replace its
            // public option on the fixture model before BuildAsync, without editing production files.
            typeof(DaprComponentResource).GetProperty(nameof(DaprComponentResource.Options))!
                .SetValue(component, new DaprComponentOptions { LocalPath = replacement });
        }
        // Runtime-only source executables must identify the same producer version as the
        // published proof consumer; MinVer otherwise derives the preceding repository tag.
        if (WorksAppHostSelection.IsPlatform)
            foreach (ProjectResource sourceProject in builder.Resources.OfType<ProjectResource>())
                builder.CreateResourceBuilder(sourceProject).WithEnvironment("MinVerVersionOverride", WorksSdkProofProducer.Version)
                    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
                    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
        string assembly = typeof(WorksSdkProofHostingStartup).Assembly.Location;
        foreach (ProjectResource resource in builder.Resources.OfType<ProjectResource>().Where(resource => resource.Name is "works" or "works-ha" or "eventstore"))
        {
            IResourceBuilder<ProjectResource> target = builder.CreateResourceBuilder(resource);
            target.WithEnvironment("HexalithEventStoreVersion", WorksSdkProofProducer.Version);
            if (WorksSdkProofProducer.Version.Contains("-story415.", StringComparison.Ordinal))
                target.WithEnvironment("RestoreAdditionalProjectSources", Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../.artifacts/story-4-15/feed")));
            target.WithEnvironment("DOTNET_STARTUP_HOOKS", assembly)
                .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", typeof(WorksSdkProofHostingStartup).Assembly.GetName().Name!)
                .WithEnvironment("WORKS_SDK_PROOF", "1").WithEnvironment("WORKS_SDK_PROOF_ROLE", resource.Name == "eventstore" ? "eventstore" : "works")
                .WithEnvironment("WORKS_SDK_PROOF_KEY", key);
            if (resource.Name != "eventstore")
            {
                if (WorksAppHostSelection.IsPlatform)
                {
                    target.WithEnvironment("WORKS_PLATFORM_PARITY", "1")
                        .WithEnvironment("WORKS_PLATFORM_RECOVERY_GRANTS", JsonSerializer.Serialize(recoveryGrants))

                        .WithEnvironment("EventStore__Processes__DiscoveryPageSize", "3")
                        .WithEnvironment("Works__Query__PermittedCoordinatorWorkloads__0", "eventstore")
                        .WithEnvironment("Authentication__Workload__AllowedCallers__0", "eventstore")
                        .WithEnvironment("Authentication__Workload__AllowedCallers__1", "eventstore-admin");
                    if (telemetryEndpoint is not null)
                        target.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", telemetryEndpoint.AbsoluteUri.TrimEnd('/'))
                            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
                            .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "1000");
                }
                target.WithEnvironment("Works__Recovery__UseSdkRuntime", "true").WithEnvironment("Works__Projection__UseSdkDelivery", "true")
                    .WithEnvironment("EventStore__Reminders__ReconciliationInterval", "00:00:02")
                    .WithEnvironment("EventStore__Processes__ReconciliationInterval", "00:00:10")
                    .WithEnvironment("Works__Recovery__ContextReadTimeout", "00:00:02")
                    .WithEnvironment("EventStore__Processes__RetryLimit", "10")
                    .WithEnvironment("EventStore__Processes__SupportedCandidateCount", "11")

                    .WithEnvironment("EventStore__Processes__OperationTimeout", "00:00:02")
                    .WithEnvironment("EventStore__Processes__LeaseDuration", "00:00:30")
                    .WithEnvironment("EventStore__Processes__SourceServiceQuantum", "00:01:00")
                    .WithEnvironment("EventStore__Processes__ReadinessMaxAge", "00:30:00");
            }
            else
            {
                if (WorksAppHostSelection.IsPlatform)
                    // Eleven sources and internal actor RPCs use authenticated sidecar partitions.
                    // Keep the explicit synthetic load finite while retaining both SDK limiters.
                    target.WithEnvironment("EventStore__RateLimiting__PermitLimit", "8192")
                        .WithEnvironment("EventStore__RateLimiting__ConsumerPermitLimit", "8192")
                        .WithEnvironment("EventStore__RateLimiting__TenantPermitLimits__anonymous", "8192")
                        .WithEnvironment("EventStore__RateLimiting__ConsumerPermitLimits__anonymous", "8192");
                target.WithEnvironment("Version", WorksSdkProofProducer.Version)
                    .WithEnvironment("EventStore__IdempotencyAdmission__Enabled", "true")
                    .WithEnvironment("EventStore__IdempotencyAdmission__ActiveDigestKeyVersion", "story415")
                    .WithEnvironment("EventStore__IdempotencyAdmission__DigestKeys__story415", digestKey);
            }
        }
    }

    /// <summary>Captures a component once so a restored compatible policy is not rewritten by the retained host defaults.</summary>
    internal static void CaptureComponentConfiguration(string original, string destination, string redisEndpoint, bool isPlatform)
    {
        if (File.Exists(destination)) return;
        string content = File.ReadAllText(original);
        if (isPlatform) content = content.Replace("localhost:6379", redisEndpoint, StringComparison.Ordinal);
        File.WriteAllText(destination, content);
    }

    private static async Task<DaprClient> DaprAsync(DistributedApplication app, CancellationToken token, string name = "works", bool allowDegraded = false)
    {
        ResourceEvent resource;
        if (allowDegraded)
        {
            await app.ResourceNotifications.WaitForResourceAsync(name, KnownResourceStates.Running, token).ConfigureAwait(false);
            app.ResourceNotifications.TryGetCurrentState(name, out ResourceEvent? current).ShouldBeTrue();
            resource = current.ShouldNotBeNull();
        }
        else resource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, name, token, token).ConfigureAwait(false);
        string Endpoint(string name) => resource.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
        return new DaprClientBuilder().UseHttpEndpoint(Endpoint("DAPR_HTTP_ENDPOINT")).UseGrpcEndpoint(Endpoint("DAPR_GRPC_ENDPOINT")).Build();
    }

    private static async Task<int> CountAsync(HttpClient client, string tenant, string item, string type, CancellationToken token)
    {
        int count = -1;
        await WaitAsync(async () =>
        {
            count = await ReadCountAttemptAsync(client, tenant, item, type, token).ConfigureAwait(false);
            return count >= 0;
        }, token).ConfigureAwait(false);
        return count;
    }

    private static async Task<int> ReadCountAttemptAsync(HttpClient client, string tenant, string item, string type, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
        {
            Content = JsonContent.Create(new { tenant, domain = "work", aggregateId = item, fromSequence = 0L, pageSize = 100 }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(tenant));
        using HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if ((int)response.StatusCode == 503 && (detail.Contains("unavailable", StringComparison.OrdinalIgnoreCase) || detail.Contains("not_ready", StringComparison.OrdinalIgnoreCase))) return -1;
            throw new InvalidOperationException($"Stream evidence failed {(int)response.StatusCode}: {detail}");
        }
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
        return page.GetProperty("events").EnumerateArray().Count(value => value.GetProperty("eventTypeName").GetString()!.Split('.').Last() == type);
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        // A busy runner can spend longer than two minutes before a sweep records the expected state.
        deadline.CancelAfter(TimeSpan.FromMinutes(4));
        while (!await condition().WaitAsync(deadline.Token).ConfigureAwait(false))
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the source retains one effect for <paramref name="expectedSequence"/>.
    /// A checkpoint at that sequence means the effect was consumed and will not appear later.
    /// </summary>
    private static async Task WaitForRetainedPlanAsync(IReadModelStore store, ProcessSource source, long expectedSequence, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(4));
        ProcessSourceState? state = null;
        while (true)
        {
            try
            {
                state = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), deadline.Token)
                    .WaitAsync(deadline.Token).ConfigureAwait(false)).Value;
                if (state?.PendingPlan?.Sequence == expectedSequence && state.PendingPlan.Effects.Count == 1)
                    return;
                if (state is { Checkpoint: var checkpoint } && checkpoint >= expectedSequence && state.PendingPlan is null)
                {
                    throw new InvalidOperationException(
                        $"Process source {source.Aggregate} checkpointed sequence {checkpoint} without retaining the plan. Status={state.Status} Reason={state.ReasonCode}.");
                }
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Process source {source.Aggregate} did not retain a plan for sequence {expectedSequence}. Checkpoint={state?.Checkpoint} Status={state?.Status} Reason={state?.ReasonCode} PendingSequence={state?.PendingPlan?.Sequence} PendingEffects={state?.PendingPlan?.Effects.Count.ToString() ?? "none"}.");
            }
        }
    }

    /// <summary>
    /// Retries state reads and writes while the shared Dapr state-store circuit breaker is open.
    /// Fault injection in this proof trips that breaker for every key, not only the fault key.
    /// </summary>
    private sealed class OpenCircuitRetryStore(IReadModelStore inner) : IReadModelStore, IReadModelBatchStore
    {
        public Task<ReadModelEntry<TValue>> GetAsync<TValue>(string storeName, string key, CancellationToken cancellationToken = default)
            where TValue : class
            => Retry(() => inner.GetAsync<TValue>(storeName, key, cancellationToken), cancellationToken);

        public Task SaveAsync<TValue>(string storeName, string key, TValue value, CancellationToken cancellationToken = default)
            where TValue : class
            => Retry(async () =>
            {
                await inner.SaveAsync(storeName, key, value, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken);

        public Task<bool> TrySaveAsync<TValue>(string storeName, string key, TValue value, string etag, CancellationToken cancellationToken = default)
            where TValue : class
            => Retry(() => inner.TrySaveAsync(storeName, key, value, etag, cancellationToken), cancellationToken);

        public Task<ReadModelBatchResult> ExecuteAsync(ReadModelBatch batch, CancellationToken cancellationToken = default)
            => Retry(() => ((IReadModelBatchStore)inner).ExecuteAsync(batch, cancellationToken), cancellationToken);

        private static async Task<T> Retry<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        {
            const int attempts = 20;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    attempt < attempts
                    && exception.ToString().Contains("circuit breaker is open", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
