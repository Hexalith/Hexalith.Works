---
title: 'Prove Platform Works Parity and Rollback'
type: 'feature'
created: '2026-09-23'
status: 'draft'
route: 'dispatch'
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

## Intent

**Problem:** Platform's opt-in Works topology has health evidence only; AD-20 R1–R11 lack one executable parity matrix and rollback proof from the target host.

**Approach:** Complete the Platform-owned topology, service defaults, health, telemetry, operations, security profile, and `eng/verify-works-host.sh`; run every historical Works scenario against published producer and Works consumer versions. Story 4.9 alone removes the old host after this gate passes.

## Boundaries & Constraints

- Preserve Platform's clean-checkout Agents host contract. Works preview Dapr hosting remains development/test only without the Platform Maintainer's time-bounded production exception.
- AD-20 requires a named producer artifact, Works consumer, passing Platform command, persisted end state, and rollback evidence per R1–R11 row. An absent seam or skipped lane is red.
- AD-24 requires mTLS, deny-by-default ACLs, network/broker policy, and a machine-readable fail-closed production profile. AD-28 requires data-owner retention/legal-hold/offboarding approval and restore drill before non-synthetic shared data or a new durable catalog type is admitted.
- The Works AppHost and ServiceDefaults remain available throughout this story.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Platform startup and restart | EventStore, Works, Dapr, operations, health, and telemetry become ready; persisted work converges after restart. |
| Historical Works lanes | Create, progress, spawn, suspend, child/date resume, cascade, claim, query, projection, and rebuild match or strengthen prior persisted end states. |
| Security negatives | Wrong port/app/trust domain/tenant/purpose/sequence and unauthorized publish fail before effect or disclosure. |
| Rollback | Prior Works host resumes from retained durable state without duplicate logical effects. |

## Code Map

- `/home/administrator/projects/hexalith/platform/apphost.cs`, `DaprComponents/`, and `eng/verify-works-host.sh` — target composition and proof command.
- `tests/Hexalith.Works.IntegrationTests/` — historical scenario baselines to re-run under Platform.
- `src/Hexalith.Works.AppHost/` — retained rollback composition until Story 4.9.

## Tasks & Acceptance

**Execution:**
- [ ] Complete Platform topology and R1/R2/R10 defaults, health, telemetry, operations, mTLS, ACLs, and production-policy profile while preserving the Agents clean-checkout verification.
- [ ] Implement `eng/verify-works-host.sh` and a row-by-row AD-20 ledger with exact producer version, Works consumer, command, persisted outcome, negative case, and rollback evidence for R1–R11.
- [ ] Run full Platform live parity, restart/redelivery, shared rebuild, unauthorized-origin, and rollback lanes without skips; record data-owner/retention/restore decisions required by AD-28.

**Acceptance Criteria:**
- Given published producer and Works consumer versions, when the Platform verifier runs, then every applicable R1–R11 row passes with persisted end-state and rollback evidence.
- Given unauthorized traffic or missing production security configuration, when Platform starts or receives a request, then it fails closed without effect or disclosure.
- Given a rollback drill after Platform writes, when the prior Works host resumes, then it remains usable and converges without duplicate logical effects.

## Implementation Notes

Story 4.9 left an uncommitted opt-in development topology in Platform; all 11 declared resources reached healthy state locally. This is R1 topology groundwork, not parity, production, or rollback proof.

## Verification

- `./eng/verify-agents-host.sh` must keep passing from a clean Platform checkout.
- `./eng/verify-works-host.sh` must pass R1–R11 with no skipped required lane and retain exact restart, security, persisted-state, and rollback logs for Story 4.9.
