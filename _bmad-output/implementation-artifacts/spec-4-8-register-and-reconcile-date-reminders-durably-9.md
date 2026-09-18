---
title: 'Close remaining Story 4.8 spec-8 review patches'
type: 'bugfix'
created: '2026-09-17'
status: 'ready-for-dev'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'a292e3b2ad3ee57a8ee180adf360972297e57bb1'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8 remains in progress because spec-8 review left four accepted patches: the clean outer exact-cancellation arm lacks a direct regression, EventId 4608 guidance omits a row-local shutdown caveat, one ledger citation misses the stream cancellation filter, and close-out artifacts describe superseded gitlinks as current.

**Approach:** Add one mutation-resistant test without changing production behavior, correct and pin the operator guidance, repair the citation, and reconcile the audit artifacts with the adopted gitlinks before re-running deterministic gates.

## Boundaries & Constraints

**Always:** Keep clean exact-caller cancellation bare and unlogged. Use exactly two tenants in the new next-index test so deleting the outer `throw` produces a false successful scan rather than a later loop-head exception. Record current gitlinks at full length while preserving old-to-new revisions as historical evidence.

**Never:** Change `IndexedPendingDateAwaitSource`, its evidence-preserving `break`, any in-tenant cancellation filter, `DateReminderReconciler`, retry policy, EventId templates, durable contracts, dependency versions, or submodule pointers. Do not claim new Tier-3 evidence, commit, or push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Clean next-index cancellation | First of exactly two tenant indexes is empty; final index throws the exact caller token | Propagate that `OperationCanceledException`; no successful scan or log | Preserve the exact token; no retry classification |
| 4608 operator response | Parking lookup warning during startup scan | Qualify later reconciliation on host life and remaining retry budget | Restart after shutdown or exhausted retries |
| Gitlink evidence | Current root index and checkout are inspected | Record the three canonical committed SHAs and their approved adoption | Preserve historical transition facts; remove only stale current-state claims |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:68-108` -- read-only behavior under test; the clean outer exact-token arm at line 97 must remain unchanged.
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs:582-905` -- reuse the boundary and prior-evidence arrangements; add the exact two-tenant clean next-index case.
- `docs/operations/subscriber-dead-letter-operator.md:128-143` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs:92-169` -- mirror and pin the 4604/4606 host-lifecycle wording in row 4608.
- `_bmad-output/implementation-artifacts/deferred-work.md:967` -- extend the cited source range through the stream exact-token filter at line 197; do not change the deferred decision.
- `references/Hexalith.Chatbot`, `references/Hexalith.Conversations`, `references/Hexalith.EventStore` -- read-only evidence at `1047ef38d3845406227891639aaeb853e5d4f116`, `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and `b5541259058320a0a7f1db19038709cbd02dad85`.
- Story 4.8, spec-8, `sprint-status.yaml`, and `tests/test-summary.md` -- resolve the four findings, replace stale current-state gitlink claims, record verification, and return the story to review.

## Tasks & Acceptance

**Execution:**
- [ ] `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs` -- add the two-tenant clean next-index exact-cancellation regression; assert the exact token, one final-index read, and no warning log.
- [ ] `docs/operations/subscriber-dead-letter-operator.md` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs` -- add and pin the 4608 host-running, shutdown, retry-budget, and restart guidance.
- [ ] `_bmad-output/implementation-artifacts/deferred-work.md` -- correct the source citation from `:145-191` to `:145-197` without rewriting the append-only decision.
- [ ] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `spec-4-8-register-and-reconcile-date-reminders-durably-8.md`, and `tests/test-summary.md` -- record the adopted gitlinks, remove checkout-drift claims, keep transition history, and close the review findings.
- [ ] `Hexalith.Works.slnx`, direct test binaries, and `sprint-status.yaml` -- build, run focused and deterministic gates, record exact results, and return Story 4.8 to review.

**Acceptance Criteria:**
- Given a clean scan, when the final tenant-index read throws exact caller cancellation, then the exact exception propagates, no warning is logged, and no successful result is returned.
- Given the 4608 runbook row, when an operator follows it, then later-attempt guidance is conditional on host life and retry budget and restart guidance covers shutdown and exhaustion.
- Given the deferred ledger, when its citation is inspected, then it covers both exact-token filters through source line 197 without changing the deferred scope.
- Given current HEAD, when audit artifacts and gates are reconciled, then all current gitlink claims match the index and checkout, historical transitions remain accurate, and Story 4.8 returns to review without new live credit.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Verification

**Commands:**
- `git ls-files -s references/Hexalith.Chatbot references/Hexalith.Conversations references/Hexalith.EventStore` plus per-submodule `git rev-parse HEAD` -- expected: index and checkout equal the three recorded SHAs.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false` -- expected: 0 warnings, 0 errors.
- Direct Release Integration binary filtered to `IndexedPendingDateAwaitSourceTests` -- expected: all pass, 0 skipped.
- Direct Release Architecture binary filtered to `SubscriberDeadLetterOperatorDocumentationTests` -- expected: all pass, 0 skipped.
- Direct Unit, Property, serial non-smoke Integration, full Architecture, and Architecture excluding only the known SDK-pin fact -- expected: all non-blocked deterministic gates pass; report the existing SDK mismatch exactly if unchanged.
