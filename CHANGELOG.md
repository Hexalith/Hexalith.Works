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

### Deferred

- The runtime/durable layer (Dapr dispatch, actor reminders for date/external resumes, checkpoint
  persistence, reminder reconciliation, AppHost restart recovery, and Aspire crash/recovery proof) is
  planned for Epic 4 (Story 4.6).
