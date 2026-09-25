---
title: 'Publish EventStore Typed Reminder Reconciliation'
type: 'feature'
created: '2026-09-24'
status: 'draft'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** EventStore lacks a generic typed reminder API. Works owns date-specific scheduling and startup recovery, leaving AD-20 R6 without a published, durable, recoverable SDK seam.

**Approach:** Publish versioned intent, registration, callback, and reconciliation APIs. Domain modules translate committed events; EventStore persists scheduler state and dispositions and submits through Story 4.13's receipt contract. Works adopts in 4.15; Platform owns production Scheduler and backup policy.

**Sequence decision (2026-09-25):** Build and publish Story 4.13's target effect receipt first, then resume this R6 producer story. Keep the R11 receipt outside 4.11's implementation scope.

## Boundaries & Constraints

**Always:** Streams are authoritative; indexes only discover candidates. Each intent binds canonical tenant/item, UTC due, typed kind/payload, source sequence, and schedule witness. Use AD-25/26 codecs for `wra-<digest>` actor IDs and `date-<token>`/`expiry-<token>` names; quarantine tuple collisions. Authenticate callback origin, tenant, purpose, and stored identity before submission. Acknowledge only durable receipts/checkpoints/quarantine; degrade readiness for unresolved work. AD-28 requires owner approval and restore drill before admitting new durable types or real shared data; use synthetic proof until then.

**Never:** Trust callback payload alone, duplicate Works decisions or payloads in EventStore, drop malformed evidence, or replace Works hosting before 4.15–4.16 parity.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
| --- | --- | --- | --- |
| Register/reschedule | Duplicate intent or new witness | One active reminder; cancel obsolete one | Pending state survives Scheduler failure |
| Callback replay | Valid witness after restart | Same effect ID and persisted receipt | Retry uncertain receipt without acknowledgement |
| Stale/forged callback | Old witness or wrong identity/purpose | No mutation or tenant disclosure | Audit denial; quarantine collision |
| Recovery | Lost firing, partial scan, restart | Re-fold discovered streams; reissue due, rearm future | Retain unresolved work; degrade readiness |
| Restore/HA | Restored state or two hosts race | One registration and target receipt | Fail closed on conflicts |

</frozen-after-approval>

## Code Map

Paths below are relative to `references/Hexalith.EventStore/` unless prefixed `Works:`.

- `src/Hexalith.EventStore.Contracts/`, `src/Hexalith.EventStore.Client/` — domain-neutral contracts; consume 4.13 codec/receipt.
- `src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs` — SDK composition lacks reminder routes/services.
- `src/Hexalith.EventStore.Server/Actors/AggregateActor.cs`, `UnpublishedEventsRecord.cs` — internal Dapr recovery precedent, not public R6 API.
- `tests/Hexalith.EventStore.DomainService.Tests/`, `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Integration/` — API and persisted Redis/Dapr proof.
- `tests/Hexalith.EventStore.Contracts.Tests/Packaging/`, `tools/release-packages.json` — package contract and inventory.
- Works: `src/Hexalith.Works/Reminders/`, `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` — date-only compatibility baseline, not code to move now.

## Tasks & Acceptance

**Execution:**
- [ ] `src/Hexalith.EventStore.Contracts/Reminders/ReminderIntent.cs` and `src/Hexalith.EventStore.Client/Reminders/IReminderIntentSource.cs`, `IReminderRegistrar.cs` — define versioned typed intent, witness, disposition, stream source, and register/cancel API using 4.13 receipts; keep each type in its own named file.
- [ ] `src/Hexalith.EventStore.DomainService/Reminders/ReminderCoordinator.cs`, `ReminderReconciler.cs`, and `EventStoreDomainServiceExtensions.cs` — implement durable scheduling, authenticated callback, periodic stream verification, quarantine, and readiness.
- [ ] `tests/Hexalith.EventStore.DomainService.Tests/ReminderCoordinatorTests.cs` and `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Integration/ReminderRecoveryTests.cs` — prove matrix with persisted state, restart, Scheduler loss, restore, and authorization.
- [ ] `tests/Hexalith.EventStore.Contracts.Tests/Packaging/PackagedReminderApiTests.cs` and `docs/guides/typed-reminders.md` — prove package-only R6 against named public version; record source and provider evidence for 4.15.

**Acceptance Criteria:**
- Given a committed intent, when registration/callback repeats after restart, then one reminder and one target receipt represent one logical submission.
- Given lost firing, when periodic reconciliation runs, then due/future intents converge from streams and unresolved work degrades readiness.
- Given a stale witness or unauthorized caller, when callback admission runs, then no mutation/disclosure occurs and the disposition is audited.
- Given the published R6 package, when a package-only consumer calls it, then no Works payload types are required.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

Stale callback witness: dispose before submission. If an old expiry command reaches Works, AD-25 still requires aggregate witness rejection.

## Verification

**Commands:**
- From `references/Hexalith.EventStore`, build/run focused Contracts, Client, DomainService, and LiveSidecar projects individually; assert persisted state.
- Run package inventory and isolated package-only R6 test against the named published version; record version, source SHA, commands, and results.
