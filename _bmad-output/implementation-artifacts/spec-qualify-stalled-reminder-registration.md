---
title: "Establish and verify a bounded cancellation policy for stalled Dapr actor reminder-registration calls."
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Establish and verify a bounded cancellation policy for stalled Dapr actor reminder-registration calls.

</frozen-after-approval>

## Implementation Notes

The registered SDK registrar and Dapr actor invoker use the configured finite operation deadline. An unresponsive actor leaves durable incomplete evidence while the next actor succeeds. Reconstruction retries the retained target, clears incomplete coverage, and restores readiness.

Focused Release integration fact StalledActorRegistrationPreservesRecoveryProgress passed 1/1.
