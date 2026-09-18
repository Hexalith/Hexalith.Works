---
title: 'Implement Works CI/CD and NuGet release verification'
type: 'feature'
created: '2026-09-18'
status: 'in-progress'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'a9f4d0a3e9a29ef0b419a9b5b10f6f2c2aff2528'
context:
  - '{project-root}/references/Hexalith.AI.Tools/hexalith-llm-instructions.md'
  - '{project-root}/references/Hexalith.Builds/.github/workflows/ci-cd-standards.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Works has no CI/release workflows, authoritative package inventory, or semantic-release tooling; Release builds still consume sibling libraries from source, one architecture test has drifted from SDK `10.0.401`, and all five intended Works package IDs currently return 404 from NuGet.org. The broad integration baseline also exposed an intermittent AppHost restart race in which the mTLS scheduler stayed unhealthy, although the focused failing method then passed.

**Approach:** Apply the common Tenants/EventStore/FrontComposer operating model: thin shared-workflow callers, package-mode Release builds, manifest-driven packing and isolated consumers, and an operator-only fail-closed release with exact-source and post-publication checks. Repair the two observed test defects and verify locally without committing, pushing, changing GitHub settings/secrets, or dispatching a release.

## Boundaries & Constraints

**Always:** Use the root-tracked Builds SHA `04d961759994396132bb2b113ee465b64740a543` as the immutable CI/release contract; keep Debug sibling-source references and Release NuGet references; run every test project separately with Microsoft.Testing.Platform; keep release manual, non-cancelling, production-protected, exact-green-`main` gated, collision-failing, and manifest/count checked; publish exactly Contracts, Server, Projections, Reactor, and Testing under one version; keep NuGet audit visible.

**Never:** Do not auto-release on push, use `--skip-duplicate`, publish hosts/samples/test runners, copy module-specific UI/container/recovery machinery, expose secrets, modify submodules, or perform commit/push/release/repository-configuration side effects. A registry check reports absence honestly; it does not authorize publication.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| CI | Push or PR to `main` | Release package-mode restore/build, package/consumer checks, and four blocking test projects | Any shard or inventory drift fails the run and uploads test evidence |
| Invalid release | Non-main/stale SHA or no exact successful push CI | Protected release job is never entered | Fail before secrets/environment approval |
| Publication collision | Any target ID/version already exists | No NuGet write occurs | Fail both verify and publish preflights; never skip duplicates |
| Partial publication | Tag exists but one of five NuGets is absent | Release verification remains red | Name every missing ID/version |
| Current registry | Five Works IDs are unregistered | Record the five 404 results | Do not synthesize success or dispatch a release |

</frozen-after-approval>

## Code Map

- `references/Hexalith.Builds/.github/workflows/{domain-ci,domain-release}.yml` -- shared execution contracts; consume at the frozen SHA, do not edit.
- `references/Hexalith.{Tenants,EventStore,FrontComposer}/.github/workflows/` and their manifests/scripts -- proven patterns; reuse the common core only.
- `Directory.Build.props`, `Directory.Solution.props`, and external-reference `.csproj` files -- add Debug/source versus Release/package selection and restore NuGet auditing; keep EventStore Operations as the source-only executable topology resource.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/{BuildConfigurationTests,DependencyDirectionTests}.cs` -- stale SDK and source-only governance assertions requiring dual-mode coverage.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` -- fixed-port restart teardown/readiness seam behind the intermittent scheduler failure.
- `.github/`, `.releaserc.json`, `package*.json`, `commitlint.config.mjs`, `scripts/`, `tools/release-packages.json` -- new module-owned CI, release, package, and governance surface.

## Tasks & Acceptance

**Execution:**
- [ ] `.github/workflows/{ci,commitlint,codeql,dependency-review,release}.yml`, `.github/dependabot.yml` -- add CI/security callers plus manual release, exact-source, and exact-package publication verification.
- [ ] `.releaserc.json`, `package.json`, `package-lock.json`, `commitlint.config.mjs` -- configure locked semantic-release/commitlint without changelog/git plugins or forbidden `chore` commits.
- [ ] `tools/release-packages.json`, `scripts/{pack-release-packages.py,validate-nuget-packages.py,validate-consumer-package-references.py,validate-release-secrets.sh,validate-publication-preflight.sh}` -- define five packages and fail-closed pack, metadata/dependency, package-only consumer, secret, source, and collision checks.
- [ ] `Directory.Build.props`, `Directory.Solution.props`, `src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj`, `src/Hexalith.Works/Hexalith.Works.csproj`, `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`, `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` -- make Debug source-based and Release package-based for published external libraries; enable all-mode audit with advisory warnings visible but non-blocking.
- [ ] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/{BuildConfigurationTests,DependencyDirectionTests,CiCdConfigurationTests}.cs` -- repair the SDK assertion and pin workflow, package inventory/count, dependency-mode, and no-skip/no-auto-release invariants.
- [ ] `WorksAppHostSmokeHarness.cs` -- wait for fixed control-plane ports at every start/teardown boundary and preserve actionable timeout diagnostics.
- [ ] `README.md` -- document local Debug, CI Release, manual release prerequisites, package inventory, and the currently unpublished status.

**Acceptance Criteria:**
- Given a clean checkout, when Release restore/build and package validation run, then only the five manifest packages are produced with correct NuGet dependencies and isolated PackageReference consumers compile.
- Given each test project is invoked separately, when the full gate runs, then Unit, Architecture, Property, and Integration suites pass, including the focused reminder restart method.
- Given workflow/static validation runs, when callers and semantic-release hooks are inspected, then actionlint passes and release requires exact green `main`, protected production, explicit `NUGET_API_KEY`, immutable Builds identity, collision checks, and five-package post-verification.
- Given NuGet.org is queried before any authorized release, when the five Works IDs remain absent, then the result is reported as five 404s rather than as published.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

Works has no container package contract, so release uses the FrontComposer-style NuGet-only path while retaining the simpler Tenants/EventStore manifest and isolated-consumer checks. The current Builds publication freeze remains fail-closed; repository owners must separately configure `production`, `NUGET_API_KEY`, and `HEXALITH_RELEASE_PUBLISH_ENABLED=true` before a later authorized dispatch.

## Verification

**Commands:**
- `actionlint .github/workflows/*.yml && npm ci --ignore-scripts && npm audit signatures` -- workflow and Node supply-chain validation passes.
- `dotnet restore Hexalith.Works.slnx && dotnet build Hexalith.Works.slnx --configuration Release --no-restore -warnaserror -m:1` -- package-mode Release gate passes with zero warnings/errors.
- `python3 scripts/pack-release-packages.py ./nupkgs 0.0.0-ci-test && python3 scripts/validate-nuget-packages.py ./nupkgs && python3 scripts/validate-consumer-package-references.py ./nupkgs` -- exact five-package contract passes.
- `for project in tests/Hexalith.Works.UnitTests tests/Hexalith.Works.ArchitectureTests tests/Hexalith.Works.PropertyTests tests/Hexalith.Works.IntegrationTests; do dotnet test "$project" --configuration Release --no-build; done` -- every project passes separately; use direct xUnit `-method` for the restart regression if broad execution is environment-blocked.
- `curl` NuGet flat-container indexes for every manifest ID -- current status is explicitly captured; after a future authorized release, every exact version must return HTTP 200.

**Observed results (2026-09-18):**
- `actionlint`, shell/Python/JSON validation, `npm ci --ignore-scripts`, `npm audit signatures`, and `git diff --check` passed. NPM installed 505 packages with zero vulnerabilities and verified 504 registry signatures plus 127 attestations.
- Release restore/build passed with zero warnings and errors. Exactly five `0.0.0-ci-test` packages passed metadata/dependency validation, and all five isolated PackageReference-only consumers built successfully.
- Unit passed 568/568, Architecture passed 246/246, and Property passed 3/3. NuGet.org returned HTTP 404 for each of the five manifest package IDs.
- Integration completed 510/516. All six live AppHost failures occurred after Dapr Sentry exited with `failed to add target /var/run/dapr/credentials: no space left on device`; the focused `WorksReminderRecoveryPipelineSmokeTests.Recovery_re_registers_a_still_future_await_that_later_fires` run failed for the same reason before the application became healthy.
- At the failure boundary, `/tmp` had zero free inodes and host inotify use was 1,048,538 of 1,048,576 watches. No unrelated processes or temporary data were modified. The full integration and focused restart acceptance gates remain environment-blocked and must pass before this spec can move to review.
