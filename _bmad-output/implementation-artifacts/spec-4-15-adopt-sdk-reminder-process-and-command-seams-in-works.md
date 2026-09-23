---
title: 'Adopt SDK Reminder, Process, and Command Seams in Works'
type: 'feature'
created: '2026-09-23'
status: 'draft'
route: 'dispatch'
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

## Intent

**Problem:** Works still owns generic reminder, process recovery, and command-submission machinery, so AD-20 R6–R8/R11 cannot be proved through published SDK seams.

**Approach:** Pin Stories 4.11–4.13 producer versions, retain Works' pure Reactor and typed intent translations, and move registration, process execution, recovery, and trusted submissions through the SDK. Platform parity belongs to Story 4.16.

## Boundaries & Constraints

- Keep the Works AppHost usable until the Platform gate passes. Works retains domain policies, exact await matching, and pure cascade/child/date/expiry translations; SDK owns generic runtime mechanics.
- AD-25 schedule witnesses and AD-26 EffectIds are durable and deterministic. AD-27 requires strict exclusive stream paging, periodic recovery, quarantine, and degraded readiness while unresolved work exists.
- Every internal submission carries authenticated tenant, workload, delegation purpose, and causation. No payload-supplied actor or cross-tenant source is authoritative.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Date or expiry reminder redelivered | One accepted transition or stable no-op; stale witness has no effect. |
| Cascade or child-resume crash | Persisted checkpoint/effect evidence drives one logical outcome after restart. |
| Corrupt or foreign source page | Quarantine and degraded readiness; no checkpoint skip. |
| Wrong origin, purpose, or effect digest | Denial/conflict before target mutation or disclosure. |

## Code Map

- `src/Hexalith.Works/Reminders/`, `src/Hexalith.Works/Recovery/`, `src/Hexalith.Works/Reactor/` — current translations and generic runtime code to separate.
- `src/Hexalith.Works/Runtime/WorksHost.cs` — current registration and routes.
- `tests/Hexalith.Works.IntegrationTests/` — reminder, cascade, child-resume, command, and recovery baselines.

## Tasks & Acceptance

**Execution:**
- [ ] Pin named published EventStore producer versions from Stories 4.11–4.13 and adapt Works typed reminders, periodic recovery, and Reactor process translations to documented SDK interfaces.
- [ ] Replace Works-owned generic submission/checkpoint/reminder machinery with SDK calls while preserving deterministic effect IDs, schedule witnesses, and the transitional host.
- [ ] Prove persisted date/expiry, cascade, child-resume, restart, lost firing, page boundary, quarantine/readiness, replay, and unauthorized submission outcomes.

**Acceptance Criteria:**
- Given a committed Works event, when the SDK reminder/process/submission paths run or restart, then the same domain decisions converge with one logical effect and durable evidence.
- Given lost firings, corrupt stream evidence, or a stale witness, when reconciliation runs, then unresolved work stays discoverable and no wrong transition is acknowledged.
- Given invalid origin, tenant, purpose, or digest, when an effect is submitted, then target state is unchanged and the disposition is auditable.

## Verification

- Record producer versions, Works consumer build, persisted restart/replay/negative-security commands, and R6–R8/R11 evidence for Story 4.16.
