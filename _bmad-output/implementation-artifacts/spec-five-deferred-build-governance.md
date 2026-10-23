---
title: 'Close five deferred architecture-lane safeguards'
type: 'chore'
created: '2026-10-23'
status: 'done'
baseline_commit: '8c1d94ae833ffd395f4656d9aabcb0b451cc2b98'
route: 'dispatch'
review_loop_iteration: 1
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

- [x] `Directory.Packages.props` and `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj`: centrally pin Locator and compile-only MSBuild packages; remove captured-path metadata, copied MSBuild runtime dependencies and exact EventLog reference (DW-77/82).
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildRuntime.cs` (new) and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs`: register an installed SDK using repository SDK policy before JIT/loading MSBuild-dependent code; retain selected SDK import filtering.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildEvaluatedItem.cs`, evaluator, policy and dependency-direction tests above: retain evaluation-time property provenance, including recursive aliases, and enforce ownership with the existing EventStore pin exception (DW-76).
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs`: replace remaining raw/package-basename shortcuts with evaluated packages and canonical source paths (DW-81).
- [x] `docs/eventstore-api-surface-constraints.md`, `docs/boundary-decision-record.md` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs`: add consistent current-source markers, clarify historical wording, and compare markers against root gitlink and consumed checkout (DW-63).
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildRuntimeTests.cs` (new) and existing affected test files: add fresh-process SDK relocation, output-inventory, provenance, imported-reference, impersonation and revision-drift controls. Keep one C# type per file.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs`, `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectSnapshot.cs` and runtime guard: expose independent supported configuration/platform/framework evidence for universal checks; keep the existing union API for dependency-discovery callers. Require exactly one host edge and independently trusted source-root identities in every lane. Include evaluated assembly References and reject project-file links masquerading as approved files. Test missing and different valid edges across platforms/frameworks plus an inspected-project root redirect.
- [x] Evaluator and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`: include effective assignment and enclosing conditions in version influence evidence; add local selectors for assignment and PropertyGroup conditions. Exercise the approved pin exception with wrong property names and arbitrary imported defining files while the exception is enabled.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildRuntime.cs` and its tests: verify Locator's selected instance against actual native SDK policy resolution; refuse a native-selected SDK that Locator filters instead of choosing another installed SDK. Add a deterministic filtered-native-selection control. Extend relocated evaluation to an actual governed Works project. XML-escape all newly interpolated fixture paths.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs`: inspect the effective source dependency root, including explicit overrides, against the owning gitlink. Validate gitlink mode/type/hash before indexing; cover missing, malformed, wrong-type and actual consumed-root drift with actionable failures.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md`: close exactly these five entries only after verification; record evidence in this spec, run skill-directed review, validate Conventional Commit, fetch/inspect divergence, then commit and push on `main`.

**Acceptance Criteria:**

- Given unchanged compiled tests and a different compatible SDK location, when a fresh process evaluates an SDK project, then it succeeds without the build-time path or copied MSBuild/EventLog runtime assemblies.
- Given shared literals/properties, Works overrides, recursive aliases and post-item reassignment, when ownership is checked, then only effective unauthorized influences fail and approved pin selection remains valid.
- Given imported/conditional Dapr references or same-basename impostors, when boundary checks run, then violations fail while genuine Debug source and Release package references pass.
- Given matching or independently mutated document markers, gitlink and checkout, when revision guards run, then only the matching binding passes without changing historical evidence.
- Given the completed batch, when architecture suites run in both modes, then all facts pass and all five ledger resolutions cite that evidence before push.

## Implementation Notes

- Review iteration 1 requires re-derivation from the amended tasks. Initial 356/356 results below qualify the first implementation only; rerun corrected suites before resolving the five entries. Workflow coordinator retains review and Git closeout.

- 2026-10-23: User approved and continued. The workspace is clean after concurrent dependency updates were committed. Implementation covers code and verification; the workflow coordinator performs the subsequent review and authorized Git closeout.

- 2026-10-23 implementation: central test-only Locator `1.11.2` and compile-only MSBuild `18.0.2` replace captured SDK paths, copied SDK assemblies and the EventLog literal. Removed the unused architecture-only NSubstitute reference because Castle.Core otherwise copied EventLog. The bootstrap uses native repository `global.json` resolution before entering a no-inline evaluator. Package versions retain ordered property provenance, recursive aliases and global origins; unsupported expressions fail ownership closed. The existing Works EventStore `3.126.1` selection is the only local version-property exception. Runtime gates use evaluated package identities and approved canonical source paths in both configurations. Both maintained source markers bind gitlink/checkout `6a0b6441f6d78a3007678a1d0ec336e9cb8ff961`; published producer and historical SHAs remain separate.

## Spec Change Log

- Review iteration 1: merged snapshots hid unsupported host lanes (B2/E2); source-root allowances trusted project-controlled roots (B3), while revision binding inspected a different root than compilation (B5); version provenance omitted assignment conditions (B4/E1); Locator enumeration could fall back when its native policy selection was filtered (B7). Amended the non-frozen tasks to require separate lane evidence, independent root authority and actual consumed-root binding, conditional influence and explicit native policy verification. These avoid green checks on missing runtime dependencies, unauthorized versions and the wrong SDK/source checkout. All remain within approved intent.
- KEEP: central test-only package references and no copied MSBuild/EventLog dependencies; no-inline bootstrap; exact current/published/historical revision distinction; existing union-based discovery behavior and fail-closed diagnostics; recursive/captured-alias provenance; fresh-process relocation/refusal controls; unchanged production runtime and submodules. Prior implementation is retained in `/tmp/works-five-build-diff-gzyzz7q3.patch` for these preservation constraints; re-derive the amended behavior rather than retaining its known-bad branches. Retain initial evidence as historical, then replace final qualification with fresh corrected results.
- Carry direct corrections into re-derivation: assembly Reference checks (B1), linked project-file impersonation (B6), representative relocated project evaluation (B9), gitlink shape diagnostics (B10), escaped temporary paths (E3), and pin-origin/name mutation coverage (V1). These add no new user-facing behavior or production surface.

## Review Triage Log

| ID | Verdict | Route | Verified evidence |
|---|---|---|---|
| B1 | medium | patch | RuntimeBoundaryViolations inspects ProjectReference and package items but omits evaluated Reference items; the compiled host fixture with Dapr.Client assembly Reference passes. Include the demonstrated assembly edge. |
| B2 | medium | bad_spec | TryEvaluateSupportedLanes unions platform/framework items into one snapshot per configuration; a DomainService edge on AnyCPU masks an absent x64 edge. Each supported lane needs its own universal runtime check. |
| B3 | medium | bad_spec | The allowance is constructed from the inspected snapshot's mutable HexalithEventStoreRoot; a project-local impostor layout authorizes itself. Compare against independent repository dependency authority. |
| B4 | medium | bad_spec | PropertyProvenance reads UnevaluatedValue only. A Works-local selector changes an effective shared property through Condition while ownership reports no violation. Trace effective selecting conditions. |
| B5 | medium | bad_spec | The revision fact calls RepositoryRoot.DependencyRoot, while Directory.Build.props honors a nonempty HexalithEventStoreRoot override. The guard can inspect another checkout than the source dependency actually selected. |
| B6 | medium | patch | CanonicalExistingFile applies Path.GetFullPath without resolving or rejecting a linked project file; a file link preserves the allowed spelling while compiling its target. Reject the demonstrated project-file link impersonation. |
| B7 | medium | bad_spec | Inspected Locator 1.11.2 DotNetSdkLocationHelper IL: GetInstances yields native selection only if GetInstance accepts it, then enumerates other SDKs. FirstOrDefault can therefore violate native policy when the selected SDK is filtered. |
| B8 | low | reject | The test verifies relocation of unchanged binaries under the same repository policy, as the approved matrix requires; no SDK-patch failure was demonstrated. Portable runtime loading and absence of copied/pinned EventLog are covered. Adding a machine-dependent alternate-SDK matrix is beyond a direct correction without demonstrated everyday harm. |
| B9 | medium | patch | The relocated child exercises an SDK project with no custom dependencies. Extending that existing child to evaluate a governed Works project directly verifies the custom dependency/import path claimed by this change. |
| B10 | low | patch | GitValue(...ls-tree...).Split()[2] throws an unhelpful indexing exception when the entry is absent and accepts an unexpected mode/type before hash comparison. Validate the captured entry before extracting its hash. |
| E1 | medium | bad_spec | Same independently reproduced conditional-selector gap as B4; grouped under condition provenance after individual adjudication. |
| E2 | medium | bad_spec | Same independently reproduced per-platform union gap as B2; grouped under lane evidence after individual adjudication. |
| E3 | medium | patch | New fixture XML embeds raw temporary paths in attributes and property values. A temp root containing ampersand makes the control fail parsing; escape all new interpolated paths. |
| V1 | medium | patch | Pre-verified gap: current pin tests vary package/value but always use the approved property and defining file. Removing the origin restriction would leave them green. Add wrong-origin and wrong-name cases with approvedRepositoryRoot enabled. |



### Review pass 2

Each correction below uses the existing private evaluation/validation seam, adds no public surface and covers the specifically demonstrated missing state. The required behavior is already settled by the approved matrix and iteration-1 tasks; these route as direct patches.

| ID | Verdict | Route | Evidence |
|---|---|---|---|
| B2-1 | medium | patch | Version provenance currently reads only effective metadata value text. A local selector choosing a PackageVersion/ItemGroup branch with a literal version yields no influences; include selecting conditions, preserving existing approved mode selection. |
| B2-2 | medium | patch | Effective Version metadata elements also carry selecting conditions; literal values bypass current expression-only provenance. Include the effective metadata ancestor conditions and a separate negative control. |
| B2-3 | medium | patch | Otherwise has no Condition of its own, so the current ancestor walk misses preceding When conditions selecting that branch. The reviewer reproduced an unauthorized shared value; fold preceding branch selectors into the existing influence collection. |
| B2-4 | medium | patch | Active conditional import ancestry is not an XML parent of the imported property. A conditional shared overwrite can look entirely shared-owned. Include the active importing conditions in the same provenance evidence with a control. |
| B2-5 | medium | patch | Reference.Identity alone misses an alias whose HintPath points at a Dapr assembly; the isolated fixture actually compiled. Capture evaluated HintPath and inspect actual assembly identity where present. |
| B2-6 | medium | patch | The host edge counter ignores existing ReferenceOutputAssembly and OutputItemType evidence. An analyzer-only or non-runtime edge passes; count only usable runtime edges and test both refusals. |
| B2-7 | medium | patch | --absolute-git-dir points at worktree admin storage, while objects live in the common repository. The new temporary-checkout control must obtain the actual object path from Git. |
| B2-8 | medium | patch | The file-link fixture requires Windows link privileges, unlike the relocation fixture. Use a privilege-free Windows junction control with equivalent boundary rejection; do not introduce qualifying skips under failSkips. |
| B2-9 | medium | patch | Generic project validation currently rejects all linked ancestors to filesystem root. This blocks valid workspace/temp aliases (including macOS /var). Confine impersonation rejection to governed file/descendant paths inside trusted roots and add an alias positive control. |
| B2-10 | medium | patch | Existing characterization facts still inspect DependencyRoot, while binding now inspects evaluated roots. Use the same consumed root evidence for characterization so the new binding cannot certify a different source than those assertions inspect. |
| E2-1 | medium | patch | Independently reproduced the PackageVersion and metadata-condition bypasses; same root cause as B2-1/B2-2 after individual triage. |
| E2-2 | medium | patch | Independently reproduced a successfully compiled Dapr assembly hidden behind Innocent + HintPath; same root cause as B2-5. |
| E2-3 | medium | patch | Independently identified the unconditional Windows file-symlink privilege failure; same root cause as B2-8. The proposed skip would conflict with failSkips, so use a privilege-free control instead. |
| V2-1 | medium | patch | Pre-verified gap: changing Register to select instances[0] leaves the direct selection helper and current one-SDK/missing-native child tests green. Add a controlled mismatch through Register in a fresh process. |
| V2-2 | medium | patch | Pre-verified gap: current imported-assembly controls only use Dapr; removing host EventStore assembly rejection leaves them green. Add a real host EventStore assembly negative case with its legitimate DomainService edge. |

## Verification

- Build and execute `tests/Hexalith.Works.ArchitectureTests` in source Debug and package Release individually, using fresh output directories and `HexalithBuildsRoot` set to the root Builds submodule. Use direct xUnit assembly execution with `-class` for focused iteration.
- Run relocation controls in isolated child processes; never remove or rename installed SDKs. Verify absent runtime dependency copies in fresh output and useful refusal without a compatible SDK.
- `git diff --check`; commitlint the exact commit message before commit. Record exact commands, results and any fallback blockers.

### Completed implementation verification — 2026-10-23

All commands ran from the Works root with .NET SDK `10.0.402`, without changing any submodule content/gitlink, production dependency selection or runtime code.

```bash
export HexalithBuildsRoot="$PWD/references/Hexalith.Builds"
dotnet restore tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -p:Configuration=Release --force --force-evaluate
dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Debug --artifacts-path .artifacts/five-build-governance/debug-verified -m:1
dotnet .artifacts/five-build-governance/debug-verified/bin/Hexalith.Works.ArchitectureTests/debug/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/works-five-debug.xml
dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release --artifacts-path .artifacts/five-build-governance/release-verified -m:1
dotnet .artifacts/five-build-governance/release-verified/bin/Hexalith.Works.ArchitectureTests/release/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/works-five-release.xml
git -c core.whitespace=cr-at-eol diff --check
```

- Debug source build: **0 warnings, 0 errors**; architecture suite: **356 passed, 0 failed, 0 skipped**.
- Release package build: **0 warnings, 0 errors**; architecture suite: **356 passed, 0 failed, 0 skipped**.
- Both complete suites include fresh-process relocation and missing-SDK refusal, output inventory without copied MSBuild/EventLog dependencies or captured path metadata, shared/literal/local/alias/global/post-item version controls, imported conditional Dapr references, canonical-path impersonation, and independent document/gitlink/checkout drift controls.
- Fresh outputs were created under the two lane-specific artifact roots. Linux relocation aliases the installed SDK into a separate .NET installation root and starts its copied host without removing or renaming any installed SDK. Windows uses a copied installation to avoid requiring symlink privileges; Windows execution was not performed here.
- Initial complete runs rejected unchanged normal kernel assets as older than the edited central package catalog. `dotnet restore ... --force --force-evaluate` regenerated those artifacts; final complete runs pass the unchanged freshness gate. Initial test-development/analyzer failures were corrected before the final runs.
- Plain `git diff --check` treats repository-required CRLF as trailing whitespace in this checkout. The explicit `core.whitespace=cr-at-eol` invocation passes with exit 0, preserves the established file endings and makes no Git configuration change.
- Raw final build/test logs, xUnit XML and forced-restore output are retained as gzip files in [verification evidence](evidence/five-deferred-build-governance-2026-10-23/).
- Exactly DW-63, DW-76, DW-77, DW-81 and DW-82 are resolved by this verified implementation. Skill-directed review and authorized Git closeout remain with the workflow coordinator.

Sources: [Microsoft loader requirements](https://learn.microsoft.com/en-us/visualstudio/msbuild/find-and-use-msbuild-versions?view=vs-2022), [Locator package](https://www.nuget.org/packages/Microsoft.Build.Locator/1.11.2). Loader API confirmed with `dnx dotnet-inspect`.


### Corrected implementation verification — review iteration 1 — 2026-10-23

This qualification supersedes the initial 356-test implementation results above. Those earlier logs and XML remain unchanged as historical evidence. The re-derived implementation retains the approved package/loading choices and adds independent supported-lane evidence, independently evaluated root authority, conditional assignment provenance, explicit native SDK/Locator agreement and actual consumed-root revision binding.

```bash
export HexalithBuildsRoot="$PWD/references/Hexalith.Builds"
dotnet restore tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -p:Configuration=Release --force --force-evaluate
dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Debug --artifacts-path .artifacts/five-build-governance/corrected-debug-verified -m:1
dotnet .artifacts/five-build-governance/corrected-debug-verified/bin/Hexalith.Works.ArchitectureTests/debug/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/works-five-rederived-debug.xml
dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release --artifacts-path .artifacts/five-build-governance/corrected-release-verified -m:1
dotnet .artifacts/five-build-governance/corrected-release-verified/bin/Hexalith.Works.ArchitectureTests/release/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/works-five-rederived-release.xml
git -c core.whitespace=cr-at-eol diff --check
```

- Source Debug: **0 warnings, 0 errors; 378 passed, 0 failed, 0 skipped**.
- Package Release: **0 warnings, 0 errors; 378 passed, 0 failed, 0 skipped**.
- Focused direct assembly execution of `DependencyDirectionTests`, `RuntimeAdapterGovernanceTests`, `MsBuildRuntimeTests` and `EventStoreApiSurfaceCharacterizationTests`: **98 passed, 0 failed, 0 skipped**. The full suites include those same controls.
- Independent lane controls cover both a missing host edge and two different valid edges across platforms and frameworks. Runtime controls also cover imported assembly `Reference` items, case-insensitive conditional packages, same-basename source impersonation, project-local root redirection and a linked project file. Fixture paths containing `&` are XML-escaped.
- Version controls include recursive/captured aliases, global origins, post-item reassignment, local assignment and PropertyGroup condition selectors, ineffective overwritten selectors, and enabled EventStore-pin exceptions with wrong property names and arbitrary imported defining files.
- Native `dotnet --info` resolution under repository policy is compared with Locator's accepted SDK identities before registration. A filtered native-selected SDK is refused rather than replaced by an accepted alternate. Physical SDK directory identity accommodates relocation aliases while retaining Locator's spelling for SDK import filtering. A fresh relocated process evaluates both a standalone SDK project and the governed Works Contracts project in its real Debug and Release modes. Missing SDK policy refusal and clean runtime output inventory pass.
- Revision controls validate gitlink mode/type/hash/path before extracting the hash, preserve all historical SHA claims and inspect every source root selected by the evaluated host. An actual temporary checkout with a different revision selected through an explicit `HexalithEventStoreRoot` override is refused with its consumed path and checkout hash in the diagnostic. No existing repository ref, submodule content or gitlink was changed by the control.
- During focused iteration, two ordinary compile errors and native-path trailing-separator/relocation-alias identity failures were corrected before qualification. Final file-ending normalization was followed by a forced restore and the fresh full builds/runs recorded above. There is no outstanding build or test blocker. Linux execution is qualified; Windows execution was not performed.
- Corrected raw build/test logs, forced-restore output and xUnit XML are retained beside the unchanged initial evidence with the `corrected-` prefix in [verification evidence](evidence/five-deferred-build-governance-2026-10-23/).
- Exactly DW-63, DW-76, DW-77, DW-81 and DW-82 are now resolved by the corrected verification. Skill-directed review and authorized Conventional Commit/fetch/divergence/push closeout remain with the workflow coordinator; the final combined execution task remains unchecked until those steps finish.


### Final coordinator verification and closeout — 2026-10-23

- Both independent review passes are triaged above. All accepted findings were corrected; B8 was rejected as a low-value unproven expansion. No finding was deferred and no new deferred entry was added. The coordinator audited the final implementation diff and passing tests for every approved matrix row.
- The implementation subagent's post-review focused Release build had 0 warnings/errors, and all four affected classes passed **113/113**, 0 failed/skipped. Their raw evidence is retained with the `direct-focused-` prefix.
- Remote `main` advanced to `7afb47ef6a002b332bb8e6a9abba510b75c8d72d` during implementation, updating the public EventStore pin to **3.127.0**. The coordinator fetched and inspected that commit, rebased the uncommitted batch with autostash, and preserved every upstream change. The ownership exception now follows the separate literal `WorksPinnedEventStoreVersion` authority, rather than retaining the earlier version literal; controls cover a later approved pin and missing, blank, dynamic and mismatched authority. This batch does not change the inherited public pin or any submodule gitlink/content.
- Final source Debug build: **0 warnings, 0 errors; 398 passed, 0 failed, 0 skipped**. Final package Release build: **0 warnings, 0 errors; 398 passed, 0 failed, 0 skipped**. Qualification ran after the upstream integration and final pin-authority correction. Earlier 356/378 and pre-integration 393 totals remain historical evidence only.
- Every edge-case family ran in both complete suites, including provenance through item/metadata/Choose/import selection; real emitted assembly identity behind an alias; unavailable runtime edges; descendant link refusal and workspace alias acceptance; ordinary/linked-worktree source-root drift; and actual Register-level native-SDK filtering refusal with owned installation files.
- Final raw logs/XML and exact source SHA-256 bindings are retained in [final-verification.json](evidence/five-deferred-build-governance-2026-10-23/final-verification.json) and the adjacent `final-*` gzip files. Linux execution is qualified; Windows execution was not performed.
- `git -c core.whitespace=cr-at-eol diff --check` passed. `npm run commitlint -- --edit /tmp/works-five-build-commit-message.txt` passed. The user explicitly authorized commit and push to `main`; the coordinator performs normal Git closeout with divergence checks and no force push.

The final two-mode qualification executed this script from the Works workspace:

```bash
#!/usr/bin/env bash
set -euo pipefail
cd /home/administrator/projects/hexalith/works
export HexalithBuildsRoot="$PWD/references/Hexalith.Builds"
task_verify_root="$PWD/.artifacts/five-build-governance/final-review"
mkdir -p "$task_verify_root"
dotnet restore tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -p:Configuration=Release --force --force-evaluate > "$task_verify_root/restore.log" 2>&1
for task_configuration in Debug Release; do
    task_lane="${task_configuration,,}"
    mkdir -p "$task_verify_root/$task_lane"
    dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c "$task_configuration" --artifacts-path "$task_verify_root/$task_lane" -m:1 > "$task_verify_root/$task_lane/build.log" 2>&1
    dotnet "$task_verify_root/$task_lane/bin/Hexalith.Works.ArchitectureTests/$task_lane/Hexalith.Works.ArchitectureTests.dll" -result-xml "$task_verify_root/$task_lane/result.xml" > "$task_verify_root/$task_lane/test.log" 2>&1
    python3 - "$task_verify_root/$task_lane/result.xml" <<'PY'
import sys, xml.etree.ElementTree as E
root=E.parse(sys.argv[1]).getroot()
for assembly in root.findall('assembly'):
    print(sys.argv[1], {key:assembly.attrib[key] for key in ('total','passed','failed','skipped')}, flush=True)
    assert int(assembly.attrib['failed']) == 0 and int(assembly.attrib['skipped']) == 0
PY
done
git -c core.whitespace=cr-at-eol diff --check
```
