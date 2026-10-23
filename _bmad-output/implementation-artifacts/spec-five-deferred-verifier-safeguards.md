---
title: 'Close five deferred Platform verifier safeguards'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five open Works deferrals weaken the Platform proof verifier: T220 omits non-C# fixture inputs, T221 omits the runtime SDK's linked Builds catalog, T222 lacks executing end-of-run drift regressions, R2B5 leaves command descendants alive after timeout/cancellation, and the final round-2 entry filters linked fixture sources using absolute paths.

**Approach:** Bind fixture projects, resources, test helpers and runner configuration; record and recheck the selected SDK Builds inputs separately from official producer source identity; extract and exercise the final verification checks; terminate owned command process groups on timeout or cancellation; and filter build outputs relative to each source owner. Preserve fail-closed evidence and existing live-proof requirements. Resolve these five ledger entries, independently review, run focused tests, commit and push Platform main, then commit and push its Works gitlink and resolution records on main, as requested.

</frozen-after-approval>

## Implementation Notes

- Initial clean revisions: Works `f10c51f4969ae66530070fd657e69dfa45963888`; Platform `068ceff07cfa72812cb6a2bd47cc612ce39ad7f3`. Both are on main. No dependency version updates, domain state migrations, deployment, or new consumer API are required. This localized verifier correction uses the oneshot route. The user explicitly requested the five-item scope and pushes.
- Code map: Platform `eng/verify_works_host.py` owns fixture/link inventories and final closure; `eng/verify_works_producer.py` owns source export identity; their `test_*.py` files contain deterministic temporary-tree controls. An internal command helper and focused tests will isolate owned-process cleanup. Works' deferred ledger and this spec record completion. Preserve historical evidence artifacts and retired domain plumbing.
- Verification: run Platform's six existing verifier evidence suites plus the new command lifecycle suite, Platform component evidence tests and Works bootstrap/release-tooling regressions. Use local Python compilation/lint checks and Git whitespace checks. Test drift with changed, added, deleted and retargeted inputs, and execute real owned parent/child timeout and cancellation controls. These are verifier changes; no AppHost or live R1–R11 qualification is claimed by deterministic tests.
- Implemented complete fixture inventories, owner-relative linked-source filtering, separate Builds Props/target snapshots for both exported and checkout SDKs, and extracted `require_unchanged_inputs` called before acceptance. Platform source membership is now re-enumerated at final closure. Added owned POSIX command groups, including cleanup on default SIGTERM and normal completion with surviving children; helper and test sources are explicitly hash-bound.
- Baseline verifier tests exposed three nonhermetic catalog reads under Platform's prohibited nested submodule path. Their temporary catalog fixture now supplies the adapter value directly. Replacing the shared `subprocess.run` mock with a `run_owned` mock also required explicitly isolating canonical/linked inventories in error-recording tests.
- Initial focused producer tests passed 18/18; executing command lifecycle controls passed 6/6; Platform component evidence tests passed 13/13; Works bootstrap/release tooling passed 53/53. Syntax/undefined-name lint passed on the six changed Python files. Full inherited Works release-script Ruff policy reports pre-existing style violations in Platform's older verifier files; no lint configuration or gate was weakened.
- Independent review reported eight findings, triaged below. Corrections bind actual copied consumer resources and helper DLLs before supporting execution, reject fixture links, close launch-time cancellation, and retain group cleanup grace. These remain local verifier corrections with no domain or deployment changes.
- Follow-up independent review confirmed the eight implementation corrections; its one test-disappearance race is corrected below. No additional work was deferred. The final combined validation passed 150 Platform verifier tests, 13 component evidence tests and 53 Works bootstrap/release-tooling tests (216 total), with zero skips or failures.
- Platform main advanced by nine unrelated commits during the task. Rebased the implementation onto `1d646b232ad862f6da1d8dd0953fe96ccd273fee`, producing `b816f59f24c5bd34638fd21b4b70d1b13d84dec4`, and repeated all 216 tests successfully. Full Ruff passes for the three new Python files; correctness lint passes for the existing changed verifier files. Python compilation and whitespace checks pass (the Works ledger check uses CRLF-aware whitespace interpretation to preserve its existing line endings). Final test logs are `/tmp/works-five-verifier-{platform,components,bootstrap}-rebased.log`.
- Exactly T220, T221, T222, R2B5 and the linked-fixture relative-path entry are resolved. The older T241 historical-packet scope correction remains open; the new verifier does compare complete current Platform membership. Historical proof packets and the current live qualification status remain unchanged; no complete live parity run is claimed.

## Review Triage Log

- **medium / patch — unsupported catalog ancestor:** confirmed the SDK's fourth property repeats the third path; restored the three actual import locations and added an unconsumed-ancestor refusal control.
- **medium / patch — pipeline closure wiring:** direct final-check tests did not protect the call from the main pipeline. Add full mocked-pipeline success and closure-refusal controls before ledger acceptance.
- **medium / patch — symbolic-link fixture directories:** `rglob` omits linked directories that MSBuild can compile. Reject input links rather than omit their contents; cover linked directories and files.
- **medium / patch — cancellation during command creation:** confirmed the process existed before cleanup protection. Defer default SIGINT/SIGTERM during launch until the process is assigned and cleanup protection is active, with real signal controls.
- **low / patch — detached-session ownership wording:** the agreed implementation owns a POSIX process group. Explicitly detached sessions are outside that group, so clarify the documented guarantee and resolution rather than claim full process-tree adoption. A detached-session control documents the boundary and explicitly cleans its own detached PID.
- **medium / patch — descendant cleanup grace:** waiting only on an exited leader immediately escalated surviving children. Wait for the owned group through the grace deadline; verify a child's termination handler can finish.
- **medium / patch — copied resource and runner drift:** source hashes do not protect executed output copies. Bind the complete consumer directory before supporting tests and re-enumerate it at closure, with copied JSON and runner controls.
- **medium / patch — compiled testing helper drift:** the original four consumer artifacts omitted `Hexalith.Works.Testing.dll`. The complete output inventory now includes this DLL and every compiled dependency, with a helper-DLL mutation control.
- **low / patch — process-reaping test race:** follow-up review confirmed the eight corrections but reproduced `FileNotFoundError` between checking and reading `/proc/<pid>/stat`. The test now treats that race as successful process disappearance, alongside `ProcessLookupError`.
