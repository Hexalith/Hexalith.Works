---
title: 'Use evaluated MSBuild dependency discovery'
type: 'bugfix'
created: '2026-08-29'
status: 'done'
baseline_revision: '103dcfde9816706d4f8bd6b2f82654bde27f0f0c'
baseline_commit: '103dcfde9816706d4f8bd6b2f82654bde27f0f0c'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/implementation-artifacts/spec-kernel-governance-drift-hardening.md'
  - '.bmad-loop/runs/20260829-091730-0d52/bundles/msbuild-dependency-discovery/intent.md'
warnings: [multiple-goals]
deferred:
  - summary: >-
      Hexalith source-consumption project discovery remains constrained by the Hexalith.Works*.csproj filename glob.
    evidence: |-
      The live source-consumption gate still enumerates projects by basename, so a differently named root-owned project could evade this specific check. This behavior predates the evaluated-input bundle and is outside its four ledger entries.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:415
    severity: low
  - summary: >-
      PackageVersion ownership does not trace Works-owned property overrides consumed by the approved shared catalog.
    evidence: |-
      Ownership follows the XML file that defines the effective Version metadata. A Works-local property could influence a version expression in the shared catalog while the catalog remains the defining file. This pre-existing property-provenance problem is broader than evaluated PackageReference and PackageVersion item discovery.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:226
    severity: low
  - summary: >-
      The architecture-test lane is bound to the build machine's installed MSBuild layout instead of resolving it at run time.
    evidence: |-
      The test project captures $(MSBuildToolsPath) into an AssemblyMetadataAttribute at compile time and copies the SDK's Microsoft.Build* assemblies next to the test host. A build-once/test-elsewhere pipeline, or an SDK patch installed between build and test, makes ConfigureInstalledMsBuild throw and fails the whole architecture lane. Replacing the hand bootstrap with MSBuildLocator adds a package dependency and is a design decision beyond this bundle.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:475
    severity: medium
  - summary: >-
      The Release lane pins Platform=AnyCPU for every evaluated project regardless of its declared Platforms.
    evidence: |-
      A project declaring a Platforms set that excludes AnyCPU would be evaluated under a platform it does not support, so platform-conditioned imports and ProjectReference items would silently drop out of the evaluated set the gates trust as complete. No project in the repository declares such a set today.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:29
    severity: low
  - summary: >-
      Restored NuGet package build props enter the custom-import freshness closure.
    evidence: |-
      IsCustomImportPath excludes installed-SDK imports and generated build output, but a package's build/buildTransitive props imported from the global packages folder is neither, so it becomes a restore input. Re-extracting or touching the package cache without restoring would report the evaluated dependency artifact stale.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:431
    severity: low
  - summary: >-
      Dependency declarations that only an inactive non-Release condition guards remain invisible when they arrive through an import.
    evidence: |-
      Owning-project XML is scanned condition-agnostically, so a conditional declaration in a governed or scanned project file fails closed. An import that declares a ProjectReference or PackageReference under `Condition="'$(Configuration)' == 'Debug'"` produces no evaluated item in the pinned Release lane and no owning-file sentinel, so no gate observes it. Evaluating a second lane, or treating a conditioned dependency item in any custom import as fail-closed, changes what the shared Builds props are allowed to declare and is a design decision beyond this bundle.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:22
    severity: medium
  - summary: >-
      Runtime-adapter confinement still decides EventStore-runtime and Dapr direction from owning-file XML and basename prefixes.
    evidence: |-
      RuntimeAdapterGovernanceTests reads declared references from the governed project file only and compares them by name prefix, so an imported item or an unrelated project with an EventStore-runtime basename escapes that specific gate. The escape is partly covered elsewhere because ScaffoldGovernanceTests routes through the evaluated EvaluateProjectFile path, but with a different message. The intent named the dependency-direction, source-consumption, and restore-freshness gates; converting this fourth gate is separate work.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs:53
    severity: low
  - summary: >-
      The architecture-test project pins one exact System.Diagnostics.EventLog assembly version to keep the MSBuild reference set warning-free.
    evidence: |-
      Dropping `Version=10.0.0.0`/`SpecificVersion` was tried and reintroduces MSB3277 (unification with the SDK's System.Configuration.ConfigurationManager), so the pin is load-bearing under the repository's zero-warning bar. An SDK band that ships a different EventLog assembly version will therefore need this literal edited. This is the same installed-SDK coupling as the MSBuild-layout entry above, but a distinct literal.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj:41
    severity: low
---

<intent-contract>

## Intent

**Problem:** Dependency-direction, source-consumption, and restore-freshness gates still trust literal owning-file XML or basenames. Imported MSBuild items, same-named unrelated projects, custom import changes, case variants, and semicolon lists can therefore escape governance.

**Approach:** Introduce one fail-closed MSBuild evaluation seam and make repository gates consume its evaluated item identities, canonical full paths, defining-file metadata, and dependency-affecting import closure.

## Boundaries & Constraints

**Always:** Evaluate with explicit Release-lane global properties; preserve analyzer-style and non-output project references; compare project identity by canonical path with platform-appropriate path comparison; retain exact allowlists; include transitive custom imports for the governed and referenced projects in artifact freshness; distinguish Works-owned package-version declarations from the approved shared Builds catalog; emit actionable owning-project, item/import, and evaluator diagnostics.

**Block If:** The installed SDK cannot provide a deterministic evaluated item/import surface, or canonical identity cannot distinguish an allowed project from an unrelated same-basename project without changing production dependencies.

**Never:** Edit the deferred-work ledger, submodules, restored artifacts, or production dependency declarations; reduce project identity to a filename; silently discard missing, malformed, conditional, opaque, or failed evaluation results; treat the imported shared Builds package catalog as Works-owned package consumption.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Imported project item | A props/targets import adds or removes a `ProjectReference` | Exact direction observes the evaluated set, including analyzer references | Missing import, evaluator failure, or unusable identity fails closed with paths |
| Same basename | An unrelated project has an allowlisted `.csproj` filename | Canonical path comparison rejects it | Report expected and actual paths |
| Import freshness | A direct, transitive, or referenced-project custom import is newer than `project.assets.json` | Evaluated graph is stale | Name the newest offending restore input and request restore |
| Package item variants | Case-variant and semicolon-delimited `PackageReference`/`PackageVersion` items contain `Hexalith.*` | Every evaluated item is inspected case-insensitively | Conditional/opaque/malformed discovery remains a violation |
| Catalog ownership | Shared Builds defines Hexalith package versions; Works defines one locally | Shared catalog remains allowed; local definition fails | Diagnose the defining project path |

</intent-contract>

## Code Map

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:188-380,682-907` -- raw direct-reference parsing, evaluated-assets freshness, fixed restore inputs, basename normalization, and reusable fail-closed diagnostics; route live project discovery through one semantic evaluation snapshot while retaining synthetic XML tests.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:9-72,274-355` -- exact name allowlists and the private raw package parser; convert exact topology to canonical paths and source-consumption to evaluated package items.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs:358-687,812-903,1125-1218` -- existing direct-item, malformed-input, identity, and freshness fixtures; extend them through the real evaluated seam.
- `src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj:7-15` -- read-only non-regression fixture: CodeGenerators is an analyzer-style `ProjectReference` omitted by `project.assets.json` but present in MSBuild evaluation.
- `Directory.Packages.props:5-11` and `references/Hexalith.Builds/Props/Directory.Packages.props` -- read-only proof of the current transitive custom import and the external catalog ownership boundary.

## Tasks & Acceptance

**Execution:**
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs` and `MsBuildProjectSnapshot.cs` -- add a focused, fail-closed evaluator and immutable result for canonical evaluated items, defining paths, and the full custom import closure, without shell interpolation or ambient configuration.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs` -- consume evaluated dependencies and import inputs for live-file governance and freshness while preserving the synthetic classifier and evaluated-closure family policy.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` -- compare exact project-reference allowlists by canonical path and replace raw package discovery with the shared evaluated, case-insensitive source-consumption path.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs` and `DependencyDirectionTests.cs` -- cover every matrix row, imported add/remove/condition behavior, analyzer references, cycles/deduplication, evaluator failure, local versus shared package ownership, and non-vacuous current-repository topology.

**Acceptance Criteria:**
- Given imported or differently spelled MSBuild dependency items, when architecture governance runs, then it observes the same evaluated identities as MSBuild and rejects every non-allowlisted canonical dependency.
- Given a current evaluated dependency artifact, when any dependency-affecting custom import in its complete governed/reference closure is newer, then freshness fails with the owning project and canonical input path.
- Given Hexalith package consumption or a Works-owned package version in any supported item spelling/list form, when the source-consumption gate runs, then it fails closed while the externally owned shared catalog remains accepted.
- Given the current repository, when focused and full architecture lanes run, then all governed and AppHost exact topologies pass and the Contracts analyzer reference remains covered.

## Spec Change Log

## Review Triage Log

### 2026-08-29 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 11: (high 0, medium 7, low 4)
- defer: 2: (high 0, medium 0, low 2)
- reject: 5: (high 0, medium 1, low 4)
- addressed_findings:
  - `[medium]` `[patch]` Preserved the owning-project raw declaration scan alongside evaluated imported items so inactive conditions cannot hide forbidden direct dependencies.
  - `[medium]` `[patch]` Evaluated and merged every declared target framework under the explicit Release lane.
  - `[medium]` `[patch]` Propagated effective ProjectReference global properties and keyed recursive visits by canonical path plus properties.
  - `[medium]` `[patch]` Made semicolon-only dependency declarations fail closed for every supported item kind.
  - `[medium]` `[patch]` Canonicalized supported item kinds case-insensitively and validated their evaluated identities consistently.
  - `[medium]` `[patch]` Limited freshness closure inputs to custom imports by excluding installed SDK/tool and generated bin/obj imports.
  - `[medium]` `[patch]` Restored architecture-test coverage semantics so unrelated helper references remain allowed while every Works source reference is checked exactly.
  - `[low]` `[patch]` Made required evaluated project/import freshness inputs report disappearance instead of being silently omitted.
  - `[low]` `[patch]` Added referenced-project file, direct-import, and transitive-import freshness matrix rows.
  - `[low]` `[patch]` Added a file-backed policy regression proving imported forbidden dependency diagnostics reach the live wrapper.
  - `[low]` `[patch]` Added imported, case-variant, semicolon-aware package discovery and defining-file ownership coverage.

### 2026-08-29 — Review pass (follow-up)
- intent_gap: 0
- bad_spec: 0
- patch: 6: (high 0, medium 2, low 4)
- defer: 3: (high 0, medium 1, low 2)
- reject: 20: (high 0, medium 2, low 18)
- addressed_findings:
  - `[medium]` `[patch]` Bounded the generated-import scan below the evaluated project so a checkout whose own path contains a `bin` or `obj` segment can no longer silently empty the custom-import closure that freshness and declaration validation depend on; added a regression fixture that evaluates a project living under an `obj` ancestor.
  - `[medium]` `[patch]` Restored fail-closed coverage for the shared declared-reference discovery sentinels (unreadable source, non-Project root, Include-less and empty-Include items) that the live runtime-adapter gate rejects before its family filters run, plus the removal/update/item-definition exclusion that lost its fixture.
  - `[low]` `[patch]` Rejected conditional, opaque, and malformed AppHost project-reference declarations before the Release-lane exact-topology comparison, so a condition resolved away by the evaluation cannot leave that allowlist silently short.
  - `[low]` `[patch]` Deduplicated the expected canonical path set in the exact project-reference comparison so a repeated allowlist entry cannot produce a cardinality diagnostic that contradicts its own empty difference sets.
  - `[low]` `[patch]` Matched the forbidden-sibling prefix comparison to the case-insensitive identity handling used by the rest of the evaluated discovery.
  - `[low]` `[patch]` Corrected the snapshot documentation that described `ImportPaths` as the complete import closure when it is the custom-import closure.

## Design Notes

`project.assets.json` remains the canonical restored closure but is insufficient for exact direct topology: it omits `ProjectReference` items with `ReferenceOutputAssembly="false"`/analyzer output. Exact item discovery must therefore use MSBuild evaluation; the assets file remains the artifact whose freshness and transitive dependency families are verified. An observed .NET 10 evaluation returned incomplete `MSBuildAllProjects`, so that property alone is not acceptable proof of the import closure; use a stronger evaluated import surface (for example MSBuild's resolved imports/preprocessed-project provenance) and pin its completeness with nested-import fixtures.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx -p:Configuration=Release -v minimal` -- expected: fresh evaluated dependency artifacts.
- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release --no-restore -m:1 -v minimal` -- expected: 0 warnings and 0 errors.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests -class Hexalith.Works.ArchitectureTests.FitnessTests.KernelDependencyPolicyTests` -- expected: focused policy tests pass.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests -class Hexalith.Works.ArchitectureTests.FitnessTests.DependencyDirectionTests` -- expected: focused direction tests pass.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: full architecture lane passes.
- `git diff --check` -- expected: no whitespace errors.

### 2026-08-29 — Review pass (second follow-up)
- intent_gap: 0
- bad_spec: 0
- patch: 4: (high 0, medium 1, low 3)
- defer: 3: (high 0, medium 1, low 2)
- reject: 32: (high 0, medium 4, low 28)
- addressed_findings:
  - `[medium]` `[patch]` Closed the conditional-declaration escape the raw-XML-to-evaluated conversion opened in the source-consumption gate: a `Hexalith.*` `PackageReference`/`PackageVersion` guarded by an inactive condition was scanned before and was invisible to the Release-lane evaluated set afterwards. `EvaluateHexalithSourceConsumption` now runs the shared condition-agnostic owning-file discovery first and fails closed on every conditional, opaque, or malformed package declaration, with a regression fixture covering both the item-level and ancestor-`ItemGroup` spellings.
  - `[low]` `[patch]` Added `GlobalPackageReference` to the evaluated dependency item kinds and to the source-consumption scan, so a Hexalith package injected for every project through central package management cannot bypass the sibling-source rule; covered by a case-variant imported fixture.
  - `[low]` `[patch]` Honoured the author-facing `UndefineProperties` `ProjectReference` metadata alongside `GlobalPropertiesToRemove`, so the referenced-project closure is evaluated with the global properties the build actually uses.
  - `[low]` `[patch]` Restored the ancestor-`ItemGroup` condition coverage lost when the exact-direction fail-closed fixtures moved to the shared discovery tests; the AppHost exact-topology and runtime-adapter gates both depend on that sentinel and no test drove the parent walk.


## Auto Run Result

Status: done

### Implemented change

Follow-up review pass over the evaluated MSBuild dependency-discovery bundle. No spec amendment and no re-derivation were needed; four findings were patched in place. The substantive one is a regression the original conversion introduced: replacing the raw XML package scan with the Release-lane evaluated item set made conditional Hexalith package declarations invisible, even though the intent's edge-case matrix requires conditional discovery to remain a violation.

### Files changed

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs` -- `EvaluateHexalithSourceConsumption` now runs the shared condition-agnostic owning-file discovery before the evaluated pass and folds an evaluation failure into the same violation set; `GlobalPackageReference` joins the package item kinds it inspects.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs` -- `GlobalPackageReference` added to the evaluated dependency item types; `UndefineProperties` honoured alongside `GlobalPropertiesToRemove` when deriving a referenced project's effective global properties.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` -- added the conditional owning-project package fixture and the imported case-variant `GlobalPackageReference` fixture.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs` -- added the ancestor-`ItemGroup` condition sentinel regression for the shared declared-reference discovery.

### Review findings breakdown

- Patches applied: 4 (medium 1, low 3).
- Items deferred: 3 new entries (medium 1, low 2), on top of the 5 recorded in earlier passes.
- Items rejected: 32.

Notable rejections, with the reason: the claim that `FrameworkReference`/`Reference` items reach an un-updated normalizer is false (`TryNormalizeDeclaredReference` handles both kinds); the claim that the canonical allowlists hard-code a checkout topology the build leaves variable is already settled by `SubmoduleLayoutTests`, which requires the `references/` layout; the root `Directory.Packages.props` is still governed, now through central package management's automatic import rather than a hand-rolled file scan; the `<missing canonical path ...>` sentinel is unreachable because `ProjectReference` canonicalization throws first; and `TryGetRequiredFreshnessWriteTime`'s second `Refresh()` does narrow the delete-during-evaluation race it appears to guard.

### Follow-up review recommendation

Patched this pass: high 0, medium 1, low 3. Score = (3 x 1) + (1 x 3) = 6, which is at or above 5, so `followup_review_recommended` is `true`.

### Verification performed

- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release --no-restore -m:1 -v minimal` -- Build succeeded, 0 warnings, 0 errors.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests -class Hexalith.Works.ArchitectureTests.FitnessTests.KernelDependencyPolicyTests` -- Total 166, Failed 0.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests -class Hexalith.Works.ArchitectureTests.FitnessTests.DependencyDirectionTests` -- Total 23, Failed 0.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- Total 235, Failed 0.
- `git diff --check` -- no whitespace errors.
- `dotnet restore` was not re-run: this pass changed only test C# sources, so no restore input changed and the existing evaluated dependency artifacts remain current.

### Residual risks

- The repository `global.json` pins SDK feature band 10.0.3xx with `rollForward: latestPatch`, but only 10.0.400 is installed, so the build and test commands above had to be invoked from outside the repository directory. This predates the bundle and is an environment/`global.json` mismatch, but the documented verification commands still do not run as written from the repository root.
- Conditional dependency declarations that arrive through an import remain outside every gate's view (deferred, medium). The owning-file conservative scan added this pass covers project files only.
- Dropping the `System.Diagnostics.EventLog` version pin was attempted and reintroduces MSB3277, so the architecture-test project stays coupled to one installed SDK's assembly version (deferred, low).
