using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.Works.Projections;

using Aspire.Hosting.ApplicationModel;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.ValueObjects;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Tier-3 authorization proof for the AppHost-owned Dapr mTLS control plane: the Sentry-issued identity of the
/// <c>eventstore</c> app is authorized to invoke Works <c>/process</c>, while <c>eventstore-admin</c> — which the
/// Works access-control policy does not name — is denied by the deny-by-default ACL.
/// </summary>
/// <remarks>
/// It shares the live AppHost harness (and the serialized live collection) with the reminder-recovery lane
/// because it needs exactly the same topology, but it is an authorization fact, not a reminder fact, so it lives
/// in its own class.
/// </remarks>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksMtlsAuthorizationSmokeTests
{
    [Fact(Explicit = true)]
    public async Task Mtls_allows_event_store_and_denies_an_unauthorized_caller_at_works_process()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        if (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true) is { } gap)
        {
            Assert.Skip($"Aspire mTLS authorization lane cannot run: {gap}.");
            return;
        }

        WorksSdkProofRedis isolation = await WorksSdkProofRedis.StartAsync(ct, activateSdkWriter: true).ConfigureAwait(true);
        await using var cleanup = isolation.ConfigureAwait(false);
        using RSA rsa = RSA.Create(2048);
        string key = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        string digestKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string tenant = "tenant-mtls-" + Guid.NewGuid().ToString("N")[..8];
        string authorizedItem = "work-mtls-" + Guid.NewGuid().ToString("N")[..12];
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (app, client, _, token) =>
        {
            await WorksAppHostSmokeHarness.SubmitToTerminalAsync(
                client,
                tenant,
                authorizedItem,
                nameof(CreateWorkItem),
                new CreateWorkItem(new TenantId(tenant), new WorkItemId(authorizedItem), "mTLS authorization proof"),
                token).ConfigureAwait(false);

            ResourceEvent worksResource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", token, token).ConfigureAwait(false);
            string Endpoint(string name) => worksResource.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
            using DaprClient dapr = new DaprClientBuilder().UseHttpEndpoint(Endpoint("DAPR_HTTP_ENDPOINT"))
                .UseGrpcEndpoint(Endpoint("DAPR_GRPC_ENDPOINT")).Build();
            var store = new DaprReadModelStore(dapr);
            var coordinator = new SharedProjectionEpochCoordinator(store, store);
            var scope = new SharedProjectionScope("statestore", tenant, "work", WorksReadModelKeys.RuntimeFamily, ["works-runtime-v3-writer"]);
            using (var projectionDeadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                projectionDeadline.CancelAfter(TimeSpan.FromMinutes(2));
                while ((await coordinator.GetCheckpointAsync(scope, authorizedItem, projectionDeadline.Token).ConfigureAwait(false))?.OwnerSequenceNumber != 1)
                    await Task.Delay(200, projectionDeadline.Token).ConfigureAwait(false);
            }
            string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
                WorksReadModelKeys.RuntimeRollUpKey(authorizedItem), WorksReadModelKeys.RuntimeHistoryKey(authorizedItem), WorksReadModelKeys.RuntimeParkingKey(authorizedItem)];
            async Task<string> FamilyDigestAsync()
            {
                var family = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, token).ConfigureAwait(false);
                family.IsAvailable.ShouldBeTrue();
                family.IsStale.ShouldBeFalse();
                family.IsDegraded.ShouldBeFalse();
                family.Value.ShouldNotBeNull().Values.ShouldAllBe(value => value != null);
                return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(family.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal))));
            }
            string familyBefore = await FamilyDigestAsync().ConfigureAwait(false);
            string checkpointBefore = JsonSerializer.Serialize(await coordinator.GetCheckpointAsync(scope, authorizedItem, token).ConfigureAwait(false));

            using var adminReadinessCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            adminReadinessCts.CancelAfter(TimeSpan.FromMinutes(2));
            ResourceEvent adminResource = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "eventstore-admin", adminReadinessCts.Token, token)
                .ConfigureAwait(false);
            using HttpClient unauthorizedSidecar = WorksAppHostTestReadiness.CreateDaprClient(
                adminResource,
                "EventStore Admin");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1.0/invoke/works/method/process")
            {
                Content = JsonContent.Create(new { }),
            };
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            requestCts.CancelAfter(TimeSpan.FromSeconds(30));
            using HttpResponseMessage response = await unauthorizedSidecar
                .SendAsync(request, requestCts.Token)
                .ConfigureAwait(false);
            response.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                "The Sentry identity for eventstore-admin is not authorized for Works /process.");
            (await FamilyDigestAsync().ConfigureAwait(false)).ShouldBe(familyBefore);
            JsonSerializer.Serialize(await coordinator.GetCheckpointAsync(scope, authorizedItem, token).ConfigureAwait(false)).ShouldBe(checkpointBefore);
            string deniedBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            deniedBody.ShouldNotContain(tenant);
            deniedBody.ShouldNotContain(authorizedItem);
            deniedBody.ShouldNotContain("mTLS authorization proof");
        }, builder => WorksSdkRecoveryPipelineSmokeTests.Configure(builder, key, digestKey, isolation.ComponentPath,
            isolation.RedisEndpoint, [new ProcessSource(tenant, "work", authorizedItem)]), "WorksSdkReminderActor",
            schedulerVolumeName: isolation.SchedulerVolumeName, certificateDirectory: isolation.CertificateDirectory).ConfigureAwait(true);
    }
}
