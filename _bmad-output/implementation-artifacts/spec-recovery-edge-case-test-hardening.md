---
title: 'Harden recovery edge-case tests and skip diagnostics'
type: 'bugfix'
created: '2026-08-27'
status: 'done'
baseline_revision: '9658cb7186ad444cc73b5f44a6787a308fed82b4'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      External test cancellation is converted into an unavailable-port result by the pre-existing TCP probe.
    evidence: |-
      `IsPortReachableAsync` catches every `OperationCanceledException` and returns false, so cancellation from `TestContext.Current.CancellationToken` is indistinguishable from the helper's two-second probe timeout and may produce a misleading `Assert.Skip`.
    location: >-
      tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:452
    severity: medium
---

<intent-contract>

## Intent

**Problem:** The recovery integration suite does not pin all parent events that clear child-completion awaits, the exact stale-checkpoint prune boundary, or the particular prerequisite port that causes the Tier-3 cascade lane to skip. These gaps permit recovery regressions and leave unavailable infrastructure harder to diagnose.

**Approach:** Add deterministic cases around the existing recovery seams and change only the Tier-3 test prerequisite probe/skip reporting so it identifies the first unavailable port. Preserve production semantics and the live lane's existing end-state assertions.

## Boundaries & Constraints

**Always:** Cover `WorkItemResumed`, `WorkItemCancelled`, `WorkItemExpired`, `WorkItemCompleted`, and terminal `WorkItemRejected` await clearing; assert an entry exactly 24 hours old is retained under a 24-hour stale threshold; report the actual OS-specific unavailable prerequisite port; follow existing xUnit v3, Shouldly, NSubstitute, cancellation, and test-fixture conventions.

**Block If:** Implementation would require changing production recovery semantics, broadening the bundle beyond the three named test files, or choosing a different stale-boundary rule than the existing strict-greater-than behavior.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md` or any `.bmad-loop` ledger; change `CascadeRecoveryReconciler`, `StreamReadingChildCompletionAwaitingParentSource`, the reactor/kernel, packages, AppHost topology, or live cascade end-state assertions; hide missing prerequisites behind a combined boolean or static all-ports message.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Parent await clearing | Suspended parent followed by each clearing event | Rebuilt awaiting-parent result is empty for every event type | Any omitted type fails its named deterministic case |
| Exact stale boundary | Dangling index entry aged exactly 24 hours with threshold 24 | Entry remains indexed and no command is submitted | A `>=` regression fails deterministically |
| Missing Tier-3 prerequisite | Redis, placement, or scheduler probe cannot reach its OS-specific port | Lane skips with that exact unavailable port in the reason | Probe cancellation/unreachability is reported as that port |

</intent-contract>

## Code Map

- `tests/Hexalith.Works.IntegrationTests/StreamReadingChildCompletionAwaitingParentSourceTests.cs:63` -- the existing resumed-parent case and `PageFor` fixture are the reuse points; extend fixture input and add per-event coverage without weakening stream traversal assertions.
- `src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:74` -- read-only evidence: `RebuildAwaitConditions` clears the await set through one switch arm for resumed, cancelled, expired, completed, and rejected events.
- `tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs:83` -- the manual clock and directly seeded dangling index already cover age zero and 25 hours; add an exact 24-hour observation before advancing beyond the threshold.
- `src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs:53` -- read-only evidence: pruning intentionally uses `now - AddedAt > staleAfter`, so equality must retain the entry.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:49` -- retain both live phases and all convergence assertions; replace the combined prerequisite boolean at line 376 with a result that surfaces the first unreachable Redis/placement/scheduler port in the skip reason.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` -- affected xUnit v3 project; build once and run focused class lanes from its Release test assembly.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Hexalith.Works.IntegrationTests/StreamReadingChildCompletionAwaitingParentSourceTests.cs` -- parameterize the parent-stream fixture for clearing-event type and add deterministic cases for all five shared-switch events, preserving the current positive-await and fail-closed tests.
- [x] `tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs` -- split clock advancement around the threshold and assert the dangling entry remains at exact equality before proving it is pruned beyond the threshold.
- [x] `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs` -- return the first unavailable prerequisite port from sequential probes and include that actual port in `Assert.Skip`, retaining OS-specific placement/scheduler ports and every existing live assertion.

**Acceptance Criteria:**
- Given a parent stream contains child-completion awaits, when each supported resume/terminal clearing event follows the suspension, then the outer source returns no awaiting parent for every event type.
- Given a dangling cascade index entry and a 24-hour threshold, when recovery runs at exactly 24 hours and again after the threshold, then equality retains the entry, the later run removes it, and neither run submits a cascade command.
- Given any probed Tier-3 prerequisite port is unreachable, when the cascade smoke lane gates startup, then the skip reason names that specific OS-resolved port while the existing live convergence assertions remain unchanged when all prerequisites are available.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 1: (high 0, medium 1, low 0)
- defer: 1: (high 0, medium 1, low 0)
- reject: 19: (high 0, medium 2, low 17)
- addressed_findings:
  - `[medium]` `[patch]` Added an all-ports-reachable prerequisite-probe test that asserts every OS-resolved port is probed in order and the helper returns no unavailable port, preventing a healthy Tier-3 lane from silently skipping.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:MinVerVersionOverride=1.0.0` -- expected: succeeds with zero warnings and errors.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.StreamReadingChildCompletionAwaitingParentSourceTests` -- expected: all deterministic child-completion source tests pass.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.CascadeCheckpointIndexRecoveryTests` -- expected: all checkpoint recovery tests pass, including exact equality.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.WorksCascadeRecoveryPipelineSmokeTests` -- expected: live lane passes when prerequisites exist or skips with the exact missing port.

**Results:**
- Integration test project Release build succeeded with 0 warnings and 0 errors.
- `StreamReadingChildCompletionAwaitingParentSourceTests`: 10 passed, 0 failed, 0 skipped.
- `CascadeCheckpointIndexRecoveryTests`: 4 passed, 0 failed, 0 skipped.
- `WorksCascadeRecoveryPipelineSmokeTests`: 4 deterministic prerequisite cases passed; the live case skipped with the exact first unavailable prerequisite, `localhost:50005`.

## Auto Run Result

Status: done

Summary: Added deterministic recovery coverage for all await-clearing parent events, pinned strict-greater-than checkpoint staleness at exact equality, and made Tier-3 cascade skips identify the first unavailable OS-specific prerequisite port without changing production semantics.

Files changed:
- `tests/Hexalith.Works.IntegrationTests/StreamReadingChildCompletionAwaitingParentSourceTests.cs` -- covers resumed, cancelled, expired, completed, and terminal rejected parent events through the real stream-reading source.
- `tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs` -- retains a dangling entry at exactly 24 hours and prunes it only after the threshold.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs` -- reports the first unavailable prerequisite port and deterministically covers unavailable and all-reachable probe paths.
- `_bmad-output/implementation-artifacts/spec-recovery-edge-case-test-hardening.md` -- records the implementation contract, verification, review triage, and final result.

Review findings breakdown: 1 medium patch applied; 1 medium pre-existing issue deferred; 19 findings rejected. Follow-up review recommendation: false (patched findings: high 0, medium 1, low 0; score `3 × 1 + 1 × 0 = 3`).

Verification performed:
- Release integration-test project build succeeded with 0 warnings and 0 errors.
- Child-completion source class: 10 passed, 0 failed, 0 skipped.
- Cascade checkpoint recovery class: 4 passed, 0 failed, 0 skipped.
- Cascade smoke class: 4 deterministic cases passed; 1 live case skipped and named `localhost:50005` as unavailable.

Residual risks: the existing live Aspire convergence case did not execute because Dapr placement was unavailable on `localhost:50005`; its end-state assertions are unchanged. The pre-existing TCP probe still converts external test cancellation into an unavailable-port result, recorded in frontmatter `deferred` for later focused work.
