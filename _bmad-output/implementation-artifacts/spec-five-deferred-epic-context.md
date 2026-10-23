---
title: 'Close five deferred Epic 4 context corrections'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five open deferred entries identify missing or misleading rules in the Epic 4 context used to plan implementations: persistence before publication, the Suspended-to-InProgress Resume transition, disclosure of a claim winner, tenant context in problem details, and projection freshness/Unavailable presentation. The authoritative architecture and current code already establish these rules, but the compact context can lead future implementations away from them.

**Approach:** Correct these five rules in `epic-4-context.md`, preserving the authoritative lifecycle, privacy, persistence, and projection contracts. Close exactly the five corresponding deferred entries with source and resolution links, independently review the documentation against those authorities, validate the patch, and commit and push Works `main` as requested.

</frozen-after-approval>

## Implementation Notes

- This is one bounded context correction across the existing context, ledger, and this spec. Investigation found no unresolved intent gaps, dependency changes, or irreversible implementation actions. The user authorizes the final commit and push. Use the oneshot route; runtime behavior and the shared agent entry points need no edits.
- Context map: `_bmad-output/implementation-artifacts/epic-4-context.md` contains all five defects. The selected ledger entries are C3-BH9 (publication order), C12-BH7 (Resume), C27-BH6 (claim winner), C27-BH7 (tenant disclosure), and C29-BH5 (freshness). Other queued context corrections remain open, including the aggregate eight-correction tracking entry.
- Authorities: `_bmad-output/planning-artifacts/architecture.md` AD-01 requires EventStore to persist before publication, AD-13 requires an exact current await match to resume, and AD-23 denies before tenant-existence disclosure. `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` accepts Resume from Suspended into InProgress and Claim from Assigned/Queued. `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs` carries status and act, without a winner identity. `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs` filters the executor view and reports generation/staleness/availability. `_bmad-output/planning-artifacts/epics.md` UX-DR6/18/23/25 requires trustworthy labeled data, Unavailable, safe next actions, and protected-content disclosure controls.
- Acceptance inspection: Given an Assigned/Queued item, Claim starts it; given Suspended work with an exact active await match, Resume returns it to InProgress. Given a claim rejection without an authorized winner source, guidance must not promise an identity. Given unauthorized access, problem details must not disclose the tenant. Given stale or unavailable projections, guidance must retain only labeled trustworthy data or explicit Unavailable, never fabricate a fresh result or zero. Given event publication, the context must explicitly require prior persistence.
- Validate through source comparison and independent review; no runtime build or added tests are necessary for this documentation-only change. Check exactly five ledger closures, relative links, unchanged unrelated ledger entries, whitespace, and commitlint. Fetch and inspect main divergence before the authorized push.

- Implementation baseline: Works `main` at `75bb0b55943db2a923cae1c131cbb09bf9040350`. Updated five existing context bullets and added status/resolution annotations to exactly the five selected ledger entries. Preserved all unrelated ledger bytes, including pre-existing mixed line endings. The three other context corrections and their aggregate tracking entry remain open.
- Additional source evidence: `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`, `Handle(ResumeWorkItem)`, enforces membership in the current await set after the lifecycle status gate. `src/Hexalith.Works.Contracts/State/WorkItemState.cs`, `Apply(WorkItemResumed)`, clears that entire set. `docs/lifecycle-transition-matrix.md` documents the same first-success semantics. The implementation notes above identify both status and payload checks together with architecture AD-13.
- Independent content review returned four clarification findings; all receive simple in-scope corrections. The runtime and test review layers do not apply to this documentation-only patch. No new deferred work was introduced.

## Review Triage Log

- **Low, patched - freshness evidence:** The initial correction required a stale label without explicitly naming its time/position. `EXPERIENCE.md` requires evidence for retained values. The context now names freshness time or source position and retains only trustworthy committed data with that evidence.
- **Low, patched - no next item:** The initial claim-loss wording implied a next item always exists. An authorized queue can be empty or Unavailable. Guidance now offers another item only if the query returns one, otherwise explains the empty/unavailable state and offers a safe refresh.
- **Low, patched - exact-match provenance:** `WorkItemLifecycle.Decide` handles status, while `WorkItemAggregate.Handle(ResumeWorkItem)` checks the condition. Appended both aggregate and replay-state sources to the implementation evidence rather than attributing the payload check to the lifecycle table.
- **Low, patched - await-set clearing:** The initial Resume correction omitted multiple-condition first-success behavior. Architecture AD-13 and the transition matrix require any one exact current match to clear the entire set. The context now explicitly preserves that behavior.

## Verification

- Manual comparison of all five corrected rules to architecture AD-01/13/23, the lifecycle table and aggregate/state handlers, rejection contract, executor-filtered query/availability metadata, and epics UX-DR6/18/23/25 confirms alignment.
- One-off Python integrity check against baseline `75bb0b55943db2a923cae1c131cbb09bf9040350`: exactly five newly completed entries; removing their ten annotation lines restores the original ledger byte-for-byte. All five new resolution links resolve, edited documentation has no trailing whitespace, and frozen-intent delimiters balance.
- `git -c core.whitespace=cr-at-eol diff --check`: exit 0. `git diff --numstat`: ledger adds ten lines; context changes five existing lines.
- `npm run commitlint -- --edit /tmp/works-five-epic-context-commit.txt`: exit 0 for `docs: close five deferred Epic 4 context corrections`.
- `git fetch origin main`: exit 0. `git rev-list --left-right --count HEAD...origin/main`: `0 0` before the authorized commit.
- No runtime code, dependencies, configuration or submodule pointers changed. Runtime tests and a live Aspire launch are unnecessary for these documentation corrections.
- Independent follow-up review confirms all four clarification fixes; no concerns remain within the reviewed scope.
