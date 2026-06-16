---
baseline_commit: fb757f2952ab2cd54e997d345df03918ad33d7d0
---

# Story 2.2: Record Raw-Act Events and Replay State

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want every accepted Work Item act to be recorded as a replayable raw-act event,
so that the Work Item history is durable, auditable, and independent of interpreted projections.

## Acceptance Criteria

1. **Given** a Work Item state change or progress fact is accepted
   **When** the domain result is produced
   **Then** it contains a past-tense domain event from the v1 catalog
   **And** the event stores the verbatim reported values required to replay the act.

2. **Given** a domain event is emitted
   **When** its payload is inspected
   **Then** it carries `AggregateId` and `Sequence` for order-tolerant projections
   **And** Works does not populate or spoof EventStore envelope metadata.

3. **Given** a sequence of Work Item events exists
   **When** the events are replayed in order through `Apply`
   **Then** the same Work Item state is reconstructed deterministically
   **And** no interpreted expectation, AI output, or sibling-module denormalization is required.

4. **Given** a command is rejected
   **When** the domain result is inspected
   **Then** the rejection is represented as an `IRejectionEvent`
   **And** the same domain result does not mix success payloads with rejection payloads.

5. **Given** serialization compatibility is required
   **When** the v1 event and command catalog is registered
   **Then** `Hexalith.PolymorphicSerializations` can resolve the payload types
   **And** a golden-payload corpus or equivalent contract test is started for additive, no-`V2` evolution.

## Tasks / Subtasks

- [x] **Task 1 — Wire `Hexalith.PolymorphicSerializations` into `Contracts` via ProjectReference (AC: #5)**
  - [x] Add a `$(HexalithPolymorphicSerializationsRoot)` root-path variable to `Directory.Build.props`, following the existing `$(HexalithEventStoreRoot)`/`$(HexalithTenantsRoot)` two-line probe pattern (local `Hexalith.PolymorphicSerializations\src\libraries\...` first, then `..\Hexalith.PolymorphicSerializations\...`). The submodule is already checked out at the repo root.
  - [x] In `src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj`, add **two ProjectReferences** (NOT `PackageReference` — CLAUDE.md and the `P0_HexalithDependenciesUseProjectReferencesNotPackageReferences` fitness test forbid any `Hexalith.*` PackageReference/PackageVersion; the README's `PackageReference` example does not apply here):
    - The library: `$(HexalithPolymorphicSerializationsRoot)\src\libraries\Hexalith.PolymorphicSerializations\Hexalith.PolymorphicSerializations.csproj`
    - The source generator **as an analyzer**: `$(HexalithPolymorphicSerializationsRoot)\src\libraries\Hexalith.PolymorphicSerializations.CodeGenerators\Hexalith.PolymorphicSerializations.CodeGenerators.csproj` with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`. This is the ProjectReference equivalent of the README's `PrivateAssets="all"`; the canonical in-repo example is `Hexalith.PolymorphicSerializations/test/Hexalith.PolymorphicSerializations.Tests/Hexalith.PolymorphicSerializations.Tests.csproj:18` (which omits `SetTargetFramework`). **If the analyzer ProjectReference raises a target-framework-mismatch warning** (a build error under warnings-as-errors — the generator targets `netstandard2.0`, Contracts targets `net10.0`), add `SetTargetFramework="TargetFramework=netstandard2.0"` to that reference (the `Hexalith.FrontComposer` SourceTools analyzer references use this form).
  - [x] Do NOT add `Hexalith.PolymorphicSerializations` to `Directory.Packages.props` (the fitness guard scans it too). Do NOT add it to `Server`/`Projections` — `Contracts` owns the catalog, and `Server`/`Projections` get the `Polymorphic` base + `PolymorphicHelper` transitively.

- [x] **Task 2 — Decorate the v1 event catalog for polymorphic registration (AC: #1, #5)**
  - [x] Add `[PolymorphicSerialization]` (namespace `Hexalith.PolymorphicSerializations`) and the `partial` keyword to each **success event** currently in `src/Hexalith.Works.Contracts/Events/`: `WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `WorkItemSuspended`, `WorkItemResumed`, `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired` (10 of the frozen 14 — the other 4, `ProgressReported`/`ReEstimated`/`WorkItemRescheduled`/`ChildSpawned`, are minted by later stories and registered then; see architecture.md:399 "register every new event/command").
  - [x] **Keep `: IEventPayload` exactly as-is and do NOT write `: Polymorphic` yourself.** The source generator emits a sibling `partial record X : Polymorphic {}` declaration; you only add the attribute + `partial`. A record may declare its base class in one partial and its interface in another — the combined type is `sealed`, derives from the (empty) `Polymorphic`, and implements `IEventPayload`. Keep the positional primary constructor on your (single) declaration. [Source: PolymorphicSerializations README §1; SerializationMapperSourceGenerator.cs:273-329]
  - [x] Discriminator defaults to the type name with no version suffix at v1 (`version < 2 ⇒ no "Vn"`): `WorkItemCreated` ⇒ `"WorkItemCreated"`. Do **not** pass an explicit name/version. NFR-12 forbids `V2` types — additive evolution = add nullable fields to the SAME record (same discriminator/`FullName`), never a new `…V2` type. [Source: PolymorphicSerializationAttribute.cs:42-43; epics.md#NFR-12]

- [x] **Task 3 — Decorate the v1 command catalog (AC: #5)**
  - [x] Add `[PolymorphicSerialization]` + `partial` to each command in `src/Hexalith.Works.Contracts/Commands/`: `CreateWorkItem`, `AssignWorkItem`, `QueueWorkItem`, `ClaimWorkItem`, `SuspendWorkItem`, `ResumeWorkItem`, `CompleteWorkItem`, `CancelWorkItem`, `RejectWorkItem`, `ExpireWorkItem` (AC #5 says "event **and command** catalog"). Commands implement no marker interface today (mirrors `Hexalith.Parties` commands); leave them otherwise unchanged.
  - [x] **Rejection events** (`WorkItemTransitionRejected`, `WorkItemCannotBeCreatedWithoutObligation`, `WorkItemCannotReferenceParentFromAnotherTenant`, all `IRejectionEvent`): also decorate them with `[PolymorphicSerialization]` + `partial`. They are part of the catalog returned in `DomainResult`; registering them keeps resolution uniform. They carry no `Sequence` and are not appended to the stream — that posture is unchanged.

- [x] **Task 4 — Confirm the generated registration entry point and expose a Works-level register call (AC: #5)**
  - [x] After Tasks 2–3 build, the generator emits `public static class HexalithWorksContractsSerialization` in namespace `Hexalith.Works.Contracts.Extensions` with `RegisterPolymorphicMappers()` (static resolver) and `AddHexalithWorksContractsPolymorphicMappers(this IServiceCollection)` (DI). **Confirm the exact generated names** in `src/Hexalith.Works.Contracts/obj/**/generated/**/SerializationMapperExtension.g.cs` — the class name is `{AssemblyMetadataName-without-dots}Serialization`, i.e. `Hexalith.Works.Contracts` ⇒ `HexalithWorksContractsSerialization`. [Source: SerializationMapperSourceGenerator.cs:110, 145, 200]
  - [x] Do not hand-write a registration class — it is generated. If a story-owned convenience seam is wanted, it is acceptable to add nothing and call the generated method directly from tests/host. (Architecture.md:465 mentions a `Server/Registration/` folder for DI extensions generally; the *polymorphic mapper* registration is generated in `Contracts` because that is where the attributed types live — do not relocate it.)

- [x] **Task 5 — Update the dependency-direction fitness test (AC: #5 — CRITICAL, build fails otherwise)**
  - [x] `DependencyDirectionTests.P0_SourceProjectReferencesFollowWorksArchitectureDirection` (`tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`) asserts `Contracts` references **exactly** `["Hexalith.EventStore.Contracts"]`. Adding the two PolymorphicSerializations ProjectReferences breaks this exact-match. Update the expected set to include `"Hexalith.PolymorphicSerializations"` and `"Hexalith.PolymorphicSerializations.CodeGenerators"` (the analyzer ref is still a `ProjectReference` element with no `Condition`, so `ProjectReferenceNames` counts it). Keep the `customMessage` accurate.
  - [x] Confirm `P0_ContractsDoesNotReferenceSiblingImplementationProjects` still passes — its forbidden list is Parties/Conversations/Tenants/EventStore client/server/aspire; PolymorphicSerializations is **not** forbidden. Confirm `P0_HexalithDependenciesUseProjectReferencesNotPackageReferences` still passes (it must — you used ProjectReference, added nothing to `Directory.Packages.props`).
  - [x] Confirm `ScaffoldGovernanceTests.P0_KernelProjectsStayInfrastructureFree` still passes — it string-matches kernel `.csproj` text for banned tokens (`Dapr*`, `ModelContextProtocol`, `Microsoft.AspNetCore.*`, `Swashbuckle`, `OpenAI`, `SemanticKernel`); none appear in the PolymorphicSerializations reference paths.

- [x] **Task 6 — Prove PolymorphicSerializations can resolve the catalog (AC: #5)**
  - [x] Add `tests/Hexalith.Works.IntegrationTests/WorkItemSerializationRegistrationTests.cs` (xUnit v3 + Shouldly). Call the generated `HexalithWorksContractsSerialization.RegisterPolymorphicMappers()` once (it is idempotent), then for each registered event/command assert PolymorphicSerializations resolves it: serialize through the base `JsonSerializer.Serialize<Polymorphic>(evt, PolymorphicHelper.DefaultJsonSerializerOptions)`, assert the JSON carries the `"$type"` discriminator equal to the type name, deserialize `JsonSerializer.Deserialize<Polymorphic>(json, PolymorphicHelper.DefaultJsonSerializerOptions)`, and assert the runtime type is the original concrete type with equal field values. [Source: PolymorphicSerializationTests.cs (`SerializeTestType1IncludesTypeDiscriminator`); PolymorphicHelper.cs:20-33]
  - [x] Add a vacuous-pass guard: assert the resolver/mapper count or the registered-type list is non-empty before asserting per-type resolution, so a no-op registration can't silently pass.

- [x] **Task 7 — Verify registration is purely additive — concrete persist/replay UNCHANGED (AC: #1, #2, #3 — regression guard)**
  - [x] EventStore persists/replays the **concrete** CLR type with plain `System.Text.Json` (`SerializeToUtf8Bytes(payload, payload.GetType())` keyed by `Type.FullName`; replay deserializes the concrete `Apply(...)` parameter type with `JsonSerializerDefaults.Web`, **no** polymorphic resolver). `Polymorphic` is an **empty** `[DataContract] record` (no members), so deriving from it must add **nothing** to concrete-type JSON. Prove it: keep/extend the existing `JsonSerializerDefaults.Web` round-trip tests and assert the concrete JSON still has **no `$type`** and no envelope fields (`messageId`/`causationId`/`correlationId`/`userId`/`metadata`/`cloudEvent`). [Source: EventPersister.cs:64-69; AggregateReplayer.cs:144; Polymorphic.cs:13-16]
  - [x] Re-run the full suite and confirm the 211 existing tests stay green (`WorkItemCreateContractFlowTests`, `WorkItemLifecycleContractFlowTests`, the unit matrix, fitness, property). If any existing concrete-shape assertion changes, STOP — that means registration was not additive and the approach must be reconsidered (see Dev Notes → Critical Decision).

- [x] **Task 8 — Start the golden-payload corpus for additive, no-`V2` evolution (AC: #5, RR-6, NFR-12)**
  - [x] Mirror the established sibling pattern in `Hexalith.Projects` (the only file-based corpus in the tree; closest EventStore-based sibling, same `JsonSerializerDefaults.Web` + xUnit v3 + Shouldly stack). Create `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/` and commit one frozen `*.v1.json` per **durable success event** (start with `WorkItemCreated.v1.json`; add the lifecycle events you choose to anchor — at minimum `WorkItemCreated` + one binding-carrying event like `WorkItemAssigned` + one terminal like `WorkItemCompleted`). Each file is the **concrete-type** `JsonSerializerDefaults.Web` serialized form (camelCase, **no `$type`** — this is the form EventStore actually persists and must keep deserializing forever per NFR-12). [Source: Hexalith.Projects/tests/Hexalith.Projects.Tests/SchemaEvolution/Golden/*.v1.json]
  - [x] Wire the fixtures into the test project: add `<None Include="SchemaEvolution\Golden\**\*.json" CopyToOutputDirectory="PreserveNewest" />` to `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj`. [Source: Hexalith.Projects.Tests.csproj:9-12]
  - [x] Add `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs`, modeled on `Hexalith.Projects/tests/Hexalith.Projects.Tests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs`: a private `ReadGolden` helper (`Path.Combine(AppContext.BaseDirectory, "SchemaEvolution", "Golden", file)` + `File.ReadAllText` + CRLF→LF normalize), and per-event `[Fact]`s asserting (a) deserialize-from-frozen reconstructs the expected field values, (b) re-serialize→deserialize round-trips to an equal record, (c) injecting an unknown `"futureField"` into the frozen JSON still deserializes (additive tolerance). Include a `File.Exists(path).ShouldBeTrue(path)` vacuous-pass guard.
  - [x] Add a header comment in the corpus folder (or a short `README`/`docs` note) stating: this corpus is the falsifiable back-compat gate (RR-6); every event ever produced must remain deserializable; new fields are additive/nullable; never mint a `…V2` type.

- [x] **Task 9 — Consolidate the raw-act/replay guarantees already in place (AC: #1, #2, #3, #4)**
  - [x] These ACs are largely **already satisfied** by Stories 1.2–2.1 (events are past-tense raw-act records carrying `(AggregateId, Sequence)`; `Apply` replays deterministically; rejections are `IRejectionEvent` and `DomainResult` cannot mix success+rejection — it throws at construction). Do NOT rebuild them. Verify coverage exists and, only where a gap is found, add a focused test. Specifically confirm: (a) a multi-event stream replays to identical state from its **serialized** form (covered by `WorkItemLifecycleContractFlowTests.Full_lifecycle_round_trips_through_serialization_to_completed`), and (b) no `Apply` reads an interpreted Expectation/AI output/sibling denormalization (AC #3 — covered by the create-flow tests asserting reference-only payloads).
  - [x] If you find any of the 10 success events lacks a serialized round-trip+replay assertion after the polymorphic change, add it to the existing contract-flow test class rather than creating a new one.

- [x] **Task 10 — Build and verify the slice (AC: #1-#5)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` (warnings-as-errors).
  - [x] Run the built xUnit v3 executables directly (Microsoft.Testing.Platform named-pipe is blocked in this sandbox): at minimum `UnitTests`, `IntegrationTests`, `ArchitectureTests`, `PropertyTests` from `tests/<proj>/bin/Release/net10.0/<proj>`.
  - [x] Confirm the previously-green suite stays green (baseline after 2.1: UnitTests 166, IntegrationTests 18, ArchitectureTests 26, PropertyTests 1 = **211**) plus the new registration, resolution, and golden-corpus tests. Do not use recursive submodule commands; do not modify sibling submodule files.

## Dev Notes

### Scope Boundary (read first — prevents over-build)

Story 2.2 owns **FR-7** (raw-act events are the durable, replayable history) and the **serialization contract** that makes the catalog evolvable: register the v1 event+command catalog with `Hexalith.PolymorphicSerializations` and start the **golden-payload corpus** (RR-6/NFR-12). Most of the raw-act/replay behavior already exists from Stories 1.2–2.1; the **net-new** work is the PolymorphicSerializations registration (AC #5) and the back-compat corpus. [Source: epics.md#Story 2.2; #FR-7; #NFR-12; architecture.md#RR-6 (line 118)]

**IN scope:** `[PolymorphicSerialization]` + `partial` on the 10 existing success events, 10 commands, and 3 rejection events; the `Hexalith.PolymorphicSerializations` (+CodeGenerators analyzer) **ProjectReference** and the `$(HexalithPolymorphicSerializationsRoot)` variable; the `DependencyDirectionTests` update; a resolution test (AC #5); an additivity/regression guard (concrete shape unchanged); the golden-payload corpus + round-trip/additive-tolerance contract tests.

**OUT of scope (defer — SM-C1/SM-C2 binding):**
- New events/commands `ProgressReported`, `ReEstimated`, `WorkItemRescheduled`, `ChildSpawned` and the `WorkItemClaimed`-vs-burn-down completion path → **Stories 2.3/2.4/3.2** (those stories decorate + corpus their own additions).
- Burn-down math / `Remaining=0 → Completed` → **Story 2.3** (`BurnDown`/`RollUp`/`Reminder` remain banned in `src` by `P0_WorkItemSliceDoesNotIntroduceDeferredBurnDownRollUpOrReminderBehavior` — your changes touch none of these terms).
- EventStore stream-append/replay wiring, the Dapr pub/sub envelope, `AddEventStoreDomainEvents(assembly)` host registration, Aspire topology → **Stories 4.5/4.6** (this story is pure Contracts+Tier-1 tests; no host, no Dapr, no containers).
- Rejection-event stream sequencing/persistence (rejections still carry no `Sequence`) → still deferred (see `deferred-work.md`); do not "fix" it here.

[Source: epics.md#Scope reminder (lines 23-30); #FR Coverage Map; 2-1...md#Scope Boundary]

### Current State (what exists after Story 2.1 — read before coding)

- The kernel is green at `fb757f2`: **UnitTests 166, IntegrationTests 18, ArchitectureTests 26, PropertyTests 1 = 211**, Release build 0 warnings / 0 errors (warnings-as-errors). [Source: 2-1...md#Senior Developer Review; tests/test-summary.md]
- `Contracts/Events/` holds 10 success events (`WorkItemCreated` + the 9 lifecycle events), all `public sealed record … : IEventPayload` with `(AggregateId, Sequence)` first (AR-4). `Contracts/Events/Rejections/` holds 3 `IRejectionEvent` records. `Contracts/Commands/` holds 10 commands (plain records, no marker). **None are `partial`; none carry `[PolymorphicSerialization]`; nothing is registered with PolymorphicSerializations** — that is precisely what 2.1 deferred to this story. [Source: src/Hexalith.Works.Contracts/**; 2-1...md#Scope Boundary "PolymorphicSerializations registration → Story 2.2"]
- `Contracts.csproj` references **only** `Hexalith.EventStore.Contracts`. Works has **zero** `Hexalith.*` PackageReferences anywhere. The `Hexalith.PolymorphicSerializations` submodule is checked out at root (`src/libraries/Hexalith.PolymorphicSerializations` + `.CodeGenerators`); there is **no** `$(HexalithPolymorphicSerializationsRoot)` variable yet. [Source: Directory.Build.props:4-7; Directory.Packages.props]
- Existing serialization tests use `new JsonSerializerOptions(JsonSerializerDefaults.Web)`, round-trip the **concrete** type, replay into a `WorkItemState`, and assert envelope fields are absent. They do **not** assert anything about `$type` (so an additive `$type`-capable registration won't break them). [Source: WorkItemCreateContractFlowTests.cs; WorkItemLifecycleContractFlowTests.cs]

### ⚠️ CRITICAL DECISION — PolymorphicSerializations registration: architecture mandate vs. ecosystem reality

There is a real, load-bearing tension here. Resolve it the way this story prescribes and **verify additivity** rather than discovering the conflict mid-implementation.

**What the planning artifacts mandate (authoritative — the story must satisfy these):**
- Architecture names `Hexalith.PolymorphicSerializations` as the serialization for event/command payloads **four times** (architecture.md:63, 193, 227, 340) and requires the golden corpus (RR-6, line 118).
- Epics **AC #5** literally requires: "the v1 event and command catalog **is registered**" ⇒ "`Hexalith.PolymorphicSerializations` **can resolve** the payload types."
- Stories 1.x/2.1 explicitly deferred "PolymorphicSerializations registration + golden corpus" **to this story**.

**What the codebase reality is (verified — do not be surprised by it):**
- **No** EventStore-based domain module decorates events with `[PolymorphicSerialization]` — confirmed zero hits in `Hexalith.Parties` (the donor), `Hexalith.Projects`, `Hexalith.Conversations`, `Hexalith.Tenants`. They use plain `IEventPayload` concrete-type records.
- **EventStore persists/replays the concrete CLR type** with plain `System.Text.Json`, keyed by `Type.FullName` — no `Polymorphic` base, no `$type`, no mapper registration in the persist/replay/pub-sub path. [Source: EventPersister.cs:64-69; AggregateReplayer.cs:144; EventStoreDomainEventsServiceCollectionExtensions.cs:84-89]
- `Hexalith.Projects`' golden corpus freezes the **concrete** (`$type`-free) form.

**Chosen path (this story): follow the architecture/epics — register the catalog — because it is both mandated AND safe.** The decisive fact that makes it safe: `Hexalith.PolymorphicSerializations.Polymorphic` is an **empty** `[DataContract] record { }`. Decorating a record with `[PolymorphicSerialization]` makes the generator add a `: Polymorphic` partial + a `{Type}Mapper`, but contributes **zero** members to concrete-type serialization. Therefore:
- Concrete `JsonSerializer.Serialize(evt, evt.GetType())` (what EventStore does) is **byte-identical** before/after — EventStore append/replay and the 211 existing tests are unaffected (Task 7 proves this; if a concrete-shape assertion changes, the additivity assumption is wrong — STOP and escalate).
- `JsonSerializer.Serialize<Polymorphic>(evt, PolymorphicHelper.DefaultJsonSerializerOptions)` now resolves the type and emits `$type` — satisfying AC #5's "can resolve the payload types" (Task 6).
- The **golden corpus freezes the concrete form** (the bytes EventStore actually persists and must keep deserializing forever — NFR-12), which is exactly the `Hexalith.Projects` pattern.

Net: registration is the *capability* AC #5 asks for; concrete-type serialization remains the *transport* EventStore uses. They coexist precisely because the base is empty. The reviewer's Acceptance Auditor will check AC #5 against the literal text — registering the catalog satisfies it; declaring "equivalent contract test" and skipping registration would contradict the architecture and is **not** the chosen path.

### Critical fitness guards your changes interact with

1. **`DependencyDirectionTests.P0_SourceProjectReferencesFollowWorksArchitectureDirection` WILL fail unless updated (Task 5).** It asserts `Contracts` references exactly `["Hexalith.EventStore.Contracts"]`. This is the single most likely build-breaker — the analog of Story 2.1's deferred-term guard update. Add the two new refs to the expected set. [Source: DependencyDirectionTests.cs:9-20]
2. **`P0_HexalithDependenciesUseProjectReferencesNotPackageReferences` (must stay green).** Bans any `Hexalith.*` PackageReference/PackageVersion across all `Hexalith.Works*.csproj` **and** `Directory.Packages.props`. ⇒ Use ProjectReference; add nothing to `Directory.Packages.props`. [Source: DependencyDirectionTests.cs:104-134]
3. **`P0_ScaffoldUsesSlnxAndCentralPackageManagement` (must stay green).** Forbids inline `Version=` on `PackageReference`. ProjectReferences carry no version, so this stays green. [Source: ScaffoldGovernanceTests.cs:77-101]
4. **`P0_KernelProjectsStayInfrastructureFree` (must stay green).** String-matches kernel `.csproj` text for banned tokens; PolymorphicSerializations paths contain none. [Source: ScaffoldGovernanceTests.cs:118-152]
5. **`P0_WorkItemServerDependsOnlyOnContracts` + `P0_WorkItemKernelRemainsPure` (unchanged, must stay green).** You add references only to `Contracts`, not `Server`; you read no clock/RNG/I/O. No change needed. [Source: ScaffoldGovernanceTests.cs:194-240]
6. **`P0_WorkItemSliceDoesNotIntroduceDeferredBurnDownRollUpOrReminderBehavior` (must stay green).** Still bans `BurnDown`/`Burndown`/`RollUp`/`Reminder` in `src/**/*.cs` (comments included). Your changes introduce none of these terms — keep it that way (don't name a corpus file or doc note with a banned term inside `src`; corpus lives under `tests/`, never scanned). [Source: ScaffoldGovernanceTests.cs:166-192; works-scaffold-facts memory]

### PolymorphicSerializations mechanics (exact — copy this)

- **Decorate:** `[PolymorphicSerialization]` (default discriminator = type name, version 1 ⇒ no suffix) + `partial`. Keep your `: IEventPayload`; the generator adds `: Polymorphic`. Do not pass name/version at v1.
- **Generated entry point** (assembly `Hexalith.Works.Contracts`): `namespace Hexalith.Works.Contracts.Extensions; public static class HexalithWorksContractsSerialization { void RegisterPolymorphicMappers(); IServiceCollection AddHexalithWorksContractsPolymorphicMappers(this IServiceCollection); }`. (`project = AssemblyMetadataName.Replace(".","")`.) Confirm the exact name in `obj/**/generated/**/SerializationMapperExtension.g.cs`. [Source: SerializationMapperSourceGenerator.cs:110,145,200]
- **Serialize/deserialize through the base** to exercise resolution: `JsonSerializer.Serialize<Polymorphic>(evt, PolymorphicHelper.DefaultJsonSerializerOptions)` / `Deserialize<Polymorphic>(json, …)`. Discriminator property is `"$type"`. [Source: PolymorphicHelper.cs:20-33]
- **csproj wiring (ProjectReference, analyzer):**
  ```xml
  <ProjectReference Include="$(HexalithPolymorphicSerializationsRoot)\src\libraries\Hexalith.PolymorphicSerializations\Hexalith.PolymorphicSerializations.csproj" />
  <ProjectReference Include="$(HexalithPolymorphicSerializationsRoot)\src\libraries\Hexalith.PolymorphicSerializations.CodeGenerators\Hexalith.PolymorphicSerializations.CodeGenerators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  ```
  [Source: Hexalith.PolymorphicSerializations/test/Hexalith.PolymorphicSerializations.Tests/Hexalith.PolymorphicSerializations.Tests.csproj:18]
- **Version note (informational):** `Hexalith.Builds/Props/Directory.Packages.props` pins these at `1.11.0`; the submodule is checked out at `v1.12.0`. Moot for Works — you reference the checked-out **source**, not a package. [works-scaffold-facts memory: "Builds props are stale"]

### Golden-payload corpus pattern (mirror `Hexalith.Projects`)

- Reference harness: `Hexalith.Projects/tests/Hexalith.Projects.Tests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` — `private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);`, `GoldenDirectory = Path.Combine(AppContext.BaseDirectory, "SchemaEvolution", "Golden")`, a 3-line `ReadGolden` (with `File.Exists(path).ShouldBeTrue(path)` + CRLF→LF normalize), and per-event facts: deserialize-from-frozen → assert fields; re-serialize→deserialize `.ShouldBe(deserialized)`; inject `"futureField"` → still deserializes.
- Fixtures: `Hexalith.Projects/.../SchemaEvolution/Golden/*.v1.json` are flat camelCase objects with **no `$type`**. Mirror that — the corpus pins the **EventStore-persisted concrete form** (the bytes that must remain deserializable per NFR-12), not the `$type` polymorphic form.
- csproj: `<None Include="SchemaEvolution\Golden\**\*.json" CopyToOutputDirectory="PreserveNewest" />`.
- No shared test helper exists in the ecosystem for this — `ReadGolden` is a private local method copied per project. Inline your own (3 lines). [Source: golden-corpus investigation; Hexalith.Projects.Tests.csproj:9-12]

### Decisions (do not re-litigate)

- **Register, don't restyle.** Add the attribute + `partial`; the generator owns the base/mapper/registration class. Don't hand-author a registration class, don't write `: Polymorphic`, don't introduce a `Polymorphic`-rooted serializer into the EventStore persist/replay path. [Source: README; SerializationMapperSourceGenerator.cs]
- **ProjectReference, never PackageReference**, for `Hexalith.PolymorphicSerializations` — CLAUDE.md + fitness gate. Add the root-path variable; don't touch `Directory.Packages.props`. [Source: CLAUDE.md#Hexalith library references; DependencyDirectionTests.cs:104-134]
- **No `V2` types.** Additive evolution = nullable fields on the same record (same discriminator + `FullName`). The corpus is the falsifiable proof. [Source: epics.md#NFR-12; architecture.md:64,399]
- **Corpus freezes the concrete `JsonSerializerDefaults.Web` form**, matching `Hexalith.Projects` and the EventStore-persisted bytes. [Source: Projects corpus; EventPersister.cs:64-69]
- **AC #1–#4 are mostly pre-satisfied** by 1.2–2.1 (raw-act `(AggregateId, Sequence)` events; deterministic `Apply`; `IRejectionEvent`; `DomainResult` rejects mixed success+rejection at construction). Verify, don't rebuild. [Source: 2-1...md; DomainResult.cs:63-95]

### File Structure (target locations — match the architecture tree)

- Decorate in place: `src/Hexalith.Works.Contracts/Events/*.cs` (10 success), `Events/Rejections/*.cs` (3), `Commands/*.cs` (10) — add attribute + `partial` only.
- `Directory.Build.props` — add `$(HexalithPolymorphicSerializationsRoot)`. `src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj` — add the two ProjectReferences.
- Tests: `tests/Hexalith.Works.IntegrationTests/WorkItemSerializationRegistrationTests.cs` (AC #5 resolution); `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` + `SchemaEvolution/Golden/*.v1.json`; extend `WorkItemCreateContractFlowTests`/`WorkItemLifecycleContractFlowTests` only if Task 7/9 find a gap.
- Fitness update: `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`.
- [Source: architecture.md#Complete Project Directory Structure (lines 429-497); AR-22]

### Testing Standards

- xUnit **v3** + Shouldly. No raw `Assert.*`, Moq, or FluentAssertions. Keep Tier-1 tests pure (no Dapr/Aspire/containers/network); the golden-corpus test reads copied-to-output files only (file read of test fixtures is allowed at the integration tier, mirroring `Hexalith.Projects`). [Source: 2-1...md#Testing Standards; AR-21]
- Every new reflection/file-system/registration test gets a **vacuous-pass guard** (assert content discovered before asserting completeness) — the Blind Hunter / Edge Case Hunter / Acceptance Auditor review will hunt for these. [Source: 2-1...md#Previous Story Intelligence]
- For resolution tests use `PolymorphicHelper.DefaultJsonSerializerOptions` and the `Polymorphic` base type; for concrete/corpus tests use `new JsonSerializerOptions(JsonSerializerDefaults.Web)` like the existing contract-flow tests. Do not conflate the two option sets.

### Build / test execution (sandbox reality)

```bash
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build  Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal
# dotnet test is blocked by Microsoft.Testing.Platform named-pipe perms — run the built xUnit v3 executables directly:
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests
```
[Source: tests/test-summary.md]

### Previous Story Intelligence

- **Source-generator-as-analyzer is novel for this repo's `src/`** — Works has never referenced a Roslyn generator before. Expect the first build after Task 2 to either generate the `…Serialization` class or report `partial`-keyword/`CS` errors if a record was missed. Build incrementally (decorate one event, build, inspect `obj/**/generated`) before decorating all 23 types. [Source: README §Troubleshooting; SerializationMapperSourceGenerator.cs]
- **Story 2.1 pattern for fitness-guard updates:** when a story legitimately introduces something a `P0_*` guard forbids, the same PR updates the guard (deferred-term list then; the dependency-direction expected-set now) and renames/justifies the message. Mirror that discipline for `DependencyDirectionTests`. [Source: 2-1...md#Task 5; #Senior Developer Review]
- **Adversarial review is expected and historically lands ≥1 patch/story** (Blind Hunter + Edge Case Hunter + Acceptance Auditor). Pre-empt it: vacuous-pass guards on the registration/corpus tests; prove additivity (Task 7) explicitly so a reviewer can't claim the polymorphic change silently altered the persisted shape; keep the File List and test counts reconciled (2.1 took two MEDIUM findings for a stale File List + stale counts). [Source: 2-1...md#Findings]
- **Validate-at-the-writer, trust-on-replay** posture is established — don't add defensive re-validation in `Apply`. This story changes serialization, not handling. [Source: 2-1...md#Decisions; WorkItemState.cs]

### Git Intelligence

- `fb757f2 feat(story-2.1)` (baseline) — added the 9 lifecycle commands/events + `WorkItemTransitionRejected`, the pure `WorkItemLifecycle` table, `WorkItemState.Sequence`, `docs/lifecycle-transition-matrix.md`, and the serialization-boundary contract-flow tests (concrete `System.Text.Json`, no registration). These are the exact records you now decorate.
- `6ea70b7 feat(story-1.4)` — `Ports/` seam + `docs/boundary-decision-record.md` + boundary fitness tests; `b0687e2 feat(story-1.3)` — `DependencyDirectionTests` (the guard you must update). Build on these; don't fork a parallel harness.

### Project Structure Notes

- Works holds domain code only; serialization registration of the domain catalog is a Contracts concern and stays in `Contracts`. No new technical layer; the only new dependency is a domain serialization library referenced by source. [Source: CLAUDE.md#Repository responsibility]
- Hexalith libraries are `ProjectReference` via `$(Hexalith<Module>Root)`; this story adds the `$(HexalithPolymorphicSerializationsRoot)` variable and references the **root** submodule projects (never `--recursive`, never nested submodules). [Source: CLAUDE.md#Hexalith library references; #Submodule rules]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.2: Record Raw-Act Events and Replay State] — story statement + AC #1–#5.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-7] — raw-act past-tense events; frozen 14-event catalog; the ordered stream *is* the history.
- [Source: _bmad-output/planning-artifacts/epics.md#NFR-2; #NFR-8; #NFR-12] — payloads-only/EventStore-owns-envelope; raw-act audit model; additive no-`V2` serialization + golden corpus.
- [Source: _bmad-output/planning-artifacts/epics.md#AR-4] — every event carries `(AggregateId, Sequence)`.
- [Source: _bmad-output/planning-artifacts/architecture.md (lines 63, 193, 227, 340)] — PolymorphicSerializations for every event/command; System.Text.Json; additive tolerant evolution; start the golden corpus.
- [Source: _bmad-output/planning-artifacts/architecture.md#RR-6 (line 118); (lines 64, 399)] — golden-payload corpus + round-trip contract test; back-compat unfalsifiable without it; register every new event/command, evolve additively.
- [Source: _bmad-output/planning-artifacts/architecture.md (lines 44, 335, 549)] — Works returns payloads only; EventStore owns envelope metadata; raw-act event is the source of truth.
- [Source: Hexalith.PolymorphicSerializations/README.md §§1-2; src/libraries/.../PolymorphicSerializationAttribute.cs:15-16,42-43; Polymorphic.cs:13-16; PolymorphicHelper.cs:20-33; PolymorphicSerializationResolver.cs:28-52] — attribute + generator mechanics; empty `Polymorphic` base; `$type`; default options; resolver API.
- [Source: Hexalith.PolymorphicSerializations/src/libraries/Hexalith.PolymorphicSerializations.CodeGenerators/SerializationMapperSourceGenerator.cs:110,145,200,273-329] — generated `{project}Serialization` class names + per-type `{Type}Mapper` + injected `: Polymorphic` partial.
- [Source: Hexalith.PolymorphicSerializations/test/Hexalith.PolymorphicSerializations.Tests/Hexalith.PolymorphicSerializations.Tests.csproj:18; PolymorphicSerializationTests.cs] — analyzer-via-ProjectReference wiring; `$type` discriminator resolution test to mirror.
- [Source: Hexalith.Projects/tests/Hexalith.Projects.Tests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs; Golden/*.v1.json; Hexalith.Projects.Tests.csproj:9-12] — golden-corpus harness, fixtures, and `<None Include … CopyToOutputDirectory>` wiring to copy verbatim.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Events/IEventPayload.cs:7; .../Hexalith.EventStore.Client/Persistence/EventPersister.cs:64-69; .../Aggregates/AggregateReplayer.cs:144] — concrete-type `Type.FullName` persist/replay (proves polymorphic registration must be additive, not the transport).
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:9-20,104-134; ScaffoldGovernanceTests.cs:118-192] — the dependency-direction guard to UPDATE + the guards to keep green.
- [Source: src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs; .../Commands/CreateWorkItem.cs; State/WorkItemState.cs; src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs] — the records to decorate + the `Handle`/`Apply` raw-act/replay loop AC #1–#3 already satisfy.
- [Source: tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs; WorkItemLifecycleContractFlowTests.cs] — concrete round-trip + envelope-absence style to extend; the existing serialized full-lifecycle replay proof.
- [Source: _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md] — previous-story handler/state pattern, fitness-guard-update discipline, review expectations.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — rejection-event sequencing remains deferred; do not action here.
- [Source: _bmad-output/implementation-artifacts/tests/test-summary.md] — 211-test baseline + sandbox build/test commands.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8[1m] (Claude Opus 4.8, 1M context)

### Debug Log References

- **Submodule build clash (root cause + fix).** Adding the `Hexalith.PolymorphicSerializations`
  ProjectReferences first failed the build with `IDE0065` (using directives inside namespace) treated as
  errors. The submodule's `.editorconfig` is `root = false`, so the Works root `.editorconfig`
  (`csharp_using_directive_placement = outside_namespace:warning`) leaks into the submodule, and the
  submodule's own `Hexalith.Build.props` makes warnings errors. EventStore avoids this only because its
  source uses outside-namespace usings. Fixed two ways (belt-and-suspenders, no submodule files touched):
  (1) `AdditionalProperties="TreatWarningsAsErrors=false"` on both PolymorphicSerializations
  ProjectReferences (mirrors the existing `NuGetAudit=false` pattern on the EventStore ref), and
  (2) a scoped `[Hexalith.PolymorphicSerializations/**.cs] dotnet_diagnostic.IDE0065.severity = none`
  section in the Works `.editorconfig` so the build output stays at 0 warnings.
- **Generated entry point confirmed** by emitting `obj/**/generated/**/SerializationMapperExtension.g.cs`:
  `Hexalith.Works.Contracts.Extensions.HexalithWorksContractsSerialization` with
  `RegisterPolymorphicMappers()` + `AddHexalithWorksContractsPolymorphicMappers(IServiceCollection)`;
  23 per-type `{Type}Mapper`s generated, each discriminator == type name (no version suffix at v1).
- **Envelope substring false-positive.** The additivity guard initially used substring containment and
  tripped on `correlationId` ⊂ `conversationCorrelationId`. Switched to top-level JSON property absence
  (matching the existing create-flow tests).
- **Golden corpus generated from the production serializer** (temporary emitter, then deleted) rather than
  hand-authored — this captured the computed `WorkItemEffort.Remaining` field and `DateOnly` format
  exactly, which a hand-authored file would have gotten wrong.

### Completion Notes List

Story 2.2 net-new work = register the v1 catalog with `Hexalith.PolymorphicSerializations` (AC #5,
additive) + start the back-compat golden corpus (RR-6/NFR-12). AC #1–#4 were verified as already
satisfied by Stories 1.2–2.1 and not rebuilt.

- **Catalog registered (AC #5):** `[PolymorphicSerialization]` + `partial` added to all 23 v1 types
  (10 success events, 10 commands, 3 rejection events). Kept `: IEventPayload`/`: IRejectionEvent` and the
  positional ctor; the generator emits the `: Polymorphic` partial + `{Type}Mapper` + the
  `HexalithWorksContractsSerialization` registration class. No registration class hand-written.
- **ProjectReference, never PackageReference:** added `$(HexalithPolymorphicSerializationsRoot)` to
  `Directory.Build.props` and the library + code-generator-as-analyzer ProjectReferences to
  `Contracts.csproj`; nothing added to `Directory.Packages.props`.
- **Fitness guard updated (build-breaker, Task 5):** `DependencyDirectionTests`
  `P0_SourceProjectReferencesFollowWorksArchitectureDirection` expected set for Contracts now
  `["Hexalith.EventStore.Contracts","Hexalith.PolymorphicSerializations","Hexalith.PolymorphicSerializations.CodeGenerators"]`.
  All other P0 guards stay green (Project/Package guard, kernel-purity, deferred-term, sibling-isolation).
- **Resolution proven (AC #5):** `WorkItemSerializationRegistrationTests` calls the generated
  `RegisterPolymorphicMappers()` and asserts every type resolves through the empty `Polymorphic` base
  with `$type` == type name and round-trips to the concrete type. Two vacuous-pass guards: catalog count
  == 23, and the static resolver reports ≥23 registered derived types.
- **Additivity proven (regression guard, AC #1/#2/#3):** `WorkItemRawActAdditivityTests` proves the
  empty base contributes nothing to concrete (EventStore-transport) serialization — no `$type`, no
  envelope fields (`messageId`/`causationId`/`correlationId`/`userId`/`metadata`/`cloudEvent`) — and a
  concrete `WorkItemCreated` still replays to `Created`. No pre-existing concrete-shape assertion changed.
- **Golden corpus started (RR-6/NFR-12):** frozen concrete-form fixtures for **all 10 durable success
  events** — `WorkItemCreated.v1.json` (rich payload), `WorkItemAssigned.v1.json`/`WorkItemClaimed.v1.json`
  (binding-carrying), `WorkItemRejected.v1.json` (`Requeue` resting-status flag), `WorkItemCompleted.v1.json`
  (terminal), plus `WorkItemQueued`/`WorkItemSuspended`/`WorkItemResumed`/`WorkItemCancelled`/`WorkItemExpired`
  (base shape) — + `SchemaEvolutionGoldenCorpusTests` (deserialize-from-frozen, round-trip, unknown-field
  tolerance, with a `File.Exists` guard) + a corpus `README.md` stating the additive/no-`V2` rule. (The dev
  pass anchored 3 of the 10; the follow-on QA-automation pass completed the corpus to 10/10 and added the
  AC #2 order-tolerant-replay test — see Senior Developer Review below.)
- **AC #1–#4 verified, not rebuilt:** full-lifecycle serialized write→persist→replay
  (`WorkItemLifecycleContractFlowTests`), reference-only payloads + `IsRejection`/`IRejectionEvent`
  (`WorkItemCreateContractFlowTests`), and `DomainResult`'s mixed-payload throw guard (EventStore lib).

**Validation:** Release build `Hexalith.Works.slnx` (warnings-as-errors) → 0 warnings / 0 errors.
Test executables: **UnitTests 166, IntegrationTests 34 (18 baseline + 16 new), ArchitectureTests 26,
PropertyTests 1 = 227** (211 baseline + 16 new), all green. (The dev pass landed at 221; the follow-on
QA-automation pass added +6 integration tests — 7 extra golden fixtures with 5 corpus tests + 1
order-tolerance replay test — bringing the verified total to 227. Re-verified at review.)

### File List

**Modified**
- `.editorconfig` — scoped `IDE0065` suppression for the vendored PolymorphicSerializations submodule.
- `Directory.Build.props` — added `$(HexalithPolymorphicSerializationsRoot)` probe pair.
- `src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj` — added the library + analyzer
  ProjectReferences (with `TreatWarningsAsErrors=false` AdditionalProperties).
- `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`, `WorkItemAssigned.cs`, `WorkItemQueued.cs`,
  `WorkItemClaimed.cs`, `WorkItemSuspended.cs`, `WorkItemResumed.cs`, `WorkItemCompleted.cs`,
  `WorkItemCancelled.cs`, `WorkItemRejected.cs`, `WorkItemExpired.cs` — `[PolymorphicSerialization]` + `partial`.
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs`,
  `WorkItemCannotBeCreatedWithoutObligation.cs`, `WorkItemCannotReferenceParentFromAnotherTenant.cs` —
  `[PolymorphicSerialization]` + `partial`.
- `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs`, `AssignWorkItem.cs`, `QueueWorkItem.cs`,
  `ClaimWorkItem.cs`, `SuspendWorkItem.cs`, `ResumeWorkItem.cs`, `CompleteWorkItem.cs`, `CancelWorkItem.cs`,
  `RejectWorkItem.cs`, `ExpireWorkItem.cs` — `[PolymorphicSerialization]` + `partial`.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` — updated Contracts
  expected reference set + message.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` — `<None Include>` to copy
  the golden corpus to output.
- `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` — added the AC #2
  order-tolerant replay test (`Out_of_order_event_stream_replays_to_completed_when_resorted_by_sequence`)
  during the QA-automation pass.

**Added**
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` — shared 23-type v1 sample catalog.
- `tests/Hexalith.Works.IntegrationTests/WorkItemSerializationRegistrationTests.cs` — AC #5 resolution test.
- `tests/Hexalith.Works.IntegrationTests/WorkItemRawActAdditivityTests.cs` — concrete-shape additivity guard.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` — corpus tests.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/*.v1.json` — frozen v1 payloads for all 10
  durable success events: `WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`,
  `WorkItemSuspended`, `WorkItemResumed`, `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`,
  `WorkItemExpired` (the dev pass anchored 3; the QA-automation pass froze the remaining 7).
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/README.md` — corpus back-compat rules.

## Change Log

| Date       | Version | Description                                                                                     | Author |
|------------|---------|-------------------------------------------------------------------------------------------------|--------|
| 2026-06-16 | 0.1     | Story 2.2 implemented: registered the v1 event/command/rejection catalog with PolymorphicSerializations (additive), updated the dependency-direction fitness guard, and started the golden-payload corpus. Build 0/0; tests 221 green. Status → review. | Amelia (Dev Agent, claude-opus-4-8[1m]) |
| 2026-06-16 | 0.2     | Adversarial code review (auto-fix): verified build 0/0 and 227/227 tests green. Reconciled the stale File List + test counts with the verified working tree (added the modified lifecycle test; golden corpus 3 → 10 files; integration 28 → 34, total 221 → 227, 16 new). No code changes required. Status → done. | Senior Review (AI, claude-opus-4-8[1m]) |

## Senior Developer Review (AI)

**Reviewer:** Administrator · **Date:** 2026-06-16 · **Outcome:** ✅ Approve (auto-fix applied) · **Mode:** adversarial, non-interactive (auto-fix all issues)

### Verification performed

- **Build:** `dotnet build Hexalith.Works.slnx -c Release` (warnings-as-errors) → **0 warnings / 0 errors**.
- **Tests (built xUnit v3 executables, run directly):** UnitTests **166/166**, IntegrationTests **34/34**,
  ArchitectureTests **26/26**, PropertyTests **1/1** → **227/227, 0 failures**.
- **Catalog decoration:** all **23** v1 types (10 success events + 10 commands + 3 rejection events) carry
  `[PolymorphicSerialization]` + `partial`; none missing the `partial` keyword; `: IEventPayload` /
  `: IRejectionEvent` preserved.
- **ProjectReference gate:** zero `Hexalith.*` PackageReferences anywhere; `$(HexalithPolymorphicSerializationsRoot)`
  probe pair added; library + code-generator-as-analyzer referenced by source. `Directory.Packages.props` untouched.
- **Fitness guard:** `DependencyDirectionTests` Contracts expected-set correctly extended to the 3 references;
  all P0 guards green.
- **AC coverage (validated against implementation + tests):** AC #1 ✔ (past-tense events carry verbatim values),
  AC #2 ✔ (`(AggregateId, Sequence)` present; envelope-absence guarded; order-tolerant replay test added),
  AC #3 ✔ (deterministic serialized replay), AC #4 ✔ (`IRejectionEvent`; `DomainResult` mixed-payload throw),
  AC #5 ✔ (catalog resolves through `Polymorphic` base; golden corpus 10/10 durable success events).
- **Task audit:** all 10 `[x]` tasks verified genuinely complete against the working tree.

### Findings

| # | Severity | Finding | Resolution |
|---|----------|---------|------------|
| 1 | MEDIUM | File List omitted the modified `WorkItemLifecycleContractFlowTests.cs` (+1 order-tolerance test added by the QA-automation pass). | Fixed — added to File List → Modified. |
| 2 | MEDIUM | File List listed only 3 golden `*.v1.json` fixtures; the working tree has 10 (QA pass froze the remaining 7). | Fixed — File List + Completion Notes updated to 10/10. |
| 3 | MEDIUM | Stale test counts: story claimed IntegrationTests 28 / total 221 / "10 new"; verified reality is 34 / 227 / 16 new. | Fixed — Completion Notes, Validation, and Change Log corrected to the re-verified numbers. |

**No CRITICAL or HIGH findings.** All three findings are documentation drift: the story file was not reconciled
after the follow-on QA-automation pass extended the working tree. The implementation itself is correct, complete,
and green — no production-code or test changes were required by this review. Root cause matches the Story 2.1
review pattern (stale File List + stale counts after a later pass); same discipline applied here.
