---
title: Preserve bounded authenticated Tenants gateway requests
type: bugfix
created: '2026-11-02'
status: superseded
route: dispatch
review_loop_iteration: 0
superseded_by: spec-preserve-tenants-retry-credentials.md
baseline_commit: ca93fe19847d3535353c8b4850497350aaba47d3
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.Tenants/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="User authorized CI repairs preserving authorization and waived human validation">

## Intent

The native owner profile now creates and persists a tenant, but its first REST list request is canceled by the API gateway's inherited ten-second attempt timeout during mandatory authority verification. Give this existing client one finite thirty-second application attempt and forward the incoming authorization consistently on every send.

## Boundaries & Constraints

This typed client serves six generated queries and eleven commands. Preserve their routes, bodies, status/error mapping, cancellation, authorization, freshness and conditional responses. Preserve caller cancellation and the previous effective thirty-second total bound; remove application resilience/replay only for this client. Preserve bearer-before-Dapr handler order and authoritative Dapr routing/token replacement.

Replace stale outbound Authorization with the exact incoming raw values on each send; absent input clears it. Never join or select ambiguous credentials into a valid credential, fabricate credentials, log tokens, or change authentication policies. Preserve bootstrap, readiness, metadata, UI, domain-invocation and source clients and all Dapr policies. SDK proof algorithms, ten-second replay and separate two-second final observation remain unchanged. No SDK/peer edits, package changes, native/container starts, test deadline increases, skips or synthetic acceptance.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Failure handling |
|---|---|---|---|
| Gateway success | Actual registration plus ServiceDefaults | One send, finite30s, unchanged payload/routing/auth | Existing SDK result mapping |
| Transient failure |503 or transport exception | One application send for query and command | Existing result/exception propagates |
| Cancellation | Caller canceled or finite client timeout | Terminal observes cancellation; no replay | Cancellation propagates |
| Repeated forwarding | Same request, one incoming bearer, stale outgoing value | Exactly one unchanged incoming value each send | No accumulated values |
| Changed/absent context | Incoming value changes or disappears | Replace with current values or remove outgoing | No stale authority |
| Ambiguous credentials | Multiple incoming raw values | No joining, selecting or normalization into one credential | Existing fail-closed policies retained |
| Other clients | Ordinary and readiness registrations | Existing resilience retained | No global removal |

</frozen-after-approval>

## Code Map

Owner: Works `references/Hexalith.Tenants`, clean main at baseline. Parent owns Git, independent review and fresh native CI. No intent gaps or irreversible implementation actions; registration plus forwarding and actual-factory controls warrant dispatch. Read tracked build/editor configuration and relevant auth/catalog docs. Do not discover skills under references or reactivate/rerender the parent's already active build workflow.

- `src/Hexalith.Tenants.Api/Program.cs:118`: existing AddEventStoreGatewayClient returns IHttpClientBuilder named IEventStoreGatewayClient. Scope transport configuration here; test the actual production registration, extracting a small registration seam only if needed to avoid a duplicate test-only composition.
- `src/Hexalith.Tenants/Bootstrap/TenantBootstrapServiceCollectionExtensions.cs:24`: reuse its existing ConfigureAdditionalHttpMessageHandlers pattern removing only ResilienceHandler, followed by ConfigureHttpClient restoring finite30s. Standard resilience sets HttpClient.Timeout infinite, so removing its handler alone is insufficient. No gateway timeout configuration key exists; do not invent a configuration API.
- `src/Hexalith.Tenants.Api/Services/InboundBearerForwardingHandler.cs:14`: TryAddWithoutValidation currently appends on each send. Copy raw values without flattening, removing previous Authorization first. Preserve other headers.
- `tests/Hexalith.Tenants.IntegrationTests/TenantsApiGatewayHandlerTests.cs`: existing forwarding test only sends once; composition test omits ServiceDefaults. Extend with focused cases using actual production registration and factory pipeline. Keep separate helper types in their own files. Existing generated controller tests cover public authorization, cancellation, responses and ETags.
- `tests/Hexalith.Tenants.Server.Tests/Bootstrap/TenantBootstrapCommandTransportTests.cs`: demonstrates real factory behavior, shortened test-only policy budgets, asynchronous cancellation handshakes and non-global resilience controls. Reuse approach, not a second transport implementation.
- `docs/production-auth-readiness.md`: document this bounded API-to-gateway client, scope to queries/commands and caller cancellation without claiming native/public acceptance.

Evidence: source run41366387653/job125027324363, SDK a82555477689e3e021c3c74bcab0ccfefd4ff307. Query starts00:30:45.244; three source reads return200 after2089/2477/1354ms. Final observation starts54.910; API Standard-AttemptTimeout fires55.240, canceling it55.257. Retry56.831 ends401/JWT ArgumentException. Duplicate-header retry is source-backed, not directly logged header evidence. Sample inventory warnings are unrelated. Source native CI is the runtime baseline; local fake transport checks are not native proof.

## Tasks & Acceptance

- [x] Apply the bounded one-attempt transport to the production typed registration and make forwarding authoritative/idempotent.
- [x] Execute every matrix control through focused handler/factory tests; retain existing controller, authorization and bootstrap coverage.
- [x] Document the actual transport scope and report complete owned diff, source hashes, exact commands/counts and remaining blockers.

Given ServiceDefaults and the actual API registration, when a request succeeds, fails or is canceled, then exactly one application attempt uses the correct authority and finite budget. Given unrelated clients, when factory pipelines are resolved, then their existing resilience remains. Parent must subsequently prove actual REST success and persisted/current-authority effects with unchanged native tests.

## Implementation Notes

Use public SDK3.131.0 and workspace Builds root; serialized Release builds, individual test projects and built xUnit single-dash selectors. Notify parent before substantial compilation. Do not commit, push, alter gitlinks, change unrelated files or claim native qualification. Preserve failed logs. User waived human checkpoints; the sole implementation contract is this spec.

## Spec Change Log

## Review Triage Log

- blind-1: medium, patch. New documentation/comment language can imply a wall-clock completion guarantee. HttpClient.Timeout is cooperative; the existing SDK invocation handler awaits durable lease release. Qualify the HTTP budget and characterize a blocked release without changing SDK cleanup or abandoning its fence.
- blind-2: medium, patch. Current cancellation cases begin after terminal entry and the substituted authority acquires immediately. Add caller/client-timeout controls during actual handler authority acquisition, asserting its token cancellation, one acquisition and zero terminal sends.
- blind-3: medium, patch. NonValidated proves in-memory raw values, while HTTP serialization may combine them; the new documentation's downstream separation language is unsupported. Describe raw collection preservation accurately and add receiver-side duplicate/comma-combined credential refusal with a valid paired control and no gateway effects on denial.
- blind-4: low, patch. The stateless handler reads the ambient accessor for every send, so no demonstrated cross-request leak exists; pooled-context concurrency remains a useful regression control for this changed forwarding contract. Add successive/overlapping actual-factory sends under separate contexts, including absent credentials, without production changes.
- blind-5: low, patch. The real pipeline removes all application resilience, but429/Retry-After is a distinct removed-policy input absent from the current response cases. Extend existing finite query/command controls to assert original429 details/retry metadata and one send.
- blind-6: low, patch. The success control claims unchanged input while asserting only selected body fields. Retain each submitted request and compare message identity and its entire payload, without asserting unrelated generated SDK fields.
- edge-1: medium, defer. Verified pre-existing RouteCatalogAuthority.ReleaseAsync uses noncancelable store reads/CAS and unlimited conflict retries; RouteCatalogInvocationHandler awaits lease disposal. This behavior exists in public7776 and current966 source and is not caused by this API transport repair. Safely bounding durable cleanup needs an SDK-owned design preserving fences; record it separately. The new documentation overstatement is addressed by blind-1, without weakening or bypassing cleanup.

All three layers completed before triage. Verification-gap returned no gaps. Each finding was classified before grouping: the new documentation claim and existing SDK cleanup are distinct bad outcomes; all accepted test/document corrections are direct patches to the same implementation owner. No new review loop is required for patches.

## Verification

Build the affected IntegrationTests project cleanly; run focused gateway/controller/auth cases without skips and relevant existing bootstrap controls individually. Assert production30s separately from any short test-only timeout used to prove cancellation without a30-second wait. Retain independent ordinary/readiness resilience and Dapr headers. Return XML/logs and every owned path; parent performs final verification, three independent reviews and hosted native qualification.

### Implementation and parent acceptance

Only six owned paths changed. Parent read the complete staged32516-byte diff and verified every raw source hash and every matrix control against passing XML. Worker clean Release builds pass;352 gateway/controller/bootstrap cases pass with zero skips. Parent's separate Release build and91 gateway/controller cases pass with zero failures/skips/not-run. Evidence: Works `.artifacts/tenants-api-gateway-transport-20261102/parent/` and `/tmp/tenants-gateway-transport-20261102/verification.json`.

The broader unchanged command-runtime suite passes151/165. All14 failing cases reproduce on an isolated untouched ca93 archive with the identical public3.131.0 package cohort/build flags and exact failure messages; parent independently compared their XML. Its EventStore TestServer entrypoint never runs the changed API Program/forwarder. Preserve these failures for the ongoing public SDK migration; no assertion or source-auth policy was weakened. This unit establishes isolated transport behavior, not native/public REST acceptance. Fresh hosted source qualification remains required.

### Superseded during concurrent-source integration

This draft was not shipped. Incoming main3a603db76f894c4ffacbae16683eb04f179d5f99 independently repairs the query deadline with a30-second attempt/total standard resilience pipeline, retaining retries and circuit breaker. Preserve that concurrent work. All six local source files, complete/index/review patches, source hashes, original spec and receipts are preserved under Works `.artifacts/tenants-api-gateway-transport-20261102/keep-before-upstream-reconciliation/`; all36 review-patched cases pass without skips and its clean build/format checks are retained. Only these owned local changes were restored before normal fast-forward. The remaining defect is exact idempotent credential forwarding across the preserved retry pipeline; continue through the replacement spec. No standalone acceptance or publication is claimed for this superseded transport design.
