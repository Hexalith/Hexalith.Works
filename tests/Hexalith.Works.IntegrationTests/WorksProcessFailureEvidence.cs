using System.Security.Cryptography;
using System.Text;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.DomainService;

using Microsoft.Extensions.Options;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Read-only bounded process-state projection before fixture disposal; never repairs or resets state.</summary>
internal static class WorksProcessFailureEvidence
{
    private static readonly HashSet<string> KnownReasons = new(StringComparer.Ordinal)
    {
        "candidate-publication", "source-busy", "retry-exhausted", "invalid-evidence", "read-worker-exhausted", "source-read",
        "evidence-unavailable", "translation-timeout", "plan-unavailable", "delegation-unavailable", "invalid-receipt",
        "effect-conflict", "effect-unknown", "catalog-unavailable", "catalog-incomplete", "candidate-page-missing", "other",
        "hosted-pass-failed", "reconciliation-disabled", "reconciler-stopped",
    };

    internal static async Task<object> CaptureAsync(IReadModelStore store, ProcessSource source, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var options = Options.Create(new EventStoreProcessOptions { SupportedCandidateCount = WorksSdkRecoveryPipelineSmokeTests.SupportedCandidates });
            var checkpointTask = store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), deadline.Token);
            var faultTask = store.GetAsync<WorksSdkProofFaultState>("statestore", "story415:fail-after-commit:" + source.Tenant, deadline.Token);
            var sweepTask = new ProcessSweepStore(store, options, TimeProvider.System).GetAsync(deadline.Token);
            await Task.WhenAll(checkpointTask, faultTask, sweepTask).WaitAsync(deadline.Token).ConfigureAwait(false);
            return Project(checkpointTask.Result.Value, faultTask.Result.Value, sweepTask.Result);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            return new { diagnosticReadFailure = exception.GetType().FullName };
        }
    }

    internal static object Project(ProcessSourceState? source, WorksSdkProofFaultState? fault, ProcessSweepState? sweep)
        => new
        {
            checkpoint = source?.Checkpoint, status = source?.Status.ToString(), attempts = source?.Attempts,
            failureSequence = source?.FailureSequence, reason = Reason(source?.ReasonCode), source?.ObservedUnresolved,
            pendingSequence = source?.PendingPlan?.Sequence, plannedEffects = source?.PendingPlan?.Effects.Count,
            effectIdsSha256 = source?.PendingPlan?.Effects.Select(effect => Hash(EffectIdentityCodec.ComputeEffectId(new EffectIdentity(
                source.Source.Tenant, source.Source.Domain, source.Source.Aggregate, source.PendingPlan.Sequence,
                effect.Kind, effect.TargetDomain, effect.TargetAggregate, effect.Ordinal)))).ToArray(),
            faultFailNext = fault?.FailNext, faultHasCommittedEffect = fault?.CommittedEffectId is not null,
            committedEffectIdSha256 = fault?.CommittedEffectId is { } id ? Hash(id) : null,
            faultDisposition = fault?.Disposition?.ToString(), faultReplayObserved = fault?.ReplayObserved, faultHoldDelegation = fault?.HoldDelegation,
            sweepComplete = sweep?.Complete, catalogComplete = sweep?.CatalogComplete, candidatesComplete = sweep?.CandidatesComplete,
            unresolved = sweep?.Unresolved, sweep?.RetainedCandidates, sweep?.CapacityExceeded,
            sweepReasons = sweep?.Reasons.ToDictionary(pair => Reason(pair.Key)!, pair => pair.Value),
            pendingObservations = sweep?.PendingObservations.Count, pendingPublications = sweep?.PendingPublications.Count,
        };

    /// <summary>Retains only bounded SDK process-health counters and redacted reason identities.</summary>
    internal static Dictionary<string, object> ProjectHealthData(IReadOnlyDictionary<string, object> data)
    {
        string[] flags = ["complete", "capacityExceeded", "stateAvailable"];
        string[] counts = ["cycle", "unresolved", "pendingDispositions", "pendingPublications", "pendingTransitions", "pendingObservations", "retainedCandidates"];
        Dictionary<string, object> result = data.Where(pair => flags.Contains(pair.Key, StringComparer.Ordinal) && pair.Value is bool
            || counts.Contains(pair.Key, StringComparer.Ordinal) && (pair.Value is int count && count >= 0 || pair.Value is long total && total >= 0))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (data.TryGetValue("reasonCounts", out object? value) && value is IReadOnlyDictionary<string, int> reasons)
        {
            var retained = reasons.Where(pair => pair.Value >= 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).Take(16)
                .ToDictionary(pair => Reason(pair.Key)!, pair => pair.Value, StringComparer.Ordinal);
            result["reasonCounts"] = retained;
            result["omittedReasonCount"] = reasons.Count(pair => pair.Value >= 0) - retained.Count;
        }
        return result;
    }

    private static string? Reason(string? reason) => reason is null || KnownReasons.Contains(reason) ? reason : "unrecognized-sha256-" + Hash(reason);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
