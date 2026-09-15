# Implementation Readiness

**Project:** works
**Assessment date:** 2026-09-15
**Intent:** Sprint planning
**Verdict:** **FAIL**

The planning set has materially improved since the 2026-09-14 assessment, but
it is not yet safe to regenerate sprint tracking. Historical Epic 1-4 identity
collisions are resolved, the PRD now records the approved product semantics,
and Epic 5 is defined. However, Architecture still contradicts the governing
re-estimation rule, the required validated Epic 5 story artifacts do not exist,
and two downstream reconciliation decisions remain incomplete.

## Evidence Reviewed

- `_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/brief.md`
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md`
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/validation-report.md`
- `_bmad-output/planning-artifacts/architecture.md`
- `_bmad-output/planning-artifacts/epics.md`
- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md`
- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md`
- `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-14.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- Existing story artifacts, epic contexts, retrospectives, action items, and
  repository knowledge under `docs/`

## Progress Since the Previous Assessment

- The PRD is final and carries the approved delivered-versus-target provenance
  boundary, overrun-preserving re-estimation rule, and Story 5.5 trace.
- `epics.md` restores the historical Story 1.1-4.9 mappings and preserves the
  rewritten target bodies under non-executable `F1-*` through `F4-*` labels.
- Epic 5 and Stories 5.1-5.5 now have explicit scopes and acceptance criteria in
  `epics.md`.
- The forward FR coverage map distinguishes historical capability from the
  undelivered Epic 5 deltas.

These corrections close the former historical/forward identity-collision
finding. They do not yet satisfy the approved prerequisites for sprint-status
generation.

## Findings

### IR-01 - Architecture still contradicts the approved re-estimation rule

**Severity:** Blocker

The final PRD and Epic 5.3 require `ReEstimate` to preserve cumulative `Done`,
derive `Remaining = max(Estimated - Done, 0)`, permit visible overrun, and leave
Status and Unit unchanged. Only `CorrectProgress` may replace cumulative
`Done`, and it accepts values at or above zero.

Architecture AD-17 still says that `CorrectProgress` is bounded to
`0..Estimated` and that `ReEstimate` clamps `Done`. Forward Candidate F2-F in
`epics.md` also retains the obsolete clamp-Done acceptance criterion. The same
`epics.md` explicitly records that AD-17 and candidate text must be amended
before implementation.

A developer cannot implement Story 5.3, its replay migration, projections,
queries, catalogs, and golden evidence against two contradictory authorities.

**Required correction:** Use `bmad-architecture` to amend AD-17 and the
lifecycle migration gate to the PRD's preserve-Done rule. Then use
`bmad-create-epics-and-stories` to reconcile the affected candidate acceptance
criteria without altering delivered Story 2.4 evidence.

### IR-02 - Required validated Epic 5 story artifacts do not exist

**Severity:** Blocker

`epics.md` defines Stories 5.1-5.5, but
`_bmad-output/implementation-artifacts` contains no corresponding `5-1-*`
through `5-5-*` story artifacts. The approved 2026-09-14 change proposal makes
their existence and validation a prerequisite to the atomic tracker update.

The missing artifacts must preserve the recorded dependency order:

- Stories 5.1 and 5.4 may proceed independently.
- Story 5.2 precedes final Story 5.3 integration.
- Story 5.3 depends on Story 5.4 for changed durable evidence.
- Story 5.5 depends on Story 5.4 before enabling a new rejection producer.

Generating tracking now would violate the approved handoff and the explicit
guard recorded in `epics.md`.

**Required correction:** Use `bmad-create-epics-and-stories` to create and
validate the five Epic 5 artifacts from the reconciled PRD, Architecture, and
Epic 5 definitions after IR-01 is closed.

### IR-03 - Multiple Await-Condition admission remains underspecified

**Severity:** High

The latest PRD validation records one remaining Product decision gate. FR-5,
FR-14, UJ-3, UX, and the epic material allow multiple simultaneous
Await-Conditions, but the normative admission schema does not completely bind
the non-empty set shape, duplicate normalization, or simultaneous-match
ordering. `WorkItemSuspended` is not consistently described as carrying the
complete set across the governing product material.

This gap is specifically gated before the next lifecycle-contract
implementation or candidate promotion. Story 5.2 changes the executable
lifecycle authority, so leaving the gap unresolved risks encoding an assumed
policy into the shared matrix and tests.

**Required correction:** Use `bmad-prd` to record the Product decision, then
reconcile Architecture and the affected Epic 3/lifecycle acceptance criteria.
If this decision is intentionally excluded from Epic 5, record that boundary
explicitly so Story 5.2 preserves the current durable contract without
inventing the future set schema.

### IR-04 - UX source provenance has not been refreshed

**Severity:** Medium

`DESIGN.md` and `EXPERIENCE.md` remain dated 2026-09-12 and their source
precedence predates the approved 2026-09-14 product amendment. Their visible
overrun, non-terminal-zero, correction/reopen, freshness, and accessible
Burn-Down behavior is already aligned, so no UX redesign is required. The
remaining gap is provenance and authority ordering.

**Required correction:** After IR-01 and IR-03 are resolved, use `bmad-ux` to
refresh source/provenance notes while preserving the current behavior and the
headless-v1 boundary.

## Required Sequence

1. Resolve IR-03's Product decision or record its explicit exclusion from Epic
   5 lifecycle hardening.
2. Amend and validate Architecture AD-17 and reconcile the obsolete candidate
   text identified by IR-01.
3. Refresh UX provenance after the governing authorities agree.
4. Create and validate Stories 5.1-5.5 as standalone implementation artifacts.
5. Rerun implementation readiness.
6. On PASS, regenerate `sprint-status.yaml` once, preserving every historical
   story status, marking Epics 1-3 done, retaining Epic 4 in progress, and
   adding only Epic 5 and Story 5.1-5.5 backlog keys.

## Gate Disposition

`_bmad-output/implementation-artifacts/sprint-status.yaml` was preserved
unchanged. The deterministic tracking generator was not run because generation
is permitted only after an implementation-readiness PASS.
