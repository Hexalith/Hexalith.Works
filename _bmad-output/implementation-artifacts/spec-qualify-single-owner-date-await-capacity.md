---
title: 'Qualify the single-owner pending-date mutation limit'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Measure where a valid single-owner date-await history stops fitting the Works shard mutation guard. Verify that an over-capacity rebuild preserves the selected generation throughout refusal and abort.

</frozen-after-approval>

## Verification

The partitioned SDK rebuild staged and promoted 1,537 distinct awaits. The captured suspension payload was 183,059 bytes, and the Works mutation charge was 261,998 bytes. Adding one await produced a 183,178-byte payload, a 246,005-byte retained history and a 262,167-byte mutation charge, exceeding the Works 262,144-byte guard. SDK accumulation accepted the history and Finalize reported an indeterminate handler failure. The old generation and its date shard remained available and stale until the SDK dispatcher completed abort. A reconstructed reader then returned all 1,537 dates and the same shard ETag and watermark.

The result describes the Works mutation guard with its 2,048-byte allowance. The selected SDK uses a separate, less conservative per-mutation charge. The captured event also falls below the default domain-service event-size setting and above the independently configured default recovery envelope budget. The fixture exercises projection rebuild admission from captured history.

## Review Triage Log

- **Medium, patched.** Exercise the real partitioned rebuild dispatcher through successful promotion, failed finalization and abort.
- **Medium, patched.** Distinguish Works' canonical mutation charge from the SDK and Redis limits.
- **Medium, patched.** Verify successful SDK accumulation and compare the exact event and history sizes with their applicable limits.
- **Low, patched.** Measure the actual logical shard key routed for the fixture owner.
