# Input Reconciliation — Sprint Change Proposal 2026-09-14

## Input

- **Authority:** `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-14.md`
- **Decision:** Approved for implementation by Administrator on 2026-09-14.
- **Compared with:** `prd.md`, `addendum.md`, and `.memlog.md` in this PRD workspace.
- **Scope of this extract:** PRD corrections only. The authority expressly withholds authorization for code, architecture, epics, sprint-status, dependency, commit, or push changes.

## Reconciliation verdict

The current PRD already carries most of the intended overrun behavior, but it does not state the complete approved rule as one normative invariant. It also lacks the delivered-versus-target provenance boundary and Story 5.5 traceability. Its frontmatter and amendment record do not reflect the approved 2026-09-14 change. The amendment can be applied without changing any existing FR identifier or removing FR-2's 4,000-character Obligation limit.

## Required PRD changes

### 1. Make the re-estimation invariant explicit and internally coherent

The approved authority requires the PRD to state explicitly that:

- **Re-estimation never changes cumulative `Done`.**
- `ReEstimate` replaces absolute `Estimated` and derives `Remaining = max(Estimated - Done, 0)`.
- `ReEstimate` preserves `Unit` and `Status`.
- `ReEstimate` never completes or reopens a Work Item.
- `Done > Estimated` is valid and remains visibly represented as overrun.
- Only the separately audited `CorrectProgress` act may change the submitted cumulative `Done` value.

The current PRD is directionally aligned:

- Glossary `Remaining` says Estimated minus Done, never below zero.
- FR-3 says Remaining is derived as Estimated minus Done and never below zero; Unit is immutable after first set.
- FR-6 says ReEstimate is legal in every non-terminal Status and never changes Status.
- FR-8 says ReEstimated never completes or reopens, a downward estimate leaves Status unchanged, and Done above Estimated remains visible.
- FR-9 says ReEstimated records a new absolute Estimated, never changes Status, never completes, and leaves Done above Estimated visible.
- `.memlog.md` line 53 already records that ReEstimate never clamps or rewrites Done and that CorrectProgress preserves visible Done above Estimated.

Those statements are fragmented and still omit the exact `max(...)` formula, explicit preservation of Unit, the explicit no-reopen rule in FR-9, and the exclusive authority of CorrectProgress over direct cumulative-Done correction. Consolidate the rule in FR-9 and keep FR-3/FR-6/FR-8 consistent with it. To avoid contradicting FR-8's positive-delta `ReportProgress` behavior, distinguish advancing Done by a progress delta from replacing/correcting the cumulative Done value: `ReportProgress` advances it with a strictly positive delta; only `CorrectProgress` supplies an absolute corrected cumulative value; `ReEstimate` does neither.

### 2. Add the delivered-versus-target provenance boundary

The PRD has no rule separating historical delivery evidence from requirements introduced after delivery. Add the approved normative boundary near Document Purpose or other source/provenance guidance:

> The durable Epic 1–4 story map records the delivered baseline. Requirements and acceptance details added after a story's delivery are target scope until a uniquely identified story implements them; they do not retroactively alter historical acceptance evidence.

This rule applies to historical Stories 1.1–4.9. It must not imply that every target requirement in the current PRD was accepted by, or delivered through, those historical stories. The amendment changes requirement provenance, not historical story status, acceptance records, retrospective verdicts, or identities.

### 3. Retain FR-2's Obligation limit and trace it to Story 5.5

FR-2 currently retains the approved product requirement: a required non-empty Obligation is bounded to 4,000 characters, with longer content delegated to a Conversation or referenced document. Keep the numeric limit unchanged.

Add explicit target-delivery traceability to **Story 5.5, Enforce the Obligation Bound Without Rewriting History**. The trace must preserve both sides of the approved boundary:

- the 4,000-character limit applies to admission of new `CreateWorkItem` commands once Story 5.5 delivers it; and
- historical `WorkItemCreated` payloads remain readable and replayable even if an Obligation accepted before that delivery exceeds 4,000 characters.

The existing addendum handoff H10 maps the Obligation bound to Story 4.4. That conflicts with the approved authority and must no longer be treated as valid traceability. For this PRD-only change, the PRD should point to Story 5.5; the stale addendum row remains a downstream reconciliation finding unless the parent workflow includes it in the authorized PRD workspace amendment.

### 4. Reconcile frontmatter and amendment history

The PRD frontmatter currently says `status: draft` and `updated: 2026-09-13`. This conflicts with `.memlog.md` entries recording finalization on 2026-06-14 and again on 2026-09-08, with no logged decision reopening the PRD as a draft.

After the approved amendment and validation are complete:

- set PRD `status: final`;
- set PRD `updated: 2026-09-14`;
- add a 2026-09-14 Amendment history row naming `../../sprint-change-proposal-2026-09-14.md` and the touched provenance, FR-2, FR-3/FR-6/FR-8/FR-9, assumptions/traceability, and status sections as applicable;
- add inline `(Amended 2026-09-14)` notes at materially changed PRD text, consistent with the PRD's own amendment rule; and
- append the approved decisions and finalization event to `.memlog.md` through the required memlog script if the parent workflow is applying the amendment.

The companion addendum also says `status: draft` and `updated: 2026-09-13`; this is a related artifact-state inconsistency, but the approved proposal's explicit PRD change targets `prd.md`. Do not silently broaden the change unless the parent workflow determines the PRD workspace companion is in scope.

## Prior memlog decisions and conflicts

| Prior entry | Relationship to approved authority | Disposition |
| --- | --- | --- |
| 2026-09-08 completion decision (line 30): ReEstimated never completes, visible overrun, and "No Reopen in v1" | The ReEstimate portion aligns. The blanket no-reopen statement conflicts with the later CorrectProgress-only reopen rule. | Already superseded by lines 42 and 53; retain no-reopen specifically for `ReEstimate` and retain the narrow correction-driven reopen. |
| 2026-09-13 override/decision (lines 42 and 53): CorrectProgress adjusts cumulative Done; Done may exceed Estimated; correction may reopen progress-driven completion; ReEstimate never clamps or rewrites Done | Aligns with the approved authority. | Preserve and make explicit in the PRD. |
| 2026-09-08 autofix (line 36): Obligation ≤ 4,000 recorded as applied; addendum H10 points it to Story 4.4 | The product limit aligns, but Story 4.4 traceability conflicts with the approved Story 5.5 delivery owner and risks presenting later target scope as historical delivery evidence. | Keep the FR-2 limit; replace/override Story 4.4 traceability with Story 5.5 and apply the historical-versus-target rule. |
| Finalization events (lines 16 and 37) versus current PRD `status: draft` | Direct artifact-state conflict; there is no subsequent memlog entry reopening the PRD to draft. | Restore `status: final` after the approved amendment and validation; update the date and log the amendment/finalization. |

## Validation criteria after amendment

The amended PRD is internally consistent only if all of these checks pass:

1. Every definition of Remaining agrees with `max(Estimated - Done, 0)` for estimated items and retains the existing undefined-until-estimated rule.
2. FR-3, FR-6, FR-8, and FR-9 agree that ReEstimate changes Estimated only, preserves Done/Unit/Status, and cannot complete or reopen work.
3. FR-8 continues to reserve correction-driven completion/reopen behavior to CorrectProgress; explicit completion remains terminal; ReportProgress remains the positive-delta progress act.
4. No text treats `Done > Estimated` as invalid, rejected, or silently clamped; it is a valid visible overrun.
5. Historical Story 1.1–4.9 acceptance evidence is explicitly non-retroactive, and no current target requirement is represented as historically delivered solely because it appears in the amended PRD.
6. FR-2 still contains the 4,000-character limit, names Story 5.5 as its delivery trace, and preserves replay compatibility for longer historical payloads.
7. Frontmatter date/status, Amendment history, inline amendment notes, and memlog entries agree on the 2026-09-14 approved amendment.
8. No FR identifier is renumbered and no out-of-scope artifact or Git state is changed.

## Reconciled outcome

Applying the above closes the PRD-level portion of the approved proposal. It does not itself claim that Story 5.5 has been implemented, alter historical Story 1.1–4.9 evidence, or authorize the downstream architecture, epics, story, sprint-status, dependency, code, or Git changes described elsewhere in the proposal.
