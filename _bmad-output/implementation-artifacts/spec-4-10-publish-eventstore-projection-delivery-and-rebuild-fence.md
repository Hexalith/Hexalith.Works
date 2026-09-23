---
title: 'Publish EventStore Projection Delivery and Rebuild Fence'
type: 'feature'
created: '2026-09-23'
status: 'draft'
route: 'dispatch'
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

## Intent

**Problem:** AD-20 R3–R4 lack a released EventStore contract that can fan out ordinary events and fence shared rebuilds through Commit without lost writes.

**Approach:** Finish the generic, bounded delivery and shared-projection epoch protocol in EventStore, prove it against durable store semantics, and publish a named SDK version with contract tests. Works adoption belongs to Story 4.14.

## Boundaries & Constraints

- EventStore owns envelope validation, subscription delivery, durable journal/checkpoint, generation selection, and retry/quarantine mechanics. It cannot depend on Works payload types or fold rules.
- AD-16 requires CAS epoch fencing from inventory capture through Commit, old-generation reads until promotion, catch-up afterward, and abort replay into the active generation.
- AD-27 requires strict exclusive `FromSequence` paging, complete state-affecting decoding, and fail-closed conflict handling. AD-28 requires accountable data-owner retention/legal-hold/offboarding approval and a restore drill before a new durable catalog type or non-synthetic shared data is admitted.
- Preserve the Works host and its current delivery path until Stories 4.14 and 4.16 prove migration.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Duplicate or conflicting envelope | Identical redelivery is idempotent; equal identity with different digest is quarantined. |
| Concurrent delivery and rebuild | No acknowledgement bypasses the epoch fence; committed readers never see partial staging. |
| Crash during capture, stage, Commit, abort, or catch-up | Restart converges from durable state without losing an acknowledged delivery. |
| Large tenant history | At least 10,000 captured streams and staged aggregate mutations remain bounded and recoverable. |

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/SharedProjection*.cs` — current opt-in epoch/chunk groundwork.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/DomainProjectionDispatcher.cs` and `DomainSharedProjectionRebuildDispatcher.cs` — generic dispatch seams.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Streams/StreamReadPageValidator.cs` — strict paging contract.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Client.Tests/Projections/` — persisted protocol tests.

## Tasks & Acceptance

**Execution:**
- [ ] Complete bounded epoch, chunk, checkpoint, control-index, cleanup, and delivery semantics; resolve pending chunk reservations after crash, oversized prepared folds, retention, and offboarding.
- [ ] Add generic event fan-out and strict page validation to the live/replay/rebuild path, with one durable retry/quarantine disposition.
- [ ] Run store-backed concurrency, 10,000-item, corruption, restart, abort, and redelivery tests; record AD-28 approval and restore drill before admitting new durable types or non-synthetic shared data; publish a named EventStore package and API contract test for R3–R4.

**Acceptance Criteria:**
- Given every required writer is registered, when inventory capture through Commit runs with concurrent delivery, then readers select one committed generation and every acknowledged post-capture envelope converges after catch-up.
- Given a crash at any durable transition, when recovery resumes, then no acknowledged envelope or manifest member is lost and conflicting bytes fail closed.
- Given the published producer artifact, when its contract suite runs without Works references, then R3–R4 APIs and bounded behavior match the documented version.

## Implementation Notes

Story 4.9 left uncommitted opt-in epoch, chunk, validator, and focused test groundwork in the EventStore submodule. It is not a published contract or a Works consumer. Preserve that work; audit it against real store CAS semantics before release.

## Verification

- Build and run EventStore Client, DomainService, and Contracts tests against the selected state-store profile.
- Record the exact producer version, contract-test command, restart/fault-injection output, and chunk retention/offboarding evidence for AD-20 R3–R4.
