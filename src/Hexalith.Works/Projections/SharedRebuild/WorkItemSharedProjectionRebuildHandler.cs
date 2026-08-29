using System.Text.Json;

using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;

namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Reconciles the complete authoritative Works tenant inventory as one shared roll-up projection.</summary>
public sealed class WorkItemSharedProjectionRebuildHandler : IAsyncDomainSharedProjectionRebuildHandler
{
    /// <summary>The named internal shared-projection route.</summary>
    public const string ProjectionTypeName = "works-shared-rollup";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public string Domain => "work";

    /// <inheritdoc/>
    public string ProjectionType => ProjectionTypeName;

    /// <inheritdoc/>
    public string RebuildStoreName => WorksReadModelKeys.StateStoreName;

    /// <inheritdoc/>
    public Task<DomainSharedProjectionRebuildCandidate> CreateEmptyCandidateAsync(
        DomainSharedProjectionRebuildIdentity identity,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToCandidate(new WorkItemSharedRebuildCandidateState([])));
    }

    /// <inheritdoc/>
    public Task<DomainSharedProjectionRebuildCandidate> AccumulateAsync(
        DomainSharedProjectionRebuildIdentity identity,
        DomainSharedProjectionRebuildCandidate candidate,
        ProjectionRequest aggregateHistory,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(aggregateHistory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(aggregateHistory.TenantId, identity.TenantId, StringComparison.Ordinal)
            || !string.Equals(aggregateHistory.Domain, identity.Domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The shared rebuild history is outside its tenant/domain identity.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateHistory.AggregateId);

        WorkItemSharedRebuildCandidateState state = FromCandidate(candidate);
        var history = new WorkItemSharedRebuildAggregateHistory(
            aggregateHistory.AggregateId,
            [.. aggregateHistory.Events ?? []]);

        // A re-supplied aggregate replaces its earlier history instead of folding the same stream twice: the
        // authoritative inventory names each aggregate once, so a repeat is a redelivery, not a second item.
        List<WorkItemSharedRebuildAggregateHistory> histories =
        [
            .. state.Histories.Where(retained => !string.Equals(retained.AggregateId, history.AggregateId, StringComparison.Ordinal)),
            history,
        ];
        return Task.FromResult(ToCandidate(new WorkItemSharedRebuildCandidateState(histories)));
    }

    /// <inheritdoc/>
    public Task<DomainProjectionRebuildPlan> FinalizeAsync(
        DomainSharedProjectionRebuildIdentity identity,
        DomainSharedProjectionRebuildCandidate candidate,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WorkItemSharedRebuildManifestBuilder.Build(identity, FromCandidate(candidate), cancellationToken));
    }

    /// <inheritdoc/>
    public Task<DomainProjectionHandlerResult> ProjectAsync(
        ProjectionRequest request,
        string dispatchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DomainProjectionHandlerResult.Failed(ProjectionDispatchReasonCodes.UnsupportedCapability));
    }

    private static WorkItemSharedRebuildCandidateState FromCandidate(DomainSharedProjectionRebuildCandidate candidate)
    {
        WorkItemSharedRebuildCandidateState? state;
        try
        {
            state = JsonSerializer.Deserialize<WorkItemSharedRebuildCandidateState>(candidate.State.Span, s_json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Works shared rebuild candidate is malformed.", exception);
        }

        return state is { Histories: not null }
            ? state
            : throw new InvalidOperationException("The Works shared rebuild candidate is malformed.");
    }

    private static DomainSharedProjectionRebuildCandidate ToCandidate(WorkItemSharedRebuildCandidateState state)
        => new(JsonSerializer.SerializeToUtf8Bytes(state, s_json));

    private static void ValidateIdentity(DomainSharedProjectionRebuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.Equals(identity.Domain, "work", StringComparison.Ordinal)
            || !string.Equals(identity.ProjectionType, ProjectionTypeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The shared rebuild identity does not target the Works roll-up route.");
        }
    }
}
