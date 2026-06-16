---
baseline_commit: c1ba6bb
---

# Story 3.1: Guard Tenant-Safe Work Tree Shape

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a coordinator,
I want Work Items to form a tenant-safe acyclic tree,
so that parent-child coordination cannot create loops, duplicate parents, or cross-tenant roll-up leaks.

## Acceptance Criteria

1. **Given** a Work Item is attached to a parent
   **When** the parent-child relationship is validated
   **Then** the child has at most one parent
   **And** the relationship stores references by ID rather than embedding child state.

2. **Given** a proposed parent-child relationship would create a cycle
   **When** the relationship is handled
   **Then** the command is rejected as a domain rejection
   **And** the existing Work Tree state is unchanged.

3. **Given** a proposed parent-child relationship crosses tenants
   **When** the relationship is handled
   **Then** the command is rejected as a domain rejection
   **And** no projection or traversal can silently treat the items as same-tenant data.

4. **Given** a proposed relationship exceeds the configured max depth
   **When** the relationship is handled
   **Then** the command is rejected as a domain rejection
   **And** the default max depth is documented as 32 unless overridden by tenant/type policy.

5. **Given** tree-shape validation is tested
   **When** negative-path tests run
   **Then** cycle, second-parent, cross-tenant, and max-depth cases are covered
   **And** breadth is not capped by the domain guard.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile scope with the existing parent-reference implementation (AC: #1, #3)**
  - [x] Read `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs`,
    `src/Hexalith.Works.Contracts/ValueObjects/ParentWorkItemReference.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`,
    `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotReferenceParentFromAnotherTenant.cs`,
    `src/Hexalith.Works.Contracts/State/WorkItemState.cs`, and
    `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` before editing.
  - [x] Preserve the existing reference-only parent shape: `ParentWorkItemReference` carries
    `TenantId` + `WorkItemId`; no child state, descendant list, roll-up totals, Party data, Tenant
    profile, Conversation data, or EventStore envelope metadata is embedded in Work Item events or state.
  - [x] Preserve existing cross-tenant creation behavior unless replacing it with an equivalent guard:
    `CreateWorkItem` with a parent from another tenant returns `WorkItemCannotReferenceParentFromAnotherTenant`
    as a rejection-only `DomainResult`.
  - [x] Keep replay behavior explicit: `WorkItemState.Apply(WorkItemCreated)` trusts persisted events
    and preserves a foreign parent reference as foreign data rather than coercing it to the work item's tenant.

- [x] **Task 2 - Add a pure tree-shape validation contract reusable by create and future spawn (AC: #1-#5)**
  - [x] Introduce a small domain-level tree guard in the kernel, preferably under
    `src/Hexalith.Works.Server/Aggregates` or `src/Hexalith.Works.Server/Validation`, that validates a
    proposed parent-child attachment from caller-supplied tree facts.
  - [x] The guard must be pure and synchronous: no Dapr, EventStore reads, projection reads, clocks,
    filesystem, HTTP, database, or generated IDs. The caller supplies all ancestry/current-parent facts
    needed to decide.
  - [x] Model only the facts needed for tree-shape decisions, such as tenant, child id, proposed parent id,
    existing child parent if known, ancestor chain or depth information, and max-depth policy. Do not build
    roll-up projection state, descendant traversal services, repositories, or a spawn workflow in this story.
  - [x] Use the existing `ParentWorkItemReference`, `TenantId`, and `WorkItemId` value objects rather than
    stringly-typed duplicates.

- [x] **Task 3 - Enforce single-parent semantics without blocking valid creation (AC: #1, #5)**
  - [x] Reject a proposed attachment when supplied current child state/facts already indicate a different
    parent. The rejection must be an `IRejectionEvent` and must not mix with success payloads.
  - [x] Treat idempotent same-parent validation as accepted or no-op according to the chosen guard API, but
    document/test the chosen behavior so later `SpawnChild` cannot reinterpret it.
  - [x] Confirm a child may have zero parent at root creation, and one parent after attachment.
  - [x] Do not add a breadth/fan-out cap. Multiple different children may reference the same parent.

- [x] **Task 4 - Enforce cycle prevention from supplied ancestry facts (AC: #2, #5)**
  - [x] Reject self-parenting (`child == proposedParent`) as a cycle.
  - [x] Reject an attachment when the proposed child appears in the proposed parent's ancestor chain.
  - [x] Ensure rejection leaves `WorkItemState` unchanged and does not advance any event `Sequence`.
  - [x] Keep the cycle guard independent of any future projection implementation. Story 3.3 will build
    roll-up; this story only defines the domain validation rule that prevents invalid tree edges.

- [x] **Task 5 - Enforce tenant equality at every attachment boundary (AC: #3, #5)**
  - [x] Centralize tenant-equality logic so create-time parent validation and later spawn-time validation
    cannot drift.
  - [x] Preserve case-normalized tenant comparison through `TenantId` equality; tests should cover same
    tenant with different input casing.
  - [x] Add negative tests proving cross-tenant parent/child facts fail closed and return a rejection,
    not an exception and not a success with a coerced tenant.
  - [x] Do not implement query-side authorization or roll-up traversal in this story; include only the
    domain guard and tests that later projections can rely on.

- [x] **Task 6 - Enforce max-depth policy with default 32 and uncapped breadth (AC: #4, #5)**
  - [x] Add a lightweight max-depth policy type or constant with default `32`. If adding configuration
    shape, keep it as a pure value supplied to the guard; do not wire appsettings, Dapr config, tenant
    policy stores, or AppHost infrastructure in this story.
  - [x] Reject attachments whose resulting depth exceeds the supplied max depth. Define depth counting in
    tests and documentation so Story 3.2 and Story 3.3 use the same rule.
  - [x] Add boundary tests for exactly-at-limit accepted and one-over-limit rejected.
  - [x] Add or preserve a test proving breadth is not capped by the tree guard.

- [x] **Task 7 - Add rejection contracts and serialization coverage only if the existing rejection is insufficient (AC: #2-#5)**
  - [x] Prefer a small set of specific rejection events over vague strings if new rejection payloads are
    needed, for example cycle, second-parent, and max-depth violations. All new rejection events must
    implement `IRejectionEvent`.
  - [x] Register any new command/event/rejection payloads with `Hexalith.PolymorphicSerializations` and
    update `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` count and samples.
  - [x] Add golden corpus coverage only for durable success event shapes or newly public rejection payloads
    when the existing schema-evolution tests require it. Do not hand-author golden JSON.
  - [x] Keep all changes additive; do not rename existing fields or introduce `V2` payloads.

- [x] **Task 8 - Strengthen tests at the right layer (AC: #1-#5)**
  - [x] Add focused xUnit v3 + Shouldly unit tests for the pure guard: valid root, valid first parent,
    same-tenant casing, second-parent rejection, self-parent cycle, ancestor-chain cycle, cross-tenant
    rejection, max-depth boundary, one-over-depth rejection, and uncapped breadth.
  - [x] Extend existing create tests if `CreateWorkItem` now delegates to the new guard.
  - [x] Add JSON contract-flow coverage for any new public rejection payloads and confirm EventStore
    envelope fields remain absent.
  - [x] Add or update architecture fitness coverage so tree-shape validation stays in the kernel and does
    not introduce Dapr, EventStore runtime reads, clock, HTTP, filesystem, database, or UI dependencies.

- [x] **Task 9 - Update documentation and validation artifacts (AC: #1-#5)**
  - [x] Add a short `docs/work-tree-shape-guard.md` or update an existing Works domain doc to record:
    single-parent, acyclic, single-tenant, max depth default `32`, configurable-by-policy seam, and
    uncapped breadth.
  - [x] Mention explicitly that Story 3.1 does not implement `ChildSpawned`, roll-up, await-conditions,
    cascade traversal, projections, or reactor/runtime behavior.
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` after implementation and keep
    final suite counts in sync with this story's Dev Agent Record.

- [x] **Task 10 - Build and verify the slice (AC: #1-#5)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  - [x] Run the built xUnit v3 executables directly if `dotnet test` is blocked by Microsoft.Testing.Platform
    named-pipe permissions:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    and `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.

## Dev Notes

### Scope Boundary

Story 3.1 starts Epic 3 by making Work Tree attachments safe. It owns the reusable domain guard for
single-parent, acyclic, single-tenant, max-depth-bounded tree shape, plus tests and documentation for
that guard. It closes the shape-guard part of FR-5/FR-13 and creates the invariant that later spawn,
roll-up, await, and cascade stories must use. [Source:
_bmad-output/planning-artifacts/epics.md#Story 3.1: Guard Tenant-Safe Work Tree Shape;
_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-5; #FR-13]

**In scope:** parent-reference validation, reusable pure tree-shape guard, same-tenant enforcement,
cycle and second-parent rejection from supplied facts, max-depth default/policy seam, uncapped breadth,
serialization/contract coverage for any new public payloads, and tests.

**Out of scope:** `SpawnChild` command/event behavior, child creation intents, storing children on the
parent, recursive roll-up, heterogeneous unit subtotals, projection handlers, await-condition storage,
suspend/resume correlation matching, cascade traversal, reactor dispatch/checkpoints, Dapr reminders,
query-side authorization, production UI/channel adapters, and AppHost runtime wiring. Those are Stories
3.2-3.6 and Epic 4. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.2; #Story 3.3; #Story 3.5;
#Story 3.6]

### Current State (files this story modifies or verifies - read before editing)

- `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs` already accepts an optional
  `ParentWorkItemReference? Parent`.
- `src/Hexalith.Works.Contracts/ValueObjects/ParentWorkItemReference.cs` already stores parent tenant
  and parent work item id only. It rejects null tenant/id and embeds no parent state.
- `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs` already carries optional `Parent` as a
  reference-only field.
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotReferenceParentFromAnotherTenant.cs`
  already exists and implements `IRejectionEvent`.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` already stores a single `Parent` property and
  applies `WorkItemCannotReferenceParentFromAnotherTenant` as a no-op rejection. Its replay comment is
  intentional: the aggregate enforces creation-time cross-tenant validation, while replay preserves
  persisted references verbatim.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` currently enforces only missing-obligation
  precedence and cross-tenant parent validation during `CreateWorkItem`. It has no cycle, second-parent,
  depth, or breadth guard yet.
- `tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs` already covers same-tenant parent persistence,
  cross-tenant rejection, replay preserving a foreign persisted parent reference, same-tenant casing, and
  missing-obligation precedence over cross-tenant parent rejection.
- `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs` already covers
  `ParentWorkItemReference` null guards.
- `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs` already covers reference-only
  JSON and cross-tenant parent rejection serialization.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` currently lists **31** payloads:
  13 success events, 13 commands, and 5 rejection events. Update the count if and only if this story adds
  public payload types.

### Key Design Decisions

- **D1 - Guard decisions are pure and caller-fed.** The aggregate/guard must not load a tree, query a
  projection, or read EventStore at decision time. Later application code can supply ancestry facts, but
  this story's domain rule must remain deterministic and unit-testable. [Source:
  _bmad-output/planning-artifacts/architecture.md#Process Patterns]
- **D2 - Parent links are references, not embedded graphs.** A child stores at most one
  `ParentWorkItemReference`; the parent is not embedded and no descendant list is required for this
  story. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-5]
- **D3 - Tenant equality is a fail-closed invariant.** Cross-tenant parent/child links are rejected so
  roll-up and cascade traversal cannot leak across tenants later. Tenant id casing should normalize
  through `TenantId`. [Source: _bmad-output/planning-artifacts/epics.md#NFR-1; #Story 3.1]
- **D4 - Max depth default is 32; breadth is uncapped.** The guard rejects over-depth trees but must not
  cap the number of children a parent may have. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-13]
- **D5 - This is not the roll-up implementation.** Do not add `RollUpView`, projection actors, per-child
  sequence LWW accounting, SignalR notifications, or property tests for roll-up convergence here. Story
  3.3 owns that. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.3]

### Technical Requirements

- Keep `Contracts`, `Server`, and `Projections` infrastructure-free and pure. No Dapr, EventStore runtime
  reads, HTTP, filesystem, clock/timer, generated IDs, UI, LLM, routing, or cost-governance dependency in
  the kernel. [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries]
- Domain rejections must implement `IRejectionEvent`; `DomainResult` must never mix success and rejection
  payloads. [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns]
- Durable success events carry `(AggregateId, Sequence)` first. New rejection events should carry enough
  diagnostic metadata for tests and future ProblemDetails mapping, but no EventStore envelope metadata.
  [Source: _bmad-output/planning-artifacts/epics.md#AR-4; #NFR-2]
- Additive serialization only: no field renames, no removals, no `V2` event types. Register new public
  payloads through `Hexalith.PolymorphicSerializations` and update catalog/golden tests as needed. [Source:
  _bmad-output/planning-artifacts/epics.md#NFR-12]
- Use project references for Hexalith libraries through root submodules. Do not add `PackageReference` or
  `Directory.Packages.props` entries for `Hexalith.*` packages, and do not initialize nested submodules.
  [Source: AGENTS.md#Hexalith library references; AGENTS.md#Submodule rules]
- Keep repository responsibility narrow: tree guard belongs in Works because it is Work Item domain logic;
  persistence, ID generation, generic projection substrate, and tenant directory behavior do not. [Source:
  AGENTS.md#Repository responsibility]

### Project Structure Notes

- Contract types live under `src/Hexalith.Works.Contracts/{Commands,Events,Events/Rejections,ValueObjects,State}`.
  Domain behavior lives under `src/Hexalith.Works.Server/Aggregates` or `src/Hexalith.Works.Server/Validation`.
  Reusable tests remain under the existing `tests/Hexalith.Works.*` projects.
- Prefer one public type per file, file-scoped namespaces under `Hexalith.Works.*`, sealed records for
  commands/events/value objects when adding new public contracts, and xUnit v3 + Shouldly for tests.
- Do not create `.UI`, `.Mcp`, portal, `.Security`, routing, LLM, cost-governance, production channel
  adapter, repository, database, or Dapr actor projects for this story.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` currently enforces
  kernel purity and blocks premature roll-up/reminder behavior. Extend its purity coverage if the new
  guard introduces any new directory or public API that should remain infrastructure-free.

### Previous Story Intelligence

- Story 2.5 finished with **332** green tests: UnitTests 260, IntegrationTests 45, ArchitectureTests 26,
  PropertyTests 1. Keep `_bmad-output/implementation-artifacts/tests/test-summary.md` and this story's
  Dev Agent Record counts in sync after implementation.
- Story 2.5 added no production lifecycle behavior; it reinforced terminal semantics and purity tests.
  Story 3.1 should continue the pattern of focused domain changes with targeted tests instead of broad
  infrastructure work.
- Story 2.4/2.5 established direct execution of built xUnit v3 test binaries as the reliable verification
  path in this sandbox when `dotnet test` is blocked by Microsoft.Testing.Platform named-pipe permissions.
- Story 2.1 established the lifecycle matrix as the single source for state transitions. Do not modify
  lifecycle transition rules for this tree-shape story unless a test proves a direct conflict.

### Git Intelligence

- `c1ba6bb test(story-2.5): Verify terminal work decisions` added terminal lifecycle coverage, strengthened
  kernel purity scans, and left production lifecycle code unchanged.
- `1814301 feat(story-2.4): Re-estimate and reschedule work` added planning-act events and catalog updates;
  it is relevant because tree guard rejections may require the same catalog/golden discipline.
- `cbf1cba feat(story-2.3): Report progress with unit-tagged burn-down` added effort math and
  progress-driven completion; do not disturb own-remaining behavior while adding tree rules.
- `ccf73c5 feat(story-2.2): Record raw-act events and replay state` added polymorphic registration and
  golden-corpus scaffolding; follow that pattern for any new public payloads.
- `fb757f2 feat(story-2.1): Define the lifecycle state machine` added matrix-style domain coverage; keep tree
  validation similarly table-driven where it improves clarity.
- The current working tree already has unrelated changes in `Hexalith.FrontComposer`, `Hexalith.Parties`,
  and `_bmad-output/story-automator/orchestration-1-20260615-182114.md`. Do not revert or mix them into
  Story 3.1 implementation.

### UX / Read-Model Context

No production UI ships in v1. UX artifacts still matter for data shape: future Roll-Up and Work Tree
surfaces must rely on tenant-safe tree edges and must never sum across invalid cross-tenant traversal.
Story 3.1 only supplies the domain guard that makes those later read models safe; it does not ship any UI
or read-model surface. [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/reconcile-prd.md]

### Latest Technical Specifics

No external technology research is required for this story. The implementation is source-local and pinned
by repo policy: .NET 10 / `net10.0`, xUnit v3 `3.2.2`, Shouldly, `System.Text.Json`
`JsonSerializerDefaults.Web`, and `Hexalith.PolymorphicSerializations`. Do not change pinned versions or
introduce new libraries.

### Testing Standards

- Tier-1 pure tests first: tree guard, `CreateWorkItem` parent validation, state unchanged after rejection,
  and JSON contract-flow coverage for any new public rejection payloads.
- Assert `DomainResult` shape directly: success vs rejection vs no-op, event count, event type, and unchanged
  state/sequence after rejection.
- Use replay through `WorkItemState` and existing test helpers; do not mutate private state or rely on test order.
- Architecture tests should scan for forbidden infrastructure symbols in kernel code when adding a new guard
  file/directory.
- Release build is warnings-as-errors. If `dotnet test` fails because of local named-pipe restrictions, build
  and run the generated xUnit v3 executables directly as in Story 2.5.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.1: Guard Tenant-Safe Work Tree Shape] - story
  statement and acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#Epic 3: Work Tree Roll-Up and Durable Await] - cross-story
  context for spawn, roll-up, await, and cascade.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-5] - parent references,
  children by ID, and await-condition context.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-13] - single-parent,
  acyclic, single-tenant, max-depth 32, breadth uncapped.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-1] - tenant isolation
  mandatory at every layer.
- [Source: _bmad-output/planning-artifacts/architecture.md#D2] - tenant equality asserted at every roll-up hop.
- [Source: _bmad-output/planning-artifacts/architecture.md#Implementation Patterns & Consistency Rules] -
  naming, purity, serialization, and dependency rules.
- [Source: src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs] - existing optional parent reference.
- [Source: src/Hexalith.Works.Contracts/ValueObjects/ParentWorkItemReference.cs] - parent reference value object.
- [Source: src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs] - creation event parent field.
- [Source: src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotReferenceParentFromAnotherTenant.cs] -
  existing cross-tenant parent rejection.
- [Source: src/Hexalith.Works.Contracts/State/WorkItemState.cs] - replay state, single parent property, and
  rejection no-op applies.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs] - current create handler and cross-tenant
  parent guard.
- [Source: tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs] - existing parent-reference unit coverage.
- [Source: tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs] - existing JSON
  contract-flow coverage.
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs] - kernel purity
  and no-premature-roll-up/reminder fitness checks.

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-16T20:25:02+02:00 — Story and sprint status moved to `in-progress`; existing
  `baseline_commit: c1ba6bb` preserved.
- Red phase: added `WorkTreeAttachmentGuardTests`; targeted unit build failed on missing guard API as
  expected.
- Green/refactor phase: implemented pure work-tree attachment guard, specific rejection contracts,
  create-time delegation, replay no-op handlers, catalog registration samples, and documentation.
- Validation: `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  passed.
- Validation: `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  passed with 0 warnings and 0 errors.
- Validation: xUnit v3 executables passed — UnitTests 274/274, IntegrationTests 46/46,
  ArchitectureTests 26/26, PropertyTests 1/1.
- QA validation: `bmad-qa-generate-e2e-tests` added four focused guard tests for tenant casing and
  max-depth policy override gaps.
- QA validation: xUnit v3 executables passed — UnitTests 278/278, IntegrationTests 46/46,
  ArchitectureTests 26/26, PropertyTests 1/1.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Implemented a pure, synchronous `WorkTreeAttachmentGuard` with caller-supplied facts for proposed
  parent, current parent, ancestor chain, parent depth, and max-depth policy.
- Preserved the existing reference-only parent shape and replay trust boundary: `WorkItemCreated`
  still carries only `ParentWorkItemReference`, and replay preserves persisted parent references
  verbatim.
- Centralized create-time parent validation through the guard while preserving cross-tenant rejection
  behavior with `WorkItemCannotReferenceParentFromAnotherTenant`.
- Added public rejection contracts for second-parent, cycle, and max-depth violations, all implementing
  `IRejectionEvent` and registered through the polymorphic catalog.
- Documented default max depth `32`, root depth `1`, policy-supplied override, idempotent same-parent
  validation, and uncapped breadth in `docs/work-tree-shape-guard.md`.
- Updated test summary counts to 351 green tests: UnitTests 278, IntegrationTests 46, ArchitectureTests
  26, PropertyTests 1.

### File List

- `_bmad-output/implementation-artifacts/3-1-guard-tenant-safe-work-tree-shape.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/work-tree-shape-guard.md`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotReferenceSecondParent.cs`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTreeCycleRejected.cs`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTreeDepthExceeded.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentFacts.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentValidationResult.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkTreeDepthPolicy.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkTreeAttachmentGuardTests.cs`

### Change Log

| Date | Version | Description | Author |
|------|---------|-------------|--------|
| 2026-06-16 | 0.1 | Implemented tenant-safe work tree shape guard, rejection contracts, tests, documentation, and validation artifacts. Status set to review. | GPT-5 Codex |
| 2026-06-16 | 0.2 | Adversarial senior review (auto-fix mode): verified Release build (0/0), 351/351 tests, all ACs, all tasks, File List vs git, polymorphic registration, and kernel purity. 0 Critical/High/Medium defects; 4 Low forward-looking notes recorded for Story 3.2/3.3. Outcome: Approve. Status set to done. | Administrator (AI review) |

## Senior Developer Review (AI)

**Reviewer:** Administrator · **Date:** 2026-06-16 · **Mode:** autonomous, auto-fix · **Outcome:** ✅ Approve

### Verification performed (claims validated against reality)

- **Build:** `dotnet build Hexalith.Works.slnx -c Release` → **0 warnings, 0 errors** (warnings-as-errors). Confirmed.
- **Tests (built xUnit v3 binaries run directly):** UnitTests **278/278**, IntegrationTests **46/46**, ArchitectureTests **26/26**, PropertyTests **1/1** → **351/351**. Matches Dev Agent Record and `test-summary.md` exactly.
- **File List vs git:** every claimed file is present in `git status`; no undocumented source changes. The only extra working-tree changes (`Hexalith.FrontComposer`, `Hexalith.Parties`, `orchestration-1-…md`) are pre-existing and correctly excluded by the story. No discrepancy.
- **Acceptance Criteria:** AC#1 single-parent + reference-by-ID (✓ `WorkItemCannotReferenceSecondParent`, `ParentWorkItemReference` is reference-only); AC#2 cycle rejected + state unchanged (✓ self-parent & ancestor-chain, create test proves replay stays at `Sequence 0`); AC#3 cross-tenant rejected + no silent same-tenant coercion (✓ parent & ancestor checks, replay preserves foreign reference verbatim); AC#4 max-depth default 32 documented + boundary tests (✓ at-limit accept / one-over reject / policy override); AC#5 negative paths for all four cases + uncapped breadth (✓). All implemented and tested.
- **Tasks 1–10:** every `[x]` is genuinely done — no false completion claims found.
- **Serialization (Task 7):** the 3 new rejection types carry `[PolymorphicSerialization]` and are *genuinely* registered — `WorkItemSerializationRegistrationTests` iterates all 34 catalog entries, serializes each through the empty `Polymorphic` base, asserts the `$type` discriminator, and round-trips. Catalog count `31 → 34` (13 success + 13 commands + 8 rejections). Not merely a count bump.
- **Kernel purity:** `WorkTreeAttachmentGuard` uses only pure LINQ over caller-supplied facts (no clock/IO/Dapr/EventStore reads/generated IDs); covered by the existing recursive `P0_WorkItemKernelRemainsPure` scan over `src/Hexalith.Works.Server` (26/26 architecture tests green).

### Findings

| # | Severity | Finding | Disposition |
|---|----------|---------|-------------|
| 1 | Low | The `CreateWorkItem` handler feeds the guard hardcoded `ProposedParentDepth: 1` and empty ancestors, so AC#2 (ancestor-chain cycle) and AC#4 (max-depth) are enforceable only through the pure guard, never from the command path. | **By design / in-scope** — at create time a brand-new item has no descendants, so an ancestor cycle is impossible and empty ancestors is *correct*; real depth/ancestry facts are the spawn story's responsibility (3.2/3.3). No change. |
| 2 | Low | A cross-tenant *ancestor* reuses `WorkItemCannotReferenceParentFromAnotherTenant` with the offending ancestor placed in the `Parent` field — slightly imprecise naming. Only reachable via future spawn (ancestors are empty at create). | Acceptable reuse for 3.1; a dedicated event would be premature scope. Flag for 3.2 if ancestor diagnostics need disambiguation. |
| 3 | Low | Guard precedence checks second-parent before the self/ancestor cycle, so a self-referential attach on an item that already has a *different* parent reports "second parent" rather than "cycle." | Both are fail-closed rejections; diagnostic-only. Existing precedence (tenant → second-parent → cycle → depth) is defensible. No change. |
| 4 | Low | `WorkTreeAttachmentFacts.ProposedParentDepth` defaults to `1` — a foot-gun if a future caller attaching under a deep parent forgets to pass the real depth (would under-count and bypass max-depth). | Convenience default; 3.2 spawn callers must pass the parent's true depth. Recorded for the spawn story. |

**Result:** 0 Critical, 0 High, 0 Medium, 4 Low. No code changes warranted — all Low items are forward-looking notes for Story 3.2/3.3, not defects in 3.1's scope. Manufacturing speculative edits to correct, tested, in-scope domain code would reduce quality, so none were applied.

### Review Follow-ups (AI) — carry into Story 3.2/3.3

- [ ] [AI-Review][Low] Spawn-time attachment must supply the parent's real depth and full ancestor chain to the guard so AC#2/AC#4 are enforced on the live tree, not only in unit tests. [src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:26]
- [ ] [AI-Review][Low] Decide whether a cross-tenant *ancestor* deserves a distinct rejection vs. reusing `WorkItemCannotReferenceParentFromAnotherTenant`. [src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs:30]
