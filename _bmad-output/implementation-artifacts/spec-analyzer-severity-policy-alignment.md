---
title: 'Align analyzer severities with the warnings-as-errors policy'
type: 'chore'
created: '2026-08-27'
status: 'done'
baseline_revision: '996bdbb4cb586c6e6abff8e4b424fd9982c9cb71'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** The root `.editorconfig` still describes CA1062, CA1822, and CA2007 as scaffolding-phase warnings even though `Directory.Build.props` makes every warning a build error. The mismatch obscures the repository's mature analyzer contract and can silently return if the two files drift.

**Approach:** Declare the three analyzers as explicit errors, replace the stale comment with the absolute-policy rationale, and add an architecture fitness assertion that verifies the editor configuration and build policy together.

## Boundaries & Constraints

**Always:** Keep `TreatWarningsAsErrors` enabled; make CA1062, CA1822, and CA2007 explicit errors; test the repository files through the existing architecture-test root locator; keep any warning escape hatch narrow, local, and justified.

**Block If:** Satisfying an existing analyzer diagnostic requires weakening one of the three target severities or adding a repository-wide escape hatch for it.

**Never:** Edit the deferred-work ledger or bundle intent; weaken `TreatWarningsAsErrors`; broadly suppress CA1062, CA1822, or CA2007; remove unrelated, justified suppressions solely to expand this bundle.

</intent-contract>

## Code Map

- `.editorconfig:57-64` -- root C# analyzer policy; the three target rules are warnings under stale scaffolding comments, while unrelated CA1014 remains suppressed.
- `Directory.Build.props:11-16` -- authoritative root build policy; `TreatWarningsAsErrors` is `true`, with package-only `NU5118`/`NU5128` in `NoWarn` and no `WarningsNotAsErrors` entry.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs` -- existing P0 fitness tests parse root build configuration through `RepositoryRoot`; extend this class with the focused cross-file governance assertion.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RepositoryRoot.cs` -- reuse `RepositoryRoot.Locate()` rather than introducing path-discovery logic.
- `src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj:7` -- read-only evidence of a local `CS1591` `NoWarn`; it does not relax any target analyzer and is outside this bundle.
- `.bmad-loop/runs/20260827-130630-f73f/bundles/analyzer-severity-policy-alignment/intent.md` -- read-only source intent and ledger evidence; the orchestrator owns ledger resolution.

## Tasks & Acceptance

**Execution:**
- `.editorconfig` -- replace the scaffolding-era analyzer comment and set CA1062, CA1822, and CA2007 to `error` -- make the declared severity match the absolute warnings-as-errors contract.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs` -- add a P0 assertion that reads both root policy files and requires `TreatWarningsAsErrors=true`, each target analyzer at explicit `error`, and no target ID in root `NoWarn` or `WarningsNotAsErrors` -- prevent silent policy divergence or an equivalent root escape hatch.

**Acceptance Criteria:**
- Given the root build policy treats warnings as errors, when analyzer governance is inspected, then CA1062, CA1822, and CA2007 are each declared with `severity = error` and the stale scaffolding language is absent.
- Given either root policy file is changed to downgrade or exempt a target analyzer, when the focused architecture fitness test runs, then the test fails with the divergent rule or build property observable in its assertion.
- Given existing unrelated warning suppressions, when this bundle is implemented, then they remain unchanged unless validation proves one is broad, unjustified, and necessary to address for the target policy.
- Given the completed change, when repository validation runs, then the architecture test project and the Release solution build succeed without weakening warnings-as-errors.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 1: (high 0, medium 1, low 0)
- defer: 0
- reject: 14: (high 3, medium 6, low 5)
- addressed_findings:
  - `[medium]` `[patch]` Made the analyzer-severity parser require declarations in the exact root `[*.cs]` section and added regression coverage so unrelated or narrow EditorConfig sections cannot satisfy the governance assertion.

## Verification

**Commands:**
- `dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release` -- expected: all architecture fitness tests pass, including the new analyzer-policy assertion.
- `dotnet build Hexalith.Works.slnx --configuration Release` -- expected: the solution builds with zero warnings and zero errors under the aligned analyzer policy.

## Auto Run Result

Status: done

Summary: Aligned CA1062, CA1822, and CA2007 with the repository's absolute warnings-as-errors policy and added a focused cross-file architecture fitness gate.

Files changed:
- `.editorconfig` -- replaced scaffolding-era guidance and made all three analyzer severities explicit errors.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs` -- added root policy alignment assertions plus EditorConfig section-applicability regression coverage.
- `_bmad-output/implementation-artifacts/spec-analyzer-severity-policy-alignment.md` -- recorded the implementation contract, review triage, and run evidence.

Review findings breakdown: 1 medium patch applied; 0 items deferred; 14 findings rejected as broader effective-build policy, hypothetical compatibility concerns disproved by validation, or cosmetic/non-contractual issues.

Follow-up review recommendation: false. Patched findings: high 0, medium 1, low 0; score `3 × 1 + 1 × 0 = 3`.

Verification performed:
- `dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release` -- passed 116 tests, 0 failed, 0 skipped.
- `dotnet build Hexalith.Works.slnx --configuration Release` -- succeeded with 0 warnings and 0 errors.
- `git diff --check` -- passed.

Residual risks: The governance assertion intentionally protects the two root declaration surfaces; evaluated command-line overrides, imported configuration, and narrowly justified local suppressions remain outside this focused gate.
