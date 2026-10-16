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

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Real Redis/Dapr Works SDK recovery with an explicitly synthetic asymmetric authority.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksSdkRecoveryPipelineSmokeTests
{
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
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        string suffix = Guid.NewGuid().ToString("N")[..10];
        string tenant = "sdk415-callback-" + suffix;
        string item = "work-sdk415-callback-" + suffix;
        using RSA rsa = RSA.Create(2048);
        string key = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new DaprReadModelStore(dapr);
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
        }, builder =>
        {
            Configure(builder, key, digestKey, isolation.ComponentPath);
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "works"))
                .WithEnvironment("WORKS_SDK_PROOF_CALLBACKS", "1")
                .WithEnvironment("EventStore__Reminders__ReconciliationEnabled", "false")
                .WithEnvironment("EventStore__Processes__ReconciliationEnabled", "false");
        }, "WorksSdkReminderActor").ConfigureAwait(true);
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
        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct).ConfigureAwait(true);
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
        string? committedEffect = null;
        string? resumedEffect = null;
        string resumeParent = "work-sdk415-await-parent-" + suffix;
        string resumeChild = "work-sdk415-completed-child-" + suffix;
        string rescheduled = "work-sdk415-rescheduled-" + suffix;
        Action<IDistributedApplicationTestingBuilder> configure = builder => Configure(builder, key, digestKey, isolation.ComponentPath);
        string? expiryCascadeEffect = null;
        WorkItemExpirySchedule? childScheduledExpiry = null;

        // Reuse the existing two expiry sources, preserving the eleven-source bounded fixture profile.
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new DaprReadModelStore(dapr);
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
            var checkpoint = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(new ProcessSource(tenant, "work", rescheduled)), token).ConfigureAwait(false)).Value!;
            checkpoint.Checkpoint.ShouldBe(3);
            var effect = checkpoint.PendingPlan!.Effects.ShouldHaveSingleItem();
            effect.Kind.ShouldBe(EffectKindCatalog.CascadeExpire);
            JsonSerializer.Deserialize<ExpireWorkItem>(effect.CommandPayload)!.ExpiryWitness.ShouldBeNull();
            await EvidenceAsync("cascade-expiry-target-commit-before-checkpoint", new { expiryCascadeEffect, childScheduledExpiry, checkpoint, childExpired = 1 }, token).ConfigureAwait(false);
        }, configure, "WorksSdkReminderActor").ConfigureAwait(true);

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new DaprReadModelStore(dapr);
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

            DateTimeOffset dateDue = DateTimeOffset.UtcNow.AddSeconds(20);
            await WorksAppHostSmokeHarness.ParkSuspendedOnDateAsync(client, tenant, dateItem, dateDue, token).ConfigureAwait(false);
            string actor = ReminderIdentityCodec.ComputeActorId(tenant, dateItem);
            string stateKey = "eventstore:reminders:v1:WorksSdkReminderActor:item:" + actor;
            JsonObject? witness = null;
            await WaitAsync(async () =>
            {
                witness = (await store.GetAsync<JsonObject>("statestore", stateKey, token).ConfigureAwait(false)).Value;
                return witness?["entries"]?.AsArray().Count > 0;
            }, token).ConfigureAwait(false);
            string name = witness!["entries"]![0]!["reminderName"]!.GetValue<string>();
            using (HttpResponseMessage deleted = await sidecar.DeleteAsync(
                $"v1.0/actors/WorksSdkReminderActor/{actor}/reminders/{name}", token).ConfigureAwait(false))
                deleted.EnsureSuccessStatusCode();
            await WaitAsync(async () => await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, dateItem, token).ConfigureAwait(false) == 1,
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
            await WaitAsync(async () =>
            {
                creationGap = (await store.GetAsync<ProcessSourceState>("statestore",
                    ProcessStateKeys.Source(new ProcessSource(tenant, "work", parentId)), token).ConfigureAwait(false)).Value;
                return creationGap?.ReasonCode == "evidence-unavailable";
            }, token).ConfigureAwait(false);
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
        }, configure, "WorksSdkReminderActor").ConfigureAwait(true);

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new DaprReadModelStore(dapr);
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
            await WaitAsync(async () => (await store.GetAsync<ProcessSourceState>("statestore",
                ProcessStateKeys.Source(new ProcessSource(tenant, "work", resumeChild)), token).ConfigureAwait(false)).Value?.PendingPlan?.Effects.Count == 1, token).ConfigureAwait(false);
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
        }, configure, "WorksSdkReminderActor").ConfigureAwait(true);

        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _sidecar, token) =>
        {
            using var retryingClient = new HttpClient(new WorksSdkProofOrdinaryRetryHandler(client, token => WorksSdkProofDiagnostics.CaptureAsync(app, token)))
                { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
            client = retryingClient;
            using DaprClient dapr = await DaprAsync(app, token).ConfigureAwait(false);
            var store = new DaprReadModelStore(dapr);
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
        }, configure, "WorksSdkReminderActor").ConfigureAwait(true);
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

    private static void Configure(IDistributedApplicationTestingBuilder builder, string key, string digestKey, string componentPath)
    {
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
            if (component.Options?.LocalPath is not { } original || component.Name is not ("statestore" or "pubsub")) continue;
            string replacement = component.Name == "statestore" ? componentPath : Path.Combine(Path.GetDirectoryName(componentPath)!, "pubsub.yaml");
            if (component.Name == "pubsub") File.Copy(original, replacement, true);
            // Aspire constructs this init-only option before the testing callback; replace its
            // public option on the fixture model before BuildAsync, without editing production files.
            typeof(DaprComponentResource).GetProperty(nameof(DaprComponentResource.Options))!
                .SetValue(component, new DaprComponentOptions { LocalPath = replacement });
        }
        string assembly = typeof(WorksSdkProofHostingStartup).Assembly.Location;
        foreach (ProjectResource resource in builder.Resources.OfType<ProjectResource>().Where(resource => resource.Name is "works" or "eventstore"))
        {
            IResourceBuilder<ProjectResource> target = builder.CreateResourceBuilder(resource);
            target.WithEnvironment("DOTNET_STARTUP_HOOKS", assembly)
                .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", typeof(WorksSdkProofHostingStartup).Assembly.GetName().Name!)
                .WithEnvironment("WORKS_SDK_PROOF", "1").WithEnvironment("WORKS_SDK_PROOF_ROLE", resource.Name)
                .WithEnvironment("WORKS_SDK_PROOF_KEY", key);
            if (resource.Name == "works")
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
            else
                target.WithEnvironment("Version", "3.123.0")
                    .WithEnvironment("EventStore__IdempotencyAdmission__Enabled", "true")
                    .WithEnvironment("EventStore__IdempotencyAdmission__ActiveDigestKeyVersion", "story415")
                    .WithEnvironment("EventStore__IdempotencyAdmission__DigestKeys__story415", digestKey);
        }
    }

    private static async Task<DaprClient> DaprAsync(DistributedApplication app, CancellationToken token, string name = "works")
    {
        ResourceEvent resource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, name, token, token).ConfigureAwait(false);
        string Endpoint(string name) => resource.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
        return new DaprClientBuilder().UseHttpEndpoint(Endpoint("DAPR_HTTP_ENDPOINT")).UseGrpcEndpoint(Endpoint("DAPR_GRPC_ENDPOINT")).Build();
    }

    private static async Task<int> CountAsync(HttpClient client, string tenant, string item, string type, CancellationToken token)
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
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (!await condition().ConfigureAwait(false))
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
    }
}
