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

### Review Findings

- [x] [Review][Patch] Retain the obsolete witness until Scheduler cancellation and audit succeed [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:553`]
- [x] [Review][Patch] Hosted reconciler retry cadence lacks behavioral coverage [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/ReminderReconcilerTests.cs:69`]
- [x] [Review][Patch] Scheduler lookup-failure re-arm path lacks coverage [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1162`]
- [x] [Review][Patch] Audit write failures release cancellation and quarantine evidence [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:553`]
- [x] [Review][Patch] Convergence uses a stale clock when arming a future intent [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:129`]
- [x] [Review][Patch] Whitespace `DAPR_APP_ID` defeats the application-name fallback [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreReminderServiceCollectionExtensions.cs:67`]
- [x] [Review][Patch] Malformed persisted collection elements strand work or terminate reconciliation [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:492`]
- [x] [Review][Patch] Retrying work skips lost-reminder recovery during backoff [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:603`]
- [x] [Review][Patch] Actor-collision convergence leaves restored state undiscoverable [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:685`]
- [x] [Review][Patch] Existing-state fail-closed errors are acknowledged from a stale snapshot [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:142`]
- [x] [Review][Defer] Potential Scheduler-callback origin bypass (unverified high) [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCallbackTokenFilter.cs:38`] — deferred: settle in Story 4.16 with a cross-application Dapr invocation test under the production mTLS/ACL profile, proving whether another workload can reach the reminder callback through the target sidecar with its automatically injected app-channel token.

#### Rejected

- `low` — Rechecking `DueUtc` in callbacks would contradict the approved design that Scheduler owns timing; an early callback requires Scheduler clock skew or privileged/misconfigured route access, so another admission branch is not justified here.
- `false` — The cited `WaitAsync(cancellationToken)` does not exist in `DaprReminderActorInvoker`, and such a call would cancel the caller's wait rather than make shutdown wait for the abandoned proxy call.
- `false` — Dapr.Actors 1.18.10 validates reminder `DueTime` from zero through `TimeSpan.MaxValue`; the 4,294,967,294-ms ceiling applies to `Task.Delay`-backed host/configuration delays, not actor reminder due times.
- `low` — The tenant registry does retain empty tenants, but pruning/sharding is explicitly deferred and its fix is disproportionate at the story's synthetic scale.
- `low` — Per-item intent and payload limits would defend against a buggy trusted in-process source, not an exposed caller; adding quota/configuration surface is disproportionate without a demonstrated workload bound.
- `low` — A second `AddEventStoreReminders<TIntentSource>` call with a different source is an uncommon host-programming error; changing the presence-keyed registration contract is disproportionate for this story.

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

Review iteration 1 (2026-09-30). Reviewers: verification-gap (VG), blind hunter (BH), edge-case hunter (EC). Every filed finding is triaged before root-cause grouping.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| R1-VG1 | Blank `DAPR_APP_ID` fallback is not verified through DI composition | low | Pre-verified: the helper is tested directly, but no test resolves registered options while the environment value is blank | patch |
| R1-VG2 | Stale-callback cancellation-failure retention is untested | medium | Pre-verified: stale success and audit failure are covered, but `RetireStaleAsync` has no `CancelFailure` regression | patch |
| R1-VG3 | Fail-closed catch re-indexing is not observed | medium | Pre-verified: the reloaded-state test begins with an indexed item and does not prove the catch restores a missing candidate | patch |
| R1-VG4 | A null persisted candidate can terminate reconciliation | medium | `candidate.ActorId` is read before the per-candidate exception boundary; the existing corrupt-index test covers only tenant mismatch | patch |
| R1-EC1 | Duplicate callbacks bypass retry backoff | medium | `HandleCallbackCoreAsync` submits a current `Retrying` witness without checking `UpdatedAt + Backoff`, unlike convergence | patch |
| R1-EC2 | Submitted-receipt cancellation failure releases the witness and readiness | medium | `SettleAsync` returns `null`, persistence removes the witness, and `RunSchedulerAsync` ignores the failed cancellation result | patch |
| R1-EC3 | Registration audit failure is ignored | medium | The future-witness path persists `Armed` even when the `Registered` disposition write returns `false`, so it never retries the audit | patch |
| R1-EC4 | A null persisted candidate faults the pass before its guard | medium | Verified at `ReminderReconciler`: dereference occurs before `try`; same root cause as R1-VG4 | patch |
| R1-EC5 | A candidate whose actor ID does not rederive from its tuple is trusted | medium | Reconciliation observes the stored ID but invokes a registrar that derives a different ID, allowing the corrupt row to remain while readiness tracks the wrong actor | patch |
| R1-EC6 | Duplicate persisted reminder names can execute twice | medium | `LoadAsync` accepts both records and the due-work loop submits both; the settlement dictionary only collapses the later persisted update | patch |
| R1-EC7 | `Attempts == int.MaxValue` overflows on retry | low | The arithmetic is unchecked, but reaching this value requires corrupted state or millennia of retries; a new saturation branch is disproportionate alone | reject |
| R1-EC8 | A manually mapped callback-only actor route suppresses all other actor routes | low | This requires a host to imitate one Dapr route without mapping the actor handler set; the resulting setup error fails loudly and broader route inference adds complexity | reject |
| R1-EC9 | Downstream path rewriting can bypass the early token filter | low | A custom middleware would have to rewrite a benign path into a reminder actor path after the startup filter; normal Dapr and path-base routing do not do this | reject |
| R1-EC10 | Story 6.1 P1R 3.109 evidence projects are absent from the packaging-validator exclusion list | medium | The tracked version-pinned evidence projects are intentionally outside CPM and make the repository-wide packaging authority test fail; this is unrelated post-baseline work | defer |
| R1-EC11 | Story 6.1 public-package evidence subprocesses have no timeout | low | Network calls are bounded, but `git`, `dotnet`, or the validator can hang the evidence replay; this is unrelated post-baseline work | defer |
| R1-EC12 | Story 6.1 rollback probe does not URI-escape its key prefix | low | Reserved characters can change the state read path, although documented evidence uses safe prefixes; this is unrelated post-baseline work | defer |
| R1-BH1 | Story 6.1 P1R 3.109 evidence breaks the packaging authority validator | medium | Same verified post-baseline defect as R1-EC10 | defer |
| R1-BH2 | Persisted-entry validation accepts structurally invalid tuples | medium | Nonblank checks do not validate the reminder token, supported kind, UTC timestamps, positive coordinates, or payload digest before normal processing | patch |
| R1-BH3 | Duplicate persisted reminder identities can submit twice | medium | Same verified root cause as R1-EC6 | patch |
| R1-BH4 | A null tenant candidate terminates the reconciliation pass | medium | Same verified root cause as R1-VG4 and R1-EC4 | patch |
| R1-BH5 | A mismatched candidate actor ID is not rejected | medium | Same verified root cause as R1-EC5 | patch |
| R1-BH6 | Submission releases state before Scheduler cancellation succeeds | medium | Same verified root cause as R1-EC2 | patch |
| R1-BH7 | Armed state can permanently lack its `Registered` audit | medium | Same verified root cause as R1-EC3 | patch |
| R1-BH8 | Dapr actor invocation cannot be cancelled after proxy dispatch | false | This repeats the prior rejected actor-wait claim: cancelling only the caller wait abandons an in-flight actor call and does not make the actor operation cancellable | reject |
| R1-BH9 | A partial restore that omits all discovery entries strands item state | maybe-false | The code cannot rediscover an item absent from both registry and candidate documents, but no approved restore model exists yet; Story 4.16's restore drill must establish whether such a state is reachable | defer |
| R1-BH10 | Actor-collision evidence does not retain the colliding coordinates | low | Production operator disposition and the restore gate are explicitly deferred to 4.16; v1 safely retains a digest and fails closed, so adding disclosed coordinates now is disproportionate | reject |
| R1-BH11 | A translator change across deployment can alter a pending command under one effect identity | false | The approved contract makes the current stream and deterministic versioned domain translator authoritative; EventStore intentionally persists the intent tuple and payload digest, not a duplicate derived command | reject |
| R1-BH12 | Live-sidecar evidence does not prove two-host reminder failover | false | The evidence table assigns restore/HA races to persisted fake-store tests (`TwoHostsRacingRegisterOnce`) and assigns the live test only restart replay and Scheduler recovery; it makes no live-HA claim | reject |
| R1-BH13 | The Story 4.11 patch contains unrelated Story 6.x work | false | The preserved baseline predates later mainline commits, so the review artifact contains them; the current working changes are limited to the reminder implementation/tests and this spec | reject |

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

**Patches applied for review iteration 1 (2026-09-30), in place, no loopback:**
- Obsolete and stale witnesses now remain durable until their audit and Scheduler cancellation succeed; failed quarantine audits retain the executable witness and do not cancel it.
- Retry backoff reconciliation repairs a lost Scheduler reminder without submitting early, and lookup failures explicitly exercise the idempotent re-arm path.
- Scheduling reads a fresh clock after the authoritative stream fold. Blank `DAPR_APP_ID` values fall through to the host application name.
- Malformed persisted collection elements are converted into durable, audited quarantine evidence. Actor collisions re-index the stored target so restored state remains discoverable.
- Existing-state fail-closed handling reloads the durable state and re-ensures its candidate before returning an unresolved result.
- Duplicate callbacks now honor retry backoff. A submitted receipt remains pending until Scheduler cancellation succeeds, and a failed `Registered` audit retains retryable state.
- Persisted entries are validated against their full durable tuple before use; invalid and duplicate reminder/effect identities are quarantined and audited instead of executed.
- Reconciliation isolates null or actor-mismatched candidate rows, marks the pass incomplete, and continues scanning valid work.

**Final verification after review iteration 1 (2026-09-30, Debug, `-m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0`):**
- Contracts.Tests, Client.Tests, DomainService.Tests, and Server.LiveSidecar.Tests all build with 0 warnings and 0 errors.
- `Contracts.Tests -class '*Reminder*'`: 26 passed, 1 skipped because no public-package directory was supplied. `Client.Tests -class '*Reminder*'`: 0 discovered; the Client reminder surface is exercised through the DomainService tests and package probe.
- `DomainService.Tests -class '*Reminder*'`: 101/101. Full binary: 261 passed, 1 failed; the unchanged failure is `TenantsDomainService_DoesNotReferenceGeneratedApiHostOrDeclarePerMessageControllers`, whose subject is the intentionally uninitialized nested `references/Hexalith.Tenants` repository.
- `Server.LiveSidecar.Tests -class '*Reminder*'`: 1/1 against Redis, placement, and Scheduler, including restart receipt replay and Scheduler re-arm.
- Local package proof: `3.110.0-local.430`; packing and validation produced 14 valid packages, and `PackagedReminderApi` passed 1/1 against that pack.
- Full `Contracts.Tests`: 2115 passed, 51 failed, 2 skipped. The failures remain repository-wide packaging/evidence environment issues: uninitialized nested repositories and pinned Builds SHAs, OQ8 gate-input drift, OCI/release evidence, plus the deferred Story 6.1 P1R 3.109 validator exclusions. The reminder-focused and package-only proofs pass.
- **Required before `done`:** after the owner publishes a named public EventStore version, rerun `PackagedReminderApi` against it and record the version, source SHA, and result. No release was published by this workflow.

Review iteration 2 (2026-10-01). Reviewers: blind hunter (BH), edge-case hunter (EC), verification-gap (VG). Each finding is judged on its own before grouping.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| R2-BH1 | Every convergence rewrites a disposition and logs `200207` for historical quarantine records | medium | carried. The chunk-1 defer already records that every convergence rewrites a disposition for each existing quarantine record (`ReminderCoordinator.cs:750-770`). `AddQuarantine` dedupes the record; the later loop still audits and logs every record so a failed audit retries. Stopping that needs a new audited flag, which the AD-28 retention gate owns | defer |
| R2-BH2 | A blank stored domain assigns `effectId = ""`, so two or more entries are quarantined as `stored-entry-duplicate` | low | `TryValidatePersistedEntry` returns true with an empty effect id when `state.Domain` is blank (`ReminderCoordinator.cs:480-485`), and `LoadAsync` groups on that id (`1518-1520`). Registration rejects a blank domain (`127-129`), so only corrupt restored state with two or more entries meets this. Those entries are quarantined, not executed. Excluding empty ids is an extra guard | reject |
| R2-BH3 | A blank tenant in the registry leaves every later pass incomplete | medium | `ListCandidatesAsync` throws `ArgumentException` on a blank tenant (`ReminderIntentIndex.cs:112`). The reconciler catch counts that as incomplete and continues (`ReminderReconciler.cs:66-71`), and nothing removes the registry row. A null candidate in the same pass is the carried R2-EC6 case | defer |
| R2-BH4 | Actor-collision re-index of a different tenant or aggregate leaves later passes incomplete | medium | carried. `QuarantineActorCollisionAsync` re-indexes `persisted.Tenant/Domain/Aggregate` (`ReminderCoordinator.cs:960-965`). When that tuple does not re-derive the actor id, `ReminderReconciler` still counts `actor-id-mismatch` and continues (`87-91`). The chunk-1 defer already assigns operator repair to Story 4.16 | defer |
| R2-BH5 | One hung actor invocation blocks the rest of the reconciliation pass | maybe-false | `DaprReminderActorInvoker.ConvergeAsync` checks cancellation only before `CreateActorProxy` and then awaits the proxy with no token (`DaprReminderActorInvoker.cs:26-30`). `ReminderActor` uses `CancellationToken.None`. Whether that call can hang forever depends on the Dapr HTTP timeout, which this change does not set. A timeout that throws is already caught per candidate | defer |
| R2-BH6 | A fail-closed persist after Scheduler cancellation strands the witness with no later callback | false | `SettleAsync` cancels before `SettleCallbackAsync` persists (`1271-1232`). A failed persist throws `state-conflict` and leaves the previous witness in the store. The next pass submits a due witness again (`804-835`). `CancelFailed` is logged only when cancellation throws, which is the path where the reminder was not confirmed removed | reject |
| R2-BH7 | The runbook omits `domain-invalid` and `arm-failed` | low | The callback quarantines a blank domain as `domain-invalid` (`1030-1034`). Arming failure stores `arm-failed` (`896`). The guide lists neither code (`275-291`) | patch |
| R2-BH8 | The host sample never sets `Purposes` | medium | The sample only assigns `ActorTypeName` (`typed-reminders.md:191`). The options table says `Purposes` has no default and a missing purpose is denied (`216`). A host copied from the sample retains every due witness as `purpose-unconfigured` | patch |
| R2-BH9 | Registration step 7 disagrees with keeping the candidate after a receipt | false | Step 7 removes state and the index only when the item holds nothing more (`typed-reminders.md:109`). The callback section says to re-fold and retain discovery while the stream still reports intents (`156-157`). Those sentences describe the same rule | reject |
| R2-BH10 | `GetCurrentIntentsAsync` does not document a null fold | low | The interface says an empty list means the stream holds nothing (`IReminderIntentSource.cs:29-30`). `ConvergeAsync` treats null as `source-unavailable` and does not cancel (`151-154`) | patch |
| R2-BH11 | The spec still says in progress, and the package test skips without a package directory | false | The status change is this review step. `PackagedReminderApiRunsWithoutWorksTypes` is written to skip until `EVENTSTORE_PACKAGE_CONTRACT_DIR` is set, and the run that set it passed 1/1. The named public version remains the close gate the frozen release decision places after review. Fixing the narrative would edit this spec | reject |
| R2-BH12 | The Story 6.1 rollback probe interpolates its key prefix into the state URI | low | carried. R1-EC12. The probe still writes the prefix raw into the read URI. Unrelated post-baseline Story 6.1 work | defer |
| R2-BH13 | `verify_public_packages.py` waits on `git` and `dotnet` with no timeout | low | carried. R1-EC11. `run_command` still uses `subprocess.run` without a timeout. Unrelated post-baseline Story 6.1 work | defer |
| R2-EC1 | Two blank-domain entries share an empty effect id and are not recreated | low | Same code as R2-BH2. They become `stored-entry-duplicate` quarantine records. Registration cannot create a blank domain, and quarantine is the fail-closed result | reject |
| R2-EC2 | A blank stored domain is classified as an actor collision | false | `IsSameTarget` includes domain (`225-228`), so a blank stored domain is not the incoming target and `QuarantineActorCollisionAsync` runs (`142-145`). That is the collision rule. A callback against the stored witness still quarantines it as `domain-invalid` (`1030-1034`) | reject |
| R2-EC3 | A retrying callback returns before backoff ends without re-arming | low | `HandleCallbackCoreAsync` returns `Retrying` and leaves the periodic reminder in place (`1101-1104`). The next fire is that reminder's `RetryMaxDelay` period. The witness stays. Re-arming the remaining backoff would add another schedule | reject |
| R2-EC4 | A blank callback name returns without cancellation | low | `HandleCallbackAsync` returns null when the name is blank (`195-198`). This runtime arms only codec reminder names. Cancelling a name this coordinator never registers adds a branch for a scheduler reminder it did not create | reject |
| R2-EC5 | An actor type name already registered to another type is left in place | low | carried. The options callback registers `ReminderActor` only when the name is absent (`EventStoreReminderServiceCollectionExtensions.cs:109-115`). The earlier reject stands: a proxy call to a non-reminder actor fails loudly, and a second host-programming registration is not an everyday path | reject |
| R2-EC6 | A null or actor-mismatched candidate leaves every later pass incomplete | medium | carried. Null candidates and actor-id mismatches still increment `incomplete` and continue (`ReminderReconciler.cs:79-91`). The chunk-1 defer already keeps those rows for Story 4.16 operator repair because deleting a mismatched row can discard the only stored coordinates | defer |
| R2-EC7 | A later `AddEventStoreReminders` call with another intent source is ignored | low | carried. The second call returns after layering configuration (`EventStoreReminderServiceCollectionExtensions.cs:44-52`). The earlier reject stands: this is an uncommon host-programming error, and the method documents that second call as configuration-only | reject |
| R2-VG1 | A null intent element has no failing test | low | Pre-verified. `ValidateIntent` returns `intent-missing` (`242-244`) and convergence quarantines that digest (`571-578`). `MalformedIntentIsQuarantinedNotDropped` never passes a null element, so replacing the null branch with `continue` keeps the suite green | patch |
| R2-VG2 | A later callback of an already quarantined witness has no failing test | medium | Pre-verified. `HandleCallbackCoreAsync` returns `Quarantined` without submitting when status is already `Quarantined` (`1023-1027`), and cancellation is best-effort. `TranslationFailureIsQuarantined` fires only once, so deleting that status return keeps the suite green and a second firing would submit | patch |

**Patches for review iteration 2:** add `domain-invalid` and `arm-failed` to the guide's reason lists; set a purpose in the host sample; document a null intent fold on `IReminderIntentSource`; add the null-intent and second-firing quarantine regressions.

Continuation review after iteration-3 patches (2026-10-01). Reviewers: blind hunter (BH), edge-case hunter (EC), verification-gap (VG). EC returned no findings. The preserved Works baseline includes earlier committed context and submodule advances; the current EventStore continuation is reviewed separately in the same artifact. Each finding was judged before grouping.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C3-BH1 | A registry with a null tenant collection is accepted as empty and prunes recorded readiness | medium | `ReminderIntentIndex.ListTenantsAsync` coalesces a present `ReminderTenantRegistry(null)` to `[]`; the complete empty pass then removes recorded unresolved items. Distinguish an absent document from a malformed present collection, retaining evidence and marking the pass incomplete. | patch |
| C3-BH2 | Backoff can never reach a configured maximum when the delay ratio exceeds 2^20 | low | `MaxBackoffExponent = 20` caps a valid 1-ms/1-hour configuration at 1048.576 seconds. The direct exponent-bound correction and a high-ratio regression add no public surface. | patch |
| C3-BH3 | Capacity recovery test does not prove caller redelivery registers the rejected item | low | The registrar contract explicitly requires retry after a throw, so the reconciler is not responsible for discovering that item. Extend the existing regression to exercise that caller obligation and a durable receipt after capacity returns. | patch |
| C3-BH4 | Capacity logs lack a tenant document digest and occupancy | low | The new log identifies the failure class and readiness counts identify incomplete discovery. Tenant registry inspection can locate the full document; adding log parameters/digests for synthetic-only diagnostics is disproportionate to the demonstrated harm. Production operator tooling remains under the existing 4.16 gate. | rejected |
| C3-BH5 | Capacity test does not distinguish >= from == for an over-capacity restored document | low | The new production guard explicitly handles counts above a reduced limit, but the test only supplies count == limit. Add a restored/over-capacity case and assert all existing candidates still converge. | patch |
| C3-BH6 | Complete-pass cadence test can pass if the loop never starts a second complete pass | low | The new test proves the requested negative interval window, and the existing incomplete-pass test proves looping, but neither observes the next complete pass. Replace the wall-clock window with controlled timer firing and assert normal-interval scheduling plus a second completed pass. | patch |
| C3-BH7 | Guide introduction still claims every cancellation first re-folds the stream | low | Orphaned, already-quarantined, and structurally invalid callbacks cancel from persisted state; the corrected coordinator summary already documents these exceptions. Correct the guide opening directly. | patch |
| C3-BH8 | Retry runbook says every retained witness resubmits a command | low | Obsolete witnesses retry audit/cancellation at the obsolete loop, and invalid-domain witnesses retry quarantine auditing. Explain these paths separately from submission retries. | patch |
| C3-BH9 | Earlier epic-context rewrite omitted explicit persist-before-publication wording | low | The omission exists in an earlier committed context refresh; this run did not change that file. The full architecture remains authoritative and loaded by the implementation handoff. Context refresh is deferred under the workflow rule for agent-context edits. | defer |
| C3-BH10 | Spec frontmatter, verification status, and review-loop counter must be aligned | false | The recorded implementation verification preceded the workflow transition to `in-review`; historical phase status is preserved. `review_loop_iteration` counts automatic loopbacks, not the manually numbered earlier reviews, and no loopback occurred. The proposed fix also edits this build's spec, which the workflow rejects as a review finding. | rejected |
| C3-VG1 | Runtime receipt tests cannot distinguish source aggregate from target aggregate | medium | Pre-verified: all inspected runtime fixtures assign both coordinates to the same aggregate; substituting the target in `CreateEffectIdentity` would evade them and collapse independently sourced intents. Add distinct-source intents with equal target/sequence/kind and independently expected persisted receipt identities. | patch |
| C3-VG2 | Guide introduction contradicts cancellation paths | low | Independently confirmed at the guide opening and orphan/quarantine callback paths; same root cause as C3-BH7. | patch (with C3-BH7) |
| C3-R1 | A present candidate document with a null collection also becomes an empty complete scan | medium | Root-agent verification: `ListCandidatesAsync` uses the same null coalescing as the registry, so a malformed present candidate list can prune recorded readiness. The existing removal test demonstrates that stored shape; leave removal behavior intact, but make scanning incomplete and preserve the durable document. | patch (with C3-BH1) |

Continuation review C4 (2026-10-01). Three review layers completed. The platform thread limit required running verification-gap after blind-hunter on the same context-free review-only agent; the implementation agent did not review its own code. The full workspace-baseline diff and the final EventStore continuation, including the untracked environment collection, were staged in `/tmp/bmad-build-4-11-review-45gnw5pt/all-changes.diff`. Each of 15 findings was judged before grouping.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C4-BH1 | Null source folds keep normal cadence despite the new unreadable-work prose | low | The probe and ConvergeAsync confirm persisted fail-closed work returns Unresolved, not Incomplete. The frozen intent requires periodic recovery and Degraded readiness, not fast retries for every retained outcome. Correct the prose and pin null-fold cadence without changing that behavior. | patch |
| C4-BH2 | Actor-type regex accepts a trailing newline | low | The runtime probe reproduces the $ end-anchor exception. Replace it with an absolute end anchor and add an invalid-options regression; the documented actor-name alphabet already excludes newlines. | patch |
| C4-BH3 | Sub-millisecond positive delays can remove the hosted pause | low | The probe confirms Task.Delay truncates this unusual tick-valued configuration. Adding a new minimum or rounding policy would narrow the approved positive-delay contract and add validation or scheduling logic for an uncommon configuration. | reject |
| C4-BH4 | Public option XML omits capacity-only cadence | low | RetryInitialDelay and ReconciliationInterval still describe the previous all-incomplete rule. Update both summaries to match the implemented capacity distinction and effective minimum. | patch |
| C4-BH5 | Guide omits witness-name uniqueness | low | The Client interface documents the schedule-name constraint, but the caller-contract guide only names effect uniqueness. Add the same witness rule and source/payload revision obligation. | patch |
| C4-BH6 | Guide omits lifetime effect-identity reuse rule | low | The interface distinguishes retrying one logical submission from distinct committed source coordinates for a new submission. Add that distinction to the caller-contract guide. | patch |
| C4-BH7 | Cleanup log loses its exception type and the guide overstates event 200214 fields | low | FailedClosed currently has only actor and reason fields; the cleanup catch discards the previously available bounded type. Preserve that type through the internal log event and pin its emitted contract. | patch |
| C4-BH8 | Runbook lacks candidate-only source-unavailable recovery | low | A cleanup fold can fail after witness release while retaining discovery and unresolved readiness. Add an operator action that does not require a witness LastReasonCode. | patch |
| C4-BH9 | Guide omits the minimum of retry delay and interval | low | ExecuteAsync uses RetryInitialDelay only when it is shorter than ReconciliationInterval. Correct both guides and cover a longer valid retry delay with controlled timers. | patch |
| C4-BH10 | Scheduler-only production wording also excludes convergence calls | low | DaprReminderActorInvoker invokes ConvergeAsync on the same actor type. Restrict the Scheduler-only requirement to callbacks and describe trusted convergence admission without prescribing a new production policy. | patch |
| C4-EC1 | Registration relabels a tenant-mismatched candidate document | low | Carried from iteration-3 chunk A BH7: this requires corrupt stored state; its foreign actor identifiers fail rederivation during scanning and keep readiness Degraded. The extra guard remains rejected as a rare-state extension. | carried reject |
| C4-EC2 | A stale callback can submit a due replacement | false | Carried from iteration-0 EC14: only the stale witness is the audited no-op. Replacement convergence submits independently current intents after admission, as explicitly recorded in Design Notes and the corrected comment. | carried reject |
| C4-VG1 | Changed log fields and cleanup event lack emitted-output tests | medium | Pre-verified: scan/cleanup regressions use NullLogger and cannot distinguish swapped reason/type arguments or a reverted callback event. Add focused tests of actual emitted fields and formatting through runtime failure paths. | patch |
| C4-VG-O1 | Null fold cadence contradicts the new guide | low | Independently reproduces C4-BH1. Correct the overbroad prose and test the preserved retained-outcome cadence rather than changing the approved fail-closed result. | patch |
| C4-VG-O2 | Event 200214 schema disagrees with the guide | low | Independently confirms C4-BH7. Preserve bounded exception-type evidence and verify the resulting event schema. | patch |

Continuation review C5 (2026-10-01). All three context-free review layers completed: blind-hunter (BH, 10 findings), edge-case-hunter (EC, 5 findings), and verification-gap (VG, no gaps). The artifact `/tmp/bmad-build-4-11-resume-ek8r3wwx/all-changes.diff` contains the complete workspace-baseline diff, the current EventStore continuation including untracked tests, and expanded reminder changes for the remaining split-review groups 3–5. Each finding below was judged before grouping. The current continuation matches every corresponding file in the final C4 review artifact, so the existing `.442` package inventory remained valid before these new patches.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C5-BH1 | Same-source rescheduling after an uncertain committed receipt changes causation and cannot replay | medium | `SubmitAsync` uses the schedule name for both delegation and submission causation; the name changes on reschedule while EffectId remains stable. `AggregateActor.ReceiptMatches` requires unchanged causation and rejects that replay. The fake currently compares only EffectId. Use stable logical-effect causation on both calls, document it, and prove replay with a faithful receipt matcher and the real target. | patch |
| C5-BH2 | Complete-pass pruning discards unresolved work registered after discovery was read | medium | `RunPassAsync` snapshots discovery before actor convergence; a concurrent registration can record a new pending witness while the old candidate list omits it. `CompletePass` then removes its host-local status. Retain records updated during the pass and add a controlled runtime interleaving regression. | patch |
| C5-BH3 | Restored quarantine reasons can enter logs verbatim | low | `IsValidPersistedQuarantine` accepts any nonblank reason. Normal runtime writes use bounded constant reasons; confidential text requires malformed or manually altered stored state. A new reason allowlist and repair path add guards for that rare state, so the proposed expansion is rejected. | reject |
| C5-BH4 | Malformed candidate actor IDs can enter logs verbatim | low | The mismatch and catch logs print stored IDs; ordinary discovery writes use derived `wra-` digests. Confidential or arbitrarily long IDs require corrupted or manually altered discovery. Additional log validation is disproportionate for this rare state. | reject |
| C5-BH5 | No Expiry end-to-end runtime case | low | carried: iteration-0 BH15 explicitly rejected this extra case because the closed kind map, both codec vectors, and generic runtime composition cover the branch. The runtime still treats both kinds through the same code. | carried reject |
| C5-BH6 | Distinct-source regression does not distinguish source and target domains | low | The current test varies SourceAggregate but keeps SourceDomain equal to the target domain. Extend the existing independent-identity assertions with different source domains so substituting the target domain fails. | patch |
| C5-BH7 | Same-actor racing test does not exercise independent actors contending on discovery CAS | low | `TwoHostsRacingRegisterOnce` deliberately serializes one actor; the exhaustion test pins refusal but does not prove successful retry retains another item's candidate or tenant. Add forced interleavings for same-tenant candidates and different-tenant registry writes, asserting the accepted durable discovery and item state. | patch |
| C5-BH8 | Health data omits scan freshness | low | LastPassAt is held internally and pass completion is logged, but health data does not expose it. The recorded contract describes a per-host view from the last completed pass, not a freshness guarantee. A stalled invocation is already deferred to 4.16; adding health output or a freshness policy for that condition is rejected here. | reject |
| C5-BH9 | Options validation hides detailed startup errors | low | carried: iteration-0 BH11 and iteration-3 chunk-A BH10 retain the generic startup validation decision. The options Validate method still exposes the individual errors, and replacing options validation adds another type for a setup-only diagnostic. | carried reject |
| C5-BH10 | Removed effect-ID grouping leaves unused validation tuple bookkeeping | low | LoadAsync still carries an EffectId tuple member and validation out parameter solely discarded after the grouping removal. Delete that unused output/bookkeeping while retaining effect-identity validation and blank-domain behavior. | patch |
| C5-EC1 | A pass prunes unresolved work registered after its snapshot | medium | Independently confirms C5-BH2 at CompletePass; same runtime interleaving and correction. | patch (with C5-BH2) |
| C5-EC2 | An existing different actor implementation suppresses reminder registration | low | carried: R2-EC5 and iteration-3 chunk-A EC12 already reject this host-programming collision. Registration skips the duplicate, but a convergence proxy to a non-reminder actor fails loudly; no ordinary reminder work is silently accepted. | carried reject |
| C5-EC3 | Positive sub-millisecond delays can busy-loop | low | carried: C4-BH3 already reproduced Task.Delay truncation and rejected narrowing the positive-delay contract with a new minimum or rounding policy for this unusual tick-valued configuration. | carried reject |
| C5-EC4 | Malformed stored actor IDs may disclose confidential text in logs | low | Independently confirms C5-BH4; normal rows carry derived digests and the proposed guard covers manually altered or corrupted discovery rather than ordinary runtime input. | reject |
| C5-EC5 | Stale callback convergence can submit an overdue replacement | false | carried: iteration-0 EC14, C4-EC2, and the resolved group-2 stale comment distinguish the stale witness's audited no-op from independently current replacement convergence. The frozen recovery rule and Design Notes explicitly permit that convergence. | carried reject |

Continuation release-closure review (2026-10-04). All three context-free layers completed before triage: blind hunter (BH, ten findings), edge-case hunter (EC, five findings), verification gap (VG, one pre-verified gap). The review artifact includes the preserved Works baseline diff and expanded EventStore reminder changes. Each finding is recorded before grouping; previously adjudicated claims retain their existing routes.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C6-BH1 | Candidate writes omit the stored tenant-header guard | low | Carried C4-EC1 / iteration-3 chunk-A BH7: only corrupt stored headers reach this; foreign actor tuples fail discovery rederivation and keep readiness Degraded. A cleanup guard would extend the same rare corruption case. | carried reject |
| C6-BH2 | Another actor implementation can occupy the configured type name | low | Carried C5-EC2 / R2-EC5: this host-programming collision fails loudly on the registrar proxy call; the earlier rejection remains applicable. | carried reject |
| C6-BH3 | Normalizing null item collections retains no corruption record | low | Carried split-group-2 BH6: this requires corrupt stored state; discovery survives and a later stream fold rebuilds witnesses. The proposed persisted corruption branch remains rejected. | carried reject |
| C6-BH4 | Live proof does not exercise an automatic Scheduler firing | low | The live proof deliberately invokes callbacks directly, as the approved execution task specifies. No Scheduler delivery or token-forwarding defect is demonstrated; adding a timed delivery fixture exceeds a direct correction. Scheduler production admission remains under the existing 4.16 gate. | reject |
| C6-BH5 | Live restart retains synthetic in-memory intents rather than replaying domain events | false | The test and guide claim persisted registration, receipt replay, and Scheduler repair, not a real domain event-fold implementation. The domain implements the intent source; Works adoption is explicitly assigned to 4.15. | reject |
| C6-BH6 | Disposition writes replace earlier transition evidence | medium | Carried iteration-0 BH10 / iteration-3 BH2-BH15: last-write-wins disposition retention and the append-only production audit sink already belong to the recorded AD-28/4.16 deferral. | carried defer |
| C6-BH7 | Dispositions and empty tenant rows have no retention/erasure policy | medium | Carried iteration-0 BH10 / iteration-3 BH2-BH15: the existing ledger records retention, TTL, tenant offboarding, and index-row erasure before real-data admission. | carried defer |
| C6-BH8 | Per-item quarantine evidence is unbounded | medium | Carried chunk-1 quarantine-cap deferral: candidate capacity does not bound an item's evidence list; the existing AD-28/4.16 work remains open. | carried defer |
| C6-BH9 | Null or actor-mismatched candidates keep later scans incomplete | medium | Carried R2-EC6 / iteration-3 BH4-EC4-EC3: deleting those rows can discard their only coordinates, so the existing audited operator-repair deferral remains. | carried defer |
| C6-BH10 | The public-package ledger entry still appears unresolved | low | The AC4 entry at deferred-work.md:1185 still describes only local proof, while retained public 3.112.0 XML proves all three consumers passed. Mark that one entry resolved with its public evidence, preserving the historical entry. | patch |
| C6-EC1 | A witness collision masks a third intent's shared effect identity during convergence | medium | ConvergeCoreAsync collapses same-name intents into desired and excludes collided names from effect grouping; a due third name sharing either hidden source tuple can submit. Callback admission examines all valid intents and quarantines the same overlap. Classify effect collisions across all valid source intents before collapsing names; no new public surface or rule is needed. | patch |
| C6-EC2 | Registration overwrites tenant-mismatched candidate headers | low | Carried C4-EC1 / iteration-3 chunk-A BH7, independently matching C6-BH1; the earlier rare-corruption rejection stands. | carried reject |
| C6-EC3 | Corrupted candidate actor identifiers can disclose arbitrary text in logs | low | Carried C5-BH4 / C5-EC4: ordinary discovery writes derived digests; confidential text requires manually altered or corrupted state, and the added validation remains disproportionate. | carried reject |
| C6-EC4 | int.MaxValue retry attempts overflow and later quarantine | low | Carried R1-EC7: reaching that counter requires corrupted state or millennia of retries; the saturation branch remains rejected. | carried reject |
| C6-EC5 | A stale callback can submit a currently due replacement | false | Carried C5-EC5 / iteration-0 EC14: only the stale witness is an audited no-op; independently current replacement convergence is explicitly permitted by Design Notes and the callback comment. | carried reject |
| C6-VG1 | The real actor adapter's Scheduler delay and repeat period are not asserted | medium | Pre-verified: unit delays are observed through FakeReminderScheduler, while the live helper checks only record existence. Swapping dueTime and period in ReminderActor.ArmAsync escapes the current assertions. Extend the existing live proof to verify both Scheduler fields after registration and re-arm. | patch |

Review iteration 4 (2026-10-05). Reviewers: blind hunter (BH), edge-case hunter (EC), verification-gap (VG). Diff: works `613a96c1..` working tree, including untracked `evidence/story-4-11-public-3.113.0/`. No layer was skipped. Each finding is judged before grouping. No intent_gap, bad_spec, or patch survivors.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C7-BH1 | Closing prose says spec and sprint status are `done` while frontmatter is `in-review` and sprint status is `in-progress` | low | The closing sentence overclaims completion. Frontmatter `in-review` is the required review state, and sprint `in-progress` matches an unfinished review. The correction is an edit to this spec. | reject |
| C7-VG1 | Same status contradiction; a workflow reading frontmatter or sprint status leaves the story open | low | Same root cause as C7-BH1. Leaving the story open during review is the workflow state, not a failed close. | reject |
| C7-BH2 | The 3.113.0 evidence is untracked and the consumer XML hunk has no diff header | false | `evidence/story-4-11-public-3.113.0/` holds `summary.json`, `public-package-consumer.xml` (1 passed, 0 skipped), and `public-collision-regression.xml` (24 passed, 0 skipped). The consumer hunk has a `diff --git` header; it sits on the previous XML line because that file has no trailing newline. | reject |
| C7-BH3 | The 3.113.0 summary omits per-archive URLs, hashes, and compared file hashes | low | The summary records version `3.113.0`, nuspec commit `865cd9e`, and the passing XML names. A full hash inventory is a new evidence artifact, not a direct correction, and product callers do not read it. | reject |
| C7-BH4 | The EventStore pin, the preserved baseline, and the published source SHA are unexplained | false | Frontmatter still preserves `613a96c1` and `f378afdb`. The diff records the superproject pin `738da5c9`. `865cd9e` is an ancestor of that pin, and the summary names it as the package source. | reject |
| C7-BH5 | Unrelated submodule pointer moves and the `references/platform` path are unexplained | false | Those gitlinks are earlier commits already on `main` inside the since-baseline range. `.gitmodules` already names the platform submodule `Hexalith.Platform` at `references/platform`. | reject |
| C7-BH6 | `works-assets-audit.json` still shows EventStore `3.112.0` while the summary says the pin is `3.113.0` | false | The audit file is the retained 2026-10-04 `3.112.0` snapshot. `references/Hexalith.Builds/Props/Directory.Packages.props` pins `HexalithEventStoreVersion` to `3.113.0`, and this run did not change that pin. | reject |
| C7-BH7 | The resolved AC4 ledger entry still cites `3.112.0` after the collision failures | false | The resolved entry covers only `PackagedReminderApiRunsWithoutWorksTypes` on public `3.112.0`. It does not claim the C6 collision regression passed there. Public `3.113.0` evidence closes that separate regression. | reject |
| C7-BH8 | Verification still expects green full Contracts and DomainService binaries | low | The command list is the original plan. Later notes record the nested-Tenants failure and the Contracts timeout as environmental, and the frozen close gate is the named public package proof. Changing the command list edits this spec. | reject |
| C7-BH9 | `implementation-verification.md` cites three XML files that are not in the tree | low | `collisions-red.xml`, `coordinator-focused.xml`, and `live-reminders-container-final.xml` are absent. Retained `domainservice-reminders.xml` (179 passed, 0 failed, 0 skipped) and `live-reminders.xml` (passed) cover the same behaviors. The missing `/tmp` files cannot be restored by a direct edit. | reject |
| C7-BH10 | This diff edits `epic-4-context.md` and drops persist-before-publication | low | Carried C3-BH9: the explicit publication-order sentence is still absent, the architecture document remains the loaded authority, and the existing deferred entry already preserves the refresh rule. | carried defer |
| C7-EC6 | Same deleted persist-before-publication sentence | low | Carried C3-BH9 with C7-BH10. Acknowledgement-after-durable-commit remains in the context. Do not defer it again. | carried defer |
| C7-BH11 | The frozen matrix and Spec Change Log have no hunks for the C6 collision or the public close gate | low | The matrix is inside the frozen intent and must stay unchanged. C6 and the `3.113.0` proof are recorded in the verification narrative. A change-log edit is an edit to this spec. | reject |
| C7-BH12 | Restore/HA is marked passed by fake-store races while live two-host failover is deferred | false | The frozen intent requires synthetic proof until the Story 4.16 AD-28 restore drill. The matrix row is covered by the persisted fake-store tests in `domainservice-reminders.xml`. | reject |
| C7-BH13 | `spec-implement-works-ci-cd.md` inserts an open iteration-3 intent-gap table after iteration 5, and its 514/514 TRX lives only under `/tmp` | medium | The table at that spec's later "Review iteration 3 (2026-09-20)" still lists intent-gap rows 72–74, 93, and 103, and the acceptance note cites `/tmp/works-loop2-final-broad.vn9QkM/final-broad.trx`. This document is outside Story 4.11. | defer |
| C7-EC1 | `pack-with-environment-pins.py` raises `IndexError` when arguments are missing | low | `sys.argv[1]` and `sys.argv[2]` are unguarded. This is a one-shot evidence script. An argument check adds a guard, and product callers never run it. | reject |
| C7-EC2 | A failed pack leaves a partial package directory | low | The script writes straight into the output directory and exits on the first non-zero pack. Replacing the directory only after success adds a staging flow. | reject |
| C7-EC3 | An existing file at the output path raises `FileExistsError` | low | `Path.mkdir` is called on the caller-supplied path. A file-versus-directory guard is extra branching on the same evidence script. | reject |
| C7-EC4 | A missing `dotnet` or `OSError` skips the command ledger | low | Only `TimeoutExpired` is caught. Recording `OSError` adds a branch on the same one-shot script. | reject |
| C7-EC5 | A pack timeout can leave an MSBuild node running | low | `subprocess.run` kills the direct child only. Process-group cleanup is additional machinery on the evidence script. | reject |
| C7-EC7 | An unauthorized callback returns 401 without a durable disposition | false | Carried EC13: `OriginDenied` logs event-style reason `200209` and returns an empty 401. Design Notes define token denial as digest-only logs, and a durable write would open an unauthenticated write path. | carried reject |

Review iteration 5 (2026-10-05), after the three C6 documentation patches. Reviewers: blind hunter (BH, 15 findings), edge-case hunter (EC, 5 findings), verification-gap (VG, no findings). No layer was skipped. Diff: `/tmp/story-4-11-diff-9wPgg1`. Each finding is judged before grouping. No intent_gap or bad_spec survivors. Carried defers are not appended again.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C8-BH1 | Closing prose says the spec is `done` and sprint status is `review`, while frontmatter is `in-review`, sprint status is `in-progress`, and the sprint header still lists the three documentation patches as open | low | Carried C7-BH1 for the status values: frontmatter `in-review` and sprint `in-progress` are the review state, and correcting the closing prose would edit this spec. The sprint header comment still said the three patches were open after they were checked off. That comment is corrected in place. | patch |
| C8-BH2 | Label precedence exists only in the dirty EventStore tree and is absent from `738da5c` and public `3.113.0`; the source-match sentence is stale | false | The review diff adds the precedence sentence to `typed-reminders.md`, `IReminderIntentSource`, and `ReminderCoordinator`. `git diff 865cd9e` on those files plus the theory summary is only that uncommitted comment patch. Public `3.113.0` is the prior runtime close. Correcting the historical match sentence would edit this spec. | reject |
| C8-BH3 | The `3.113.0` summary omits archive hashes, and the collision XML does not name package bytes | low | Carried C7-BH3: the summary records version `3.113.0`, nuspec commit `865cd9e`, and the passing XML names. A hash inventory is a new evidence artifact, and product callers do not read it. | carried reject |
| C8-BH4 | `works-assets-audit.json` still lists EventStore `3.112.0` | false | Carried C7-BH6: that file is the retained 2026-10-04 `3.112.0` snapshot. `Directory.Packages.props` already pins `3.113.0`, and this run did not change the pin. | carried reject |
| C8-BH5 | C6 `summary.json` and `source-audit.json` still say the public gate is open | false | Those files are the 2026-10-04 C6 snapshot. The later `evidence/story-4-11-public-3.113.0/summary.json` records the satisfied gate. Rewriting the snapshot would erase the state it captured. | reject |
| C8-BH6 | `implementation-verification.md` cites three XML files that are not in the tree | low | Carried C7-BH9: `collisions-red.xml`, `coordinator-focused.xml`, and `live-reminders-container-final.xml` are absent. Retained `domainservice-reminders.xml` (179 passed, 0 failed, 0 skipped) and `live-reminders.xml` cover the same behaviors. The `/tmp` files cannot be restored by a direct edit. | carried reject |
| C8-BH7 | Design Notes omit source domain and aggregate from the effect tuple and omit `Unhealthy` for a missing `APP_API_TOKEN` | low | The coordinator comment and interface already include source domain and aggregate. Iteration-0 BH9 made a missing token `Unhealthy`. Correcting Design Notes would edit this spec. | reject |
| C8-BH8 | The epic-context rewrite drops persist-before-publication | low | Carried C3-BH9 and C7-BH10: the explicit sentence is still absent, the architecture document remains the loaded authority, and the existing deferred entry already preserves the refresh rule. Not deferred again. | carried defer |
| C8-BH9 | `spec-implement-works-ci-cd.md` still shows an open iteration-3 intent-gap table after iteration 5, with its TRX only under `/tmp` | medium | Carried C7-BH13: the table and `/tmp/works-loop2-final-broad.vn9QkM/final-broad.trx` citation are unchanged. This document is outside Story 4.11. Not deferred again. | carried defer |
| C8-BH10 | Deferred-work uses one absolute `source_spec` and repeats retention, hung-call, and nonconverging-candidate items | low | The repeated topics are the existing C6-BH6, C6-BH7, C6-BH8, C6-BH9, and R2-BH5 defers. Product callers never execute the ledger, and existing deferred entries are not rewritten. | reject |
| C8-BH11 | The Code Map still says EventStore HEAD is `f378afdb` and clean, and the Spec Change Log heading is empty | low | Both corrections edit this spec. Carried C7-BH11 for the empty change log: C6 and the `3.113.0` proof stay in the verification narrative, and the matrix stays frozen. | reject |
| C8-BH12 | The resolved AC4 ledger entry stops at public `3.112.0` | false | Carried C7-BH7: that entry covers only `PackagedReminderApiRunsWithoutWorksTypes` on public `3.112.0`. It does not claim the C6 collision regression passed there. Public `3.113.0` evidence closes that separate regression. | carried reject |
| C8-BH13 | The `3.113.0` close does not tie the 2026-10-04 live XML to source `865cd9e` | false | Against `865cd9e`, the reminder guide, intent-source remarks, coordinator, and theory summary differ only by the uncommitted comment patch (14 insertions, 6 deletions). Scheduler delay and repeat period are unchanged, so the retained live proof still matches the published runtime. | reject |
| C8-BH14 | `pack-with-environment-pins.py` hardcodes the EventStore path, reads `argv` unchecked, and writes packages before the full set succeeds | low | Carried C7-EC1 and C7-EC2 for the missing arguments and partial directory. The hardcoded path is the same one-shot evidence script. Product callers never run it, and parameterizing the path adds a parameter. | carried reject |
| C8-BH15 | `client-reminders.xml` is an empty assembly recorded as a completed check, and several proof XML files have no trailing newline | false | Client `-class '*Reminder*'` discovers zero tests; iteration-1 verification records that as the completed check. The missing newline is the retained XML shape already noted in C7-BH2 and does not change the result. | reject |
| C8-EC1 | `pack-with-environment-pins.py` raises `IndexError` when arguments are missing | low | Carried C7-EC1: `sys.argv[1]` and `sys.argv[2]` are unguarded. An argument check adds a guard on a one-shot evidence script that product callers never run. | carried reject |
| C8-EC2 | A non-timeout error while opening a pack log omits that package from the ledger | low | Carried C7-EC4: only `TimeoutExpired` is caught, and the log `open` sits outside that handler. Recording `OSError` adds a branch on the same script. | carried reject |
| C8-EC3 | An existing file at the output path raises `FileExistsError` | low | Carried C7-EC3: `Path.mkdir` is called on the caller-supplied path. A file-versus-directory guard is extra branching on the same evidence script. | carried reject |
| C8-EC4 | Logs and `pack-commands.json` are written in the output parent, so a second output directory overwrites the ledger | low | `out.parent` is the log and ledger directory. A second run with a sibling output directory can overwrite `pack-commands.json`. Product callers never run this script, and moving the ledger would change the retained evidence layout. | reject |
| C8-EC5 | Rewriting `pack-commands.json` is not atomic | low | `write_text` can leave a truncated ledger if the process stops mid-write. Replacing it through a temporary file adds staging machinery on the same one-shot script. | reject |

**Patch for review iteration 5:** the sprint-status header now says the three C6 documentation patches are applied. Development status is `review`. No EventStore behavior changed, so the recorded reminder, live, and public `3.113.0` checks were not repeated.

Review iteration 6 (2026-10-05) is a chunked full-story re-review. Group 2 (the coordinator core) found 12 patches, 6 carried defers, and 23 rejections. Its per-finding record is the last "Review Findings" section of this spec, with IDs prefixed `C9-`. Groups 1, 3, and 4 remain.

Continuation review C10 (2026-10-05), after the twelve C9 patches. All three context-free layers completed: blind hunter (BH, ten findings), edge-case hunter (EC, none), verification-gap (VG, no gaps). Artifact: `/tmp/bmad-build-4-11-c9-review-uc998hu8/all-changes.diff`, containing the complete preserved Works-baseline diff, current EventStore continuation, and untracked source/evidence. Every finding was judged before grouping. Six independent direct patches remain; no intent_gap or bad_spec survivor.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C10-BH1 | A same-version foreign item write is accepted after the ownership re-read | false | The reproducer directly seeded changed bytes while keeping Version 1; the reviewer confirmed no normal runtime writer can do this. The only coordinator writer increments Version on accepted CAS, and actor turns serialize. A privileged out-of-band writer bypassing that protocol does not demonstrate a concurrent SDK-turn defect. | reject |
| C10-BH2 | Durable JSON fixtures pin default PascalCase instead of the configured Dapr writer | medium | DaprReadModelStore delegates typed writes to DaprClient and reads with that client's JsonSerializerOptions; the default domain host registers AddDaprClient. The new fixtures serialize with different default options. Use configured client options and fixed camelCase documents so the tests actually protect the production format. | patch |
| C10-BH3 | Discovery-document JSON is not pinned | medium | The new fixture class covers item/disposition records but omits ReminderTenantRegistry and ReminderTenantCandidates/ReminderCandidate. Renaming their fields could strand restart discovery while item fixtures stay green. Add fixed writer/reader fixtures for both discovery documents. | patch |
| C10-BH4 | Wrong-prefix callback case also fails the length guard | low | The generated other- prefix has six characters, making the identifier 58 rather than 56 characters. Use an incorrect four-character prefix with the same valid digest, isolating the prefix check. | patch |
| C10-BH5 | No test covers cancellation requested during translation | low | Carried from iteration-3 chunks B/C BH cancellation rejection: the shutdown branch still propagates before settlement/persistence, retaining the witness. Production callbacks use CancellationToken.None. The new behavior is non-shutdown cancellation, covered on both submission paths. | carried reject |
| C10-BH6 | Audit summary conflates independent evidence capture with entry quarantine | low | SettleAsync retains Retrying/audit-unavailable when a translation quarantine's disposition write fails; only ReminderQuarantineRecord capture is independent. Qualify the summary and state the audit-gated entry behavior. | patch |
| C10-BH7 | Invalid-receipt theory does not continue to successful recovery | false | The three invalid values all return the same Retrying/receipt-mismatch outcome before settlement. Existing UncertainReceiptIsRetriedWithoutAcknowledgement and crash/restart receipt replay tests exercise the shared retry and valid-receipt release path; the stored reason is not a recovery branch. Their cases passed in the complete reminder run. | reject |
| C10-BH8 | Sprint comments still say twelve C9 patches are open | low | Both last_updated comments still describe the pre-implementation action items. Update them and distinguish the earlier completed split review from the later C9 remaining groups and public-release gate. | patch |
| C10-BH9 | Retained build commands omit timeout information | low | Implementation-agent clarification: DomainService used shell timeout 180s, which its command entry omits; the other three used Python subprocess.run timeout=180 with correctly recorded bare dotnet command arrays. Correct the DomainService array and record Python timeout metadata without inventing shell wrappers. | patch |
| C10-BH10 | Local dirty-tree proof lacks source/package hash manifests | low | The evidence explicitly identifies an uncommitted local pack and does not claim an immutable public release. Closure still requires owner publication and version/source-SHA proof. A new archival hash inventory adds artifacts without fixing current behavior or a claimed public proof; the earlier public-hash omission was also rejected as low. | reject |

Continuation review C11 (2026-10-05). All three context-free workflow layers completed over the complete preserved Works-baseline diff plus EventStore reminder API, runtime, coordinator, tests, package probes, and guide changes through `7e19e60b4510551dc3285c567d70c54e92f23096`. This covers the remaining current full-story groups 1, 3, and 4. Each of 23 findings was judged individually before grouping: nine patch groups (eleven findings), two pre-existing shared-SDK deferrals, and ten carried rejections. Parent acceptance/matrix audit confirms all five frozen behavior rows have executed passing coverage; the owner public-release gate remains open.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C11-BH1 | Tenant-mismatched discovery is relabeled on write | low | Carried C4-EC1 / split-review chunk A BH7: unchanged write lambdas relabel only corrupt discovery; foreign actor identifiers fail scan re-derivation and retain Degraded readiness. Do not add the previously rejected rare-state guard. | carried reject |
| C11-BH2 | Slow failed submission consumes its stored retry window | medium | Convergence and callbacks pass a pre-submission clock to settlement; Retry persists it after an awaited failure. The reproduced 40-second failure with a 30-second backoff permits immediate resubmission. Timestamp retry state when the failed operation finishes. | patch |
| C11-BH3 | Slow Scheduler lookup delays future re-arming | medium | The per-entry clock precedes awaited IsHeldAsync, and future arming subtracts that stale instant. Refresh after lookup and clamp a crossed deadline to zero; the same correction applies to a backoff reminder being repaired. | patch |
| C11-BH4 | Callback discards malformed sibling intents before successful cleanup | medium | Callback filters the current fold with Where(ValidateIntent is null) and records no invalid siblings. If the valid effect empties the source, settlement erases item/discovery without retaining observed invalid evidence. Persist the existing digest-based quarantine records before submission/release. | patch |
| C11-BH5 | Package Client probe uses schedule causation | low | The probe supplies ComputeReminderName to ReminderDelegationRequest although the public contract and runtime use wrk-<EffectId>. Use the submission logical identifier and assert the binding in the synthetic provider. | patch |
| C11-BH6 | Public disposition summaries overclaim cleanup completion | low | Submitted/Stale/Cancelled audits precede Scheduler cancellation and state release; a failed cancellation retains work with those audits. Correct the enum summaries and include audit/cancellation failure retries. | patch |
| C11-BH7 | Public convergence count summaries omit retained discovery and cleanup gating | low | Armed increments for retry re-arms; Submitted increments only after applied Submitted settlement; Unresolved includes failed final source folds after witness release. Correct units without changing the contract shape. | patch |
| C11-BH8 | Delay-limit comment conflates host delays with actor due times | low | The options comment attributes the Task.Delay ceiling to reminder timers, while the pinned actor SDK accepts longer due times. Correct the comment; retain all option bounds. | patch |
| C11-BH9 | No Expiry runtime or mixed-kind execution case | low | Carried C5-BH5 / iteration-0 BH15: this explicit extra runtime case remains rejected; both kinds use the same generic coordinator path and the closed codec map/composition tests pin kind support. | carried reject |
| C11-BH10 | Malformed candidate ActorId is logged verbatim | medium | Both mismatch and catch paths in ReminderReconciler log candidate.ActorId before canonical validation. A restored tenant:domain:aggregate value is disclosed. Log only canonical digests or a bounded placeholder; assert emitted fields and preserve the candidate. | patch |
| C11-EC1 | Custom store can resolve an eraser for a different backend | medium | ReadModelStoreServiceCollectionExtensions at the preserved f378afdb baseline already falls back to DaprReadModelStore when a custom IReadModelStore lacks conditional erasure. Reminder cleanup exposes this existing shared registration defect. Qualify matching store/eraser bindings centrally rather than changing generic registrations in this reminder continuation. | defer |
| C11-EC2 | Slow uncertainty expires retry timestamp before settlement | medium | Independently confirmed at the same pre-await timestamp and Retry persistence as BH2; retain its failure-end timestamp regression on convergence and callback paths. | patch |
| C11-EC3 | Candidate write/removal ignores a tenant header mismatch | low | Carried C4-EC1 / chunk A BH7, with the unchanged index write/removal behavior; the earlier rare corrupt-state rejection remains applicable. | carried reject |
| C11-EC4 | Another actor implementation suppresses reminder registration | low | Carried C6-BH2 / C5-EC2 / R2-EC5: unchanged registration skips an occupied name; a proxy call to a non-reminder actor fails loudly. This host-programming collision was explicitly rejected. | carried reject |
| C11-EC5 | Same-Version foreign write is accepted after CAS | false | Carried C10-BH1: the isolated reproducer seeds a privileged replacement while preserving Version. Every normal coordinator writer increments Version on CAS and actor turns serialize, so it does not demonstrate a concurrent SDK writer defect. | carried reject |
| C11-EC6 | Callback catch returns Retrying without readiness bookkeeping | low | Carried C9-BH3 / R2-BH6 / split-group-2 AA2: on failed persistence the durable witness remains; the next pass recovers it within the interval. The earlier rejection stands. | carried reject |
| C11-EC7 | Corrupt discovery fields can disclose raw text in logs | medium | Same demonstrated malformed candidate ActorId disclosure as BH10. Preserve evidence and sanitize only the diagnostic identifier. | patch |
| C11-EC8 | Persisted quarantine ReasonCode allows arbitrary logged text | medium | IsValidPersistedQuarantine accepts any nonblank reason; convergence passes it directly to event 200207. Require one of the existing quarantine reason literals; use the existing malformed-quarantine digest repair to retain other evidence and verify secret text is absent from diagnostics. | patch |
| C11-EC9 | Attempts wraps at int.MaxValue | low | Carried C9-EC8 / R1-EC7 / C6-EC4: unchanged arithmetic needs an extreme or corrupt restored counter, and the extra saturating branch was already rejected. | carried reject |
| C11-EC10 | UpdatedAt near MaxValue overflows backoff arithmetic | low | Carried C9-EC7 / chunk A EC6: unchanged arithmetic requires a timestamp within the configured backoff of MaxValue. The prior rare-state guard rejection stands. | carried reject |
| C11-VG1 | CreateEffectIdentity has no Expiry execution assertion | low | Carried C5-BH5 / iteration-0 BH15. The filed hardcoded-kind mutant elaborates the same explicitly rejected lack of an Expiry runtime case at the unchanged generic coordinator path; preserve the recorded verdict. | carried reject |
| C11-VG2 | Mapped HTTP cancellation forwarding lacks regression tests | medium | Pre-verified HTTP-boundary gap belongs to existing unrelated /query, /replay-state and /project changes from ff1f5588e2b0d6807b8df018637ae3dbdf9c86fb (git blame). The 4.11 reminder change adds only its opt-in mapping hunk to that shared file. | defer |
| C11-VG3 | Same-Version foreign item state can be overwritten | false | Carried C10-BH1, independently reproduced through an out-of-band same-Version seed. That write bypasses the normal monotonic Version/CAS protocol, as in the previously rejected case. | carried reject |

Continuation review C12 (2026-10-08). All three context-free workflow layers reviewed the complete preserved Works-baseline diff, including the new public `3.117.1` closure evidence. Blind hunter returned ten findings, edge-case hunter four, and verification-gap no gaps. Every finding was judged before grouping. Six direct evidence/metadata corrections are assigned; two pre-existing agent-context omissions are deferred. Previously adjudicated findings retain their routes and are not filed again.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C12-BH1 | Public SDK regression host retains source-built transitive dependencies | low | The shadow host replaces six EventStore DLLs and retains `Hexalith.Commons.UniqueIds/1.0.0` from its source test graph; the package-only consumers independently restore public package dependencies. Qualify the runtime fixture in the evidence rather than claiming a wholly restored package graph. | patch |
| C12-BH2 | Recorded test commands omit download and shadow-host preparation | low | `commands.json` starts with builds and later assumes the public archives and prepared host exist. Retain a replay recipe for downloading/hash-checking the named packages and preparing the same fixture before executing the recorded checks. | patch |
| C12-BH3 | Shadow-host dependency manifest is hashed but not retained | low | `public-runtime-bindings.json` binds the `.deps.json` hash without retaining its bytes. Copy that manifest into the evidence and bind its retained filename so the fixture graph remains inspectable. | patch |
| C12-BH4 | Reused live proof lacks retained actor/test/fixture source comparisons | low | Parent comparison confirms unchanged actor, live reminder test, and reminder-specific fixtures. The shared fixture gained an optional HotReload override that is inactive when its environment variable is absent. Retain those comparisons and the default-path qualification; do not claim every fixture byte is unchanged. | patch |
| C12-BH5 | Current sprint comments still report an open public-release gate | low | The current sprint header predates the passing `3.117.1` proof. Correct its publication comments and review-phase status now; the final workflow transition will synchronize completion. | patch |
| C12-BH6 | Client result filename omits the retained gzip suffix | low | The new summary names `source-client-full.xml`, while the retained bytes are `source-client-full.xml.gz`. Name the actual artifact and state its compression. | patch |
| C12-BH7 | Epic context says Claim is the only entry to InProgress | low | `epic-4-context.md` contains that statement while the existing lifecycle also resumes Suspended work to InProgress. This pre-existing agent-context wording can misdirect future stories; record the Claim-versus-Resume qualification for a context refresh. | defer |
| C12-BH8 | Epic context omits explicit persist-before-publication wording | low | Carried C3-BH9 / C7-BH10: the same committed context omission and loaded architecture authority remain. Preserve the existing deferred entry without rewriting context or filing it again. | carried defer |
| C12-BH9 | Epic context omits runtime persisted-end-state evidence requirements | low | The earlier context refresh retains Tier-1 isolation but omits its previous explicit runtime evidence sentence. This story itself requires and executes persisted-state tests; restore the context requirement in its next dedicated refresh. | defer |
| C12-BH10 | Works CI/CD acceptance TRX is retained only under /tmp | medium | Carried C7-BH13 / C8-BH9: the unchanged CI/CD spec still cites its temporary broad Integration TRX. The existing separate-story deferred entry already records this defect. | carried defer |
| C12-EC1 | Historical packaging helper raises IndexError without arguments | low | Carried C7-EC1 / C8-EC1: the same one-shot evidence helper reads positional arguments directly. Adding an argument guard remains disproportionate for a helper product callers never execute. | carried reject |
| C12-EC2 | Historical packaging helper can retain stale output archives | low | The retained one-shot fallback accepts an existing output directory; historical proof used a fresh versioned directory and canonical validation. A new nonempty-directory guard adds branching for an uncommon reuse of an archival helper. | reject |
| C12-EC3 | Epic context removes persist-before-publication | low | Carried C3-BH9, as for C12-BH8. The existing context-refresh deferral is preserved and not appended again. | carried defer |
| C12-EC4 | Stale callback convergence may submit a due replacement | false | Carried iteration-0 EC14 / C4-EC2 / C6-EC5: the stale witness is the audited no-op; admitted convergence may independently submit a current due intent. The recorded design explicitly permits that behavior. | carried reject |

Delta review C13 (2026-10-08). Scope: the reminder files where public `3.117.1` source `0dad344d` (an ancestor of EventStore HEAD `07d1e23a`, with no reminder-file change between them) differs from the C11-reviewed `7450da14`. Those are `EventStoreReminderEndpointExtensions.cs`, `docs/guides/typed-reminders.md`, `EventStoreReminderCompositionTests.cs`, and `ReminderDiagnosticsTests.cs`: 174 diff lines from EventStore Story 5.5 (`c4d5455a`) and the secret-scan CI fix (`0de100da`). The 3.117.1 source audit compared only the Contracts and Client reminder directories, the coordinator, and the reconciler, so no earlier layer had seen these bytes. Review mode: full. Four context-free layers completed: blind hunter 12, acceptance auditor 5, verification gap with no gaps and 3 other findings, and edge-case hunter 8. Each of the 28 findings was judged before grouping.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C13-BH1 | The credentials subsection captures the options table | low | `### Trusted-effect submission credentials` (`typed-reminders.md:269`) sits inside `## Host composition`. So the `EventStore:Reminders` options table, its validation paragraph, and the host `APP_API_TOKEN` paragraph now read as credential content. | patch |
| C13-BH2 | The pre-mapped composition test still uses the bare `MapActorsHandlers()` | low | `PreMappedActorHandlersAreNotDuplicated` (`EventStoreReminderCompositionTests.cs:178`) maps bare handlers before `UseEventStoreDomainService`. The startup inventory would reject that host because `/actors/*` lacks `SidecarChannel` and `/healthz` stays anonymous. The test passes only because it never starts the host. | patch (with C13-AA5, C13-VG-O2, C13-VG-O3) |
| C13-BH3 | The new route test's filter can pass vacuously | false | The test also asserts `EventStoreDomainServiceEndpointInventory.Validate(...)` is empty. Validate flags any anonymous non-probe route, and any `/healthz`, `/dapr/config`, or `/actors/*` route without `SidecarChannel`. Its fallback, `CreateAnyWorkloadPolicy(WorkloadScheme)`, is the same policy `AddEventStoreDomainServiceSecurity` installs (`EventStoreDomainServiceSecurityExtensions.cs:80`). | reject |
| C13-BH4 | The required submitter identity is unstated | low | The gateway rejects an assertion whose `azp` differs from `dapr-caller-app-id` (`WorkloadAssertionEvaluator.cs:82`, `security-model.md:348`). The reminder guide's step 2 never says the symmetric `Workload` must equal the submitter's Dapr app ID, and never links to that rule. | patch (credential-reference group) |
| C13-BH5 | Authority-mode scope provisioning is undocumented in the guide | low | The guide names the two optional scopes but not how to declare them or their mappers. `security-model.md:356,365` documents both. The local realm has no submitter by design (`typed-reminders.md:296`). | patch (credential-reference group) |
| C13-BH6 | A custom gateway audience is not covered | low | `AddEventStoreTrustedEffectWorkloadAssertion(string gatewayAudience = "eventstore")` (`EventStoreTrustedEffectSubmissionExtensions.cs:29`) must match a changed `Authentication:DaprInternal:Audience`, and the scope name follows that audience. The guide hard-codes `eventstore`. | patch (credential-reference group, with C13-EC6) |
| C13-BH7 | The provisioning list omits gateway-side prerequisites | low | Once `AllowedCallers` is non-empty, EventStore needs its own `APP_API_TOKEN` (`configuration-reference.md:680`), and the submitter needs the `Authentication:JwtBearer` contract (`security-model.md:383`). Neither is in the three-item list. | patch (credential-reference group) |
| C13-BH8 | The token-lifetime step names no setting | low | Symmetric `Authentication:WorkloadIssuer:LifetimeSeconds` defaults to 120 and is capped at 300 (`configuration-reference.md:510`). Lowering the receiver's `MaximumLifetimeSeconds` below it rejects every assertion. | patch (credential-reference group, with C13-EC7) |
| C13-BH9 | A credential `401` looks like a transient submission failure | low | `HttpTrustedEffectSubmitter` calls `EnsureSuccessStatusCode` (`HttpTrustedEffectSubmitter.cs:38`), and the coordinator's catch-all records `Retrying`/`submission-uncertain` with the `HttpRequestException` type only (`ReminderCoordinator.cs:1484-1492`). The runbook has no pointer to the gateway's event `5501` reason code. A new reason code would be a runtime change; a runbook sentence suffices. | patch (runbook) |
| C13-BH10 | The readiness runbook describes a state its own composition cannot reach | low | `typed-reminders.md:353-355` says readiness is `Unhealthy` with every actor call refused `401` when `APP_API_TOKEN` is missing outside Development. With `AddEventStoreDomainService()`, `EventStoreDomainServiceSecurityStartupValidator.StartingAsync` throws first. The callback-admission statement at `:152` is still true as defense in depth. | patch (with C13-AA4) |
| C13-BH11 | Terminology and XML-doc drift | low | The XML-doc part is real and grouped with C13-VG-O1: the `MapEventStoreReminders` summary (`EventStoreReminderEndpointExtensions.cs:14`) omits the policy requirement for self-mapped handlers. The terminology part is false. The "app-channel filter" (`ReminderCallbackTokenFilter`) and "sidecar-channel policy" are two real, distinct layers. The test name's "AppChannel" matches the handler's documented role: it authenticates the Dapr application channel. | patch (XML-doc part); reject (terminology part) |
| C13-BH12 | The leak assertions catch only full-string leaks, and the fixture no longer says "token" | low | The delta only renamed constants. Assertion strength is unchanged (VG checked it). Reminder logs emit only digests, codes, and bounded placeholders. The repair under test replaces any unknown stored reason, so the fixture's wording is irrelevant. | reject |
| C13-AA1 | The SDK-mapped sidecar-channel actor path has no live proof | false | The claimed outcome was that no actor type registers and no reminder fires. Dapr `release-1.18` `pkg/channel/http/http_channel.go` sends `GET /dapr/config` and actor calls through `constructRequest`, which sets `dapr-api-token`. `pkg/actors/actors.go` (1.16) starts no app `/healthz` prober, and the C11 live callback proof already showed actor calls carry the token. Real-host live composition is 4.15/4.16 parity work. | reject |
| C13-AA2 | The 3.117.1 closure audit omits a changed reminder runtime file | low | The recorded comparison is accurate for the paths it names. Fixing the omission means editing this spec's closure record, and this C13 entry now records the review of that delta. | reject |
| C13-AA3 | The 4.15 handoff omits the new host requirements | low | `typed-reminders.md:480-492` does not mention three requirements: self-mapped actor handlers need `.RequireEventStoreSidecarChannel()` (or must not be mapped), the submitter needs `.AddEventStoreTrustedEffectWorkloadAssertion()`, and EventStore needs an `AllowedCallers` entry. Works `WorksHost.cs:130-133` maps bare handlers after `UseEventStoreDomainService`, so the inventory will fail its startup once Works consumes ≥3.117. | patch (guide handoff) |
| C13-AA4 | The readiness runbook state is unreachable | low | Same defect as C13-BH10. | patch (with C13-BH10) |
| C13-AA5 | A reminder test uses the setup the guide says fails startup | low | Same defect as C13-BH2. | patch (with C13-BH2) |
| C13-VG-O1 | The public `IReminderRegistrar` remarks prescribe plain `MapActorsHandlers` | low | `IReminderRegistrar.cs:14-15` (Client package IntelliSense) contradicts the guide's `MapActorsHandlers().RequireEventStoreSidecarChannel()` requirement, which the startup inventory enforces (`EventStoreDomainServiceEndpointInventory.cs:57-62`). | patch (with C13-BH11 XML part) |
| C13-VG-O2 | The pre-mapped test builds the rejected host shape | low | Same defect as C13-BH2. | patch (with C13-BH2) |
| C13-VG-O3 | The supported pre-mapped shape is untested | low | Nothing combines a policy-wrapped pre-map with `AddEventStoreReminders`, a no-duplicate assertion, and a clean inventory. This follows from C13-BH2's unchanged test. | patch (with C13-BH2) |
| C13-EC1 | Reminders mapped without `AddEventStoreDomainService` lack the policy | low | Every documented composition registers `AddEventStoreDomainService`: the guide sample and Works. The live fixture pre-maps its own handlers. Without the registration, each actor call fails loudly on the missing policy, and readiness is Degraded. A new registration guard adds branching for an undocumented host. | reject |
| C13-EC2 | Dapr app health checks on the default `/healthz` are now refused | low | Dapr `release-1.18` `HealthProbe` sends no `dapr-api-token`, so `/healthz` (now `SidecarChannel`, no anonymous access) returns `401`, and an operator who enables app health with the default path marks the reminder host unhealthy. The in-repo Aspire paths use `/alive` (`HexalithEventStoreDomainModuleExtensions.cs:14`, Works `AppHost/Program.cs:180`), but the guide does not state the requirement. | patch (guide) |
| C13-EC3 | A host that never maps the canonical domain-service endpoints skips the inventory | low | `EventStoreDomainServiceEndpointSource` is captured only by `MapEventStoreDomainService`. A host that registers the domain service but maps reminders directly is undocumented. A misconfigured actor route then hits the any-workload fallback and fails loudly. A guard adds branching. | reject |
| C13-EC4 | The new test's `ShouldNotBeEmpty` is vacuous | false | Same refutation as C13-BH3. | reject |
| C13-EC5 | The no-leak assertions skip the captured exception channel | false | No `ReminderLog` method takes an `Exception` parameter, so no reminder log entry can carry an exception object. | reject |
| C13-EC6 | A custom gateway audience breaks submissions | low | Same defect as C13-BH6. | patch (with C13-BH6) |
| C13-EC7 | `MaximumLifetimeSeconds` below 120 rejects symmetric assertions | low | Same defect as C13-BH8. | patch (with C13-BH8) |
| C13-EC8 | The "fails startup" guide claim is overbroad | false | The sentence is scoped to hosts that map handlers "before `UseEventStoreDomainService`". That call captures the route builder, and `StartingAsync` validates every data source, including later mappings. | reject |

Delta review C14 (2026-10-08). Scope: the 183-line diff of the eight C13 patches in EventStore (`07d1e23a` plus uncommitted guide, XML-doc, and test edits). Reviewers: blind hunter (BH), edge-case hunter (EC), verification-gap (VG). The full baseline diff is about 3 million lines and was not reviewable; the earlier layers already covered it.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C14-BH1 | No negative tests for the three documented startup failures | false | Bare `MapActorsHandlers()` is already a covered violation (`EventStoreDomainServiceExtensionsTests.cs:1114`, case `actors`), and `DomainServiceTrustBoundaryTests.ProductionWithoutChannelToken_FailsStartup` (`:601`) covers the missing token. The docs describe pre-existing behavior. | reject |
| C14-EC3 | Bare `MapActorsHandlers()` failure is unguarded in the reminder test class | false | Same evidence as C14-BH1; the inventory rule is tested once at its owner. | reject |
| C14-BH2 | Test repeats the long `fallbackPolicy` expression | low | Cosmetic. The same expression is already used a few lines above in the same class, and the fix is a refactor. | reject |
| C14-BH3 | `WorksHost.cs:130-133` is a hard-coded line range | low | The range is accurate today (`WorksHost.cs:130` `UseEventStoreDomainService`, `:133` `MapActorsHandlers`) but goes stale after any edit. A symbol citation is a direct correction. | patch (with C14-EC1, C14-EC2) |
| C14-EC2 | Same hard-coded line range | low | Same defect as C14-BH3. | patch (with C14-BH3) |
| C14-EC1 | Handoff bullet never names `MapEventStoreReminders()` as the replacement for the removed mapping | low | The bullet says to remove `MapActorsHandlers()` without saying what maps the actor routes. `UseEventStoreDomainService` calls `MapEventStoreReminders()` when reminders are registered (`EventStoreDomainServiceExtensions.cs:184`), but a Works adopter has to infer that. | patch |
| C14-BH3b | "Works consumes 3.117" does not say what is versioned | false | It is the EventStore package version series; Works pins `3.114.0` today, and the change is in `3.117.1`. | reject |
| C14-BH4 | `/alive` guidance gives only the Kubernetes annotation | false | The sentence is generic (point Dapr's application health check at `/alive`); the annotation and Aspire wiring are examples. | reject |
| C14-BH5 | "Mapper" undefined; lifetime interplay with `MaximumLifetimeSeconds`; authority token lifetime location | false | The clauses restate Story 5.5 requirements and link `security-model.md`. "Must not exceed" already covers a lowered maximum. An authority-issued token's lifetime is the identity provider's setting. | reject |
| C14-EC5 | Default lifetime 120 vs a lower `MaximumLifetimeSeconds` | false | Same refutation as C14-BH5. | reject |
| C14-BH6 | Runbook points at event `5501` with no link; proposes distinguishing the statuses | low | `5501` is documented at `security-model.md:348`, which the guide's credentials section already links. Distinguishing the statuses would be new runtime surface. | reject |
| C14-EC6 | `5501` is the only discriminator for a credential `401` | low | Same disposition as C14-BH6; the runbook sentence already names it. | reject |
| C14-BH7 | Missing release note for the breaking missing-token change | false | Failing startup without `APP_API_TOKEN` is pre-existing (EventStore Story 5.5, tested by `ProductionWithoutChannelToken_FailsStartup`). The diff only corrects docs. | reject |
| C14-BH8 | New `### Options` heading risks broken anchors | false | The table had no anchor of its own; a new heading adds an anchor and removes none. | reject |
| C14-EC4 | No test for mapping after `UseEventStoreDomainService` with the policy | low | The guide prescribes mapping before it, and `StartingAsync` validates every data source regardless of order. A new test would cover an undocumented layout. | reject |
| C14-VG | No verification gaps | n/a | The layer reported none. | n/a |

Delta review C15 (2026-10-09). Scope: EventStore `07d1e23a..29e8e270`, the committed C13 and C14 patches (five files, +69/−15, 184 diff lines). C14 saw 183 uncommitted lines, so only the C14 handoff fix is new to review. Review mode: full. Four layers completed: blind hunter (BH) 11, edge-case hunter (EC) 12, acceptance auditor (AA) 9 plus one informational, and verification gap with no gaps. Each of the 32 findings was judged before grouping; multi-part findings carry one row per part.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C15-BH1a | The inventory rule ships in `v3.116.0`, not 3.117 | false | `git tag --contains c4d5455a` starts at `v3.116.0`, but nuget.org lists `3.114.0`, `3.115.0`, `3.117.0`, and `3.117.1`: `3.116.0` was never published, so package consumers first meet the rule at 3.117. | reject |
| C15-BH1b | Works' sibling-source Debug host already fails the inventory | high | Works Debug defaults `UseHexalithProjectReferences=true` (`Directory.Build.props:26`), so `WorksHost` compiles against EventStore source, which carries Story 5.5 since Works `dcd90e7` (`07d1e23a`). The Debug integration-test build fails on 19 `xUnit1051` analyzer errors; with that rule demoted, `WorksDomainEventSubscriptionTests.WorksHostExposesOneDeliveryRouteAndOneDiscoveryRoute` fails with "route inventory violates the internal trust boundary" for `/project`, `/work/events`, `/dapr/config`, and `/actors/*`. Pre-existing since `dcd90e7`; not caused by this diff. | defer |
| C15-BH2a | The 4.15 handoff names only the actor-handler failure | medium | The same inventory rejects `WorksHost`'s own `MapPost("/project")` (catalog policy missing) and `MapWorksDomainEvents()` (topic route without `SidecarChannel`), as the C15-BH1b run shows. An adopter who fixes only the bullet's actor mapping still fails startup, and the first story to bump the EventStore pin (likely 4.14) meets all three. | patch (with C15-AA1) |
| C15-BH2b | `MapEventStoreReminders()` throws without `AddEventStoreReminders`; the explicit call is redundant | false | The 4.15 bullets adopt the seam (intent source, actor type, purposes), so reminders are registered wherever the replacement applies. The bullet already says `UseEventStoreDomainService` calls it. | reject |
| C15-BH3a | `caller-conflict` is the wrong reason for a `Workload`/app-ID mismatch | low | `WorkloadAssertionEvaluator` checks `AllowedCallers` first (`caller-not-allowed`), then the `dapr-caller-app-id` cross-check (`caller-conflict`). With step 1 allow-listing the app ID, a differing `Workload` is denied as `caller-not-allowed`; `caller-conflict` appears only when the differing value is itself allow-listed. | patch (with C15-EC4, C15-AA6) |
| C15-BH3b | `EventStore:Reminders:Workload` must also equal the assertion caller | low | `TrustedEffectsController` takes the workload from the verified assertion, `JwtTrustedEffectDelegationVerifier.cs:82` requires the delegation's `workload` claim to equal it, and `ReminderCoordinator` requests the delegation with `EventStoreReminderOptions.Workload`. Both identities default to `DAPR_APP_ID`, then the application name, but an explicit `EventStore:DomainService:AppId` or `Workload` diverges and every submission receives `403`. The options row says only "Workload named in the trusted-effect context". | patch |
| C15-BH4a | `app-health-check-path` needs `enable-app-health-check` | false | Dapr app health checks are off by default; with them off no probe runs and no `401` occurs. The sentence governs where an enabled check points. | reject |
| C15-BH4b | Self-hosted `daprd` and Azure Container Apps forms are missing | false | Carried C14-BH4: the instruction is generic; the annotation and Aspire wiring are examples. | carried reject |
| C15-BH4c | Development without a token hides the `401` | low | True (`development-unverified`), but the guidance is unconditional, so following it avoids the failure in every environment. A Development caveat adds prose for no behavior change. | reject |
| C15-BH5 | The Kubernetes sample domain service lacks token configuration | low | `samples/deploy/kubernetes/dapr-annotations-example.yaml` `sample_annotations` has no `dapr.io/app-token-secret` and no `APP_API_TOKEN` entry, while every SDK domain service fails startup without the token outside Development. Pre-existing since Story 5.5; the sample is not a reminder host. The missing health-check annotations are not a defect (C15-BH4a). | defer |
| C15-BH6a | The runbook omits submitter-side events `200219`, `5542`, and `5511` | low | Carried C14-BH6/C14-EC6: the runbook names gateway event `5501`, and the credentials section links `security-model.md`. | carried reject |
| C15-BH6b | A `403` admission denial also surfaces as `submission-uncertain` | low | Delegation mismatches and admission denials return `Forbid()` (`TrustedEffectsController`), and `HttpTrustedEffectSubmitter` turns that into an `HttpRequestException` through `EnsureSuccessStatusCode`. The sentence names only a credential `401` and transient failures, and event `5501` does not cover delegation denials. | patch (with C15-EC7) |
| C15-BH7a | The JwtBearer requirement reads as conditional on `AllowedCallers` | low | `EventStoreDomainServiceSecurityStartupValidator` requires `APP_API_TOKEN`, the workload options, and the JwtBearer contract on every SDK domain service outside Development, whatever EventStore's allow-list holds. Only the EventStore half of the sentence depends on `AllowedCallers`. | patch |
| C15-BH7b | The guide does not say symmetric signing is Development-only | false | Step 2 names it "symmetric `Development` mode" and contrasts authority mode. | reject |
| C15-BH8 | The authority-mode lifetime location and `assertion-stale` are unnamed | low | Carried C14-BH5: an authority-issued token's lifetime is the identity provider's setting, and "must not exceed" covers it. | carried reject |
| C15-BH9 | `### Options` now holds the host `APP_API_TOKEN` paragraph | low | `typed-reminders.md:345-348` ("The host must also configure `APP_API_TOKEN`…") follows the options validation paragraph under `### Options`; it is host configuration, not an `EventStore:Reminders` key. C13-BH1 named this paragraph as one of three misplaced blocks. | patch (with C15-AA7) |
| C15-BH10 | The `configuration-reference.md` `APP_API_TOKEN` row contradicts itself | low | The row says the token is required on "every domain service: without it the domain service fails startup", then calls the reminder check "defense in depth for another composition". Only `AddEventStoreDomainService()` registers the startup validator, so the first sentence needs that qualifier. | patch (with C15-AA5) |
| C15-BH11a | The bare pre-mapping case lost its test | false | Carried C14-BH1/C14-EC3: bare `MapActorsHandlers()` is covered at `EventStoreDomainServiceExtensionsTests.cs:1114` (case `actors`); carried C14-EC4 for the after-activation order. | carried reject |
| C15-BH11b | The test name no longer describes the test | false | It still asserts a single reminder route (`CountReminderRoutes(app).ShouldBe(1)`), which is the name's claim; the inventory assertion is additive. | reject |
| C15-EC1 | Non-reminder domain services also need `/alive` | low | An SDK host without actor handlers never maps `/healthz`, so Dapr's default probe fails there too. That predates this diff and Story 4.11; the reminder guide is correctly scoped. | defer |
| C15-EC2 | The self-hosted `daprd` form is missing | false | Carried C14-BH4. | carried reject |
| C15-EC3 | `AddAspireDaprDomainModule` with `EnableAppHealthCheck` and no path uses Dapr's `/healthz` | low | The lower-level API takes the caller's path; `AddHexalithEventStoreDomainModule` sets `/alive` (`HexalithEventStoreDomainModuleExtensions.cs:14,75-76`), and the guide tells callers to use `/alive`. A defaulting branch adds runtime surface. | reject |
| C15-EC4 | `caller-not-allowed` is evaluated before `caller-conflict` | low | Same defect as C15-BH3a. | patch (with C15-BH3a) |
| C15-EC5 | `Workload` can fall back to the application name | low | The guide states the invariant (`Workload` must equal the Dapr app ID); the proposed startup check is new runtime branching. | reject |
| C15-EC6 | The scope names assume the default prefixes | low | `WorkloadAssertionIssuerOptions.AudienceScopePrefix` and `OperationScopePrefix` are configurable (defaults `eventstore-audience.` and `eventstore-operation.`), and the guide names only default-prefixed scopes. A default-prefix qualifier beside the existing outbound-settings link is a direct correction. | patch |
| C15-EC7 | Timeouts surface as `TaskCanceledException`, not `HttpRequestException` | low | `ReminderCoordinator` rethrows only caller cancellation, so an `HttpClient.Timeout` falls into the catch-all and logs `TaskCanceledException`. The runbook names only `HttpRequestException` for transient failures. | patch (with C15-BH6b) |
| C15-EC8 | A policy-carrying mapping after activation duplicates routes and passes the inventory | low | Carried C14-EC4: the guide, `IReminderRegistrar`, and the handoff all require mapping before `UseEventStoreDomainService`; duplicate detection would be a runtime change. | carried reject |
| C15-EC9 | The `IReminderRegistrar` "fails startup" claim is overbroad without `AddEventStoreDomainService` | false | Carried C13-EC1/C13-EC8: every documented composition registers it, and the undocumented host fails loudly. | carried reject |
| C15-EC10 | `MapEventStoreReminders` without the policy registered returns `500` | low | Carried C13-EC1: undocumented composition, loud failure, and a guard adds branching. | carried reject |
| C15-EC11 | The test validates against a rebuilt fallback, not the registered one | low | The C13 patch asked for "the registered fallback policy", but the test passes `CreateAnyWorkloadPolicy(...)` instead of `IOptions<AuthorizationOptions>.Value.FallbackPolicy` from `app.Services`, so a change to the SDK-registered fallback would go unnoticed. `BuildApp` calls `AddEventStoreDomainService()`, so the registered policy is resolvable. | patch (with C15-AA8) |
| C15-EC12 | Removing the bare-mapping case leaves the fails-startup claim untested | false | Same evidence as C15-BH11a. | carried reject |
| C15-AA1 | The 4.15 handoff names one of three `WorksHost` inventory failures | medium | Same defect as C15-BH2a. | patch (with C15-BH2a) |
| C15-AA2 | Pushed Works `main` pins an EventStore commit missing from its remote | medium | Works `origin/main` is `aae6b9b`, which pins `29e8e270`; EventStore `origin/main` is `07d1e23a`, and the local branch is ahead by one. A fresh clone cannot check out `references/Hexalith.EventStore`. The fix is a push, which is the owner's call; the stale "remain uncommitted" C14 closure sentence would edit this spec and is not routed. Resolved by the owner: EventStore `c4dff081` (rebased, same patch-id) and Works `9fc21f5`. | decision (resolved) |
| C15-AA3 | The corrected IntelliSense ships only in a later package | low | True, and inherent to a post-release doc fix; the guide is authoritative. Correcting the "no packaged-API bytes changed" wording would edit this spec. | reject |
| C15-AA4 | The readiness runbook drops "outside Development" for `Unhealthy` | low | `ReminderCallbackTokenFilter.IsConfigured` is `IsDevelopment() || token present`, so in Development a missing token never makes `eventstore-reminders-unresolved` Unhealthy; the new sentence says it does "when the token is missing". | patch |
| C15-AA5 | The `APP_API_TOKEN` row contradicts itself | low | Same defect as C15-BH10. | patch (with C15-BH10) |
| C15-AA6 | The guide names the wrong denial reason for the likeliest misconfiguration | low | Same defect as C15-BH3a. | patch (with C15-BH3a) |
| C15-AA7 | `### Options` covers the host paragraph | low | Same defect as C15-BH9. | patch (with C15-BH9) |
| C15-AA8 | The test uses a rebuilt fallback | low | Same defect as C15-EC11. | patch (with C15-EC11) |
| C15-AA9 | The handoff offers a self-mapping path that AD-20 R9 rules out | false | R9 binds the final minimal executable (Story 4.9). Story 4.15 is transitional while `DateReminderActor` remains, and the bullet names the SDK mapping first. | reject |
| C15-AA10 | No AC or frozen-constraint breach | n/a | Informational. | n/a |
| C15-VG | No verification gaps | n/a | The layer reported none. | n/a |
| C16-BH1 | C15 summary counts 19 patched findings but lists 17 | low | The checklist and C15 triage rows identify 17 patched finding IDs; the count in the summary is stale. Correcting only this build spec is excluded by the review route. | reject (spec-only) |
| C16-BH2 | Checked C15 tasks have no current verification record | low | The C14 227-test note predates these patches. The current reminder binary passed 227/227 on 2026-10-09; the completion record below captures the result independently of this spec-only review request. | reject (spec-only) |
| C16-BH3 | Custom audience guidance hard-codes the default scope prefix | low | `AudienceScopePrefix` is configurable, but the custom-audience instruction still says `eventstore-audience.<audience>`. This requests a scope the issuer need not expose. | patch |
| C16-BH4 | Workload mismatch always described as HTTP 403 | low | `ReminderCoordinator` calls the domain-owned delegation provider first; it can return `delegation-failed` or `delegation-unavailable` before HTTP. A verified mismatched delegation can reach gateway 403. | patch (with C16-EC1) |
| C16-BH5 | Runbook suggests a 403 can be distinguished from reminder diagnostics | low | Event 200219 logs only `ExceptionType`, and the witness stores `submission-uncertain`. The runbook must condition 403-specific advice on gateway-side status evidence. | patch |
| C16-BH6 | Works handoff does not locate the topic route policy call | low | `MapWorksDomainEvents()` returns `IEndpointRouteBuilder`, while its inner `MapPost` returns the route builder. The sidecar policy belongs on that inner mapping. | patch |
| C16-EC1 | `caller-conflict` is promised even without caller attribution | low | `WorkloadAssertionEvaluator` checks `dapr-caller-app-id` only when the header is present; an allow-listed assertion with no header passes this comparison. | patch (with C16-BH4) |
| C16-VG1 | Registered fallback is unexercised by the changed composition test | low | The test maps no ordinary route without authorization metadata, so `Validate` never reaches its fallback-dependent branch. The constructed-policy inventory tests do not prove the registered policy. | patch |

Delta review C17 (2026-10-09). Scope: EventStore `c4dff081..8d033bd8`, the committed C15 action items and C16 patches (three files, +49/−26, 181 diff lines). C16 saw the C15 patches before commit; its own five patches are new to review. Review mode: full. Four layers completed: blind hunter (BH) 10, edge-case hunter (EC) 7, acceptance auditor (AA) 4, and verification gap with no gaps. Each of the 21 findings was judged before grouping; multi-part findings carry one row per part.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C17-BH1 | Issuer and reminder workloads default from different sources | low | The issuer `Workload` falls back to `EventStore:DomainService:AppId` (`EventStoreDomainServiceSecurityExtensions.cs:91`), which falls back to `DAPR_APP_ID`, then the application name (`EventStoreDomainServiceExtensions.cs:531`). `EventStore:Reminders:Workload` reads only `DAPR_APP_ID`, then the application name (`EventStoreReminderServiceCollectionExtensions.cs:126-133`). A host that sets `EventStore:DomainService:AppId`, typically because `DAPR_APP_ID` is absent, requests delegations for a different workload, so every due reminder stays `Retrying`. The guide states both defaults and the equality rule but never says to set the reminder key alongside `AppId`. A shared default or startup check is runtime work (carried C15-EC5). | patch (docs, with C17-EC4) |
| C17-BH2 | Permanent `401`/`403` denials retry forever, and the guide does not call that a limitation | false | Retaining a denied submission as `Retrying` with readiness `Degraded` is the frozen "retain unresolved work; degrade readiness" behavior. The runbook already states the disposition and that diagnostics keep only the exception type. A status-specific reason code is new runtime surface (carried C14-BH6). | reject |
| C17-BH3 | "Gateway-side status evidence" names no signal, and the admission-policy link has no anchor | low | Controller `Forbid()` resolves through the default policy scheme (`ServiceCollectionExtensions.cs:83,87`) to the DaprInternal workload handler. Its `Forbidden` event (`WorkloadJwtBearerEvents.cs:94`) logs event `5501` with `StatusCode=403`, and `TrustedEffectAdmissionPolicy` appends a payload-free `denied` audit record (`:51`, `:85`). The runbook names neither, and links `trusted-effects.md` without `#production-admission-gate`. | patch (with C17-EC3) |
| C17-BH4 | Item 2 nests the mode-independent identity rule inside the symmetric-mode sentence | low | The Options row states the rule for both modes ("must equal the submitting workload assertion's caller identity"). In authority mode the default `EventStore:Reminders:Workload` (`DAPR_APP_ID`) already equals the required `azp`, the app ID. Restructuring item 2 is a rewrite, not a direct correction. | reject |
| C17-BH5 | Scope-prefix wording derives only the audience scope | low | `GetOperationScope` builds `OperationScopePrefix` plus the operation with `:` replaced by `.` (`WorkloadAssertionIssuerOptions.cs:90-93`). The guide marks both names as default-prefix forms but derives only a changed audience, so an operator with a custom `OperationScopePrefix` must infer the operation scope the authority declares. One clause completes C15-EC6 and C16-BH3. | patch (with C17-EC6) |
| C17-BH6 | The registered-fallback assertion checks only `DenyAnonymous`; the sibling test still passes a rebuilt fallback | false | The registered fallback's scheme is asserted at `EventStoreDomainServiceExtensionsTests.cs:1001`, so a fallback weakened to another scheme fails there. The sibling `ReminderActorRoutesRequireTheAppChannelAndAreNeverAnonymous` maps no route that relies on the fallback, so its result is the same with either policy. `/custom` carries no `IAuthorizeData` by construction. | reject |
| C17-BH7 | Test name and target no longer match; no negative control; `StartingAsync` not run | false | Carried C15-BH11b: the test still asserts one reminder route. The no-fallback negative case is `EndpointInventory_WithoutFallbackPolicy_RequiresExplicitMetadata` (`EventStoreDomainServiceExtensionsTests.cs:1052`), and startup failure on an inventory violation is tested at its owner (carried C14-BH1). | reject |
| C17-BH8 | The Works bullet's timing contradicts itself, "sibling-source" is undefined, and Works symbols will go stale | false | "Consumes 3.117" is the package pin (Works `HexalithEventStoreVersion` 3.114.0); the next sentence covers the source-reference build mode, which Works `README.md` and `Directory.Build.props` call "sibling-source". Symbol citations replaced line ranges deliberately (C14-BH3). | reject |
| C17-BH9 | The bullet omits the dead-letter redrive caller and uses a different `/project` call than `upgrade-path.md` | false | Dapr attaches `dapr-api-token` to every app-channel request, service invocation included, so the `eventstore-operations` redrive still passes the sidecar-channel policy. Updating the Works remark belongs to the 4.15 change that adds the policy. `RequireEventStoreDomainServicePolicy("/project")` resolves to the same `EventStoreDomainServicePolicies.Project` policy. | reject |
| C17-BH10a | The domain-service `APP_API_TOKEN` requirement appears in three places | false | Each place states a different consequence (host composition, startup, readiness); none gives a wrong instruction. | reject |
| C17-BH10b | The JwtBearer sentence omits `APP_API_TOKEN` and `Authentication:Workload` | false | The domain-service token requirement is the host-composition paragraph this diff placed directly before the section (C15-BH9), and the sentence scopes `AllowedCallers` to "EventStore itself". SDK defaults satisfy `Authentication:Workload` (audience = app ID, caller `eventstore`), so no default-configured host fails on an unnamed key. | reject |
| C17-BH10c | Startup failure is scoped to `AddEventStoreDomainService()`, though `AddEventStoreDomainServiceSecurity()` is public | low | No guide documents `AddEventStoreDomainServiceSecurity()` as a standalone composition. A host that uses it fails startup more strictly than described, and loudly (carried C13-EC1 pattern). | reject |
| C17-BH10d | The Azure Container Apps sentence permits rather than forbids a self-defined token | low | "So operators do not set their own" reads as optional. `deployment-azure-container-apps.md:498` says not to define `APP_API_TOKEN` because a different value causes `401`, and on a reminder host that `401` refuses every actor call. The paragraph is in this diff (moved by C15-BH9), and aligning it is a direct correction. | patch |
| C17-AA1 | Works `b4df05e` pins an EventStore commit that is not on its remote | medium | Works `main` is ahead of `origin/main` by `b4df05e`, which pins `8d033bd8`; EventStore `main` is ahead 1, behind 14 (local tracking refs, not fetched). The 14 upstream commits do not touch the three reviewed files, so a rebase is clean but changes the SHA that the C16 completion note and the gitlink record. Pushing Works first repeats C15-AA2. The push order is the owner's call. Resolved by the owner: keep both local until the C17 patches land, then push EventStore first, repoint Works once, and correct the recorded SHA. | decision (resolved) |
| C17-AA2 | The split JwtBearer sentence lists only part of the startup requirements | false | Same evidence as C17-BH10b. | reject |
| C17-AA3 | The sibling inventory test still passes a rebuilt fallback | false | Same evidence as C17-BH6. | reject |
| C17-AA4 | The registered-fallback test proves only `DenyAnonymous`, and its summary overclaims | false | Same evidence as C17-BH6; "protects" matches the inventory's contract, `DeniesAnonymous`. | reject |
| C17-EC1 | Persistent `400`, `413`, and `500` also surface as `submission-uncertain` | false | `HttpTrustedEffectSubmitter` always sends a submission, so the controller's `BadRequest` (`TrustedEffectsController.cs:31`) is unreachable. The closed catalog's `works.date-resume.v1` and `works.expiry.v1` commands are far below the 1 MiB limit (`:23`), and a `500` is the transient failure the sentence names. | reject |
| C17-EC2 | A `2xx` with a null or malformed body surfaces as other exception types | false | The gateway returns `Ok(result)` with the router's result (`TrustedEffectsController.cs:73`). A null or malformed body needs a non-EventStore intermediary the guide does not describe. | reject |
| C17-EC3 | Admission and delegation `403`s also log `5501` with `Reason=operation-not-granted` | low | Same path as C17-BH3. The runbook confines `5501` to "assertion denials", so an operator who sees `operation-not-granted` chases the operation-scope grant instead of the delegation or admission rule. | patch (with C17-BH3) |
| C17-EC4 | Workload defaults diverge when `EventStore:DomainService:AppId` is set | low | Same defect as C17-BH1. | patch (with C17-BH1) |
| C17-EC5 | A mismatched but allow-listed identity is admitted when no `dapr-caller-app-id` header arrives | false | The guide already conditions `caller-conflict` on that header (C16-EC1). Reminder submissions travel by Dapr service invocation (`AddEventStoreDaprServiceInvocation`), which attaches it. Requiring the header is a gateway runtime change. | reject |
| C17-EC6 | Custom scope prefixes are not derived | low | Same defect as C17-BH5. | patch (with C17-BH5) |
| C17-EC7 | The runbook dropped its link to the credentials section | low | The C16 rewrite removed `[Trusted-effect submission credentials](#trusted-effect-submission-credentials)` after "assertion lifetime provisioning" (`typed-reminders.md:474`). The allow-list and lifetime checks no longer lead to their setup steps. | patch |
| C17-VG | No verification gaps | n/a | The layer reported none. | n/a |

Delta review C19 (2026-10-09). Scope: the committed C17 guide patches and C18 follow-ups. That is EventStore `6caf9ef4..691fc1bd`, limited to `docs/guides/typed-reminders.md` and `docs/guides/configuration-reference.md` (+28/−15), plus Works `71cb292..53c56ab` for this spec and `sprint-status.yaml` (+29/−10): 183 diff lines. Later EventStore commits (`fe4e7559..37451b52`, Story 8.4 and route inventory #369) are not 4.11 work. Review mode: full. Four layers completed: blind hunter (BH) 12, edge-case hunter (EC) 9, acceptance auditor (AA) 5 with no acceptance-criterion violation, and verification gap with no gaps. Each of the 26 findings was judged before grouping; multi-part findings carry one row per part.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C19-BH1 | The new `Workload` condition fires for every Aspire host and names the wrong risk | medium | The condition is right; what is missing is its trigger on the documented Aspire path. `AddEventStoreDomainModule` sets `EventStore__DomainService__AppId` (`HexalithEventStoreDomainModuleExtensions.cs:65`). Neither it nor CommunityToolkit Dapr hosting sets `DAPR_APP_ID`, so the reminder default falls back to the application name. Under the Works AppHost that name is `Hexalith.Works`, while the issuer's `azp` is the app ID `works`. Every Aspire-composed reminder host, including Works in 4.15, stays `Retrying` unless it sets `EventStore__Reminders__Workload`, and the guide never says that the helper sets `AppId`. EventStore itself has no Aspire reminder host. | patch |
| C19-BH2a | The issuer `Workload` row omits the reminder coupling | low | `configuration-reference.md:504` gives the issuer default (`EventStore:DomainService:AppId`) but not that a reminder host must keep it equal to `EventStore:Reminders:Workload`. Only the reminder rows state the coupling. This is the same incomplete C17-BH1 mitigation as C19-BH1. | patch (with C19-BH1) |
| C19-BH2b | `EventStore:DomainService:AppId` has no configuration-reference entry | low | Pre-existing; the rows at `:496` and `:504` name it as a default. A new section is more than a direct correction, and no wrong setting follows from its absence. | reject |
| C19-BH3 | Not every `5501` `401` is an assertion denial | low | `WorkloadJwtBearerEvents.GetMessageRejection` returns `verifier-unconfigured` and the app-channel `channel-*` reasons. `MessageReceived` fails with them, and `Challenge` records them as `401`. The paragraph calls every `401` an assertion denial and lists only submitter-side checks, so a fault in the gateway's own `APP_API_TOKEN` or `Authentication:JwtBearer` sends the operator to the wrong service. | patch |
| C19-BH4 | The newly named `503` has no remedy | low | `Challenge` returns `503` only for `verifier-unavailable`, which means signing keys or metadata cannot be retrieved (`security-model.md`, Internal Workload Assertions). Every check the paragraph lists is a credential remedy. | patch (with C19-BH3) |
| C19-BH5 | "Check admission component registration" hides the likeliest cause | false | The same paragraph links `trusted-effects.md#production-admission-gate`. Its first sentence says `ITrustedEffectRetentionGate` has no default registration, and its audit paragraph says no default audit sink is registered. The "C18-BH4 evidence understates" part would edit this spec. | reject |
| C19-BH6 | The audit-record check cannot be applied, and correlation guidance is missing | false | With no sink, `TrustedEffectAdmissionPolicy.PrepareAsync` throws before any audit write, which is exactly the "no record" branch the runbook covers. Correlating `5501`, `denied`, and event `200219` by trace, workload, purpose, and time would be new runbook material. The production sink belongs to Story 4.16 (carried). | reject |
| C19-BH7 | The Azure Container Apps sentence understates the impact and contradicts "must configure" | false | The platform injection satisfies the preceding requirement, as the sentence itself says. "Every reminder actor call receives `401`" is accurate for this guide's scope; the broader domain-service consequence is in the host-composition paragraph (carried C17-BH10a/b). An operator who follows the instruction meets no failure. | reject |
| C19-BH8 | The runbook paragraph should be a status/reason table | low | Readability only; restructuring is a rewrite (C17-BH4 precedent). | reject |
| C19-BH9 | C18 rejects BH2 and BH3, then implements both | low | True of the record, but the fix edits this spec. The C18 completion does record the reachable `6caf9ef4`. | reject (spec-only) |
| C19-BH10 | The C18 triage block breaks the triage format, and its IDs collide with iteration 0 | low | True: the IDs `BH1`…`EC1` are unprefixed, and the block sits after the C17 Rejected list. The fix edits this spec. C19 rows use the `C19-` prefix. | reject (spec-only) |
| C19-BH11a | Spec `done` disagrees with sprint `review` | false | bmad-build step 05 sets spec `done` and sprint `review`, which is the house handoff. Correcting the C18-BH1 "in review" wording would edit this spec. | reject |
| C19-BH11b | The sprint comments omit the C18 runbook patches | low | True; this review's status sync rewrites both comment lines. | reject (superseded) |
| C19-BH12a | The verification record names no command and does not record the anchor check | low | Spec-record wording, so the fix edits this spec. C19 confirmed that every new anchor exists. | reject (spec-only) |
| C19-BH12b | The cited test evidence does not test a documentation change | false | The completion says the patch is documentation-only. It cites the tests as unchanged-runtime regression evidence, not as proof of the guide. | reject |
| C19-BH12c | The push decision has no record of being carried out, and the gitlink moved twice | medium | Same defect as C19-AA1. | decision (resolved, with C19-AA1) |
| C19-EC1 | Authority mode needs `Workload` set to the client's `azp` | false | The guide requires the authority client's `azp` to be the app ID, so the `DAPR_APP_ID` default already matches it (carried C17-BH4). The case where `DAPR_APP_ID` is unset is C19-BH1. | reject |
| C19-EC2 | An explicit `Workload` with surrounding whitespace mismatches | low | `JwtWorkloadAssertionIssuer` trims `Workload` and `ResolveWorkload` does not. Configuration binders add no whitespace, though, so this takes an operator typo. The correcting trim is runtime work (carried C15-EC5 pattern). | reject |
| C19-EC3 | A custom `AudienceScopePrefix` with the default audience is not derived | low | `GetAudienceScope` always prepends the configured prefix (`WorkloadAssertionIssuerOptions.cs:81`). The guide derives the scope for a changed audience and for a custom operation prefix, but not for this case. The authority then rejects the default-form scope, and every submission gets `401`. | patch |
| C19-EC4 | `channel-*` and `verifier-unconfigured` `401`s get submitter-side remedies | low | Same defect as C19-BH3. | patch (with C19-BH3) |
| C19-EC5 | `503 verifier-unavailable` has no remedy | low | Same defect as C19-BH4. | patch (with C19-BH3) |
| C19-EC6 | Actor-stage `denied` records can be misattributed to a `403` | low | `AggregateActor` appends actor-stage `denied` records, but those failures reach the caller as `500`, not as a `5501` `403`. A retried reminder repeats one stage, so a wrong attribution needs two concurrent failure modes. The proposed qualifiers are new correlation guidance. | reject |
| C19-EC7 | The runbook names the wrong exception type for a timeout | low | `AddEventStoreDomainService` calls `AddServiceDefaults` (`EventStoreDomainServiceExtensions.cs:490`), which adds `AddStandardResilienceHandler` to every client (`ServiceDefaults/Extensions.cs:41-42`), the documented submitter included. Its total and attempt timeouts fire before `HttpClient.Timeout`, so a timeout surfaces as `TimeoutRejectedException` and an open circuit as `BrokenCircuitException`. The coordinator logs that type name (`ReminderCoordinator.cs:1484-1492`), but the rewritten paragraph names `TaskCanceledException`. | patch |
| C19-EC8 | Pushed Works `c231da0` pins an EventStore commit that no ref contains | medium | Same defect as C19-AA1. | decision (resolved, with C19-AA1) |
| C19-EC9 | The delegation-refusal check lost its route to the delegation claim contract | low | The old page-level link reached `### Delegation claim contract`. The new anchor lands on `## Production admission gate`, which covers retention and audit, not delegation claims. A mismatched `Workload` (C19-BH1) is exactly a delegation refusal. | patch |
| C19-AA1 | The Works gitlink was repointed twice, and pushed `c231da0` pins an unpublished EventStore commit | medium | Works `c231da0`, on `origin/main`, pins `0e6276a6`, the pre-rebase copy of `691fc1bd`. No local or remote branch or tag contains it, and GitHub answers `422 No commit found for SHA`. A checkout or bisect at `c231da0` cannot fetch the submodule. This repeats C15-AA2 despite the C17 decision to repoint once. The tip (`3e32d5c`) is fine. Restoring fetchability takes an outward-facing push, which is the owner's call. The scan found eight such pins. Resolved by the owner: `archive/works-*` EventStore tags for all eight, plus Works `push.recurseSubmodules=check`. | decision (resolved) |
| C19-AA2 | The C16 SHA was annotated rather than corrected | low | The same record as C19-BH9; the fix edits this spec. | reject (spec-only) |
| C19-AA3 | The status wording disagrees, and the sprint comment omits C18 | low | Same as C19-BH11a and C19-BH11b. | reject |
| C19-AA4 | C18 ran three layers, not four | false | bmad-build's internal review runs three context-free layers by design (C4, C5, C10, C11, C16, C18). The code-review workflow adds the acceptance auditor, as this C19 run does. | reject |
| C19-AA5 | The audience-prefix guidance is still lopsided | low | Same defect as C19-EC3. | patch (with C19-EC3) |
| C19-VG | No verification gaps | n/a | The layer reported none. | n/a |

### C20 review triage (2026-10-10)

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C20-BH1 | The parent shows a dirty EventStore submodule | false | The current working tree contains the reviewable guide edits. This workflow has not requested a commit or gitlink advance; the parent dirty marker reflects those uncommitted edits, not a missing documentation change. | reject |
| C20-BH2 | The issuer Workload row applies its equality rule to authority mode | medium | `EventStoreTrustedEffectSubmissionExtensions` says symmetric assertions use `Workload`, while authority assertions use the client's `azp`. The table's unconditional reminder-host sentence can misdirect authority configuration. | patch |
| C20-BH3 | `verifier-unconfigured` guidance names only JwtBearer | low | `WorkloadJwtBearerEvents.GetMessageRejection` also returns this reason when `WorkloadAuthenticationOptions.GetConfigurationFailure` fails for receiver `AllowedCallers`, `Audience`, or `MaximumLifetimeSeconds`. | patch |
| C20-BH4 | An open circuit produces no gateway event 5501 | low | `BrokenCircuitException` is raised in the submitter's resilience handler before HTTP reaches EventStore; the current next sentence directs operators only to gateway event 5501. | patch |
| C20-BH5 | The 503 text conflates metadata retrieval and a signing-key mismatch | low | `WorkloadJwtBearerEvents.Challenge` returns 503 only for `verifier-unavailable`. `WorkloadAssertionEvaluator.ClassifyFailure` maps ordinary signature failures to 401 reasons; a metadata load failure may be 503. | patch |
| C20-BH6 | Other 401 reasons omit receiver-side checks | low | `ClassifyFailure` emits `audience-invalid`, `issuer-invalid`, and `algorithm-invalid` for receiver JWT validation; the runbook routes all remaining 401s to submitter and caller checks. | patch |
| C20-EC1 | Authority mode uses client-token azp rather than issuer Workload | medium | Same verified outcome as C20-BH2. | patch (with C20-BH2) |
| C20-EC2 | Invalid DaprInternal receiver settings produce verifier-unconfigured | low | Same verified outcome as C20-BH3. | patch (with C20-BH3) |
| C20-EC3 | `channel-token-missing` can come from bypassing Dapr invocation | low | `HttpTrustedEffectSubmitter` requires `AddEventStoreDaprServiceInvocation` in its documented client chain. A direct HTTP call can omit the sidecar-injected channel token even with EventStore configured. | patch |
| C20-EC4 | Malformed issuer metadata can be verifier-unavailable | low | `WorkloadAssertionValidator.ValidateAsync` catches metadata-load exceptions and returns `verifier-unavailable`; reachability alone does not settle it. | patch (with C20-BH5) |
| C20-EC5 | A custom client can still surface TaskCanceledException | low | The new text correctly names the standard handler, but removing the former custom-HttpClient timeout clue leaves other supported client configurations without it. | patch |
| C20-VG | No verification gaps | n/a | The layer reported none. | n/a |

### C21 review triage (2026-10-10)

Delta review C21 (2026-10-10). Scope: the C19 and C20 guide patches and their Works record. That is EventStore `691fc1bd..1bc1c76e`, limited to `docs/guides/typed-reminders.md` and `docs/guides/configuration-reference.md`, plus Works `00b989a..e25fe90` for this spec, `sprint-status.yaml`, and the EventStore gitlink: 239 diff lines (+85/−20). The other EventStore commits in the gitlink range (Story 8.4 tests, the 6.1-P1R spec, nested pointer bumps) and the later escape fix `9b26956f` touch no reminder runtime source and are not 4.11 work. Review mode: full. Four layers completed: blind hunter (BH) 10, edge-case hunter (EC) 10, acceptance auditor (AA) 3 with no runtime acceptance-criterion violation, and verification gap with no gaps plus 2 other findings (VG-O). Each of the 25 findings was judged before grouping; multi-part findings carry one row per part.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C21-BH1a | Pushed Works commits pin an EventStore commit GitHub does not have | medium | Works `896d3be` and `976d372`, both on `origin/main`, pin EventStore `890d346f`. It was rebased into `1bc1c76e` (identical patch-id) at 08:44:57, no branch or tag contains it, and the GitHub API answers `422 No commit found for SHA`. This repeats C19-AA1. `push.recurseSubmodules=check` did not stop it: a scratch repro showed the check refuses a pin that is reachable locally but unpushed, yet passes a pin that a rebase or amend has orphaned, because git treats a commit unreachable from every local ref as absent and skips it. | decision |
| C21-BH1b | The C20 completion note names `890d346f` as an unpushed local commit | low | The gitlink is `1bc1c76e` on EventStore `origin/main`, with identical guide content. The fix edits this spec's record, and the decision's resolution record will supersede the note. | reject |
| C21-BH2 | The C20-BH1 rejection rationale is contradicted by the later commit | low | The rationale was true while the build review ran on uncommitted edits; `896d3be` committed and advanced the gitlink afterwards. The fix edits this spec's record. | reject |
| C21-BH3 | Spec `done` disagrees with sprint `review` | false | This is the bmad-build step-05 handoff (C19-BH11a precedent); the verification-gap layer confirmed step-05 prescribes it. | reject |
| C21-BH4 | The Aspire `Workload` trap is documented but not fixed at its source | medium | `AddEventStoreDomainModule` already holds `appId` and sets only `EventStore__DomainService__AppId` (`HexalithEventStoreDomainModuleExtensions.cs:65`); `ResolveWorkload` reads neither that setting nor `Authentication:WorkloadIssuer:Workload`. Pre-existing runtime and package behavior; changing it is outside this docs-only delta and would need a new named public release. | defer |
| C21-BH5 | The Story 4.15 `Workload` handoff exists only in the EventStore guide | medium | `spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md` (draft, 53 lines) never mentions `EventStore__Reminders__Workload`, and the Works AppHost calls `AddEventStoreDomainModule` (`src/Hexalith.Works.AppHost/Program.cs:141`) without it. The fix edits another spec. | defer |
| C21-BH6a | The open-circuit sentence hides the failures that opened the breaker | false | The next sentence sends every request that reached the gateway, including the failures that tripped the breaker, to event `5501`; nothing tells operators to skip them. | reject |
| C21-BH6b | Standard retries can write several `5501` records per submission | low | True for a retried `503`, but each record is accurate and duplicates do not change the diagnosis. | reject |
| C21-BH7 | The "other `401`" bucket misroutes `caller-conflict`, `credential-conflict`, `operation-missing`, and `assertion-stale` | false | The bucket links the credentials section, which documents `caller-conflict` (`typed-reminders.md:299-302`), the operation scope and mapper behind `operation-missing` (`:309-311`), and the lifetime cap behind `assertion-stale` (`:325-329`). `credential-conflict` is a submitter client handler fault, which the bucket names. | reject |
| C21-BH8 | The runbook paragraph should be a status/reason table | low | A rewrite, not a direct correction (C17-BH4, C19-BH8 precedent). | reject |
| C21-BH9 | `configuration-reference.md` gives two identity targets and mixes env-var notation | false | Both name one identity: an authority client's `azp` must be the app ID (the `ClientId` row and credentials step 2). `EventStore__Reminders__Workload` is the form an Aspire host sets. | reject |
| C21-BH10 | The C20 record lacks a range, counts, and patch entries | low | The fix edits this spec's record. The unrelated gitlink-range commits touch no `src/` (AA and VG confirmed). | reject |
| C21-EC1 | Receiver-side `401` mappings omit submitter-side causes | low | Same defect as C21-AA2. | patch (with C21-AA2) |
| C21-EC2 | `caller-conflict` is routed as a handler or lifetime fault | false | Same refutation as C21-BH7. | reject |
| C21-EC3 | An empty or kid-less JWKS yields `401 signature-invalid`, not `503` | false | `SecurityTokenSignatureKeyNotFoundException` derives from `SecurityTokenInvalidSignatureException`, and the guide already maps a key that does not validate to `401 signature-invalid` with a JWKS comparison. The runbook is keyed by the observed status and reason, so the actual `401` reaches the right check. | reject |
| C21-EC4 | `channel-token-invalid` can also come from a direct gateway call | low | The documented chain passes `DAPR_API_TOKEN` to `AddEventStoreDaprServiceInvocation` (`typed-reminders.md:253`). `DaprServiceInvocationHandler` sets `dapr-api-token` without rewriting the URI, so a client aimed at the gateway presents the submitter's token, and `DaprAppChannelToken.Verify` answers `Invalid`, not `Missing`. The runbook names the direct-call check only for `channel-token-missing`. | patch |
| C21-EC5 | An open circuit follows repeated earlier failures | false | Same refutation as C21-BH6a. | reject |
| C21-EC6 | Event `5501` records only credential denials | low | `WorkloadAuthenticationTelemetry.cs:79` logs "Internal credential denied", called only from the channel and workload authentication paths and projection provenance. The rewrite dropped the former list of covered outcomes, so "For requests that reach the gateway, check event `5501`" now implies every such request has one; a `400`, `413`, or actor-stage `500` (C19-EC6) has none. | patch |
| C21-EC7 | Spec status should be `review` | false | Same refutation as C21-BH3. | reject |
| C21-EC8 | The deleted general checks left a `403` missing grant without a remedy | false | The deleted sentence never named the operation grant. A documented submitter's token always carries `eventstore:trusted-effect` (symmetric) or only the trusted-effect scopes (authority), so a missing grant surfaces as `401 operation-missing`, which the "other `401`" sentence routes to the credentials section. | reject |
| C21-EC9 | The C20 note's evidence SHA is orphaned | low | Same as C21-BH1b. | reject |
| C21-EC10 | The C20-BH1 rejection rests on a stale premise | low | Same as C21-BH2. | reject |
| C21-AA1a | The spec SHA is unpushed and Works history pins it; the sprint comment undercounts | medium | Same defect as C21-BH1a. The sprint comment "8 orphaned EventStore pins archived as tags" stays true but will be stale once the decision is resolved. | decision (with C21-BH1a) |
| C21-AA1b | The spec records an unpushed EventStore SHA | low | Same as C21-BH1b. | reject |
| C21-AA2 | The `401` routing no longer keeps submitter checks for mismatch reasons | low | C19's instruction was "Keep the submitter checks for the other `401` reasons." The C20 text sends `caller-not-allowed`, `audience-invalid`, `issuer-invalid`, and `algorithm-invalid` only to EventStore keys. The credentials section says `caller-not-allowed` is a mismatch of the submitter's `Workload` with the allow-list, `audience-invalid` follows a `gatewayAudience` mismatch, a symmetric submitter signs with its own `Authentication:JwtBearer:Issuer` (`JwtWorkloadAssertionIssuer.cs:181`), and `algorithm-invalid` follows a signing-mode mismatch. Event `5501` does not log the presented caller, so an operator who finds the EventStore value correct has no next step. | patch |
| C21-AA3 | The C20-BH1 rejection reason is false | low | Same as C21-BH2. | reject |
| C21-VG-O1 | A ninth unfetchable Works pin | medium | Same defect as C21-BH1a. | decision (with C21-BH1a) |
| C21-VG-O2 | The C20 completion note is out of date | low | Same as C21-BH1b. | reject |
| C21-VG | No verification gaps | n/a | The layer reported none. | n/a |

### C22 review triage (2026-10-10)

Review scope was the full Works baseline diff plus the current EventStore guide
patch. The broad baseline includes historical Story 4.11 evidence; repeated
findings below retain their earlier verdicts and routes. The blind layer filed
15 findings, the edge-case layer 5, and verification-gap 1. Each finding was
judged before grouping.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C22-BH1 | Dirty EventStore gitlink prevents a clean checkout from receiving the guide edit | false | The guide is the working-tree change under review; the spec is `in-review`, not a claim that HEAD already delivers it. Delivery is the workflow handoff after review. | reject |
| C22-BH2 | Sprint tracker still calls the three C21 patches open | low | The guide and spec now mark them complete, but both tracker comments and the story status still describe the earlier review state. | patch |
| C22-BH3 | Submitter `Authentication:JwtBearer` wording misroutes authority mode | low | The new receiver-mismatch sentence applies to both supported assertion modes; the guide's credential section says an external authority issues and signs in authority mode. | patch |
| C22-BH4 | Clone-local pre-push guard does not protect other clones | low | The C21 owner decision explicitly installed a local guard and did not claim a shared policy; tracking a cross-clone enforcement tool would be a separate governance change beyond the R6 intent. | reject |
| C22-BH5 | Aspire helper leaves reminder Workload default mismatched | medium | Carried C21-BH4: the helper sets AppId but not the reminder Workload; the guide documents the override and the source fix needs a separate public release. | carried defer |
| C22-BH6 | Story 4.15 draft lacks the Workload override | medium | Carried C21-BH5: the Works adoption requirement is already in the deferred ledger. | carried defer |
| C22-BH7 | Production callback origin needs a cross-workload negative test | maybe-false | Carried the original callback-origin deferral: the production mTLS/ACL profile and reachability proof belong to the Story 4.16 admission gate. | carried defer |
| C22-BH8 | Restore omitting both discovery documents can strand item state | maybe-false | Carried R1-BH9: code cannot discover that item, but the approved restore model has not established whether this state is reachable; the 4.16 drill must settle it. | carried defer |
| C22-BH9 | Dispositions lack retention and offboarding erasure | medium | Carried C6-BH7: last-write-wins disposition and index retention wait for the AD-28/4.16 real-data gate. | carried defer |
| C22-BH10 | Quarantine lacks an operator disposition path | medium | Carried the original operator-disposition deferral: a quarantined name stays unarmed until the 4.16 repair path exists. | carried defer |
| C22-BH11 | Corrupt discovery rows can degrade readiness indefinitely | medium | Carried R2-BH3/R2-BH4: malformed registry/candidate evidence is retained; safe repair is deferred to 4.16 rather than deleted on scan. | carried defer |
| C22-BH12 | Hung actor work could block a pass | maybe-false | Carried R2-BH5: the proxy awaits without a caller token, but the Dapr HTTP/store timeout behavior under a hung dependency has not been established. | carried defer |
| C22-BH13 | Epic context says Claim is the only InProgress entry | low | Carried C12-BH7: Resume also enters InProgress. This is an agent-context correction already deferred. | carried defer |
| C22-BH14 | Epic context omits persist-before-publication wording | low | Carried C3-BH9 and C7-BH10: architecture remains authoritative; agent-context refresh was deferred. | carried defer |
| C22-BH15 | One deferred ledger source path is absolute | low | Carried C8-BH10: the entry is historical, readable here, and changing it would rewrite an existing deferred entry. | carried reject |
| C22-EC1 | Replay fixture does not verify the copied test and retained dependency DLLs | medium | `public-runtime-bindings.json` records the test assembly and UniqueIds source DLL hashes, but `prepare_host` checks only the dependency manifest before replacement. A dirty or stale build can run different tests or dependency code. | patch |
| C22-EC2 | Replay accepts an empty or skipped xUnit selection | medium | Both filtered executables exit zero for no matches; `run` checks only the exit code and does not inspect the XML. The recorded current proof did execute 1 and 227 passing tests, but a later replay can falsely report success. | patch |
| C22-EC3 | Replay downloads an unbounded archive before hash checking | low | `response.read()` has no size bound although the inventory records `archive_bytes`; a too-large response can consume memory before SHA verification. | patch |
| C22-EC4 | Epic context misstates Claim-only entry | low | Same location and claim as C22-BH13 and C12-BH7. | carried defer |
| C22-EC5 | Epic context drops explicit publication ordering | low | Same location and claim as C22-BH14 and C3-BH9. | carried defer |
| C22-VG1 | Replay success does not require selected tests to pass | medium | Pre-verified by the reviewer's zero-match filtered runs: both exited zero and emitted `total=0`. The replay needs XML count, pass, failure, and skip checks. | patch (with C22-EC2) |

### C23 review triage (2026-10-10)

Delta review C23 (2026-10-10). Scope: the C21 guide patches and the C22 fixes. That is EventStore `1bc1c76e..c3bac11a`, limited to `docs/guides/typed-reminders.md` (`c3bac11a`), plus Works `e25fe90..79689aa` (`a1e282a` and two gitlink bumps): 8 file diffs, 408 diff lines (+279/−13). The other commits in the gitlink range (`36a99504` versioned-actor tests, `6421b236` nested pointer bumps, `9b26956f` escape fix) touch no `src/` and are not 4.11 work. Review mode: full. Four layers completed: blind hunter (BH) 16, edge-case hunter (EC) 10, acceptance auditor (AA) 7 with no acceptance-criterion violation, and verification gap with 1 gap (VG1) plus 3 other findings (VG-O). Each of the 37 findings was judged before grouping; multi-part findings carry one row per part.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C23-BH1 | The evidence packet no longer holds the recipe that ran | medium | `parent-replay-summary.json:17` records `executed_recipe_sha256` `fa11cdd6…`, while `evidence-sha256.json` now binds `replay-proof.py` to `e6a6f48c…`. The executed bytes exist only at `a1e282a^`. Neither `REPLAY.md` nor the summary says so, and the bound recipe has never run end to end (C22 record). | decision |
| C23-BH2 | The test-assembly hash binds the replay to one absolute checkout path | low | The built `Hexalith.EventStore.DomainService.Tests.dll` embeds absolute `.cs` paths (`/home/administrator/projects/hexalith/works/references/Hexalith.EventStore/tests/…`) from xUnit v3 `CallerFilePath`, and EventStore sets no `PathMap` or `ContinuousIntegrationBuild`. `REPLAY.md:6` says only "Run from the Works workspace", so a replay from any other clone fails with `SHA-256 mismatch for source fixture test assembly` and no explanation. | patch |
| C23-BH3a | `REPLAY.md` still describes the pre-C22 recipe | low | `REPLAY.md:17` says the recipe "verifies its exact dependency manifest" and omits the test-assembly and retained-dependency hashes, the 1/227 result checks, the bounded download, and `test_replay_proof.py`. | patch (with C23-VG-O2) |
| C23-BH3b | The spec gives no command for the three focused replay tests | low | The fix edits this spec's record. | reject |
| C23-BH4 | The Contracts consumer test is unhashed and the checkout may be dirty | low | True: the recipe checks only `git rev-parse HEAD` and records no Contracts.Tests hash. Pre-existing, a replay needs a fresh `07d1e23a` submodule checkout, and a clean-tree check adds a guard. | reject |
| C23-BH5 | The focused tests cover less than the C22 record claims | low | Only `(0,0,0)` and `(1,0,1)` are exercised; failed, error, wrong-count, multi-assembly, short-download and public-DLL-swap cases are not. `verify_result_xml` is an exact dictionary comparison that rejects them all by inspection; adding cases to a hash-bound recipe test is more than a direct correction. | reject |
| C23-BH6 | The replay ledger does not record the new checks | low | True: `replay-commands.json` lists command exits only, the counts are hard-coded, and a failed check leaves only exit-0 command rows. The failure signal is the process exit and traceback, and the ledger never claims success. Recording checks adds code. | reject |
| C23-BH7 | Symmetric-mode advice compares the signing key for `algorithm-invalid` | low | `JwtWorkloadAssertionIssuer.cs:187` always signs HS256, and a symmetric receiver accepts only `[]` or `HS256` (`configuration-reference.md:453`). So `algorithm-invalid` means the two sides use different modes, while a wrong key yields `signature-invalid`. The C21-AA2 fix text said "signing mode". | patch (with C23-AA3, C23-EC6) |
| C23-BH8 | The `DAPR_API_TOKEN` sentence states a fact without its consequence | low | Read alone, "forwards the submitter's `DAPR_API_TOKEN`" can suggest aligning it with EventStore's `APP_API_TOKEN`. The point is that a direct gateway call presents that token, which EventStore rejects as `channel-token-invalid`. A one-sentence rewording fixes it. | patch |
| C23-BH9a | The non-`5501` status list is incomplete | false | "Event `5501` records internal credential denials only" makes the list exhaustive; `400`, `413`, and `500` are examples. | reject |
| C23-BH9b | The runbook gives no next step for a non-credential gateway failure | low | True since C21-EC6 scoped `5501` truthfully, but the gap predates it: the old text misrouted these failures to `5501`. Naming the right log per status needs gateway-logging research, and these are incident-only paths. | reject |
| C23-BH10 | The runbook does not say how to find a reminder's `5501` record | low | Pre-existing instruction; the trace-ID correlation fallback would need verification before documenting it. | reject |
| C23-BH11 | The C22-BH1 rejection cites `in-review` against frontmatter `done` | low | The fix edits this spec's record. | reject |
| C23-BH12 | The C22 record lacks range, counts, layer status, and the unrelated gitlink commits | low | The fix edits this spec's record (C21-BH10 precedent). None of `36a99504`, `6421b236`, or `9b26956f` touches `src/`. | reject |
| C23-BH13 | Known-false C20 statements stay without a superseded pointer | low | The fix edits this spec's record. | reject |
| C23-BH14 | The pre-push guard is untracked and skips merges and uninitialized paths | low | Verified: `git diff-tree -r` without `-m` prints nothing for a merge (4 merges in 349 Works commits), and `[ -e "$path/.git" ] \|\| continue` skips uninitialized submodules by design. The hook is local tooling outside this diff (C21 owner decision; C22-BH4 rejected shared enforcement), and a merge that introduces a gitlink neither parent pushed is rare. | reject |
| C23-BH15a | Sprint tracker comments omit the C22 fixes | low | Both `last_updated` comments stop at "C21 guide patches complete"; the C22 replay hardening and guide fix are unmentioned. | patch |
| C23-BH15b | The commit title "close review" contradicts "handed to review" | low | Changing it rewrites history. | reject |
| C23-BH16a | The new ledger entries use the legacy format | false | Every 4.11 ledger section since 2026-09-29 uses this format, and `bmad-loop-sweep --migrate` converts legacy entries. | reject |
| C23-BH16b | The ledger C21-BH4 entry cites `:126` without its file and says the helper sets "only" AppId | low | `ResolveWorkload` is at `src/Hexalith.EventStore.DomainService/EventStoreReminderServiceCollectionExtensions.cs:126`, not the Aspire file cited just before, and `HexalithEventStoreDomainModuleExtensions.cs:66` also sets `EventStore__DomainService__ServiceVersion`. | patch |
| C23-EC1 | Relocated replays fail the test-assembly hash | low | Same defect as C23-BH2. | patch (with C23-BH2) |
| C23-EC2 | A dirty checkout can change the Contracts consumer test | low | Same as C23-BH4. | reject |
| C23-EC3 | A rejected replay leaves a ledger of exit-0 commands | low | Same as C23-BH6. | reject |
| C23-EC4 | A failing subtest skips its restore and masks the second subtest | low | True, but the failing first subtest already fails the test, so there is no false pass; only the second subtest's diagnostic is lost. | reject |
| C23-EC5 | The non-`5501` list omits `429` | false | Same refutation as C23-BH9a; the remaining no-next-step claim is C23-BH9b. | reject |
| C23-EC6 | The symmetric `algorithm-invalid` branch compares keys | low | Same defect as C23-BH7. | patch (with C23-BH7) |
| C23-EC7 | The mode split names only Development | low | `JwtBearerAuthenticationContract.cs:132` allows symmetric validation in a non-Production environment with `AllowInsecureSymmetricKey=true` (`configuration-reference.md:456`), which "symmetric Development mode" excludes. | patch |
| C23-EC8 | The executed recipe is gone from the packet | medium | Same defect as C23-BH1. | decision (with C23-BH1) |
| C23-EC9 | The C22 record understates the recipe change | low | The fix edits this spec's record; the path binding itself is C23-BH2. | reject |
| C23-EC10 | `a1e282a` pins `9b26956f`, which lacks the guide edit | low | True, but the next commit `79689aa` pins `c3bac11a`, both are unpushed, and `9b26956f` is fetchable; a fix rewrites history. | reject |
| C23-AA1 | The replay recipe was replaced after it ran | medium | Same defect as C23-BH1. | decision (with C23-BH1) |
| C23-AA2a | The C21-BH6a/EC5 rejection rationale no longer holds | low | The fix edits this spec's record. | reject |
| C23-AA2b | Breaker-tripping `500`s and timeouts have no diagnostic route | low | Same gap as C23-BH9b. | reject |
| C23-AA3 | The symmetric sentence departs from the C21-AA2 fix text | low | Same defect as C23-BH7. | patch (with C23-BH7) |
| C23-AA4 | The C22-BH1 rejection rests on a false status | low | Same as C23-BH11. | reject |
| C23-AA5a | `REPLAY.md` omits the C22 checks | low | Same defect as C23-BH3a. | patch (with C23-BH3a) |
| C23-AA5b | The new hash check may make the replay path-dependent | low | Same defect as C23-BH2. | patch (with C23-BH2) |
| C23-AA6 | The focused tests cover less than the C22 claim | low | Same as C23-BH5. | reject |
| C23-AA7 | The C22 record omits the unrelated gitlink commits | low | Same as C23-BH12. | reject |
| C23-VG1 | The `main()` pass-count gates are untested | low | Pre-verified: deleting both `verify_result_xml(output / …)` calls left all three tests green. The filed `patch` disposition was weighed: the gates are two lines in a hash-bound evidence recipe whose every edit rebinds `evidence-sha256.json` under review, the tests run only by hand, and the fix is a new mocked end-to-end test of `main()`. | reject |
| C23-VG-O1 | The retained replay evidence came from a recipe the packet no longer has | medium | Same defect as C23-BH1. | decision (with C23-BH1) |
| C23-VG-O2 | `REPLAY.md` is out of date | low | Same defect as C23-BH3a. | patch (with C23-BH3a) |
| C23-VG-O3 | The C22 record names only part of the submodule bump | low | Same as C23-BH12. | reject |

### C24 review triage (2026-10-10)

The three context-free review layers examined the full Works baseline diff, the
current EventStore guide diff, and the restored untracked recipe. That baseline
also contains already reviewed historical commits and a separate CI/CD spec.
Blind hunter reported 11 findings, edge-case hunter 8, and verification-gap 1.
Each finding was judged before grouping. No new patch or deferral resulted.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C24-BH1 | The public source audit omits the changed endpoint extension | low | Carried C13-AA2: the audit comparison is limited to the paths it names; C13 separately reviewed the endpoint delta and C13-AA1 verified the Dapr channel behavior. | carried reject |
| C24-BH2 | A dirty replay checkout can alter the Contracts consumer | low | Carried C23-BH4: `HEAD` alone does not bind uncommitted Contracts test edits; the existing hash-bound manual replay and fresh-checkout requirement remain. | carried reject |
| C24-BH3 | The XML count gate does not check a package-consumer test name | low | The method filter selects `*PackagedReminderApiRunsWithoutWorksTypes`, and a zero-match run fails the new 1/1 gate. A different same-named or edited test requires the pre-existing unbound Contracts fixture in C23-BH4; checking XML names alone would not authenticate its body. | reject |
| C24-BH4 | A trickling package response can outlast the socket timeout | low | `response.read(archive_bytes + 1)` is length-bounded but has no whole-download deadline. This manual replay uses official NuGet archives; deadline machinery is disproportionate to that rare failure. | reject |
| C24-BH5 | Replay failures may leave no command ledger | low | Carried C23-BH6: process failure and traceback report a failed check, and the ledger never claims a successful run. Persisting every exit path adds code to a one-shot evidence recipe. | carried reject |
| C24-BH6 | The replay requires historical dependency pins and one absolute checkout path | low | `REPLAY.md` now states the bound absolute path (C23-BH2 fix), and Works history records the root dependency pointers used by the historical run. A portable build would not reproduce the path-bound assembly hash; reconstructing every dependency pin in this archival guide exceeds a direct correction. | reject |
| C24-BH7 | Epic context says Claim is the only entry to InProgress | low | Carried C12-BH7: Resume also enters InProgress; the agent-context refresh is already deferred. | carried defer |
| C24-BH8 | Epic context omits explicit persist-before-publication wording | low | Carried C3-BH9: the architecture remains authoritative and the context refresh is already deferred. | carried defer |
| C24-BH9 | Epic context omits the runtime persisted-state evidence rule | low | Carried C12-BH9: this story did run persisted-state checks; the agent-context refresh is already deferred. | carried defer |
| C24-BH10 | The separate CI/CD spec retains unresolved intent-gap rows | medium | Carried C12-BH10 and its existing ledger entry: this is another story's spec, outside Story 4.11's reminder intent. | carried defer |
| C24-BH11 | The separate CI/CD spec cites a temporary TRX | medium | Carried C12-BH10: the retained CI/CD evidence issue is already in the deferred ledger. | carried defer |
| C24-EC1 | Uncommitted replay test edits could create a false proof | low | Carried C23-BH4 and C24-BH2: the Contracts fixture is not hash-bound. | carried reject |
| C24-EC2 | An ignored Contracts test binary may be reused | low | Carried C23-BH4: the unbound Contracts fixture includes binary provenance; the DomainService regression assembly is hash-bound before public DLL substitution. | carried reject |
| C24-EC3 | A slow archive response has no whole-download deadline | low | Carried C24-BH4: the length cap does not bound elapsed time, but the manual official-NuGet replay does not justify extra deadline machinery. | carried reject |
| C24-EC4 | The retained executed recipe can accept zero or skipped tests | low | The file is an immutable copy of the recipe that ran on 2026-10-08; changing it would destroy the evidence hash. `REPLAY.md` directs new runs to the hardened recipe, whose exact XML gates reject those outcomes. | reject |
| C24-EC5 | Epic context misstates Claim-only entry | low | Carried C12-BH7 and C24-BH7. | carried defer |
| C24-EC6 | Epic context drops explicit publication ordering | low | Carried C3-BH9 and C24-BH8. | carried defer |
| C24-EC7 | Epic context drops the persisted-state test rule | low | Carried C12-BH9 and C24-BH9. | carried defer |
| C24-EC8 | Unauthorized callback has no durable disposition | false | Carried EC13 and C7-EC7: token denial logs audit event 200209 and returns an empty 401; writing an unauthenticated durable disposition would open an unauthenticated write path. | carried reject |
| C24-VG1 | Focused tests do not execute the new `main()` XML gates | low | Carried C23-VG1: deleting those two calls leaves 3/3 focused tests green, but each gate is two explicit lines in a hash-bound, hand-run recipe; a mocked end-to-end test is disproportionate. | carried reject |

### C25 review triage (2026-10-10)

Delta review C25 (2026-10-10). Scope: the C23 patches and the C24 record. That is Works `79689aa..c43ff7f` (`5f6b536`, `c43ff7f`), limited to `_bmad-output/implementation-artifacts` and the EventStore gitlink, plus EventStore `c3bac11a..c6d26698`, limited to `docs/guides/typed-reminders.md` (`c6d26698`): 9 file diffs, 478 diff lines (+312/−17). Excluded as not 4.11 work: the Hexalith.Conversations and Hexalith.Tenants gitlink bumps in `5f6b536`, and EventStore `882a0761` (Story 6.6 logical-replay removal, 143 files, no reminder or Effects file). Review mode: full. Four layers completed: blind hunter (BH) 12, edge-case hunter (EC) 6, acceptance auditor (AA) 8 with no acceptance-criterion violation, and verification gap with no gap plus 2 other findings (VG-O). Each of the 28 findings was judged before grouping; multi-part findings carry one row per part.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C25-BH1 | The EventStore gitlink move carries an undisclosed `src/` deletion | low | `c3bac11a..c6d26698` includes `882a0761`, which deletes 104 `src/*.cs` files in Client and Server and removes `DomainServiceRequestRouter.ProcessCompletedLogicalAsync`. This spec never names it, and the C23 completion says "No runtime code or package API changed". The bump is the owner's routine sync in `5f6b536`; it touches no reminder or Effects file, and no Works `.cs` file references any of the 104 deleted type names. Disclosing it edits this spec's record. | reject |
| C25-BH2 | The C24 record omits range, size, and mode, and ran without the acceptance auditor | low | True: three layers, no range, and no C24 findings or completion section. The fix edits this spec's record; this four-layer C25 review, acceptance auditor included, now covers the same delta. | reject |
| C25-BH3 | `REPLAY.md` says the retained summaries describe the executed recipe, but two summary checks came from outside it | low | `parent-replay-summary.json` reports `recorded_test_assembly_reproduced` and `recorded_source_transitive_dependencies_reproduced` as `true`. The executed recipe (`fa11cdd6…`) hashes neither the test assembly nor `Hexalith.Commons.UniqueIds.dll`, and `parent-replay-commands.json` records no such hash; the C12 record says the parent compared those bytes separately. `REPLAY.md:39` reads as if the executed recipe made checks that only the current recipe has. | patch |
| C25-BH4 | The C23 decision does not explain the 2 of 14 uncached archives | low | The fix edits this spec's record. | reject |
| C25-BH5 | The new `DAPR_API_TOKEN` sentence names the wrong sidecar | low | Same defect as C25-VG-O2. | patch (with C25-VG-O2) |
| C25-BH6 | The symmetric text drops the signing-key remedy and misroutes `signature-invalid` | false | The sentence names the cause ("A mismatched symmetric signing key yields `signature-invalid`"), so `signature-invalid` is not an "other `401` reason", and the same paragraph says to "compare the assertion's signer with EventStore's trusted signing key or issuer JWKS". | reject |
| C25-BH7 | `AllowInsecureSymmetricKey` lacks its configuration path | low | Every other setting in the paragraph is a full path, and `configuration-reference.md:872` names `Authentication:JwtBearer:AllowInsecureSymmetricKey`; `typed-reminders.md:506` gives only the leaf name. | patch |
| C25-BH8a | `REPLAY.md` does not name the `Hexalith.Builds` pin | low | Carried C24-BH6: a mismatched Builds pin changes the bound test-assembly hash and fails loudly, and reconstructing every root pin exceeds a direct correction. | carried reject |
| C25-BH8b | `REPLAY.md` gives no restore step after rewinding the live submodule | low | Same defect as C25-EC3. | patch (with C25-EC3) |
| C25-BH9 | The new `REPLAY.md` result sentence is garbled and incomplete | low | `REPLAY.md:20-21` ("rejects an empty, failed, or skipped package-only result instead of accepting anything other than 1/1 passed") is a double negative; lines 24-25 omit the `errors=0` and single-assembly checks that `verify_result_xml` enforces; line 26 is a stray short wrap. | patch (with C25-AA8) |
| C25-BH10 | The commit-message evidence is incomplete and unreferenced | false | The JSON retains each validated candidate verbatim (both commits are single-line); the Works entry cannot record its own commit SHA; `5f6b536` and `882a0761` are owner commits outside the assistant validation rule; citing the file from this spec edits its record. | reject |
| C25-BH11 | Spec frontmatter `done` conflicts with tracker `review` | low | Carried C23-BH11: the fix edits this spec's record. | carried reject |
| C25-BH12 | The C23 result line counts 7 patch entries, but 8 were applied | false | The line counts the 7 finding-derived entries (13 rows) exactly; the eighth patch is labelled "from the C23-BH1 decision" and came from the owner's choice. | reject |
| C25-EC1 | The service-invocation sentence points operators at the submitter's sidecar | low | Same defect as C25-VG-O2. | patch (with C25-VG-O2) |
| C25-EC2 | A replay from another path fails late with a generic hash mismatch | low | Carried C23-BH2: `REPLAY.md:5-8` now documents the bound path, and a path guard adds code to the hash-bound recipe. | carried reject |
| C25-EC3 | Rewinding the live submodule for a replay leaves the Works gitlink modified | low | `REPLAY.md:5-8` requires `07d1e23a` checked out in `references/Hexalith.EventStore` and gives no restore step. A later `commit -a` re-pins EventStore to `07d1e23a`, which the local pre-push guard accepts because that commit is pushed. One sentence fixes it. | patch |
| C25-EC4 | Tracker "ready for human review" conflicts with spec `done` | low | Same as C25-BH11. | carried reject |
| C25-EC5 | The tracker no longer records the C22 fixes, though C23-BH15a is ticked | low | Same defect as C25-AA1. | patch (with C25-AA1) |
| C25-EC6 | An unreviewed runtime deletion enters the Works EventStore pin | low | Same as C25-BH1. | reject |
| C25-AA1 | The C23-BH15a tracker patch is ticked but no longer in effect | low | `5f6b536` named the "C22 replay-recipe hardening" and "C17-C22 corrections"; `c43ff7f` replaced all three comments (`sprint-status.yaml:2,38,40`) with C23/C24 wording that names neither the C22 replay hardening nor the C22 guide fix. The C23 completion claim "The Works sprint tracker records the C22 replay and guide fixes" is false at `c43ff7f`. | patch |
| C25-AA2 | The `DAPR_API_TOKEN` sentence names the wrong sidecar | low | Same defect as C25-VG-O2. | patch (with C25-VG-O2) |
| C25-AA3 | The gitlink move pulls in `882a0761` without disclosure | low | Same as C25-BH1. | reject |
| C25-AA4 | C24 was recorded as a pass without the acceptance auditor | low | Same as C25-BH2. | reject |
| C25-AA5 | Story status conflicts between spec and tracker | low | Same as C25-BH11. | carried reject |
| C25-AA6 | The commit-message evidence is unreferenced and labelled C24 | false | Same refutation as C25-BH10; the folder names the C24 round that produced those commits. | reject |
| C25-AA7 | `REPLAY.md` does not name the root dependency pins | low | Same as C25-BH8a. | carried reject |
| C25-AA8 | One `REPLAY.md` sentence is garbled | low | Same defect as C25-BH9. | patch (with C25-BH9) |
| C25-VG-O1a | The gitlink move carries `882a0761`, and Works CI never compiles that source | low | Same as C25-BH1; Works package-mode CI is pre-existing. | reject |
| C25-VG-O1b | The Conversations and Tenants gitlink moves were not reviewed | false | Neither is 4.11 work; both were excluded by the scope approved at the C25 checkpoint. | reject |
| C25-VG-O2 | The new sentence says the submitter's sidecar presents EventStore's app-channel credential | low | `typed-reminders.md:490-491`. In Dapr service invocation the receiving application's own sidecar presents `APP_API_TOKEN` (`HexalithEventStoreAppChannelExtensions.cs:14`, `DaprAppChannelToken.cs:15`), as the guide itself says at lines 275-276 and 487. An operator diagnosing `channel-token-invalid` could inspect the submitter's sidecar. | patch |

### C26 review triage (2026-10-10)

Delta review C26 (2026-10-10). Scope: the C25 patches. That is Works `3ebc68b..718726d` (`718726d`), limited to `_bmad-output/implementation-artifacts` and the EventStore gitlink, plus EventStore `c6d26698..a5405602`, limited to `docs/guides/typed-reminders.md` (`a5405602`): 6 file diffs, 177 diff lines (+60/−26). Excluded as not 4.11 work: the Hexalith.Builds (`6a002df5..9c46679b`) and Hexalith.Projects (`44188d89..0ee8d9a4`) gitlink bumps in `718726d`, and EventStore `83987f22` (Story 6.6 event-versioning boundary fixes) and `73c4b4f3` (6.1 published-run evidence and `tools/p1r*`), neither of which touches a reminder or Effects file. Review mode: full. Four layers completed: blind hunter (BH) 9, edge-case hunter (EC) 6, acceptance auditor (AA) 7 with no acceptance-criterion violation (one carried), and verification gap with no gap plus 2 other findings (VG-O). Each of the 24 findings was judged before grouping; multi-part findings carry one row per part.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C26-BH1a | The restore step checks out the gitlink in the Works index, so a staged rewound gitlink survives it | low | True: `git submodule update` checks out the index gitlink. The operator must first stage `07d1e23a`, which `REPLAY.md:17-18` forbids committing, and the fix adds an unstage-and-verify guard. | reject |
| C26-BH1b | The restore leaves the submodule on a detached `HEAD` instead of its previous branch | low | True: the submodule is on `main`, and `git submodule update` detaches it at the pin. Replays are rare (C23 owner decision: no end-to-end rerun), a commit on a detached `HEAD` fails at `git push`, and restoring branch context adds steps beyond a direct correction. | reject |
| C26-BH2a | `REPLAY.md` gives no rewind command or clean-tree precondition | low | Lines 3-8, unchanged here, name the commit and the bound path; a guarded rewind procedure exceeds a direct correction for this archival guide. | reject |
| C26-BH2b | The restore step does not say it also applies after a failed replay | false | "After the replay" is not limited to a successful run; a `ValueError` or `RuntimeError` exit still ends the replay. | reject |
| C26-BH2c | Replay build outputs from `07d1e23a` survive the restore | low | True, as after any checkout. The checkout gives the changed sources new timestamps, so the next incremental `dotnet build` rebuilds; running stale binaries requires skipping the build, and the caveat adds guidance. | reject |
| C26-BH3 | The executed-recipe sentence omits the missing result gate and archive-length check | low | `replay-proof.executed-2026-10-08.py` only checked exit codes for test runs (line 102); `verify_result_xml`, the bounded archive read, and the length check exist only in the current recipe (`diff -u`). `REPLAY.md:45-47` names only the test-assembly and retained-dependency hashes, so a reader can attribute the 1/1 and 227/227 gate at lines 27-30 to the 2026-10-08 run. The C12 record says the parent saw zero errors, failures, or skips. | patch |
| C26-BH4 | `REPLAY.md` no longer says the recipe enforces the result gate | low | The C25 rewrite replaced "It rejects …" with "Each run's result XML must report …" (`REPLAY.md:27-30`), stating the gate but not that `replay-proof.py:137,142` raises on it. C24-EC4 was rejected because `REPLAY.md` directs new runs to a recipe whose gates reject those outcomes. Same defect as C26-BH3: the text does not say which recipe enforces the gate. | patch (with C26-BH3) |
| C26-BH5 | `test_replay_proof.py` does not test zero or two assemblies, `errors>0`, `failed>0`, or a missing attribute | low | Carried C23-VG1 and C24-VG1. `verify_result_xml` (`replay-proof.py:28-36`) rejects each case by construction: an assembly-count check plus whole-dict equality, with a missing attribute read as `-1`. This diff changed no code. | carried reject |
| C26-BH6a | The EventStore gitlink move carries undisclosed runtime code, contradicting "No runtime code or package API changed" | low | `c6d26698..a5405602` adds `83987f22`, which changes `EventPersister`, `EventStoreDomainEventProcessor`, `EventStoreAggregate`, `EventPayloadEvolutionRegistry`, and `DomainProjectionDispatcher`, and `73c4b4f3` (3,044 evidence and tooling files). Neither touches a reminder or Effects source file, the C26-VG-O1 trace found no affected Works consumer, and the 3.117.1 proof is bound to public packages and `07d1e23a`. Carried C25-BH1: disclosure edits this spec's record. | carried reject |
| C26-BH6b | The Builds and Projects gitlink moves and guide commit `a5405602` are not recorded | low | Both pins are reachable from their `origin/main`, neither is 4.11 work, and `718726d` is pushed. Recording them edits this spec's record. | reject |
| C26-BH7a | The completion record says the guide sends the token "only" to its own sidecar; the guide has no "only" | low | True (`typed-reminders.md:490`); the guide matches the C25 fix line. The fix edits this spec's record. | reject |
| C26-BH7b | The guide does not say the submitter's token never reaches the gateway | false | `typed-reminders.md:490-493` traces the documented path to EventStore's sidecar presenting EventStore's `APP_API_TOKEN`, and names a direct gateway call as the way the submitter's token reaches it. | reject |
| C26-BH7c | The guide treats `DAPR_API_TOKEN` as always present | false | The documented call passes `builder.Configuration["DAPR_API_TOKEN"]` (line 253), and a `channel-*` `401` points to EventStore's `APP_API_TOKEN` whether or not the submitter has a token, so the diagnosis does not change. | reject |
| C26-BH8 | The symmetric-mode sentence does not say the submitter also needs `AllowInsecureSymmetricKey` | low | True: `JwtWorkloadAssertionIssuer.cs:121-124` validates the same contract and logs `contract-unusable` (event `5512`) without attaching an assertion. The resulting `assertion-missing` `401` is an "other `401` reason", which `typed-reminders.md:513-516` routes to the submitter's assertion handler. This affects only symmetric mode outside Development, and adding a submitter failure path exceeds a direct correction. | reject |
| C26-BH9 | The C25 guide edit breaks the paragraph's line wrap | low | `typed-reminders.md:492` is 145 characters and `:508` is 82, in a paragraph wrapped near 80. MD013 is disabled, so lint passes. A rewrap is a direct correction, as C25-BH9 made in `REPLAY.md`. | patch |
| C26-EC1 | A staged rewound gitlink survives the restore | low | Same as C26-BH1a. | reject |
| C26-EC2a | The restore loses the submodule's branch context | low | Same as C26-BH1b. | reject |
| C26-EC2b | The rewind or restore aborts on local submodule changes | false | Git refuses a checkout that would overwrite local changes, which is a loud, correct failure. | reject |
| C26-EC3 | The restore step leaves other rewound root submodules rewound | low | `REPLAY.md:4-5` requires "the same root-declared source dependencies", so a faithful replay also rewinds `Hexalith.Builds` (now `9c46679b`) to its recorded pin. `REPLAY.md:16-18` restores only EventStore, so a later `commit -a` re-pins Builds: the harm C25-EC3 patched for EventStore. Naming every rewound root submodule is a direct correction. | patch |
| C26-EC4 | Stale `07d1e23a` test binaries can run after the restore | low | Same as C26-BH2c. | reject |
| C26-EC5 | "No runtime code or package API changed" is false for the gitlink move | low | Same as C26-BH6a. | carried reject |
| C26-EC6 | `718726d` moves the Builds and Projects gitlinks without disclosure | low | Same as C26-BH6b. | reject |
| C26-AA1a | The gitlink move pulls in Story 6.6 runtime code and a 3,044-file commit | low | Same as C26-BH6a. | carried reject |
| C26-AA1b | The Builds and Projects moves and `a5405602` are not cited | low | Same as C26-BH6b. | reject |
| C26-AA2 | The C25 completion records no verification commands | low | True. This review re-ran the manifest check (39/39 match), and the auditor re-ran markdownlint, `git diff --check`, and `test_replay_proof.py`, all passing. The fix edits this spec's record. | reject |
| C26-AA3 | The record's "only" is not in the guide | low | Same as C26-BH7a. | reject |
| C26-AA4 | `REPLAY.md` cites the C12 record for a method it does not record | false | The C12 record says the parent's replay matched the test assembly and retained source-built dependency bytes. "Outside it" follows from the retained executed recipe, which the same paragraph names by hash and which hashes neither. | reject |
| C26-AA5 | The guide patch adds a stray 145-character line | low | Same defect as C26-BH9. | patch (with C26-BH9) |
| C26-AA6 | The restore step ignores stale build outputs | low | Same as C26-BH2c. | reject |
| C26-AA7 | Spec frontmatter `done` conflicts with tracker `review` | low | Carried C23-BH11 and C25-BH11. The C12 record names `done` and `review` as the bmad-build handoff. | carried reject |
| C26-VG-O1 | The submodule bump is not docs-only, but the record says no runtime code changed | low | Same as C26-BH6a; the layer traced no Works regression (distinct `Handle` command names, Works' own processor and `/project` route). | carried reject |
| C26-VG-O2 | The Builds and Projects gitlink moves were not reviewed | false | Neither is 4.11 work; both were excluded by the scope approved at the C26 checkpoint. | reject |

### C27 review triage (2026-10-10)

The baseline diff and the current EventStore guide edit were reviewed by blind hunter (12 findings), edge-case hunter (8 findings), and verification gap (no gaps). Each finding was checked before grouping. No intent or implementation change is required; three previously unlogged agent-context omissions are deferred.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| C27-BH1 | The parent diff omits EventStore source and the guide edit | low | The parent gitlink alone does not expose historical EventStore source changes, but the guide edit is appended to this review diff and the reminder runtime already has the C11 source and public-package reviews. The review-input limitation does not identify a current artifact defect. | reject |
| C27-BH2 | The public source audit compares only part of the reminder runtime | low | Carried C13-AA2 and C24-BH1: `public-source-audit.json` names its compared paths, and C13 separately reviewed the endpoint and admission delta. | carried reject |
| C27-BH3 | Epic context says Claim is the only entry to InProgress | low | Carried C12-BH7: Resume also enters InProgress; the agent-context correction is already deferred. | carried defer |
| C27-BH4 | Epic context omits persist-before-publication | low | Carried C3-BH9: the architecture remains authoritative and this context refresh is already deferred. | carried defer |
| C27-BH5 | Epic context omits persisted-end-state runtime test requirements | low | Carried C12-BH9: the omission affects later context-guided stories and is already deferred. | carried defer |
| C27-BH6 | Epic context promises a claim loser the winning party's identity | low | `WorkItemTransitionRejected` contains status and attempted act, not the winner, and the executor query filters other parties' assigned items. The context can misdirect a later surface; changing agent context is deferred. | defer |
| C27-BH7 | Epic context leaves tenant context in problem details unqualified | low | The context says problem details carry tenant context, while the epic's AD-23 acceptance denies tenant-existence disclosure before authorization. A later surface needs an explicit authorized-only qualification; changing agent context is deferred. | defer |
| C27-BH8 | The spec's created date changed | low | The original draft used 2026-09-24 and the current spec uses 2026-09-29. Correcting historical spec metadata edits this build's spec, which this review route rejects. | reject |
| C27-BH9 | The Code Map calls the historical EventStore baseline a clean HEAD | low | Carried C8-BH11: `f378afdb` is the recorded historical baseline, while later sections name the reviewed and public revisions. The requested correction edits this spec. | carried reject |
| C27-BH10 | Replay accepts a dirty checkout at the recorded HEAD | low | Carried C23-BH4: the recipe checks the commit but not tracked modifications; a fresh checkout is documented and adding a clean-tree guard exceeds a direct correction to this archival replay. | carried reject |
| C27-BH11 | A timed-out command is absent from the replay ledger | low | `replay-proof.py` writes the command row after `subprocess.run` returns; on timeout the log and raised exception remain. This rare failure does not create a false success, and recording attempts requires exception handling. | reject |
| C27-BH12 | `last_updated` parses as a YAML date | false | The field is a human-readable tracker timestamp with no repository consumer; its current bare date does not change a checked behavior. The earlier baseline value was also not a full ISO timestamp. | reject |
| C27-EC1 | Epic context omits Resume as an entry to InProgress | low | Carried C12-BH7 and C27-BH3; the context correction is already deferred. | carried defer |
| C27-EC2 | Replay permits tracked checkout modifications | low | Carried C23-BH4 and C27-BH10. | carried reject |
| C27-EC3 | A trickling archive can outlast the socket timeout | low | Carried C24-BH4: the manual official-NuGet replay is length-bounded, and an overall deadline adds machinery for a rare failure. | carried reject |
| C27-EC4 | A timed-out command can leave a child process running | low | `subprocess.run` times out its direct process, which may leave a descendant. This manual replay is rare and a process-group manager is more than a direct correction; the failed run does not claim success. | reject |
| C27-EC5 | The historical packaging helper accepts a nonempty output directory | low | Carried C12-EC2: the retained proof used a fresh versioned directory, and guarding reuse adds branching to an archival helper. | carried reject |
| C27-EC6 | Epic context omits persist-before-publication | low | Carried C3-BH9 and C27-BH4. | carried defer |
| C27-EC7 | Epic context omits persisted-end-state runtime tests | low | Carried C12-BH9 and C27-BH5. | carried defer |
| C27-EC8 | Epic context omits the terminal-work mutation rule | low | The context no longer states the rule, though the epic requires terminal invalidity to reject and the architecture specifies the defined no-ops. A later implementation can miss the rule; changing agent context is deferred. | defer |

## Design Notes

**Callback admission, in order:**
1. The app-channel token matches `APP_API_TOKEN`. It is required outside Development, and the callback fails closed without it.
2. The stored full tuple re-derives both the actor ID and the reminder-name token.
3. The name prefix matches the stored kind and its configured purpose.
4. The intent source still reports the intent as current.

A mismatched tuple is quarantined. A stale witness gets an audited no-op followed by cancel.

**Submission:**
- `EffectIdentity` is (tenant, source, source sequence, kind, target, ordinal `0`), with `MessageId` = `IdempotencyKey` = `wrk-<EffectId>`.
- A durable result deletes pending state only after Scheduler cancellation succeeds; the index entry goes last.
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

### Review Findings

Chunk 1 (reminder implementation source), 2026-09-30. Reviewers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor.

- [x] [Review][Patch] Stale callback cancels the witness before replacement convergence is durable [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1102`]
- [x] [Review][Patch] A null intent-source result is treated as an empty stream and cancels stored reminders [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:138`]
- [x] [Review][Patch] A terminal receipt removes the witness without folding the stream again [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1343`]
- [x] [Review][Patch] A null persisted candidate list throws when a candidate is removed [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderIntentIndex.cs:78`]
- [x] [Review][Patch] A blank target domain is stored, and a later callback retires its witness as stale [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1013`]
- [x] [Review][Patch] Blank workload denial has no regression test [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/ReminderCoordinatorTests.cs:449`]
- [x] [Review][Patch] A blank or null translation is not covered by a quarantine test [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/ReminderCallbackAdmissionTests.cs:344`]
- [x] [Review][Patch] A repeated app-channel token is not denied by the admission tests [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCallbackTokenFilter.cs:94`]
- [x] [Review][Patch] An uncertain submission drops the exception type [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1330`]
- [x] [Review][Defer] Quarantine keeps a reminder name from being armed again [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:592`] — deferred: operator disposition of quarantine is explicitly outside 4.11 and belongs to Story 4.16
- [x] [Review][Defer] Item quarantine evidence has no cap [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:717`] — deferred: retention, TTL, and offboarding of reminder evidence wait for the Story 4.16 AD-28 gate
- [x] [Review][Defer] A mismatched candidate actor id leaves every later pass incomplete [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:87`] — deferred: dropping the corrupt row can hide the only copy of its target coordinates; operator repair belongs to Story 4.16

#### Rejected

- `false` — `SubmitAsync` is not required to match `CommandType` to `PayloadType` or re-hash the command payload. The source contract makes the current stream and `TranslateDueIntent` authoritative.
- `low` — A second `AddEventStoreReminders` call ignoring a different intent source is an uncommon host-programming error, and the method documents that second call as configuration-only.
- `low` — A blank resolved workload denies submission and retains the witness. That is the fail-closed path, and collapsing `Validate()` errors into one startup message is a setup-only annoyance.
- `low` — Different token lengths return `token-invalid` before `FixedTimeEquals`. Measuring that length requires timing an internal app-channel route, and a padding scheme is more than a direct correction.
- `false` — Capturing `ReminderCoordinator` for the actor activation matches `IReminderIntentSource`, which already requires the source to re-read the stream on every call for that lifetime.
- `false` — With reconciliation disabled, `ExecuteAsync` returns without `CompletePass`. Readiness stays degraded until a pass completes, which is the rule that recovery has not run.
- `false` — After durable item state exists, `ReminderFailClosedException` reloads that state, re-ensures its candidate, and returns a non-zero unresolved count. The next pass reconverges from the stream.
- `false` — An actor-collision digest is stored on the owning item's quarantine list, and `HasWork` treats quarantine as retained work, so erasing the owner does not drop that digest.
- `low` — `RemoveCandidateAsync` leaves the tenant registered. Empty-tenant pruning is already deferred, and a scan of an empty tenant is negligible at this story's scale.
- `low` — The readiness registration's failure status is `Degraded` only when the check throws. The missing-token path returns `Unhealthy` itself.
- `low` — A document that fails JSON deserialization makes `GetAsync` throw, and the reconciler already marks that pass incomplete. Quarantining the raw bytes needs a store seam this coordinator does not have, and everyday writes are produced by `PersistAsync`.
- `false` — The callback does not re-check `DueUtc`. The approved design gives timing to the Scheduler.
- `low` — `UpdatedAt + Backoff` can throw only when `UpdatedAt` is within one max delay of `DateTimeOffset.MaxValue`. Backoff is already capped at the validated max delay.

### Review Findings

Chunk A (reminder source, `src/`, 43 files), review iteration 3, 2026-10-01. Diff: EventStore `f378afdb..19dc1f82`, reminder paths only. Reviewers: blind-hunter (BH), edge-case-hunter (EC), verification-gap (VG), acceptance-auditor (AA). Of 51 findings: 2 decision-needed (resolved 2026-10-01: one became a patch, one a deferral), 10 patch (left as action items), 6 defer, 28 rejected. Chunks B (`tests/`) and C (`docs/`, `scripts/`) were not reviewed in this iteration.

- [x] [Review][Patch] A first registration that fails closed on a full tenant index never degrades readiness [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:64`] — Resolved decision (2026-10-01): the reconciler reports a tenant whose candidate document holds `MaxCandidatesPerTenant` candidates as Degraded, with a regression test. With no durable state, `ConvergeAsync` rethrows without recording status (`ReminderCoordinator.cs:155-168`), and an unindexed item is invisible to the reconciler, so `index-capacity` otherwise leaves `/ready` Healthy (AC2). (AA2)
- [x] [Review][Patch] The guide's reason-code lists and the callback's step 2 disagree with the code [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:284`] — `arm-failed` is only ever a `Pending` reason, never `Retrying` or quarantine. `domain-invalid` only quarantines; a failed audit on that path retries as `audit-unavailable`. `tuple-mismatch` is unreachable, because `LoadAsync`/`TryValidatePersistedEntry` re-derive the stored tuple first and quarantine a mismatch as `stored-entry-invalid` before `ReminderCoordinator.cs:1038` runs. Correct the lists, and comment the step-2 branch as defense in depth. (BH14, AA1, AA6, VG-other)
- [x] [Review][Patch] The guide's registration steps 5 and 6 put cancellation after the state write [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:101`] — Obsolete and newly quarantined reminders are audited and cancelled before `PersistAsync` (`ReminderCoordinator.cs:700-782`); only arming and submission follow the write. This matches the iteration-1 decision to retain witnesses until cancellation succeeds. (AA7)
- [x] [Review][Patch] The coordinator summary claims the stream is re-folded before anything is cancelled [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:19`] — Orphan, already-quarantined, repaired-quarantine, and `domain-invalid` callbacks cancel from persisted state without a fold. (EC14)
- [x] [Review][Patch] `IReminderIntentSource` misstates the effect-identity tuple [`references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Reminders/IReminderIntentSource.cs:17`] — Effect identity also includes the source domain and source aggregate, so only intents that share `(source domain, source aggregate, source sequence, kind, target)` collide. (EC15)
- [x] [Review][Patch] No test pairs an actor ID with a target that does not derive it [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:132`] — Add `ConvergeRejectsTargetThatDoesNotDeriveActor`. It should assert an `ArgumentException`, no state under either key, no candidate, and no arm. (VG1)
- [x] [Review][Patch] No test covers an unreadable tenant registry [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:49`] — Assert an incomplete pass, that recorded items are not pruned, and that readiness is Degraded. (VG2)
- [x] [Review][Patch] No test proves retry backoff stops at `RetryMaxDelay` [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:533`] — Drive six or more uncertain attempts and assert that the re-armed due time equals `RetryMaxDelay`. (VG3)
- [x] [Review][Patch] Several option validation rules have no test [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreReminderOptions.cs:75`] — Add rows for a zero `ReconciliationInterval`, a zero `RetryInitialDelay`, a blank `StateStoreName`, `IndexWriteAttempts` of 0 and 101, and a blank purpose value. (VG4)
- [x] [Review][Patch] No test proves a complete hosted pass waits `ReconciliationInterval` [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:138`] — With a short retry delay and a long interval, assert there is no second pass within a window well beyond the retry delay. (VG5)
- [x] [Review][Defer] The Dapr actor `/healthz` route runs every registered health check [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreReminderEndpointExtensions.cs:30`] — deferred (decision 2026-10-01): the gateway and Operations hosts share the same Dapr SDK `/healthz` behavior; fix every actor host once in the R2 service defaults and prove it in 4.16. `MapActorHealthChecks` (Dapr.Actors.AspNetCore 1.18.10) maps `/healthz` with no predicate, so an Unhealthy `ready` check answers 503 and Dapr disconnects a host that reports actor types from placement (dapr/dapr#7355). (EC13)
- [x] [Review][Defer] Disposition records are last-write-wins, never expire, and outlive erasure [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1603`] — deferred: carried from iteration 0 (BH10) and chunk 1. Retention, TTL, audit history, and offboarding wait for the AD-28 gate and the Platform audit sink in Story 4.16. (BH2, BH15)
- [x] [Review][Defer] Every convergence re-audits and Error-logs every stored quarantine record [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:750`] — deferred: carried R2-BH1. This loop is also the retry path for failed actor-collision and repair audits, so stopping it needs a persisted audited flag, which the AD-28 gate owns. (BH3, EC10)
- [x] [Review][Defer] A permanently bad index candidate keeps every pass incomplete [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:89`] — deferred: carried from chunk 1, R2-EC6, R2-BH3, and R2-BH4. Four kinds of candidate keep readiness Degraded and the loop on retry cadence: null rows, actor-ID mismatches, stored headers that were re-indexed without re-derivation (on the fail-closed and actor-collision paths), and folds that always throw, for example after erasure. Operator repair belongs to Story 4.16. (BH4, EC4, EC3)
- [x] [Review][Defer] No timeout bounds the calls inside a reminder actor turn [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/DaprReminderActorInvoker.cs:27`] — deferred: maybe-false, unverified medium, carried R2-BH5. To settle it, confirm whether the Dapr actor proxy and the store and submitter clients enforce a finite HTTP timeout when a dependency hangs. (BH9)
- [x] [Review][Defer] AC4 is not yet proven against a published package [`references/Hexalith.EventStore/tests/Hexalith.EventStore.Contracts.Tests/Packaging/PackagedReminderApiTests.cs:22`] — deferred: this is the close gate set by the frozen release decision. Public 3.110.0 (source `27279fe6`) does not contain the API. Rerun the test after the owner publishes. (AA5)

#### Rejected

- `low` — BH1, EC1: The callback does not check `DueUtc`. Rejected three times before, and the Scheduler owns timing. An early firing needs Scheduler clock skew or a caller that holds the app-channel token. Revisit only if Story 4.16 confirms the deferred Scheduler-callback origin bypass, because clock-free handlers would accept an early witnessed command.
- `low` — BH5: Every replica scans the whole index. Correctness holds through actor turns. Partitioning and jitter are beyond the synthetic scale that the AD-28 gate allows.
- `low` — BH6: There is one hot candidate document per tenant, and CAS retries run without backoff. Rejected before (BH13). Contention ends in a fail-closed retry, not a loss. The duplicate CAS loop has no named divergent caller.
- `low` — BH7: `EnsureCandidateAsync` relabels a tenant-mismatched document. Only corrupt state reaches this. Each foreign candidate then fails actor-ID re-derivation and is counted as incomplete, so no tenant is crossed and readiness stays Degraded.
- `low` — BH8, EC9: A failed `RemoveCandidateAsync` after release surfaces as a failure. It needs CAS exhaustion and heals itself: the caller's retry or the next pass removes the candidate, and nothing is submitted twice.
- `low` — BH10: Options validation drops error details and accepts a missing workload or purposes. Rejected before. Both cases fail closed, keep the work, and report Degraded.
- `low` — BH11, EC11: A second intent source is ignored. Rejected three times before; the method documents a second call as configuration-only.
- `rejected` — BH12: The kind map holds only the two v1 Works kinds. The frozen spec mandates the closed v1 `EffectKindCatalog`, so changing it means editing the spec.
- `low` — BH13: There are no reminder metrics or traces. Bounded metrics and alerts belong to the AD-20 R8 row (Platform plus SDK), outside 4.11.
- `low` — BH16, EC16: Unresolved counts differ between the fail-closed and normal paths. Only the count changes. A lost race stays Degraded by design, and readiness depends only on non-zero totals.
- `low` — BH17: Reminder-route detection hard-codes the Dapr template. Dapr is pinned at 1.18.10, and a mismatch fails loudly as ambiguous routes.
- `low` — BH18: The token filter allocates `Split` arrays on every request. This is a negligible cost on the app channel.
- `low` — BH19: `receipt-mismatch` and a permanently denied delegation retry without escalating. The Design Notes and AD-27 require keeping the work and retrying periodically until an audited disposition.
- `low` — BH20: The `byte[]` payloads in public records compare by reference. This matches `TrustedEffectSubmission`, and no internal code compares intents with record equality.
- `low` — AA3: An actor collision whose save fails is acknowledged without the colliding target's digest. This needs a split-brain write race inside an actor turn on top of a collision. The colliding target is never indexed, even on success.
- `low` — AA4: The audit results for denied and retrying outcomes are ignored. The witness is kept and re-armed, never released, and the next pass of the stored target re-audits the collision digest.
- `low` — AA8: The AD-28 production gate exists only in the docs. The rule is procedural, and a runtime production profile belongs to AD-24 and Story 4.16.
- `low` — EC2: Undeserializable item state throws on every convergence. Rejected in chunk 1; the pass is marked incomplete, and quarantining the raw bytes needs a store seam.
- `low` — EC5: Two blank-domain entries share an empty effect ID. Carried rejection (R2-BH2, R2-EC1); they are quarantined, not executed.
- `low` — EC6: A future `UpdatedAt` after a clock jump stretches the backoff window. It needs a backward clock jump, and the work is kept, not lost.
- `low` — EC7: A due instant beyond the Scheduler's duration range fails to arm. It needs an intent roughly three centuries ahead; the witness stays `Pending` and readiness stays Degraded.
- `low` — EC8: The tenant registry has no capacity bound. Registry pruning is already deferred, and the document holds only tenant slugs.
- `low` — EC12: A different actor class already registered under `ActorTypeName` is left in place. Carried rejection (R2-EC5); a proxy call to it fails loudly.
- `low` — EC17: A non-`wra-` route actor ID is logged verbatim. Only a caller that holds the app-channel token can choose that ID, and the Scheduler only fires codec names.

### Review Findings

Chunks B and C plus the uncommitted `src` fixes, review iteration 3, 2026-10-01. Diff: EventStore `f378afdb..` working tree (HEAD `19dc1f82` plus the uncommitted iteration-3 and C3 fixes), 27 files. That is `tests/` reminder paths (20 files), `docs/guides/typed-reminders.md`, `docs/guides/configuration-reference.md`, `scripts/validate-consumer-package-references.py`, and the four uncommitted `src` files. `ContractsPackageDependencyTests.cs` (Story 6.1) is excluded. Reviewers: blind-hunter (BH), edge-case-hunter (EC), verification-gap (VG), acceptance-auditor (AA); no layer failed. The four layers filed 42 findings (BH 19, AA 10, EC 9, VG 4); several multi-part findings split between a patch and a rejection. Triage produced 0 decision-needed, 13 patch entries (2 medium, 11 low), 0 defer, and 19 rejected entries. Both medium patches come from the uncommitted C3/iteration-3 `src` fixes.

- [x] [Review][Patch] Registration overwrites a present null-list discovery document, undoing C3-BH1/C3-R1 [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderIntentIndex.cs:34`] — medium. Scanning now fails closed on `ReminderTenantRegistry(null)` and `ReminderTenantCandidates(_, null)`, but `EnsureCandidateAsync` still coalesces both lists to `[]` (`:34`, `:46`) and saves a one-entry document. It runs on every convergence of an item that holds work (`ReminderCoordinator.cs:176`, `:694`, `:964`, `:1460`). One registration in any tenant, or in the same tenant, therefore erases the malformed evidence. The next pass is complete, `CompletePass` prunes the recorded unresolved items, and readiness turns Healthy while `Pending` items whose arming failed can no longer be discovered. Fix: throw `index-registry-invalid` / `index-candidates-invalid` from the two write lambdas when a present document has a null list, matching the read side and the `index-capacity` fail-closed precedent. Extend both rows of `NullDiscoveryCollectionRetainsRecordedItemsAndDegradesReadiness` with a convergence of another target (another tenant for the registry row, another item for the candidates row) before the pass. Assert that the document is unchanged, the pass is incomplete, and readiness is Degraded. (BH, EC, VG, AA)
- [x] [Review][Patch] A full tenant index keeps the hosted reconciler on the retry cadence indefinitely [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:74`] — medium. The capacity check adds to `incomplete`, and `ExecuteAsync` (`:146-148`) then waits `RetryInitialDelay` instead of `ReconciliationInterval`. A tenant that legitimately holds exactly `MaxCandidatesPerTenant` candidates, with no refused registration, makes every replica re-fold every candidate of every tenant every 30 seconds by default, and logs a 200212 warning each time, for as long as the tenant stays full. The 2026-10-01 owner decision asked only for Degraded readiness. Fix: count capacity-limited tenants separately in the internal `ReminderReconciliationPass`. Keep them in the readiness incomplete count, but derive the hosted delay from incompleteness other than capacity. Add a hosted-cadence regression with a full index that uses the controlled-timer pattern from `CompleteHostedPassWaitsForReconciliationInterval`. (BH, EC, VG, AA)
- [x] [Review][Patch] Reconciler scan logs drop fail-closed reason codes [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:69`] — low. `ScanFailed` and `CandidateFailed` log `exception.GetType().Name`, so `index-registry-invalid`, `index-candidates-invalid`, `index-tenant-mismatch`, `index-conflict` and `state-conflict` all appear as `ReminderFailClosedException` in the 200211/200212 logs the runbook points to (`:53`, `:69`, `:114`). The capacity call already passes a reason code in that slot. Fix: log `ReasonCode` when the exception is a `ReminderFailClosedException`. (BH, AA)
- [x] [Review][Patch] The guide omits fail-closed, scan, and audit reason codes [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:290`] — low. The runbook lists `Pending`/`Retrying`/quarantine codes only. It never names the 200214 fail-closed codes (`state-conflict`, `index-capacity`, `index-conflict`), the scan codes (`index-registry-invalid`, `index-candidates-invalid`, `index-tenant-mismatch`, `candidate-missing`, `actor-id-mismatch`), or the `witness-not-current` reason on `Stale` dispositions. Add them, with the action for each. (BH, AA)
- [x] [Review][Patch] The configuration reference disagrees with the guide on a full index [`references/Hexalith.EventStore/docs/guides/configuration-reference.md:377`] — low. The `MaxCandidatesPerTenant` row still says only "A full tenant index fails registration closed", while `typed-reminders.md:238` adds "and degrades readiness". Align the row with the guide and with the cadence fix above. (AA)
- [x] [Review][Patch] Option validation rules are undocumented [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:230`] — low. `InvalidOptionsFailValidation` proves these rules, but neither options table states them: `IndexWriteAttempts` 1–100, `MaxCandidatesPerTenant` ≥ 1, `ReconciliationInterval` and `RetryInitialDelay` > 0, `RetryInitialDelay` ≤ `RetryMaxDelay`, a non-blank `StateStoreName`, the `ActorTypeName` character set, and no purpose for a non-reminder kind. Operators otherwise learn them from an `OptionsValidationException`. (BH)
- [x] [Review][Patch] The orphan-callback sentence ignores load-time repair persistence [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:149`] — low. "A callback without a persisted witness mutates and discloses nothing", yet `HandleCallbackCoreAsync` first calls `LoadAsync` (`ReminderCoordinator.cs:983`). When stored entries fail validation, `LoadAsync` persists their quarantine normalization (`:1559-1561`) before the orphan branch runs. Qualify the sentence: the callback itself writes nothing, but loading may persist the deterministic quarantine of corrupt stored entries. (EC)
- [x] [Review][Patch] No test pins a complete pass over absent discovery documents [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderIntentIndex.cs:103`] — low. Pre-verified: the new throws fire only for a present document with a null list. No test runs a pass on an empty store and asserts `Incomplete = 0` (`FirstPassRunsAtStartup` asserts only `PassCompleted`, which an incomplete pass also sets). Shortening either guard to `entry.Value?.Tenants is null` would leave every fresh host Degraded on the retry cadence with the suite green. Add `EmptyDiscoveryIndexCompletesPass`, asserting `new ReminderReconciliationPass(0, 0, 0, 0, 0, 0, 0, 0)` and Healthy readiness. (VG)
- [x] [Review][Patch] Composition tests read and mutate the process-wide `DAPR_APP_ID` [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/EventStoreReminderCompositionTests.cs:85`] — low. `BlankDaprApplicationIdUsesHostApplicationName` sets `DAPR_APP_ID` to `" \t"` with no `DisableParallelization` collection, while `AddEventStoreDomainService` reads the same variable with `??` (`EventStoreDomainServiceExtensions.cs:458`), so a concurrently composed host could fail `DomainProjectionIdentityOptions` validation. `AddEventStoreRemindersRegistersRuntime` (`:45`) expects `GetEnvironmentVariable("DAPR_APP_ID") ?? "widget-host"`, which disagrees with `ResolveWorkload`'s blank fallback on an agent where the variable is blank. Fix: put the class in a non-parallel collection and use the blank-aware expectation. (BH, EC, VG, AA)
- [x] [Review][Patch] `BlankWorkloadIsDeniedAndRetained` under-asserts its own summary [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/ReminderCoordinatorTests.cs:494`] — low. The summary says the workload is denied "before delegation", but the test never asserts `harness.Tokens.Requests` is empty. Unlike `UnconfiguredPurposeIsDeniedAndRetained`, it also never asserts the `Denied` disposition record that AC3's audit requires. Add both assertions. (BH, AA)
- [x] [Review][Patch] Canonical-text refusals of the reminder codec are untested [`references/Hexalith.EventStore/tests/Hexalith.EventStore.Contracts.Tests/Reminders/ReminderIdentityCodecTests.cs:111`] — low. `WriteText` refuses non-NFC text (`ReminderIdentityCodec.cs:272`), and names must use the Crockford alphabet. No test passes decomposed text, and `MalformedReminderNamesDoNotParse` rejects lowercase but not the excluded letters `I`/`L`/`O`/`U`. Removing either check keeps the suite green. Add one decomposed-text rejection and one excluded-letter row. (BH)
- [x] [Review][Patch] `FakeReminderIntentSource.ReturnNull` is documented as one-shot but stays on [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/Fixtures/FakeReminderIntentSource.cs:17`] — low. "Whether the next fold returns null" describes a single fold, but the flag returns null on every fold until reset. Correct the comment. (BH)
- [x] [Review][Patch] `DisabledReconciliationNeverCompletesAPass` relies on a 200 ms sleep [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/ReminderReconcilerTests.cs:485`] — low. On a slow start, a regression that ignores the flag could still pass. Replace the sleep with an awaited `reconciler.ExecuteTask` (completed when disabled), then assert no pass and no stream read. (BH)

#### Rejected

- `false` — BH, AA: `TwoHostsRacingRegisterOnce` serializes and the Restore/HA row is untested. The serialization is deliberate: Dapr actor turns serialize each actor across hosts in production, and the in-process invoker models that (Design Notes: "Actor turns plus the index CAS give one registration"). `ConflictingWriterFailsClosed` covers the compare-and-swap split brain, and `CrashBetweenReceiptAndReleaseReplaysOnAnotherHost` covers the receipt half. `RestoredStateConvergesToStream` persists a witness older than the stream, which is what a restore produces, and `MissingIndexEntryIsRestored` covers index restore. R1-BH12 assigned this row to these tests.
- `false` — BH: A null or blank issuer token is never exercised. With no provider, the same `IsNullOrWhiteSpace(delegation)` guard (`ReminderCoordinator.cs:1392`) yields `delegation-unavailable`, and `MissingSubmissionSeamsFailClosed` covers that path.
- `false` — BH: The fake delegation provider throws synchronously instead of faulting. The call is awaited inside the coordinator's `try`, so a synchronous throw and a faulted task reach the same catch (`ReminderCoordinator.cs:1378-1389`).
- `low` — BH: Cancellation paths are untested. They are shutdown-only, and cancellation returns before any persist, so the witness is retained by construction.
- `low` — BH, AA: Log redaction, event IDs, and the 200208/200209 audit logs are not asserted. `LoggerMessage` templates fix the fields at compile time, and asserting them needs a log-capture fixture this project lacks. The EC13 log-audit decision stands.
- `low` — BH: `StaleCallbackKeepsReminderWhenReplacementIndexIsFull` stops before readiness. The new capacity check makes the next pass Degraded in that state, and the periodic witness fires again.
- `low` — BH: Live-test cleanup is not in a `finally`, and step 2 checks only `Version`. The actor type is per run, so a leftover job affects no later test. Unit tests pin no-mutation on token denial.
- `low` — BH, AA: The package proof is not automated or tied to a version, the probes are shallow, and `python3` is hard-coded. This is carried: the named-public-version run is the deferred owner close gate (AA5), iteration-0 BH15 rejected a `WebApplication` probe, and `python3` matches the sibling trusted-effect package tests.
- `low` — BH: A golden-vector derivation script is missing. The vectors are frozen AD-26 data, and a script adds tooling outside this story.
- `low` — BH: `PersistBeforeFailure` ignores `Disposition`. Modelling an uncertain result followed by a `Rejection` needs a fake extension, and `Rejection`/`NoOp` receipts are already covered (iteration-0 VG2).
- `low` — BH: There is no case for an explicit `Workload` overriding `DAPR_APP_ID`. The `ResolveWorkload` helper is tested directly (R1-VG1).
- `low` — BH: The `MapActorsHandlers` ordering trap has no guard, and test `WebApplication` instances are not disposed. This is carried: iteration-0 EC1 chose docs because the failure is loud, and Story 4.15 owns the Works host change.
- `low` — BH: No test pins the missing due check. Carried rejection; the Scheduler owns timing.
- `low` — BH: `LiveActorTrustedEffectSubmitter` calls `CompleteAsync` outside a `finally` and bypasses the gateway. An actor exception fails the test anyway, and the fixture documents that it stands in for the gateway.
- `low` — BH: A persistent null or actor-mismatched candidate also keeps the retry cadence. Carried: this is deferred in iteration-3 chunk A (a permanently bad index candidate) for Story 4.16 operator repair.
- `low` — BH: `effect-identity-invalid` and `tuple-mismatch` are untested. Both are defense in depth behind load-time and intent validation, and reaching them needs fault injection.
- `low` — EC: A null element in a candidate list makes `EnsureCandidateAsync` throw `NullReferenceException`. This needs a corrupt element. The failure is loud, the reconciler already marks that row `candidate-missing`, and a guard would extend the carried null-candidate repair deferral.
- `low` — EC, EC: The live `ConvergeAsync` retry could observe zero counts, and a missing `python3` throws `Win32Exception`. The first needs an exception after a successful actor turn inside the placement window. The second runs only when the inventory variable is set and matches the sibling package tests.
- `low` — AA: Other first-registration failures leave readiness Healthy, and the live test neither reads the target stream nor checks cancellation after submission. Transient conflicts route to the documented caller retry, and the owner decision covered only the persistent capacity state. The 4.13 `Replayed` receipt is the one-submission proof, and unit tests pin cancellation after receipts.

## Implementation continuation and verification (2026-09-30)

The EventStore implementation and the previously listed review patches were already present in the clean checkout at `d0f8b241a8be34f7d8b9f60f89db130709d944c5`. This continuation adds one correction within the existing unresolved-readiness rule:

- A null or failing final stream fold after a submitted receipt or stale disposition retained its discovery candidate, but callers overwrote the helper's unresolved readiness with zero. The helper now returns its unresolved count, and convergence, submitted callbacks, and stale callbacks preserve that count. Convergence also returns it to the registrar, preventing a second overwrite there. A later successful pass clears the unresolved count and removes the candidate when the stream is empty. The approved nonempty-fold behavior remains unchanged.
- Six regression cases cover null and throwing cleanup folds through those three paths, assert durable candidate/receipt/Scheduler state and Degraded readiness, and prove recovery to Healthy. All six failed before the correction and passed afterward.
- The typed-reminder guide and coordinator remarks now document audit, successful Scheduler cancellation, witness release, and the final stream fold in the implemented order. The guide also documents retaining stale witnesses until replacement convergence is durable.

Works code, `docs/ci.md`, the frozen intent, both baseline commits, and the AD-26 codec and golden vectors remain unchanged. No commit, push, publication, dependency update, or nested-submodule initialization was performed.

### Commands and results

Commands below ran from `references/Hexalith.EventStore`. Logs and the synthetic local packages are under `/tmp/eventstore-reminders-4-11-20260930/`.

All four required projects built with `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0`, with zero warnings and zero errors:

| Project | Build log |
| --- | --- |
| `Hexalith.EventStore.Contracts.Tests` | `build-Hexalith.EventStore.Contracts.Tests.log` |
| `Hexalith.EventStore.Client.Tests` | `build-Hexalith.EventStore.Client.Tests.log` |
| `Hexalith.EventStore.DomainService.Tests` | `build-Hexalith.EventStore.DomainService.Tests.log` |
| `Hexalith.EventStore.Server.LiveSidecar.Tests` | `build-Hexalith.EventStore.Server.LiveSidecar.Tests.log` |

| Exact test command | Result | Log |
| --- | --- | --- |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -method '*UnavailableCleanupFold*'` before the fix | Six failures reproduced the overwritten unresolved count | `cleanup-red.log` |
| `tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -class '*Reminder*'` | 26 passed; one package-only case skipped until the pack was supplied | `contracts-focused.log` |
| `tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests -class '*Reminder*'` | Zero discovered; these interfaces are exercised by the runtime tests and package probe | `client-focused.log` |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -class '*Reminder*'` | 116/116 passed, including all six regression cases | `domainservice-focused.log` |
| `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.LiveSidecar.Tests -class '*Reminder*'` | 1/1 passed against Redis, placement, and Scheduler, including host restart, receipt replay, and Scheduler re-arm | `live-focused.log` |
| `tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests` | 838/838 passed | `client-full.log` |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests` | 276 passed; one existing architecture failure | `domainservice-full.log` |
| `tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests` | 2115 passed, 51 failed, two skipped; all failures are in `Packaging/*`, matching the prior environment/evidence failures | `contracts-full.log` |

The exact DomainService blocker remains `TenantsDomainService_DoesNotReferenceGeneratedApiHostOrDeclarePerMessageControllers`: its required subject, `references/Hexalith.EventStore/references/Hexalith.Tenants/src/Hexalith.Tenants/Hexalith.Tenants.csproj`, is absent because that nested submodule is intentionally uninitialized. The full Contracts failures remain repository-wide package governance/evidence checks involving absent nested repositories and pinned Builds commits, release evidence, and OQ8 `global.json` identity drift. The focused reminder checks have no failures.

The repository-required Aspire baseline attempt, `aspire start --isolated --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json`, exited 2 after its 120-second startup timeout while restoring. Log: `aspire-baseline.log`. `aspire describe` and subsequent `aspire stop` reported no running AppHost. The live-sidecar proof above ran successfully through its independent fixture.

### Local package proof and public release gate

The canonical `python3 tools/pack-release-packages.py /tmp/eventstore-reminders-4-11-20260930/packages 3.110.0-local.431` stalled in restore and was stopped after a bounded attempt (exit 143; `pack.log`). The temporary `pack-with-environment-pins.py` replays the same validated release manifest and canonical pack arguments, adding only `-m:1 -p:NuGetAudit=false` and a 180-second per-project bound. No repository packaging script or build gate was changed.

- `python3 /tmp/eventstore-reminders-4-11-20260930/pack-with-environment-pins.py /tmp/eventstore-reminders-4-11-20260930/packages 3.110.0-local.431` passed and produced all 14 packages (`pack-pinned.log`). The pack contains the final local code based on `d0f8b241a8be34f7d8b9f60f89db130709d944c5` plus the uncommitted readiness correction above.
- `python3 tools/validate-release-packages.py /tmp/eventstore-reminders-4-11-20260930/packages 3.110.0-local.431` passed: 14 valid packages (`package-validation.log`).
- `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/eventstore-reminders-4-11-20260930/packages tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -method '*PackagedReminderApi*'` passed 1/1 with no skipped case, validating all three isolated package-only consumers (`package-consumer.log`).

The coordinating agent's read-only public-registry check on 2026-09-30 found that the NuGet version indexes for Contracts, Client, and DomainService all end at `3.110.0`. The downloaded packages under `/tmp/eventstore-4-11-public-inspection-sv8c7q91/` all identify source commit `27279fe6431925a6ea046c3f89af61487185c7de`, which predates reminder feature commit `00c6c36522602097b28cd533084927453212c779`. Contracts and Client XML contain no `ReminderIdentityCodec` or `IReminderIntentSource`. That public version cannot satisfy the R6 package acceptance criterion.

**Still required before `done`:** the owner publishes a named public EventStore release containing this API after review, then the package-only test is repeated against that release and its version, source SHA, and result are recorded. This workflow published no release; the story remains `in-progress`.

## Current-checkout verification (2026-09-30)

The resumed implementation inspected the full spec and both frontmatter context files. The EventStore checkout is clean at `6dededdecd62dd6dc6d1f15810108d860ec70c8f`, which includes the readiness correction and six regression cases recorded above. All implementation tasks are present; this continuation required no source changes. The only repository edit is this verification record. The story remains `in-progress` because its named public-package acceptance gate is outstanding.

Fresh commands ran from `references/Hexalith.EventStore`; build logs, test logs, and xUnit XML results are under `/tmp/story-4-11-current-verification/`:

| Command | Result |
| --- | --- |
| `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for Contracts.Tests, Client.Tests, DomainService.Tests, and Server.LiveSidecar.Tests | All four passed with zero warnings and errors |
| `tests/<Project>/bin/Debug/net10.0/<Project> -class '*Reminder*' -xml /tmp/story-4-11-current-verification/<Project>-reminders.xml` for those four projects | Contracts: 26 passed and one package-only skip before supplying the inventory; Client: zero discovered as documented; DomainService: 116 passed; LiveSidecar: one passed against Redis, placement, and Scheduler |
| `python3 tools/validate-release-packages.py /tmp/eventstore-reminders-4-11-20260930/packages 3.110.0-local.431` | All 14 existing synthetic local packages validated; this continuation did not repack |
| `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/eventstore-reminders-4-11-20260930/packages tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -method '*PackagedReminderApi*' -xml /tmp/story-4-11-current-verification/packaged-reminder-api.xml` | One passed, none skipped; all three isolated package-only consumers validated |

Both the implementation and coordinating agents checked the fresh XML against the frozen matrix. All five rows have present, passing covering tests: register/reschedule, callback replay, stale/forged admission, recovery, and restore/HA. The six `UnavailableCleanupFoldRetainsUnresolvedReadiness` cases also passed. The live proof covers durable registration, direct callback, host restart with Redis retained, replay of the same effect receipt, and rearming a deleted Scheduler reminder.

The coordinating agent repeated the read-only public-registry inspection. Contracts, Client, and DomainService still end at public version `3.110.0`, and each downloaded package identifies source SHA `27279fe6431925a6ea046c3f89af61487185c7de`. Their XML lacks `ReminderIdentityCodec`, `IReminderIntentSource`, and `AddEventStoreReminders`; this release cannot satisfy the reminder API acceptance gate. Downloaded evidence is under `/tmp/story-4-11-public-check-3sdxmya6/`.

The previously recorded broad-gate limitations remain unchanged: absent intentionally uninitialized nested Tenants sources, repository-wide packaging/evidence drift, and the Aspire baseline restore timeout. Broad suites were not repeated because no implementation changed and their existing evidence is already recorded above. No commit, push, release publication, dependency update, nested-submodule initialization, Works source change, or sealed `docs/ci.md` change was performed. After the owner publishes a release containing R6, rerun the package-only test against that named public version and record its source SHA before setting `done`.

## Review iteration 3 implementation and verification (2026-10-01)

The continuation started from clean EventStore commit `19dc1f82122564453163ac010dc7e5ae81db7ed3`. All ten iteration-3 patch findings are implemented; their checkboxes above are complete. The frozen intent, baseline identifiers, and owner-publication close gate are unchanged.

- The reconciler now marks a tenant candidate document at or above `MaxCandidatesPerTenant` as an incomplete discovery pass, preserving Degraded readiness even when a refused first registration has no durable item state. It still converges every existing candidate. The new regression failed before this correction (the pass incorrectly reported `Incomplete = 0`), passes afterward, and proves readiness recovers after capacity becomes available.
- Added persisted-state regressions for an actor/target mismatch, an unreadable tenant registry that must not prune recorded items, seven uncertain attempts whose backoff caps at `RetryMaxDelay`, all six requested option-validation rows, and a complete hosted pass that waits beyond the short retry cadence without starting a second pass.
- Corrected the intent-source effect tuple and coordinator summary. The guide now reflects audit/cancellation before witness persistence, load-time tuple quarantine plus the callback's defense-in-depth check, the actual Pending/Retrying/quarantine reason codes, and full-index readiness.

All fresh logs, XML, the synthetic package inventory, and a JSON result summary are under `/tmp/story-4-11-review-3/`. Commands ran from `references/Hexalith.EventStore` unless noted.

The exact build command, `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0`, passed with zero warnings and errors for `Hexalith.EventStore.Contracts.Tests`, `Hexalith.EventStore.Client.Tests`, `Hexalith.EventStore.DomainService.Tests`, and `Hexalith.EventStore.Server.LiveSidecar.Tests`. Each log is named `build-<Project>.log`.

| Exact test command | Result | Log / XML stem |
| --- | --- | --- |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -method '*FullTenantIndexDegradesReadinessAndStillConvergesCandidates*' -xml /tmp/story-4-11-review-3/full-index-red.xml` before the correction | One failure reproduced missing capacity readiness | `full-index-red` |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -class '*Reminder*' -xml /tmp/story-4-11-review-3/domainservice-reminders.xml` | 128/128 passed | `domainservice-reminders` |
| `tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -class '*Reminder*' -xml /tmp/story-4-11-review-3/Hexalith.EventStore.Contracts.Tests-reminders.xml` | 26 passed; one package-only skip before supplying the inventory | `Hexalith.EventStore.Contracts.Tests-reminders` |
| `tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests -class '*Reminder*' -xml /tmp/story-4-11-review-3/Hexalith.EventStore.Client.Tests-reminders.xml` | Zero discovered; Client interfaces are exercised by the runtime and package probes | `Hexalith.EventStore.Client.Tests-reminders` |
| `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.LiveSidecar.Tests -class '*Reminder*' -xml /tmp/story-4-11-review-3/Hexalith.EventStore.Server.LiveSidecar.Tests-reminders.xml` | 1/1 passed against Redis, placement, and Scheduler, including host restart, receipt replay, and Scheduler re-arm | `Hexalith.EventStore.Server.LiveSidecar.Tests-reminders` |
| `tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -class '*EffectIdentityCodecTests*' -xml /tmp/story-4-11-review-3/effect-codec.xml` | 17/17 passed | `effect-codec` |
| `tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests -xml /tmp/story-4-11-review-3/client-full.xml` | 838/838 passed | `client-full` |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -xml /tmp/story-4-11-review-3/domainservice-full.xml` | 288 passed; the existing nested-Tenants architecture failure | `domainservice-full` |
| `tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -xml /tmp/story-4-11-review-3/contracts-full.xml` | 2115 passed, 51 Packaging-only failures, two skipped; the recorded broad-gate blockers | `contracts-full` |

The coordinating agent independently audited the passing DomainService XML against all five frozen matrix rows. The full DomainService blocker is still `TenantsDomainService_DoesNotReferenceGeneratedApiHostOrDeclarePerMessageControllers`, whose required `references/Hexalith.EventStore/references/Hexalith.Tenants/src/Hexalith.Tenants/Hexalith.Tenants.csproj` is absent because nested initialization is intentionally prohibited. Full Contracts failures remain the recorded package-governance/evidence issues involving absent nested repositories, unavailable pinned Builds commits, release evidence, and OQ8 `global.json` identity drift. No reminder regression failed.

The repository-required baseline command, `aspire start --isolated --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json`, exited 2 after its 120-second restore/startup timeout (`aspire-baseline.log`). `aspire describe` and `aspire stop` reported no running AppHost (`aspire-stop.log`). The independent live-sidecar fixture nevertheless passed as recorded above.

### Fresh local package proof

- `timeout 180s python3 tools/pack-release-packages.py /tmp/story-4-11-review-3/packages 3.110.0-local.433` exited 124 while restoring Server, after packing Contracts and Client (`pack-canonical.log`).
- `python3 /tmp/story-4-11-review-3/pack-with-environment-pins.py /tmp/story-4-11-review-3/packages 3.110.0-local.433` passed and freshly packed all 14 packages (`pack-pinned.log`). This temporary helper replays the same validated manifest and canonical pack arguments, adding only `-m:1 -p:NuGetAudit=false` and a 180-second per-project bound. It is the unchanged environment fallback documented above; no repository packaging script or gate changed.
- `python3 tools/validate-release-packages.py /tmp/story-4-11-review-3/packages 3.110.0-local.433` passed for all 14 packages (`package-validation.log`).
- `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/story-4-11-review-3/packages tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -method '*PackagedReminderApi*' -xml /tmp/story-4-11-review-3/packaged-reminder-api.xml` passed 1/1 with no skipped case, proving all three isolated package-only consumers (`package-consumer.log`). The inventory contains EventStore `19dc1f82` plus the current uncommitted iteration-3 corrections.

No new public-registry inspection or publication was performed in this continuation; the prior public inspection remains dated 2026-09-30. **Still required before `done`:** the owner publishes a named public EventStore release after review, then the package-only test passes against that version and records its source SHA. The story remains `in-progress`.

Works source, the sealed `docs/ci.md`, the AD-26 codec and its golden-vector source, and the spec's frozen section are unchanged. `git diff --check` passed. No commit, push, branch change, dependency update, release publication, or nested-submodule initialization was performed. The already deferred Story 4.16 production, retention, audit, callback-origin, restore, actor-health, and operator-repair work remains deferred.

## Final continuation verification after review patches (2026-10-01)

All continuation-review patch entries C3-BH1/2/3/5/6/7/8, C3-VG1/2, and C3-R1 are resolved. The same implementation agent applied the smallest fixes in place; there was no spec loopback. C3-BH4 and C3-BH10 were rejected with the recorded evidence; the earlier context-refresh omission C3-BH9 was appended to deferred work. All three review layers completed before triage.

- Present discovery documents with null tenant or candidate collections now fail scanning closed instead of becoming complete empty scans. The durable documents, pending witnesses, and recorded unresolved readiness are retained; absent documents and null-list removal behavior remain unchanged.
- Backoff now covers every positive supported delay ratio and reaches `RetryMaxDelay`, including the valid 1-ms/1-hour configuration.
- Regression coverage now includes a full and over-capacity restored index, caller redelivery and a receipt for previously rejected registration, two distinct source aggregates with separate expected effect receipts, and a second complete hosted pass driven by controlled timers. No dependency or fixture-type additions were needed.
- The guide now distinguishes authoritative stream folds from persisted-evidence cancellation, and submission retries from retirement/quarantine retries.

The follow-up agent's focused build and Coordinator/Reconciler checks passed 74/74. Its red evidence proves both malformed-list cases and the large-ratio backoff failure before their corrections: `/tmp/story-4-11-review-fixes/regressions-red.xml` records three failures and one unaffected default-backoff pass. The fixed evidence is `/tmp/story-4-11-review-fixes/focused.xml`.

The coordinating agent independently verified the final tree. Logs, exact commands, XML, fresh packages, and the matrix audit are under `/tmp/bmad-4-11-final-review-pf5_557x/verification/`; `commands.json` records every command and exit code, and `summary.json` records test totals.

| Final command / check | Result |
| --- | --- |
| `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for Contracts, Client, DomainService, and Server.LiveSidecar test projects | Four successful builds; zero warnings/errors |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -class '*Reminder*' -result-xml <artifact>/Hexalith.EventStore.DomainService.Tests-reminders.xml` | 133/133 passed; no skips |
| `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.LiveSidecar.Tests -class '*Reminder*' -result-xml <artifact>/Hexalith.EventStore.Server.LiveSidecar.Tests-reminders.xml` | 1/1 passed against Redis, placement, and Scheduler |
| `tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests -class '*Reminder*' -result-xml <artifact>/Hexalith.EventStore.Client.Tests-reminders.xml` | Zero discovered, as documented; runtime and consumer probes exercise these interfaces |
| `python3 /tmp/story-4-11-review-3/pack-with-environment-pins.py <artifact>/packages 3.110.0-local.434` | All 14 final packages freshly packed using the unchanged documented environment fallback |
| `python3 tools/validate-release-packages.py <artifact>/packages 3.110.0-local.434` | All 14 packages validated |
| `EVENTSTORE_PACKAGE_CONTRACT_DIR=<artifact>/packages tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -class '*Reminder*' -result-xml <artifact>/Hexalith.EventStore.Contracts.Tests-reminders.xml` | 27/27 passed; includes the package-only R6 proof and all three isolated consumers, with no skips |

Here `<artifact>` is `/tmp/bmad-4-11-final-review-pf5_557x/verification`. The final synthetic inventory contains EventStore `19dc1f82122564453163ac010dc7e5ae81db7ed3` plus this uncommitted continuation. The matrix audit verifies passing coverage for all five frozen rows and all eight newly strengthened review cases. The final package-only case itself passed once within the Contracts reminder run; it was not redundantly rerun.

The earlier broad-suite evidence, Aspire baseline restore timeout, and canonical pack timeout remain recorded separately above; the repository gates were not weakened. Broad suites were not repeated after these reminder-only private fixes because their unrelated failure subjects and package-governance inputs did not change. Works source, `docs/ci.md`, the AD-26 codec and golden-vector source, the frozen intent, and both preserved baseline identifiers remain unchanged. No commit, push, branch change, dependency update, nested-submodule initialization, public-registry inspection, or release publication occurred.

The local implementation and review work are complete. The frozen close gate still requires the owner to publish a named public EventStore version after review and then record a passing package-only proof against that public version and its source SHA. Local package evidence does not close that gate.

### Review Findings

Split review, group 1: public reminder contracts, Client interfaces, identity codec,
codec tests, package consumer probes, and package-only test (2026-10-01).
EventStore `f378afdb7cdeec85144fffc20dd9a13a9775bf85..9efe9c6df8e63d48b82984fca0607f20f0fa44be`;
13 files, 852 additions, no deletions, 986 diff lines. Review mode: full.
Context: this spec, Epic 4 context, Works architecture, and EventStore project context.
All four layers completed: blind-hunter (BH), edge-case-hunter (EC),
verification-gap (VG), and acceptance-auditor (AA). Eleven individual findings
were triaged before grouping: 0 decision-needed, 4 patch, 0 defer, 7 rejected.
This group does not complete the remaining runtime, tests, or documentation review.

- [x] [Review][Patch] Pin case-sensitive aggregate identifiers in reminder regression tests [`references/Hexalith.EventStore/tests/Hexalith.EventStore.Contracts.Tests/Reminders/ReminderIdentityCodecTests.cs:15`] — medium, verification gap (VG). The actor and schedule tuple writers encode aggregate identifiers verbatim, but every existing reminder vector uses lowercase identifiers. A temporary copy of both tuple writers changed `WriteText(buffer, item)` to `WriteText(buffer, item.ToLowerInvariant())`; all 26 existing reminder codec tests still passed. That regression would route valid targets `item-1` and `Item-1` into one actor, where the coordinator's ordinal target comparison quarantines the second. Add case-distinct actor and schedule vectors. In the runtime-test group, add a persisted-state test proving both targets arm independently with separate states and no quarantine. The current implementation preserves case; this finding concerns missing regression protection.
- [x] [Review][Patch] Document reminder-name uniqueness for intent sources [`references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Reminders/IReminderIntentSource.cs:17`] — low (BH2). The public remarks require unique effect tuples but omit the separate schedule-name constraint. Two intents with distinct source coordinates can satisfy those remarks while sharing target, kind, due instant, and revision; `ConvergeCoreAsync` then records `witness-collision` and submits neither. Add the existing name/witness constraint to the interface documentation, including changing the witness when its source or payload changes. No runtime behavior or public surface needs to change.
- [x] [Review][Patch] Clarify effect identity reuse across successive schedules [`references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Reminders/IReminderIntentSource.cs:17`] — low (BH3). Uniqueness only "among current intents" leaves the lifetime requirement unstated. A later schedule that reuses source domain/aggregate/sequence, kind, and target retains the previous effect identity even if due time or schedule revision changes; the target replays the old receipt or rejects changed command semantics. Document that one logical submission retains its source coordinates for retry, while a distinct logical submission must have distinct committed source-event coordinates. This clarifies the existing AD-26 receipt contract.
- [x] [Review][Patch] State admissible intent field bounds in the public parameter documentation [`references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Reminders/ReminderIntent.cs:14`] — low (BH4). `ValidateIntent` rejects a non-positive source sequence, negative schedule revision, blank payload type, and null payload, but the record's parameter documentation does not state those constraints. A domain implementer can construct such a record and discover the rule only through quarantine. Add those bounds to the existing parameter descriptions; preserve the record shape and boundary validation.

Verification artifacts are under `/tmp/bmad-4-11-review-85wss_e0/`:

- `dotnet build tests/Hexalith.EventStore.Contracts.Tests/Hexalith.EventStore.Contracts.Tests.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` passed with zero warnings/errors (`contracts-build.log`).
- The built Contracts test executable with `-class '*ReminderIdentityCodecTests' -class '*EffectIdentityCodecTests' -result-xml /tmp/bmad-4-11-review-85wss_e0/contracts-codecs.xml` passed 43/43, with no skips (`contracts-codecs.log`).
- The built Contracts test executable with `-method '*PackagedReminderApiRunsWithoutWorksTypes'`, `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/bmad-4-11-final-review-pf5_557x/verification/packages`, and `-result-xml /tmp/bmad-4-11-review-85wss_e0/contracts-packages.xml` passed 1/1, with no skips (`contracts-packages.log`). This reruns all three isolated consumers against the existing synthetic `3.110.0-local.434` inventory; no fresh packages were produced for this review, and this is not the named-public-version close proof.
- Python AST parsing of `scripts/validate-consumer-package-references.py` passed. The isolated case-normalization mutation passed 26/26 (`case-mutation.log`, `case-mutation.xml`); no repository source was mutated.

**Group 1 patches applied (2026-10-01).** All four entries above are resolved. The
mixed-case actor and schedule vectors were calculated independently with Python
SHA-256 and the frozen tuple encoding; the existing vectors remain unchanged.
`CaseDistinctAggregatesArmIndependently` proves separate persisted aggregate state,
armed witnesses, Scheduler registrations, and discovery candidates for `item-1`
and `Item-1`, with no quarantine. Public XML documentation now states witness-name
uniqueness, source identity reuse for the same kind and target, and the required
intent field bounds. The runtime codec, receipt behavior, and public API shapes
were not changed.

Final verification, from `references/Hexalith.EventStore` (artifacts under
`/tmp/bmad-4-11-review-85wss_e0/`):

| Command | Result | Artifact |
| --- | --- | --- |
| `dotnet build tests/Hexalith.EventStore.Contracts.Tests/Hexalith.EventStore.Contracts.Tests.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` | Passed; zero warnings/errors | `patch-contracts-build.log` |
| `dotnet build tests/Hexalith.EventStore.DomainService.Tests/Hexalith.EventStore.DomainService.Tests.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` | Passed; zero warnings/errors | `patch-domainservice-build-final.log` |
| `tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -class '*ReminderIdentityCodecTests' -class '*EffectIdentityCodecTests' -result-xml /tmp/bmad-4-11-review-85wss_e0/patch-contracts-codecs.xml` | 45/45 passed; no skips | `patch-contracts-codecs.log`, `patch-contracts-codecs.xml` |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -class '*Reminder*' -result-xml /tmp/bmad-4-11-review-85wss_e0/patch-domainservice-reminders.xml` | 134/134 passed; no skips | `patch-domainservice-reminders.log`, `patch-domainservice-reminders.xml` |
| `dotnet run --project /tmp/bmad-4-11-review-85wss_e0/case-mutation/CaseMutation.csproj -c Debug -p:NuGetAudit=false -- -result-xml /tmp/bmad-4-11-review-85wss_e0/patch-case-mutation.xml` | Expected exit 1: both new mixed-case vectors reject the case-normalization mutant; the other 26 cases pass | `patch-case-mutation.log`, `patch-case-mutation.xml` |
| `aspire start --isolated --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json` | Exit 2: 120-second startup timeout during restore; OpenSSL certificate trust diagnostic also recorded | `patch-aspire-start.log` |
| `aspire describe --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json` and `aspire stop --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive` | Both report no running AppHost | `patch-aspire-describe.log`, `patch-aspire-stop.log` |

The four group 1 fixes do not resolve the other review groups, earlier open
findings, or the frozen named-public-package close gate. Story and sprint status
remain `in-progress`. No package was repacked or published, and no commit, push,
branch change, dependency update, or submodule initialization was performed.

Remaining groups for follow-up runs: (2) coordinator, persisted state, and index;
(3) actor admission, composition, readiness, periodic reconciliation, and their
composition/reconciler tests; (4) coordinator/callback unit tests and fixtures;
(5) live-sidecar tests/fixtures and guide/configuration documentation.
The frozen public-release close gate and earlier open findings remain in force.

#### Rejected

- `low` — BH1: Nonzero final Base32 padding passes the name parser. A temporary consumer confirmed parsing succeeds but stored-tuple rederivation returns false. SDK-generated tokens always have canonical padding, so everyday callers do not encounter this; another parser guard adds complexity without changing callback safety.
- `low` — EC1: The same final-padding acceptance. Every executable stored witness must pass rederivation, which refuses the demonstrated token; a new shape guard is disproportionate for an impossible SDK-generated token.
- `low` — AA1: The public name helper accepts noncanonical final padding. The helper deliberately does not authenticate a tuple, and the actual tuple guard refuses it. The small cosmetic validation benefit does not justify another branch for this rare input.
- `low` — BH5: The Client package probe's synthetic delegation uses an effect identity with a different source aggregate from its synthetic reminder. It proves public API availability, not production delegation admission; the fake is never submitted to a gateway. Reworking the fake into an authority validator exceeds a direct correction and would duplicate the separate runtime proof.
- `low` — BH6: The isolated Contracts consumer probes only DateResume, not Expiry. The current-source codec tests already pin both golden names and kind mappings, and the package lane builds the archives from that source. Expanding the probe adds duplicate coverage without a demonstrated archive defect.
- `low` — BH7: Unknown/repeated `--package` selections and malformed unselected inventory lack dedicated selector regressions. Inspection confirms complete inventory validation precedes filtering, unknown IDs are rejected, and repeated IDs are deduplicated. The successful three-package proof exercises the selection path; a new test harness for uncommon argument mistakes is disproportionate.
- `low` — BH8: Caller cancellation is reported as a 30-minute timeout. The subprocess is still killed and the test fails safely. A separate cancellation branch would improve a rare shutdown diagnostic but does not affect the contract or validation result.

### Review Findings

Split review, group 2: coordinator, persisted state, and discovery index (2026-10-01).
EventStore `f378afdb7cdeec85144fffc20dd9a13a9775bf85..9efe9c6df8e63d48b82984fca0607f20f0fa44be`
(this group has no uncommitted changes); 14 files under
`src/Hexalith.EventStore.DomainService/` (`ReminderCoordinator`, `ReminderIntentIndex`,
`ReminderLog`, and the persisted state, record, and key types); 2,178 additions,
no deletions, 2,262 diff lines. Review mode: full. Context: this spec, Epic 4 context,
Works architecture, and EventStore project context. All four layers completed:
blind-hunter (BH, 15), acceptance-auditor (AA, 6), edge-case-hunter (EC, 16), and
verification-gap (VG, 4 gaps plus 2 other). Each of the 43 findings was judged before
grouping: 1 decision-needed (3 findings; resolved 2026-10-01 as a patch), 9 patch (10 findings), 0 new defer,
20 rejected. Ten findings repeat items already recorded: 5 repeat earlier deferrals
(quarantine re-audit, quarantine disposal, disposition retention, and unverified
stored headers), and 5 repeat the open iteration-3 `EnsureCandidateAsync` null-list
patch. None is re-recorded here.

- [x] [Review][Patch] A reschedule that keeps its source coordinates is permanently quarantined as `stored-entry-duplicate` [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1521`] — medium (VG, EC). `IReminderIntentSource` allows keeping the source coordinates for the same logical submission, so the old witness A and its replacement B can share one effect identity while their names differ. Two paths persist A next to B: `RetireStaleAsync` (A is retained while `ConvergeCoreAsync` adds B, `ReminderCoordinator.cs:1154-1164`), and convergence after a failed audit or cancellation of the obsolete A (`:714-724`). The next `LoadAsync` groups stored entries by effect ID (`:1521-1524`) and quarantines both. The `stored-entry-duplicate` record then stops B from being recreated (`:623-630`). VG probed the unmodified code. On the stale path with no failure, A returns `Stale` and leaves no entries, two duplicate records, and 0 submissions after B's due time. On the cancel-outage path it also leaves 0 submissions. Removing the load-time effect-ID grouping keeps the suite at 134/134. Two current intents that share an identity are still quarantined at convergence (`:601-611`) and at callback (`:1089-1095`). Every existing reschedule test changes the source sequence. Resolved decision (2026-10-01): option 1 of 3. Delete the load-time effect-ID grouping (`:1521-1524`) and keep the reminder-name grouping. That grouping was added in `fcdae3c6` with the name grouping; its only test, `DuplicatePersistedReminderIdentitiesAreQuarantined`, stores one entry twice, which the name grouping already catches. The stream-side effect-collision checks, submitting only current witnesses, and the 4.13 effect-ID receipt still prevent a double execution. Add two regressions. Late firing: register A (seq 3, due +1h), move the stream to B (seq 3, due +8h), fire A, and expect `Stale` with only B stored and no quarantine; then fire B and expect exactly one submission. Failed cancellation: the same reschedule with the Scheduler cancel failing; after the outage clears, A is cancelled and B submits once when due. Rejected alternatives: a contract rule that EventStore cannot enforce, which leaves the fault-only failure in place (option 2), and a deferral to Story 4.15, which would ship the defect in the public R6 API (option 3).
- [x] [Review][Patch] Callback-path quarantine audit records and their audit gate are untested [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1285`] — medium (VG, pre-verified). No test reads a reminder-name `Quarantined` disposition, and none fails the audit write on a callback quarantine. VG made two mutations and the suite stayed at 134/134. Replacing the audit gate with `if (true)` cancels the reminder during an audit outage and leaves the record `Registered`. Writing `Cancelled` at `:731` passed too. Fixes: assert the name's `Quarantined` record and reason code in the callback quarantine tests; add a `FailDispositionWrites` variant of `TranslationFailureIsQuarantined` that expects `Retrying`/`audit-unavailable` with the reminder still armed; tighten `BlankStoredDomainIsQuarantined` to `ShouldBe(Quarantined)` with `domain-invalid`; and assert the name record in `WitnessCollisionOnConvergencePathIsQuarantined`.
- [x] [Review][Patch] The `state-conflict` branch for a refused erase in `PersistAsync` is untested [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1577`] — medium (VG, pre-verified). With the `TryEraseAsync` result ignored, the suite stays at 134/134. VG then seeded a competing write and the convergence reported `Cancelled=1` and removed the candidate while the competitor's work stayed stored. Add `ConflictingEraseFailsClosed` next to `ConflictingWriterFailsClosed`, asserting no `Cancelled`, the competitor state retained, the candidate kept, and the item unresolved.
- [x] [Review][Patch] `StaleWitnessWithoutAuditIsKept` cannot see whether the retry state was persisted [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/ReminderCallbackAdmissionTests.cs:86`] — medium (VG, pre-verified). All four of its assertions hold even if `RetireStaleAsync` (`ReminderCoordinator.cs:1132-1150`) persists nothing. With that persist removed, the witness stays `Armed` and readiness reports 0 unresolved during an audit outage. Assert `Status = Retrying`, `LastReasonCode = audit-unavailable`, `Attempts = 1`, and one unresolved item.
- [x] [Review][Patch] The null-collection repair in `LoadAsync` is untested [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1492`] — low (VG, pre-verified). Every existing seed uses null elements, never null lists. Changing the reads to `loaded.Entries!` and `loaded.Quarantine!` keeps the suite green, but every convergence of that document would then throw `NullReferenceException`. Add a row to `MalformedPersistedCollectionElementsAreQuarantined` with `ReminderItemState(..., null!, null!)`, asserting that the current intent is armed and that empty, non-null lists are persisted.
- [x] [Review][Patch] A failed cleanup fold is logged as a callback failure for the name `malformed` [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1452`] — low (BH, AA). `ReleaseDiscoveryIfIdleAsync` also runs from registration and reconciliation, usually after the witness is released. Event 200216 nonetheless reports "callback failed … the witness is retained" for a made-up name. Log `FailedClosed(actorId, "source-unavailable")` (200214) instead. That is the event `ConvergeAsync` already uses for the same failure.
- [x] [Review][Patch] Events 200211/200212 log reason codes in an `ExceptionType` slot [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderLog.cs:55`] — low (AA). `ReminderReconciler` already passes `index-capacity`, `candidate-missing`, and `actor-id-mismatch` into `ExceptionType` (`ReminderReconciler.cs:78`, `:89`, `:97`). The open iteration-3 patch "Reconciler scan logs drop fail-closed reason codes" would add more codes to that slot. Add a `ReasonCode` placeholder to `CandidateFailed` and `ScanFailed`, and apply this fix together with that open patch.
- [x] [Review][Patch] `Attempts` is documented as uncertain submissions only [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderEntry.cs:20`] — low (BH). The counter also advances on `audit-unavailable`, `cancel-failed`, a failed `Registered` audit, obsolete cancel failures, and `Denied` outcomes, and it drives the backoff. Correct the `Attempts` parameter text in `ReminderEntry` and `ReminderDispositionRecord` (`:20`).
- [x] [Review][Patch] `ReminderFailClosedException` claims the operation stops without scheduling [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderFailClosedException.cs:5`] — low (EC). The settling `PersistAsync` (`ReminderCoordinator.cs:911`, `:1235`) can throw `state-conflict` after `ArmAsync`, a submission, or an inline Scheduler cancellation has already run. The next pass replays the work with the same effect identity. Reword the summary to say that earlier scheduler or submission side effects may already have happened and that the durable state is unchanged.
- [x] [Review][Patch] The `RetireStaleAsync` comment says nothing is submitted [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1130`] — low (EC). The embedded `ConvergeCoreAsync` (`:1154`) submits any other current witness of the item that is due. Only the stale witness is a no-op. Correct the comment.

#### Rejected

- `false` — BH3: Concurrent first writes to the index lose an entry because an empty ETag is last-write-wins. `DaprReadModelStore.TrySaveAsync` writes with `ConcurrencyMode.FirstWrite`. The components-contrib Redis `setDefaultQuery` Lua script marks every FirstWrite key with `first-write`. An empty ETag maps to version `0`, so a write to an existing key fails atomically. That matches the in-memory fake's create-only rule.
- `false` — BH4: Two first inserts of an item both see version 1 and both win. The first insert is create-only (BH3), and item writes run inside the item's serialized actor turn.
- `false` — BH5: A `Submitted` callback cancels before persisting and strands the witness. Carried R2-BH6. A failed persist keeps the previous witness, and the next pass resubmits it with the same effect identity.
- `false` — BH12: A fail-closed convergence looks like success. The `ConvergeAsync` catch records and returns at least one unresolved item (`ReminderCoordinator.cs:177-180`). The reconciler's incomplete count only sets the cadence (carried from chunk A BH16/EC16).
- `false` — EC12: Due times above 4,294,967,294 ms cannot be armed. Carried from iteration 0: Dapr.Actors 1.18.10 accepts reminder due times up to `TimeSpan.MaxValue`. That ceiling applies to host delay options.
- `low` — BH1: A released witness is resubmitted every pass while the stream still reports it. Carried from iteration-0 BH4. The `IReminderIntentSource` contract requires the source to stop reporting an intent once its target has handled it, and a replay returns the same receipt.
- `low` — BH6, AA1: A null `Entries` list is normalized and erased without quarantine evidence. This needs corrupt stored state. The discovery candidate survives, and the next pass rebuilds the witnesses from the stream. Recording quarantine for that state would add a branch.
- `low` — BH7, EC3: Blank-domain entries are grouped as duplicates. Carried (R2-BH2, R2-EC1, chunk A EC5).
- `low` — BH9, EC9: The hot per-tenant document has no CAS backoff. Carried (iteration-0 BH13, chunk A BH6).
- `low` — BH14: The callback reads the stream before its backoff check, and idle convergence reads it twice. Moving the backoff check first would let a stale `Retrying` witness wait instead of retiring. The extra reads are bounded by the `RetryMaxDelay` period.
- `low` — AA2: A callback exception leaves readiness at its earlier value until the next pass. Carried (R2-BH6): the next pass resubmits within `ReconciliationInterval`, and readiness bookkeeping in the generic catch would add a branch.
- `low` — AA3: A witness collision found at callback keeps no digest of the stream's colliding intent. This needs a domain-source bug. The stream still holds that intent, and the stored quarantined witness keeps its own evidence.
- `low` — EC5: A fail-closed `RemoveCandidateAsync` after erasure reports `Retrying`. Carried (chunk A BH8, EC9). It heals itself and submits nothing twice.
- `low` — EC10: The callback has no due check. Carried four times, because the Scheduler owns timing.
- `low` — EC11: Clock skew inside the backoff window waits a full `RetryMaxDelay`. Carried (R2-EC3).
- `low` — EC13: A future `UpdatedAt` stretches the backoff. Carried (chunk A EC6). It needs a backward clock jump, and the work is kept.
- `low` — EC14: A valid entry can share its name with a `stored-entry-invalid` record. This needs corrupt state with two entries of the same name. The callback submits only a witness that the current stream reports exactly.


## Outstanding review patches implemented and verified (2026-10-01)

This continuation loaded the complete spec and both frontmatter context files,
then worked from clean EventStore commit
`b01c9fe0b8bd4f052cd0740c7d4ca2fac460964b` in the owning repository. All 23 outstanding patch findings from the
iteration-3 chunks B/C review and split review group 2 are now implemented.
Their checkboxes above are complete. This resolves those filed patches; the
remaining split-review groups and the frozen public-release close gate are
still outstanding.

- Registration now refuses present discovery documents whose tenant or
  candidate collections are null, matching the scan guards. Both regression
  rows try a new convergence before scanning and prove the malformed document's
  ETag, pending work, discovery evidence, and Degraded readiness survive.
- Full or over-capacity tenant indexes still degrade readiness and converge
  every existing candidate. The pass separately counts capacity-limited
  tenants, so capacity alone keeps `ReconciliationInterval`; unreadable work
  still uses the shorter retry cadence. Controlled timers prove two passes
  for ordinary, capacity-only, and capacity-plus-source-failure cases.
- Load-time duplicate detection now groups only by reminder name, as the
  resolved group-2 decision requires. Successive schedules for the same logical
  submission may retain the same source coordinates. Late stale firing and
  failed obsolete cancellation both retain the replacement without quarantine
  and ultimately produce one target receipt. Current-stream witness and effect
  collisions remain quarantined.
- Callback quarantine tests assert the durable subject's disposition and reason
  code. A translation failure during an audit outage retains a retrying, armed
  witness until auditing recovers. Stale-audit retry state, refused erase CAS,
  null persisted item collections, and workload denial now have persisted-state
  assertions. Composition tests that modify `DAPR_APP_ID` run in a non-parallel
  collection, and disabled reconciliation awaits service completion.
- Scan and candidate logs now carry `ReasonCode` separately from
  `ExceptionType`; fail-closed exceptions preserve their reason. Failed cleanup
  folds use event 200214 with `source-unavailable`. Retry-counter and
  fail-closed/stale comments now describe the actual behavior.
- The guide and configuration reference document option bounds, fail-closed and
  scan actions, `witness-not-current`, capacity-only cadence, and deterministic
  load-time repair before orphan handling. Codec tests cover decomposed text
  refusal and the excluded Crockford letters I/L/O/U. The runtime codec and
  frozen AD-26 golden vectors are unchanged.

Fresh logs, XML, packages, test totals, and matrix evidence are under
`/tmp/story-4-11-current-patches/`. Commands below ran from
`references/Hexalith.EventStore`.

| Command | Result | Artifact |
| --- | --- | --- |
| `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for Contracts.Tests, Client.Tests, DomainService.Tests, and Server.LiveSidecar.Tests | Four successful builds; zero warnings/errors | `contracts-build.log`, `client-build.log`, `domainservice-build.log`, `live-build.log` |
| Built DomainService test executable with `-method` selections for the two same-source reschedules, null discovery lists, refused erase, and hosted cadence, before runtime corrections | 7 executed: five expected regression failures, two passing existing guards | `regressions-red.log`, `regressions-red.xml` |
| `tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests -class '*Reminder*' -result-xml /tmp/story-4-11-current-patches/domainservice-reminders.xml` | 142/142 passed; no skips | `domainservice-reminders.log` |
| `tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -class '*ReminderIdentityCodecTests' -class '*EffectIdentityCodecTests' -result-xml /tmp/story-4-11-current-patches/contracts-codecs.xml` | 50/50 passed; no skips | `contracts-codecs.log` |
| `tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests -result-xml /tmp/story-4-11-current-patches/client-full.xml` | 838/838 passed; no skips | `client-full.log` |
| `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.LiveSidecar.Tests -class '*ReminderRecoveryLiveSidecarTests' -result-xml /tmp/story-4-11-current-patches/live-reminders.xml` | 1/1 passed against Redis, placement, and Scheduler; registration, direct callback, restart receipt replay, and deleted-reminder recovery proved | `live-reminders.log` |
| Full built DomainService test executable with `-result-xml /tmp/story-4-11-current-patches/domainservice-full.xml` | 302 passed; one existing nested-Tenants architecture failure | `domainservice-full.log` |
| Full built Contracts test executable with `-result-xml /tmp/story-4-11-current-patches/contracts-full.xml` | 2,124 passed; 51 failures, all in Packaging; two skips | `contracts-full.log` |
| `aspire start --isolated --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json` | Exit 2: 120-second restore/startup timeout; OpenSSL certificate-trust diagnostic recorded | `aspire-baseline.log` |
| `aspire describe --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json` and `aspire stop --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive` | Both report no running AppHost | `aspire-describe.log`, `aspire-stop.log` |
| `git diff --check` | Passed | Final working-tree check |

Both the implementation and coordinating agents audited the fresh XML against
all five frozen matrix rows; every selected covering case executed and passed.
Evidence: `matrix-audit.json`, `root-matrix-audit.json`, and
`test-summary.json`. The DomainService failure remains
`TenantsDomainService_DoesNotReferenceGeneratedApiHostOrDeclarePerMessageControllers`,
whose nested Tenants source is intentionally absent. The 51 Contracts failures
remain the recorded repository-wide package-governance, pinned-source, and
release-evidence blockers. No reminder regression failed and no gate was weakened.

### Fresh local package proof and remaining public gate

- `timeout 180s python3 tools/pack-release-packages.py /tmp/story-4-11-current-patches/packages 3.110.0-local.441`
  exited 124 during Server restore after packing Contracts and Client
  (`pack-canonical.log`).
- `python3 /tmp/story-4-11-current-patches/pack-with-environment-pins.py /tmp/story-4-11-current-patches/packages 3.110.0-local.441`
  freshly packed all 14 packages (`pack-pinned.log`). This copies the previously
  documented temporary environment fallback, retaining the validated manifest
  and canonical Release/package-mode arguments while adding only `-m:1`,
  `-p:NuGetAudit=false`, and a 180-second per-project bound. No repository
  packaging script changed.
- `python3 tools/validate-release-packages.py /tmp/story-4-11-current-patches/packages 3.110.0-local.441`
  passed for all 14 archives (`package-validation.log`).
- `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/story-4-11-current-patches/packages tests/Hexalith.EventStore.Contracts.Tests/bin/Debug/net10.0/Hexalith.EventStore.Contracts.Tests -method '*PackagedReminderApiRunsWithoutWorksTypes' -result-xml /tmp/story-4-11-current-patches/packaged-reminder-api.xml`
  passed 1/1 with no skips, proving all three isolated package-only consumers
  against the final local code (`package-consumer.log`). The pack contains
  EventStore `b01c9fe0b8bd4f052cd0740c7d4ca2fac460964b` plus this uncommitted continuation.

The coordinating agent's independent read-only public inspection on 2026-10-01
still finds Contracts, Client, and DomainService ending at public `3.110.0`,
source SHA `27279fe6431925a6ea046c3f89af61487185c7de`; their XML contains none
of the reminder API terms. Evidence:
`/tmp/story-4-11-public-inspection-bf9m9060/summary.json`.
**Still required before `done`:** the owner publishes a named public EventStore
release containing the API after review, then the package-only proof passes
against that named version and records its source SHA. Local proof does not
close that gate, so the story remains `in-progress`.

Works source, sealed `docs/ci.md`, the frozen intent and baseline identifiers,
and AD-26 codec/vector source remain unchanged. Protected-file hashes are in
`protected-files.json`. No commit, push, branch change, dependency update,
submodule initialization, or release publication was performed. Previously
recorded Story 4.16 production, operator-repair, callback-origin, audit,
retention, actor-health, and restore work remains deferred.

### Continuation review C4 patches

- [x] [Review][Patch] Clarify incomplete-scan versus retained null-fold cadence and the effective minimum; cover null folds and a longer retry delay with controlled timers.
- [x] [Review][Patch] Reject a trailing newline in ActorTypeName with an absolute regex end anchor and an invalid-options regression.
- [x] [Review][Patch] Align public option XML with capacity-only reconciliation cadence.
- [x] [Review][Patch] Carry witness-name uniqueness and logical-submission source identity reuse into the guide caller contract.
- [x] [Review][Patch] Preserve bounded exception-type evidence in event 200214 and add emitted-diagnostic regression coverage for events 200211, 200212, and 200214.
- [x] [Review][Patch] Add candidate-only source-unavailable operator recovery to the runbook.
- [x] [Review][Patch] Limit the Scheduler-only production requirement to callback admission and document trusted convergence calls.

### C4 final implementation, review, and verification (2026-10-01)

All seven grouped C4 patch tasks above are resolved. Fifteen individual review
findings are recorded in the triage log before grouping; the three rejected
findings preserve the recorded rare-configuration and earlier admission/index
decisions. No new work was deferred. This continuation review does not claim
to complete the separately listed split-review groups 3–5.

- Actor type validation now uses an absolute end anchor and rejects a trailing
  newline. Public option XML and both guides distinguish incomplete scans from
  retained outcomes, capacity-only cadence, and the effective minimum of the
  retry delay and normal interval.
- Controlled timers cover null folds with and without capacity, and a valid
  retry delay longer than the interval. They verify two passes, persisted
  witnesses/candidates, no submission, and Degraded readiness where required.
- Event 200214 retains bounded exception-type metadata. Runtime diagnostic
  tests inspect the actual structured state and formatter output for events
  200211, 200212, and 200214, preserving reason/type separation and excluding
  exception messages. Each new C# type has its own file.
- The guide carries the existing name/witness and source-identity lifetime
  rules, explains candidate-only cleanup recovery, and distinguishes Scheduler
  callback admission from trusted convergence calls. It requires a changed
  schedule witness when evidence changes without prescribing revision as the
  only way to change that witness.

The implementation agent's affected-class check passed 53/53. The coordinating
agent independently read the final source, tests, and documentation diff, then
ran final checks from `references/Hexalith.EventStore`. Final commands, logs,
XML, packages, summary, protected-file hashes, and matrix audit are under
`/tmp/bmad-build-4-11-review-45gnw5pt/final-verification/`.

| Final check | Result |
| --- | --- |
| `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for Contracts, Client, DomainService, and Server.LiveSidecar test projects | Four successful builds, zero warnings/errors |
| Built DomainService executable with `-class '*Reminder*' -result-xml <artifact>/domainservice-reminders.xml` | 150/150 passed, no skips |
| Built Contracts executable with `-class '*ReminderIdentityCodecTests' -class '*EffectIdentityCodecTests' -result-xml <artifact>/contracts-codecs.xml` | 50/50 passed, no skips |
| Built LiveSidecar executable with `-class '*ReminderRecoveryLiveSidecarTests' -result-xml <artifact>/live-reminders.xml` | 1/1 passed against Redis, placement, and Scheduler |
| `python3 /tmp/story-4-11-current-patches/pack-with-environment-pins.py <artifact>/packages 3.110.0-local.442` | All 14 final packages freshly packed using the unchanged documented temporary environment fallback |
| `python3 tools/validate-release-packages.py <artifact>/packages 3.110.0-local.442` | All 14 packages validated |
| `EVENTSTORE_PACKAGE_CONTRACT_DIR=<artifact>/packages` and built Contracts executable with `-class '*Reminder*' -result-xml <artifact>/contracts-reminders-packaged.xml` | 34/34 passed, including the fresh package-only R6 proof and all three isolated consumers; no skips |
| Independent final matrix and protected-file audits; `git diff --check` in both repositories | All five frozen rows have passing executed coverage; protected files unchanged; diff checks pass |

Here `<artifact>` is the final-verification directory above. The inventory
contains EventStore `b01c9fe0b8bd4f052cd0740c7d4ca2fac460964b` plus this final
uncommitted continuation. Client's full 838/838 result remains the earlier
fresh result from this run; the follow-up does not change Client code. The
recorded broad-suite blockers (one nested-Tenants architecture test and 51
Contracts packaging/evidence checks), Aspire baseline timeout, and canonical
pack timeout remain separate from these passing focused proofs. Those broad
checks were not repeated after the private reminder/option/diagnostic fixes
because their unrelated failure subjects did not change.

The frozen close gate remains open: the owner publishes a named public
EventStore version after review, then package-only proof must pass against
that version and record its source SHA. Spec and sprint status remain
`in-progress`; the completion/commit phase is not entered while this
acceptance gate remains unsatisfied. No staging, commit, push, branch change,
dependency update, nested-submodule initialization, or publication occurred.
Works code, the frozen intent, baseline identifiers, sealed CI document, and
AD-26 codec/vector source remain unchanged.

### Continuation review C5 patches

- [x] [Review][Patch] Keep delegation and submission causation stable for one logical effect across schedule changes; document the contract and prove replay after a committed-but-uncertain original receipt, including the real target.
- [x] [Review][Patch] Preserve unresolved status recorded during a reconciliation pass; prove a newly registered durable pending witness remains Degraded after the older discovery snapshot completes.
- [x] [Review][Patch] Extend the distinct-source receipt test to use different source domains and independently expected identities.
- [x] [Review][Patch] Prove discovery CAS retries preserve independent accepted items and tenants under forced registration interleavings.
- [x] [Review][Patch] Remove unused persisted-entry effect-ID tuple/output bookkeeping while preserving all validation.

### C5 final implementation, review, and verification (2026-10-01)

The remaining split-review groups 3–5 are reviewed: actor admission and host
composition, readiness and reconciliation, unit tests and fixtures, live proof,
and documentation. All three independent review layers completed. Every one
of their fifteen findings has a triage row above: six patch findings grouped
into five corrections, nine rejected findings, and no new deferred work.
All five C5 patch tasks are resolved. Earlier split-review groups and their
filed patches remain resolved; the public-release close gate remains open.

- Delegation and submission now share stable `wrk-<EffectId>` causation across
  schedule changes. Tests first commit a receipt while returning an uncertain
  transport result, then reschedule with the same source coordinates. Matching
  command semantics replay and release the new witness; changed semantics
  conflict and retain it for retry. The fake checks the committed receipt's
  identity, command, workload, purpose, and causation. The live Redis/Dapr test
  also reschedules one logical effect and reads the real target receipt to
  verify its stable causation and replay.
- Readiness captures a record version before discovery begins and prunes only
  older, undiscovered records. A controlled runtime interleaving registers a
  new durable pending witness during an older pass and verifies it stays
  unresolved and `Degraded` after that pass completes. Existing obsolete-record
  pruning coverage remains passing.
- Independent registrations force both tenant-candidate and tenant-registry
  CAS races, proving successful retries preserve both accepted items, their
  Scheduler registrations, and discovery. The distinct-source receipt test
  uses different source domains and independently expected effect tuples.
- Persisted-entry loading drops its unused effect-ID tuple/output while
  retaining codec validation and the existing blank-domain behavior. The guide
  and public delegation XML explain stable causation and its distinction from
  the schedule witness; no public API shape changes.

The new regression run reproduced four failures before the corrections; its
two discovery-contention rows already passed and add coverage for existing
behavior. The implementation agent then passed 135/135 affected unit tests and
1/1 live test. Evidence is under
`/tmp/story-4-11-review-fixes-final-tu5cwzen/`, including `regressions-red.xml`.
The coordinating agent independently read the final source/test/documentation
diff and ran final checks from `references/Hexalith.EventStore`:

| Final check | Result |
| --- | --- |
| `dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for Contracts, Client, DomainService, and Server.LiveSidecar | Four builds passed; zero warnings/errors |
| Built DomainService executable, `-class '*Reminder*' -result-xml <artifact>/domainservice-reminders.xml` | 155/155 passed, no skips |
| Built Contracts executable, `-class '*ReminderIdentityCodecTests' -class '*EffectIdentityCodecTests' -result-xml <artifact>/contracts-codecs.xml` | 50/50 passed, no skips |
| Built LiveSidecar executable, `-class '*ReminderRecoveryLiveSidecarTests' -result-xml <artifact>/live-reminders.xml` | 1/1 passed against Redis, placement, and Scheduler |
| `python3 /tmp/story-4-11-current-patches/pack-with-environment-pins.py <artifact>/packages 3.110.0-local.451` | All 14 packages freshly packed using the unchanged, previously documented environment fallback |
| `python3 tools/validate-release-packages.py <artifact>/packages 3.110.0-local.451` | All 14 release packages validated |
| `EVENTSTORE_PACKAGE_CONTRACT_DIR=<artifact>/packages` and built Contracts executable, `-method '*PackagedReminderApiRunsWithoutWorksTypes' -result-xml <artifact>/package-consumer.xml` | 1/1 passed; all three isolated package-only consumers proved, no skips |
| Independent XML/matrix and protected-file audits; `git diff --check` in both repositories | All five frozen matrix rows have passing executed coverage; 190 protected entries unchanged; diff checks passed |

Here `<artifact>` is
`/tmp/bmad-build-4-11-resume-ek8r3wwx/final-verification/`. It contains exact
commands, logs, XML, fresh packages and package hashes, `summary.json`,
`matrix-audit.json`, and `protected-files.json`. The local pack represents
EventStore `b01c9fe0b8bd4f052cd0740c7d4ca2fac460964b` plus the preserved
uncommitted continuation and C5 corrections. Works HEAD remains
`d73b058c1a430cc2b988e1db4bd5ffcda775434c`; both original full baseline
identifiers remain unchanged. The review input and rewritten final diff are
retained in the parent artifact directory.

The previously recorded broad-suite blockers (one nested-Tenants architecture
test and 51 Contracts packaging/evidence checks) and canonical pack timeout
were not repeated: none of their failure subjects changed. The environment
fallback packs the same validated release inventory with Release package
references, serialized builds, and NuGet audit disabled. The repository's
canonical pack tooling and dependency versions remain unchanged.

Before the C5 edits, `aspire start --isolated --apphost
src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj
--non-interactive --format Json` exited 2 after a 120-second restore timeout,
also reporting the OpenSSL certificate-trust diagnostic. `aspire describe
--apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj
--non-interactive --format Json` and `aspire stop --apphost
src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj
--non-interactive` both exited 0 and confirmed no running AppHost. Logs are
`aspire-start.log`, `aspire-describe.log`, and `aspire-stop.log` in the
implementation agent's artifact directory above. This baseline environment
blocker remains separate from the passing Redis/Dapr live proof.

A fresh read-only inspection of official NuGet indexes and packages still
finds Contracts, Client, and DomainService at public `3.110.0`, source SHA
`27279fe6431925a6ea046c3f89af61487185c7de`, without the reminder API. Evidence:
`/tmp/bmad-build-4-11-resume-ek8r3wwx/public-inspection/summary.json`.
**Still required before `done`:** the owner publishes a named public EventStore
release containing the reviewed API, then the package-only proof passes
against that version and records its source SHA. Local proof cannot close this
frozen gate. Spec and sprint status remain `in-progress`; the workflow's
completion/commit phase is not entered while acceptance remains incomplete.

Works source, the frozen intent, sealed `docs/ci.md`, and AD-26 codec/vector
source remain unchanged. Existing Story 4.16 deferred work remains deferred.
No staging, commit, push, branch change, dependency update, submodule
initialization, or release publication was performed.

### Resumption public-release gate audit (2026-10-01)

The resumed build found no missing local implementation or review task. The
current EventStore HEAD remains `b01c9fe0b8bd4f052cd0740c7d4ca2fac460964b`;
all 24 changed/untracked source files and all 14 local `.451` package archives
match the C5 final-verification hashes. Independent XML audits confirm
155/155 runtime tests, 50/50 codecs, 1/1 live Redis/Dapr proof, and 1/1 local
package-only proof, with no skips and passing coverage for every frozen matrix
row. All 190 protected entries match. Passing unchanged checks were not rerun;
no source correction was warranted. Audit evidence is in
`/tmp/story-4-11-current-audit-1ceyfg95/audit-summary.json` and
`/tmp/bmad-build-4-11-public-gate-590_8z_j/prior-verification-audit.json`.

A fresh read-only inspection of the official NuGet indexes and downloaded
packages still finds Contracts, Client, and DomainService ending at public
`3.110.0`, source SHA `27279fe6431925a6ea046c3f89af61487185c7de`.
Their assemblies contain none of the required reminder API names; Contracts
and Client XML also lack the API, and DomainService ships no XML. Package
hashes, indexes, nuspec metadata, and symbol checks are retained in
`/tmp/bmad-build-4-11-public-gate-590_8z_j/summary.json`.

The approved close gate remains unsatisfied: the owner publishes a named
public EventStore release containing the reviewed API, then package-only proof
must pass against that version and record its source SHA. Spec and sprint
status remain `in-progress`; the workflow does not enter completion while
acceptance remains incomplete. This resumption only records the fresh audit;
existing source changes, baseline identifiers, frozen intent, deferred work,
and sprint status are preserved. No staging, commit, push, branch change,
dependency update, submodule initialization, or publication occurred.

### Current-checkout and public-release gate audit (2026-10-04)

The complete spec and both frontmatter context files were loaded before this
audit. Works started clean at `381e33b291340d363d64ff132e31439aeb5d6b81` and
EventStore remains clean at `9b525ba8f0adf30a466f8727f1b37564181798eb`.
Every implementation and review patch is already present. All 24 files
committed by the final C5 correction commit
`4339eb6aa4d52b83adc558d2c03687b7ac7d43f2` still match that commit byte for byte;
no source correction was warranted. The earlier temporary verification
directories are absent in this container, so their historical XML/package
hashes were not re-audited. Fresh focused evidence is under
`/tmp/story-4-11-current-20261004-0qkoa02d/`.

Commands ran from `references/Hexalith.EventStore`:

| Command | Result |
| --- | --- |
| `timeout 180s dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for DomainService.Tests and Contracts.Tests | Both passed; zero warnings/errors |
| Built DomainService executable with `-class '*Reminder*' -result-xml /tmp/story-4-11-current-20261004-0qkoa02d/domainservice-reminders.xml` | 155/155 passed; no skips/errors |
| Built Contracts executable with `-class '*ReminderIdentityCodecTests' -class '*EffectIdentityCodecTests' -result-xml /tmp/story-4-11-current-20261004-0qkoa02d/contracts-codecs.xml` | 50/50 passed; no skips/errors |
| Python AST parsing of `scripts/validate-consumer-package-references.py`; `git diff --check` in both repositories | Passed |

The implementation and coordinating agents independently matched executed,
passing persisted-state tests to all five frozen matrix rows. Evidence:
`matrix-audit.json` and `audit-summary.json` in the fresh artifact directory,
plus `/tmp/bmad-build-4-11-resume-20261004-iyj60jg0/root-matrix-audit.json`.
The audit records hashes for 62 reminder-related files and confirms the
AD-26 codec, closed kind catalog, golden-vector test source, and sealed
`docs/ci.md` still match the original EventStore baseline. The historical
live-sidecar and local-package proofs were not repeated in this gate audit.

A fresh read-only inspection of official NuGet indexes and downloaded archives
still finds Contracts, Client, and DomainService ending at public `3.110.0`,
source SHA `27279fe6431925a6ea046c3f89af61487185c7de`. None of their assemblies
contains the required reminder API names; Contracts/Client XML also lacks them,
and DomainService ships no XML. Indexes, package hashes, metadata, and symbol
checks are retained in `/tmp/story-4-11-public-20261004-tct_s112/summary.json`.

The frozen close gate remains open: the owner publishes a named public release
containing R6, then package-only proof must pass against that version and record
its source SHA. Spec and sprint status remain `in-progress`. This continuation
only appends the audit record; source, frozen intent, baseline identifiers, and
existing deferred work are unchanged. No staging, commit, push, branch change,
dependency update, submodule initialization, or publication was performed.

### Public 3.112.0 package gate satisfied (2026-10-04)

The owner-published EventStore `3.112.0` release is now available. All 14 public
archives were downloaded from the official NuGet flat-container endpoints and
validated without changing their bytes. Their nuspec source commit is
`38efbefd5da65d538723f9f85eca6a186dfc0a2f`. Contracts, Client, and DomainService
contain the required typed-reminder APIs.

`PackagedReminderApiRunsWithoutWorksTypes` passed 1/1 with zero skips against
these public packages, exercising all three isolated package-only consumers.
The frozen named-public-package close gate is satisfied; this supersedes the
earlier dated observations that a reminder release was unavailable.

At the user's request, the shared Builds catalog now pins EventStore to
`3.112.0`. All Works projects restore and the Release solution build passes with
zero warnings/errors. The central catalog, family alignment, Works consumer
authority, and package-version exception checks also pass. Restored Works
assets select only EventStore `3.112.0` packages.

Durable evidence, including official package URLs/hashes/source metadata and
passing test XML, is in [the public package summary](evidence/story-4-11-public-3.112.0/summary.json).
This dependency update records the successful acceptance proof; spec and
sprint workflow status are preserved. No source implementation, frozen intent,
baseline identifier, submodule checkout, staging, commit, or push changed.

### Final implementation verification after public release (2026-10-04)

The complete spec and both frontmatter context files were loaded before this
verification. Every execution task and review patch is already implemented in
the clean EventStore checkout at
`2242ad55a1b678828df8aa093fd92399c29af5bf`. All 24 files from the final C5
correction commit still match that commit byte for byte; no source correction
was needed. The sealed `docs/ci.md`, AD-26 effect codec, closed kind catalog,
and effect golden-vector test source match the original EventStore baseline.

Fresh commands ran from `references/Hexalith.EventStore`. Logs, XML, archive
audits, and the executed matrix audit are under
`/tmp/story-4-11-implementation-20261004-B5HFMQjR/`.

| Command / check | Result |
| --- | --- |
| `timeout 180s dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for DomainService.Tests and Contracts.Tests | Both passed; zero warnings/errors |
| Built DomainService executable with `-class '*Reminder*' -result-xml <artifact>/domainservice-reminders.xml` | 155/155 passed; no errors/skips |
| Built Contracts executable with `-class '*ReminderIdentityCodecTests' -class '*EffectIdentityCodecTests' -result-xml <artifact>/contracts-codecs.xml` | 50/50 passed; no errors/skips |
| Audit of the existing public `3.112.0` archives against the checked-in public package evidence | All 14 archive hashes and nuspec source commits match |
| `python3 tools/validate-release-packages.py /tmp/eventstore-3-112-0-qf7z2woi/packages 3.112.0` | All 14 public release packages validated |
| `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/eventstore-3-112-0-qf7z2woi/packages` and built Contracts executable with `-method '*PackagedReminderApiRunsWithoutWorksTypes' -result-xml <artifact>/public-package-consumer.xml` | 1/1 passed; all three isolated package-only consumers exercised; no errors/skips |
| Executed XML matrix audit, Python AST parsing of the package-consumer validator, and `git diff --check` in both repositories | All five frozen matrix rows have passing persisted-state coverage; checks passed |

Here `<artifact>` is the fresh verification directory above. The public
packages identify source SHA
`38efbefd5da65d538723f9f85eca6a186dfc0a2f`; this repeat confirms the named
public-package acceptance gate remains satisfied. The existing durable
[public package summary](evidence/story-4-11-public-3.112.0/summary.json)
and proof XML are preserved.

No implementation source changed, so the previously recorded full-suite,
live-sidecar, and Aspire-baseline evidence was not repeated. The historical
unrelated broad-suite blockers remain separately recorded; none was hidden
by changing a gate. Works adoption and the Story 4.16 production, delegation,
callback-origin, audit, retention, actor-health, operator-repair, and restore
work remain outside this story. This verification changes only this record;
it does not change the frozen intent, baseline identifiers, dependency pins,
submodule checkout, or Git history.

### Release-closure review C6 patches

- [x] [Review][Patch] Classify shared effect identities across every valid current intent before reminder-name collapse; prove overlapping witness/effect collisions cannot submit or arm the third witness.
- [x] [Review][Patch] Assert the real Scheduler delay and repeat period after initial registration and reconciliation re-arm in the existing live-sidecar proof.
- [x] [Review][Patch] Mark the historical public R6 package-proof ledger entry resolved and link the retained 3.112.0 evidence.

### C6 verification and corrected-release gate (2026-10-04)

The review found a runtime defect in convergence: collapsing two same-name
witnesses before grouping effect identities concealed either witness's overlap
with a third reminder. The correction groups every valid intent first, then
retains the existing name-collision quarantine. All three evidence digests have
durable quarantine dispositions; no command is submitted and no reminder is
armed. The 24 regressions cover both source overlaps, all six input orders,
and both due and future work. All 24 failed before the correction.

The existing live proof now checks the actual Scheduler delay against the
scheduling operation's clock interval, with two seconds of tolerance and a
90-second operation bound, and checks the repeat period exactly against
`RetryMaxDelay`. It verifies both registration and reconciliation re-arm and
parses Scheduler's `@every <Go duration>` representation without discarding
unparsed text.

Durable evidence is under
[story-4-11-c6-2026-10-04](evidence/story-4-11-c6-2026-10-04/summary.json).
The coordinator independently inspected the patches and persisted-state
assertions, rebuilt all four required Debug/source-reference projects with
zero warnings/errors, and ran the following checks after the implementation
agent returned. Exact commands, environments, exit codes, and results are in
[commands.json](evidence/story-4-11-c6-2026-10-04/commands.json).

| Check | Result |
| --- | --- |
| DomainService `-class '*Reminder*'` | 179/179 passed, including the 24 new collision cases |
| Contracts `-class '*Reminder*'`, supplied with the new local inventory | 34/34 passed; includes the one R6 proof exercising all three isolated package-only consumers |
| Contracts `-class '*EffectIdentityCodecTests'` | 17/17 passed; together with the 33 reminder codec cases above, all 50 codec cases passed |
| Client `-class '*Reminder*'` | Zero discovered, as previously documented; exit 0 |
| LiveSidecar `-class '*Reminder*'` | 1/1 passed against Redis, placement, and Scheduler; zero errors/skips |
| Fresh local `3.112.0-local.462` release inventory and canonical validator | All 14 packages packed and validated |
| Collision regressions against assemblies extracted from the new local packages | 24/24 passed |
| The same regressions against official public `3.112.0` assemblies | 24/24 failed on unintended submission or arming; zero errors/skips |
| Frozen matrix audit | All five rows have passing persisted-state coverage |
| Frozen intent, sealed CI document, effect codec/catalog, and golden vectors | Bytes unchanged; both repository diff checks passed |

Packing reused the previously documented environment fallback: the validated
release manifest and canonical pack arguments, adding only `-m:1` and
`-p:NuGetAudit=false`, with a 180-second per-project limit. No repository
packaging script or dependency changed. The local package metadata names
source base `2242ad55a1b678828df8aa093fd92399c29af5bf`; its corrected runtime
includes the uncommitted C6 patch, so this is not an immutable public release
SHA. Archive and source hashes are retained in the evidence directory.

The normal live fixture failed before the test body on exhausted administrator
inotify allocation; a separate root host subprocess then encountered a Redis
published-port EOF. The successful agent run and independent coordinator
repeat used the cached .NET 10 Alpine image on Docker host networking, with
the same read-only Dapr 1.18.4 binary and existing backing containers. No
shared service, host setting, limit, or test gate changed. The exact image
digest and command are retained.

Broader verification remains blocked. The post-patch full DomainService
binary returned exit 1: 339 passed and one failed because the intentionally
uninitialized nested Tenants project is absent. The pre-patch full Contracts
binary, supplied with public packages, timed out at 180 seconds (exit 124),
after reporting 44 unrelated packaging/governance failures; it produced no
final XML or aggregate result. Both commands and logs are retained. A fresh
pre-change `aspire start --isolated --apphost
src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj
--non-interactive --format Json` returned exit 2 on the same missing nested
Tenants projects; `aspire describe` and `aspire stop` returned exit 0. No
nested submodule was initialized to make these checks pass.

The historical public R6 API-availability proof and its resolved ledger entry
remain valid, but public `3.112.0` does not contain this runtime correction.
**Story 4.11 remains `in-progress`:** the owner must publish a corrected named
public release with its immutable source SHA, then repeat the package-only
R6 proof and collision regressions against that version before closure.
The build workflow stops at review verification while the broader checks
remain blocked; no `done` transition or commit was made. The preserved
baseline identifiers and `review_loop_iteration: 0` remain unchanged.

### Public 3.113.0 corrected-release gate satisfied (2026-10-05)

The owner-published EventStore `3.113.0` release contains the C6 overlap correction.
All 14 public archives were downloaded from the official NuGet flat-container
endpoints and `python3 tools/validate-release-packages.py` accepted them at
`3.113.0`. Every nuspec repository commit is
`865cd9e49273dffbb1cdae85efeaf1aac322e09e`. Reminder contract, client, and
coordinator sources at that commit match the current EventStore tree.

`PackagedReminderApiRunsWithoutWorksTypes` passed 1/1 with zero skips against
these public packages, exercising all three isolated package-only consumers.
`OverlappingWitnessAndEffectCollisionsQuarantineEveryIntent` passed 24/24 with
zero skips when the DomainService test host loaded the official `3.113.0`
Client, Contracts, DomainService, ServiceDefaults, Server, and Testing
assemblies. That is the corrected named-public proof the C6 close gate required.

Durable evidence is in
[the public 3.113.0 summary](evidence/story-4-11-public-3.113.0/summary.json).
The shared Builds catalog already pins EventStore to `3.113.0`; this run did
not change it. Works adoption remains Story 4.15. Story 4.16 still owns
production Scheduler admission, delegation, callback origin, audit retention,
actor health, operator repair, and restore. The previously recorded full-suite
and Aspire blockers stay environmental: the nested Tenants project is
intentionally uninitialized, and no gate was weakened to hide them.

No EventStore source change, commit, push, dependency update, or nested
submodule initialization was performed. The frozen intent and both baseline
identifiers are unchanged. Spec status is `done`. Sprint status is `review`.

### Review Findings

Review of the C6 correction (2026-10-05). EventStore `2242ad55a1b678828df8aa093fd92399c29af5bf..f6f7fd4a973b9db3466d84b632015e4e8b0f2a2e`
(HEAD `738da5c95107d3ad84b3856558bf4a0ba7c3a9ca` matches `f6f7fd4a` for these files; later `AggregateActor` commits touch drain reminders only and are outside 4.11). Three files:
`ReminderCoordinator.cs`, `ReminderCoordinatorTests.cs`, and `ReminderRecoveryLiveSidecarTests.cs`;
161 additions, 18 deletions, 363 diff lines. Review mode: full. Context: this spec, Epic 4 context,
and Works architecture (AD-20 R6, AD-25 to AD-28). All four layers completed:
blind-hunter (BH, 11), acceptance-auditor (AA, 4), edge-case-hunter (EC, 2), and
verification-gap (VG, none). Each of the 17 findings was judged before grouping:
0 decision-needed, 3 patch (5 findings), 0 defer, 12 rejected. No finding reached the runtime
behavior: the reordered classification is correct on the fresh, stored-entry, reconciler, and
callback paths, and the 24 regressions plus the existing `SharedEffectIdentityIsQuarantined`
(stored armed entry) cover it. All three patches are comment or documentation corrections.

- [x] [Review][Patch] The shared-effect docs contradict the label the overlap case records [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:49`] — low (AA, BH). The guide (`:49-51`, "both are quarantined as `effect-collision`"; `:105-109`) and the public `IReminderIntentSource` remarks (`src/Hexalith.EventStore.Client/Reminders/IReminderIntentSource.cs:17-19`) say every intent that shares an effect identity is quarantined as `effect-collision`. When a shared-name pair also overlaps a third name, `ReminderCoordinator.cs:593` labels the shared name `effect-collision` and `:603` overwrites it with `witness-collision`. The new theory pins that result: two `witness-collision` records and one `effect-collision` record. An operator holding the third name's `effect-collision` record finds no `effect-collision` partner. Document the precedence: a witness collision on a shared name is recorded as `witness-collision` even when that name also shares an effect identity. Add the same sentence to the code comment at `:584`. The suggested partner pointer on quarantine records is rejected; it adds a durable field that needs AD-28 approval.
- [x] [Review][Patch] The convergence comment no longer says why shared effect identities are quarantined [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:584`] — low (BH). C6 deleted the reason: the target rejects the second submission on a semantic-digest conflict, or silently replays the first receipt. The new comment explains only why grouping now runs before name collapse. Restore the reason in the new comment.
- [x] [Review][Patch] The new theory's summary says both witnesses are retained [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/ReminderCoordinatorTests.cs:932`] — low (BH, AA). The test asserts `state.Entries.ShouldBeEmpty()`. Only the evidence digests of both same-name intents are kept, as quarantine records. Reword the summary.

#### Rejected

- `false` — BH5: No callback test covers the overlap case. The callback path never merges intents by name. It compares the fired name's effect ID with every valid intent under another name (`ReminderCoordinator.cs:1090-1092`), so the C6 defect never existed there. `SharedEffectIdentityAtCallbackIsQuarantined` covers that comparison.
- `false` — BH6: Wider grouping could over-quarantine, repeated passes could be unstable, and mixed due/future input is untested.
  - Over-quarantine: a group needs two distinct names that share one effect ID (`Distinct().Skip(1).Any()`), so an unrelated intent cannot join it. Existing multi-intent tests would catch over-quarantine.
  - Repeated passes: `AddQuarantine` deduplicates by digest (`:346-349`). Re-auditing on each pass is a carried rejection.
  - Mixed due/future: classification (`:584-595`) never compares due time with now, and collided names are removed before any submission or arming.
- `false` — BH8: The timing comment contradicts its bounds. The comment says the measured operation interval is bounded first and two seconds are added on top. The accepted range, `[due − completed − 2 s, due − started + 2 s]`, is exactly that.
- `false` — BH11 (part): The test never shows that earlier reminder names left the Scheduler. A submitted callback releases the item only after `TryCancelAsync` succeeds (`:1275-1278`); a failed cancel keeps a `Retrying` witness (`:1281`). Each `ItemState.ShouldBeNull()` after a callback therefore proves the cancellation.
- `low` — BH4, AA2: The no-submit/no-arm proof starts from empty state. Reading the code shows the stored-armed third witness (`:646-651`, `:728-740`) and the stored shared name are both quarantined and cancelled. C6 changed only how `collided` is computed, and that computation does not depend on stored state. `SharedEffectIdentityIsQuarantined` already covers a stored armed entry turning into an `effect-collision`.
- `low` — BH7: The `inputOrder` switch falls back with `_ =>`, and 24 `InlineData` rows are hand-written. Every current row uses 0–5, so no case repeats today. A throwing arm adds a branch for a typing mistake.
- `low` — BH9 (`maybe-false`): The Scheduler may report remaining rather than registered `dueTime`, which would eat the two-second tolerance. The period comes back as the raw `@every` job schedule, which suggests the stored job fields are echoed. Both live runs passed. To settle it, check what `dueTime` returns from Dapr 1.18 Scheduler-backed `GetReminder`. If it is wrong, it would only cause a slow-run test failure.
- `low` — BH10: The duration parser is brittle.
  - Unsupported units and signs fail loudly through the concatenation assertion. Go renders sub-second units only for durations under one second, and these are at least 15 minutes.
  - Inputs like `1s1s` would need the Scheduler to return malformed text.
  - The inline `Regex`, the `KeyNotFoundException` messages, and the duplicated `HttpClient` code are test style only.
- `low` — BH11 (part): Timing is checked only on registration and the reconciler re-arm. That is the scope the C6 patch requested. Registration, restart, reschedule, and reconciler arming all share one delay computation (`ReminderCoordinator.cs:859`).
- `low` — EC1, BH8 (part): The 90-second operation bound can trip when placement retries finish near the 60-second deadline and the final attempt then takes more than 30 seconds. The C6 record sets this bound deliberately. The helper has no per-attempt timeout to derive a tighter bound from, and both live runs passed.
- `low` — EC2: Commit `f6f7fd4a` is titled `feat(tests)` but changes production code. That commit is published in `3.113.0`, and rewriting it is not proportionate. The release version itself was correct.
- `low` — AA4: The new live assertions run before cleanup, which is not in a `finally` block. A failing run can leave a 15-minute periodic reminder in the shared Scheduler. Earlier assertions in the same test have the same exposure, and it only happens when the test has already failed. A `try`/`finally` restructure is more than a direct correction.

### C6 documentation patches (2026-10-05)

The three open review patches above are comment and documentation corrections. Runtime classification is unchanged.

- The caller contract in `docs/guides/typed-reminders.md` and the `IReminderIntentSource` remarks now state that a witness collision on a shared name is recorded as `witness-collision` even when that name also shares an effect identity.
- The convergence comment restores the reason shared effect identities are quarantined: the target rejects a second submission on a semantic-digest conflict, or silently replays the first receipt. It keeps the explanation that grouping runs before name collapse, and it records the same label precedence.
- `OverlappingWitnessAndEffectCollisionsQuarantineEveryIntent` now says the name collision keeps the evidence digests of both same-name intents as quarantine records.

Verification from `references/Hexalith.EventStore`:

| Command | Result |
| --- | --- |
| `timeout 180s dotnet build tests/Hexalith.EventStore.DomainService.Tests/Hexalith.EventStore.DomainService.Tests.csproj -c Debug -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` | Passed; 0 warnings, 0 errors |
| Built DomainService executable with `-class '*ReminderCoordinatorTests' -method '*OverlappingWitnessAndEffectCollisions*' -method '*SharedEffectIdentityIsQuarantined'` | 25/25 passed; 0 failed, 0 skipped |

The full Contracts, Client, and LiveSidecar reminder binaries were not rerun. These edits do not change executable behavior, and the live Scheduler proof still depends on Redis, placement, and Scheduler. Works code, the frozen intent, sealed `docs/ci.md`, and the AD-26 codec remain unchanged. No commit, push, publication, dependency update, or nested-submodule initialization was performed.

### Review Findings

Review iteration 6 (2026-10-05) is a chunked full-story re-review in a fresh LLM context. This section covers group 2, the coordinator core. The diff is EventStore `f378afdb..8f34b39`, restricted to `ReminderCoordinator.cs` and its persisted state, record, key, log, outcome, and exception types: ten files, 1,999 additions, 2,059 diff lines. `dcc6124a`, the committed C6 documentation patch, matches the patch C8 reviewed line for line, and no reminder file changed between `bf9066f` and `8f34b39`. Review mode: full. Context: this spec, Epic 4 context, and architecture AD-11, AD-20 R6, AD-23 to AD-29.

All four layers completed: blind hunter (BH, 14), edge-case hunter (EC, 20), acceptance auditor (AA, 2, no acceptance-criterion violation), and verification gap (VG, 7). Each of the 43 findings was judged before grouping. Result: 0 decision-needed, 12 patch entries (13 findings), 6 carried defers (7 findings, not appended to the ledger again), and 23 rejected. Groups 1 (public API and codec), 3 (runtime plumbing), and 4 (coordinator, admission, and live tests) remain for follow-up runs.

C9-EC4 and C9-EC15 change runtime code. Public `3.113.0` does not contain them, so applying either reopens the named-public-package close gate. The other ten are tests, comments, or documentation.

- [x] [Review][Patch] A crafted callback actor ID aliases a disposition key, and the load repair erases that audit record [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:197`] — medium (C9-EC4).
  - `HandleCallbackAsync` rejects only a blank actor ID. `ReminderStateKeys.Item` appends the ID after `:item:`, so the ID `wra-<digest>:disposition:<subject>` names that actor's disposition key.
  - `LoadAsync` reads the disposition JSON as a `ReminderItemState` with null collections and marks it repaired. The normalized state holds no work, so `PersistAsync` erases the key through its ETag.
  - Reaching the route needs the app-channel token, or the callback-origin bypass deferred to Story 4.16.
  - Fix: return `null` before any store access unless the ID is `wra-` followed by a 52-character digest. The Scheduler fires only the `wra-` IDs this runtime registers.
- [x] [Review][Patch] No failing test covers a quarantined witness whose intent leaves or changes in the stream [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:644`] — medium (C9-VG2).
  - Callback-path quarantines (`translation-failed` and similar) keep only the quarantined entry; no quarantine record is written.
  - No test converges afterwards with the intent removed or changed. Deleting this branch turns the entry obsolete: it is audited `Cancelled`, erased, and its candidate can be released, and the suite stays green.
  - Fix: add `QuarantinedWitnessSurvivesIntentRemoval`.
- [x] [Review][Patch] No failing test covers purpose denial on the convergence path [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1335`] — medium (C9-VG3).
  - `UnconfiguredPurposeIsDeniedAndRetained` is denied at callback step 3 and never reaches `SubmitAsync`. Removing this guard lets registration or reconciliation submit a kind that has no configured purpose.
  - Fix: add `UnconfiguredPurposeOnConvergenceIsDeniedAndRetained`. It asserts no delegation request, no submission, a `Retrying` entry with `purpose-unconfigured`, and a `Denied` audit.
- [x] [Review][Patch] No failing test covers an undefined receipt disposition or a null receipt [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1425`] — medium (C9-VG4).
  - `MismatchedReceiptIsRetained` overrides only `EffectId`.
  - Dropping the `Enum.IsDefined` check would release a witness on a receipt the coordinator cannot interpret. `HttpTrustedEffectSubmitter` deserializes an out-of-range number as an undefined value.
  - Fix: make that test a theory over a mismatched effect ID, an undefined disposition, and a null receipt.
- [x] [Review][Patch] No failing test covers the write-ownership re-read in `PersistAsync` [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1597`] — medium (C9-VG5).
  - The fake stores can interpose only before the compare-and-swap.
  - Dropping the version comparison lets a turn adopt a foreign writer's ETag and then overwrite that writer's state.
  - Fix: add an after-save hook to the test store, and add `ForeignWriteAfterSaveFailsClosed`.
- [x] [Review][Patch] The persisted key layout and enum strings are only round-tripped, never pinned [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderStateKeys.cs:35`] — medium (C9-VG6).
  - Every test writes and reads through `ReminderStateKeys` and the same record types.
  - Removing a `JsonStringEnumConverter`, renaming a status, or changing the key prefix would strand retained state and the runbook keys, and no test would fail.
  - Fix: pin the key literals and the raw JSON of one item and one disposition, and deserialize a fixed fixture.
- [x] [Review][Patch] No test rejects a non-canonical target domain [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:103`] — low (C9-VG1).
  - Only a different aggregate and a blank domain are tested.
  - Without this comparison, `Widget` derives the same actor ID as `widget`, so item state and a candidate are written for the non-canonical domain.
  - Fix: add `NonCanonicalTargetDomainIsRejected`.
- [x] [Review][Patch] Event `200207` is not pinned, although runbook step 1 uses it to find quarantined items [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderLog.cs:39`] — low (C9-VG7).
  - `ReminderDiagnosticsTests` pins `200211`, `200212`, and `200214`, but not `200207`.
  - Fix: add `QuarantineEmitsStructuredEvent`. It asserts the event ID, `ActorId`, `Subject`, and `ReasonCode`, with no tenant or aggregate.
- [x] [Review][Patch] An `OperationCanceledException` from translation that is not a shutdown skips quarantine [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1350`] — low (C9-EC15).
  - Unlike the filters at `:1067` and `:1392`, this filter lets every `OperationCanceledException` escape. Translation takes no token, so the exception cannot come from shutdown.
  - On convergence it aborts the loop before settled entries are persisted. On a callback, the generic catch returns `Retrying` on every firing, so the witness is never quarantined as `translation-failed`.
  - Fix: add `|| !cancellationToken.IsCancellationRequested` to the filter.
- [x] [Review][Patch] The precedence sentence added by `dcc6124a` is wrong for a stored witness [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:587`] — low (C9-EC18).
  - Case: a stored witness's name now carries different evidence, and that name also shares an effect identity. Convergence runs the collided branch (`:649`) before the witness check (`:666`) and records `effect-collision`. A callback on the same state records `witness-collision` (`:1084`).
  - The guide (`docs/guides/typed-reminders.md:109-113`) counts a stored witness with changed evidence as a witness collision, then says witness collisions take precedence. That is wrong for this case.
  - Both paths quarantine and submit nothing. Only the label differs.
  - Fix: in the comment, the guide (`:51-53`, `:111-113`), and the `IReminderIntentSource` remarks, limit the precedence sentence to two current intents that share one name. State that convergence labels a changed stored witness on a shared name as `effect-collision`.
- [x] [Review][Patch] The `ReminderEntryStatus` summaries no longer match how the statuses are used [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderEntryStatus.cs:6`] — low (C9-AA1, C9-BH9).
  - `Pending` is also set after a successful arm whose `Registered` audit failed (`ReminderCoordinator.cs:880-888`).
  - `Retrying` also covers audit and cancellation failures (`:720`, `:746`, `:1142`, `:1182`, `:1284`) and `Denied` outcomes (`:1309`).
  - `Quarantined` also covers `translation-failed`, `translation-invalid`, `effect-identity-invalid`, and `domain-invalid`. A `Denied` admission is retained, not quarantined.
  - Fix: rewrite the three summaries. An earlier review corrected the matching `Attempts` docs.
- [x] [Review][Patch] The audit wording overclaims for quarantine capture [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderDispositionRecord.cs:8`] — low (C9-AA2).
  - The record summary says an acknowledged outcome always has audit evidence, and the comment at `ReminderCoordinator.cs:701` says audit comes before "releasing or recording".
  - In fact, new quarantine records are persisted whatever their audit result (`:756-791`), and `QuarantineActorCollisionAsync` discards its audit result and returns normally (`:954`). That behavior stays (carried chunk A AA3/AA4).
  - Fix: say that release waits for a durable audit, while quarantine capture is persisted first and audited again on every convergence.
- [x] [Review][Defer] A stored item header is never re-derived against its actor ID [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1494`] — deferred: carried R2-BH4 and the iteration-3 chunk A bad-candidate deferral. A corrupt or wrongly restored tenant or aggregate turns the legitimate target into an `actor-collision` and can index a bad candidate. Readiness stays Degraded until Story 4.16 operator repair. (C9-BH4, C9-EC5)
- [x] [Review][Defer] Disposition records are last-write-wins [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1634`] — deferred: carried C6-BH6 (AD-28, Story 4.16). (C9-EC9)
- [x] [Review][Defer] Every convergence audits every quarantine record again [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:756`] — deferred: carried R2-BH1. (C9-EC10)
- [x] [Review][Defer] No timeout bounds source, delegation, or submitter calls inside a turn [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:150`] — deferred: carried R2-BH5 (maybe-false). (C9-EC16)
- [x] [Review][Defer] Callback settlement re-arms without re-ensuring discovery [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1239`] — deferred: carried R1-BH9 (restore model, Story 4.16). Stranding needs an index restored older than the item plus a Scheduler reminder that is also lost. (C9-EC20)
- [x] [Review][Defer] An orphan callback cancels the last pointer after a state restore older than the Scheduler [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/ReminderCoordinator.cs:1017`] — deferred: carried R1-BH9. (C9-BH14)

#### Rejected

- `false` — BH1: A malformed re-read cancels a still-current witness. The stream is authoritative and no longer reports that witness validly. The malformed intent is quarantined by digest, so the candidate stays and readiness is Degraded. Nothing executes, and a corrected source recreates the same deterministic witness.
- `low` — BH2: UTF-8 replacement characters can merge two malformed evidence digests. That needs a domain source emitting two intents that differ only in lone surrogates, and changing `AppendText` would re-key every existing quarantine digest.
- `low` — BH3: The callback catch blocks skip readiness bookkeeping. Carried R2-BH6 and split-group-2 AA2: when the persist fails the durable witness is untouched, and the next pass resubmits within the interval.
- `low` — BH5: A source that throws escapes `ConvergeAsync` while a null fold is handled, and the fail-closed result reports zero actions. Both fail closed: the registrar's caller retries, and the reconciler marks the pass incomplete, so readiness is Degraded. The counters only feed logs.
- `low` — BH6: Obsolete and stale dispositions are written before a cancellation that may then fail. This write-ahead audit is the design: the entry keeps `cancel-failed` and is retried. Transition history is the carried C6-BH6 deferral.
- `low` — BH7: A cancellation failure after a receipt resubmits on the next retry. The target replays the same receipt. A "receipt held, cancellation pending" status would add a new state to save load during a Scheduler outage.
- `false` — BH8: A cancelled callback returns `null`, which looks like an orphan. `ReminderActor.ReceiveReminderAsync` discards the result and passes `CancellationToken.None`, so no production caller reaches that branch or reads the value.
- `false` — BH10: `ComputeActorId` in the submission catch can throw. Every submitted entry passed `Rederives` against this actor, or its state was built from a target that derived it. The extra hash runs only on failure.
- `false` — BH11: Duplicate checks. None of them causes harm. The `SubmitAsync` purpose check guards the convergence path (C9-VG3). `EndsWith` is documented defense in depth. AD-26 golden vectors freeze the digest alphabet.
- `low` — BH12: `ConvergeCoreAsync` is large. No defect is named, and a refactor is not a direct correction.
- `low` — BH13: A target `Rejection` is logged at Information level. A domain rejection is an expected outcome and appears in the logged disposition field. A new Warning event would be new operator surface.
- `low` — EC1: A stored JSON `null` with an ETag drops the ETag, so later writes conflict. This needs corrupt state. It fails closed with Degraded readiness, like the carried rejection for an undecodable document.
- `low` — EC2: An undecodable item document blocks its item. Carried chunk 1 and chunk A EC2: quarantining the raw bytes needs a store seam.
- `low` — EC3: Null collections are normalized without a quarantine record. Carried C6-BH3 and split-group-2 BH6.
- `low` — EC6: A valid entry shares its name with a `stored-entry-invalid` record. Carried split-group-2 EC14.
- `low` — EC7: An `UpdatedAt` near `MaxValue` overflows the backoff, or a future one stretches it. Carried chunk 1 and chunk A EC6.
- `low` — EC8: `Attempts` overflows at `int.MaxValue`. Carried R1-EC7 and C6-EC4.
- `low` — EC11: A failed `RemoveCandidateAsync` after release reports `Retrying`. Carried chunk A BH8/EC9 and split-group-2 EC5.
- `false` — EC12: A null reload after stale retirement leaves readiness stale. `ConvergeCoreAsync` keeps the stale witness through `retainedReminderNames`, so inside the serialized turn the reload always returns state.
- `low` — EC13: The callback does not check the due instant. Carried several times: the Scheduler owns timing.
- `low` — EC14: A callback inside the backoff window waits a full `RetryMaxDelay`. Carried R2-EC3 and split-group-2 EC11.
- `false` — EC17: A superseded write is reported as `state-conflict`. The re-read proves ownership before the turn chains another write. A version from another writer is not this turn's to build on, and the convergence catch then judges the reloaded durable state.
- `low` — EC19: After later passes, an older `effect-collision` digest keeps its first reason. The label depends on history, but the evidence is never dropped. Rewriting the reason of a deduplicated record would add update logic for a cosmetic label.


### C9 coordinator patches implemented and verified (2026-10-05)

The complete spec and both frontmatter context files were read before work in
EventStore, starting from clean source commit
`8f34b395d2b05b068ed15635811e5d4adbbf5dc5`. All twelve C9 patch entries above
are implemented. The frozen intent, baseline identifiers, and deferred work
are unchanged. This completes the filed group-2 patches; the remaining full-story
review groups are still open.

- Callback admission now refuses actor IDs unless they are `wra-` plus exactly
  52 uppercase Crockford digest characters, before any store read. Six cases
  cover a disposition-key alias, short/long IDs, lowercase, an excluded letter,
  and a wrong prefix, asserting the legitimate audit record and ETag survive.
- Translation `OperationCanceledException` is quarantined as `translation-failed`
  when the supplied token is not cancelled, on convergence and callback paths.
  All eight runtime regression cases failed before these two fixes.
- Persisted-state regressions cover quarantine retention after stream removal
  or changed evidence, purpose denial during convergence, invalid/null receipts,
  a foreign write between accepted CAS and ownership re-read, and non-canonical
  target domains. Event 200207 is pinned through emitted structured output.
- A new `ReminderPersistenceContractTests` class pins every version-one key,
  fixed item/disposition JSON fixtures, tolerant deserialization, and all stored
  lifecycle/disposition enum names. The test-store wrapper can observe a write
  after it commits; the submitter fake can now return a null receipt deliberately.
- The guide and intent-source remarks limit witness-collision precedence to
  two current intents under one name and state the stored-witness convergence
  exception. Status summaries and quarantine-audit wording describe the retained
  state without changing classification or audit behavior.

Durable XML, exact commands, environment blockers, protected-file hashes, and
an executed matrix audit are in
[the C9 verification summary](evidence/story-4-11-c9-2026-10-05/summary.json).
Additional build/test/pack logs and the synthetic local packages are under
`/tmp/story-4-11-c9-implementation/`. Commands ran from
`references/Hexalith.EventStore`.

| Check | Result |
| --- | --- |
| `timeout 180s dotnet build tests/<Project>/<Project>.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` for Contracts, Client, DomainService, and LiveSidecar | All four passed; zero warnings/errors |
| Built DomainService executable with `-class '*Reminder*'` | 211/211 passed; no skips/errors |
| Built Contracts executable with both reminder and effect codec class filters | 50/50 passed; no skips/errors |
| Full built Client executable | 917/917 passed; no skips/errors |
| Canonical `tools/pack-release-packages.py` and `tools/validate-release-packages.py`, version `3.113.0-local.491` | All 14 packages packed and validated; no packaging fallback required |
| `EVENTSTORE_PACKAGE_CONTRACT_DIR=/tmp/story-4-11-c9-implementation/packages` with Contracts `-class '*Reminder*'` | 34/34 passed; includes the R6 package-only proof and all three isolated consumers |
| LiveSidecar `-class '*ReminderRecoveryLiveSidecarTests'`, cached Alpine image and Docker host networking | 1/1 passed; persisted registration, restart receipt replay, same-source reschedule, deleted-Scheduler recovery, due time, and repeat period proved |
| Executed frozen matrix audit | All five rows have passing persisted-state coverage |
| `git diff --check` in EventStore and Works | Passed |

The native live command failed before its test body: Dapr's Redis connection to
`localhost:6379` returned EOF. The successful repeat used the same cached Alpine
image, read-only `daprd` 1.18.4, existing backing containers, and host-networking
workaround already recorded by C6. No service or host configuration changed.

The full DomainService executable returned exit 1: 397 passed and one failed,
`TenantsDomainService_DoesNotReferenceGeneratedApiHostOrDeclarePerMessageControllers`,
whose nested Tenants source is intentionally absent. The pre-change command
`aspire start --isolated --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json`
returned exit 2 on the same absent nested Tenants host projects. `aspire describe`
and `aspire stop` confirmed no running AppHost. The known unrelated Contracts
packaging/governance failures were not rerun; their subjects did not change.
No nested submodule was initialized and no validation gate was weakened.

All four protected files match this session's starting EventStore HEAD. The
sealed `docs/ci.md` already differs from the original `f378afdb` story baseline;
this continuation leaves its current bytes untouched. Effect codec, catalog,
and golden-vector source still match that original baseline. Works source is
unchanged. No staging, commit, push, branch change, dependency update, or
publication was performed.

**The corrected public-release gate is open again.** Public `3.113.0` predates
C9-EC4 and C9-EC15. The owner must publish the reviewed corrections as a named
public release, then the R6 package-only proof and runtime regressions must pass
against that release with its immutable source SHA recorded. The synthetic local
pack includes uncommitted C9 code and does not close that gate. Story status
remains `in-progress`.


### C10 review corrections and final verification (2026-10-05)

All six direct C10 patches are implemented. Persistence fixtures now resolve
serializer options from the domain host's configured Dapr client and pin the
actual camelCase item, disposition, tenant registry, and candidate documents.
The wrong-prefix vector keeps a valid actor-ID length and digest. The audit
summary distinguishes independent quarantine-record evidence from audit-gated
entry transitions. Sprint comments and exact build timeout metadata are corrected.
No executable reminder behavior changed after the C9 fixes.

All four test projects rebuilt in Debug with sibling sources, zero warnings,
and zero errors. Final parent-agent checks passed: DomainService reminders
212/212; packaged Contracts reminders 34/34; effect codec 17/17; Client full
917/917; live recovery 1/1 with the documented container fallback. The packaged
Contracts run includes 33 reminder codec cases and the package-only proof, so
both codec suites remain 50/50. Every frozen matrix row was checked against the
final executed XML. All three review layers completed; six direct corrections
were applied, four findings rejected or carried as rejections, and no new work
was deferred. The existing nested-Tenants/Aspire blocker remains documented.

[Final C10 commands, build logs, test XML, and matrix audit](evidence/story-4-11-c10-2026-10-05/summary.json)
bind this verification to EventStore source commit
`7e19e60b4510551dc3285c567d70c54e92f23096`. That local Conventional Commit passed explicit commitlint
validation and the repository commit hook. The baseline identifiers and frozen
intent are unchanged. The existing fourteen-package local inventory was reused
because C10 changes only tests, comments, and metadata; it is not a public-release
closure proof.

The workflow's generic done transition is withheld under the frozen release
decision: a named owner-published release containing the C9 runtime fixes still
needs package-only and runtime regression proof. Spec and sprint status remain
`in-progress`. Current full-story re-review groups 1, 3, and 4 remain as previously
recorded. No publication, push, dependency update, branch change, or nested
submodule initialization was performed.


### Continuation review C11 patches (2026-10-05)

- [x] [Review][Patch] Record retry timestamps after failed awaited operations, with persisted-state convergence/callback regressions. (C11-BH2, C11-EC2)
- [x] [Review][Patch] Recompute Scheduler delays after lookup, including deadlines crossed during lookup. (C11-BH3)
- [x] [Review][Patch] Persist malformed callback-fold evidence before releasing a valid witness. (C11-BH4)
- [x] [Review][Patch] Use logical-effect causation in the package-only Client consumer and validate it. (C11-BH5)
- [x] [Review][Patch] Correct public disposition summaries for write-ahead audit and cleanup retries. (C11-BH6)
- [x] [Review][Patch] Correct public convergence count summaries without changing the record shape. (C11-BH7)
- [x] [Review][Patch] Correct the host-delay limit comment without changing options validation. (C11-BH8)
- [x] [Review][Patch] Keep malformed discovery identifiers out of diagnostics while retaining discovery evidence. (C11-BH10, C11-EC7)
- [x] [Review][Patch] Repair unknown persisted quarantine reasons through existing digest quarantine and keep arbitrary text out of diagnostics. (C11-EC8)


### C11 final verification and reviewable local commit (2026-10-05)

All nine C11 patch groups are implemented and verified. The current full-story
review scopes 1, 3, and 4 are complete through the workflow's three independent
layers and the parent acceptance/matrix audit. Every one of 23 findings has its
own triage row; two existing shared-SDK issues were appended to deferred work,
and ten prior rejections were carried. Existing Story 4.16 deferrals remain.

Retry timestamps now follow failed operations. Lost future/backoff reminders
use the clock after Scheduler lookup and clamp elapsed delays to zero. Callbacks
persist malformed sibling evidence before valid submission or witness release.
Diagnostics retain discovery evidence while logging only derived digests or a
bounded placeholder; unknown stored quarantine reasons use the existing digest
repair. The package probe binds logical-effect causation, and public summaries
describe write-ahead audit and actual count semantics. No public API shape, enum
value, codec, catalog kind, option validation, or persisted key changed.

All 14 new regression cases failed before runtime corrections. Final parent
verification passed 226/226 reminder tests, 51/51 reminder/effect codec and local
package tests, 917/917 Client tests, 14/14 regressions against fresh packaged SDK
assemblies, and 1/1 live Redis/Dapr recovery using the documented container
fallback. All four required Debug/source-reference test projects built without
warnings or errors. Canonical packing and validation accepted all 14 fresh
`3.113.0-local.511` packages; all three isolated package-only consumers passed.
Every frozen matrix row has executed passing persisted-state coverage.

The full DomainService executable returned exit 1 with 412 passed and the one
known `TenantsDomainService_DoesNotReferenceGeneratedApiHostOrDeclarePerMessageControllers`
failure, whose nested source is absent. The pre-change Aspire command returned
exit 2 for the same missing nested Tenants host projects; describe and stop
confirmed no running AppHost. Native live recovery failed before its test body
on Redis EOF; the cached Alpine/host-networking repeat passed. No nested
submodule was initialized and no validation gate was weakened.

[Commands, build logs, test XML, package hashes, source audit, and matrix proof](evidence/story-4-11-c11-2026-10-05/summary.json)
bind the reviewed source bytes to local EventStore commit
`7450da14b18d8ae9374b1cacde9f39765bcb28d9`. Explicit commitlint and the normal commit hook passed.
The local packages were packed before that commit; their nuspec source metadata
identifies the precommit base, and the source audit records the final bytes.
The frozen intent and both original baseline identifiers are unchanged.
Protected files match this continuation's starting HEAD; effect codec, catalog,
and golden-vector source also match the original EventStore baseline. Works
implementation source is unchanged.

**The owner public-release gate remains open.** The owner must publish the
reviewed C9/C11 runtime corrections as a named public release, then the package-only
proof and runtime regressions must pass against that release with its immutable
source SHA recorded. Synthetic local proof does not close that gate. The
workflow's generic done transition is withheld under the frozen release decision;
spec and sprint status remain `in-progress`. The local implementation commit
makes the changes reviewable. No push, publication, dependency update, branch
change, or nested-submodule initialization was performed.

### Public 3.117.1 corrected-release proof (2026-10-08)

The complete spec and both frontmatter context files were loaded before this
continuation. Every implementation task and filed patch is already present in
the clean EventStore checkout at
`07d1e23a6c5b06bbbb1fc8ddb5174cc3382d3d93`; no implementation change was needed.

All 14 owner-published EventStore `3.117.1` archives were downloaded from the
official NuGet flat-container endpoints. The canonical release validator
accepted the complete inventory. Every nuspec identifies source commit
`0dad344d37343f589d859d6d8d6701283122b338`. Its Contracts and Client reminder
directories, coordinator, and reconciler are byte-identical to the reviewed
C11 source at `7450da14b18d8ae9374b1cacde9f39765bcb28d9`. This proof uses exact
source equality; it does not claim the C11 commit is an ancestor of the release.

`PackagedReminderApiRunsWithoutWorksTypes` passed 1/1 with zero skips against
these public packages, exercising all three isolated package-only consumers.
The current DomainService reminder test host also passed 227/227 with zero
skips after its Contracts, Client, DomainService, ServiceDefaults, Server, and
Testing assemblies were replaced with the public package assemblies. The
executed XML includes all 24 C6 collision cases, eight C9 admission/translation
cases, and fourteen C11 regressions. The binding artifact records each public
archive, extracted assembly, test-host assembly, test assembly, and result XML
hash; the SDK assembly hashes were checked again after the run.

Fresh source checks also passed: 227/227 DomainService reminder tests, 50/50
reminder/effect codec tests, and 1,498/1,498 Client tests, with no errors or
skips. All four required test projects built in Debug with sibling sources,
zero warnings, and zero errors. Every frozen matrix row has passing executed
persisted-state coverage against the public runtime.

[Durable commands, package metadata and hashes, assembly bindings, XML, source audit, and matrix proof](evidence/story-4-11-public-3.117.1-2026-10-08/summary.json)
record this named-public-release acceptance proof. **The corrected public
release gate is satisfied**, superseding the dated open-gate observation above.
Workflow status is preserved for the final review.

The retained C11 Redis/Dapr live proof remains valid: its actor and test source
are unchanged. It was not rerun for this documentation-only continuation.
Historical broad-suite and Aspire environment limitations remain separately
recorded; no gate was weakened. Works adoption remains Story 4.15, and the
production delegation, callback-origin/ACL, audit/retention, actor-health,
operator-repair, and restore gates remain Story 4.16 work.

Works source and its `3.114.0` package pin, the frozen intent, both baseline
identifiers, sealed `docs/ci.md`, and the effect codec/catalog/vector source are
unchanged. No staging, commit, push, branch change, dependency update,
submodule initialization, or publication was performed.

### C12 final review and release closure (2026-10-08)

All three independent review layers completed. Each of fourteen findings has
an individual C12 triage row: six evidence/metadata corrections are resolved,
two pre-existing Epic 4 context-refresh issues are deferred, and six findings
retain their prior routes or are rejected. No intent or implementation gap
remains in this story. Reminder behavior and Works source are unchanged.

The parent executed the retained replay recipe from a fresh temporary directory.
All fourteen public archives reproduced their retained hashes and source SHA;
the three isolated package consumers and all 227 reminder checks passed again,
with zero errors, failures, or skips. Every frozen matrix row and all C6/C9/C11
regression cases passed in that fresh execution. The test assembly, dependency
manifest, six public SDK assemblies, and retained source-built dependency bytes
also matched the original fixture. The evidence explicitly distinguishes that
source-based regression fixture from the independently restored package-only
consumer graph.

[Final parent replay, review dispositions, commands, logs, and XML](evidence/story-4-11-public-3.117.1-2026-10-08/parent-replay-summary.json)
complete the named-public `3.117.1` close gate. The spec is `done`; sprint status
is `review`, the bmad-build handoff for human review. The final workflow step
records the documentation and evidence in a validated local Conventional Commit.
No push, publication, dependency update, branch change, or nested-submodule
initialization accompanies this closure. Previously recorded broad-suite
environment limitations and the Story 4.16 production gates remain separately
documented.

### Review Findings

Delta review C13 (2026-10-08) is a fresh-context review. It covers the reminder bytes in public `3.117.1` that no earlier 4.11 layer had seen. The diff is EventStore `7450da14..07d1e23a` over four files:
- `EventStoreReminderEndpointExtensions.cs`
- `docs/guides/typed-reminders.md`
- `EventStoreReminderCompositionTests.cs`
- `ReminderDiagnosticsTests.cs`

That is 174 lines, identical to `7450da14..0dad344d`, and it comes from EventStore Story 5.5 `c4d5455a` and CI fix `0de100da`. Review mode: full.

All four layers completed:
- blind hunter (BH): 12 findings;
- acceptance auditor (AA): 5;
- verification gap (VG): no gaps, 3 other findings;
- edge-case hunter (EC): 8.

Each of the 28 findings was judged before grouping. Result: 0 decision-needed, 8 patch entries (19 findings), 0 defer, 9 rejected. The triage log carries every C13 row.

Only one runtime change is in scope: SDK-mapped actor handlers now require `SidecarChannel`, and the anonymous metadata on `/healthz` is removed. Checked against Dapr `release-1.18` source, `GET /dapr/config` and actor calls carry `dapr-api-token`, so actor registration and callbacks are unaffected. None of the eight patches changes runtime code. They are guide, XML-doc, and test corrections, so the named-public `3.117.1` close gate stays satisfied, as with the C6 documentation patches.

- [x] [Review][Patch] The credentials subsection captures the `EventStore:Reminders` options table [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:269`] — low (C13-BH1).
  - The options table, its validation paragraph, and the host `APP_API_TOKEN` paragraph now sit under `### Trusted-effect submission credentials`.
  - Fix: move the subsection after the `APP_API_TOKEN` paragraph (`:319-322`), or add an `### Options` heading before the table.
- [x] [Review][Patch] The credentials section restates Story 5.5 requirements only partially and links to neither authoritative reference [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:277`] — low (C13-BH4, C13-BH5, C13-BH6, C13-BH7, C13-BH8, C13-EC6, C13-EC7).
  - The symmetric `Workload` must equal the submitter's Dapr app ID, or the gateway denies it as `caller-conflict`.
  - Authority mode needs the audience and operation scopes declared, each with one mapper.
  - A changed `Authentication:DaprInternal:Audience` needs `AddEventStoreTrustedEffectWorkloadAssertion(gatewayAudience)` and the matching `eventstore-audience.<audience>` scope.
  - Once `AllowedCallers` is non-empty, EventStore itself needs `APP_API_TOKEN`, and the submitter needs the `Authentication:JwtBearer` contract.
  - Step 3 should name `Authentication:WorkloadIssuer:LifetimeSeconds` (default 120, capped at 300) as the value that must not exceed the receiver's `MaximumLifetimeSeconds`.
  - Fix: add these clauses and link `security-model.md` (workload assertions, breaking upgrade step) and the `configuration-reference.md` internal-caller rows.
- [x] [Review][Patch] The guide does not say Dapr app health checks must target `/alive` [`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreReminderEndpointExtensions.cs:32`] — low (C13-EC2).
  - Dapr 1.18's `HealthProbe` sends no `dapr-api-token`. `/healthz` on a reminder host now requires `SidecarChannel`, so an app health check on Dapr's default path returns `401` and marks the host unhealthy.
  - The in-repo Aspire wiring already uses `/alive`.
  - Fix: state the requirement in the host-composition section (`typed-reminders.md:259`), with `dapr.io/app-health-check-path: /alive` on Kubernetes.
- [x] [Review][Patch] The readiness runbook describes a running host that startup validation now prevents [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:353`] — low (C13-BH10, C13-AA4).
  - With `AddEventStoreDomainService()`, a missing `APP_API_TOKEN` outside Development fails startup in `EventStoreDomainServiceSecurityStartupValidator` before readiness is ever reported.
  - Fix: say startup fails, and keep the `Unhealthy` check only as defense in depth. Correct the same sentence in `configuration-reference.md:680`.
- [x] [Review][Patch] The pre-mapped composition test still models the bare `MapActorsHandlers()` host the guide now rejects [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/EventStoreReminderCompositionTests.cs:178`] — low (C13-BH2, C13-AA5, C13-VG-O2, C13-VG-O3).
  - Fix: pre-map with `MapActorsHandlers().RequireEventStoreSidecarChannel()`, keep the single-route assertion, and assert `EventStoreDomainServiceEndpointInventory.Validate(...)` is empty under the registered fallback policy. That covers the supported self-mapping shape together with `AddEventStoreReminders`.
- [x] [Review][Patch] The public XML docs still prescribe plain `MapActorsHandlers` [`references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Reminders/IReminderRegistrar.cs:14`] — low (C13-VG-O1, C13-BH11).
  - The `IReminderRegistrar` remarks say to call `MapActorsHandlers` before `UseEventStoreDomainService`.
  - The `MapEventStoreReminders` summary (`EventStoreReminderEndpointExtensions.cs:14`) mentions only the token filter.
  - Fix: both must name `.RequireEventStoreSidecarChannel()` for self-mapped handlers. This is a doc-comment-only change.
- [x] [Review][Patch] The Story 4.15 handoff omits the new host requirements [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:480`] — low (C13-AA3).
  - Fix: add three bullets.
    - Remove the host's own `MapActorsHandlers()`, or apply `.RequireEventStoreSidecarChannel()` and map before `UseEventStoreDomainService`.
    - Attach `.AddEventStoreTrustedEffectWorkloadAssertion()` to the submitter client.
    - Allow-list the Works workload on EventStore.
  - Works `WorksHost.cs:130-133` maps bare handlers after `UseEventStoreDomainService`, so its startup inventory fails as soon as Works consumes ≥3.117.
- [x] [Review][Patch] The runbook gives no way to tell a credential `401` from a transient submission failure [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:404`] — low (C13-BH9).
  - Both surface as `Retrying`/`submission-uncertain` with `HttpRequestException`.
  - Fix: add a runbook sentence. If `submission-uncertain` persists with `HttpRequestException`, check the gateway's event `5501` reason code and the assertion handler, `AllowedCallers`, and lifetime provisioning.

#### Rejected

- `false` — C13-BH3 and C13-EC4: The new route test's filter can pass vacuously. Its `Validate` assertion flags any anonymous non-probe route and any sidecar-originated route without `SidecarChannel`. The fallback it passes is the exact policy the SDK installs.
- `false` — C13-BH11 (terminology part): "App-channel filter" and "sidecar-channel policy" name two distinct real layers, and the test name matches the handler's documented role.
- `low` — C13-BH12: Leak assertions catch only full strings, and the fixture now says "evidence". The rename kept assertion strength. Reminder logs emit only digests, codes, and placeholders, and the repair under test ignores the stored wording.
- `false` — C13-AA1: No live proof of the SDK-mapped path, so actors might never register. Dapr 1.18 sends `/dapr/config` and actor calls through `constructRequest` with `dapr-api-token`, and runs no actor `/healthz` prober. The real-host live composition is 4.15/4.16 parity work.
- `low` — C13-AA2: The 3.117.1 closure audit omits the changed runtime file. The fix would edit this spec's closure record, and this C13 entry now records the delta review.
- `low` — C13-EC1: A host that maps reminders without `AddEventStoreDomainService` lacks the policy. Every documented composition registers it. The failure is loud, and a guard adds branching for an undocumented host.
- `low` — C13-EC3: A host that never maps the canonical endpoints skips the inventory. The composition is undocumented, a misconfigured route fails loudly on the any-workload fallback, and a guard adds branching.
- `false` — C13-EC5: The no-leak assertions skip exceptions. No `ReminderLog` method accepts an `Exception`.
- `false` — C13-EC8: "Fails startup" is overbroad. The sentence is scoped to `UseEventStoreDomainService` hosts, whose captured builder is validated at `StartingAsync` across every data source.

### C13 patches implemented (2026-10-08)

All eight C13 patches are implemented as uncommitted changes in `references/Hexalith.EventStore`. They change only the guide, two XML doc comments, and one test. No runtime behavior, public API shape, codec, option, or persisted key changed, so the named-public `3.117.1` close gate stays satisfied.

- `docs/guides/typed-reminders.md`:
  - an `### Options` heading now precedes the options table;
  - the credentials section names the `caller-conflict` identity rule, the authority-mode scopes with one mapper each, the non-default gateway audience, `Authentication:WorkloadIssuer:LifetimeSeconds`, and the `APP_API_TOKEN` and `Authentication:JwtBearer` prerequisites, and links `security-model.md` and `configuration-reference.md`;
  - the host-composition section requires app health checks on `/alive`;
  - the readiness runbook says startup fails without `APP_API_TOKEN`, keeping the `Unhealthy` check as defense in depth;
  - the Story 4.15 handoff gains the three host requirements;
  - the runbook explains how to tell a credential `401` from a transient submission failure.
- `docs/guides/configuration-reference.md`: the `APP_API_TOKEN` row matches the startup-failure wording.
- `IReminderRegistrar` and `MapEventStoreReminders` XML docs name `.RequireEventStoreSidecarChannel()`.
- `PreMappedActorHandlersAreNotDuplicated` pre-maps with `MapActorsHandlers().RequireEventStoreSidecarChannel()`, keeps the single-route assertion, and asserts the endpoint inventory is empty under the any-workload fallback policy.

Verification (from `references/Hexalith.EventStore`): `dotnet build tests/Hexalith.EventStore.DomainService.Tests/Hexalith.EventStore.DomainService.Tests.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` passed with 0 warnings and 0 errors. The built executable with `-class '*Reminder*'` passed 227/227 with no skips. `git diff --check` is clean. The live-sidecar and package-only proofs were not rerun because no runtime or packaged-API bytes changed. Nothing was staged, committed, pushed, or published.

### C14 delta review and patch (2026-10-08)

Delta review C14 covered the eight C13 patches. Fifteen findings were judged. One patch group was filed: the Story 4.15 handoff bullet now names `MapEventStoreReminders()` as the replacement for the host's own `MapActorsHandlers()`, and cites `WorksHost` symbols instead of a line range. The other findings were rejected as false or cosmetic; each has a C14 triage row. No intent or implementation gap remains.

Verification: the DomainService test project built in Debug with zero warnings and errors. The built xUnit v3 assembly with `-class '*Reminder*'` passed 227/227 with zero skips. The final patch is docs only, so that result stands. `git diff --check` is clean in EventStore. The public `3.117.1` close gate is unaffected: no runtime, API, codec, option, or persisted-key change.

The C13 and C14 changes remain uncommitted in `references/Hexalith.EventStore`. No staging, push, branch change, dependency update, or nested-submodule initialization was performed.

### Review Findings

Delta review C15 (2026-10-09) is a fresh-context review of the committed C13 and C14 patches: EventStore `07d1e23a..29e8e270`, five files, 184 diff lines. Review mode: full. All four layers completed: blind hunter 11, edge-case hunter 12, acceptance auditor 9 plus one informational, and verification gap with no gaps. Each of the 32 findings was judged before grouping. Result: 1 decision-needed (resolved by the owner), 10 patch entries (19 findings), 3 defer, 19 rejected. The reviewed bytes now live on EventStore `main` as `c4dff081`, identical to `29e8e270` for all five files. The triage log carries every C15 row. None of the entries changes reminder runtime behavior, so the named-public `3.117.1` close gate is unaffected.

- [x] [Review][Decision] Pushed Works `main` pins an EventStore commit missing from its remote — medium (C15-AA2). Works `origin/main` `aae6b9b` pinned EventStore `29e8e270`, but EventStore `origin/main` was still `07d1e23a`, so a fresh clone or CI submodule checkout could not resolve `references/Hexalith.EventStore`. Resolved by the owner on 2026-10-09: the patch was rebased onto the Story 6.6 commits and pushed as EventStore `c4dff081` (same patch-id as `29e8e270`; the five reviewed files are byte-identical), and Works `9fc21f5` repoints the gitlink to it. No commit was created or pushed by this review.
- [x] [Review][Patch] The Story 4.15 handoff names only one of three `WorksHost` inventory failures [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:523`] — medium (C15-BH2a, C15-AA1).
  - The startup inventory also rejects `WorksHost`'s own `MapPost("/project")`, which lacks `.RequireEventStoreDomainServicePolicy("/project")`, and its `MapWorksDomainEvents()` topic route, which lacks `.RequireEventStoreSidecarChannel()`. The C15-BH1b run shows all three.
  - Fix: add a sentence to the bullet naming both routes and their policy calls, and note that sibling-source Debug builds already consume the inventory.
- [x] [Review][Patch] The guide names the wrong denial reason for a `Workload`/app-ID mismatch [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:294`] — low (C15-BH3a, C15-EC4, C15-AA6).
  - The allow-list is checked first, so a differing `Workload` is denied as `caller-not-allowed`; `caller-conflict` appears only when that value is itself allow-listed.
  - Fix: name both reasons in that order.
- [x] [Review][Patch] `EventStore:Reminders:Workload` must also equal the assertion caller [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:328`] — low (C15-BH3b).
  - The gateway takes the workload from the assertion, and the delegation's `workload` claim, requested with `EventStore:Reminders:Workload`, must equal it or the call receives `403`.
  - Fix: say so in the options row and in step 2.
- [x] [Review][Patch] `### Options` now holds the host `APP_API_TOKEN` paragraph [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:345`] — low (C15-BH9, C15-AA7).
  - Fix: move the paragraph into the host-composition section before `### Trusted-effect submission credentials`.
- [x] [Review][Patch] The `APP_API_TOKEN` row contradicts itself [`references/Hexalith.EventStore/docs/guides/configuration-reference.md:680`] — low (C15-BH10, C15-AA5).
  - Fix: scope the startup-failure sentence to domain services composed with `AddEventStoreDomainService()` and keep the reminder readiness check as defense in depth for another composition.
- [x] [Review][Patch] The readiness runbook drops "outside Development" for `Unhealthy` [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:380`] — low (C15-AA4).
  - In Development a missing token never makes the check `Unhealthy` (`ReminderCallbackTokenFilter.IsConfigured`).
  - Fix: restore the qualifier.
- [x] [Review][Patch] The runbook's `submission-uncertain` sentence omits admission `403` denials and timeouts [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:458`] — low (C15-BH6b, C15-EC7).
  - A delegation or admission `403` also becomes `HttpRequestException`, and an `HttpClient` timeout logs `TaskCanceledException`.
  - Fix: name both, and point `403` at the delegation provider and admission policy.
- [x] [Review][Patch] The JwtBearer requirement reads as conditional on `AllowedCallers` [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:310`] — low (C15-BH7a).
  - Every SDK domain service needs `APP_API_TOKEN` and the JwtBearer contract outside Development; only EventStore's own token requirement depends on `AllowedCallers`.
  - Fix: split the sentence.
- [x] [Review][Patch] The scope names assume the default prefixes [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:298`] — low (C15-EC6).
  - Fix: mark the scope names as the default `AudienceScopePrefix` and `OperationScopePrefix` forms, beside the existing outbound-settings link.
- [x] [Review][Patch] The pre-mapped test validates against a rebuilt fallback instead of the registered one [`references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/EventStoreReminderCompositionTests.cs:189`] — low (C15-EC11, C15-AA8).
  - Fix: pass `app.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy`.
- [x] [Review][Defer] Works' sibling-source Debug host already fails the EventStore startup inventory [`src/Hexalith.Works/Runtime/WorksHost.cs:111`] — deferred: pre-existing since Works `dcd90e7` advanced EventStore to Story 5.5 (`07d1e23a`); high for Works developers (C15-BH1b). With `xUnit1051` demoted (the Debug integration-test build otherwise fails on 19 analyzer errors), `WorksHostExposesOneDeliveryRouteAndOneDiscoveryRoute` fails on `/project`, `/work/events`, `/dapr/config`, and `/actors/*`. Owner: the Works host adoption stories (4.14/4.15), or an earlier Works host fix.
- [x] [Review][Defer] The Kubernetes sample domain service lacks token configuration [`references/Hexalith.EventStore/samples/deploy/kubernetes/dapr-annotations-example.yaml:64`] — deferred: pre-existing EventStore Story 5.5 sample gap, outside Story 4.11 (C15-BH5).
- [x] [Review][Defer] Non-reminder domain services also need Dapr app health on `/alive` [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:269`] — deferred: pre-existing general domain-service deployment guidance, outside the reminder guide's scope (C15-EC1).

#### Rejected

- `false` — C15-BH1a: 3.116.0 was never published to nuget.org, so package consumers first meet the inventory at 3.117.
- `false` — C15-BH2b: The 4.15 bullets register reminders, so `MapEventStoreReminders()` does not throw, and the bullet already says activation calls it.
- `false` — C15-BH4a: Dapr app health checks are off by default; the sentence governs where an enabled check points.
- `false` — C15-BH4b, C15-EC2: Carried C14-BH4; the instruction is generic, and the annotation and Aspire wiring are examples.
- `low` — C15-BH4c: The guidance is unconditional, so a Development caveat changes no behavior.
- `low` — C15-BH6a: Carried C14-BH6/C14-EC6; gateway event `5501` and the linked security model suffice.
- `false` — C15-BH7b: Step 2 already names "symmetric `Development` mode".
- `low` — C15-BH8: Carried C14-BH5; an authority-issued lifetime is the identity provider's setting.
- `false` — C15-BH11a, C15-EC12: Carried C14-BH1/C14-EC3/C14-EC4; bare mapping is covered at `EventStoreDomainServiceExtensionsTests.cs:1114`.
- `false` — C15-BH11b: The test still asserts a single reminder route, as its name says.
- `low` — C15-EC3: The lower-level Aspire API takes the caller's path, the high-level one sets `/alive`, and a defaulting branch adds runtime surface.
- `low` — C15-EC5: The guide states the identity invariant; a startup check adds runtime branching.
- `low` — C15-EC8: Carried C14-EC4; every surface requires mapping before activation, and duplicate detection is a runtime change.
- `false` — C15-EC9: Carried C13-EC1/C13-EC8; every documented composition registers the domain service.
- `low` — C15-EC10: Carried C13-EC1; undocumented composition, loud failure, and a guard adds branching.
- `low` — C15-AA3: Stale IntelliSense in the published 3.117.1 package is inherent to a post-release doc fix; correcting the closure wording would edit this spec.
- `false` — C15-AA9: AD-20 R9 binds the final Story 4.9 executable, not the transitional 4.15 host.

### C16 completion (2026-10-09)

The ten C15 action items and five C16 patch groups are complete in EventStore commit `8d033bd82bda13fc8593f6c2788635b7b6a58046`. C16 reviewed the current three-file documentation/test delta: eight findings were triaged individually, six finding rows produced five small patches, and two spec-only findings were rejected under the review route. No new work was deferred; the three C15 deferrals remain assigned to their recorded owners. No runtime or package API changed, so the named-public `3.117.1` package proof remains valid.

Verification: all four EventStore test projects built in Debug with zero warnings and errors. The DomainService reminder suite passed 227/227; the Contracts reminder filter passed 33 tests with its package-only probe skipped because no package directory was supplied; the Client reminder filter matched zero tests. The live reminder test passed 1/1 against Redis and Dapr with `EVENTSTORE_TEST_DAPR_HOT_RELOAD=false`, proving callback, restart replay, and rearm. Its first run without that fixture override failed when Dapr could not create a file watcher (`no space left on device`). The earlier public-package consumer proof and five-row matrix audit remain recorded in `evidence/story-4-11-public-3.117.1-2026-10-08/`.

### Review Findings

Delta review C17 (2026-10-09) is a fresh-context review of the committed C15 action items and C16 patches: EventStore `c4dff081..8d033bd8`, three files, 181 diff lines. Review mode: full. All four layers completed: blind hunter 10, edge-case hunter 7, acceptance auditor 4, and verification gap with no gaps. Each of the 21 findings was judged before grouping (24 rows with parts). Result: 1 decision-needed, 5 patch entries (8 finding rows), 0 defer, 15 rejected. The triage log carries every C17 row. No entry changes reminder runtime behavior, so the named-public `3.117.1` close gate is unaffected.

- [x] [Review][Decision] Works `b4df05e` pins an EventStore commit that is not on its remote — medium (C17-AA1). Works `main` is one commit ahead of `origin/main`, and that commit pins EventStore `8d033bd8`. EventStore `main` is ahead 1, behind 14. The 14 upstream commits do not touch the three reviewed files, so a rebase is clean, but it changes the SHA recorded in the C16 completion note and in the gitlink. Pushing Works first would repeat C15-AA2. Resolved by the owner on 2026-10-09: keep both repositories local until the C17 patches land; then rebase EventStore onto `origin/main` and push it first, repoint the Works gitlink once, correct the recorded EventStore SHA, and push Works. No commit was created or pushed by this review.
- [x] [Review][Patch] `EventStore:Reminders:Workload` and the issuer `Workload` default from different sources [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:342`] — low (C17-BH1, C17-EC4).
  - The issuer default reads `EventStore:DomainService:AppId` first; the reminder default reads only `DAPR_APP_ID`, then the application name. A host that sets `AppId` because `DAPR_APP_ID` is absent requests mismatched delegations, and every reminder stays `Retrying`.
  - Fix: in the Options row (and the `configuration-reference.md:371` row), say to set `Workload` explicitly to the same value whenever `EventStore:DomainService:AppId` or `Authentication:WorkloadIssuer:Workload` is set, because its default reads neither key.
- [x] [Review][Patch] The runbook's `403` guidance names no signal and confines `5501` to assertion denials [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:471`] — low (C17-BH3, C17-EC3).
  - A controller `Forbid()` for an admission or delegation refusal also logs gateway event `5501` with `StatusCode=403` and `Reason=operation-not-granted`, the same reason as a missing operation grant. The admission policy's payload-free `denied` audit record tells them apart.
  - Fix: say that `5501` covers both `401` assertion denials and `403` refusals, that a `403` with `operation-not-granted` can also be an admission or delegation refusal shown by the trusted-effect audit sink's `denied` record, and link [`trusted-effects.md#production-admission-gate`](trusted-effects.md#production-admission-gate).
- [x] [Review][Patch] Scope-prefix wording derives only the audience scope [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:310`] — low (C17-BH5, C17-EC6).
  - `GetOperationScope` builds `OperationScopePrefix` plus the operation with `:` replaced by `.`; the guide derives only a changed audience scope.
  - Fix: name the full keys in the parenthetical, and add that a custom `Authentication:WorkloadIssuer:OperationScopePrefix` makes the operation scope `<OperationScopePrefix>eventstore.trusted-effect`, which the authority must declare instead.
- [x] [Review][Patch] The runbook dropped its link to the credentials section [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:474`] — low (C17-EC7).
  - Fix: restore "described under [Trusted-effect submission credentials](#trusted-effect-submission-credentials)" after "assertion lifetime provisioning".
- [x] [Review][Patch] The Azure Container Apps sentence permits a self-defined token [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:275`] — low (C17-BH10d).
  - Fix: say operators must not define their own `APP_API_TOKEN` there, because a value that differs from the injected one makes every reminder actor call receive `401`, matching `deployment-azure-container-apps.md`.

#### Rejected

- `false` — C17-BH2: Retaining a denied submission with readiness `Degraded` is the frozen retain-and-degrade behavior, already stated in the runbook; a status-specific reason code is carried C14-BH6.
- `low` — C17-BH4: The Options row states the identity rule for both modes, and the authority-mode default already matches `azp`; restructuring item 2 is a rewrite.
- `false` — C17-BH6, C17-AA3, C17-AA4: The registered fallback's scheme is asserted at `EventStoreDomainServiceExtensionsTests.cs:1001`; the sibling test maps no fallback-dependent route.
- `false` — C17-BH7: Carried C15-BH11b; the no-fallback negative case is at `EventStoreDomainServiceExtensionsTests.cs:1052`, and startup failure is tested at its owner (carried C14-BH1).
- `false` — C17-BH8: "Consumes 3.117" is the package pin; "sibling-source" is the Works build-mode term; symbol citations are the C14-BH3 choice.
- `false` — C17-BH9: Dapr sends `dapr-api-token` on service invocation too, so the redrive still passes; both `/project` calls require the same policy.
- `false` — C17-BH10a: Each mention states a different consequence.
- `false` — C17-BH10b, C17-AA2: The domain-service token requirement sits in the preceding host-composition paragraph; SDK defaults satisfy `Authentication:Workload`.
- `low` — C17-BH10c: `AddEventStoreDomainServiceSecurity()` alone is undocumented and fails startup loudly (carried C13-EC1 pattern).
- `false` — C17-EC1: `BadRequest` is unreachable from the SDK submitter, catalog commands are far below 1 MiB, and `500` is the named transient case.
- `false` — C17-EC2: The gateway always returns `Ok(result)`; a malformed `2xx` needs an undocumented intermediary.
- `false` — C17-EC5: The guide already conditions `caller-conflict` on the header (C16-EC1), and Dapr service invocation attaches it.

### C18 triage (2026-10-09)

The current documentation and tracker delta was reviewed by blind hunter (BH), edge-case hunter (EC), and verification gap (VG). VG found no gaps. Each reported finding was checked against the current source before grouping.

| ID | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| BH1 | Sprint tracker still says five C17 patches are open and leaves Story 4.11 in progress | low | All five C17 entries above are complete and this spec is in review, while `sprint-status.yaml` retains the old note and `in-progress` value. | patch |
| BH2 | C16 completion note names an EventStore SHA not reachable from current HEAD | low | `8d033bd8` is no longer an ancestor after the rebase; `6caf9ef4b2712988d17ea4285ef1a7c8b2e93706` carries the reviewed patch on current `main`. The proposed correction edits this build's spec. | reject |
| BH3 | Checked C17 entries lack a new completion note | low | The checkboxes have no adjacent C17 completion record or verification summary. The proposed correction edits this build's spec. | reject |
| BH4 | The runbook treats a missing `denied` audit record as decisive for a 403 | low | `TrustedEffectAdmissionPolicy.PrepareAsync` throws before writing an audit record when the audit sink is absent, and `TrustedEffectsController` returns `Forbid()`. | patch |
| BH5 | The 5501 troubleshooting text omits verifier-unavailable 503 | low | `WorkloadJwtBearerEvents.Challenge` records event 5501 with `StatusCode=503` for `verifier-unavailable`. | patch |
| EC1 | A 403 can have no `denied` audit record when an admission component is absent | low | The same pre-audit fail-closed branch in `TrustedEffectAdmissionPolicy.PrepareAsync` produces this case. | patch (BH4 root cause) |

### C18 completion (2026-10-09)

The five C17 guide actions and the C18 diagnostic and tracker follow-ups are complete. EventStore commit `691fc1bdf717413a08c52e1c8fa499c5aca188ac` contains documentation only. The C16 note's `8d033bd8` is its historical pre-rebase identifier; the same reviewed patch is reachable on current EventStore `main` as `6caf9ef4b2712988d17ea4285ef1a7c8b2e93706`.

All four EventStore test projects built in Debug with zero warnings and errors. The current reminder filters passed 33 Contracts tests (one package probe skipped without `EVENTSTORE_PACKAGE_CONTRACT_DIR`), 227 DomainService tests, and one live Redis/Dapr test; the Client reminder filter matched zero tests. The previously recorded public `3.117.1` package-only and five-row matrix proof remains the release evidence because this patch changes no runtime or package API. Focused guide/link and sprint YAML checks and `git diff --check` passed. C18 review found no verification gap and deferred no new work.

### Review Findings

Delta review C19 (2026-10-09) is a fresh-context review of the committed C17 guide patches and C18 follow-ups. That is EventStore `6caf9ef4..691fc1bd` (two guide files) plus Works `71cb292..53c56ab` (this spec and `sprint-status.yaml`): 183 diff lines.

- Review mode: full. All four layers completed: blind hunter 12, edge-case hunter 9, acceptance auditor 5 (no acceptance-criterion violation), and verification gap with no gaps.
- Each of the 26 findings was judged before grouping, giving 31 rows with parts.
- Result: 1 decision-needed (3 finding rows), 5 patch entries (10 finding rows), 0 defer, 17 rejected.
- The triage log carries every C19 row.
- No entry changes reminder runtime behavior or package API, so the named-public `3.117.1` close gate is unaffected.

- [x] [Review][Decision] Pushed Works commits pin EventStore commits that GitHub did not have — medium (C19-AA1, C19-EC8, C19-BH12c).
  - Works `c231da0`, now in `origin/main` history, sets `references/Hexalith.EventStore` to `0e6276a6`, the pre-rebase copy of `691fc1bd`; Works `53c56ab` then repointed it.
  - Before the fix, no branch or tag contained `0e6276a6`, and the GitHub API answered `422 No commit found for SHA`, so a checkout or bisect at `c231da0` could not fetch the submodule. This repeats C15-AA2, even though the C17 decision said to repoint once.
  - A history scan found the same defect in eight Works commits. Each pins a pre-rebase EventStore commit that survived only in the local reflog. Each has a patch-identical twin on EventStore `main`, and four of them (`7450da14`, `7e19e60b`, `29e8e270`, `8d033bd8`) are this spec's cited review targets.
  - Works CI and release check out with `submodules: false`, so they were unaffected.
  - Resolved by the owner on 2026-10-10 (option a, all eight). EventStore lightweight tags `archive/works-<Works commit>` were pushed in one push: `c231da0`→`0e6276a6`, `b4df05e`→`8d033bd8`, `aae6b9b`→`29e8e270`, `bc7f928`→`7450da14`, `c7a2757`→`7e19e60b`, `4cf9f6c`→`bf9066f9`, `6a1d262`→`6f558784`, and `0bb0adf`→`56bf3487`.
  - The GitHub API now serves all eight, and a fresh repository fetched `0e6276a6` by SHA.
  - No EventStore workflow triggers on tags, semantic-release reads only `v*` tags, and the `Protect` ruleset targets branches.
  - To prevent recurrence, the Works clone now sets `push.recurseSubmodules=check`, so `git push` refuses any pushed Works commit whose EventStore pin is not on an EventStore remote. Bump the Works gitlink only after EventStore is pushed.
- [x] [Review][Patch] Aspire-composed reminder hosts always get mismatched `Workload` defaults, and the issuer row omits the coupling [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:347`] — medium (C19-BH1, C19-BH2a).
  - `AddEventStoreDomainModule` sets `EventStore__DomainService__AppId`, and nothing in Aspire sets `DAPR_APP_ID`. The reminder default therefore becomes the application name (`Hexalith.Works` under the Works AppHost), while the issuer `azp` is the app ID.
  - Fix part 1: in the Options row, the `configuration-reference.md:371` row, and the Story 4.15 Works handoff list (`typed-reminders.md:543`), say that a host composed with `AddEventStoreDomainModule` sets `AppId` but not `DAPR_APP_ID`, so it must set `EventStore__Reminders__Workload` to its app ID.
  - Fix part 2: in the `Authentication:WorkloadIssuer:Workload` row (`configuration-reference.md:504`), add that on a reminder host it must equal `EventStore:Reminders:Workload`.
- [x] [Review][Patch] The `5501` runbook gives only submitter-side remedies for gateway-side outcomes [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:476`] — low (C19-BH3, C19-BH4, C19-EC4, C19-EC5).
  - `Challenge` records `verifier-unconfigured` and the app-channel `channel-*` reasons as `401`, and it returns `503` only for `verifier-unavailable`.
  - Fix: say that a `channel-*` reason points at EventStore's own `APP_API_TOKEN` and its sidecar, `verifier-unconfigured` at EventStore's `Authentication:JwtBearer`, and a `503` at EventStore reaching its issuer's metadata and signing keys. Keep the submitter checks for the other `401` reasons.
- [x] [Review][Patch] A custom `AudienceScopePrefix` with the default audience is not derived [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:314`] — low (C19-EC3, C19-AA5).
  - Fix: make the prefix guidance symmetric. If `Authentication:WorkloadIssuer:AudienceScopePrefix` is customized, request and declare `<AudienceScopePrefix><audience>` even when the audience stays `eventstore`.
- [x] [Review][Patch] The runbook names `TaskCanceledException` for a timeout that the standard resilience handler reports as `TimeoutRejectedException` [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:475`] — low (C19-EC7).
  - `AddEventStoreDomainService` calls `AddServiceDefaults`, whose `AddStandardResilienceHandler` covers the documented submitter client.
  - Fix: say that with those defaults a timeout surfaces as `TimeoutRejectedException` and an open circuit as `BrokenCircuitException`.
- [x] [Review][Patch] The delegation-refusal check no longer reaches the delegation claim contract [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:489`] — low (C19-EC9).
  - Fix: link [`trusted-effects.md#delegation-claim-contract`](trusted-effects.md#delegation-claim-contract) beside "the delegation token provider", and keep the production-admission-gate link.

#### Rejected

- `low` — C19-BH2b: `EventStore:DomainService:AppId` is pre-existing and named as a default at `configuration-reference.md:496/504`; a new section is more than a direct correction.
- `false` — C19-BH5: The linked production admission gate opens by stating that the retention gate has no default registration, and it says that no default audit sink is registered.
- `false` — C19-BH6: With no sink, `PrepareAsync` throws before auditing, which is the runbook's "no record" branch. Correlation guidance is new material, and the sink belongs to 4.16 (carried).
- `false` — C19-BH7: The platform injection satisfies "must configure", and the reminder-scope consequence is accurate (carried C17-BH10a/b).
- `low` — C19-BH8: The status-table restructure is a rewrite (C17-BH4 precedent).
- `low` — C19-BH9, C19-BH10, C19-BH12a, C19-AA2: Each fix edits this spec's record.
- `false` — C19-BH11a, C19-AA3 (status part): Spec `done` with sprint `review` is the bmad-build step-05 handoff.
- `low` — C19-BH11b, C19-AA3 (sprint-comment part): Superseded, because this review's status sync rewrites both comment lines.
- `false` — C19-BH12b: The tests are cited as unchanged-runtime regression evidence, not as proof of the guide.
- `false` — C19-EC1: The guide requires the authority client's `azp` to be the app ID (carried C17-BH4); the unset-`DAPR_APP_ID` case is C19-BH1.
- `low` — C19-EC2: A whitespace-padded `Workload` needs an operator typo, and the trim is runtime work (C15-EC5 pattern).
- `low` — C19-EC6: Actor-stage denials reach the caller as `500`, so a misattribution needs two concurrent failure modes.
- `false` — C19-AA4: bmad-build's internal reviews run three layers by design; this code review adds the acceptance auditor.

### C19 completion (2026-10-10)

All five C19 documentation patches are applied in `typed-reminders.md` and
`configuration-reference.md`; no runtime or package API changed. The story is
handed to review with the public `3.117.1` package proof still valid.

`npx --no-install markdownlint-cli2 docs/guides/typed-reminders.md` passed with
zero issues. Linting both edited guides reported four pre-existing issues at
`configuration-reference.md:862` (`MD038` and `MD056`); the line is identical in
`HEAD` and the working tree. `git diff --check` and a focused check of the new
guide anchors and Aspire workload setting passed.

### C20 review completion (2026-10-10)

The three build review layers found seven guide corrections, all patched in the
two EventStore guide files. The review triage above records every finding. The
verification-gap layer found no gap, and no runtime or package source changed.
The public `3.117.1` package-only proof and five-row matrix audit remain the
unchanged-runtime evidence.
The guide changes are in local EventStore commit
`890d346f571aa7dd28c29d9db04077cb29841cd9`; it has not been pushed.

After the corrections, `git diff --check` passed in both repositories. Running
`npx --no-install markdownlint-cli2 docs/guides/typed-reminders.md
docs/guides/configuration-reference.md` reported only four pre-existing
`MD038`/`MD056` findings on the unchanged configuration-reference line 862.

### Review Findings

Delta review C21 (2026-10-10) is a fresh-context review of the C19 and C20 guide patches and their Works record. That is EventStore `691fc1bd..1bc1c76e` (two guide files) plus Works `00b989a..e25fe90` (this spec, `sprint-status.yaml`, and the EventStore gitlink): 239 diff lines.

- Review mode: full. All four layers completed: blind hunter 10, edge-case hunter 10, acceptance auditor 3 (no runtime acceptance-criterion violation), and verification gap with no gaps plus 2 other findings.
- Each of the 25 findings was judged before grouping, giving 28 rows with parts.
- Result: 1 decision-needed (3 finding rows), 3 patch entries (4 finding rows), 2 defer, 19 rejected.
- The triage log carries every C21 row.
- No entry changes reminder runtime behavior or package API, so the named-public `3.117.1` close gate is unaffected.

- [x] [Review][Decision] Pushed Works commits again pin an EventStore commit GitHub does not have — medium (C21-BH1a, C21-AA1a, C21-VG-O1).
  - Works `896d3be` and `976d372`, both in `origin/main` history, pin EventStore `890d346f`, the pre-rebase copy of `1bc1c76e` (identical patch-id); `b89368f` then repointed to `1bc1c76e`.
  - No branch or tag contains `890d346f`, and the GitHub API answers `422 No commit found for SHA`, so a checkout or bisect at either commit cannot fetch the submodule. This repeats C19-AA1.
  - The C19 safeguard did not catch it. A scratch repro showed `push.recurseSubmodules=check` refuses a pin that is reachable locally but unpushed, and silently passes a pin that a rebase or amend has orphaned, because git treats a commit unreachable from every local ref as absent. The reflog fits that path: Works `976d372` was committed at 08:42:01, EventStore was rebased at 08:44:57, and Works `b89368f` followed at 08:45:28.
  - Options: (a) push lightweight tag `archive/works-896d3be` → `890d346f` (it covers `976d372` too), as in C19, and record that the check misses rebase-orphaned pins; (b) do (a) and add a local Works `pre-push` hook that refuses any pushed gitlink not reachable from an EventStore remote-tracking ref; (c) accept the unfetchable history.
  - A full Works history scan during the decision found three more orphans that the C19 scan missed, each with a patch-identical twin on EventStore `main`: `7fa36253` (Works `7011082`, twin `ac5b0c47`), `319c78f1` (Works `c7e699f` and `5928ee9`, twin `5cbb1535`), and `c8a2bf8b` (Works `d9235f5` and `a9d8578`, twin `5a9b502f`). Seven Works commits on `origin/main` were unfetchable in all. The three older objects were already past the default 30-day unreachable-reflog expiry, so the next local `gc` could have pruned them.
  - Resolved by the owner on 2026-10-10 (option b, all four). EventStore lightweight tags `archive/works-7011082`→`7fa36253`, `archive/works-c7e699f`→`319c78f1`, `archive/works-d9235f5`→`c8a2bf8b`, and `archive/works-896d3be`→`890d346f` were pushed in one push. The GitHub API now serves all four, a fresh repository fetched each by SHA, EventStore carries 12 `archive/works-*` tags, and the tag push started no workflow run.
  - The Works clone now has an untracked `.git/hooks/pre-push`. For every pushed commit it refuses any changed gitlink that no remote-tracking branch of an initialized submodule contains. Replaying the incident range `00b989a..b89368f` and the `d9235f5` range was refused; the clean range `b89368f..f00b82d`, a no-op push, a new-branch push, and a deletion passed. On refusal, amend the unpushed Works commits to the pushed submodule commit before pushing again.
- [x] [Review][Patch] Receiver-side `401` mappings drop the submitter half of each mismatch [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:489`] — low (C21-AA2, C21-EC1).
  - Fix: after the four receiver mappings, say that each value must match what the submitter presents: its `Authentication:WorkloadIssuer:Workload` or client `azp` for `caller-not-allowed`, the `gatewayAudience` passed to `AddEventStoreTrustedEffectWorkloadAssertion` for `audience-invalid`, and its own `Authentication:JwtBearer` issuer and signing mode for `issuer-invalid` and `algorithm-invalid`.
- [x] [Review][Patch] `channel-token-invalid` can also come from a direct gateway call [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:486`] — low (C21-EC4).
  - Fix: name `channel-token-missing` or `channel-token-invalid` in the Dapr service-invocation check, because the documented chain forwards the submitter's `DAPR_API_TOKEN`.
- [x] [Review][Patch] The runbook implies every request reaching the gateway logs event `5501` [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:484`] — low (C21-EC6).
  - Fix: say that event `5501` records only internal credential denials (`401`, `403`, and `503 verifier-unavailable`), so a `400`, `413`, or `500` has no `5501` entry.
- [x] [Review][Defer] `AddEventStoreDomainModule` leaves the reminder `Workload` default on the application name [`references/Hexalith.EventStore/src/Hexalith.EventStore.Aspire/HexalithEventStoreDomainModuleExtensions.cs:65`] — deferred: pre-existing runtime and package default (C21-BH4); the guides document the workaround, and a source fix needs a new named public release.
- [x] [Review][Defer] Story 4.15 does not carry the `EventStore__Reminders__Workload` requirement [`_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`] — deferred: the fix edits another spec (C21-BH5); the requirement lives only in the EventStore guide's 4.15 handoff list.

#### Rejected

- `low` — C21-BH1b, C21-EC9, C21-AA1b, C21-VG-O2, C21-BH2, C21-EC10, C21-AA3, C21-BH10: Each fix edits this spec's record. The decision's resolution record will supersede the stale `890d346f` note.
- `false` — C21-BH3, C21-EC7: Spec `done` with sprint `review` is the bmad-build step-05 handoff (C19-BH11a).
- `false` — C21-BH6a, C21-EC5: The next sentence routes every request that reached the gateway, including the ones that tripped the breaker, to event `5501`.
- `low` — C21-BH6b: Each retried `503` record is accurate; duplicates do not change the diagnosis.
- `false` — C21-BH7, C21-EC2: The "other `401`" bucket links the credentials section, which documents `caller-conflict`, the operation-scope mappers, and the lifetime cap.
- `low` — C21-BH8: A table restructure is a rewrite (C17-BH4, C19-BH8).
- `false` — C21-BH9: Both rows name one identity, because the authority `azp` must be the app ID; the env-var form is what an Aspire host sets.
- `false` — C21-EC3: A kid-less JWKS raises a signature-invalid subtype, and the guide already maps `401 signature-invalid` to a JWKS comparison.
- `false` — C21-EC8: A documented submitter always carries the trusted-effect operation, so a missing grant is `401 operation-missing`, which reaches the credentials section.

### C21 patch completion (2026-10-10)

The three open C21 runbook patches are applied in EventStore
`docs/guides/typed-reminders.md`. The guide now scopes event `5501` to internal
credential denials, names both direct-call channel-token failures, and pairs
receiver `401` settings with the submitter values to inspect.

`git diff --check` passed in Works and EventStore. EventStore
`npx --no-install markdownlint-cli2 docs/guides/typed-reminders.md` reported
zero issues. This is a documentation-only continuation; the named-public
`3.117.1` package proof remains the runtime acceptance evidence. The retained
`public-matrix-audit.json` matches its executed XML SHA-256, and all 22 listed
tests passed across the five frozen matrix rows. No runtime tests were rerun for
these guide edits.

### C22 review fixes and verification (2026-10-10)

- [x] [Review][Patch] Sprint tracker now records C21 guide completion and the review handoff (C22-BH2).
- [x] [Review][Patch] The runbook distinguishes Development symmetric signing from external authority signing when checking issuer and algorithm mismatches (C22-BH3).
- [x] [Review][Patch] The replay recipe verifies its copied test assembly and retained source dependency hashes before substituting public SDK DLLs (C22-EC1).
- [x] [Review][Patch] The replay recipe rejects empty, skipped, failed, or changed-count xUnit selections; both recorded XML results retain 1/1 and 227/227 passes with no skips (C22-EC2, C22-VG1).
- [x] [Review][Patch] Package downloads stop at the recorded archive length plus one byte and reject length mismatches (C22-EC3).

The three focused replay tests passed. The retained `evidence-sha256.json` now
binds the revised recipe and its test file; both hashes were checked. The
original public package archives, execution XML, matrix audit, and named-public
release evidence were not changed. The guide correction is EventStore commit
`c3bac11a49eef12af54b185cf3d689f9dfa70771`. EventStore guide lint and `git diff --check`
passed. The full public replay was not rerun because this correction only
hardens the repeatable proof recipe and the recorded checkout is not the
current EventStore HEAD.

### Review Findings

Delta review C23 (2026-10-10) is a fresh-context review of the C21 guide patches and the C22 fixes. That is EventStore `1bc1c76e..c3bac11a` (`docs/guides/typed-reminders.md`) plus Works `e25fe90..79689aa` (this spec, `sprint-status.yaml`, `deferred-work.md`, the replay recipe, its test and hash manifest, and the EventStore gitlink): 408 diff lines.

- Review mode: full. All four layers completed: blind hunter 16, edge-case hunter 10, acceptance auditor 7 (no acceptance-criterion violation), and verification gap with 1 gap plus 3 other findings.
- Each of the 37 findings was judged before grouping, giving 43 rows with parts.
- Result: 1 decision-needed (4 finding rows), 7 patch entries (13 finding rows), 0 defer, 26 rejected.
- The triage log carries every C23 row.
- No entry changes reminder runtime behavior or package API, so the named-public `3.117.1` close gate is unaffected.

- [x] [Review][Decision] The evidence packet no longer holds the replay recipe that ran — medium (C23-BH1, C23-EC8, C23-AA1, C23-VG-O1).
  - `parent-replay-summary.json:17` records `executed_recipe_sha256` `fa11cdd6…` for `replay-proof.py`. The C22 fix replaced that file and rebound `evidence-sha256.json` to `e6a6f48c…`, so no file in the packet now has the executed hash; the bytes exist only at Works `a1e282a^`.
  - The bound recipe has never run end to end (C22 record), and neither `REPLAY.md` nor either summary says the recipe changed after its last run.
  - The new gates should pass at the recorded checkout: the retained XMLs hold exactly 1/1 and 227/227, 12 of 14 cached archives match their recorded length and SHA-256, and the parent replay reproduced the bound test-assembly and dependency hashes at `references/Hexalith.EventStore`.
  - Options: (a) keep the executed bytes as `replay-proof.executed-2026-10-08.py`, bind it in `evidence-sha256.json`, and note in `REPLAY.md` that the current recipe hardens it without an end-to-end run; (b) do (a) and also rerun the hardened recipe at a temporary `07d1e23a` submodule checkout, keeping that run as separate evidence; (c) add only a `REPLAY.md` pointer to `git show a1e282a^:…/replay-proof.py`; (d) accept as is.
  - Resolved by the owner on 2026-10-10: option (a), applied with the two `REPLAY.md` patches so that file is rebound once. A rerun was declined because Works `Hexalith.Builds` moved since the replay (`58d9b546` → `6a002df5`), so it would need two submodule rewinds, and the `3.117.1` gate does not depend on it. The executed bytes come from pushed Works `f00b82d`, whose `replay-proof.py` hashes to `fa11cdd6…`.
- [x] [Review][Patch] Keep the executed replay recipe in the evidence packet [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/evidence-sha256.json`] — medium (from the C23-BH1 decision).
  - Fix: restore `f00b82d:…/replay-proof.py` as `replay-proof.executed-2026-10-08.py`, confirm SHA-256 `fa11cdd6…`, bind it in `evidence-sha256.json`, and state in `REPLAY.md` which recipe the 2026-10-08 replay ran and that `replay-proof.py` has not run end to end. Leave the retained summaries unchanged.
- [x] [Review][Patch] The symmetric-mode check compares the signing key for `algorithm-invalid` [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:501`] — low (C23-BH7, C23-EC6, C23-AA3).
  - Fix: compare the issuer for `issuer-invalid`; for `algorithm-invalid`, say the submitter and EventStore use different signing modes (a symmetric submitter always signs `HS256`), while a wrong key yields `signature-invalid`.
- [x] [Review][Patch] The mode split names only Development [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:501`] — low (C23-EC7).
  - Fix: say "symmetric mode (Development, or a non-Production environment with `AllowInsecureSymmetricKey=true`)".
- [x] [Review][Patch] The `DAPR_API_TOKEN` sentence lacks its consequence [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:490`] — low (C23-BH8).
  - Fix: say that a direct gateway call presents the submitter's `DAPR_API_TOKEN` (or none), which EventStore rejects, so the remedy is routing through Dapr service invocation, not aligning tokens.
- [x] [Review][Patch] `REPLAY.md` still describes the pre-C22 recipe [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/REPLAY.md:17`] — low (C23-BH3a, C23-AA5a, C23-VG-O2).
  - Fix: list the test-assembly and retained-dependency hash checks, the 1/227 non-skipped pass checks, the length-bounded downloads, and how to run `test_replay_proof.py`.
- [x] [Review][Patch] The test-assembly hash binds the replay to one absolute checkout path, undocumented [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/REPLAY.md:6`] — low (C23-BH2, C23-EC1, C23-AA5b).
  - Fix: state that the bound test assembly embeds absolute source paths, so the fixture must be built from the Works submodule at `/home/administrator/projects/hexalith/works/references/Hexalith.EventStore` checked out at `07d1e23a`.
- [x] [Review][Patch] Sprint tracker comments omit the C22 fixes [`_bmad-output/implementation-artifacts/sprint-status.yaml:2`] — low (C23-BH15a).
  - Fix: name the C22 replay hardening and guide fix in both `last_updated` comments when the C23 outcome is recorded.
- [x] [Review][Patch] The ledger C21-BH4 entry has an ambiguous citation and overstates "only" [`_bmad-output/implementation-artifacts/deferred-work.md:1235`] — low (C23-BH16b).
  - Fix: cite `src/Hexalith.EventStore.DomainService/EventStoreReminderServiceCollectionExtensions.cs:126` for `ResolveWorkload`, and say the helper sets `EventStore__DomainService__AppId` and `ServiceVersion` but not the reminder `Workload`.

#### Rejected

- `low` — C23-BH3b, C23-BH11, C23-AA4, C23-BH12, C23-AA7, C23-VG-O3, C23-BH13, C23-EC9, C23-AA2a: Each fix edits this spec's record.
- `low` — C23-BH4, C23-EC2: The pre-existing Contracts-side gap needs a dirty checkout of a fresh `07d1e23a` submodule, and a clean-tree check adds a guard.
- `low` — C23-BH5, C23-AA6: `verify_result_xml` rejects every uncovered case by exact comparison; more cases in a hash-bound recipe test are more than a direct correction.
- `low` — C23-BH6, C23-EC3: The process exit and traceback carry failure; the ledger records true command exits and never claims success.
- `false` — C23-BH9a, C23-EC5: "Records internal credential denials only" already excludes `429`, `404`, `415`, `502`, and `504`.
- `low` — C23-BH9b, C23-AA2b: A pre-existing, incident-only routing gap; naming the right log per status needs gateway-logging research.
- `low` — C23-BH10: The trace-ID correlation fallback needs verification before it can be documented.
- `low` — C23-BH14: The untracked hook is outside this diff and was an owner decision; a merge that introduces an unpushed gitlink is rare.
- `low` — C23-BH15b, C23-EC10: Each fix rewrites Works history; the next commit pins `c3bac11a`.
- `false` — C23-BH16a: The legacy format matches every 4.11 ledger section, and `bmad-loop-sweep --migrate` converts it.
- `low` — C23-EC4: A failing first subtest already fails the test; there is no false pass.
- `low` — C23-VG1: The `main()` gates are two lines in a hash-bound, review-gated recipe whose tests run only by hand; a mocked end-to-end test is more than a direct correction.

### C23 patch completion (2026-10-10)

All eight C23 patches are applied. The evidence packet now retains the exact
recipe executed on 2026-10-08 as `replay-proof.executed-2026-10-08.py`, with
SHA-256 `fa11cdd6591cbad4523deff70ed1bc0497f75dc635f6e139685bd6ab989180ef`.
The current `replay-proof.py` is documented as hardened but not yet run end to
end. `REPLAY.md` names the fixed checkout path, the new proof checks, and the
focused test command. Its new hash and the executed recipe hash are bound in
`evidence-sha256.json`; all 39 listed files match that manifest. The retained
replay summaries, public archives, execution XML, and matrix audit are unchanged.

The EventStore guide now distinguishes issuer, signing mode, and signing-key
failures and explains why a direct gateway call's `DAPR_API_TOKEN` fails. The
Works sprint tracker records the C22 replay and guide fixes, and the deferred
ledger cites the correct `ResolveWorkload` file and both Aspire settings.

Verification: `python3 -m unittest -v test_replay_proof.py` passed 3/3; the
restored recipe is byte-identical to `f00b82d` (`git show ... | cmp`); EventStore
guide markdownlint reported zero issues; `git diff --check` passed in Works and
EventStore. No runtime code or package API changed, so the named public
`3.117.1` acceptance proof remains the existing runtime evidence. The current
recipe was not run end to end, in accordance with the C23 owner decision.

### Review Findings

Delta review C25 (2026-10-10) is a fresh-context review of the C23 patches and the C24 record. That is Works `79689aa..c43ff7f` (this spec, `sprint-status.yaml`, `deferred-work.md`, the evidence packet, the new commit-message evidence, and the EventStore gitlink) plus EventStore `c3bac11a..c6d26698` (`docs/guides/typed-reminders.md`): 478 diff lines.

- Review mode: full. All four layers completed: blind hunter 12, edge-case hunter 6, acceptance auditor 8 (no acceptance-criterion violation), and verification gap with no gap plus 2 other findings.
- Each of the 28 findings was judged before grouping, giving 30 rows with parts.
- Result: 0 decision-needed, 6 patch entries (12 finding rows), 0 defer, 18 rejected.
- The triage log carries every C25 row.
- No entry changes reminder runtime behavior or package API, so the named-public `3.117.1` close gate is unaffected. The three `REPLAY.md` patches change a hash-bound file; rebind `evidence-sha256.json` once.

- [x] [Review][Patch] The service-invocation sentence names the wrong sidecar [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:490`] — low (C25-VG-O2, C25-BH5, C25-EC1, C25-AA2).
  - Fix: say the submitter sends its `DAPR_API_TOKEN` to its own sidecar, which relays the call to EventStore's sidecar, and EventStore's sidecar presents EventStore's `APP_API_TOKEN` to the gateway.
- [x] [Review][Patch] The sprint tracker no longer records the C22 fixes [`_bmad-output/implementation-artifacts/sprint-status.yaml:2`] — low (C25-AA1, C25-EC5).
  - Fix: name the C22 replay hardening and guide fix again in the tracker comments (lines 2, 38, and 40), so the C23-BH15a patch and the C23 completion claim hold.
- [x] [Review][Patch] `REPLAY.md` gives no restore step after rewinding the live submodule [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/REPLAY.md:8`] — low (C25-EC3, C25-BH8b).
  - Fix: say that after the replay, a non-recursive `git submodule update -- references/Hexalith.EventStore` restores the pinned commit, and that the rewound gitlink must not be committed.
- [x] [Review][Patch] The `REPLAY.md` result sentence is garbled and incomplete [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/REPLAY.md:20`] — low (C25-BH9, C25-AA8).
  - Fix: say each run must report exactly one assembly with no failures, errors, or skips (1/1 package-only, 227/227 reminders), and rejoin the stray line-26 wrap.
- [x] [Review][Patch] `REPLAY.md` attributes two parent-side checks to the executed recipe [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/REPLAY.md:39`] — low (C25-BH3).
  - Fix: say the retained summaries describe that run, and that its test-assembly and retained-dependency matches were checked by the parent outside the executed recipe (C12 record).
- [x] [Review][Patch] `AllowInsecureSymmetricKey` lacks its configuration path [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:506`] — low (C25-BH7).
  - Fix: write `Authentication:JwtBearer:AllowInsecureSymmetricKey=true`.

#### Rejected

- `low` — C25-BH1, C25-EC6, C25-AA3, C25-VG-O1a: Disclosing `882a0761` edits this spec's record; it touches no reminder or Effects file, and no Works `.cs` file references the 104 deleted types.
- `false` — C25-VG-O1b: The Conversations and Tenants gitlink moves are not 4.11 work and were excluded at the C25 checkpoint.
- `low` — C25-BH2, C25-AA4, C25-BH4: Each fix edits this spec's record; C25 now reviews the C24 delta with all four layers.
- `low` — C25-BH11, C25-EC4, C25-AA5: Carried C23-BH11; the fix edits this spec's record.
- `low` — C25-BH8a, C25-AA7: Carried C24-BH6; a wrong Builds pin fails the bound test-assembly hash loudly.
- `low` — C25-EC2: Carried C23-BH2; the path is documented, and a guard adds code to the hash-bound recipe.
- `false` — C25-BH6: The sentence names the key-mismatch cause, and the paragraph says to compare the signer with EventStore's trusted signing key.
- `false` — C25-BH10, C25-AA6: The candidates are retained verbatim, the Works SHA cannot be self-recorded, the other two commits are owner commits, and citing the file edits this spec's record.
- `false` — C25-BH12: The line counts the 7 finding-derived entries exactly; the eighth came from the owner's decision.

### C25 patch completion (2026-10-10)

All six C25 patches are applied. The EventStore guide now says the submitter
sends its `DAPR_API_TOKEN` only to its own sidecar, which relays the call to
EventStore's sidecar, and that EventStore's sidecar presents EventStore's
`APP_API_TOKEN` to the gateway. The symmetric-mode exception now names the
full `Authentication:JwtBearer:AllowInsecureSymmetricKey=true` path.

`REPLAY.md` now says to restore the pinned submodule after a replay with the
non-recursive `git submodule update -- references/Hexalith.EventStore` and
never to commit the rewound `07d1e23a` gitlink. It states the exact result gate
that `verify_result_xml` enforces: one assembly, all tests passed, and no
failures, errors, or skips, with 1/1 for the package-only method and 227/227
for the reminder checks. It also says the parent checked the test-assembly and
retained-dependency matches outside the executed recipe, which hashes neither
(C12 record). `evidence-sha256.json` rebinds `REPLAY.md` once to
`1cd27d9882b288c6a95ba3a64b4741bd617901ef9539ef6b3460865cfaeeb34b`. All 39
listed files match that manifest, and the retained summaries, archives, XML,
and both recipes are unchanged.

The Works sprint tracker again names the C22 replay-recipe hardening and guide
fix, records the C23 and C25 patches, and keeps 4.11 at `review` pending a
C26 delta review of these patches. No runtime code or package API changed, so
the named-public `3.117.1` acceptance proof remains the existing runtime
evidence.

### Review Findings

Delta review C26 (2026-10-10) is a fresh-context review of the C25 patches. That is Works `3ebc68b..718726d` (this spec, `sprint-status.yaml`, `REPLAY.md`, `evidence-sha256.json`, and the EventStore gitlink) plus EventStore `c6d26698..a5405602` (`docs/guides/typed-reminders.md`): 177 diff lines.

- Review mode: full. All four layers completed: blind hunter 9, edge-case hunter 6, acceptance auditor 7 (no acceptance-criterion violation), and verification gap with no gap plus 2 other findings.
- Each of the 24 findings was judged before grouping, giving 32 rows with parts.
- Result: 0 decision-needed, 3 patch entries (5 finding rows), 0 defer, 27 rejected.
- The triage log carries every C26 row.
- No entry changes reminder runtime behavior or package API, so the named-public `3.117.1` close gate is unaffected. The two `REPLAY.md` patches change a hash-bound file; rebind `evidence-sha256.json` once.

- [x] [Review][Patch] `REPLAY.md` does not say which recipe enforces the result gate [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/REPLAY.md:27`] — low (C26-BH3, C26-BH4).
  - Fix: at lines 27-30, say the recipe rejects any run whose result XML does not report exactly one assembly whose tests all passed (1/1 package-only, 227/227 reminders). At lines 45-47, say the executed recipe checked neither archive lengths nor result counts and hashed neither the test assembly nor the retained source dependency, accepting any run that exited zero. The parent checked those counts and matches outside it (C12 record).
- [x] [Review][Patch] The restore step leaves other rewound root submodules rewound [`_bmad-output/implementation-artifacts/evidence/story-4-11-public-3.117.1-2026-10-08/REPLAY.md:16`] — low (C26-EC3).
  - Fix: say to restore every root submodule rewound for the replay with the non-recursive `git submodule update -- <path>…` (for example `references/Hexalith.EventStore` and `references/Hexalith.Builds`), and never to commit a rewound gitlink.
- [x] [Review][Patch] The C25 guide edit breaks the paragraph's line wrap [`references/Hexalith.EventStore/docs/guides/typed-reminders.md:492`] — low (C26-BH9, C26-AA5).
  - Fix: rewrap lines 490-494 and 505-510 to about 80 columns, changing no words.

#### Rejected

- `low` — C26-BH1a, C26-EC1: A staged rewound gitlink requires an action `REPLAY.md` forbids, and the guard adds steps.
- `low` — C26-BH1b, C26-EC2a: The detached `HEAD` follows a rare replay and fails loudly at `git push`; restoring branch context adds steps.
- `low` — C26-BH2a: The prerequisites name the commit and bound path; a guarded rewind procedure exceeds a direct correction.
- `false` — C26-BH2b: "After the replay" includes a failed run.
- `low` — C26-BH2c, C26-EC4, C26-AA6: Stale outputs follow any checkout; the next incremental build replaces them.
- `low` — C26-BH5: Carried C23-VG1 and C24-VG1; the whole-dict check rejects each listed case by construction.
- `low` — C26-BH6a, C26-EC5, C26-AA1a, C26-VG-O1: Carried C25-BH1. The Story 6.6 and 6.1 commits touch no reminder or Effects source file, no affected Works consumer was found, and disclosure edits this spec's record.
- `low` — C26-BH6b, C26-EC6, C26-AA1b: The Builds and Projects pins are pushed and not 4.11 work; recording them edits this spec's record.
- `low` — C26-BH7a, C26-AA3: The guide matches the C25 fix line; removing "only" edits this spec's record.
- `false` — C26-BH7b, C26-BH7c: The guide traces the documented token path, and the `channel-*` diagnosis does not depend on the submitter's token.
- `low` — C26-BH8: The `assertion-missing` `401` is already routed to the submitter's assertion handler; a submitter failure path exceeds a direct correction.
- `false` — C26-EC2b: Git refusing to overwrite local changes is a correct, loud failure.
- `low` — C26-AA2: The checks pass on rerun; recording them edits this spec's record.
- `false` — C26-AA4: The C12 record states the parent's matches, and the retained recipe shows they were outside it.
- `low` — C26-AA7: Carried C23-BH11 and C25-BH11.
- `false` — C26-VG-O2: Excluded by the scope approved at the C26 checkpoint.

### C27 completion (2026-10-10)

The three C26 patches are complete. The replay recipe unit tests pass (3/3), all 39 retained evidence hashes match, and the public matrix audit names 22 passing XML cases across all five frozen rows. Markdown lint and `git diff --check` pass for the guide and evidence edits. C27 found no verification gap or new 4.11 patch; three agent-context corrections were deferred.
