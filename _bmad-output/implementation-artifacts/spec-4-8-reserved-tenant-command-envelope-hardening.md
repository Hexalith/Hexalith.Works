---
title: 'Harden Story 4.8 reserved-tenant command envelopes'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '25dd4ca932819f8f424fd7870051f42154034787'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Fourteen of the fifteen EventStore aggregate adapters reject the reserved `tenants` identifier only when it appears in the command payload. A normal payload addressed by a reserved-tenant envelope can therefore persist into the registry-colliding stream, while the subscription test intended to protect the corresponding event boundary cannot detect removal of its guard.

**Approach:** Make every aggregate adapter envelope-aware and refuse a reserved payload or envelope tenant before kernel delegation. Pin all fifteen commands through EventStore's reflection dispatcher, retain the existing payload guard, and repair the event-processor test so it proves rejection occurs before marker acquisition and handler invocation.

## Boundaries & Constraints

**Always:** Validate both normalized payload tenant and `CommandEnvelope.TenantId` at the host adapter edge; preserve `LinkConversation` domain/tenant/aggregate identity checks; keep the exception-based refusal because `/process` turns it into one caller-visible failure without writing a rejection event; keep wrappers thin and the pure kernel unchanged.

**Never:** Add general envelope/payload identity matching to the other fourteen commands; change EventStore dependency code, durable keys/contracts, catalog count 40, kernel/Reactor behavior, or topology; address mixed-case direct `/project` requests; implement the separately deferred reminder recovery/scheduling bundle; change Story 4.8 sprint status while that bundle remains open.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Normal dispatch | Ordinary matching payload and envelope tenant | Existing kernel result remains unchanged for every command | Existing domain handling applies |
| Reserved envelope | Ordinary payload tenant, envelope tenant `tenants` | All fifteen handlers refuse before kernel delegation or persistence | `InvalidOperationException` identifies the reserved registry collision |
| Reserved payload | Payload tenant `tenants`, ordinary or matching envelope | Existing payload-side refusal remains enforced | Same bounded adapter failure |
| Reserved event delivery | Matching reserved tenant in event payload and envelope | Processor returns invalid payload before marker acquisition or handler invocation | No durable marker or handler side effect |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- fourteen two-parameter wrappers need `CommandEnvelope`; reuse the reserved-tenant helper and retain `LinkConversation`'s stronger identity checks.
- `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs` -- existing reflection-dispatch and reserved-payload coverage; add a fifteen-command reserved-envelope theory using runtime-type serialization.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` -- canonical source of exactly fifteen command fixtures; reuse without changing the catalog.
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- repair the reserved-tenant pre-marker fact to use a matching `WorkItemCancelled` handler/payload/envelope and an acquire-capable marker substitute.
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md` -- close only the matching review findings and record exact verification while leaving Story 4.8 in progress.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- add `CommandEnvelope` to the fourteen unguarded handlers and centralize payload-plus-envelope reserved-tenant refusal without widening identity policy.
- [x] `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs`, `WorkItemV1Catalog.cs` -- exercise all fifteen reflection handlers with a normal payload inside a reserved envelope and retain the reserved-payload Create proof.
- [x] `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- make the pre-marker reserved-event assertion non-vacuous by aligning event type and identities and proving the handler would run if the early guard disappeared.
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md` -- resolve the scoped findings and record actual commands/results without claiming reminder or live-topology evidence.

**Acceptance Criteria:**
- Given any of the fifteen command payloads uses an ordinary tenant, when EventStore reflection dispatch addresses it with envelope tenant `tenants`, then the adapter refuses before persistence and identifies the registry collision.
- Given a reserved-tenant event whose payload and envelope identities agree, when the Works event processor receives it, then it returns `FailedInvalidPayload` without acquiring a marker or invoking the matching handler.
- Given focused and repository verification runs, when the slice completes, then changed tests and the Release build pass with zero new warnings/errors, catalog count remains 40, and Story 4.8 remains in progress for the deferred reminder bundle.

## Implementation Notes

- All fifteen wrappers now use EventStore's supported `(command, state, CommandEnvelope)` reflection shape and
  call one payload-plus-canonical-envelope reserved-tenant guard before the pure kernel. The helper adds no general
  envelope/payload equality rule; `LinkConversation` alone retains its existing stronger identity validation.
- `WorkItemV1Catalog.All` remains unchanged at 40 entries. The adapter test selects its canonical fifteen
  command fixtures by the command namespace, serializes each using its runtime type, and runs both a reserved-
  envelope refusal row and an ordinary-envelope pure-kernel parity row.
- The event-processor proof now constructs matching reserved payload and envelope identities, registers the
  matching `WorkItemCancelled` handler, and configures the marker substitute to acquire if reached.

## Spec Change Log

- 2026-09-16 -- Implemented every execution task and recorded focused/repository verification. Frozen intent,
  constraints, matrix, and acceptance criteria were not changed.

## Review Triage Log

- [blind-hunter] **high → patch** — `WorkItemEventStoreAggregate` checks raw `CommandEnvelope.TenantId`, while EventStore routes through `CommandEnvelope.AggregateIdentity`, which lowercases tenant ids. A mixed-case `TENANTS` envelope therefore reaches the canonical reserved stream for fourteen commands while bypassing the new guard. Check the canonical envelope tenant and pin mixed-case reflection dispatch.
- [blind-hunter] **medium → patch** — the retained reserved-payload Create fact copies the reserved payload tenant into the envelope, so the envelope guard masks removal of the payload guard. Address the reserved payload with an ordinary envelope tenant so the test independently pins payload-side refusal.
- [blind-hunter] **low → reject** — the reserved-envelope theory does not distinguish a guard immediately before kernel delegation from one immediately after a successful pure-kernel call. The current wrappers visibly guard first, persistence still cannot occur without a returned result, and constructing malformed-but-deserializable command objects would add disproportionate test machinery for an unlikely internal reorder.
- [blind-hunter] **low → reject** — ordinary-envelope parity uses null state for the canonical command fixtures. The changed wrappers pass the supplied state through unchanged, and building valid state fixtures for all lifecycle commands would broaden this narrow envelope-guard slice without covering a demonstrated regression.
- [blind-hunter] **false → reject** — direct `EventStoreAggregate.ProcessAsync` exceptions do prove no persistence: `AggregateActor` awaits domain-service invocation before entering its event-persistence step, and routes an invocation exception to infrastructure failure without a `DomainResult` to persist.
- [blind-hunter] **false → reject** — the older reserved-tenant flat ledger block is a legacy, pre-DW-format append-only source record, not a canonical schedulable entry; the sweep documentation says legacy items remain read-only until operator-run migration, whose manifest determines their done state.
- [blind-hunter] **low → reject** — the reminder bundle appears twice as legacy flat ledger text, but attended workflow appends are explicitly append-only and the active build workflow required appending without deduplication. Operator-run migration is the sanctioned place to merge duplicate legacy items; deleting either record here would violate the ledger contract.
- [blind-hunter] **low → patch** — the baseline diff includes the attended split append in `deferred-work.md`, but the Story 4.8 dated File List omits that file. Add it so bookkeeping matches the reviewed baseline diff.
- [blind-hunter] **low → patch** — the aggregate class remarks say all envelope-aware wrappers enforce adapter-boundary identity even though only `LinkConversation` performs full identity matching. Clarify that all wrappers reject the reserved tenant and only `LinkConversation` enforces the stronger match.
- [blind-hunter] **low → patch** — all-command guard theories now live in a class whose summary still describes only `LinkConversation`. Update the class documentation so the aggregate-wide contract is discoverable without relocating the spec-directed tests.
- [edge-case-hunter] **high → patch** — independently confirmed the raw-versus-canonical envelope tenant mismatch: `AggregateIdentity` lowercases `TENANTS` for routing after the ordinal raw-value guard. Grouped with the first blind-hunter finding for one canonical-envelope fix.
- [verification-gap] No findings.

## Verification

**Commands:**
- `aspire start --non-interactive -- --EnableKeycloak=false`, `aspire wait works --non-interactive`, `aspire describe --non-interactive`, then `aspire stop --non-interactive` -- expected: capture the required pre-change topology baseline and release build locks.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -m:1 -p:NuGetAudit=false` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.LinkConversationRuntimeAdapterTests -class Hexalith.Works.IntegrationTests.WorksDomainEventProcessorTests` -- expected: all focused tests pass.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -p:NuGetAudit=false` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: no new failure; record the known SDK-pin assertion separately if it remains the sole failure.

**Current-run results (2026-09-16):**
- Aspire baseline: `aspire start` succeeded, but `aspire wait works` timed out after 120 seconds because
  `dapr-sentry` exited; `aspire describe` showed Works and all dependent resources waiting. `aspire stop`
  completed successfully and released build locks. This was pre-change baseline evidence only.
- IntegrationTests project build: 0 warnings, 0 errors.
- Focused direct xUnit run: `LinkConversationRuntimeAdapterTests` plus `WorksDomainEventProcessorTests`
  **61/61** passed, 0 skipped. This includes fifteen reserved-envelope rows, fifteen ordinary-envelope parity
  rows, the retained reserved-payload proof, the mixed-case canonical-envelope proof, and the non-vacuous
  reserved-event pre-marker proof.
- Release solution build: 0 warnings, 0 errors.
- Full ArchitectureTests: **236/237**. The sole failure is the pre-existing SDK assertion expecting `10.0.400`
  while `global.json` pins `10.0.401`; excluding only that method passes **236/236**, including catalog count 40.
- No reminder/mTLS live lane was run or claimed. Story 4.8 and its sprint entry remain `in-progress` for the
  separately deferred reminder recovery/scheduling bundle.
