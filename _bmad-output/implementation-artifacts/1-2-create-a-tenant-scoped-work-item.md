---
baseline_commit: 37b65f5f8bb974cd8feea20ce889f148176a86c0
---

# Story 1.2: Create a Tenant-Scoped Work Item

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want to create the first tenant-scoped Work Item through the domain contract,
so that Works proves it can record a durable, replayable obligation without copying sibling-module data.

## Acceptance Criteria

1. **Given** a caller supplies a `TenantId`, an edge-assigned `WorkItemId`, and a non-empty Obligation description
   **When** `CreateWorkItem` is handled against no prior state
   **Then** the domain returns a `WorkItemCreated` payload
   **And** the created state replays to Status `Created`
   **And** the aggregate identity is consistent with `{tenant}:work:{workItemId}`.

2. **Given** a caller supplies optional initial Effort, Unit, Schedule, parent reference, Executor Binding, or Conversation correlation ID
   **When** the Work Item is created
   **Then** `WorkItemCreated` carries only the supplied coordination facts and reference IDs
   **And** no Party, Tenant, Conversation, EventStore envelope, or Commons implementation data is copied into the aggregate state.

3. **Given** a caller supplies no Estimated effort
   **When** the Work Item is created
   **Then** creation succeeds
   **And** Remaining is represented as undefined-until-estimated
   **And** the item is not considered completed by the Remaining=0 rule.

4. **Given** a caller supplies a missing or whitespace Obligation description
   **When** `CreateWorkItem` is handled
   **Then** creation is rejected as a domain rejection event
   **And** the rejection does not mix with a success event in the same domain result.

5. **Given** `CreateWorkItem` is handled by the kernel
   **When** purity checks or architecture tests run
   **Then** the handler does not generate IDs, read a clock, perform I/O, call Dapr, or populate EventStore envelope metadata
   **And** emitted events can be replayed deterministically into the same state.

## Tasks / Subtasks

- [x] **Task 1 - Add the minimal Story 1.2 contract surface (AC: #1, #2, #3, #4, #5)**
  - [x] Add `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs`.
  - [x] Add `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`.
  - [x] Add a focused rejection event, recommended name `WorkItemCannotBeCreatedWithoutObligation`, implementing `IRejectionEvent`.
  - [x] Add the minimum value/state contracts needed by this story: `WorkItemId`, `TenantId`, `Obligation`, `WorkItemStatus`, `WorkItemState`, and simple coordination value objects for optional effort, schedule, parent work reference, executor binding, and conversation correlation.
  - [x] Keep contract types serialization-friendly and additive: public records/value objects, no `V2` types, no EventStore envelope fields, no sibling module implementation DTOs.

- [x] **Task 2 - Implement pure create handling and replay (AC: #1, #3, #4, #5)**
  - [x] Add `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` with pure static `Handle(CreateWorkItem, WorkItemState?) -> DomainResult`.
  - [x] Add `Apply(WorkItemCreated)` and no-op `Apply(WorkItemCannotBeCreatedWithoutObligation)` on `WorkItemState`.
  - [x] For create against `null` state, emit exactly one `WorkItemCreated` success payload.
  - [x] Reject missing/whitespace obligation with exactly one `IRejectionEvent`; do not throw for this business failure.
  - [x] Do not call `DateTime.Now`, `DateTimeOffset.Now`, `UtcNow`, `Guid.NewGuid`, `UniqueIdHelper`, Dapr, file/network I/O, or EventStore envelope APIs inside `Handle`/`Apply`.

- [x] **Task 3 - Resolve the Story 1.1 EventStore.Client purity trap explicitly (AC: #5)**
  - [x] Do **not** add `Hexalith.EventStore.Client` to `Hexalith.Works.Server` for this story unless the dependency-direction decision is documented and architecture tests are updated with a narrow, named exception.
  - [x] Recommended implementation: keep Story 1.2 as a pure domain slice tested directly through `WorkItemAggregate.Handle` and `WorkItemState.Apply`; defer EventStore processor wiring to the later command-pipeline/Aspire story.
  - [x] If the dev chooses to subclass `EventStoreAggregate<TState>` now, record the reason in this story's Dev Agent Record and update fitness tests to detect the transitive Dapr dependency rather than letting it pass invisibly.

- [x] **Task 4 - Update architecture fitness tests from scaffold-only to ongoing governance (AC: #5)**
  - [x] Replace or rename `P0_StoryElevenRemainsScaffoldOnly`; it must no longer reject legitimate `WorkItem` code after Story 1.2.
  - [x] Preserve the important boundary checks: no UI/MCP/security/routing/LLM/cost/channel projects, no inline package versions, `.slnx` only, root submodules only, and kernel project files free of direct Dapr/UI/LLM packages.
  - [x] Add or extend a purity scan over `src/Hexalith.Works.Server` for banned symbols: `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Stopwatch`, `Guid.NewGuid`, `UniqueIdHelper.Generate`, `File.`, `Directory.`, `HttpClient`, `Dapr`.
  - [x] Add a test or static assertion that `Hexalith.Works.Server.csproj` remains `Contracts`-only unless Task 3 intentionally documents otherwise.

- [x] **Task 5 - Add unit tests for create behavior and state replay (AC: #1, #2, #3, #4, #5)**
  - [x] Replace the placeholder `ScaffoldTests` with Work Item tests in `tests/Hexalith.Works.UnitTests`.
  - [x] Test valid create: success result, one `WorkItemCreated`, status replays to `Created`, and identity string equals `{tenant}:work:{workItemId}` using `AggregateIdentity`.
  - [x] Test optional fields: initial effort/unit, schedule, same-tenant parent reference, executor binding, and conversation correlation are preserved as reference/coordination facts only.
  - [x] Test unestimated create: no remaining value is materialized and status remains `Created`.
  - [x] Test blank obligation: rejection result only; no success payload and no state mutation.
  - [x] Use xUnit v3 and Shouldly. Do not introduce Moq, FluentAssertions, raw `Assert.*`, or solution-level `dotnet test` as the required lane.

- [x] **Task 6 - Build and test the focused slice (AC: #1-#5)**
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -v minimal`.
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -v minimal`.
  - [x] Run affected test projects individually, at minimum `tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj` and `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj`.
  - [x] Do not use any recursive submodule command.

## Dev Notes

### Scope Boundary

Story 1.2 is the first real domain behavior slice. It creates one Work Item and replays the resulting state. It does **not** implement lifecycle transitions beyond `Created`, progress reporting, roll-up, suspend/resume, queueing, claim, assignment behavior, projection rebuild, reactor runtime, timers, UI, MCP, routing, LLM, cost, email, or security hardening. Those are later stories. [Source: _bmad-output/planning-artifacts/epics.md#Story 1.2: Create a Tenant-Scoped Work Item; _bmad-output/planning-artifacts/architecture.md#Requirements to Structure Mapping]

### Required Domain Model Shape

- Domain name for EventStore identity is `work`; canonical identity is `{tenant}:work:{workItemId}`. Use `Hexalith.EventStore.Contracts.Identity.AggregateIdentity` for tests rather than inventing string parsing. [Source: _bmad-output/planning-artifacts/epics.md#FR-1; Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Identity/AggregateIdentity.cs]
- `CreateWorkItem` must take the ID from the edge. The aggregate must never generate IDs. Use supplied `TenantId` and `WorkItemId` values only. [Source: _bmad-output/planning-artifacts/architecture.md#Data Architecture]
- `WorkItemCreated` is a domain payload, not an EventStore envelope. It must not carry `MessageId`, `CorrelationId`, `CausationId`, `UserId`, EventStore metadata, persisted CloudEvent fields, or generated ID implementation details. [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns; Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Commands/CommandEnvelope.cs]
- Architecture requires every Works domain event payload to carry `(AggregateId, Sequence)`. For this first create event, use `AggregateId = workItemId` and `Sequence = 1` when prior state is `null`; later stories can increment from state. [Source: _bmad-output/planning-artifacts/epics.md#Additional Requirements; Hexalith.EventStore/src/Hexalith.EventStore.Client/Aggregates/AggregateReplayer.cs]
- Rejection payloads implement `IRejectionEvent`; success payloads implement `IEventPayload`. `DomainResult` enforces that success and rejection payloads cannot be mixed. [Source: Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Events/IEventPayload.cs; Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Events/IRejectionEvent.cs; Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Results/DomainResult.cs]

### Suggested File Layout

- `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotBeCreatedWithoutObligation.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemId.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/TenantId.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/Unit.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemSchedule.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/Priority.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ParentWorkItemReference.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/AuthorityLevel.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ConversationCorrelationId.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelPurityTests.cs`

This list is intentionally contract-heavy because Story 1.2 must carry optional create facts without copying sibling data. Keep each type minimal and avoid implementing future behavior in these types.

### Existing Files To Update Carefully

- `src/Hexalith.Works.Server/Hexalith.Works.Server.csproj` currently references only `Hexalith.Works.Contracts`. Preserve that by default to avoid the Story 1.1 purity trap.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` currently has `P0_StoryElevenRemainsScaffoldOnly`, which scans for `WorkItem` and related terms. That test must change for Story 1.2 or it will reject legitimate implementation.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` still asserts "Story 1.1" topology. Update names/messages to become enduring architecture assertions where possible.
- `tests/Hexalith.Works.UnitTests/ScaffoldTests.cs` is only a placeholder and should be replaced or complemented with real create/replay tests.
- `docs/eventstore-api-surface-constraints.md` records the Story 1.1 finding: EventStore optimistic concurrency is state-store ETag based, and online rebuild is checkpoint based. Do not assume an expected-version append argument.

### Contract Details

- `WorkItemStatus` can contain only `Unknown = 0` and `Created = 1` for this story, or the full 9-state catalog if the dev wants to predeclare values. Do not implement transitions beyond create.
- `Obligation` must reject null, empty, or whitespace descriptions at command handling time as a domain rejection event. Avoid exceptions for the business rejection path.
- Do not make invalid-obligation commands impossible to construct in tests. `CreateWorkItem` can carry a raw nullable/string description or a factory path that still lets `Handle` observe invalid input and return `WorkItemCannotBeCreatedWithoutObligation`.
- Unestimated work is valid. Model it as `WorkItemEffort?` or an equivalent optional value so "Remaining" is not coerced to `0`. Completion by Remaining=0 is not active for unestimated items.
- If an initial effort is supplied, keep `Estimated >= 0`; `Done` should start at `0`; `Remaining` can be derived. Do not add progress/re-estimate behavior yet.
- `Schedule` may carry `Priority?` and `DueDate?`. Do not read the current time and do not expire overdue items in this story.
- `ParentWorkItemReference` should be same-tenant by construction or validation. Do not implement acyclic/depth/tree traversal rules yet.
- `ExecutorBinding` is data only. Do not branch on channel, executor kind, or authority level.
- `ConversationCorrelationId` is an optional reference only. Do not add a comment store or call Conversations.

### EventStore.Client Decision

Story 1.1 found that `EventStoreAggregate<TState>` lives in `Hexalith.EventStore.Client`, and that project carries a direct `Dapr.Client` package reference. Referencing it from `Hexalith.Works.Server` would make the kernel's "no Dapr type" fitness signal incomplete because the existing test only scans direct project text. [Source: _bmad-output/implementation-artifacts/1-1-set-up-initial-project-from-starter-template.md#Kernel-purity vs EventStore.Client]

For Story 1.2, the safest approach is a pure aggregate class with static `Handle`/state `Apply` methods tested directly. The EventStore command processor integration can be introduced in the command-pipeline/Aspire story after the team decides whether `EventStore.Client` is an accepted kernel exception or whether an adapter seam should own it.

### Testing Standards

- Use xUnit v3 and Shouldly. Do not use raw `Assert.*`, Moq, or FluentAssertions. [Source: Hexalith.EventStore/_bmad-output/project-context.md#Testing Rules; Hexalith.Parties/_bmad-output/project-context.md#Testing Rules]
- Run test projects individually. Use `.slnx` for restore/build only. [Source: Hexalith.EventStore/_bmad-output/project-context.md#Testing Rules]
- Keep Tier-1 tests pure: no Dapr, Aspire, containers, network, file I/O, or sleeps. [Source: _bmad-output/planning-artifacts/architecture.md#Implementation Patterns & Consistency Rules]
- Because the repo has `TreatWarningsAsErrors=true`, analyzer warnings are build failures. Use `ArgumentNullException.ThrowIfNull` and `ArgumentException.ThrowIfNullOrWhiteSpace` patterns where appropriate. [Source: Hexalith.EventStore/_bmad-output/project-context.md#C# Language-Specific Rules]

### Latest Technical Notes

- Local version pins remain authoritative for this story: .NET SDK `10.0.301`, Dapr packages `1.18.2`, Aspire `13.4.3`, xUnit v3 `3.2.2`, central package management. Do not upgrade as part of Story 1.2. [Source: _bmad-output/planning-artifacts/architecture.md#Starter Template Evaluation; Directory.Packages.props]
- Microsoft documents `global.json` as the SDK selection mechanism and `rollForward` as the allowed fallback policy; this reinforces keeping the existing pinned `global.json` instead of changing SDK selection in this story. [Source: https://learn.microsoft.com/en-us/dotnet/core/tools/global-json]
- Dapr v1.18 docs identify reminders as Scheduler-persisted and distinct from non-persisted timers. That matters later for suspend/resume; Story 1.2 must not add timer/reminder behavior. [Source: https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/]
- xUnit.net v3 docs describe Microsoft Testing Platform support for `dotnet test`; this matches the existing .NET 10/MTP setup. Keep the current test package style. [Source: https://xunit.net/docs/getting-started/v3/getting-started]

### Git Intelligence

Recent implementation commit `37b65f5 feat(story-1.1): Set up initial project from starter template` completed scaffold setup and review patches. Relevant carry-forward items:

- Story 1.1 build and architecture tests were green after enabling real project references.
- The unit/property/integration tests are still placeholders; Story 1.2 should replace the unit placeholder with real domain tests.
- The deferred Story 1.1 review item about transitive `EventStore.Client`/Dapr purity is assigned to Story 1.2 and must be addressed in the implementation notes, not ignored.

### Project Structure Notes

- Works repository responsibility is domain code for work items. Do not add technical layers here unless required for the Works domain. Persistence belongs in `Hexalith.EventStore`; ID generation belongs in `Hexalith.Commons`; identity/dialogue/isolation remain references to their owning modules.
- Only root submodules may be initialized or updated. Never run `git submodule update --init --recursive` and never initialize nested submodules inside root submodules.
- Do not modify sibling submodule files for this story.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.2: Create a Tenant-Scoped Work Item] - story statement and acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#Functional Requirements] - FR-1, FR-2, FR-21, FR-22 tenant-scoped create/reference rules.
- [Source: _bmad-output/planning-artifacts/epics.md#Additional Requirements] - AR-3, AR-4, AR-20, AR-21, AR-22.
- [Source: _bmad-output/planning-artifacts/architecture.md#Data Architecture] - aggregate ID at edge, payload sequence, event-sourcing constraints.
- [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns] - raw-act payloads, no mixed success/rejection, additive serialization.
- [Source: _bmad-output/planning-artifacts/architecture.md#Project Structure & Boundaries] - target source/test layout and kernel/adapter boundary.
- [Source: _bmad-output/implementation-artifacts/1-1-set-up-initial-project-from-starter-template.md#Kernel-purity vs EventStore.Client] - deferred purity decision for this story.
- [Source: docs/eventstore-api-surface-constraints.md] - live EventStore API constraints recorded by Story 1.1.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Identity/AggregateIdentity.cs] - canonical identity validation and formatting.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Results/DomainResult.cs] - success/rejection/no-op invariants.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Client/Aggregates/AggregateReplayer.cs] - replay sequence starts at 1 and must be contiguous.
- [Source: Hexalith.Tenants/src/Hexalith.Tenants.Server/Aggregates/TenantAggregate.cs] - sibling static `Handle` pattern using `DomainResult`.
- [Source: Hexalith.Parties/src/Hexalith.Parties.Contracts/State/PartyState.cs] - sibling state `Apply` pattern and rejection no-op applies.

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-16T10:17:55+02:00: Exact `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -v minimal` exited non-zero without diagnostics in the sandbox; serialized restore with `-p:NuGetAudit=false -m:1` passed because the environment cannot reach NuGet vulnerability data.
- 2026-06-16T10:17:55+02:00: Exact non-serialized solution build exited non-zero without diagnostics; serialized `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` passed with 0 warnings and 0 errors.
- 2026-06-16T10:17:55+02:00: `dotnet test` was blocked by sandbox named-pipe permissions (`SocketException: Permission denied`); ran built xUnit v3 assemblies directly.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Story 1.2 scope intentionally keeps EventStore processor wiring out unless the EventStore.Client purity decision is documented and fitness-tested.
- Added pure Story 1.2 create/replay contract surface with edge-supplied `TenantId` and `WorkItemId`, `WorkItemCreated`, `WorkItemCannotBeCreatedWithoutObligation`, replayable `WorkItemState`, and minimal coordination value objects.
- Implemented `WorkItemAggregate.Handle(CreateWorkItem, WorkItemState?)` as a pure static handler that emits one success event for valid create against no prior state and one rejection event for missing/whitespace obligation.
- Kept `Hexalith.Works.Server` free of `Hexalith.EventStore.Client`; architecture tests now assert server remains `Contracts`-only and scan the server kernel for banned clock, ID generation, Dapr, I/O, and network symbols.
- Replaced placeholder unit tests with create/replay tests covering success, optional coordination facts, unestimated remaining behavior, and rejection-only blank obligation behavior.
- Senior review auto-fixes aligned tenant/work identity validation with `AggregateIdentity`, normalized create-time initial effort so `Done` starts at `0`, and corrected the documented `Priority`/`AuthorityLevel` catalogs.

### File List

- `_bmad-output/implementation-artifacts/1-2-create-a-tenant-scoped-work-item.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotBeCreatedWithoutObligation.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/AuthorityLevel.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ConversationCorrelationId.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ParentWorkItemReference.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/Priority.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/TenantId.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/Unit.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemId.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemSchedule.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
- `tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj`
- `tests/Hexalith.Works.UnitTests/ScaffoldTests.cs` (deleted)
- `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemEffortTests.cs`
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj`
- `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs`

### Senior Developer Review (AI)

Reviewer: GPT-5 Codex on 2026-06-16

Outcome: Approved after auto-fix. No critical issues remain.

Findings fixed:

- HIGH: `TenantId` and `WorkItemId` accepted values that could not form the canonical `{tenant}:work:{workItemId}` identity, making replayed state fail or diverge when `AggregateIdentity` was requested. Fixed by validating through `AggregateIdentity` and normalizing tenant IDs.
- MEDIUM: `CreateWorkItem` accepted an initial effort with non-zero `Done`, even though Story 1.2 requires create-time effort to start at `Done = 0`. Fixed by normalizing create-time initial effort to the supplied estimate/unit with zero done progress.
- MEDIUM: `AuthorityLevel` did not match the documented carried-not-enforced catalog. Fixed to `Read`, `Contribute`, `Coordinate`, `Administer`.
- MEDIUM: `Priority` did not expose the documented `Critical` level and used an ordering that did not match queue precedence. Fixed to `Critical`, `High`, `Normal`, `Low`.
- MEDIUM: Git-discovered implementation files were missing from the story File List. Fixed by adding the integration flow test, effort test, value-object test, and test-summary artifact.
- LOW: `ParentWorkItemReference` allowed a null parent ID through its positional record constructor. Fixed with explicit constructor validation.

Validation:

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` passed with 0 warnings and 0 errors.
- `./tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` passed: 25/25.
- `./tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` passed: 17/17.
- `./tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` passed: 7/7.
- `./tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` passed: 1/1.
- `dotnet test` remains blocked in this sandbox by Microsoft.Testing.Platform named-pipe permissions (`SocketException: Permission denied`), so direct xUnit v3 executables were used.

### Change Log

- 2026-06-16: Implemented Story 1.2 tenant-scoped Work Item create/replay domain slice and focused tests.
- 2026-06-16: Updated architecture fitness tests from scaffold-only checks to ongoing Work Item governance and kernel purity assertions.
- 2026-06-16: Senior review auto-fixed identity validation, create-time effort normalization, documented enum catalogs, file-list completeness, and value-object null validation; story moved to done.
