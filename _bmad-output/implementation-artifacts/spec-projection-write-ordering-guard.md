---
title: 'Guard projection write ordering'
type: 'bugfix'
created: '2026-08-29'
status: 'done'
baseline_revision: '2c4870c82650a3045032c5a14123ee0e652fe2d6'
review_loop_iteration: 0
followup_review_recommended: true
context: []
warnings: [oversized]
deferred:
  - summary: >-
      Pre-change ineligible index state can lack a durable accepted-sequence watermark after the old index-first partial-failure window.
    evidence: |-
      Before this bundle, an ineligible replay removed the tenant-index item before unconditionally saving the roll-up and retained no tombstone. A crash between those writes can leave no record of the newer ineligible sequence, so a later older eligible replay cannot be distinguished without a migration or backfill decision.
    location: >-
      src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:145
    severity: medium
  - summary: >-
      The retained tenant-index sequence watermark is never pruned, so the single per-tenant what's-next document grows by one permanent entry per work item ever projected.
    evidence: |-
      WorksWhatsNextTenantIndex.LastSequences keeps an entry after the item leaves the eligible set and nothing removes it. Before this bundle the document was bounded by the currently eligible item count. It is read, copied, and rewritten on every dispatch for the tenant and read on every what's-next query, so a long-lived tenant eventually meets the state-store value-size limit. The sibling PendingDateAwaitTenantIndex has the same unbounded shape, so a retention policy (cap, TTL, or compaction) is a cross-index design decision rather than a local fix.
    location: >-
      src/Hexalith.Works/Projections/WorksWhatsNextReadModel.cs:57
    severity: medium
  - summary: >-
      A refused stale replay still issues a conditional write through ReadModelWritePolicy, bumping the key's ETag instead of skipping the store round-trip.
    evidence: |-
      ReadModelWritePolicy.UpdateAsync unconditionally calls TrySaveAsync with whatever the transform returns, so both ordering guards refuse at the value surface only: the persisted document is unchanged, but the version advances and a concurrent legitimate writer can lose an attempt from its bounded retry budget. This is the pre-existing platform contract (the old unconditional SaveAsync also wrote), so the change adds no write it did not already make; suppressing the write needs a no-op signal in the EventStore write policy, which this story's Block If fences off.
    location: >-
      references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/ReadModelWritePolicy.cs:78
    severity: low
  - summary: >-
      The sibling pending-date-await index transform still mutates the instance it loaded, so a rejected write can leave that mutation visible in a reference-returning store.
    evidence: |-
      MaintainPendingDateAwaitIndexAsync does `PendingDateAwaitTenantIndex index = current ?? new(...)` and then mutates `index` in place inside ReadModelWritePolicy's retry transform. The what's-next transform was converted to build replacement dictionaries on every retry precisely so a rejected attempt cannot leak into loaded state; the pending-date sibling was left on the older pattern because this story's Never clause fences off pending-date behavior. No test depends on it today (RejectNextTrySaves is never armed on that key), but the two sibling transforms now disagree on a semantics the docs claim for the index family.
    location: >-
      src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:330
    severity: low
  - summary: >-
      The /project response still echoes the freshly computed item even when both ordering guards refused the replay, so a caller can receive state that was never persisted.
    evidence: |-
      DispatchAsync serializes `item` into ProjectionResponse unconditionally, and `indexAccepted` is computed and then discarded. Before this change the writes were unconditional, so the response always matched persisted state; a refused stale replay now returns a document the store does not hold, with no field distinguishing accepted from refused. Adding an acceptance signal changes the projection response contract, which this story's Block If fences off.
    location: >-
      src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:179
    severity: low
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

### 2026-08-29 — Design Notes correction (no implementation loopback)

- **Triggering finding:** the Design Notes prescribed "compare the greatest of `LastSequences[id]` and any legacy `Items[id].LatestAcceptedSourceSequence`". The implementation followed it with an unconditional `Math.Max`, which the I/O matrix's "Legacy eligible index" row does not ask for — that row scopes the item watermark to the case where the tombstone map is absent or empty.
- **What was amended:** the Design Notes comparison sentence now states the item watermark is a fallback for a missing retained entry, never a competing maximum, and says why (the two watermarks come from different projections).
- **Known-bad state avoided:** the two projections' accept filters disagree on a `ChildSpawned` carrying no child id (accepted by `WhatsNextQueueProjection`, refused by `WorkItemRollUpTenantIsolation`'s identity registry). Under `Math.Max` such a stream leaves the stored item permanently ahead of the roll-up watermark the guard compares against, so every later replay of that item is refused — its index entry and its notifications freeze until an event both projections accept catches the roll-up up.
- **KEEP:** the roll-up-first write ordering and its strict-newer skip; the ETag-transform placement of both comparisons; replacement-dictionary construction on every retry; equal-sequence acceptance for deterministic refresh; retention of `LastSequences[id]` across eligibility removal; and the full existing regression set.
- The amendment describes the corrected behavior the patch in the same pass implements; no code was reverted and no re-derivation was required.

## Review Triage Log

### 2026-08-29 — Review pass

- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 0, medium 4, low 3)
- defer: 1: (high 0, medium 1, low 0)
- reject: 12: (high 0, medium 4, low 8)
- addressed_findings:
  - `[medium]` `[patch]` Added independent tombstone-only stale-replay coverage, including persisted query state.
  - `[medium]` `[patch]` Added deterministic shared tenant-index ETag-conflict coverage for two distinct work-item ids.
  - `[medium]` `[patch]` Verified `LastSequences` JSON round-trip behavior and compatibility when legacy JSON omits the property.
  - `[medium]` `[patch]` Proved equal-sequence redispatch repairs the index after non-atomic index retry exhaustion.
  - `[low]` `[patch]` Gated projection notifications on acceptance by the tenant-index ordering guard.
  - `[low]` `[patch]` Tightened stale-replay write observations to prove roll-up-first ordering and a skipped index write.
  - `[low]` `[patch]` Corrected additive-rollout documentation to describe a watermark missing for one aggregate id.

### 2026-08-29 — Review pass (follow-up)

- intent_gap: 0
- bad_spec: 0
- patch: 4: (high 0, medium 3, low 1)
- defer: 2: (high 0, medium 1, low 1)
- reject: 21: (high 0, medium 5, low 16)
- addressed_findings:
  - `[medium]` `[patch]` Added the accepting-edge notifier proof (`Accepted_replay_notifies_once_and_a_later_stale_replay_does_not`): the new `indexAccepted` gate had only a negative assertion, so a permanently false gate silenced every subscriber with the whole suite green. Mutation-verified — forcing `incomingAccepted = false` fails this test and only this test.
  - `[medium]` `[patch]` Added `Older_terminal_replay_cannot_delete_newer_eligible_index_entry`: the deletion half of the intent's problem statement (a stale replay that ends terminal removing a newer eligible index entry) had no coverage; every stale-replay test used an eligible stale replay.
  - `[medium]` `[patch]` Scoped the CHANGELOG and `docs/eventstore-api-surface-constraints.md` claim: runtime `/project` persistence is not monotonic on accepted envelope positions as a whole — the pending date-await index the same dispatch maintains keeps its pre-existing raw stream-sequence watermark. Also recorded the additive serialized `lastSequences` member.
  - `[low]` `[patch]` Corrected the guard's documented scope in `WorksWhatsNextReadModel` XML docs and `docs/whats-next-projection.md` (it refuses older replays while the item is still present, not only after removal), described the legacy fallback as the implemented greater-of comparison, and repaired an orphaned line wrap in `docs/eventstore-api-surface-constraints.md`.

### 2026-08-29 — Review pass (follow-up 2)

- intent_gap: 0
- bad_spec: 0
- patch: 4: (high 0, medium 2, low 2)
- defer: 2: (high 0, medium 0, low 2)
- reject: 20: (high 0, medium 5, low 15)
- addressed_findings:
  - `[medium]` `[patch]` Made the legacy item watermark a fallback for a missing `LastSequences` entry instead of an unconditional `Math.Max`. Three review layers independently found that the two watermarks come from different projections whose accept filters disagree on a `ChildSpawned` with no child id, so `Math.Max` let a stored item outrank its own tombstone and freeze that item's index entry and notifications permanently. Mutation-verified — restoring `Math.Max` fails the new test and only that test.
  - `[medium]` `[patch]` Added `Newer_replay_over_a_retained_tombstone_restores_eligibility`: every prior tombstone test proved refusal, so the forward edge (an item leaving and re-entering the eligible set at a newer sequence) had no coverage — a guard that refused it would have evicted the item from the tenant queue permanently with the suite green.
  - `[low]` `[patch]` Documented the narrowed notification contract. `docs/whats-next-projection.md` still said the adapter notifies "only when `Changed` is set"; after the `indexAccepted` gate it was the last stale statement of the notifier seam. Also recorded the narrowing in the CHANGELOG.
  - `[low]` `[patch]` Repaired the 152-character mid-sentence line this change introduced in `docs/whats-next-projection.md` and aligned the fallback wording across the CHANGELOG, both docs, and the `LastSequences` XML docs.

## Design Notes

Persist the roll-up first. Its document is its own watermark; if the policy returns a strictly newer stored sequence, skip this replay's tenant-index write. Otherwise update the index under its own ETag policy. Compare `LastSequences[id]`, falling back to a legacy `Items[id].LatestAcceptedSourceSequence` only when that id has no retained entry — the two watermarks are produced by different projections, so the item is a compatibility fallback, never a competing maximum. Only a strictly greater stored sequence refuses the incoming model, allowing equal-sequence redispatch to refresh deterministic pre-change documents. Build replacement dictionaries inside retry transforms and retain `LastSequences[id]` when removing eligibility. The keys remain separate, non-atomic read models that converge on replay.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:NuGetAudit=false -m:1 -v:minimal` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorkItemProjectionQueryAdapterTests` -- expected: all ordering, tombstone, conflict, and existing adapter tests pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.PendingDateAwaitIndexDispatcherTests` -- expected: existing tombstone/index behavior remains green.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemRollUpProjectionTests` -- expected: pure recursive behavior remains green.
- `git diff --check` -- expected: no whitespace errors.

## Auto Run Result

Second follow-up review pass over the same baseline (`2c4870c`). No intent gap and no spec defect requiring re-derivation. Four patches were applied: one real behavioral defect in the guard's legacy-compatibility branch, one missing forward-edge proof, and two documentation corrections about the change's own contract.

The behavioral patch is the substantive one. Three of the four review layers independently converged on the same seam: the index guard compared the incoming roll-up watermark against `Math.Max(LastSequences[id], Items[id].LatestAcceptedSourceSequence)`, but those two watermarks are produced by different projections. `WhatsNextQueueProjection.EventMatchesDelivery` accepts a `ChildSpawned` on tenant/work-item match alone, while `WorkItemRollUpTenantIsolation`'s identity registry refuses one whose `ChildWorkItemId` is null (reachable because `ChildWorkItemId` is a non-`required` positional member, so a partial persisted payload decodes it as null). For such a stream the stored item sits permanently ahead of the roll-up watermark the guard compares against, and `Math.Max` then refuses every subsequent replay of that item — freezing its index entry and suppressing its notifications until an event both projections accept caught the roll-up up. The intent's "Legacy eligible index" matrix row never asked for a maximum; it scopes the item watermark to the case where the tombstone map has no entry. The fix restores that scoping.

Files changed in this pass:

- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` — the legacy item watermark is now consulted only when the aggregate id has no `LastSequences` entry; the comment records why the two watermarks cannot be maximised over.
- `src/Hexalith.Works/Projections/WorksWhatsNextReadModel.cs` — XML docs only: a retained entry is the sole ordering authority for its id.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` — adds `Stored_item_ahead_of_its_retained_sequence_does_not_freeze_the_index` and `Newer_replay_over_a_retained_tombstone_restores_eligibility` (27 → 29 tests in this class).
- `docs/whats-next-projection.md` — fallback-not-maximum wording, the narrowed notification contract, and the repaired 152-character line wrap.
- `docs/eventstore-api-surface-constraints.md` — same fallback scoping.
- `CHANGELOG.md` — records that a retained entry is the sole authority and that a refused stale replay announces nothing.
- `_bmad-output/implementation-artifacts/spec-projection-write-ordering-guard.md` — Design Notes correction plus its Spec Change Log entry, triage log, two new deferred items, this result.

Review findings: 4 patches applied (high 0, medium 2, low 2), 2 items deferred (high 0, medium 0, low 2), 20 items rejected (high 0, medium 5, low 15). Follow-up review recommendation: `true`; patch score `8` (`3 × 2 medium + 1 × 2 low`).

Verification:

- The pinned `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:NuGetAudit=false -m:1 -v:minimal` command still fails to load: `global.json` pins SDK 10.0.301 and this environment provides only 10.0.400. Unchanged pre-existing environment constraint.
- Integration and unit projects built in Release through `/home/administrator/.dotnet/sdk/10.0.400/MSBuild.dll` with zero warnings and zero errors.
- `WorkItemProjectionQueryAdapterTests`: 29 passed, 0 failed, 0 skipped.
- `PendingDateAwaitIndexDispatcherTests`: 11 passed, 0 failed, 0 skipped.
- Full `Hexalith.Works.UnitTests` suite (includes `WorkItemRollUpProjectionTests`): 528 passed, 0 failed, 0 skipped.
- Mutation check on the behavioral patch: restoring the `Math.Max` comparison fails `Stored_item_ahead_of_its_retained_sequence_does_not_freeze_the_index` and no other test (29 total, 1 failed); the fix was restored and rebuilt before the final run.
- `git diff --check`: clean.

Residual risks: unchanged from the implementation pass (pinned-SDK mismatch, intentionally non-atomic two-key boundary repaired by equal-sequence replay, legacy pre-change index state without a surviving watermark — DW-67), plus the unbounded `LastSequences` growth and the refused-replay conditional write already recorded as DW-68/DW-69. Two further deferred items record that the sibling pending-date-await transform still mutates its loaded instance in place, and that the `/project` response echoes computed state even when both guards refused — both need decisions this story's Block If fences off (pending-date behavior, and the projection response contract). The divergence that motivated this pass's behavioral patch is itself only reachable from a malformed persisted `ChildSpawned`; the guard now degrades to a refreshable index rather than a frozen one when it occurs, but the underlying payload-validation asymmetry between the two projections is untouched.
