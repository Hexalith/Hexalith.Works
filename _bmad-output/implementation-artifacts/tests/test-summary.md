# Test Automation Summary — Story 2.2 (Record Raw-Act Events and Replay State)

Workflow: `bmad-qa-generate-e2e-tests`. Role: QA automation engineer (test generation only — no code
review or story validation). Baseline before this run (dev-authored, green): **221** tests
(UnitTests 166, IntegrationTests 28, ArchitectureTests 26, PropertyTests 1).
Framework detected and reused: **xUnit v3 + Shouldly**, Tier-1 (no Dapr/Aspire/containers/network).
All tests auto-applied this run; all green.

## Generated Tests (gaps auto-applied this run)

### API / Contract & Serialization Tests

- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/*.v1.json` — **+7 frozen golden
  fixtures**, completing the RR-6 / NFR-12 back-compatibility corpus. The dev started the corpus with
  3 of the 10 durable success events (`WorkItemCreated`, `WorkItemAssigned`, `WorkItemCompleted`), yet
  the corpus README states *"Every event ever produced must remain deserializable forever."* The seven
  unfrozen events — including the two that carry distinguishing payload (`WorkItemClaimed` → executor
  binding, `WorkItemRejected` → the `Requeue` resting-status flag) — were not gated. Added frozen v1
  fixtures for `WorkItemQueued`, `WorkItemClaimed`, `WorkItemSuspended`, `WorkItemResumed`,
  `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`. **Generated from the production serializer**
  (a temporary emitter, run once then deleted — not hand-authored), so camelCase, enum-name casing, and
  property order are byte-accurate to the EventStore-persisted concrete form (no `$type`).
- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` —
  **+5 tests** wiring the new fixtures into the gate (now 10/10 durable success events):
  - `WorkItemClaimed_DeserializesFromFrozenBytesAndRoundTrips` — deserialize-from-frozen asserts every
    field incl. the binding (`partyId` / `channel=Mcp` / `authorityLevel=Coordinate`); re-serialize →
    deserialize round-trips to an equal record.
  - `WorkItemRejected_DeserializesFromFrozenBytesAndRoundTrips` — asserts the frozen `requeue: false`
    discriminator survives exactly (it steers replay to `Rejected` vs `Queued`).
  - `Base_shape_lifecycle_events_deserialize_from_frozen_bytes_and_round_trip` — the five
    `(AggregateId, Sequence, TenantId, WorkItemId)` events (`Queued`/`Suspended`/`Resumed`/`Cancelled`/
    `Expired`), each frozen independently so a future per-event field addition is gated by its own entry.
  - `WorkItemClaimed_ToleratesAdditiveUnknownField` / `WorkItemRejected_ToleratesAdditiveUnknownField` —
    inject an unknown `futureField` into the frozen bytes; the enriched events still deserialize (additive,
    no-`V2` tolerance). Vacuous-pass guard: `File.Exists(path)` inside `ReadGolden` reports a missing
    fixture as the root cause before any value assertion.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` — **+1 test** closing
  the AC #2 *"order-tolerant projections"* gap (previously only implicit — every existing replay test
  applied events in arrival order):
  - `Out_of_order_event_stream_replays_to_completed_when_resorted_by_sequence` — persists the six-event
    lifecycle stream through JSON, delivers it **out of order** (deterministic reverse — no RNG), then a
    projection recovers the canonical order purely from `Sequence` and replays into an independent
    `WorkItemState`, converging to `Completed` / `Sequence = 6` identical to the write side. Guards: the
    delivered sequences are asserted to be the contiguous, gap-free `1..6` and the stream count `== 6`
    before the order-tolerance claim is made.

### E2E Tests

- [x] Browser/UI E2E is **not applicable** to Story 2.2: the slice is pure `Contracts` + Tier-1 tests —
  serialization registration and the raw-act event catalog, with no UI, MCP, public route, or host
  surface (host/Dapr/Aspire wiring is deferred to Stories 4.5/4.6). The executable end-to-end path is
  **command → `WorkItemAggregate.Handle` → raw-act event → JSON transport shape → replayed
  `WorkItemState`**, exercised end-to-end by the contract-flow + golden-corpus tests above.

### Pre-existing coverage (dev-authored, verified green — not regenerated)

- [x] `WorkItemSerializationRegistrationTests` — AC #5: every one of the 23 v1 types resolves through the
  empty `Polymorphic` base, emits `$type` == type name (no version suffix), and round-trips to the
  concrete type. Two vacuous-pass guards (catalog count == 23; resolver reports ≥23 derived types).
- [x] `WorkItemRawActAdditivityTests` — AC #1/#2/#3 regression guard: concrete-type serialization emits
  **no** `$type` and **no** EventStore envelope fields, and a concrete `WorkItemCreated` still replays to
  `Created` (proves the polymorphic registration is purely additive).
- [x] `WorkItemCreateContractFlowTests` / `WorkItemLifecycleContractFlowTests` — create + full-lifecycle
  serialized write → persist → replay, reference-only payloads, the requeue flag steering replay, and
  rejection-only results (`WorkItemTransitionRejected`, cross-tenant-parent, missing-obligation) that
  carry context but no `sequence`.

## Coverage

Mapped against the Story 2.2 surface (10 success events + 10 commands + 3 rejection events; the
PolymorphicSerializations registration; the golden corpus):

| AC | What it requires | Status |
|----|------------------|--------|
| #1 | Accepted act → past-tense v1 event carrying verbatim replay values | Pre-existing (create/lifecycle flow + additivity); **reinforced** by 7 new frozen fixtures |
| #2 | Event carries `(AggregateId, Sequence)` for **order-tolerant** projections; no envelope spoofing | **Gap closed** — added out-of-order-resort-by-`Sequence` replay test; envelope-absence already guarded |
| #3 | In-order replay through `Apply` reconstructs state deterministically; no interpreted/AI/sibling data | Pre-existing (full-lifecycle serialized replay; reference-only payloads) |
| #4 | Rejection → `IRejectionEvent`; result never mixes success + rejection payloads | Pre-existing (per-path `IsRejection`/`IsSuccess` exclusivity; mixed-payload throw guarded in EventStore lib) |
| #5 | Catalog registered & resolvable by PolymorphicSerializations; golden corpus started, additive/no-`V2` | Pre-existing registration + **gap closed**: corpus completed to **10/10** durable success events |

**Durable-event corpus coverage: 10 / 10** success events frozen (was 3 / 10).
**Not corpus candidates (by design):** the 10 commands and 3 rejection events are not appended to the
event stream (commands are transient inputs; rejections are returned to the caller with no `Sequence`),
so they are not part of the persisted-bytes back-compat gate — they remain covered by the resolution and
contract-flow tests.

### Test counts (built Release, warnings-as-errors → 0 warnings / 0 errors)

| Suite | Before | After | Δ |
|-------|-------:|------:|--:|
| UnitTests | 166 | 166 | — |
| IntegrationTests | 28 | **34** | +6 |
| ArchitectureTests | 26 | 26 | — |
| PropertyTests | 1 | 1 | — |
| **Total** | **221** | **227** | **+6** |

## Validation

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  **0 warnings, 0 errors** (warnings-as-errors).
- Generated xUnit v3 executables run directly (Microsoft.Testing.Platform named-pipe is blocked in this
  sandbox, per the established pattern):
  - **UnitTests: 166/166** (unchanged)
  - **IntegrationTests: 34/34** (was 28/28 → **+6**)
  - **ArchitectureTests: 26/26** (unchanged — the `DependencyDirectionTests` update was dev-authored)
  - **PropertyTests: 1/1** (unchanged)
  - Total: **227/227**, 0 failures.

```bash
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build  Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests
```

## Checklist

- [x] API/contract tests generated (golden-corpus completion + order-tolerance replay).
- [x] E2E/UI tests marked not applicable (Story 2.2 is pure Contracts + Tier-1; no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (full out-of-order lifecycle replay to `Completed`; all 10 durable events deserialize from frozen bytes).
- [x] Tests cover critical error/edge cases (additive unknown-field tolerance on enriched events; out-of-order delivery recovered by `Sequence`).
- [x] All generated tests run successfully (227/227).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; the corpus reads copied-to-output fixtures only).
- [x] Tests are independent (no order dependency; each builds its own state; registration is an idempotent static ctor).
- [x] Test summary created with coverage metrics.

## Notes

- This run is **QA gap-filling only** — no production code was changed; only test files and frozen
  fixtures were added/modified.
- Two genuine gaps were discovered and auto-applied: (1) the back-compat golden corpus gated only 3 of
  the 10 durable success events despite its own "every event ever produced must remain deserializable"
  rule — completed to 10/10; (2) AC #2's *order-tolerant projections* claim was only implicit (all replay
  tests applied events in arrival order) — added a shuffle-then-resort-by-`Sequence` replay proof.
- Golden fixtures were generated from the production serializer (temporary emitter, deleted after the run)
  rather than hand-authored, matching the dev's established methodology so casing/ordering are exact. The
  existing `SchemaEvolution\Golden\**\*.json` `<None>` glob copies the new files to output — no csproj
  change was needed.
- The 10 commands and 3 rejection events are intentionally **not** in the persisted-bytes corpus
  (transient inputs / caller-returned, never stream-appended); they stay covered by the polymorphic
  resolution test and the contract-flow rejection tests.
