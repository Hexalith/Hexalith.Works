---
title: 'Land Story 4.8 Group 1 review patches'
type: 'bugfix'
created: '2026-09-08'
status: 'draft'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '6e2fb4da8be3a3c4e90e0620d074ec9402129397'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8's reminder runtime is shipped and the parked-candidate skip is already in the working tree, but nine Group 1 review patches are still open. Those holes leave parked telemetry wrong, poison `/project` forever on identity/STJ failures, let reserved tenant `tenants` enter through `/process`, and leave host wiring unproven.

**Approach:** Apply only the remaining Group 1 host-edge patches and their deterministic tests, keep the uncommitted parked skip, then return the story and sprint to `review`. Do not re-prove the live reminder lane.

## Boundaries & Constraints

**Always:** Keep deterministic reminder names, idempotent `DateResume`, index-as-discovery / stream-as-truth, clock-free kernel, catalog 40 with a 4.8 delta of 0, and DW-56 open. Keep the dirty parked-candidate skip (`Parked=true` is a clean scan skip, EventId 4607). Refuse reserved tenant `tenants` at every host ingress (`/project`, `/work/events`, `/process`). Distinct EventIds: 4501 decode-failed, 4502 first park, 4503 later parked skip, 4504 decoder skip.

**Never:** Re-run live reminder/mTLS lanes, change kernel/`Handle` rules or catalog types, update submodules, restore `Works:Recovery:Tenants`, tombstone parked index entries, add unpark/replay, pull DW-56/DW-68/DW-70/DW-73/DW-84/DW-85/DW-86, SharedRebuild, F-PROJ-1, the ~5s give-up, the index durability window, Story 4.9, or the dirty PRD files.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Already parked `/project` | Parking doc `Parked=true` | Ack, log 4503 Warning, no write, no 4502 Error | N/A |
| Identity mismatch or `NotSupportedException` | State-affecting `/project` event | `Malformed=true`; `ParkOrRetryAsync` runs | Budget then park; no uncaught 500 |
| Reserved tenant `/process` | `CreateWorkItem` tenant `tenants` | Adapter throws before persist | `/project` never sees the stream |
| Reserved tenant `/work/events` | Envelope `TenantId=tenants` | `FailedInvalidPayload` before marker acquire | No handler, no reminder |
| Registry ETag retry | `EnsureTenantRegisteredAsync` retries | Copy-on-write `HashSet`; both tenants kept | No in-place mutate |
| Bound parking budget | Host `Works:Projection` Max=1 | First undecodable `/project` parks | Invalid Max fails ValidateOnStart |
| Recovery source wiring | Built recovery services | `IPendingDateAwaitSource` is `IndexedPendingDateAwaitSource` | A fake/no-op registration must fail this fact |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs` -- keep dirty `ScanTenantAsync` parking read; do not tombstone
- `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` -- keep EventId 4607; do not reuse 450x
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- `ParkOrRetryAsync` 615-647 returns unchanged parked doc so 4503 never fires; `EnsureTenantRegisteredAsync` 689-693 mutates `Tenants` in place; index/registry/parking contexts all use `WhatsNextProjectionType`
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs` -- identity mismatch throws (65-67); catch misses `NotSupportedException`; `SkippedEvent` EventId 4501 collides with `ProjectionDecodeFailed`
- `src/Hexalith.Works/Projections/WorksReadModelKeys.cs` -- add distinct projection-type tokens for index, registry, parking; keep `ReservedTenantId`
- `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- `ProcessAsync` is not overridable; guard every `Handle` TenantId (and `LinkConversation` envelope) with `IsReservedTenantId`
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs` -- reserved-tenant branch 52-60 exists; add a before-acquire test
- `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs` -- line 72 registers the indexed source
- `src/Hexalith.Works/Runtime/WorksHost.cs` -- binds `Works:Projection` 92-97; `/project` must use `IOptions` value, not `new WorksProjectionOptions()`
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs` -- extend parking, reserved-tenant, registry-retry coverage
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs` -- keep the two dirty parked facts
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- mirror `Works_processor_rejects_invalid_envelope_before_marker_acquisition`
- `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs` -- assert concrete source type; add projection-options bind/validate facts (or a sibling file)
- `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs` -- reserved-tenant `/process` via `ProcessAsync`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` -- Group 1 lines 738-746; promote Status to `review`
- `_bmad-output/implementation-artifacts/sprint-status.yaml` -- return key to `review`

Do not change kernel, Reactor, AppHost, SharedRebuild, or `references/`.

## Tasks & Acceptance

**Execution:**
- [ ] `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- if already parked, log 4503 and return without writing; copy-on-write the registry `HashSet`; set index/registry/parking `ProjectionType` to new `WorksReadModelKeys` tokens -- Group 1 MEDIUM parked-log, ETag-mutate, and attribution holes
- [ ] `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs` -- return `Malformed` on identity mismatch; catch `NotSupportedException` like `WorksEventDecoder`; move `SkippedEvent` to EventId 4504 -- stop immortal `/project` 500s and EventId collapse
- [ ] `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- refuse reserved tenant on every command `Handle` -- close the `/process` ingress `/project` already rejects
- [ ] `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs` -- already-parked 4503 path; identity/`NotSupportedException` parks; registry retry keeps both tenants without in-place mutate -- prove dispatcher/decoder patches
- [ ] `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- reserved tenant before marker acquire -- deleting the branch must fail
- [ ] `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs` -- `ProcessAsync(CreateWorkItem)` with tenant `tenants` throws and writes nothing -- `/process` hole
- [ ] `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs` -- resolved source is `IndexedPendingDateAwaitSource`; invalid `Works:Projection` fails ValidateOnStart; bound Max=1 is what `/project`/dispatcher uses -- host wiring
- [ ] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` and `sprint-status.yaml` -- check Group 1 patches; Status/`development_status` = `review`; leave DW-56 open -- story close-out after patches

**Acceptance Criteria:**
- Given a parked aggregate is redispatched, when `/project` runs, then it is acknowledged with EventId 4503 and `FailureCount` is unchanged.
- Given a state-affecting event has a foreign identity or throws `NotSupportedException`, when `/project` decodes it, then parking starts and the endpoint does not throw before `ParkOrRetryAsync`.
- Given tenant id `tenants`, when `/process` or `/work/events` is invoked, then the command/event is refused before persist or marker acquire.
- Given an ETag retry on the pending-date registry, when two tenants register, then both survive and the transform does not mutate the store instance.
- Given recovery services are built, when `IPendingDateAwaitSource` is resolved, then the concrete type is `IndexedPendingDateAwaitSource`.
- Given `Works:Projection:MaxUndecodableEventDispatchesBeforeParking` is bound, when the host starts, then invalid values fail ValidateOnStart and `/project` uses the bound budget.
- Given Group 1 patches and their tests land, when bookkeeping is updated, then the story and sprint say `review` and DW-56 stays open.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

`EventStoreAggregate.ProcessAsync` is not virtual, so reserved-tenant refusal belongs in every adapter `Handle`, not a base override. First-park stays 4502 (`FailureCount == max` on the transition); later passes must detect `Parked` before incrementing. Decoder skip (4504) is "unknown/blank type"; 4501 remains consecutive decode-failure retry.

## Verification

**Commands:**
- `DOTNET_CLI_HOME=/tmp dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -m:1` -- expected: 0 warnings / 0 errors
- Run the IntegrationTests binary with `-class` for `PendingDateAwaitIndexDispatcherTests`, `IndexedPendingDateAwaitSourceTests`, `WorksDomainEventProcessorTests`, `WorksRecoveryOptionsTests`, and `LinkConversationRuntimeAdapterTests` -- expected: all invoked facts pass
- `DOTNET_CLI_HOME=/tmp dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -m:1` then run that binary -- expected: fitness still green (catalog 40, no new runtime tokens outside the host)

Do not run the live reminder or mTLS smoke lanes.
