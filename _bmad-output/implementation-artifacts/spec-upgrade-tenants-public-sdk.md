---
title: Adopt the verified current public SDK in Tenants
type: chore
created: '2026-11-01'
status: superseded
route: dispatch
superseded_by: spec-qualify-tenants-public-sdk-3-132.md
review_loop_iteration: 0
baseline_commit: 86244ede82095a0d07c8715dee537bd97cbef0e8
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.Tenants/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-state-instructions.md
---

<frozen-after-approval reason="User authorized latest SDK/packages and CI repair and waived human validation">

## Intent

Tenants still defaults to an older public EventStore SDK and its finite package-consumer verification cannot admit the current verified stable cohort. Adopt the verified current public SDK while preserving authorization, compatibility selections, truthful package ownership and qualification boundaries.

## Boundaries & Constraints

Always preserve explicit legacy/source selections, all existing denial and persistence assertions, finite exact stable-version acceptance and real package-only Release validation. Existing unknown-version, prerelease, nonzero revision and mixed-cohort refusals remain. Keep source-only runtime owner-profile enrollment distinct from published capability declarations; accepting a package version does not establish native/runtime qualification.

Never initialize/update nested submodules or their recorded gitlinks, publish, mutate Git/remotes, alter historical evidence, fabricate runtime/catalog authority, add fictional operations, change bootstrap behavior, relax cold-bootstrap checks, suppress tests or alter timeouts/retries. Parent handles dependency-pointer authorization, Git, hosted gates and release separately.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Ordinary default | No explicit SDK override | Current verified public stable cohort | Compile and discover real package declarations |
| Explicit compatibility | Existing supported legacy or source selection | Existing selected behavior | Keep publication restrictions |
| Exact supported new stable | Current version, zero fourth revision or valid build metadata | Same finite stable identity | No generic open-ended acceptance |
| Unsupported shape | Prerelease, nonzero revision, unknown version | Existing refusal | Never mark qualified |
| Packed consumer | Actual Contracts/Server archives at one producer and SDK cohort | Restore/discover approved real declarations | Refuse mixed source/version/missing dependency |

</frozen-after-approval>

## Code Map

Owner `/home/administrator/projects/hexalith/works/references/Hexalith.Tenants`, clean main baseline in frontmatter. Read tracked build/editor/test/architecture guidance. Works root declares AI.Tools for the permitted baseline; never load skills under references.

Target public EventStore3.131.0, producer7776e64fdb5b9b86d0d180a3a8f47c1f11379466. Its14 archives passed source/signature verification in Works `.artifacts/upgrade-release-orchestration-20261101/sdk-3.131.0-public/verification.json`. Do not consume the unpublished terminal-refusal patch.

Source gate41336228705 remains separate: SDK6e9f2fc/3.130.2 with compatibility fixture3.128.4 reached bound bootstrap command/actor processing, then returned400. NewSDK alone is not a demonstrated fix. Existing declarations/adapters are genuine. Do not edit bootstrap services, SourceCatalogBootstrapEvidence or source-owner assertions. Parent retains native diagnosis/qualification and nested-pointer permission.

## Tasks & Acceptance

- [x] `Directory.Build.props`, `Directory.Build.targets` — advance default3.129.0 to3.131.0, finite normalized forms and diagnostics; preserve explicit/source overrides and all publication restrictions.
- [x] `.github/workflows/ci.yml` — advance only ordinary generated-controller package qualification from3.129.0.
- [x] `tests/Hexalith.Tenants.Contracts.Tests/PackageGovernanceTests.cs` — update default/new stable cases; retain legacy and negative controls.
- [x] `scripts/verify-catalog-package-consumer.py`, `tests/scripts/test_catalog_package_consumer.py` — extend existing3.129.0/1 finite cohort check with3.131.0; preserve producer, lockstep and package-source isolation, with accepted/mixed/unsupported tests.
- [x] `docs/development-route-catalog.md` — append measured package evidence and limits; preserve dated records.

Given the verified public cohort, when a normal consumer builds, then genuine current APIs/declarations compile and approved operations remain unchanged. Given explicit compatibility or unsupported input, when governance runs, then its original selection/refusal remains. Given packed mixed inputs, when the isolated consumer verifies, then it rejects them rather than accepting synthetic qualification.

## Implementation Notes

Use serialized Release/public-package inputs: UseHexalithProjectReferences=false, HexalithEventStoreFromSource=false. Notify parent before substantial compilation. No native/AppHost/container starts, Git/remotes, or nested pointers/checkouts. Preserve failed attempts and exact case counts. Return complete owned patch/hashes and receipts. Parent owns broader checks, reviews, Git and hosted qualification.

## Spec Change Log

## Review Triage Log

## Verification

Run focused Python package-consumer/provisioning tests and built PackageGovernanceTests. Build affected Contracts/Server/integration projects serially against actual3.131.0; run generated HTTP controls selected by existing CI. Pack actual Contracts+Server archives in a new private output and validate their exact producer/SDK cohort with the isolated package consumer. Record native source-profile and official release gates as pending.

## Implementation Evidence

The seven-file candidate patch has SHA-256 `d28a09785b4578d497e211e324f6efef4f785bdf2b44d487102d3783832eefda`. All affected Release builds were clean; 358 governance cases, 66 generated HTTP cases and 12 Python tests passed with zero skips. Actual packed-library discovery retained 22 operations/routes and 12 adapters. Public3.131.0 archive hashes match the verified producer. Exact receipts and case XMLs are under `.artifacts/upgrade-release-orchestration-20261101/tenants-public-sdk-3.131.0-20261101T204440Z/`. No native/source or official publication credit is inferred.

## Upstream Reconciliation

Upstream331c528219e921e990402a6134fed9bfc50f0441 independently supplies the same default/finite3.131.0 migration plus a stronger package CI matrix and bootstrap fixes. The seven-file local candidate is preserved in its private artifact and stash4442ac658986e66b67ba360a8a65769473c68e1e; it was not reapplied over those upstream changes. No reviewers were launched for the obsolete frozen candidate. Owner checkout is now clean at331c5282. Fresh hosted CI still fails because the Aspire validator misclassifies the public EventStore.ServiceDefaults dependency; source owner qualification separately times out before HTTP202 despite eventseq1 and completed projection. No completion or publication qualification is claimed.

## Current public gateway fixture follow-up — 2026-11-02

The API transport repair found14 original command-runtime failures with public3.131.0, independently reproduced with identical names/failure messages on untouched ca93fe19847d3535353c8b4850497350aaba47d3 and identical SDK DLLs. Evidence: /tmp/tenants-gateway-transport-20261102/verification.json. Retest these against the next verified public SDK before final migration acceptance; never grant routes or weaken guards merely to preserve malformed fixtures.

Read-only attribution: twelve successes/rejection-shape cases use CreateBootstrapRequest, whose helper sets domain `tenants` for BootstrapGlobalAdmin, while the real declaration belongs to `global-administrators`. The real committed resolver correctly finds no such route and returns503 before the fake router. These cases intend to exercise JWT subject/tenant/permission and error mapping; investigate a truthful declared public command fixture preserving those assertions. Two AuthenticatedBootstrapCannotAssignDifferentTarget cases use the correct domain but a signed `ui` surface, while the owner declaration explicitly makes bootstrap internal;403 precedes domain rejection. Preserve their trusted-subject/no-GlobalAdministratorSet business assertions through the actual approved internal/delegated bootstrap transport rather than granting public bootstrap ingress. No source, test expectation, catalog or authentication changes have been made for these fourteen cases.

## Cohort succession — 2026-11-02

The earlier cohort plan is superseded by the independently reviewed3.132 migration in `spec-qualify-tenants-public-sdk-3-132.md` and the subsequent mechanical3.132.1 adoption. Frozen intent above is unchanged. Current source commits5da5670e andf1e356f6 preserve authorization/legacy selections, repair the truthful gateway fixtures and qualify signed public0ace/3.132.1 packages. Current local qualification passes977 cases,16 normal CI selector cases and8 Python guards; real package discovery is22routes/22operations/12adapters. Exact committed native CI, approved dependency recording and publication remain orchestration gates. Earlier failed probes and dated evidence retain their original scope.
