---
title: 'Qualify Platform on the current public SDK cohort'
type: 'bugfix'
created: '2026-11-02'
status: 'in-review'
route: 'dispatch'
review_loop_iteration: 0
owner_base_commit: 6064b363a8e1a3e0fdaec00443b46318abc74c75
baseline_commit: 6064b363a8e1a3e0fdaec00443b46318abc74c75
context:
  - /home/administrator/projects/hexalith/works/AGENTS.md
  - /tmp/works-platform-package-ci-workspace-20261102/references/Hexalith.Platform/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The official Builds 4.42.1 catalog selects public EventStore 3.132.1, but Platform's finite cohort gates omit that version. Its host therefore compiles a legacy security path and fails the SDK startup validator. The same exact cohort must be qualified across Composition and its test host rather than inferred from a future version range.

**Approach:** Add only exact 3.132.1 to the existing catalog and V2 catalog gates in the three owning csproj files. Preserve every older qualified cohort, source-mode behavior and future/malformed version refusal. Run the actual current public-package build and host/catalog selectors before treating this cohort as supported. Keep HTTP authentication/enrollment, artifact-source checks, native acceptance and all catalog validators intact. Correct the separately observed CA2007 in the catalog observation stream's asynchronous disposal without altering its transport, payload bounds or lifetime.

Given exact public 3.132.1, when the owner projects compile, then they use their existing qualified catalog/security paths and preserve authenticated host startup. Given an older accepted cohort, when evaluated, then its prior flags remain unchanged. Given 3.132.0, 3.132.2, a prerelease, metadata suffix, malformed version or nonzero/four-part alias, when evaluated, then these new enrollment conditions remain false. Given a catalog observation stream, when its scope exits, then asynchronous disposal completes without synchronizing on a caller context.

This unit does not change startup deadlines or the stalled-worker fixture; that independent observed race is investigated separately. No production authority, anonymous access, release approval or machine gate is relaxed.

</frozen-after-approval>

## Implementation Notes

- Authoritative failing tuple run41412824773 has a clean Release build and two seed-subscription host startup failures caused by missing UseEventStoreHttpHostSecurity enrollment. Three CATALOG_SDK and two adjacent V2_CATALOG gates omit the selected exact version. The existing modern paths already support older3.130 cohorts.
- Local source Debug build additionally reports exactly one CA2007 at PlatformRoutingCatalogMetadata.cs23, on await-using disposal; ReadAsStreamAsync itself already configures its continuation.
- Public Server API comparison3.130.2..3.132.1 reports one additive member and no removals. Actual compilation and focused behavioral checks remain required.
- Public Contracts API comparison3.130.2..3.132.1 reports19 additive members/types and no removals. The actual .NET10 runtime exposes ConfigureAwait(IAsyncDisposable,bool), so stream acquisition and configured asynchronous disposal can retain the same scope and exception behavior.
- Applied exact3.132.1 to the three CATALOG_SDK and two V2_CATALOG gates. Source-only technical-catalog flags are unchanged. The stream remains owned until method-scope exit through a configured async-disposal lifetime. No SDK range or validator was widened.
- Nonincremental standalone Release build passes with zero warnings/errors. Both previously failing configured/declarative seed-subscription startup cases pass (2/2), and contract-binding/public-metadata controls pass (24/24), with zero failed/skipped/error/not-run cases. XML retained in `/tmp/platform-sdk31321-{seed-subscriptions,catalog-controls}-20261102.xml`.
- Finite cohort validation covers36 package evaluations and3 source-mode evaluations. The first matrix run omitted the existing source EventStore resource root in its3 source cases; the corrected invocation supplies that existing root and retains all conditions.
- Replan trigger: the fresh blind review identified coupled capability gates beyond the initial three projects: CatalogBuilder still selects a legacy policy binding, the packaged host needs the published technical receiver, and the durable qualification matrix/source map omit3.132.1. Compilation plus seed/metadata selectors alone cannot establish complete cohort compatibility. Stop implementation until this code map and the complete verification scope are planned; preserve the frozen intent and all prior older/future-version boundaries.

## Code Map

Owner worktree: `/tmp/works-platform-package-ci-workspace-20261102/references/Hexalith.Platform`, detached clean baseline6064 with four existing owned cohort/disposal edits. Preserve them and complete this unit there; no original checkout edits, Git commits, remote operations or submodule initialization. Read owner `.editorconfig`, `.gitattributes`, Directory.Build/Packages files and relevant existing tests. The sibling Builds root is the existing approved root-declared checkout at2afc; public SDK producer0ace differs from newer unrelated source7c.

- `src/Hexalith.Platform.EventStoreHost/Hexalith.Platform.EventStoreHost.csproj` and `Program.cs` — CATALOG_SDK/shared security and technical receiver gates. Preserve SOURCE_CATALOG as source-only; TECHNICAL_CATALOG_SDK must include source or exact packaged3.132.1. Existing published API calls need no shape change.
- Composition and Composition.Tests csproj — existing CATALOG_SDK/V2_CATALOG gates; exact3.132.1 already added. Preserve other constants.
- `src/Hexalith.Platform.CatalogBuilder/Hexalith.Platform.CatalogBuilder.csproj`, `Program.cs` — existing legacy3.130 regex selects three-argument Bind. Add exact3.132.1 without admitting new3.132.1 aliases/suffixes; retain existing3.130 aliases byte-for-byte in behavior. Adjust supported-version diagnostic.
- `.github/workflows/catalog-builder.yml`, `tests/catalog-builder/verify_catalog_builder.py` — append exact3.132.1 finite matrix/CLI choice and immutable SAMPLE_SOURCES mapping to `0ace1545c433bf234f54cef3a57ac77d62b4175f`. Preserve old fixture default and public-only restore/source provenance.
- `eng/tuple-ci-suites.json` — add committed catalog proofs (29 current cases) and native preparation (14 current cases) to a bounded explicit group. A new positive future-deadline preparation case makes15; require exact44 total, not skips.
- `tests/Hexalith.Platform.Composition.Tests/PlatformNativeTestPreparationTests.cs` — add a positive source-validation case asserting source-validated progress before expected missing application-model boundary, without network/runtime acceptance. Existing elapsed deadline and V2 refusal controls must survive.
- `src/Hexalith.Platform.Composition/PlatformRoutingCatalogMetadata.cs` — existing configured async-disposal correction retained.
- Existing ephemeral finite matrix evidence `/tmp/platform-sdk31321-qualification-draft-20261102/finite-gate-verification.json` and `.artifacts` owner evidence can guide validation, never count as runtime acceptance. Do not apply the independent startup-clock draft in this unit.

## Tasks & Acceptance

- [x] Complete all coupled finite project/tool/technical gates and diagnostic above; preserve exact-only3.132.1 activation, older cohorts and source flags.
- [x] Add a small durable real-MSBuild finite gate verification script under `tests/catalog-builder/`, executed by the workflow once per relevant matrix cohort or otherwise bounded. Check all four projects, old accepted literals, old tool aliases, exact3.132.1, unsupported adjacent/future versions, prerelease/metadata/nonzero or four-part aliases, and explicit source mode. Source-mode flags differ legitimately by project; assert prior semantics.
- [x] Extend executable workflow, CLI/source map and exact-count tuple proof selectors; add meaningful future-deadline source-validation positive control.
- [x] Retain configured stream disposal and run complete nonincremental package Release build, seed2, existing catalog24, committed29 and preparation15 cases, and finite gate script. Run actual public3.132.1 catalog-builder verifier; retain logs and artifact identities under a new `/tmp/platform-sdk31321-expanded-verification-20261102` directory. No tests may silently skip.

Given public3.132.1 and Builds4.42.1, the actual CatalogBuilder must execute scoped consumer binding and its published activation controls successfully, with genuine public source/artifact hashes. Given source/future/legacy inputs, finite evaluation must preserve all existing behavior and exclude unqualified new aliases. Given the V2 structural unit fixtures, both valid committed proofs and native source validation must execute; refusals must remain. This is tuple regression evidence only, never candidate/native runtime acceptance.

## Verification

Build uses existing roots only: `dotnet build Hexalith.Platform.slnx --configuration Release --no-incremental -p:UseHexalithProjectReferences=false -p:HexalithBuildsRoot=/home/administrator/projects/hexalith/works/references/Hexalith.Builds -p:HexalithEventStoreRoot=/home/administrator/projects/hexalith/works/references/Hexalith.EventStore -p:HexalithBuildsPublishedToolRoot=/tmp/platform-builds4421-owner-artifact-identity-20261102/published/tools/net10.0/any -p:GeneratePackageOnBuild=false -m:1 /nr:false`. Expect zero warnings/errors. Invoke the built Composition.Tests assembly directly with xUnit single-dash selectors and XML results; no solution-level dotnet test. Run the actual verifier with `--sdk-version 3.132.1 --builds-root /home/administrator/projects/hexalith/works/references/Hexalith.Builds --evidence <unique-dir>`; inspect its argparse before invocation for further required fields. No CI environment simulation, broad analyzer suppression, live starts, fixture deadline changes, release-policy changes or evidence fabrication. Report any incomplete validation exactly.

## Review Triage Log

- Cohort blind finding1 — medium, patch in expanded unit: CatalogBuilder remains on legacy bindings/suffixed-gateway refusal for current3.132.1 despite Composition enabling V2. The owning finite gate and actual executable qualification must agree.
- Cohort blind finding2 — high, patch in expanded unit: public3.132.1 includes a technical receiver contract, but the host's source-only flag leaves a technical:false finite receiver while Composition requests V2. Verify the published API, then align the exact cohort; retain older behavior and every receiver validator.
- Cohort blind finding3 — medium, patch in expanded unit: durable CI qualification omits current3.132.1 and its committed/V2 proof controls. Extend the finite matrix, immutable public-source mapping and exact positive test profile; do not count compilation as runtime acceptance.

Parent verified the full combined owner diff, exact current cohort gates/public source mapping, 120 finite evaluations, genuine executable13cases, focused70cases and no production gate relaxation. Separate clock fixture is also in the frozen review diff; its original28 plus2 new controls are tracked by its independent spec.
