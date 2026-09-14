# Implementation Readiness

**Project:** works  
**Assessment date:** 2026-09-13  
**Repository baseline:** `ed55cc695591ae53e055b22848eb52798742d293`  
**Intent:** Sprint planning  
**Verdict:** **FAIL**

The recorded plan is not currently implementable without inventing decisions or
silently reassigning historical story identities. Sprint tracking must not be
regenerated until the conflicts below are resolved.

## Evidence Reviewed

- `_bmad-output/planning-artifacts/epics.md`
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`
- `_bmad-output/planning-artifacts/architecture.md`
- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md`
- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md`
- `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-13.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- Existing story artifacts and Epic 1/Epic 2 retrospective action items
- Repository knowledge under `docs/`

## Findings

### IR-01 — Historical and forward story identities conflict

**Severity:** Blocker

`epics.md` currently defines a rewritten Epic 1 and a rewritten eight-story Epic
2, while `sprint-status.yaml` and the durable implementation artifacts track the
historical five-story decompositions. The same `1.x` and `2.x` identities
therefore refer to different scopes, titles, and acceptance criteria.

The approved sprint change proposal explicitly classifies the rewritten Epic 2
as non-executable future scope and states that it cannot be used as either the
historical record or an implementation-ready backlog. A deterministic status
refresh cannot reconcile these identities without silently changing provenance.

**Required correction:** Apply the approved `bmad-correct-course` handoff. Use
`bmad-create-epics-and-stories` to restore the historical mappings, preserve the
rewritten material under non-executable provisional labels, and introduce unique
identifiers for new work.

### IR-02 — Re-estimation semantics contradict each other

**Severity:** Blocker

- Epic Story 2.6 says a downward re-estimate clamps cumulative `Done` to the new
  `Estimated` value.
- PRD FR-8 and FR-9 preserve cumulative `Done`, expose `Done > Estimated` as
  overrun, and clamp only `Remaining`.
- Architecture AD-17 says `ReEstimate` clamps `Done` and bounds corrected `Done`
  to `0..Estimated`.
- The approved change proposal selects the PRD rule: re-estimation preserves
  `Done`; only a separately audited correction may change it.

A developer cannot implement replay, projections, rebuild behavior, and golden
contracts consistently while these authorities disagree.

**Required correction:** Use `bmad-prd` and `bmad-architecture` to apply and
validate the approved overrun-preserving rule, replay-migration requirements,
and lifecycle/catalog gates. Then update the story acceptance criteria from
those aligned authorities.

### IR-03 — Approved remediation work has no executable stories

**Severity:** High

The approved proposal requires four Epic 5 stories covering overflow safety,
the singular executable lifecycle authority, overrun-preserving re-estimation
with bounded notes, and assembly-derived durable-catalog completeness. No Epic 5
story artifacts or sprint keys currently exist.

Adding tracker entries before the story specifications exist would violate the
approved sequencing and leave implementation scope underdefined.

**Required correction:** Use `bmad-create-epics-and-stories` to create and
validate Stories 5.1–5.4, including their dependencies and proof requirements,
before adding them to `sprint-status.yaml`.

### IR-04 — Epic 1 planning provenance remains unresolved

**Severity:** High

The Epic 1 retrospective action ledger still records the mismatch between the
as-built five-story Epic 1 and the current rewritten Epic 1. Until that mapping
is reconciled, requirements and completed implementation artifacts do not trace
bidirectionally through stable story identities.

**Required correction:** Complete the broader Epic 1 reconciliation through
`bmad-correct-course` and `bmad-create-epics-and-stories`; do not close the
action merely because the Epic 2 overlap is repaired.

## Required Sequence

1. Apply the approved PRD, architecture, and epic-history corrections.
2. Reconcile Epic 1's historical and forward story mapping.
3. Create and validate Epic 5 Stories 5.1–5.4.
4. Update sprint status atomically with the reconciled epic/story sources.
5. Rerun implementation readiness and sprint planning.

## Gate Disposition

`sprint-status.yaml` was preserved unchanged. The deterministic tracking
generator was not run because generation is permitted only after a readiness
PASS.
