---
title: 'Reconcile v1 catalog count references'
type: 'refactor'
created: '2026-08-28'
status: 'done'
baseline_revision: '64c9cffc6644554162bdddc351772db72a0f551f'
review_loop_iteration: 0
followup_review_recommended: false
context: ['{project-root}/AGENTS.md']
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Maintained Works documentation still describes the frozen v1 polymorphic catalog as 36, and three governance-test names still encode 36 even though their assertions and the authoritative catalog count are 37. These contradictions make the current contract and test surface misleading.

**Approach:** Reconcile the current documentation, the three test names, and every maintained story/test-summary reference to 37 without changing the catalog membership, serialized payloads, or runtime behavior.

## Boundaries & Constraints

**Always:** Keep `WorkItemV1Catalog.Count` at 37 (14 success events, 14 commands, 9 rejection events); rename all three governed methods with the `CatalogStays37` suffix; update their maintained references together; preserve time-stamped historical numeric baselines that do not reference the renamed methods.

**Block If:** The authoritative catalog is no longer 37, a requested reference cannot be classified as current documentation versus historical evidence, or reconciliation would require a payload/catalog/runtime change.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md`, the bundle intent/triage/log files, `WorkItemV1Catalog`, golden payload fixtures, production `src/`, or serialized contract shapes. Do not rewrite historical story counts merely because they were accurate before `WorkItemInitialEffortRejected` raised the catalog to 37.

</intent-contract>

## Code Map

- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs:18` -- read-only authority: `Count = 37`, composed of 14 events, 14 commands, and 9 rejections.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:358` -- three `...CatalogStays36` declarations at the Story 4.2, 4.3, and 4.4 guards; each assertion already expects 37, and the Story 4.3 mirror comment quotes the Story 4.2 method.
- `docs/boundary-decision-record.md:109` -- four maintained Story 4.2-4.5 catalog statements still say 36; the later Story 4.6 section already says 37.
- `docs/lifecycle-transition-matrix.md:198` -- current single-claim-wins note incorrectly says the catalog stays 36.
- `docs/whats-next-projection.md:133` -- current boundary statement incorrectly says the catalog stays 36.
- `_bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md:412` -- maintained fully-qualified reference to the first renamed governance test.
- `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md:112` -- full and abbreviated references to the Story 4.2/4.3 governance methods; numeric historical baselines remain unchanged.
- `_bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md:191` -- abbreviated references to the renamed governance methods; numeric historical baselines remain unchanged.
- `_bmad-output/implementation-artifacts/tests/test-summary.md:91` -- four fully-qualified governance-method references spanning Stories 4.2-4.4; numeric historical test/catalog baselines remain unchanged.
- `_bmad-output/implementation-artifacts/deferred-work.md:335` -- read-only ledger entries DW-36/DW-41; the orchestrator owns resolution state.

## Tasks & Acceptance

**Execution:**
- [x] `docs/boundary-decision-record.md`, `docs/lifecycle-transition-matrix.md`, `docs/whats-next-projection.md` -- replace the six identified maintained catalog-count statements with 37 while preserving their surrounding decisions and golden-corpus claims.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` -- rename the three methods and the internal mirror reference from `CatalogStays36` to `CatalogStays37`; leave test bodies and `ShouldBe(37)` assertions unchanged.
- [x] `_bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md`, `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md`, `_bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md` -- update every full or abbreviated `CatalogStays36` method-name reference to `CatalogStays37`, without changing historical count/result prose.
- [x] Repository-wide verification -- prove the focused ArchitectureTests surface still passes and the protected catalog, payload, source, and ledger paths have no implementation diff.

**Acceptance Criteria:**
- Given the authoritative catalog count is 37, when the three maintained product documents are read, then all six identified current statements report 37 and none reports that the current v1 catalog stays 36.
- Given the architecture governance suite is discovered, when the three Story 4.2-4.4 guards are enumerated, then their names end in `CatalogStays37`, their bodies still assert 37, and the focused test project passes.
- Given the maintained Story 4.2-4.4 artifacts and test summary, when method references are searched, then no `CatalogStays36` reference remains and each renamed guard is referenced as `CatalogStays37` where applicable.
- Given the completed diff, when protected paths are inspected, then the deferred-work ledger, catalog membership/count, golden fixtures, serialized shapes, and production source are unchanged.

## Spec Change Log

## Review Triage Log

### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 0
- defer: 0
- reject: 19: (high 0, medium 5, low 14)
- addressed_findings:
  - none

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release` -- expected: succeeds with zero warnings and zero errors.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests -class Hexalith.Works.ArchitectureTests.FitnessTests.ScaffoldGovernanceTests` -- expected: all focused governance tests pass.
- `rg -n 'CatalogStays36' tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs _bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md _bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md _bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md _bmad-output/implementation-artifacts/tests/test-summary.md` -- expected: no stale method-name matches.
- `rg -n 'catalog stays (\*\*)?36|v1 catalog stays (\*\*)?36' docs/boundary-decision-record.md docs/lifecycle-transition-matrix.md docs/whats-next-projection.md` -- expected: no stale current-catalog matches.
- `git diff --check` plus protected-path diff inspection -- expected: clean formatting and no changes to the ledger, catalog, fixtures, serialized contracts, or `src/`.

**Results:**
- Release build succeeded with 0 warnings and 0 errors.
- Focused `ScaffoldGovernanceTests` run passed 16/16 with no failures or skips.
- Both stale-reference searches returned no matches.
- `git diff --check` passed; the deferred-work ledger, `WorkItemV1Catalog`, schema-evolution fixtures, serialized contracts, and production `src/` have no diff.

## Auto Run Result

Status: done

Summary: Reconciled the six maintained catalog-count statements to 37, renamed the three governance tests to `CatalogStays37`, and updated all maintained Story 4.2-4.4 and test-summary method references. Catalog membership, payload shapes, production source, fixtures, and the deferred-work ledger remain unchanged.

Files changed:
- `docs/boundary-decision-record.md` -- corrected four current catalog-count statements.
- `docs/lifecycle-transition-matrix.md` -- corrected the current single-claim-wins catalog count.
- `docs/whats-next-projection.md` -- corrected the current projection-boundary catalog count.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` -- renamed three governance methods and their mirror comment.
- `_bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md` -- updated the maintained Story 4.2 method reference.
- `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md` -- updated full and abbreviated Story 4.2/4.3 method references.
- `_bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md` -- updated abbreviated governance-method references.
- `_bmad-output/implementation-artifacts/tests/test-summary.md` -- updated four fully qualified governance-method references.
- `_bmad-output/implementation-artifacts/spec-v1-catalog-count-reconciliation.md` -- recorded the plan, evidence, review triage, and result.

Review findings breakdown: 0 patches applied, 0 items deferred, and 19 reviewer findings rejected after deduplication as context-asymmetry noise, pre-existing design commentary, or readings contradicted by the verbatim ledger-bounded intent.

Follow-up review recommendation: false. Patched findings: high 0, medium 0, low 0; score `3 × 0 + 1 × 0 = 0`.

Verification performed:
- Release build succeeded with 0 warnings and 0 errors.
- Focused `ScaffoldGovernanceTests` passed 16/16 with no failures or skips.
- Focused stale-count and stale-method-name searches returned no matches.
- `git diff --check` passed.
- Protected-path inspection confirmed no diff in the ledger, bundle intent, catalog authority, schema-evolution fixtures, serialized contracts, or production `src/`.

Residual risks: Repository-external test filters cannot be enumerated; the repository-wide reference investigation found no maintained filter or configuration consumer outside the updated surfaces. Historical numeric 36 baselines remain intentionally unchanged where they record the state before `WorkItemInitialEffortRejected` raised the catalog to 37.
