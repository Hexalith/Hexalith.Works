---
title: "Architecture and Works domain audit — creation-boundary, fitness-gate, read-side defense, and runtime-wiring corrections"
status: approved-implementing
created: 2026-07-21
trigger: "Direct user instruction (2026-07-21): find any architectural design or Works domain issues and fix them."
scope: moderate
---

# Sprint Change Proposal - Works Architecture and Domain Audit (2026-07-21)

## 1. Issue Summary

A full adversarial audit of the Works implementation against the architecture spine
(`architecture.md`), the lifecycle transition matrix, the projection docs, and the ecosystem
baseline was run on 2026-07-21 at the user's direct instruction. Four parallel audit passes
(kernel fitness, domain logic, projections/read-side, adapter/runtime) each verified findings
against actual code before reporting.

Headline results:

- The **kernel is in excellent architectural health**: zero purity violations, zero
  dependency-direction violations, zero executor-kind branching, a genuinely mechanical reactor,
  a lifecycle table that mirrors the transition matrix cell-for-cell, and clean B3 type
  separation in the read models.
- The **build was broken** at audit start: the checked-out `references/Hexalith.EventStore`
  submodule (fbc78e58, past v3.80.0) raised package floors above Works' central pins
  (18 NU1109 errors).
- The **creation boundary is the weak point of the domain**: a duplicate/late `CreateWorkItem`
  is accepted in any status — including terminal — and resets the lifecycle; the create path
  feeds the tree guard hardcoded facts, bypassing the depth cap.
- The **fitness-test layer under-enforces the invariants it exists to protect**: the strongest
  banned-symbol scan skips the Reactor project; RNG/environment/Guid-parse are banned nowhere;
  the mandated mutation-validated tenant-isolation gate does not exist.
- Most seriously, **no reactor-driven behavior executes in the live topology**: the host maps
  no event subscription, so the cascade dispatcher and child-completion→resume translator have
  no production trigger; the date-reminder reconciliation scan uses a gateway route that
  unconditionally rejects it; the cascade checkpoint replay has no production caller. The parts
  are all built and unit/component-tested — the wiring between the event stream and those parts
  is missing. These gaps are foreshadowed in the repo's own limitation notes
  (`docs/eventstore-api-surface-constraints.md`, `docs/boundary-decision-record.md`) but their
  combined runtime consequence was not visible in any planning artifact.

Evidence for every finding is file:line-verified; the inventory is in Section 4.

## 2. Impact Analysis

**Epic impact**

- Epics 1–3: no scope change. Findings in their areas are code/test-level corrections within
  already-delivered story intent (creation guard belongs to Story 1.2/3.1 intent; projection
  defense to 3.3/3.4).
- Epic 4 (in-progress): Stories 4.5/4.6 proved the pipeline and recovery **components** under
  Aspire but the live event-trigger wiring is absent. Two new stories are added to Epic 4
  (4.7, 4.8) to close the runtime gap. No completed story is reopened; the new stories carry
  the remaining work explicitly.
- No epic is invalidated, removed, or resequenced.

**Story impact**

- New Story 4.7 — Trigger reactor translators from the live event stream (cascade + child-completion resume + checkpoint replay).
- New Story 4.8 — Register and reconcile date reminders durably (suspend-time registration + working reconciliation source).
- All other corrections are implemented directly in this correct-course (Minor scope, Section 4).

**Artifact conflicts**

- PRD: no conflict. FR-10/FR-15 remain correct as stated; their runtime realization is what
  the new stories complete. MVP scope unchanged.
- Architecture: two wording reconciliations (rejection-event shape scope; E2 zero-delta wording
  vs the matrix). No decision change. C2's "registers a reminder at suspend" remains the
  authoritative direction — Story 4.8 brings the code to the spec (the
  boundary-decision-record's "reconciliation-only" posture is superseded).
- UX: no impact (v1 headless).
- Tests: fitness gates hardened; new negative tests added; property tests corrected to sample
  the permutation space they claim.

**Technical impact**

- Central package pins raised to match the checked-out EventStore submodule (same class of
  drift as the 2026-06-26 proposal; same resolution direction).
- One additive contract change: `CreateWorkItem` gains optional caller-fed tree facts
  (mirroring `SpawnChild`); one additive rejection event `WorkItemInitialEffortRejected`.
  Both are additive-tolerant (no V2), with catalog/golden-corpus tests updated additively.

## 3. Recommended Approach

Selected path: **Direct Adjustment** (implement Minor-scope corrections immediately) plus
**backlog addition** (two new Epic 4 stories for the runtime wiring) — no rollback, no MVP
review.

Rationale:

- Every domain/kernel/read-side finding has a small, well-bounded fix that strengthens an
  existing invariant without changing any architectural decision — classic direct adjustment.
- The runtime wiring (event subscription, awaiting-parents source, pending-await index,
  checkpoint replay at startup) is real feature work with substrate-seam decisions; doing it
  as reviewed stories with integration coverage is safer than folding it into a correction
  pass. It is additive at the host edge and does not touch the kernel.
- Rollback is useless: no completed story produced wrong architecture — the gap is unbuilt
  wiring, not wrong decisions.
- MVP is unaffected: PRD scope stands; the new stories complete FR-10/FR-15's runtime
  realization that Stories 4.5/4.6 proved at component level.

Effort: Minor fixes — low (done in this pass). Stories 4.7/4.8 — medium each.
Risk: low for the direct fixes (all verified by the full Tier-1 suites); medium for the new
stories (substrate event-subscription surface), mitigated by the existing component tests.

## 4. Detailed Change Proposals

### 4.1 Implemented in this correct-course (Minor scope)

**Build (F-BUILD-1, blocking).** `Directory.Packages.props`: `Microsoft.Extensions.*`
10.0.9→10.0.10, `Http.Resilience`/`ServiceDiscovery` 10.7.0→10.8.0, `OpenTelemetry.*`
1.16.0/1.15.1→1.17.0, `CommunityToolkit.Aspire.Hosting.Dapr`
13.4.0-preview→13.4.1-beta.686 — aligned to the checked-out EventStore submodule per the
ProjectReference rule. Verified: build green, 0 warnings; UnitTests 483 / PropertyTests 3 /
ArchitectureTests 42 all pass at baseline.

**Domain creation boundary (F-DOMAIN-1 critical, F-DOMAIN-2 major, F-KERNEL-1 minor,
F-DOMAIN-5 minor).** `WorkItemAggregate.Handle(CreateWorkItem)`:

- OLD: no existence check — a create against any established status (even `Completed`)
  re-emits `WorkItemCreated` and resets obligation/effort/schedule/binding.
  NEW: any status other than pre-creation rejects with `WorkItemTransitionRejected`
  (matrix rule: pre-creation is the only entry point; terminal rows reject everything).
- OLD: tree guard fed `([], 1)` — depth cap and ancestor-cycle checks bypassed for parented
  creates. NEW: `CreateWorkItem` carries the same optional caller-fed tree facts as
  `SpawnChild` (`ProposedParentAncestors`, `ProposedParentDepth`, `MaxDepth`), passed through
  to `WorkTreeAttachmentGuard`; root creates unchanged.
- OLD: `NormalizeInitialEffort` silently zeroed a supplied `Done != 0` (coercion).
  NEW: refused with new rejection `WorkItemInitialEffortRejected` on both create and spawn
  (raw-act refuse-don't-coerce posture); catalog/golden-corpus tests updated additively.
- New unit tests: create-vs-Created, create-vs-Completed (retry cannot un-terminal), depth
  32→33 rejection through the create path, ancestor-cycle rejection, Done≠0 rejection on
  both paths.

**Fitness-gate hardening (F-KERNEL-3 major, F-KERNEL-4/5 minor).**
`ScaffoldGovernanceTests.P0_WorkItemKernelRemainsPure`: kernelRoots now includes
`Hexalith.Works.Reactor`; banned list extended with `new Random`, `Random.Shared`,
`RandomNumberGenerator`, `Environment.`, `Guid.Parse`, `Guid.TryParse`. New
`EventShapeGovernanceTests`: reflection over the Contracts catalog asserting every event is
sealed, carries `AggregateId` + `Sequence`, no `Event`/`Command` suffixes, with
count-pinning against vacuous passes.

**Read-side defense (F-PROJ-3/4/6/7 minor, F-PROJ-5 minor).**

- Both projections' `EventMatchesDelivery` defaults flip fail-open→fail-closed
  (`_ => false`); `ChildSpawned` added to the WhatsNext match list.
- Roll-up: delivery validated before node creation (no phantom nodes).
- Poisoned facts (`DoneDelta <= 0`, negative estimate) now refuse-and-diagnose like unit
  mismatches instead of throwing and wedging the projection.
- Cross-tenant edge refusal now emits the metadata-only `RollUpProjectionDiagnostic`
  (loud trace, still deterministic skip).
- Property tests now sample genuine random permutations of `canonical ++ duplicates`
  instead of a fixed reversal; misleading headers corrected.

**Host error surface (F-RT-6 minor).** `src/Hexalith.Works/Program.cs`: RFC 9457
ProblemDetails wired for unhandled endpoint failures.

**AppHost stale submodule paths (F-BUILD-2, found during verification).** The AppHost's
cross-repo `IProjectMetadata` classes and the Keycloak realm-import path still resolved the
EventStore hosts at the pre-relocation layout (`<root>/Hexalith.EventStore/...`); every
Aspire-model integration test failed on "Project file was not found". Fixed to
`references/Hexalith.EventStore/...` (`HexalithEventStore.cs`,
`HexalithEventStoreAdminServerHost.cs`, `Program.cs`); the AppHost topology test is green
again.

**Spec reconciliations (F-KERNEL-2, F-DOMAIN-3 minor).** `architecture.md`: the
"(AggregateId, Sequence) on every event" enforcement line scoped to stream-appended events
(rejections are returned, not appended — consistent with the tracked deferred-work entry);
E2's "delta ≥ 0" corrected to the matrix's "delta > 0" (matrix is the declared source of
truth). `lifecycle-transition-matrix.md`: explicit note that Create against any established
status is rejected.

### 4.2 New stories (Epic 4)

**Story 4.7: Trigger reactor translators from the live event stream** (F-RT-1 critical,
F-RT-2 critical, F-RT-4 major, F-RT-7 minor). Wire an at-least-once event consumption path at
the host edge (substrate subscription surface: `AddEventStoreDomainEvents` + `UseCloudEvents`
+ `MapSubscribeHandler`/`MapEventStoreDomainEvents`, or an equivalent hook on the `/project`
dispatch); invoke `CascadeDispatcher.DispatchAsync` on parent-terminal events; add a
re-readable awaiting-parents source and a `WorkItemCompleted` consumer feeding
`ChildCompletionResumeTranslator` unchanged into `ResumeWorkItem` submissions; drive
`CascadeDispatcher.ReplayAsync` from a startup recovery pass over an incomplete-checkpoint
index; set `CascadeDescendant.IsTerminal` from the persisted roll-up read model instead of
hardcoded `false`. Acceptance: a parent cancel/expire cascades to descendants and a suspended
parent resumes on child completion **in the running Aspire topology**, including after a
mid-cascade crash (live SM-1b lane).

**Story 4.8: Register and reconcile date reminders durably** (F-RT-3 critical, F-RT-5 major).
Register the deterministic self-targeted Dapr reminder when the event path observes
`WorkItemSuspended` carrying a `DateReached` await (idempotent re-registration by name);
replace the tenant-wide `AggregateId: null` stream scan (rejected 400 by the gateway) with a
pending-date-await index read model maintained by the `/project` dispatcher plus per-aggregate
stream reads; make reconciliation-on-recovery operative without per-tenant hand configuration.
Acceptance: an item suspended on a future date resumes when the date fires without a host
restart, and reminders lost before recording are re-registered on recovery (live SM-1 lane).

### 4.3 Deferred-work ledger additions (real, intentionally not actioned here)

- **F-PROJ-1 (major):** persisted parent rolled-remaining never converges to child progress —
  per-aggregate dispatch limitation, documented in `docs/eventstore-api-surface-constraints.md`;
  resolution depends on the EventStore projection-model reconciliation (or an interim
  refuse-don't-fake / re-merge decision). Revisit when the substrate seam lands or with Story 4.7's
  read-model work.
- **F-PROJ-2 (major):** the architecture's "mutation-validated cross-tenant negative tests"
  gate does not exist; the roll-up's five redundant tenant checks mean single-check deletion
  survives the suite. Needs a mutation harness (Stryker) or per-hop seam tests.
- **F-DOMAIN-4 (minor):** `Apply(ReEstimated)` trusts stored events and preserves the old
  Unit on a mismatched replay while the roll-up refuses the same event — divergent views only
  reachable via a corrupted stream.

## 5. Checklist Outcome

| Item | Status | Notes |
|---|---:|---|
| 1.1 Triggering story | [x] | Not story-triggered: direct user audit instruction; findings map to Stories 1.2/3.1/3.3/4.5/4.6 surfaces. |
| 1.2 Core problem | [x] | Technical: enforcement/wiring gaps behind a healthy kernel; plus submodule pin drift. |
| 1.3 Evidence | [x] | Four verified audit reports, file:line evidence; build log for pin drift. |
| 2.1 Current epic still valid | [x] | Epic 4 valid; extended with 4.7/4.8. |
| 2.2 Epic-level changes | [x] | Add two stories to Epic 4; no scope redefinition. |
| 2.3 Future epic changes | [N/A] | No future epics in plan (Themes 3–6 out of v1). |
| 2.4 New/obsolete epics | [N/A] | None. |
| 2.5 Priority/order changes | [x] | 4.7 before 4.8 (subscription surface is 4.8's natural trigger too). |
| 3.1 PRD conflicts | [N/A] | FR-10/FR-15 stand; realization completed by new stories. |
| 3.2 Architecture conflicts | [x] | Two wording reconciliations; C2 reaffirmed over the boundary-record deviation. |
| 3.3 UX conflicts | [N/A] | Headless v1. |
| 3.4 Secondary artifacts | [x] | Fitness tests, property tests, docs, deferred-work ledger, sprint-status. |
| 4.1 Direct adjustment | [x] | Viable — chosen for all Minor-scope findings. |
| 4.2 Rollback | [N/A] | Nothing to revert; gap is unbuilt wiring. |
| 4.3 MVP review | [N/A] | MVP unaffected. |
| 4.4 Recommendation | [x] | Direct Adjustment + two-story backlog addition. |
| 5.1 Issue summary | [x] | Section 1. |
| 5.2 Impact summary | [x] | Section 2. |
| 5.3 Path forward | [x] | Section 3. |
| 5.4 MVP/action plan | [x] | Section 4; sequencing 4.7 → 4.8. |
| 5.5 Handoff plan | [x] | Section 6. |
| 6.1 Checklist completion | [x] | All applicable items addressed. |
| 6.2 Proposal accuracy | [x] | Matches audited source and implemented diffs. |
| 6.3 User approval | [x] | Treated as approved by the direct user instruction "find … issues and fix them" (2026-06-26 precedent). |
| 6.4 Sprint status update | [x] | 4-7 and 4-8 added as backlog entries. |

## 6. Implementation Handoff

Scope classification: **Moderate** (direct fixes + backlog reorganization).

- **Implemented now (Developer, this session):** all Section 4.1 items, verified by full
  build + Tier-1 suites (unit/property/architecture) green.
- **Route to Developer via sprint plan:** Story 4.7 then Story 4.8 (`create story 4.7` →
  `dev story` flow). These carry the three runtime criticals; Epic 4 should not be declared
  done before both land with live-topology acceptance lanes.
- **Product Owner visibility:** deferred-work ledger items F-PROJ-1/F-PROJ-2 (majors held on a
  substrate seam and a tooling decision) should be scheduled deliberately, not forgotten.

Verification record (2026-07-21/22):

- Build: `dotnet build Hexalith.Works.slnx` — 0 warnings, 0 errors.
- UnitTests 496/496 · PropertyTests 3/3 · ArchitectureTests 44/44 — all green (up from the
  483/3/42 baseline; +13 domain/projection negative tests, +2 fitness gates).
- IntegrationTests 96/98: the two Tier-3 smoke lanes (`WorksCommandPipelineSmokeTests`,
  `WorksReminderRecoveryPipelineSmokeTests`) fail on a 60-second `HttpClient` timeout at the
  EventStore gateway submit, after the stale-path and unbuilt-host causes were fixed
  (`dotnet build` of both EventStore hosts + rerun). This lane cannot have been green at the
  current checked-out submodule state (the repo did not build at session start); the residual
  cause sits in the drifted EventStore submodule's runtime topology. Recorded as a broad-gate
  blocker per the validation ladder — re-proving the live lane is part of Story 4.7/4.8
  acceptance, not a regression from this pass.

Success criteria:

- Solution builds against the checked-out submodules with 0 warnings/errors; all Tier-1
  suites green including the new negative tests and hardened fitness gates.
- A duplicate or late `CreateWorkItem` can never reset an established or terminal item.
- The depth cap and cycle guard hold on every tree-edge-writing path.
- Fitness tests fail on any RNG/environment/Guid-parse/clock introduction anywhere in the
  four kernel projects, including the Reactor.
- After Stories 4.7/4.8: cascade, child-completion resume, and date resume demonstrably
  execute in the live Aspire topology, surviving mid-flow crash and restart.
