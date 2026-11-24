---
title: 'Keep the repository SDK pin in isolated package consumers'
type: 'bugfix'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-reconcile-works-ci-cd-intent-gaps.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Deferred finding (late iteration-3 row 79 of `spec-implement-works-ci-cd.md`): `scripts/validate-consumer-package-references.py` builds its PackageReference-only consumers under a temporary root with no `global.json`. The `dotnet` host can therefore pick a different installed SDK than the repository pins (`global.json`, 11.0.100). That SDK is the one that builds and packs the packages.

**Approach:** Copy the repository `global.json` into the isolated root, alongside the existing MSBuild isolation files, and prove that the copy happens and that the isolated root resolves the same SDK as the repository.

</frozen-after-approval>

## Implementation Notes

- Ledger entry: "Copy the repository SDK pin into the isolated package consumers built by `validate-consumer-package-references.py`."
- `write_isolation_boundaries` writes a `global.json` holding only the repository's `sdk` object. Other settings, such as the `test` runner, are not carried over. A repository `global.json` that is missing or has no `sdk.version` raises, so the check fails closed.
- The mismatch is latent on this host. The newest installed SDK is the pinned 11.0.100, and `rollForward: latestPatch` stays within its band. It appears when a newer feature band is installed, or when a `global.json` sits above the temporary directory.
- Intended trade-off: consumers now build with the SDK that produced the packages, so the check is deterministic. It no longer incidentally tests an outside consumer on a newer SDK.
- `ConsumerIsolationTests` in `scripts/tests/test_release_tooling.py`:
  - The isolated `global.json` equals `{"sdk": …}` from the repository, and the three MSBuild boundary files have their exact content (central package management is explicitly `false`).
  - A missing pin raises `FileNotFoundError`, and a pin without `sdk` raises `ValueError`.
  - A consumer subdirectory, as `main()` uses, resolves the repository SDK. This check runs only where an unpinned directory resolves differently, and skips with a message otherwise; it also skips when `dotnet` is absent and reports stderr on failure.

## Verification

- On Works `b24caa5` plus this change, `python3 -m unittest scripts.tests.test_release_tooling` reports OK. The SDK-resolution case is skipped on this host, as explained above.
- The README rehearsal passed. `python3 scripts/pack-release-packages.py <scratch>/nupkgs 0.0.0-ci-test`, then `validate-nuget-packages.py`, then `validate-consumer-package-references.py`: all five isolated consumers restored, built with 0 warnings and ran under the pinned root.
- `python3 -m unittest discover -s scripts/tests` has one unrelated environmental failure: `test_actual_ci_gate_ignores_ambient_git_repository_selectors` reports EventStore source-revision drift. The worktree's EventStore checkout (`5257363c`) differs from its gitlink (`f2ade96`), a pre-existing change from another session.

## Review Triage Log

- **Medium, patched.** The SDK test did not discriminate on this host, and the spec claimed it did. The test now skips when an unpinned directory already resolves the pinned SDK, and the spec says the mismatch is latent here.
- **Low, patched.** The comment overstated when the mismatch occurs; it now names the newer feature band and the parent `global.json` cases.
- **Low, patched.** A line exceeded ruff's 120-character limit; it is now wrapped.
- **Low, patched.** The central-package-management assertion was weak; it now checks the exact `false` element.
- **Low, patched.** The fail-closed behaviour was untested; missing and sdk-less pins are now covered.
- **Low, patched.** The test did not use the path `main()` takes; resolution now runs from a consumer subdirectory.
- **Medium, patched.** The verbatim copy carried non-SDK policy; only the `sdk` object is now written.
- **Low, patched.** The trade-off was undocumented; it is now recorded.
- **Medium, patched.** The real script was never run; the README rehearsal now passes end to end.
- **Low, patched.** The ledger entry was open and had no trace link; it is now closed, and the context lists the source specs.
- **Low, patched.** The test failed when `dotnet` was absent and hid stderr; it now skips without `dotnet` and includes stdout and stderr on failure.
