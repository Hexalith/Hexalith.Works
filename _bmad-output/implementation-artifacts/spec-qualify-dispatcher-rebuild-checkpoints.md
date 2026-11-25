---
title: "Determine whether `SharedProjectionEpochCoordinator.GetCheckpointAsync` can be read after a dispatcher-driven rebuild commit of owners that had earlier live deliveries (maybe-false, medium, EventStore)."
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Determine whether `SharedProjectionEpochCoordinator.GetCheckpointAsync` can be read after a dispatcher-driven rebuild commit of owners that had earlier live deliveries (maybe-false, medium, EventStore).

</frozen-after-approval>

## Implementation Notes

Two previously live owners have readable checkpoints immediately after Commit and after Verify in both rebuild generations, with current captured identity references. Subsequent canonical ordinary deliveries preserve both exact persisted histories and advance both checkpoints. This settles the supported owner-sequenced dispatcher path; direct zero-sequence legacy journal entries are outside this proof.

All four WorksSdkPartitionedRebuildTests passed with the pinned EventStore SDK. The real-store abort fixture uses the legacy journal construction and remains separately scoped.
