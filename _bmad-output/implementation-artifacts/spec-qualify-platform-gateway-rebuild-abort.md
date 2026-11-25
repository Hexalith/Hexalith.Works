---
title: "Add the abort-under-concurrent-delivery branch to the gateway-driven Platform projection checks at the next Platform requalification."
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Add the abort-under-concurrent-delivery branch to the gateway-driven Platform projection checks at the next Platform requalification.

</frozen-after-approval>

## Implementation Notes

The focused Platform projection fact now submits a reschedule through the gateway during another rebuild. It verifies the new event hits the projection fence, aborts through the protected dispatcher, and checks automatic convergence against the authoritative fold on the original served generation. The shared recovery fixture still writes one conversation event.

The Release solution builds. The existing R4 Platform projection fact executes the new abort assertions and records both query lifecycle observations and the fenced dispatch outcome.
