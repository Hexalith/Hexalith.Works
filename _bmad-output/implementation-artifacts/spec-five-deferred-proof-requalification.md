---
title: 'Close five deferred proof requalification items'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five root-owned deferred items with current evidence:

1. T241: qualify complete source membership and byte comparison for the retained Story 4.16 packet recheck; preserve its original result and distinguish current Platform differences from isolated proof inputs.
2. Assess telemetry admission when a supported Platform composition provides no OTLP keys, using actual composition callbacks; correct any reproduced bypass without weakening existing overlap checks.
3. Qualify synthetic Redis capture when the last hash field expires during key acquisition. Use deterministic real-Redis controls to establish the current fail-closed behavior or a small expiry-safe correction; preserve corruption refusal and owned-resource isolation.
4. Execute the Platform verifier's supporting Works conformance classes in Debug and prove the lane detects a controlled Debug-only failing mutation. This assesses the runtime coverage gap without changing upstream admission requirements.
5. Reconcile Story 4.8 catalog/trust-domain deferred documentation with current source and focused catalog/architecture controls. Preserve the historical frozen baseline and distinguish its 37-type starting point from the 40-type current catalog and zero Story 4.8 delta. Treat the repeated catalog observations as one item.

Keep changes bounded and reversible. Reuse SDK and Platform ownership; retain historical evidence and close only the selected ledger entries. Run independent review and relevant checks, then commit and push to main as requested.

</frozen-after-approval>

## Implementation Notes

- Clean main baseline: a1a87ca685d2a1f7db3ddd35ef1256eb0b348e80. The user explicitly requested five items together and a push to main. No new public API, dependency update, schema migration or production operation is needed; use the one-shot route.
- Read the root baseline, Git/state instructions, build/test configuration and architecture ownership constraints. Current Platform already compares complete source membership; the historical root packet helper remains recorded-path-only.
- Aspire prerequisite: `aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json` exited 2 with NU1605 (Dapr hosting and EventStore Aspire/ServiceDefaults downgrades). Output is `/tmp/works-five-deferred-baseline.log`. Other running AppHosts were left alone. No fresh live Platform qualification is claimed.
- Intent gaps: none after investigation. Irreversible implementation steps: none. Footprint: root proof helpers/tests, retained evidence and ledger/spec notes; no upstream source changes.
- The supported preview's implicit Aspire callbacks compose both exporter keys. The initial absent-key hypothesis failed its precondition; no broader telemetry-admission policy was introduced. The new actual-composition control asserts both keys and the existing foreign-exporter refusal. Workspace cleanup is scoped to the owner's registered workspace leases, avoiding DCP-only hosted services.
- Extracted per-key Redis acquisition without changing its behavior. Command-gated tests expire a real hash field against Redis server time and leave the capture command to encounter natural expiry. Active expiry may delete the key, while lazy expiry may retain an atomically empty logical hash. Controls accept only exact acquisition refusals, an absent key or an empty atomic hash snapshot; no expired field may qualify as live evidence.
- Added the current root inventory recheck and an adjacent historical-packet scope correction. Original results/helpers remain unchanged. Consumer and fixture membership use their respective canonical exclusions; symlinked source directories are refused because MSBuild can compile through them. Refusal results replace stale success JSON, and successful results bind roots plus ledger, isolation and executing-helper SHA-256 hashes.
- Appended a dated Story 4.8 reconciliation outside its frozen intent. Current source and policies establish catalog 40, zero Story 4.8 delta, localhost control-plane trust and public workload trust. The repeated constraints-document observation is already superseded; both ledger entries count as one item.

## Review Triage Log

- Medium, patched: consumer scans initially applied fixture-only TestResults/__pycache__ exclusions. They now follow the canonical consumer scope, with executing added-source controls in both directories.
- Medium, patched: fixture root symlinks escaped descendant validation. The root itself is now refused before inventory enumeration.
- Medium, patched: source-directory symlinks escaped suffix filtering while MSBuild included their C# children. Enumerate directories before filtering, refuse links and execute both owner refusal controls.
- Medium, patched: telemetry source discovery failed for outputs outside the checkout. Reuse the existing source locator, which supports the working directory and refuses conflicting checkouts.
- Low, patched: actual preview composition created an undisposed private workspace. Resolve and dispose the owner's registered workspace leases; do not resolve unrelated DCP services for a model-only test.
- Medium, patched: the retained replay originally trusted an existing baseline DLL. The corrected replay builds a fresh isolated Debug baseline and mutant with explicit source-mode properties before execution.
- Low, patched: replay preservation checked only the Integration DLL despite its broader output claim. Compare the complete ordinary output file/directory inventory and hashes, with add/change/remove sensitivity controls.
- Medium, patched: an input-acquisition refusal left previous success JSON in place. Write an explicit refusal result and execute success-then-refusal control.
- Low, patched: replay used XML text escaping in quoted attribute values. Generate target XML with an XML writer and qualify quote/metacharacter path controls.
- Low, patched: recheck output omitted exact input identifiers. Record selected roots and SHA-256 identities for the ledger, isolation descriptor and executing helper, with a CLI assertion of all values.
- Final combined Debug run: 379/379 passed across all 21 selected classes, zero failures/errors/skips/not-run. Executed assembly hash stayed identical across the run. Final Integration build passed with zero warnings/errors.
- Debug Architecture build and runtime/catalog governance controls: 43 passed, zero failures/skips. Complete Python tooling suite: 96 passed; focused input helper: 9 test methods covering CLI mutation/refusal cases. Current Platform input verifier controls: 19 passed. Script syntax/lint and focused Ruff checks passed.
- Independent correction recheck confirmed all ten findings addressed and found no remaining concrete material defect. Corrected isolated Debug replay execution is retained separately by the supporting-evidence task.

## Verification

- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1 --nologo` — passed with zero warnings/errors.
- Exact final 21-class xUnit assembly command and before/after SHA-256 identities are in the retained packet's `final-command.json`; `final.xml` requires all selected classes and 379 passing tests.
- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Debug --no-restore -m:1 --nologo` and the built assembly with `-class '*RuntimeAdapterGovernanceTests' -class '*ScaffoldGovernanceTests'` — 43 passed.
- `python3 -m unittest discover -s scripts/tests` — 96 passed.
- `python3 -m unittest discover -s scripts/tests -p test_platform_proof_inputs.py -v` — 9 passed, including source-membership and exact-input controls.
- `python3 -m unittest discover -s references/Hexalith.Platform/eng -p test_works_verifier_inputs.py` — 19 passed.
- `npm run lint:scripts`, focused `uv tool run --from ruff==0.13.2 ruff check` and `git -c core.whitespace=cr-at-eol diff --check` — passed.
- `npm run commitlint -- --edit /tmp/works-five-proof-commit.txt` — passed for `fix: close five deferred proof qualification gaps`; fetch showed no divergence before commit preparation.
- [Retained evidence packet](evidence/five-deferred-proof-requalification-2026-10-28/README.md) contains final results and exploratory diagnostics. Exact Platform startup exited 2 with the existing NU1605 blocker, separately from the focused passing checks.

- Corrected replay completed: fresh isolated Debug/source baseline 345/345 across all 16 classes; exact baseline method 1/1 passed; separately built mutant method 1/1 failed on the deliberate Debug assertion. Both isolated builds passed with zero warnings/errors. All 562 ordinary output entries stayed unchanged; three XML quote/metacharacter controls passed. The initial globally forced source-flag invocation failed because dependency roots were not forwarded; its diagnostic is retained separately, and the successful invocation supplies only root-declared source paths. No repository configuration changed.
- Refusal correction also covers malformed inventory shapes after a prior successful run. The final complete tooling suite passed all 96 tests after that control was added.
