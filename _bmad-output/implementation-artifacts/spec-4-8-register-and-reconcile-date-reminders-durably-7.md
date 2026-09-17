---
title: 'Resolve Story 4.8 remaining review action items'
type: 'bugfix'
created: '2026-09-17'
status: 'ready-for-dev'
route: 'dispatch'
review_loop_iteration: 0
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
- [ ] `IndexedPendingDateAwaitSource.cs` + its test -- patch between-tenant cancellation; keep three in-tenant filters unchanged.
- [ ] `ReminderReconciliationService.cs` + its test -- complete cleanly when retry delay receives the exact stop token.
- [ ] `WorksRecoveryLog.cs`, operator runbook, and its fitness test -- reconcile log docs and 4603/4605 guidance.
- [ ] `LinkConversationRuntimeAdapterTests.cs` -- data-contract round-trip malformed tenants for fourteen ordinary adapters.
- [ ] Story, `deferred-work.md`, `sprint-status.yaml`, and `tests/test-summary.md` -- reconcile ledger, File List, evidence, nine checkboxes, and status.

**Acceptance Criteria:**
- Given a prior failure, when shutdown occurs between tenants, then typed evidence survives and no later tenant is read.
- Given a retry wait, when the exact stop token is canceled, then the service completes without a fault or retry.
- Given malformed data-contract tenant values, when ordinary adapters dispatch, then results match the kernel and existing reserved/`LinkConversation` guards remain.
- Given final artifacts, when checked against HEAD and executed evidence, then nine actions are closed, history stays auditable, and story plus sprint read `review`.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

At the next-tenant boundary, cancellation with prior failures exits into the typed throw; otherwise it stays bare. Remove the redundant post-return branch. Catch the exact stop-token race during `Task.Delay`, not only before it.

## Verification

**Commands:**
- `dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false` -- 0 warnings/errors.
- Run the three changed IntegrationTests classes and `SubscriberDeadLetterOperatorDocumentationTests` from their Release binaries -- all focused cases pass without skips.
- Run all four direct test binaries; separate the known SDK-pin architecture failure from focused evidence. Do not run Tier-3 smoke lanes.
