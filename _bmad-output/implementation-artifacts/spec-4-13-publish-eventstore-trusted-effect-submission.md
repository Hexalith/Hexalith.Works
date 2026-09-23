---
title: 'Publish EventStore Trusted Effect Submission'
type: 'feature'
created: '2026-09-23'
status: 'draft'
route: 'dispatch'
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

## Intent

**Problem:** AD-20 R11 has a gateway and trusted-intent API but no complete deterministic effect receipt and retention contract for cross-aggregate Works submissions.

**Approach:** Publish the EventStore-owned AD-26 codec, trusted submission adapter, and target-partition receipt protocol. Works assigns domain effect kinds and consumes the seam in Story 4.15.

## Boundaries & Constraints

- EventStore owns codec, gateway dispatch, target-scoped inbox, receipt atomicity, replay conflict, and retention binding. Works owns pure effect translation and immutable effect ordinals.
- AD-26 binds EffectId, MessageId, IdempotencyKey, tuple/digest, purpose, workload, and causation. Same identity with different semantics is conflict plus quarantine.
- AD-23/24 require origin, tenant, delegation purpose, and actor authorization before target mutation. AD-28 requires joint source/target receipt retention, audited offboarding, accountable data-owner approval, and a restore drill before new durable receipt types or non-synthetic shared data are admitted.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Same effect redelivered after commit or restore | Recorded disposition returns without redispatch. |
| Same ID with different tuple or command | Conflict is quarantined; target does not mutate. |
| Unauthorized purpose, origin, or tenant | Denied before target disclosure or append. |
| Receipt write or gateway failure | No acknowledgement without durable target outcome. |

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.Gateway/` and `src/Hexalith.EventStore.Server/Actors/` — dispatch and target-partition commit.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/` — trusted domain-service adapter.
- `src/Hexalith.Works/Reactor/` — source effect families used for golden vectors.

## Tasks & Acceptance

**Execution:**
- [ ] Implement versioned AD-26 canonical codec and golden vectors for every Works effect family.
- [ ] Commit target-scoped receipts atomically with target events/outcome, and enforce replay, conflict, retention, and authorized offboarding behavior.
- [ ] Publish a named EventStore producer artifact and contract tests for trusted gateway submission and negative origin/purpose/tenant cases.

**Acceptance Criteria:**
- Given an identical effect submitted twice or after restore, when the target actor handles it, then one target outcome exists and the same persisted disposition is returned.
- Given a colliding ID or unauthorized provenance, when submission is attempted, then no target state or tenant existence is disclosed and the attempt is quarantined or audited as specified.
- Given the published API, when Works supplies pure effect facts, then no Works-owned generic gateway receipt machinery is needed.

## Verification

- Record producer version, golden-vector corpus, replay/conflict/restore commands, and R11 contract evidence for Story 4.15.
