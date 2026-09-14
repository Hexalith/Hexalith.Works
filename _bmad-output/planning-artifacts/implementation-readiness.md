# Implementation Readiness

**Project:** works
**Assessment date:** 2026-09-14
**Intent:** Sprint planning
**Verdict:** **FAIL**

The recorded plan is not currently implementable without inventing decisions or
silently reassigning delivered story identities. The approved 2026-09-14 sprint
change proposal defines the required correction, but its ordered remediation has
not yet been applied to the authoritative planning artifacts. Sprint tracking
must not be regenerated until the conflicts below are resolved.

## Evidence Reviewed

- `_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/brief.md`
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`
- `_bmad-output/planning-artifacts/architecture.md`
- `_bmad-output/planning-artifacts/epics.md`
- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md`
- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md`
- `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-13.md`
- `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-14.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- Existing story artifacts, Epic 1 through Epic 4 contexts and retrospectives,
  and the deferred-work ledger
- Repository knowledge under `docs/`

## Findings

### IR-01 — Historical and forward story identities conflict across Epics 1–4

**Severity:** Blocker

`epics.md` still assigns rewritten scopes, titles, and acceptance criteria to
historical `1.x`, `2.x`, `3.x`, and `4.x` identities. `sprint-status.yaml` and
durable implementation artifacts use those same identities for different
delivered or active stories:

- Epic 1 has five historical stories, while `epics.md` defines four different
  stories in the `1.1`–`1.4` range.
- Epic 2 has five historical stories, while `epics.md` defines eight different
  stories in the `2.1`–`2.8` range.
- Epic 3 has six historical stories, while `epics.md` defines ten different
  stories in the `3.1`–`3.10` range.
- Epic 4 tracks nine historical stories, while `epics.md` defines ten different
  stories in the `4.1`–`4.10` range.

The approved 2026-09-14 proposal requires the historical mappings to be
restored and the rewritten material to be quarantined under non-executable
`F1-*` through `F4-*` labels. That change is not present. A deterministic status
refresh would therefore transfer historical status to unrelated forward work.

**Required correction:** Apply the approved `bmad-correct-course` handoff with
`bmad-create-epics-and-stories`: restore the historical mappings, retain the
rewritten material under provisional labels, and prevent provisional labels
from becoming sprint keys.

### IR-02 — Re-estimation semantics remain contradictory

**Severity:** Blocker

- PRD FR-8 and FR-9 preserve cumulative `Done`, clamp only `Remaining`, and
  expose `Done > Estimated` as valid overrun.
- Architecture AD-17 says `ReEstimate` clamps `Done` and bounds corrected
  `Done` to `0..Estimated`.
- Rewritten Epic 2 acceptance criteria inherit the conflicting clamp behavior.
- The approved 2026-09-14 proposal selects the PRD rule and requires a replay,
  projection, query, snapshot, and rebuild migration gate.

A developer cannot implement aggregate replay and read-side behavior
consistently while these authorities disagree.

**Required correction:** Use `bmad-prd` and `bmad-architecture` to apply and
validate the approved overrun-preserving rule and migration requirements. Then
use `bmad-create-epics-and-stories` to derive aligned acceptance criteria.

### IR-03 — Approved Epic 5 remediation has no executable stories

**Severity:** Blocker

The approved proposal defines five remediation stories:

1. Make progress and event ordinals overflow-safe.
2. Make the lifecycle authority executable and singular.
3. Preserve re-estimate overrun and enforce bounded act notes.
4. Derive durable catalog completeness from contracts.
5. Enforce the Obligation bound without rewriting history.

`epics.md` contains no Epic 5, no validated Story 5.1–5.5 artifacts exist, and
`sprint-status.yaml` contains no Epic 5 keys. The dependencies and proof
requirements therefore exist only in change-proposal prose, not in an
executable backlog.

**Required correction:** Use `bmad-create-epics-and-stories` to create and
validate Epic 5 and Stories 5.1–5.5 before adding any Epic 5 tracker key.

### IR-04 — The Obligation bound has no stable implementation owner

**Severity:** High

PRD FR-2, UX, and the rewritten epic inventory require a trimmed, non-empty
Obligation of at most 4,000 characters. The current rewritten Story 1.2 appears
to own that behavior, but historical Story 1.2 already identifies the delivered
tenant-scoped creation story. The requirement cannot trace to a unique
executable story without reusing delivered identity.

The approved correction assigns this work to Story 5.5 and requires command
admission enforcement without breaking replay of previously accepted longer
payloads. Story 5.5 has not been created.

**Required correction:** Create and validate Story 5.5 through
`bmad-create-epics-and-stories`, trace PRD FR-2 to it, and retain the historical
Story 1.2 acceptance record unchanged.

### IR-05 — Approved artifact reconciliation remains incomplete

**Severity:** High

The 2026-09-14 proposal is approved for implementation, but approval explicitly
does not authorize premature tracking generation. Its required source changes
remain absent:

- PRD provenance and normative re-estimation text are not updated.
- Architecture AD-17 and the migration gate are not updated.
- `epics.md` does not separate delivered history from forward candidates.
- UX source/provenance notes have not been refreshed from aligned authorities.
- Stories 5.1–5.5 have not been created and validated.

The proposal is an implementation handoff, not a substitute for applying its
changes to the authoritative artifacts.

**Required correction:** Complete the approved sequence with `bmad-prd`,
`bmad-architecture`, `bmad-create-epics-and-stories`, and `bmad-ux` before
rerunning this gate.

## Required Sequence

1. Apply and validate the approved PRD and architecture corrections.
2. Restore the historical Epic 1–4 mappings and quarantine rewritten forward
   candidates under provisional labels.
3. Create and validate Epic 5 Stories 5.1–5.5 with their recorded dependencies
   and proof requirements.
4. Refresh UX source/provenance notes from the aligned authorities.
5. Update sprint status atomically from the reconciled epic and story sources;
   preserve every historical story status and add no provisional key.
6. Resolve the prerequisite retrospective action gates recorded by the approved
   proposal.
7. Rerun implementation readiness and run sprint planning only after a PASS.

## Gate Disposition

`sprint-status.yaml` was preserved unchanged. The deterministic tracking
generator was not run because generation is permitted only after a readiness
PASS.
