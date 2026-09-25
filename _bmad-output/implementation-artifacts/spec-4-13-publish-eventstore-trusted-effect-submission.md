---
title: 'Publish EventStore Trusted Effect Submission'
type: 'feature'
created: '2026-09-25'
status: 'in-progress'
baseline_commit: 'c3badb0f075b537d4e0aa91a80bbae48de7f9677'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Expiring gateway idempotency records do not atomically bind cross-aggregate effects to target outcomes. A crash or restore can run the target again, blocking AD-20 R11 and Story 4.11.

**Approach:** Publish an EventStore versioned codec, trusted SDK submission API, and private target receipt committed with the outcome. Works supplies pure effect facts in 4.15.

## Boundaries & Constraints

**Always:** AD-26 derives `EffectId` from canonical tenant, source domain/aggregate/envelope sequence, effect kind, target domain/aggregate, and immutable ordinal. Encode text with four-byte big-endian UTF-8 length; integers as signed eight-byte big-endian; render SHA-256 as 52 uppercase Crockford Base32 characters. MessageId and IdempotencyKey equal `wrk-<EffectId>`. The target actor stores full tuple, semantic digest, success/rejection/no-op, workload, purpose, and causation with the outcome. Exact replay returns the receipt without Handle; collision quarantines. Check origin, signed purpose delegation, tenant, target, and command authority before disclosure. Version the family/ordinal catalog and golden vectors before producers. Retain source and target evidence together across legal hold, offboarding, and source-floor checks. AD-28 requires owner approval and restore drill before new durable-type or real-data admission; use synthetic proof meanwhile.

**Never:** Use payload sequence, traversal order, caller digest, expiring idempotency, or broker dead letter as receipt authority. Do not emit receipt domain events, log protected payloads, change Works translators, or claim Platform parity before 4.16.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
| --- | --- | --- | --- |
| First effect | Authorized tuple/command | One target outcome and atomic receipt | Inspect uncertain save |
| Replay/restore | Same key, tuple, digest | Return receipt without Handle | Ignore expired gateway status |
| Conflict | Same key, changed semantics | No new target event | Durable audited quarantine |
| Denied caller | Wrong origin/purpose/tenant/target | No read, mutation, or disclosure | Audit; fail closed |
| Old source/offboarding | Below floor or under legal hold | Refuse replay; retain evidence | Joint authorized erasure |

</frozen-after-approval>

## Code Map

Paths below are relative to `references/Hexalith.EventStore/` unless prefixed `Works:`.

- `src/Hexalith.EventStore/Controllers/CommandsController.cs`, `src/Hexalith.EventStore.Server/Pipeline/SubmitCommandHandler.cs` — gateway Replay/Expired bypasses target; add effect-aware admission.
- `src/Hexalith.EventStore.Server/Commands/CommandRouter.cs` — canonical target routing; current fence omits source/purpose/digest.
- `src/Hexalith.EventStore.Server/Actors/AggregateActor.cs` — events commit before terminal idempotency; stage receipt with first event batch or no-op batch and inspect ambiguous saves.
- `src/Hexalith.EventStore.Server/Actors/IdempotencyTenantLifecycleActor.cs` — current purge omits target receipt.
- `src/Hexalith.EventStore.DomainService/IIdempotencyIntentAdapter.cs` — reuse server-trusted semantic intent adapter, without trusting caller extensions.
- `tests/Hexalith.EventStore.Server.Tests/Actors/FaultInjectingActorStateManager.cs`, `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Events/EventPersistenceIntegrationTests.cs` — commit fault and persisted provider seams.
- `tests/Hexalith.EventStore.Contracts.Tests/Packaging/ProjectionPackageContractTests.cs` — public package-only precedent.
- Works: `src/Hexalith.Works.Reactor/`, `src/Hexalith.Works/Recovery/`, `src/Hexalith.Works/Reminders/` — effect family evidence, no producer dependency.

## Tasks & Acceptance

**Execution:**
- [ ] `src/Hexalith.EventStore.Contracts/Effects/EffectIdentityCodec.cs`, `EffectKindCatalog.cs`, `tests/Hexalith.EventStore.Contracts.Tests/Effects/EffectIdentityCodecTests.cs` — freeze codec, family ordinals, and synthetic vectors before producers.
- [ ] `src/Hexalith.EventStore.Contracts/Effects/TrustedEffectSubmission.cs`, `TrustedEffectContext.cs`, `TrustedEffectResult.cs`, `src/Hexalith.EventStore.Client/Effects/ITrustedEffectSubmitter.cs` — expose domain-neutral request, context, and result.
- [ ] `src/Hexalith.EventStore/Controllers/TrustedEffectsController.cs`, `src/Hexalith.EventStore.Server/Commands/TrustedEffectAdmissionPolicy.cs` — validate delegation, origin, tuple, command digest; reserve `wrk-` for trusted effects.
- [ ] `src/Hexalith.EventStore.Server/Actors/IAggregateActor.cs`, `AggregateActor.cs`, `EffectReceipt.cs` — read receipt before Handle; atomically commit eventful/no-op outcomes; inspect uncertain saves; quarantine conflict.
- [ ] `src/Hexalith.EventStore.Server/Actors/IdempotencyTenantLifecycleActor.cs`, `docs/guides/trusted-effects.md` — bind retention, source floor, joint offboarding, and AD-28 gate.
- [ ] `tests/Hexalith.EventStore.Server.Tests/Actors/TrustedEffectReceiptTests.cs`, `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Events/TrustedEffectPersistenceTests.cs`, `tests/Hexalith.EventStore.Contracts.Tests/Packaging/TrustedEffectPackageContractTests.cs` — prove matrix, receipt-write failure, crash/restore, persisted state, authorization, and named public package API.

**Acceptance Criteria:**
- Given an authorized effect, when the target commits or the caller loses its acknowledgement, then one durable target outcome and receipt exist and replay returns that disposition without Handle.
- Given a colliding identity or unauthorized provenance, when submission reaches admission, then no target mutation or tenant disclosure occurs and denial/conflict is durably auditable.
- Given a retained source floor, legal hold, or authorized offboarding, when replay or erasure is requested, then source and target evidence obey one retention decision.
- Given the named public SDK, when a package-only consumer submits synthetic effect facts, then no Works assembly or gateway status record is required for correctness.

## Implementation Notes

2026-09-25: Tasks 1–2 are implemented and verified. The approved task list remains frozen. EventStore now has a version-one identity codec, seven-family catalog, public SDK, a signed-delegation admission path, private actor receipt staging for eventful and no-op outcomes, and target-scoped collision records. Ordinary submission reserves the `wrk-` namespace. The trusted endpoint remains closed in production because `ITrustedEffectRetentionGate` has no production registration. No Works translator changed.

Remaining work: complete retained source-floor validation; one authorized source/target legal-hold and offboarding erasure path through the lifecycle actor; append-only privileged audit with fail-closed mutation; dedicated signed-delegation negative tests; production caller mTLS/ACL and restore proof; full release inventory. The data owner's AD-28 approval and restore-drill evidence are prerequisites for real-data admission. The old-source/offboarding matrix row has no passing test. Tasks 3–6 and the acceptance criteria remain open.

## Spec Change Log

## Review Triage Log

## Design Notes

Golden vectors cover current child/date resume and cascade cancel/expire, plus planned registry, expiry, and late-attach families. Use envelope sequence; check target receipt before expiring admission.

## Verification

**Commands:**
- Build Gateway; run focused Contracts, Client, Server, and LiveSidecar test projects individually; assert persisted state after faults.
- Validate release inventory and package-only trusted-effect test against named public packages; record version, source SHA, commands, and restore proof for 4.11/4.15.

**Observed 2026-09-25:** EventStore Gateway Release build passed. Debug codec test assembly `-class Hexalith.EventStore.Contracts.Tests.Effects.EffectIdentityCodecTests`: 9/9 passed. Debug Server test assembly with the three trusted-effect classes: 10/10 passed. Agent-run Client project: 838/838 passed; Server project: 3,351 passed, 25 skipped; one live Redis receipt test passed. Isolated Contracts and Client 3.108.1 package consumers restored, built, and ran. The full Contracts run was stopped after 1,889 passed and 39 unrelated governance/environment failures (missing prohibited nested submodule paths, unavailable pinned Builds commit, OQ8 packet drift). Release inventory and restore drill were not validated.
