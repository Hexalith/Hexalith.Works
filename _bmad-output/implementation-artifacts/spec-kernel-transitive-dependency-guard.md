---
title: 'Guard kernel transitive dependencies'
type: 'bugfix'
created: '2026-08-27'
status: 'done'
baseline_revision: '1fa9d73ad3ae1f7ec78a1b4bea09d17a969b9896'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - '.bmad-loop/runs/20260826-171625-6b20/bundles/kernel-transitive-dependency-guard/intent.md'
warnings: []
deferred:
  - summary: >-
      The governed kernel project set is a hard-coded four-name list that nothing reconciles against
      what actually exists under `src/`, so a fifth kernel project would be silently ungoverned.
    evidence: |-
      `KernelDependencyPolicy.GovernedProjects` and `DependencyDirectionTests`' allowlists each name the
      kernel projects independently, and `GovernedProjectSetIsExact` pins the policy list literally. Nothing
      compares either list against the `src/` directory listing, which is the same class of blind spot DW-1
      was filed for. Pre-existing: the original `P0_KernelProjectsStayInfrastructureFree` carried the same
      hard-coded shape before this story.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:10
    severity: medium
  - summary: >-
      The forbidden-family taxonomy exists twice with nothing reconciling the two lists: the direct
      project-file text scan keeps its own literal string list while the evaluated-closure policy keeps
      structured families.
    evidence: |-
      `ScaffoldGovernanceTests.P0_KernelProjectsStayInfrastructureFree` holds a `forbiddenReferences` array of
      eight raw strings, and `KernelDependencyPolicy.ForbiddenFamily` independently implements eleven families
      plus segment and prefix lists. Adding a family to one leaves the other blind, and no test compares them.
      This is the same drift class as DW-19, but for the forbidden set rather than the governed project set.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:126
    severity: medium
  - summary: >-
      Two further kernel-purity fitness tests keep their own hand-maintained kernel project lists that did not
      adopt the centralized governed set, and one of them omits Reactor.
    evidence: |-
      `P0_WorkItemKernelRemainsPure` lists four kernel roots and `P0_WorkItemKernelDoesNotLogPayloadsOrPii`
      lists three (Reactor absent), both as local `string[]` literals rather than
      `KernelDependencyPolicy.GovernedProjects`. The logging gap is currently covered elsewhere by
      `RuntimeAdapterGovernanceTests.P0_PureProjectsRemainFreeOfActorClockLoggingNetworkFileAndEventStoreRuntimeApis`,
      which scans all four projects, so this is drift risk rather than an open hole today. Pre-existing: both
      lists predate this story.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:205
    severity: medium
  - summary: >-
      `IsFrameworkLibrary` exempts every `Microsoft.*` and `System.*` name from segment-based classification, so
      a Microsoft-branded adapter is governed only when an explicit rule names it.
    evidence: |-
      The exemption is load-bearing for safe framework names such as `System.Security.Cryptography`, whose
      `Security` segment would otherwise match `_namedAdapterSegments`. The cost is that names such as
      `Microsoft.<x>.Mcp`, `Microsoft.<x>.Client`, or `Microsoft.<x>.UI` bypass every segment family; the LLM
      family already needed hand-written `Microsoft.Extensions.AI` and `Azure.AI` rules for exactly this reason.
      Narrowing the exemption to known framework roots is a false-positive tradeoff that needs a deliberate call.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:637
    severity: medium
---

<intent-contract>

## Intent

**Problem:** `P0_KernelProjectsStayInfrastructureFree` scans only raw project-file text, so Contracts, Server, Projections, or Reactor can acquire forbidden infrastructure through an allowed direct project reference without failing the architecture gate.

**Approach:** Extend the gate to inspect each kernel project's restored, evaluated dependency closure while preserving the existing exact direct-reference allowlists. Add a focused negative policy seam that proves a forbidden transitive dependency is reported.

## Boundaries & Constraints

**Always:** Govern Contracts, Server, Projections, and Reactor; require a present and parseable evaluated dependency graph for every governed project; allow `Hexalith.EventStore.Contracts` while rejecting EventStore client/runtime projects; report the owning kernel project and forbidden dependency; keep the negative proof non-vacuous.

**Block If:** The repository's normal restore/build does not produce an evaluated dependency artifact that the architecture test can inspect without invoking nested builds or mutating dependency state.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md`; weaken or replace `P0_SourceProjectReferencesFollowWorksArchitectureDirection`; classify source-code symbol scans as dependency-closure evidence; add a real forbidden package/project to production projects as the negative fixture; update dependencies or submodules.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Clean kernel closure | Evaluated closure contains Contracts and approved low-level dependencies only | Gate passes for all four kernel projects | No error expected |
| Forbidden transitive dependency | A direct allowed project brings in Dapr, EventStore client/runtime, UI, MCP, LLM, OpenAPI, hosting, telemetry, persistence, or another named adapter family | Policy returns a violation naming the forbidden dependency and governed project | Architecture assertion fails with actionable diagnostics |
| Missing or malformed graph | A governed project's evaluated dependency artifact is absent, has no target graph, or cannot be parsed | Gate cannot pass vacuously | Architecture assertion fails with the artifact path and reason |

</intent-contract>

## Code Map

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:122` -- Existing direct-text purity test; extend its governed set to Reactor and route evaluated closure entries through the policy without removing the direct scan.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:10` -- Exact direct `ProjectReference` allowlists for Contracts, Server, Projections, and Reactor; preserve unchanged.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RepositoryRoot.cs:5` -- Existing repository locator for resolving each `src/<project>/obj/project.assets.json` artifact.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs` -- New single-purpose parser/policy seam for extracting evaluated target-library names and classifying forbidden adapter dependencies.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs` -- New non-vacuous negative proof using a synthetic evaluated closure; no production dependency mutation.
- `src/Hexalith.Works.{Contracts,Server,Projections,Reactor}/obj/project.assets.json` -- Read-only restore outputs currently expose `net10.0` target closures; never edit or commit them.
- `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj` -- Kernel `ProjectReference` set; Reactor must stay referenced so an isolated architecture-test restore produces its evaluated assets.

## Tasks & Acceptance

**Execution:**
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs` -- parse restored target libraries defensively and classify forbidden dependency families while allowing EventStore Contracts -- centralizes deterministic policy and diagnostics.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` -- inspect evaluated closures for all four kernel projects in addition to retaining direct project-text checks -- closes the transitive blind spot.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs` -- prove the seam accepts the current safe shape, rejects a forbidden transitive Dapr/EventStore client chain, and refuses missing/malformed graph input -- prevents vacuous or parser-only success.

**Acceptance Criteria:**
- Given restored dependency graphs for Contracts, Server, Projections, and Reactor, when the architecture suite runs, then every evaluated transitive dependency is checked and the current repository passes.
- Given a synthetic evaluated closure where an otherwise allowed direct dependency introduces `Hexalith.EventStore.Client` and `Dapr.Client`, when the policy seam runs, then both forbidden dependencies are returned as violations.
- Given direct project references are evaluated, when the suite runs, then the existing exact allowlists remain enforced unchanged.
- Given an evaluated graph is absent or unusable, when the gate runs, then it fails explicitly rather than passing with an empty closure.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 13: (high 3, medium 10, low 0)
- defer: 0
- reject: 6: (high 0, medium 2, low 4)
- addressed_findings:
  - `[high]` `[patch]` Validate restored-project identity so another project's safe assets file cannot satisfy the governed closure.
  - `[high]` `[patch]` Reject stale assets older than the governed project so a newly edited reference cannot hide behind an old safe graph.
  - `[high]` `[patch]` Inspect evaluated framework references so `Microsoft.AspNetCore.App` and equivalent hosting/UI surfaces cannot bypass target-library checks.
  - `[medium]` `[patch]` Add Reactor to the architecture test project's references so isolated restore/build produces its evaluated assets.
  - `[medium]` `[patch]` Centralize and exact-test the four governed projects so Reactor cannot silently fall out of the gate.
  - `[medium]` `[patch]` Add namespace-aware Picker and non-EventStore Client adapter-family classification.
  - `[medium]` `[patch]` Cover common storage, network, and messaging client families omitted by the first policy pass.
  - `[medium]` `[patch]` Anchor named adapter segments to avoid false positives for safe framework libraries such as `System.Security.Cryptography` and `System.Threading.Channels`.
  - `[medium]` `[patch]` Fail closed on malformed framework entries, aliases, and framework-reference metadata.
  - `[medium]` `[patch]` Inspect every matching target object, including case-variant duplicates.
  - `[medium]` `[patch]` Add table-driven negative coverage for every required dependency family and safe near-match coverage.
  - `[medium]` `[patch]` Preserve the synthetic EventStore Client-to-Dapr chain proof while locking the exact governed project set.
  - `[medium]` `[patch]` Align restore with the Release `--no-restore` build configuration.

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 0, medium 4, low 3)
- defer: 1: (high 0, medium 1, low 0)
- reject: 27: (high 0, medium 2, low 25)
- addressed_findings:
  - `[medium]` `[patch]` Freshness compared the artifact only against its own `.csproj`, so a forbidden transitive dependency arriving through central package management (`Directory.Packages.props`) or the shared props/targets/NuGet config left every governed `.csproj` untouched and hid behind an old safe graph; the artifact is now checked against the shared restore inputs too.
  - `[medium]` `[patch]` `IsFrameworkLibrary` exempts every `Microsoft.*` name from segment matching, so the mainline LLM abstraction family passed; added explicit `Microsoft.Extensions.AI`, `Azure.AI`, and `Anthropic` rules.
  - `[medium]` `[patch]` The UI family missed non-Microsoft Blazor packages and the telemetry family missed logging sinks; added `Blazorise`, `Radzen`, any `Blazor` segment, `Serilog`, and `NLog`.
  - `[medium]` `[patch]` The gate's own path composition and file-reading path had no negative proof — only the in-memory seam did; added `KernelDependencyPolicy.EvaluateGovernedProject` plus repository-layout tests proving a forbidden closure is reported and a clean one accepted from `src/<project>/obj/project.assets.json`.
  - `[low]` `[patch]` A library key whose name part is whitespace (`" /1.0.0"`) normalized to a blank dependency name and was classified safe; it is now reported as a malformed library entry.
  - `[low]` `[patch]` The gate aborted on the first direct-text violation or missing project file, hiding the remaining kernel projects' evaluated-closure diagnostics; text and missing-file findings are now collected alongside closure violations and reported together.
  - `[low]` `[patch]` The spec's Code Map omitted `Hexalith.Works.ArchitectureTests.csproj`, which the change edits and whose Reactor reference the gate depends on.

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 1, medium 3, low 3)
- defer: 3: (high 0, medium 3, low 0)
- reject: 21: (high 0, medium 3, low 18)
- addressed_findings:
  - `[high]` `[patch]` Framework references were inspected only where the governed project declares them, but a restored library carries its own shared-framework demands under `targets/<tfm>/<library>.frameworkReferences`; verified against this repository's real artifacts, `Grpc.AspNetCore.Server`, `OpenTelemetry.Instrumentation.AspNetCore`, and `Hexalith.EventStore.DomainService` all carry `Microsoft.AspNetCore.App` there while their own names classify safe, so the kernel could acquire the ASP.NET Core shared framework transitively with the gate green. Target-library framework references are now classified and reported with their carrying library, and malformed ones fail closed.
  - `[medium]` `[patch]` A target graph claimed by no declared framework was silently skipped — the one fail-open path in an otherwise fail-closed parser — so a forbidden dependency living only in a leftover or extra target escaped inspection; unclaimed target graphs are now reported.
  - `[medium]` `[patch]` Freshness ignored referenced project files, so a `git submodule update` bringing a forbidden dependency into `references/Hexalith.EventStore` left the governed `.csproj` and every shared root input untouched and the old clean graph was still judged fresh; the artifact's own `project.restore.frameworks.<tfm>.projectReferences` paths are now freshness inputs.
  - `[medium]` `[patch]` The shared restore-input list omitted `Directory.Solution.props` and `global.json`, both present at this repository's root, and spelled `NuGet.config` only two of three ways on a case-sensitive filesystem; all are now checked, and the freshness theory covers each.
  - `[low]` `[patch]` `TryNormalizeLibraryName` rejected an all-whitespace name but never trimmed a padded one, so `" Dapr.Client /1.18.5"` normalized to a name that matched no family and passed as safe; names are now trimmed before classification.
  - `[low]` `[patch]` The gate's failure message named only the evaluated dependency closure while the collected list also holds missing-project-file and direct project-file findings; the message and the list's name now describe everything they carry.
  - `[low]` `[patch]` The direct scan's new message claimed `{project} directly references '{forbidden}'` for what is a whole-file substring match that also fires on comments and unrelated values; it now reports a project-file mention.


## Design Notes

NuGet's `obj/project.assets.json` target keys are the restored graph after MSBuild property/condition evaluation and include transitive packages and projects. Read the target selected for the project's target framework, normalize each `name/version` key to `name`, and treat graph discovery/parsing failure as a gate failure. Keep policy matching case-insensitive and diagnostic-first; do not pin safe dependency versions.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx -p:Configuration=Release -v minimal` -- expected: all four governed projects receive current evaluated assets files.
- `dotnet build Hexalith.Works.slnx --configuration Release --no-restore -m:1 -v minimal` -- expected: 0 warnings and 0 errors.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: architecture suite passes, including the negative seam test.

## Auto Run Result

Status: done (second follow-up review pass on the completed 2026-08-27 implementation)

Summary: `P0_KernelProjectsStayInfrastructureFree` inspects the restored, evaluated dependency closures of Contracts, Server, Projections, and Reactor while preserving the existing exact direct-reference allowlists. This pass closed the one remaining transitive bypass — shared frameworks demanded by restored libraries rather than by the governed project itself — made unclaimed target graphs fail closed like every other unusable shape, widened freshness to referenced project files and the two root restore inputs that were missing, trimmed padded library keys before classification, and corrected the gate's failure diagnostics to describe what they actually assert.

Files changed:
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs` -- evaluated-closure parser, project-set ownership, artifact identity, freshness (governed project, shared restore inputs, referenced projects), transitive framework-reference classification, unclaimed-target-graph detection, and the forbidden-family policy.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicyTests.cs` -- policy cases covering clean, forbidden, transitive framework references, malformed, mismatched, stale, unclaimed-target, whitespace-padded-key, project-set, and repository-layout scenarios.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` -- applies the closure policy to every governed kernel project through the layout entry point while retaining the direct text scan, collecting rather than short-circuiting diagnostics, with messages that match what is asserted.
- `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj` -- references Reactor so isolated architecture-test restore/build produces its assets graph.
- `_bmad-output/implementation-artifacts/spec-kernel-transitive-dependency-guard.md` -- records intent, implementation map, review triage, deferred work, and verification evidence.

Review findings breakdown: 7 patches applied (1 high, 3 medium, 3 low); 3 items deferred; 21 findings rejected -- chiefly same-tick and cross-machine timestamp races behind loud fail-closed diagnostics, requests to walk `dependencies` edges (NuGet's `targets/<tfm>` object already is the flattened resolved closure), multi-TFM and non-default-`obj` scenarios this single-TFM repository does not produce, style and naming conventions this repository does not use, and requested edits to the orchestrator-owned deferred-work ledger or to text inside `<intent-contract>`.

Follow-up review recommendation: `true` -- patched findings were high 1, medium 3, low 3; a high-severity patch alone sets the recommendation, and the weighted score is `3 x 3 + 1 x 3 = 12`, above the threshold of 5.

Verification performed:
- `dotnet build Hexalith.Works.slnx --configuration Release -m:1 -v minimal` succeeded with 0 warnings and 0 errors.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` passed 114/114 with 0 errors, failures, skips, or not-run tests (103 before this pass).
- Evidence for the high-severity finding was taken from this repository's real restore artifacts, not from the reviewer's claim: `targets/net10.0/Grpc.AspNetCore.Server/2.80.0`, `.../OpenTelemetry.Instrumentation.AspNetCore/1.17.0`, `.../Hexalith.Works.ServiceDefaults/1.0.0`, `.../Hexalith.EventStore.DomainService/3.97.0`, and `.../Hexalith.EventStore.ServiceDefaults/3.97.0` each carry `frameworkReferences: ["Microsoft.AspNetCore.App"]`, while every governed kernel project's own `project.frameworks.net10.0.frameworkReferences` reads `["Microsoft.NETCore.App"]`.
- Before adding referenced projects and the two new root inputs to the freshness set, each candidate's timestamp was compared against the four governed artifacts to confirm the stricter gate does not turn red on the current tree.
- Matrix audit: executed tests cover the clean closure, forbidden transitive dependency (both target libraries and the shared frameworks they demand), and missing/malformed/unclaimed graph rows, at both the in-memory seam and the repository-layout entry point the gate calls.
- `git diff --check` passed.

Residual risks: dependency classification remains identifier-based and trusts NuGet's generated assets schema, so a newly introduced adapter family still needs an explicit policy entry; `System.*` and `Microsoft.*` names stay exempt from the generic named-adapter segment list and are governed only by the explicit rules (deferred above). The artifact path assumes the default `obj/` intermediate layout, which this repository uses. Freshness now covers the governed project, the shared restore inputs, and the directly referenced projects recorded in the artifact, but not those projects' own transitive inputs, so correctness there still rests on the build having re-restored. Timestamp comparison is strictly-older, so an artifact and an input written in the same filesystem tick are treated as fresh. The deferred-work ledger was not edited by this pass.
