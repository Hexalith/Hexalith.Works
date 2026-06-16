# Work Item Lifecycle Transition Matrix

> **Single source of truth.** This document enumerates every legal, illegal, and idempotent outcome
> of the work-item lifecycle state machine. It mirrors the pure transition table in
> `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` **1:1**. Later lifecycle stories
> (2.3 / 2.4 / 2.5, 3.5, 3.6, 4.1–4.3, 4.6) **reference this artifact and must not choose transition
> behavior locally.** If the code table and this document ever disagree, that is a defect — they are
> changed together.
>
> Owner story: 2.1 — Define the Lifecycle State Machine (FR-6; the transition slice of FR-10).

## Statuses (9)

Non-terminal: `Created`, `Assigned`, `Queued`, `InProgress`, `Suspended`.
Terminal: `Completed`, `Cancelled`, `Rejected`, `Expired`.

`Unknown` is a pre-creation sentinel only. A lifecycle command handled against `Unknown` (or a null
state — "not created") is **rejected**; the sole way to leave the pre-creation state is `CreateWorkItem`
(owned by Story 1.2, not a lifecycle transition listed here).

## Lifecycle commands → events

| Command | Trigger (act) | Success event | Notes |
|---|---|---|---|
| `AssignWorkItem` | Assign | `WorkItemAssigned` | Binds/rebinds an executor. |
| `QueueWorkItem` | Queue | `WorkItemQueued` | Places into the shared pool. |
| `ClaimWorkItem` | Claim | `WorkItemClaimed` | The only `InProgress`-entry event in the v1 catalog. |
| `SuspendWorkItem` | Suspend | `WorkItemSuspended` | No await payload in v1 (Story 3.5). |
| `ResumeWorkItem` | Resume | `WorkItemResumed` | Resume is a transition back to `InProgress` — there is no resting `Resumed` status. |
| `CompleteWorkItem` | Complete | `WorkItemCompleted` | Explicit complete act. |
| `ReportProgress` | ReportProgress | `ProgressReported` / `WorkItemCompleted` | Progress act accepted only from `InProgress` with estimated effort and matching Unit; completion is emitted when Remaining reaches zero. |
| `CancelWorkItem` | Cancel | `WorkItemCancelled` | Terminal cancel. |
| `RejectWorkItem` | Reject | `WorkItemRejected` | `Requeue=true` (default) rests at `Queued`; `Requeue=false` reaches terminal `Rejected`. |
| `ExpireWorkItem` | Expire | `WorkItemExpired` | Command-driven; handling reads no clock (advisory-until-fired). |

An illegal transition emits no success event and produces **no state change**; the handler returns a
`WorkItemTransitionRejected` rejection event (carrying `FromStatus` + `AttemptedAct`) to the caller.
This is distinct from the terminal `Rejected` **status**, which is reached only by
`RejectWorkItem(Requeue: false)`.

## Progress act

`ReportProgress` is a progress act, not a lifecycle reclassification command. It is accepted only when
the work item is already `InProgress`, has estimated effort, carries a positive `DoneDelta`, and uses
the established effort `Unit`. Accepted progress emits `ProgressReported` with the raw reported delta.
If replaying that delta makes own Remaining reach zero, the same accepted result also emits
`WorkItemCompleted` as the next success event.

| From | `ReportProgress` outcome |
|---|---|
| `Created` | R |
| `Assigned` | R |
| `Queued` | R |
| `InProgress` | `→InProgress` while Remaining > 0; `→Completed` when Remaining = 0 |
| `Suspended` | R |
| `Completed` | R |
| `Cancelled` | R |
| `Rejected` | R |
| `Expired` | R |

Progress-specific validation failures (`DoneDelta <= 0`, missing estimated effort, or mismatched
`Unit`) return `WorkItemProgressRejected` and produce no state change. Status failures return
`WorkItemTransitionRejected`.

## Transition matrix

Legend: `→X` = **Accept** (transition to status X, emitting the paired event); `R` = **Reject** via
`WorkItemTransitionRejected`; `NoOp` = **`DomainResult.NoOp`** (acknowledged, no state change).

| From \ Act | Assign | Queue | Claim | Suspend | Resume | Complete | Cancel | Reject | Expire |
|---|---|---|---|---|---|---|---|---|---|
| **Created**    | →Assigned          | →Queued | R           | R         | R          | R          | →Cancelled | R                                   | →Expired |
| **Assigned**   | →Assigned (rebind) | →Queued | →InProgress | R         | R          | R          | →Cancelled | →Queued *(requeue)* / →Rejected *(non-requeue)* | →Expired |
| **Queued**     | →Assigned          | R       | →InProgress | R         | R          | R          | →Cancelled | R                                   | →Expired |
| **InProgress** | R                  | R       | R           | →Suspended | R         | →Completed | →Cancelled | R                                   | →Expired |
| **Suspended**  | R                  | R       | R           | R         | →InProgress | →Completed | →Cancelled | R                                 | →Expired |
| **Completed**  | R                  | R       | R           | R         | R          | NoOp       | R          | R                                   | R |
| **Cancelled**  | R                  | R       | R           | R         | R          | R          | NoOp       | R                                   | R |
| **Rejected**   | R                  | R       | R           | R         | R          | R          | R          | NoOp *(non-requeue dup)* / R *(requeue)* | R |
| **Expired**    | R                  | R       | R           | R         | R          | R          | R          | R                                   | NoOp |

Notes:

- **Terminal row rule (AC #4).** From any terminal status, *every* command is `R` **except** the
  exact-duplicate terminal command, which is `NoOp` (the diagonal `NoOp` cells). The explicit pairs are
  listed under [Idempotent no-op list](#idempotent-no-op-list).
- **`Assigned + Reject` is the only flag-dependent accept cell.** `Requeue=true` (default) → `→Queued`;
  `Requeue=false` → `→Rejected` (AC #5; FR-10). Both emit `WorkItemRejected`; the event's `Requeue`
  flag is what `WorkItemState.Apply(WorkItemRejected)` reads to choose the resting status.
- **`Rejected + Reject` is flag-dependent only between NoOp and R.** A non-requeue reject of an
  already-`Rejected` item is the idempotent duplicate (`NoOp`); a requeue reject of a terminal item
  cannot un-terminal it, so it is `R`.
- **`Claim` is the single `InProgress`-entry act** for both `Assigned→InProgress` and
  `Queued→InProgress`. There is no `WorkItemStarted` event. Single-claim-wins concurrency is Story 4.3.

## Per-state Cancel / Expire decision (AR-13)

Cancel and Expire have an explicit, enumerated decision in **every** state. The reactor cascade
(Story 3.6) and Story 2.5 depend on this table; already-terminal items being unaffected is the basis
for an idempotent cascade.

| Status | `CancelWorkItem` | `ExpireWorkItem` |
|---|---|---|
| `Created`    | →Cancelled | →Expired |
| `Assigned`   | →Cancelled | →Expired |
| `Queued`     | →Cancelled | →Expired |
| `InProgress` | →Cancelled | →Expired |
| `Suspended`  | →Cancelled | →Expired |
| `Completed`  | R          | R        |
| `Cancelled`  | **NoOp** (idempotent self-cancel) | R |
| `Rejected`   | R          | R        |
| `Expired`    | R          | **NoOp** (idempotent self-expire) |

Reading: every **non-terminal** status accepts both cancel and expire (→ the matching terminal
status). Every **terminal** status is *unaffected* — cancel/expire is `R`, except the status's own
idempotent duplicate, which is `NoOp`. Cancelling an already-`Expired` item (or vice-versa) is `R`,
not a re-terminalization.

## Idempotent no-op list

These are the **only** `(terminal status, command)` pairs that return `DomainResult.NoOp`. Every other
command from a terminal status is `R`.

| Terminal status | Duplicate command | Outcome |
|---|---|---|
| `Completed` | `CompleteWorkItem` | NoOp |
| `Cancelled` | `CancelWorkItem` | NoOp |
| `Expired`   | `ExpireWorkItem` | NoOp |
| `Rejected`  | `RejectWorkItem(Requeue: false)` | NoOp |

`RejectWorkItem(Requeue: true)` against a `Rejected` item is **not** in this list — it is `R`.

## Purity

The transition table is a pure function of `(status, act, requeue)`. Handling reads no clock, RNG,
I/O, Dapr, or EventStore envelope APIs (NFR-5; enforced by `P0_WorkItemKernelRemainsPure`). `Expire`
is the adapter-fired signal, not a clock read (AR-15/C3); TTL/date sourcing is Story 4.6.
