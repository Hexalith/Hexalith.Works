# Downstream consistency review — Hexalith.Works

Reviewed 20 November 2026 against the current PRD, its addendum and decision log, the approved September 15 proposal, the November 20 audit, the architecture, the epic acceptance criteria and the lifecycle matrix.

**Verdict.** Three downstream acceptance criteria could authorize an overly broad query or produce a different durable record from the PRD. Two further discrepancies affect measurement and identity-admission tests. F-prefixed stories are planning candidates. The findings apply when those criteria are used for promotion or implementation. They do not establish a defect in a deployed service.

## High

### The coordinator query admits an ordinary tenant member in F2-H

FR-20 requires a “trusted internal origin” to request the all-work view. An executor can read their own assignments and the shared queue. F2-H instead gives “an authenticated tenant member” who omits the PartyId every assigned and queued item. The generic filtering clause in that example leaves the positive expected result in place. The same epic already has the correct restriction in its FR20 inventory. [PRD FR-20 and §9, lines 393 and 502–504](prd.md#fr-20-resolve-a-whats-next-ordering) · [epics.md, lines 61 and 1231–1239](../../epics.md#forward-candidate-f2-h-discover-the-next-work-deterministically)

**Fix.** Give F2-H three explicit cases. An ordinary member requesting the coordinator view is denied. A trusted internal caller can receive all assigned and queued work in scope. An executor query accepts only the authenticated Party’s own ID, including when another valid Party ID belongs to the same tenant.

### Story 5.1 can erase effort that exceeds the estimate

FR-8 says, “A report on an existing overrun preserves Done.” Story 5.1 says that whenever the positive delta reaches or exceeds Remaining, “Done saturates to Estimated.” Take an in-progress item with 7 hours Done whose estimate has dropped to 4. Remaining is zero. A new report of 1 satisfies the story criterion, which would lower Done to 4. FR-8 and the lifecycle matrix retain all 7 hours, record the submitted delta, and complete the item. The November audit explicitly calls for exercising a pre-existing overrun. [PRD FR-8, lines 221–224](prd.md#fr-8-report-progress-and-complete) · [Story 5.1, lines 2949–2952](../../epics.md#story-51-make-progress-and-event-ordinals-overflow-safe) · [Lifecycle matrix, lines 107–109](../../../../docs/lifecycle-transition-matrix.md) · [November audit, finding 1](../../sprint-change-proposal-2026-11-20.md#findings-and-decisions)

**Fix.** Split the criterion into positive Remaining and existing-overrun cases. In the latter, require the reported delta, unchanged cumulative Done, zero Remaining and progress/completion event order. Replay must recover the same result.

### F3-F can discard or reorder durable Await-Conditions

FR-14 records every entry “in submitted order, including exact duplicates.” F3-F instead specifies a “set” and says “duplicate identical conditions are normalized.” An implementation could pass that criterion while turning an input of child, date, child into child, date. The two histories have the same possible triggers but different durable evidence. AD-13 gives the matching rule without specifying preservation on admission. The approved September decision explicitly requires these downstream corrections. [PRD FR-14 and FR-15, lines 313–317](prd.md#fr-14-suspend-on-an-ordered-collection-of-await-conditions) · [F3-F, lines 1764–1767](../../epics.md#forward-candidate-f3-f-suspend-work-on-multiple-await-conditions) · [AD-13, lines 208–214](../../architecture.md) · [September 15 proposal, lines 196–206 and 256–262](../../sprint-change-proposal-2026-09-15.md)

**Fix.** Carry the ordered, duplicate-preserving example through F3-F, AD-13 and the Story 5.2 preservation checks. Require replay of all three entries in order and a legacy singular event as one member. In F3-G, specify that the first matching Resume committed on the Work Item wins regardless of entry order.

## Medium

### Forward acceptance criteria turn illustrative timings into mandatory thresholds

SM-2 requires correct totals after acknowledgements and calls five seconds a “non-gating benchmark until G8 binds a measured budget.” F4-C and F4-J instead require convergence within five seconds and reads under 200 ms. AD-22 also says the Platform lane “proves convergence within the PRD's provisional five-second bound.” A correct run could pass SM-2 and fail the downstream acceptance check solely because the timings exceeded those examples. [PRD §9, G8 and SM-2, lines 514, 534 and 544](prd.md#11-success-metrics-build-signal-acceptance) · [F4-C and F4-J, lines 2547–2550 and 2912–2915](../../epics.md) · [AD-22, lines 414–416](../../architecture.md)

**Fix.** Require acknowledged convergence as the correctness assertion. Record the fixture timings as measurements until Product and Architecture approve G8’s dataset, environment, percentile, window and failure policy. Bind the acceptance checks to the approved values at that point.

### Tenant casing has two different acceptance boundaries

PRD §9 specifies “case-insensitive comparison” between command and envelope tenant. The current Works adapter uses that comparison. AD-15 separately requires lowercase canonical tenant input at trust boundaries and directs them to “reject rather than transform” other input. For a payload tenant of `tenant-a` and a nonreserved envelope tenant of `TENANT-A`, an isolated SDK adapter test and a target ingress test can legitimately expect different outcomes. A caller deriving its wire contract from §9 alone cannot tell where the stricter rule starts. [PRD §9, lines 491–492](prd.md#9-cross-cutting-nfrs) · [AD-15, lines 233–242](../../architecture.md) · [Works adapter, lines 163–174](../../../../src/Hexalith.Works/WorkItemEventStoreAggregate.cs)

**Fix.** Label the existing adapter’s comparison and the target ingress’s canonical-input rule with their respective boundaries. Add one paired example to the acceptance criteria so the adapter and ingress fixtures assert the intended outcome at each boundary.

## Checks that agree

The registry attaches an edge only after matching durable child creation in FR-16, AD-21 and F3-E. Story 5.3 now preserves Done when an estimate changes. Story 4.9 owns the final host cutover after prerequisite Stories 4.10–4.16 and AD-20 parity evidence. These checks describe requirement agreement. Operational recovery evidence and exact-candidate release qualification remain separate. [PRD FR-16](prd.md#fr-16-spawn-child-work) · [AD-20 and AD-21](../../architecture.md) · [Epic acceptance criteria, lines 1729–1737, 2300–2303, 2346–2348 and 3053–3066](../../epics.md)
