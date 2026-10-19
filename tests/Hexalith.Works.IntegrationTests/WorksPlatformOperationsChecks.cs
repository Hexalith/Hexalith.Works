#if WORKS_PLATFORM_PARITY
using System.Net;
using System.Text.Json.Nodes;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.Platform.Works.Runtime;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Protected real-host operator requests bind scope and commit durable audit before disposition.</summary>
internal static class WorksPlatformOperationsChecks
{
    /// <summary>Proves policy denial, audit outage refusal, attributed audit, and eventual SDK disposition.</summary>
    internal static async Task<object> VerifyAsync(DistributedApplication app, IReadModelStore store,
        ProcessSource source, CancellationToken cancellationToken)
    {
        ResourceEvent admin = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore-admin", cancellationToken, cancellationToken).ConfigureAwait(false);
        using HttpClient sidecar = WorksAppHostTestReadiness.CreateDaprClient(admin, "Admin");
        string auditKey = WorksSyntheticDispositionAuditSink.Key(source);
        ProcessSourceState before = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), cancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();
        int priorAuditCount = (await store.GetAsync<WorksProcessDispositionAuditDocument>("statestore", auditKey, cancellationToken).ConfigureAwait(false)).Value?.Attempts.Count ?? 0;
        Dictionary<string, object> bindings = new()
        {
            [EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType] = source.Tenant,
            [EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType] = source.Domain,
            ["eventstore:bound-aggregate"] = source.Aggregate,
            [EventStoreWorkloadAuthenticationDefaults.ActorBindingClaimType] = "synthetic-operator-416",
            ["eventstore:bound-purpose"] = "works.process-disposition",
        };
        object body = new { source, reason = "Synthetic Platform parity revalidation" };
        string Assertion() => WorksPlatformProofRequests.Assertion("eventstore-admin", "works", ["domain-service:process", "process:dispose"], bindings);
        int circuitRefusals = 0;
        async Task<HttpResponseMessage> PostThroughCircuitAsync(CancellationToken token)
        {
            using var transportDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            transportDeadline.CancelAfter(TimeSpan.FromSeconds(60));
            while (true)
            {
                HttpResponseMessage response = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/work/processes/revalidate",
                    body, Assertion(), transportDeadline.Token).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync(transportDeadline.Token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.InternalServerError
                    || !responseBody.Contains("circuit breaker", StringComparison.OrdinalIgnoreCase)
                    || !(responseBody.Contains("open", StringComparison.OrdinalIgnoreCase) || responseBody.Contains("too many requests", StringComparison.OrdinalIgnoreCase)))
                    return response;
                response.Dispose();
                circuitRefusals++;
                await Task.Delay(1000, transportDeadline.Token).ConfigureAwait(false);
            }
        }
        async Task<object> RequireReceiverRefusalAsync(string kind, int receiverStatus)
        {
            string nonce = Guid.NewGuid().ToString("N");
            Dictionary<string, string> headers = new() { [WorksPlatformDispositionProbe.NonceHeader] = nonce, [WorksPlatformDispositionProbe.KindHeader] = kind };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(65));
            int lastStatus = 0;
            string lastBody = string.Empty;
            try
            {
                while (true)
                {
                    using HttpResponseMessage refused = await WorksPlatformProofRequests.PostAsync(sidecar, "works", WorksPlatformDispositionProbe.Route,
                        body, Assertion(), deadline.Token, headers).ConfigureAwait(false);
                    lastStatus = (int)refused.StatusCode;
                    string raw = await refused.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
                    lastBody = raw[..Math.Min(raw.Length, 2000)];
                    bool transportOpen = refused.StatusCode == HttpStatusCode.InternalServerError
                        && raw.Contains("circuit breaker", StringComparison.OrdinalIgnoreCase)
                        && (raw.Contains("open", StringComparison.OrdinalIgnoreCase) || raw.Contains("too many requests", StringComparison.OrdinalIgnoreCase));
                    (lastStatus == receiverStatus || transportOpen).ShouldBeTrue("Expected the actual refusal or an explicit Dapr circuit refusal. HTTP=" + lastStatus + "; body=" + lastBody);
                    if (transportOpen) circuitRefusals++;
                    JsonObject? observed = (await store.GetAsync<JsonObject>("statestore", WorksPlatformDispositionProbe.Key(source, kind), deadline.Token).ConfigureAwait(false)).Value;
                    if (observed?["nonce"]?.GetValue<string>() == nonce)
                    {
                        int observedStatus = observed["status"]!.GetValue<int>();
                        observedStatus.ShouldBe(receiverStatus);
                        observed["kind"]!.GetValue<string>().ShouldBe(kind);
                        observed["route"]!.GetValue<string>().ShouldBe(WorksPlatformDispositionProbe.Route);
                        observed["tenant"]!.GetValue<string>().ShouldBe(source.Tenant);
                        observed["domain"]!.GetValue<string>().ShouldBe(source.Domain);
                        observed["aggregate"]!.GetValue<string>().ShouldBe(source.Aggregate);
                        observed["workload"]!.GetValue<string>().ShouldBe("eventstore-admin");
                        // The receiver's own persisted status, not the expected value, is the recorded observation.
                        return new { nonce, callerStatus = lastStatus, receiverStatus = observedStatus, expectedReceiverStatus = receiverStatus, receiver = observed, callerBody = lastBody };
                    }
                    await Task.Delay(500, deadline.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("No correlated actual receiver refusal completed before the bounded transport deadline. kind="
                    + kind + "; HTTP=" + lastStatus + "; body=" + lastBody);
            }
        }
        async Task RequireUnchangedDispositionAsync()
        {
            ProcessSourceState current = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), cancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();
            current.LastDispositionGeneration.ShouldBe(before.LastDispositionGeneration);
            current.Checkpoint.ShouldBe(before.Checkpoint);
            current.PendingPlan.ShouldBe(before.PendingPlan);
            ((await store.GetAsync<WorksProcessDispositionAuditDocument>("statestore", auditKey, cancellationToken).ConfigureAwait(false)).Value?.Attempts.Count ?? 0).ShouldBe(priorAuditCount);
        }
        bindings["eventstore:bound-purpose"] = "wrong-purpose";
        object wrongPurpose = await RequireReceiverRefusalAsync("wrong-purpose", 403).ConfigureAwait(false);
        await RequireUnchangedDispositionAsync().ConfigureAwait(false);
        bindings["eventstore:bound-purpose"] = "works.process-disposition";
        bindings[EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType] = source.Tenant + "-foreign";
        object wrongTenant = await RequireReceiverRefusalAsync("wrong-tenant", 403).ConfigureAwait(false);
        await RequireUnchangedDispositionAsync().ConfigureAwait(false);
        bindings[EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType] = source.Tenant;
        bindings["eventstore:bound-aggregate"] = source.Aggregate + "-foreign";
        object wrongAggregate = await RequireReceiverRefusalAsync("wrong-aggregate", 403).ConfigureAwait(false);
        await RequireUnchangedDispositionAsync().ConfigureAwait(false);
        bindings["eventstore:bound-aggregate"] = source.Aggregate;
        _ = await ReadModelWritePolicy.UpdateAsync<WorksPlatformAuditFault>(store, "statestore", WorksPlatformAuditFaultStore.FaultKey,
            _ => new WorksPlatformAuditFault(true), maxAttempts: 8, cancellationToken: cancellationToken).ConfigureAwait(false);
        object auditOutage = await RequireReceiverRefusalAsync("audit-outage", 500).ConfigureAwait(false);
        await RequireUnchangedDispositionAsync().ConfigureAwait(false);
        (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), cancellationToken).ConfigureAwait(false)).Value!
            .LastDispositionGeneration.ShouldBe(before.LastDispositionGeneration);
        ((await store.GetAsync<WorksProcessDispositionAuditDocument>("statestore", auditKey, cancellationToken).ConfigureAwait(false)).Value?.Attempts.Count ?? 0).ShouldBe(priorAuditCount);
        WorksPlatformAuditFault observedFault = (await store.GetAsync<WorksPlatformAuditFault>("statestore", WorksPlatformAuditFaultStore.FaultKey,
            cancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();
        observedFault.ObservedOutages.ShouldBeGreaterThan(0, "The real HTTP audit attempt must reach the unavailable provider.");
        _ = await ReadModelWritePolicy.UpdateAsync<WorksPlatformAuditFault>(store, "statestore", WorksPlatformAuditFaultStore.FaultKey,
            current => current! with { Unavailable = false }, maxAttempts: 8, cancellationToken: cancellationToken).ConfigureAwait(false);
        int acceptedCallerStatus;
        using (HttpResponseMessage accepted = await PostThroughCircuitAsync(cancellationToken).ConfigureAwait(false))
        {
            acceptedCallerStatus = (int)accepted.StatusCode;
            string responseBody = await accepted.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ((int)accepted.StatusCode is 202 or 204).ShouldBeTrue("Disposition must complete after the explicit transport refusal clears. HTTP="
                + (int)accepted.StatusCode + "; body=" + responseBody[..Math.Min(responseBody.Length, 2000)]);
        }
        WorksProcessDispositionAuditDocument audit = (await store.GetAsync<WorksProcessDispositionAuditDocument>("statestore", auditKey, cancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();
        audit.Attempts.Count.ShouldBe(priorAuditCount + 1);
        audit.Attempts.Last().Actor.ShouldBe("synthetic-operator-416");
        audit.Attempts.Last().Workload.ShouldBe("eventstore-admin");
        audit.Attempts.Last().Source.ShouldBe(source);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        ProcessSourceState? after;
        do
        {
            after = (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), deadline.Token).ConfigureAwait(false)).Value;
            if (after?.LastDispositionGeneration > before.LastDispositionGeneration) break;
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        } while (true);
        return new { beforeGeneration = before.LastDispositionGeneration, afterGeneration = after.LastDispositionGeneration,
            audit, wrongPurpose, wrongTenant, wrongAggregate, auditOutage, acceptedCallerStatus,
            providerOutageAttempts = observedFault.ObservedOutages, circuitRefusals };
    }
}
#endif
