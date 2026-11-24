---
title: 'Measure unpaged what''s-next responses at admitted limits'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Measure whether the unpaged authorized what's-next response still works at the admitted queue limits before anyone defines paging (deferred-work R3B9: "Qualify unpaged authorized queue responses at real invocation limits before defining paging behavior"). Use the real SDK state store, so that each measured population yields a handler time and the exact unpaged payload size. That evidence shows which limit binds first.

</frozen-after-approval>

## Implementation Notes

- `WorksQueueMeasurementTests.WhatsNextMeasuresUnpagedEligibleResponses` is an explicit theory over 256, 1,024, 2,048, 4,096 and 10,000 eligible owners. 10,000 is the admitted queue-membership cap (`ProjectionDispatchOptions.DefaultMaxSharedRebuildAggregateCount`). The 256 row is the success-path control and must pass all three reads.
- Every owner is an Assigned item with a priority, a due date, its own effort and a 64-character executor party, staged as one generation through the SDK coordinator. Work-item ids are 10 characters and the tenant is 46, so 543 bytes per item is a representative size, not an upper bound. The test also serializes the same item with the longest admitted tenant (64), work-item (256) and party (256): 998 bytes.
- The test serializes the complete authorized result from the selected generation in queue order, before any read is timed. That gives the payload size and its base64 `QueryResult` wire form even when the budget refuses the query. Every success must match it exactly.
- Only the coordinator caller (`AllWork`, no Parties lookup) was measured. A delegated Executor also spends up to two seconds of the same five-second budget on identity resolution, so its headroom is smaller.
- `scripts/measure-projection-delivery.py` adds the `unpaged-queue` scenario with a per-scenario timeout table: 2,400 s for this scenario, because the run took 627 s. Report validation requires payload and wire sizes for every successful read.
- Real invocation was not exercised. The harness sidecar has no app channel, so comparisons with Dapr's default 4 MiB body limit are arithmetic over the measured wire sizes.

## Verification

`python3 scripts/measure-projection-delivery.py --scenario unpaged-queue --results <scratch>/unpaged-queue.xml` passed 5/5 rows in 627 s on Works `64919bc`, with the Release build (EventStore 5.5.1 packages), Dapr 1.19.2 runtime `sha256:ed4715…575a` and Redis `sha256:ff02b5…eadf`.

| Eligible owners | Successful reads | Handler time | Unpaged payload | `QueryResult` wire bytes |
| ---: | --- | ---: | ---: | ---: |
| 256 | 3 of 3 | 643–718 ms | 139,009 | 185,647 |
| 1,024 | 3 of 3 | 1,820–3,492 ms | 556,033 | 741,679 |
| 2,048 | 2 of 3 (cold read timed out) | 3,461–3,529 ms; one ≥5 s | 1,112,065 | 1,483,055 |
| 4,096 | 0 of 3 | ≥5 s (deadline) | 2,224,129 | 2,965,614 (computed) |
| 10,000 (cap) | 0 of 3 | ≥5 s (deadline) | 5,430,001 | 7,240,110 (computed) |

**Result.** Unpaged responses fail inside the admitted population. The five-second read budget binds first, at about 2,048 eligible items. The cost is not the response itself: the handler reads a parking and a roll-up document for every queue member, so terminal members cost the same, as `spec-measure-terminal-queue-membership.md` showed. The base64 wire form is 1.33 times the payload, so Dapr's default 4 MiB body limit is crossed at about 5,800 representative items, or about 3,150 items with maximum-length identities. (Since `spec-bound-whats-next-related-reads.md`, terminal and other ineligible members are no longer read; each eligible candidate still costs two related reads.) A paging design therefore has to restrict related reads to the returned page and keep each page's wire size under the invocation limit. That decision, and a real-transport check of the chosen page size, are recorded in `deferred-work.md`.

## Review Triage Log

- **Medium, patched.** The ledger was not updated. R3B9 is now resolved, and the paging decision and transport check are new entries.
- **Medium, patched (scoped).** Real invocation was not run. The spec states this, and the transport check is a new follow-up entry.
- **Medium, patched.** The 4 MiB comparison ignored base64. Wire sizes are now measured, and the thresholds use them.
- **Medium, patched.** 543 bytes was presented as "fully populated". It is now called representative, and a maximum-identity item size (998 bytes) is measured.
- **Medium, patched.** The result attributed the timeout to response size. It now attributes it to per-member related reads and cites the terminal-queue measurement.
- **Low, patched.** No population had to succeed. The 256 row must pass 3/3, in both the test and the script.
- **Low, patched.** The script did not validate the byte outputs. It now requires payload and wire sizes and their consistency with `successful`.
- **Low, patched.** The setup did not check item count or populated fields. Both are now asserted.
- **Low, patched.** Only the coordinator caller was measured; the notes now say so.
- **Low, patched.** The threshold was not located. A 2,048 row was added, and timed-out rows now read ≥5 s.
- **Medium, patched (found by the next review).** `WorksAppHostSelectionTests.LiveFactsRequireExplicitSelection` requires every explicit test to be registered; the new theory is now listed under standalone measurements.
- **Low, patched.** The run was not reproducible. The record now includes the command, commit, image digests and measured duration, and timeouts live in a per-scenario table.
