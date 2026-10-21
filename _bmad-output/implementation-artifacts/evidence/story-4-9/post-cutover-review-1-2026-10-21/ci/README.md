# Review pass 1: qualifying CI entry point

The actual `bash scripts/verify-platform-parity-ci.sh` entry point exited 0 on
2026-10-21, from 13:11:48 to 13:39:19 UTC. Both bootstrap and the owning
canonical verifier report `passed-disposable-synthetic-parity-and-rollback`.
All eleven AD-20 rows, all 44 proof kinds, complete-fleet rollback and final
source/runtime/archive/launcher closure passed. This is local disposable
synthetic qualification, not a hosted GitHub Actions run or production admission.

The selected `ci` profile actually uses Release/public-package consumers and
Release Agents commands. The parent audited both nested component command
records and every top-level .NET build command. No Debug check, dependency mode
or assembly inventory is claimed. The separate [full profile](../full/README.md)
qualifies source Debug conformance as well as Release/packages.

| Required lane | Passed |
| --- | ---: |
| Canonical Python evidence and component helpers | 110 |
| Platform package Release | 114 |
| Supporting Works consumer | 255 |
| Archived-host readiness preflight, separate from rollback | 1 |
| Recovery, including date callback and complete rollback | 4 |
| Historical lifecycle/security/restart | 1 |
| Two-host HA | 1 |
| mTLS | 1 |
| Shared rebuild and concurrent catch-up | 1 |

Every required XML lane has zero failures, skips or unexecuted tests. The gate
binds 32 persisted phases, eleven actual fleet starts and 439 runtime artifacts.
The date callback records the committed suspension's exact reminder kind,
identity, target and future due time with periodic workers disabled. All seven
explicit facts omitted from ordinary integration execution ran in this gate.

Platform is based on `7b5548f0d308d27af9d5fda5b68c6b959c053185` plus seven exact
executable/test overlays. The adjacent source-binding SHA-256 is
`255a9e88aae63e0f3e40dd5f40ee1771bafa1a309b60a182ef79600d2f029206`.
The ordinary Works, Platform, fixture and helper source bytes match the full
profile. Public EventStore 3.126.0 is independently qualified at producer
`fcf78d6cafb6c0c9f52dc350a9a09aff54ded288`; Builds is bound at
`17d0c6ffec5fb3c8d8058e67cb85644460cb5256` with 1,779 ordinary inputs.
Both owning repositories' working source overlays are also preserved in the
closure directory as compressed unified diffs.

## Recorded local Docker Desktop environment

Earlier CI attempts remain nonqualifying under `verification/`. The first saw
`Degraded` readiness in the archived-host preflight. Two later supporting cases
timed out on a disposable Redis host binding; the last was independently
observed answering internal PING while host TCP refused. The intermittent cause
remains unresolved. Twenty automatic and ten explicit isolated port probes all
passed, as did an unchanged fourteen-case restore focus.

For this qualifying local run only, a hash-bound Docker adapter replaced the
automatic loopback port in the exact labelled `works-sdk415-*` Redis creation
request with a free explicit loopback port. It forwards every other argument
and Docker request unchanged. Repository code, CI workflow, actual TCP/PING
admission, caller cancellation, cleanup and every application/domain proof
assertion remain unchanged. A second fourteen-case focus passed before this
run. The adapter logged 23 fixture requests during the gate, plus fourteen in
that focus; the independent observer sampled 22 gate fixtures with successful
host responses and no host error.

The exact adapter, its real-Docker hash, configuration, original/effective
argument lists, focus XML/log and observer are preserved in `closure/`.
Adapter SHA-256 is
`d8921c8cb9d53848984684b73391ccac1c9c87b07e9fb150b920dcdf2909ca25`.
This workaround is not installed globally or added to the product. The separate
full profile passed with ordinary Docker port assignment.

Agents Full remains expected-unavailable and is not a Works prerequisite.
Its actual Release/package nested custody probe passed 129 tests; the SDK guard
failed compilation before executing any test. Its missing coverage and runtime
inputs remain in the component diagnostics. No Agents Full qualification is
claimed or counted as Works parity.

The parent audit is `closure/review-pass1-ci-audit.json.gz`. Ledger SHA-256 is
`49039f67b95840d703a11b993a13e95e5a169febf944b733d12a4137cd5dec0c`.
The parent wrapper restored the approved temporary watch quota to 1,048,576
and read it back successfully. Platform sources remained frozen. No foreign
fleet, global Docker setting, dependency, Git index or history was changed.

`packet-manifest.json` binds all 67 gzip members by stored and uncompressed
SHA-256, byte count and original path. Raw results remain at
`/mnt/wsl/story49-qualification-20261021T072935Z/review-pass1-ci4/gate`.
