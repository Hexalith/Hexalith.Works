---
title: 'Provide explicit model custody to Works preview tests'
type: 'bugfix'
created: '2026-11-02'
status: 'ready-for-dev'
route: 'dispatch'
review_loop_iteration: 0
context:
  - /home/administrator/projects/hexalith/works/AGENTS.md
  - /tmp/works-platform-package-ci-workspace-20261102/references/Hexalith.Platform/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Works' canonical parity gate executes147 Platform Works model tests;9 fail because their fixtures omit the now-required owner-private Development catalog profile. Make those structural tests supply explicit private model-only custody/components and run the real selected Works owner preparation under the same SDK/configuration mapping as their owning process. Preserve every original assertion and public Compose invocation, mandatory real compiled declarations/adapters, SDK byte-identity checks, and production retained-state protection. Add a missing-profile negative control. Do not start live resources, reset shared Redis, suppress child preparation or substitute authority/metadata. These are model tests and grant no native/runtime acceptance. Actual R1–R11/rollback gates remain mandatory.

</frozen-after-approval>

## Code Map

Owner: `/tmp/works-platform-package-ci-workspace-20261102/references/Hexalith.Platform`. Work there only. Parent owns prior staged cohort/clock repair; preserve it and any commits made before dispatch. No commits/remotes/submodule initialization, no changes in original checkouts. Read relevant tracked build files and test/architecture guidance. Detailed read-only findings: `/tmp/works-preview-fixture-investigation-20261102/findings.md`.

- `tests/Hexalith.Platform.Works.Tests/WorksPreviewModelTests.cs` — all model setup uses private Builder helper;9 successes reach mandatory profile/owner enrollment. Its explicit SDK-root case substitutes only gateway/admin/operations metadata; the real selected Works owner must still prepare.
- New one-type test-only fixture `.cs` beside it — own one private temp profile/components/custody, builder and nonstarted application's actual lifetime disposal. Use0700directory/0600 regular nonlink profile/components outside all checkouts. Fresh32byte model-only digest key; unique authority IDs; custody under profile directory. Copy existing component YAML with only modeled Redis endpoint replaced by clearly unreachable loopback, never shared6379. Do not assert backend provisioning. SDK `tests/Hexalith.EventStore.AppHost.Tests/Configuration/DevelopmentCatalogModelCustody.cs` is established ordinary-source precedent.
- `src/Hexalith.Platform.Composition/WorksDevelopmentCatalogProfile.cs` — guard is authoritative and unchanged. `WorksPreviewModel.cs` lines311–325 performs actual selected-child build, immutable output snapshot, compiled contract/adapter and catalog enrollment; preserve it unchanged unless a concrete non-fixture bug is proven and reported first.
- `tests/Hexalith.Platform.Composition.Tests/PlatformRunIsolationReviewTests.cs` shows resolving IHostedService lifetimes on a nonstarted app before disposal; inspect to ensure prepared owner/custody/workspaces clean up even on partial failure. This unit does not broadly fix its separate fake-owner fixture.
- `eng/verify_works_host.py` consumer_build_command + platform-test run around1170: currently tests receive only Platform__Works__SourceRoot. Couple nested Works build env to the actual owning configuration, public/source mode, version, selected runtime SDK root, Builds root and same artifact root. `Configuration` alone may not override actual Aspire launch.BuildConfiguration: inspect SDK's public launch hook and exercise actual preparation; never infer parity from equal versions.
- `eng/test_verify_works_host.py` / verifier input/command tests — add meaningful regression for the coupled build/test/child mapping, not source-text assertion. Existing consumer_build_command semantics stay intact.

## Tasks & Acceptance

- [ ] Add model-only private custody fixture and use disposable fixture lifetimes across all original preview tests; preserve their source/credential/wait/sidecar assertions and existing refusal cases.
- [ ] Add missing-profile refusal proving no resources or new owned hosted lifetime before refusal. Keep owner guard and actual owner preparation intact.
- [ ] Forward exact selected build mapping to model process and nested owner using ordinary public launch/build environment; no bypass, dummy assembly, hash waiver or arbitrary new proof claim. Factor a bounded helper if needed so command/environment coupling can be exercised by regression.
- [ ] Run actual package Release Platform.Works.Tests original147 plus new control (expected148 total, no failures/skips/errors), then source Debug using same verifier mapping and exact SDK source selected by Works. Run relevant Python verifier regressions. Retain actual child-preparation receipts and SDK hash equality; do not treat full live parity as completed by model tests.

Given public3.132.1 and the selected actual Works source, all existing model assertions must execute through public Compose and genuine child preparation. Given absent owner profile, the original retained-state refusal must occur before resource/lifetime creation. Given source Debug, child and parent must consume exactly identical loaded SDK assembly bytes. Every model-only resource remains unstarted and every private fixture/profile/prepared owner has a bounded owned cleanup lifetime.

## Verification

Serialize any owner builds with parent. Package Release uses existing approved root Builds2afc and publicSDK3.132.1; actual Works root is `/home/administrator/projects/hexalith/works` and SDK source root `/home/administrator/projects/hexalith/works/references/Hexalith.EventStore` (currently7c93a1234d14c66f5ee85ed48caaee1d30a671f3, public producer remains0ace). Use unique owner-contained artifacts directory for tests' checkout discovery and pass the same artifact root into actual nested owner build. Do not fake CI flags or run full live verifier locally. Use built assembly xUnit single-dash selectors and XML, then complete148 cases. For source Debug actual Builds.Tooling references may need existing root-declared preparation roots; no nested init or source stubs. Report any environmental or deeper owner qualification failure exactly instead of weakening it.

## Implementation Notes

