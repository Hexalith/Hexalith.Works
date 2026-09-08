---
title: 'Land Story 4.8 Group 1 review patches'
type: 'bugfix'
created: '2026-09-08'
status: 'done'
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
- [x] `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- if already parked, log 4503 and return without writing; copy-on-write the registry `HashSet`; set index/registry/parking `ProjectionType` to new `WorksReadModelKeys` tokens -- Group 1 MEDIUM parked-log, ETag-mutate, and attribution holes
- [x] `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs` -- return `Malformed` on identity mismatch; catch `NotSupportedException` like `WorksEventDecoder`; move `SkippedEvent` to EventId 4504 -- stop immortal `/project` 500s and EventId collapse
- [x] `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- refuse reserved tenant on every command `Handle` -- close the `/process` ingress `/project` already rejects
- [x] `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs` -- already-parked 4503 path; identity/`NotSupportedException` parks; registry retry keeps both tenants without in-place mutate -- prove dispatcher/decoder patches
- [x] `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- reserved tenant before marker acquire -- deleting the branch must fail
- [x] `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs` -- `ProcessAsync(CreateWorkItem)` with tenant `tenants` throws and writes nothing -- `/process` hole
- [x] `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs` -- resolved source is `IndexedPendingDateAwaitSource`; invalid `Works:Projection` fails ValidateOnStart; bound Max=1 is what `/project`/dispatcher uses -- host wiring
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` and `sprint-status.yaml` -- check Group 1 patches; Status/`development_status` = `review`; leave DW-56 open -- story close-out after patches

**Acceptance Criteria:**
- Given a parked aggregate is redispatched, when `/project` runs, then it is acknowledged with EventId 4503 and `FailureCount` is unchanged.
- Given a state-affecting event has a foreign identity or throws `NotSupportedException`, when `/project` decodes it, then parking starts and the endpoint does not throw before `ParkOrRetryAsync`.
- Given tenant id `tenants`, when `/process` or `/work/events` is invoked, then the command/event is refused before persist or marker acquire.
- Given an ETag retry on the pending-date registry, when two tenants register, then both survive and the transform does not mutate the store instance.
- Given recovery services are built, when `IPendingDateAwaitSource` is resolved, then the concrete type is `IndexedPendingDateAwaitSource`.
- Given `Works:Projection:MaxUndecodableEventDispatchesBeforeParking` is bound, when the host starts, then invalid values fail ValidateOnStart and `/project` uses the bound budget.
- Given Group 1 patches and their tests land, when bookkeeping is updated, then the story and sprint say `review` and DW-56 stays open.

## Implementation Notes

- Parked-candidate skip (EventId 4607) landed in `0527d12` after spec baseline `6e2fb4da`; kept, not rewritten.
- Web STJ does not throw `NotSupportedException` for `WorkItemSuspended` poison bytes; `A_not_supported_decode_failure_is_malformed_and_parks` pins `IsHandledDecodeFailure` and parks via the same malformed `/project` path.
- Reserved-tenant `/process` is proven by `ProcessAsync` throwing `InvalidOperationException` before a `DomainResult` (no store write).
- Verification (this session): IntegrationTests 77/77 on the five named classes; ArchitectureTests 237/237; both Release builds 0 warnings. Live reminder/mTLS lanes not run. DW-56 left open.


## Spec Change Log

## Review Triage Log

| ID | Verdict | Route | Evidence |
| --- | --- | --- | --- |
| B1 | false | reject | Group 1 working tree vs HEAD does not edit PRD files. Those hunks are in `0527d12`, not this spec's patches. |
| B2 | false | reject | `references/Hexalith.Parties` is not in the Group 1 working-tree diff vs HEAD. |
| B3 | false | reject | `architecture-validation-2026-09-08/` is untracked and was not written by this implementation. |
| B4 | false | reject | `epic-4-context.md` was regenerated in Build step 01 because planning artifacts were newer; it is not a Group 1 host-edge change. |
| B5 | low | reject | File List omits already-committed parked-skip files. Cosmetic story bookkeeping; everyday operators do not meet it. |
| B6 | false | reject | `sprint-status.yaml` sets `4-8-register-and-reconcile-date-reminders-durably` to `review`. |
| B7 | false | reject | Live `/process` runs EventStore `TenantValidator`: envelope/actor tenant must equal command tenant, so a reserved envelope with a legal command tenant cannot persist. `ProcessAsync` throws before a `DomainResult`. |
| B8 | low | reject | `NotSupportedException` is in `IsHandledDecodeFailure`; Web STJ never throws it for these bytes. Adding a Decode mock is more than a direct correction. |
| B9 | low | reject | Identity mismatch parks via 4501/4502. Spec does not require 4504 on that branch; operators still see the park log. |
| B10 | low | reject | New `ProjectionType` tokens are used on index/registry/parking writes. Proving them needs extra conflict-log machinery. |
| B11 | low | reject | EventId 4607 is defined and called; the parked-skip facts prove skip behavior, not the numeric id. |
| B12 | false | reject | Fix would be editing this spec's Code Map. Rejected. |
| B13 | false | reject | The host adapter already owns Projections. Fitness 237/237; no new forbidden token. |
| B14 | false | reject | `/project` uses `GetRequiredService<IOptions<WorksProjectionOptions>>()`. Constructor `?? new` is only the test/null fallback. |
| B15 | false | reject | Already-parked `ParkOrRetryAsync` returns true and `DispatchAsync` returns `NotEligibleResponse()` before index or what's-next writes. |
| B16 | false | reject | Empty triage/change-log headings are this step's input; fixing them by editing the spec is rejected. |
| E1 | medium | defer | Pre-existing: `ThrowIfReservedTenantId` is Ordinal on the raw request, then `new TenantId` lowercases. `TENANTS` misses the guard. EventStore poller already sends lowercase stream identity. |
| E2 | false | reject | Same as B7: EventStore `TenantValidator` rejects envelope/command tenant mismatch before Handle. |
| E3 | false | reject | A parking `Get` failure is a store error and must mark the scan incomplete. Park-after-read is one extra pass, then the next scan skips. Matches the skip-without-tombstone decision. |
| E4 | false | reject | Duplicate of E2. |

## Design Notes

`EventStoreAggregate.ProcessAsync` is not virtual, so reserved-tenant refusal belongs in every adapter `Handle`, not a base override. First-park stays 4502 (`FailureCount == max` on the transition); later passes must detect `Parked` before incrementing. Decoder skip (4504) is "unknown/blank type"; 4501 remains consecutive decode-failure retry.

## Verification

**Commands:**
- `DOTNET_CLI_HOME=/tmp dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -m:1` -- expected: 0 warnings / 0 errors
- Run the IntegrationTests binary with `-class` for `PendingDateAwaitIndexDispatcherTests`, `IndexedPendingDateAwaitSourceTests`, `WorksDomainEventProcessorTests`, `WorksRecoveryOptionsTests`, and `LinkConversationRuntimeAdapterTests` -- expected: all invoked facts pass
- `DOTNET_CLI_HOME=/tmp dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -m:1` then run that binary -- expected: fitness still green (catalog 40, no new runtime tokens outside the host)

Do not run the live reminder or mTLS smoke lanes.
