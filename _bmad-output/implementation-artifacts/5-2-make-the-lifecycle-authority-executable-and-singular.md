# Story 5.2: Make the Lifecycle Authority Executable and Singular

Status: backlog

As a Hexalith maintainer,
I want every Work Item act governed by one executable lifecycle authority,
So that handlers, documentation, tests, and durable rejection evidence cannot drift apart.

## Dependencies and scope

Supplies the executable lifecycle policy for final Story 5.3 integration. Covers PRD FR-6 through FR-10 and the FR-14/FR-15 preservation rule in AD-13.

## Acceptance criteria

**Given** progress, re-estimation, rescheduling, or any lifecycle command is handled
**When** Status legality is evaluated
**Then** the decision comes from the shared executable lifecycle policy
**And** no handler-local status table or contradictory legality branch remains.

**Given** the executable policy accepts an act
**When** its `LifecycleOutcome` is consumed
**Then** the handler uses or explicitly verifies the policy's target Status
**And** emitted events and applied state produce exactly that target without recomputing it independently.

**Given** the executable policy rejects an act
**When** the domain rejection is produced
**Then** it carries the stable durable `AttemptedAct` defined for that matrix cell
**And** every rejected cell is covered by a focused assertion.

**Given** the human-readable lifecycle Markdown matrix
**When** verification runs
**Then** the document is generated from or mechanically cell-compared with the executable policy
**And** any missing, extra, or contradictory cell fails the test.

**Given** ReEstimate is evaluated across lifecycle state
**When** matrix coverage runs
**Then** all five non-terminal statuses and every terminal or `Unknown` status have an explicit tested outcome
**And** no status falls through to an implicit handler default.

**Given** ordered suspension evidence with duplicate members and two eligible Resume triggers
**When** the lifecycle policy is extracted
**Then** admission still requires a nonempty collection of valid non-null conditions, replay preserves every member in order, and the first matching Resume committed on the Work Item clears the collection.

**Given** existing durable diagnostic act-name values
**When** the singular policy is introduced
**Then** historical values remain readable and retain their meaning
**And** any normalization uses an additive compatibility mapping rather than rewriting durable history.

## Implementation tasks

- [ ] Inventory the implemented acts and their durable rejection labels. Move the local progress, re-estimate and reschedule status checks into the shared decision path. Have handlers consume or verify the returned target and preserve payload shape and rejection precedence.
- [ ] Compare each matrix cell with the executable result, including the accepted target, no-op and rejection label. Cover all five live statuses, four terminal statuses and the pre-creation sentinel for ReEstimate. Demonstrate that changing one cell fails the comparison.
- [ ] Submit a collection of conditions in the order A, B, A. Verify that all three survive serialization and replay. Deliver competing matching Resumes in both committed orders. The winner consumes its condition and clears the entire wait. Retain singular historical readers, consumed-key retries and the rejection for a different later trigger.

## Compatibility and verification

The present documentation fitness test checks names. Extend it to compare outcomes. Exercise status-neutral planning acts through their handlers as well as the policy. Handoff and CorrectProgress are separately recorded targets. This extraction preserves the delivered command surface, durable act names and authorization boundary.

## Source and test entry points

- [WorkItemLifecycle.cs](../../src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs)
- [LifecycleOutcome.cs](../../src/Hexalith.Works.Server/Aggregates/LifecycleOutcome.cs)
- [WorkItemAggregate.cs](../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs)
- [lifecycle-transition-matrix.md](../../docs/lifecycle-transition-matrix.md)
- [LifecycleTransitionMatrixDocTests.cs](../../tests/Hexalith.Works.ArchitectureTests/FitnessTests/LifecycleTransitionMatrixDocTests.cs)
- [WorkItemLifecycleTests.cs](../../tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs)
- [WorkItemSuspendResumeTests.cs](../../tests/Hexalith.Works.UnitTests/WorkItemSuspendResumeTests.cs)
- [AwaitConditionSerializationContractFlowTests.cs](../../tests/Hexalith.Works.IntegrationTests/AwaitConditionSerializationContractFlowTests.cs)

[Product requirements](../planning-artifacts/prds/prd-works-2026-06-14/prd.md) · [Architecture decisions](../planning-artifacts/architecture.md) · [Epic 5 acceptance criteria](../planning-artifacts/epics.md#story-52-make-the-lifecycle-authority-executable-and-singular) · [September handoff and retrospective gates](../planning-artifacts/sprint-change-proposal-2026-09-14.md)
