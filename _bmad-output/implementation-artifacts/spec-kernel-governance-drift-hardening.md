---
title: 'Harden kernel governance against policy drift'
type: 'bugfix'
created: '2026-08-28'
status: 'done'
baseline_revision: '54252467c60a93c7706fdc20269b606e3d2e6281'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/implementation-artifacts/spec-kernel-transitive-dependency-guard.md'
  - '.bmad-loop/runs/20260827-214141-f7db/bundles/kernel-governance-drift-hardening/intent.md'
warnings: [multiple-goals, oversized]
deferred:
  - summary: >-
      Exact dependency-direction allowlists inspect literal project files but not ProjectReference items introduced by imported MSBuild props or targets.
    evidence: |-
      `DependencyDirectionTests.ProjectReferenceNames` loads only the owning `.csproj`. A safe-family imported reference such as Server to Projections would not violate the forbidden-family classifier and could bypass the exact literal allowlist. This limitation predates the current centralized governed-set work.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:277
    severity: medium
  - summary: >-
      Evaluated dependency artifact freshness does not cover the complete custom MSBuild import closure.
    evidence: |-
      `SharedRestoreInputs` checks the known root restore inputs, but a dependency-affecting custom imported props or targets file could change without making an existing `project.assets.json` fail the timestamp gate. The prior transitive-dependency implementation already carried this limitation.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:811
    severity: medium
  - summary: >-
      Exact ProjectReference allowlists normalize by project filename rather than canonical evaluated path identity.
    evidence: |-
      A reference to an unrelated project with an allowlisted `.csproj` basename can normalize to the permitted name. Closing this safely requires evaluated path identity and is a pre-existing limitation of the exact direction test, not a defect introduced by this bundle.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:298
    severity: medium
  - summary: >-
      The Hexalith-source consumption gate still reads PackageReference and PackageVersion item specifications raw, outside the shared fail-closed discovery.
    evidence: |-
      `DependencyDirectionTests.PackageReferenceNames` matches item names case-sensitively, takes `Include` or `Update` verbatim, and never splits semicolon-delimited item lists, so `Include="Something;Hexalith.Foo"` evades the "Hexalith libraries must come from sibling source" rule. The governed-set and forbidden-family paths this story centralized do not consume this helper, and the rule it serves is outside the kernel-purity scope this bundle reconciled.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:300
    severity: low
---

<intent-contract>

## Intent

**Problem:** Kernel governance has several independently maintained project lists and two unrelated forbidden-dependency taxonomies, so a new source project or adapter family can escape one or more architecture gates. The blanket `Microsoft.*` segment exemption also permits Microsoft-branded MCP, Client, and UI adapters.

**Approach:** Establish one auditable source-project classification and one forbidden-family classifier, derive every kernel purity/logging/dependency consumer from them, retain the `System.*` exemption, and classify exact Microsoft adapter segments with negative and safe-near-match proofs.

## Boundaries & Constraints

**Always:** Classify every top-level `src/*/*.csproj` as governed kernel or deliberate adapter; govern Contracts, Server, Projections, and Reactor; keep exact per-project dependency-direction allowlists and reconcile their keys with the governed set; use the same family classifier for direct declarations and evaluated closures; keep diagnostics actionable and discovery non-vacuous; include Reactor in every purity/logging gate; run an independent follow-up review and resolve confirmed defects before the architecture lane.

**Block If:** A current source project cannot be classified from the established architecture, or semantic project-file reference discovery cannot preserve the existing exact dependency-direction guarantees.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md`; change production projects, dependencies, submodules, or restored artifacts; weaken `P0_SourceProjectReferencesFollowWorksArchitectureDirection`; remove explicit family rules; classify `System.*` by generic named segments; treat comments or unrelated XML text as declared dependencies.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Current source layout | Seven top-level Works source projects | Four kernel projects are governed and three adapter projects are explicitly classified | Empty, missing, mismatched, or unclassified project discovery fails with names and paths |
| New source project | An additional `src/<name>/<name>.csproj` | Governance fails until the project is deliberately classified and, if governed, receives an exact direction rule and architecture-test reference | Report the unclassified project without silently expanding policy |
| Direct or transitive adapter | The same forbidden family appears in project XML or `project.assets.json` | Both paths report the owning project, dependency, reference kind, and canonical family | Malformed direct XML or evaluated graphs fail closed |
| Microsoft segment | Exact `Mcp`, `Client`, or `UI` segment under `Microsoft.*` | Classified by the applicable adapter family | Exact-boundary near-matches remain allowed |
| System segment | `System.Security.Cryptography` or `System.Contoso.Client` | Generic segment matching remains exempt | Explicit rules such as `System.Data.SqlClient` still take precedence and remain forbidden |

</intent-contract>

## Code Map

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:10-48,526-642` -- current four-project list, family predicates, named segments, and blanket System/Microsoft exemption; make this the source classification and shared direct/evaluated policy seam.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs:16-107,529-579` -- repeated exact project list plus table-driven family, near-match, and repository-layout fixtures; replace literal pinning with reconciliation and add direct-parity/Microsoft/System proofs.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:13-64,122-160,202-250,596-625` -- required source set, eight-string direct scan, and two local kernel-root arrays; consume centralized classification and roots, including Reactor in logging.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:10-41` -- exact kernel `ProjectReference` allowlists; preserve values while reconciling rule keys to the centralized governed set.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs:22-58,101-143` -- two more four-project purity lists; derive both from the governed set.
- `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj:19-27` -- architecture-test references that produce isolated restored assets; verify their Works-kernel names cover the governed set.
- `src/*/*.csproj` -- read-only source-project discovery surface: governed Contracts/Server/Projections/Reactor and classified adapters Works/AppHost/ServiceDefaults.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs` -- centralize the seven-project classification, derive governed names/roots, reconcile discovered source projects, semantically inspect direct Project/Package/Framework references through `ForbiddenFamily`, and restrict the generic framework exemption to `System.*` while preserving explicit-rule precedence.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs` -- replace local source/kernel arrays and raw forbidden strings with centralized classification, roots, and direct-reference evaluation so every purity/logging gate scans the identical set.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` and `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj` -- retain exact expected references, assert the exact-rule keys and architecture-test kernel references equal the governed set, and make any newly governed project fail until fully wired.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs` -- add non-vacuous current/synthetic source-layout reconciliation, direct/evaluated family parity, malformed direct-reference, exact Microsoft segment, System exemption, explicit System rule, and safe near-match coverage for every matrix row.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/*.cs` -- dispatch an independent reviewer over the completed governance diff against DW-19 through DW-23, patch confirmed findings in their owning files, and rerun focused plus full architecture verification without touching the ledger.

**Acceptance Criteria:**
- Given the current repository, when source-project governance runs, then all seven source projects are classified exactly once and the four governed projects drive every purity, logging, direct dependency, evaluated closure, exact direction, and restore-reference gate.
- Given a newly discovered or missing source project, when reconciliation runs, then it fails with an actionable classification diagnostic before any project can be silently ungoverned.
- Given the exact dependency-direction policy, when its governed keys are compared with the canonical set, then they match and the existing per-project allowed references remain unchanged.
- Given any canonical forbidden family, when declared directly or present in an evaluated closure, then both paths classify it consistently and identify the owning project and family.
- Given exact Microsoft MCP, Client, and UI segments plus safe near-matches, when classification runs, then only exact adapter segments fail; System generic segments remain exempt while explicit `System.Data.SqlClient` remains forbidden.
- Given the finished implementation, when an independent follow-up review and the full architecture lane run, then all confirmed findings are resolved and the lane passes.

## Spec Change Log

## Review Triage Log

### 2026-08-28 — Follow-up review pass (independent, post-`done`)
- intent_gap: 0
- bad_spec: 0
- patch: 13: (high 0, medium 4, low 9)
- defer: 0
- reject: 18: (high 0, medium 3, low 15)
- addressed_findings:
  - `[medium]` `[patch]` Eight rules of the centralized forbidden-family taxonomy (`AdminPortal`, `ConsumerPortal`, `CostGovernance`, `Email`, `Routing`, `Llm`, `AppHost`, `ServiceDefaults`) were classified by no test once the retired eight-literal text scan was deleted. Deleting `Routing` and the `Llm` clause left the full lane green; `ForbiddenDependencyFamilyIsReported` now pins one row per rule, and the same mutation now fails four theories.
  - `[medium]` `[patch]` Dropping the blanket `Microsoft.*` exemption subjects every `Microsoft.*` name to the whole segment vocabulary, but only `Mcp`, `Client`, and `UI` were proved. Added Microsoft-qualified rows for `Security`, `Routing`, `Email`, and `Llm` so the deliberate widening is pinned rather than latent.
  - `[medium]` `[patch]` The intent's "govern Contracts, Server, Projections, and Reactor" had no assertion left after `GovernedProjectSetIsExact` was retired: only `Reactor` and the runnable host were pinned, so a coordinated demotion of Contracts could pass. `SourceProjectClassificationIsCompleteAndDisjoint` now pins both classifications by name; demoting Contracts fails it.
  - `[medium]` `[patch]` `DeclaredReferenceNames(projectPath, kind)` promised an unmatchable sentinel for every unusable specification but loaded the file unguarded, so a missing or malformed `.csproj` escaped as a raw `XmlException`/`FileNotFoundException`. It now returns `<unreadable …>` / `<unusable …>` sentinels, covered by `MalformedProjectFileFailsExactDirectionDiscoveryClosed`.
  - `[low]` `[patch]` The actionable classification pre-check reached only two gates; `P0_WorkItemKernelRemainsPure`, `P0_WorkItemKernelDoesNotLogPayloadsOrPii`, the EventStore/Dapr confinement gate, the pure-project API gate, and the restore-coverage gate surfaced a missing governed project as an unhandled IO exception. All five now run `ReconcileSourceProjects` first.
  - `[low]` `[patch]` The EventStore/Dapr confinement gate filtered discovered references by substring, so a `<conditional …>` or `<unreadable …>` sentinel passed both filters unseen. It now rejects any sentinel before classifying.
  - `[low]` `[patch]` Two purity diagnostics re-embedded the hand-maintained list `(Contracts, Server, Projections, Reactor)` in their message prose; both now render `KernelDependencyPolicy.GovernedProjects`.
  - `[low]` `[patch]` `IsFrameworkLibrary` no longer meant "framework library" after the Microsoft arm was removed, inviting a re-add of the exact hole this bundle closed. Renamed to `IsSegmentMatchingExempt` with the decision recorded at the predicate.
  - `[low]` `[patch]` `IsBuildOutput` was added to `KernelDependencyPolicy` while three identical private copies survived; the copies now delegate to the shared predicate.
  - `[low]` `[patch]` The per-project rationale for each exact dependency-direction allowlist was lost to one generic message. `_governedProjectReferences` now carries the rationale and the failure text names it.
  - `[low]` `[patch]` The new fail-closed branches for a missing governed project file and empty project XML had no coverage; `UnusableGovernedProjectFileIsReportedAsAPolicyViolation` exercises both.
  - `[low]` `[patch]` Nothing pinned that the direct-family gate deliberately reads through `Condition` (stricter) while exact-direction discovery fails closed on it. `ConditionalForbiddenDeclarationIsStillClassifiedByTheFamilyGate` fixes the asymmetry in place.
  - `[low]` `[patch]` `SemicolonDelimitedProjectReferencesAreAllDiscovered` pinned incidental MSBuild item order and its message described a disallowed first item that the fixture does not contain.
- rejected (not defects of this change): the `PackageReference Update=` narrowing is deliberate MSBuild semantics already pinned by `RemovalAndItemDefinitionMetadataAreNotDeclaredDependencies`; the retired text scan's whole-file reach is what the intent's `Never` clause forbids; `ForbiddenProjectFragments` is the separate project-name vocabulary the Design Notes justify; the architecture-test `.csproj` needed no edit because it already satisfies the new coverage assertion; and every ledger, spec-metadata, and orchestrator-bookkeeping finding is outside this story's authority.
- note: the reviewers re-surfaced `DependencyDirectionTests.PackageReferenceNames` as a fourth private raw parser. It is already recorded in this spec's `deferred` list (last item); no duplicate entry was added.

### 2026-08-28 — Follow-up review pass
- intent_gap: 0
- bad_spec: 0
- patch: 9: (high 0, medium 2, low 7)
- defer: 1: (high 0, medium 0, low 1)
- reject: 24: (high 0, medium 3, low 21)
- addressed_findings:
  - `[medium]` `[patch]` `RuntimeAdapterGovernanceTests` kept a third, private project/package reference parser for the P0 EventStore-runtime and Dapr confinement gate; a semicolon-delimited or case-variant item evaded it. Both scans now consume the centralized fail-closed discovery.
  - `[medium]` `[patch]` `ReconcileSourceProjects` enumerated only one directory level, so a source project nested deeper under `src` (or placed directly in it) was silently ungoverned. Discovery is now recursive, and any project file outside the top-level `src/<name>/<name>.csproj` position is reported.
  - `[low]` `[patch]` Nothing pinned that the eight literals the retired kernel text scan guarded are still classified by `ForbiddenFamily`; a theory now proves each one is reported.
  - `[low]` `[patch]` `GovernedProjectRootsCoverCanonicalGovernedSetExactly` restated the implementation expression; it now observes the repository (each root exists and owns its project file, roots are distinct, no adapter root appears).
  - `[low]` `[patch]` `SourceProjectClassificationIsCompleteAndDisjoint` asserted structurally guaranteed disjointness and magic counts; it now pins the two classifications that carry architectural meaning (Reactor governed, the runnable host a deliberate adapter).
  - `[low]` `[patch]` `TryGetDirectReferenceKind` allocated its supported-kind array for every inspected element; the list is now a static readonly field.
  - `[low]` `[patch]` The architecture-test restore-coverage gate compared every `ProjectReference`, so a legitimate test-only reference would have broken it; it now compares the Works source references and still fails closed on malformed or conditional items.
  - `[low]` `[patch]` A hint-path style assembly `Reference` such as `..\libs\Dapr.Client.dll` normalized to a path that matched no family; it is now classified by its assembly name.
  - `[low]` `[patch]` A missing or unclassified governed project surfaced as an unhandled IO exception from the root and project-file scans; both gates now fail first with the actionable classification diagnostic.

### 2026-08-28 — Independent DW-19 through DW-23 follow-up
- intent_gap: 0
- bad_spec: 0
- patch: 5
- defer: 0
- final_result: no confirmed defects remain
- addressed_findings:
  - Fail closed on opaque MSBuild property, item, metadata, and wildcard reference specifications while preserving statically named project files below property-based root paths.
  - Inspect every dependency in semicolon-delimited MSBuild item lists in both direct-family and exact-direction paths.
  - Make conditional project references fail the exact direction allowlist closed instead of silently excluding them.
  - Restrict direct-reference discovery to dependency additions under `ItemGroup`, excluding removal/update-only items and item-definition defaults.
  - Normalize padded evaluated framework-reference names before applying the shared forbidden-family classifier.

### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 12: (high 0, medium 5, low 7)
- defer: 3: (high 0, medium 3, low 0)
- reject: 10: (high 0, medium 2, low 8)
- addressed_findings:
  - `[medium]` `[patch]` Route direct assembly `Reference` declarations, including fusion names and case-variant item kinds, through the shared forbidden-family classifier.
  - `[medium]` `[patch]` Make architecture-test restore coverage compare the complete semantic ProjectReference addition set so malformed, conditional, or extra references cannot be filtered away.
  - `[medium]` `[patch]` Match supported MSBuild dependency item kinds case-insensitively in direct-family and exact-direction discovery.
  - `[medium]` `[patch]` Prove `GovernedProjectRoots` returns one canonical root for every governed project so all root-based purity and logging scans share the full set.
  - `[medium]` `[patch]` Add a negative file-backed `EvaluateProjectFile` proof so the architecture gate's actual wrapper path cannot discard parser violations.
  - `[low]` `[patch]` Make a semicolon-only ProjectReference Include fail closed instead of disappearing as an empty sequence.
  - `[low]` `[patch]` Restrict exact-direction discovery to Include additions under ItemGroup, excluding Remove, Update, and ItemDefinition defaults.
  - `[low]` `[patch]` Cover duplicate classified basenames so the exactly-once reconciliation branch is non-vacuous.
  - `[low]` `[patch]` Cover wildcard direct-reference declarations that must fail closed.
  - `[low]` `[patch]` Cover reversed and multiple forbidden semicolon-list entries so every item is demonstrably inspected.
  - `[low]` `[patch]` Cover ancestor ItemGroup conditions in exact ProjectReference discovery.
  - `[low]` `[patch]` Prove safe near-match behavior through the direct declaration seam as well as evaluated assets.

## Design Notes

The auditable project classification must distinguish the four governed projects from the three legitimate adapter projects instead of inferring purity from names. Direct project-file governance should parse declared reference elements and feed normalized names into the same classifier as restored assets; `ForbiddenProjectFragments` remains a separate v1 product-surface vocabulary rule because AppHost and ServiceDefaults are legitimate source projects but forbidden kernel dependencies.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx -p:Configuration=Release -v minimal` -- expected: current Release assets for every governed project.
- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release --no-restore -m:1 -v minimal` -- expected: 0 warnings and 0 errors.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests -class Hexalith.Works.ArchitectureTests.FitnessTests.KernelDependencyPolicyTests` -- expected: focused policy tests pass.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: full architecture lane passes with no failures.
- `git diff --check` -- expected: no whitespace errors.

## Auto Run Result

Status: done
Blocking condition: none

### Implemented change

A follow-up review pass over the completed kernel-governance-drift diff (`5425246..HEAD` plus working tree). No intent gap and no spec defect surfaced; thirteen findings were patched in place and the full architecture lane re-verified.

### Files changed in this pass

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs` -- fail-closed `DeclaredReferenceNames` file loading, `IsFrameworkLibrary` renamed to `IsSegmentMatchingExempt` with the System-only decision recorded, `IsBuildOutput` promoted to the shared predicate.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs` -- governed/adapter membership pinned by name, one theory row per forbidden-family rule plus Microsoft-qualified segment rows, unusable-project-file and conditional-forbidden-declaration coverage.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` -- per-project allowlist rationale restored into the failure message, classification pre-check on restore coverage, unreadable/missing/non-Project fail-closed test, corrected semicolon-discovery assertion, shared build-output predicate.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs` -- classification pre-checks, sentinel rejection before the Dapr/EventStore substring filters, derived governed-project diagnostic.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` -- classification pre-checks on both kernel-root scans, derived governed-project logging diagnostic, shared build-output predicate.

### Review findings breakdown

- Patches applied: 13 (high 0, medium 4, low 9)
- Items deferred: 0 new (one re-surfaced finding already sits in the `deferred` list)
- Items rejected: 18 (high 0, medium 3, low 15)
- Follow-up review recommendation: `true` -- patched medium 4, low 9; score `3 x 4 + 1 x 9 = 21`, at or above the threshold of 5.

### Verification performed

- `dotnet restore Hexalith.Works.slnx -p:Configuration=Release -v minimal` -- restored.
- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release --no-restore -m:1 -v minimal` -- 0 warnings, 0 errors.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests -class Hexalith.Works.ArchitectureTests.FitnessTests.KernelDependencyPolicyTests` -- Total 148, Failed 0.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- Total 204, Failed 0 (was 189 before this pass).
- Non-vacuity proved by mutation: deleting the `Routing` segment and the `Llm` clause failed 4 theories (green before this pass); demoting Contracts to a deliberate adapter failed `SourceProjectClassificationIsCompleteAndDisjoint` plus both direction gates. Both mutations were reverted and the lane re-verified green.
- `git diff --check` -- no whitespace errors.

### Residual risks

- The four `deferred` entries are unchanged and remain open: imported-props reference discovery, restore-input freshness across the custom import closure, project-filename rather than evaluated-path identity in the exact allowlists, and the raw `PackageReference`/`PackageVersion` parser behind the Hexalith-source consumption gate.
- Widening `Microsoft.*` to full segment matching is now pinned by tests but is still latent against the repository: no current kernel closure contains a `Microsoft.*` name carrying an adapter segment, so the first such transitive dependency is where it will bite.
- Adding a `src/<name>/` project now requires three coordinated edits (classification, direction allowlist, architecture-test reference); the failure diagnostics name the first, and nothing outside the test assembly documents the other two.
