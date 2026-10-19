# Story 4.16 review iteration 1 corrections

On 2026-10-18 the canonical Platform verifier **exits 0** on the corrected graph, against named published EventStore **3.125.1**, with status `passed-disposable-synthetic-parity-and-rollback`. All eleven R1–R11 rows derive their persisted, negative, restart and rollback proofs from named phases and qualifying state fields, with zero skips. Story 4.16 stays **in progress** until root's independent review accepts it, and Story 4.9 has not resumed.

```bash
cd /home/administrator/projects/hexalith/platform
bash eng/verify-works-host.sh --works-root /home/administrator/projects/hexalith/works --runtime-sdk-root /tmp/story416-sdk-3.125.1-public-source --artifacts-path /tmp/story416-review1-canonical1
```

The run started at 21:45:37 UTC and finished at 22:15:02 UTC. The [admission result](canonical-admission/result.json) summarizes it. The [ledger](canonical-admission/ledger.json), every command log and test XML, the [host starts](canonical-admission/host-starts.jsonl) and the [persisted outcomes](canonical-admission/persisted-states.jsonl.gz) are retained beside it. The earlier [public 3.125.1 packet](../public-3.125.1-2026-10-18/README.md) is historical supporting evidence only.

## Canonical results

| Check | Result |
| --- | --- |
| Evidence-gate unit tests | 38 passed (25 host, 13 producer) |
| Agents Full (approved contract) | exit 1 `DependencyNotAvailable: EXT-HOST-1`; LocalScaffold exit 0 |
| Platform source Debug / package Release | 98 / 98 passed, zero skips |
| Works Debug and Release consumer builds | passed |
| Supporting Release consumer classes | 222 passed, zero skips |
| Producer qualification | 2,102 `src/**` and central build files match the official commit, trees and tag `v3.125.1` |
| Live recovery class (restart, callback, restore) | 4/4, 874.2 s |
| Live historical lifecycle/tree/claim/security | 1/1, 143.9 s |
| Live two-host HA, lost firing, stale callback, mTLS | 1/1, 105.4 s |
| Live focused projection rebuild | 1/1, 182.5 s |
| Host starts | nine `platform-target` (one Degraded-expected), one `retained-rollback`, all on owned Scheduler volumes |

## Corrections proven live

- **T1 hosting composition.** Every `platform-target` `works` host, and the HA `works-ha` replica, loaded `Hexalith.Platform.Works.Runtime` first and the proof second. Each recorded `platformRuntimeEnabled=true`, `platformTelemetryRegisteredBeforeProof=true` and exactly one telemetry publisher registration. The verifier refused nothing because nothing was replaced; its unit tests cover each refusal.
- **T3 rate limits.** Parity ran at the four 8192 development/test limits Platform now composes; the fixture injected none.
- **T5 proofs.** The ledger's `requiredProofs` lists, for each row and proof, the phases, their record hashes and the checked fields. 32 phases were recorded with no duplicate. Four cross-phase checks passed: the restart family equals the persisted family, and the restored fleet, runtime binaries and replayed receipt equal the Platform-written snapshot.
- **T6 readiness.** The callback start records `readinessPassed=false` with `worksReadinessExpectation=Degraded`; the other nine starts passed readiness.
- **T7 mTLS.** The probe reached the Works sidecar's internal gRPC endpoint. A client certificate signed by the owned Sentry issuer completed HTTP/2 setup. The sidecar itself refused a client without a certificate (`tlsv13 alert certificate required`) and a foreign-CA client (`bad certificate`). The probe accepted the server only as `spiffe://public/ns/default/works` and rejected it when expecting `spiffe://public/ns/default/eventstore`.
- **R9 negative.** A fully credentialed request to the retired `/project` route returned the canonical SDK's `404`; without the channel credential it returned `401`. The accepted family digest was unchanged.
- **Rollback.** The restore reproduced 640 Redis keys and 1,070 runtime entries. Retained queries, the original NoOp receipt replay, one date resume, one child cancellation, one child expiry, the attributed operator audit and a complete restored sweep converge.
- **R4.** The focused rebuild caught up automatically after Commit to owner 4 / global 14. After three restarts, all eleven named projection heads converged naturally in one poll (209 ms).

## Inputs

The run bound 261 Platform consumer sources, 21 R10 configuration files, the verifier and its helpers, 41 Works fixture sources and 211 Works consumer sources. [`input-binding.json`](input-binding.json) re-hashes them after the run with zero mismatches. Both repositories carry the corrections as uncommitted working-tree changes: Platform on HEAD `2d5178f`, Works on HEAD `1ab960c`. No Git, submodule or publication work was performed. Unrelated concurrent Platform changes (story 1.10 files, `README.md` and the Platform EventStore gitlink) are not inputs to this run and were preserved.

## Preparation and focused diagnostics

These runs are diagnostic only; the canonical run above is the sole admission evidence.

- **[Preparation](preparation/ledger.json).** `--prepare-only` passed on the corrected graph before any live fact.
- **[HA attempt 1](focused-diagnostics/ha1/run.json) failed.** The owned Sentry issues Ed25519 credentials, and .NET cannot sign a certificate with them (`'1.3.101.112' is not a known key algorithm`). Its host-start record already showed Platform's hosting startup loading first in `works` and `works-ha`. The probe now has the host OpenSSL CLI sign its client certificate.
- **[HA attempt 2](focused-diagnostics/ha2/run.json) passed** with the server-side refusals above.
- **[Callback attempt 1](focused-diagnostics/callback1/run.json) failed** before any assertion: `dapr-sentry` could not add a file watch (`no space left on device`). A foreign `fluentui-mcp` process held 1,037,468 of the 1,048,576 inotify watches for uid 1000. A temporary runtime-only limit of 2,097,152 was set and [restored](environment-capacity.json) after every owned fleet stopped. No foreign process was stopped.
- **[Callback attempt 2](focused-diagnostics/callback2/run.json) passed**, including the new R9 legacy-route phase.

## Deterministic suites

[Release and Debug](deterministic-suites/) each pass with zero skips: Unit 589, Property 3, Architecture 269, and Integration 907, which excludes the `*SmokeTests` and `*Platform*` classes. The new `WorksPlatformHostingCompositionTests` and the Docker-backed Scheduler inventory regression run in the supporting set above.

## Not claimed

- 4.16 acceptance: independent review remains with the root.
- Story 4.9 resumption.
- Real-data or new-catalog admission: the AD-28 gates still apply.
- Production admission: the Works preview stays development/test only, and its broker/network policy hashes are format-checked only until AD-28.
- Full Agents EXT-HOST-1 qualification.
