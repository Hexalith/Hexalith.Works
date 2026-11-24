---
title: 'Prove the shared-rebuild abort branch on the real state store'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Close deferred finding R27A-A3: AC2's shared-rebuild failure branch, an abort while deliveries arrive concurrently, is proven only over `InMemoryReadModelStore`. Exercise the same abort-under-concurrent-delivery path against the real Dapr state store. Assert that the committed generation stays served and that journaled deliveries converge on it.

</frozen-after-approval>

## Implementation Notes

- `WorksRealStoreRebuildAbortTests.AbortedRebuildUnderConcurrentDeliveryKeepsTheServedGenerationOnTheRealStore` is an explicit fact. It runs on the disposable Dapr/Redis harness (`scripts/measure-projection-delivery.py --scenario aborted-rebuild`) and is registered in `WorksAppHostSelectionTests.LiveFactsRequireExplicitSelection`.
- Sequence, on the real store and then identically on `InMemoryReadModelStore`:
  1. Two owners are delivered live.
  2. One authoritative rebuild is committed, so the served generation is 1.
  3. A second rebuild captures and stages. Its Begin fences the writer: owner A's `WorkItemQueued` must return `StaleLease`, and after a refresh both owners' `WorkItemQueued` deliveries journal.
  4. The abort runs with the dispatcher's coordinator sequence, `AbortAsync(..., deferOpen: true)` then `CompleteAbortAsync`, with the production `FoldCatchUpAsync` fold. Concurrently, owner B journals Assigned, Claimed and a `DateReached` suspension, so the pending-date index has content.
  5. A further rebuild of the same histories is committed, then a live delivery is folded.
- Assertions:
  - During the capture, the stale served documents equal the committed documents, and both checkpoints show the journaled position 2.
  - After the abort, a restarted coordinator serves generation 1, not building, not degraded, with no pending deliveries. A is Queued at position 2, and B is suspended at position 5 with a pending date.
  - The later rebuild promotes a newer generation (3, because the aborted epoch consumed 2) and reproduces the converged family. Live delivery then reaches A Assigned at position 3.
  - All four snapshots (documents plus checkpoints) must equal the in-memory reference. The test emits both digests and the script requires them to match, with served generation 1.
- Rebuilds are staged through `WorkItemSharedRebuildManifestBuilder` and coordinator Begin/Stage/Commit, not the dispatcher's Accumulate/Finalize. Driving the dispatcher made `GetCheckpointAsync` throw "malformed captured identity references" on both stores. That is recorded in `deferred-work.md` for EventStore.
- The Platform gateway's live rebuild check (`WorksPlatformProjectionChecks`) still drives only Begin→Commit. A gateway-delivered abort check is recorded as a follow-up.

## Verification

`python3 scripts/measure-projection-delivery.py --scenario aborted-rebuild --results <scratch>/aborted.xml` passed 1/1 in 2.2 s. The run used the uncommitted change on Works `b4ba8c6`, the Release build with EventStore 5.5.1 packages, Dapr 1.19.2 (`sha256:ed4715…575a`) and Redis (`sha256:ff02b5…eadf`). The served generation was 1 and the rebuilt generation 3. The real and reference digests were both `8ed2622d…4340`. The abort, with racing deliveries, took 359 ms on a cold first run. `WorksAppHostSelectionTests` passed 28/28.

## Review Triage Log

- **High, patched.** The explicit-test guard did not list the new fact, nor the committed unpaged-queue theory. Both are now registered; the latter was amended into its own commit.
- **Medium, patched (scoped).** The test skipped the production abort sequence. It now uses the dispatcher's deferred-open abort and `CompleteAbortAsync`. Dispatcher staging is out of scope because of the checkpoint defect recorded in the ledger.
- **Medium, patched.** The stale-lease fence was optional; the first journal after Begin must now return `StaleLease`.
- **Medium, patched.** The capture check was weak. The stale served documents must now equal the committed documents, with journaled checkpoints asserted.
- **Medium, patched.** The abort's side effects were not exercised. A later rebuild of the same histories must now promote and reproduce the converged family.
- **Medium, patched.** No delivery raced the abort; three deliveries now journal while it runs.
- **Low, patched.** The pending-date oracle was vacuous. A dated suspension now populates it, and the owner is asserted.
- **Medium, patched.** The evidence could not fail. The test now emits real and reference digests and the exact generations, and the script requires matching digests and served generation 1.
- **Medium, patched.** The ledger was not updated. R27A-A3 is resolved, and a follow-up for the Platform lane is recorded.
- **Low, patched.** The verification did not record its inputs and the new file had LF endings. The record now gives the commit, package version, images and cold-run timing, and the file uses CRLF.
