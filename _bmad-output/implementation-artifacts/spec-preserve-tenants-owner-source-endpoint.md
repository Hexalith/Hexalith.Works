---
title: Preserve the catalog-selected endpoint for tenant authority reads
type: bugfix
created: '2026-11-02'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 5a21dd2c4f0a96cd3cfafe418c61e1e197c99c42
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.Tenants/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="User authorized latest SDK migration and CI repairs preserving authorization, and waived human validation">

## Intent

The real source-owner bootstrap now gets HTTP202, but current-administrator verification fails before any owner-source response. TenantOwnerSourceGateway's production registration rewrites localhost to127.0.0.1 while committed SDK coordinates retain localhost; the SDK correctly refuses that different endpoint. Preserve the actual catalog-selected endpoint and exercise the existing v2 endpoint fence in production-client composition tests.

## Boundaries & Constraints

Always retain exact scheme/hostname/port binding to the current independently selected SDK runtime coordinates. Use the same effective configuration precedence as SDK coordinate capture, preserving an explicitly selected localhost or127.0.0.1 without substitution. Keep per-send Dapr token lookup, eventstore target selection, workload assertion, bound RSA read proof, root/epoch/activation binding, native source proof and current membership/revocation checks unchanged.

Never weaken or normalize away the SDK endpoint fence, trust wire administrator flags/JWT bits alone, fabricate native source proof, claim native support from unit doubles, change authorities/verifiers/read models/projection behavior or deadlines, skip failures, initialize/update peers, or change unrelated transport behavior. Retain all genuine bootstrap receipt/terminal sequence/current projection/cold-backend and worker-profile gates.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Explicit endpoint | Catalog-selected localhost or127.0.0.1 | Owner request preserves exact selected host/scheme/port | Matching bound invocation reaches transport |
| Configuration precedence | Effective configuration differs from raw environment | Owner and captured SDK endpoint agree | No stale independently reconstructed endpoint |
| Port fallback | Only selected Dapr port/default available | Existing SDK fallback/capture semantics | No invented alias or port |
| Mismatch | Different host or port than independently bound coordinates | SDK refuses before terminal transport | No source proof or authority inferred |
| Source authorization | Valid or invalid existing read proof/current membership | Existing scope/token/proof and revocation behavior | Existing fail-closed unavailability |

</frozen-after-approval>

## Code Map

Owner `/home/administrator/projects/hexalith/works/references/Hexalith.Tenants`, clean main at baseline. Its prior reviewed bounded-bootstrap transport is committed; preserve it. Read tracked build/editorconfig/gitattributes, architecture/source-authority docs and relevant tests before edits. Never load skills under references. Parent owns Git, review, release and fresh native evidence.

- `src/Hexalith.Tenants/Program.cs:65–74`: ownerDaprEndpoint currently reads raw environment and applies Replace(localhost,127.0.0.1) before AddHttpClient<TenantOwnerSourceGateway> and AddEventStoreDaprServiceInvocation. Correct only this registration; extract one internal focused registration helper if needed so tests invoke the actual production composition. Prefer the existing SDK coordinate/configuration seam to a second divergent endpoint implementation. No new public API.
- `Authorization/TenantOwnerSourceGateway.cs`: preserve all allowed tenant/domain/aggregate checks, short-lived RSA-bound read proof, real workload assertion and response identity checks; this gateway's relative request relies on the configured base URI.
- Read-only SDK `/tmp/works-sdk-compat-qualification-20261101/src/Hexalith.EventStore.ServiceDefaults/Routing/RouteCatalogStateCoordinates.cs:18–23`: effective IConfiguration endpoint/port, URI validation and coordinate capture. `RouteCatalogInvocationHandler.OwnedEndpoint` requires matching IdnHost/scheme/port when TechnicalHttpReceivers.Count>0. These definitions also exist in selected public3.131.0 and actual sourcefa978a28. Do not edit SDK.
- `tests/Hexalith.Tenants.IntegrationTests/TenantOwnerSourceGatewayTests.cs`: current test manually duplicates production registration, supplies an incomplete snapshot without technical receivers/independent coordinates, and line87 codifies the wrong rewritten host. Replace that obsolete URI expectation to assert exact configured host as required by this fix, retaining all request/proof/header assertions. Use real production registration and an independently selected v2-bound snapshot so the endpoint guard is active. Assert that it is active; a snapshot with no technical receiver inventory is insufficient. Use published SDK declarations/capture helpers where available. Clearly scoped authority doubles may exercise isolated guard controls; no native proof/qualification claim follows.
- Existing global-administrator authority, metadata, source-query and unavailable/revocation tests are compatibility controls. Do not alter their accepting/denying assertions. SourceCatalogBootstrapEvidence/AspireTopologyBootstrap current receipt/status/authority sequence remains unchanged.

Evidence: real run41358425596/job125003396221 at consumer5a21dd2c and SDKfa978a28 records genuine hosted event2001 accepted. All19 owner-read starts then fail RouteCatalogUnavailableException with 'The v2 invocation uses a different Dapr runtime endpoint'; request host127.0.0.1 versus authority-state hostlocalhost, same scheme/port, zero owner-read response headers. NativeSourceProof/event replay have not yet been reached. WireAdminAssertionRemoved is deliberate canonical-query behavior, not the diagnosed cause. Parent safe summary is Works `.artifacts/upgrade-release-orchestration-20261101/tenants-5a-native-source-failure/summary.json`.

## Tasks & Acceptance

- [x] Correct and, if necessary, isolate the actual owner-source client registration, retaining every authentication/header/cancellation policy.
- [x] Verify explicit hostname and effective-configuration precedence plus fallback, using the real production registration with active v2 endpoint binding and paired mismatch controls.
- [x] Retain native read proof/current membership/revocation/unavailable-source tests and record exact changed files/hashes, commands, counts and failures.

Given an effective configured endpoint and independently captured matching SDK coordinates, when the real owner client reads, then it uses that exact endpoint and reaches the original authorized transport. Given mismatched coordinates, then the unmodified SDK fence rejects before transport. Parent must separately rerun the unchanged native cold bootstrap and persisted/current-authority gates; a later blocker must be reported rather than bypassed.

## Implementation Notes

No Git mutations/remotes, native/container starts, peer changes, broad multi-project runs or unpublished APIs. Notify parent before substantial compilation. Finite serialized Release/public-package3.131.0 verification is sufficient; use explicit workspace HexalithBuildsRoot and UseHexalithProjectReferences=false where needed. Tests run individually through built xUnit single-dash selectors. No source-authority grant or timeout workaround is authorized.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Route and evidence |
|---|---|---|
| blind-1 | high | patch: Directory.Build.targets enables HEXALITH_DEVELOPMENT_CATALOG_SOURCE for the explicit3.129.0 floor. Published3.129.0 RouteCatalogHostOptions inspection lacks RequireTechnicalHttpBinding, and the3.129/3.130 Contracts API lacks technical receiver inventory. Current production Capture exists on3.129; the defect is new test capability selection. |
| blind-2 | medium | patch: The old case resolved the client through WebApplicationFactory; all replacement cases call the helper directly. Removing its Program call would now leave these new tests green. A focused production-host smoke test restores that regression detection without changing runtime policy. |
| blind-3 | low | reject-low: The difference is real for explicit3.128.4 compatibility, but the original Program used the same unrestricted new Uri and no catalog coordinate API. Current supported catalog profiles use SDK Capture. Invalid user-configured endpoints are uncommon, and fixing legacy validation needs added guard logic rather than a direct correction. |
| blind-4 | low | patch: Current URI normalization is SDK-owned and unchanged; the new tests do not directly lock equivalent hostname casing/default-port spellings across independent client and authority captures. Adding paired positive input rows is a direct test correction with no new production guard. |
| blind-5 | false | reject: CreateBoundSnapshot calls the real SDK RouteCatalogStateCoordinates.Capture on an independent IConfiguration; the production helper calls the same API with effective IConfiguration and configured options. Capture derives HttpEndpoint solely from endpoint/port configuration; its options only choose StateStore/StateKey. The allowed isolated authority double therefore exercises the real endpoint comparison, and no distinct production disagreement caused by nondefault catalog options is shown. |
| verification-1 | medium | patch: Pre-verified reviewer traced source-reference.yml selections and the public matrix generated-controller-only selector. The only full integration suite first selects3.128.4, excluding catalog-enabled controls. Wire the existing endpoint class into source and both package-matrix variants while preserving every current selection. |


## Verification

Build/run the affected Integration test project and focused source-client/authority/bootstrap controls serially. Ensure every matrix control actually executes with no new skips; a zero-selected run is not evidence. Return complete owned diff including new helper files and precise provenance/verification receipts. Parent handles broader checks, independent review and fresh native CI before release acceptance.

### Parent implementation verification

Parent read the complete three-file diff and verified all current source hashes against the implementation receipt. All five matrix rows execute passing focused controls. Public SDK 3.131.0 builds are clean; 18 endpoint, 72 bootstrap, 147 Server authority/proof/query/bootstrap and 20 native-authority controls pass without skips. One unchanged native-authority test fails identically on the exact baseline with receiver/catalog-disagreement HTTP503; matched public package and copied runtime DLL hashes establish that it predates this repair. Its assertion remains unchanged and the overall maintenance task still includes resolving relevant remaining CI failures. No native qualification is claimed. Full evidence is in Works `.artifacts/tenants-owner-source-endpoint-20261102/review1/`.

### Parent final patch verification

All three reviewers returned before individual triage of six findings. The same implementation owner patched supported-floor API selection, actual Program coverage, equivalent URI controls and CI execution. Parent read the complete patch delta, verified its file hashes, rebuilt against strictly public SDK3.131.0 and independently passed25 endpoint,72 bootstrap and147 authority/proof/query controls (244 passed, zero skips). The worker also built and passed21 endpoint cases on explicit floor3.129.0. No fresh review loop was run for these patches. Fresh native evidence remains separately required; the unchanged baseline noncanonical-token fixture has an isolated passing one-line correction planned as a distinct repair.
