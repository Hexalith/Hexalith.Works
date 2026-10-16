using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Security.Cryptography;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
#if WORKS_PLATFORM_PARITY
using Hexalith.Platform.Works.Runtime;
#endif
using Hexalith.Works.Queries;

using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Queries and replays the restored real fleet through its protected sidecars.</summary>
internal static class WorksPlatformRollbackChecks
{
    /// <summary>Verifies usable persisted SDK queries, exact receipt replay, and unchanged logical resume count.</summary>
    internal static async Task<object> VerifyAsync(DistributedApplication app, HttpClient gateway, RSA authority,
        string tenant, string parent, string resumedEffect, CancellationToken cancellationToken, bool restored = false)
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
        JsonObject changedCommand = JsonNode.Parse(submission.CommandPayload)!.AsObject();
        string conditionKey = changedCommand.Select(property => property.Key)
            .Single(name => string.Equals(name, "AwaitCondition", StringComparison.OrdinalIgnoreCase));
        changedCommand[conditionKey].ShouldNotBeNull();
        changedCommand[conditionKey] = null;
        TrustedEffectSubmission conflicting = submission with { CommandPayload = JsonSerializer.SerializeToUtf8Bytes(changedCommand) };
        string conflictingDelegation = (await new WorksSdkProofDelegationProvider(key, new WorksSdkProofIntentRegistry(), store)
            .GetDelegationTokenAsync(new ReminderDelegationRequest(conflicting, "works", "works.child-completion-resume", causation), cancellationToken)
            .ConfigureAwait(false)).ShouldNotBeNull();
        using HttpResponseMessage conflict = await WorksPlatformProofRequests.PostAsync(worksSidecar, "eventstore", "/api/v1/trusted-effects",
            new TrustedEffectSubmitRequest(conflicting, "works.child-completion-resume", causation, conflictingDelegation),
            WorksPlatformProofRequests.Assertion("works", "eventstore", ["eventstore:trusted-effect"]), cancellationToken).ConfigureAwait(false);
        conflict.IsSuccessStatusCode.ShouldBeFalse("Conflicting admitted semantics must never execute through an existing effect identity.");
        string actor = Uri.EscapeDataString(new Hexalith.EventStore.Contracts.Identity.AggregateIdentity(tenant, "work", parent).ActorId);
        JsonElement index = await gatewaySidecar.GetFromJsonAsync<JsonElement>($"v1.0/actors/AggregateActor/{actor}/state/effect_evidence_index", cancellationToken).ConfigureAwait(false);
        string[] collisions = [.. Property(index, "CollisionKeys").EnumerateArray().Select(value => value.GetString()!)
            .Where(value => value.StartsWith("effect_collision_" + resumedEffect + "_", StringComparison.Ordinal))];
        string collisionKey = collisions.ShouldHaveSingleItem();
        JsonElement collision = await gatewaySidecar.GetFromJsonAsync<JsonElement>(
            $"v1.0/actors/AggregateActor/{actor}/state/{Uri.EscapeDataString(collisionKey)}", cancellationToken).ConfigureAwait(false);
        Property(collision, "EffectId").GetString().ShouldBe(resumedEffect);
        Property(collision, "Workload").GetString().ShouldBe("works");
        Property(collision, "Purpose").GetString().ShouldBe("works.child-completion-resume");
        Property(collision, "AttemptedSemanticDigest").GetString().ShouldNotBe(Property(collision, "OriginalSemanticDigest").GetString());
        WorksSdkProofAuditDocument trustedAudit = (await gatewaySidecar.GetFromJsonAsync<WorksSdkProofAuditDocument>(
            "v1.0/state/statestore/" + Uri.EscapeDataString("story415:trusted-audit:" + tenant), cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        trustedAudit.Attempts.ShouldContain(attempt => attempt.Action == "collision" && attempt.EffectId == resumedEffect
            && attempt.Workload == "works" && attempt.Disposition == "quarantined");
        (await WorksAppHostSmokeHarness.CountResumedAsync(gateway, tenant, parent, cancellationToken).ConfigureAwait(false)).ShouldBe(1);
        ProcessSweepState? sweep = null;
        if (restored)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            do
            {
                sweep = (await store.GetAsync<ProcessSweepState>("statestore", ProcessStateKeys.Sweep, deadline.Token).ConfigureAwait(false)).Value;
                if (sweep is { Complete: true, Unresolved: 0, RetainedCandidates: 11 }) break;
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            } while (true);
            sweep.Pending.Count.ShouldBe(0);
#if WORKS_PLATFORM_PARITY
            // The operator record belongs to the completed child process source, not the resumed parent.
            var source = new Hexalith.EventStore.Client.Processes.ProcessSource(submission.Identity.Tenant, submission.Identity.SourceDomain, submission.Identity.SourceAggregate);
            WorksProcessDispositionAuditDocument audit = (await store.GetAsync<WorksProcessDispositionAuditDocument>("statestore",
                WorksSyntheticDispositionAuditSink.Key(source), cancellationToken).ConfigureAwait(false)).Value.ShouldNotBeNull();
            audit.Attempts.ShouldHaveSingleItem().Actor.ShouldBe("synthetic-operator-416");
#endif
        }
        return new { receipt, query = result.GetPayload(), resumedCount = 1, restoredSweep = sweep,
            conflictingReplayStatus = (int)conflict.StatusCode, collision, durableCollisionCount = collisions.Length,
            attributedCollisionAudit = true };
    }

    private static JsonElement Property(JsonElement document, string name)
        => document.EnumerateObject().Single(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Binds the exact compatible runtime and host binaries used by the restore drill.</summary>
    internal static IReadOnlyDictionary<string, string> RuntimeBinaryHashes()
    {
        string platform = Environment.GetEnvironmentVariable("WORKS_PLATFORM_ROOT")!;
        string works = Environment.GetEnvironmentVariable("WORKS_PLATFORM_WORKS_ROOT") ?? Path.GetFullPath(Path.Combine(platform, "..", "works"));
        string eventStore = Environment.GetEnvironmentVariable("WORKS_PLATFORM_SDK_SOURCE_ROOT")
            ?? Path.Combine(works, "references", "Hexalith.EventStore");
        string configuration = typeof(WorksPlatformRollbackChecks).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string[] projects =
        [
            Path.Combine(works, "src", "Hexalith.Works", "Hexalith.Works.csproj"),
            Path.Combine(eventStore, "src", "Hexalith.EventStore", "Hexalith.EventStore.csproj"),
            Path.Combine(eventStore, "src", "Hexalith.EventStore.Operations", "Hexalith.EventStore.Operations.csproj"),
            Path.Combine(eventStore, "src", "Hexalith.EventStore.Admin.Server.Host", "Hexalith.EventStore.Admin.Server.Host.csproj"),
            Path.Combine(works, "src", "Hexalith.Works.AppHost", "Hexalith.Works.AppHost.csproj"),
        ];
        string[] directories = [.. projects.Select(project => WorksPlatformRuntimeArtifacts.DirectoryFor(project, configuration)),
            Path.GetDirectoryName(typeof(Projects.Hexalith_Works_AppHost).Assembly.Location)!,
            Path.GetDirectoryName(Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_ASSEMBLY"))!];
        return directories.SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => !string.Equals(path, Environment.ProcessPath, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path,
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
    }
}
