# PRD quality review — Hexalith.Works

Reviewed the November 20 working-copy PRD and addendum against the BMad quality rubric. The review revisited the six high, five medium and two low findings from the standalone validation and checked the affected architecture decisions and forward acceptance criteria.

## Overall verdict

**Excellent.** All 13 prior findings have a consistent product outcome in the revised planning documents. An engineer can now tell which retries may replay a receipt, how a parent catches up after an early child completion and who can read active work after a handoff. Product approved both the reopened-descendant cascade and the wider detail entitlement. Exact-candidate integration, active-detail implementation, hosted parity and recovery qualification still require engineering evidence.

## Decision-readiness — strong

FR-10 settles M1. If a correction reopens an attached descendant that a Cancel or Expire cascade previously skipped, the correction and reopening stay in its history. The reopened child contributes its own active Remaining until the Reactor applies the matching termination. Restart must recover the outstanding work, and retries can produce at most one effective termination for that reopening. The 20 November Product decision replaces the provisional assumption. [PRD, FR-10](prd.md#fr-10-cancel-reject-expire) · [Architecture, AD-10](../../architecture.md)

Section 9 settles M5 separately for list and detail callers. After Claim, the current executor can still read the individual item and its Roll-Up in InProgress and Suspended. An active handoff transfers that entitlement. A reader must withhold the item when current binding cannot be established, even if a stale projection still names the previous executor. The current SDK’s narrower filter is an explicit implementation gap. [PRD, authorization matrix and single-item acceptance](prd.md#9-cross-cutting-nfrs) · [Current SDK detail filter](../../../../src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs)

## Substance over theater — strong

FR-8, FR-9 and Story 5.1 distinguish the submitted positive delta, the amount applied and the retained effort after an overrun. With 7 hours Done and a revised estimate of 4, a new report of 1 records that delta, preserves 7 Done and 0 Remaining, and precedes the completion event. Restart restores the recorded inputs and Status, including an active zero-Remaining or unestimated item. These examples resolve H4 and L1. [PRD, FR-8](prd.md#fr-8-report-progress-and-complete) · [Story 5.1](../../epics.md#story-51-make-progress-and-event-ordinals-overflow-safe)

## Strategic coherence — strong

The thin-kernel scope and shared executor model remain consistent with the original brief. SM-3 now compares event types, order, normalized domain payloads and status outcomes across executor kinds. The test separately checks the authenticated actor in each envelope, preserving the audit distinction raised in M2. SM-C1 and SM-C2 limit speculative expansion. [Success metrics](prd.md#11-success-metrics-build-signal-acceptance) · [Original brief](../../briefs/brief-works-2026-06-14/brief.md)

## Done-ness clarity — strong

G1 and AD-26 resolve H1 with two distinct receipt outcomes. An exact scoped key, tuple and digest replays the stored disposition. A changed tuple or digest under that key produces a quarantined conflict, preserves the receipt and emits no target event. FR-6 and FR-16 resolve H2 by specifying the effect of the reserved suspend flag on an in-progress, suspended or terminal parent and defining bookkeeping redelivery. [PRD, exit gates](prd.md#101-v1-dependency-and-exit-gates) · [PRD, FR-16](prd.md#fr-16-spawn-child-work) · [Architecture, AD-21 and AD-26](../../architecture.md)

FR-15 resolves H6 even when a child completes before its parent records the requested wait. The Reactor reconciles durable completion with that specific wait after restart. A prior rejection keeps its original disposition, another winning trigger remains effective, and a delayed retry cannot wake a later suspension. FR-24 and SM-1 also resolve L2 by creating an estimated item, assigning or queueing it, claiming it and reporting partial progress before spawning the child. [PRD, FR-15](prd.md#fr-15-resume-on-a-matching-trigger) · [PRD, FR-24](prd.md#fr-24-run-the-kernel-through-the-platform-hosted-domain-service-topology) · [PRD addendum](addendum.md)

## Scope honesty — strong

G8 and the forward performance criteria resolve M3. Convergence is checked at acknowledged quiescence. Five seconds and 200 ms are recorded as benchmark measurements pending an approved workload, environment, percentile, window and failure policy. FR-13 and FR-24 separately identify the evidence that qualifies competing writers and the exact release candidate. [PRD, cross-cutting requirements](prd.md#9-cross-cutting-nfrs) · [Architecture, AD-22](../../architecture.md) · [Forward acceptance criteria](../../epics.md)

## Downstream usability — strong

FR-20, §9, AD-03 and candidate F2-H resolve H3. An ordinary delegated caller needs their own verified PartyId and sees their assigned work plus the tenant queue. Missing or mismatched identity is forbidden. The approved coordinator workload can list all assigned and queued items in its authorized tenant. InProgress and Suspended work remain outside both next-work lists. [PRD, FR-20](prd.md#fr-20-resolve-a-whats-next-ordering) · [F2-H](../../epics.md#forward-candidate-f2-h-discover-the-next-work-deterministically)

FR-14, AD-13 and F3-F resolve H5 using a three-entry example with a duplicate. Recording and replay preserve all three entries in order. The first matching Resume committed on the item clears the collection. Section 9 and AD-15 resolve M4 with a boundary-specific example. A lowercase tenant and uppercase envelope can compare equal in the isolated Works adapter, while production ingress rejects the noncanonical envelope before lookup. [PRD, FR-14](prd.md#fr-14-suspend-on-an-ordered-collection-of-await-conditions) · [PRD, tenant identity](prd.md#9-cross-cutting-nfrs) · [Architecture, AD-13 and AD-15](../../architecture.md)

## Shape fit — strong

The capability scenarios show what builders and executors can do with the headless kernel. Ada’s sign-off example illustrates why a completion observed before a parent waits still matters. The deferred email journey has a separate roadmap home, and the addendum carries protocol details that engineers need to implement the behavioral contract.

## Mechanical notes

- The PRD retains 26 distinct FR headings, four journey IDs, six success metrics, two counter-metrics and eight exit gates.
- The source links checked in the PRD and addendum resolve. The assumptions index records the 20 November cascade decision.
- Open specification findings from this rubric pass: 0 critical, 0 high, 0 medium and 0 low. All 13 findings from the prior standalone validation were checked.
