---
title: 'Reconcile the Works CI/CD spec intent-gap record'
type: 'chore'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `spec-implement-works-ci-cd.md` still ends its Review Triage Log with a second "Review iteration 3" table, placed after iteration 5, whose five `intent_gap` rows (72–74, 93, 103) read as open. The record also cites its acceptance Integration TRX files only under `/tmp`, which no longer exist (deferred-work entry "Reconcile the open intent-gap table in the Works CI/CD spec and retain its broad Integration TRX outside `/tmp`").

**Approach:** Add dated reconciliation notes to the historical spec without rewriting its evidence: mark the late table as a superseded duplicate pass, show where its intent gaps were settled, and record that the `/tmp` TRX files were not retained, with the current durable evidence source. Then close the ledger entry.

</frozen-after-approval>

## Implementation Notes

- `spec-implement-works-ci-cd.md` keeps every historical row and its frozen block unchanged. It gains a status banner outside the frozen block, a dated note before the late table, and an evidence-retention note at the end.
- Provenance: `git log -S` shows that commit `78e4585` (2026-10-03) appended the late table. Iteration 2's rows 72–101 match the Review loop 2 citations, so the Spec Change Log refers to the earlier tables.
- The release path was removed by `18853d2` (`.releaserc.json`, 2026-11-17) and replaced by the CiCd apply commit `3996de2` (2026-11-19), which pins the shared Builds release workflow and `Github/publish` at `v6.8.1`. Recovery now follows the README's "Release operations" section.
- The six `/tmp` TRX directories were absent. A search to depth 6 of the Works checkout and `~/.codex/artifacts` found no copy. The TRX files are not recreated: the Hexalith CI/CD baseline forbids committing bulk evidence, and artifact storage remains an open owner decision.

## Review Triage Log

- **Medium, patched.** The late table's provenance was unstated. The note now cites `78e4585` (2026-10-03).
- **Low, patched.** Rows 72–101 of iteration 2 carry no separate header. The note now says the Spec Change Log citations refer to the earlier tables.
- **Medium, patched.** "Settled by loops 2 and 3" overstated the record. The note now says those loops dropped the cross-run recovery claim in favour of the frozen collision rule.
- **Medium, patched.** "New patch version" is a retired recovery rule. The note now points to the README's "Release operations" section.
- **Low, patched.** The note credited the wrong commit for the release switch. It now cites `18853d2`, `3996de2` and the `v6.8.1` pin.
- **Medium, patched.** The notes said the `/tmp` hashes were the only fingerprints, but the accepting loop-3/4 runs recorded none. The notes now say so.
- **Low, patched.** "Hosted CI run" named no lane. The note now names the `Build and tests` and `custom-platform-parity` jobs.
- **Medium, deferred.** Release scripts that no workflow invokes are still tracked and tested; a new ledger entry records this.
- **Medium, deferred.** Late rows 79 (no SDK pin for scratch consumers) and 97 (no host-ID rejection) are still open in the rehearsal scripts; two new ledger entries record them.
- **Low, patched.** Nothing at the top of the old spec marked it historical. A banner now does, outside the frozen block.
- **False.** "The new spec is incomplete": this finalization sets its status and notes, and the ledger closes only after review.
- **Low, patched.** The claim that the files were "never copied" was unproven. It now records the bounded search that was actually run.
- **Low, patched.** The evidence policy now cites `hexalith-llm-instructions.md`.
- **Low, patched.** The ledger resolution now lists the Story 4.11 carries it closes (C7-BH13, C8-BH9, C12-BH10, C24-BH10, C24-BH11).
