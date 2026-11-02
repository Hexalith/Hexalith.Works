---
title: Restore ChatBot authentication denial auditing and current boundary tests
type: bugfix
created: '2026-11-01'
status: in-review
route: dispatch
review_loop_iteration: 0
baseline_commit: 2ac8c4b5bc87cc253c92ca60e806a86ab0948e99
context:
  - /tmp/works-chatbot-timesheets-adoption-20261101/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
---

<frozen-after-approval reason="User authorized CI repairs, preserving current authorization; human validation checkpoints waived">

## Intent

The already committed authenticated-human boundary returns typed command denials before ChatBot's old audit writer runs, losing the required safe authorization-failure fact. Two old hub tests also assume anonymous negotiation that the committed policy intentionally rejects. Restore exact denial auditing and exercise the current authenticated boundary without weakening it.

## Boundaries & Constraints

Always keep the committed human bearer handshake policy, typed401/403 response and correlation/task metadata. Record exactly one sanitized command authorization-failure fact without dispatch, reservations, protected reads, precommit audit or invented tenant/actor authority. Unauthenticated facts use unavailable tenant and anonymous actor. Treat caller payload identity/resource fields as untrusted; command-name metadata must be finite/sanitized using existing domain seams. Authentication denial precedes malformed-body disclosure. Cancellation and real backend failures retain existing behavior.

For authorized hub broadcasting, use genuinely validated signed fixture JWTs bound separately to each tenant and retain original delivery/isolation assertions. For anonymous HTTP access, require failed negotiation. Preserve the old anonymous JoinTenant guard assertion separately at the actual hub method boundary; do not retain an HTTP expectation explicitly retired by the committed policy.

Never weaken production authorization, add permit-all or anonymous metadata, fabricate authenticated principals in production, change meaningful denials merely to get green, skip tests, introduce a new generic platform abstraction, initialize nested modules, or modify the concurrent catalog implementation.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Anonymous command | No human bearer, normal submission | Original typed401, one safe failure fact, zero dispatch/reservation/precommit effects | No payload authority |
| Malformed/invalid credential | Invalid body/token, missing or unsafe metadata | Authentication denial retains precedence and safe facts | No raw payload/token logging |
| Authorized hub | Two valid signed identities bound to different tenants | Existing same-tenant broadcast and cross-tenant isolation | Same domain guards |
| Anonymous/invalid hub HTTP | Missing or invalid bearer | Negotiation401/403, no unauthorized group membership | Never an anonymous HTTP connection |
| Direct anonymous invocation | Existing anonymous hub context | Original tenant-forbidden HubException | No group join |

</frozen-after-approval>

## Code Map

Owner `/tmp/works-chatbot-timesheets-adoption-20261101`; inspect its build/editor/architecture guidance before changes. Concurrent catalog owner modifies Routing, query declarations, four fallback overloads, Program enrollment and trusted-authority fixtures; preserve all those files. Parent owns integration, Git and publication.

- `src/Hexalith.ChatBot.Server/Authentication/ChatBotApplicationBoundaryResultHandler.cs`: existing exact named-policy handler produces typed denials ahead of endpoint/correlation middleware, but has no audit call. Extend only the actual command-submission challenge/forbid path through existing safe audit facilities.
- `Gateway/ChatBotCommandAdmissionPipeline.cs`, `ChatBotGatewayRequestAdapter` and audit fact/writer contracts: reuse established sanitized failure/correlation metadata and bounded request parsing. Do not invoke the full admission pipeline early because it can reserve/write. Avoid unbounded reads, cross-request caching, duplicate audit or speculative authority.
- `tests/Hexalith.ChatBot.Server.Tests/Gateway/CommandGatewayAdmissionApiE2ETests.cs`: failing CommandGatewayApi_ShouldRejectUnauthenticatedSubmissionBeforeDispatch requires its original safe audit fact and zero endpoint effects. Add only necessary malformed/invalid metadata controls.
- `tests/Hexalith.ChatBot.Server.Tests/Projections/ChatBotProjectConversationHubE2ETests.cs`: retain delivery and isolation assertions with real JWT validation, plus anonymous negotiation and direct invocation guards. Reuse existing repository signed-token fixtures and actual SDK policy configuration.
- History settles policy: e89a3ac11f4e952febfb9156d6660e08b9f0c6c4 (2026-10-31) intentionally changed permissive assertion to SDK CreateHumanPolicy and explicitly recorded these three failures. Anonymous test setups date to June21. This task does not introduce a new client-visible authentication decision.

## Tasks & Acceptance

- [ ] Restore one safe denial fact at the command boundary without calling domain admission or dispatch.
- [ ] Align hub HTTP tests with the existing intentional human policy using actual signed credentials; retain business-level anonymous invocation protection separately.
- [ ] Run affected existing auth/command/hub classes, retain all original security and isolation assertions, and report exact new/old case reconciliation and failure evidence.
- [ ] Return an owned-path-only patch, baseline/current hashes, commands/results and remaining risks; do not include the concurrent catalog change as your own.

Given anonymous submission, when the existing boundary challenges, then the original typed response and required sanitized audit fact both occur exactly once. Given valid tenant-specific hub credentials, when broadcasting, then original tenant delivery/isolation holds. Given anonymous access, when negotiating or directly invoking its guard, then the authoritative transport/domain denial occurs before effects.

## Implementation Notes

No Git/remote operations, dependency updates or real native/container starts. Parent approval is required only for the local shared build window: notify before compiling/testing because the catalog worker currently uses the same Server.Tests outputs. Source edits in the owned auth/test paths can proceed immediately. Use the immutable `/tmp/chatbot-routing-Directory.Packages-35b58049.props` for a labeled finite Release/public-package check against3.130.2; parent owns latest3.131.0 adoption and complete fresh hosted CI. Do not infer deployed/public qualification from fixture tests.

## Spec Change Log

## Review Triage Log

## Verification

Serialized Server.Tests Release/public-package build, then built xUnit assembly single-dash selectors for affected boundary/command/hub classes. Zero unexplained skips and no expectation relaxation outside the explicitly obsolete anonymous-handshake setup. Preserve initial failures and return complete owned-path source diff including new files. Final full-project verification and independent review remain parent-owned.


## Parent Review1 Triage

All three layers returned final before triage; verification reported no gaps. Every finding received an individual verdict before grouping. All change-owned fixes are direct patches without a new public API.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | medium | patch | The new pre-denial body read has a byte bound but waits solely on RequestAborted. A slow partial/chunked body can delay the response and fact indefinitely. Add a small internal metadata-only read deadline; its expiry yields unknown metadata, while actual request cancellation still propagates. |
| blind-2 | medium | patch | ReadAsync is outside the JSON exception scope. Malformed framing or other non-cancellation request-body transport exceptions can suppress typed denial and auditing. Treat unavailable body metadata as unknown while preserving request abort and audit backend failure behavior. |
| blind-3 | medium | patch | AuditMetadata.SafeCommandName is a generic safe-string scrubber, not a finite command registry; personal data such as an email can pass. Select only existing canonical known command names; everything else becomes unknown_command. |
| blind-4 | medium | defer | InMemoryAuditWriter storage is pre-existing and unbounded across existing denial callers, including the original anonymous pipeline before the Oct31 boundary hardening. This change restores the already-required fact through the same sink; it does not define production retention or durable storage. Bounded ingestion/retention needs its own existing audit storage contract work; no new capacity or production durability is claimed here. |
| blind-5 | medium | patch | The new signed fixture sets key/issuer/audience but leaves a possible Authority value. Production configuration prefers Authority, so clear it explicitly for this isolated fixture and prove external authority settings cannot replace the intended validation path. |
| blind-6 | low | patch | Real-bearer HTTP API cases are all negative; genuine signed-token hub positives prove shared validation, but a small API positive-credential control should show the bound principal reaching existing ordinary domain admission/denial, without inventing successful persistence or changing current closed policies. |
| blind-7 | low | reject | Wrong signature, anonymous and nonhuman credentials already exercise the modified fixture and actual SDK policy, while issuer/audience/lifetime validators are unchanged SDK behavior. Additional temporal/issuer/audience matrix dimensions add test-fixture controls without a distinct changed production branch. Keep the accepted valid-credential and Authority-isolation controls instead. |
| edge-1 | medium | patch | Independently reproduced escaped unpaired surrogates: JsonDocument.Parse can succeed while property.Name or GetString throws InvalidOperationException. Contain malformed metadata conversion in its narrow extraction boundary and retain typed denial/one safe fact. |
| edge-2 | medium | patch | Independent malformed HTTP framing ReadAsync/BadHttpRequestException path suppresses fact/typed response. Same unavailable-metadata fallback as blind-2, without swallowing real request cancellation or audit errors. |
| edge-3 | medium | patch | Independent claims finding: exactly-one sanitized fact and typed response fail on the two demonstrated metadata exceptions. Covered by the narrow metadata conversion/body-transport fixes; record this verdict separately. |
