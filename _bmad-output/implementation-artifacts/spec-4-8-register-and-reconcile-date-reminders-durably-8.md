---
title: 'Close Story 4.8 spec-7 review patches'
type: 'bugfix'
created: '2026-09-17'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'ea0590a1d75787e9440b9afdccdd5656df1a6af4'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-7.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8 has seven accepted review patches: one exact-cancellation path can discard earlier cross-tenant evidence, retry guidance is overstated, and close-out evidence predates three retained submodule advances.

**Approach:** Apply the two recorded decisions and five direct patches, then re-run deterministic gates on current HEAD before returning Story 4.8 to review.

## Boundaries & Constraints

**Always:** Preserve partials, counts, and the earlier cause when a later tenant-index read throws exact caller cancellation. Keep clean cancellation bare. Record full gitlink revisions and actual results; correct ledgers append-only.

**Never:** Change the three in-tenant filters, `DateReminderReconciler`, EventId 4603's template, retry policy, durable contracts, SDK/package pins, or submodule pointers. Do not add EventId 4610 or claim Tier-3 evidence.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Later tenant cancellation | Prior failure; next index read throws exact caller token | Throw typed incomplete evidence; do not read another tenant | Do not count/log cancellation or replace the cause |
| Clean boundary cancellation | No earlier failure exists | Propagate the exact caller cancellation unchanged | No retry classification |
| Recovery warnings | Operators see 4603–4609 | Distinguish startup, shutdown, exhaustion, and non-startup events | Never promise a retry |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs` -- change only the outer exact-token catch and remarks; reuse the loop-head count rule without replacing `lastFailure`.
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs` -- add ordinary failure → next-read cancellation → typed evidence; keep existing boundary facts.
- `docs/operations/subscriber-dead-letter-operator.md` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs` -- fix rendered `Reason`, shutdown caveats, and startup/non-startup scope.
- `tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs` -- pin 4605 retry eligibility and reject `will retry`.
- `references/Hexalith.Chatbot`, `references/Hexalith.Conversations`, `references/Hexalith.EventStore` -- read-only evidence at `3c787993213ccf33f8912e6ad5caac605586fa15`, `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, `629168e3983e5a9cd1639013f39d758fb0068cac`.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- append a correction: the outer path does not require an in-tenant change.
- Story 4.8, spec-7, this spec, `sprint-status.yaml`, and `tests/test-summary.md` -- close patches, disclose gitlink deviation, record gates/File List, then advance.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works.AppHost/Program.cs` -- run/inspect/stop the pre-edit Aspire baseline; record blockers without live credit.
- [x] `IndexedPendingDateAwaitSource.cs` and its test -- preserve prior evidence on the outer exact-cancellation path.
- [x] Operator runbook and its architecture test; `DateReminderRecoveryRuntimeTests.cs` -- correct and pin warning semantics.
- [x] `deferred-work.md` and spec-7 -- append corrections and reconcile evidence without changing frozen intent.
- [x] `Hexalith.Works.slnx` and four direct test binaries -- run focused/full deterministic gates and isolate the SDK-pin blocker.
- [x] Story 4.8, both specs, `sprint-status.yaml`, and `tests/test-summary.md` -- close patches and record executed evidence/File List.

**Acceptance Criteria:**
- Given prior cross-tenant failure evidence, when the next index read throws exact caller cancellation, then typed evidence retains partials/counts/cause and no later tenant is read.
- Given no earlier failure, when boundary cancellation occurs, then the exact cancellation still propagates bare.
- Given the runbook and tests, when retry guidance is inspected, then startup scope, shutdown, exhaustion, and non-startup 4603/4609 origins are explicit and rendered correctly.
- Given current HEAD, when gates and audit artifacts are reconciled, then results and gitlinks are exact, no live credit is added, and Story 4.8 returns to review.

## Implementation Notes

- The outer exact-caller cancellation catch now breaks to the existing typed incomplete-result throw when an
  earlier tenant or candidate failure has already been counted. It leaves the three in-tenant filters unchanged,
  does not count/log cancellation, and preserves the prior partials/counts/cause.
- Added the exact missed-window regression: a successful tenant contributes a partial, an ordinary tenant-index
  failure records the cause, the next tenant-index read throws exact caller cancellation, and the following tenant
  is never read. Existing clean-boundary facts still prove bare exact cancellation.
- Reconciled operator semantics for 4603–4609, including correctly rendered `Reason`, shutdown-qualified later
  attempts for 4604/4606, and the distinction between 4603/4609 non-startup origins and bounded startup-scan
  evidence. The architecture guard reads the post-table guidance and rejects the broken nested-backtick form.
- Pinned 4605 to the shipped `remains eligible for retry under the configured recovery policy` wording and
  explicitly rejected `will retry`; no runtime template, EventId, or retry policy changed.
- Appended the deferred-ledger correction, closed all seven follow-up patches in the parent story and spec-7,
  and returned story/sprint tracking to `review` without modifying either frozen intent block.
- The spec-8 run recorded Chatbot `3c787993213ccf33f8912e6ad5caac605586fa15`, Conversations
  `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and EventStore
  `629168e3983e5a9cd1639013f39d758fb0068cac`. Later adopted superproject commits advanced Chatbot to
  `1047ef38d3845406227891639aaeb853e5d4f116` and EventStore to
  `b5541259058320a0a7f1db19038709cbd02dad85`; Conversations remained unchanged. As corrected by spec-9 on
  2026-09-18, the root index and all three checkouts match those current full SHAs. The prior revisions are
  retained only as transition history; the root index and checkouts agree.
- The required pre-edit Aspire run started its control plane, then `eventstore` finished with exit code 134 and
  an unhealthy canceled `/alive` probe while `works` remained waiting. Aspire stopped cleanly; no live credit is
  claimed.

## Spec Change Log

- 2026-09-17: Implemented all six execution tasks, closed the seven spec-7 follow-up patches, recorded exact
  gitlink and verification evidence, and returned Story 4.8 plus sprint tracking to `review`.
- 2026-09-17: Independent review added the candidate-failure side of the next-index exact-cancellation regression,
  qualified 4605 cancellation guidance, recorded two pre-existing deferrals, and refreshed every verification gate.
- 2026-09-18: Spec-9 closed the four remaining review patches: current adopted gitlinks were reconciled while
  preserving their transition history, clean final-index cancellation gained a direct regression, EventId 4608
  gained pinned host-lifecycle guidance, and the cancellation citation was extended through line 197.

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| BH-01 — EventStore gitlink violates the frozen pointer constraint | superseded | patch | Corrected by spec-9: later superproject commit `28724f2` adopted EventStore `629168e3983e5a9cd1639013f39d758fb0068cac` → `b5541259058320a0a7f1db19038709cbd02dad85`; current index and checkout agree, and the transition remains historical evidence. |
| BH-02 — File List and completion record omit the EventStore change | superseded | patch | Corrected by spec-9: current audit artifacts name the adopted Chatbot, Conversations, and EventStore SHAs and state that spec-9 changed no pointer. |
| BH-03 — Gates did not run against the retained EventStore pointer | superseded | patch | Corrected by spec-9: every deterministic gate was rerun with index and checkout both at the adopted `b5541259058320a0a7f1db19038709cbd02dad85`. |
| BH-04 — The outer catch improperly changes in-tenant exact-cancellation semantics | false | reject | The three in-tenant exact-token filters still propagate unchanged. With prior global failure evidence the cancellation is not a clean scan, so the outer boundary correctly preserves that earlier evidence without counting or logging the cancellation; only clean cancellation is required to remain bare. |
| BH-05 — No test combines prior global failure with later in-tenant exact cancellation | low | reject | The combination is not an acceptance-matrix branch, the unchanged in-tenant filters and changed outer decision are independently exercised, and adding three dependency-specific arrangements for a rare shutdown interleaving would exceed the value of this extra pin. |
| BH-06 — The changed `failedCandidateCount` side lacks a next-index cancellation regression | medium | patch | The new fact proves only `failedTenantCount`; replacing the outer condition with `failedTenantCount > 0` would leave the checked suite green and could discard candidate-failure evidence. Add the parallel candidate-failure → next-index exact-cancellation fact. |
| BH-07 — The 4605 row overstates that exact caller cancellation always avoids 4603 | low | patch | When earlier scan evidence exists, a later exact cancellation can produce the typed incomplete result; if there is no partial operation that propagates cancellation, the service logs 4603. Qualify the sentence while retaining the quiet propagated-cancellation case. |
| BH-08 — No hosted-service end-to-end test covers the new source cancellation path | low | reject | Source tests pin the changed classification and service/reconciler tests separately pin typed-incomplete and exact-cancellation handling. A new hosted composition test for this rare shutdown interleaving is more than a direct correction and is not needed to verify the changed branch. |
| BH-09 — EventId 4603 still promises a retry | medium | defer | `WorksRecoveryLog` still says `will be retried` even on a final attempt or non-repeating call site. This is pre-existing and the approved frozen constraint explicitly forbids changing EventId 4603's template in this build. |
| BH-10 — The 4608 row omits shutdown qualification | false | reject | The paragraph immediately following the table explicitly includes 4608 among startup-scan warnings whose next attempt can be prevented by shutdown, so the documented operator procedure already contains the claimed caveat. |
| EC-01 — Current-tenant partial evidence is lost when an in-tenant exact cancellation unwinds the scan | medium | defer | The cited `PartialTenantScanCanceledException` guard does not exist, but the underlying loss is real because an exact cancellation unwinds `ScanTenantAsync` before its local result is returned. That behavior predates this change and the frozen scope explicitly leaves all three in-tenant filters unchanged. |
| EC-02 — The EventStore index/checkout mismatch changes the retained dependency contract | superseded | patch | Corrected by spec-9: the superproject adopted `b5541259058320a0a7f1db19038709cbd02dad85` and the checkout matches it. |
| VG-01 — Candidate-failure evidence is unverified on next-index cancellation | medium | patch | Pre-verified by the verification-gap layer: existing candidate coverage stops at the loop-head check, so no test enters the changed outer catch with `FailedCandidateCount == 1`. |
| VG-02 — The reviewed diff silently advances EventStore despite the evidence record | superseded | patch | Corrected by spec-9: the later adopted pointer transition is explicit in the evidence record, and no pointer changed in the spec-9 patch. |

## Verification

**Commands:**
- `aspire run --non-interactive -- --EnableKeycloak=false`, `aspire describe`, `aspire stop --non-interactive` -- expected: healthy baseline or exact blocker; no live claim.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false` -- expected: 0 warnings, 0 errors.
- Direct Release binaries for four affected Integration classes and `SubscriberDeadLetterOperatorDocumentationTests` -- expected: all pass, 0 skips.
- Direct Unit, Property, non-smoke Integration, full Architecture, then Architecture excluding `*P0_GlobalJsonPinsSdkTestRunnerAndAspireSdk` -- expected: non-blocked gates pass; isolate the known SDK-pin failure if unchanged.

**Executed results:**
- `aspire run --non-interactive -- --EnableKeycloak=false` started the AppHost, but Dapr Sentry exited 1 with `failed to add target /var/run/dapr/credentials: no space left on device`; placement, Scheduler, EventStore, and Works remained waiting. `aspire stop --non-interactive` succeeded. No live lane ran.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false` passed with 0 warnings and 0 errors.
- Focused Integration binaries passed: `IndexedPendingDateAwaitSourceTests` **28/28**, `DateReminderRecoveryRuntimeTests` **13/13**, `ReminderReconciliationServiceTests` **4/4**, and `LinkConversationRuntimeAdapterTests` **136/136**, all with 0 skips.
- `SubscriberDeadLetterOperatorDocumentationTests` passed **3/3** with 0 skips.
- Direct suites passed Unit **568/568**, Property **3/3**, and standalone serial non-smoke Integration **492/492**, all with 0 skips. An initial concurrent launch produced two transient catalog-registration failures; the isolated class immediately passed **2/2** before the full serial pass.
- Full Architecture passed **237/238**; the sole failure remains `P0_GlobalJsonPinsSdkTestRunnerAndAspireSdk` (`10.0.400` expected versus `10.0.401` configured). Excluding exactly that fact passed **237/237** with 0 skips.

### Review Findings

_Scope: `e5c173e...HEAD` (HEAD `28724f2`). 15 raw findings triaged to 1 decision, 3 patch, 0 defer, 7 rejected (6 appendix bullets; the two Aspire-result findings share one line); the gitlink decision resolved 2026-09-17 to patch. Spec-9 closed all four patches on 2026-09-18._

- [x] [Review][Patch] **MEDIUM** — recorded gitlinks do not match HEAD, and the reviewed range moves submodule pointers against spec-8's frozen Never list — resolved 2026-09-18: current root index and checkouts match Chatbot `1047ef38d3845406227891639aaeb853e5d4f116`, Conversations `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and EventStore `b5541259058320a0a7f1db19038709cbd02dad85`; artifacts preserve the earlier transitions and no pointer changed in spec-9.

- [x] [Review][Patch] Clean next-index cancellation at the edited outer catch is untested: dropping the `throw` arm returns a successful scan [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:97] — resolved 2026-09-18 by the exact two-tenant final-index regression; focused class passed 29/29.
- [x] [Review][Patch] Operator 4608 still tells operators to confirm reconciliation succeeds with no host-lifecycle caveat, while this bundle added that caveat to 4604/4606 [docs/operations/subscriber-dead-letter-operator.md:135] — resolved 2026-09-18 by the host-running, retry-budget, shutdown, exhaustion, and restart wording plus architecture assertions.
- [x] [Review][Patch] The in-tenant cancellation ledger citation ends at `:145-191` and misses the stream-read exact-token filter now at `:192-197` [_bmad-output/implementation-artifacts/deferred-work.md:967] — resolved 2026-09-18 by extending the citation to `:145-197` without changing the deferred decision.

**Rejected:**
- `false` — the outer exact-token catch converting later-tenant parking/stream rethrows after prior failure "changes in-tenant semantics": the three in-tenant filters still `throw`; the outer catch is the specified `ScanTenantAsync` boundary.
- `false` — spec-8 left the DateReminderReconciler remarks deferral without a ledger item: it is already recorded at `deferred-work.md:968`.
- `low`, not worth fixing — no hosted-service fact covers 4605's new typed-incomplete/4603 sentence: piecewise source and reconciler tests already pin the two arms, and a composition test is more than a direct correction.
- Fix edits the spec under review — spec-8 frontmatter still has `review_loop_iteration: 0` and `status: 'done'` while the file holds a triage log and a second Spec Change Log line.
- Fix edits the spec under review — spec-8 Implementation Notes record a pre-edit Aspire EventStore exit 134, while Executed results for the same command record Dapr Sentry `no space left on device`.
- Fix edits the spec under review — spec-8 Code Map still describes only ordinary tenant-index failure → next-read cancellation and never names the candidate-failure arrangement.
