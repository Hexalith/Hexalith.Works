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
- [ ] `src/Hexalith.EventStore.Contracts/Reminders/` — one type per file:
  - `ReminderIntent` binds tenant, target domain and aggregate, UTC due, catalog kind, opaque typed payload, source domain/aggregate/sequence, and schedule revision.
  - `ReminderDisposition`.
  - `ReminderIdentityCodec` (versioned):
    - `wra-<52>` over the tuple `("reminder-actor", tenant, item)`.
    - `wrs-<52>` over the tuple `("schedule", tenant, item, due UTC ticks, revision)`.
    - `date-`/`expiry-` names from a closed kind map.
- [ ] `src/Hexalith.EventStore.Client/Reminders/` — `IReminderIntentSource` is domain-implemented: it re-folds current intents from the stream and translates a due intent into a command type and payload. Also add `IReminderRegistrar` (`ConvergeAsync(target)`) and `IReminderDelegationTokenProvider`.
- [ ] `src/Hexalith.EventStore.DomainService/`:
  - `ReminderActor` with a configurable actor type name.
  - `ReminderCoordinator`.
  - `ReminderIntentIndex`: a tenant registry plus per-tenant candidates, written by CAS before scheduling.
  - `ReminderReconciler`: a hosted service with a periodic pass that starts at startup and uses `TimeProvider`.
  - `ReminderCallbackTokenFilter`.
  - `EventStoreReminderOptions` bound to `EventStore:Reminders`.
  - `eventstore-reminders-unresolved` readiness check.
  - `AddEventStoreReminders`/`MapEventStoreReminders` extensions.
  - The csproj reference.
- [ ] `tests/Hexalith.EventStore.Contracts.Tests/Reminders/ReminderIdentityCodecTests.cs` — golden vectors, collision detection, rejection of unknown kinds, and unchanged `EffectIdentityCodec` vectors.
- [ ] `tests/Hexalith.EventStore.DomainService.Tests/` — `ReminderCoordinatorTests`, `ReminderCallbackAdmissionTests`, `ReminderReconcilerTests`, and composition tests. Cover every matrix row with persisted fake-store state.
- [ ] `tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Integration/ReminderRecoveryLiveSidecarTests.cs` — against Redis/Dapr:
  - Persist registration.
  - Invoke the callback directly; do not wait for a timer.
  - Restart the host with Redis kept: expect the same effect ID and a replayed receipt.
  - Delete the Scheduler reminder: the reconciler rearms it.
- [ ] `tests/Hexalith.EventStore.Contracts.Tests/Packaging/PackagedReminderApiTests.cs` plus a new probe in `scripts/validate-consumer-package-references.py` — a package-only consumer using the reminder API, with no Works types.
- [ ] `docs/guides/typed-reminders.md` — the contract, the codec, the operator runbook for quarantine and readiness, the production gate, and evidence handed to 4.15.

**Acceptance Criteria:**
- Given a committed intent, when registration/callback repeats after restart, then one reminder and one target receipt represent one logical submission.
- Given lost firing, when periodic reconciliation runs, then due/future intents converge from streams and unresolved work degrades readiness.
- Given a stale witness or unauthorized caller, when callback admission runs, then no mutation/disclosure occurs and the disposition is audited.
- Given the published R6 package, when a package-only consumer calls it, then no Works payload types are required.

## Implementation Notes

## Spec Change Log

## Review Triage Log

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
