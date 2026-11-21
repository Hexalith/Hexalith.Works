# Story 5.1: Make Progress and Event Ordinals Overflow-Safe

Status: backlog

As a Hexalith maintainer,
I want progress arithmetic and event ordinal allocation to fail safely at their numeric limits,
So that valid work never wraps, partially succeeds, or corrupts durable replay.

## Dependencies and scope

Can start independently of Story 5.4. Covers PRD FR-7 and FR-8 and architecture decision AD-17.

## Acceptance criteria

**Given** an estimated `InProgress` Work Item with positive Remaining
**When** a valid progress delta equals or exceeds Remaining
**Then** Done reaches Estimated without an overflowing addition
**And** `ProgressReported` records the entire submitted delta before `WorkItemCompleted`.

**Given** an `InProgress` item with 7 hours Done whose estimate has been lowered to 4 hours
**When** the executor reports 1 more hour
**Then** the recorded delta is 1, Done stays at 7 and Remaining stays at zero
**And** replay of `ProgressReported` followed by `WorkItemCompleted` reproduces those values and the completed status.

**Given** the process restarts after a lower estimate left a Work Item `InProgress` with zero Remaining
**When** the aggregate recovers
**Then** the recorded Status, Unit, Estimated and Done are restored. Remaining is zero.

**Given** a valid positive progress delta below Remaining
**When** progress is handled
**Then** the delta is added exactly
**And** arithmetic cannot overflow by construction.

**Given** a command requires one success-event ordinal while the current Sequence is `long.MaxValue`
**When** ordinal headroom is preflighted
**Then** handling fails with the AD-17 infrastructure integrity fault (`System.OverflowException`) before result construction
**And** no success event is returned, appended, or published.

**Given** progress would produce both `ProgressReported` and `WorkItemCompleted`
**When** fewer than two event ordinals remain, including current Sequence `long.MaxValue - 1`
**Then** handling fails before constructing either event
**And** no partial result, state mutation, append, or publication occurs.
**And** the same whole-result preflight applies when a spawn also suspends its parent.

**Given** focused numeric-boundary tests
**When** they exercise `decimal.MaxValue`, `long.MaxValue`, and `long.MaxValue - 1`
**Then** saturating progress, exact sub-Remaining progress, one-event exhaustion, and two-event exhaustion produce their specified deterministic outcomes
**And** replay remains unchanged for accepted histories below the limits.

## Implementation tasks

- [ ] Preserve the existing compare-before-add behavior in `WorkItemEffort.Report`. For (Estimated 4, Done 7), reporting 1 records the submitted delta and leaves Done at 7. Replaying the following completion must reproduce the original result.
- [ ] In `WorkItemAggregate`, establish the successful result size before creating payloads. Cover every use of `NextSequence`, including the second event in progress-to-completion and spawn-with-suspension. A domain rejection or no-op allocates zero Works ordinals.
- [ ] Exercise one-event success at max−1 and failure at max. For both two-event paths, prove success at max−2 and failure at max−1. On exhaustion assert `System.OverflowException` at the handler and verify the stream, publication and aggregate state at the dispatch boundary.

## Compatibility and verification

Use the existing maximum-decimal, re-estimation and completion-order cases as regression coverage. Add the missing ordinal boundaries to the unit suite and a dispatch/persistence check for failure before append. The Works payload ordinal and the canonical EventStore envelope position can differ after a rejection. This story owns only the former.

## Source and test entry points

- [WorkItemAggregate.cs](../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs)
- [WorkItemEffort.cs](../../src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs)
- [WorkItemEffortTests.cs](../../tests/Hexalith.Works.UnitTests/WorkItemEffortTests.cs)
- [WorkItemProgressTests.cs](../../tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs)
- [WorkItemProgressContractFlowTests.cs](../../tests/Hexalith.Works.IntegrationTests/WorkItemProgressContractFlowTests.cs)

[Product requirements](../planning-artifacts/prds/prd-works-2026-06-14/prd.md) · [Architecture decisions](../planning-artifacts/architecture.md) · [Epic 5 acceptance criteria](../planning-artifacts/epics.md#story-51-make-progress-and-event-ordinals-overflow-safe) · [September handoff and retrospective gates](../planning-artifacts/sprint-change-proposal-2026-09-14.md)
