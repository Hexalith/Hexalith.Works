---
baseline_commit: 68de3f5
---

# Story 4.1: Bind Work to a Uniform Party Executor

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want every executor to be represented by one `ExecutorBinding`,
so that system agents, internal users, and external parties use the same domain model.

## Acceptance Criteria

1. **Given** the executor binding contract is inspected
   **When** it is used by Work Item commands, events, state, and read models
   **Then** it contains `PartyId`, `Channel`, and `AuthorityLevel`
   **And** it does not contain an executor-kind-specific subtype or branch discriminator.

2. **Given** a Work Item is assigned to a system, internal user, or external party
   **When** the binding is persisted
   **Then** the same value-object shape is used for all three cases
   **And** the only variation is field values such as Party ID, Channel, and AuthorityLevel.

3. **Given** `AuthorityLevel` is carried in v1
   **When** create, assign, or reassign events are replayed
   **Then** the AuthorityLevel is preserved in state and read models
   **And** no v1 behavior branches on AuthorityLevel.

4. **Given** future UI surfaces need a single Party chip treatment
   **When** read-model contracts are inspected
   **Then** executor kind, Channel, and AuthorityLevel are exposed as data
   **And** no separate model is required for bot, human, or external executor presentation.

5. **Given** architecture-fitness tests run
   **When** domain code is scanned
   **Then** there is no branch on executor kind
   **And** no LLM, routing, email, MCP, UI, or security adapter is introduced for executor binding.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile the existing executor-binding surface before changing code (AC: #1-#5)**
  - [x] Read `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs`,
    `PartyId.cs`, `Channel.cs`, `AuthorityLevel.cs`,
    `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs`,
    `AssignWorkItem.cs`, `ClaimWorkItem.cs`, `SpawnChild.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`,
    `WorkItemAssigned.cs`, `WorkItemClaimed.cs`, `ChildSpawned.cs`,
    `src/Hexalith.Works.Contracts/State/WorkItemState.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`,
    `src/Hexalith.Works.Contracts/Models/*.cs`,
    `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs`,
    `WorkItemCreateTests.cs`, `WorkItemLifecycleTests.cs`, and
    `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`.
  - [x] Confirm the current shape already uses `ExecutorBinding(PartyId, Channel, AuthorityLevel)` in create,
    assign, claim, child-spawn, event payloads, and replayed state.
  - [x] Confirm there is no legacy `ExecutorId`, executor subtype, `BindingKind`, `BotExecutor`,
    `HumanExecutor`, `ExternalExecutor`, or equivalent branch discriminator in production contracts.
  - [x] Preserve `PartyId` as a stable reference to `Hexalith.Parties`; do not copy party names,
    person/organization details, contact channels, provider addresses, or display metadata into Works.

- [x] **Task 2 - Close value-object validation gaps without changing the public shape (AC: #1, #3)**
  - [x] Keep `ExecutorBinding` properties exactly `PartyId`, `Channel`, and `AuthorityLevel`.
  - [x] Keep `Channel` as the current closed v1 catalog (`Unknown`, `Mcp`, `Cli`, `Chatbot`, `Email`) with
    `Unknown` rejected by `ExecutorBinding`.
  - [x] Treat `AuthorityLevel.Unknown` as a deserialization sentinel only and reject it in `ExecutorBinding`;
    also reject undefined `AuthorityLevel` enum casts. This gap exists now: `ExecutorBinding` rejects unknown
    `Channel` but not unknown `AuthorityLevel`.
  - [x] Add focused tests in `WorkItemContractValueObjectTests` for `AuthorityLevel.Unknown` and undefined
    authority rejection.
  - [x] Do not add an authorization service, role gate, policy engine, step-up auth, signed-link handling, or
    authority enforcement. `AuthorityLevel` is carried-not-enforced in v1.

- [x] **Task 3 - Prove one binding shape for system, internal, and external executors (AC: #1-#3)**
  - [x] Add table-driven unit tests that exercise at least three representative bindings through the same
    code path: system/agent (`Channel.Mcp` or `Channel.Chatbot`), internal user (`Channel.Cli` or `Channel.Mcp`),
    and external party (`Channel.Email`). Use only different field values.
  - [x] For `CreateWorkItem`, assert `WorkItemCreated.ExecutorBinding` preserves `PartyId`, `Channel`, and
    `AuthorityLevel`, and replayed `WorkItemState.ExecutorBinding` equals the supplied binding.
  - [x] For `AssignWorkItem`, assert `WorkItemAssigned.Binding` and replayed state preserve the full binding
    for every representative executor case.
  - [x] For reassignment via the existing `AssignWorkItem` path, replay two accepted `WorkItemAssigned` events
    with different bindings and assert the latest binding is authoritative without a special handoff command.
  - [x] For create/assign/claim events already covered by schema tests, preserve or extend JSON round-trip
    assertions so `authorityLevel` survives concrete `System.Text.Json` serialization.

- [x] **Task 4 - Expose executor binding in read-model contracts without UI or routing scope (AC: #3, #4)**
  - [x] Inspect current read models under `src/Hexalith.Works.Contracts/Models`. Today they are roll-up-focused
    and do not expose executor binding.
  - [x] If a reusable Work Item read-model contract already exists by implementation time, add nullable
    `ExecutorBinding` to it and test that `AuthorityLevel` survives projection/replay into the model.
  - [x] If no suitable read model exists yet, add the smallest contract-level model needed by 4.1, for example
    a `WorkItemExecutorBindingView` or equivalent under `Contracts/Models`, carrying only `PartyId`,
    `Channel`, and `AuthorityLevel` through `ExecutorBinding`.
  - [x] Do not add display name, party profile, contact details, kind-specific UI fields, Fluent UI types,
    FrontComposer annotations, SignalR wiring, query handlers, or the "what's next" queue projection here.
    Story 4.4 owns the tenant queue projection.
  - [x] Treat "executor kind" for future Party chip rendering as resolver/display data outside the Works kernel.
    The Works contract must not introduce a durable kind discriminator; `PartyId`, `Channel`, and
    `AuthorityLevel` are the data Works owns.

- [x] **Task 5 - Add architecture fitness coverage for zero executor-kind branching (AC: #1, #3, #5)**
  - [x] Extend `ScaffoldGovernanceTests` or add a focused fitness test that scans production `src/**/*.cs`
    (excluding `bin`, `obj`, generated output, and value-object enum definitions) for executor-kind branching
    patterns such as `switch`/`if` over `Channel`, `AuthorityLevel`, `PartyId`, or `ExecutorBinding`.
  - [x] The fitness test must allow validation inside `ExecutorBinding` itself, but fail domain behavior if
    `WorkItemAggregate`, projections, reactor, or future read-model logic branches on channel/authority/kind.
  - [x] Add explicit forbidden project/package checks if needed so Story 4.1 cannot introduce LLM, routing,
    email/MCP adapter, UI, security, or cost-governance packages into `Contracts`, `Server`, or `Projections`.
  - [x] Preserve existing kernel purity and dependency-direction tests; do not relax their expected reference
    lists to make this story pass.

- [x] **Task 6 - Update documentation and story bookkeeping (AC: #1-#5)**
  - [x] Update `docs/boundary-decision-record.md` only if code or wording drifts; it already states that
    Works references Parties by `PartyId` and carries but does not enforce `AuthorityLevel`.
  - [x] If a new read-model contract is added, update the relevant contract or boundary docs with a short note:
    Party identity is a reference, Channel is an interaction medium, AuthorityLevel is carried-not-enforced,
    and there is no executor-kind branch discriminator.
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` with Story 4.1 verification
    commands, counts, files changed, and any not-applicable runtime/UI surfaces.

- [x] **Task 7 - Verify the slice (AC: #1-#5)**
  - [x] Use the Story 3.6 final baseline of **481** green tests: UnitTests 389, IntegrationTests 63,
    ArchitectureTests 28, PropertyTests 1.
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
    and require 0 warnings / 0 errors.
  - [x] Run the direct xUnit v3 binaries after the Release build, the reliable path in this sandbox:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`, and
    `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.
  - [x] Do not run recursive submodule commands.

## Dev Notes

### Scope Boundary

Story 4.1 is a contract, replay, read-model, and guardrail story for FR-17 and FR-19. The core outcome is
that every executor is represented by one `ExecutorBinding` shape and no domain behavior branches on
whether the executor is a system agent, internal user, or external party. [Source:
_bmad-output/planning-artifacts/epics.md#Story 4.1: Bind Work to a Uniform Party Executor; _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-17]

**In scope:** value-object validation hardening; tests proving create/assign/reassign carry the same
binding shape for system/internal/external executor examples; read-model contract exposure of
`ExecutorBinding` data; architecture fitness tests for zero executor-kind branching; documentation and
test summary updates.

**Out of scope:** Story 4.2's broader assignment/handoff behavior, Story 4.3's expected-version
single-claim-wins, Story 4.4's "what's next" queue projection/query, Story 4.5's Aspire command/event
pipeline proof, Story 4.6's reminder/reactor runtime recovery, production UI, MCP/chatbot/email adapters,
executor routing, eligibility filtering, escalation ladders, `AuthorityLevel` enforcement, signed links,
security hardening, LLM/NL parsing, and cost governance. [Source:
_bmad-output/planning-artifacts/epics.md#Epic 4; _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#6.2 Out of Scope for MVP]

### Current State (files this story modifies or verifies - read before editing)

- `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs` already exposes only `PartyId`,
  `Channel`, and `AuthorityLevel`. It validates `PartyId` and `Channel`, but currently does **not** reject
  `AuthorityLevel.Unknown` or undefined authority enum values.
- `src/Hexalith.Works.Contracts/ValueObjects/PartyId.cs` is a Works-owned reference value object that
  validates the party aggregate id component through `AggregateIdentity`; it does not depend on Parties
  implementation packages.
- `src/Hexalith.Works.Contracts/ValueObjects/Channel.cs` is a closed v1 enum with `Unknown = 0`,
  `Mcp`, `Cli`, `Chatbot`, and `Email`; `Unknown` is documented as a sentinel rejected by
  `ExecutorBinding`.
- `src/Hexalith.Works.Contracts/ValueObjects/AuthorityLevel.cs` is the documented ordered set
  `Unknown`, `Read`, `Contribute`, `Coordinate`, `Administer`; `Unknown` should be a sentinel only.
- `CreateWorkItem`, `AssignWorkItem`, `ClaimWorkItem`, and `SpawnChild` already carry optional or
  required `ExecutorBinding` values. Do not add kind-specific commands.
- `WorkItemCreated`, `WorkItemAssigned`, `WorkItemClaimed`, and `ChildSpawned` already persist the same
  binding shape. Do not add a `WorkItemAssignedToBot`, `WorkItemAssignedToHuman`, or similar event.
- `WorkItemState.Apply(WorkItemCreated)`, `Apply(WorkItemAssigned)`, and `Apply(WorkItemClaimed)` already
  preserve `ExecutorBinding` in replayed state.
- `WorkItemAggregate.Handle(AssignWorkItem)` and `Handle(ClaimWorkItem)` pass the supplied binding through
  to events without inspecting `Channel` or `AuthorityLevel`. Preserve that behavior.
- `src/Hexalith.Works.Contracts/Models` currently contains roll-up read models only. Story 4.1 must not
  force a full queue/query projection, but it does need a contract-level way for future read models to
  carry executor binding data.
- `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs` already tests channel catalog and
  `ExecutorBinding` channel rejection; extend it for authority sentinel rejection.
- `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs` already covers assign/claim state transitions;
  extend or add focused tests for binding preservation across different executor examples.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` already
  asserts `authorityLevel` round-trips for existing frozen create/assign payloads; preserve compatibility.

### Design Decisions and Guardrails

- **One binding shape.** `ExecutorBinding` is `PartyId + Channel + AuthorityLevel`; system agents, internal
  users, and external parties differ only by field values. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Executor Binding value object]
- **No executor-kind branch discriminator.** Do not add `ExecutorKind`, `PartyKind`, inheritance, tagged
  subtypes, or durable branch fields to Works contracts. Future display of "bot / person / external" is
  resolved outside the Works kernel from Party identity or adapter-side metadata. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-17; _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Component Patterns]
- **Authority is carried, not enforced.** `AuthorityLevel` must survive create/assign/reassign replay and
  read-model exposure, but no v1 handler can accept/reject behavior based on it. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-19; docs/boundary-decision-record.md#Deferred seams]
- **Channel is an interaction medium, not executor identity.** `Channel.Email` can represent an external
  party interaction path, and `Channel.Mcp` can represent a system agent path, but domain behavior must not
  branch on those values. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#Glossary; _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Channel & Surface Matrix]
- **Read models carry data only.** Future Party chips need `PartyId`, `Channel`, and `AuthorityLevel` data;
  Works must not introduce UI types, presentation colors, adapter-specific contact data, or separate bot /
  human / external models. [Source: _bmad-output/planning-artifacts/epics.md#UX-DR4; _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md#Components]

### Technical Requirements

- Keep the kernel projects (`Contracts`, `Server`, `Projections`) free of Dapr runtime, EventStore server/client
  implementations, HTTP, files, clocks, timers, generated IDs, LLM/routing/email/MCP/UI/security/cost packages,
  and sibling implementation DTOs. [Source: _bmad-output/planning-artifacts/architecture.md#Structure Patterns]
- Keep events and commands registered with `Hexalith.PolymorphicSerializations`; evolve only additively and do
  not add `V2` event types. [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns]
- Use file-scoped namespaces, sealed records for contracts/value objects where established, xUnit v3, Shouldly,
  and existing test projects/helpers. [Source: Hexalith.Projects/_bmad-output/project-context.md#Critical Implementation Rules]
- Do not add or upgrade packages. Local pins remain authoritative: .NET SDK `10.0.301`, Dapr `1.18.2`,
  Aspire `13.4.3`, xUnit v3 `3.2.2`, Shouldly `4.3.0`. [Source:
  _bmad-output/planning-artifacts/architecture.md#Starter Template Evaluation; Directory.Packages.props]
- Hexalith dependencies must remain root-submodule `ProjectReference`s. Never add `Hexalith.*`
  `PackageReference`s or `Hexalith.*` versions to `Directory.Packages.props`. [Source: AGENTS.md#Hexalith library references - ALWAYS use ProjectReference, NEVER PackageReference]

### Previous Work Intelligence

- Story 1.3 created `PartyId`, `Channel`, and the current `ExecutorBinding(PartyId, Channel, AuthorityLevel)`
  shape; it also deliberately avoided copying Party profiles/contact data into Works. Build on that work, do
  not replace it. [Source: _bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md#Reference Ownership Rules]
- Story 2.1 added `AssignWorkItem`, `ClaimWorkItem`, `WorkItemAssigned`, `WorkItemClaimed`, and replay behavior.
  It explicitly treated `AssignWorkItem` as the bind/rebind operation and left single-claim-wins concurrency to
  Story 4.3. [Source: _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md#Tasks / Subtasks]
- Story 2.5 reiterated that `AuthorityLevel` is carried but not enforced; do not introduce authority gating in
  this story. [Source: _bmad-output/implementation-artifacts/2-5-complete-cancel-reject-and-expire-work.md#Dev Notes]
- Epic 3 retrospective states 4.1 builds on already-carried `ExecutorBinding` / `PartyId` and must preserve
  carried-not-enforced `AuthorityLevel`. [Source: _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md#Next Epic Preview]
- Epic 3 also established a review lesson: first implementation passes under-cover; make an explicit QA
  gap-filling pass and reconcile test counts against `tests/test-summary.md` before review. [Source:
  _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md#Action Items]
- `dotnet test` is unreliable in this sandbox because Microsoft.Testing.Platform named-pipe permissions block
  solution-level test runs. The sanctioned path is restore, Release build, then direct xUnit v3 binaries under
  `bin/Release/net10.0/`. [Source: _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md#What Was Challenging]

### Git Intelligence

Recent commits before Story 4.1:

- `68de3f5 feat: Update documentation and project structure for Epic 3 completion` - refreshed docs and structure after Epic 3.
- `216e9e7 feat(story-3.6): Cascade terminal work through active descendants` - added pure reactor cascade translator and tests.
- `f8856f2 feat(story-3.5): Suspend and resume on await conditions` - added await-condition resume semantics and pure child-completion translation.
- `61ec4c5 feat(story-3.4): Preserve heterogeneous unit subtotals` - hardened roll-up unit safety and diagnostics.
- `5c95d1e feat(story-3.3): Maintain recursive roll-up with per-child sequence` - added per-child-sequence roll-up projection.

Story 4.1 should stay closer to the existing Contracts/Server/test surfaces than to the newer Reactor runtime work.

### Project Structure Notes

- Value-object changes belong in `src/Hexalith.Works.Contracts/ValueObjects`.
- Durable command/event shape changes, if any, belong in `src/Hexalith.Works.Contracts/Commands` and
  `src/Hexalith.Works.Contracts/Events`; avoid adding new event types.
- Read-model contract additions belong in `src/Hexalith.Works.Contracts/Models`. Do not implement Story 4.4's
  projection/query here.
- Domain behavior, if touched, belongs in `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`; there
  should be no need to branch there on executor data.
- Fitness tests belong in `tests/Hexalith.Works.ArchitectureTests/FitnessTests`; unit tests should extend
  existing `WorkItemContractValueObjectTests`, `WorkItemCreateTests`, and `WorkItemLifecycleTests` where possible.
- Do not modify sibling submodule files and do not initialize nested submodules.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.1: Bind Work to a Uniform Party Executor] - story statement and acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-17] - uniform assign/reassign/handoff through one operation and no executor-kind branch.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-19] - AuthorityLevel carried on binding.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-17: Bind, reassign, and hand off via one uniform operation] - everything is a Party.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-19: Carry AuthorityLevel on the binding] - authority set and carried-not-enforced rule.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#Success Metrics] - SM-3 zero branching on executor kind and SM-5 handoff equals one operation.
- [Source: _bmad-output/planning-artifacts/architecture.md#Executor binding] - zero branching on executor kind and identical command path.
- [Source: _bmad-output/planning-artifacts/architecture.md#Project Structure & Boundaries] - kernel/adapter boundaries and file locations.
- [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md#Components] - Party chip uses identical treatment and monochrome glyph/badge.
- [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Component Patterns] - Party chip and handoff behavior.
- [Source: docs/boundary-decision-record.md] - Parties boundary and AuthorityLevel deferred enforcement.
- [Source: AGENTS.md] - root submodule and ProjectReference rules.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (1M context) — `claude-opus-4-8[1m]` (bmad-dev-story workflow).

### Debug Log References

- Restore: `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- Build: `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — **0 warnings / 0 errors**.
- xUnit v3 binaries, dev-story pass: UnitTests **414/414**, IntegrationTests **72/72**, ArchitectureTests **29/29**, PropertyTests **1/1** (516 total).
- xUnit v3 binaries, final after the `bmad-qa-generate-e2e-tests` gap-filling pass (the authoritative count; see `tests/test-summary.md`): UnitTests **419/419**, IntegrationTests **79/79**, ArchitectureTests **29/29**, PropertyTests **1/1** (`Ok, passed 100 tests.`) — **528 total green, 0 warnings / 0 errors**. Re-verified during the senior-developer review.

### Completion Notes List

- **Task 1 (reconcile):** Confirmed by inspection that `ExecutorBinding(PartyId, Channel, AuthorityLevel)` is already the uniform shape across `CreateWorkItem`/`AssignWorkItem`/`ClaimWorkItem`/`SpawnChild`, the matching events, `WorkItemState` replay, and `WorkItemAggregate` (which passes the binding through without inspecting `Channel`/`AuthorityLevel`). No legacy `ExecutorId`, `BindingKind`, `BotExecutor`/`HumanExecutor`/`ExternalExecutor`, or `ExecutorKind`/`PartyKind` discriminator exists. `PartyId` remains a reference value object; no Party profile/contact/display data is copied into Works.
- **Task 2 (validation gap):** `ExecutorBinding` now rejects `AuthorityLevel.Unknown` and undefined authority enum casts, mirroring the existing `Channel` guard. Public shape unchanged; `Channel` catalog unchanged; no authorization/policy/step-up/signed-link logic added (authority is carried-not-enforced).
- **Task 3 (one shape):** Added table-driven unit tests proving system-agent / internal-user / external-party bindings flow through the identical create/assign/claim handlers and that reassignment via a second `AssignWorkItem` makes the latest binding authoritative (no handoff command). Added integration tests round-tripping the three bindings through concrete `System.Text.Json`, asserting `authorityLevel` survives.
- **Task 4 (read model):** No reusable Work Item read model existed (the existing `Models` are roll-up-focused), so added the smallest contract `WorkItemExecutorBindingView` carrying `TenantId`, `WorkItemId`, and nullable `ExecutorBinding`. No display/kind/contact/UI/SignalR/query/queue scope (Story 4.4 owns the queue projection). Test proves `AuthorityLevel` survives projection/replay into the view.
- **Task 5 (fitness):** Added `P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority` scanning production `src` for branching over `Channel`/`AuthorityLevel`/`PartyId`, allowing only `ExecutorBinding`'s own validation. Existing kernel-purity (`P0_WorkItemKernelRemainsPure`), infrastructure-free (`P0_KernelProjectsStayInfrastructureFree`), project-set, and dependency-direction tests were preserved unchanged (forbidden LLM/routing/email/MCP/UI/security/cost project + package introduction stays enforced there).
- **Task 6 (docs):** Added a Story 4.1 note to `docs/boundary-decision-record.md` (one shape; Party identity = reference; Channel = interaction medium; AuthorityLevel = carried-not-enforced; no kind discriminator; new read model). Updated `tests/test-summary.md` with commands, counts, files, and N/A surfaces.
- **QA gap-filling pass (`bmad-qa-generate-e2e-tests`):** after the dev-story pass (516), a QA pass added **+12** tests (+5 unit, +7 integration) to close residual gaps, reaching **528** green. It added `UniformExecutorBindingLifecycleFlowTests` (end-to-end aggregate → serialize → replay → view, incl. mid-lifecycle reassignment through claim/complete), `SpawnChild`/`ChildSpawned` as the fourth binding-carrying pair (unit + serialization), and an `ExecutorBinding` structural shape-lock test. No production code was changed by the QA pass. Full detail is in `tests/test-summary.md`.
- **No durable wire shape changed:** `WorkItemV1Catalog.Count` stays 36 and the golden corpus is unchanged. The new read model is a plain (non-polymorphic) record.

### File List

**Production (modified):**
- `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs`

**Production (new):**
- `src/Hexalith.Works.Contracts/Models/WorkItemExecutorBindingView.cs`

**Tests (modified):**
- `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`

**Tests (new):**
- `tests/Hexalith.Works.UnitTests/WorkItemUniformExecutorBindingTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemExecutorBindingViewTests.cs`
- `tests/Hexalith.Works.IntegrationTests/UniformExecutorBindingSerializationTests.cs`
- `tests/Hexalith.Works.IntegrationTests/UniformExecutorBindingLifecycleFlowTests.cs` (added by the QA gap-filling pass; was previously undocumented in this list)

**Docs / bookkeeping (modified):**
- `docs/boundary-decision-record.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md`

## Change Log

| Date | Change |
|------|--------|
| 2026-06-17 | Story 4.1 implemented: `ExecutorBinding` rejects `AuthorityLevel.Unknown`/undefined; added `WorkItemExecutorBindingView` read model; added uniform-binding unit + serialization tests and a zero-executor-kind-branching fitness test; updated boundary record and test summary. +35 tests (516 green); 0 warnings / 0 errors. Status → review. |
| 2026-06-17 | Senior Developer Review (AI): hardened the zero-branching fitness test to also forbid relational comparisons (`>`,`>=`,`<`,`<=`) on the ordered `AuthorityLevel` enum (closes a carried-not-enforced guard hole); reconciled the stale story record with reality — added the previously-undocumented `UniformExecutorBindingLifecycleFlowTests.cs` to the File List and corrected the verified count to **528 green** (419/79/29/1). 0 CRITICAL findings → Status → done. |

## Senior Developer Review (AI)

**Reviewer:** Administrator · **Date:** 2026-06-17 · **Outcome:** Approve (after auto-fixes)

Adversarial review of the implementation against the five acceptance criteria, the seven tasks, and git
reality. The core implementation is sound: `ExecutorBinding` is the single `PartyId + Channel +
AuthorityLevel` shape with no kind discriminator (sealed, structurally locked by test), the aggregate and
`WorkItemState` carry the binding through create/assign/claim/complete without inspecting it,
`AuthorityLevel` survives serialization and replay, and the kernel stays free of forbidden
LLM/routing/email/MCP/UI/security packages. All four test binaries are green and the Release build is
clean. **Verified counts: UnitTests 419, IntegrationTests 79, ArchitectureTests 29, PropertyTests 1 = 528
green; 0 warnings / 0 errors.** No CRITICAL or HIGH findings — every `[x]` task and AC is genuinely
implemented.

Findings (all MEDIUM, auto-fixed during review):

1. **Fitness-guard false negative (test quality, AC #3/#5).**
   `ScaffoldGovernanceTests.P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority` — the guard
   that enforces "no v1 behavior branches on `AuthorityLevel`" — only forbade `==`/`!=`/`case`/`switch`/
   `is`. `AuthorityLevel` is an *ordered* set, so the realistic enforcement shape (`authority >=
   AuthorityLevel.Coordinate`) would have slipped through. Extended the forbidden patterns to cover
   relational operators on `AuthorityLevel`. Confirmed no production code currently uses such a
   comparison, so the guard stays green; also read each scanned file once instead of once-per-pattern.

2. **Incomplete File List (documentation transparency).** Git shows a new integration test,
   `UniformExecutorBindingLifecycleFlowTests.cs`, that `tests/test-summary.md` documents (the QA pass's
   end-to-end flow test) but the story's Dev Agent Record → File List omitted. Added it.

3. **Stale verification counts (documentation transparency).** The Debug Log / Completion Notes / Change
   Log recorded only the dev-story pass (516; UnitTests 414 / IntegrationTests 72) and never mentioned the
   `bmad-qa-generate-e2e-tests` gap-filling pass that `test-summary.md` documents. Corrected the counts to
   the verified **528** and recorded the QA pass.

Observation (not fixed — out of story scope): the working tree carries a `Hexalith.Tenants` submodule
gitlink change (`51b5b42` → `2cfbe0f`) unrelated to Story 4.1, which touches no sibling submodule. Left
untouched rather than reverted, since it may be an intentional dependency bump and is outside this
story's changeset; flagged here for visibility.
