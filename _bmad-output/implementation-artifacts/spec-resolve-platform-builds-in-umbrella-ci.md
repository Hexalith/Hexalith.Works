---
title: 'Resolve the verified Builds producer in umbrella CI'
type: 'bugfix'
created: '2026-11-02'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
owner_repository: '/home/administrator/projects/hexalith/works/references/Hexalith.Platform'
owner_base_commit: 92f87e6a49e4357f91b2ffb821e044931291c0e6
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Works initializes its root-declared Builds and Platform dependencies without nested checkouts. Its actual `bash scripts/prepare-platform-builds.sh` command fails because Platform's recorded-artifact verifier looks only under Platform's uninitialized `references/Hexalith.Builds`, despite an existing workspace Builds checkout at the exact approved producer.

**Approach:** Let Platform's artifact and tuple verifiers select the existing sibling Builds catalog when present, otherwise retain the standalone owner-declared path. Keep all recorded-owner, clean-source, full producer/blob/catalog identity, signed archive, tooling hash and executable version checks. An existing mismatched sibling must refuse; it must never silently fall back. This change must neither initialize nested submodules nor grant candidate/runtime acceptance.

Given an umbrella workspace with a matching sibling, when recorded artifact preparation runs, then the sibling passes the existing identity checks before the public archive is verified. Given standalone Platform without that sibling catalog, when preparation runs, then its owner-declared Builds checkout remains authoritative. Given a stale selected source or dirty/wrong owner pin, when preparation runs, then it refuses before downloads, extraction or executable invocation.

The user's existing maintenance instruction authorizes CI repairs and publishing after automated checks. Their `yrs` reply explicitly authorizes the three recorded Builds pointers already applied; this repair needs no additional local nested initialization. Human validation is waived; automated source/package checks remain required.

</frozen-after-approval>

## Implementation Notes

- Isolated implementation in `/tmp/works-platform-package-ci-20261102` after a concurrent workspace sync committed the separate solution-fixture fix. Execution base is `a9bba99829fc2d375d968be5d2a5ea2f63bb5c6f`; the shared checkout was returned exactly to its current tracked bytes by reversing only this repair's saved patch. No user edits were reverted.
- The sync also moved Platform's recorded Builds pointer to unpublished source `d2a430c988462a634674a69c562ea4afd1f06ef4` while the official binding remains 4.42.1/`2afc90eb107d73490aa0299a32efbca4bc160b05`. Reapply the already approved recorded producer in a separate maintenance change; preserve all other incoming dependency pointers. Latest official Builds and SDK releases remain 4.42.1 and 3.132.1.
- Planned helper edits: shared root selection in `eng/builds_tuple.py`, both artifact/tuple consumers, and focused standalone/sibling/stale-source refusal controls in `eng/test_builds_tuple.py`. The first 18 binding and 15 resolution tests pass in the workspace; repeat in isolation.
- A workspace solution build produced 316 analyzer errors under inherited Works settings; it is retained as an environment-specific attempt. A standalone owner worktree build uses the owner configuration and the verified public tool closure.
- Final isolated worktree: `/tmp/works-platform-package-ci-workspace-20261102/references/Hexalith.Platform`. Its existing sibling Builds path is a filesystem link to the already initialized Works root dependency; no nested repository was initialized. Both the binding (18 cases) and host-resolution (15 cases) suites pass in this layout.
- Separate mechanical CI-graph repair committed as `fb3c91df8ba7fdbcf01248f264617e7bdbdb1da7`: the missing ArchiveProcessFixture solution member and restored official Builds gitlink. All other upstream gitlink updates remain. A full nonincremental isolated Release/package build passes with zero warnings/errors using verified public tool assemblies and the existing root EventStore resource at its matching recorded source.
- Helper review input is frozen against `fb3c91df8ba7fdbcf01248f264617e7bdbdb1da7`; four-file hash receipt and exact patch are in `.artifacts/upgrade-release-orchestration-20261101/platform-umbrella-lookup-review-input.{json,patch}`. Selected mismatched siblings fail the real catalog source guard before downloads, extraction or signature/executable calls.
- Review patches now pass 19 binding tests (including matching/stale tuple-preflight paths and explicit MSBuild root under an ambient override) plus 15 host-resolution tests. The stale-sibling control includes a nested catalog that can satisfy the bound identity, and still refuses without fallback or artifact side effects.
- The new preflight test initially intercepted Python platform-information subprocesses; isolating that synthetic platform probe fixed the unit fixture without changing production behavior.

## Review Triage Log

- Finding 1 — medium, patch: Directory.Build.props honors HexalithBuildsRoot, while preflight previously verified its selected root but did not pass it to MSBuild. Every graph/build/test property set now explicitly binds the verified root; the positive preflight control supplies a contradictory ambient root and checks the explicit argument. No new public input or source fallback.
- Finding 2 — medium, patch: the prior stale-sibling control lacked an alternative valid nested catalog. Add its matching catalog and Git-response controls and require the selected sibling to fail after exactly three reads, before any archive work.
- Finding 3 — medium, patch: selection tests did not exercise run_tuple_ci. Add direct audit-only preflight controls for successful sibling verification and stale-source retained failure, checking candidateAcceptance:false, command ordering, and the MSBuild root binding. Synthetic controls do not claim runtime or publication acceptance.
- Helper repair committed as `6064b363a8e1a3e0fdaec00443b46318abc74c75`. The unchanged Works preparation script (byte-identical isolated-layout copy) now completes with --require-recorded, a clean committed owner, exact producer/catalog checks, fresh public NuGet archive/signature verification and exact CLI version. Actual output: `/tmp/works-published-builds.d7kqAB/verified/artifact-identity.json`; candidateAcceptance remains false.
