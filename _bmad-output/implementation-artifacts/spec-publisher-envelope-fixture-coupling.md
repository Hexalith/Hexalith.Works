---
title: 'Couple dead-letter fixtures to the publisher envelope'
type: 'bugfix'
created: '2026-09-05'
status: 'done'
baseline_revision: 'c08cb3497768806d80a8e949d320cb28ccc40afc'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '_bmad-output/implementation-artifacts/spec-dapr-subscription-operations-hardening.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** The EventStore operations consumer tests hand-author structured CloudEvent JSON, so a producer-side `Server.Events.EventEnvelope` member rename can make real dead letters replay-ineligible while parser and capture tests remain green.

**Approach:** Give the operations test project its approved direct reference to `Hexalith.EventStore.Server`, serialize a real server `EventEnvelope` inside one shared structured-CloudEvent fixture helper, and use that helper at both the parser and capture endpoint boundaries.

## Boundaries & Constraints

**Always:** Use `JsonSerializerDefaults.Web` to mirror Dapr's camel-case data serialization; construct the real `Hexalith.EventStore.Server.Events.EventEnvelope`; keep CloudEvent `id` equal to `data.messageId`; retain the parser's intentional alias and adversarial literal cases; assert all replay identity fields and exact capture bytes.

**Never:** Edit the deferred-work ledger or `.bmad-loop`; change production parser or publisher behavior; add package versions or references outside the approved Operations.Tests-to-Server project reference; replace adversarial malformed/alias fixtures that intentionally exercise non-publisher shapes; expose payload data through diagnostics.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Publisher-shaped parse | Shared helper serializes a real server envelope into a structured CloudEvent | Parser returns the envelope's message, tenant, domain, aggregate, correlation, and event-type identity as replayable | A producer member rename or incompatible wire-shape change breaks compilation or the consumer assertion |
| Capture endpoint | Chunked request contains bytes from the same shared helper and the configured maximum equals their length | Endpoint returns 200 and forwards those exact bytes to the drain actor | Any truncation or shape drift fails the endpoint test |
| Alternate and invalid shapes | Existing hand-authored alias, malformed, ambiguous, incomplete, conflicting, and oversized cases | Existing compatibility and fail-closed behavior remains covered | Replay-ineligible cases retain stable unidentified identity |

</intent-contract>

## Code Map

- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/Hexalith.EventStore.Operations.Tests.csproj` -- add the approved direct `ProjectReference` to `../../src/Hexalith.EventStore.Server/Hexalith.EventStore.Server.csproj`; existing references already cover operations and testing support.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventEnvelope.cs:29` -- public producer record whose named constructor members and Web JSON wire names must drive the fixture; read-only.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPublisher.cs:178-205` -- read-only publisher evidence: creates the publish envelope, supplies matching CloudEvent id/type/source metadata, and passes it to Dapr `PublishEventAsync`.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/StructuredCloudEventFixture.cs` -- new shared test helper; construct a real server envelope and serialize a structured CloudEvent with Web JSON options.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/DeadLetterEnvelopeParserTests.cs:50-100` -- replace the hand-written producer-shaped literal with the helper and assert values sourced from its real envelope; preserve alias/adversarial literals.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/DeadLetterCaptureBodyTests.cs:61-137` -- use the shared producer-derived bytes for accepted and hash-conflict capture endpoint cases; remove the duplicated literal fixture.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Operations/Capture/DeadLetterEnvelopeParser.cs:15-51` -- read-only consumer boundary requiring the structured CloudEvent and complete identity tuple, including the `eventTypeName` producer alias.

## Tasks & Acceptance

**Execution:**
- [x] `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/Hexalith.EventStore.Operations.Tests.csproj` -- add the approved Server project reference -- make producer contract drift visible to the consumer test build.
- [x] `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/StructuredCloudEventFixture.cs` -- add the shared real-envelope structured-CloudEvent serializer -- centralize the producer-derived bytes and expected identity.
- [x] `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/DeadLetterEnvelopeParserTests.cs` and `DeadLetterCaptureBodyTests.cs` -- consume the helper at parser and endpoint surfaces while preserving intentional non-producer cases -- close DW-55 without weakening compatibility or fail-closed coverage.

**Acceptance Criteria:**
- Given the real Server `EventEnvelope` contract, when the shared fixture is serialized and parsed, then all replay-safe identity values equal the producer instance and `IsReplayable` is true.
- Given the shared producer-derived structured CloudEvent bytes in a chunked capture request, when the capture endpoint accepts the exact configured boundary, then it returns 200 and sends the byte-identical body to the actor.
- Given a producer envelope member is renamed or its Web JSON shape no longer satisfies the consumer, when Operations.Tests compiles and runs, then the shared helper or parser assertions fail rather than allowing a green consumer suite.
- Given existing alternate publisher aliases and invalid structured CloudEvents, when the focused suite runs, then compatibility and stable replay-ineligible behavior remain unchanged.

## Spec Change Log

## Review Triage Log

### 2026-09-05 — Review pass
- verdicts: 8 findings — high 0, medium 0, low 0, false 8, maybe-false 0
- findings:
  - `[false]` `[reject]` The helper should execute the real `EventPublisher`/Dapr boundary — the approved intent explicitly asks for a shared structured-CloudEvent helper that serializes the real server envelope, and the named constructor plus parsed-member assertions make a producer member rename fail without claiming to test Dapr itself.
  - `[false]` `[reject]` Web JSON options could differ from a customized production Dapr serializer — the current host registers `AddDaprClient()` without serializer customization, and `JsonSerializerDefaults.Web` is the current repository convention and an explicit intent constraint; no reachable alternative configuration was demonstrated.
  - `[false]` `[reject]` Capture body tests should retain small stable local JSON — producer-schema dependency in both capture and parser tests is the requested regression coupling, so a producer contract change interrupting those tests is the intended outcome rather than unrelated fragility.
  - `[false]` `[reject]` Operations.Tests should not reference Server — the verbatim approved decision specifically requires this direct test-project reference, so the reported dependency expansion is the selected design rather than a defect.
  - `[false]` `[reject]` No normally executed test observes bytes from the live publishing boundary — that broader integration proof is not required to close the stated member-rename escape; the focused suite now compiles against, serializes, and parses the real producer type, and current production has no divergent serializer registration.
  - `[false]` `[reject]` Test-side CloudEvent reconstruction diverges from the strongest possible publisher-path reading — the more specific bundle wording selects a shared helper around the real envelope, which the diff implements at the producer-member and consumer-parser surfaces.
  - `[false]` `[reject]` Capture tests do not separately assert replayable identity — `CaptureAsync` parses the helper bytes before actor invocation, while the parser test using the identical helper asserts the full identity; together they make the consumer suite fail on producer-shape drift as requested.
  - `[false]` `[reject]` Some valid parser fixtures remain hand-written — the retained `eventName` case deliberately covers a supported non-producer alias and the remaining literals are adversarial mutations; the producer-shaped and capture fixtures now share the real-envelope helper.

## Design Notes

The helper should return the serialized body together with the exact producer envelope used to create it. This keeps expected values independent of copied strings while allowing both parser and endpoint tests to share the same bytes. An anonymous structured-CloudEvent wrapper is acceptable because only `data` is the producer contract under test; its CloudEvent attributes mirror the metadata supplied by `EventPublisher`.

## Verification

**Commands:**
- `dotnet test tests/Hexalith.EventStore.Operations.Tests/Hexalith.EventStore.Operations.Tests.csproj --configuration Release` -- expected: the focused operations suite passes with the real Server envelope reference and shared fixture.
- `dotnet build Hexalith.EventStore.slnx --configuration Release` -- expected: the repository builds with zero errors and no new warnings.
- `git diff --check` -- expected: no whitespace errors.

## Auto Run Result

Status: done
Blocking condition: none

### Implemented change

Added the approved Operations.Tests-to-Server project reference and a shared structured-CloudEvent fixture that constructs and Web-serializes the real `Server.Events.EventEnvelope`. Parser coverage now derives its expected replay identity from that producer instance, and capture endpoint coverage sends the same serialized bytes through bounded chunked-body handling and exact actor forwarding.

### Files changed

- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/Hexalith.EventStore.Operations.Tests.csproj` -- references the Server project directly for producer-contract fixtures.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/StructuredCloudEventFixture.cs` -- creates the shared producer-derived structured CloudEvent bytes and source envelope.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/DeadLetterEnvelopeParserTests.cs` -- replaces the copied publisher-shaped JSON and asserts every replay identity field from the real envelope.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/DeadLetterCaptureBodyTests.cs` -- reuses the shared bytes for accepted and hash-conflict endpoint paths and removes its duplicated valid-envelope literal.

### Review findings breakdown

Patches applied: 0. Items deferred: 0. Eight findings were rejected: four proposed undoing or broadening the explicitly approved direct-reference/helper design; one requested a live Dapr publishing proof beyond the producer-member regression; one preferred a stronger publisher-path reading over the bundle's specific helper instruction; one duplicated identity assertions already covered across the shared parser/endpoint fixture; and one treated intentional alias/adversarial literals as unresolved producer coupling.

Follow-up review recommendation: false. No review finding required a patch.

### Verification performed

- `dotnet test tests/Hexalith.EventStore.Operations.Tests/Hexalith.EventStore.Operations.Tests.csproj --configuration Release` -- 76/76 passed, 0 skipped.
- `dotnet build Hexalith.EventStore.slnx --configuration Release` -- succeeded with 0 warnings and 0 errors.
- `git diff --check` and `git diff --cached --check` -- clean.
- Matrix audit -- publisher-shaped parsing, exact-boundary capture/byte forwarding, alternate alias parsing, and malformed/adversarial fail-closed cases all ran in the focused suite.

### Residual risks

The helper intentionally verifies the real producer CLR/JSON member surface, not a live broker/sidecar round trip. That broader integration boundary is unchanged and is not required for DW-55's approved member-rename regression.
