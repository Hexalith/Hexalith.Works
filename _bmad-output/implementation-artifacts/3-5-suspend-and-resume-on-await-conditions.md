---
baseline_commit: 61ec4c5
---

# Story 3.5: Suspend and Resume on Await-Conditions

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a coordinator,
I want a Work Item to suspend on one or more Await-Conditions and resume on the first matching trigger,
so that long-running work can park safely until a child completes, a date arrives, or an external signal is received.

## Acceptance Criteria

1. **Given** an `InProgress` Work Item
   **When** it is suspended with one or more Await-Conditions
   **Then** `WorkItemSuspended` records each Await-Condition kind and correlation key
   **And** the item transitions to `Suspended`.

2. **Given** a Work Item is `Suspended`
   **When** progress is reported before a matching resume
   **Then** the progress command is rejected
   **And** current Remaining still participates in Roll-Up.

3. **Given** a resume command carries a correlation key matching one current Await-Condition
   **When** `ResumeWorkItem` is handled
   **Then** `WorkItemResumed` is emitted with the consumed Await-Condition key
   **And** the item transitions back to `InProgress`
   **And** all Await-Conditions from that suspension are cleared.

4. **Given** a `ResumeWorkItem` command carries no key matching the current Await-Condition set while the item is `Suspended`
   **When** the command is handled
   **Then** the command emits a domain rejection
   **And** the item remains `Suspended`.

5. **Given** a `ResumeWorkItem` command repeats the consumed key from the accepted `WorkItemResumed` event
   **When** the duplicate command is handled after the item has already resumed
   **Then** the command returns `DomainResult.NoOp`
   **And** no duplicate `WorkItemResumed` event is emitted.

6. **Given** child-completion resumes are required
   **When** a child completes
   **Then** the pure reactor translation can produce a parent `ResumeWorkItem` command intent for matching child-completion Await-Conditions
   **And** the aggregate, not the reactor, decides whether the resume is accepted.

7. **Given** date and external resumes are required seams
   **When** the contracts are inspected
   **Then** `DateReached` and `ExternalSignal` Await-Condition cases exist
   **And** the aggregate never reads a clock or calls an external adapter.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile the existing placeholder suspend/resume slice before writing code (AC: #1-#7)**
  - [x] Read `src/Hexalith.Works.Contracts/ValueObjects/AwaitCondition.cs`,
    `src/Hexalith.Works.Contracts/Commands/SuspendWorkItem.cs`,
    `src/Hexalith.Works.Contracts/Commands/ResumeWorkItem.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemSuspended.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemResumed.cs`,
    `src/Hexalith.Works.Contracts/State/WorkItemState.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs`,
    `src/Hexalith.Works.Reactor/WorksReactorAssembly.cs`,
    `tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs`,
    `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs`,
    `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs`,
    `tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs`,
    `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs`,
    and the suspend/resume portions of `tests/Hexalith.Works.IntegrationTests`.
  - [x] Confirm the current behavior is only a placeholder: `AwaitCondition` models child completion only;
    `SuspendWorkItem` carries no conditions; `ResumeWorkItem` carries no key; `WorkItemSuspended` carries
    one nullable `AwaitCondition`; `WorkItemResumed` carries no consumed key; `WorkItemState.Apply` clears
    then adds at most one condition; `Handle(ResumeWorkItem)` accepts any resume from `Suspended`.
  - [x] Preserve Story 3.2 spawn behavior: `SpawnChild(..., SuspendParentUntilChildCompletes: true)` still
    emits `ChildSpawned` followed by `WorkItemSuspended` and records a child-completion await condition.
  - [x] Preserve Story 3.3/3.4 roll-up behavior: `WorkItemSuspended` and `WorkItemResumed` update status
    only; a suspended item keeps its current own Remaining and continues to contribute to roll-up.

- [x] **Task 2 - Make Await-Condition a discriminated, key-comparable contract (AC: #1, #3, #6, #7)**
  - [x] Extend `AwaitCondition` to represent exactly these cases: `ChildCompleted(childId)`,
    `DateReached(instant)`, and `ExternalSignal(correlationId)`.
  - [x] Give every condition a stable kind and correlation key that can be compared without clock, I/O,
    adapter calls, or sibling-module hydration. Use ordinal/value equality; do not infer one condition
    kind from another.
  - [x] Keep the existing `new AwaitCondition(WorkItemId childWorkItemId)` child-completion construction
    path working or update all current callers/tests in one focused pass. Do not introduce a second,
    parallel await type.
  - [x] Validate null/empty keys at construction boundaries. A malformed await condition should fail
    before event emission, not create a partially matchable suspended state.

- [x] **Task 3 - Update suspend/resume command and event contracts additively (AC: #1, #3, #5, #7)**
  - [x] Change `SuspendWorkItem` to carry one or more `AwaitCondition`s. Direct suspend without any condition
    is no longer a valid Story 3.5 command; it should return a domain rejection and leave state unchanged.
  - [x] Change `ResumeWorkItem` to carry the matching `AwaitCondition` or its stable correlation key. Prefer
    passing the `AwaitCondition` value when it keeps kind+key unambiguous.
  - [x] Change `WorkItemSuspended` to record the full await-condition set for the suspension. Preserve
    backward-compatible deserialization of the existing single nullable `AwaitCondition` payload from
    Story 3.2 if the implementation changes the serialized shape.
  - [x] Change `WorkItemResumed` to record the consumed await-condition key/value. This field is required
    for new events and should deserialize older golden payloads tolerantly where the field is absent.
  - [x] Register any changed command/event contract shape with the existing polymorphic serialization
    catalog and update the golden-payload corpus intentionally. Additive schema evolution only: no `V2`
    events, no event renames, no removal of existing fields without compatibility handling.

- [x] **Task 4 - Enforce first-match resume semantics in the aggregate and replay state (AC: #1-#5, #7)**
  - [x] `Handle(SuspendWorkItem)` accepts only from `InProgress` and only when the command carries at least
    one valid await condition. It emits exactly one `WorkItemSuspended` with all conditions and advances
    sequence once.
  - [x] `WorkItemState.Apply(WorkItemSuspended)` sets Status `Suspended`, replaces the current await set
    with the event's full set, and clears any previous consumed-resume key.
  - [x] `Handle(ResumeWorkItem)` from `Suspended` accepts only when the supplied key/value matches one of
    `state.AwaitConditions`. On match, emit exactly one `WorkItemResumed` with the consumed key/value.
  - [x] `WorkItemState.Apply(WorkItemResumed)` sets Status `InProgress`, clears the full await-condition
    set from that suspension, and stores the consumed key/value as replayed state so duplicate detection
    survives rehydration.
  - [x] A `ResumeWorkItem` while `Suspended` with no matching current condition returns a domain rejection,
    emits no `WorkItemResumed`, burns no sequence number, and leaves all await conditions intact.
  - [x] A duplicate `ResumeWorkItem` after the item has already returned to `InProgress` returns
    `DomainResult.NoOp` only when its key/value equals the last consumed `WorkItemResumed` key/value.
    A different post-resume key remains a normal transition rejection; do not silently accept unrelated
    triggers.
  - [x] The aggregate must not read `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, timers, Dapr,
    HTTP, filesystem, RNG, or any external adapter. Date and external signals arrive only as commands.

- [x] **Task 5 - Add the pure child-completion reactor translation without runtime dispatch (AC: #6)**
  - [x] Add a small pure translation surface under `src/Hexalith.Works.Reactor` that can turn a
    `WorkItemCompleted` child event plus an explicit awaiting-parent snapshot/read-model input into one
    or more parent `ResumeWorkItem` command intents for matching `ChildCompleted(childId)` conditions.
  - [x] Keep the reactor mechanical: it must not decide whether the parent should resume, whether the
    await is still current, whether the parent is in `Suspended`, or whether the command is a duplicate.
    Those decisions belong to `WorkItemAggregate.Handle`.
  - [x] Do not implement Dapr dispatch, checkpoint persistence, retry loops, reminder registration,
    reminder reconciliation, or Aspire crash/recovery proof in this story. Those are deferred runtime
    concerns owned by later stories, especially Story 4.6.
  - [x] Keep `Hexalith.Works.Reactor` referencing inward only. Do not add infrastructure packages,
    logging I/O, Dapr actor APIs, timers, or host wiring to satisfy AC #6.

- [x] **Task 6 - Add focused unit tests for suspend/resume behavior (AC: #1-#5, #7)**
  - [x] Add or refactor tests in `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs` and/or a focused
    `WorkItemSuspendResumeTests.cs` using xUnit v3 + Shouldly. Reuse `WorkItemStateBuilder`; do not create
    a parallel lifecycle harness.
  - [x] Cover `SuspendWorkItem` from `InProgress` with multiple conditions (`ChildCompleted`,
    `DateReached`, `ExternalSignal`) emitting `WorkItemSuspended`, replaying to `Suspended`, and preserving
    the full condition set.
  - [x] Cover keyless/empty suspend rejection and invalid lifecycle sources (`Created`, `Assigned`,
    `Queued`, `Suspended`, terminals) leaving state and sequence unchanged.
  - [x] Cover progress from `Suspended` rejecting through the existing transition-rejection path while
    effort remains unchanged.
  - [x] Cover matching resume consuming one condition, emitting `WorkItemResumed` with the consumed key,
    replaying to `InProgress`, and clearing all conditions from that suspension.
  - [x] Cover non-matching resume while suspended returning a rejection and preserving the full await set.
  - [x] Cover duplicate consumed-key resume after replayed `WorkItemResumed` returning `DomainResult.NoOp`
    with no event, and a different post-resume key returning a rejection.
  - [x] Cover DateReached and ExternalSignal as command-delivered data; no test should depend on wall-clock
    sleeps or actual time passing.

- [x] **Task 7 - Add reactor, serialization, integration, and roll-up regression coverage (AC: #2, #3, #6, #7)**
  - [x] Add pure reactor unit tests proving `WorkItemCompleted(child)` + matching awaiting-parent input
    produces a parent `ResumeWorkItem` intent, and non-matching input produces no intent. Assert the reactor
    does not inspect parent state or decide acceptance.
  - [x] Update `WorkItemLifecycleContractFlowTests` so the serialized full lifecycle uses a real await
    condition and a matching `ResumeWorkItem`; replay must converge to `InProgress` after resume and
    `Completed` after completion.
  - [x] Update `WorkItemSerializationRegistrationTests`, `WorkItemV1Catalog`, golden corpus files, and
    schema-evolution tests for the changed command/event shapes. Keep old/legacy payload tolerance for
    `WorkItemSuspended` and `WorkItemResumed` where fields were previously absent.
  - [x] Add/adjust `WorkItemSpawnChildTests` and `WorkItemSpawnChildContractFlowTests` so spawn-suspend
    emits a child-completion await condition in the new shape and replay preserves it.
  - [x] Add a roll-up regression in `WorkItemRollUpProjectionTests` if existing coverage is not explicit:
    a suspended child with Remaining still contributes its current Remaining; a resumed child only changes
    status and does not alter Remaining.

- [x] **Task 8 - Update docs, architecture fitness, and verification notes (AC: #1-#7)**
  - [x] Update `docs/lifecycle-transition-matrix.md`: remove the placeholder "No await payload in v1";
    document that Suspend requires one or more await conditions, Resume requires a matching key while
    suspended, and duplicate consumed-key resume is the only resume no-op.
  - [x] Add or update a short suspend/resume section in the relevant domain docs if one exists. State the
    first-match policy clearly: accepted resume consumes one key, clears the full suspension condition set,
    and records the consumed key for duplicate detection.
  - [x] Keep architecture fitness tests green: no clock/timer/Dapr/HTTP/file/infra references in
    `Contracts`, `Server`, or `Projections`; reactor pure translation only; no UI/MCP/channel adapter
    project is created.
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` with final Story 3.5 commands
    and counts. Story 3.4 baseline is **416** green tests: UnitTests 335, IntegrationTests 52,
    ArchitectureTests 28, PropertyTests 1.
  - [x] Preserve Hexalith dependency policy: use `ProjectReference` for Hexalith libraries via root
    submodule path variables; no `Hexalith.*` `PackageReference`; do not initialize nested submodules.

- [x] **Task 9 - Build and verify the slice (AC: #1-#7)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
    must finish with 0 warnings and 0 errors.
  - [x] Run direct xUnit v3 binaries, the reliable path in this sandbox:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    and `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.

## Dev Notes

### Scope Boundary

Story 3.5 realizes FR-14 and FR-15 in the pure domain slice: the aggregate can park an `InProgress`
Work Item on a set of await conditions and resume only when an inbound command presents a matching
condition key. The accepted resume is first-match: it consumes one condition, clears the full set from
that suspension, records the consumed key in `WorkItemResumed`, and returns the item to `InProgress`.
[Source: _bmad-output/planning-artifacts/epics.md#Story 3.5: Suspend and Resume on Await-Conditions;
_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-14; #FR-15]

**In scope:** await-condition contract shape for `ChildCompleted`, `DateReached`, and `ExternalSignal`;
multi-condition suspension; matching resume; mismatch rejection; duplicate consumed-key no-op; replay
state that preserves the last consumed key; child-completion pure reactor command-intent translation;
serialization/golden-corpus updates; unit/integration/architecture/roll-up regression tests.

**Out of scope:** Dapr actor reminders, reminder registration names, reminder reconciliation after host
restart, Dapr dispatch, checkpoint persistence, retry loops, AppHost crash/recovery proof, external
webhook/email adapters, UI rendering of "Waiting on", cascade terminal behavior, and any production
channel adapter. Timer/runtime recovery is deferred to Story 4.6; cascade terminal traversal is Story
3.6. [Source: _bmad-output/planning-artifacts/sprint-change-proposal-2026-06-15.md#Proposal F;
_bmad-output/planning-artifacts/epics.md#Story 3.6: Cascade Terminal Work Through Active Descendants]

### Current State (files this story modifies or verifies - read before editing)

- `src/Hexalith.Works.Contracts/ValueObjects/AwaitCondition.cs` is currently child-only:
  `new AwaitCondition(WorkItemId childWorkItemId)` and `ChildWorkItemId`. It has no kind, no generic
  correlation key, no DateReached, and no ExternalSignal.
- `src/Hexalith.Works.Contracts/Commands/SuspendWorkItem.cs` carries only `TenantId` and `WorkItemId`.
  Its XML summary explicitly says direct suspend commands carry no await payload; Story 3.5 must replace
  that placeholder.
- `src/Hexalith.Works.Contracts/Commands/ResumeWorkItem.cs` carries only `TenantId` and `WorkItemId`.
  Its XML summary says correlation-key matching is out of scope; Story 3.5 brings that matching into scope.
- `src/Hexalith.Works.Contracts/Events/WorkItemSuspended.cs` carries one nullable `AwaitCondition`.
  Story 3.2 uses it for child completion, and integration tests include a legacy payload without the field.
  Preserve backward-compatible deserialization when moving to a full condition set.
- `src/Hexalith.Works.Contracts/Events/WorkItemResumed.cs` currently has the base shape only
  `(AggregateId, Sequence, TenantId, WorkItemId)`. Story 3.5 must add the consumed key/value so duplicate
  resume can be recognized after replay.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` already stores `_awaitConditions`, but
  `Apply(WorkItemSuspended)` clears and adds only the single nullable condition, while
  `Apply(WorkItemResumed)` clears the list and does not remember the consumed key. Add replay state for
  the last consumed await key/value.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` currently accepts `SuspendWorkItem` from
  `InProgress` and emits a keyless `WorkItemSuspended`; it accepts any `ResumeWorkItem` from `Suspended`
  and emits a keyless `WorkItemResumed`. This is the main behavior gap.
- `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` encodes the status matrix. Keep the status
  transition rules, but add key/payload validation in the aggregate around the accepted Suspended -> InProgress
  cell. The lifecycle matrix alone is not enough for Story 3.5.
- `src/Hexalith.Works.Reactor/WorksReactorAssembly.cs` is only a marker type. Story 3.5 may add pure
  command-intent translation here, but not runtime dispatch or Dapr infrastructure.
- `tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs` builds `Suspended` with a keyless
  `WorkItemSuspended`; update it so arranged suspended states have at least one valid await condition by
  default or add an overload for condition-specific setup.
- `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs` and `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs`
  currently use keyless suspend/resume. They must be updated to the new condition-carrying commands.
- `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs` and `tests/Hexalith.Works.IntegrationTests/WorkItemSpawnChildContractFlowTests.cs`
  already assert spawn-with-await records child completion. Update assertions to the new multi-condition
  shape rather than adding duplicate spawn tests.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` already treats suspend/resume as
  status-only projection events. Keep that invariant and add only missing regressions.

### Key Design Decisions

- **D1 - First-match clears the whole suspension set.** A matching resume consumes one condition but clears
  all conditions from that suspension. The sprint change proposal explicitly replaced ambiguous
  "clear or retain" wording with "all Await-Conditions from that suspension are cleared." [Source:
  _bmad-output/planning-artifacts/sprint-change-proposal-2026-06-15.md#Proposal F]
- **D2 - Duplicate resume needs replayed consumed-key state.** After a successful resume, `Status` is
  `InProgress` and the current await set is empty. To return `DomainResult.NoOp` for a duplicate consumed
  key after rehydration, `WorkItemResumed` must carry the consumed key/value and `WorkItemState.Apply`
  must remember it. Without this, the implementer can only reject duplicates or no-op too broadly.
- **D3 - Matching is exact and kind-aware.** `ChildCompleted(child-1)`, `DateReached(2026-07-15T10:00:00Z)`,
  and `ExternalSignal(child-1)` are different conditions even if their serialized key text collides.
  Compare kind + key/value, not key text alone.
- **D4 - Reactor remains mechanical.** The pure reactor translation may map a child-completed event plus
  explicit awaiting-parent input into `ResumeWorkItem` intents. It must not decide whether the resume is
  legal, current, or duplicate; `WorkItemAggregate.Handle` owns that decision. [Source:
  _bmad-output/planning-artifacts/architecture.md#C1]
- **D5 - Date/external awaits are contracts, not adapters.** Story 3.5 defines the data shape and command
  path for date and external signals. It does not schedule timers, inspect clocks, or call external
  systems. `Handle` remains clock-free. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Await-Condition (FR-14)]
- **D6 - Additive event evolution only.** Existing Story 3.2/3.4 golden payloads include
  `WorkItemSuspended` and `WorkItemResumed`. Any contract update must preserve deserialization of older
  payloads and intentionally update/add fixtures for the new shape. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-12]

### Technical Requirements

- Keep `Contracts`, `Server`, and `Projections` pure and infrastructure-free: no Dapr, EventStore.Server,
  HTTP, filesystem, timers/clocks, generated IDs, logging I/O, UI, LLM, routing, or cost-governance
  dependency. [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries]
- Use the existing `DomainResult` conventions: success and rejection payloads never mix; rejections are
  `IRejectionEvent`; duplicate consumed-key resume is `DomainResult.NoOp` with no events. [Source:
  _bmad-output/planning-artifacts/architecture.md#Format Patterns]
- Every new or changed command/event stays under the v1 naming rules: commands imperative with no
  `Command` suffix, events past-tense with no `Event` suffix, sealed records, file-scoped namespaces.
  [Source: _bmad-output/planning-artifacts/architecture.md#Naming Patterns]
- Keep event payloads as raw acts and Works-owned facts only. Do not copy Party, Tenant, Conversation,
  EventStore envelope, external adapter, or timer runtime details into suspend/resume events. [Source:
  _bmad-output/planning-artifacts/epics.md#FR-7; #FR-21]
- Use xUnit v3 + Shouldly; tests must be deterministic and pure unless they explicitly exercise the
  existing serialization boundary. No wall-clock sleeps for date awaits. [Source:
  Hexalith.EventStore/_bmad-output/project-context.md#Testing Rules; _bmad-output/planning-artifacts/architecture.md#Test-type taxonomy]
- No new third-party packages and no version upgrades. The repo-pinned stack remains .NET SDK `10.0.301`,
  Aspire `13.4.3`, Dapr `1.18.2`, xUnit v3 `3.2.2`, Shouldly `4.3.0`, FsCheck `3.3.3`. [Source:
  global.json; Directory.Packages.props; _bmad-output/planning-artifacts/architecture.md#Selected Starter]

### Project Structure Notes

- Await-condition contracts belong in `src/Hexalith.Works.Contracts/ValueObjects`.
- Suspend/resume commands and events belong in `src/Hexalith.Works.Contracts/Commands` and
  `src/Hexalith.Works.Contracts/Events`.
- Matching/idempotency behavior belongs in `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
  and replay state in `src/Hexalith.Works.Contracts/State/WorkItemState.cs`.
- Pure child-completion translation belongs under `src/Hexalith.Works.Reactor`; runtime dispatch,
  Dapr, reminders, checkpoints, and AppHost wiring do not belong in this story.
- Tests should extend the existing UnitTests, IntegrationTests, ArchitectureTests, and Testing helpers.
  Do not create a new test harness when `WorkItemStateBuilder` and existing lifecycle helpers can be
  extended.
- Do not create `.UI`, `.Mcp`, portal, `.Security`, channel-adapter, database, Dapr-actor, repository,
  runtime-host, or reminder projects for this story.

### Previous Story Intelligence

- Story 3.4 completed in commit `61ec4c5` and left the workspace at **416** green tests: UnitTests 335,
  IntegrationTests 52, ArchitectureTests 28, PropertyTests 1. Use that as the baseline.
- Story 3.4 established that projection diagnostics must be pure data, not logging I/O, and that
  `Projections` must remain Contracts-only. Carry the same purity discipline into Story 3.5.
- Story 3.3/3.4 roll-up code already handles `WorkItemSuspended`/`WorkItemResumed` as status changes and
  keeps Remaining contributions separate from status. Do not make the projection infer resume eligibility
  or change Remaining when status flips.
- The working tree already has unrelated modified submodule pointers (`Hexalith.FrontComposer`,
  `Hexalith.Parties`) and a story-automator orchestration file. Do not revert or depend on those.
- The reliable verification path remains restore -> build -> direct xUnit v3 binaries.

### Git Intelligence Summary

- Recent commits show tight additive story slices with tests/docs and no broad refactors:
  `61ec4c5 feat(story-3.4): Preserve heterogeneous unit subtotals`,
  `5c95d1e feat(story-3.3): Maintain recursive roll-up with per-child sequence`,
  `eaeaf2e feat(story-3.2): Spawn child work from a parent`,
  `5792291 feat(story-3.1): Guard tenant-safe work tree shape`,
  `c1ba6bb test(story-2.5): Verify terminal work decisions`.
- Story 3.5 is expected to touch durable contracts, so it should update serialization registration,
  golden corpus, unit/integration tests, docs, and test summary together.

### Latest Technical Information

- No new web/API research is required for this story. The implementation uses already-pinned local
  .NET/Hexalith contracts and pure in-process logic.
- Do not upgrade Dapr/Aspire/xUnit or introduce a scheduler/reminder package to satisfy DateReached.
  The story only defines the command/event seam; runtime scheduling is deferred.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.5: Suspend and Resume on Await-Conditions]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-5]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-14]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-15]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-5]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-9]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Await-Condition (FR-14)]
- [Source: _bmad-output/planning-artifacts/sprint-change-proposal-2026-06-15.md#Proposal F: Pin First-Match Await-Condition Behavior]
- [Source: _bmad-output/planning-artifacts/architecture.md#C1]
- [Source: _bmad-output/planning-artifacts/architecture.md#C4]
- [Source: _bmad-output/planning-artifacts/architecture.md#Communication Patterns]
- [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Status pill]
- [Source: _bmad-output/implementation-artifacts/3-4-preserve-heterogeneous-unit-subtotals.md#Previous Story Intelligence]
- [Source: docs/lifecycle-transition-matrix.md]
- [Source: AGENTS.md#Hexalith library references — ALWAYS use ProjectReference, NEVER PackageReference]
- [Source: AGENTS.md#Submodule rules — READ BEFORE RUNNING ANY git submodule COMMAND]

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-16: Confirmed placeholder behavior in existing contracts/state/aggregate before implementation.
- 2026-06-16: `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` passed.
- 2026-06-16: `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` passed with 0 warnings and 0 errors.
- 2026-06-16: Direct xUnit v3 binaries passed (dev-story pass): UnitTests 352/352, IntegrationTests 54/54, ArchitectureTests 28/28, PropertyTests 1/1.
- 2026-06-16: Final counts after the QA gap-filling pass (+16 unit, +6 integration): UnitTests 368/368, IntegrationTests 60/60, ArchitectureTests 28/28, PropertyTests 1/1 (457 total). See `tests/test-summary.md`.
- 2026-06-17: Automated review pass re-verified the slice — clean build (0 warnings / 0 errors) and 457/457 green binaries reproduced.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Implemented kind-aware await conditions for `ChildCompleted`, `DateReached`, and `ExternalSignal`.
- Updated suspend/resume commands and events to carry condition sets and consumed resume conditions while preserving legacy JSON tolerance.
- Enforced first-match resume semantics, mismatch rejection, no sequence burn on rejection, and duplicate consumed-key no-op after replay.
- Added pure child-completion reactor translation with no runtime dispatch or parent-state acceptance decision.
- Updated unit, integration, golden-corpus, roll-up, docs, and verification summary coverage.

### Change Log

- 2026-06-16: Implemented Story 3.5 suspend/resume await-condition contracts, aggregate behavior, reactor translation, docs, tests, and verification notes.
- 2026-06-17: Automated code review (story-automator) — added the omitted `AwaitConditionSerializationContractFlowTests.cs` to the File List, reconciled the Debug Log test counts with the final QA-pass totals (457), and set Status to `done`. No production code changes required.

### File List

- `_bmad-output/implementation-artifacts/3-5-suspend-and-resume-on-await-conditions.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/lifecycle-transition-matrix.md`
- `docs/work-tree-shape-guard.md`
- `src/Hexalith.Works.Contracts/Commands/ResumeWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/SuspendWorkItem.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemResumed.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemSuspended.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/AwaitCondition.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/AwaitConditionKind.cs`
- `src/Hexalith.Works.Reactor/AwaitingParent.cs`
- `src/Hexalith.Works.Reactor/ChildCompletionResumeTranslator.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `tests/Hexalith.Works.IntegrationTests/AwaitConditionSerializationContractFlowTests.cs`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/WorkItemResumed.v1.json`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/WorkItemSuspended.v1.json`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemSpawnChildContractFlowTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`
- `tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs`
- `tests/Hexalith.Works.UnitTests/ChildCompletionResumeTranslatorTests.cs`
- `tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj`
- `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemSuspendResumeTests.cs`

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot (automated story-automator review) · **Date:** 2026-06-17 · **Outcome:** Approve

**Verification reproduced:** `dotnet build Hexalith.Works.slnx -c Release` → 0 warnings / 0 errors;
direct xUnit v3 binaries → UnitTests 368/368, IntegrationTests 60/60, ArchitectureTests 28/28,
PropertyTests 1/1 (457 total).

**Acceptance criteria:** AC #1–#7 all validated against the implementation. Suspend accepts only from
`InProgress` with ≥1 condition and records the full set (#1); progress from `Suspended` is rejected while
Remaining keeps contributing to roll-up (#2, regression `Suspended_child_keeps_contributing_remaining_and_resume_only_flips_status`);
matching resume consumes one condition, emits `WorkItemResumed` with the consumed key, and clears the
whole set (#3); non-matching/keyless resume while `Suspended` is a `WorkItemTransitionRejected` with no
sequence burn (#4); duplicate consumed-key after resume is `DomainResult.NoOp`, a different post-resume
key is a rejection (#5); `ChildCompletionResumeTranslator` is a pure, mechanical, kind-aware fan-out that
makes no acceptance decision (#6); `DateReached`/`ExternalSignal` cases exist and `WorkItemAggregate.Handle`
reads no clock/adapter — enforced by `P0_WorkItemKernelRemainsPure` (#7).

**Quality gates:** kind-aware exact matching (D3) holds across the serialization boundary; first-match
clears the full set (D1); replayed `LastConsumedAwaitCondition` survives rehydration (D2); additive
schema evolution with legacy single/null payload tolerance preserved (D6); Reactor references inward to
Contracts only (`DependencyDirectionTests`).

**Findings (all documentation/transparency — no production-code defects):**
1. *[Medium] File List incompleteness* — `AwaitConditionSerializationContractFlowTests.cs` was present in
   git and described in `test-summary.md` but omitted from the File List. **Fixed:** added to the File List.
2. *[Low] Stale Debug Log* — the Debug Log recorded only the dev-story-pass counts (352/54), not the final
   post-QA-pass totals. **Fixed:** added a reconciling line (368/60, 457 total).
3. *[Low] Latent equality wart (not changed)* — `WorkItemSuspended` keeps both `AwaitConditions` and the
   legacy single `AwaitCondition` property, so a migrated legacy single-condition payload and a new-path
   event for the same suspension are not value-equal (`AwaitCondition` null vs set). Harmless for replay
   (`Apply` reads only `AwaitConditions`) and the legacy property is required by the backward-compat
   deserialization contract/tests, so it is intentionally left in place.
