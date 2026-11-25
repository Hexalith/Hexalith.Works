---
title: "Verify persisted process sweep claims with host-supplied Dapr JSON serializer options."
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Verify persisted process sweep claims with host-supplied Dapr JSON serializer options.

</frozen-after-approval>

## Implementation Notes

Works registration retains the host-supplied Dapr serializer. A new client with matching snake-case options reopens the active owner, lease, fence, generation, and cursors from actual Redis; an incompatible client refuses without changing the record. The adoption guide records the compatibility requirement.

The standalone sweep-serialization scenario passed 1/1 using the pinned SDK and isolated Dapr and Redis images.
