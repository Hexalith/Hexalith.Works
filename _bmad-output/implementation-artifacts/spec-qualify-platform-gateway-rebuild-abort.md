---
title: "Add the abort-under-concurrent-delivery branch to the gateway-driven Platform projection checks at the next Platform requalification."
type: 'chore'
created: '2026-11-25'
status: 'draft'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Add the abort-under-concurrent-delivery branch to the gateway-driven Platform projection checks at the next Platform requalification.

</frozen-after-approval>

## Implementation Notes

The gateway probe reached Begin, Accumulate, Finalize, Stage and Abort with a
real reschedule fenced during capture. After Abort, epoch 3 reopened with
served generation 2 and owner sequence 4. The gateway's ordinary full prefix
then hit `delivery_identity_conflict` at historical global position 1. The
new event at sequence 5 and global position 15 never reached the projection.
The diagnostic redelivery repeated the same conflict.

In EventStore 5.5.1, Abort clears the candidate capture inventory. The ordinary
full-prefix retry then reaches the next-owner-sequence admission check for an
older envelope without an applicable current-epoch receipt. The SDK records the
conflict and the family remains degraded. The owner checkpoint and the served
generation survive. The existing coordinator-level abort proof uses a
separate delivery path and remains part of the verification record.

The required Platform test retains its previous Commit and concurrent-delivery
coverage while this item awaits a published EventStore correction. Requalify
using the actual public gateway after the SDK accepts valid historical prefixes
and folds the new event without promoting the aborted generation.

[Hosted Platform projection failure](https://github.com/Hexalith/Hexalith.Works/actions/runs/45566346523/job/139046021972).
