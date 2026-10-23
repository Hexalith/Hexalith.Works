---
title: 'Close five deferred architecture-lane safeguards'
type: 'chore'
created: '2026-10-23'
status: 'draft'
route: 'dispatch'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five open deferred entries leave the architecture lane coupled to its build machine or unable to detect dependency and documentation drift: DW-63, DW-76, DW-77, DW-81, and DW-82.

**Approach:** Make MSBuild loading portable, trace effective version ownership, evaluate runtime dependency boundaries, and bind maintained source-revision claims to the actual EventStore gitlink. Verify all five and push the completed batch to `main`, as requested.

## Boundaries & Constraints

**Always:** Preserve source Debug and package Release behavior, fail-closed evaluation, warnings-as-errors, and existing approved EventStore version selection. Use centrally pinned, test-only MSBuild dependencies. Keep published-producer identity separate from the current source gitlink and historical proof revisions. Preserve unrelated work.

**Never:** Change production runtime behavior, external library versions, submodule content or gitlinks; rewrite historical evidence; initialize nested submodules; publish or deploy packages.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected behavior | Error handling |
|---|---|---|---|
| Relocated test run | Different installed SDK path from build | Discover compatible installed MSBuild before loading its types | Missing/incompatible installation reports actionable failure |
| Package ownership | Shared version expression consumes Works property or alias | Report effective property provenance | Preserve only the existing approved EventStore pin exception |
| Runtime boundary | Imported Dapr package or misleading project basename | Enforce evaluated identities and canonical paths in both configurations | Fail on unknown or unevaluable evidence |
| Revision drift | Maintained current-source marker differs from gitlink or consumed checkout | Reject stale current-source claim | Preserve historical SHA claims as historical |

</frozen-after-approval>

## Code Map

Baseline: `b1efcbd44823e7344e730c8df28909da4f4828dc`.

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs`: captures compile-time `MSBuildToolsPath`; version ownership records metadata XML only. Reuse snapshot and supported-lane evaluation.
- `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj`: copies SDK MSBuild assemblies and pins EventLog `10.0.0.0`.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`: actual-project version ownership gate and temporary-project fixture patterns.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs`: source-consumption policy and canonical dependency roots.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs`: pure-project checks already evaluate both configurations; host Dapr check still reads raw XML and project classification uses basenames.
- `docs/eventstore-api-surface-constraints.md`: historical `c6173920…` is still called current. Baseline gitlink: `2361e7b6760a7d0065c67661c2e95c2af94bdb4d`; published 3.126.1 producer differs. Re-read the binding before implementation because concurrent dependency updates appeared during planning.

## Tasks & Acceptance

**Execution:**

- [ ] `Directory.Packages.props` and `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj`: centrally pin Locator and compile-only MSBuild packages; remove captured-path metadata, copied MSBuild runtime dependencies and exact EventLog reference (DW-77/82).
- [ ] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildRuntime.cs` (new) and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs`: register an installed SDK using repository SDK policy before JIT/loading MSBuild-dependent code; retain selected SDK import filtering.
- [ ] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildEvaluatedItem.cs`, evaluator, policy and dependency-direction tests above: retain evaluation-time property provenance, including recursive aliases, and enforce ownership with the existing EventStore pin exception (DW-76).
- [ ] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs`: replace remaining raw/package-basename shortcuts with evaluated packages and canonical source paths (DW-81).
- [ ] `docs/eventstore-api-surface-constraints.md`, `docs/boundary-decision-record.md` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs`: add consistent current-source markers, clarify historical wording, and compare markers against root gitlink and consumed checkout (DW-63).
- [ ] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildRuntimeTests.cs` (new) and existing affected test files: add fresh-process SDK relocation, output-inventory, provenance, imported-reference, impersonation and revision-drift controls. Keep one C# type per file.
- [ ] `_bmad-output/implementation-artifacts/deferred-work.md`: close exactly these five entries only after verification; record evidence in this spec, run skill-directed review, validate Conventional Commit, fetch/inspect divergence, then commit and push on `main`.

**Acceptance Criteria:**

- Given unchanged compiled tests and a different compatible SDK location, when a fresh process evaluates an SDK project, then it succeeds without the build-time path or copied MSBuild/EventLog runtime assemblies.
- Given shared literals/properties, Works overrides, recursive aliases and post-item reassignment, when ownership is checked, then only effective unauthorized influences fail and approved pin selection remains valid.
- Given imported/conditional Dapr references or same-basename impostors, when boundary checks run, then violations fail while genuine Debug source and Release package references pass.
- Given matching or independently mutated document markers, gitlink and checkout, when revision guards run, then only the matching binding passes without changing historical evidence.
- Given the completed batch, when architecture suites run in both modes, then all facts pass and all five ledger resolutions cite that evidence before push.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Verification

- Build and execute `tests/Hexalith.Works.ArchitectureTests` in source Debug and package Release individually, using fresh output directories and `HexalithBuildsRoot` set to the root Builds submodule. Use direct xUnit assembly execution with `-class` for focused iteration.
- Run relocation controls in isolated child processes; never remove or rename installed SDKs. Verify absent runtime dependency copies in fresh output and useful refusal without a compatible SDK.
- `git diff --check`; commitlint the exact commit message before commit. Record exact commands, results and any fallback blockers.

Sources: [Microsoft loader requirements](https://learn.microsoft.com/en-us/visualstudio/msbuild/find-and-use-msbuild-versions?view=vs-2022), [Locator package](https://www.nuget.org/packages/Microsoft.Build.Locator/1.11.2). Loader API confirmed with `dnx dotnet-inspect`.
