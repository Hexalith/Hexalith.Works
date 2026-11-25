---
title: 'Qualify the deferred reminder actor turn deadline'
type: 'chore'
created: '2026-11-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent">

## Intent

Determine whether every dependency call inside an EventStore reminder actor
turn has a finite SDK deadline. This is separate from the bounded wait used
by the outer reconciliation pass.

</frozen-after-approval>

## Implementation Notes

The concrete EventStore 5.5.1 `ReminderActor` forwards an uncancelable token to
the first item-state read. `ReminderActorTurnKeepsWaitingAfterCallerDeadline`
holds that read in a controlled provider, waits for provider entry, and
confirms that a 500 ms caller deadline leaves the actor turn pending. The test
then releases the read and drains the turn with a separate cleanup deadline.
No reminder state is created for the valid creation-only stream.

This counterexample establishes that the SDK has no deadline around every
actor-turn await. The actor's scheduler calls and the coordinator's submitter
also rely on provider-level transport settings. This test covers the actor
entry point and the state-store interface. It does not time live Dapr transport.

The focused Release integration test passed against the pinned EventStore
5.5.1 packages.
