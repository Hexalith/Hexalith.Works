---
title: 'Refresh dependencies and publish a verified Works release'
type: 'chore'
created: '2026-11-16'
status: 'in-review'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'bbbb1edf17a21066d79e1a4a8bd359feb51b378e'
context:
  - '/home/administrator/projects/hexalith/works/AGENTS.md'
  - '/home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md'
  - '/home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-git-instructions.md'
---

<frozen-after-approval reason="user explicitly authorizes updates, push, release and bypass of human validation">

## Intent

**Problem:** The user requests synchronized repositories, the latest .NET SDK and package versions, corrected CI failures, and a published Works release whose NuGet packages are verified.

**Approach:** Synchronize the root-declared dependency repositories, verify SDK selection against Microsoft's latest stable release, update the shared package catalog and Works-owned dependencies, resolve observed failures, and release the exact green main revision through existing trusted publishing.

## Boundaries & Constraints

**Always:** Preserve pre-existing work. Work inside each owning repository. Use current stable packages where the target framework and measured compatibility contract permit them; document holds precisely. Keep dependency audits, blocking tests and package integrity checks active. Validate exact full Conventional Commit messages before committing. Run required repository checks before pushing. Freeze revisions once release starts. Use run-with-temp for disposable jobs and save useful results outside TMPDIR.

**Never:** Initialize or update nested submodules in this umbrella workspace; perform destructive resets; suppress framework support failures; claim an unverified source/package pairing; publish before the required CI succeeds; alter already published NuGet package contents. Root synchronization includes only the 14 dependencies declared in the umbrella .gitmodules. The requested release is Hexalith.Works; publish dependency repositories only when required to deliver this release.

**Authorization:** The user's request authorizes commits, synchronization, pushes, publication and proceeding without additional human validation. Existing remote technical checks remain requirements.

</frozen-after-approval>

## Code Map

- `global.json` and all .NET root-declared dependency `global.json` files already select SDK 11.0.100, the latest stable SDK verified on 2026-11-16.
- `references/Hexalith.Builds/Props/Directory.Packages.props` centrally owns 317 package entries and finite compatibility views; `DEVELOPMENT.md` describes retained SDK4.2, OpenAPI and net10 compiler cohorts. Generic defaults can advance independently of retained views.
- `Directory.Packages.props` delegates Microsoft.Build, Microsoft.Build.Framework and Microsoft.Build.Locator ownership to Works. Microsoft.Build 18.9.6 is stale; 18.12.2 is current. Framework 18.12.2 and Locator 1.11.2 already match latest stable.
- `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj` overrides RuntimeFrameworkVersion but inherits net10.0. CI run 43506383603 fails because transitive Microsoft.NET.StringTools 18.12.2 requires net11.0. Align this test host; update the Works product target to net11.0 when required to consume current stable packages.
- `tools/source-debug-dependencies.json` records reviewed EventStore and serialization source/package pairs. The existing gitlinks have advanced beyond the manifest. Investigate and verify actual compilation before changing their identities.
- `scripts/verify-platform-parity-ci.sh` invokes Platform's canonical parity gate; CI run 43506383603 has a separate parity failure requiring its artifact diagnostics.
- `.github/workflows/ci.yml`, `README.md`, `scripts/validate-release-source.sh` define required checks. `.github/workflows/release.yml` uses production trusted publishing and verifies the exact five packages in `tools/release-packages.json`. Production currently has no reviewer rules; publication is enabled.

## Tasks & Acceptance

**Execution:**
- [x] Root-declared repositories — fast-forward clean main branches to fetched origin/main, preserving user work and avoiding nested initialization.
- [x] `references/Hexalith.Builds/Props/Directory.Packages.props` — verify and update available stable packages: AngleSharp 1.10.4, FluentUI Components/Icons 5.1.0, Testcontainers 4.17.0, Verify/Verify.XunitV3 33.5.0, coverlet.collector 10.1.1, default EventStore 5.2.0 and Tenants 8.0.1. Preserve independently justified compatibility views and test the changed catalog.
- [x] `Directory.Packages.props` and architecture test project — align Microsoft.Build to 18.12.2 and target its architecture test process at net11.0; build and run the affected project.
- [x] Existing failing product or release inputs — use concrete CI diagnostics to fix the observed failures. Record unresolved external requirements without weakening gates.
- [ ] Owning repositories and root gitlinks — inspect the complete diff, validate messages, run mandatory checks, commit and push dependency content before root pointers; verify remote availability.
- [ ] Existing Release workflow — once exact main CI is green, dispatch, observe completion, and query NuGet for all five exact package versions and the matching release tag.

**Acceptance Criteria:**
- Given the synchronized source, when SDK selection is evaluated, then every .NET repository selects current stable SDK 11.0.100.
- Given changed dependencies, when required checks run, then the relevant build/tests pass and no audit or skip gate was weakened.
- Given a release, when publication completes, then its tag resolves to the released main revision and all five manifest IDs have the exact published version on NuGet.org.

## Implementation Notes

- The implementation handoff covers content changes and focused local verification. The parent orchestrator handles commits, remote operations, full CI, and publication after reviewing the changes. Do not commit or push from the implementation handoff.
- Also update root `@semantic-release/github` to stable 12.0.11 and regenerate its lockfile with the repository's npm. Keep semantic-release 25.0.9 pending an actual Node >=26.13 pipeline migration; current shared execution is Node24.
- Confirmed metadata: Microsoft.Build18.12.2 and EventStore5.2.0 require net11. Works product and historical Platform proof retain SDK4.2/net10; preserve historical proof fixtures, while advancing current Works product/consumer targets to net11.0 to consume current stable packages. Do not add a new Works net10 compatibility view merely to avoid that migration.


- Final content verification: Release/package build and Debug/source proof pass; 597 unit tests, 404 architecture tests, 3 property tests, 131 release-tooling tests and 1419 ordinary integration tests pass. The nine explicit live facts remain assigned to canonical parity, with no skip policy changed. Both actual public SDK and unchanged-binary ABI probes pass.
- All five 2.0.0-releasecheck.1 package candidates and symbols packed; inventory and isolated net11 package consumers pass. These are local validation artifacts, not a public release.
- Fixture-only finite timing allowances were adjusted after contention exposed provenance/work-unit tests using100–500ms real-time budgets. Production deadlines, candidate counts, scheduling bounds and every assertion remain unchanged.
- Builds source/audit commits were merged with the concurrent upstream refresh and pushed at f517ff787823f7a9b2074a062dedaf7f071ee42e. The complete 317-package/157-family audit validator passes. Platform selector correction was pushed at 2ce2a485d65ce66be7b7a6c7901d415b1fafa6e9 after 99 focused owner tests passed.
- Remaining release blocker: the actual canonical CI-profile parity run passes its evidence/closure tests but refuses at agents-full-refusal. Its underlying pin check reports "The current catalog view requires the exact authenticated catalog Git body and SHA256." The new catalog/SDK cohort has not acquired the owner-qualified publication binding. No gate or package-cohort admission was relaxed; no new public Works release has been created.
- The Works commit must carry the validated BREAKING CHANGE footer because current packages now require .NET 11. Remote exact-source CI and publication remain open tasks.

## Spec Change Log

- Investigation confirmed current stable serialization, Commons and SDK packages require net11.0. The initial assumption that Works product targets should stay net10 was unsupported by the user request. Advance current product targets and consumer probes as necessary, preserving explicitly historical proof fixtures.

## Review Triage Log

| Finding | Verdict | Evidence and disposition |
| --- | --- | --- |
| Blind 1: parity catalog selector | high | The owner helper rejects the actual catalog's retained/current declarations before any lane. Patch existing selection using isolated MSBuild evaluation; retain exact literal/default agreement checks. |
| Blind 2: Operations output path | high | Full integration run passed 1417 cases but this fact failed after reading net10 output from a net11 build. Patch discovery to use the evaluated project target. |
| Blind 3: Works ABI current paths | high | Current Contracts compilation emits net11 while the documented probe reads net10 metadata and DLLs. Patch current paths separately from the frozen baseline. |
| Blind 4: package target validation | false | All current release projects inherit net11 and their selected public SDK/serialization dependencies reject net10 compilation. No incorrectly targeted artifact was demonstrated; package-only consumers and existing inventory checks remain required. |
| Blind 5: temporary clone lifecycle | low | Source checkouts are unnecessary after artifact verification. Direct cleanup at hook exit preserves only the separately extracted verified artifacts. |
| Blind 6: ambient Git selectors | medium | Newly added git -C inspection can be redirected by GIT_DIR/WORK_TREE/INDEX_FILE. Reuse the existing inspection policy and add an actual poisoned-environment regression. |
| Blind 7: CRLF comment | low | whitespace=cr-at-eol recognizes CR line endings; it does not force checkout conversion. Correct the comment only. |
| Blind 8: breaking version | false | Spec type is not the release commit. The user authorized current dependencies, README states .NET11 minimum, and the parent will validate a BREAKING CHANGE Conventional Commit before publication. |
| Blind 9: descendant stream timeout | maybe-false | No selected SDK/project evaluation was shown to spawn a descendant retaining redirected handles. A process-double reproducer would settle the claim; record as unverified follow-up, with no invented failure claim. |
| Blind 10: SDK working directory | medium | dotnet selects global.json from the caller's directory. Set selected-project cwd and verify refusal of an explicitly unavailable selected SDK. |
| Verification 1: clean HEAD race | medium | The tested dirty-source race leaves a clean revision change untested; mutation of only the HEAD guard survived. Add a clean revision-change refusal test. |
| Verification 2: SDK consumer target | high | The configured public SDK5.2 only supplies net11 assets, but the documented probe creates net10 consumers and extracts net10 assemblies. Patch current consumer/extraction/runtime context. |
| Verification 3: Works ABI paths | high | Same confirmed current-output defect as Blind 3; retain a separate finding record and patch the shared cause. |

Edge-case reviewer reported no additional findings.

## Verification

- Verify available versions with Microsoft release metadata and official NuGet flat-container indexes.
- Run the Builds catalog and adopted-module checker according to its documentation, using a standalone disposable clone when direct dependencies are required.
- Restore/build the Works solution in Release package mode and run individual test projects; inspect exact-source GitHub CI for all mandatory lanes.
- Run release tooling regression tests and source-pair verification; preserve distinct failures.
- Validate Conventional Commit messages with each owning repository's pinned validator before committing.
- Query the final release and all five NuGet flat-container indexes after successful publication.
