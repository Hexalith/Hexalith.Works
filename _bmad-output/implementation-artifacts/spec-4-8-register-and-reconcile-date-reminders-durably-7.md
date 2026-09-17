---
title: 'Resolve Story 4.8 remaining review action items'
type: 'bugfix'
created: '2026-09-17'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '3c042f94c1c7cc01cae18a4d015271e5318e609a'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8 has nine review actions: shutdown can discard typed incomplete-scan evidence or fault the retry delay, while tests, telemetry, and tracking artifacts do not fully describe the ratified behavior.

**Approach:** Apply the two 2026-09-17 decisions and seven direct patches as one close-out, then return the story to review.

## Boundaries & Constraints

**Always:** Keep partial-result reconciliation, exact-token filters inside a tenant, bounded log fields with structured exceptions, and ordinary adapters' tolerance of malformed non-reserved envelope tenants. Correct ledger history with dated resolution notes. Record executed test counts.

**Never:** Add EventId 4610, exhaustion/per-await policy, in-tenant cancellation changes, envelope validation, durable-contract changes, dependency/submodule updates, or live-lane claims. Leave `DateReminderReconciler` and the SDK-pin blocker unchanged.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Prior failure then shutdown | Cancellation occurs between tenants after a recorded failure | Throw typed incomplete evidence; do not read another tenant | Do not discard partial results/counts/cause |
| Clean shutdown | Cancellation occurs between tenants with no failure | Preserve bare exact caller cancellation | No retry noise |
| Retry delay | Host stops after a failed startup attempt | Log 4603 once and complete without another attempt | Swallow only exact stopping-token cancellation |
| Malformed envelope | Data-contract tenant is null, empty, or invalid | Fourteen ordinary adapters preserve kernel results | `LinkConversation` stays strict |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs` -- route cancellation after prior failure into the typed throw; remove redundant post-return logic; retain/document in-tenant behavior.
- `src/Hexalith.Works/Reminders/ReminderReconciliationService.cs` -- contain exact stopping-token cancellation around retry delay.
- `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` -- correct 4604/4606 wording; document 4605's exception.
- `tests/Hexalith.Works.IntegrationTests/{IndexedPendingDateAwaitSourceTests,ReminderReconciliationServiceTests,LinkConversationRuntimeAdapterTests}.cs` -- pin cancellation and data-contract cases.
- `docs/operations/subscriber-dead-letter-operator.md` plus its architecture test -- add 4603 guidance and point 4605 at it; retain 4609 for scheduling.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- resolve two stale telemetry claims and correct the architecture count while preserving the real SDK mismatch and existing submit deferral.
- Story 4.8, `sprint-status.yaml`, and `tests/test-summary.md` -- fix the three-file omission, record evidence, close nine actions, and set `review`.

## Tasks & Acceptance

**Execution:**
- [x] `IndexedPendingDateAwaitSource.cs` + its test -- patch between-tenant cancellation; keep three in-tenant filters unchanged.
- [x] `ReminderReconciliationService.cs` + its test -- complete cleanly when retry delay receives the exact stop token.
- [x] `WorksRecoveryLog.cs`, operator runbook, and its fitness test -- reconcile log docs and 4603/4605 guidance.
- [x] `LinkConversationRuntimeAdapterTests.cs` -- data-contract round-trip malformed tenants for fourteen ordinary adapters.
- [x] Story, `deferred-work.md`, `sprint-status.yaml`, and `tests/test-summary.md` -- reconcile ledger, File List, evidence, nine checkboxes, and status.

**Acceptance Criteria:**
- Given a prior failure, when shutdown occurs between tenants, then typed evidence survives and no later tenant is read.
- Given a retry wait, when the exact stop token is canceled, then the service completes without a fault or retry.
- Given malformed data-contract tenant values, when ordinary adapters dispatch, then results match the kernel and existing reserved/`LinkConversation` guards remain.
- Given final artifacts, when checked against HEAD and executed evidence, then nine actions are closed, history stays auditable, and story plus sprint read `review`.

## Implementation Notes

- Superseded review close-out (spec-8): closed the seven follow-up patches without changing frozen spec-7
  intent, in-tenant filters, retry policy, durable contracts, or runtime EventIds. Exact retained gitlinks and
  refreshed deterministic evidence are recorded in the parent story and test summary.

- Preserved partial scan evidence by routing between-tenant shutdown after any recorded failure into `PendingDateAwaitScanIncompleteException`; clean shutdown still throws the exact caller cancellation, and the three in-tenant exact-token filters are unchanged.
- Contained only exact stopping-token cancellation from the startup retry delay, so shutdown after a failed attempt completes the background service without another attempt or fault.
- Reconciled recovery telemetry documentation, operator guidance, stale ledger claims, the Story 4.8 review record, sprint status, and executed evidence without adding a new event or changing durable contracts.
- Added the five-value malformed data-contract tenant matrix for all fourteen ordinary runtime adapters while retaining the existing strict `LinkConversation` coverage.
- Verified Release build with 0 warnings/errors; focused suites passed 26, 4, 136, and 3 tests after review fixes; direct Unit, Property, and non-smoke Integration binaries passed 568, 3, and 489 tests. Architecture passed 237/238, with only the pre-existing SDK-pin mismatch (`10.0.400` expected versus `10.0.401` configured); excluding that exact blocker passed 237/237. Tier-3 smoke lanes were not run.

## Spec Change Log

- 2026-09-17: Implemented all five execution tasks, closed the nine Story 4.8 review actions, recorded executed evidence, and advanced the story and sprint entry to `review`.
- 2026-09-17: Spec-8 closed all seven follow-up review patches, corrected the incorrectly sited outer-cancellation deferral, and refreshed gitlink/gate evidence.

## Review Triage Log

| Finding | Verdict / route | Evidence |
|---|---|---|
| BH-01 | medium / defer | A cancellation can arrive after the outer boundary check and make the next tenant-index read throw the exact caller token, which rethrows bare and drops earlier evidence. At that point the next tenant scan has begun, so fixing it requires changing the exact-token in-tenant filter that the frozen intent explicitly leaves unchanged. |
| BH-02 | medium / patch | The newly reachable shutdown-after-incomplete path emits 4605 and then exits from the canceled retry delay, so the runtime phrase `will retry` is not always true; it should describe later retry eligibility instead. |
| BH-03 | low / patch | Existing tests preserve partial results during ordinary failures, but the new boundary-shutdown fact carries an empty result. Clearing accumulated results only in the boundary branch would therefore escape the focused coverage. |
| BH-04 | low / patch | The new outer guard has independent failed-tenant and failed-candidate branches, while the added multi-tenant shutdown fact exercises only the failed-tenant branch. |
| BH-05 | false / reject | `BackgroundService.StartAsync` invokes `ExecuteAsync` synchronously until its first incomplete await; all source/reconciler failure tasks in this test are already completed, so `StartAsync` returns only after the 60-second `Task.Delay` has been entered. Stopping afterward does cancel an active delay. |
| BH-06 | false / reject | The changed delay is `Task.Delay(..., stoppingToken)`, whose cancellation completion carries the token supplied to it. A foreign dependency cancellation cannot originate from that delay path, so the proposed production scenario is not reachable. |
| BH-07 | low / patch | The 70-row theory proves adapter parity but never proves that the data-contract round trip retained each requested null, empty, over-length, non-ASCII, or invalid-shape tenant value. |
| BH-08 | medium / patch | EventId 4603 is shared by seven reminder, cascade, child-completion, and actor paths, but the new row describes every occurrence as a bounded startup retry. Guidance must branch on the structured `Reason`. |
| BH-09 | medium / patch | No later startup attempt can mean host shutdown during the retry delay as well as exhausted budget, so the new 4603 operator conclusion is not sound as written. |
| BH-10 | medium / patch | Exact caller cancellation while partial results are processed intentionally emits no 4603; the new 4605 row needs to limit its 4603 promise to non-cancellation failures. |
| BH-11 | low / patch | The only 4605 caller passes `PendingDateAwaitScanIncompleteException`, whose exception chain contains the final dependency cause; the new XML text incorrectly calls the structured slot itself the dependency failure. |
| BH-12 | medium / patch | The new architecture assertion requires generic 4603 prose to contain startup and retry-budget language, reinforcing the shared-EventId error instead of requiring reason-scoped guidance. |
| BH-13 | low / patch | A successful retry with no pending awaits emits no 4602 completion record, so the new instruction to confirm completion has no generally observable signal. The runbook should ask operators to verify dependency health and watch for repeated 4603 rather than claim a completion event. |
| EC-01 | medium / defer | Independent tracing confirms BH-01: exact cancellation after the loop check can bypass the typed result. It is the same in-tenant exact-token limitation explicitly excluded by the frozen intent. |
| EC-02 | medium / patch | Independent tracing confirms BH-08: 4603 has non-startup callers and therefore needs reason-scoped operator advice. |
| VG-01 | low / patch | The verification-gap review found no test combining a first-tenant candidate failure, caller cancellation, and a following tenant; changing the guard to tenant failures only leaves all current tests green. |
| VG-02 | low / patch | No test asserts the rendered 4604/4606 shutdown qualification, so restoring the old unconditional `still scanned` wording leaves the current suite green. |

## Design Notes

At the next-tenant boundary, cancellation with prior failures exits into the typed throw; otherwise it stays bare. Remove the redundant post-return branch. Catch the exact stop-token race during `Task.Delay`, not only before it.

## Verification

**Commands:**
- `dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false` -- 0 warnings/errors.
- Run the three changed IntegrationTests classes and `SubscriberDeadLetterOperatorDocumentationTests` from their Release binaries -- all focused cases pass without skips.
- Run all four direct test binaries; separate the known SDK-pin architecture failure from focused evidence. Do not run Tier-3 smoke lanes.

### Review Findings

- [x] [Review][Patch] Three submodule pointers moved in the reviewed range, against spec-7's frozen Never list — **Decided 2026-09-17 (human): keep, re-record gates, document the Never-list deviation.** Resolved by spec-8 with full retained revisions, refreshed gates, and explicit committed-gitlink versus checkout evidence. [references/Hexalith.EventStore]
- [x] [Review][Patch] The remaining between-tenant shutdown window is real, and spec-7 deferred it on the wrong site — **Decided 2026-09-17 (human): `break` at `:88-92` when counts are already non-zero; correct the ledger.** Resolved by spec-8 with the outer catch only, a targeted exact-token regression, and an append-only ledger correction. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:88-92]
- [x] [Review][Patch] Class remarks still say every eligible tenant is attempted and that between-tenant cancellation always preserves collected failure evidence — resolved by spec-8 with boundary-qualified and scope-accurate remarks. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:26-33]
- [x] [Review][Patch] The 4603 table row is reason-scoped, then the following paragraph treats 4603–4609 as startup-recovery evidence handled by the bounded retry policy; the fitness test only reads table rows — resolved by spec-8 with origin-scoped prose and a post-table architecture assertion. [docs/operations/subscriber-dead-letter-operator.md:139]
- [x] [Review][Patch] Operator 4604 and 4606 rows still tell operators to confirm a later startup/reconciliation attempt, without the shutdown caveat this bundle added to those log templates — resolved by spec-8 with conditional-attempt and restart guidance pinned by the architecture test. [docs/operations/subscriber-dead-letter-operator.md:131]
- [x] [Review][Patch] Nested backticks in the 4603/4605 cells (`same-`Reason``) break the rendered field name; the architecture test matches the raw source — resolved by spec-8 with valid rendered `Reason` text and negative assertions for the broken form. [docs/operations/subscriber-dead-letter-operator.md:130]
- [x] [Review][Patch] Restoring the old 4605 template ending `will retry` still keeps `DateReminderRecoveryRuntimeTests` green because it only asserts EventId, `"2 parked"`, and the structured exception — resolved by spec-8 with exact retry-eligibility and negative `will retry` assertions. [tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs:157]
- [x] [Review][Defer] EventId 4603's runtime template still says the step "will be retried at-least-once" [src/Hexalith.Works/Runtime/WorksRecoveryLog.cs:36] — deferred: pre-existing shared template already recorded under spec-5; this close-out newly fires it on shutdown-after-incomplete.
- [x] [Review][Defer] Exact caller cancellation after a prior in-tenant candidate failure still discards that tenant's locals [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:145-191] — deferred: spec-7 Never list; already recorded at `deferred-work.md:957`.
- [x] [Review][Defer] `DateReminderReconciler` remarks still say an incomplete scan is rethrown so the hosted service retries it [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:33-38] — deferred: spec-7 Never list leaves that type unchanged.
