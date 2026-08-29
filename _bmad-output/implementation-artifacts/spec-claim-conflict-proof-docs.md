---
title: 'Correct claim-conflict proof documentation'
type: 'bugfix'
created: '2026-08-29'
status: 'done'
baseline_revision: '92eb340887560fc2ca5d8e5a8cb168b87339f7c0'
baseline_commit: '92eb340887560fc2ca5d8e5a8cb168b87339f7c0'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Two maintained claim-concurrency explanations still assign live ETag conflict retry and exhaustion coverage to Story 4.5's Aspire lane, although that lane never issued competing claims. Readers are sent to nonexistent evidence and cannot distinguish the in-process actor characterization from unproven provider behavior.

**Approach:** Replace both stale Story 4.5/Aspire claims with the exact `WorkItemClaimPersistenceConflictTests` retry and exhaustion method names, describe what those in-process tests prove, and state explicitly that live provider-ETag behavior remains unproven.

## Boundaries & Constraints

**Always:** Preserve the pure Tier-1 claim test's scope; name both integration-test methods exactly; distinguish deterministic in-process `AggregateActor`/`EventPersister` characterization from Aspire, Dapr sidecar, network, and live provider-ETag coverage.

**Block If:** The named tests no longer exist or no longer exercise retry success and retry exhaustion through the real in-process EventStore actor persistence pipeline.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md`; change production behavior, test behavior, catalog/golden payloads, historical Story 4.5 results, or files under `references/`; claim that the conflict injector proves a live state-store provider's ETag behavior.

</intent-contract>

## Code Map

- `tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs:30` -- maintained class XML documentation containing the stale Story 4.5/Aspire assertion; retain its Tier-1 domain-outcome boundary while linking both exact integration methods.
- `docs/lifecycle-transition-matrix.md:184` -- maintained single-claim-wins explanation containing the second stale Aspire assertion; preserve the lifecycle and catalog semantics while replacing only the proof scope.
- `tests/Hexalith.Works.IntegrationTests/WorkItemClaimPersistenceConflictTests.cs:39` -- read-only executable evidence added by commit `8feae806b8d79f2b329ca375c9bade0a0a7cecfa`; the retry-success and retry-exhaustion facts are the canonical pointers.
- `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md:494` and `_bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md:269` -- read-only reconciled wording examples: Story 4.5 submitted one command at a time; the actor tests are in-process and do not prove provider ETags.
- `_bmad-output/implementation-artifacts/tests/test-summary.md:2449` -- read-only detailed characterization and focused verification command; reuse its precise scope rather than inventing new coverage claims.
- `_bmad-output/implementation-artifacts/spec-envelope-canonical-sequencing.md:77` -- read-only origin evidence for DW-40; do not rewrite the deferred entry or the separate deferred-work ledger.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs` -- replace the stale live/Aspire paragraph with exact fully qualified retry-success and retry-exhaustion test pointers, preserving the Tier-1/no-network boundary and explicitly recording that live provider-ETag behavior is unproven.
- [x] `docs/lifecycle-transition-matrix.md` -- replace the Story 4.5/Aspire assertion with the same two exact executable pointers and proof boundary, without changing lifecycle cells, rejection semantics, or catalog statements.

**Acceptance Criteria:**
- Given the maintained claim-concurrency documentation, when claim persistence-conflict proof is followed, then both surfaces name `WorkItemClaimPersistenceConflictTests.RetryingClaimAfterPersistenceConflictCommitsWinnerAndPublishesLoserRejection` and `WorkItemClaimPersistenceConflictTests.ExhaustingClaimPersistenceConflictRetryReturnsConcurrencyConflictWithoutLoserEffects`.
- Given a reader evaluates the proof boundary, when either maintained explanation is read, then it says the methods characterize the real EventStore actor/persister in-process and that live provider-ETag behavior remains unproven.
- Given the completed diff, when protected paths and behavior are inspected, then only the two documentation surfaces plus this workflow spec changed, and the deferred-work ledger, production code, tests, historical story results, and `references/` remain untouched.

## Spec Change Log

## Review Triage Log

### 2026-08-29 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 1: (high 0, medium 1, low 0)
- defer: 0
- reject: 14: (high 0, medium 2, low 12)
- addressed_findings:
  - `[medium]` `[patch]` The lifecycle text still grouped retry exhaustion with dead-letter behavior and neither maintained surface stated the two cited tests' distinct outcomes; corrected both surfaces to record retry-success loser rejection persistence/publication and exhaustion returning `ConcurrencyConflict` with no loser append, publication, or dead-letter effect.

## Verification

**Commands:**
- `rg -n -U -P 'Story 4\\.5(?s:.{0,320})(ETag|conflict|retry)' tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs docs/lifecycle-transition-matrix.md` -- expected: no stale Story 4.5 proof attribution.
- `rg -n 'RetryingClaimAfterPersistenceConflictCommitsWinnerAndPublishesLoserRejection|ExhaustingClaimPersistenceConflictRetryReturnsConcurrencyConflictWithoutLoserEffects|live provider-ETag behavior remains unproven' tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs docs/lifecycle-transition-matrix.md` -- expected: both methods and the unproven-live-provider boundary appear in both surfaces.
- `dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj --configuration Release --no-restore -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: 0 warnings and 0 errors, proving the edited XML documentation compiles.
- `git diff --check && git diff --name-only` -- expected: no whitespace errors and no protected-path changes.

**Results (2026-08-29):**
- Both documentation scans passed: no stale Story 4.5 proof attribution remains, and both exact method pointers plus the live-provider boundary appear in both maintained surfaces.
- The prescribed `dotnet build` was blocked before MSBuild because `global.json` requires SDK `10.0.301` and the environment provides only `10.0.400`.
- Direct invocation of the installed SDK's MSBuild compiled both the unit-test and integration-test projects in Release with 0 warnings and 0 errors; the focused `WorkItemClaimPersistenceConflictTests` lane passed 2/2.
- `git diff --check` passed; the working-tree scope is limited to the two maintained documentation surfaces and this workflow spec.

## Auto Run Result

Summary: Replaced the two remaining false Story 4.5/Aspire claim-conflict proof attributions with exact in-process actor-test pointers, documented retry-success and exhaustion outcomes, and stated that live provider-ETag behavior remains unproven.

Files changed:
- `../../tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs` -- corrected the class XML proof pointers, outcome descriptions, and coverage boundary.
- `../../docs/lifecycle-transition-matrix.md` -- corrected the maintained single-claim-wins proof attribution and retry-exhaustion semantics.
- `spec-claim-conflict-proof-docs.md` -- recorded the implementation contract, verification, review triage, and completion result.

Review findings breakdown: 1 medium patch applied; 0 items deferred; 14 review suggestions rejected as non-defects, already-covered evidence, or scope-neutral improvements.

Follow-up review recommendation: false. Patched findings were high 0, medium 1, low 0; score = `3 × 1 + 1 × 0 = 3`, below the threshold of 5.

Verification performed:
- Stale-attribution and exact-pointer scans passed for both maintained surfaces.
- The prescribed `dotnet build` entry point could not start because SDK `10.0.301` is unavailable; direct MSBuild from installed SDK `10.0.400` compiled the Release unit-test project with 0 warnings and 0 errors.
- `WorkItemClaimPersistenceConflictTests` passed 2/2 with 0 failures and 0 skips.
- `git diff --check` passed, and working-tree inspection confirmed no deferred-work ledger, production, historical-story, golden-payload, or `references/` change.

Residual risks: Live provider-ETag behavior remains intentionally unproven. The repository-pinned SDK entry point was environment-blocked, although the installed-SDK fallback compiled cleanly.
