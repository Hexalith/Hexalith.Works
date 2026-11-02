using System.Reflection;
using System.Net.Http;
using System.Text.Json;

using Hexalith.EventStore.Client.Streams;
using Hexalith.EventStore.Contracts.Identity;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Parties.Authorization;
using Hexalith.Parties.Contracts;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.Queries;
using Hexalith.Parties.Contracts.Security;
using Hexalith.Parties.Contracts.State;
using Hexalith.Parties.Contracts.ValueObjects;
using Microsoft.Extensions.Options;

namespace Hexalith.Parties.Queries;

/// <summary>Strict current and action-time identity reads, with no stale fallback or profile result.</summary>
public sealed class PartyIdentityQueryService(IPartyIdentityAuthority authority, TimeProvider timeProvider,
    IAuthoritativeEventStreamReader? reader = null, IIdentityHistoryCustody? custody = null,
    IRetainedIdentityHistoryReader? historyReader = null, IOptionsMonitor<PartyIdentityOptions>? identityOptions = null)
{
    /// <summary>Gets the shared work boundary; tests may supply an isolated finite capacity.</summary>
    internal PartyIdentityQueryWorkLimiter WorkLimiter { get; init; } = PartyIdentityQueryWorkLimiter.Shared;

    /// <summary>Gets an internal observer invoked within the bounded interval-selection operation.</summary>
    internal Action? BeforeBindingSelection { get; init; }


    /// <summary>Gets an internal observer invoked within bounded historical envelope parsing.</summary>
    internal Action? BeforeHistoricalParsing { get; init; }

    /// <summary>Gets an internal observer invoked within bounded current envelope parsing.</summary>
    internal Action? BeforeCurrentParsing { get; init; }

    /// <summary>Resolves current classification and eligibility from complete source and actor evidence.</summary>
    public async Task<PartyIdentityResult> ResolveAsync(QueryEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        long startedAt = timeProvider.GetTimestamp();
        try
        {
            using var deadline = new PartyIdentityQueryDeadline(TimeSpan.FromSeconds(30), timeProvider, cancellationToken, startedAt, WorkLimiter);
            PartyIdentityOptions? options = await deadline.RunAsync(_ => identityOptions?.CurrentValue).ConfigureAwait(false);
            deadline.ApplyTimeout(options?.QueryTimeout ?? TimeSpan.FromSeconds(30));
            deadline.ThrowIfCancellationRequested();
            IdentityHistoryPolicy? policy = options?.Policy;
            ResolvePartyIdentity? query = await deadline.RunAsync(_ =>
            {
                BeforeCurrentParsing?.Invoke();
                return JsonSerializer.Deserialize<ResolvePartyIdentity>(envelope.Payload, PartiesJsonOptions.Default);
            }).ConfigureAwait(false);
            if (query is null || !Matches(envelope, query.TenantId, query.PartyId))
            {
                deadline.ThrowIfCancellationRequested();
                return new(PartyIdentityOutcome.Unavailable, null);
            }

            IdentityAdmissionEvidence? admitted = (await deadline.RunAsync(_ => authority.Admit(envelope)).ConfigureAwait(false)).Evidence;
            if (admitted is null || reader is null
                || query.ExpectedActorId is not null && admitted.TargetActorId != query.ExpectedActorId)
            {
                deadline.ThrowIfCancellationRequested();
                return new(PartyIdentityOutcome.Unavailable, null);
            }

            deadline.ThrowIfCancellationRequested();
            AuthoritativeStreamReadResult read = await deadline.ReadAsync(token => reader.ReadAsync(new(query.TenantId, "party", query.PartyId), token)).ConfigureAwait(false);
            deadline.ThrowIfCancellationRequested();
            if (!read.IsAuthoritative || !MatchesSource(envelope, read.Stream)
                || read.Stream!.ObservedAt > timeProvider.GetUtcNow())
            {
                deadline.ThrowIfCancellationRequested();
                return new(PartyIdentityOutcome.Unavailable, null);
            }

            PartyState state = await deadline.RunAsync(token => PartyIdentitySourceFold.Fold(read.Stream!, token)).ConfigureAwait(false);
            if (!state.HasBeenCreated)
            {
                deadline.ThrowIfCancellationRequested();
                return new(PartyIdentityOutcome.Unavailable, null);
            }

            PartyIdentityClassification classification = state.Type == PartyType.Person ? PartyIdentityClassification.Human
                : state.Type == PartyType.Organization ? PartyIdentityClassification.Organization
                : PartyIdentityClassification.Unknown;
            bool eligible = state.IsActive && !state.IsRestricted && state.ErasureStatus == ErasureStatus.Active;
            bool requiresHumanPolicy = classification == PartyIdentityClassification.Human && eligible;
            HumanActorBindingEvidence? binding = null;
            if (requiresHumanPolicy)
            {
                if (!await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false))
                {
                    deadline.ThrowIfCancellationRequested();
                    return new(PartyIdentityOutcome.Unavailable, null);
                }

                HumanActorBindingEvidence[] candidates = await deadline.RunAsync(token =>
                    SelectBindingsAt(state.HumanActorBindings.Select(item => item.Evidence),
                        item => item, timeProvider.GetUtcNow(), token)).ConfigureAwait(false);
                if (candidates.Length == 1 && candidates[0].ActorId == query.ExpectedActorId
                    && admitted.TargetActorId == candidates[0].ActorId && admitted.ActorActive
                    && admitted.ActorRevision >= candidates[0].ActorRevision && custody is not null)
                {
                    if (!candidates[0].Custody.Satisfies(policy!, candidates[0].ValidFrom))
                    {
                        deadline.ThrowIfCancellationRequested();
                        return new(PartyIdentityOutcome.Unavailable, null);
                    }

                    bool canRead = await deadline.ReadAsync(token => custody.CanReadAsync(read.Stream!.Identity, candidates[0].Custody, token)).ConfigureAwait(false);
                    deadline.ThrowIfCancellationRequested();
                    if (!await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false))
                    {
                        deadline.ThrowIfCancellationRequested();
                        return new(PartyIdentityOutcome.Unavailable, null);
                    }

                    if (canRead)
                    {
                        binding = candidates[0];
                    }
                }

                eligible &= binding is not null;
            }

            IdentityAdmissionEvidence? currentAuthority = (await deadline.RunAsync(_ => authority.Admit(envelope)).ConfigureAwait(false)).Evidence;
            deadline.ThrowIfCancellationRequested();
            bool currentPolicy = !requiresHumanPolicy || await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false);
            DateTimeOffset completedAt = timeProvider.GetUtcNow();
            if (completedAt < read.Stream!.ObservedAt || !SameAuthority(admitted, currentAuthority, completedAt)
                || !currentPolicy
                || binding is not null && (!currentAuthority!.ActorActive || completedAt < binding.ValidFrom || completedAt >= binding.ValidUntil
                    || completedAt >= binding.Custody.ExpiresAt))
            {
                deadline.ThrowIfCancellationRequested();
                return new(PartyIdentityOutcome.Unavailable, null);
            }

            AuthoritativeEventStream source = read.Stream!;
            var evidence = new PartyIdentityEvidence(1, query.TenantId, query.PartyId, classification,
                state.IsActive, state.IsRestricted, state.ErasureStatus != ErasureStatus.Active,
                source.Head, source.ObservedAt, source.ObservationId, binding);
            deadline.ThrowIfCancellationRequested();
            return new(eligible && classification != PartyIdentityClassification.Unknown
                ? PartyIdentityOutcome.Resolved : PartyIdentityOutcome.Ineligible, evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(PartyIdentityOutcome.Unavailable, null);
        }
    }

    /// <summary>Resolves the unique recorded interval without substituting current actor or binding state.</summary>
    public async Task<HumanActorBindingResult> ResolveAtAsync(QueryEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        long startedAt = timeProvider.GetTimestamp();
        ResolveHumanActorBindingAt? query = null;
        try
        {
            using var deadline = new PartyIdentityQueryDeadline(TimeSpan.FromSeconds(30), timeProvider, cancellationToken, startedAt, WorkLimiter);
            PartyIdentityOptions? options = await deadline.RunAsync(_ => identityOptions?.CurrentValue).ConfigureAwait(false);
            deadline.ApplyTimeout(options?.QueryTimeout ?? TimeSpan.FromSeconds(30));
            deadline.ThrowIfCancellationRequested();
            IdentityHistoryPolicy? policy = options?.Policy;
            query = await deadline.RunAsync(_ =>
            {
                BeforeHistoricalParsing?.Invoke();
                return JsonSerializer.Deserialize<ResolveHumanActorBindingAt>(envelope.Payload, PartiesJsonOptions.Default);
            }).ConfigureAwait(false);
            if (query is null || !Matches(envelope, query.TenantId, query.PartyId)
                || string.IsNullOrWhiteSpace(query.ExpectedActorId) || query.ExpectedBindingVersion <= 0)
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            IdentityAdmissionEvidence? admitted = (await deadline.RunAsync(_ => authority.Admit(envelope)).ConfigureAwait(false)).Evidence;
            if (admitted is null
                || admitted.TargetActorId != query.ExpectedActorId || historyReader is null || custody is null
                || !await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false))
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            deadline.ThrowIfCancellationRequested();
            RetainedIdentityHistoryReadResult read = await deadline.ReadAsync(token => historyReader.ReadAsync(new(query.TenantId, "party", query.PartyId),
                RetainedIdentityHistoryReadRequest.AttributionPurpose, token)).ConfigureAwait(false);
            deadline.ThrowIfCancellationRequested();
            if (!await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false) || !read.IsAuthoritative || read.Stream is not { } retainedSource
                || retainedSource.Identity != new AggregateIdentity(query.TenantId, "party", query.PartyId)
                || !await deadline.RunAsync(_ => RetainedIdentityHistoryValidator.IsComplete(new(retainedSource.Identity,
                    RetainedIdentityHistoryReadRequest.AttributionPurpose), retainedSource, timeProvider.GetUtcNow())).ConfigureAwait(false))
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            if (query.ActionAt > read.Stream!.ObservedAt || read.Stream.ObservedAt > timeProvider.GetUtcNow())
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            IReadOnlyList<RetainedHumanActorBinding> history = await deadline.RunAsync(token => RetainedHumanActorHistoryFold.Fold(read.Stream!, policy!.PolicyId, token)).ConfigureAwait(false);
            IdentityAdmissionEvidence? afterFoldAuthority = (await deadline.RunAsync(_ => authority.Admit(envelope)).ConfigureAwait(false)).Evidence;
            if (!SameAuthority(admitted, afterFoldAuthority, timeProvider.GetUtcNow()) || !await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false)
                || !SameAuthority(admitted, afterFoldAuthority, timeProvider.GetUtcNow()))
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            RetainedHumanActorBinding[] matches = await deadline.RunAsync(token =>
                SelectBindingsAt(history, item => item.Evidence, query.ActionAt, token)).ConfigureAwait(false);
            if (matches.Length != 1)
            {
                deadline.ThrowIfCancellationRequested();
                if (!SameAuthority(admitted, afterFoldAuthority, timeProvider.GetUtcNow()))
                {
                    return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
                }

                return HistoryFailure(query, matches.Length == 0 ? HumanActorBindingOutcome.Gap : HumanActorBindingOutcome.Ambiguous);
            }

            HumanActorBindingEvidence evidence = matches[0].Evidence;
            if (!evidence.Custody.Satisfies(policy!, evidence.ValidFrom))
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            if (evidence.ActorId != query.ExpectedActorId || evidence.BindingVersion != query.ExpectedBindingVersion)
            {
                deadline.ThrowIfCancellationRequested();
                if (!SameAuthority(admitted, afterFoldAuthority, timeProvider.GetUtcNow()))
                {
                    return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
                }

                return HistoryFailure(query, HumanActorBindingOutcome.Mismatch);
            }

            if (timeProvider.GetUtcNow() >= evidence.Custody.ExpiresAt)
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Expired);
            }

            bool canRead = await deadline.ReadAsync(token => custody.CanReadAsync(read.Stream!.Identity, evidence.Custody, token)).ConfigureAwait(false);
            deadline.ThrowIfCancellationRequested();
            if (!await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false))
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            DateTimeOffset completedAt = timeProvider.GetUtcNow();
            if (!canRead || completedAt >= evidence.Custody.ExpiresAt)
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Expired);
            }

            DateTimeOffset finalValidationAt = timeProvider.GetUtcNow();
            bool completeSource = await deadline.RunAsync(_ => RetainedIdentityHistoryValidator.IsComplete(new(retainedSource.Identity,
                RetainedIdentityHistoryReadRequest.AttributionPurpose), retainedSource, finalValidationAt)).ConfigureAwait(false);
            IdentityAdmissionEvidence? currentAuthority = (await deadline.RunAsync(_ => authority.Admit(envelope)).ConfigureAwait(false)).Evidence;
            if (!await PolicyIsCurrentAsync(policy, deadline).ConfigureAwait(false))
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            completedAt = timeProvider.GetUtcNow();
            if (completedAt >= evidence.Custody.ExpiresAt)
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Expired);
            }

            // Validation can be slow. Re-admit after it, and recheck its exclusive
            // certificate expiry at the terminal instant rather than its start time.
            // Reject backward UTC movement to preserve every validated observation
            // lower bound, including destruction certificates, without another scan.
            if (!completeSource || completedAt < finalValidationAt || retainedSource.ValidUntil <= completedAt
                || !SameAuthority(admitted, currentAuthority, completedAt))
            {
                deadline.ThrowIfCancellationRequested();
                return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
            }

            deadline.ThrowIfCancellationRequested();
            return new(HumanActorBindingOutcome.Resolved, 1, query.TenantId, query.PartyId, query.ActionAt,
                read.Stream.Head, read.Stream.ObservationId, evidence) { BindingSourcePosition = matches[0].SourcePosition };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return HistoryFailure(query, HumanActorBindingOutcome.Unavailable);
        }
    }

    private T[] SelectBindingsAt<T>(IEnumerable<T> source, Func<T, HumanActorBindingEvidence> evidence,
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        BeforeBindingSelection?.Invoke();
        return PartyIdentityBindingSelection.SelectAt(source, evidence, at, cancellationToken);
    }

    private Task<bool> PolicyIsCurrentAsync(IdentityHistoryPolicy? policy, PartyIdentityQueryDeadline deadline)
        => deadline.RunAsync(_ => policy is { IsValid: true } && identityOptions?.CurrentValue.Policy == policy);

    private static HumanActorBindingResult HistoryFailure(ResolveHumanActorBindingAt? query, HumanActorBindingOutcome outcome)
        => new(outcome, 1, query?.TenantId ?? string.Empty, query?.PartyId ?? string.Empty,
            query?.ActionAt ?? default, 0, null, null);

    private static bool SameAuthority(IdentityAdmissionEvidence original, IdentityAdmissionEvidence? current, DateTimeOffset now)
        => current is not null && current.Scope == original.Scope && current.SourceId == original.SourceId
            && current.TargetActorId == original.TargetActorId && current.ActorRevision == original.ActorRevision
            && current.AuthorityRevision == original.AuthorityRevision && current.IssuedAt <= now && current.ExpiresAt > now;

    private static bool MatchesSource(QueryEnvelope envelope, AuthoritativeEventStream? stream)
        => stream is not null && stream.Identity.TenantId == envelope.TenantId
            && stream.Identity.Domain == envelope.Domain && stream.Identity.AggregateId == envelope.AggregateId;

    private static bool Matches(QueryEnvelope envelope, string tenantId, string partyId)
        => envelope.Domain == "party" && envelope.TenantId == tenantId && envelope.AggregateId == partyId
            && (envelope.EntityId is null || envelope.EntityId == partyId)
            && new AggregateIdentity(tenantId, "party", partyId).TenantId == tenantId;
}
