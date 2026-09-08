---
title: 'Close Story 4.8 after durable reminder proof'
type: 'feature'
created: '2026-09-08'
status: 'draft'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '42c4318'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8's runtime is shipped — suspend-time registration, index-driven recovery, clock-free kernel, and a recorded live 4/4 — but the story file and sprint still say `in-progress`, and a few story/test-summary lines still claim catalog 37.

**Approach:** Close the story as bookkeeping: reconcile those artifacts with the evidence already on disk, keep deferred follow-ups out, and advance status to `review`. Whether the live reminder + mTLS lane must be re-run against the current EventStore pin is an open question.

## Boundaries & Constraints

**Always:** Keep deterministic reminder names, idempotent `DateResume`, deny-by-default ACLs, host-edge clocks only, and the durable catalog's 4.8 delta at zero (current `WorkItemV1Catalog.Count` is 40 because of Story 1.5). Preserve the uncommitted 2026-09-08 live-evidence notes already in the story file and `tests/test-summary.md`. Leave the open 2026-09-01 marker-store checkbox cross-referenced to DW-56.

**Never:** Re-implement reminder/index/recovery code, restore `Works:Recovery:Tenants`, change kernel or catalog types, update submodules, clear Redis, weaken ACLs, pull DW-56/DW-57/DW-68/DW-70/DW-73/DW-85/DW-86, SharedRebuild defects, the ~5s startup-reconciliation give-up, the index durability window, or Story 4.9 hosting migration.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Status close-out | Story + sprint still `in-progress`; Tasks 1–8 checked | Both artifacts say `review`; `last_updated` is 2026-09-08 | Do not mark `done` |
| Catalog prose | Story/test-summary still say catalog 37 | Prose states current count 40 and 4.8 delta 0 | Do not change fitness pins |
| Deferred leftovers | DW-56 checkbox still open | Stays open and named as out of scope | Do not invent a Works-only marker retry |

</frozen-after-approval>

## Open Questions

- How should this 4.8 slice finish? — options: **A. Close-out only** (reconcile story/sprint/docs and advance to `review`; keep the recorded 4/4 live pass against EventStore `8745b14b`; do not spend another ~16 minutes on the live lane) / **B. Re-prove then close** (run the reminder + mTLS live classes against current EventStore pin `57c0ad56`, record the exact result, then do A; if live fails, diagnose and patch only 4.8-owned host/test surfaces) / **C. Different remaining work** (name the implementation you still want; this close-out spec is then the wrong slice)

## Code Map

- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` -- story status, catalog-37 task lines, File List / Completion Notes; keep the 2026-09-08 evidence block
- `_bmad-output/implementation-artifacts/sprint-status.yaml` -- `4-8-register-and-reconcile-date-reminders-durably: in-progress`; `last_updated` already 2026-09-07
- `_bmad-output/implementation-artifacts/tests/test-summary.md` -- 2026-09-08 4/4 at 957.974s against `8745b14b`; pin later moved
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md` -- prior slice, `status: done`; do not resume or rewrite
- `_bmad-output/implementation-artifacts/deferred-work.md` -- DW-56 stays; do not append close-out noise
- `src/Hexalith.Works/Reminders/WorkItemSuspendedReminderHandler.cs` -- live `work.events` registration; do not relocate to `/project`
- `src/Hexalith.Works/Reminders/{IndexedPendingDateAwaitSource,DateReminderReconciler,ReminderReconciliationService,DateReminderName,DateResume}.cs` -- recovery source + overdue/future split; Tenants gate already gone
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- `MaintainPendingDateAwaitIndexAsync` + parking; do not touch F-PROJ-1 / SharedRebuild
- `src/Hexalith.Works/Runtime/{WorksHost.cs,WorksRecoveryOptions.cs,WorksRecoveryExtensions.cs}` -- handler registration; `MaxStreamPagesPerAggregate` + deprecated alias
- `src/Hexalith.Works.AppHost/Program.cs` -- JWT `SigningKey` composition for `eventstore`/`eventstore-admin` on `--EnableKeycloak=false`; no `Works:Recovery:Tenants` forwarding
- `tests/Hexalith.Works.IntegrationTests/{WorksReminderRecoveryPipelineSmokeTests,WorksMtlsAuthorizationSmokeTests,WorksAppHostSmokeHarness,WorksAppHostTopologyTests}.cs` -- live 4 facts + topology pins
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` -- `Count = 40`; fitness already pins 40
- `references/Hexalith.EventStore` -- current pin `57c0ad56`; live 4/4 was on `8745b14b`; read-only

## Tasks & Acceptance

**Execution:**

- [ ] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` -- set Status to `review`; replace leftover catalog-37 claims with current 40 / 4.8-delta-0; keep DW-56 open -- story file still advertises an obsolete catalog gate
- [ ] `_bmad-output/implementation-artifacts/sprint-status.yaml` -- set `4-8-register-and-reconcile-date-reminders-durably` to `review` and `last_updated` to 2026-09-08 -- sprint still says in-progress after implementation finished
- [ ] `_bmad-output/implementation-artifacts/tests/test-summary.md` -- keep the 2026-09-08 4/4 record; add a one-line pin note (`8745b14b` evidence, current `57c0ad56`) -- close-out must not imply the live lane was re-run unless question B is chosen

**Acceptance Criteria:**

- Given Tasks 1–8 and in-scope review patches are already checked, when close-out lands, then the story file and sprint key both say `review` and neither says `done`.
- Given Story 1.5 raised the catalog to 40, when the touched prose is read, then it states count 40 and a zero 4.8 catalog delta without changing fitness pins.
- Given DW-56 and the other named deferred items, when this slice finishes, then they remain deferred and no reminder/index/recovery production code changed unless question B forced a host/test patch.

## Implementation Notes

- Route is `dispatch` because the live-reproof choice is an intent gap; footprint is otherwise two or three artifact files and no irreversibles.
- Status target is `review`, not `done`: sprint rules send the developer to review, then a separate code-review pass.
- Continuity: Story 4.7 supplied the `work.events` surface (`IEventStoreDomainEventHandler<WorkItemSuspended>`) that 4.8 already uses.

## Spec Change Log

## Review Triage Log
