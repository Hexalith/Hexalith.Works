---
title: 'Qualify Platform on the current public SDK cohort'
type: 'bugfix'
created: '2026-11-02'
status: 'in-progress'
route: 'oneshot'
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
