---
title: 'Read what''s-next related documents only for candidates'
type: 'bugfix'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-measure-terminal-queue-membership.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Deferred finding "Reduce the what's-next query's related-document reads for large retained terminal populations". The handler reads a parking and a roll-up document for every queue member, retained terminal members included. With 4,096 terminal members and only eight eligible items, every query hit the five-second budget.

**Approach:** Read parking and roll-up documents only for the authorized eligible candidates, plus parking for any member named by SDK parking evidence. Keep the single selected generation, the degraded and generation checks, and refusal of missing or corrupt documents that the result depends on.

</frozen-after-approval>

## Implementation Notes

- `WhatsNextQueryHandler.ReadSdkAsync` builds the authorized, ordered candidate list from the selected index, using the same membership, eligibility and executor filter as before. An index whose item key and work-item id disagree is refused as `invalid-index`, because it could alias related documents.
- It then reads `GetStatusAsync`. A degraded, offboarded or generation-mismatched status is refused before any related read.
- Parking is read for the candidates and for members named in `ParkingEvidence`; roll-ups for the candidates only. All of it goes through `WorksRuntimeIndexReader.ReadRelatedAsync`, which pins the preliminary roots and generation.
- Why evidence members are read: the SDK never deletes parking evidence, only marks it resolved, and unresolved evidence already degrades the status. But re-parking at a position already recorded appends no new evidence while still writing `Parked=true` (`SharedProjectionEpochCoordinator`, the `head.Parked` branch). Without the evidence read, a stale non-candidate could hide behind a non-degraded status.
- After the related read, status is checked again as defense in depth, together with evidence coverage.
- Decision: a missing or corrupt parking or roll-up document refuses the queue only when it belongs to a candidate or an evidenced member. Other members' documents do not affect the result, as with the detail query, which reads only its own item. `docs/sdk-projection-query-adoption.md` states this.
- `WorksProjectionQueryCancellationTests` seeds its blocked item as Queued, because a Created-only item is no longer a candidate. `WorksQueueMeasurementTests.WhatsNextMeasuresRetainedTerminalMembershipCost` now requires all three reads to succeed at every population.
- Still open: each eligible candidate costs two related reads, so the unpaged queue still times out near 2,048 eligible items. That is the paging follow-up.

## Verification

- Real-store fix proof: `python3 scripts/measure-projection-delivery.py --scenario terminal-queue` on Works `56d46ae` plus this change (Release, EventStore 5.5.1 packages, Dapr 1.19.2, catalog Redis) passed 3/3 and required every read to succeed. With 0 terminal owners the reads took 58–93 ms; with 1,024, 151–209 ms (previously 1,490–3,029 ms); with 4,096, 159–168 ms (previously three timeouts at about 5,000 ms).
- `dotnet …/Hexalith.Works.IntegrationTests.dll -class …WorksWhatsNextRelatedReadTests -class …WorksSdkProjectionQueryTests -class …WorksProjectionQueryCancellationTests` passed 68/68, covering:
  - The exact related-read set for coordinator and Executor callers: only the returned candidates, each read once. Terminal members, a created item and another party's assignment cost nothing.
  - A corrupt, mismatched or missing candidate roll-up or parking document refuses the query.
  - A parked candidate is omitted with `works-runtime-parked` and Degraded; no earlier test asserted this warning.
  - A corrupt terminal parking or roll-up document no longer affects the queue.

## Review Triage Log

- **Medium, patched.** The ledger entry was not closed; it now is.
- **Medium, patched.** Nothing showed the fix against the target failure. The real-store run now passes at 4,096 terminal members, and the measurement requires success.
- **Low, patched.** The verification could not be reproduced. The record now gives commands, commit and counts.
- **Medium, false.** The review called the evidence reads unprotective. Re-parking at an already recorded position writes `Parked=true` without new evidence, so a non-degraded status with a parked non-candidate is reachable; the notes now explain this.
- **Low, patched.** The post-read check's comment overstated it; it is now described as defense in depth.
- **Medium, patched.** The party-scoped path was untested. An Executor case now proves another party's assignment costs no reads.
- **Medium, patched.** The parked candidate warning was untested. It is now covered. An evidenced non-candidate and the pre-read refusal need SDK-produced parking or degraded state and remain covered only by code review.
- **Medium, patched.** The corruption cases were narrow. They now cover a mismatched roll-up, a corrupt candidate parking document, and a corrupt terminal parking document, with lifecycle metadata asserted.
- **Low, patched.** Reads were not bounded. The test now asserts the exact related-key set and that each key is read once.
- **Medium, patched.** A contradictory index aliased reads; it is now refused explicitly as `invalid-index`.
- **Low, patched.** The documentation was stale. The handler summary, query docs and the unpaged-measurement spec are updated, and the remaining per-candidate cost is stated.
- **Low, rejected.** The physical-key helper is duplicated in test code, as in the existing tests; extracting it would touch unrelated files. The misleading `s_retained` name was replaced.
