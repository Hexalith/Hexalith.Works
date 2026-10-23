---
title: 'Close five deferred planning-record integrity gaps'
type: 'chore'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred tracking items make the maintained planning record difficult to use: the ledger/archive lack navigation and provenance, archived decisions repeat or have unexplained dates, four historical headings are truncated, concurrent Story 4.8 review claims lack a dated reconciliation, and the sprint's original generation timestamp fails its validator.

**Approach:** Complete these five items with reciprocal navigation and historical provenance, canonical decisions with preserved reaffirmation history, source-backed heading repairs, an evidence-linked audit of the concurrent review claims, and a validator-compatible generation stamp that preserves the original precise instant in a comment. Verify the records, independently review the changes, then commit and push Works main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: `ddd571996df3057be39bb337cb0a9a0ff6f983ff`, obtained with `git rev-parse HEAD`. The working tree was clean on Works main; the user authorized five deferred items and the final push.
- Small, reversible documentation/metadata change: the ledger, archive, sprint status and this spec only. No production API, dependency update, migration or topology change is required. Use the oneshot route; no epic story key applies.
- Scope is exactly the five source-spec entries headed "Add navigation and provenance guidance", "Reconcile duplicate and ambiguously dated decision fields", "Reconstruct the compacted deferred-work entry titles", "Audit stale concurrent-review records", and "Make the sprint tracker's generated stamp". Preserve original review evidence, human decisions and existing story/epic statuses.
- Archive provenance: creation/compaction commit `5856fab739d37e9d7f34ec9b0f5d2df30fe400e9`. Ledger statuses remain current; archived locations and statuses describe their recorded snapshots. Preserve both DW-56 lifecycle snapshots.
- Collapse exact duplicate decision fields within each archived snapshot only. DW-56's original decision was recorded in `05583c6c527136523e1ef3d441071c77d9d6aaa9` on 2026-08-29 and repeated unchanged in `37da71625afc70506283e8d8f23971c7967ec7a9` on 2026-09-01; retain the later date as a reaffirmation, without inventing an amended policy.
- Recover DW-20 from `spec-kernel-transitive-dependency-guard.md`, DW-53/54 from `spec-rollup-contract-drift-hardening.md`, and DW-55 from `spec-dapr-subscription-operations-hardening.md`. Use surviving full summary text, with whitespace normalized, in both ledger and archive.
- Concurrent review evidence: `56a235c28753d2f87ef6897b39a032a1989b5610` contains default control-plane topology assertions and the localhost control-plane/public ACL distinction. Current ownership is documented in `docs/platform-hosting-cutover.md` and `evidence/story-4-9/verification/retired-test-replacements.json`. Audit the stale claims without claiming fresh live verification or silently closing separate runtime work.
- Sprint baseline: `uv run .agents/skills/bmad-sprint-planning/scripts/sprint_plan.py validate --status-file _bmad-output/implementation-artifacts/sprint-status.yaml` returns `valid: false` only for `generated: 2026-06-15T18:47:50+02:00`. Direct python invocation fails because system Python lacks ruamel.yaml; uv supplies the script's declared tool dependency without changing repository dependencies. Preserve all development_status/action_items and the original instant, then require valid=true and no problems.
- Verify reciprocal local links, title equality to surviving source summaries, canonical decision counts and preserved lifecycle/status/evidence fields with focused assertions. Run the existing sprint validator and inspect its JSON validity (its exit code alone does not signal validity). Run `git -c core.whitespace=cr-at-eol diff --check` and commitlint before committing. Runtime builds/tests are not relevant to this documentation-only patch.

- Implementation completed the five selected metadata repairs in three existing files. The archive keeps both DW-56 snapshots with distinct anchors; only repeated decision copies were removed, and the later identical dated decision is explicitly a reaffirmation. No runtime checks or newly executed live qualification are claimed.

## Verification

- `uv run .agents/skills/bmad-sprint-planning/scripts/sprint_plan.py validate --status-file _bmad-output/implementation-artifacts/sprint-status.yaml`: `valid: true`, `problems: []`. The baseline returned `valid: false` for the generated stamp only.
- `python3 /tmp/works-five-tracking-check.py`: seven focused assertion groups passed. Both files' four repaired headings equal their source summaries; all 76 archive snapshots preserve identity, lifecycle, cause, resolution and decision meanings; both DW-56 snapshots retain their dates; exactly five ledger resolutions were added with original evidence untouched; sprint bytes outside the timestamp/comment are identical to baseline; all added local links/anchors resolve; historical topology source line references and full Git identifiers exist and support the audit. This was a temporary verification helper, not a new repository test suite.
- Every archived ledger DW number has a corresponding archive entry. Story and epic statuses, action items, last_updated, production code and submodule gitlinks are unchanged.
- `git -c core.whitespace=cr-at-eol diff --check`: passed. `git -c fetch.recurseSubmodules=false fetch origin main` succeeded; `git rev-list --left-right --count HEAD...origin/main` reported `0 0` before committing.


## Heading Reconstruction Sources

Each heading equals the whitespace-normalized `summary` at the original baseline revision:

- DW-20: [kernel dependency summary](https://github.com/Hexalith/Hexalith.Works/blob/ddd571996df3057be39bb337cb0a9a0ff6f983ff/_bmad-output/implementation-artifacts/spec-kernel-transitive-dependency-guard.md#L27).
- DW-53: [roll-up effect summary](https://github.com/Hexalith/Hexalith.Works/blob/ddd571996df3057be39bb337cb0a9a0ff6f983ff/_bmad-output/implementation-artifacts/spec-rollup-contract-drift-hardening.md#L24).
- DW-54: [queue coverage summary](https://github.com/Hexalith/Hexalith.Works/blob/ddd571996df3057be39bb337cb0a9a0ff6f983ff/_bmad-output/implementation-artifacts/spec-rollup-contract-drift-hardening.md#L36).
- DW-55: [publisher-fixture summary](https://github.com/Hexalith/Hexalith.Works/blob/ddd571996df3057be39bb337cb0a9a0ff6f983ff/_bmad-output/implementation-artifacts/spec-dapr-subscription-operations-hardening.md#L17).

## Review Triage Log

- Low, patched: B/V finding identifiers lacked their original Story 1.5 review link. The audit now links the review at the immutable baseline and names the identifier namespace.
- Low, patched: readers opening the original topology/ACL findings could miss the later correction. Both now have adjacent dated audit links; original evidence and statuses remain unchanged.
- Low, patched: heading source files lacked immutable reconstruction links. The four exact source summaries are now linked above at the full baseline revision.
- False: missing executed record-consistency evidence. The Verification section already records the completed seven assertion groups and their outcomes, including title equality, canonical decision counts, lifecycle preservation and reciprocal links; it was added while review was running.
- False: missing successful sprint validation evidence. Verification already records the exact command and `valid: true`, `problems: []`, alongside the separately identified failing baseline. The final check reruns that command and asserts the JSON validity field.


- Review follow-up confirmed all five findings resolved with no remaining gap. No new work was deferred. The seven focused verification groups passed again after the traceability patches; immutable heading-source links and the original review line anchor were checked against Git.
- Commit message validation: `npm run commitlint -- --edit /tmp/works-five-tracking-commit.txt` passed for `docs: close five deferred tracking integrity gaps`.
