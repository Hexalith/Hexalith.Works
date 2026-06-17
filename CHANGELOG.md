# Changelog

All notable changes to Hexalith.Works will be documented in this file.

## Unreleased

### Added

- Initial Works scaffold, build configuration, and architecture fitness tests (Epic 1).
- Tenant-scoped work item kernel: event-sourced `WorkItemAggregate` owning obligation, executor
  binding (`ExecutorBinding`/`PartyId`), schedule, status, and parent/child references; sibling
  modules referenced by ID rather than copied (Epic 1).
- Boundary ports and decision record: `IExpectationResolver` with a no-LLM `LiteralExpectationResolver`,
  the `IExecutorRouter` seam, and `ExpectationReference` (Epic 1).
- Work item lifecycle state machine with a single pure transition table (`WorkItemLifecycle`) mirrored
  1:1 by `docs/lifecycle-transition-matrix.md`: assign, queue, claim, suspend, resume, complete,
  cancel, reject, expire (Epic 2).
- Unit-tagged effort burn-down — `ReportProgress`, `ReEstimate`, `RescheduleWorkItem` — with a
  per-item `Unit` that is immutable after the first estimate (Epic 2).
- Terminal lifecycle handling: complete, cancel, reject (requeue / non-requeue), and expire, with
  idempotent terminal no-ops (Epic 2).
- Tenant-safe work tree guard: acyclic, single-parent, single-tenant attachment with a policy-supplied
  maximum depth (default 32) (Epic 3).
- `SpawnChild` parent→child spawning, with an optional suspend-until-child-completes await (Epic 3).
- Recursive remaining-effort roll-up projection with per-child-sequence last-writer-wins accounting and
  tenant-equality assertions at every traversal hop (Epic 3).
- Heterogeneous-unit roll-up safety: per-unit subtotals, fail-closed on unit mismatch with
  metadata-only diagnostics and a `Degraded` indicator, never a coerced all-unit total (Epic 3).
- Suspend/resume on await-conditions (`ChildCompleted`, `DateReached`, `ExternalSignal`); resume on the
  first matching trigger with an idempotent duplicate-resume no-op (Epic 3).
- Pure reactor translators in `Hexalith.Works.Reactor` — `ChildCompletionResumeTranslator` and
  `TerminalCascadeTranslator` — for cascading cancel/expire through still-active descendants (Epic 3).
- Uniform `ExecutorBinding` executor model (`PartyId` + `Channel` + `AuthorityLevel`) covering system,
  internal, and external parties with one shape — no executor-kind discriminator; `AuthorityLevel`
  carried but not enforced in v1 (Epic 4).
- Assign, reassign, and hand-off through one uniform `AssignWorkItem` operation; return-to-pool requeue
  re-emits `WorkItemQueued` while retaining the last binding in state (Epic 4).
- Single-claim-wins: claiming a `Queued`/`Assigned` item emits `WorkItemClaimed`; concurrent losers
  receive an observable `WorkItemTransitionRejected` through EventStore expected-version (ETag)
  concurrency — no new rejection type added (Epic 4).
- Tenant "what's next" queue: pure `WhatsNextQueueProjection` + `WhatsNextItem` read model, ordered by
  Priority → earliest Due Date → creation/identity (both-null sorts last), with tenant scoping and a
  distinct query-side authorization filter; `WhatsNextQueryHandler` exposes it (Epic 4).
- Runnable adapter-edge host `Hexalith.Works` proving the command/event pipeline under the Aspire
  AppHost: `WorkItemEventStoreAggregate : EventStoreAggregate<WorkItemState>` wraps the pure kernel,
  with `/process`, `/project`, `/query`, and `/replay-state` endpoints and persist-then-publish (Epic 4,
  Story 4.5).
- Runtime/durable layer in the `Hexalith.Works` host, with the kernel kept clock-free: Dapr actor
  reminders for date resumes (deterministic reminder names), startup reminder reconciliation, reactor
  cascade dispatch with bounded checkpoints, checkpoint replay, and AppHost restart recovery (Epic 4,
  Story 4.6).

### Deferred

- Live end-to-end proof of the two Tier-3 Aspire lanes (`WorksCommandPipelineSmokeTests`,
  `WorksReminderRecoveryPipelineSmokeTests`) requires Docker + `dapr init` + Dapr placement/scheduler;
  both skip cleanly in a headless sandbox, so their decision logic is currently proven only by
  deterministic adapter tests.
- Tenant-wide / domain-wide pending-await discovery is bounded by the EventStore per-aggregate
  stream-read route; cross-aggregate roll-up convergence and live `IProjectionChangeNotifier` wiring
  remain substrate-deferred.
- Channel & surface adapters (UI / web shell / SignalR, MCP, chatbot, email), executor routing &
  eligibility, and `AuthorityLevel` enforcement remain out of scope for v1 (Themes 3–6).
