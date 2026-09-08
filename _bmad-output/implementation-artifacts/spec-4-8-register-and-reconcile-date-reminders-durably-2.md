---
title: 'Close Story 4.8 after durable reminder proof'
type: 'feature'
created: '2026-09-08'
status: 'done'
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

**Approach:** Close the story as bookkeeping: reconcile those artifacts with the evidence already on disk, keep deferred follow-ups out, and advance status to `review`.

**Decision (2026-09-08):** Close-out only. Keep the recorded live 4/4 against EventStore `8745b14b`. Do not re-run the reminder or mTLS live lane against the current pin `57c0ad56`.

## Boundaries & Constraints

**Always:** Keep deterministic reminder names, idempotent `DateResume`, deny-by-default ACLs, host-edge clocks only, and the durable catalog's 4.8 delta at zero (current `WorkItemV1Catalog.Count` is 40 because of Story 1.5). Preserve the uncommitted 2026-09-08 live-evidence notes already in the story file and `tests/test-summary.md`. Leave the open 2026-09-01 marker-store checkbox cross-referenced to DW-56.

**Never:** Re-run the live reminder or mTLS lane, re-implement reminder/index/recovery code, restore `Works:Recovery:Tenants`, change kernel or catalog types, update submodules, clear Redis, weaken ACLs, pull DW-56/DW-57/DW-68/DW-70/DW-73/DW-85/DW-86, SharedRebuild defects, the ~5s startup-reconciliation give-up, the index durability window, or Story 4.9 hosting migration.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Status close-out | Story + sprint still `in-progress`; Tasks 1–8 checked | Both artifacts say `review`; `last_updated` is 2026-09-08 | Do not mark `done` |
| Catalog prose | Story/test-summary still say catalog 37 | Prose states current count 40 and 4.8 delta 0 | Do not change fitness pins |
| Deferred leftovers | DW-56 checkbox still open | Stays open and named as out of scope | Do not invent a Works-only marker retry |

</frozen-after-approval>

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

- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` -- set Status to `review`; replace leftover catalog-37 claims with current 40 / 4.8-delta-0; keep DW-56 open -- story file still advertises an obsolete catalog gate
- [x] `_bmad-output/implementation-artifacts/sprint-status.yaml` -- set `4-8-register-and-reconcile-date-reminders-durably` to `review` and `last_updated` to 2026-09-08 -- sprint still says in-progress after implementation finished
- [x] `_bmad-output/implementation-artifacts/tests/test-summary.md` -- keep the 2026-09-08 4/4 record; add a one-line pin note that evidence is from `8745b14b` and the live lane was not re-run on current `57c0ad56` -- close-out must not imply a fresh live pass

**Acceptance Criteria:**

- Given Tasks 1–8 and in-scope review patches are already checked, when close-out lands, then the story file and sprint key both say `review` and neither says `done`.
- Given Story 1.5 raised the catalog to 40, when the touched prose is read, then it states count 40 and a zero 4.8 catalog delta without changing fitness pins.
- Given DW-56 and the other named deferred items, when this slice finishes, then they remain deferred and no reminder/index/recovery production code or live-lane run is added.

## Implementation Notes

- Human chose close-out only (2026-09-08): no live re-proof; recorded 4/4 on `8745b14b` stands.
- Close-out applied 2026-09-08: story + sprint are `review`; catalog current-gate prose is 40 / 4.8 delta 0; DW-56 checkbox left open. Matrix rows verified by a passing artifact assertion script (no production or fitness files changed). Diff reviewed at `/tmp/bmad-4-8-closeout-1788859636.diff`.
- Status target is `review`, not `done`: sprint rules send the developer to review, then a separate code-review pass.
- Continuity: Story 4.7 supplied the `work.events` surface (`IEventStoreDomainEventHandler<WorkItemSuspended>`) that 4.8 already uses.

## Spec Change Log

## Review Triage Log

| ID | Verdict | Route | Evidence |
| --- | --- | --- | --- |
| B1 | low | patch | Close-out prose names EventStore `57c0ad56` as current; HEAD gitlink is `c6efdbba` (`git -C references/Hexalith.EventStore log -1`). The live lane was not re-run on either pin. |
| B2 | false | reject | Working tree has no submodule edits (`git status` clean for `references/`). EventStore/FrontComposer/Parties pointer moves vs `42c4318` arrived in later commits `0bb802a`/`0e5c126`, not this close-out. |
| B3 | false | reject | The 2026-09-08 close-out File List names the three artifacts this slice changed. The spec, `epic-4-context.md`, and gitlinks are not this slice's deliverable. |
| B4 | medium | patch | Completion Notes still say Task 5 skips AC #1 and "AC #1 … remain open" after the same file's close-out records live 4/4 including the scheduler-fire resume. Creation-time Validation Notes risk (b) is historical and is not that contradiction. |
| B5 | false | reject | `epic-4-context.md` matches HEAD and is not in the close-out working-tree diff. Tenant-isolation wording is not this slice. |
| B6 | false | reject | Same file: fitness/shared-rebuild condensation matches HEAD and was not edited by close-out. |
| B7 | false | reject | Text after `--` on checked tasks is planning rationale (why the task existed), not a leftover failure. |
| B8 | false | reject | `in-review` is this build spec; `review` is the sprint story. Empty triage/change-log headings are this step's input; fixing them by editing the spec is rejected. |
| B9 | low | patch | Same pin-label defect as B1 in the test-summary close-out sentence. The long 2026-09-08 evidence block is required by the frozen Always (preserve those notes), not a separate defect. |
| B10 | medium | defer | `docs/eventstore-api-surface-constraints.md:301-302` still says the catalog was 37 at Story 4.8 completion and that Story 1.5 came later. This close-out did not touch that file. |
| B11 | false | reject | Implementation Notes are true of the close-out dirty set (story, sprint, test-summary, spec). No `src/` or fitness file changed. |
| E1 | low | patch | `tests/test-summary.md:2584` still says "the durable catalog remains **37**" in present tense. Later 4.8 text already states 40 / delta 0. |
| E2 | medium | patch | Same Completion Notes contradiction as B4. |
| E3 | low | patch | Same `57c0ad56` vs HEAD `c6efdbba` pin-label defect as B1. |
| E4 | false | reject | Same as B2: FrontComposer/Parties gitlinks are not in the close-out working tree. |
| E5 | false | reject | Same as B3. |
| E6 | false | reject | Same as B5. |
| E7 | false | reject | Same as B6. |
| E8 | false | reject | The since-baseline diff includes later commits after `42c4318`; those extra rewrites are not this close-out. |
| E9 | low | patch | Same pin-label defect as B1. |
| E10 | low | patch | Same leftover present-tense catalog-37 line as E1. |
| V1 | low | patch | Verification-gap Other finding: named pin `57c0ad56` vs applied HEAD `c6efdbba`. Same as B1. `d45206f7..c6efdbba` has empty `src tests` in EventStore. |
| V2 | false | reject | Same as B2. Works has no FrontComposer/Parties project references. |
