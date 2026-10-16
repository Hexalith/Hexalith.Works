using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.Works.Queries;

using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Queries and replays the restored real fleet through its protected sidecars.</summary>
internal static class WorksPlatformRollbackChecks
{
    /// <summary>Verifies usable persisted SDK queries, exact receipt replay, and unchanged logical resume count.</summary>
    internal static async Task<object> VerifyAsync(DistributedApplication app, HttpClient gateway, RSA authority,
        string tenant, string parent, string resumedEffect, CancellationToken cancellationToken)
    {
        ResourceEvent works = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", cancellationToken, cancellationToken).ConfigureAwait(false);
        ResourceEvent eventStore = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore", cancellationToken, cancellationToken).ConfigureAwait(false);
        using HttpClient worksSidecar = WorksAppHostTestReadiness.CreateDaprClient(works, "Works");
        using HttpClient gatewaySidecar = WorksAppHostTestReadiness.CreateDaprClient(eventStore, "EventStore");
        using Dapr.Client.DaprClient dapr = new Dapr.Client.DaprClientBuilder()
            .UseHttpEndpoint(works.Snapshot.EnvironmentVariables.Single(variable => variable.Name == "DAPR_HTTP_ENDPOINT").Value!)
            .UseGrpcEndpoint(works.Snapshot.EnvironmentVariables.Single(variable => variable.Name == "DAPR_GRPC_ENDPOINT").Value!).Build();
        var store = new DaprReadModelStore(dapr);
        WorksSdkProofFaultState fault = (await store.GetAsync<WorksSdkProofFaultState>("statestore",
            "story415:fail-after-commit:" + tenant, cancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();
        TrustedEffectSubmission submission = fault.Submission.ShouldNotBeNull();
        string causation = fault.CausationId.ShouldNotBeNull();
        EffectIdentityCodec.ComputeEffectId(submission.Identity).ShouldBe(resumedEffect);
        var key = new RsaSecurityKey(authority) { KeyId = "story415-ephemeral" };
        string delegation = (await new WorksSdkProofDelegationProvider(key, new WorksSdkProofIntentRegistry(), store)
            .GetDelegationTokenAsync(new ReminderDelegationRequest(submission, "works", "works.child-completion-resume", causation), cancellationToken)
            .ConfigureAwait(false)).ShouldNotBeNull();
        using HttpResponseMessage replay = await WorksPlatformProofRequests.PostAsync(worksSidecar, "eventstore", "/api/v1/trusted-effects",
            new TrustedEffectSubmitRequest(submission, "works.child-completion-resume", causation, delegation),
            WorksPlatformProofRequests.Assertion("works", "eventstore", ["eventstore:trusted-effect"]), cancellationToken).ConfigureAwait(false);
        replay.EnsureSuccessStatusCode();
        TrustedEffectResult receipt = (await replay.Content.ReadFromJsonAsync<TrustedEffectResult>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        receipt.Replayed.ShouldBeTrue();
        receipt.EffectId.ShouldBe(resumedEffect);
        receipt.Disposition.ShouldBe(TrustedEffectDisposition.NoOp);
        (await WorksAppHostSmokeHarness.CountResumedAsync(gateway, tenant, parent, cancellationToken).ConfigureAwait(false)).ShouldBe(1);
        var query = new QueryEnvelope(tenant, "work", parent, GetWorkItemQueryHandler.GetWorkItemQueryType, [], "story416-rollback-query", "workload:eventstore")
        { AuthenticatedWorkloadId = "eventstore" };
        using HttpResponseMessage queried = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/query", query,
            WorksPlatformProofRequests.Assertion("eventstore", "works", ["domain-service:query"]), cancellationToken).ConfigureAwait(false);
        queried.EnsureSuccessStatusCode();
        QueryResult result = (await queried.Content.ReadFromJsonAsync<QueryResult>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.GetPayload().ToString().ShouldContain(parent);
        return new { receipt, query = result.GetPayload(), resumedCount = 1 };
    }

    /// <summary>Binds the exact compatible runtime and host binaries used by the restore drill.</summary>
    internal static IReadOnlyDictionary<string, string> RuntimeBinaryHashes()
    {
        string platform = Environment.GetEnvironmentVariable("WORKS_PLATFORM_ROOT")!;
        string works = Environment.GetEnvironmentVariable("WORKS_PLATFORM_WORKS_ROOT") ?? Path.GetFullPath(Path.Combine(platform, "..", "works"));
        string eventStore = Path.Combine(works, "references", "Hexalith.EventStore");
        string configuration = typeof(WorksPlatformRollbackChecks).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string[] directories =
        [
            Path.Combine(works, "src", "Hexalith.Works", "bin", configuration, "net10.0"),
            Path.Combine(eventStore, "src", "Hexalith.EventStore", "bin", configuration, "net10.0"),
            Path.Combine(eventStore, "src", "Hexalith.EventStore.Operations", "bin", configuration, "net10.0"),
            Path.Combine(eventStore, "src", "Hexalith.EventStore.Admin.Server.Host", "bin", configuration, "net10.0"),
            Path.GetDirectoryName(typeof(Projects.Hexalith_Works_AppHost).Assembly.Location)!,
            Path.GetDirectoryName(Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_ASSEMBLY"))!,
        ];
        return directories.SelectMany(directory => Directory.EnumerateFiles(directory))
            .Where(path => !string.Equals(path, Environment.ProcessPath, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path,
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
    }
}
