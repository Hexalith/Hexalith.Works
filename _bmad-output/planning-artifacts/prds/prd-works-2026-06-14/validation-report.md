# PRD validation — Hexalith.Works

- **Grade** Fair
- **Open findings** 0 critical · 6 high · 5 medium · 2 low
- **Run at** 2026-11-20T17:46:03+01:00
- **Reviewed working copy** [PRD](prd.md) and [addendum](addendum.md), based on Works `5287e187`.

## Overall verdict

The PRD gives builders a testable contract for effort replay, lifecycle transitions and most tenant-scoped operations. Two gaps affect correctness across aggregates. A matching key can point to different submitted work, and a child can finish before its parent has recorded the wait. Both need observable acceptance outcomes before teams qualify distributed retries and recovery.

The full review also found conflicting expectations for parent suspension, access to coworkers’ assignments, preservation of reported effort and the contents of stored suspension events. Resolve the six high findings before using the affected requirements or story criteria for acceptance. The November amendment’s re-estimation example and ordered-condition rule remain valid. Those two rules need corrections in the downstream stories.

## Findings by severity

### Critical (0)

None.

### High (6)

#### H1 · Reusing an effect key can return the wrong result

PRD FR-26, §10 and G1 (lines 345, 519, 527), architecture AD-26. The PRD lets a repeated internal command key return an earlier success. AD-26 treats reuse with a different target tuple or different command content as a conflict. A changed command could therefore be reported as successful even though it never ran.

**Suggested correction.** Replay the recorded result only when the key, effect tuple and semantic command digest match. Require a conflict and quarantine on mismatch, with no target mutation. [PRD §10](prd.md#10-constraints-guardrails--exit-gates) · [Architecture AD-26](../../architecture.md)

#### H2 · The parent can stay active or suspend after the same attachment

PRD FR-6 and FR-16 (lines 195, 329–334), architecture AD-21. FR-6 says child bookkeeping preserves the parent’s status. FR-16 accepts a request to suspend the parent. With the flag set on an in-progress parent, these readings yield different outcomes after the child attaches.

**Suggested correction.** Specify each parent state. Suspend an in-progress parent when requested, add the child condition to an already suspended parent, preserve terminal status, and make redelivery idempotent. Cover the flag-cleared case. [PRD FR-6](prd.md#fr-6-enforce-the-lifecycle-state-machine) · [Architecture AD-21](../../architecture.md)

#### H3 · The coordinator query has conflicting authorization criteria

PRD FR-20 and §9 (lines 393, 502–504), forward candidate F2-H (epics lines 1231–1239). The PRD reserves the tenant-wide work list for a trusted internal caller. F2-H gives all assigned and queued work to an authenticated tenant member who omits the executor ID. An ordinary member could see coworkers’ assignments under that acceptance test.

**Suggested correction.** Give F2-H distinct tests for an ordinary member requesting all work, a trusted coordinator and an executor requesting their own or someone else’s assignments. [PRD FR-20](prd.md#fr-20-resolve-a-whats-next-ordering) · [Candidate F2-H](../../epics.md#forward-candidate-f2-h-discover-the-next-work-deterministically)

#### H4 · Story 5.1 can erase effort already reported

PRD FR-8 (lines 221–224), Story 5.1 (epics lines 2949–2952). With 7 hours done against a revised estimate of 4, Remaining is zero. A further positive report meets Story 5.1’s criterion that Done saturates to Estimated, which would reduce Done to 4. The PRD and lifecycle matrix preserve all 7 hours.

**Suggested correction.** Split the story into a report when effort remains and a report on an existing overrun. For the latter, assert the raw delta, 7 Done, 0 Remaining and the progress/completion event order on replay. [PRD FR-8](prd.md#fr-8-report-progress-and-complete) · [Story 5.1](../../epics.md#story-51-make-progress-and-event-ordinals-overflow-safe) · [Lifecycle matrix](../../../../docs/lifecycle-transition-matrix.md)

#### H5 · The suspension story permits loss of ordered evidence

PRD FR-14/FR-15 (lines 313–317), F3-F (epics lines 1764–1767). FR-14 preserves every submitted condition in order, including duplicates. F3-F uses set-normalization wording that permits storing [child, date] when [child, date, child] was submitted. Both representations can resume on the same triggers, but only one replays the original act.

**Suggested correction.** Require F3-F and its downstream compatibility tests to preserve all three entries in order. Carry forward legacy single-condition reads and the first matching committed Resume. [PRD FR-14](prd.md#fr-14-suspend-on-an-ordered-collection-of-await-conditions) · [Candidate F3-F](../../epics.md#forward-candidate-f3-f-suspend-work-on-multiple-await-conditions) · [Approved September decision](../../sprint-change-proposal-2026-09-15.md)

#### H6 · A child can finish before its parent starts waiting

UJ-3, FR-15/16/26 (PRD lines 73, 322–335, 345–347), architecture AD-21. An attached child can complete before the parent records its requested wait. An early Resume then finds no matching condition. The parent can subsequently suspend with no second completion to wake it, and a retry can replay the recorded rejection.

**Suggested correction.** Define how an already-completed child satisfies the wait belonging to its spawn. Cover delayed bookkeeping across a restart, another trigger winning and the parent becoming terminal. Require one effective Resume when the wait still applies. [PRD FR-16](prd.md#fr-16-spawn-child-work) · [Architecture AD-21](../../architecture.md) · [Child-completion acceptance criteria](../../epics.md)

### Medium (5)

#### M1 · Reopening a child after a completed cascade has no specified outcome

PRD FR-8, FR-10, FR-16 and FR-26 (lines 223, 246–247, 334, 346). A cancellation cascade can skip a child that completed through progress. A later correction can reopen that child after the cascade finished. The late-attachment requirement does not say whether a reopened child can remain active under its terminal ancestor.

**Suggested correction.** Choose whether reactivation starts a new cascade or allows the child to continue. Specify eventual status, Roll-Up contribution and restart behavior, preserving the correction event. [PRD FR-10](prd.md#fr-10-cancel-reject-expire) · [PRD FR-8](prd.md#fr-8-report-progress-and-complete)

#### M2 · The executor-parity test cannot compare whole event envelopes literally

PRD FR-7, §9 and SM-3 (lines 212–213, 500, 545). SM-3 expects event streams to differ only in binding values. The authenticated actors also differ across the system, employee and external-party fixtures, and their identities belong in the audit envelopes.

**Suggested correction.** Compare event types, order, lifecycle outcomes and normalized domain payloads. Assert authenticated actor provenance separately for each fixture. [PRD success metrics](prd.md#11-success-metrics-build-signal-acceptance) · [PRD FR-7](prd.md#fr-7-record-raw-act-domain-events)

#### M3 · The same performance measurement can pass the PRD and fail an epic

PRD §9, G8 and SM-2 (lines 514, 534, 544), AD-22, F4-C and F4-J. The PRD makes five-second convergence and 200 ms reads indicative until G8 sets the budget. The forward criteria and architecture use those figures as pass conditions.

**Suggested correction.** Assert correctness at acknowledged convergence. Record the fixture timings until Product and Architecture approve a dataset, environment, percentile, measurement window and failure policy under G8. [PRD success metrics](prd.md#11-success-metrics-build-signal-acceptance) · [Architecture AD-22](../../architecture.md) · [Forward acceptance criteria](../../epics.md)

#### M4 · Tenant casing depends on the admission boundary

PRD §9 (lines 491–492), architecture AD-15 (lines 239–242). The Works adapter compares tenants case-insensitively. The target ingress rejects noncanonical tenant input. A mixed-case envelope can pass the isolated adapter check and fail ingress, yet the PRD does not tell callers which rule applies where.

**Suggested correction.** Specify the existing adapter behavior and the target production ingress rule separately. Add a paired acceptance example for the same mixed-case input at both boundaries. [PRD §9](prd.md#9-cross-cutting-nfrs) · [Architecture AD-15](../../architecture.md) · [Current adapter](../../../../src/Hexalith.Works/WorkItemEventStoreAggregate.cs)

#### M5 · A single-item lookup lacks an authorization rule after Claim

FR-11 and §9 (PRD lines 255–265, 490–505). The authorization matrix defines list access but omits an individual item and its Roll-Up. The current detail handler returns not found for an ordinary delegated reader once their assigned item becomes in progress. The PRD does not settle whether the executor should still see it.

**Suggested correction.** Product and Security should specify the result for the bound executor, another tenant member and a trusted coordinator. Cover queued and in-progress items, including the transition immediately after Claim. [PRD §9](prd.md#9-cross-cutting-nfrs) · [Detail query handler](../../../../src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs)

### Low (2)

#### L1 · Restart can restore zero or undefined Remaining

PRD FR-8/FR-9 (lines 228, 235). FR-8 says a crash leaves positive Remaining. The FR-9 example already permits an in-progress item at zero after re-estimation, and an unestimated item has no numeric Remaining.

**Suggested correction.** State that restart restores the last committed status and effort. Include an in-progress item at zero in the recovery example. [PRD FR-9](prd.md#fr-9-re-estimate-and-reschedule)

#### L2 · The hosted lifecycle example skips the step that starts work

FR-24 and SM-1 (PRD lines 430, 543). The example goes from Create straight to progress. FR-1 leaves a newly created item in Created, and FR-8 accepts progress only for estimated work in progress. The first report would fail if the example were run literally.

**Suggested correction.** Create with an estimate, assign or queue the item, claim it, and report partial progress before spawning the child. [PRD FR-24](prd.md#fr-24-run-the-kernel-through-the-platform-hosted-domain-service-topology) · [PRD FR-1](prd.md#fr-1-create-a-work-item)

## Dimension verdicts

### Decision-readiness — adequate

The split between kernel acceptance, distributed integration and production admission gives Product and Platform useful decision points. FR-24 names the exact release candidate, G3 names the recovery rehearsal, and G8 assigns performance and recovery budgets to owners. One shipped read surface lacks a product decision that an implementer can extract from §9.

Related findings: M1, M5.

### Substance over theater — strong

FR-8 distinguishes the recorded progress delta from its capped effect on effort. FR-9’s 10-hour estimate, 7 hours Done and successive estimates of 4 and 10 expose a specific replay defect. FR-20 gives a deterministic queue order, including absent values. G8 reserves binding performance thresholds for a defined environment and workload.

### Strategic coherence — strong

Uniform executor binding and an event-sourced work kernel support the two bets in §1 and the original brief. SM-3 measures channel independence. SM-C1 and SM-C2 constrain additions to the kernel. The McpCli roadmap identifies who owns a future command surface and what enrollment requires. [Original brief, success criteria and scope](../../briefs/brief-works-2026-06-14/brief.md)

### Done-ness clarity — thin

The local lifecycle matrix and FR-14/FR-15 distinguish admission, matching, duplicate retries and terminal rejection. Across aggregate boundaries, two implementations can follow the printed acceptance criteria yet produce different results for the same sequence.

Related findings: H1, H2, M2, L1, H6.

### Scope honesty — strong

Sections 2, 5 and 12 separate channel bindings exercised in the harness from production MCP, CLI and email experiences. FR-13 gives an interim single-writer restriction and a race test for competing writers. FR-24 and G3 identify release evidence that this document review cannot supply.

### Downstream usability — adequate

Reviewers can trace the 26 stable FRs, four journeys, six success metrics, two counter-metrics and eight gates. The addendum labels its September handoff as historical. Teams preparing ingress and parity fixtures have two narrower interpretation issues to resolve.

Related findings: H3, H4, H5, M3, M4, L2.

### Shape fit — strong

A capability-led PRD fits this headless product. UJ-1 and UJ-2 identify the builder and service-party scenarios. Ada’s UJ-3 supplies a reason for the competing-trigger rule, and Mary’s UJ-4 makes the future email and identity decisions concrete. The detailed protocol sketches and historical handoffs belong in the addendum.

## Reviewer files

- [PRD quality rubric](review-rubric.md). Seven dimensions assessed. Cross-aggregate retry and recovery rules account for the two high rubric findings. Findings H1, M4, H6, M5, L2.
- [Behavioral readiness](review-readiness.md). Overlapping lifecycle, retry and event-audit rules can give different results for the same command sequence. Findings H1, H2, M1, M2, L1.
- [Downstream consistency](review-downstream-drift.md). Story 5.1 and forward candidates F2-H and F3-F disagree with the PRD on observable outcomes. F-prefixed entries are planning candidates. Findings H3, H4, H5, M3, M4.

## Mechanical notes

- All 26 functional requirements, four journeys, six success metrics, two counter-metrics and eight gates are present and uniquely numbered. The checked Markdown source links resolve.
- Findings reported by multiple reviewers are counted once. The earlier amendment check and historical reviews cover different scopes. The standalone grade uses the three reviews listed above.
- This is a document assessment. Exact-candidate integration, hosted Platform parity, recovery from affected snapshots and multiwriter qualification still require their own evidence.

Rubric: `.agents/skills/bmad-prd/assets/prd-validation-checklist.md`.
