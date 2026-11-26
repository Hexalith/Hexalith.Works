---
title: 'Bind the restored operator audit to its process source'
type: 'bugfix'
created: '2026-11-26'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

The Platform rollback test times out when the replayed resume effect originates from the parent suspension. The operator audit is recorded for the completed child, so the effect source can select a different audit key. [Hosted failure](https://github.com/Hexalith/Hexalith.Works/actions/runs/45718217720)

Use the same explicit process source for the operator disposition and the restored audit lookup. Require the restored record to contain one attempt with the expected source, actor, and workload. Keep checking the replayed effect receipt independently.

</frozen-after-approval>

## Implementation Notes

- The hosted evidence records the completed child as the operator disposition source and the parent suspension as the resume effect source.
- The recovery fixture now supplies the completed child to both audit checks. The restored audit lookup asserts the recovered record immediately after the fleet and sweep checks.
- The canonical Platform `platform-recovery` lane verifies the restored audit on the live fleet. Hosted verification of the change is pending.
- The .NET 11 parity build passed. The local supporting lane surfaced an intermittent Redis startup timeout in `CompleteFleetRoundTripPreservesEveryLogicalValueAndCapturedFile`. A single focused rerun passed.

## Review Triage Log

- `medium`, patched. Both call sites explicitly select the verification mode through the source argument.
- `false`. The drill verifies the captured Redis bytes before starting the retained host. Receipt replay and collision checks do not call the operator disposition sink or write its audit key.
- `low`, patched. A missing record reports the process source and audit key.
- `low`, patched. The verification note names the live recovery lane and records its status.
