#if WORKS_PLATFORM_PARITY
using System.Net;

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
        bindings["eventstore:bound-purpose"] = "wrong-purpose";
        using (HttpResponseMessage denied = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/work/processes/revalidate", body, Assertion(), cancellationToken).ConfigureAwait(false))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        bindings["eventstore:bound-purpose"] = "works.process-disposition";
        bindings[EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType] = source.Tenant + "-foreign";
        using (HttpResponseMessage denied = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/work/processes/revalidate", body, Assertion(), cancellationToken).ConfigureAwait(false))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        bindings[EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType] = source.Tenant;
        bindings["eventstore:bound-aggregate"] = source.Aggregate + "-foreign";
        using (HttpResponseMessage denied = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/work/processes/revalidate", body, Assertion(), cancellationToken).ConfigureAwait(false))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        bindings["eventstore:bound-aggregate"] = source.Aggregate;
        await store.SaveAsync("statestore", WorksPlatformAuditFaultStore.FaultKey, new WorksPlatformAuditFault(true), cancellationToken).ConfigureAwait(false);
        using (HttpResponseMessage outage = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/work/processes/revalidate", body, Assertion(), cancellationToken).ConfigureAwait(false))
            outage.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), cancellationToken).ConfigureAwait(false)).Value!
            .LastDispositionGeneration.ShouldBe(before.LastDispositionGeneration);
        ((await store.GetAsync<WorksProcessDispositionAuditDocument>("statestore", auditKey, cancellationToken).ConfigureAwait(false)).Value?.Attempts.Count ?? 0).ShouldBe(priorAuditCount);
        await store.SaveAsync("statestore", WorksPlatformAuditFaultStore.FaultKey, new WorksPlatformAuditFault(false), cancellationToken).ConfigureAwait(false);
        using (HttpResponseMessage accepted = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/work/processes/revalidate", body, Assertion(), cancellationToken).ConfigureAwait(false))
            ((int)accepted.StatusCode).ShouldBeOneOf(202, 204);
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
            audit, wrongPurpose = 403, wrongTenant = 403, wrongAggregate = 403, unavailableAudit = 500 };
    }
}
#endif
