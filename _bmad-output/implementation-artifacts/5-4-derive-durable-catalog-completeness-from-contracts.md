# Story 5.4: Derive Durable Catalog Completeness from Contracts

Status: backlog

As a Hexalith quality maintainer,
I want durable-catalog completeness derived from the Contracts assembly,
So that new or orphaned durable types cannot evade compatibility evidence through hand-maintained counts.

## Dependencies and scope

Can start independently of Story 5.1. Supplies the evidence gate for Story 5.3 contract changes and any new Story 5.5 rejection producer. Covers FR-7, NFR-12 and AD-29.

## Acceptance criteria

**Given** the built Works Contracts assembly
**When** the completeness test reflects over its durable contract surface
**Then** it derives the complete set of concrete decorated commands, success events, and `IRejectionEvent` types
**And** a hand-authored count may be supplemental but cannot define the universe being checked.

**Given** any derived durable type
**When** its compatibility evidence is inspected
**Then** it has exactly one stable discriminator and one non-vacuous catalog sample
**And** it has every writer and reader golden representation required by the serialization policy.

**Given** the derived contract set and the catalog and golden evidence sets
**When** completeness is compared
**Then** a contract without evidence fails
**And** evidence without a corresponding concrete contract also fails.

**Given** a decorated durable type is introduced without updating its catalog sample or required golden corpus
**When** the focused mutation or fixture test runs
**Then** the completeness gate demonstrably fails for the missing entry
**And** the test cannot pass vacuously because of an empty reflected or evidence set.

**Given** existing frozen discriminators and golden payloads
**When** the assembly-derived gate is adopted
**Then** their discriminator values and payload bytes remain unchanged
**And** historical reader compatibility continues to pass before any new producer is enabled.

## Implementation tasks

- [ ] Reflect over the built Works Contracts assembly. Inventory concrete decorated commands, success events and rejections. Classify each type once, including rejections that implement the event payload interface. Fail if an expected category is empty.
- [ ] Compare that inventory in both directions with serialized samples and registered discriminators. Detect missing and extra entries and duplicate discriminators or samples. Verify each policy-required representation for each contract category.
- [ ] Exercise a missing sample and a missing or orphaned fixture. A test with the same hand-maintained list on both sides cannot detect an omitted contract. Keep fixtures checked into source and test copied-output membership where the corpus requires it.

## Compatibility and verification

The current catalog lists 15 commands, 15 success events and 10 rejections by hand. Events require the tolerant Web-reader corpus and exact EventPersister writer bytes. Commands follow the command corpus policy, including exact command bytes where specified. Use the repository's representative command samples for all decorated commands. Preserve historical bytes and discriminators. This story builds the verification gate and introduces no product command or event.

## Source and test entry points

- [WorksContractsAssembly.cs](../../src/Hexalith.Works.Contracts/WorksContractsAssembly.cs)
- [WorkItemV1Catalog.cs](../../tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs)
- [WorkItemSerializationRegistrationTests.cs](../../tests/Hexalith.Works.IntegrationTests/WorkItemSerializationRegistrationTests.cs)
- [SchemaEvolutionGoldenCorpusTests.cs](../../tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs)
- [EventPersisterGoldenCorpusTests.cs](../../tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGoldenCorpusTests.cs)
- [README.md](../../tests/Hexalith.Works.IntegrationTests/SchemaEvolution/CommandGolden/README.md)

[Product requirements](../planning-artifacts/prds/prd-works-2026-06-14/prd.md) · [Architecture decisions](../planning-artifacts/architecture.md) · [Epic 5 acceptance criteria](../planning-artifacts/epics.md#story-54-derive-durable-catalog-completeness-from-contracts) · [September handoff and retrospective gates](../planning-artifacts/sprint-change-proposal-2026-09-14.md)
