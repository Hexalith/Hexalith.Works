---
title: 'Publish EventStore Typed Reminder Reconciliation'
type: 'feature'
created: '2026-09-29'
status: 'in-progress'
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
