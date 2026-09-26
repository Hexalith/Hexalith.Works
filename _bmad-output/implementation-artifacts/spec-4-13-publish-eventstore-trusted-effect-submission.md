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
- [x] `src/Hexalith.EventStore.Contracts/Effects/EffectIdentityCodec.cs`, `EffectKindCatalog.cs`, `tests/Hexalith.EventStore.Contracts.Tests/Effects/EffectIdentityCodecTests.cs` — freeze codec, family ordinals, and synthetic vectors before producers.
- [x] `src/Hexalith.EventStore.Contracts/Effects/TrustedEffectSubmission.cs`, `TrustedEffectContext.cs`, `TrustedEffectResult.cs`, `src/Hexalith.EventStore.Client/Effects/ITrustedEffectSubmitter.cs` — expose domain-neutral request, context, and result.
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

2026-09-25 continuation: The trusted HTTP ingress now selects only the Dapr internal authentication scheme, and signed-delegation tests reject changed tenant, source sequence, target, command, workload, purpose, causation, and semantic digest. This closes the generic bearer-principal route and part of the authorization test gap. Production admission remains closed. The retained source floor, joint source/target hold and erasure, fail-closed privileged audit, deployment mTLS/ACL evidence, owner approval, restore drill, and release inventory are still open; the old-source/offboarding matrix row remains unverified.

2026-09-26 continuation: EventStore has an opt-in trusted-effect retention gate that rejects a non-active tenant lifecycle, an unknown or crossed source floor, an absent or mismatched source envelope, and a denied joint-retention decision. The host must supply authoritative floor and joint-retention implementations; neither has a production registration. These checks cover denial for old sources and held/offboarding tenants, but no authorized joint source/target erasure path exists. EventStore AD-30 assigns full erasure to a later domain-policy workflow. The user chose to keep Story 4.13 open until the authorized joint erasure path and required evidence exist; the frozen acceptance scope is unchanged. Append-only privileged audit, production mTLS/ACL proof, data-owner approval, restore drill, and release inventory also remain open; production admission stays closed.

2026-09-26 continuation: The tenant lifecycle now registers trusted-effect evidence before target dispatch and requires a joint erasure callback before final purge. Legal hold and deletion entry serialize with registration. A privileged audit sink contract gates admission, evidence registration, collision quarantine, and erasure; a missing or failing sink prevents target dispatch or the affected write. A synthetic committed-state test exercises legal hold, an interrupted source/target erasure, retry, and final purge with the source envelope, target event, receipt, and collision record. The production source-floor provider, joint eraser, and audit sink are still unregistered. Production mTLS/ACL evidence, data-owner AD-28 approval, restore drill, and release inventory remain open. This synthetic result does not authorize real-data admission or satisfy the acceptance criteria.

2026-09-26 continuation: Target actor admission now checks the gateway proof and target partition before registering lifecycle evidence; forged proof and wrong-partition denials are audited before any retention mutation or receipt read. Outside Development, internal HTTP authentication requires a matching Dapr app-channel token; missing `APP_API_TOKEN` also fails readiness. The Kubernetes example binds the receiving sidecar and app container to one secret. These changes do not attest a production caller or open trusted-effect admission.

AD-28 authority audit: Works `architecture.md` AD-28 assigns tenant-keyed retention/erasure to EventStore, append-only privileged audit and backup/restore to Platform, and retention/legal-hold/offboarding approval to an accountable data owner. The checked EventStore tree has no authoritative retained source-floor API (`StreamReadMetadata` does not carry one), no complete tenant inventory/eraser for source and target streams, receipts, collisions, and keys, and no Platform audit backend. Registering synthetic or placeholder providers would violate the one-decision and fail-closed requirements. `ITrustedEffectRetentionGate` therefore remains unregistered for production. No accountable owner approval or AD-28 restore receipt for stream, registry, projection, pending intent, checkpoint, audit, and tenant-key order was found. Tasks 3–6 and acceptance criteria remain open.

Deployment evidence: `deploy/dapr/accesscontrol.yaml` declares `defaultAction: deny` and a trust-domain/namespace policy, but grants an admin `/**` wildcard and no named trusted-effect workload. The sample Kubernetes annotations bind `dapr.io/config: accesscontrol` and an app-channel token secret. These are static configuration facts; no running mTLS handshake, caller ACL denial/allow proof, or approved production profile was available. The existing AppHost start blocker from missing prohibited nested Tenants paths still prevents local runtime topology proof.

## Spec Change Log

## Review Triage Log

## Design Notes

Golden vectors cover current child/date resume and cascade cancel/expire, plus planned registry, expiry, and late-attach families. Use envelope sequence; check target receipt before expiring admission.

## Verification

**Commands:**
- Build Gateway; run focused Contracts, Client, Server, and LiveSidecar test projects individually; assert persisted state after faults.
- Validate release inventory and package-only trusted-effect test against named public packages; record version, source SHA, commands, and restore proof for 4.11/4.15.

**Observed 2026-09-25:** EventStore Gateway Release build passed. Debug codec test assembly `-class Hexalith.EventStore.Contracts.Tests.Effects.EffectIdentityCodecTests`: 9/9 passed. Debug Server test assembly with the three trusted-effect classes: 10/10 passed. Agent-run Client project: 838/838 passed; Server project: 3,351 passed, 25 skipped; one live Redis receipt test passed. Isolated Contracts and Client 3.108.1 package consumers restored, built, and ran. The full Contracts run was stopped after 1,889 passed and 39 unrelated governance/environment failures (missing prohibited nested submodule paths, unavailable pinned Builds commit, OQ8 packet drift). Release inventory and restore drill were not validated.

**Continuation verification 2026-09-25:** `dotnet build tests/Hexalith.EventStore.Server.Tests/Hexalith.EventStore.Server.Tests.csproj --configuration Debug --no-restore -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` passed with zero warnings and errors. Running the built Server test assembly with `-class Hexalith.EventStore.Server.Tests.Actors.TrustedEffectReceiptTests -class Hexalith.EventStore.Server.Tests.Commands.TrustedEffectAdmissionPolicyTests -class Hexalith.EventStore.Server.Tests.Authentication.JwtTrustedEffectDelegationVerifierTests -class Hexalith.EventStore.Server.Tests.Authentication.ProductionAuthorityAuthenticationTests` passed 11/11. These focused tests do not cover the old-source/offboarding matrix row or constitute production proof.

**Continuation verification 2026-09-26:** `dotnet build tests/Hexalith.EventStore.Server.Tests/Hexalith.EventStore.Server.Tests.csproj --configuration Debug --no-restore -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` passed with zero warnings and errors. Running the built Server test executable with `-class Hexalith.EventStore.Server.Tests.Commands.TrustedEffectRetentionGateTests -class Hexalith.EventStore.Server.Tests.Actors.TrustedEffectReceiptTests -class Hexalith.EventStore.Server.Tests.Commands.TrustedEffectAdmissionPolicyTests -class Hexalith.EventStore.Server.Tests.Authentication.JwtTrustedEffectDelegationVerifierTests -class Hexalith.EventStore.Server.Tests.Authentication.ProductionAuthorityAuthenticationTests` passed 21/21 with no skips. `git diff --cached --check` passed in EventStore. These tests do not prove authorized erasure, production admission, or the AD-28 restore drill.

The Debug LiveSidecar test project build passed with zero warnings and errors. Its `TrustedEffectPersistenceTests` executable class filter passed 1/1 with no skips and read back the persisted target event and receipt after a synthetic submit and replay. Its fixture supplies a synthetic admission policy, so this result does not exercise the new retention gate.

**Continuation verification 2026-09-26:** In EventStore, `dotnet build tests/Hexalith.EventStore.Server.Tests/Hexalith.EventStore.Server.Tests.csproj --configuration Debug --no-restore -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` passed with zero warnings and errors. The built test assembly with `-class` filters for `IdempotencyTenantLifecycleActorTests`, `TrustedEffectRetentionGateTests`, `TrustedEffectAdmissionPolicyTests`, `TrustedEffectReceiptTests`, and `TrustedEffectJointOffboardingTests` passed 68/68. The single `TrustedEffectJointOffboardingTests` class passed 1/1. `git diff --check` and `git diff --cached --check` passed. The earlier Debug LiveSidecar persisted trusted-effect test and Gateway Release build also passed during this continuation. The joint offboarding test uses synthetic committed actor state; no production eraser or AD-28 restore drill was verified.

**AppHost runtime blocker 2026-09-26:** `aspire start --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --isolated --non-interactive --nologo` exited 2 after its Debug build succeeded with zero warnings and errors. `TenantsProjectPaths.Resolve()` stopped startup because both projects under `references/Hexalith.Tenants/` inside EventStore are absent. Those are nested submodule paths, and the workspace instructions prohibit initializing them. `aspire ps --non-interactive` confirmed no AppHost remained running. No runtime resource state was available to inspect.

**Current continuation verification 2026-09-26:** EventStore HEAD is `cdad82781dc3641775b652c7852fc66db3453663`; the working tree is dirty, so that SHA is a baseline, not an immutable release source identity. `dotnet build tests/Hexalith.EventStore.Server.Tests/Hexalith.EventStore.Server.Tests.csproj --configuration Debug --no-restore -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` passed with zero warnings/errors. The built Server test DLL run with `-class` filters for `DaprInternalAuthenticationHandlerTests`, `ProductionAuthorityAuthenticationTests`, `JwtTrustedEffectDelegationVerifierTests`, `TrustedEffectReceiptTests`, `TrustedEffectAdmissionPolicyTests`, `TrustedEffectRetentionGateTests`, and `TrustedEffectJointOffboardingTests` passed 40/40 with no skips. This includes synthetic joint offboarding and an actor re-creation replay, not a backend restore drill.

After the final code edits, `python3 tools/pack-release-packages.py /tmp/eventstore-story-4-13-final-pack-20260926 3.108.1` completed without error and `python3 tools/validate-release-packages.py /tmp/eventstore-story-4-13-final-pack-20260926 3.108.1` reported the exact 14/14 local manifest packages. `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/eventstore-story-4-13-final-pack-20260926 dotnet tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests.dll -class Hexalith.EventStore.Contracts.Tests.Packaging.TrustedEffectPackageContractTests` passed 1/1, using 13 isolated library consumers and one tool. The [local inventory and exact package SHA-256 values](evidence/story-4-13/local-package-inventory.md) are recorded separately. These packages have a synthetic local version from an uncommitted tree and are not a published release or a canonical source SHA. Repeat against an immutable candidate before 4.11/4.15 adoption. `git diff --check` passed after the final edits.
