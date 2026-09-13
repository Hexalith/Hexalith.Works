# Sprint Change Proposal — Epic 2 Historical Baseline and Lifecycle Reconciliation (2026-09-13)

**Project:** works  
**Prepared for:** Administrator  
**Workflow:** `bmad-correct-course`  
**Mode:** Batch  
**Status:** Approved for implementation  
**Primary evidence:** `_bmad-output/implementation-artifacts/epic-2-retro-2026-09-13.md`

## 1. Issue Summary

The Epic 2 retrospective established two facts that the current planning set does not represent
together:

1. Epic 2 historically delivered **Reliable Single-Item Lifecycle and Burn-Down** through five stories,
   all recorded `done`, with one commit and one durable story artifact per story.
2. The working `epics.md` replaced that decomposition with a materially different lifecycle plan and
   reused the `2.x` identifiers. At the retrospective evidence boundary this was a seven-story rewrite
   (`2.1`–`2.7`). The current working tree now also contains Story `2.8`, so the live drift has grown to
   eight rewritten stories. That additional item does not invalidate the retrospective; it is another
   item behind the same provenance boundary.

The replacement text mixes delivered behavior, retrospective remediation, and unimplemented contracts
such as `CorrectProgress`, `ProgressCorrected`, `WorkItemReopened`, `WorkItemHandedOff`, completion
provenance, and schedule witnesses. It therefore cannot be used as either the historical record or an
implementation-ready backlog.

The retrospective also rejected Epic 2 against its declared criteria despite its completed delivery
status. Three implementation gaps and one planning gap remain open:

- progress and payload sequencing are not overflow-safe;
- lifecycle policy is duplicated instead of executable from one authority;
- the planning contract conflicts with delivered history and with itself;
- durable-catalog completeness is proven from a hand-authored list rather than the Contracts assembly.

The planning conflict includes a product decision that must be made before further lifecycle work:
downward re-estimation either preserves cumulative `Done` as visible overrun or clamps `Done` to the new
`Estimated` value. The PRD and UX preserve overrun; AD-17, current code/tests, and rewritten Story 2.6
clamp `Done`.

### Decisions proposed

1. **Preserve the delivered five-story Epic 2 as immutable historical truth.** Delivery status and
   retrospective verdict remain distinct: the five stories are `done`; the retrospective is `done`
   with a `rejected` acceptance verdict and open remediation.
2. **Classify the seven-story rewrite as future scope, not a replacement.** Preserve its content as a
   candidate future-scope section without executable `2.x` identifiers. Apply the same classification
   to the additional Story 2.8.
3. **Preserve cumulative `Done` on downward re-estimation.** `ReEstimate` changes the plan only;
   `CorrectProgress` is the only planned act that may change historical cumulative progress.
   `Remaining = max(Estimated - Done, 0)`, `Done > Estimated` is visible overrun, and re-estimation never
   changes Status or emits completion.
4. **Create a new remediation epic with new identifiers.** Do not reopen or renumber Stories 2.1–2.5,
   and do not implement future correction/handoff contracts under reused historical identifiers.

## 2. Impact Analysis

### Epic impact

- **Historical Epic 2:** remains the five-story delivery at commits `fb757f2`, `ccf73c5`, `cbf1cba`,
  `1814301`, and `c1ba6bb`. The current story artifacts remain unchanged.
- **Rewritten Epic 2:** becomes non-executable future scope. Its useful deltas are mapped either to the
  remediation epic or to later extension candidates; already-delivered capabilities are not planned a
  second time.
- **New Epic 5 — Stabilize the Single-Item Lifecycle Contract:** contains four remediation stories,
  one for each open Epic 2 retrospective action area. It is the only new executable epic proposed here.
- **Future lifecycle extension:** progress correction/reopen, completion provenance, active handoff,
  schedule witnesses, and strengthened query authorization remain future candidates until their
  architecture and dependency gates are satisfied. They receive no `2.x` sprint keys.
- **Epics 1, 3, and 4:** are not redefined by this proposal. Existing completed stories and evidence
  remain intact. Cross-epic status repair and prerequisite actions are handled explicitly below.

### Story impact

- Stories 2.1–2.5 keep their historical titles, acceptance criteria, artifacts, commits, and `done`
  status.
- No Story 2.6, 2.7, or 2.8 is added to sprint status under historical Epic 2.
- Four new remediation stories are proposed as 5.1–5.4.
- Correction/reopen and handoff are retained as provisional future stories with new identifiers to be
  finalized only after the architecture and identity gates are closed.

### Artifact conflicts

| Artifact | Current conflict | Required disposition |
| --- | --- | --- |
| `epics.md` | Reuses Epic 2 and `2.x` identifiers for a different decomposition; currently contains eight rewritten stories although the retrospective captured seven. | Restore the five-story historical Epic 2; move all rewritten content to an explicitly non-executable future-scope section; add Epic 5 remediation. |
| PRD FR-3/FR-8/FR-9 | Correctly preserves `Done > Estimated`, but does not explicitly separate the delivered baseline from the expanded target scope in its amendment boundary. | Retain overrun semantics and add the approved decision/provenance note plus the delivered-versus-target boundary. |
| Architecture AD-05/AD-17 | Requires `Done <= Estimated`, bounds correction to `0..Estimated`, and clamps `Done` on re-estimate. | Permit overrun, clamp only `Remaining`, preserve `Done`, and specify replay/rebuild consequences. |
| Architecture AD-17 / lifecycle matrix | Claims one authority while handlers and token-only doc checks duplicate policy. | Make the policy executable and mechanically bind the rendered matrix and all consumers. |
| Historical story artifacts | Correctly describe the delivered five-story scope, including the original clamp implementation in Story 2.4. | Preserve unchanged as historical evidence; do not retroactively rewrite their acceptance records. |
| New story specifications | No unique implementation stories own the four retrospective actions. | Create Stories 5.1–5.4 with explicit dependencies and acceptance evidence. |
| `sprint-status.yaml` | Historical Epic 2 remains `in-progress` although all five stories and its retrospective are `done`; no remediation epic exists. | Set historical Epic 2 to `done`, preserve 2.1–2.5, add Epic 5 backlog entries, and retain action-item provenance. |
| UX `DESIGN.md` / `EXPERIENCE.md` | Already presents overrun and non-terminal zero correctly, but its source boundary predates this decision. | No behavioral redesign; refresh provenance after PRD/architecture alignment before any future surface work. |

No whole planning `spec-*.md` exists. The implementation-artifact specs for corrupted negative and
mismatched-unit `ReEstimated` replay are completed hardening evidence, not substitutes for the missing
forward specification of overrun-preserving valid replay.

### Technical impact

Preserving `Done` changes the interpretation of a valid historical `ReEstimated` event when its new
estimate is below the previously accumulated work. Event bytes need not change, but replayed state may:
current code reduces `Done`; the proposed rule retains it. Aggregate state, roll-up folds, query payloads,
snapshots, golden reconstruction, and rebuild expectations must therefore move together. A rebuild or
migration proof is required before the rule is accepted for existing durable streams.

The overflow and lifecycle-authority work changes no intended product behavior, but it affects shared
paths used by many commands. Sequence exhaustion must fail before any one- or two-event result is
constructed, and the transition matrix must cease being an independently maintained policy copy.

The future correction and handoff contracts would add durable types. They remain blocked on catalog
completeness, reader-first compatibility, and the existing cross-command envelope/payload identity work.

### Epic 1 dependencies and coupled actions

The Epic 2 retrospective explicitly keeps these Epic 1 items as the owners of shared prerequisites; no
duplicate Epic 2 action is created:

| Epic 1 action | Relationship to this proposal | Gate |
| --- | --- | --- |
| `epic-1-retro-item-2-turn-the-architecture-fitness-gate-green` | A red architecture lane cannot certify Stories 5.1–5.4. | May proceed in parallel, but must be green before any Epic 5 story is marked `done`. |
| `epic-1-retro-item-4-extend-the-envelope-payload-identity-che` | New `CorrectProgress` and `HandoffWorkItem` wrappers must inherit the all-command identity rule rather than widen the one-wrapper exception. | Must close before any new lifecycle command producer is implemented. |
| `epic-1-retro-item-13-escalate-a-skipped-retrospective-instead` | The missing Epic 2 retrospective remained invisible for three months because the unattended loop treated a stalled retro as skippable. | Must close before unattended implementation resumes; supervised planning/remediation is still allowed. |

Two other Epic 1 actions are coupled but are not substituted by this proposal:

- Item 6 remains the owner of the broader Epic 1 planning-history reconciliation. Applying this
  proposal completes only its Epic 2 overlap.
- Item 8 remains the owner of full action-channel repair and Epic 3/Epic 4 action backfill. This
  proposal may correct Epic 2's status, but item 8 does not close until its full acceptance is met.

## 3. Recommended Approach

Use a **hybrid of Direct Adjustment and MVP Review**:

1. Re-establish the delivered five-story Epic 2 as the historical baseline.
2. Classify the rewritten lifecycle plan as future implementation scope with provisional identifiers.
3. Align the PRD and architecture on overrun-preserving re-estimation.
4. Create and execute a uniquely numbered remediation epic before any future lifecycle extension.

**Do not roll back.** Reverting the delivered five stories would destroy working behavior and provenance
without resolving the specification conflict. The corrective work is additive except for the deliberate,
explicit replay-semantic change for downward re-estimation.

**MVP effect:** the delivered June baseline remains historical v1 delivery evidence. The PRD's later
correction, handoff, security, and recovery amendments are an expanded target scope, not retroactive
proof that those features shipped in Epic 2. Stakeholders may still call that target “v1,” but sprint
artifacts must label it undelivered until new stories close.

**Scope classification:** Major — product semantics, replay behavior, planning identity, architecture,
story decomposition, and sprint tracking all change, and PM/Architect approval is required.

**Provisional effort and timeline:**

- planning/architecture reconciliation: 1–2 focused working days;
- Epic 1 prerequisite closure relevant to this change: 1–3 working days, depending on the orchestrator
  change and identity-wrapper breadth;
- Epic 5 remediation and verification: one focused sprint, approximately 6–10 engineering days;
- future correction/handoff extensions: not estimated until Epic 5 and their platform/identity gates
  are green.

**Risk:** High for replay/migration semantics; Medium for lifecycle centralization; Low-to-Medium for
overflow and catalog verification. The proposal reduces risk by separating shipped history,
remediation, and future feature scope.

## 4. Detailed Change Proposals

### 4.1 Epics — restore history and quarantine the rewrite as future scope

**File:** `_bmad-output/planning-artifacts/epics.md`  
**Sections:** Epic List; Epic 2; new historical/future-scope provenance note; new Epic 5.

OLD:

> ## Epic 2: Move Work Reliably from Assignment to Completion
>
> Stories 2.1–2.7 redefine the delivered identifiers, and the current working tree adds Story 2.8.

NEW:

> ## Epic 2: Reliable Single-Item Lifecycle and Burn-Down — Delivered Baseline
>
> Epic 2 was delivered through Stories 2.1–2.5 and is preserved as historical truth. Its acceptance
> verdict is recorded separately by the 2026-09-13 retrospective. Later target-scope material does
> not replace, rename, or renumber these stories.

Restore the contemporaneous five-story Epic 2 text from commit `c1ba6bb` and add this provenance map:

| Historical story | Delivered title | Commit | Durable artifact |
| --- | --- | --- | --- |
| 2.1 | Define the Lifecycle State Machine | `fb757f2` | `2-1-define-the-lifecycle-state-machine.md` |
| 2.2 | Record Raw-Act Events and Replay State | `ccf73c5` | `2-2-record-raw-act-events-and-replay-state.md` |
| 2.3 | Report Progress with Unit-Tagged Burn-Down | `cbf1cba` | `2-3-report-progress-with-unit-tagged-burn-down.md` |
| 2.4 | Re-Estimate and Reschedule Work | `1814301` | `2-4-re-estimate-and-reschedule-work.md` |
| 2.5 | Complete, Cancel, Reject, and Expire Work | `c1ba6bb` | `2-5-complete-cancel-reject-and-expire-work.md` |

Move the rewritten content without deleting it under:

> ## Future Scope Candidate: Expanded Single-Item Lifecycle
>
> This material is a post-delivery re-baseline candidate. It is non-executable until reconciled with
> the PRD, architecture, historical story map, and sprint status. Labels `F2-A` onward are provisional
> planning labels, not story IDs.

Use this disposition map:

| Current rewritten item | Classification and new home |
| --- | --- |
| 2.1 Enforce One Authoritative Lifecycle | Historical 2.1 capability plus Epic 5 Story 5.2 remediation; label `F2-A` in the candidate section. |
| 2.2 Assign, Queue, and Claim Through One Binding | Primarily delivered across historical 2.1/2.2 and Stories 4.2/4.3; retain only undelivered authorization deltas as `F2-B`. |
| 2.3 Track Effort in One Immutable Unit | Historical 2.3/2.4 plus Epic 5 Story 5.3 overrun correction; label remaining target deltas `F2-C`. |
| 2.4 Report Progress and Complete Work | Historical 2.3/2.5 plus future completion-provenance delta; label `F2-D`. |
| 2.5 Correct Progress and Reopen Eligible Work | Undelivered future scope; provisional future extension story, label `F2-E`. |
| 2.6 Re-Estimate and Reschedule Work | Historical 2.4 plus Epic 5 Story 5.3; schedule-witness additions remain future scope, label `F2-F`. Replace its clamp rule before it can become a story. |
| 2.7 Hand Off Active Work Without Changing Its State | Undelivered future scope; provisional future extension story, label `F2-G`. |
| 2.8 Discover the Next Work Deterministically | Outside the retrospective's cited seven-story range; map delivered behavior to Story 4.4 and retain only undelivered query/authorization deltas as `F2-H`. |

**Rationale:** this keeps both bodies of work, but only one of them is allowed to answer “what Epic 2
delivered?” New work cannot inherit a historical `done` status accidentally.

### 4.2 PRD — make overrun preservation and scope provenance explicit

**File:** `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`  
**Sections:** amendment history, FR-3, FR-8, FR-9, MVP scope boundary.

The existing FR-8/FR-9 rule is retained:

> A re-estimate below Done clamps Remaining to 0 and leaves Done > Estimated as the record of the
> over-run.

Add this normative clarification:

> **Re-estimation never changes cumulative Done.** `ReEstimate` replaces the plan (`Estimated`) and
> derives `Remaining = max(Estimated - Done, 0)` while preserving Status. Only the separately audited
> `CorrectProgress` act may change cumulative Done. A downward re-estimate is therefore not a progress
> correction and cannot erase visible overrun.

Add this scope/provenance clarification:

> The five-story Epic 2 delivery is the historical June baseline. Requirements added on 2026-09-08
> and 2026-09-13—including correction/reopen, completion provenance, active Handoff, and schedule
> witnesses—are forward target requirements until uniquely identified stories deliver them. They do
> not retroactively change the completed Epic 2 acceptance record.

Update the amendment history with this approved proposal when applied.

**Rationale:** the PRD already carries the preferred product rule. The edit removes any remaining
possibility that “clamped Remaining” means “clamped Done” and stops later requirements from being read
as already delivered.

### 4.3 Architecture — align effort invariants, lifecycle authority, and compatibility gates

**File:** `_bmad-output/planning-artifacts/architecture.md`  
**Sections:** AD-05, AD-17, AD-29, Capability Map, Readiness Gates.

#### AD-05 / AD-17 — downward re-estimation

OLD:

> CorrectProgress absolute Done is within 0..Estimated; Estimated is nonnegative; Unit follows AD-04;
> ReEstimate clamps Done and never completes.

NEW:

> Estimated is nonnegative and Unit follows AD-04. `ReEstimate` preserves cumulative Done, derives
> `Remaining = max(Estimated - Done, 0)`, never changes Status, and never completes or reopens the item.
> `Done > Estimated` is a valid visible overrun. Only `CorrectProgress` may replace cumulative Done;
> its approved product bound is `Done >= 0`, with completion/reopen behavior governed by FR-8.

AD-05 must stop treating `Done <= Estimated` as a universal value-object invariant. Command admission
may still reject an initial create payload whose `Done` is nonzero; valid replay and valid re-estimation
must be able to represent overrun.

Add a replay migration rule:

> Changing valid `ReEstimated` replay from clamp-`Done` to preserve-`Done` is a semantic migration.
> Before rollout, replay representative pre-change streams through aggregate and projection folds,
> compare live versus rebuilt state, rebuild affected disposable projections, and document the
> snapshot/cache invalidation boundary. No producer rollout is complete while aggregate and read-side
> interpretations differ.

#### AD-17 — overflow and sequence exhaustion

Add:

> Progress saturation compares the positive delta with current Remaining before addition; it never
> evaluates an overflowing `Done + delta`. Before producing a state-changing result, the aggregate
> proves ordinal headroom for the entire result. Insufficient headroom for a one- or two-event result
> fails as a typed infrastructure/integrity fault with no partial event result and no append.

#### AD-17 — singular executable lifecycle policy

Add:

> The executable lifecycle policy owns source Status, act, outcome, target Status, and stable
> `AttemptedAct`. Handlers consume or assert the returned target; they do not repeat status tables.
> The Markdown matrix is generated from or cell-compared with that policy. Token-presence tests are
> insufficient.

#### AD-29 — durable catalog completeness

Add:

> Catalog completeness is derived from the Contracts assembly: every concrete decorated command,
> success event, and rejection must have exactly one stable discriminator, a catalog sample, and every
> required golden representation. Hand-authored count equality is supplemental and cannot define the
> universe being checked.

Extend the Lifecycle Migration readiness gate to include valid-event replay comparison, projection
rebuild evidence, arithmetic boundaries, ordinal exhaustion, and assembly-derived catalog closure.

### 4.4 Story specifications — new Epic 5 remediation

OLD:

> The four Epic 2 retrospective actions exist only as open action items, while the rewritten `2.x`
> stories mix them with delivered and future scope.

NEW:

#### Epic 5: Stabilize the Single-Item Lifecycle Contract

##### Story 5.1: Make Progress and Event Ordinals Overflow-Safe

**Owner:** Amelia (Dev)  
**Source action:** `epic-2-retro-item-14-make-progress-and-payload-sequencing-ove`

Acceptance requirements:

- A valid positive delta at or above Remaining saturates `Done` to `Estimated` without evaluating an
  overflowing decimal addition.
- A delta below Remaining adds exactly and cannot overflow by construction.
- A one-event result at `Sequence == long.MaxValue` and a two-event result without two free ordinals
  fail before result construction as the architecture-defined typed integrity fault.
- No partial success event is returned or appended.
- Focused tests cover `decimal.MaxValue`, `long.MaxValue`, and `long.MaxValue - 1`, including the
  progress-plus-completion path.

##### Story 5.2: Make the Lifecycle Authority Executable and Singular

**Owner:** Charlie (Senior Dev)  
**Source action:** `epic-2-retro-item-15-make-the-lifecycle-authority-executable`

Acceptance requirements:

- Progress, re-estimation, rescheduling, and every lifecycle command obtain status legality from the
  shared executable policy.
- Each handler consumes or verifies `LifecycleOutcome.Target`; no handler-local status table remains.
- The Markdown matrix is generated from or mechanically cell-compared with the executable policy.
- Every rejected matrix cell asserts the stable durable `AttemptedAct` value.
- Re-estimation is covered from all five non-terminal statuses and every terminal/unknown status.
- Existing diagnostic act-name values remain compatible; any normalization is additive mapping, not a
  rewrite of durable historical values.

##### Story 5.3: Preserve Re-Estimate Overrun and Enforce Bounded Act Notes

**Owner:** Amelia (Dev), with Winston (Architect)  
**Source action:** planning portion of
`epic-2-retro-item-16-reconcile-the-epic-2-planning-contract-p`

Acceptance requirements:

- Downward `ReEstimate` preserves cumulative `Done`, derives nonnegative Remaining, preserves Unit and
  Status, and emits no completion/reopen event.
- Aggregate state, roll-up/projected state, serialized query evidence, and rebuild from historical
  streams converge on the same overrun values.
- Pre-change streams that previously rebuilt with clamped `Done` are included in a documented migration
  fixture and rebuild proof.
- Every currently note-bearing lifecycle/planning command is inventoried. The approved common policy is
  `null` or 0–1,000 characters accepted and preserved verbatim; more than 1,000 characters produces the
  defined domain rejection with no success event or mutation.
- Command validators, aggregate handling, durable catalog samples, golden payloads, and focused tests
  carry the same limit. Logs and ProblemDetails do not echo the note.

##### Story 5.4: Derive Durable Catalog Completeness from Contracts

**Owner:** Dana (QA)  
**Source action:** `epic-2-retro-item-17-derive-durable-catalog-completeness-from`

Acceptance requirements:

- Reflection over the Contracts assembly derives the complete set of concrete decorated commands,
  success events, and `IRejectionEvent` types.
- Each derived type has exactly one stable discriminator, one non-vacuous catalog sample, and all
  required writer/reader golden representations.
- Checks remain bidirectional: a contract without evidence and evidence without a contract both fail.
- Adding a decorated durable type without updating the catalog/golden corpus demonstrably fails the
  focused test.
- Existing frozen discriminators and payload bytes are unchanged.

#### Provisional future stories — not added to sprint status yet

The future-scope section must retain, under new identifiers, at least these independently reviewable
stories:

- **Future 6.1 — Correct Progress and Reopen Progress-Completed Work:** additive
  `ProgressCorrected`, `CompletionKind`, and narrow `WorkItemReopened` semantics; `Done >= 0`, original
  acts preserved, explicit completion never reopens. Depends on Stories 5.2–5.4 and Epic 1 item 4.
- **Future 6.2 — Hand Off Active Work Without Changing State:** additive
  `HandoffWorkItem`/`WorkItemHandedOff`, actor-to-binding authorization, preserved Status/effort/await
  conditions. Depends on Stories 5.2/5.4 and Epic 1 item 4.

Schedule-witness and query-authorization deltas remain separate candidates and must not be hidden in
either story. Product Owner and Architect finalize their identifiers only after dependency and
architecture review.

### 4.5 Sprint status — separate delivery, retrospective outcome, and remediation

**File:** `_bmad-output/implementation-artifacts/sprint-status.yaml`

OLD:

```yaml
development_status:
  epic-2: in-progress
  2-1-define-the-lifecycle-state-machine: done
  2-2-record-raw-act-events-and-replay-state: done
  2-3-report-progress-with-unit-tagged-burn-down: done
  2-4-re-estimate-and-reschedule-work: done
  2-5-complete-cancel-reject-and-expire-work: done
  epic-2-retrospective: done
```

NEW, after the four story specifications exist:

```yaml
development_status:
  epic-2: done
  2-1-define-the-lifecycle-state-machine: done
  2-2-record-raw-act-events-and-replay-state: done
  2-3-report-progress-with-unit-tagged-burn-down: done
  2-4-re-estimate-and-reschedule-work: done
  2-5-complete-cancel-reject-and-expire-work: done
  epic-2-retrospective: done
  epic-5: backlog
  5-1-make-progress-and-event-ordinals-overflow-safe: backlog
  5-2-make-the-lifecycle-authority-executable-and-singular: backlog
  5-3-preserve-re-estimate-overrun-and-enforce-bounded-act-notes: backlog
  5-4-derive-durable-catalog-completeness-from-contracts: backlog
  epic-5-retrospective: optional
```

Do not add rewritten `2.6`, `2.7`, or `2.8` keys. Do not change the five historical story statuses to
represent the retrospective verdict.

Action-item transitions when the approved artifact edits are actually applied:

- Epic 2 items 14, 15, and 17 remain `open` and point to Stories 5.1, 5.2, and 5.4 respectively.
- Epic 2 item 16 becomes `done` only after the epics, PRD, architecture, story, and sprint-status
  changes in this proposal are all present; Story 5.3 then owns implementation of its selected rule.
- Epic 1 items 2, 4, and 13 remain `open` until their own evidence passes; dependency references are
  added to the relevant Epic 5/future story notes rather than duplicating the actions.
- Epic 1 items 6 and 8 remain open unless their full, broader acceptance is completed. The Epic 2 part
  alone is not enough to close either.

### 4.6 UX disposition

**Files:** `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md` and
`EXPERIENCE.md`

No UX behavior change is required for the selected re-estimation rule. The current UX already defines:

- visible `Done > Estimated` overrun;
- Remaining zero without an implicit completion announcement;
- no determinate progress bar that hides an overrun;
- separate historical Burn-Down and terminal roll-up contribution.

After PRD/architecture alignment, refresh the source/provenance note and validate terminology. This is
not a blocker for the headless Epic 5 remediation, but it is required before any future human surface is
implemented.

### 4.7 Required order before implementation resumes

1. Approve this proposal.
2. Apply the epics/PRD/architecture edits and record their provenance.
3. Create and validate the four Epic 5 story artifacts with the acceptance requirements above.
4. Update sprint status atomically with those story files; mark historical Epic 2 `done`.
5. Repair the Epic 1 item 2 architecture gate before any Epic 5 story is accepted as `done`.
6. Close Epic 1 item 13 before starting an unattended implementation loop.
7. Implement Epic 5 in dependency order: 5.1 and 5.4 may run independently; 5.2 precedes 5.3's final
   integration because the selected rule must enter through the single lifecycle authority.
8. Prove overrun-preserving aggregate/projection replay and rebuild before enabling the new semantics
   against durable environments.
9. Close Epic 1 item 4 before implementing any new correction or handoff command producer.
10. Re-run implementation readiness; only then promote provisional future stories into sprint status.

## 5. Implementation Handoff

### Scope and recipients

**Scope:** Major — fundamental replan with a durable replay-semantic correction.  
**Route to:** John (PM), Winston (Architect), Product Owner/Sprint Manager, Amelia and Charlie (Dev),
Dana (QA), and Administrator for orchestration/status governance.

- **John (PM):** restore the historical Epic 2 section, preserve the rewrite as future scope, approve
  the delivered-versus-target MVP boundary, and own final future story identifiers.
- **Winston (Architect):** amend AD-05/AD-17/AD-29, define the valid-event replay migration and rebuild
  gate, and verify correction/handoff dependencies.
- **Product Owner / Sprint Manager:** create Stories 5.1–5.4, update sprint status only after their
  artifacts exist, and keep future candidates out of executable status.
- **Amelia (Dev):** implement Stories 5.1 and 5.3 after their gates are met.
- **Charlie (Senior Dev):** implement Story 5.2 and retain compatibility of durable attempted-act
  diagnostics.
- **Dana (QA):** implement Story 5.4 and own the assembly-derived compatibility gate.
- **Administrator:** close or explicitly supervise around Epic 1 item 13 and preserve action/status
  provenance.

### Success criteria

- `epics.md` answers “what did Epic 2 deliver?” with exactly the five historical stories and their
  provenance.
- The seven-story rewrite named by the retrospective, plus the additional Story 2.8, is visibly future scope
  and cannot inherit `done` through identifier reuse.
- PRD, architecture, story acceptance, aggregate state, projection folds, UX semantics, and tests all
  preserve cumulative `Done` across downward re-estimation.
- Remaining is never negative; re-estimation never completes or changes Status; overrun remains visible.
- Progress and sequence boundaries cannot overflow or return partial results.
- One executable policy determines lifecycle legality, targets, and attempted-act diagnostics, with an
  exact mechanical documentation check.
- Catalog completeness fails from assembly-derived evidence when any durable contract lacks a stable
  discriminator, sample, or required golden representation.
- Historical Stories 2.1–2.5 and their artifacts remain unchanged and `done`; Epic 5 carries the open
  implementation work.
- Epic 1 items 2, 4, and 13 retain their original ownership and gate the relevant work without duplicate
  actions.

## 6. Checklist Outcome

| Item | Status | Finding |
| --- | ---: | --- |
| 1.1 Triggering story | [x] | No single triggering story; the Epic 2 retrospective exposed contract drift and failures against historical Stories 2.1/2.3/2.4. |
| 1.2 Core problem | [x] | Planning-history replacement plus contradictory forward product/architecture semantics and three verification/implementation gaps. |
| 1.3 Evidence | [x] | Epic 2 retro, historical commit `c1ba6bb`, five story artifacts, current epics/PRD/architecture/UX/status, code and focused tests. |
| 2.1 Current epic viability | [x] | Historical Epic 2 is complete as delivery history but rejected against declared criteria; remediation must be separate. |
| 2.2 Epic-level changes | [x] | Restore historical Epic 2, classify rewrite as future, add remediation Epic 5. |
| 2.3 Remaining epic impact | [x] | Story 4.4 and Epic 1 shared gates are mapped; no delivered epic is retroactively redefined. |
| 2.4 New/obsolete epics | [x] | Epic 5 is new; rewritten Epic 2 ceases to be executable and becomes future candidate scope. |
| 2.5 Priority/order | [x] | Planning alignment precedes Epic 5; identity/orchestration gates precede future command work/unattended execution. |
| 3.1 PRD conflicts | [x] | PRD overrun rule selected; delivered-versus-expanded-target boundary added. MVP target remains achievable but is not already delivered. |
| 3.2 Architecture conflicts | [x] | AD-05/17/29, replay migration, sequence boundary, lifecycle authority, and readiness-gate edits identified. |
| 3.3 UX conflicts | [x] | UX already matches overrun; provenance refresh deferred until source alignment and before surface implementation. |
| 3.4 Other artifacts | [x] | Historical stories, new specs, sprint status, action dependencies, lifecycle matrix, catalog/goldens, rebuild evidence identified. |
| 4.1 Direct Adjustment | [x] Viable | Required for remediation and artifact alignment; Medium implementation effort. |
| 4.2 Potential Rollback | [N/A] Not viable | Would destroy valid delivery provenance and does not resolve forward semantics. |
| 4.3 MVP Review | [x] Viable | Used to separate the delivered June baseline from the expanded target; no feature is falsely claimed as shipped. |
| 4.4 Recommended path | [x] | Hybrid Direct Adjustment + MVP Review/future-scope re-baseline. |
| 5.1–5.5 Proposal components | [x] | Issue, impacts, approach, detailed edits, sequencing, owners, and success criteria are present. |
| 6.1 Checklist review | [x] | All applicable sections addressed; pending approval/status work is explicit. |
| 6.2 Proposal accuracy | [x] | Cross-checked against primary retro, live artifacts, historical Epic 2, code, and tests. |
| 6.3 User approval | [x] | Approved by Administrator on 2026-09-13. |
| 6.4 Sprint-status update | [!] | Proposed only; must occur after approval and story creation. |
| 6.5 Handoff confirmation | [x] | Major-scope handoff confirmed to PM, Architect, PO/SM, Dev, QA, and Administrator. |

## 7. Approval Record

**Decision:** Approved for implementation.  
**Approved by:** Administrator  
**Date:** 2026-09-13  
**Conditions or revisions:** Execute the dependency and artifact order in §4.7. Create and validate
the Epic 5 story artifacts before adding their sprint-status keys; do not promote provisional future
scope or reuse historical `2.x` identifiers.

Approval authorizes the implementation handoff defined in §5. This workflow finalized the proposal
only; it did not pre-empt the ordered PRD, architecture, story, code, or sprint-status changes.

## 8. Workflow Execution Log

| Date | Event | Result |
| --- | --- | --- |
| 2026-09-13 | Change trigger accepted from the Epic 2 retrospective | Five-story history, planning rewrite, re-estimation conflict, four actions, and Epic 1 dependencies entered scope. |
| 2026-09-13 | Batch impact analysis completed | Hybrid Direct Adjustment + MVP Review selected; rollback rejected. |
| 2026-09-13 | Sprint Change Proposal prepared | Major scope; Epic 5 remediation and future-scope boundary proposed. |
| 2026-09-13 | Administrator approval received | Proposal finalized and approved without revision. |
| 2026-09-13 | Implementation handoff recorded | Routed to John (PM), Winston (Architect), Product Owner/Sprint Manager, Amelia and Charlie (Dev), Dana (QA), and Administrator, subject to §4.7 gates. |
