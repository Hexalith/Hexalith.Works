# Story 4.16 public 3.125.1 qualification

On 2026-10-18 the canonical Platform verifier **exits 0** against named published EventStore **3.125.1**, with status `passed-disposable-synthetic-parity-and-rollback`. All eleven R1–R11 rows pass with persisted, negative, restart and rollback evidence and zero skips. The complete fleet rollback to the retained Works host converges. Story 4.16 stays **in progress** until independent review accepts it, and Story 4.9 has not resumed.

```bash
cd /home/administrator/projects/hexalith/platform
bash eng/verify-works-host.sh --works-root /home/administrator/projects/hexalith/works --runtime-sdk-root /tmp/story416-sdk-3.125.1-public-source --artifacts-path /tmp/story416-public-3.125.1-admission-run3
```

The [admission result](canonical-admission/result.json) summarizes the run. The [ledger](canonical-admission/ledger.json), command logs, test XML, [host starts](canonical-admission/host-starts.jsonl) and [persisted outcomes](canonical-admission/persisted-states.jsonl.gz) are retained beside it.

## Producer selection

The standing latest-version instruction selects 3.125.1:

- **Indexes.** All nine official NuGet flat-container indexes list 3.125.1 as the latest stable version at 17:43 UTC.
- **Archives.** Every archive's nuspec names `55432c50d4b6f1666b4bd6d4a60bfc263b2d0e0b`, and the official tag `v3.125.1` names the same commit. The local cache archives match the official downloads byte for byte. See the [receipts](public-producer-receipts.json).
- **Corrections.** Both prepared 4.16 corrections (`9dbb3aaf`) and the 4.15 journal-closeout correction (`216b7906`) are ancestors of the tag.
- **Runtime source.** An exact `git archive` export of the tag supplies the runtime and Debug producer source, without touching the shared SDK checkout. All 22,998 tracked blobs match, including `global.json`, rewritten from its raw blob after archive applied an `eol=crlf` attribute. Its Builds link is the tag's own gitlink, `4629ff2d`. See the [export record](isolated-producer-source.json).
- **Verifier qualification.** The verifier independently qualifies 1,978 runtime and build files against the official commit, the trees and the named tag.

## Canonical results

| Check | Result |
| --- | --- |
| Evidence-gate unit tests | 24 passed |
| Agents Full (approved contract) | exit 1 `DependencyNotAvailable: EXT-HOST-1`; LocalScaffold exit 0 |
| Platform source Debug / package Release | 92 / 92 passed, zero skips |
| Works Debug and Release consumer builds | passed |
| Supporting Release consumer classes | 212 passed, zero skips |
| Live recovery class (restart, callback, restore) | 4/4, 780.5 s |
| Live historical lifecycle/tree/claim/security | 1/1, 124.3 s |
| Live two-host HA, lost firing, stale callback | 1/1, 93.1 s |
| Live focused projection rebuild | 1/1, 150.4 s |
| Host starts | nine `platform-target`, one `retained-rollback`, all on owned Scheduler volumes |

**R4.** The focused rebuild fact is the one that left public 3.124.0 at owner 3 / global 3. It now catches up automatically after Commit to owner 4 / global 14, and the old generation stays readable during capture.

**Rollback.** The complete-family fact restores 682 Redis keys and 1,070 runtime entries, and both inventories equal the snapshot. Restored configuration stays byte-identical. Retained queries return the item and the original receipt replays, with exactly one resume, one child cancellation and one child expiry. The restored recovery sweep completes with zero unresolved work.

**HA.** The two-host fact observes a real stale callback after the primary stops, removes the retired reminder and resumes exactly once. Its sentry trust check accepts the configured root and denies a foreign root.

## I/O matrix coverage

| Scenario | Executed coverage |
| --- | --- |
| Startup/restart | `periodic-writers-disabled-readiness-degraded`, `disabled-recovery-otlp-alert-export`, `durable-recovery-alert-export-and-clear`, every `*-restart-*` phase, four Platform restarts |
| Historical scenarios | `platform-historical-lifecycle-tree-concurrent-claim-persisted`, `platform-historical-restart-replay-and-family`, paged process phases, reminder reissue, `platform-named-redelivery-capture-through-commit-complete-family`, `platform-focused-projection-rebuild` |
| Wrong origin/policy | `platform-executor-filtering-and-origin-tenant-publish-refusals`, HA foreign-root denial, protected disposition and audit-outage refusal, plus the Platform profile/ACL and protected-HTTP supporting suites |
| Complete fleet rollback | `complete-platform-fleet-snapshot`, then `complete-restore-retained-host-query-and-receipt-replay` |

## Changes qualified by this run

- **Works pins.** `Directory.Packages.props`, `Directory.Build.props` and the IntegrationTests `WorksPinnedEventStoreVersion` select 3.125.1.
- **Works live harness.** `WorksAppHostDiscoveryIsolation` gives every sidecar of one start a run-owned SQLite name-resolution registry. The registry is derived from the sidecar's exact configuration, uses rollback journaling with a busy timeout, and is removed at teardown. Twelve deterministic `WorksAppHostDiscoveryIsolationTests` cover it.
- **Platform `apphost.cs`.** It selects `Hexalith.EventStore.Aspire` and `ServiceDefaults` 3.125.1, matching the Builds catalog.
- **Platform verifier.**
  - It reads the version from the catalog and refuses a differing Works default pin.
  - It binds the runtime source to the official `v<version>` tag.
  - It requires the focused projection fact for R4 and runs the discovery-isolation tests as supporting evidence.
  - Its errors name the test runner's assembly list.
- **Platform inputs.** The owner committed the verifier, helpers, 261 consumer sources, R10 configuration and `apphost.cs` during the run as `d8b444d`. Every input is byte-identical to the hashes the run recorded.
- **Works inputs.** The Works consumer is HEAD `1ab960c` plus the uncommitted files listed in the result.

## Failed attempts

- **[Attempt 1](attempt1-inotify-exhaustion/diagnosis.json).** `dapr-sentry` could not add a file watch. Foreign sessions held 1,342,482 inotify watches for uid 1000, above the 1,048,576 runtime limit. A temporary runtime-only increase was [restored afterwards](environment-capacity.json); no foreign process was stopped.
- **[Attempt 2](attempt2-mdns-cross-talk/diagnosis.json).** Default mDNS discovery round-robined Works' `eventstore` invocations onto an unrelated session's non-mTLS sidecar. Every other workload read failed with `tls: first record does not look like a TLS handshake`. Diagnostic runs reproduced the failure and exposed a WAL initialization race in the first SQLite isolation. A rollback-journal registry then passed the full restore fact (non-qualifying diagnostics, retained in the same folder).

## Story 4.15 live input

Story 4.15 handed 4.16 a live requalification on the final decoder. [`run-source-debug-lane.py`](run-source-debug-lane.py) builds Works and the exact `v3.125.1` export in Debug source mode, then runs the retained-host facts. [Lane 3](source-debug-lane/lane3/results.json) passes the restart fact (ten persisted phases, 356.1 s) and the callback fact (32.4 s), with zero skips. Lanes 1 and 2 failed control-plane startup readiness before any fixture assertion: on a restart, EventStore placement was disconnected and the placement health probe stayed unhealthy. Both failures are retained.

## Deterministic suites

[Release and Debug](deterministic-suites/) each pass with zero skips: Unit 589, Property 3, Architecture 269, and Integration 907, which excludes the `*SmokeTests` and `*Platform*` classes. Release restores the public 3.125.1 packages; Debug builds the `v3.125.0` gitlink source.

## Not claimed

- 4.16 acceptance: three independent review lenses remain with the root.
- Story 4.9 resumption.
- Real-data or new-catalog admission: the AD-28 gates still apply.
- Production admission: the Works preview stays development/test only.
- Full Agents EXT-HOST-1 qualification.

A direct developer run of the preview outside this fixture still uses mDNS discovery; [deferred-work.md](../../../deferred-work.md) records that gap.
