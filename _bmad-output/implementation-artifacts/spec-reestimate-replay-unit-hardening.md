---
title: 'Harden mismatched-unit re-estimate replay'
type: 'bugfix'
created: '2026-08-27'
status: 'done'
baseline_revision: 'aeee0d655ac744fb474629dcc49985cc3b410cd0'
baseline_commit: 'aeee0d655ac744fb474629dcc49985cc3b410cd0'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - 'docs/work-roll-up-projection.md'
warnings: []
deferred:
  - summary: >-
      A persisted same-unit ReEstimated event with a negative estimate can still throw during aggregate replay.
    evidence: |-
      WorkItemState.Apply(ReEstimated) calls WorkItemEffort.ReEstimate for a matching established unit, and that value object rejects negative estimates. WorkItemRollUpProjection already refuses and diagnoses the same corrupted fact, so this separate pre-existing corruption case can wedge aggregate replay.
    location: >-
      src/Hexalith.Works.Contracts/State/WorkItemState.cs:176
    severity: high
---

<intent-contract>

## Intent

**Problem:** `WorkItemState.Apply(ReEstimated)` currently applies a persisted estimate even when its `Unit` conflicts with the established effort unit. The roll-up projection already refuses that corrupted fact, so replay can produce different aggregate and projection effort views.

**Approach:** Mirror the projection's defensive unit check in aggregate replay: retain the last valid effort for a mismatched persisted `ReEstimated`, while still advancing aggregate replay sequence. Add one direct corrupted-stream replay test that drives the same events through aggregate state and roll-up projection and proves their effort views remain aligned.

## Boundaries & Constraints

**Always:** Preserve normal same-unit re-estimation and first-estimate behavior. Treat the persisted mismatched success event as consumed by advancing `WorkItemState.Sequence` to its sequence, while retaining the complete prior `WorkItemEffort` value. Keep the projection's existing degraded/diagnostic behavior unchanged. Use xUnit v3 and Shouldly and retain warnings-as-errors cleanliness.

**Block If:** Newer tracked architecture or event-contract evidence requires aggregate replay to adopt the persisted mismatched unit, or requires a different replay-sequence policy than consuming the corrupted event.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md` or any deferred-work ledger. Do not change command-side validation, event contracts, serialization, roll-up algorithms, diagnostics, public APIs, or documentation beyond the generated implementation spec. Do not throw from replay or coerce/convert effort units.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Corrupted established-unit replay | Existing `WorkItemEffort` in hours; persisted `ReEstimated` in points at sequence N | Aggregate retains the prior estimate, done, remaining, and hour unit; aggregate sequence advances to N. Roll-up retains the same effort view, advances its accepted source sequence to N, and remains degraded with the existing metadata-only diagnostic | Refuse the incompatible effort mutation without throwing or wedging replay |
| Valid established-unit replay | Existing effort and `ReEstimated` use the same unit | Existing `ReEstimate` behavior updates estimated effort, clamps done as defined, and advances sequence | No error expected |
| First estimate replay | No existing effort | `ReEstimated` establishes its estimate and unit and advances sequence | No error expected |

</intent-contract>

## Code Map

- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` -- `Apply(ReEstimated)` is the bug site; guard only the established-effort unit mismatch and assign `Sequence` for every accepted persisted success event.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` -- read-only behavioral reference: mismatched `ReEstimated` calls `Refuse`, retains `OwnEffort`, records a diagnostic, and advances `LatestAcceptedSourceSequence` during rebuild.
- `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs` -- focused aggregate replay tests and arrange helpers; add the direct aggregate/projection corrupted-stream convergence regression here.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` -- read-only proof of existing mismatch refusal semantics and expected `RollUpProjectionDiagnostic` shape.
- `docs/work-roll-up-projection.md` -- read-only policy statement for retained last-valid effort and deterministic degraded replay.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works.Contracts/State/WorkItemState.cs` -- make `Apply(ReEstimated)` skip effort replacement when an established unit differs, but always advance `Sequence` -- align aggregate replay with the projection's corrupted-event policy.
- [x] `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs` -- add a direct persisted-event replay regression that applies a valid create followed by mismatched `ReEstimated` to both `WorkItemState` and `WorkItemRollUpProjection`; assert retained effort convergence, consumed sequence, degraded projection metadata, and no exception -- prevent recurrence across the two replay surfaces.

**Acceptance Criteria:**
- Given aggregate state and roll-up state have the same established effort unit, when both replay a persisted `ReEstimated` carrying another unit, then both retain the prior effort values and units.
- Given the mismatched event has sequence N, when aggregate and projection replay it, then aggregate `Sequence` and projection `LatestAcceptedSourceSequence` both advance to N while the projection exposes its existing deterministic degradation diagnostic.
- Given a same-unit re-estimate or an unestimated state, when `ReEstimated` is replayed, then existing update/establish behavior remains unchanged and all focused unit tests pass.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 1: (high 0, medium 0, low 1)
- defer: 1: (high 1, medium 0, low 0)
- reject: 17: (high 0, medium 0, low 17)
- addressed_findings:
  - `low` `patch` Added an inline explanation that mismatched-unit persisted events advance replay sequence while retaining last-valid effort, mirroring projection policy.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release -m:1 -p:NuGetAudit=false` -- expected: build succeeds with zero warnings and errors.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemReEstimateTests` -- expected: focused xUnit v3 class passes.

**Results (2026-08-27):**
- Release build passed with 0 warnings and 0 errors.
- Focused xUnit v3 class passed all 13 tests with 0 failures, 0 skips, and 0 tests not run.

## Auto Run Result

Status: done

Summary: Hardened aggregate replay so a persisted `ReEstimated` with a mismatched established unit retains the last valid effort while consuming the event sequence. Added a direct aggregate/roll-up replay regression proving their effort and sequence views no longer diverge.

Files changed:
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` -- refuses mismatched-unit effort replacement while advancing replay sequence, with the corrupted-event policy documented inline.
- `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs` -- covers the corrupted persisted-event replay through aggregate state and roll-up projection.
- `_bmad-output/implementation-artifacts/spec-reestimate-replay-unit-hardening.md` -- records intent, execution evidence, review triage, and completion.

Review findings breakdown: 1 low-severity patch applied, 1 high-severity pre-existing item deferred in this spec, and 17 review suggestions rejected as duplicates, already covered, incompatible with replay guarantees, or outside DW-9's mismatched-unit scope. The deferred-work ledger was not edited.

Follow-up review recommendation: false. Patched findings: high 0, medium 0, low 1; score = `3 × 0 + 1 × 1 = 1`.

Verification performed:
- `dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release -m:1 -p:NuGetAudit=false` -- passed with 0 warnings and 0 errors after the review patch.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemReEstimateTests` -- passed 13/13 with 0 failures, skips, or tests not run after the review patch.
- Matrix audit -- corrupted mismatch, valid same-unit replay, and first-estimate replay were all exercised by the focused class run.

Residual risks: a separate pre-existing same-unit negative-estimate corruption path can still throw during aggregate replay; it is recorded in `deferred` for later orchestration. No broader test lane was required by this focused bundle.
