# Test Automation Summary — Story 2.1 (Define the Lifecycle State Machine)

## Generated Tests

### API / Contract Tests (gaps auto-applied this run)
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` — **+5 tests** filling the discovered serialization-boundary gap. The dev-authored unit suite exercises every `(status, command)` cell in-memory (`Handle`/`Apply`), but **no lifecycle event crossed the JSON serialization boundary** that event-sourcing depends on (NFR-2). These tests drive accepted commands → emitted event → `System.Text.Json` round-trip → replay into an independent `WorkItemState`:
  - `Full_lifecycle_round_trips_through_serialization_to_completed` — the canonical happy path `Created → Assigned → InProgress(Claim) → Suspended → InProgress(Resume) → Completed`, six events; the replay state (rebuilt only from round-tripped JSON) converges to `Completed` with a monotonic, gap-free `Sequence = 6` identical to the write-side state.
  - `Created_branch_events_round_trip_and_replay_to_their_target_status` — `WorkItemQueued`, `WorkItemCancelled`, `WorkItemExpired` (the success events off the happy path) each survive serialization and replay to `Queued` / `Cancelled` / `Expired`, so **all nine success events** cross the boundary.
  - `Reject_event_round_trips_and_the_requeue_flag_drives_the_resting_status` — AC #5 across the boundary: the `Requeue` flag on `WorkItemRejected` round-trips and still steers replay to `Queued` (requeue) vs terminal `Rejected` (non-requeue).
  - `Illegal_transition_serializes_a_transition_rejection_only` — an illegal `Claim` from `Created` yields a rejection-only result; `WorkItemTransitionRejected` round-trips carrying `FromStatus` + `AttemptedAct` + tenant/work-item, and serializes **no `sequence`** (rejections are returned to the caller, never appended to the stream).
  - `Assigned_event_round_trips_with_minimal_envelope_free_binding_payload` — AR-4 contract guard: the `ExecutorBinding` is the only enriched field, `(aggregateId, sequence)` lead the serialized shape, and no transport envelope leaks.

### E2E Tests
- [x] Browser/UI E2E is **not applicable** for Story 2.1: the slice is a pure event-sourced domain state machine with no UI, MCP, public route, or command-pipeline host surface (the lifecycle host/Aspire proof is deferred to Stories 4.5/4.6). The executable end-to-end path here is **command → `WorkItemAggregate.Handle` → event → JSON transport shape → replayed `WorkItemState`**, exercised end-to-end by the contract-flow tests above.

### Pre-existing coverage (dev-authored, verified green — not regenerated)
- [x] `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs` — exhaustive matrix: every `(status, command)` cell across the 9 statuses (Accept/Reject/NoOp), the flag-dependent `Reject` column for both requeue values, uncreated-state rejection, AC #1–#5 scenarios, and sequence monotonicity (incl. "a rejection does not advance the sequence").
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/LifecycleTransitionMatrixDocTests.cs` — vacuous-pass-guarded fitness test asserting `docs/lifecycle-transition-matrix.md` enumerates all 9 statuses, all 9 commands, and both the `NoOp` and `WorkItemTransitionRejected` outcomes (AC #4/#6).
- [x] `ScaffoldGovernanceTests` — `P0_WorkItemKernelRemainsPure` (clock/RNG/I/O/Dapr-free) and the renamed deferred-runtime guard (`BurnDown`/`RollUp`/`Reminder` still banned) stay green (AC #7).

## Coverage

Mapped against the implemented Story 2.1 surface (9-state `WorkItemStatus`, 9 commands, 9 success events, `WorkItemTransitionRejected`, the pure `WorkItemLifecycle` table, `WorkItemState.Sequence`):

- **AC #1–#5 (legal/illegal/idempotent transitions):** every matrix cell covered in-memory (unit) — **added** the serialization-boundary slice so accepted transitions and the reject-requeue flag survive write → JSON → replay.
- **AC #4 (terminal idempotency / rejection):** terminal-duplicate `NoOp` and illegal-transition `Reject` covered in-memory — **added** the serialized `WorkItemTransitionRejected` contract (context-carrying, sequence-free).
- **AC #6 (matrix doc is the single source of truth):** doc fitness test (pre-existing).
- **AC #7 (deterministic tests + handler purity):** matrix theories + kernel-purity guard (pre-existing) — **added** a gap-free monotonic-sequence assertion across a full six-event lifecycle *reconstructed from serialized events* (NFR-2 event-sourcing invariant).
- **AR-4 (event member order / minimal payload):** **added** `(aggregateId, sequence)`-first + envelope-free serialization guards on every round-tripped event (`messageId`/`causationId`/`correlationId`/`userId`/`metadata`/`cloudEvent` asserted absent).

- API/contract events covered: **10/10** (9 success events + `WorkItemTransitionRejected`) now cross the serialization boundary; previously **1/10** (`WorkItemCreated` only, Story 1.x).
- UI features: 0/0 (no UI surface in this slice).

## Validation

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -v minimal` — **0 warnings, 0 errors** (warnings-as-errors).
- Generated xUnit v3 executables run directly (Microsoft.Testing.Platform named-pipe is blocked in this sandbox, per the Story 1.x pattern):
  - **UnitTests: 166/166** (unchanged — matrix already exhaustive)
  - **IntegrationTests: 18/18** (was 13/13 → **+5** contract-flow tests)
  - **ArchitectureTests: 26/26** (unchanged)
  - **PropertyTests: 1/1** (unchanged)
  - Total: **211/211**, 0 failures.

```bash
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build  Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests
```

## Checklist

- [x] API/contract tests generated (serialization-boundary contract flow for all lifecycle events).
- [x] E2E/UI tests marked not applicable (Story 2.1 has no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (full Created→Completed lifecycle).
- [x] Tests cover critical error cases (illegal transition → serialized rejection; reject-non-requeue → terminal `Rejected`).
- [x] All generated tests run successfully (211/211).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; no Dapr/Aspire/containers/network/file I/O).
- [x] Tests are independent (no order dependency; each builds its own state via `Handle`/`Apply`).
- [x] Test summary created with coverage metrics.

## Notes

- This run is **QA gap-filling only** — no production code was changed. The single discovered gap was that the nine lifecycle events and `WorkItemTransitionRejected` were exercised purely in-memory and never round-tripped through `System.Text.Json`, leaving the event-sourcing write/replay invariant (NFR-2) and the AR-4 minimal/envelope-free payload shape unguarded for the new events.
- New tests follow the existing `WorkItemCreateContractFlowTests` pattern (`JsonSerializerDefaults.Web`, envelope-field absence checks, replay-into-`WorkItemState`). They reference only `Hexalith.Works.Server` (transitively Contracts) — no new project reference and no PolymorphicSerializations registration (deferred to Story 2.2; these remain plain `System.Text.Json` records).
- One iteration was required: the first draft of the full-lifecycle test did not advance the write-side state before issuing the next command (the second command was correctly rejected by the production state machine) — a test-harness bug, fixed by replaying each emitted event into the write state. The production lifecycle code was not touched.
