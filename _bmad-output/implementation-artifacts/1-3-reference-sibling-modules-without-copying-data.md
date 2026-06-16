---
baseline_commit: 5f3e497d17e7a41b6988fb697e7ff1ceceaa664f
---

# Story 1.3: Reference Sibling Modules Without Copying Data

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want Work Items to carry only reference value objects for sibling-module concepts,
so that Works owns coordination facts while Parties, Conversations, Tenants, EventStore, and Commons remain the systems of record.

## Acceptance Criteria

1. **Given** the Works contracts define references to sibling concepts
   **When** Work Item commands, events, state, and read-model contracts are inspected
   **Then** Parties are represented by `PartyId`
   **And** Conversations are represented by a correlation/reference ID
   **And** Tenants are represented by `TenantId`
   **And** Work IDs are supplied from the edge rather than generated in the aggregate.

2. **Given** a Work Item is created with Party, Conversation, Tenant, and parent/work references
   **When** its event payloads and replayed state are inspected
   **Then** they contain only stable reference IDs and coordination facts
   **And** they do not contain Party display names, contact channels, tenant profiles, conversation messages, EventStore envelopes, or generated ID implementation details.

3. **Given** a Conversation correlation ID is absent
   **When** a Work Item is created or replayed
   **Then** the Work Item remains valid
   **And** no comment store or conversation storage is created inside Works.

4. **Given** a future adapter or projection needs sibling-module details
   **When** the domain contract is inspected
   **Then** the contract exposes only references that can be resolved on demand outside the aggregate
   **And** no direct infrastructure, client, or server dependency on sibling implementation details is required in `Contracts`.

5. **Given** tenant isolation is mandatory
   **When** commands, events, keys, and log scopes are derived for a Work Item
   **Then** the tenant reference is present in the coordination identity
   **And** tests prove cross-tenant references cannot be silently treated as same-tenant data.

## Tasks / Subtasks

- [x] **Task 1 - Make sibling references explicit contract value objects (AC: #1, #2, #4)**
  - [x] Add `src/Hexalith.Works.Contracts/ValueObjects/PartyId.cs` as a stable reference ID to `Hexalith.Parties`.
  - [x] Add `src/Hexalith.Works.Contracts/ValueObjects/Channel.cs` as an additive-tolerant executor channel value, or an enum with `Unknown = 0` plus named values needed by v1 tests.
  - [x] Update `ExecutorBinding` to carry `PartyId`, `Channel`, and `AuthorityLevel`; remove or obsolete the generic `ExecutorId` string shape in favor of `PartyId`.
  - [x] Keep `ConversationCorrelationId` as the optional dialogue reference. Rename to `ConversationId` only if the migration is low-risk and all Story 1.2 tests are updated consistently.
  - [x] Keep `TenantId` and `WorkItemId` as Works-owned reference value objects validated through `AggregateIdentity`; do not call Commons ID generation from contracts or server code.

- [x] **Task 2 - Enforce tenant-safe work references without tree behavior (AC: #1, #2, #5)**
  - [x] Update `ParentWorkItemReference` so it carries the parent `TenantId` as well as the parent `WorkItemId`, or add an equivalent tenant-scoped work reference value object.
  - [x] Ensure create handling rejects or otherwise prevents a parent reference from another tenant from being accepted silently.
  - [x] Represent same-tenant parent links as stable IDs only; do not embed parent obligation, status, effort, schedule, executor binding, or child collections.
  - [x] Do not implement acyclic/depth/tree traversal rules in this story; those belong to Epic 3.

- [x] **Task 3 - Preserve reference-only create payloads and replayed state (AC: #1, #2, #3, #5)**
  - [x] Update `CreateWorkItem`, `WorkItemCreated`, and `WorkItemState` to use the explicit reference value objects from Tasks 1 and 2.
  - [x] Keep `ConversationCorrelationId` nullable; create and replay must remain valid when absent.
  - [x] Keep `WorkItemCreated` as an EventStore payload only: `AggregateId`, `Sequence`, `TenantId`, `WorkItemId`, obligation, and coordination/reference facts.
  - [x] Do not create read-model/projection contracts solely for Story 1.3; if an existing model contract is touched, keep it reference-only under the same no-copy rules.
  - [x] Do not add Party display names, email addresses, phone numbers, tenant names/profiles, conversation text/messages, EventStore envelope fields, generated ID internals, or sibling DTOs to commands, events, state, or read-model contracts.
  - [x] Keep `WorkItemAggregate.Handle` pure: no generated IDs, no clock, no Dapr, no I/O, no HTTP/client calls, and no EventStore envelope APIs.

- [x] **Task 4 - Add architecture fitness tests for sibling boundaries (AC: #4, #5)**
  - [x] Add or extend tests to fail if `Hexalith.Works.Contracts` references sibling client/server/implementation projects such as `Hexalith.Parties.Client`, `Hexalith.Parties.Server`, `Hexalith.Conversations.*`, `Hexalith.Tenants.Server`, `Hexalith.EventStore.Client`, or any adapter/runtime project.
  - [x] Add a fitness test that `Hexalith.*` dependencies in Works project files are `ProjectReference`s, never `PackageReference`s or entries in `Directory.Packages.props`.
  - [x] If new root-path variables are needed, add them to `Directory.Build.props` using the existing `$(Hexalith<Module>Root)` pattern; do not hard-code sibling paths and do not add NuGet package references for Hexalith libraries.
  - [x] Preserve existing dependency direction tests: `Server` references `Contracts` only; `Projections` references `Contracts` only; `Reactor` stays adapter-ring and references inward.

- [x] **Task 5 - Add focused tests for reference-only behavior (AC: #1-#5)**
  - [x] Update `WorkItemCreateTests` to create with `PartyId`, `Channel`, `AuthorityLevel`, tenant-scoped parent reference, and optional conversation correlation ID.
  - [x] Assert replayed state contains the same reference value objects and no denormalized sibling data.
  - [x] Add JSON round-trip assertions that serialized `WorkItemCreated` omits Party names/contact values, tenant profiles, conversation messages/comment bodies, EventStore envelope metadata, and generated ID implementation details. The executor `Channel` value itself is allowed because it is part of `ExecutorBinding`.
  - [x] Test absent conversation correlation ID remains valid and does not materialize a comment store or conversation collection.
  - [x] Test cross-tenant parent/work references fail closed as a domain rejection or are impossible through the value object; do not let a foreign tenant reference replay as same-tenant data.

- [x] **Task 6 - Build and verify the focused slice (AC: #1-#5)**
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`.
  - [x] Run affected test assemblies/projects individually, at minimum UnitTests, IntegrationTests, and ArchitectureTests.
  - [x] If `dotnet test` is blocked by Microsoft.Testing.Platform named-pipe permissions in this sandbox, build first and run the generated xUnit v3 test executables as Story 1.2 did.
  - [x] Do not use recursive submodule commands.

## Dev Notes

### Scope Boundary

Story 1.3 is a contract and guardrail story for FR-21. It tightens the boundary created in Story 1.2 so Works stores reference IDs and coordination facts only. It does **not** add Parties, Conversations, Tenants, EventStore, or Commons runtime integration; it does **not** add a comment store; it does **not** add production adapters, routing, LLM, cost, UI, lifecycle transitions, roll-up, queueing, claim, spawn, suspend/resume, or EventStore command-pipeline wiring. [Source: _bmad-output/planning-artifacts/epics.md#Story 1.3: Reference Sibling Modules Without Copying Data; _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-21: Reference sibling modules, never copy them]

### Reference Ownership Rules

- Works owns Work Item coordination facts: obligation, optional effort/schedule, executor binding as a value object, parent/work references, tenant-scoped identity, and optional conversation correlation. Sibling modules own identity, dialogue, persistence/envelopes, tenant lifecycle/isolation source of truth, and ID generation. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#4.6 Thin-Core Boundaries & Module Ports]
- `ExecutorBinding` must be the documented `PartyId + Channel + AuthorityLevel` shape. The current Story 1.2 implementation uses a generic `string ExecutorId`; Story 1.3 should replace that ambiguity with an explicit `PartyId` value object and channel value. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Executor Binding value object; src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs]
- The `PartyId` is a stable reference to `Hexalith.Parties`, not a display profile. Do not store party display name, person details, organization details, contact values, provider addresses, or executor kind branches in Works events/state. [Source: Hexalith.Parties/_bmad-output/project-context.md#Critical Don't-Miss Rules; _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#Glossary]
- Conversation linkage is optional and by correlation/reference ID only. Works must not create a comment store, conversation messages collection, transcript table, provider session ID store, or `Hexalith.Conversations` implementation dependency. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-21; Hexalith.Conversations/_bmad-output/project-context.md#Critical Don't-Miss Rules]
- `TenantId` must be present in the command/event/state identity path and parent/work references must be tenant-safe. Tenant IDs are coordination references; Works does not copy tenant profile/configuration data from `Hexalith.Tenants`. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFRs; Hexalith.Tenants/_bmad-output/project-context.md#Identity Rules]
- `WorkItemId` is supplied from the edge. `WorkItemAggregate.Handle` must not call `Guid.NewGuid`, `UniqueIdHelper.Generate`, or any Commons helper. Commons is the ID source at the edge, not inside the aggregate. [Source: _bmad-output/planning-artifacts/architecture.md#Data Architecture; _bmad-output/implementation-artifacts/1-2-create-a-tenant-scoped-work-item.md#Required Domain Model Shape]

### Existing Files To Update Carefully

- `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs` currently carries optional `ExecutorBinding`, `ParentWorkItemReference`, and `ConversationCorrelationId`. Preserve backward-compatible create semantics where possible while replacing ambiguous executor and parent reference shapes.
- `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs` currently serializes all create coordination facts. Keep it additive and reference-only; do not add EventStore envelope fields or sibling DTOs.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` currently replays `WorkItemCreated` into tenant/work identity, optional effort, schedule, parent, executor binding, and conversation correlation. Preserve replay determinism and `AggregateIdentity` derivation.
- `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs` currently stores `string ExecutorId`; this is the main Story 1.3 refactoring target.
- `src/Hexalith.Works.Contracts/ValueObjects/ParentWorkItemReference.cs` currently stores only `WorkItemId`; Story 1.3 must prevent cross-tenant parent references from being accepted silently.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` currently validates obligation, normalizes initial effort, and emits `WorkItemCreated`. Keep it pure and add only reference/tenant validation needed by this story.
- `tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs`, `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs`, and `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs` already cover create/replay and reference-only JSON. Extend these rather than adding a parallel test style.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` and `ScaffoldGovernanceTests.cs` already enforce dependency direction and infrastructure-free kernel projects. Extend them for sibling-reference and no-Hexalith-PackageReference rules.

### Project Reference Rules

- Hexalith libraries must be referenced through root submodule `ProjectReference`s, never `PackageReference`s. This repository already defines `$(HexalithEventStoreRoot)` and `$(HexalithTenantsRoot)` in `Directory.Build.props`; add `$(HexalithPartiesRoot)`, `$(HexalithConversationsRoot)`, or `$(HexalithCommonsRoot)` only if the implementation truly needs compile-time contract references. [Source: AGENTS.md#Hexalith library references - ALWAYS use ProjectReference, NEVER PackageReference; Directory.Build.props]
- Prefer Works-owned lightweight reference value objects over compile-time dependencies when only a stable ID string is needed. Story 1.3 should not add a dependency on `Hexalith.Parties.Client`, `Hexalith.Parties.Server`, `Hexalith.Conversations.Client`, `Hexalith.Tenants.Server`, `Hexalith.EventStore.Client`, or Commons implementation packages. [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries]
- Do not add any `Hexalith.*` entry to `Directory.Packages.props`. Third-party packages still use central package management as usual, but this story should not need new third-party packages. [Source: AGENTS.md#Hexalith library references - ALWAYS use ProjectReference, NEVER PackageReference]

### Testing Standards

- Use xUnit v3 and Shouldly. Do not introduce raw `Assert.*`, Moq, or FluentAssertions. [Source: Hexalith.EventStore/_bmad-output/project-context.md#Testing Rules; Hexalith.Parties/_bmad-output/project-context.md#Testing Rules]
- Run test projects individually. Use `.slnx` for restore/build only. [Source: Hexalith.EventStore/_bmad-output/project-context.md#Testing Rules]
- Keep Tier-1 tests pure: no Dapr, Aspire, containers, network, file I/O, sleeps, or sibling service calls. [Source: _bmad-output/planning-artifacts/architecture.md#Implementation Patterns & Consistency Rules]
- Add negative tests for tenant isolation and denormalized sibling-data leakage, not only happy-path reference preservation. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFRs]

### Latest Technical Notes

- Local version pins are authoritative: .NET SDK `10.0.301`, Dapr `1.18.2`, Aspire `13.4.3`, xUnit v3 `3.2.2`, central package management. Do not upgrade versions as part of Story 1.3. [Source: _bmad-output/planning-artifacts/architecture.md#Starter Template Evaluation; Directory.Packages.props]
- Story 1.1 recorded that EventStore optimistic concurrency is Dapr state-store ETag based, not an explicit expected-version append argument. Story 1.3 should not add EventStore.Client or command-pipeline assumptions. [Source: docs/eventstore-api-surface-constraints.md]
- Story 1.2 validation notes still apply in this sandbox: use `NuGetAudit=false` when network-restricted vulnerability lookup causes `NU1900`; use serialized `-m:1` builds; `dotnet test` may be blocked by MTP named-pipe permissions, so generated xUnit executables are acceptable after a successful build. [Source: _bmad-output/implementation-artifacts/tests/test-summary.md]

### Previous Story Intelligence

Story 1.2 completed the first pure create/replay slice and left these patterns to preserve:

- `WorkItemAggregate.Handle(CreateWorkItem, WorkItemState?)` is a pure static handler returning `DomainResult`.
- `WorkItemCreated` carries `AggregateId` and `Sequence`, and `WorkItemState.Apply` reconstructs state deterministically.
- `TenantId` and `WorkItemId` are validated through `AggregateIdentity` and normalize casing.
- Initial effort is normalized so create-time `Done` starts at `0`.
- `Hexalith.Works.Server` intentionally avoids `Hexalith.EventStore.Client`; architecture tests assert Server depends only on Contracts.
- Existing tests use xUnit v3 + Shouldly and direct handler/state replay. Keep extending this style. [Source: _bmad-output/implementation-artifacts/1-2-create-a-tenant-scoped-work-item.md#Completion Notes List]

### Git Intelligence

Recent commits:

- `5f3e497 feat(story-1.2): Create a Tenant-Scoped Work Item` established the current contract/value-object surface, pure create handler, create/replay tests, JSON round-trip tests, and architecture fitness tests.
- `37b65f5 feat(story-1.1): Set up initial project from starter template` established the scaffold, EventStore API-surface constraints, `.slnx` build pattern, and kernel/adapter boundaries.

Story 1.3 should build on those files rather than introduce another aggregate/test harness or sibling adapter layer.

### Project Structure Notes

- Works repository responsibility is domain code for work items. The Aspire host is allowed as the repository-specific test/manual host; other technical layers belong in shared Hexalith modules unless absolutely Works-specific.
- Only root submodules may be initialized or updated. Never use `--recursive` and never initialize nested submodules inside root submodules.
- Do not modify sibling submodule files for this story.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.3: Reference Sibling Modules Without Copying Data] - story statement and acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-21] - sibling module reference mapping and no-copy rule.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-21: Reference sibling modules, never copy them] - reference value object requirements.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Reference Value Objects] - `PartyId`, `ConversationId`, `TenantId`, Commons ID helper boundary.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Executor Binding value object] - `ExecutorBinding(PartyId, Channel, AuthorityLevel)`.
- [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries] - sibling module boundaries and dependency direction.
- [Source: _bmad-output/planning-artifacts/architecture.md#Project Structure & Boundaries] - target file locations and kernel/adapter split.
- [Source: _bmad-output/implementation-artifacts/1-2-create-a-tenant-scoped-work-item.md#Previous Story Intelligence] - Story 1.2 implementation patterns and review fixes.
- [Source: AGENTS.md#Hexalith library references - ALWAYS use ProjectReference, NEVER PackageReference] - root submodule `ProjectReference` rule.
- [Source: docs/eventstore-api-surface-constraints.md] - EventStore API constraints from Story 1.1.

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-16: `dotnet test tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release --no-restore -m:1 -v minimal` returned a generic Microsoft.Testing.Platform build failure, so validation used Release builds plus generated xUnit v3 executables as allowed by the story notes.
- 2026-06-16: `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` passed.
- 2026-06-16: `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` passed.
- 2026-06-16: Generated xUnit executable runs passed: UnitTests 34/34, IntegrationTests 9/9, ArchitectureTests 19/19, PropertyTests 1/1.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Implemented Works-owned `PartyId` and `Channel` value objects and updated `ExecutorBinding` to the `PartyId + Channel + AuthorityLevel` shape.
- Made parent work references tenant-scoped and added a pure aggregate rejection for cross-tenant parent references.
- Preserved nullable `ConversationCorrelationId`, reference-only `WorkItemCreated` payloads, and deterministic replay without adding sibling runtime dependencies or ID generation.
- Added architecture guardrails for sibling implementation references and Hexalith `ProjectReference` usage, plus focused unit/integration tests for reference-only JSON and tenant isolation.

### File List

- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotReferenceParentFromAnotherTenant.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/Channel.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/ParentWorkItemReference.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/PartyId.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs`
- `_bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`

### Change Log

- 2026-06-16: Completed Story 1.3 implementation and validation; story moved to review.

## Review Findings

_Adversarial code review 2026-06-16 (Blind Hunter + Edge Case Hunter + Acceptance Auditor). All 5 ACs functionally met; 0 Blocker, 0 High. Severities below are post-triage._

### Decisions (resolved 2026-06-16)

- [x] [Review][Decision→Patch] **`Channel` enum forward-compatibility** → resolved: **closed enum + reject `Unknown`**. Keep `Channel` a closed v1 catalog (no tolerant converter), correct the misleading `Channel_exposes_documented_additive_tolerant_catalog` test name, and reject `Channel.Unknown`/undefined casts in `ExecutorBinding`. See patches PD1a/PD1b below. [Channel.cs; ExecutorBinding.cs]
- [x] [Review][Decision→Patch] **Cross-tenant invariant on replay** → resolved: **document the trust boundary**. `Handle` is the sole writer; events are trusted on replay (the replay test stands as proof). Add a clarifying comment, no defensive `Apply` check. See patch PD2 below. [WorkItemState.cs:48]
- [x] [Review][Decision] **`Hexalith.FrontComposer` submodule bump** → resolved: **keep (intentional)**. No action; the `f9ef3c0 → 8f260ed` gitlink bump is accepted by the author.

### Patch

_All 11 patches applied and verified 2026-06-16: Release build 0 warnings / 0 errors; UnitTests 47/47, IntegrationTests 11/11, ArchitectureTests 19/19 green._

- [x] [Review][Patch] **PD1a** — Keep `Channel` closed; rename/rewrite `Channel_exposes_documented_additive_tolerant_catalog` to assert a closed v1 catalog and drop the "additive-tolerant" claim; document `Channel` as closed [src/Hexalith.Works.Contracts/ValueObjects/Channel.cs; tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs]
- [x] [Review][Patch] **PD1b** — Reject `Channel.Unknown` and undefined enum casts in the `ExecutorBinding` constructor (`Unknown` is a deserialization sentinel only) + add a rejection test [src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs; tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs]
- [x] [Review][Patch] **PD2** — Add a clarifying comment on `WorkItemState.Apply(WorkItemCreated)` (and the aggregate guard) recording that `Handle` is the sole writer and the cross-tenant invariant is enforced there, so events are trusted on replay [src/Hexalith.Works.Contracts/State/WorkItemState.cs:48; src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:25]
- [x] [Review][Patch] Scope the over-broad `"Hexalith.Conversations"` denylist entry to implementation suffixes (the adjacent `P0_SourceProjectReferencesFollowWorksArchitectureDirection` allowlist already enforces AC #4 more strictly) [tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs]
- [x] [Review][Patch] Guard `P0_HexalithDependenciesUseProjectReferencesNotPackageReferences` against a vacuous pass — assert the expected project files were discovered before checking violations [tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs]
- [x] [Review][Patch] Add `PartyId` local boundary/validation tests: 256-char passes / 257 throws, trailing separator throws, non-ASCII throws, and case-sensitivity contrast (`PartyId("Party-1") != PartyId("party-1")` vs lowercased `TenantId`) [tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs]
- [x] [Review][Patch] Add a positive same-tenant, different-casing parent test (parent tenant `"Tenant-Alpha"` vs command tenant `"tenant-alpha"` → accepted) to pin the normalization equivalence the cross-tenant guard relies on [tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs]
- [x] [Review][Patch] Add a precedence test for a command that is both missing-obligation and cross-tenant-parent, pinning which rejection wins [tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs]
- [x] [Review][Patch] Add an obligation whitespace-trim round-trip test (padded `"  x  "` → replayed `Obligation.Description == "x"`) [tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs]
- [x] [Review][Patch] Add a mixed-optionals create/replay test (some of Parent/Executor/Schedule/Effort/Conversation null, some present) asserting each replays to its exact value/null [tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs]
- [x] [Review][Patch] Tighten the decoupled `ShouldContain("\"partyId\"")` + `ShouldContain("\"party-123\"")` into the bound token `ShouldContain("\"partyId\":\"party-123\"")` (mirrors the old `executorId` assertion) [tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs]

### Deferred

- [x] [Review][Defer] Rejection events carry no `Sequence`/`AggregateId`; the `state is null ? 1 : 2` sequence logic assumes rejections never advance the stream [WorkItemCannotReferenceParentFromAnotherTenant.cs; WorkItemState.cs:44] — deferred, resolve when EventStore stream-append is wired (out of scope for 1.3; pre-existing pattern from 1.2)
- [x] [Review][Defer] Same-tenant self-parent (parent `WorkItemId` == own `WorkItemId`) is accepted [WorkItemAggregate.cs:25] — deferred, acyclic/tree rules are explicitly Epic 3
- [x] [Review][Defer] `ConversationCorrelationId` has no `AggregateIdentity` validation (accepts colons/whitespace/unbounded length) [ConversationCorrelationId.cs] — deferred, pre-existing from story 1.2; tighten only if it becomes key/topic-bearing

### Dismissed (noise / false positive)

- Blind Hunter "High": `PartyId` canonicalization corrupts/​double-prefixes the value — **false positive** (Blind layer had no project access; `AggregateIdentity.AggregateId` is case-preserving and returns the bare id, so the value round-trips; identical to existing `WorkItemId`).
- Vacuous/fragile `ShouldNotContain("Guid"/"metadata"/...)` negatives — harmless belt-and-suspenders; the structural shape of `WorkItemCreated` is the real guarantee.
- PackageReference guard lacks a positive `ProjectReference` assertion — already covered by the existing allowlist direction test.
