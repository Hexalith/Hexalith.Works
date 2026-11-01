---
title: Exercise the noncanonical owner denial with a valid service workload token
type: bugfix
created: '2026-11-02'
status: done
route: oneshot
baseline_commit: 114badd2f4c7b796f9441996bc7d8b99abf4c2c6
---

<frozen-after-approval reason="User authorized SDK migration and CI repairs preserving authorization and waived human validation">

## Intent

Restore the existing noncanonical owner-authority negative test's ability to reach its intended domain denial after the current SDK catalog guard. Its manually minted workload JWT omits the signed service-surface claim used by the normal workload test issuer. Supply that one missing fixture claim; preserve HTTP success plus typed safe-denial and zero authoritative-source reads.

## Boundaries & Constraints

Change only the manually signed test token in OriginalNoncanonicalOwnerAuthorityHintRemainsRefusedAfterSdkStripping. Preserve the wrong originating authority, all payload and delegating claims, cryptographic signature/issuer/audience/lifetime and every accepting/denying assertion. Do not change production authentication, catalog policy, query handlers or source authority; do not skip a test or invent a human/tenant grant.

## I/O & Edge-Case Matrix

| Case | Expected result |
|---|---|
| Valid signed service workload plus original noncanonical owner hint | Existing HTTP200 QueryResult.Success=false, safe-denial, source.ReadCount=0 |

</frozen-after-approval>

## Tasks & Acceptance

- [x] Add the same signed eventstore:surface=service claim already supplied by CreateWorkloadAssertion to this single manual workload JWT.
- [x] Run the unchanged noncanonical control and related native-authority/authentication controls against public SDK3.131.0, with no new skips or assertion changes.

Given a valid signed service-channel assertion and a noncanonical originating owner context, when the query reaches the real receiver, then the existing typed safe-denial occurs without reading the authoritative source.

## Implementation Notes

Owner is Works references/Hexalith.Tenants. Implement after the separate endpoint repair is committed so its owned diff remains isolated. Read owning guidance/build/test configuration. The exact baseline5a21dd2c test fails HTTP503 before the handler; parent proved in a separate git-archive snapshot that adding only this signed fixture claim makes the unchanged test pass1/1 against public SDK3.131.0. First two archive builds lacked inherited build imports; the third supplied existing absolute import paths and built cleanly. Original baseline and all failures are retained. Evidence: Works .artifacts/upgrade-release-orchestration-20261101/tenants-noncanonical-service-surface/.

## Verification

Run the affected built Integration assembly with single-dash selectors, serialized, against public3.131.0 and retain exact source/package provenance. Use owning pinned commitlint and normal hooks for the separate one-line test commit. Human checkpoints remain waived by the maintenance task.

## Review Triage Log

- blind-1: medium, defer. The existing test observes HTTP200/safe-denial/zero source reads without a direct handler invocation assertion; dispatcher-level binding refusal could produce the same result. This verification weakness predates the one-line fixture correction. Current SDK source verifies the original signed query, installs VerifiedQueryCaller and then invokes the owner handler; that handler explicitly denies the original administrator hint. Preserve the existing denial assertions and record a future handler-observation improvement separately.

## Final verification

The actual owner builds cleanly against public SDK3.131.0 after the endpoint repair was normally rebased onto upstream434fefb0. All21 native-authority controls pass with zero failures/skips/not-run cases; the previously failing exact test keeps every original assertion. The sole source diff adds eventstore:surface=service to its existing manually signed JWT. Parent trace confirms original-query binding and the owner handler denial. Exact commands, source SHA256, XML and isolated baseline/fix attempts are retained in Works .artifacts/upgrade-release-orchestration-20261101/tenants-noncanonical-service-surface/.
