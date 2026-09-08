using System.Net;
using System.Net.Http.Json;

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
    [Fact]
    public async Task Mtls_allows_event_store_and_denies_an_unauthorized_caller_at_works_process()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        if (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true) is { } gap)
        {
            Assert.Skip($"Aspire mTLS authorization lane cannot run: {gap}.");
            return;
        }

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

            ResourceEvent adminResource = await WorksAppHostTestReadiness
                .WaitForResourceHealthyAsync(app, "eventstore-admin", token, ct)
                .ConfigureAwait(false);
            using HttpClient unauthorizedSidecar = WorksAppHostTestReadiness.CreateDaprClient(
                adminResource,
                "EventStore Admin");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1.0/invoke/works/method/process")
            {
                Content = JsonContent.Create(new { }),
            };
            using HttpResponseMessage response = await unauthorizedSidecar
                .SendAsync(request, token)
                .ConfigureAwait(false);
            response.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                "The Sentry identity for eventstore-admin is not authorized for Works /process.");
        }).ConfigureAwait(true);
    }
}
