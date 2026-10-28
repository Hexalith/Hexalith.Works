---
title: 'Close five deferred producer and recovery qualification gaps'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Reconcile five historical deferred producer/recovery gates against their retained qualification: published projection/runtime producers (including the reminder-publication alias), the create-only receipt-conflict correction, final-decoder source Debug restart/callback, the owner-selected public 3.126.0 qualification, and the twelve-source synthetic process budget. Add missing focused fixture-profile controls and evidence integrity checks, preserve original findings, and distinguish historical live acceptance from fresh deterministic checks. Independently review, validate, commit and push main as requested.

</frozen-after-approval>

## Implementation Notes

- Starting revision: `2773039ef77fc36df287527bef70040926efaf9e`; clean main. User explicitly requests five deferred items and a main push. Read baseline LLM/state/Git instructions, root guidance, EditorConfig, build/test configuration, architecture AD-19/20/27/28 and SDK adoption/cutover documents. Read-only delegated investigation identified five distinct historical gates; the repeated publication prerequisite is an alias, counted once.
- Small test/fixture/documentation change without intent gaps, public API, runtime behavior, migration, dependency update or production action: use oneshot. Works has no AppHost, and no topology change needs a live launch. Historical immutable packets remain untouched; fresh source Debug tests do not requalify current upstream live behavior.
- Code map: `WorksSdkRecoveryPipelineSmokeTests.Configure` currently hardcodes the synthetic options separately from its supported-source constant and budget explanation. Extract those existing environment settings into one test-only profile helper consumed by Configure, then bind them through the real SDK options validator in a new focused test class. Pin twelve candidates, five effects, 1.9-second operations, 56-second source quantum, thirty-minute freshness and ten-second cadence; reject the superseded 2-second/60-second profile at twelve sources and an undersized effect quantum. Preserve the current fixture values and grant semantics.
- `docs/sdk-reminder-process-adoption.md` already links historical public 3.125.1 and 3.126.0 acceptance but stale current-pin prose says 3.128.0. Identify the current pin via root props without changing it. Append a five-row closure map explaining what each historical packet establishes and the boundaries of fresh deterministic qualification.
- Annotate exactly five canonical `deferred-work.md` summaries and the publication alias, preserving all prior text. No unrelated scheduler, health, runtime capacity, retention or source-gitlink item is closed.
- Evidence: parse retained canonical results, ledger proof inventory, live xUnit outcomes, producer receipts and source Debug lane results rather than relying only on README totals. Retain a portable verifier, historical input hashes and fresh commands/logs/XML in `evidence/five-deferred-producer-recovery-2026-10-28`. A packet integrity check does not re-execute live Dapr or authenticate today's external feeds.
- Validation: Debug integration build, focused direct xUnit classes, relevant composition/profile regressions, formatting and CRLF-aware whitespace. Report any broad baseline blocker separately. Independent blind-hunter review precedes finalization; record every finding and disposition. Validate Conventional Commit, fetch and inspect divergence before pushing main.

- Broad Debug Integration build initially failed only on new helper CA2007 (missing ConfigureAwait). After using ConfigureAwait(true), the full project graph builds with zero warnings/errors; no fallback or analyzer changes are needed. The initial log is retained.
- Six new profile cases pass within the 84/84 focused recovery/composition/hosting-guard run, with zero errors/failures/skips/unexecuted cases. Actual environment callback values and all nine fixture settings are retained unchanged by extraction. Removing the twelve ledger annotation lines restores the exact baseline bytes: five canonical items plus one publication alias.
- Full-file whitespace verification exits 2 for thirty existing violations on fourteen unchanged smoke-fixture lines (272–673); each diagnostic line matches baseline. The new profile test file has a separate formatter lane. No unrelated formatting or gate weakening is included.

- The historical verifier binds 87 immutable inputs, two named producers, 88 proof kinds, final historical decoder/reader source digests, source Debug inventories, correction ancestry, actual catch-up state and required persisted restart phases. Historical packaging nuances are explicit: one named duplicate initial-convergence phase, two packaged logs with independently retained manifest identities, one omitted restored Redis aggregate digest and a narrower source Debug inventory. Original packets are unchanged.

## Review Triage Log

The independent review reported ten small evidence/test improvements. Historical semantic mutations deliberately rebind the manifest; untouched records were already byte-integrity protected. All ten are classified low and patched; no finding is deferred.

- Consumer-pin inventory: require both exact consumer entries, removing vacuous all() acceptance.
- Proof schema: accept legacy boolean proofs only for the older packet; the newer schema requires structured evidence bindings.
- Rollback contents: compare captured/restored fleet fields and runtime inventories, respecting the documented older expiry representation.
- Executed hosts: require the ten recorded Platform/rollback starts and bind their role and producer identities.
- Command identity: require the exact twenty canonical command names.
- Row producers: bind each row's version and producer to the published package inventory.
- Source SDK identity: recompute the matching source Debug subset of the canonical producer inventory and require equality.
- Source mode: require recorded Debug/source build arguments and dependency environment values.
- Fresh theory identity: require the distinct primary/replica, eleven/twelve-source and unsafe-quantum/population argument sets; six deliberately duplicated/rehashed XML controls refuse.
- Inclusive effect quantum: add the exact 55.1-second acceptance control alongside refusal immediately below it.

Independent follow-up confirms all ten original findings corrected and reruns the historical/fresh verifiers and their mutation controls successfully.

## Verification

- Final full Integration Debug build: zero warnings/errors. The four focused recovery/composition/hosting-guard classes pass **85/85**, including seven profile cases, with zero errors/failures/skips/unexecuted cases. The earlier 84-case note is the pre-review assembly. No runtime dependency, source gitlink, production configuration or historical packet is changed.
- Normal and optimized historical replay pass for 87 retained inputs, two named public producers and 88 proof kinds. Two valid relocated history packets pass and all 26 byte/semantic mutations refuse. Normal and optimized fresh replay pass; two valid relocated fresh packets pass and all six duplicate-argument mutations refuse. Pinned Ruff, new-file formatting, CRLF-aware whitespace and Conventional Commit checks pass.
- Full smoke-file formatting remains separately nonqualifying: the exact retained command exits 2 on thirty pre-existing diagnostics over fourteen unchanged lines. Original ledger bytes and fixture environment settings are preserved, as verified against the starting revision. No fresh live parity or current public-feed authentication is claimed.
- Commands, raw compressed build/test/format results, source/assembly hashes, exact source/gitlink revisions and portable replay instructions are retained in [the evidence packet](evidence/five-deferred-producer-recovery-2026-10-28/README.md). Main push is explicitly authorized by the user.
