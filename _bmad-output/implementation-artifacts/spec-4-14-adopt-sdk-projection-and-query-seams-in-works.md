---
title: 'Adopt SDK Projection and Query Seams in Works'
type: 'feature'
created: '2026-09-23'
updated: '2026-10-13'
status: 'in-progress'
baseline_commit: '8708bb935c4306c42d7453b761e203682aad5919'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '_bmad-output/implementation-artifacts/evidence/story-4-14/planning.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Works owns projection delivery and generation selection; queries enforce tenant scoping without Executor authorization. Rebuilds omit pending-date and parking state.

**Approach:** Put Works folds and query policy behind published SDK contracts, proving R3–R5 consumer behavior with persisted synthetic state.

## Boundaries & Constraints

**Always:** Consume EventStore 3.120.0, retaining Story 4.10's published contract. SDK owns validation, fencing, persistence, checkpoints, and generation selection; Works owns folds and query policy. One tenant/family `works-runtime-v3` contains roll-up, queue, topology, pending intents, and parking. Family equals handler projection type. Reads stay generation-pinned and report stale/unavailable. Executor requests require authoritative current Party binding and return own Assigned work plus tenant Queued work; all-work requires an explicitly permitted trusted workload. Missing authority denies before Works state access.

**Never:** Acknowledge through two writers; infer PartyId from UserId; trust payload privileges; disclose foreign results; skip corrupt state-affecting evidence. Preserve legacy hosting and complete pre-cutover restore until 4.16. Exclude production activation, host removal, reminder/process migration, new durable catalog types, and non-synthetic admission; AD-28 gates remain.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Live/redelivery/restart | Persisted folds and checkpoints converge once logically. |
| Concurrent rebuild | Prior generation serves marked-stale reads; full promotion/catch-up restore freshness; abort drains safely. |
| Executor/coordinator query | Only permitted tenant results, ordered deterministically. |
| Wrong actor/tenant/origin | No unauthorized state access, writes, or disclosure. |
| Corrupt/gapped/conflicting evidence | Durable unresolved/quarantine disposition and degraded readiness. |
| Rollback | Restore pre-cutover store/fleet and replay source; no in-place v2 downgrade. |

</frozen-after-approval>

## Code Map

Implementation paths below are relative to `src/Hexalith.Works/`, except repository-root configuration and tests. Expanded map and published API evidence are in the context file.

- `Runtime/WorksHost.cs` — SDK discovery plus bespoke projection writer.
- `Projections/SharedRebuild/` — reuse history/relationship folds; ordinary delivery returns UnsupportedCapability and manifests omit recovery state.
- `Projections/WorkItemProjectionDispatcher.cs`, `Reminders/PendingDateAwaitProjection.cs` — reuse decoders, domain transformations, and tombstones.
- `Queries/`, reminder and cascade readers — replace schema-key selection; retain authoritative stream refolds/exclusive cursors.

## Tasks & Acceptance

**Execution:**

- [x] `Directory.Packages.props`, `Directory.Build.props`, `src/Hexalith.Works/Hexalith.Works.csproj` — freeze EventStore 3.120.0 and host-only Parties.Client 1.2.1; preserve source-Debug/package-Release modes.
- [x] `Runtime/WorksHost.cs`, `Runtime/WorksProjectionOptions.cs` — default legacy; opt in exclusively to SDK named delivery and matching readers; preserve reminder/Reactor composition.
- [x] `Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs` — implement epoch scope and per-envelope catch-up using SDK generation reads/logical mutations and bounded domain fold context.
- [x] `Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs`, `Projections/WorksReadModelKeys.cs` — stage complete family, including topology, cleared-await tombstones and parking; preserve rollback evidence and reject partial promotion.
- [ ] `Queries/WorksQueryPolicy.cs`, `Queries/WorksQueryOptions.cs` — verify Party binding through published Parties client and authenticated gateway transport; empty coordinator allowlist denies; restrict SDK query ingress to gateway origin.
- [x] `Queries/WhatsNextQueryHandler.cs`, `Queries/GetWorkItemQueryHandler.cs` — authorize before reading, reuse pure result predicates, and emit SDK freshness metadata.
- [x] `Reminders/IndexedPendingDateAwaitSource.cs`, `Recovery/Cascade/StreamReadingCascadeDescendantSource.cs` — SDK tenant discovery and pinned related reads; preserve stream truth/partial failures.
- [ ] `tests/Hexalith.Works.IntegrationTests/` — extend named classes in context; prove matrix with durable model/checkpoint assertions, package-only consumer, and Dapr/Redis state.
- [x] `docs/eventstore-api-surface-constraints.md`, `docs/boundary-decision-record.md`, `docs/sdk-projection-query-adoption.md` — document configuration, exact producer/consumer evidence, and complete restore rollback.

**Acceptance Criteria:**

- Given SDK mode, when delivery overlaps rebuild and restart/redelivery, then persisted family state/checkpoints converge without lost or duplicate logical updates.
- Given verified Executor or permitted coordinator authority, when queries execute, then only permitted results return with truthful freshness; denied requests perform no Works state read/disclosure.
- Given isolated migration fixtures, when mode selection and restore rollback run, then one writer is active and legacy state remains recoverable.

## Implementation Notes

- The SDK mode and published-package consumer compile; ordinary delivery, rebuild, restart, malformed/gapped evidence, protected HTTP ingress, and isolated Redis restore/replay have focused proof. The legacy writer remains the default.
- Executor policy fails closed with the current forwarding transport: EventStore 3.120.0 removes the caller Authorization header before invoking Works. Published Parties.Client can carry an independently configured service credential through its injected HttpClient, and EventStore exposes identity-admission seams, but the existing gateway and trusted-reader policies do not compose a supported service-to-service identity-read path. The required authoritative current binding remains unproved end to end; a package-version change is not inherently required.
- The protected SDK receiver additionally rejects any Authorization header alongside a workload assertion with HTTP 401. The executable HTTP test proves this denial occurs before Works state access. Published-client success, lifetime, and custody checks are proven separately through synthetic HTTP authority at the adapter-policy boundary; they do not close the end-to-end transport gap.
- Ordinary delivery now preserves pinned persisted parking dispositions during tenant refolds. Healthy delivery for another aggregate cannot clear terminal parking or degraded query metadata; complete authoritative rebuilds may reset parking.
- The isolated restore fixture verifies the Works state/configuration sequence. A deployed fleet and source archive remain an admission gate for Story 4.16.

## Spec Change Log

## Review Triage Log

## Design Notes

SDK delivery supplies one envelope, not full history. Retain bounded domain fold context and use a common document representation for heterogeneous pinned reads. Authority transport failure never falls back to identity equality.

## Verification

- Follow-up implementation evidence and exact commands are recorded in [evidence/story-4-14/implementation-follow-up.md](evidence/story-4-14/implementation-follow-up.md). The authority task remains incomplete; the [matrix audit](evidence/story-4-14/matrix-audit.md) also records the missing durable conflict-disposition proof.
- `dotnet build Hexalith.Works.slnx --configuration Release --no-restore -warnaserror -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0`: passed, zero warnings/errors (package mode).
- Final focused SDK/context verification passed 172/172. `tests/Hexalith.Works.IntegrationTests/run-sdk-rollback-restore.sh` passed; Release integration build zero warnings/errors, live Dapr/Redis SDK projection class 15/15, isolated restore/replay 1/1. Both new parking and concurrent named-delivery regressions ran against Redis.
- Focused Release integration classes `WorksSdkProjectionQueryTests`, `WorksSdkProtectedHttpTests`, `WorksDomainEventSubscriptionTests`, and `WorksRecoveryOptionsTests`: 39/39 passed after the query predicate fix.
- `WorksSdkRecoverySourceTests`: 5/5 passed; the implementation agent also reported 119/119 related existing and adjacent recovery tests passing. `WorksReminderRecoveryPipelineSmokeTests` was retried with `timeout 120s` and exited 124 after reporting a prerequisite failure: AppHost control-plane ports 50001, 51005, and 51006 plus a scheduler volume were occupied after 60 seconds. The existing resources were left untouched.
- The implementation agent reported UnitTests 568/568, PropertyTests 3/3, and architecture dependency-direction checks 24/24 passing. Full ArchitectureTests passed 268/268 after updating stale root-submodule and EventStore source-shape expectations; a full unfiltered IntegrationTests run stalled before completion. `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build src/Hexalith.Works/Hexalith.Works.csproj --configuration Debug --no-restore -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -v:q` exited 1: CS1704 duplicate PolymorphicSerializations assemblies and CS0246 missing identity-evidence types in local Parties.Contracts (11 errors, 1 warning).
- Published EventStore 3.120.0 and Parties.Client 1.2.1 are pinned in `Directory.Packages.props`. Platform/AD-28 and deployed rollback gates remain open.
