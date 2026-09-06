# Sprint Change Proposal — 2026-09-06

**Project:** works · **Prepared by:** Administrator (correct-course workflow) · **Mode:** Batch
**Trigger:** Residue of the 2026-09-06 architecture-gate update (architecture.md open-findings
register): VAL-H03 product fork, VAL-H10 advanced due date, and the outstanding §4.7 secondary
artifact edit from the approved 2026-09-05 proposal.

## 1. Issue Summary

The 2026-09-05 architecture validation gate failed with five criticals; the 2026-09-06 update run
cleared the gate by binding AD-01…AD-25 but deliberately left an open-findings register. Three of
those residue items require sprint-change handling now:

1. **VAL-H03 — FR-20 "creation order" is absent from the model.** The PRD (FR-20 consequences),
   epics (FR inventory and Story 4.4 AC), and architecture (AD-03) promise a "what's next" ordering
   of Priority → earliest Due Date → **creation order**, but the kernel records no cross-aggregate
   creation coordinate (`WorkItemState` carries only a per-aggregate `Sequence`; envelope timestamps
   are EventStore-owned and Works never populates envelope metadata). The shipped, fitness-asserted
   Story 4.4 implementation (`WhatsNextOrdering`) tiebreaks by `WorkItemId.Value` ordinal — a
   replay-stable strict total order that is not creation order for arbitrary valid ids. The gate
   ruled this a **product fork** to resolve through `bmad-correct-course` **before Epic 4 closure**.
2. **VAL-H10 — transport idempotency due date advanced.** AD-21's reserve→spawn→release saga
   translations need deterministic causation/message IDs, so the open VAL-H10 binding (MessageId /
   IdempotencyKey reuse, retention, replay result at the AD-20 R11 `IWorkCommandSubmitter` seam)
   moved from "before Theme 3 / R3 acceptance" to **before the AD-21 registry story is drafted**.
   The registry story does not exist yet; nothing in the backlog artifacts encodes this sequencing,
   so sprint planning could draft the registry story first and violate the constraint.
3. **`docs/boundary-decision-record.md` predates AD-20.** Section 4.7 of the approved 2026-09-05
   proposal requires the record to name the platform host. AD-20 has since named
   **`Hexalith.Platform`** (owner: Platform Maintainer, Hexalith), but the record still says "the
   Solution Architect must name that repository and owner before Story 4.9 enters implementation"
   and "until the Solution Architect names that repository and owner, migration may not begin."

**Product decision taken in this workflow (2026-09-06, Administrator):** resolve VAL-H03 by
**blessing deterministic identity order** — amend the PRD/epics/architecture to state the FR-20
tiebreak as deterministic identity order (`WorkItemId` ordinal) rather than adding a creation
coordinate. Supporting fact: `Hexalith.Commons.UniqueIds.UniqueIdHelper.GenerateSortableUniqueStringId()`
mints monotonic ULIDs, so edges that use the ecosystem generator get identity order that
approximates true creation order in practice; this is recorded as guidance, not an enforced
contract.

## 2. Impact Analysis

### Epic impact

- **Epic 4** (in-progress; 4.1–4.7 done, 4.8 in-progress, 4.9 backlog): the VAL-H03 fork was the
  only product-level blocker flagged "before Epic 4 closure". With identity order blessed, no new
  Epic 4 story is needed; closure still awaits Stories 4.8 and 4.9 on their own merits. Story 4.4
  needs an AC wording correction only — its implementation, tests, and evidence stand unchanged.
- **Epic 3**: the not-yet-drafted AD-21 Work-Tree Registry and AD-22 fan-out stories will land in
  this epic's domain. This proposal adds a pending-additions note with the VAL-H10 predecessor so
  sprint planning cannot draft the registry story before the idempotency contract is bound.
- **Epics 1–2**: no impact. No epic is added, removed, resequenced, or invalidated; no story is
  added or removed, so `sprint-status.yaml` entries are unchanged.

### Artifact impact

- **PRD** (`prds/prd-works-2026-06-14/prd.md`): FR-20 consequence line — one edit. MVP unchanged.
- **Epics** (`epics.md`): FR-20 inventory line, Story 4.4 AC line, Epic 3 list entry note — three edits.
- **Architecture** (`architecture.md`): AD-03 binds/rule, A2 narrative line, open-register VAL-H03
  row, implementation-handoff paragraph — four edits. VAL-H10 row already carries the advanced due
  date; no architecture edit needed for item 2.
- **UX**: N/A — v1 is headless; no screen or flow is affected.
- **Secondary artifacts**: `docs/boundary-decision-record.md` (two edits, per approved §4.7);
  `docs/whats-next-projection.md` (one wording alignment). No code, test, fixture, golden-corpus,
  read-model, or catalog change; no rebuild.

### Technical impact

None at runtime. The shipped `WhatsNextOrdering` comparator becomes the documented product rule
instead of a flagged substitute. The VAL-H10 binding itself remains open architecture work owned by
the Solution Architect; this proposal only makes its sequencing mechanical.

## 3. Recommended Approach

**Direct Adjustment** (checklist §4.1) — documentation-only edits plus one backlog sequencing
constraint. Effort: **Low**. Risk: **Low**. Timeline impact: none; it *unblocks* Epic 4 closure.

- **Rollback** (§4.2): N/A — nothing to revert; Story 4.4 behavior is correct under the approved rule.
- **MVP review** (§4.3): N/A — MVP outcome unchanged; FR-20's substance (deterministic,
  authorization-filtered, "neither sorts last" ordering without a routing engine) is delivered.

Rationale for blessing identity order over adding a creation coordinate: the coordinate would
require an additive `WhatsNextItem` field sourced from the creation envelope timestamp, projection
and comparator changes, new tests, and a read-model rebuild — buying only a fairer tiebreak among
items with equal Priority *and* equal/absent Due Date, a case the ULID guidance already
approximates for ecosystem-generated ids. The identity tiebreak is replay-stable, immune to
out-of-order delivery, and a strict total order — properties a wall-clock coordinate would weaken.

## 4. Detailed Change Proposals

### 4.1 PRD change (VAL-H03)

**File:** `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md` — FR-20 consequences.

OLD:
> - The query returns `Queued` and `Assigned` items for a tenant ordered by Priority (then earliest Due Date, then creation order); items with neither sort last.

NEW:
> - The query returns `Queued` and `Assigned` items for a tenant ordered by Priority (then earliest Due Date, then deterministic identity order — `WorkItemId` ordinal, a replay-stable strict total order); items with neither sort last. Works records no cross-aggregate creation coordinate; edges that want the tiebreak to approximate true creation order should mint ids with the `Hexalith.Commons` sortable ULID generator (guidance, not an enforced contract). *(Amended 2026-09-06: was "creation order" — VAL-H03 resolved by approved correct-course.)*

**Rationale:** states the shipped, testable rule; preserves the load-bearing "neither sorts last" clause.

### 4.2 Epics changes

**File:** `_bmad-output/planning-artifacts/epics.md`

**(a) FR-20 requirements inventory (VAL-H03):**

OLD:
> - **FR-20: Resolve a "what's next" ordering** — Read-side query returning a tenant's
>   `Queued`+`Assigned` items ordered by Priority → earliest Due Date → creation order (neither sorts
>   last); …

NEW:
> - **FR-20: Resolve a "what's next" ordering** — Read-side query returning a tenant's
>   `Queued`+`Assigned` items ordered by Priority → earliest Due Date → deterministic identity order
>   (`WorkItemId` ordinal; neither sorts last — amended 2026-09-06, VAL-H03); …

**(b) Story 4.4 acceptance criterion (VAL-H03):**

OLD:
> **Then** it sorts by Priority, then earliest Due Date, then creation order

NEW:
> **Then** it sorts by Priority, then earliest Due Date, then deterministic identity order (`WorkItemId` ordinal)

**(c) Epic 3 list entry — pending additions + VAL-H10 predecessor (append to the Epic 3 entry in the Epic List):**

NEW (addition):
> _Pending additions (2026-09-06 architecture update): the AD-21 Work-Tree Registry and AD-22
> registry-backed fan-out stories are routed into this epic through sprint planning.
> **Predecessor:** the VAL-H10 transport-idempotency contract must be bound at the AD-20 R11
> command-submission seam (deterministic MessageId/causation derivation for the
> reserve→spawn→release translations) **before the registry story is drafted** — Solution
> Architect, via the architecture workflow._

**Rationale:** (a)/(b) align planning text with the approved rule without touching Story 4.4's
evidence; (c) encodes the advanced VAL-H10 due date where story drafting actually starts.

### 4.3 Architecture changes (VAL-H03 closure)

**File:** `_bmad-output/planning-artifacts/architecture.md`

**(a) AD-03 binds line:** "(Priority → Due Date → creation order; none sorts last)" →
"(Priority → Due Date → deterministic identity order; none sorts last)".

**(b) AD-03 rule:**

OLD:
> **Rule:** ordering logic consumes the enum's declared order only. The FR-20 creation-order
> coordinate is a still-open product fork (VAL-H03) routed through `bmad-correct-course`; the live
> identity-order substitute must not be documented as creation order.

NEW:
> **Rule:** ordering logic consumes the enum's declared order only. The FR-20 tiebreak is
> **deterministic identity order** (`WorkItemId` ordinal) — the VAL-H03 product fork was resolved
> by the approved 2026-09-06 correct-course; Works records no cross-aggregate creation coordinate.
> Edge originators are advised (not bound) to mint sortable ULIDs via `Hexalith.Commons` so
> identity order approximates creation order in practice.

**(c) A2 narrative (Core Architectural Decisions):** "(Priority → Due Date → creation order; none
sorts last)" → "(Priority → Due Date → deterministic identity order; none sorts last — VAL-H03
resolved 2026-09-06)".

**(d) Open-findings register, VAL-H03 row:**

OLD:
> | VAL-H03 — FR-20 creation order vs live identity order | **Product fork** — route through `bmad-correct-course`; do not document the substitute as creation order | Before Epic 4 closure |

NEW:
> | VAL-H03 — FR-20 creation order vs live identity order | **Resolved 2026-09-06** — approved correct-course blessed deterministic identity order (`WorkItemId` ordinal) as the FR-20 tiebreak; PRD/epics/AD-03 amended, no creation coordinate added | Closed (sprint-change-proposal-2026-09-06) |

**(e) Implementation-handoff paragraph:** "Route the AD-21 Work-Tree Registry and AD-22 fan-out
into the backlog through sprint planning, and route the FR-20 creation-order fork (VAL-H03)
through `bmad-correct-course`." → "Route the AD-21 Work-Tree Registry and AD-22 fan-out into the
backlog through sprint planning — binding the VAL-H10 transport-idempotency contract at the R11
seam first. The FR-20 creation-order fork (VAL-H03) is resolved: deterministic identity order
(approved 2026-09-06)."

### 4.4 Boundary decision record (approved §4.7 follow-through)

**File:** `docs/boundary-decision-record.md`

**(a) Approved-course-correction callout:**

OLD (last sentence):
> A designated platform/host repository owns Aspire topology
> and generic runtime plumbing. The Solution Architect must name that repository and owner before
> Story 4.9 enters implementation.

NEW:
> A designated platform/host repository owns Aspire topology
> and generic runtime plumbing. Named 2026-09-06 (architecture AD-20): that repository is
> **`Hexalith.Platform`** (github.com/Hexalith/Hexalith.Platform), owned by the Platform
> Maintainer (Hexalith); Story 4.9 may enter implementation.

**(b) "Hosting and runtime ownership" section:**

OLD:
> The designated platform/host repository owns Aspire composition, ServiceDefaults, Dapr components,
> health and telemetry, generic command/event delivery, projection/query storage plumbing,
> subscriptions, reminders, and recovery orchestration. Story 4.9 migrates the historical Works-owned
> hosting topology only after the platform topology has equivalent passing evidence. Until the Solution
> Architect names that repository and owner, migration may not begin and the current host assets may not
> be removed.

NEW:
> The designated platform/host repository — **`Hexalith.Platform`**, owned by the Platform
> Maintainer (Hexalith), per architecture AD-20 (2026-09-06) — owns Aspire composition,
> ServiceDefaults, Dapr components, health and telemetry, generic command/event delivery,
> projection/query storage plumbing, subscriptions, reminders, and recovery orchestration. Story 4.9
> migrates the historical Works-owned hosting topology only after the platform topology has
> equivalent passing evidence. Migration may begin; a current host asset may be removed only after
> the corresponding AD-20 migration-matrix row is green in the `Hexalith.Platform` conformance lane.

### 4.5 Runtime doc alignment (VAL-H03)

**File:** `docs/whats-next-projection.md`

OLD:
> Works has no creation timestamp in the kernel (`WorkItemState` carries only
> a per-aggregate `Sequence`; envelope timestamps are EventStore-owned), so "creation order" is realized as
> this rebuild-deterministic identity order.

NEW:
> Works has no creation timestamp in the kernel (`WorkItemState` carries only
> a per-aggregate `Sequence`; envelope timestamps are EventStore-owned). Deterministic identity order
> is the approved FR-20 tiebreak (correct-course 2026-09-06, resolving VAL-H03) — not a stand-in for
> creation order; edges minting ids with the Commons sortable ULID generator get identity order that
> approximates creation order in practice.

### Deliberately unchanged

- **No VAL-H10 closure:** the idempotency contract itself (identity derivation, key reuse,
  retention, replay result, digest behavior) is architecture work — handed to the Solution
  Architect below. The open-register row stays open with its advanced due date.
- **`sprint-status.yaml`:** no story or epic is added/removed, so no entry changes (a dated header
  comment noting this proposal is added on approval, matching the 2026-09-05 precedent).
- **Code, tests, golden corpus, catalog (37 durable types + Story 1.5's additive 40 target):** untouched.

## 5. Implementation Handoff

Scope classification: **Moderate** — documentation edits are directly appliable, but the VAL-H10
predecessor requires backlog-sequencing discipline and an architect deliverable.

- **Developer (immediately, on approval):** apply edits 4.1–4.5 verbatim; add the dated header
  comment to `sprint-status.yaml`.
- **Solution Architect (before the AD-21 registry story is drafted):** bind the VAL-H10
  transport-idempotency contract at the AD-20 R11 submission seam via the architecture workflow —
  deterministic MessageId/causation derivation for every internal originator, key reuse/retention/
  replay-result semantics, and the distinction between transport dedup, semantic no-op, and
  projection offset dedup. Update the open-findings register row when bound.
- **Product Owner / sprint planning:** treat the Epic 3 pending-additions note as a drafting gate;
  re-run sprint-planning readiness after the registry/fan-out story specifications exist. Epic 4
  closure remains gated only on Stories 4.8 and 4.9.

Success criteria:

- No planning artifact promises "creation order" for FR-20; all state deterministic identity order
  with the ULID guidance note.
- Architecture open-findings register shows VAL-H03 resolved/closed; VAL-H10 remains open with the
  "before the registry story is drafted" condition and is discharged before that story exists.
- `docs/boundary-decision-record.md` names `Hexalith.Platform` and the Platform Maintainer, with
  removal still gated on green AD-20 matrix rows.
- Story 4.4 evidence and all shipped bytes remain untouched.

## 6. Checklist Outcome

| Item | Status | Notes |
| --- | ---: | --- |
| 1.1 Triggering story | [x] | Not a story — the 2026-09-06 architecture-gate update's open-findings register (+ approved 2026-09-05 §4.7 follow-through). |
| 1.2 Core problem | [x] | One product fork (VAL-H03), one sequencing constraint (VAL-H10), one stale secondary artifact. |
| 1.3 Evidence | [x] | architecture.md AD register + open register; ARCHITECTURE-VALIDATION.md VAL-H03/H10; `WhatsNextOrdering.cs`; `whats-next-projection.md`; `UniqueIdHelper.cs`; sprint-status.yaml; prior proposal §4.7. |
| 2.1 Current epic viability | [x] | Epic 4 viable; VAL-H03 closure removes its product-level gate; 4.8/4.9 remain. |
| 2.2 Epic-level changes | [x] | AC wording fix (Story 4.4) + Epic 3 pending-additions note; no story added/removed. |
| 2.3 Remaining epic impact | [x] | Future AD-21/AD-22 stories inherit the VAL-H10 predecessor. |
| 2.4 New/obsolete epics | [N/A] | None. |
| 2.5 Priority/order | [x] | VAL-H10 binding ordered before registry story drafting; otherwise unchanged. |
| 3.1 PRD conflicts | [x] | One FR-20 consequence line; MVP intact. |
| 3.2 Architecture conflicts | [x] | AD-03/A2/register/handoff edits; VAL-H10 row already correct. |
| 3.3 UX conflicts | [N/A] | Headless v1. |
| 3.4 Secondary artifacts | [x] | boundary-decision-record.md, whats-next-projection.md. |
| 4.1 Direct Adjustment | [x] Viable | Selected — Low effort / Low risk. |
| 4.2 Rollback | [N/A] Not viable | Nothing to revert. |
| 4.3 MVP review | [N/A] | Scope unchanged. |
| 5.1–5.5 Proposal components | [x] | Sections 1–5 above. |
| 6.4 sprint-status.yaml | [N/A] | No structural change; dated comment only. |

**Decision record:** VAL-H03 resolved as "bless deterministic identity order" — chosen by
Administrator in this workflow (2026-09-06) over adding a creation coordinate or binding ids to
ULID format.
