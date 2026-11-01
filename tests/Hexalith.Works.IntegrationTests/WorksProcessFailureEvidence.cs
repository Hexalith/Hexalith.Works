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

    private static string? Reason(string? reason) => reason is null || KnownReasons.Contains(reason) ? reason : "unrecognized-sha256-" + Hash(reason);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
