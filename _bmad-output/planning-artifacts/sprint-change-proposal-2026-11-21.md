# Sprint change proposal · Epic 5 readiness

21 November 2026 · Hexalith.Works · Planning baseline `cd617eb1`

## Decision

Apply the previously approved Epic 5 handoff. The sprint review found five stories defined in the epic and zero standalone story files. Two instructions a developer would need were inconsistent across documents. Story 5.1 referred to an unspecified ordinal-exhaustion fault, and the UX guide described Handoff actor eligibility as open after FR-17 assigned it to the outgoing Executor. [Approved September handoff](sprint-change-proposal-2026-09-15.md)

The correction is a direct planning adjustment of moderate scope. It fits in a single planning pass. Its principal risk is accidentally marking work delivered while refreshing the tracker. A rollback would discard valid delivered evidence, and reducing MVP scope would leave the same inconsistencies in the handoff.

## Changes applied

| Area | Finding | Applied decision |
| --- | --- | --- |
| AD-17 and Story 5.1 | Whole-result failure was required, but the decision record omitted its type and the second two-event path. | Check headroom before constructing any success payload. Expose `System.OverflowException` for exhausted Works ordinals. Cover progress plus completion and spawn plus suspension. |
| Experience and design | The guide pointed to superseded decision-log entries for Handoff and correction. | Use the amended PRD/addendum. For Handoff the authenticated outgoing Executor acts from InProgress or Suspended. Display availability from the server. |
| Lifecycle and replay | The lifecycle matrix called the suspension evidence a set. The old readiness assessment still listed re-estimation as disputed. | Record order, duplicates and the first committed Resume in the matrix. AD-17 records the stream-based migration proof for snapshots that lost cumulative Done. |
| Story 5.5 | The epic described verbatim storage as already delivered and implied that a domain rejection never reaches the event store. | Distinguish the current trimming constructor from the verbatim write target. A refusal emits no creation event and consumes no Works payload ordinal. EventStore can persist its rejection envelope. |
| Epic 5 and sprint tracking | All five standalone files were missing. The epic contained conflicting instructions about when to run readiness. | Validate each numbered story against the epic and its implementation entry points, pass readiness, then publish one tracker update. |

## Handoff

| Story | Owner and first verification | Dependency |
| --- | --- | --- |
| [5.1 · Progress and ordinals](../implementation-artifacts/5-1-make-progress-and-event-ordinals-overflow-safe.md) | Developer and QA exercise one- and two-event results around `long.MaxValue`. | Independent of 5.4 |
| [5.2 · Lifecycle authority](../implementation-artifacts/5-2-make-the-lifecycle-authority-executable-and-singular.md) | Developer compares each policy cell, target and rejection label with the matrix. | Before final 5.3 integration |
| [5.3 · Effort and notes](../implementation-artifacts/5-3-preserve-re-estimate-overrun-and-enforce-bounded-act-notes.md) | Developer and QA compare live/rebuilt effort and exercise every note-bearing command. | 5.2 for integration and 5.4 for changed durable evidence |
| [5.4 · Catalog completeness](../implementation-artifacts/5-4-derive-durable-catalog-completeness-from-contracts.md) | QA compares the Contracts assembly, samples and required golden representations in both directions. | Independent of 5.1 |
| [5.5 · Obligation admission](../implementation-artifacts/5-5-enforce-the-obligation-bound-without-rewriting-history.md) | Developer proves new admission at 4,000 characters and replay of a previously accepted oversized value. | 5.4 before a new rejection producer |

The sprint owner keeps all five stories in backlog. Epics 1–3 close at their delivered baselines, and Story 4.9 remains active in Epic 4. The tracker closes two retrospective actions on the planning evidence: Epic 1 item 6 (historical mapping and the Obligation story) and Epic 2 item 16 (lifecycle mapping and disposition of correction, Handoff and note bounds). Epic 1 item 2 still gates story completion, item 13 gates unattended execution, and item 4 gates a new correction or Handoff producer. [Retrospective action criteria](sprint-change-proposal-2026-09-14.md)

## Verification

All 31 Epic 5 scenarios matched the epic and all story references resolved. Architecture lint returned zero findings. The generator recognized all 37 stories across five epics. Its three heading notices referred to the document title, the delivered-story mapping and the epic index. All three are navigation headings.

The sprint generator defaults matching story files to ready-for-dev. This refresh applied the approved backlog status explicitly and preserved the dated comments on Story 4.9. BMAD validated the published tracker with zero problems. It contains 31 completed stories, one in progress and five in backlog. All 17 retrospective action records are present, including the two closed above. [Readiness assessment](implementation-readiness.md) · [Sprint tracker](../implementation-artifacts/sprint-status.yaml)
