---
title: 'Close five deferred architecture guard gaps'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred architecture items leave the dependency guard incomplete or redundant: DW-75 misses projects whose filenames do not start with Hexalith.Works; DW-78 forces AnyCPU despite declared platforms; DW-79 treats restored NuGet build imports as custom restore-freshness inputs; DW-80 misses imported dependencies active only in Debug; DW-85 duplicates projection payload coverage.

**Approach:** Close exactly these five entries through bounded architecture-test infrastructure changes and executing regressions. Discover every repository-owned project, evaluate supported Debug/Release, platform and framework lanes while preserving explicit child globals, exclude resolved package-cache imports from freshness, and retain the stronger combined projection catalog test. Verify, independently review, commit and push Works main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: `c88003a1200e2487bf45b4b685ee58dd29b53f7b`; clean Works main. The user expressly requested five deferred items and the final push. This is a small change confined to existing architecture helpers, tests, ledger and this spec; it introduces no production API, migration or dependency update. Select oneshot.
- Code map: `DependencyDirectionTests.RepositoryWorksProjectFiles` owns project discovery and both repository-wide package sweeps. Discover all `*.csproj` while retaining root-relative references, evidence and build-output exclusions; prove an unfamiliar project participates in governance.
- `MsBuildProjectEvaluation` currently expands frameworks but fixes the default to Release/AnyCPU. Expand declared platforms only when no caller platform is supplied; add a supported-lane helper for Debug/Release callers. Preserve the exact-global-property overload and ProjectReference metadata when following child edges. Test platform-conditioned imports/references and framework combinations.
- `KernelDependencyPolicy.EvaluateProjectFile`, `EvaluateHexalithSourceConsumption` and root closure discovery must use supported lanes. Existing direct XML checks remain conservative. Add imported Debug-only and platform-specific forbidden dependency controls, including a referenced child's selected import in freshness.
- Exclude actual evaluated NuGet package roots from `ImportPaths` using path boundaries; retain custom siblings, nested custom imports, generated-output exclusions and dependency declaration validation. Use fixtures for relocated and relative package roots. No new NuGet package or MSBuild loading approach is needed.
- Remove `WorkItemRollUpPayloadCoverageTests.cs`; its complete assertion set already exists in `ProjectionPayloadCoverageTests.VerifyCatalog`, alongside duplicate, disposition and intentional-no-op guards.
- Verification: build `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj` explicitly in Debug; invoke its executable with xUnit class filters, then run the entire architecture assembly. Preserve exact commands/results and any blocker separately. Use `git -c core.whitespace=cr-at-eol diff --check` and commitlint before commit. Works has no AppHost and this batch changes no runtime behavior.

- Initial `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Debug --no-restore -m:1` exited 1 with CS0234 for Hexalith.Commons in EventStore.Contracts because the existing restore assets were stale. Running the same build without `--no-restore` restored the existing dependency graph and passed with zero warnings/errors. No dependency versions or submodule revisions changed.
- The initial focused run passed 205/206; the existing conditional-owning-project assertion expected only two conservative sentinels, while supported Debug evaluation now also reports the two real evaluated items. It now asserts all four diagnostics. The first full run exposed four explicit-global identity failures caused by adding default Platform=AnyCPU during an exact evaluation. Platform expansion now belongs only to default/supported-lane entry points; the exact-global overload retains caller identity. The full suite then passed 322/322, including the four original identity checks.


## Review Triage Log

- **Medium, patched — platform discovery:** Discovery could fail on an import for an unsupported default platform before selecting declared platforms. A separate permissive discovery collection now contributes only platform names; every selected lane is loaded strictly. The x64-only control succeeds without a default import and fails when its supported import is removed.
- **Medium, patched — framework-specific platforms:** Platforms were not reconsidered after framework selection. The lane queue now rediscovers platform declarations in each framework context. A net10.0/x64-only forbidden import is detected; removing that selected import still fails closed.
- **Medium, patched — exact direction coverage:** The live direction allowlist checked only Release. It now checks supported configurations/platforms/frameworks for Works-owned edges, retaining external edge enforcement in the dependency-mode gate. A Debug-only Works-to-Works edge control demonstrates the allowlist rejection.
- **Medium, patched — explicitly expected projects:** The repository-wide package sweep excluded projects whose separate expectations did not expand platforms. Those expectations now consume supported-lane snapshots. External reference identities are compared as canonical sets while child global properties remain intact.
- **Medium, patched — global packages:** Live repository and dependency-mode guards omitted GlobalPackageReference. They now inspect both package item kinds; the unfamiliar-name live-sweep fixture executes both controls.
- **Medium, patched after executing the previously unverified concern — child dependency contents:** Supported closure discovery supplied freshness inputs but trusted the active root restore graph alone for dependency contents. A Debug-only neutral child declaring Dapr.Client passed before the fix (retained red test: one test, one failure). Governance now also evaluates dependency contents for every source-closure snapshot, so the regression passes while active restored-graph validation remains. This does not claim separately restored NuGet artifacts for every configuration/platform pair.
- **Low, patched — package import negative controls:** Cache fixtures initially proved only freshness exclusion. Each now replaces the cached import with a forbidden Dapr dependency and then an unresolved declaration, proving dependency checks remain active independently of the freshness filter.


## Verification

- Final build: `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Debug --no-restore -m:1` exits 0, zero warnings/errors, after the normal restore repaired the initial stale assets.
- Final full suite: `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/works-five-architecture-final.xml` exits 0: **327/327 passed**, zero errors/failures/skips/unexecuted. The final build includes the completed review fixes and formatting.
- Whitespace verification: `dotnet format whitespace tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --no-restore --verify-no-changes --include` followed by the five edited C# file paths exits 0. The formatter emits a generic workspace-load warning; the actual Debug build is warning-free. `git -c core.whitespace=cr-at-eol diff --check` passes.
- Commit message: `npm run commitlint -- --edit /tmp/works-five-architecture-commit.txt` exits 0 for `fix: close five deferred architecture guard gaps`.
- Independent follow-up rechecked all seven review groups and their executing controls, confirmed them resolved within scope, and found no concrete new regression. No new work is deferred. Exactly DW-75, DW-78, DW-79, DW-80 and DW-85 are closed.
- [Retained evidence](evidence/five-deferred-architecture-guards-2026-10-23/manifest.json) binds final source/assembly hashes, exact validation commands/results, compressed final logs/XML and the red Debug-child control. Earlier failed attempts remain identified separately. All submodule checkouts/gitlinks and production runtime code are unchanged.
