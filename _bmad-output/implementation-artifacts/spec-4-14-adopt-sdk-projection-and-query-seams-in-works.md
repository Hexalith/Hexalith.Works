---
title: 'Adopt SDK Projection and Query Seams in Works'
type: 'feature'
created: '2026-09-23'
updated: '2026-10-13'
status: 'ready-for-dev'
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

- [ ] `Directory.Packages.props`, `Directory.Build.props`, `src/Hexalith.Works/Hexalith.Works.csproj` — freeze EventStore 3.120.0 and host-only Parties.Client 1.2.1; preserve source-Debug/package-Release modes.
- [ ] `Runtime/WorksHost.cs`, `Runtime/WorksProjectionOptions.cs` — default legacy; opt in exclusively to SDK named delivery and matching readers; preserve reminder/Reactor composition.
- [ ] `Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs` — implement epoch scope and per-envelope catch-up using SDK generation reads/logical mutations and bounded domain fold context.
- [ ] `Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs`, `Projections/WorksReadModelKeys.cs` — stage complete family, including topology, cleared-await tombstones and parking; preserve rollback evidence and reject partial promotion.
- [ ] `Queries/WorksQueryPolicy.cs`, `Queries/WorksQueryOptions.cs` — verify Party binding through published Parties client and authenticated gateway transport; empty coordinator allowlist denies; restrict SDK query ingress to gateway origin.
- [ ] `Queries/WhatsNextQueryHandler.cs`, `Queries/GetWorkItemQueryHandler.cs` — authorize before reading, reuse pure result predicates, and emit SDK freshness metadata.
- [ ] `Reminders/IndexedPendingDateAwaitSource.cs`, `Recovery/Cascade/StreamReadingCascadeDescendantSource.cs` — SDK tenant discovery and pinned related reads; preserve stream truth/partial failures.
- [ ] `tests/Hexalith.Works.IntegrationTests/` — extend named classes in context; prove matrix with durable model/checkpoint assertions, package-only consumer, and Dapr/Redis state.
- [ ] `docs/eventstore-api-surface-constraints.md`, `docs/boundary-decision-record.md`, `docs/sdk-projection-query-adoption.md` — document configuration, exact producer/consumer evidence, and complete restore rollback.

**Acceptance Criteria:**

- Given SDK mode, when delivery overlaps rebuild and restart/redelivery, then persisted family state/checkpoints converge without lost or duplicate logical updates.
- Given verified Executor or permitted coordinator authority, when queries execute, then only permitted results return with truthful freshness; denied requests perform no Works state read/disclosure.
- Given isolated migration fixtures, when mode selection and restore rollback run, then one writer is active and legacy state remains recoverable.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

SDK delivery supplies one envelope, not full history. Retain bounded domain fold context and use a common document representation for heterogeneous pinned reads. Authority transport failure never falls back to identity equality.

## Verification

Run the exact commands/classes in context: source Debug build, isolated Release/package consumer, each test project individually, and persisted provider proof. Record package/source identities, R3–R5 evidence, and remaining Platform/AD-28 gates.
