using FsCheck;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Hexalith.Works.Testing;
using FSharpFuncConvert = Microsoft.FSharp.Core.FuncConvert;

namespace Hexalith.Works.PropertyTests;

/// <summary>
/// Story 4.3 (AC #2/#5) — single-claim-wins is <b>order-independent</b>. For a Queued item and any
/// generated set of K ≥ 2 distinct claim attempts, every candidate can be derived from the same observed
/// state; after the substrate independently commits one candidate, the remaining K − 1 re-handles are
/// domain-rejected, regardless of which claimant commits. This generalizes the two-claimant proof in
/// <c>WorkItemClaimConcurrencyTests</c> (UnitTests) to arbitrary fan-in.
/// <para>
/// Like that unit test this is a <b>deterministic</b> domain-outcome proof (RR-3): no threads, no
/// <c>Task.Run</c>, no sleeps. All K claims observe the same Queued state and each computes the same
/// state-changing payload ordinal. That equality neither creates nor identifies the EventStore conflict;
/// the ETag-backed actor-state save independently selects one committed candidate. The chosen candidate
/// advances state to InProgress, and every other claim re-handles against InProgress to a
/// <see cref="WorkItemTransitionRejected"/>. <b>Duplicate-delivery idempotency is a substrate concern</b>
/// (the actor's CausationId/offset dedup, NFR-9/AR-11), not the kernel: re-handling a claim against an
/// already-InProgress item is a rejection, never <see cref="DomainResult.NoOp"/> — so this property does
/// not assert NoOp convergence, only loser-re-handle convergence after a selected commit.
/// </para>
/// </summary>
public sealed class WorkItemClaimConvergencePropertyTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly Channel[] Channels = [Channel.Mcp, Channel.Chatbot, Channel.Cli, Channel.Email];

    private static readonly AuthorityLevel[] Authorities =
        [AuthorityLevel.Read, AuthorityLevel.Contribute, AuthorityLevel.Coordinate, AuthorityLevel.Administer];

    [Fact]
    public void Loser_rehandles_converge_for_any_ETag_selected_candidate_from_the_same_observed_state()
    {
        FsCheck.Gen<int[]> generatedCases = FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 255));
        Arbitrary<int[]> arbitraryCases = FsCheck.Fluent.Arb.ToArbitrary(generatedCases);
        Property property = FsCheck.FSharp.Prop.ForAll(
            arbitraryCases,
            FSharpFuncConvert.FromFunc<int[], bool>(values =>
            {
                // K distinct claims, K in [2, 6]. The winner index varies with the generated value, so
                // every position is exercised as the winner across the run (order-independence).
                int k = 2 + (values.Length == 0 ? 0 : Math.Abs(values[0]) % 5);
                int winnerIndex = values.Length < 2 ? 0 : Math.Abs(values[1]) % k;
                ExecutorBinding[] claims = [.. Enumerable.Range(0, k).Select(BuildDistinctBinding)];

                WorkItemState queued = WorkItemStateBuilder.InStatus(WorkItemStatus.Queued, Tenant, Item);
                long n = queued.Sequence;

                // Every claim handled against the same Queued snapshot accepts and derives the same
                // state-changing payload ordinal N+1. This proves deterministic domain candidate generation,
                // not EventStore envelope assignment or ETag commit enforcement.
                WorkItemClaimed[] candidates = [.. claims
                    .Select(binding => WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, binding), queued))
                    .Select(result => result.IsSuccess ? result.Events[0] as WorkItemClaimed : null)
                    .OfType<WorkItemClaimed>()];

                if (candidates.Length != k || candidates.Any(claimed => claimed.Sequence != n + 1))
                {
                    return false;
                }

                // Model state after the independent ETag-backed actor-state save commits the selected candidate.
                WorkItemState advanced = WorkItemStateBuilder.InStatus(WorkItemStatus.Queued, Tenant, Item);
                advanced.Apply(candidates[winnerIndex]);
                if (advanced.Status != WorkItemStatus.InProgress
                    || advanced.Sequence != n + 1
                    || advanced.ExecutorBinding != claims[winnerIndex])
                {
                    return false;
                }

                // Every other claim re-handles against the now-InProgress state and is domain-rejected.
                int rejected = 0;
                for (int i = 0; i < k; i++)
                {
                    if (i == winnerIndex)
                    {
                        continue;
                    }

                    DomainResult loser = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, claims[i]), advanced);
                    if (!loser.IsRejection
                        || loser.Events.Count != 1
                        || loser.Events[0] is not WorkItemTransitionRejected rejection
                        || rejection.FromStatus != WorkItemStatus.InProgress
                        || rejection.AttemptedAct != "Claim")
                    {
                        return false;
                    }

                    rejected++;
                }

                // Every possible committed candidate yields exactly K-1 rejected re-handles.
                return rejected == k - 1;
            }));

        Check.One(Config.QuickThrowOnFailure, property);
    }

    // Builds pairwise-distinct, valid executor bindings (Story 4.1 requires a valid AuthorityLevel). The
    // unique PartyId per index guarantees distinctness regardless of channel/authority cycling.
    private static ExecutorBinding BuildDistinctBinding(int index)
        => new(new PartyId($"party-{index}"), Channels[index % Channels.Length], Authorities[index % Authorities.Length]);
}
