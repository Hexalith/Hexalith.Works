# PRD quality review — Hexalith.Works

Reviewed 20 November 2026. Scope includes the complete working-tree PRD and addendum, the decision log, the original brief, and the cited architecture and acceptance criteria.

## Overall verdict

The PRD gives builders a testable contract for effort replay, lifecycle transitions and most tenant-scoped operations. Two gaps affect correctness across aggregates. A matching key can point to different submitted work, and a child can finish before its parent has recorded the wait. Both need observable acceptance outcomes before teams qualify distributed retries and recovery.

## Decision-readiness — adequate

The split between kernel acceptance, distributed integration and production admission gives Product and Platform useful decision points. FR-24 names the exact release candidate, G3 names the recovery rehearsal, and G8 assigns performance and recovery budgets to owners. One shipped read surface lacks a product decision that an implementer can extract from §9.

### Findings

- **[medium] Single-item read entitlement is unspecified** (FR-11 and §9, `prd.md` lines 255–265 and 490–505). FR-11 exposes own and rolled effort for “each Work Item.” The authorization matrix specifies the shared queue, executor list and coordinator view. It gives no rule for an executor looking up their own item immediately after claiming it. The current detail handler lets an ordinary delegated reader see a queued item or their own assigned item and returns a not-found result for an in-progress item. *Fix:* Product and Security should specify the permitted principals and results for own active, shared queued, unrelated same-tenant and coordinator single-item reads. Cover the post-Claim lookup and any roll-up values it returns. [Current detail filter, lines 145–156](../../../../src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs)

## Substance over theater — strong

FR-8 distinguishes the recorded progress delta from its capped effect on effort. FR-9’s 10-hour estimate, 7 hours Done and successive estimates of 4 and 10 expose a specific replay defect. FR-20 gives a deterministic queue order, including absent values. G8 reserves binding performance thresholds for a defined environment and workload.

## Strategic coherence — strong

Uniform executor binding and an event-sourced work kernel support the two bets in §1 and the original brief. SM-3 measures channel independence. SM-C1 and SM-C2 constrain additions to the kernel. The McpCli roadmap identifies who owns a future command surface and what enrollment requires. [Original brief, success criteria and scope](../../briefs/brief-works-2026-06-14/brief.md)

## Done-ness clarity — thin

The local lifecycle matrix and FR-14/FR-15 distinguish admission, matching, duplicate retries and terminal rejection. Across aggregate boundaries, two implementations can follow the printed acceptance criteria yet produce different results for the same sequence.

### Findings

- **[high] Key reuse can replay a result for different work** (§10 and G1, `prd.md` lines 519 and 527, also FR-26 line 345). The stated rule is “reusing the same message/idempotency key returns the original success, no-op, or rejection.” For an internal effect, a second submission with the same key and a changed semantic payload could receive the earlier success under that wording. AD-26 and the forward acceptance criteria require a conflict and quarantine when the tuple or semantic digest differs. *Fix:* Limit original-outcome replay to the same scoped logical effect and semantic content. Add a G1 case for a changed payload under the same key that produces a conflict, preserves the receipt and appends no domain event. [Architecture AD-26, lines 490–496](../../architecture.md) · [Epics, lines 1564–1572](../../epics.md)
- **[high] Child completion can precede the parent’s wait** (UJ-3, FR-15, FR-16 and FR-26, `prd.md` lines 73, 322–326, 329–335 and 345–347). An attached child can complete before delayed parent bookkeeping records the requested suspension. At completion time a parent still in progress has no matching condition. When the wait is recorded later, no second child completion is required. Replaying a recorded rejection under the same key also cannot supply a successful Resume. AD-21 orders attachment before parent bookkeeping, and the forward story retries missing attachment. Neither defines the result of this already-attached interleaving. *Fix:* Require an already-completed child to satisfy a subsequently recorded wait from the same spawn, unless another eligible trigger already won or the parent became terminal. Exercise this ordering across restart and require one effective Resume. [Architecture AD-21, lines 359–373](../../architecture.md) · [Epics, lines 1844–1856](../../epics.md)

## Scope honesty — strong

Sections 2, 5 and 12 separate channel bindings exercised in the harness from production MCP, CLI and email experiences. FR-13 gives an interim single-writer restriction and a race test for competing writers. FR-24 and G3 identify release evidence that this document review cannot supply.

## Downstream usability — adequate

Reviewers can trace the 26 stable FRs, four journeys, six success metrics, two counter-metrics and eight gates. The addendum labels its September handoff as historical. Teams preparing ingress and parity fixtures have two narrower interpretation issues to resolve.

### Findings

- **[medium] Tenant casing needs a boundary-specific acceptance rule** (§9, `prd.md` lines 490–492). The Works adapter deliberately compares the command tenant and envelope tenant without regard to case. AD-15 requires canonical lowercase ASCII tenant slugs at trust boundaries and rejection of other spellings. A tester submitting an uppercase tenant envelope to the production boundary cannot infer the expected result from the command-to-stream paragraph alone. *Fix:* Identify the existing adapter compatibility rule and the target production ingress rule separately, and give one ordinary mixed-case tenant example at each boundary. Confirm which boundary has authority to validate before key construction. [Works adapter, lines 163–171](../../../../src/Hexalith.Works/WorkItemEventStoreAggregate.cs) · [Architecture AD-15, lines 233–242](../../architecture.md)
- **[low] The hosted lifecycle example omits Claim** (FR-24 and SM-1, `prd.md` lines 430 and 543). “Create → progress” is shorthand. FR-1 leaves even a bound new item in Created, FR-6 requires Claim to enter InProgress, and FR-8 accepts a report only for estimated work. Used literally as a smoke-test script, the first progress report is rejected. *Fix:* Show an estimated create, assignment or queueing, claim and a partial progress report before the spawn, then identify where the parent records its wait. [PRD, FR-1](prd.md#fr-1-create-a-work-item)

## Shape fit — strong

A capability-led PRD fits this headless product. UJ-1 and UJ-2 identify the builder and service-party scenarios. Ada’s UJ-3 supplies a reason for the competing-trigger rule, and Mary’s UJ-4 makes the future email and identity decisions concrete. The detailed protocol sketches and historical handoffs belong in the addendum.

## Mechanical notes

- The 26 FR headings are unique. FR-26 is grouped with the saga and appears in the feature inventory. Journey, metric and gate IDs are also continuous within their respective schemes.
- Section 14 includes the suspension, roll-up, cap, authorization and transport assumptions used by the reviewed cases. The September 15 decision supersedes the earlier deferred suspension decision in the log.
- This review concerns specification quality. Exact-candidate integration and hosted Platform parity, affected-snapshot recovery and multiwriter qualification require their own evidence.
