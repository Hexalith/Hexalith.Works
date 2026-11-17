---
title: 'Refresh dependencies and publish a verified Works release'
type: 'chore'
created: '2026-11-16'
status: 'in-review'
route: 'dispatch'
review_loop_iteration: 3
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
- The original canonical parity run refused at Agents catalog authority transport. The repair uses the unchanged authenticated Builds6.0.5 context for Agents while the actual Works file host consumes SDK5.2. The real scaffold build and expected Full refusal now pass; complete live parity and rollback still require a fresh full run. No new public Works release has been created.
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

Final release review (2026-11-17):

| Finding | Verdict | Evidence and disposition |
| --- | --- | --- |
| Final blind 1: absolute untracked review hunk | low | The exported new-test hunk contains an absolute path prefix. Regenerate the review artifact with repository-relative paths; this changes no product files. |
| Final blind 2: hidden original Platform index flags | medium | Preparation checks original-owner status before and after, but clones committed HEAD into a fresh index before its stronger producer checks. Original assume-unchanged/skip-worktree edits can therefore escape the stated clean-owner refusal. Reject those flags at both original-owner checks and exercise real Git cases without changing source or export on refusal. |
| Final blind 3: relative runtime project argument | low | The helper computes an absolute project directory but passes the original relative argument from that new cwd. Normalize the argument before both uses and preserve the selected SDK context. |
| Final blind 4: inherited output pipes | medium | The added MSBuild query bounds launcher exit but waits on stdout/stderr without a deadline. The edge reviewer compiled the actual helper and reproduced a33s return under its30s limit using an exited launcher and an inherited pipe. Bound both streams under the same operational deadline. |
| Final blind 5: custom output layout | medium | Default output reconstruction predates this change: baseline DirectoryFor already assumed bin/configuration/net10. Current nondefault qualification uses the explicit artifacts branch. Defer the pre-existing general custom-layout limitation; future OutputPath/RID layouts require evaluated TargetDir instead of reconstruction. |
| Final blind 6: manifest bound after parsing | medium | Qualified-runtime code reads/parses before its16MiB check; declared-runtime capture lacks an equivalent pre-read limit. Apply a bounded manifest read before parsing and on subsequent manifest verification, retaining exact hashes and refusal behavior. |
| Final blind 7: truncated reason selection | low | More than16 valid reasons are selected in dictionary insertion order without recording omission. Make selection deterministic and retain a bounded omitted count while preserving all health decisions. |
| Final blind 8: ambient token regression | medium | The committed transport test never supplies its valid server credential as ambient DAPR_API_TOKEN, so removing explicit empty-token clearing can escape assertions. Add isolated-process coverage. |
| Final blind 9: last-batch evidence failure | medium | Controlled failures cover only the first batch. Exercise a fifth-child failure after four valid descendants and assert that no effects escape. |
| Final blind 10: additional nested C# types | low | The baseline explicitly requires additional C# types in separately named files. Move SidecarApiTokenHandler, StatusControllerOnly, BrokenCredential and ControlledReads without widening their public surface or behavior. |
| Final edge 1: inherited output pipes | medium | Independently reproduced the same launcher-exit/pipe-completion defect as final blind4 with the actual helper; fix the shared deadline boundary. |
| Final verification 1: ambient token regression | medium | Pre-verified gap: the installed Dapr builder captures ambient DAPR_API_TOKEN, while committed tests leave the valid random server token out of that environment. Group with final blind8 and add the isolated regression. |
| Final verification 2: redirect credential isolation | medium | Pre-verified gap: both test servers return only200/401, so enabling automatic redirects survives the existing direct-foreign-URI assertion. Exercise an actual redirect to a second endpoint and require the redirect response plus zero destination requests. |

## Verification

- Verify available versions with Microsoft release metadata and official NuGet flat-container indexes.
- Run the Builds catalog and adopted-module checker according to its documentation, using a standalone disposable clone when direct dependencies are required.
- Restore/build the Works solution in Release package mode and run individual test projects; inspect exact-source GitHub CI for all mandatory lanes.
- Run release tooling regression tests and source-pair verification; preserve distinct failures.
- Validate Conventional Commit messages with each owning repository's pinned validator before committing.
- Query the final release and all five NuGet flat-container indexes after successful publication.


## Remaining Parity Context Repair

The immediate canonical parity failure is authority transport drift, not missing joint Agents acceptance. `PlatformAppModel.ComposeEntry()` routes Works directly to `WorksPreviewModel.Compose()`, bypassing generic module cohort/extension admission. Do not broaden those unrelated admission sets or publish a new dependency merely to bypass this drift.

- [x] `references/Hexalith.Platform/eng/` — provide a reusable run-owned materializer for the exact existing producer named by `eng/builds-tuple.json` (currently signed Builds6.0.5/e2bbbd0893f586e4d447ef8358ff747ea3d34435). Clone from the initialized producer, fetch that exact revision only into the private clone when shallow, checkout and call existing `verify_catalog()` against the unchanged binding. Sanitize Git redirection, preserve the original checkout and its shallow boundary, never initialize nested submodules, and leave cleanup with the owning run. Add real-Git fixture tests for newer/shallow sources and refusal of wrong/modified catalogs; existing loader/signature/handoff verification remains authoritative.
- [x] `references/Hexalith.Platform/eng/verify-agents-host.sh` — add explicit bound producer context transport. Verify the effective bound checkout, then pass `--catalog`, the binding's authenticated catalog SHA256, and the actual owner `eng/builds-tuple.json` as `--producer-binding` to `verify-platform-pins.py`. Use that same Builds root for LocalScaffold compilation. Keep Full's exact expected `DependencyNotAvailable: EXT-HOST-1` refusal; actual joint Agents acceptance remains unclaimed. Do not authenticate an arbitrary current file by hashing itself.
- [x] `references/Hexalith.Platform/eng/verify_works_host.py` — materialize the existing bound producer under run-owned artifacts and supply its child-only context to BOTH Agents checks. Preserve the new/current catalog for all actual Works consumers and runtime proofs, and record the two contexts explicitly. Include every new helper in the verifier closure. Explicitly pass the selected SDK5.2.0 to the actual file-host build; its named Platform default otherwise selects retained4.2 while metadata currently claims5.2. Continue proving actual restored/runtime SDK identities.
- [x] `references/Hexalith.Platform/eng/verify_works_host.py` and retained build helpers — rebuild the immutable historical topology in the current managed context with explicit TargetFramework=net11.0 when using selected SDK5.2, preserving archive bytes/tree checks and recording actual compilation/runtime properties. These are historical topology sources rebuilt for current qualification; never relabel them original historical binaries. Full retained readiness and rollback remain required.
- [x] `scripts/prepare-platform-builds.sh` — reuse the Platform-owned materializer for the same bound checkout instead of maintaining two producer-acquisition implementations. Keep clean original Platform checks before/after, verified published artifact preparation and successful-only GITHUB_ENV export. Update the real-Git regression fixtures to exercise the shared materializer without registry access or invented publication evidence.
- [ ] Run focused owner/context tests and the actual canonical ci-profile gate. Fix concrete failures without skipping lanes, weakening hash/signature checks or broadening unrelated generic module admission. Parent handles commit validation, remote operations and final verification after the implementation handoff.

The earlier interpretation that a new generic SDK5.2 owner cohort/publication was necessarily required is superseded by the actual Works routing and exact6.0.5 pin-check reproduction. Both validators and actual Agents scaffold build must consume the same bound context; current Works compilation/runtime must consume selected5.2. No missing human approval has been established.


## Follow-up CI Qualification

- Platform producer-context repair was reviewed by three independent agents. Four concrete findings were fixed: per-invocation bound-checkout cleanup, caller-relative explicit paths, the Full-mode child environment, and assume-unchanged/skip-worktree input concealment. Focused Platform tests107/107 and preparation-hook tests6/6 pass. Real Release LocalScaffold succeeds, Full preserves its exact closed EXT-HOST-1 refusal, and all five actual file-host SDK5.2 DLLs match the public package archives. Platform source is pushed in commit550905f8.
- Hosted Works2758966 passes compilation,597 unit tests,3 property tests and1419 ordinary integration tests. Its single architecture failure reads an approved workflow object absent from the shallow Builds checkout. The C# reader now acquires that exact immutable object into a private store; an actual shallow-checkout regression passes with unchanged original HEAD and shallow boundary. The Python release compatibility reader uses the same private acquisition policy.
- Anonymous GitHub metadata requests caused preparation HTTP403 failures. Builds compatibility commit3641d6226b4e6150a4ed43c472c97373378f3679 is exactly four scoped job-token environment additions over the original frozen execution commit; no build, test, audit or publication command changes. The five module workflow pins and direct parity preparation are handled through a bounded CiCd-owner proposal, preserving foreign edits and all existing gates.
- Builds current-source CI exposed an unmaterialized catalog reader selecting retained Tenants7.0.7 before the current8.0.1 default. The owner concurrently delivered the same correction with stronger quote/case handling and explicit conditional-only/ambiguous refusal cases at0a1d2e028c554798969643485631eab245ef020d. Preserve that upstream implementation and skip the redundant unpublished local commit; the original real-catalog assertion remains unchanged.
- The CiCd owner repair's independent review found hard-linked targets could overwrite a foreign alias. The closed all-target preflight now refuses hard links; its real-Git regression verifies all owned and foreign bytes remain unchanged on refusal.


## SDK5.2 Declared Runtime Dependency Repair

The completed local canonical run now preserves the Agents refusal and passes its Release scaffold build, then fails9/171 Platform Works model tests: the published SDK5.2 refuses the preview's DOTNET_STARTUP_HOOKS. Its guard also rechecks immediately before launch, so moving hook insertion later cannot qualify. Current live fixtures append their own proof hook and require the same migration. Keep the public SDK guard unchanged.

This is limited to the existing disposable synthetic Platform proof. Ordinary Works Release builds and the five-package publication inventory must acquire no Platform or proof dependency. No production admission, generic cohort expansion, SDK publication or new package is authorized by this correction.

- [ ] Nonpackable Works host — declare Platform.Runtime only under the existing explicit PlatformWorksRuntimeRoot synthetic selector; mark the qualification context and fail a missing explicit project. Preserve the ordinary Release graph and package artifacts.
- [ ] Platform preview — configure the actual ordinary child ProjectLaunchConfiguration.BuildEnvironment using the SDK-owned CreateLaunchConfigurationAsync/WithDebugSupport pattern before preparation. Retain identity/configuration and real builds. Activate the now-declared hosting startup with ASPNETCORE_HOSTINGSTARTUPASSEMBLIES and WORKS_PLATFORM_RUNTIME, preserving telemetry and HTTP defaults; remove the forbidden runtime hook.
- [ ] Proof fixtures — compile the proof consumer first. Use a fixed tracked targets file containing only finite named-host binary Reference items for that prebuilt proof assembly. Supply the identical explicit import and proof-assembly properties to runtime prebuilds and selected owner launch build environments before preparation. Avoid a test-project/host-project build cycle and leave immutable SDK host sources unchanged.
- [ ] Own the external input closure — the published SDK hashes deployed DLL/deps/runtimeconfig, but does not hash external import bodies. Record a frozen manifest of the fixed target bytes and original proof dependency inventory; verify it before/after preparation and immediately before every relevant resource launch. Bind it in final verifier evidence. Do not claim the SDK authenticates these extra source inputs.
- [ ] Preserve activation semantics — the Platform hosting startup executes before the synthetic proof startup; both retain their explicit enablement/Development gates, exact live assembly hashes and telemetry registration/order evidence. Prove the actual ordinary .deps graph declares these runtime dependencies and that no forbidden hook is configured. Keep undeclared-hook refusal tests.
- [ ] Run actual SDK preparation/prestart and focused model/hosting/guard tests, then repeat the full canonical live parity, retained readiness and complete-fleet rollback. Parent validates commits, exact-source hosted CI and final publication after both implementation handoffs settle.

Independent research reproduced both constraints: simply copying a startup DLL beside a .NET11 host leaves it unresolved; a prebuilt proof DLL referring to its host can be added through an ordinary binary Reference without a project build cycle, including its transitive dependency in .deps.json. The source import must remain a bounded synthetic input, not a general plugin or unqualified producer bypass.


The declared proof dependency intentionally changes deterministic Works executable identity even though method IL, assembly references and attributes remain equivalent. Do not normalize or relax DLL hashes. Retained enrollment must consume the newly qualified exact runtime deployment through a separate frozen runtime-artifact manifest and bounded collectible metadata context; share SDK contract types only when their loaded image/file identities equal the frozen runtime copies, resolve remaining managed dependencies only from that frozen output, and never execute the target entry point. Preserve the archived SuppressBuild semantics and dispose the context with the catalog. The original consumer/proof-import manifest remains separately frozen.

Builds fixture repair07fdb22578ba55e092b2db1b4ff43f253b53fbbf replaces simulated SDK/Tenants/Memories defaults instead of appending duplicate defaults. Production ambiguity refusal is unchanged. The affected24 direct catalog cases and6 default-selection/refusal cases pass with audits enabled; new hosted CI is running.


## Declared Runtime Verification and Review Result

- Platform model15/15, proof-context14/14, Works boundary3/3, and actual hostStart/Stop/DI checks pass. The ordinary Release output has neither Platform nor proof dependencies.
- Combined qualification56/56 and verifier87/87 tests pass. Actual published SDK preparation, nonempty command/operation inventories from the frozen runtime metadata context, prestart mutation refusal, deployed proof DLL/deps binding, and disposal mutation checks are covered.
- Three independent review lenses identified and closed: foreign self-consistent evidence, missing loaded-proof binding, SDK paths outside the runtime inventory, retained hook evidence, missing startup-event coverage, unbounded framework fallback, non-SDK loaded-image tracking, construction/disposal cleanup, and shared JSON contract identity. All preserve original SDK guards and exact runtime hashes.
- The one new public-package preparation class is compiled and required only in Release; actual MSBuild Compile-item checks confirm Debug/Release selection, and verifier tests require the class in Release while preserving all prior Debug supporting classes. No new skipped or not-runnable policy was added.
- Current dependency revisions are frozen for the next canonical run. Full R1–R11/live readiness/retained rollback, exact-source hosted CI and final five-package publication remain pending.


## Isolated Public SDK Qualification

Hosted5d5abe1 and local supporting397-case runs exposed the same new-test boundary: the hot fixture process can contain an SDK image with no file identity. The public SDK correctly refuses it. The required Release test now runs all existing preparation, declaration/operation, exact-body, retained-closure and startup-refusal assertions in a clean ordinary console. It builds a separate normalized public-package consumer, freezes that full output inventory, and binds the original launcher assembly separately from the qualified consumer. SDK guards and all exact hashes remain unchanged; no skipped/not-runnable rule was added.

Targeted and hot-pool MTP qualification pass. The complete private CI-layout run passed1433 cases plus the nine explicitly reserved live facts; its single independent symlink-placement Operations build failure passed1/1 after using a physical private SDK checkout. No Operations source or assertions changed. The parent repeats canonical qualification and hosted exact-source CI on the resulting fix commit.


## Final Pre-release Synchronization

- A concurrent `/pushall` advanced root main to e89d8b8 and seven dependency gitlinks. Those commits are preserved. EventStore3b2350e/Release5.2 was requalified in two complete Debug builds with zero warnings/errors, 235 profile contract tests, 19 affected architecture tests, and the exact golden vectors. Only the current manifest and three current-source documentation markers changed; historical publication identities remain intact.
- Platform artifact preparation now authenticates the clean recorded owner and the independently bound signed producer separately. It records both revisions and rechecks them before emitting success. Tuple-adoption equality, archive/hash/signature/handoff checks and nested gitlinks remain unchanged. Platform168b8732 contains this fix; 49 focused tests pass, including an integrated source-change refusal that fails when the final recheck is removed. Actual clean private preparation verifies the published6.0.5 archive.
- The fixed synthetic proof import aligns Google.Protobuf3.36.1 and System.IO.Hashing10.0.14 only in its three existing proof hosts. The SDK qualifier independently checks those package assets, dependency records and actual PE versions, and now preserves strict warning and audit settings through real SDK preparation. Ordinary publication graphs remain separate.
- The immutable retained topology now receives explicit net11 compile/restore properties, the existing release output pivot, and qualification-only framework metadata across its six known reference wrappers. Its AppHost explicitly uses current catalog Aspire.Hosting.AppHost13.6.5 instead of the archived SDK's implicit13.6.1 reference. Archive bytes and build suppression remain unchanged. Platform3f14b712 contains the verifier context update.
- The identical settled proof target/helper passed all six actual strict runtime prebuilds with zero warnings/errors, 87 verifier tests and one strict public SDK qualification test. Those focused runs used recorded Builds07fdb225; the synchronized final Builds catalog includes the upstream OpenAPI3.11.1 patch and requires fresh complete qualification. The catalog's 362 positive/negative probes pass.
- All132 release-tooling tests pass. Independent review found no remaining verification gaps after the integrated final-refusal regression. Complete canonical live parity, exact-source hosted CI and public five-package publication are still pending.
- External dependency CI failures are recorded under the external source-rebind evidence directory. Unpublished Projects.Contracts2.0.0-story66.3, an incompatible Projects API edge and missing EventStore qualification/attestation authority are not claimed resolved by Works' local qualification.

The final Builds synchronization is af3752ddcbe94c007630703ab3803df5654f9933, a merge preserving the concurrent local and remote histories with a tree identical to audited upstream a1e0c310. The317-package/157-family audit and seven focused catalog/guard checks pass. CiCd owner proposal9b7fa8dbb1e9abedad9161bf0b5691b164d5aed9 closes the exact root07→af3752dd gitlink delta, including the sole OpenAPI3.11.1 catalog change, while preserving the root index and all product edits. All14 selected direct dependency commits are published and clean; no nested submodule is initialized.


## Native Owner Listener Isolation

Exact-source fe426ba hosted compilation/tests, lint, CodeQL and commit policy pass. Its complete canonical run passes397 supporting checks, all six strict runtime prebuilds, binary/telemetry checks and actual retained-host readiness, then rejects both live recovery starts. The SDK correctly detects that the harness cleared the gateway HTTP listener after preparation captured it.

The harness now preserves that selected gateway listener for current Platform and retained starts. Other application/sidecar ports remain dynamic; mTLS, discovery isolation and unchanged SDK identity/credential guards remain active. The actual published SDK5.2 observer and Dapr toolkit regression proves preserved48123 succeeds and the old null rewrite is refused. All25 focused discovery cases pass, the Release build has zero warnings/errors with audits enabled, and independent review reports no findings. A new complete canonical run and exact-source hosted CI remain required.


## Direct Notification and Declarative Subscription Alignment

Exact5254bb5 passes hosted build/tests, lint and toolchain; the local canonical run passes399 supporting checks, six strict prebuilds, binary/telemetry and retained startup. Current Gateway starts but stays unready: its SDK technical transport refuses admission while its unused programmatic `*.*.projection-changed` subscription is denied by the deliberately bounded broker policy. Exact hosted evidence is retained separately.

Platform1ead34c65619c064e8e5fd374d5e6a56be19e830 explicitly retains the SDK's existing `Direct` projection notification transport and enables supported declarative subscriptions for Gateway. ETag regeneration and broadcasts remain direct; the three broker topic grants and scopes are byte-identical. The public SDK5.2 HTTP probe confirms programmatic mode advertises the wildcard while declarative mode removes that metadata and returns404 for discovery. All30 model/policy tests pass with a clean audited Release build; independent review finds no issues. Full live readiness and exact-source CI still require a new run.


## Preserve Prepared Runtime Inputs

The complete cd60c28 evidence identifies the earliest failure: Works fails its actual SDK prestart snapshot check because the harness adds managed-runtime environment variables after owner preparation. Operations and Admin then cannot start, so the catalog cannot activate and Gateway stays unready. The passive `DescribeCore@344` exceptions are caught during normal unmarked-core endpoint scanning; they were not the fatal admission reason. The public SDK/.NET11 probe verifies all35 marked actions pass every initial action-source predicate.

`ConfigureQualificationProjectLaunches` now requires the actual retained-host selector and leaves current Platform annotations/environment unchanged. Retained hosts preserve all four existing compatibility/worker overrides. The unchanged public SDK preparation qualification invokes the real helper after Prepare, then requires successful BeforeResourceStarted and changed-original refusal. Audited Release build0warnings/errors, helper3/3 and actual public SDK qualification1/1 pass; independent review finds no issues. No SDK guard, environment comparison, readiness gate or assertion is relaxed. Complete live parity and exact-source CI remain pending.


## Authenticate Owned Fixture Sidecar Clients

The f20ccbf complete run now passes live startup/readiness, confirming the prepared-runtime fix. Both recovery bodies then fail on their first direct Gateway Dapr state call because fixture clients omitted the SDK's selected private sidecar API credential. This is distinct from the application-channel token and mTLS workload identity.

All live fixture SDK/HTTP factories now resolve the selected app-model resource's endpoints and `DAPR_API_TOKEN` together through the supported Aspire execution configuration API. They refuse configuration errors and unselected resource objects, ignore masked/forged snapshots, and clear ambient SDK-token fallback for tokenless resources. HTTP credentials stay on the selected endpoint, redirects are disabled, explicit wrong headers remain unchanged, and private proof IPC never traverses the credential handler. Existing wrong-sender/mTLS assertions remain intact.

Audited Release build0warnings/errors,24 focused tests and actual Dapr1.19.1 HTTP/gRPC checks pass. Selected owner succeeds; foreign and tokenless clients remain401/Unauthenticated even when the ambient probe token is valid. Independent review reports no findings. Complete recovery/rollback and exact-source CI remain pending; no public release is claimed yet.


## Explicit Tenant Status Selection

The3a62b97 live run passes startup/readiness and authenticated Dapr state access, then the recovery command poller reports unknown. The actual Gateway rejects its bearer-only status request with403 because SDK5.2 requires an explicit tenant selector before reading command status. The response schema and Completed state remain compatible.

Both legitimate live status pollers now send `X-Hexalith-Tenant` with their existing tenant and bearer token. Deadlines, final status assertions and intentional unauthorized requests remain unchanged. An actual published SDK controller/Kestrel regression invokes the real submit/poll helper and proves own200/Completed, missingselector403 and foreignselector403, with no unauthorized store read. Audited Release build0warnings/errors and the focused regression pass. Complete live recovery/rollback and exact-source CI remain pending.


## Bounded Cascade Context Acquisition

Private diagnostic revision27a60035 (not published) passed all399 supporting tests, telemetry and retained startup, then reproduced the five-child recovery failure. The durable parent reached checkpoint6/Attempts10/Parked with reason translation-timeout and no pending plan; process readiness reported complete=true/unresolved1 while reminders were Healthy. No parent trusted-effect submission occurred. Actual translation observations repeatedly cancelled at1897–1902ms, while successful individual gateway reads commonly took300–570ms. Expiry receipt replay succeeded with HTTP200 in about444ms. Redacted read-only process/HTTP observations and complete canonical evidence are preserved in the external release-refresh evidence directory.

The Works translator currently reads source history and five independent child histories sequentially inside the unchanged SDK1.9s translation deadline. Implement bounded batches of four child-context reads through the existing authoritative stream reader and shared SDK admission. Validate the parent first, preserve sorted effect and failure ordering, and construct no effects until every child validates. Keep all source, byte, page, lease, retry, deadline, freshness and health requirements unchanged. Controlled concurrency, missing/corrupt relationship evidence, cancellation and late completion tests plus full canonical and hosted qualification remain required.


Final review patches passed the audited Release build with zero warnings/errors,66 focused C# cases, the final18 affected process/auth cases,8 real-Git preparation cases, focused Ruff and whitespace checks. Accepted fixes retain existing recovery budgets, authority/hash guards, configuration and output-layout semantics. Full canonical live parity, exact-source hosted CI and public release verification are still pending.


Canonical644264b passed all404 supporting tests and all6 recovery cases with no failures or skips, including five-child translation within1.69s, persisted five-effect replay, no-op restart, twelve-source coverage, protected operations, alert clearing and complete-fleet retained rollback. The next historical lane then refused before composition because its fixture omitted the required owned development-catalog profile. Wire capture and explicit profile paths through the finite historical, mTLS and projection callers; preserve HA's existing two-replica profile and all admission guards. Targeted post-recovery diagnostics and a fresh complete exact-source gate remain required.


Hosted644264b independently exposed remaining translation pressure: the parent parked at checkpoint6/Attempts10/translation-timeout with no plan; saved observations repeatedly reached the1.9s deadline. Fixed batches of eight now let the required five-child workload acquire its child contexts in one round after validating parent history, within the unchanged global sixteen-read SDK admission. Existing authority, complete-history validation, failure ordering, cancellation and every operational budget remain intact. Controlled5/8/9 cases14/14 plus27 intent and8 diagnostic cases pass; audited Release build has zero warnings/errors. Independent read-only review found no new issues. Profile caller checks passed28 selection,14 SDK-path,4 transport and1 profile-owner cases. A frozen private diagnostic snapshot34f542ec runs the four post-recovery live lanes with explicit incomplete qualification; unchanged full hosted canonical CI remains the release gate.


Hostedc4e273f failed before delayed-child creation: checkpoint6/Parked/Attempts10/translation-timeout, so the fixture never observed the canonical missing-child reason. Saved observations confirm deadline cancellations but do not establish when a particular404 returned. A controlled regression demonstrates that the aggregate Task.WhenAll barrier masks an already-known first-sorted MissingStream while unrelated providers remain blocked. The translator now awaits and validates each sorted result, cancels its linked batch on failure, drains contextual wrappers and rechecks caller cancellation. SDK admission remains held until actual provider termination; no partial effect or later batch escapes. The regression fills the remaining real SDK slots and proves the seventeenth provider is refused before invocation. Audited Release build and15 concurrency +27 intent +8 diagnostic cases pass; independent review found no issues. A private recovery-only diagnostic351cdae5 restricts only its own process tree to two allowed CPUs; full unchanged hosted canonical CI remains mandatory before publication.
