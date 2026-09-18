---
title: 'Close remaining Story 4.8 spec-8 review patches'
type: 'bugfix'
created: '2026-09-17'
status: 'done'
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
- [x] `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs` -- add the two-tenant clean next-index exact-cancellation regression; assert the exact token, one final-index read, and no warning log.
- [x] `docs/operations/subscriber-dead-letter-operator.md` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs` -- add and pin the 4608 host-running, shutdown, retry-budget, and restart guidance.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- correct the source citation from `:145-191` to `:145-197` without rewriting the append-only decision.
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `spec-4-8-register-and-reconcile-date-reminders-durably-8.md`, and `tests/test-summary.md` -- record the adopted gitlinks, remove checkout-drift claims, keep transition history, and close the review findings.
- [x] `Hexalith.Works.slnx`, direct test binaries, and `sprint-status.yaml` -- build, run focused and deterministic gates, record exact results, and return Story 4.8 to review.

**Acceptance Criteria:**
- Given a clean scan, when the final tenant-index read throws exact caller cancellation, then the exact exception propagates, no warning is logged, and no successful result is returned.
- Given the 4608 runbook row, when an operator follows it, then later-attempt guidance is conditional on host life and retry budget and restart guidance covers shutdown and exhaustion.
- Given the deferred ledger, when its citation is inspected, then it covers both exact-token filters through source line 197 without changing the deferred scope.
- Given current HEAD, when audit artifacts and gates are reconciled, then all current gitlink claims match the index and checkout, historical transitions remain accurate, and Story 4.8 returns to review without new live credit.

## Implementation Notes

- Added `Clean_exact_caller_cancellation_from_the_final_tenant_index_propagates_without_warning` with exactly two
  registry tenants. The first tenant index is empty; the final tenant-index read cancels and throws the exact caller
  token. The fact asserts that token, exactly one final-index read, and no Warning-or-higher entry. Removing the clean
  outer `throw;` would return a successful empty scan and fail the fact. Production source remains unchanged.
- EventId 4608 now mirrors the 4604/4606 lifecycle rule: a later attempt is conditional on the host still running
  and retry budget remaining, shutdown can prevent it, and remediation requires restart after shutdown or
  exhaustion. The architecture fact pins each condition in the row itself.
- Extended the append-only in-tenant cancellation citation through `IndexedPendingDateAwaitSource.cs:145-197`;
  the deferred decision and scope are unchanged.
- Reconciled the adopted root gitlinks and matching checkouts at Chatbot
  `1047ef38d3845406227891639aaeb853e5d4f116`, Conversations
  `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and EventStore
  `b5541259058320a0a7f1db19038709cbd02dad85`. Audit artifacts retain the earlier revisions as transition history
  and state that the root index and checkouts agree. This patch changed no submodule pointer.
- Closed all four spec-8 follow-up findings, refreshed the deterministic evidence, and returned the parent story
  plus sprint entry to review. No Tier-3 lane ran and no live credit was added.
- Review hardened the cancellation fact to assert the same exception instance and exact caller token at every
  index read, and hardened the 4608 architecture fact to pin the later candidate read and exhausted-retry restart
  condition. The post-review deterministic rerun matched the recorded results.

## Spec Change Log

- 2026-09-18: Implemented all five execution tasks, closed the four remaining spec-8 review patches, refreshed
  every deterministic gate, and returned Story 4.8 plus sprint tracking to review.
- 2026-09-18: Review applied two test-only hardening patches, recorded two pre-existing archive-quality deferrals,
  and reran the complete deterministic verification set with unchanged results.

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| BH-01 — Ledger archiving exceeds the spec-9 citation task | false | reject | Commit `5856fab` had already created the archive and compacted the ledger before this build run began. The spec-9 implementation changes only the required `:145-191` → `:145-197` citation, so attributing the migration to this implementation is false. |
| BH-02 — Story File List omits `deferred-work-archive.md` | false | reject | The File List is explicitly headed “2026-09-18 four-patch spec-9 close-out.” The archive was a pre-existing committed file, not a file changed by the close-out implementation. |
| BH-03 — Archived ledger stubs do not link to the archive | low | defer | The primary ledger marks entries `archived: 2026-09-18` but does not tell readers where the removed evidence lives. This predates the implementation and can cause avoidable discovery friction. |
| BH-04 — Archive lacks a title, policy, backlink, and provenance | low | defer | The archive begins at `### DW-1` with no navigational context. This is a real pre-existing documentation gap in commit `5856fab`, not a Story 4.8 patch defect. |
| BH-05 — DW-6 repeats an identical decision | low | defer | The archived entry contains the same decision twice. No active runtime parser consumes the archive, so impact is limited to historical-data quality and the issue predates this implementation. |
| BH-06 — DW-38 repeats an identical decision | low | defer | The archived entry duplicates its decision field; this is pre-existing archive hygiene rather than a defect caused by spec-9. |
| BH-07 — DW-45 repeats an identical decision | low | defer | The archived entry duplicates its decision field; this is pre-existing archive hygiene rather than a defect caused by spec-9. |
| BH-08 — DW-46 repeats an identical decision | low | defer | The archived entry duplicates its decision field; this is pre-existing archive hygiene rather than a defect caused by spec-9. |
| BH-09 — DW-55 repeats an identical decision | low | defer | The archived entry duplicates its decision field; this is pre-existing archive hygiene rather than a defect caused by spec-9. |
| BH-10 — DW-56 records two decision dates without explaining precedence | low | defer | The two historical decisions use different dates and no amendment marker. The ambiguity is real but pre-existing and confined to archive provenance. |
| BH-11 — Cancellation test does not assert exception instance identity | medium | patch | The fact asserts only `CancellationToken`; replacing the thrown exception with a new instance carrying the same token would pass despite the AC requiring the exact exception to propagate. |
| BH-12 — Cancellation test accepts any token on the final index read | medium | patch | Both setup and receipt verification use `Arg.Any<CancellationToken>()`, so caller-token forwarding can regress while the fact stays green. |
| BH-13 — 4608 guard does not pin retry exhaustion | medium | patch | The runbook contains exhaustion guidance, but the architecture fact does not assert it, so deleting that accepted behavior would leave the guard green. |
| BH-14 — 4608 guard does not pin the later recovery check | medium | patch | The runbook tells operators to confirm a later reconciliation reads the candidate, but the guard asserts neither the later attempt nor candidate-read outcome. |
| EC-01 — Final index can receive a non-caller token undetected | medium | patch | Independent tracing confirms the broad NSubstitute matcher leaves caller-token forwarding unverified. |
| EC-02 — Later reconciliation confirmation can disappear undetected | medium | patch | Independent tracing confirms the documentation fact does not pin the recovery-verification action. |
| EC-03 — Retry-exhaustion restart guidance can disappear undetected | medium | patch | Independent tracing confirms the fact asserts `restart` but not the condition that startup retries were exhausted. |

The two pre-existing archive groups are deferred below. The current-change findings share two root causes: the
new cancellation regression under-specifies identity/token forwarding, and the 4608 documentation guard
under-specifies the accepted operator action. Both route to minimal test-only patches.

## Verification

**Commands:**
- `git ls-files -s references/Hexalith.Chatbot references/Hexalith.Conversations references/Hexalith.EventStore` plus per-submodule `git rev-parse HEAD` -- expected: index and checkout equal the three recorded SHAs.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false` -- expected: 0 warnings, 0 errors.
- Direct Release Integration binary filtered to `IndexedPendingDateAwaitSourceTests` -- expected: all pass, 0 skipped.
- Direct Release Architecture binary filtered to `SubscriberDeadLetterOperatorDocumentationTests` -- expected: all pass, 0 skipped.
- Direct Unit, Property, serial non-smoke Integration, full Architecture, and Architecture excluding only the known SDK-pin fact -- expected: all non-blocked deterministic gates pass; report the existing SDK mismatch exactly if unchanged.

**Executed results:**
- `git ls-files -s` and the three per-submodule `git rev-parse HEAD` commands matched exactly: Chatbot
  `1047ef38d3845406227891639aaeb853e5d4f116`, Conversations
  `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, EventStore
  `b5541259058320a0a7f1db19038709cbd02dad85`.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false` passed with
  0 warnings and 0 errors.
- Focused Integration binaries passed: `IndexedPendingDateAwaitSourceTests` **29/29**,
  `DateReminderRecoveryRuntimeTests` **13/13**, `ReminderReconciliationServiceTests` **4/4**, and
  `LinkConversationRuntimeAdapterTests` **136/136**, all with 0 skips.
- `SubscriberDeadLetterOperatorDocumentationTests` passed **3/3** with 0 skips.
- Direct suites passed Unit **568/568**, Property **3/3**, and serial non-smoke Integration **493/493**, all with
  0 skips.
- Full Architecture passed **237/238**; the sole failure remains
  `P0_GlobalJsonPinsSdkTestRunnerAndAspireSdk` (`10.0.400` expected versus `10.0.401` configured). Excluding
  exactly that fact passed **237/237** with 0 skips.
- No Tier-3 smoke lane ran; this patch claims no new live evidence.
