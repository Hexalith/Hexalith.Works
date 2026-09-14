# Sprint Change Proposal — Restore Implementation Readiness and Story Provenance (2026-09-14)

**Project:** works
**Prepared for:** Administrator
**Workflow:** `bmad-correct-course`
**Mode:** Batch
**Status:** Approved for implementation
**Primary evidence:** `_bmad-output/planning-artifacts/implementation-readiness.md`
**Carries forward:** approved `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-13.md`

## 1. Issue Summary

The 2026-09-13 implementation-readiness assessment failed because the planning set cannot currently
produce a deterministic executable backlog. The approved 2026-09-13 Epic 2 correction has not yet
been applied, Epic 1 still has no stable historical-to-forward mapping, and the selected
overrun-preserving re-estimation rule still conflicts with architecture and rewritten story text.

The readiness report records four findings:

1. **IR-01 — historical and forward story identities conflict.** Current `epics.md` assigns new scopes,
   titles, and acceptance criteria to historical `1.x` and `2.x` identities that already identify five
   delivered stories in each epic.
2. **IR-02 — re-estimation semantics conflict.** PRD FR-8/FR-9 and UX preserve cumulative `Done` and
   display `Done > Estimated` as overrun. Architecture AD-17 and rewritten Story 2.6 clamp `Done`.
   Rewritten Story 2.5 also rejects corrected `Done > Estimated`, contrary to the approved target rule.
3. **IR-03 — approved remediation has no executable stories.** Stories 5.1–5.4 have approved intent but
   no validated story artifacts or sprint keys.
4. **IR-04 — Epic 1 planning provenance remains unresolved.** The five delivered Epic 1 stories and
   the four rewritten Epic 1 stories reuse the same identity range without a durable mapping. The PRD's
   4,000-character Obligation bound also has no implementation story.

The required all-epic review found the same identity-collision class beyond the four recorded findings:
current `epics.md` reuses historical `3.x` and `4.x` identities for different target decompositions.
Sprint status and durable artifacts track six historical Epic 3 stories and nine historical Epic 4
stories, while the current planning document defines ten different stories in each range. Leaving those
collisions in place would make a later readiness PASS nondeterministic even after IR-01–IR-04 were fixed.

`sprint-status.yaml` was correctly preserved. No tracker generation or status transition should occur
until the source documents and new story artifacts are reconciled.

### Decisions proposed

1. Carry forward the approved 2026-09-13 decisions unchanged: preserve delivered history, preserve
   cumulative `Done` on re-estimation, keep rewritten material as forward scope, and create uniquely
   identified remediation work.
2. Restore the durable historical story identities for **all delivered Epics 1–4**, not only Epic 2.
3. Retain the current rewritten story bodies under provisional `F1-*` through `F4-*` labels. These are
   planning labels, not executable story IDs and not sprint-status keys.
4. Create and validate Epic 5 Stories 5.1–5.4 from the approved handoff.
5. Add Story 5.5 for the 4,000-character Obligation bound. This is preferred over removing the bound
   from the PRD and UX because it preserves the accepted target behavior and gives Epic 1 action item 6
   an explicit implementation owner.
6. Update sprint status only after the reconciled epic source and all five Epic 5 story artifacts exist.

## 2. Impact Analysis

### Epic impact

| Epic | Delivered authority | Current conflict | Required disposition |
| --- | --- | --- | --- |
| Epic 1 | Five completed stories recorded by `epic-1-context.md`, sprint status, story/spec artifacts, and the Epic 1 retrospective | Four rewritten stories reuse `1.1`–`1.4`; the Obligation bound is presented as delivered scope although it is not implemented | Restore the five-story history; move rewritten bodies to `F1-A`–`F1-D`; create Story 5.5 for the bound |
| Epic 2 | Five completed stories and a completed retrospective with a rejected acceptance verdict | Eight rewritten stories reuse `2.1`–`2.8`; planning and architecture disagree on re-estimation | Apply the approved 2026-09-13 mapping; move rewritten bodies to `F2-A`–`F2-H`; create Stories 5.1–5.4 |
| Epic 3 | Six completed stories plus retrospective | Ten rewritten target stories reuse `3.1`–`3.10` | Restore the six-story history; retain target material as `F3-A`–`F3-J` |
| Epic 4 | Stories 4.1–4.8 have durable artifacts; 4.8 is in review; 4.9 is backlog | Ten rewritten target stories reuse the range for different scopes | Restore the tracked nine-story decomposition; retain target material as `F4-A`–`F4-J`; keep Epic 4 in progress |
| Epic 5 | No historical identity | Approved remediation exists only in prose | Create five validated story artifacts, then add backlog keys atomically |

No delivered story is reopened, renumbered, or retroactively judged against later acceptance criteria.
Retrospective verdicts remain distinct from delivery status.

### Story impact

The restored historical map is:

| Story | Delivered title | Durable authority |
| --- | --- | --- |
| 1.1 | Set Up Initial Project from Starter Template | `1-1-set-up-initial-project-from-starter-template.md` |
| 1.2 | Create a Tenant-Scoped Work Item | `1-2-create-a-tenant-scoped-work-item.md` |
| 1.3 | Reference Sibling Modules Without Copying Data | `1-3-reference-sibling-modules-without-copying-data.md` |
| 1.4 | Expose Boundary Ports and Decision Record | `1-4-expose-boundary-ports-and-decision-record.md` |
| 1.5 | Link a Conversation After Work Item Creation | `spec-1-5-link-a-conversation-after-creation.md` |
| 2.1 | Define the Lifecycle State Machine | `2-1-define-the-lifecycle-state-machine.md` |
| 2.2 | Record Raw-Act Events and Replay State | `2-2-record-raw-act-events-and-replay-state.md` |
| 2.3 | Report Progress with Unit-Tagged Burn-Down | `2-3-report-progress-with-unit-tagged-burn-down.md` |
| 2.4 | Re-Estimate and Reschedule Work | `2-4-re-estimate-and-reschedule-work.md` |
| 2.5 | Complete, Cancel, Reject, and Expire Work | `2-5-complete-cancel-reject-and-expire-work.md` |
| 3.1 | Guard Tenant-Safe Work Tree Shape | `3-1-guard-tenant-safe-work-tree-shape.md` |
| 3.2 | Spawn Child Work from a Parent | `3-2-spawn-child-work-from-a-parent.md` |
| 3.3 | Maintain Recursive Roll-Up with Per-Child Sequence | `3-3-maintain-recursive-roll-up-with-per-child-sequence.md` |
| 3.4 | Preserve Heterogeneous Unit Subtotals | `3-4-preserve-heterogeneous-unit-subtotals.md` |
| 3.5 | Suspend and Resume on Await-Conditions | `3-5-suspend-and-resume-on-await-conditions.md` |
| 3.6 | Cascade Terminal Work Through Active Descendants | `3-6-cascade-terminal-work-through-active-descendants.md` |
| 4.1 | Bind Work to a Uniform Party Executor | `4-1-bind-work-to-a-uniform-party-executor.md` |
| 4.2 | Assign, Reassign, and Hand Off Work | `4-2-assign-reassign-and-hand-off-work.md` |
| 4.3 | Claim Queued Work with Single-Claim-Wins | `4-3-claim-queued-work-with-single-claim-wins.md` |
| 4.4 | Resolve the Tenant's What's Next Queue | `4-4-resolve-the-tenant-s-what-s-next-queue.md` |
| 4.5 | Prove the Command/Event Pipeline Under Aspire | `4-5-prove-the-command-event-pipeline-under-aspire.md` |
| 4.6 | Prove Reminder and Reactor Recovery | `4-6-prove-reminder-and-reactor-recovery.md` |
| 4.7 | Trigger Reactor Translators from the Live Event Stream | `4-7-trigger-reactor-translators-from-the-live-event-stream.md` |
| 4.8 | Register and Reconcile Date Reminders Durably | `4-8-register-and-reconcile-date-reminders-durably.md` |
| 4.9 | Migrate Works Hosting to the Platform Boundary | sprint-status key plus approved context |

The current rewritten ranges remain available without executable identities:

| Current range | Provisional range | Disposition |
| --- | --- | --- |
| 1.1–1.4 | `F1-A`–`F1-D` | Forward kernel-contract candidates; split delivered capability from undelivered deltas before promotion |
| 2.1–2.8 | `F2-A`–`F2-H` | Apply the detailed mapping already approved on 2026-09-13 |
| 3.1–3.10 | `F3-A`–`F3-J` | Forward registry, Reactor, reminder, and recovery candidates; map delivered pure-domain behavior explicitly |
| 4.1–4.10 | `F4-A`–`F4-J` | Forward projection, security, data, host, and harness candidates; map delivered/runtime work explicitly |

No provisional label may appear in `sprint-status.yaml`. Promotion requires a unique final story ID,
dependency review, validated story artifact, and traceability to the aligned PRD and architecture.

### Artifact conflicts

| Artifact | Conflict | Required change |
| --- | --- | --- |
| `epics.md` | Historical and forward decompositions share IDs across Epics 1–4 | Restore historical maps; relabel current rewritten bodies as non-executable candidates; add Epic 5 |
| PRD FR-2 | 4,000-character Obligation bound is target behavior without an executable story | Keep the bound and map it to Story 5.5; clarify delivered-versus-target provenance |
| PRD FR-8/FR-9 | Product rule is correct but still contradicted downstream | Add an explicit normative statement that ReEstimate never changes cumulative `Done` |
| Architecture AD-17 | Clamps `Done` and bounds correction to `0..Estimated` | Preserve `Done`, clamp only Remaining, permit visible overrun, and require replay/rebuild proof |
| Rewritten Stories 2.5/2.6 | Correction/re-estimation rules disagree with the approved product rule | Correct the candidate text before any future promotion |
| UX `DESIGN.md` / `EXPERIENCE.md` | Behavior already matches overrun, but source provenance predates the aligned authorities | Refresh provenance only after PRD/architecture edits; no redesign |
| Historical story artifacts | Correctly preserve delivered scope | Keep immutable; never rewrite their acceptance record to later target criteria |
| Epic 5 story artifacts | Missing | Create and validate Stories 5.1–5.5 before tracker changes |
| `sprint-status.yaml` | Correct historical keys, stale epic statuses, no Epic 5 | Preserve until story creation; then update atomically |
| Retrospective action ledger | Epic 1/2 actions exist; Epic 3/4 backfill remains incomplete | Preserve ownership; transition only with full evidence |

No planning `spec-*.md` exists. Implementation-artifact specs are evidence for delivered/hardening work,
not substitutes for the new Epic 5 story specifications.

### Technical impact

The only intended product-semantic change to current behavior is the already approved downward
re-estimation rule. Existing valid `ReEstimated` bytes do not change, but replayed state can change:
current code may reduce `Done`, while the target retains it. Aggregate state, projection folds, query
evidence, snapshots, golden reconstruction, and rebuild results must migrate together.

Story 5.5 changes admission for new creation commands but must not make historical payloads unreadable.
An already persisted `WorkItemCreated` with an Obligation longer than 4,000 characters remains valid
historical evidence during replay; the new limit applies at command admission. If a new rejection type
is required, reader/catalog/golden support must land before its producer.

The identity correction is a planning change, not a source-code rollback. It prevents generators from
silently assigning historical `done` state to undelivered forward work.

### UX impact

No v1 UI is added. Current UX already distinguishes overrun, non-terminal zero Remaining, completion,
reopen, and projection freshness correctly. Only source/provenance notes need refresh after the product
and architecture authorities align.

## 3. Recommended Approach

Use a **hybrid of Direct Adjustment and MVP/Delivery-Boundary Review**:

1. Restore historical story identities and mark the current rewrite as forward target scope.
2. Align PRD and architecture on overrun-preserving re-estimation and replay migration.
3. Create five uniquely identified remediation stories.
4. Update sprint status atomically from the reconciled sources.
5. Rerun readiness and sprint planning.

**Do not roll back.** Reverting completed stories would remove working behavior and destroy provenance
without resolving the forward contract. Historical artifacts remain evidence; remediation is additive
except for the explicit replay interpretation of downward re-estimation.

**MVP effect:** the delivered June baseline remains delivery history. Requirements added later remain
target v1 scope until uniquely identified stories deliver them. This changes no product vision; it
prevents target intent from being mistaken for shipped evidence.

**Scope classification:** Major — the correction spans product semantics, replay behavior, all epic
identity ranges, story creation, architecture, and sprint tracking. PM and Architect ownership is
required before implementation.

**Estimated effort and timeline:**

- artifact and provenance reconciliation: 2–3 focused working days;
- Epic 5 story creation and validation: 1–2 focused working days;
- Epic 5 implementation and evidence: approximately 8–13 engineering days;
- readiness/sprint regeneration: less than one focused day after all gates pass.

**Risk:** High for replay/migration semantics and planning identity; Medium for lifecycle authority and
Obligation admission; Low-to-Medium for overflow and catalog verification. Stable historical maps,
reader-first rollout, and atomic tracker generation are the principal controls.

## 4. Detailed Change Proposals

### 4.1 Epics — separate delivered history from forward targets

**File:** `_bmad-output/planning-artifacts/epics.md`

**OLD:**

> The document presents four current epics with rewritten `1.x`–`4.x` story identities as the complete
> executable breakdown, although sprint status and durable artifacts assign those identities to
> different delivered stories.

**NEW:**

> ## Planning Authority and Provenance
>
> Epics 1–4 below preserve the delivered story identities recorded by sprint status, durable story
> artifacts, contexts, and retrospectives. Later target decompositions are retained under `F1-*`
> through `F4-*` provisional labels. An `F*` label is not an executable story ID, cannot inherit a
> historical status, and cannot enter sprint status until a uniquely identified validated story is
> created from aligned product and architecture authorities.

Apply the historical map in §2, retaining the full contemporaneous acceptance record by reference to
the durable story artifacts. Relabel the existing rewritten bodies in their current order as
`F1-A`–`F1-D`, `F2-A`–`F2-H`, `F3-A`–`F3-J`, and `F4-A`–`F4-J`. Relabel the current requirements
inventory and coverage map as **forward target** inventory/coverage; add a separate delivered-baseline
map so current target FRs are not represented as retroactive acceptance criteria.

Add Epic 5 as the only new executable remediation epic. Preserve every useful rewritten story body;
the change is identity quarantine and traceability, not deletion.

### 4.2 PRD — bind provenance, overrun, and Obligation delivery

**File:** `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`

**OLD:**

> A re-estimate below Done clamps Remaining to 0 and leaves Done > Estimated as the record of the
> over-run.

This rule is correct but lacks an explicit downstream-authority and delivery-boundary statement.

**NEW:**

> **Re-estimation never changes cumulative Done.** `ReEstimate` replaces `Estimated`, derives
> `Remaining = max(Estimated - Done, 0)`, preserves Unit and Status, and never completes or reopens the
> Work Item. Only the separately audited `CorrectProgress` act may change cumulative `Done`.
> `Done > Estimated` is valid visible overrun.

Add:

> The durable Epic 1–4 story map records the delivered baseline. Requirements and acceptance details
> added after a story's delivery are target scope until a uniquely identified story implements them;
> they do not retroactively alter historical acceptance evidence.

Keep FR-2's 4,000-character limit and add traceability to Story 5.5. Record this proposal in the
amendment history only when approved and applied.

### 4.3 Architecture — align numeric authority and migration gates

**File:** `_bmad-output/planning-artifacts/architecture.md`

**OLD (AD-17):**

> CorrectProgress absolute Done is within 0..Estimated; Estimated is nonnegative; Unit follows AD-04;
> ReEstimate clamps Done and never completes.

**NEW:**

> Estimated is nonnegative and Unit follows AD-04. `ReEstimate` preserves cumulative `Done`, derives
> `Remaining = max(Estimated - Done, 0)`, preserves Status, and never completes or reopens the item.
> `Done > Estimated` is a valid overrun. Only `CorrectProgress` may replace cumulative `Done`; its
> product bound is `Done >= 0`, with completion/reopen behavior governed by FR-8.

Add the approved migration rule:

> Changing valid `ReEstimated` replay from clamp-`Done` to preserve-`Done` is a semantic migration.
> Before rollout, replay representative pre-change streams through aggregate and projection folds,
> compare live and rebuilt state, rebuild affected disposable projections, and document cache/snapshot
> invalidation. Aggregate and read-side interpretations must change atomically.

Retain the approved additions for saturating progress arithmetic, event-ordinal headroom, singular
executable lifecycle policy, and assembly-derived durable-catalog completeness from the 2026-09-13
proposal. Extend the Lifecycle Migration readiness gate to include Obligation admission/replay
compatibility and the five validated Epic 5 stories.

### 4.4 Story specifications — create Epic 5

**OLD:**

> The remediation requirements exist in the approved proposal and retrospective action ledger but have
> no validated story artifacts or executable keys.

**NEW:**

#### Epic 5: Stabilize the Work Item Contract and Planning Record

##### Story 5.1: Make Progress and Event Ordinals Overflow-Safe

Carry forward the complete acceptance requirements from the approved 2026-09-13 proposal: saturating
progress without overflowing addition; preflight headroom for one- and two-event results; no partial
result; focused `decimal.MaxValue`, `long.MaxValue`, and `long.MaxValue - 1` coverage.

##### Story 5.2: Make the Lifecycle Authority Executable and Singular

Carry forward the approved requirements: one executable source for legality, outcome, target Status,
and stable AttemptedAct; handlers consume/assert its target; Markdown is generated or mechanically
cell-compared; all ReEstimate statuses and rejection labels are covered.

##### Story 5.3: Preserve Re-Estimate Overrun and Enforce Bounded Act Notes

Carry forward the approved requirements: downward ReEstimate preserves `Done`; aggregate, projection,
query, replay, and rebuild agree; note-bearing acts apply the common 0–1,000-character policy; invalid
notes do not mutate or leak into diagnostics.

##### Story 5.4: Derive Durable Catalog Completeness from Contracts

Carry forward the approved requirements: reflection derives all decorated concrete durable types;
each has exactly one discriminator, sample, and required golden representations; checks are
bidirectional and demonstrably fail for a missing entry; historical bytes remain unchanged.

##### Story 5.5: Enforce the Obligation Bound Without Rewriting History

**Owner:** Amelia (Dev), with John (PM) and Winston (Architect)
**Source action:** `epic-1-retro-item-6-reconcile-epics-md-with-the-as-built-epi`

Acceptance requirements:

- New `CreateWorkItem` admission accepts a trimmed non-empty Obligation of at most 4,000 characters and
  returns the defined domain rejection without mutation when the bound is exceeded.
- The command validator, aggregate, contract catalog, golden payloads, headless evidence, and tests use
  the same limit and do not echo rejected content in logs or ProblemDetails.
- Historical `WorkItemCreated` payloads remain deserializable and replayable even when their previously
  accepted Obligation exceeds the new admission bound.
- Any new rejection type follows reader/validator/catalog/golden-first rollout and depends on Story 5.4.
- Focused boundary tests cover 0/whitespace, 1, 4,000, and 4,001 characters, including trimming and
  replay compatibility.

Dependencies: 5.1 and 5.4 may proceed independently; 5.2 precedes final 5.3 integration; 5.3 depends on
5.4 for any changed durable evidence; 5.5 depends on 5.4 before any new rejection producer is enabled.
Epic 1 action item 2 must be green before any Epic 5 story is marked done. Epic 1 item 13 must close
before unattended implementation resumes. Epic 1 item 4 gates any new correction/handoff producer.

### 4.5 UX — refresh authority only

**Files:** `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md` and
`EXPERIENCE.md`

**OLD:** source precedence predates the approved re-estimation and historical/target boundary.

**NEW:** refresh source/provenance notes after PRD and architecture alignment. Preserve current overrun,
non-terminal-zero, correction/reopen, freshness, and accessible Burn-Down behavior unchanged. Keep all
human-facing implementation outside v1.

### 4.6 Sprint status — update only from validated sources

**File:** `_bmad-output/implementation-artifacts/sprint-status.yaml`

Keep the file unchanged during this proposal workflow and during artifact reconciliation. After
Stories 5.1–5.5 exist and validate, perform one atomic update:

```yaml
development_status:
  epic-1: done
  epic-2: done
  epic-3: done
  epic-4: in-progress
  epic-5: backlog
  5-1-make-progress-and-event-ordinals-overflow-safe: backlog
  5-2-make-the-lifecycle-authority-executable-and-singular: backlog
  5-3-preserve-re-estimate-overrun-and-enforce-bounded-act-notes: backlog
  5-4-derive-durable-catalog-completeness-from-contracts: backlog
  5-5-enforce-the-obligation-bound-without-rewriting-history: backlog
  epic-5-retrospective: optional
```

Preserve every historical story key and current status. Epic 4 remains `in-progress` because Story 4.8
is in review and Story 4.9 is backlog. Do not add any provisional `F*` key.

Action transitions occur only with full evidence:

- Epic 1 item 6 may become `done` after the Epic 1 map and Story 5.5 artifact are present.
- Epic 2 item 16 may become `done` after epics, PRD, architecture, story, and tracker reconciliation.
- Epic 1 item 8 remains open until Epic 3/Epic 4 retrospective actions are backfilled and all specified
  status repairs are complete.
- Epic 1 items 2, 4, and 13 and Epic 2 items 14, 15, and 17 remain open until their own evidence passes.

### 4.7 Required remediation sequence

1. Approve this consolidated proposal.
2. Apply PRD and architecture corrections and record their provenance.
3. Reconcile `epics.md` across Epics 1–4 and preserve the rewrite under provisional labels.
4. Create and validate Stories 5.1–5.5 with `bmad-create-epics-and-stories`.
5. Update sprint status and eligible action-item states atomically from the reconciled sources.
6. Resolve Epic 1 item 2 before accepting any Epic 5 story as done; resolve item 13 before unattended
   execution.
7. Implement Epic 5 in dependency order and prove replay/rebuild migration before durable rollout.
8. Rerun implementation readiness.
9. Run sprint planning only after readiness passes.

## 5. Implementation Handoff

**Scope:** Major — fundamental replan and replay-semantic alignment.

**Route to:** John (PM), Winston (Architect), Product Owner/Sprint Manager, Amelia and Charlie (Dev),
Dana (QA), and Administrator.

- **John (PM):** approve the delivered-versus-target boundary, restore the historical maps, own Story
  5.5's product traceability, and assign unique final IDs to promoted future work.
- **Winston (Architect):** amend AD-17 and migration/readiness gates; verify overrun, Obligation replay,
  and durable-compatibility rules.
- **Product Owner / Sprint Manager:** create and validate Stories 5.1–5.5; update tracker state only
  after all artifacts exist.
- **Amelia (Dev):** implement Stories 5.1, 5.3, and 5.5 after their gates are met.
- **Charlie (Senior Dev):** implement Story 5.2 and preserve compatible durable act diagnostics.
- **Dana (QA):** implement Story 5.4 and own assembly-derived compatibility and failure proofs.
- **Administrator:** preserve action provenance, close or supervise around skipped-retrospective policy,
  and rerun readiness/sprint planning in the required order.

### Success criteria

- Every historical `1.x`–`4.x` key maps to exactly one delivered/tracked story and durable artifact.
- Every rewritten story body remains available under a non-executable provisional label and cannot
  inherit historical status.
- PRD, architecture, candidate stories, aggregate state, projections, queries, replay, rebuild, UX,
  and tests preserve cumulative `Done` across downward re-estimation.
- Remaining is nonnegative; overrun is visible; ReEstimate never completes, reopens, or changes Status.
- New creation enforces the 4,000-character Obligation limit while historical payloads remain readable.
- Stories 5.1–5.5 exist and validate before any Epic 5 tracker key is added.
- `sprint-status.yaml` changes once, atomically, with Epics 1–3 marked done, Epic 4 preserved in progress,
  and no provisional key.
- The rerun implementation-readiness verdict is PASS before sprint planning regenerates tracking.

## 6. Checklist Outcome

| Item | Status | Finding |
| --- | ---: | --- |
| 1.1 Triggering story | [x] | No single story; the failed-readiness report exposed planning identity and authority conflicts across historical and target work. |
| 1.2 Core problem | [x] | Misaligned planning authorities and reused story identities prevent deterministic implementation planning. |
| 1.3 Evidence | [x] | Readiness report, approved prior proposal, PRD, epics, architecture, UX, sprint status, story artifacts, contexts, and retrospectives. |
| 2.1 Current epic viability | [x] | Delivered Epics 1–3 remain valid historical outcomes; Epic 4 remains active; current rewritten identities are not executable. |
| 2.2 Epic-level changes | [x] | Restore historical Epics 1–4, quarantine target rewrite, and add Epic 5. |
| 2.3 Remaining epic impact | [x] | The collision also affects `3.x`/`4.x`; all four ranges must be reconciled in one pass. |
| 2.4 New/obsolete epics | [x] | Epic 5 is new; no delivered epic is obsolete; rewritten decompositions become candidates. |
| 2.5 Priority/order | [x] | Authority alignment and identity restoration precede story creation and tracker regeneration. |
| 3.1 PRD conflicts | [x] | Keep overrun and Obligation rules; add delivery provenance and executable traceability. MVP remains achievable. |
| 3.2 Architecture conflicts | [x] | AD-17 and lifecycle/replay/readiness gates require correction. |
| 3.3 UX conflicts | [x] | Behavior already aligns; provenance refresh only. |
| 3.4 Other artifacts | [x] | Sprint status, retrospectives, action ledger, durable stories, lifecycle matrix, catalog/goldens, and rebuild evidence are affected. |
| 4.1 Direct Adjustment | [x] Viable | Necessary for authority alignment, historical maps, story creation, and tracker repair; Medium-to-High effort. |
| 4.2 Potential Rollback | [N/A] Not viable | It would destroy valid delivery evidence and would not reconcile forward requirements. |
| 4.3 MVP Review | [x] Viable | Separates delivered baseline from target v1 without dropping the product vision. |
| 4.4 Recommended path | [x] | Hybrid Direct Adjustment + MVP/Delivery-Boundary Review. |
| 5.1 Issue summary | [x] | Trigger, evidence, four readiness findings, and additional all-epic collision documented. |
| 5.2 Impact and adjustments | [x] | Epic, story, artifact, technical, and UX impacts identified. |
| 5.3 Approach and rationale | [x] | No rollback; stable history plus additive remediation and explicit migration. |
| 5.4 MVP/action plan | [x] | Delivered-versus-target boundary and ordered nine-step remediation defined. |
| 5.5 Handoff plan | [x] | PM, Architect, PO/SM, Dev, QA, and Administrator responsibilities assigned. |
| 6.1 Checklist review | [x] | All applicable analysis items addressed. |
| 6.2 Proposal accuracy | [x] | Cross-checked against the five readiness inputs and historical tracker/artifact evidence. |
| 6.3 User approval | [x] | Approved by Administrator on 2026-09-14. |
| 6.4 Sprint-status update | [!] | Intentionally deferred until reconciled sources and five validated story artifacts exist. |
| 6.5 Next steps/handoff | [x] | Recipients, order, gates, and success criteria are explicit. |

## 7. Approval Record

**Decision:** Approved for implementation.
**Approved by:** Administrator.
**Date:** 2026-09-14.
**Conditions or revisions:** Execute the remediation sequence in §4.7. Keep `sprint-status.yaml`
unchanged until `epics.md`, the PRD, architecture, and validated Story 5.1–5.5 artifacts agree; do not
promote provisional `F*` labels or reuse historical story identities.

Approval authorizes the implementation handoff and ordered artifact corrections above. It does not
authorize premature sprint-status generation, code implementation, commit, or push.

## 8. Workflow Execution Log

| Date | Event | Result |
| --- | --- | --- |
| 2026-09-14 | Failed-readiness trigger accepted | IR-01–IR-04 entered scope; sprint status confirmed preserved. |
| 2026-09-14 | Batch artifact discovery completed | PRD, epics, architecture, UX, prior proposal, tracker, contexts, story artifacts, and retrospectives reviewed. |
| 2026-09-14 | All-epic impact analysis completed | Additional historical/target collisions identified in Epic 3 and Epic 4. |
| 2026-09-14 | Path-forward evaluation completed | Hybrid Direct Adjustment + MVP/Delivery-Boundary Review selected; rollback rejected. |
| 2026-09-14 | Sprint Change Proposal prepared | Major scope; submitted for Administrator review. |
| 2026-09-14 | Administrator approval received | Proposal finalized and approved without revision. |
| 2026-09-14 | Implementation handoff recorded | Routed to PM, Architect, Product Owner/Sprint Manager, Dev, QA, and Administrator subject to §4.7 gates. |
