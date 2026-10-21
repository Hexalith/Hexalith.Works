# Story 4.9: review corrections, pass 3

This packet preserves the fifteen review corrections qualified on Works `6ef86e36eaeb9af0f306e3923cbde533d66e3872` and Platform `1144e30bf4cb49f470941be1dbb8e85939c75141`, plus the subsequent ordinary-build correction. The story remains in progress until changed inputs have complete qualification and independent final review.

## Completed qualification before the build correction

| Profile | Location | Result |
| --- | --- | --- |
| Full | Local canonical verifier | All R1–R11 rows, 44 proof kinds and complete-fleet rollback passed |
| CI | [GitHub Actions run 39656915517](https://github.com/Hexalith/Hexalith.Works/actions/runs/39656915517) | Canonical parity passed; the ordinary build failed twice |

The full and hosted CI runs bind identical 401 Works, 669 Platform, 114 fixture and 67 linked fixture inputs. The downloaded hosted artifact ZIP matches GitHub's SHA-256 digest `3ea4df236be062b10a5b7be78dbf8311d63f1f399f8e8089914c885a4b54d5da`; the actual coordinator accepts its ledger. Each complete gate executes all seven explicit topology facts. Agents Full is expected-unavailable and supplies no Works parity proof.

Platform now owns the toolchain pin consumed by both hosted lanes: Dapr CLI 1.18.2 and runtime 1.18.7, whose Linux amd64 executable SHA-256 is `e988a6f53abec017fc9ebcb4f89de9bed11bce005eb49bd8e85e8d3d6adbf488`. A bounded disposable comparison using synthetic credentials and a non-mTLS test configuration observed the disputed self-caller request return 200 with runtime 1.18.3 and 401 with 1.18.7. Platform's production profile refuses older and prerelease runtimes. Preview production admission remains closed.

## Subsequent ordinary-build correction

The hosted ordinary build failed first on a static-web-assets cache lock and again on a Contracts deps-file lock. An evaluated solution graph identifies six duplicate Works project nodes: universal SDK-version forwarding changes the global-property identity on reference edges while outputs remain shared.

The correction forwards the selected SDK version only to external source dependencies and preserves the solution configuration through those dependencies. Release graph nodes fall from 22 to 16 with no duplicates; the Debug graph also has no duplicates. Four regression cases cover Debug/Release and implicit/explicit SDK selection. Reinstating universal forwarding makes both implicit-selection cases fail; reinstating the original configuration makes all four cases fail.

Fresh parallel Release and source-backed Debug builds passed with zero warnings or errors. The isolated Release diagnostic uses a documented MinVer override because its shared clone initially reported MINVER1001. The real source Debug build uses the repository's normal versioning. Current ordinary tests pass 589 unit, 3 property (300 generated cases), 287 architecture and 749 integration facts, with seven explicit facts reserved for the complete gate. All 21 build-configuration cases pass.

The first current integration run failed during Scheduler-volume cleanup. The leftover volume's exact fixture ownership label and absence of using containers were checked before removing it. The affected fact and complete integration retry passed. The failed run and cleanup record are retained.

Only four canonical Works inputs change after the earlier complete gates: `Directory.Build.props`, `Directory.Build.targets`, `BuildConfigurationTests.cs` and `MsBuildProjectEvaluation.cs`. Platform inputs remain identical. Those changed inputs now have their own complete full qualification, which exited 0 at 20:44:03 UTC: all eleven rows, all 44 proofs, complete rollback, 32 persisted phases, eleven fleet starts and final closure. Both Platform configurations pass 121/121, supporting consumers 263/263, retained preflight 1/1, recovery 4/4, historical 1/1, HA 1/1, mTLS 1/1 and shared rebuild 1/1. All four frozen scenarios map to named executed passing methods, and all seven ordinary explicit exclusions are independently matched to passed full-gate tests.

Fresh hosted CI parity, ordinary hosted tests and independent final review are still required for the build correction. Publishing the local correction requires authorization beyond CI inspection/reruns. The story and sprint remain `in-progress`.

## Preserved failures and scope

The original local CI attempt remains nonqualifying: Sentry could not allocate its credentials watch, and an owned probe admitted zero inotify watches despite ample disk space and inodes. A later bounded probe admitted 3,035 watches before reaching the unchanged quota, motivating a fresh full attempt at the existing limit. No machine limit or foreign process was changed.

Exact executable commands, XML, source inventories, runtime/archive/launcher bindings, original failures, focused retries and parent audits are compressed under `ordinary/`, `full/`, `ci/`, `build-graph-full/`, `local-ci-inotify-failure/` and `source/`. The fresh build correction reuses the unchanged immutable Platform/SDK export whose binding is preserved under `full/`. The untouched downloaded hosted ZIP is under `hosted/`. `manifest.json` records compressed and uncompressed SHA-256 digests for every archive; `qualification-summary.json` identifies complete qualifications and outstanding work. Evidence is for disposable synthetic parity and rollback.
