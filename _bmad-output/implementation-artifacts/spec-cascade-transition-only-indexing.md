---
title: 'Cascade transition-only indexing'
type: 'bugfix'
created: '2026-08-27'
status: 'done'
baseline_revision: 'eec4ce9359af4f6b9cc5ad34d593f91e9b3788f4'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/.bmad-loop/runs/20260827-214141-f7db/bundles/cascade-transition-only-indexing/intent.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Every incomplete cascade checkpoint save currently rewrites the single global cross-tenant discovery index. A large or hot cascade therefore creates O(2N) ETag contention that can exhaust retries and disrupt unrelated tenant cascades.

**Approach:** Make index writes follow checkpoint lifecycle transitions: publish discovery before the first incomplete checkpoint becomes durable, persist target progress without touching discovery, and remove discovery only after the completed checkpoint is durable. Prove write counts, restart convergence, crash-safe ordering, and concurrent identity preservation with focused integration tests.

## Boundaries & Constraints

**Always:** Preserve the existing checkpoint identity/key and shape, ETag-safe global-index merge, replay-without-rediscovery behavior, deterministic correlation IDs, and Attempted-before-submit ordering. Keep all persistence behind `IReadModelStore` and `ReadModelWritePolicy`, use `ConfigureAwait(false)` in production, and assert persisted read-model state in integration tests.

**Block If:** The transition cannot be derived from the currently durable checkpoint without changing the checkpoint contract or weakening the index-before-incomplete / completed-before-index-removal ordering.

**Never:** Shard or rename the index, change aggregate/reactor behavior, introduce a new persistence mechanism, modify any `references/` submodule, or edit `deferred-work.md` or any deferred-work ledger.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| First incomplete checkpoint | No durable checkpoint; incoming checkpoint is incomplete | Add identity to discovery before saving the checkpoint; one index write | If checkpoint save fails after discovery, retain the harmless indexed identity for restart/pruning |
| Intermediate progress | Durable checkpoint is incomplete; incoming targets move through Attempted/Completed while cascade remains incomplete | Save the checkpoint only; do not rewrite the index | Propagate checkpoint failure so delivery can retry |
| Completion | Durable checkpoint is incomplete; incoming checkpoint is completed | Save completed checkpoint before removing discovery; one removal write | If removal fails, the durable completed checkpoint remains safely replayable and later reconciliation removes discovery |
| Empty cascade | No durable checkpoint; incoming checkpoint is already completed | Save checkpoint without adding or removing discovery | Propagate checkpoint failure |
| Concurrent identities | Two tenants/parents first become incomplete concurrently | ETag retries merge both identities; neither is lost | Exhaustion remains explicit; no last-write-wins clobbering |
| Restart after interruption | Indexed checkpoint contains pending/attempted targets | Recovery replays outstanding work, persists completion, and clears discovery | Duplicate target submission remains safe through existing deterministic idempotency |

</intent-contract>

## Code Map

- `src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:38` -- `SaveAsync` currently adds on every incomplete save and removes on every completed save; derive absent/incomplete/completed transitions from the durable checkpoint while retaining the current write ordering and `ReadModelWritePolicy.UpdateAsync` merge at lines 86-116.
- `src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs:107` -- initial checkpoint creation and target-free completion; `DriveAsync` at line 153 persists Attempted, target Completed, and final cascade Completed checkpoints and is the outer behavior whose index-write amplification must disappear without changing dispatch sequencing.
- `src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs:29` -- restart path enumerates discovery, replays checkpoints, and idempotently removes entries; preserve its stale dangling-entry handling.
- `tests/Hexalith.Works.IntegrationTests/Story47InMemoryReadModelStore.cs:8` -- ETag-aware persisted-state fake; add thread-safe per-key successful-write observation and deterministic first-write conflict coordination for assertions, without weakening ETag behavior.
- `tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs:22` -- existing lifecycle, restart, stale-entry, and overflow coverage; extend with transition write counts, crash-order evidence, multi-target dispatcher amplification proof, empty-cascade behavior, and concurrent identity merge.
- `tests/Hexalith.Works.IntegrationTests/CascadeRecoveryRuntimeTests.cs:24` -- freezes replay-not-rediscover and Attempted-before-submit semantics; must remain green unchanged.

## Tasks & Acceptance

**Execution:**
- `src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs` -- read the existing durable checkpoint and perform index add/remove only on lifecycle transitions, bracketing the checkpoint write in crash-safe order -- eliminates O(2N) global writes without sacrificing discovery.
- `src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs` -- avoid a redundant post-replay removal when checkpoint completion already cleared discovery, while retaining cleanup of completed checkpoints left indexed by a failed removal -- keeps restart recovery transition-only.
- `tests/Hexalith.Works.IntegrationTests/Story47InMemoryReadModelStore.cs` -- make the fake concurrency-safe and expose deterministic write-count/order/conflict instrumentation -- lets tests observe persisted effects rather than implementation call mocks.
- `tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs` -- add focused store and real-dispatcher cases for the complete matrix, including two concurrent cross-tenant identities and restart replay -- prevents regression of performance and durability semantics.

**Acceptance Criteria:**
- Given a cascade with multiple targets, when the real dispatcher runs to completion, then the global index has exactly one successful add and one successful removal regardless of target count, while every Attempted/Completed checkpoint remains durable in order.
- Given an incomplete checkpoint is recovered after restart, when reconciliation drives its outstanding targets, then completion becomes durable before discovery disappears and a second recovery pass is inert.
- Given distinct tenant/parent identities race on the global index, when optimistic-concurrency retry occurs, then both identities remain discoverable with no cross-tenant loss.
- Given an empty cascade completes at creation, when it is persisted, then no discovery-index write occurs.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 3: (high 1, medium 2, low 0)
- defer: 0
- reject: 12: (high 1, medium 6, low 5)
- addressed_findings:
  - `[high]` `[patch]` Prevented a durable completed checkpoint from regressing to an undiscoverable incomplete checkpoint and added persisted-state coverage.
  - `[medium]` `[patch]` Replaced the per-entry singleton-index reread in startup recovery with identity-local checkpoint inspection while preserving failed-removal cleanup.
  - `[medium]` `[patch]` Added multi-entry recovery coverage proving exactly one lifecycle removal per identity and an inert second pass.

### 2026-08-27 — Review pass (follow-up)
- intent_gap: 0
- bad_spec: 0
- patch: 3: (high 0, medium 3, low 0)
- defer: 0
- reject: 22: (high 3, medium 7, low 12)
- addressed_findings:
  - `[medium]` `[patch]` Logged the reconciler's stranded-index-entry repair (new `CascadeIndexEntryStranded`, EventId 4705) so the failed-removal crash window this branch exists to close is visible in host telemetry instead of silent while its sibling prune branch logs.
  - `[medium]` `[patch]` Bounded the integration store fake's coordinated first-write conflict wait so an unmet coordination fails the unattended run instead of hanging it forever.
  - `[medium]` `[patch]` Pinned the deterministic cascade correlation-id wire format with a literal assertion; nothing in the repository observed that format except the production helper it is meant to freeze.

### 2026-08-27 — Review pass (follow-up 2)
- intent_gap: 0
- bad_spec: 0
- patch: 4: (high 0, medium 3, low 1)
- defer: 0
- reject: 23: (high 2, medium 8, low 13)
- addressed_findings:
  - `[medium]` `[patch]` Corrected the false causal claim in the reconciler's stranded-entry comment and in the `CascadeIndexEntryStranded` (4705) message. Both asserted "an earlier index removal had failed", but the branch is also reached with no removal ever attempted: the index add succeeds, the first checkpoint save fails, and the retry re-discovers no outstanding targets, so the absent-to-completed save writes no index entry. Both now state the observed durable state and enumerate the two paths.
  - `[medium]` `[patch]` Refreshed the persistence contract documentation this change invalidated: `ReadModelCascadeCheckpointStore`'s remarks still claimed unqualified last-write-wins, and `ICascadeCheckpointStore.SaveAsync` still read "creates or overwrites" with no `<exception>` tag, while the new guard rejects completed-to-incomplete. Both now state that completion is monotonic.
  - `[medium]` `[patch]` Added `Intermediate_progress_failure_propagates_and_leaves_discovery_intact`, closing the only I/O-matrix row whose error column had no test: intermediate progress must propagate a checkpoint failure so delivery can retry, and must leave discovery published without rewriting the index. Rows 1, 3, 4, 5, and 6 were already covered.
  - `[low]` `[patch]` Corrected `RecoverAsync`'s XML summary. The stranded-repair branch increments the counter without replaying while the sibling prune branch deliberately does not (frozen by `Stale_index_entry_with_no_checkpoint_is_pruned_after_threshold`); the summary now names both counted paths and the excluded one.

## Design Notes

The durable checkpoint is the lifecycle authority. The index is only a discovery aid: absent to incomplete requires add-before-save; incomplete to completed requires save-before-remove. Intermediate mutations do not change discoverability and therefore must never contend on the global key.

## Verification

**Commands:**
- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` -- expected: restore succeeds.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` -- expected: zero warnings and zero errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.CascadeCheckpointIndexRecoveryTests` -- expected: all focused transition/index/recovery tests pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.CascadeRecoveryRuntimeTests` -- expected: all frozen dispatch/replay ordering tests pass.

## Auto Run Result

Status: done

Summary: Second follow-up review pass over the transition-only cascade indexing change. Four independent review layers again found the lifecycle contract itself intact: `ReadModelCascadeCheckpointStore.SaveAsync` writes the singleton discovery index only at the absent-to-incomplete and incomplete-to-completed transitions, in crash-safe order (add before the first incomplete save, remove after the completed save is durable), and startup recovery resolves each identity from its own durable checkpoint rather than re-reading the global index per entry. This pass patched three accuracy defects the previous passes' own additions introduced, plus one uncovered row of the intent's I/O matrix. No production control flow changed.

Files changed (this pass):
- `../../src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` -- `CascadeIndexEntryStranded` (4705) no longer asserts an unverifiable cause ("an earlier index removal had failed") and reports the observed durable state instead.
- `../../src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs` -- the stranded-branch comment now enumerates both paths that reach it, drops the same false causal claim, and states precisely what the identity-local repair avoids; `RecoverAsync`'s XML summary now matches what the counter actually counts.
- `../../src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs` -- class remarks record that completion is monotonic, replacing the now-false unqualified last-write-wins claim.
- `../../src/Hexalith.Works/Recovery/Cascade/ICascadeCheckpointStore.cs` -- `SaveAsync` documents the monotonic-completion precondition and its `InvalidOperationException`.
- `../../tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs` -- adds `Intermediate_progress_failure_propagates_and_leaves_discovery_intact`.

Cumulative files changed since `eec4ce9`:
- `../../src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs` -- derives durable lifecycle transitions, preserves crash-safe write ordering, enforces monotonic completion, and documents both.
- `../../src/Hexalith.Works/Recovery/Cascade/ICascadeCheckpointStore.cs` -- seam contract records the monotonic-completion precondition.
- `../../src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs` -- bounded, logged, identity-local recovery cleanup without redundant singleton-index writes.
- `../../src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` -- stranded-entry recovery warning (EventId 4705).
- `../../tests/Hexalith.Works.IntegrationTests/Story47InMemoryReadModelStore.cs` -- thread-safe persisted-write observation, failure injection, and bounded deterministic ETag-conflict coordination.
- `../../tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs` -- transition counts/order, crash windows, dispatcher amplification, empty cascades, concurrent identities, monotonicity, intermediate-failure propagation, multi-entry restart recovery, and correlation-id format (13 tests).
- `spec-cascade-transition-only-indexing.md` -- implementation contract, review triage, and verified result.

Review findings breakdown: 4 patches applied (high 0, medium 3, low 1); 0 items deferred; 23 findings rejected after deduplication. Each rejection was checked against the code rather than dismissed. The two high-severity rejections: (a) "transition-only indexing destroys the old self-healing republication of a lost index entry" is true but is precisely what the intent's Approach mandates ("persist target progress without touching discovery") -- re-adding on progress saves would restore the O(2N) contention this story exists to remove, so it is excluded on intent authority, not spec authority; (b) "the completed-to-incomplete guard is a non-atomic check-then-act" is true and already recorded as a residual risk -- it is a monotonicity assertion for the documented single-writer-per-cascade path, and the redelivery route that reviewers proposed cannot reach it because `CascadeDispatcher.EnsureCheckpointAsync` reuses the durable checkpoint and `DriveAsync` early-returns on `Completed`. Other verified rejections: the stranded entry lingering until the next startup pass is exactly the intent's row-3 error column ("later reconciliation removes discovery"); the empty-cascade absent-to-completed save touching no index is verbatim row 4; the `AddedAt` semantic shift has one consumer, the prune branch, which is now gated on `checkpoint is null` -- a state in which no progress save ever refreshed it, so the prune decision is unchanged; the narrowed prune condition self-heals on the next pass because a re-read then yields null; the per-save checkpoint read is the cost the intent's Block-If clause explicitly mandates; and the hard-coded test key literals cannot go false-green, because the positive write-count assertions in the same file fail loudly on any production key rename.

Follow-up review recommendation: true. Patched finding score = `3 x 3 medium + 1 x 1 low = 10`, at or above the threshold of 5; no high-severity patch was needed this pass. Note that all four patches were documentation, comment, and test-coverage accuracy -- no production control flow changed in this pass, and two consecutive passes have now left the lifecycle logic untouched.

Verification performed:
- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` -- passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` -- passed with 0 warnings and 0 errors.
- Focused `CascadeCheckpointIndexRecoveryTests` direct xUnit v3 executable lane -- 13 passed, 0 failed, 0 skipped (12 before this pass).
- Frozen `CascadeRecoveryRuntimeTests` direct xUnit v3 executable lane -- 4 passed, 0 failed, 0 skipped.
- Full deterministic lanes: IntegrationTests (`-class- "*SmokeTests"`) 176 passed, UnitTests 522 passed, ArchitectureTests 117 passed, PropertyTests 3 passed; 0 failures across all four.
- `git diff --check` -- clean.

Residual risks: unchanged from the prior passes. Validation is deterministic against the ETag-aware in-memory read-model store; no live Dapr contention or load lane was run, so the O(2N)-to-O(1) index-write reduction is proved at a fixed target count (N=2) as an exact write count rather than as a curve. Same-identity concurrent saves still rely on the documented single-writer-per-cascade invariant: the completed-to-incomplete guard reads then writes without an ETag, so it is a monotonicity assertion for the sequential path, not a concurrency barrier. The per-save durable-checkpoint read trades index-write amplification for read amplification on the per-identity key, and a transient read failure now fails a save that previously would have proceeded; both are inherent to the transition-derivation approach the intent mandates. One risk is worth restating for operators: discovery is now published exactly once per cascade and never republished, and a stranded index entry is repaired only by a recovery pass, which `CascadeRecoveryService` runs once at host start -- both follow directly from the intent, but they mean a long-running host does not self-correct discovery between restarts.
