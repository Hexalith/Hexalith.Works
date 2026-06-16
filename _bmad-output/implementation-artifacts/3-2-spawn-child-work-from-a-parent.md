---
baseline_commit: 5792291
---

# Story 3.2: Spawn Child Work from a Parent

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a coordinator,
I want a Work Item to spawn child work,
so that a larger obligation can be broken into smaller replayable obligations without losing parent context.

## Acceptance Criteria

1. **Given** a parent Work Item is eligible to spawn child work
   **When** `SpawnChild` is handled
   **Then** `ChildSpawned` is emitted on the parent
   **And** the child creation request follows `CreateWorkItem` semantics with a parent reference.

2. **Given** child work is spawned
   **When** the child Work Item is created
   **Then** the child carries the same Tenant as the parent
   **And** the parent reference is stored as a reference ID.

3. **Given** a parent optionally suspends while spawning a child
   **When** the spawn request includes an await-on-child intent
   **Then** the parent records an Await-Condition for the child completion
   **And** no progress is accepted on the parent while it is Suspended.

4. **Given** the spawn request violates the tree guard
   **When** `SpawnChild` is handled
   **Then** no parent event and no child creation intent are accepted
   **And** the rejection is replay-safe.

5. **Given** spawn behavior is tested
   **When** events are replayed
   **Then** parent state, child reference, and optional await-condition reconstruct deterministically.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile current state and contracts before editing (AC: #1-#5)**
  - [x] Read `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentFacts.cs`,
    `src/Hexalith.Works.Contracts/State/WorkItemState.cs`,
    `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`,
    `src/Hexalith.Works.Contracts/Commands/SuspendWorkItem.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemSuspended.cs`, and
    `docs/work-tree-shape-guard.md` before implementation.
  - [x] Preserve Story 3.1 invariants: parent links use `ParentWorkItemReference`, tree validation is
    pure/caller-fed, depth default remains `32`, breadth is uncapped, and cross-tenant/cycle/second-parent
    failures are domain rejections.
  - [x] Do not add projection traversal, EventStore reads, Dapr, repository reads, generated IDs, or
    runtime configuration to decide whether spawn is valid.

- [x] **Task 2 - Add additive spawn contracts (AC: #1, #2, #4)**
  - [x] Add `src/Hexalith.Works.Contracts/Commands/SpawnChild.cs` as an imperative
    `[PolymorphicSerialization]` sealed partial record.
  - [x] The command must target the parent aggregate with `TenantId` and parent `WorkItemId`, and must
    carry a caller-supplied child `WorkItemId`. `Handle` must not generate the child id.
  - [x] Reuse the `CreateWorkItem` creation shape for child facts where applicable: obligation,
    optional `WorkItemEffort`, optional `WorkItemSchedule`, optional `ExecutorBinding`, and optional
    `ConversationCorrelationId`.
  - [x] Include an explicit await-on-child flag or intent value, for example
    `SuspendParentUntilChildCompletes`, instead of inferring suspension from child fields.
  - [x] Include the caller-fed tree facts the aggregate needs to reuse `WorkTreeAttachmentGuard` without
    reads: proposed parent depth, proposed parent ancestor chain, max-depth policy, and optional existing
    child parent fact if the caller is retrying or reconciling against a previously known child id.
  - [x] Add `src/Hexalith.Works.Contracts/Events/ChildSpawned.cs` as a past-tense
    `[PolymorphicSerialization]` event with `(AggregateId, Sequence)` first and no `EventStore`
    envelope metadata.
  - [x] `ChildSpawned` must carry enough replayable data for the parent to remember the child reference
    and for the adapter/command pipeline to construct an equivalent `CreateWorkItem` command for the
    child with `Parent = new ParentWorkItemReference(parentTenant, parentWorkItemId)`.

- [x] **Task 3 - Represent parent-owned child references without embedding child state (AC: #1, #2, #5)**
  - [x] Add the minimum value object needed for a child reference, or use `WorkItemId` directly if that
    remains clearer. Do not embed child state, roll-up totals, descendant lists, Party/Tenant/Conversation
    data, or EventStore metadata in the parent.
  - [x] Extend `WorkItemState` to reconstruct the parent's spawned child references from `ChildSpawned`.
  - [x] Keep `WorkItemCreated.Parent` unchanged: the child aggregate stores only the existing
    `ParentWorkItemReference` shape.
  - [x] Ensure duplicate replay of the same event stream remains deterministic; do not make `Apply` depend
    on collection ordering outside the event order.

- [x] **Task 4 - Handle `SpawnChild` in the aggregate using the existing tree guard (AC: #1, #2, #4)**
  - [x] Add `WorkItemAggregate.Handle(SpawnChild command, WorkItemState? state)`.
  - [x] Reject spawn when there is no existing parent state to spawn from. Use a specific `IRejectionEvent`
    if an existing rejection is not semantically correct.
  - [x] Accept spawn only from live, non-terminal parent statuses: `Created`, `Assigned`, `Queued`,
    `InProgress`, or `Suspended`. Reject `Unknown`, `Completed`, `Cancelled`, `Rejected`, and `Expired`
    using the existing lifecycle rejection pattern or a more specific rejection if needed.
  - [x] If `SuspendParentUntilChildCompletes` is requested, require the parent to be in `InProgress` so
    `WorkItemSuspended` remains consistent with the lifecycle table. Spawn without suspension may be
    accepted from any live, non-terminal parent status above.
  - [x] Build the proposed child parent reference from the current parent command target:
    `new ParentWorkItemReference(command.TenantId, command.WorkItemId)`.
  - [x] Call `WorkTreeAttachmentGuard.Validate` for the child edge. Supply `TenantId`, child id, proposed
    parent reference, any known child current-parent fact, proposed parent ancestors, proposed parent depth,
    and max-depth policy from the caller-fed facts available to the command.
  - [x] If validation rejects, return only the rejection payload. Do not emit `ChildSpawned`, do not emit
    `WorkItemSuspended`, and do not create/accept a child creation intent.
  - [x] If validation accepts, emit `ChildSpawned` on the parent with the next sequence.
  - [x] The success path must use monotonic parent sequences. If also suspending, emit `ChildSpawned` first
    and `WorkItemSuspended` second with the next sequence.

- [x] **Task 5 - Add the minimal child-completion await shape required by spawn (AC: #3, #5)**
  - [x] Resolve the existing scope tension deliberately: `SuspendWorkItem` currently documents await payloads
    as Story 3.5 scope, but Story 3.2 requires spawn to record a child-completion await condition. Implement
    only the child-completion condition needed by spawn; do not implement date/external matching or the full
    Story 3.5 resume saga.
  - [x] Introduce an additive `AwaitCondition` contract under `src/Hexalith.Works.Contracts/ValueObjects`
    if none exists. For this story, it only needs to represent child completion by child `WorkItemId`.
  - [x] Add an optional nullable/defaulted await-condition field to `WorkItemSuspended` so existing call
    sites and old serialized payloads remain compatible.
  - [x] Extend `WorkItemState` to store the current await conditions when `WorkItemSuspended` is applied.
    A parent that spawned with await intent must replay to `Status = Suspended` with the child-completion
    condition present.
  - [x] Preserve existing `ReportProgress` behavior: `WorkItemAggregate.Handle(ReportProgress, state)`
    already rejects every status except `InProgress`, so a suspended parent accepts no progress.
  - [x] Do not implement `ResumeWorkItem` condition matching, date reminders, external signal correlation,
    reactor dispatch, or timer behavior in this story.

- [x] **Task 6 - Register serialization and contract-flow coverage (AC: #1-#5)**
  - [x] Register new public command/event/rejection payloads with `Hexalith.PolymorphicSerializations` via
    the existing attribute/catalog pattern. Plain value objects are covered through the containing payload
    unless the existing codebase establishes a different pattern.
  - [x] Update `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` count and samples. It currently
    lists **34** payloads: 13 success events, 13 commands, and 8 rejection events.
  - [x] Extend integration contract-flow tests to round-trip `SpawnChild`, `ChildSpawned`, the new
    child-completion await shape, and any new rejection payloads.
  - [x] Confirm serialized domain payloads still omit EventStore envelope fields such as `messageId`,
    `causationId`, `correlationId`, `userId`, `metadata`, and `cloudEvent`.
  - [x] Keep schema evolution additive: no field removals, no field renames, no `V2` payloads. Generate or
    update golden corpus fixtures only through the existing schema-evolution test workflow; do not hand-write
    golden JSON.

- [x] **Task 7 - Add focused unit tests for aggregate behavior (AC: #1-#5)**
  - [x] Cover successful spawn from an existing parent: result contains `ChildSpawned`, parent state replays
    with the child reference, and child create facts include the same tenant and parent reference.
  - [x] Cover successful spawn with await intent: result contains `ChildSpawned` then `WorkItemSuspended`,
    replay sets parent status to `Suspended`, and the child-completion await condition is recorded.
  - [x] Cover progress rejection while the parent is suspended after spawn using the existing
    `ReportProgress` handler.
  - [x] Cover tree-guard rejection paths by supplying facts for cross-tenant child facts, self/cycle facts,
    second-parent facts, and max-depth overflow. Assert no success payload appears with the rejection.
  - [x] Cover replay determinism: applying the success events reconstructs parent sequence, status, child
    references, and await conditions exactly.
  - [x] Cover child id is caller-supplied and no `Guid`, ULID, clock, or random value is generated inside
    `Handle`.

- [x] **Task 8 - Update architecture fitness, documentation, and verification artifacts (AC: #1-#5)**
  - [x] Extend architecture fitness tests if new files expose new purity boundaries. `Contracts`, `Server`,
    and `Projections` must remain free of Dapr, EventStore runtime reads, clocks, filesystem, HTTP,
    databases, random generation, UI, LLM, routing, and cost-governance dependencies.
  - [x] Update `docs/work-tree-shape-guard.md` or add a short spawn doc noting that `SpawnChild` reuses the
    guard and supplies caller-fed facts; do not duplicate the guard rules in a divergent document.
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` after implementation with final
    test counts and commands.
  - [x] Preserve Hexalith dependency policy: use `ProjectReference` for `Hexalith.*` dependencies through
    root submodule path variables; do not add Hexalith package references and do not initialize nested
    submodules.

- [x] **Task 9 - Build and verify the slice (AC: #1-#5)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  - [x] Run direct xUnit v3 binaries if `dotnet test` is blocked by Microsoft.Testing.Platform named-pipe
    permissions:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    and `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.

## Dev Notes

### Scope Boundary

Story 3.2 owns the parent aggregate command/event for spawning child work, the replayable parent child
reference, and the minimal child-completion await condition required when spawn asks the parent to
suspend. It realizes FR-16 and the spawn part of FR-5/FR-13 while reusing the tree-shape guard from
Story 3.1. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.2: Spawn Child Work from a
Parent; _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-16]

**In scope:** `SpawnChild` command, `ChildSpawned` event, parent replay of child references, child
creation facts equivalent to `CreateWorkItem` with `ParentWorkItemReference`, tree-guard reuse,
optional child-completion await condition on spawn, serialization/catalog coverage, and focused unit
and contract tests.

**Out of scope:** recursive roll-up projection, heterogeneous-unit subtotals, date/external await
matching, `ResumeWorkItem` condition matching, reactor dispatch, Dapr reminders, cascade traversal,
query-side authorization, "what's next" ordering, production UI/channel adapters, routing, LLM, cost
governance, and AppHost runtime wiring. Stories 3.3-3.6 and Epic 4 own those behaviors. [Source:
_bmad-output/planning-artifacts/epics.md#Story 3.3; #Story 3.4; #Story 3.5; #Story 3.6]

### Current State (files this story modifies or verifies - read before editing)

- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` currently handles create, lifecycle,
  progress, re-estimate, reschedule, complete/cancel/reject/expire. There is no `SpawnChild` handler.
  `ReportProgress` accepts only `InProgress`, which already enforces "no progress while Suspended."
- `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs` is the reusable pure tree guard.
  It accepts root/no parent, accepts same-parent idempotently, rejects cross-tenant parent or ancestor,
  rejects second parent, rejects self/ancestor cycles, rejects depth over policy, and has no breadth cap.
- `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentFacts.cs` currently models caller-fed facts:
  tenant, child id, proposed parent, current parent, proposed parent ancestors, proposed parent depth,
  and max depth.
- `src/Hexalith.Works.Server/Aggregates/WorkTreeDepthPolicy.cs` defines `DefaultMaxDepth = 32`.
- `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs` already carries `TenantId`, `WorkItemId`,
  `Obligation`, optional initial effort, schedule, parent reference, executor binding, and conversation
  correlation id. Spawn child creation facts should mirror this shape where possible.
- `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs` already carries optional `Parent` as a
  reference-only `ParentWorkItemReference`.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` stores a single `Parent`, `Status`, monotonic
  `Sequence`, initial effort, schedule, executor binding, and conversation correlation id. It has no
  child-reference collection and no await-condition collection yet.
- `src/Hexalith.Works.Contracts/Commands/SuspendWorkItem.cs` and
  `src/Hexalith.Works.Contracts/Events/WorkItemSuspended.cs` currently carry no await-condition payload;
  the command comment says await payloads were deferred to Story 3.5. This story must add only the
  minimal child-completion condition required by spawn, additively and without building full resume saga
  behavior.
- `tests/Hexalith.Works.UnitTests/WorkTreeAttachmentGuardTests.cs` already documents and protects guard
  semantics. Extend rather than duplicate those semantics in a new guard.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` currently lists **34** payloads: 13 success
  events, 13 commands, and 8 rejection events.
- `docs/work-tree-shape-guard.md` explicitly says later stories must supply caller facts and reuse the
  guard before writing new tree edges.

### Key Design Decisions

- **D1 - Child IDs are supplied at the edge.** `SpawnChild` must carry the child `WorkItemId`; the
  aggregate does not call `Guid`, ULID, Commons ID helpers, clocks, RNG, or I/O. [Source:
  _bmad-output/planning-artifacts/architecture.md#Data Architecture; #Pattern Examples]
- **D2 - Parent emits the replayable spawn fact; child creation remains a separate command-pipeline
  concern.** `ChildSpawned` is emitted on the parent and carries the facts needed to construct the child
  `CreateWorkItem` request. Do not synchronously mutate a child aggregate from inside the parent
  aggregate. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-16;
  _bmad-output/planning-artifacts/architecture.md#Event-sourced on Hexalith.EventStore]
- **D3 - The tree guard is reused, not rewritten.** Spawn supplies child-edge facts to
  `WorkTreeAttachmentGuard.Validate`. `SpawnChild` must therefore carry the needed caller-fed facts:
  parent depth, parent ancestor chain, max-depth policy, and optional known child current-parent. Do not
  read projections or EventStore in the aggregate to discover these facts. [Source:
  docs/work-tree-shape-guard.md#Rules]
- **D4 - Await scope is intentionally narrow.** Add the child-completion await condition required by
  optional spawn suspension. Do not implement date/external conditions, resume matching, or reminders in
  this story. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.2; #Story 3.5]
- **D5 - Parent references remain lightweight.** Parent state may remember child IDs for replay and future
  projections, but it must not embed child state or roll-up totals. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-5]

### Technical Requirements

- Keep `Contracts`, `Server`, and `Projections` infrastructure-free and pure. No Dapr, EventStore runtime
  reads, HTTP, filesystem, clock/timer, generated IDs, UI, LLM, routing, or cost-governance dependency in
  the kernel. [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries]
- Domain rejections must implement `IRejectionEvent`; `DomainResult` must never mix success and rejection
  payloads. [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns]
- Durable success events carry `(AggregateId, Sequence)` first. New rejection events carry enough
  diagnostic data for tests/future ProblemDetails mapping, but no EventStore envelope metadata. [Source:
  _bmad-output/planning-artifacts/epics.md#AR-4; #NFR-2]
- Register every new public command/event/rejection payload with `Hexalith.PolymorphicSerializations` and
  update catalog/serialization tests. [Source: _bmad-output/planning-artifacts/epics.md#NFR-12]
- Additive serialization only: no field renames, no removals, no `V2` event types. Optional additions to
  existing records must have nullable/defaulted parameters so existing payloads remain deserializable.
  [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-12]
- Use xUnit v3 + Shouldly. Keep tier-1 tests pure and fast. [Source:
  _bmad-output/planning-artifacts/architecture.md#Test-type taxonomy]
- Use project references for Hexalith libraries through root submodules. Do not add `PackageReference` or
  `Directory.Packages.props` entries for `Hexalith.*` packages, and do not initialize nested submodules.
  [Source: AGENTS.md#Hexalith library references; AGENTS.md#Submodule rules]

### Project Structure Notes

- New command contracts belong in `src/Hexalith.Works.Contracts/Commands`.
- New durable event contracts belong in `src/Hexalith.Works.Contracts/Events`.
- New rejection contracts, if required, belong in `src/Hexalith.Works.Contracts/Events/Rejections`.
- Await and child-reference value objects belong in `src/Hexalith.Works.Contracts/ValueObjects` unless the
  existing implementation chooses a direct `WorkItemId` list for child references.
- Aggregate behavior stays in `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`; tree guard facts
  and validation stay beside the existing guard unless a small extension is needed.
- Serialization/catalog tests stay under `tests/Hexalith.Works.IntegrationTests`; aggregate tests stay under
  `tests/Hexalith.Works.UnitTests`; architecture fitness changes stay under
  `tests/Hexalith.Works.ArchitectureTests/FitnessTests`.
- Do not create `.UI`, `.Mcp`, portal, `.Security`, routing, LLM, cost-governance, production channel
  adapter, repository, database, or Dapr actor projects for this story.

### Previous Story Intelligence

- Story 3.1 completed in commit `5792291` and introduced the pure tree-shape guard plus rejection
  contracts: `WorkItemCannotReferenceSecondParent`, `WorkItemTreeCycleRejected`, and
  `WorkItemTreeDepthExceeded`.
- Story 3.1 updated `WorkItemAggregate.Handle(CreateWorkItem)` to call the guard for create-time parent
  validation. Spawn should follow the same fail-closed domain-rejection pattern.
- Story 3.1 documented tree rules in `docs/work-tree-shape-guard.md`; later stories are expected to supply
  caller facts and reuse the guard before writing tree edges.
- Story 3.1 finished with **351** green tests: UnitTests 278, IntegrationTests 46, ArchitectureTests 26,
  PropertyTests 1. Keep test-summary and the Dev Agent Record counts in sync after implementation.
- Direct execution of built xUnit v3 test binaries remains the reliable verification path in this sandbox
  when `dotnet test` is blocked by Microsoft.Testing.Platform named-pipe permissions.

### Git Intelligence Summary

- Recent commits show the project is moving story-by-story through focused domain slices:
  `5792291 feat(story-3.1): Guard tenant-safe work tree shape`,
  `c1ba6bb test(story-2.5): Verify terminal work decisions`,
  `1814301 feat(story-2.4): Re-estimate and reschedule work`.
- Story changes have been additive, with focused contract/event additions, catalog updates, unit tests,
  integration serialization tests, and architecture fitness tests rather than broad infrastructure work.

### Latest Technical Information

- No new third-party libraries are needed for this story. Use the repository-pinned stack already captured
  in architecture and project context: .NET SDK 10.0.301 with `rollForward: latestPatch`, xUnit v3 3.2.2,
  Shouldly, and `Hexalith.PolymorphicSerializations`.
- Do not upgrade Dapr, Aspire, Fluent UI, xUnit, Roslyn, or SDK pins as part of this story.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.2: Spawn Child Work from a Parent]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-5]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-13]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-16]
- [Source: _bmad-output/planning-artifacts/architecture.md#Data Architecture]
- [Source: _bmad-output/planning-artifacts/architecture.md#Implementation Patterns & Consistency Rules]
- [Source: docs/work-tree-shape-guard.md#Rules]
- [Source: _bmad-output/implementation-artifacts/3-1-guard-tenant-safe-work-tree-shape.md#Previous Story Intelligence]
- [Source: AGENTS.md#Hexalith library references]
- [Source: AGENTS.md#Submodule rules]

## Dev Agent Record

### Agent Model Used

GPT-5

### Debug Log References

- 2026-06-16T21:12:21+02:00 — Sprint status moved to `in-progress`; existing
  `baseline_commit: 5792291` preserved.
- Red phase: added focused spawn unit and integration tests; targeted unit build failed on missing
  `SpawnChild` contract as expected.
- Green/refactor phase: implemented `SpawnChild`, `ChildSpawned`, `AwaitCondition`,
  `WorkItemSuspended.AwaitCondition`, parent replay of spawned child ids/current await conditions, and
  `WorkItemAggregate.Handle(SpawnChild)`.
- Serialization phase: updated `WorkItemV1Catalog` to 36 payloads and generated
  `ChildSpawned.v1.json` from a temporary production-serializer emitter; temporary emitter was deleted.
- Validation: `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  passed.
- Validation: `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  passed with 0 warnings and 0 errors.
- Validation: xUnit v3 executables passed at the dev baseline — UnitTests 296/296, IntegrationTests
  52/52, ArchitectureTests 26/26, PropertyTests 1/1.
- Review (2026-06-16) re-verified the full suite after the QA pass: restore/build clean (0 warnings,
  0 errors) and xUnit v3 executables green — UnitTests 307/307, IntegrationTests 52/52,
  ArchitectureTests 26/26, PropertyTests 1/1 (**386 total**).

### Completion Notes List

- Implemented parent-owned spawn as an additive domain slice: the parent emits `ChildSpawned`, stores
  only child `WorkItemId` references, and carries enough event facts for an adapter/command pipeline to
  construct an equivalent child `CreateWorkItem` with `ParentWorkItemReference`.
- Reused `WorkTreeAttachmentGuard` with caller-fed facts from `SpawnChild`; the aggregate performs no
  projection traversal, EventStore reads, repository reads, runtime configuration lookup, clocks, or id
  generation.
- Added the minimal child-completion await condition required by Story 3.2. Spawn-with-await emits
  `ChildSpawned` then `WorkItemSuspended` with monotonic parent sequences and replays to
  `Status = Suspended` with `AwaitCondition(childWorkItemId)`.
- Preserved existing progress behavior: suspended parents reject `ReportProgress` through the existing
  transition rejection path.
- Dev baseline finished at 375 green tests (UnitTests 296, IntegrationTests 52, ArchitectureTests 26,
  PropertyTests 1). A subsequent QA gap-filling pass added 11 unit tests (5 methods, two Theories) to
  `WorkItemSpawnChildTests.cs`, raising the suite to **386 green** (UnitTests 307, IntegrationTests 52,
  ArchitectureTests 26, PropertyTests 1) — matching `tests/test-summary.md`. No production code changed in
  that pass.

### File List

- `_bmad-output/implementation-artifacts/3-2-spawn-child-work-from-a-parent.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/work-tree-shape-guard.md`
- `src/Hexalith.Works.Contracts/Commands/SpawnChild.cs`
- `src/Hexalith.Works.Contracts/Commands/SuspendWorkItem.cs`
- `src/Hexalith.Works.Contracts/Events/ChildSpawned.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemSuspended.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/AwaitCondition.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/ChildSpawned.v1.json`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemSpawnChildContractFlowTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs`

### Change Log

| Date | Version | Description | Author |
|------|---------|-------------|--------|
| 2026-06-16 | 0.1 | Implemented spawn-child contracts, aggregate handling, parent replay state, child-completion await condition, serialization/golden coverage, documentation, and validation artifacts. Status set to review. | GPT-5 Codex |
| 2026-06-16 | 0.2 | Adversarial code review: re-verified all ACs and tasks against the implementation; build clean and 386/386 tests green. Corrected stale Dev Agent Record test counts (375→386, UnitTests 296→307) to match `tests/test-summary.md`. Added a doc comment to `SpawnChild` noting the `MaxDepth`/depth literals mirror `WorkTreeDepthPolicy.DefaultMaxDepth`. No CRITICAL/HIGH issues found. Status set to done. | Jérôme Piquot |

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot — **Date:** 2026-06-16 — **Outcome:** Approve (status → done)

**Scope verified:** all 16 File-List files read; git working tree cross-checked; `dotnet restore`/`build`
clean (0 warnings, 0 errors); all four xUnit v3 suites executed green (UnitTests 307, IntegrationTests
52, ArchitectureTests 26, PropertyTests 1 — **386 total**).

**Acceptance Criteria:** AC #1–#5 all **IMPLEMENTED** and covered by tests — `SpawnChild` → `ChildSpawned`
on the parent with `CreateWorkItem`-equivalent child facts (AC #1/#2); same-tenant child via reused
parent `TenantId` and `ParentWorkItemReference` (AC #2); optional `SuspendParentUntilChildCompletes`
emits `ChildSpawned` then `WorkItemSuspended` with an `AwaitCondition`, and suspended parents reject
`ReportProgress` (AC #3); tree-guard violations return rejection-only, replay-safe, burning no sequence
(AC #4); replay reconstructs sequence/status/child refs/await conditions deterministically (AC #5).

**Task audit:** every `[x]` task verified done against the code (no falsely-completed tasks). The
aggregate reuses `WorkTreeAttachmentGuard` with caller-fed facts and performs no EventStore/projection
reads, clocks, or id generation (D1–D5 honored). `AwaitCondition` follows the existing value-object
convention (`ParentWorkItemReference`/`Obligation`).

**Findings:**
- MEDIUM (fixed) — Dev Agent Record test counts were stale (375/UnitTests 296) versus the verified
  386/UnitTests 307; corrected to match `tests/test-summary.md`.
- MEDIUM (noted, no fix) — `Hexalith.FrontComposer` and `Hexalith.Parties` submodule pointers are
  modified in the working tree but are unrelated to story 3.2 and absent from the File List; left
  untouched (out of scope; donor-submodule drift).
- LOW (fixed) — `SpawnChild.MaxDepth = 32` literal can drift from `WorkTreeDepthPolicy.DefaultMaxDepth`
  (Contracts cannot reference the Server constant); added a doc comment requiring the two stay in sync.

**Security/quality:** pure `Contracts` + `Server` slice — no injection/auth/IO surface; cross-tenant
attachment is fail-closed via the tree guard. No performance concerns (in-memory, monotonic sequence).
