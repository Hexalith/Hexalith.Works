---
title: 'Restore Works parity with committed route-catalog admission'
type: 'feature'
created: '2026-10-31'
status: 'in-progress'
route: 'dispatch'
baseline_commit: '038f49080a86d312b30d12567f521c3954593569'
review_loop_iteration: 0
context:
  - '_bmad-output/implementation-artifacts/story-4-9-route-catalog-design.md'
---

<frozen-after-approval reason="human-approved prerequisite delta; user approved implementation on 2026-10-31">

## Intent

**Problem:** SDK 3.129.0 enforces committed route catalogs, but Works lacks enrollment and its operational request is unsupported. The newer SDK cohort helpers are unpublished, and Platform cannot qualify the required package/Release CI path.

**Approach:** Implement the approved SDK, Platform and Works prerequisites, preserve existing behavior through genuine catalog admission, then resume Story 4.9's full parity and rollback qualification. The user approved the documented prerequisite delta by answering "yes" to implementation.

## Boundaries & Constraints

**Always:** Keep generic admission, testing, activation and recovery mechanics in EventStore/Platform. Preserve endpoint-bound host identity, existing request/status/cancellation semantics, durable audit before disposition, all existing admitted callers and command payloads. Observe actual endpoints and each host independently. Use source Debug locally and published-package Release for CI, retaining only previously accepted source exceptions. Preserve both historical archive hashes and bind every current/retained producer explicitly. Preserve all concurrent user work.

**Never:** Replace guards with permissive/null implementations, discard leases or authority state, infer readiness from files, manufacture source/package equivalence, add agent eligibility, change durable discriminators, restore Works-owned hosting, or claim live/full/CI success from fixture checks. No production preview admission is authorized.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
| --- | --- | --- | --- |
| Admitted call | Actual committed authority, correct generation, signed service/UI surface | Existing operation executes; cancellation reaches its handler | Existing failure/status semantics |
| Refused call | Missing/forged identity, root, epoch, target or surface | Refusal before effects or disclosure | Existing authentication/catalog refusals |
| Disposition | Valid/malformed/operator-denied/audit-unavailable request | Preserve 204/202, 400, 403 and 503 respectively; durable audit precedes mutation | Preserve separately proven injected-provider 500 |
| Restart/HA | Changed coordinates or two HostIds sharing AppId | Fenced successor or exact resume; independent readiness; one logical effect | Outstanding/unproved leases remain fenced |
| Catalog drift | Changed executable, component, backend, grant or key material | Refuse stale/ambiguous enrollment | Fail closed; preserve predecessor evidence |
| Rollback | Complete captured fleet and unchanged archive | Exact restore; retained query/receipt replay; no false new-generation admission | Reject partial inventory or unbound producer |

</frozen-after-approval>

## Code Map

- EventStore `src/Hexalith.EventStore.DomainService/DomainRouteCatalogGuard.cs`, `EventStoreDomainServiceSecurityExtensions.cs`, `DevelopmentDomainRouteCatalog.cs`, and actual endpoint observation/finite-receiver helpers: add a typed, immutable process-source operation binding and preserve ordinary operation policies.
- EventStore `src/Hexalith.EventStore.{Aspire,ServiceDefaults,Contracts,Client,Server}/` routing helpers: reuse existing real cohort activation; support restart, independently observed replicas, stable digest-key generations and exact predecessor/lease fencing. Add a public activated fixture in the existing Testing libraries.
- Platform `src/Hexalith.Platform.Composition/{WorksPreviewModel,PlatformRoutingCatalogBootstrap,PlatformCatalogContractBinding}.cs`: reuse SDK enrollment with explicit owner/adapter bindings and source/package selection; retain exact resources, finite transport grants and caller closure.
- Works `src/Hexalith.Works.Contracts/Routing/`, aggregate/query/projection registrations, runtime adapters and `Recovery/WorksProcessDispositionEndpoints.cs`: declare existing operations and consume the SDK seam. Tests `WorksSdkProtectedHttpTests`, `WorksSdkEndpointCancellationTests`, `WorksPlatform*`, `WorksAppHost*` must use genuine readback and signed surfaces.
- Platform `eng/{verify_works_host,works_host_snapshot,retained_works_host}.py`, Works fleet capture/restore and CI bootstrap: bind catalog state/files/keys/cohort before cleanup and every runtime producer. `eng/builds-tuple.json` requires actual qualified publication data.

## Tasks & Acceptance

**Execution:**
- [ ] EventStore routing and Testing libraries/tests — implement typed operational admission and a genuinely activated public fixture; keep production guards installed.
- [ ] EventStore activation/topology/Aspire helpers and owner tests — implement exact resume, fenced forward recovery, replica attestations and stable key material without weakening coordinate or quiescence checks.
- [ ] Works Contracts/runtime/query/projection/adapters/tests — enroll all existing operations, preserve metadata/wire behavior, and restore HTTP/cancellation checks.
- [ ] Platform composition/tests and Works proof harness — enroll the complete actual fleet in source and package modes with exact finite grants and independent HA identity.
- [ ] Platform verifier/retained-host helpers and Works snapshot tests — preserve complete restore and historical archive/producer binding, including catalog leases and private artifacts.
- [ ] Owning SDK/tooling publication inputs and consumer pins — prepare reviewed, tested artifacts and obtain genuine public producer closure before Release/package qualification.
- [ ] Ordinary source checks, fresh canonical full then actual CI — pass every frozen Story 4.9 matrix row, R1–R11/44 proofs, complete rollback and final input/artifact closure.

**Acceptance Criteria:**
- Given the existing Works caller inventory, when the enrolled operations run, then their authorization, status, persisted-state and cancellation outcomes remain intact.
- Given each selected configuration, when qualification runs, then source Debug and published-package Release exercise the same declared operations without an added source exception.
- Given final inputs, when full succeeds and actual CI follows, then all ordinary tests and required live facts pass with exact source/runtime/fixture/archive/launcher bindings.

## Implementation Notes

The approved plan is the existing `producer-platform-prerequisites-2026-10-31/PREREQUISITE-DELTA.md`; that immutable packet retains its historical approval-pending wording. Work runs first in the fixed workspace recorded by `/tmp/story49-routing-20261031-dbrb5luk/capture.json`, SHA256 `5e05dfc2cf9bf83d1d06724f23d0d18ea9fd2db30390086ea72a572e9521e2dd`. Original Story 4.9's frozen block and baseline remain unchanged. Apply reviewed owning patches back by merging against current bytes; never overwrite concurrent changes.

## Spec Change Log

## Review Triage Log

## Verification

Run owning routing/activation/topology/fixture tests first, then Platform composition and source/package checks, then focused Works protected HTTP/cancellation and fleet restore. Build the Debug solution and run each test project individually. Use built xUnit v3 executables with single-dash filters. After genuine producer publication, run fresh canonical full, actual `scripts/verify-platform-parity-ci.sh`, and independent proof/matrix/closure audits. Keep failed attempts and public qualification limits explicit.
