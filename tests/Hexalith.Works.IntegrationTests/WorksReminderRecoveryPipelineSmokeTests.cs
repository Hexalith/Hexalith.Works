using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Projections;
using Hexalith.Works.Reminders;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 4.8 SM-1 runtime proof of durable date-reminder registration and reconciliation in the live topology.
/// It starts the full Works AppHost under <see cref="Aspire.Hosting.Testing"/> and proves the story's core value
/// end-to-end: <b>recovery (AC #2/#3)</b> — after an AppHost restart against the same <c>dapr init</c> Redis, <em>with
/// no <c>--Works:Recovery:Tenants</c> argument</em>, a parked date-await resumes exactly once because recovery
/// auto-discovers it from the durable pending-date-await index the <c>/project</c> dispatcher maintains. A separate
/// fact covers <b>steady state (AC #1)</b> — suspend-time registration → Dapr Scheduler fire → resume with no restart.
/// </summary>
/// <remarks>
/// <para>It is Tier-3: it requires Docker and a <c>dapr init</c> Redis, and the AppHost-owned mTLS Sentry,
/// placement, and scheduler ports must be free. When those prerequisites are absent (e.g. the headless sandbox)
/// the tests <see cref="Assert.Skip(string)"/> with a clear reason rather than failing or hanging — mirroring
/// <c>WorksCommandPipelineSmokeTests</c>. The registration, index-maintenance, discovery, and reconciliation
/// <em>decision logic</em> is proven deterministically by <c>WorkItemSuspendedReminderHandlerTests</c>,
/// <c>PendingDateAwaitIndexDispatcherTests</c>, <c>IndexedPendingDateAwaitSourceTests</c>, and
/// <c>DateReminderRecoveryRuntimeTests</c>; these lanes prove the end-to-end resume acceptance under a real
/// Aspire topology.</para>
/// <para><b>No hand configuration (AC #3).</b> The AppHost is launched with <c>--EnableKeycloak=false</c> and an
/// explicit Development environment, but no Works recovery-tenant arguments.
/// Story 4.8 removed the <c>Works:Recovery:Tenants</c> forwarding; the restart's <c>ReminderReconciliationService</c>
/// runs on by default and discovers the tenants with pending date awaits from the durable registry the
/// <c>/project</c> dispatcher maintains, then re-folds each candidate's per-aggregate stream (every stream read
/// carries an <c>AggregateId</c> — the tenant-wide null-aggregate read is gateway-rejected).</para>
/// <para><b>Actor-scheduler substrate.</b> The AppHost composes mTLS-enabled Dapr placement and scheduler services
/// and passes their fixed local proxy addresses explicitly to every sidecar. Once the prerequisites hold, failure
/// to deliver a reminder is an acceptance failure, not a skip. Both suspend-time registration and recovery-time
/// re-registration of a still-future await are exercised through a real scheduler firing.</para>
/// <para>Auth uses the EventStore EnableKeycloak=false symmetric-key dev path; the signing key matches the
/// EventStore dev key. <b>Every fact owns its own tenant</b> as well as its own work-item ids: recovery
/// auto-discovers from a tenant-scoped durable index, so a shared tenant would make each restart re-fold the
/// other facts' items and couple facts that are meant to be independent.</para>
/// </remarks>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksReminderRecoveryPipelineSmokeTests
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Recovery_reissues_a_parked_date_await_from_the_durable_index_without_hand_configuration()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        if (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true) is { } gap)
        {
            Assert.Skip($"Aspire reminder-recovery lane cannot run: {gap}.");
            return;
        }

        string tenant = NewTenant("overdue");
        string recoveryItem = NewWorkItem("work-overdue");
        string secondPassProbeItem = NewWorkItem("work-second-pass-probe");

        // Host 1 — park a future await, prove its reminder was registered, then deliberately remove only that
        // Scheduler reminder. The durable pending-await index remains and must drive recreation after restart.
        // The await becomes overdue only while the host is genuinely down.
        DateTimeOffset recoveryInstant = default;
        PendingDateAwait? overdueAwait = null;
        PendingDateAwait? secondPassProbeAwait = null;
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (_, client, sidecarClient, token) =>
        {
            recoveryInstant = DateTimeOffset.UtcNow.AddMinutes(1);
            overdueAwait = WorksAppHostSmokeHarness.PendingAwait(tenant, recoveryItem, recoveryInstant);
            await WorksAppHostSmokeHarness.ParkSuspendedOnDateAsync(client, tenant, recoveryItem, recoveryInstant, token).ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .WaitForReminderRegisteredAsync(sidecarClient, overdueAwait!, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .WaitForPendingDateAwaitIndexedAsync(sidecarClient, overdueAwait!, token)
                .ConfigureAwait(false);
            await WaitForSuspensionMarkerCompletedAsync(client, sidecarClient, tenant, recoveryItem, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .DeleteReminderAsync(sidecarClient, overdueAwait!, token)
                .ConfigureAwait(false);

            DateTimeOffset secondPassProbeInstant = DateTimeOffset.UtcNow.AddMinutes(10);
            secondPassProbeAwait = WorksAppHostSmokeHarness.PendingAwait(
                tenant,
                secondPassProbeItem,
                secondPassProbeInstant);
            await WorksAppHostSmokeHarness
                .ParkSuspendedOnDateAsync(client, tenant, secondPassProbeItem, secondPassProbeInstant, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .WaitForReminderRegisteredAsync(sidecarClient, secondPassProbeAwait, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .WaitForPendingDateAwaitIndexedAsync(sidecarClient, secondPassProbeAwait, token)
                .ConfigureAwait(false);
            await WaitForSuspensionMarkerCompletedAsync(client, sidecarClient, tenant, secondPassProbeItem, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .DeleteReminderAsync(sidecarClient, secondPassProbeAwait, token)
                .ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, recoveryItem, token).ConfigureAwait(false)).ShouldBe(0);
        }).ConfigureAwait(true);
        TimeSpan untilOverdue = recoveryInstant - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        if (untilOverdue > TimeSpan.Zero)
        {
            await Task.Delay(untilOverdue, ct).ConfigureAwait(true);
        }

        // Host 2 — restart against the same Redis WITHOUT any --Works:Recovery:Tenants argument. Recovery
        // auto-discovers the parked await from the durable registry+index (no hand configuration), re-folds the
        // per-aggregate stream, finds it overdue, and reissues the resume through the reconciler → command gateway.
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (_, client, sidecarClient, token) =>
        {
            (await WorksAppHostSmokeHarness.WaitForResumedCountAsync(client, tenant, recoveryItem, atLeast: 1, token).ConfigureAwait(false))
                .ShouldBe(1, "Recovery must auto-discover the overdue await from the durable index (no hand config) and resume it exactly once.");
            await WorksAppHostTestReadiness
                .WaitForReminderRegisteredAsync(sidecarClient, secondPassProbeAwait!, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .DeleteReminderAsync(sidecarClient, secondPassProbeAwait!, token)
                .ConfigureAwait(false);

            // The reconciliation pass is idempotent: re-reading after a settle interval shows no duplicate resume.
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, recoveryItem, token).ConfigureAwait(false))
                .ShouldBe(1, "Recovery must not add a duplicate WorkItemResumed.");
        }).ConfigureAwait(true);

        // Host 3 — a second genuine startup reconciliation against the same durable state must remain convergent.
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (_, client, sidecarClient, token) =>
        {
            await WorksAppHostTestReadiness
                .WaitForReminderRegisteredAsync(sidecarClient, secondPassProbeAwait!, token)
                .ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, recoveryItem, token).ConfigureAwait(false)).ShouldBe(1);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task Recovery_re_registers_a_still_future_await_that_later_fires()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        if (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true) is { } gap)
        {
            Assert.Skip($"Aspire reminder-recovery lane cannot run: {gap}.");
            return;
        }

        string tenant = NewTenant("future");
        string futureRecoveryItem = NewWorkItem("work-future");

        DateTimeOffset futureInstant = default;
        PendingDateAwait? futureAwait = null;
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (_, client, sidecarClient, token) =>
        {
            // Select the instant only after host readiness. A pre-start timestamp can expire during a slow but
            // healthy distributed startup and would no longer prove future-reminder re-registration.
            futureInstant = DateTimeOffset.UtcNow.AddMinutes(4);
            futureAwait = WorksAppHostSmokeHarness.PendingAwait(tenant, futureRecoveryItem, futureInstant);
            await WorksAppHostSmokeHarness.ParkSuspendedOnDateAsync(client, tenant, futureRecoveryItem, futureInstant, token).ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .WaitForReminderRegisteredAsync(sidecarClient, futureAwait!, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .WaitForPendingDateAwaitIndexedAsync(sidecarClient, futureAwait!, token)
                .ConfigureAwait(false);
            await WorksAppHostTestReadiness
                .DeleteReminderAsync(sidecarClient, futureAwait!, token)
                .ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, futureRecoveryItem, token).ConfigureAwait(false)).ShouldBe(0);
        }).ConfigureAwait(true);

        DateTimeOffset.UtcNow.ShouldBeLessThan(futureInstant, "The second host must observe a genuinely future await.");
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (_, client, sidecarClient, token) =>
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(
                futureInstant,
                "The recovered reminder must still be future after the second host is fully ready.");
            await WorksAppHostTestReadiness
                .WaitForReminderRegisteredAsync(
                    sidecarClient,
                    futureAwait!,
                    token,
                    probeToken => AlreadyResumedAsync(client, tenant, futureRecoveryItem, probeToken))
                .ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.WaitForResumedCountAsync(
                    client,
                    tenant,
                    futureRecoveryItem,
                    atLeast: 1,
                    token,
                    futureInstant.AddSeconds(30))
                .ConfigureAwait(false))
                .ShouldBe(1, "Startup recovery must re-register the future reminder and its later scheduler firing must resume exactly once.");
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, futureRecoveryItem, token).ConfigureAwait(false))
                .ShouldBe(1, "Future-reminder recovery must not add a duplicate WorkItemResumed.");
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task Suspend_time_registration_resumes_the_item_when_the_scheduler_fires()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        if (await WorksAppHostSmokeHarness.PrerequisiteGapAsync(ct).ConfigureAwait(true) is { } gap)
        {
            Assert.Skip($"Aspire reminder-recovery lane cannot run: {gap}.");
            return;
        }

        string tenant = NewTenant("steady");
        string steadyItem = NewWorkItem("work-steady");

        // Steady state (AC #1): suspend on a near-future date; the reminder registered at suspend time on the live
        // work.events subscription must fire via the Dapr Scheduler and resume the item with NO restart.
        await WorksAppHostSmokeHarness.WithAppHostAsync(ct, async (_, client, sidecarClient, token) =>
        {
            DateTimeOffset instant = DateTimeOffset.UtcNow.AddSeconds(30);
            PendingDateAwait steadyAwait = WorksAppHostSmokeHarness.PendingAwait(tenant, steadyItem, instant);
            await WorksAppHostSmokeHarness.ParkSuspendedOnDateAsync(client, tenant, steadyItem, instant, token).ConfigureAwait(false);

            // A reminder that has already fired is removed by the actor, so its absence here is only a failure
            // when the resume it exists to produce has not happened either.
            await WorksAppHostTestReadiness
                .WaitForReminderRegisteredAsync(
                    sidecarClient,
                    steadyAwait,
                    token,
                    probeToken => AlreadyResumedAsync(client, tenant, steadyItem, probeToken))
                .ConfigureAwait(false);
            int resumed = await WorksAppHostSmokeHarness.WaitForResumedCountAsync(client, tenant, steadyItem, atLeast: 1, token).ConfigureAwait(false);
            resumed.ShouldBe(1, "Suspend-time registration + Dapr Scheduler fire must resume the item exactly once with no restart.");
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            (await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, steadyItem, token).ConfigureAwait(false))
                .ShouldBe(1, "Suspend-time delivery must not add a duplicate WorkItemResumed.");
        }).ConfigureAwait(true);
    }

    private static async Task<bool> AlreadyResumedAsync(
        HttpClient client,
        string tenant,
        string workItemId,
        CancellationToken cancellationToken)
        => await WorksAppHostSmokeHarness.CountResumedAsync(client, tenant, workItemId, cancellationToken).ConfigureAwait(false) > 0;

    private static async Task WaitForSuspensionMarkerCompletedAsync(
        HttpClient client,
        HttpClient sidecarClient,
        string tenant,
        string workItemId,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        string? suspensionMessageId = null;
        string lastDiagnostic = "The suspension event was not yet visible in the aggregate stream.";

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (suspensionMessageId is null)
                {
                    var body = new
                    {
                        tenant,
                        domain = "work",
                        aggregateId = workItemId,
                        fromSequence = 0L,
                        pageSize = 100,
                    };
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
                    {
                        Content = JsonContent.Create(body),
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        WorksAppHostSmokeHarness.MintToken(tenant));

                    using HttpResponseMessage response = await client
                        .SendAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    string responseBody = await response.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                    lastDiagnostic = $"Stream status={(int)response.StatusCode} ({response.StatusCode}); Body={Bound(responseBody)}";
                    if (response.IsSuccessStatusCode)
                    {
                        JsonElement page = JsonSerializer.Deserialize<JsonElement>(responseBody);
                        suspensionMessageId = page.GetProperty("events")
                            .EnumerateArray()
                            .Where(static streamEvent => string.Equals(
                                SimpleTypeName(streamEvent.GetProperty("eventTypeName").GetString() ?? string.Empty),
                                nameof(WorkItemSuspended),
                                StringComparison.Ordinal))
                            .Select(static streamEvent => streamEvent.GetProperty("messageId").GetString())
                            .SingleOrDefault();
                    }
                }

                if (suspensionMessageId is not null)
                {
                    string markerKey = string.Concat(
                        "eventstore:domain-events:markers:",
                        Uri.EscapeDataString("work.events"),
                        ":",
                        Uri.EscapeDataString("/work/events"),
                        ":",
                        suspensionMessageId);
                    string markerPath = $"/v1.0/state/{WorksReadModelKeys.StateStoreName}/{Uri.EscapeDataString(markerKey)}";
                    using HttpResponseMessage markerResponse = await sidecarClient
                        .GetAsync(markerPath, cancellationToken)
                        .ConfigureAwait(false);
                    string markerBody = await markerResponse.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                    lastDiagnostic = $"Marker status={(int)markerResponse.StatusCode} ({markerResponse.StatusCode}); Body={Bound(markerBody)}";
                    if (markerResponse.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(markerBody))
                    {
                        EventStoreDomainEventMarkerRecord? marker = JsonSerializer
                            .Deserialize<EventStoreDomainEventMarkerRecord>(markerBody, s_web);
                        if (marker?.State == EventStoreDomainEventMarkerState.Completed)
                        {
                            return;
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                lastDiagnostic = $"{ex.GetType().Name}: {ex.Message}";
            }
            catch (JsonException ex)
            {
                lastDiagnostic = $"{ex.GetType().Name}: {ex.Message}";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastDiagnostic = "The durable marker observation request exceeded the HTTP client timeout.";
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"[subscription-marker] Suspension event for work item '{workItemId}' in tenant '{tenant}' did not "
            + $"reach its durable Completed marker within 60 seconds. {lastDiagnostic}");
    }

    private static string SimpleTypeName(string typeName)
    {
        int lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    private static string Bound(string value)
        => value.Length <= 500 ? value : value[..500] + "…";

    // Unique per fact (canonical lowercase) so a re-run against a persistent dapr-init Redis never collides with
    // a prior run, and so one fact's auto-discovering reconciliation never re-folds another fact's items.
    private static string NewTenant(string purpose)
        => $"tenant-recovery-{purpose}-" + Guid.NewGuid().ToString("N")[..8];

    private static string NewWorkItem(string prefix)
        => prefix + "-" + Guid.NewGuid().ToString("N")[..12];
}
