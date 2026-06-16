---
baseline_commit: b0687e2b8a30d7a799552e735beedd54aa88fad4
---

# Story 1.4: Expose Boundary Ports and Decision Record

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want Works to expose explicit domain ports and a boundary decision record,
so that future LLM, routing, cost, security, and sibling-module integrations attach without changing the kernel's ownership model.

## Acceptance Criteria

1. **Given** the Works contract surface is inspected
   **When** domain ports are reviewed
   **Then** `IExpectationResolver` is available as a domain-owned abstraction
   **And** a no-LLM implementation is provided for v1
   **And** Work Item behavior remains valid when no interpreted Expectation is resolved.

2. **Given** executor routing is deferred to a later theme
   **When** the Works contract surface is inspected
   **Then** `IExecutorRouter` exists only as an abstraction
   **And** no v1 implementation, routing engine, scoring model, escalation policy, LLM dependency, or cost-governance dependency is wired into the kernel.

3. **Given** the kernel dependency graph is checked
   **When** `Contracts`, `Server`, and `Projections` are inspected
   **Then** they reference no LLM, routing, cost-governance, UI, channel adapter, or infrastructure implementation type
   **And** architecture-fitness tests enforce the dependency boundary.

4. **Given** the boundary decision record is generated
   **When** `docs/boundary-decision-record.md` is reviewed
   **Then** it enumerates what Works owns versus references for Parties, Conversations, EventStore, Tenants, Commons, and PolymorphicSerializations
   **And** it explains why Works owns coordination facts but not identity, dialogue, persistence, isolation, or ID generation.

5. **Given** future themes will add adapters
   **When** the decision record and port contracts are reviewed
   **Then** they preserve the named seams for AI-inferred expectations, executor routing, cost meter/spend governance, and trust/security hardening
   **And** they explicitly state that those deferred capabilities are not v1 behavior.

## Tasks / Subtasks

- [x] **Task 1 — Add the `IExpectationResolver` port + `Expectation`/`ExpectationReference` seam types (AC: #1, #5)**
  - [x] Create `src/Hexalith.Works.Contracts/Ports/` folder (new; does not exist yet).
  - [x] Add `src/Hexalith.Works.Contracts/Ports/ExpectationReference.cs` — a Works-owned, optional *reference* value object (the stable pointer that gets resolved on demand). Validate it the same way other reference value objects are (mirror `PartyId.cs` — route through `AggregateIdentity` if it will ever be key/topic-bearing; otherwise a trimmed non-empty `string Value` is acceptable for v1). This is a **reference**, never the interpreted value.
  - [x] Add `src/Hexalith.Works.Contracts/Ports/Expectation.cs` — the *interpreted-on-demand* result the resolver returns. Document that it is **resolved on demand and never stored** in an event, command, or replayed state (FR-2, NFR-11).
  - [x] Add `src/Hexalith.Works.Contracts/Ports/IExpectationResolver.cs` — domain-owned abstraction: `ValueTask<Expectation?> ResolveAsync(ExpectationReference reference, CancellationToken cancellationToken = default)`. Place it in namespace `Hexalith.Works.Contracts.Ports`. Document the no-LLM-in-v1 / prompt-injection-boundary intent (NFR-11) **without** introducing any LLM/infrastructure type.
  - [x] Keep all three types pure Contracts (no infra, no LLM, no Dapr). Do **not** add a `Hexalith.*` `PackageReference` or any new sibling reference for these types.

- [x] **Task 2 — Wire the optional Expectation reference onto `Obligation` (FR-2 seam) (AC: #1, #5)**
  - [x] Add an optional `ExpectationReference? Reference = null` to `src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs`, additively and nullably, preserving the existing `new Obligation(string description)` call site in `WorkItemAggregate.Handle` (add an optional second parameter, do not break the trim-on-construct behavior).
  - [x] The reference is **reference-only**: `WorkItemCreated`/`WorkItemState` must never carry a resolved `Expectation`. Replay with the reference **absent** and **present-but-unresolved** must both reconstruct deterministically.
  - [x] Do **not** call `IExpectationResolver` from `WorkItemAggregate.Handle` or from `Apply`. The aggregate stays pure and never resolves; resolution happens on demand outside the aggregate (AC #1: "behavior remains valid when no interpreted Expectation is resolved").
  - [x] Confirm `WorkItemCreated` serialization stays additive (no `V2`); a nullable field on `Obligation` is backward-compatibly deserializable.

- [x] **Task 3 — Add the no-LLM `IExpectationResolver` implementation (AC: #1)**
  - [x] Create `src/Hexalith.Works.Server/Resolvers/` folder (new) and add the no-LLM resolver (e.g. `NoLlmExpectationResolver.cs` / `LiteralExpectationResolver.cs`) implementing `IExpectationResolver`.
  - [x] The v1 impl **does not interpret**: it performs a literal/passthrough resolution (e.g. returns an `Expectation` echoing the reference verbatim, or returns `null`). It must call **no** LLM, no clock, no I/O, no Dapr, no network. It stays inside `Server` (which references `Contracts` only).
  - [x] (Optional, lightweight) If you add a DI registration helper, put it in `src/Hexalith.Works.Server/Registration/` and use only `Microsoft.Extensions.DependencyInjection.Abstractions` (infra-free, allowed) — **never** the full DI/hosting stack, Dapr, or any adapter package. Registration is not required to satisfy AC #1; shipping the concrete resolver class is sufficient.

- [x] **Task 4 — Add the `IExecutorRouter` abstraction-only port (AC: #2, #5)**
  - [x] Add `src/Hexalith.Works.Contracts/Ports/IExecutorRouter.cs` — abstraction only. Suggested minimal shape: `ValueTask<ExecutorBinding?> SelectExecutorAsync(TenantId tenantId, WorkItemId workItemId, CancellationToken cancellationToken = default)`. No routing engine, no scoring model, no escalation policy, no LLM/cost type.
  - [x] Ship **no** implementation of `IExecutorRouter` anywhere in the kernel (`Contracts`/`Server`/`Projections`) or adapters in v1. It is a named seam only (Theme 4).
  - [x] **CRITICAL — banned-substring guard:** `P0_WorkItemSliceDoesNotIntroduceDeferredRuntimeBehavior` (`ScaffoldGovernanceTests.cs`) fails the build if **any** `src/**/*.cs` file contains (case-insensitive) `BurnDown`, `Burndown`, `RollUp`, `Suspend`, `Resume`, `Reminder`, `Claim`, or `Queue` — **including XML doc comments**. Keep the port's name and comments neutral ("executor routing / selection seam, deferred to Theme 4"). Do **not** write phrases like "claim queued work" in `src`. Put all deferred-seam narrative that needs those words in `docs/boundary-decision-record.md` (which is **not** scanned).

- [x] **Task 5 — Author `docs/boundary-decision-record.md` (AC: #4, #5)**
  - [x] Create `docs/boundary-decision-record.md` (FR-23 / AR-19 tracked deliverable). Enumerate **owns vs references** for exactly these six modules: **Parties, Conversations, EventStore, Tenants, Commons, PolymorphicSerializations** (use one row/section per module).
  - [x] For each, state what Works **owns** (coordination facts: obligation, executor binding value object, effort/schedule, parent/work references, tenant-scoped identity, optional conversation correlation, status) versus what it **references** (identity→Parties `PartyId`; dialogue→Conversations correlation ID; persistence/events/envelopes→EventStore; isolation→Tenants `TenantId`; ID generation→Commons; payload (de)serialization→PolymorphicSerializations).
  - [x] Explain **why** Works owns coordination facts but not identity, dialogue, persistence, isolation, or ID generation (no-copy / reference-only rule, FR-21; each sibling remains the system of record).
  - [x] Record the four preserved **deferred seams** and state explicitly they are **not v1 behavior**: (a) AI-inferred expectations → `IExpectationResolver` + `ExpectationReference` (no-LLM impl only in v1, NFR-11 prompt-injection boundary); (b) executor routing/escalation → `IExecutorRouter` (abstraction only, Theme 4); (c) cost meter / spend governance → cost-ready `Meter` reuse (Theme 5); (d) trust / security hardening (signed links, step-up auth, audit query, `AuthorityLevel` enforcement) → carried-not-enforced seams (Theme 6).
  - [x] Cross-reference the architecture decision document (AR-18/AR-19) and FR-21/FR-22/FR-23.

- [x] **Task 6 — Add architecture-fitness tests that enforce the boundary (AC: #2, #3, #4)**
  - [x] Add reflection-based port fitness tests (suggested `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BoundaryPortTests.cs`). To reflect over kernel types, add `ProjectReference`s to `Hexalith.Works.Contracts`, `Hexalith.Works.Server`, and `Hexalith.Works.Projections` in `Hexalith.Works.ArchitectureTests.csproj` (these are kernel projects — referencing them from a test project violates no dependency-direction rule). Use the existing marker types (`WorksContractsAssembly`, `WorksServerAssembly`, `WorksProjectionsAssembly`) to get the assemblies.
  - [x] Test: `IExpectationResolver` and `IExecutorRouter` are declared in `Hexalith.Works.Contracts` (`.Ports` namespace).
  - [x] Test: **no concrete type** across the loaded Works kernel assemblies (`Contracts`, `Server`, `Projections`) implements `IExecutorRouter` (abstraction-only, AC #2). Make it fail closed (assert count == 0).
  - [x] Test: **at least one** concrete type in `Hexalith.Works.Server` implements `IExpectationResolver` (the no-LLM impl exists, AC #1).
  - [x] Extend / keep `P0_KernelProjectsStayInfrastructureFree` (`ScaffoldGovernanceTests.cs`) as the csproj-level guard that the kernel references no LLM/Dapr/UI/MCP/OpenAPI package (AC #3). The existing forbidden list already includes `OpenAI`, `SemanticKernel`, `Dapr.Client`, `ModelContextProtocol`, ASP.NET Components, Swashbuckle — verify it still passes; only add entries if a new package would otherwise slip in.
  - [x] Add a decision-record fitness test (suggested `BoundaryDecisionRecordTests.cs`, file-system based like `RepositoryRoot.Locate()` usage): assert `docs/boundary-decision-record.md` exists and contains each of the six module names (`Parties`, `Conversations`, `EventStore`, `Tenants`, `Commons`, `PolymorphicSerializations`) and each of the four deferred-seam markers (expectation/AI, routing, cost, security). This makes AC #4/#5 falsifiable.
  - [x] Keep all existing fitness tests green: `DependencyDirectionTests` (Contracts→EventStore.Contracts only; Server/Projections/Reactor→Contracts only) and `ScaffoldGovernanceTests` must still pass — the new `Ports/` and `Resolvers/` folders must not add forbidden references.

- [x] **Task 7 — Add focused unit tests for resolver + reference seam (AC: #1, #5)**
  - [x] Add `tests/Hexalith.Works.UnitTests/ExpectationResolverTests.cs` (xUnit v3 + Shouldly): the no-LLM resolver returns a literal/non-interpreted result (or `null`) and never throws for a valid reference; it performs no interpretation.
  - [x] Add create/replay tests proving a Work Item is **valid when no interpreted Expectation is resolved**: create with `Obligation` having (a) no reference and (b) a present `ExpectationReference`; assert replayed state carries the **reference** only and never a resolved `Expectation`.
  - [x] Extend the existing reference-only JSON round-trip assertions (in `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs`) to confirm serialized `WorkItemCreated` carries the expectation **reference** (when supplied) and never a resolved/interpreted `Expectation`. Follow the established bound-token assertion style (`ShouldContain("\"...\":\"...\"")`).
  - [x] Do not add raw `Assert.*`, Moq, or FluentAssertions; extend existing test styles rather than introducing a parallel harness.

- [x] **Task 8 — Build and verify the focused slice (AC: #1-#5)**
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] Run `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` (warnings-as-errors).
  - [x] Run the affected test assemblies individually, at minimum `UnitTests`, `IntegrationTests`, and `ArchitectureTests`.
  - [x] If `dotnet test` is blocked by Microsoft.Testing.Platform named-pipe permissions in this sandbox, build first and run the generated xUnit v3 executables (as Stories 1.2/1.3 did).
  - [x] Do not use recursive submodule commands. Do not modify sibling submodule files.

## Dev Notes

### Scope Boundary

Story 1.4 is the **final Epic 1 story**: it makes the module-boundary ports explicit and writes the owns-vs-references decision record. It covers **FR-22** (module ports as abstractions), **FR-23** (boundary decision record), the remaining **FR-2** seam (Obligation's optional Expectation reference resolved via `IExpectationResolver`), and the **NFR-5 / SM-3 / SM-4** dependency-boundary fitness enforcement. [Source: _bmad-output/planning-artifacts/epics.md#Story 1.4: Expose Boundary Ports and Decision Record; #FR-22; #FR-23; #AR-18; #AR-19]

It does **NOT** implement: the lifecycle state machine, raw-act events beyond `WorkItemCreated`, burn-down, roll-up, the work tree, suspend/resume, executor assignment/claim behavior, the "what's next" query, the reactor runtime, Dapr reminders, Aspire pipeline proof, or any LLM/routing/cost/security implementation. Those are Epics 2–4 and Themes 3–6. **Counter-metrics SM-C1 (don't grow the kernel) and SM-C2 (don't over-fit to deferred themes) are binding here** — ship the named seams, not the machinery. [Source: _bmad-output/planning-artifacts/epics.md#Scope reminder; #SM-C1; #SM-C2]

### Current State (what exists after Story 1.3 — read before coding)

- The kernel is scaffolded and green: `Contracts` (Commands/Events/State/ValueObjects), `Server` (`WorkItemAggregate`), `Projections`/`Reactor` (marker types only), plus `Testing` and five test projects. Story 1.3 finished a pure, reference-only create/replay slice (UnitTests 47/47, IntegrationTests 11/11, ArchitectureTests 19/19, PropertyTests 1/1 at Release with 0 warnings). [Source: _bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md#Patch]
- **There is no `Ports/` folder and no `Resolvers/` folder yet** — Story 1.4 creates both. `grep` for `IExpectationResolver`/`IExecutorRouter`/`Expectation` across `src`/`tests`/`docs` returns nothing today.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` is a pure static handler: `Handle(CreateWorkItem, WorkItemState?) → DomainResult`, currently constructing `new Obligation(command.Obligation)`. Adding an optional `ExpectationReference?` to `Obligation` must not break this call site or the trim-on-construct contract. [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:38]
- `src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs` is today `sealed record Obligation` with a single `string description` constructor that trims. Extend additively. [Source: src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs]
- `WorkItemStatus` currently has only `Unknown=0, Created=1` (the 9-state machine is Epic 2). This is **why** the deferred-term guard below currently passes — do not introduce the deferred status values here. [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs]
- `docs/` contains only `eventstore-api-surface-constraints.md` today; `boundary-decision-record.md` is new. [Source: docs/]

### CRITICAL — Two fitness guards your new files must not trip

1. **Banned-substring guard (`P0_WorkItemSliceDoesNotIntroduceDeferredRuntimeBehavior`)** scans every `src/**/*.cs` (except `*Assembly.cs`, `ServiceDefaults/Extensions.cs`, `AppHost/Program.cs`) and fails if the text contains, case-insensitively: `BurnDown`, `Burndown`, `RollUp`, `Suspend`, `Resume`, `Reminder`, `Claim`, `Queue`. **This includes comments.** Your `IExecutorRouter`, `Expectation*`, and resolver files must avoid all eight substrings in code *and* doc comments. Keep deferred-seam prose (which legitimately needs words like "queue", "claim", "suspend/resume", "cost burn-down") in `docs/boundary-decision-record.md`, which is not scanned. Do **not** weaken this guard to fit a comment — relocate the prose. [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:167-191]
2. **Kernel-purity guards** — `P0_WorkItemKernelRemainsPure` bans `DateTime.Now/UtcNow`, `DateTimeOffset.*`, `Stopwatch`, `Guid.NewGuid`, `UniqueIdHelper.Generate`, `File.`, `Directory.`, `HttpClient`, `Dapr` anywhere in `Server`. The no-LLM resolver must be a pure literal/passthrough (no clock, no I/O, no RNG). `P0_KernelProjectsStayInfrastructureFree` bans LLM/Dapr/UI/MCP/OpenAPI packages from kernel `.csproj`s. [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:118-152,194-220]

### Required Port Shapes (decisions — do not re-litigate)

- **`IExpectationResolver`** (Contracts/Ports) — domain-owned abstraction; resolves an `ExpectationReference` to an `Expectation` *on demand, outside the aggregate*. The no-LLM v1 impl ships in `Server`; nothing in `Handle`/`Apply` calls it. `Expectation` is **never serialized into events/state** (NFR-11, FR-2). This is the future prompt-injection boundary — keep it free of any LLM type. [Source: _bmad-output/planning-artifacts/epics.md#FR-2; #FR-22; #NFR-11; #AR-18]
- **`IExecutorRouter`** (Contracts/Ports) — **abstraction only, no impl wired in v1.** No routing engine, scoring, escalation, LLM, or cost type. Theme 4 fills it. A reflection fitness test asserts zero implementers in the kernel. [Source: _bmad-output/planning-artifacts/epics.md#FR-22; #AR-18; architecture.md#Ports]
- **`ExpectationReference`** is a Works-owned *reference* (stable pointer, may be carried on `Obligation`); **`Expectation`** is the *interpreted result* (resolved on demand, never stored). Mirror the lightweight Works-owned value-object pattern (`PartyId.cs`/`Channel.cs`) — do not add a `Hexalith.Parties`/`Conversations`/`Commons` dependency for these. [Source: _bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md#Reference Ownership Rules; src/Hexalith.Works.Contracts/ValueObjects/PartyId.cs]
- Port doc-comment style: mirror `Hexalith.Parties/.../Search/IPartySearchProvider.cs` — a short summary + a `<remarks>` block that states the v1 limitation and that the contracts package must not require an LLM/infra backend. [Source: Hexalith.Parties/src/Hexalith.Parties.Contracts/Search/IPartySearchProvider.cs]

### Boundary Decision Record content (AC #4/#5 — the falsifiable shape)

`docs/boundary-decision-record.md` must contain, at minimum:

- A per-module owns-vs-references table covering **Parties, Conversations, EventStore, Tenants, Commons, PolymorphicSerializations** (AC #4 names exactly these six).
- The "why": each sibling is the system of record for identity / dialogue / persistence+envelopes / tenant isolation / ID generation / payload serialization; Works owns only *coordination facts* and references the rest by stable ID (FR-21 no-copy rule).
- The four preserved deferred seams, each explicitly tagged **not v1 behavior**: AI-inferred expectations (`IExpectationResolver`), executor routing (`IExecutorRouter`, Theme 4), cost meter/spend governance (cost-ready `Meter`, Theme 5), trust/security hardening (signed links, audit, `AuthorityLevel` enforcement, Theme 6).

[Source: _bmad-output/planning-artifacts/epics.md#FR-21; #FR-23; #AR-19; #UX-DR7-DR16 (deferred horizon); architecture.md#Architectural Boundaries; #Decision Priority Analysis (Deferred Decisions)]

### Reference Ownership Rules (carried from Story 1.3)

- Works owns coordination facts; siblings own identity (Parties), dialogue (Conversations), persistence/envelopes (EventStore), isolation (Tenants), ID generation (Commons), payload serialization (PolymorphicSerializations). References are stable IDs/correlation IDs resolved on demand — never denormalized copies. [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries; _bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md#Reference Ownership Rules]
- `WorkItemAggregate.Handle`/`Apply` stay pure: no generated IDs, no clock, no I/O, no Dapr, no EventStore envelope APIs. IDs are supplied at the edge via Commons (outside the aggregate). [Source: _bmad-output/planning-artifacts/architecture.md#Process Patterns; works-scaffold-facts]

### Project Reference Rules

- Hexalith libraries are referenced as `ProjectReference` through root-submodule root-path variables (`$(Hexalith<Module>Root)`), **never** `PackageReference`, and never added to `Directory.Packages.props`. `Directory.Build.props` currently defines `$(HexalithEventStoreRoot)` and `$(HexalithTenantsRoot)` only. **Story 1.4 should need no new sibling reference** — the ports and reference types are Works-owned. If you genuinely need one, add the root-path variable following the existing pattern. [Source: CLAUDE.md#Hexalith library references; Directory.Build.props; tests/.../DependencyDirectionTests.cs#P0_HexalithDependenciesUseProjectReferencesNotPackageReferences]
- `Hexalith.Commons` and `Hexalith.PolymorphicSerializations` are consumed as **NuGet packages** in this ecosystem; `EventStore`/`Tenants`/`Parties`/`Conversations` are **project references**. `ExpectationReference`/`Expectation` are not event-payload polymorphic types, so they need **no** `PolymorphicSerializations` registration — plain `System.Text.Json`-serializable records suffice (like `PartyId`). [Source: works-scaffold-facts]

### Testing Standards

- xUnit **v3** + Shouldly (+ NSubstitute if a port test double is needed). No raw `Assert.*`, Moq, or FluentAssertions. Run test projects individually; `.slnx` is for restore/build only. [Source: _bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md#Testing Standards; works-scaffold-facts]
- Keep Tier-1 tests pure: no Dapr, Aspire, containers, network, file I/O, sleeps, or sibling service calls. The decision-record and reflection fitness tests use file-system / loaded-assembly reflection only — no runtime substrate. [Source: _bmad-output/planning-artifacts/architecture.md#Implementation Patterns & Consistency Rules]
- The `ArchitectureTests` project is currently file-system/XML based and references no kernel assembly. Adding `ProjectReference`s to the three kernel projects (Contracts/Server/Projections) to enable reflection is acceptable and breaks no direction rule. [Source: tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj]

### Latest Technical Notes

- Pins are authoritative and frozen: .NET SDK `10.0.301`, Dapr `1.18.2`, Aspire `13.4.3`, xUnit v3 `3.2.2`, central package management. Do not upgrade. [Source: _bmad-output/planning-artifacts/architecture.md#Starter Template Evaluation; works-scaffold-facts]
- Story 1.1 recorded that EventStore concurrency is Dapr state-store ETag based (no `expectedVersion` append arg) and online rebuild is checkpoint-per-aggregate. Story 1.4 adds **no** EventStore command-pipeline wiring, so these constraints do not bind this story — but do not contradict them in the decision record. [Source: docs/eventstore-api-surface-constraints.md]
- Sandbox build notes still apply: `NuGetAudit=false` when network-restricted vulnerability lookup causes `NU1900`; serialized `-m:1` builds; `dotnet test` may be blocked by MTP named-pipe permissions, so generated xUnit executables are acceptable after a successful build. [Source: _bmad-output/implementation-artifacts/tests/test-summary.md]

### Previous Story Intelligence (Story 1.3)

- Story 1.3 established Works-owned reference value objects (`PartyId`, `Channel`) and the `ExecutorBinding(PartyId, Channel, AuthorityLevel)` shape; `Channel` is a **closed** v1 enum (rejects `Unknown`/undefined casts). Mirror this "Works-owned, validated, additive but not silently tolerant" posture for `ExpectationReference`. [Source: _bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md#Decisions; src/Hexalith.Works.Contracts/ValueObjects/Channel.cs]
- Cross-tenant parent invariant is enforced once in `Handle` and events are trusted on replay (documented trust boundary, no defensive `Apply` check). Follow the same "validate at the writer, trust on replay" pattern if `ExpectationReference` gets any validation. [Source: 1-3...md#Decisions PD2]
- Review style is adversarial (Blind Hunter + Edge Case Hunter + Acceptance Auditor). Pre-empt it: add a positive same-shape JSON round-trip assertion, a negative "no interpreted Expectation in payload" assertion, and a vacuous-pass guard on any new reflection test (assert the assemblies/types were actually discovered before asserting emptiness). [Source: 1-3...md#Review Findings; tests/.../DependencyDirectionTests.cs#P0_HexalithDependenciesUseProjectReferencesNotPackageReferences (vacuous-pass guard pattern)]

### Git Intelligence

- `b0687e2 feat(story-1.3): Reference sibling modules without copying data` — added `PartyId`/`Channel`, tenant-scoped parent reference + cross-tenant rejection, and the `DependencyDirectionTests` sibling-boundary + no-PackageReference guards. Build on those files; do not fork a parallel test harness.
- `5f3e497 feat(story-1.2)` established the pure create handler + create/replay + JSON round-trip tests. `37b65f5 feat(story-1.1)` established the scaffold, EventStore API-surface constraints, `.slnx` build pattern, and kernel/adapter boundaries.

### Project Structure Notes

- Works holds domain code only; the Aspire host is the one allowed technical component. Other technical layers belong in shared Hexalith modules. The new `Ports/` and `Resolvers/` folders are domain-boundary code and belong here. [Source: CLAUDE.md#Repository responsibility; _bmad-output/planning-artifacts/architecture.md#Complete Project Directory Structure]
- Target locations match the architecture tree: `Contracts/Ports/`, `Server/Resolvers/` (and optional `Server/Registration/`), `docs/boundary-decision-record.md`. [Source: _bmad-output/planning-artifacts/architecture.md#Complete Project Directory Structure]
- Only root submodules may be initialized/updated. Never `--recursive`; never initialize nested submodules. Do not modify sibling submodule files. [Source: CLAUDE.md#Submodule rules; works-scaffold-facts]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.4: Expose Boundary Ports and Decision Record] — story statement and acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-22: Expose module ports as abstractions] — `IExpectationResolver` (no-LLM impl shipped) + `IExecutorRouter` (abstraction only, no impl wired).
- [Source: _bmad-output/planning-artifacts/epics.md#FR-23: Produce the boundary decision record] — `docs/boundary-decision-record.md` tracked deliverable.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-2: Carry an Obligation with an optional Expectation reference] — Obligation + optional Expectation reference, resolved on demand, never stored.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-21: Reference sibling modules, never copy them] — owns-vs-references mapping for the decision record.
- [Source: _bmad-output/planning-artifacts/epics.md#NFR-5: Domain purity; #NFR-11: NL-is-data boundary] — kernel purity + prompt-injection boundary intent of `IExpectationResolver`.
- [Source: _bmad-output/planning-artifacts/architecture.md#AR-18 Ports realization (FR-22); #AR-19 Boundary decision record (FR-23)] — port realization and decision-record requirements.
- [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries; #Complete Project Directory Structure] — sibling boundaries, dependency direction, target file locations.
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs] — banned-substring guard (167-191), kernel purity (194-220), infra-free kernel csproj (118-152).
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs] — dependency-direction + no-Hexalith-PackageReference guards (extend, keep green).
- [Source: src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs; src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:38] — Obligation shape and the call site to preserve.
- [Source: Hexalith.Parties/src/Hexalith.Parties.Contracts/Search/IPartySearchProvider.cs] — sibling port/abstraction doc-comment pattern to mirror.
- [Source: docs/eventstore-api-surface-constraints.md] — EventStore constraints from Story 1.1 (do not contradict in the record).
- [Source: CLAUDE.md#Hexalith library references; #Submodule rules] — ProjectReference-only and root-submodule-only rules.

## Dev Agent Record

### Agent Model Used

Opus 4.8 (1M context) — claude-opus-4-8[1m]

### Debug Log References

- `dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1` — succeeded (all projects restored).
- `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1` — first pass failed with 2 × `xUnit1051` (a `CancellationToken`-accepting call in `ExpectationResolverTests` must pass `TestContext.Current.CancellationToken`); fixed both call sites; second pass succeeded with **0 warnings, 0 errors** (warnings-as-errors).
- `dotnet test` was not used; per the Story 1.2/1.3 sandbox pattern (Microsoft.Testing.Platform named-pipe permissions), the generated xUnit v3 executables were run directly after a successful Release build.

### Completion Notes List

- **AC #1** — Added domain-owned `IExpectationResolver` (`Contracts/Ports`) with `Expectation`/`ExpectationReference` seam types, and the no-LLM `LiteralExpectationResolver` (`Server/Resolvers`) that performs a literal, verbatim passthrough (no LLM/clock/RNG/I/O/Dapr/network). Wired an optional, reference-only `ExpectationReference? Reference` onto `Obligation` additively (preserving the `new Obligation(string)` call site); the aggregate never resolves, so a work item stays valid when no interpreted `Expectation` is produced. Unit + fitness tests prove an implementation exists and that replay carries the reference only.
- **AC #2** — Added `IExecutorRouter` (`Contracts/Ports`) as an abstraction-only seam (no impl, engine, scoring, escalation, LLM, or cost type). A fail-closed reflection fitness test asserts zero concrete implementers across the kernel (`Contracts`/`Server`/`Projections`).
- **AC #3** — Kept the kernel dependency boundary green: `P0_KernelProjectsStayInfrastructureFree`, `DependencyDirectionTests`, and the banned-substring/kernel-purity guards all still pass with the new `Ports/`+`Resolvers/` folders. The new ports avoid the eight deferred-domain substrings in code and doc comments (deferred narrative lives only in `docs/`).
- **AC #4 / #5** — Authored `docs/boundary-decision-record.md`: per-module owns-vs-references for Parties, Conversations, EventStore, Tenants, Commons, PolymorphicSerializations; the "why" (each sibling is the system of record; FR-21 no-copy); and the four preserved deferred seams (AI-inferred expectations, executor routing/Theme 4, cost meter/Theme 5, trust-security/Theme 6) each explicitly tagged not-v1. A file-system fitness test makes the six modules + four seams falsifiable.
- **No new dependency** was added: no new `Hexalith.*` reference, no `Directory.Packages.props` change, no root-path variable. The `ArchitectureTests` project now references the three kernel projects (Contracts/Server/Projections) for reflection — this violates no dependency-direction rule.
- **Counter-metrics SM-C1/SM-C2 honored:** shipped named seams only; no routing engine, no lifecycle, no LLM/cost/security machinery, no kernel growth beyond the ports and the additive `Obligation.Reference`.
- **Test results (Release):** UnitTests 60/60, IntegrationTests 13/13, ArchitectureTests 25/25, PropertyTests 1/1 — 0 failures, 0 warnings. (The xUnit `[Theory]` cases in `ExpectationResolverTests` and the fifth `BoundaryPortTests` case were initially under-counted as 52/24; the executed totals are 60/25.)

### File List

**Added (source):**

- `src/Hexalith.Works.Contracts/Ports/ExpectationReference.cs`
- `src/Hexalith.Works.Contracts/Ports/Expectation.cs`
- `src/Hexalith.Works.Contracts/Ports/IExpectationResolver.cs`
- `src/Hexalith.Works.Contracts/Ports/IExecutorRouter.cs`
- `src/Hexalith.Works.Server/Resolvers/LiteralExpectationResolver.cs`

**Added (docs):**

- `docs/boundary-decision-record.md`

**Added (tests):**

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BoundaryPortTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BoundaryDecisionRecordTests.cs`
- `tests/Hexalith.Works.UnitTests/ExpectationResolverTests.cs`

**Modified:**

- `src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs` — added optional, reference-only `ExpectationReference? Reference = null`.
- `tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj` — added `ProjectReference`s to Contracts/Server/Projections for reflection-based fitness tests.
- `tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs` — added reference-carried serialization test + additive backward-compatible deserialization test.

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot (story-automator adversarial review) · **Date:** 2026-06-16 · **Outcome:** Approve (status → done)

### Scope & method

Read every file in the Dev Agent Record → File List and cross-referenced it against `git status`/`git diff`. The git-changed source set matches the story File List exactly; the only other dirty files are `_bmad-output/**` tracking artifacts (excluded from review per the workflow). Re-ran restore + Release build (warnings-as-errors) and executed the four test executables directly (Microsoft.Testing.Platform named-pipe sandbox pattern).

### Acceptance Criteria verdict

- **AC #1 — IMPLEMENTED.** `IExpectationResolver` is a domain-owned abstraction in `Contracts.Ports`; the no-LLM `LiteralExpectationResolver` ships in `Server`; the aggregate never resolves, and create/replay tests prove a work item is valid with the reference absent or present-but-uninterpreted.
- **AC #2 — IMPLEMENTED.** `IExecutorRouter` is abstraction-only; a fail-closed reflection test (with a vacuous-pass guard) asserts zero concrete implementers across `Contracts`/`Server`/`Projections`.
- **AC #3 — IMPLEMENTED.** Kernel stays infra-free: `P0_KernelProjectsStayInfrastructureFree`, `P0_WorkItemServerDependsOnlyOnContracts`, the banned-substring guard, and the kernel-purity guard all pass; the new `Ports/`+`Resolvers/` files trip none of the eight deferred-term substrings.
- **AC #4 — IMPLEMENTED.** `docs/boundary-decision-record.md` enumerates owns-vs-references for all six modules with a "why" section; a file-system fitness test asserts the six module names.
- **AC #5 — IMPLEMENTED.** All four deferred seams (expectations, routing, cost, security) are recorded and explicitly tagged not-v1; the decision-record test asserts the four markers. Cost/security are doc-only seams (no port), consistent with SM-C1/SM-C2 and the story's explicit scoping.

### Findings

- **[MEDIUM] Inaccurate recorded test counts (fixed).** Completion Notes and Change Log claimed UnitTests 52/52 and ArchitectureTests 24/24; the executed totals are 60/60 and 25/25 (xUnit counts each `[Theory]` `InlineData` case, and a fifth `BoundaryPortTests` case was added beyond Task 6's four). All green — reality exceeded the claim. Corrected both figures in this story file.
- **[LOW] `Expectation.InterpretedValue` is not trimmed** while `ExpectationReference.Value` is. Left unchanged by design: `Expectation` is interpreted-on-demand output (never persisted), so preserving the value verbatim is defensible; it already rejects null/whitespace-only via `ArgumentException.ThrowIfNullOrWhiteSpace`.
- **[INFO] Expectation-reference seam is reachable only by direct `Obligation` construction**, not through the `CreateWorkItem` command (`Handle` keeps the single-arg `new Obligation(command.Obligation)` call site). This is exactly what Task 2 mandated ("preserve the existing call site"); command-pipeline capture is deferred. No action.

No CRITICAL or HIGH findings. Tasks marked `[x]` were all verified against the implementation.

### Verification evidence

- `dotnet build Hexalith.Works.slnx -c Release` → Build succeeded, **0 Warning(s), 0 Error(s)**.
- UnitTests **60/60**, IntegrationTests **13/13**, ArchitectureTests **25/25**, PropertyTests **1/1** — 0 failures.

## Change Log

| Date | Change |
| --- | --- |
| 2026-06-16 | Implemented Story 1.4: added `IExpectationResolver`/`IExecutorRouter` ports + `Expectation`/`ExpectationReference` seam types, the no-LLM `LiteralExpectationResolver`, the optional reference-only `Obligation.Reference`, `docs/boundary-decision-record.md`, and boundary fitness/unit/integration tests. Build clean (0 warnings); UnitTests 60/60, IntegrationTests 13/13, ArchitectureTests 25/25, PropertyTests 1/1. Status → review. |
| 2026-06-16 | Adversarial code review (story-automator): all 5 ACs verified implemented; build re-run clean (0 warnings, 0 errors); UnitTests 60/60, IntegrationTests 13/13, ArchitectureTests 25/25, PropertyTests 1/1 all green. Corrected under-reported test counts (52→60, 24→25). 0 CRITICAL / 0 HIGH findings. Status → done. |
