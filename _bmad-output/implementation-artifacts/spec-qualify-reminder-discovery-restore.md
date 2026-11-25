---
title: "Establish whether an approved restore can contain reminder item state while omitting both its tenant registry and candidate index entries."
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Establish whether an approved restore can contain reminder item state while omitting both its tenant registry and candidate index entries.

</frozen-after-approval>

## Implementation Notes

The synthetic fleet verifier rejects loss of either captured reminder discovery index and of both together, even when the item record is intact. Restore preserves all three Redis records. A snapshot already missing both indexes passes byte-for-byte restore verification, so that verification alone cannot establish discovery completeness at capture.

Both Release integration cases in RestorePreservesTheCapturedReminderDiscoveryInventory passed on isolated Redis.
