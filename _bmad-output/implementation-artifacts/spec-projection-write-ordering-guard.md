---
title: 'Guard projection write ordering'
type: 'bugfix'
created: '2026-08-29'
status: 'in-progress'
baseline_revision: '2c4870c82650a3045032c5a14123ee0e652fe2d6'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Delayed older `/project` full replays can overwrite a newer per-item roll-up or delete/resurrect newer what's-next index state. The roll-up write is also unconditional despite the documented EventStore optimistic-concurrency policy.

**Approach:** Make both persisted read-model paths monotonic on `LatestAcceptedSourceSequence`, move the roll-up under `ReadModelWritePolicy`, and retain a tenant-index sequence watermark after an item becomes ineligible so stale replays cannot mutate either key.

## Boundaries & Constraints

**Always:** Compare accepted EventStore envelope positions before mutation; preserve tenant-scoped keys, reliable own effort/status/structure, boundary-safe rolled-total refusal, and retry-safe idempotence; verify persisted end state, not only responses or calls.

**Block If:** Correctness requires a public contract change, an EventStore/submodule change, a migration/backfill decision, or atomic coordination across the roll-up and tenant-index keys.

**Never:** Edit the deferred-work ledger; use unconditional `SaveAsync` for the roll-up; use request arrival order or the raw stream high-watermark; weaken tenant/query isolation or the pure projection; broaden into pending-date, notifier, or cross-aggregate reconciliation behavior.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Older ineligible replay | Eligible item and roll-up at accepted sequence 3, then created-only replay at sequence 1 | Both persisted models remain at sequence 3; the index item is not deleted | Stale replay is an idempotent persistence no-op |
| Older eligible replay | Terminal roll-up/index tombstone at sequence 3, then eligible replay at sequence 2 | Roll-up stays terminal; index stays absent with tombstone 3 | Stale replay cannot resurrect eligibility |
| ETag conflict | Older and newer replays race on the same scoped keys | Retry reloads persisted state and the greatest accepted sequence wins | Existing policy exhausts and throws after its bounded retry budget |
| No accepted model | Empty or rejection-only replay follows authoritative state | Neither roll-up nor index is mutated | No ordering authority is inferred |
| Legacy eligible index | Existing item has a sequence but the new tombstone map is absent/empty | Existing item sequence participates in the comparison | Older replay remains refused during additive rollout |

</intent-contract>

## Code Map

- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:89` -- replay adapter; currently writes the index first, then calls unconditional `SaveAsync`. Persist the roll-up through `ReadModelWritePolicy`, use its persisted sequence as a strict-newer guard, and pass the incoming accepted sequence into the index policy.
- `src/Hexalith.Works/Projections/WorksWhatsNextReadModel.cs:39` -- host-edge STJ tenant index; add a retained per-item `LastSequences` watermark analogous to the pending-date index.
- `src/Hexalith.Works/Projections/PendingDateAwaitIndex.cs:17` -- read-only local precedent for a tombstone watermark that survives entry removal.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/ReadModelWritePolicy.cs:53` -- read-only retry contract: reload, idempotent transform, ETag `TrySaveAsync`, three attempts by default.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs:30` -- persisted-state/query harness; add sequential stale, tombstone, empty replay, and optimistic-conflict regressions.
- `tests/Hexalith.Works.IntegrationTests/Story47InMemoryReadModelStore.cs:73` -- deterministic exact-key conflict coordination; transforms must return replacement models to avoid fake reference aliasing.
- `docs/eventstore-api-surface-constraints.md:74`, `docs/work-roll-up-projection.md:56`, `docs/whats-next-projection.md:53` -- document monotonic independently guarded writes and retained index watermarks without claiming cross-key atomicity.

## Tasks & Acceptance

**Execution:**
- `src/Hexalith.Works/Projections/WorksWhatsNextReadModel.cs` -- add the additive sequence-tombstone dictionary and document its eligibility-removal semantics.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- make roll-up and index transforms accept only non-older models under ETag retry, skip all persistence for a missing accepted model, and keep equal-sequence redispatch able to refresh deterministic documents.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` -- cover every matrix row, including deterministic same-key ETag conflict and persisted roll-up/index/tombstone assertions.
- `docs/eventstore-api-surface-constraints.md`, `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md`, `CHANGELOG.md` -- record the ordering guarantee, compatibility fallback, and non-atomic two-key boundary.

**Acceptance Criteria:**
- Given any completed set of stale/current same-aggregate dispatches, when persisted state is read, then both keys expose the greatest accepted source sequence and matching newer domain evidence.
- Given colliding inner ids in different tenants, when either ordering guard runs, then each tenant's roll-up, index item, and tombstone remain isolated.
- Given an optimistic conflict, when retries succeed, then no unconditional fallback occurs; when retries exhaust, the existing contextual policy failure propagates.

## Spec Change Log

## Review Triage Log

## Design Notes

Persist the roll-up first. Its document is its own watermark; if the policy returns a strictly newer stored sequence, skip this replay's tenant-index write. Otherwise update the index under its own ETag policy. Compare the greatest of `LastSequences[id]` and any legacy `Items[id].LatestAcceptedSourceSequence`; only a strictly greater stored sequence refuses the incoming model, allowing equal-sequence redispatch to refresh deterministic pre-change documents. Build replacement dictionaries inside retry transforms and retain `LastSequences[id]` when removing eligibility. The keys remain separate, non-atomic read models that converge on replay.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:NuGetAudit=false -m:1 -v:minimal` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorkItemProjectionQueryAdapterTests` -- expected: all ordering, tombstone, conflict, and existing adapter tests pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.PendingDateAwaitIndexDispatcherTests` -- expected: existing tombstone/index behavior remains green.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemRollUpProjectionTests` -- expected: pure recursive behavior remains green.
- `git diff --check` -- expected: no whitespace errors.
