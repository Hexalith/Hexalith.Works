# Story 5.5: Enforce the Obligation Bound Without Rewriting History

Status: backlog

As a Hexalith builder,
I want new Work Item obligations bounded at admission while historical events remain replayable,
So that aggregate content stays appropriately small without invalidating previously accepted work.

## Dependencies and scope

Story 5.4 must qualify any new rejection before its producer is enabled. Covers PRD FR-2 and FR-7 and AD-29.

## Acceptance criteria

**Given** a new `CreateWorkItem` command whose Obligation has a trimmed length from 1 through 4,000 characters
**When** admission validation and aggregate handling run
**Then** creation is accepted when all other rules pass and the new Raw Act preserves the submitted text verbatim
**And** surrounding whitespace is ignored only when determining whether the value is empty and whether its bounded content exceeds the limit.

**Given** a new Obligation is empty, whitespace-only, or has a trimmed length greater than 4,000 characters
**When** `CreateWorkItem` is handled
**Then** the defined domain rejection is returned with no success event or state mutation
**And** EventStore may persist the rejection under its own envelope sequence. No creation event is appended or published and the Works payload ordinal stays unchanged.

**Given** the Obligation admission rule is represented at every boundary
**When** the command validator, aggregate, contract catalog, golden payloads, headless evidence, and tests are inspected
**Then** they use the same 4,000-character limit and trimming rule
**And** logs and ProblemDetails do not echo rejected Obligation content.

**Given** a historical `WorkItemCreated` payload contains a previously accepted Obligation longer than 4,000 characters
**When** current readers deserialize and replay it
**Then** the event remains readable and reconstructs the same historical state
**And** the new command-admission bound is not retroactively applied during replay.

**Given** implementation requires a new rejection type for the over-limit case
**When** rollout is prepared
**Then** reader, validator, discriminator, catalog sample, and required golden representations land and pass through Story 5.4's completeness gate before the producer is enabled
**And** rollback retains the reader while the new durable evidence exists.

**Given** focused Obligation-boundary tests
**When** they exercise empty or whitespace-only, 1-character, 4,000-character, and 4,001-character inputs with surrounding-whitespace variants
**Then** every admission outcome matches the common trimming and length rules
**And** a historical oversized-event fixture proves replay compatibility.

## Implementation tasks

- [ ] Check the submitted description at every new Create admission. Measure its trimmed length for the 1–4,000 bound. Preserve the original accepted text in the Raw Act. The current `Obligation` constructor trims its argument. Separate admission and event representation explicitly so a shared constructor does not silently alter the Raw Act or impose a new length check during replay.
- [ ] Cover null, empty, whitespace-only, one character, exactly 4,000 and 4,001 characters. Add padded cases and verify the emitted text, the rejection disposition, the unchanged Works payload ordinal and the absence of a creation event. Capture diagnostic fields and ProblemDetails to check that rejected input is absent.
- [ ] Retain the blank-input rejection and its frozen bytes. Qualify an over-limit rejection with the appropriate reader, validation, catalog and golden fixtures before enabling its producer. Replay a pre-change `WorkItemCreated` with a 4,001-character description through deserialization and state reconstruction. Rollback must still read all produced evidence.

## Compatibility and verification

The present Create handler rejects blanks and has no upper limit. EventStore may persist a domain refusal at an envelope position without creating an item. Keep accepted historical descriptions readable. This story changes new Create admission. It does not authorize rewriting streams, applying the bound during reads or expanding the public API to other actors.

## Source and test entry points

- [CreateWorkItem.cs](../../src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs)
- [Obligation.cs](../../src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs)
- [WorkItemCreated.cs](../../src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs)
- [WorkItemCannotBeCreatedWithoutObligation.cs](../../src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotBeCreatedWithoutObligation.cs)
- [WorkItemAggregate.cs](../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs)
- [WorkItemCreateTests.cs](../../tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs)
- [WorkItemCreateContractFlowTests.cs](../../tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs)
- [SchemaEvolution](../../tests/Hexalith.Works.IntegrationTests/SchemaEvolution)

[Product requirements](../planning-artifacts/prds/prd-works-2026-06-14/prd.md) · [Architecture decisions](../planning-artifacts/architecture.md) · [Epic 5 acceptance criteria](../planning-artifacts/epics.md#story-55-enforce-the-obligation-bound-without-rewriting-history) · [September handoff and retrospective gates](../planning-artifacts/sprint-change-proposal-2026-09-14.md)
