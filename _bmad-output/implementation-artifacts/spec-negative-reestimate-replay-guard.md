---
title: 'Guard aggregate replay from negative re-estimates'
type: 'bugfix'
created: '2026-08-28'
status: 'done'
baseline_revision: '90670e075dbffb1be24a70e77fec88d86e19ea50'
baseline_commit: '90670e075dbffb1be24a70e77fec88d86e19ea50'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** `WorkItemState.Apply(ReEstimated)` sends a persisted same-unit negative estimate into `WorkItemEffort.ReEstimate`, which throws and can wedge aggregate replay even though the roll-up projection already refuses the corrupted fact safely.

**Approach:** Refuse negative effort mutation at the aggregate replay boundary while still consuming the event sequence. Prove directly that aggregate state retains its last valid effort and remains sequence-aligned with the projection's existing refuse-and-diagnose behavior.

## Boundaries & Constraints

**Always:** Screen a negative persisted estimate before either the `WorkItemEffort` constructor or `ReEstimate` can run. Advance `WorkItemState.Sequence` to the corrupted success event's sequence without replacing any established `WorkItemEffort`; if no effort is established, leave it unestimated. Preserve valid first-estimate, same-unit re-estimate, and mismatched-unit replay behavior.

**Block If:** Tracked event-contract or architecture evidence requires corrupted persisted success events to abort aggregate replay or not consume their payload sequence.

**Never:** Change command-side validation, `ReEstimated`, `WorkItemEffort`, projection logic or diagnostics, public APIs, serialization, documentation, or any deferred-work ledger. Do not throw, clamp, take an absolute value, or otherwise coerce a negative persisted estimate into valid effort.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Established effort plus negative re-estimate | Current effort is 8 hours with 3 done; persisted same-unit `ReEstimated(-1)` at sequence N | Aggregate retains the complete 8/3/hour effort and advances to N; projection retains the same effort, advances to N, and exposes its existing deterministic degradation diagnostic | Consume without throwing or mutating effort |
| Unestimated state plus negative re-estimate | No current effort; persisted `ReEstimated(-1)` at sequence N | Aggregate remains unestimated and advances to N | Consume without constructing invalid effort |
| Valid re-estimate | Non-negative persisted first estimate or same-unit update | Existing establish/update semantics and sequence advancement remain unchanged | No error expected |
| Established unit mismatch | Persisted non-negative estimate uses another unit | Existing last-valid-effort retention and sequence advancement remain unchanged | Consume without throwing or coercing units |

</intent-contract>

## Code Map

- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` -- `Apply(ReEstimated)` is the fault site. It already retains effort for unit mismatch and advances `Sequence` after every branch, but negative values can still reach the throwing value-object paths.
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs` -- read-only invariant source: the constructor and `ReEstimate` reject negative estimates; replay must avoid invoking them for a corrupted persisted fact.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` -- read-only behavioral precedent: `ApplyPayload` refuses negative `ReEstimated`, retains `OwnEffort`, records a diagnostic, and rebuild still advances `LatestAcceptedSourceSequence`.
- `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs` -- focused direct aggregate/projection replay coverage and existing valid first-estimate, same-unit, and mismatched-unit regressions.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` -- read-only proof that a poisoned negative estimate degrades the projection and retains the last valid effort.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works.Contracts/State/WorkItemState.cs` -- guard negative `ReEstimated` before all effort construction/update branches while leaving unconditional sequence consumption intact -- prevent corrupted streams from wedging aggregate replay.
- [x] `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs` -- add a direct persisted-event regression through aggregate state and roll-up projection asserting no throw, complete effort retention, sequence advancement, and the projection's existing diagnostic -- lock aggregate and projection replay policy together.

**Acceptance Criteria:**
- Given aggregate state and roll-up state share an established valid effort, when both replay a persisted same-unit negative `ReEstimated` at sequence N, then neither replaces that effort, both replay sequences advance to N, aggregate replay does not throw, and the projection exposes its existing deterministic degradation diagnostic.
- Given aggregate state has no effort, when it replays a persisted negative `ReEstimated`, then it remains unestimated, does not throw, and advances its sequence.
- Given a persisted `ReEstimated` is non-negative, when aggregate replay applies it, then existing valid first-estimate, same-unit update, and mismatched-unit retention behavior is unchanged.

## Spec Change Log

## Review Triage Log

### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 1: (high 0, medium 0, low 1)
- defer: 0
- reject: 18: (high 2, medium 5, low 11)
- addressed_findings:
  - `low` `patch` Narrowed the aggregate replay comment to claim only last-valid-effort retention parity with the projection, avoiding an implication that aggregate state also emits the projection's degradation diagnostic.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release -m:1 -p:NuGetAudit=false` -- expected: focused test project builds with zero warnings and errors.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemReEstimateTests` -- expected: all focused re-estimate tests pass with no failures, skips, or tests not run.

## Auto Run Result

Status: done

Summary: Aggregate replay now consumes a persisted negative `ReEstimated` without invoking throwing effort construction or mutation paths. It retains the last valid effort (or remains unestimated), advances the payload sequence, and stays aligned with the projection's existing effort-retention behavior.

Files changed:
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` -- screens negative persisted estimates before effort construction/update while preserving unconditional replay-sequence advancement.
- `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs` -- adds direct established-effort aggregate/projection replay coverage and an unestimated aggregate replay regression.
- `_bmad-output/implementation-artifacts/spec-negative-reestimate-replay-guard.md` -- records intent, planning, review triage, verification, and completion evidence.

Review findings breakdown: 1 low-severity patch applied, 0 items deferred, and 18 suggestions rejected as duplicates, unsupported corruption scenarios, scope expansions beyond direct aggregate replay, or conflict with the explicit no-ledger-edit instruction.

Follow-up review recommendation: false. Patched findings: high 0, medium 0, low 1; score = `3 × 0 + 1 × 1 = 1`.

Verification performed:
- `dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release -m:1 -p:NuGetAudit=false` -- passed with 0 warnings and 0 errors after the review patch.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemReEstimateTests` -- passed 15/15 with 0 errors, failures, skips, or tests not run after the review patch.
- Matrix audit -- established negative, unestimated negative, valid first/same-unit, and non-negative mismatched-unit replay rows all had executed passing coverage.
- `git diff --check` -- passed; the deferred-work ledger and `.bmad-loop` run data were not edited.

Residual risks: none within DW-28's persisted negative re-estimate replay scope. Broader malformed-event validation and EventStore integration replay were intentionally unchanged.
