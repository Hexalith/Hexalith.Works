---
title: 'Publish EventStore Typed Reminder Reconciliation'
type: 'feature'
created: '2026-09-29'
status: 'in-review'
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
