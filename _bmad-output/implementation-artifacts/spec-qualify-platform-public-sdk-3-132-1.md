---
title: 'Qualify Platform on the current public SDK cohort'
type: 'bugfix'
created: '2026-11-02'
status: 'draft'
route: 'dispatch'
review_loop_iteration: 0
owner_base_commit: 6064b363a8e1a3e0fdaec00443b46318abc74c75
context: []
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

- `src/Hexalith.Platform.EventStoreHost/Hexalith.Platform.EventStoreHost.csproj` and `Program.cs` — finite shared-security and technical-receiver enrollment; SDK3.132.1 must use APIs verified from the actual public archive. Preserve startup validators.
- `src/Hexalith.Platform.Composition/Hexalith.Platform.Composition.csproj`, tests Composition csproj — same exact catalog/V2 cohort selection; retain older/source modes and unsupported-version refusal.
- CatalogBuilder project/entry point and existing catalog-builder verifier/workflow — investigate the finite capability selection and public producer map before extending3.132.1.
- Existing committed-catalog and V2 preparation positive controls — qualify the newly selected runtime paths in CI, preserving declared positive counts and immutable public provenance.
- `src/Hexalith.Platform.Composition/PlatformRoutingCatalogMetadata.cs` — the separately observed asynchronous-disposal analyzer correction; no transport, payload, credentials or proof semantics change.

## Review Triage Log

- Cohort blind finding1 — medium, patch in expanded unit: CatalogBuilder remains on legacy bindings/suffixed-gateway refusal for current3.132.1 despite Composition enabling V2. The owning finite gate and actual executable qualification must agree.
- Cohort blind finding2 — high, patch in expanded unit: public3.132.1 includes a technical receiver contract, but the host's source-only flag leaves a technical:false finite receiver while Composition requests V2. Verify the published API, then align the exact cohort; retain older behavior and every receiver validator.
- Cohort blind finding3 — medium, patch in expanded unit: durable CI qualification omits current3.132.1 and its committed/V2 proof controls. Extend the finite matrix, immutable public-source mapping and exact positive test profile; do not count compilation as runtime acceptance.
