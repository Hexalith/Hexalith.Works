---
title: Bound tenant bootstrap command transport to one application attempt
type: bugfix
created: '2026-11-01'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 7c7fe7f47d7970607e37ca5dfb741a3950d10753
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.Tenants/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="User authorized CI repairs preserving authorization and waived human validation checkpoints">

## Intent

The real cold source bootstrap commits its command but never receives HTTP202: general HTTP10-second attempt retries and Dapr5-second invocation retries repeatedly cancel a legitimate11–14-second inline projection. Preserve the current30-second overall command transport budget while allowing one application submission to use it; keep genuine HTTP receipt, terminal status and authoritative persisted/projected membership gates.

## Boundaries & Constraints

Always use the existing Dapr channel, delegated administrator credential, signed workload/tenant/catalog bindings and buffered identical request for existing Dapr-side retries. Keep one application-level bootstrap attempt, explicit finite30-second send timeout and caller/application-stopping cancellation. Preserve existing catalog-readiness timing independently: its100-second fallback is not a command timeout. Keep ordinary default-client resilience and credential-client redirect behavior unchanged. Apply Dapr30-second timeout only to the eventstore app target in both current templates; retain Dapr retry counts, other app/state/pubsub timeouts, circuit breakers and no-read-retry policy.

Never increase the existing30-second total send budget, bypass Dapr/authentication/catalog guards, infer a receipt from persisted effects, weaken any native assertion, skip failures, change projection/orchestrator/outbox behavior, alter worker-enabled or worker-disabled profiles, introduce detached work, edit SDK or peer pointers, or initialize/update nested dependencies. Removing resilience handlers must not leave HttpClient.Timeout infinite.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Legitimate slow command | Reply slower than inherited attempt limits but within total budget | One application send, actual reply/receipt | Finite30-second client budget |
| Transient failure | Single503 or transport exception | No automatic application replay | Existing bootstrap failure posture |
| Deadline/cancellation | Held send or caller cancellation | Cancel actual send; no accepted receipt | Scope/request disposed |
| Other HTTP clients | General defaults and credential request | Existing resilience and no-redirect credential policy | No policy leakage |
| Dapr deployment | Current AppHost and deployment templates | eventstore invocation budget30s; other policies unchanged | Existing parser/component checks |
| Cold native acceptance | Actual fresh backend and bound owner profile | Genuine202, terminal sequence, persisted/current projected administrator | No fabricated acceptance |

</frozen-after-approval>

## Code Map

Owner `/home/administrator/projects/hexalith/works/references/Hexalith.Tenants`, clean main at baseline. Read tracked configuration, editorconfig/gitattributes, architecture/bootstrap docs and relevant tests before changes. No skills under references. The workspace baseline is permitted through Works root-declared AI.Tools; do not initialize peers. Parent owns Git/remotes, review and native CI.

- `src/Hexalith.Tenants/Bootstrap/TenantBootstrapServiceCollectionExtensions.cs`: existing credential-only named client and hosted registration. Add one internal command-client name and a named client that removes inherited application resilience and explicitly sets finite30-second HttpClient.Timeout. Preserve global default and credential registrations.
- `.../TenantBootstrapHostedService.cs`: current unnamed client at104 supplies readiness budget to TenantBootstrapBoundCommand.CreateAsync at133. Retain that existing readiness source; actual SendAsync at148 must use the separately named command transport. Dispose client wrappers correctly; no credential/command/header/body/receipt changes and no new failure acceptance.
- `.../TenantBootstrapBoundCommand.cs`: existing signed buffered request intentionally supports Dapr retries. Preserve it and its catalog-readiness fallback/deadline. Existing BoundCommandTests assert unnamed ServiceDefaults client timeout is infinite; do not rewrite that assertion to match the new named client.
- `src/Hexalith.Tenants.AppHost/DaprComponents/resiliency.yaml` and `deploy/dapr/resiliency.yaml`: add a named30s command invocation duration and bind only apps.eventstore.timeout to it. Keep named daprSidecar5s for all other existing targets. Do not change retry configuration, workers or source-generation recipes.
- `tests/Hexalith.Tenants.Server.Tests/Bootstrap/`: existing HostedService, BoundCommand, Receipt and Redirect tests are compatibility controls. Add real factory-composition tests with actual ServiceDefaults plus bootstrap registration, including both registration orders. Use an instrumented inner handler and shortened test-only general budgets to observe that the command client does not inherit short retries; assert default30s and test actual cancellation without a30-second wall-clock sleep by using a smaller explicit test client timeout. Keep every original meaningful assertion.
- AppHost `DaprComponentOverrideTests`/`LocalDaprIdentityTests` and architecture checks provide current policy/template assertions. Add exact30s eventstore binding and unchanged5s state/query/no-read-retry controls without mirroring implementation.

Evidence: Works `.artifacts/upgrade-release-orchestration-20261101/tenants-7c-native-source-failure/stages.json` records real run41353744654/job124989259788. Cached actor RPCs421–613ms and lease releases7–12ms complete; inline ProjectionDelivery11114–13612ms completes after request cancellation. SourceCatalogOwnerProfileTests uses worker-enabled AspireTopologyFixture; a separate authenticated profile intentionally disables workers. Removing inline projection is not safe: precommit activation can be completed before command commit. Standard resilience also configures HttpClient.Timeout=Infinite; handler removal alone leaves it infinite. Its original effective total budget is30s.

## Tasks & Acceptance

- [x] Add bounded one-application-attempt bootstrap command transport while preserving catalog-readiness and credential policy.
- [x] Align only eventstore Dapr invocation timeout with that existing30-second total budget in both templates.
- [x] Verify all matrix controls, existing bootstrap/authentication tests and relevant Dapr/architecture controls; retain exact commands, failures, counts/skips and file hashes.

Given a valid command whose projection takes more than a nested default attempt limit but less than30 seconds, when bootstrap sends it, then application replay cannot cancel it prematurely and only an actual accepted response records an accepted receipt. Given timeout, denial or cancellation, then no success is inferred from committed effects. Parent must still obtain fresh native cold-bootstrap and authenticated-profile evidence.

## Implementation Notes

No Git mutations/remotes, native/container starts, peer changes or broad multi-project builds. Notify parent before compilation. Use finite serialized Release/package verification with UseHexalithProjectReferences=false and explicit HexalithBuildsRoot=/home/administrator/projects/hexalith/works/references/Hexalith.Builds where needed; current public SDK3.131.0 is sufficient. Test projects individually with supported built-assembly single-dash selectors. No claim of native qualification from local factory tests.

## Spec Change Log

## Review Triage Log

## Verification

Build/run the narrow Server bootstrap and AppHost configuration projects serially, retaining original suites and no skipped new cases. Return complete owned diff/manifests and exact receipts. Source/native hosted acceptance remains parent-owned and must use the unchanged fresh-backend, real202, terminal status and authoritative membership gates.


## Parent Acceptance Audit Before Review

Parent read the complete32,395-byte9-file owned diff, checked every current source SHA256 against manifest.json, and verified403Server plus42AppHost passing cases with zero skips, including20new controls. Both affected Release/package builds have zero warnings/errors. Factory tests demonstrate actual signed requests, finite named timeout, no application replay, unchanged general retry policy, both registration orders, cancellation and honest receipt phases. Existing cold native/profile tests and every202/persistence/projection assertion are unchanged; their fresh hosted run remains an explicit parent release gate, not local qualification.


## Review1 Individual Triage

All three fresh layers returned final before triage. Verification-gap layer found none. This is a patch-only route; no public surface or new state guard is introduced.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | medium | patch | The new named command client retains primary-handler redirects. A307/308 can resend a signed POST and forward custom assertion/proof headers, contrary to one application submission. Disable command redirects with a real loopback no-forwarding control; retain ordinary and credential policies. |
| blind-2 | false | reject | Equal30s values do not promise a sidecar timeout response or completed retries. The client has an explicit finite30s limit/caller cancellation, and hosted deadline/exception controls retain attempted receipt with no accepted status; Dapr replay preserves the same signed identity. Unknown remote completion remains failure, as intended. No evidence demonstrates a new incorrect acceptance or an unbounded caller. |
| blind-3 | low | patch | Dapr targets apps.eventstore as a whole, so the new commandInvocation label can misleadingly suggest a method-only policy. The plan intentionally changes this app target, retaining caller-specific budgets and all other targets. Correct the label/comment to describe all EventStore invocations; no new routing or deadline logic. |
| blind-4 | medium | defer | ResponseContentRead buffering before the bounded rejection reader is real and predates this change: prior unnamed HttpClient.SendAsync also buffers the entire body. The new finite30s client improves time bounding but not the existing memory bound. Separate response-consumption repair should preserve signed receipts and distinguish headers/body completion. |
| blind-5 | low | patch | All new real hosted factory scenarios select bound workload mode, while ordinary /api/v1/commands also switches clients. Existing mocked coverage proves selection but not composed ordinary failure/cancellation. Add real factory ordinary accepted, failure and cancellation controls without changing assertions or authority. |
| blind-6 | low | patch | On a watchdog/assertion failure, provider disposal precedes the current finally cancellation and directory removal. Active bootstrap can then use disposed dependencies/deleted receipt files. Cancel and await the actual scoped operation before provider disposal/storage cleanup; preserve original test failures. |
| edge-1 | medium | patch | Independently confirms the307/308 signed-POST replay and custom-header forwarding on the new command client. Group with blind-1 only after this individual verdict. |


## Final Parent Verification

Same implementation owner applied the four direct review patch groups, preserving all accepted behavior and existing native assertions. Parent read the complete26,420-byte review delta and checked all10current source hashes. New command redirect refusal is verified with real same/cross-origin307/308 requests; ordinary delegated POSTs retain signed bearer/body and honest outcome diagnostics. Parent reran417Server bootstrap/configuration/authorization/architecture cases and42AppHost cases: all459pass, zero skips/failures/not-run. Affected Release build has zero warnings/errors. Current full diff is refreshed at `/tmp/works-tenants-bootstrap-transport-review1.diff`. The pre-existing response-body buffering issue is individually triaged/deferred in the root ledger. Native cold-bootstrap/authenticated-profile acceptance remains a separate pending parent CI/release gate.


Normal pinned commitlint21.2.4 and hooks recorded/pushed exactly10owned files in `5a21dd2c4f0a96cd3cfafe418c61e1e197c99c42`. No peer gitlink was changed. Fresh exact-source CI and native profile are now pending; no native acceptance is claimed by this implementation receipt.
