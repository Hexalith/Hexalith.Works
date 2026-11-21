# Story 5.3: Preserve Re-Estimate Overrun and Enforce Bounded Act Notes

Status: backlog

As a Hexalith maintainer,
I want re-estimation to preserve audited progress and all act notes to follow one bounded policy,
So that planning changes remain truthful, replayable, and safe to diagnose.

## Dependencies and scope

Integrate after Story 5.2. Story 5.4 gates changed durable evidence. Covers PRD FR-3 and FR-7 through FR-9, AD-17 and AD-29.

## Acceptance criteria

**Given** a Work Item whose cumulative Done exceeds a proposed non-negative replacement estimate in the established Unit
**When** `ReEstimate` is accepted from an eligible Status
**Then** the replacement Estimated value is recorded while cumulative Done remains unchanged and Remaining derives as `max(Estimated - Done, 0)`
**And** Unit and Status remain unchanged and no completion or reopen event is emitted.

**Given** accepted re-estimation evidence is consumed by aggregate replay, projection folds, serialized queries, or rebuild
**When** each path reaches quiescence
**Then** every path reports the same Estimated, cumulative Done, Remaining, Unit, and Status
**And** visible `Done > Estimated` overrun is not clamped, hidden, or treated as corruption.

**Given** representative pre-change streams that previously rebuilt with clamped Done
**When** the semantic migration is validated
**Then** checked-in migration fixtures prove the new aggregate and read-side interpretation atomically preserves Done
**And** affected disposable projections are rebuilt with documented snapshot and cache invalidation before rollout.

**Given** the inventory of every currently note-bearing lifecycle and planning command
**When** a note is `null` or contains from zero through 1,000 characters
**Then** the command accepts and preserves the supplied note verbatim when its other rules pass
**And** every inventoried command uses the same common policy.

**Given** any inventoried note contains more than 1,000 characters
**When** its command validator or aggregate handler evaluates it
**Then** the defined domain rejection is returned with no success event or state mutation
**And** logs and ProblemDetails do not echo the submitted note.

**Given** note-bearing durable contracts and tests
**When** compatibility verification runs
**Then** command validators, aggregate handling, catalog samples, golden payloads, and boundary tests all enforce the same limit
**And** any changed durable evidence is introduced only after Story 5.4's catalog-completeness gate is available.

## Implementation tasks

- [ ] Reuse the shipped preserve-Done arithmetic. Replay estimate 10, Done 7, then estimates 4 and 10. Compare the live and rebuilt values at each position across aggregate, roll-up and serialized query. Re-estimation alone preserves Unit and Status.
- [ ] Inventory command contracts with a Note at the implementation revision. The current commands are `ReportProgress`, `ReEstimate` and `RescheduleWorkItem`. Share admission checks across their validation and handling paths. Exercise null, empty, 1,000 and 1,001 characters, including whitespace. Accepted notes retain their exact text. Refusals carry a bounded reason and exclude the input from diagnostics.
- [ ] Provide a representative pre-change replay fixture. Record how to find affected snapshots, rebuild from the original stream when Done was lost, invalidate caches and rebuild the selected projections. Compare the served and rebuilt result at quiescence before switching the deployed readers and writers.

## Compatibility and verification

The November re-estimation fix already preserves Done in the aggregate and roll-up test. Its presence alone does not qualify a snapshot migration or note enforcement. Read existing event payloads, including historical long notes. Introducing the future correction, reopen or handoff commands is outside this story.

## Source and test entry points

- [WorkItemEffort.cs](../../src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs)
- [Commands](../../src/Hexalith.Works.Contracts/Commands)
- [WorkItemState.cs](../../src/Hexalith.Works.Contracts/State/WorkItemState.cs)
- [WorkItemAggregate.cs](../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs)
- [WorkItemRollUpProjection.cs](../../src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs)
- [WorkItemReEstimateTests.cs](../../tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs)
- [WorkItemReEstimateRescheduleContractFlowTests.cs](../../tests/Hexalith.Works.IntegrationTests/WorkItemReEstimateRescheduleContractFlowTests.cs)
- [WorkItemSharedProjectionRebuildHandlerTests.cs](../../tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs)

[Product requirements](../planning-artifacts/prds/prd-works-2026-06-14/prd.md) · [Architecture decisions](../planning-artifacts/architecture.md) · [Epic 5 acceptance criteria](../planning-artifacts/epics.md#story-53-preserve-re-estimate-overrun-and-enforce-bounded-act-notes) · [September handoff and retrospective gates](../planning-artifacts/sprint-change-proposal-2026-09-14.md)
