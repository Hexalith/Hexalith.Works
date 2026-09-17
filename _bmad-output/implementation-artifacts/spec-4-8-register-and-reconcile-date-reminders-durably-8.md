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
- Retained reviewed-range gitlinks: Chatbot `3c787993213ccf33f8912e6ad5caac605586fa15`, Conversations
  `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and EventStore
  `629168e3983e5a9cd1639013f39d758fb0068cac`. A pre-existing EventStore checkout-only drift to
  `b5541259058320a0a7f1db19038709cbd02dad85` changes only that repository's planning artifacts/status; it was
  neither changed nor staged here, but is disclosed because the deterministic commands used that checkout.
- The required pre-edit Aspire run started its control plane, then `eventstore` finished with exit code 134 and
  an unhealthy canceled `/alive` probe while `works` remained waiting. Aspire stopped cleanly; no live credit is
  claimed.

## Spec Change Log

- 2026-09-17: Implemented all six execution tasks, closed the seven spec-7 follow-up patches, recorded exact
  gitlink and verification evidence, and returned Story 4.8 plus sprint tracking to `review`.
- 2026-09-17: Independent review added the candidate-failure side of the next-index exact-cancellation regression,
  qualified 4605 cancellation guidance, recorded two pre-existing deferrals, and refreshed every verification gate.

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| BH-01 — EventStore gitlink violates the frozen pointer constraint | false | reject | The root index still records `629168e3983e5a9cd1639013f39d758fb0068cac` and has no staged gitlink change. The diff hunk reflects an unrelated shared-worktree checkout at `b5541259058320a0a7f1db19038709cbd02dad85`, which this build did not create or adopt. |
| BH-02 — File List and completion record omit the EventStore change | false | reject | The checkout-only drift is not an implementation file, and both this spec and `tests/test-summary.md` explicitly disclose its actual revision, unstaged status, and separation from the retained root gitlink. |
| BH-03 — Gates did not run against the retained EventStore pointer | false | reject | The only files between the retained and checked-out EventStore revisions are that submodule's spec and sprint-status artifacts; `git diff --quiet` confirms no source, test, project, package, or SDK build input differs. The exact checkout used by the gates is disclosed rather than misrepresented. |
| BH-04 — The outer catch improperly changes in-tenant exact-cancellation semantics | false | reject | The three in-tenant exact-token filters still propagate unchanged. With prior global failure evidence the cancellation is not a clean scan, so the outer boundary correctly preserves that earlier evidence without counting or logging the cancellation; only clean cancellation is required to remain bare. |
| BH-05 — No test combines prior global failure with later in-tenant exact cancellation | low | reject | The combination is not an acceptance-matrix branch, the unchanged in-tenant filters and changed outer decision are independently exercised, and adding three dependency-specific arrangements for a rare shutdown interleaving would exceed the value of this extra pin. |
| BH-06 — The changed `failedCandidateCount` side lacks a next-index cancellation regression | medium | patch | The new fact proves only `failedTenantCount`; replacing the outer condition with `failedTenantCount > 0` would leave the checked suite green and could discard candidate-failure evidence. Add the parallel candidate-failure → next-index exact-cancellation fact. |
| BH-07 — The 4605 row overstates that exact caller cancellation always avoids 4603 | low | patch | When earlier scan evidence exists, a later exact cancellation can produce the typed incomplete result; if there is no partial operation that propagates cancellation, the service logs 4603. Qualify the sentence while retaining the quiet propagated-cancellation case. |
| BH-08 — No hosted-service end-to-end test covers the new source cancellation path | low | reject | Source tests pin the changed classification and service/reconciler tests separately pin typed-incomplete and exact-cancellation handling. A new hosted composition test for this rare shutdown interleaving is more than a direct correction and is not needed to verify the changed branch. |
| BH-09 — EventId 4603 still promises a retry | medium | defer | `WorksRecoveryLog` still says `will be retried` even on a final attempt or non-repeating call site. This is pre-existing and the approved frozen constraint explicitly forbids changing EventId 4603's template in this build. |
| BH-10 — The 4608 row omits shutdown qualification | false | reject | The paragraph immediately following the table explicitly includes 4608 among startup-scan warnings whose next attempt can be prevented by shutdown, so the documented operator procedure already contains the claimed caveat. |
| EC-01 — Current-tenant partial evidence is lost when an in-tenant exact cancellation unwinds the scan | medium | defer | The cited `PartialTenantScanCanceledException` guard does not exist, but the underlying loss is real because an exact cancellation unwinds `ScanTenantAsync` before its local result is returned. That behavior predates this change and the frozen scope explicitly leaves all three in-tenant filters unchanged. |
| EC-02 — The EventStore checkout drift changes the retained dependency contract | false | reject | `git ls-files -s` proves the retained superproject contract remains `629168e3983e5a9cd1639013f39d758fb0068cac`; the unstaged checkout-only drift is external state, not an adopted gitlink. |
| VG-01 — Candidate-failure evidence is unverified on next-index cancellation | medium | patch | Pre-verified by the verification-gap layer: existing candidate coverage stops at the loop-head check, so no test enters the changed outer catch with `FailedCandidateCount == 1`. |
| VG-02 — The reviewed diff silently advances EventStore despite the evidence record | false | reject | The root index and staging area do not advance EventStore, while the evidence record expressly identifies the actual checkout used. A raw worktree diff showing external submodule state does not make that state part of this implementation. |

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
