---
title: 'Harden Story 4.8 reserved-tenant command envelopes'
type: 'bugfix'
created: '2026-09-16'
status: 'ready-for-dev'
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
- [ ] `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- add `CommandEnvelope` to the fourteen unguarded handlers and centralize payload-plus-envelope reserved-tenant refusal without widening identity policy.
- [ ] `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs`, `WorkItemV1Catalog.cs` -- exercise all fifteen reflection handlers with a normal payload inside a reserved envelope and retain the reserved-payload Create proof.
- [ ] `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- make the pre-marker reserved-event assertion non-vacuous by aligning event type and identities and proving the handler would run if the early guard disappeared.
- [ ] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md` -- resolve the scoped findings and record actual commands/results without claiming reminder or live-topology evidence.

**Acceptance Criteria:**
- Given any of the fifteen command payloads uses an ordinary tenant, when EventStore reflection dispatch addresses it with envelope tenant `tenants`, then the adapter refuses before persistence and identifies the registry collision.
- Given a reserved-tenant event whose payload and envelope identities agree, when the Works event processor receives it, then it returns `FailedInvalidPayload` without acquiring a marker or invoking the matching handler.
- Given focused and repository verification runs, when the slice completes, then changed tests and the Release build pass with zero new warnings/errors, catalog count remains 40, and Story 4.8 remains in progress for the deferred reminder bundle.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Verification

**Commands:**
- `aspire start --non-interactive -- --EnableKeycloak=false`, `aspire wait works --non-interactive`, `aspire describe --non-interactive`, then `aspire stop --non-interactive` -- expected: capture the required pre-change topology baseline and release build locks.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -m:1 -p:NuGetAudit=false` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.LinkConversationRuntimeAdapterTests -class Hexalith.Works.IntegrationTests.WorksDomainEventProcessorTests` -- expected: all focused tests pass.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -p:NuGetAudit=false` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: no new failure; record the known SDK-pin assertion separately if it remains the sole failure.
