---
title: 'Complete Story 4.8 reminder recovery and scheduling hardening'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '1100405c5492bf54a2b43ef4206c0706333a8d2c'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8 still hides parked reminder candidates from pass-level outcomes, mislabels parking-store faults as stream failures, and omits bounded scheduling-failure telemetry. Several deterministic tests also leave the approved retry, validation, and cleanup behavior weakly pinned.

**Approach:** Complete the deferred reminder-recovery bundle: make parked skips countable without making them retryable, classify parking and scheduler failures precisely, and strengthen focused tests around multi-date scheduling, retry exhaustion, option validation, and test-host cleanup.

## Boundaries & Constraints

**Always:** Preserve stream-as-truth refolding, partial-result processing followed by typed incomplete-scan retry, deterministic reminder identity, and scheduler-exception propagation for at-least-once redelivery. Keep telemetry warning-level and bounded to reason codes, tenant/work-item identity, deterministic reminder name, and counts.

**Never:** Treat a parked candidate as a failed scan, read its stream, add unpark/replay behavior, change durable keys/contracts or kernel/Reactor behavior, add periodic reconciliation or a new exhaustion policy, rerun the live reminder lane, update submodules, or reopen the marker-store/DW-56 tracking decision.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Parked discovery | Indexed candidate has terminal parking state | Skip the stream, emit 4607 Warning, and expose one skipped parked candidate in clean or incomplete outcomes | Do not consume retry budget |
| Parking lookup fault | Parking document read throws | Do not read the stream; count the candidate failure and emit 4608 Warning | Preserve partial results and typed incomplete-scan retry |
| Scheduling fault | One folded pending date await cannot be scheduled | Emit bounded 4609 Warning and rethrow the original exception | Suppress the warning only for cancellation attributable to the caller token |
| Multiple dates | Latest suspension contains two distinct `DateReached` conditions | Schedule both with distinct deterministic names and correct due times | Duplicate delivery remains idempotent |
| Retry budget | Startup reconciliation always fails | Finish naturally after exactly the configured maximum attempts | No production retry-policy change |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Reminders/IPendingDateAwaitSource.cs`, `PendingDateAwaitScanResult.cs`, `IndexedPendingDateAwaitSource.cs` -- return pending awaits plus parked-skip count; isolate parking-store reads from authoritative stream reads.
- `src/Hexalith.Works/Reminders/PendingDateAwaitScanIncompleteException.cs`, `DateReminderReconciler.cs` -- retain the count through incomplete scans, rewraps, structured logging, and `ReminderReconciliationOutcome`.
- `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` -- include parked count in 4605, raise 4607 to Warning, and add bounded 4608/4609 events.
- `src/Hexalith.Works/Reminders/WorkItemSuspendedReminderHandler.cs` -- log scheduling failure per reminder and rethrow; retain authoritative fold and cancellation semantics.
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`, `DateReminderRecoveryRuntimeTests.cs`, `PendingDateAwaitScanIncompleteExceptionTests.cs` -- pin clean/incomplete count propagation and correct 4606/4608 classification.
- `tests/Hexalith.Works.IntegrationTests/WorkItemSuspendedReminderHandlerTests.cs`, `ReminderReconciliationServiceTests.cs` -- pin multi-date scheduling, bounded failure diagnostics, original-exception propagation, and natural max-attempt completion.
- `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs` -- assert the exact projection validation failure and make temporary key-directory cleanup exception-safe.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works/Reminders/IPendingDateAwaitSource.cs`, `PendingDateAwaitScanResult.cs`, `IndexedPendingDateAwaitSource.cs`, `PendingDateAwaitScanIncompleteException.cs`, `DateReminderReconciler.cs` -- implement scan-result/count propagation and split parking-read failure handling from stream-read failure handling.
- [x] `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs`, `src/Hexalith.Works/Reminders/WorkItemSuspendedReminderHandler.cs` -- add bounded 4605/4607/4608/4609 telemetry and steady-state scheduling failure handling without weakening redelivery.
- [x] `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`, `DateReminderRecoveryRuntimeTests.cs`, `PendingDateAwaitScanIncompleteExceptionTests.cs`, `WorkItemSuspendedReminderHandlerTests.cs`, `ReminderReconciliationServiceTests.cs`, `WorksRecoveryOptionsTests.cs` -- add or strengthen focused deterministic coverage for every matrix row, exact option validation, and exception-safe test-host cleanup.
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md` -- reconcile review checkboxes and validation evidence using only results produced by this run.

**Acceptance Criteria:**
- Given clean and incomplete scans containing parked candidates, when reconciliation consumes them, then skipped counts remain observable while only actual read failures trigger retry.
- Given scheduler or startup-reconciliation failure, when the focused tests run, then bounded diagnostics and the configured retry/redelivery semantics are proven without exposing exception text.
- Given the repository gates run, when verification completes, then the Release build and affected deterministic suites pass with zero new failures, and catalog 40 plus historical live-lane evidence remain unchanged.

## Implementation Notes

- Added `PendingDateAwaitScanResult` as the source-pass contract and carried `SkippedParkedCount` through tenant
  aggregation, `PendingDateAwaitScanIncompleteException`, EventId 4605, and
  `ReminderReconciliationOutcome`. Parked candidates remain clean skips and never consume the retry budget.
- Isolated the parking document lookup from the authoritative stream fold. A parking-store fault records one
  candidate failure, emits bounded Warning 4608 with reason `parking-read-failed`, leaves the stream unread, and
  preserves partial results for typed retry. A confirmed park emits Warning 4607 and increments only the skip
  count.
- Added bounded Warning 4609 around each steady-state schedule call. The original scheduler exception is
  rethrown for redelivery; only an `OperationCanceledException` attributable to the caller token bypasses the
  warning. Startup Warning 4603 was also kept bounded by omitting attached exception text.
- Added deterministic facts for clean/incomplete skip-count propagation, 4606/4608 classification, multi-date
  registration, scheduling failure/cancellation semantics, exact retry exhaustion, exact option validation, and
  exception-safe temporary key-directory cleanup. A shared recording logger keeps telemetry assertions aligned.

## Spec Change Log

- 2026-09-16 — Implemented the approved reminder recovery/scheduling hardening bundle and recorded verification.

## Review Triage Log

| ID | Verdict | Route | Evidence |
|---|---|---|---|
| VG-1 | medium | patch | The sole 4608 test has one candidate, so replacing the new `continue` with `break` would suppress a later healthy candidate without failing. Add a same-tenant continuation/partial-results fact. |
| VG-2 | medium | patch | The 4608/4609 tests do not inspect structured `TenantId` or `WorkItemId`; swapped wrapper arguments would pass. Capture structured logger state and assert identities, reason, and reminder name. |
| VG-3 | medium | patch | The cleanup helpers have no observation of the generated path and no build/dispose failure injection, so making deletion a no-op would leave the suite green. Add focused private-seam tests for both failure paths. |
| VG-4 | false | reject | The EventStore pointer was already dirty before this workflow started, the nested repository is clean, and the implementation agent did not update it. Reverting it would destroy preserved user state rather than fix this story. |
| BH-1 | false | reject | Same as VG-4: the submodule pointer predates the story implementation and was explicitly left untouched. It is present only because the required baseline diff includes all working-tree state. |
| BH-2 | medium | patch | `cancellationToken.IsCancellationRequested` alone cannot attribute the caught cancellation; a scheduler-owned token can coincide with caller cancellation and suppress 4609. Match the exception token and add a two-token race fact. |
| BH-3 | medium | defer | `DateReminderReconciler` has excluded every `OperationCanceledException` from incomplete-scan rewraps since before this baseline. A scheduler-owned cancellation can discard typed scan context, but this is a pre-existing reconciler policy defect. |
| BH-4 | low | defer | EventId 4603's pre-existing template says every failure will be retried even on the final configured attempt. The message is misleading during exhaustion, but correcting the shared event needs a distinct exhausted-policy decision. |
| BH-5 | medium | defer | Pre-existing 4604/4606 calls attach provider/gateway exceptions and can expose unbounded text. The current intent adds bounded 4608/4609 telemetry; changing the older tenant/stream diagnostics is separate recovery-observability work. |
| BH-6 | medium | patch | The governing story still says scheduling failures continue in-place, while the approved frozen spec requires rethrow for marker release and at-least-once redelivery. Correct the stale story sentence to the implemented policy. |
| BH-7 | false | reject | The handler correctly proves two distinct pending awaits and due times; deterministic identity is separately pinned by `DateReminderName` tests, and the unchanged actor computes its registration name from the same correlation key. A constant actor-name mutation is outside this changed surface. |
| BH-8 | medium | patch | Duplicate of VG-2 at 4609: the rendered-message assertion does not pin structured tenant/work-item attribution. |
| BH-9 | medium | patch | Duplicate of VG-1: the one-candidate parking-read test cannot prove later candidates remain available as partial results. |
| BH-10 | medium | patch | No test drives an incomplete scan followed by submit/schedule failure, so the new skipped count and either cause could be dropped from the typed rewrap. Add a fact asserting counts and both aggregate causes. |
| BH-11 | low | patch | Every new parked-count fact uses one parked candidate, so `+=` could regress to assignment. A direct multi-park aggregation fact is small and protects a likely multi-item state. |
| BH-12 | low | reject | A simultaneous host failure and temp-directory deletion failure can mask the primary exception, but that double fault is rare and choosing preserve-versus-aggregate semantics would add complexity beyond a direct correction. |
| EC-1 | medium | defer | Duplicate of BH-3: non-caller cancellation can bypass the typed incomplete-scan rewrap, but the filter is pre-existing rather than caused by this story. |
| EC-2 | false | reject | `Pending` is a non-nullable public contract and every in-repository source constructs it non-null; a custom source passing `null!` violates the annotated interface and already fails loudly. No reachable repository path was shown. |
| EC-3 | medium | patch | Duplicate of BH-2: caller-token state alone does not establish that the caught cancellation came from that token. |
| EC-4 | low | reject | A throwing logging provider could replace the scheduler exception, but standard providers do not normally throw and guarding every logger call would add defensive branches for an undemonstrated runtime state. |
| EC-5 | low | reject | Duplicate of BH-12: cleanup can mask a simultaneous primary failure, but the rare double-fault semantics are not settled and the fix is non-trivial. |
| EC-6 | medium | patch | Duplicate of VG-3: the exception-safe cleanup claim lacks an effective failure-path test. |

## Design Notes

`PendingDateAwaitScanResult` is the explicit source-pass value. `SkippedParkedCount` also travels on `PendingDateAwaitScanIncompleteException` and `ReminderReconciliationOutcome`; the existing outcome constructor keeps a default zero to reduce caller churn. Per-candidate 4607 remains the operator signal—no second aggregate summary log is added.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -m:1 -p:NuGetAudit=false` -- expected: zero warnings and errors.
- `for test_class in IndexedPendingDateAwaitSourceTests DateReminderRecoveryRuntimeTests PendingDateAwaitScanIncompleteExceptionTests WorkItemSuspendedReminderHandlerTests ReminderReconciliationServiceTests WorksRecoveryOptionsTests; do tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class "*${test_class}" || exit; done` -- expected: all six deterministic classes pass without starting Aspire.
- `dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false --no-restore` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: no new failures; record the pre-existing SDK-pin mismatch separately if it remains.

**Results (2026-09-16):**

- Focused IntegrationTests Release build: 0 warnings, 0 errors.
- Six focused deterministic classes: 56/56 passed, 0 skipped (`18 + 8 + 2 + 12 + 2 + 14`).
- Release solution build: 0 warnings, 0 errors.
- Full deterministic suites: UnitTests 568/568, PropertyTests 3/3, and IntegrationTests excluding
  `*SmokeTests` 377/377; all passed with 0 skipped.
- ArchitectureTests: 236/237 passed. The sole failure is the pre-existing
  `P0_GlobalJsonPinsSdkTestRunnerAndAspireSdk` mismatch (test expects SDK `10.0.400`; checked-in `global.json`
  pins `10.0.401`). Excluding exactly that assertion passed 236/236, including the catalog guard at 40.
- The live reminder lane was not run, as required by the frozen boundary. Its historical evidence is unchanged.
