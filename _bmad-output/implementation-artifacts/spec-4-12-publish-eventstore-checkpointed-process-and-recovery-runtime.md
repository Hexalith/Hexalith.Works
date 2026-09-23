---
title: 'Publish EventStore Checkpointed Process and Recovery Runtime'
type: 'feature'
created: '2026-09-23'
status: 'draft'
route: 'dispatch'
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

## Intent

**Problem:** AD-20 R7–R8 still depend on Works-owned process/recovery loops, and parked work can escape a generic readiness and operations contract.

**Approach:** Publish an EventStore checkpointed process runner with Story 4.10's strict stream validator, Story 4.13's effect receipts, durable retry/quarantine, periodic reconciliation, and health/operations evidence. Works translations belong to Story 4.15.

## Boundaries & Constraints

- EventStore owns generic source traversal, process checkpoints, retry/parking, and health signals. Story 4.13 owns target effect receipts; Works owns cascade, child-completion, and other Reactor translations.
- AD-27 makes `FromSequence` exclusive: reuse `LastSequenceReturned`, validate each page identity and increasing envelope positions, and decode all state-affecting evidence before checkpointing.
- Acknowledgement requires persisted effect/checkpoint or tenant-scoped quarantine; unresolved work degrades readiness and remains discoverable for authenticated, audited disposition.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Page boundary and long stream | No skipped or duplicated state-affecting envelope. |
| Crash after effect before checkpoint | Deterministic replay yields one logical effect. |
| Corrupt, foreign, or unknown evidence | Quarantine captures the source and readiness degrades. |
| Parked item after restart | Periodic retry and operations evidence persist until disposition. |

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Streams/` — stream paging contract.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/` — process runner, recovery, and readiness producer seams.
- `src/Hexalith.Works/Recovery/` and `src/Hexalith.Works/Reactor/` — historical consumer behavior.

## Tasks & Acceptance

**Execution:**
- [ ] Publish generic checkpointed process execution, reuse the shared strict page validator from Story 4.10, integrate Story 4.13 effect acknowledgement, and expose periodic recovery interfaces.
- [ ] Persist bounded retry/parking/quarantine and expose degraded readiness, bounded reason metrics, alerts, and authenticated disposition hooks.
- [ ] Prove crash, page boundary, replay, unresolved work, and restart with source-backed tests; publish a named EventStore package/API contract for R7–R8.

**Acceptance Criteria:**
- Given a multi-page source and a crash at a checkpoint boundary, when the runner restarts, then every state-affecting envelope is processed once logically and no page boundary is skipped.
- Given invalid evidence or exhausted retry, when recovery continues, then the tenant-scoped unresolved record persists, readiness is degraded, and authorized disposition re-enters the same validator.
- Given the released SDK contract, when Works translations are supplied without generic runtime code, then the runner can drive them through the documented seam.

## Verification

- Record producer version, contract tests, fault-injection/restart commands, readiness/alert assertions, and R7–R8 API evidence for Story 4.15.
