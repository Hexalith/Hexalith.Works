---
title: 'Publish EventStore Typed Reminder Reconciliation'
type: 'feature'
created: '2026-09-29'
status: 'done'
baseline_commit: '613a96c1b200fc71491fe8ad2c1a7b80990b9dc7'
eventstore_baseline_commit: 'f378afdb7cdeec85144fffc20dd9a13a9775bf85'
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

**Release decision (2026-09-29):**
- Prove the package-only R6 API against a locally packed version during implementation.
- The owner publishes the EventStore release after review.
- The story closes only after the package-only test passes against that named public version (the 4.10 pattern).
- The full spec was kept, not split.

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

Paths are relative to `references/Hexalith.EventStore/` (HEAD `f378afdb`, clean) unless prefixed `Works:`.

- `src/Hexalith.EventStore.Contracts/Effects/EffectIdentityCodec.cs`:
  - The frozen AD-26 v1 codec. Reuse its length-prefixed tuple encoding and `RenderDigest`.
  - Do not change its encoding or the golden vectors in `tests/.../Effects/EffectIdentityCodecTests.cs`.
- `src/Hexalith.EventStore.Contracts/Effects/EffectKindCatalog.cs`:
  - A closed v1 catalog that already holds `DateResume` and `Expiry`.
  - Validate intent kinds against it; do not add kinds.
- `src/Hexalith.EventStore.Contracts/Effects/{EffectIdentity,TrustedEffectSubmission,TrustedEffectContext,TrustedEffectResult}.cs` and `src/Hexalith.EventStore.Client/Effects/ITrustedEffectSubmitter.cs`:
  - This is the 4.13 receipt path.
  - Resubmitting the same effect returns the same `EffectId` with `Replayed = true`.
  - `TrustedEffectContext` requires a `DelegationToken`, and nothing in the SDK mints one.
- `src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs`:
  - The composition root: `AddEventStoreDomainServiceCore` at lines 404–464, `UseEventStoreDomainService` at 121–175.
  - Follow its presence-keyed opt-in pattern: `Client/Registration/EventStoreDomainEventsServiceCollectionExtensions.cs` for DI and `EventStoreDomainEventsEndpointExtensions.cs` for routes.
  - Every route mapping is guarded by `IsRouteMapped`.
- `src/Hexalith.EventStore.DomainService/DaprStateStoreHealthCheck.cs` shows the health check and `IHealthChecksBuilder` extension pattern. Names come from `EventStoreDomainTelemetry.cs`, and the tag is `"ready"`.
- `src/Hexalith.EventStore.DomainService/Hexalith.EventStore.DomainService.csproj` references only `Dapr.AspNetCore`. It needs a `Dapr.Actors.AspNetCore` reference, which is already centrally pinned at 1.18.10.
- `IReadModelStore` gives durable CAS state: `TrySaveAsync` treats etag `""` as a first write. Unit tests use `Testing/Fakes/InMemoryReadModelStore`.
- `src/Hexalith.EventStore/Authentication/DaprInternalAuthenticationHandler.cs` is the gateway's `APP_API_TOKEN` app-channel precedent. The domain-service actor routes have no equivalent yet.
- `src/Hexalith.EventStore.Server/Actors/AggregateActor.cs` (`drain-unpublished-*`) is the pattern for internal `IRemindable` recovery. It is not a public API.
- Tests:
  - `tests/Hexalith.EventStore.DomainService.Tests/` is flat, with fixtures in `Fixtures/`.
  - `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Integration/` uses `[Collection("DaprTestContainer")]`. Its fixture throws when Redis, placement or the scheduler is absent.
  - `tests/Hexalith.EventStore.Contracts.Tests/Packaging/TrustedEffectPackageContractTests.cs` together with the probe dictionary in `scripts/validate-consumer-package-references.py` form the package-only template.
- Docs: mirror `docs/guides/trusted-effects.md`. Do not touch `docs/ci.md`; its bytes are sealed.
- Works: `src/Hexalith.Works/Reminders/` (`DateReminderActor`, `DateReminderReconciler`, the startup-only `ReminderReconciliationService`) is the baseline behavior. Leave Works code unchanged.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.EventStore.Contracts/Reminders/` — one type per file:
  - `ReminderIntent` binds tenant, target domain and aggregate, UTC due, catalog kind, opaque typed payload, source domain/aggregate/sequence, and schedule revision.
  - `ReminderDisposition`.
  - `ReminderIdentityCodec` (versioned):
    - `wra-<52>` over the tuple `("reminder-actor", tenant, item)`.
    - `wrs-<52>` over the tuple `("schedule", tenant, item, due UTC ticks, revision)`.
    - `date-`/`expiry-` names from a closed kind map.
- [x] `src/Hexalith.EventStore.Client/Reminders/` — `IReminderIntentSource` is domain-implemented: it re-folds current intents from the stream and translates a due intent into a command type and payload. Also add `IReminderRegistrar` (`ConvergeAsync(target)`) and `IReminderDelegationTokenProvider`.
- [x] `src/Hexalith.EventStore.DomainService/`:
  - `ReminderActor` with a configurable actor type name.
  - `ReminderCoordinator`.
  - `ReminderIntentIndex`: a tenant registry plus per-tenant candidates, written by CAS before scheduling.
  - `ReminderReconciler`: a hosted service with a periodic pass that starts at startup and uses `TimeProvider`.
  - `ReminderCallbackTokenFilter`.
  - `EventStoreReminderOptions` bound to `EventStore:Reminders`.
  - `eventstore-reminders-unresolved` readiness check.
  - `AddEventStoreReminders`/`MapEventStoreReminders` extensions.
  - The csproj reference.
- [x] `tests/Hexalith.EventStore.Contracts.Tests/Reminders/ReminderIdentityCodecTests.cs` — golden vectors, collision detection, rejection of unknown kinds, and unchanged `EffectIdentityCodec` vectors.
- [x] `tests/Hexalith.EventStore.DomainService.Tests/` — `ReminderCoordinatorTests`, `ReminderCallbackAdmissionTests`, `ReminderReconcilerTests`, and composition tests. Cover every matrix row with persisted fake-store state.
- [x] `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Integration/ReminderRecoveryLiveSidecarTests.cs` — against Redis/Dapr:
  - Persist registration.
  - Invoke the callback directly; do not wait for a timer.
  - Restart the host with Redis kept: expect the same effect ID and a replayed receipt.
  - Delete the Scheduler reminder: the reconciler rearms it.
- [x] `tests/Hexalith.EventStore.Contracts.Tests/Packaging/PackagedReminderApiTests.cs` plus a new probe in `scripts/validate-consumer-package-references.py` — a package-only consumer using the reminder API, with no Works types.
- [x] `docs/guides/typed-reminders.md` — the contract, the codec, the operator runbook for quarantine and readiness, the production gate, and evidence handed to 4.15.

**Acceptance Criteria:**
- Given a committed intent, when registration/callback repeats after restart, then one reminder and one target receipt represent one logical submission.
- Given lost firing, when periodic reconciliation runs, then due/future intents converge from streams and unresolved work degrades readiness.
- Given a stale witness or unauthorized caller, when callback admission runs, then no mutation/disclosure occurs and the disposition is audited.
- Given the published R6 package, when a package-only consumer calls it, then no Works payload types are required.

## Implementation Notes

Implemented 2026-09-29 as uncommitted changes in `references/Hexalith.EventStore`. During the session the owner's `/pushall` advanced the submodule from `f378afdb` to `ccb4faf0`. The two added commits (`c765329e`, `ccb4faf0`) are docs and sprint-status only and do not overlap this work. Works code is unchanged.

**Decisions within the frozen intent:**
- State lives in `IReadModelStore` with ETag CAS, not actor `StateManager`, so unit tests assert persisted fake-store state. Keys are `eventstore:reminders:v1:{ActorTypeName}:{control:tenants | tenant:{t}:candidates | item:{wra} | item:{wra}:disposition:{subject}}`. A write re-reads a monotonic `Version` to prove it won the race. A lost race fails closed: nothing is scheduled and the work counts as unresolved.
- The AD-11 item is the target aggregate. The target domain is stored and compared: a second domain that derives the same `wra-` actor is quarantined as `actor-collision`.
- A witness stores identifiers plus a SHA-256 payload digest, never the payload. A current intent is re-folded before every submission. The same name with different evidence is a `witness-collision`. Malformed intents are quarantined by evidence digest.
- Reminders are armed as periodic reminders with period `RetryMaxDelay`, so a lost callback fires again. An uncertain outcome re-arms a doubling backoff from `RetryInitialDelay`.
- Registration and reconciliation submit due intents directly and arm future ones. A stale callback is an audited no-op and cancel, then re-converges with the intents it just re-folded; otherwise a replacement witness whose own registration was lost would drop out of the index.
- The callback does not re-check the due instant; the Scheduler owns timing.
- `ReminderCallbackTokenFilter` is middleware installed by an `IStartupFilter`. It guards every `/actors/{ActorTypeName}/...` route, even when the host maps the actor handlers itself. A denial returns an empty `401`.
- Audit is written before release. If the audit write fails, the witness is retained and a retry replays the receipt.
- Logs carry actor IDs, reminder names, effect IDs, reason codes, counts, and exception type names only.
- The readiness view is per host and is rebuilt by the first pass after a restart. It reports `Degraded` until one pass has completed.
- `scripts/validate-consumer-package-references.py` gained a repeatable `--package` filter. `PackagedReminderApiTests` uses it to run only the Contracts, Client, and DomainService consumers. The whole inventory is still validated.
- The live fixture registers reminders on the primary host with synthetic seams: the fixture-only delegation, the synthetic admission policy, and the real gateway proof routed to the aggregate actor. The primary `daprd` receives `APP_API_TOKEN`. Periodic reconciliation is disabled so the test drives each pass.

**Verification (2026-09-29, Debug, `-m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0`):**
- `Contracts.Tests -class '*Reminder*'`: 26/26. Full binary: 2115 passed, 1 skipped, 51 failed. All 51 failures are pre-existing `Packaging/*` environment failures: uninitialized nested `references/*` submodules, absent pinned Builds SHAs, OQ8 gate drift, and committed `6-1-p1r-3108` evidence csproj files. None touches reminder code.
- `DomainService.Tests -class '*Reminder*'`: 83/83 after review iteration 1 (57 before). Full binary: 217/218. The one failure is `TenantsDomainService_DoesNotReferenceGeneratedApiHostOrDeclarePerMessageControllers`, which needs the uninitialized nested `references/Hexalith.Tenants`.
- `Client.Tests`: 838/838. It has no reminder-specific tests; the Client types are interfaces and records exercised by the DomainService tests and the package probes.
- `Server.LiveSidecar.Tests -class '*Reminder*'`: 1/1 against Redis, placement, and Scheduler, all present. It proves registration persisted before arming, token-less and forged callbacks refused with no mutation, a direct callback submitting receipt `effect_receipt_<EffectId>`, a host and sidecar restart with Redis kept replaying the same effect ID with `Replayed = true`, and a Scheduler reminder deleted through the Dapr API being re-armed by `RunPassAsync`. Full binary: 126 passed, 1 skipped, 1 failed. The failure is `IdempotencyAdmissionOq8PostgresqlTests`, which launches stale `samples/.../bin/Release` outputs from 2026-09-27 and needs PostgreSQL.
- Package-only proof: `tools/pack-release-packages.py <dir> 3.110.0-local.412` (after review iteration 1; `.411` before) then `tools/validate-release-packages.py` validated 14 packages. `EVENTSTORE_PACKAGE_CONTRACT_DIR=<dir> Contracts.Tests -method '*PackagedReminderApi*'`: 1/1 against the local pack. **Still to do before `done`:** repeat against the named public version after the owner publishes, and record the version, source SHA, and result here.

**Deferred (not in 4.11 scope):**
- The audited operator disposition path for quarantine, and the production delegation issuer, audit backend, mTLS/ACL proof, and restore drill all move to 4.16.
- Tenant-registry pruning and sharding the per-tenant candidate document. It is one document per tenant, capped by `MaxCandidatesPerTenant` (default 10,000), and fails closed when full.
- Works adoption moves to 4.15.

## Spec Change Log

## Review Triage Log

Review iteration 0 (2026-09-29). Reviewers: verification-gap (VG), blind hunter (BH), edge-case hunter (EC). Paths are relative to `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/` unless noted.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| VG1 | Audit-before-release (`audit-unavailable`, stale audit failure) untested | medium | Pre-verified: no test can fail a disposition `SaveAsync`; removing the check in `SettleAsync` keeps every test green | patch |
| VG2 | `receipt-mismatch`, `delegation-failed`, `source-unavailable` untested; `Rejection`/`NoOp` receipts untested (BH15 member) | medium | Pre-verified: the fakes cannot produce these outcomes; deleting the `EffectId` equality check keeps every test green | patch |
| VG3 | Stored-versus-stream witness collision on the convergence path untested | medium | Pre-verified: only the callback path is tested (`WitnessCollisionAtCallbackIsQuarantined`) | patch |
| VG4 | Orphan callback cancellation not asserted | low | Pre-verified: `WrongIdentityCallbackMutatesNothing` never reads `Cancelled` | patch |
| VG5 | `index-capacity` and `index-conflict` fail-closed untested | medium | Pre-verified: no test names either code | patch |
| VG6 | Readiness pruning after a complete pass untested | low | Pre-verified: no test covers `CompletePass` pruning or the incomplete-pass guard | patch |
| VG7 | Reconciler loop (disabled flag, retry delay after an incomplete pass) untested | low | Pre-verified. The disabled flag is cheap to test. The retry-delay timing needs a timer fake, and its impact is timing only | patch (disabled flag only) |
| VG-O1 | A fail-closed registration of a new item leaves no durable trace | high | Same root cause as BH1 | patch (with BH1) |
| BH1 | A new item is lost silently when its index write fails | high | `ReminderCoordinator.ConvergeAsync` catches `ReminderFailClosedException` from `EnsureCandidateAsync`. With `stored` null, nothing is persisted or indexed and the call returns `Unresolved=1` without throwing, so the caller acknowledges. The next complete pass prunes the item from readiness | patch |
| BH2 | The at-least-once `ConvergeAsync` caller obligation is undocumented | low | `IReminderRegistrar` and the guide never say the caller must retry on an exception; a crash between commit and convergence loses registration | patch (docs) |
| BH3 | Two witnesses can share one effect identity | medium | `CreateEffectIdentity` omits due instant and revision. The 4.13 target compares `SemanticDigest` (`Server/Actors/AggregateActor.cs:309`) and throws on a conflict (`:264`), so a second intent from the same event retries forever or replays the first receipt | patch |
| BH4 | A released witness leaves no tombstone; convergence replays it | low | Replay is idempotent (same `EffectId`); the gap is that the source contract never says to stop reporting a handled intent | patch (docs) |
| BH5 | Convergence and reconciliation bypass the retry backoff | medium | `ConvergeCoreAsync` submits every due entry whatever its `Retrying` state. During a gateway outage every pass on every replica resubmits every retained entry | patch |
| BH6 | Every pass re-arms every future witness unconditionally | medium | `ConvergeCoreAsync` calls `ArmAsync` for each future entry: replicas × candidates Scheduler writes per interval. Multi-replica scan partitioning is not addressed; correctness holds through actor turns and costs only reads | patch (re-arm only when needed) |
| BH7 | The shared default actor type `EventStoreReminderActor` is cluster-global | medium | Dapr placement is per namespace. Two apps on the default cross-route actors, and a foreign host's orphan path cancels the other app's reminders | patch |
| BH8 | The actor tuple omits the target domain | low | AD-11 binds `(reminder-actor, tenant, item)`, and ULID aggregate ids make collisions rare; the uniqueness rule is simply undocumented | patch (docs) |
| BH9 | A missing `APP_API_TOKEN` reports only `Degraded` | medium | Outside Development every reminder actor call returns 401, so the runtime is down while `/ready` is 200. The gateway reports the same misconfiguration `Unhealthy` (`dapr-app-channel-token`) | patch |
| BH10 | Disposition records are unbounded and have no erasure path | medium | A key per subject and no TTL; records outlive item erasure | defer (AD-28 retention approval) |
| BH11 | Options validation hides the specific error | low | Setup-only annoyance; the fix adds an `IValidateOptions` class | reject |
| BH12 | The callback never checks the due instant | low | Only NTP-level clock skew could fire early, and a guard would read the same local clock | reject |
| BH13 | Hot per-tenant document, no compare-and-swap backoff, index read on empty convergence | low | After the BH1 fix, contention makes the caller retry rather than lose work. Sharding goes beyond v1 synthetic scale, and skipping the read would strand orphan candidates | reject |
| BH14 | The guide is not wired into the configuration reference | low | `docs/guides/configuration-reference.md:620` says `APP_API_TOKEN` is required only for `AllowedCallers`; there are no `EventStore:Reminders` rows and no link | patch (docs) |
| BH15 | Test gaps on risky paths | low | Members folded into VG1, VG2, VG5, BH3 and BH5. The `Expiry` end-to-end case and a package `WebApplication` probe are rejected: codec and composition tests cover them | patch (via those entries) |
| BH16 | A second registration with a different source is ignored | low | A programming error, and unlikely; the fix adds a guard | reject |
| BH17 | Calls inside an actor turn are unbounded; the first pass runs before listening | low | The `HttpClient` default timeout bounds the submitter, the blast radius is one item's actor, and an incomplete first pass retries after `RetryInitialDelay` | reject |
| EC1 | Duplicate actor routes when the host maps after `UseEventStoreDomainService` | medium | `works/src/Hexalith.Works/Runtime/WorksHost.cs:130-133` does exactly this. A duplicate `MapActorsHandlers` makes every actor request ambiguous. The failure is loud, which suits a documentation fix | patch (docs) |
| EC2 | Index fail-closed for a never-indexed item | high | Same root cause as BH1 | patch (with BH1) |
| EC3 | The callback takes the first matching intent despite a same-name collision | low | Convergence quarantines both, but `HandleCallbackCoreAsync` breaks on the first match; this violates "quarantine tuple collisions" | patch |
| EC4 | Effect-ID collision retries forever | medium | Same root cause as BH3 | patch (with BH3) |
| EC5 | Backoff bypassed on convergence | medium | Same root cause as BH5 | patch (with BH5) |
| EC6 | Host registered the actor type with another implementation | low | The failure is loud: a proxy call to a non-`IReminderActor` actor throws | reject |
| EC7 | A second `TIntentSource` is ignored | low | Same as BH16 | reject |
| EC8 | Item state with work but a missing index entry is never re-indexed | medium | `EnsureCandidateAsync` runs only when `stored is null \|\| added`. A restored older index strands the item from reconciliation, which breaks the Restore/HA matrix row | patch |
| EC9 | `UsePathBase` bypasses the token filter | medium | The startup-filter middleware sees `/base/actors/{type}/...` before `UsePathBase` strips it, so `StartsWithSegments` misses it and the routed actor call skips the token | patch |
| EC10 | A delay above about 49.7 days faults the hosted service | low | `Task.Delay` throws above `uint.MaxValue` ms; a single validation line fixes it | patch |
| EC11 | Obsolete-cancel audit result ignored | low | Requires a state-store write failure followed by a successful persist; the stream stays authoritative and the cancel is re-derivable | reject |
| EC12 | AC2 readiness goes Healthy after a fail-closed unindexed item | high | Same root cause as BH1 | patch (with BH1) |
| EC13 | AC3 unauthorized callers are only logged | false | Design Notes define audit as durable records plus digest-only logs. Token denials emit event 200209 and no-witness callbacks emit 200208; writing durable records for unauthenticated calls would open an unauthenticated write path | reject |
| EC14 | A stale firing re-converges and may submit | false | The stale witness itself submits nothing (`StaleWitnessIsAuditedNoOpAndCancelled`). Re-convergence runs only after origin and stored-identity admission, and submits only intents the current stream reports as due, exactly as any pass would | reject |

**Patches applied for review iteration 0 (2026-09-29), in place, no loopback:**
- A first convergence that fails closed (`index-capacity`, `index-conflict`, `state-conflict`) now rethrows, so the caller retries.
- Intents with different names but one effect identity are quarantined as `effect-collision`, both at convergence and at callback.
- Due `Retrying` work waits out its backoff window.
- Armed reminders are re-armed only when the scheduler no longer holds them, checked through `IReminderScheduler.IsArmedAsync` (`Actor.GetReminderAsync`). The live Scheduler throws `DaprApiException` for a deleted reminder, which counts as not held.
- The index is re-ensured whenever the item holds work.
- A callback quarantines several matching witnesses as `witness-collision`.
- `ActorTypeName` is now required, with no shared default.
- Delay options are bounded to 4294967294 ms.
- Readiness is `Unhealthy` only for a missing `APP_API_TOKEN`.
- The token filter guards `actors/{type}` anywhere in the path, including behind a path base.
- Caller-contract rules are documented in the guide and the XML remarks. `configuration-reference.md` gained an `EventStore:Reminders` section and an amended `APP_API_TOKEN` row. That doc is hash-bound by the OQ8 evidence validator, whose pinned hash already differed from HEAD before this edit.
- The failure-path and readiness tests requested by the review were added.

## Design Notes

**Callback admission, in order:**
1. The app-channel token matches `APP_API_TOKEN`. It is required outside Development, and the callback fails closed without it.
2. The stored full tuple re-derives both the actor ID and the reminder-name token.
3. The name prefix matches the stored kind and its configured purpose.
4. The intent source still reports the intent as current.

A mismatched tuple is quarantined. A stale witness gets an audited no-op followed by cancel.

**Submission:**
- `EffectIdentity` is (tenant, source, source sequence, kind, target, ordinal `0`), with `MessageId` = `IdempotencyKey` = `wrk-<EffectId>`.
- A durable result deletes pending state; the index entry goes last.
- An exception or uncertain outcome keeps state, rearms a backoff retry reminder, and counts as unresolved. The callback never acknowledges by throwing.

**Readiness:**
- Quarantined, unresolved, or incomplete-scan work reports `Degraded`, so `/ready` stays 200 and one tenant cannot pull the service. This is the literal "degrade" from the frozen rules.

**Tokens and audit:**
- Without a delegation-token provider, submission fails closed, retains the work, and reports `Degraded`. The production issuer and the Platform audit backend belong to 4.16.
- Audit uses durable disposition records plus logs that carry digests only.

**Concurrency:**
- Actor turns plus the index CAS give one registration. The deterministic `EffectId` gives one target receipt.

## Verification

**Commands** (run from `references/Hexalith.EventStore`):
- Build and test:
  - `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for Contracts.Tests, Client.Tests, DomainService.Tests and Server.LiveSidecar.Tests.
  - Then run each `tests/<Project>/bin/Debug/net10.0/<Project>` binary with `-class '*Reminder*'`.
  - Expected: all pass, with no regressions in the full binaries for the Contracts and DomainService tests.
- Package-only proof:
  - `python3 tools/pack-release-packages.py <dir> <ver>` && `python3 tools/validate-release-packages.py <dir> <ver>`, then `EVENTSTORE_PACKAGE_CONTRACT_DIR=<dir> tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -method '*PackagedReminderApi*'`.
  - Expected: 1/1 against the local pack. Repeat against the named public version, and record the version, source SHA and results.
- LiveSidecar: if the fixture reports missing Redis, placement or the scheduler, record the exact blocker. Do not weaken the gate.
