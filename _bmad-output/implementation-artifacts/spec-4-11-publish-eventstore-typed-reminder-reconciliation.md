---
title: 'Publish EventStore Typed Reminder Reconciliation'
type: 'feature'
created: '2026-09-23'
status: 'draft'
route: 'dispatch'
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

## Intent

**Problem:** AD-20 R6 has no generic SDK seam for typed durable reminder registration, callback validation, and reconciliation. Works currently owns runtime machinery around date intents.

**Approach:** Publish an EventStore reminder contract and reconciler that accepts domain-owned typed intents while EventStore handles idempotent scheduler effects and recovery through Story 4.13's target receipt contract. Works adoption belongs to Story 4.15.

## Boundaries & Constraints

- Works owns DateResume/Expiry intent translation and domain decisions; EventStore owns generic durable registration, cancellation, callback identity, and reconciliation. Platform owns Scheduler availability and backup.
- Follow AD-25 schedule witnesses: stale callbacks cannot expire or resume a rescheduled item. Preserve existing deterministic names and committed event provenance.
- Acknowledgement follows durable intent/effect/checkpoint or quarantine capture. No callback derives identity or authorization from payload alone.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Duplicate registration or callback | One logical effect and stable reminder identity. |
| Lost firing or restart | Overdue intent is reissued; future intent is re-registered. |
| Reschedule or stale token | Old callback has no state-changing effect and is auditable. |
| Scheduler or state-store failure | Pending intent remains discoverable; readiness reflects unreconciled work. |

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/` — generic producer contracts and registration.
- `src/Hexalith.Works/Reminders/` — existing date intent, actor, scheduler, and reconciliation behavior used as compatibility evidence.
- `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` — historical persisted-state baseline.

## Tasks & Acceptance

**Execution:**
- [ ] Define a typed, versioned reminder intent/receipt contract and generic SDK registration, cancellation, callback, and periodic reconciliation APIs.
- [ ] Prove duplicate, stale-fire, lost-fire, restart, HA/scheduler persistence, and backup/restore behavior with durable state and negative authorization cases.
- [ ] Publish a named EventStore producer version and contract-test command for AD-20 R6 without Works domain payload dependencies.

**Acceptance Criteria:**
- Given a committed typed intent, when registration or callback is repeated, then one deterministic reminder and one logical domain submission remain.
- Given a missing firing or restarted host, when reconciliation runs, then overdue and future intents converge without manual tenant setup.
- Given a stale schedule witness or unauthorized caller, when a callback arrives, then no state-changing command is admitted and the disposition is auditable.

## Verification

- Record EventStore package/version, generic contract tests, scheduler restart/restore evidence, and the R6 producer API consumed by Story 4.15.
