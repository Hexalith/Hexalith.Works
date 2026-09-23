---
title: 'Adopt SDK Projection and Query Seams in Works'
type: 'feature'
created: '2026-09-23'
status: 'draft'
route: 'dispatch'
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

## Intent

**Problem:** Works still owns generic `/project` delivery and query/rebuild plumbing, so EventStore R3–R4 contracts have no Works consumer and AD-20 R5 query authorization is unproved under the target boundary.

**Approach:** Consume the published Story 4.10 SDK seam for ordinary projection, fan-out, and shared rebuild while keeping Works folds pure; adapt tenant queries to the SDK authorization and result-filter path. Platform parity belongs to Story 4.16.

## Boundaries & Constraints

- Preserve the Works AppHost and a usable rollback route until Story 4.16 passes. Opt-in or dual-path migration cannot acknowledge the same delivery through two independent writers.
- Works owns roll-up/what's-next/topology/pending-intent folds and query policy. EventStore owns envelope validation, delivery, epoch fence, generation selection, and generic query transport.
- AD-16 requires generation-pinned reads across query and recovery consumers; AD-23/24 require tenant, actor, origin, and result filtering before disclosure. Unknown or corrupt state-affecting evidence fails closed.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Live accepted event | SDK delivery converges the Works read models and acknowledges after durable state/checkpoint. |
| Shared rebuild with live traffic | Old generation serves reads until Commit; catch-up converges the promoted generation. |
| Query from wrong actor or tenant | No tenant data, membership, or result leaks. |
| Restart or redelivery | Same persisted projection and query result, without a second logical update. |

## Code Map

- `src/Hexalith.Works/Runtime/WorksHost.cs` and `src/Hexalith.Works/Projections/` — current custom route and pure folds.
- `src/Hexalith.Works/Queries/` and `src/Hexalith.Works/Recovery/` — tenant query and generation-sensitive readers.
- `tests/Hexalith.Works.IntegrationTests/` — current projection, query, and rebuild baseline.

## Tasks & Acceptance

**Execution:**
- [ ] Pin a named, published Story 4.10 EventStore package and migrate Works ordinary delivery/fan-out to its documented handler and epoch contracts.
- [ ] Connect Works shared-rebuild fold and generation-selected query/recovery readers, including full pending-date and parking manifests, without generic Works-owned storage plumbing.
- [ ] Adapt authorized executor/coordinator queries and result filters; run persisted-state live, rebuild, restart, redelivery, and negative security tests. Preserve the rollback path for Story 4.16.

**Acceptance Criteria:**
- Given the SDK delivery and rebuild path is enabled, when accepted events and a concurrent rebuild run, then persisted roll-up, what's-next, and pending intents converge without lost or duplicate logical writes.
- Given an authorized executor or coordinator query, when the SDK query path executes, then only permitted tenant results are returned with explicit stale/unavailable status.
- Given an unauthorized tenant, origin, or result filter, when a query or projection call arrives, then it has no effect or disclosure.

## Implementation Notes

Story 4.9 corrected two Works recovery readers' exclusive stream cursor and added focused tests. Those fixes remain in the current working tree, but do not by themselves make Works an SDK epoch consumer.

## Verification

- Record the exact EventStore producer version, Works consumer build, persisted query/projection/rebuild commands, and R3–R5 evidence for the Platform matrix.
