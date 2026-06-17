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
| `SuspendWorkItem` | Suspend | `WorkItemSuspended` | Requires one or more await conditions and records the full set. |
| `ResumeWorkItem` | Resume | `WorkItemResumed` | Requires a current await-condition match while `Suspended`; records the consumed condition and returns to `InProgress`. |
| `CompleteWorkItem` | Complete | `WorkItemCompleted` | Explicit complete act. |
| `ReportProgress` | ReportProgress | `ProgressReported` / `WorkItemCompleted` | Progress act accepted only from `InProgress` with estimated effort and matching Unit; completion is emitted when Remaining reaches zero. |
| `ReEstimate` | ReEstimate | `ReEstimated` | Planning act accepted from every non-terminal status; records the new absolute estimate without changing `Status`. |
| `RescheduleWorkItem` | RescheduleWorkItem | `WorkItemRescheduled` | Planning act accepted from every non-terminal status; replaces Priority/Due-Date schedule facts without changing `Status`. |
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

## Re-estimate act

`ReEstimate` is a planning act, not a lifecycle reclassification command. It is accepted from every
non-terminal status (`Created`, `Assigned`, `Queued`, `InProgress`, `Suspended`) and emits
`ReEstimated` with the raw reported absolute `Estimated` value and `Unit`. It does **not** change
`Status`, and it never emits `WorkItemCompleted` even when replay clamps Remaining to zero. Completion
remains owned by `ReportProgress` and explicit `CompleteWorkItem`.

The first accepted `ReEstimate` on an unestimated item establishes the first estimate and its Unit.
After that, the established Unit is immutable: a re-estimate in a different Unit is rejected and the
existing Unit/Estimated values remain unchanged.

| From | `ReEstimate` outcome |
|---|---|
| `Created` | `→Created` |
| `Assigned` | `→Assigned` |
| `Queued` | `→Queued` |
| `InProgress` | `→InProgress` |
| `Suspended` | `→Suspended` |
| `Completed` | R |
| `Cancelled` | R |
| `Rejected` | R |
| `Expired` | R |

Re-estimate-specific invariant failures (`Estimated < 0`, or mismatched established `Unit`) return
`WorkItemReEstimateRejected` and produce no state change. Status failures return
`WorkItemTransitionRejected`.

## Reschedule act

`RescheduleWorkItem` is a planning act, not a lifecycle reclassification command. It is accepted from
every non-terminal status (`Created`, `Assigned`, `Queued`, `InProgress`, `Suspended`) and emits
`WorkItemRescheduled` with the new end-state `WorkItemSchedule`. Replay replaces the whole schedule
fact and does **not** change `Status`.

Any `Priority?` and any `DateOnly?` are valid, including a schedule with both values null. The
both-null schedule is the explicit "sorts last" fact for the future what-next projection. v1 priority
remains the ordered enum shape only; there is no routing score, escalation band, confidence, cost,
spend, or numeric weight field.

| From | `RescheduleWorkItem` outcome |
|---|---|
| `Created` | `→Created` |
| `Assigned` | `→Assigned` |
| `Queued` | `→Queued` |
| `InProgress` | `→InProgress` |
| `Suspended` | `→Suspended` |
| `Completed` | R |
| `Cancelled` | R |
| `Rejected` | R |
| `Expired` | R |

Reschedule has no schedule-content rejection: both nullable fields may be absent. Status failures
return `WorkItemTransitionRejected`.

## Suspend / Resume Await Conditions

`SuspendWorkItem` is accepted only from `InProgress` and only when the command carries at least one
await condition. The accepted `WorkItemSuspended` event records the full await-condition set and replay
sets `Status = Suspended` without changing own effort or roll-up contribution.

Await conditions are kind-aware keys. The v1 cases are `ChildCompleted(childId)`,
`DateReached(instant)`, and `ExternalSignal(correlationId)`. Matching compares kind plus stable
correlation key exactly; an external signal whose key text equals a child id is still not a child
completion condition.

`ResumeWorkItem` while `Suspended` is accepted only when the supplied await condition matches one of
the current conditions. The accepted `WorkItemResumed` consumes that one condition, clears the full
condition set from that suspension, records the consumed condition for replay, and rests at
`InProgress`.

If a resume condition does not match the current suspended set, the command returns
`WorkItemTransitionRejected`, emits no `WorkItemResumed`, burns no sequence number, and leaves the
condition set intact. After a successful resume, repeating the consumed condition is the only resume
no-op; a different post-resume condition is rejected. Date and external resumes arrive as command data;
the aggregate never reads a clock or calls an adapter.

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
  `Queued→InProgress`. There is no `WorkItemStarted` event. **Single-claim-wins (Story 4.3; FR-18/NFR-3)**
  is realized as the *composition* of two separately-owned layers, with **no cell change** here: (1) the
  pure lifecycle above — `Queued/Assigned → Claim = Accept(InProgress)`, every other status (including
  `InProgress` and `Suspended`) `R` — and (2) the EventStore substrate's **expected-version (ETag)
  optimistic concurrency** (`AggregateActor → EventPersister → SaveStateAsync`). When two executors claim
  the same item at expected version `N`, both compute `WorkItemClaimed` at sequence `N+1`; the store admits
  exactly one append (the winner → `InProgress`), and the loser's commit conflicts, so on retry it
  re-handles against the now-`InProgress` state and lands on the existing `InProgress + Claim = R` cell →
  `WorkItemTransitionRejected(InProgress, "Claim")`. The loser's observable rejection is therefore the
  **existing** `WorkItemTransitionRejected` (DC1) — **no** `ClaimRejected`/`ConcurrencyRejected` type is
  added and the v1 catalog stays **36**. Retry-exhaustion under hot contention is an infrastructure failure
  (exception/dead-letter), not a domain rejection; the live ETag append/retry path is exercised under
  Aspire in Story 4.5.
- **Active work is not directly reassigned or requeued (D4 — finalizes Story 2.1's deferred edge cell).**
  The `InProgress` and `Suspended` rows keep `Assign = R` and `Queue = R`: hand-off in v1 happens while the
  item is `Assigned` (a rebind — `Assigned → Assign`, latest binding wins) or via
  `Assigned → Queue → (re)Claim` by the new executor. To change hands, active work is completed/cancelled
  or requeued first; there is deliberately **no** `InProgress → Assigned` transition. Story 2.1 deferred
  only *reassign of `InProgress` (Assign)* to Story 4.2 (`InProgress → Queue = R` was already decided);
  Story 4.2 finalizes it as `R`. The matrix is the single source of truth — later stories must not add a
  local active-hand-off path (SM-C2). (Owner: Story 4.2; FR-17.)
- **Requeue is `QueueWorkItem`/`WorkItemQueued`, and the queued item keeps its last binding in state.**
  Returning assigned work to the shared pool is `Assigned → Queue` (FR-18); every queue entry — from
  `Created` or `Assigned` — emits the one `WorkItemQueued` event, which carries **no** binding. Queueing is
  not an executor-binding act, so `Apply(WorkItemQueued)` leaves `WorkItemState.ExecutorBinding` at its last
  value (the last raw act); "who currently owns a `Queued` item" is presentation owned by the Story 4.4
  what's-next projection, not an aggregate-state mutation (D2/D6). This is distinct from Story 2.5's
  `RejectWorkItem(Requeue: true) → WorkItemRejected` decline-and-rest-at-`Queued` path.

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
