# Story 4.16 review iteration 2 corrections

On 2026-10-19 the canonical Platform verifier **exits 0** on the corrected graph, against named published EventStore **3.125.1**, with status `passed-disposable-synthetic-parity-and-rollback`. All eleven R1–R11 rows derive their persisted, negative, restart and rollback proofs from named phases and qualifying state fields, with zero skips. Story 4.16 stays **in progress** until root's independent review accepts it, and Story 4.9 has not resumed.

```bash
cd /home/administrator/projects/hexalith/platform
bash eng/verify-works-host.sh --works-root /home/administrator/projects/hexalith/works --runtime-sdk-root /tmp/story416-sdk-3.125.1-public-source --artifacts-path /tmp/story416-review2-canonical2
```

The run started at 07:00:21 UTC and finished at 07:24:22 UTC. The [admission result](canonical-admission/result.json) summarizes it. The [ledger](canonical-admission/ledger.json), every command log and test XML, the [host starts](canonical-admission/host-starts.jsonl) and the [persisted outcomes](canonical-admission/persisted-states.jsonl.gz) are retained beside it. The earlier [review iteration 1 packet](../review1-corrections-2026-10-18/README.md) and [public 3.125.1 packet](../public-3.125.1-2026-10-18/README.md) are historical supporting evidence only.

## Canonical results

| Check | Result |
| --- | --- |
| Evidence-gate unit tests | 48 passed (35 host, 13 producer) |
| Agents Full (approved contract) | exit 1 `DependencyNotAvailable: EXT-HOST-1`; LocalScaffold exit 0 |
| Platform source Debug / package Release | 107 / 107 passed, zero skips |
| Works Debug and Release consumer builds | passed; the consumer's and Platform's `Hexalith.Platform.Works.Runtime.dll` are byte-identical (`4bdcb8eb1e4bf7cc…`) |
| Supporting Release consumer classes | 231 passed, zero skips |
| Producer qualification | 2,102 `src/**` and central build files match the official commit, trees and tag `v3.125.1` |
| Live recovery class (restart, callback, restore) | 4/4, 749.8 s |
| Live historical lifecycle/tree/claim/security | 1/1, 135.4 s |
| Live two-host HA, lost firing, stale callback, mTLS | 1/1, 88.7 s |
| Live focused projection rebuild | 1/1, 173.0 s |
| Host starts | nine `platform-target` (one Degraded-expected), one `retained-rollback`, all on owned Scheduler volumes |
| Persisted phases | 32, no duplicate; four cross-phase bindings passed |

## Corrections proven live

- **T78 delegated queries.** The fixture grants no `Works:Query:PermittedCoordinatorWorkloads`. Every query a proof reads is a delegated Executor query: gateway workload `eventstore`, `IsDelegated`, the actor as both actor and user, and only `partyId` in the payload. These are the R5 restart query and the R1, R3, R4 and R5 rebuild, quarantine and rollback queries. A test-only Parties identity authority resolves them on the Platform and retained hosts, and each phase's `queryIdentity` records the envelope and the resolutions the authority observed. On the fourth Platform start a non-delegated full-tenant `whats-next` query is refused as `Forbidden` with zero identity resolutions. The retained host's delegated query finds the Executor item with the post-capture correlation `synthetic-capture-416-f5aed88a90`.
- **Executor-visible source.** The Executor view shows only its own Assigned and the Queued work. The eleven existing recovery sources all end terminal or `InProgress`, so a twelfth source stays Assigned to the synthetic Executor. The focused lane's owner keeps three events (created, assigned, reassigned) and rests Assigned. The synthetic process profile now supports twelve sources, with 1.9-second operations and a 56-second quantum, so that SDK validation still fits (`deferred-work.md` records this for review).
- **T84 composition guard.** No `platform-target` start set a key Platform composed outside three checked shapes. The proof extends the composed hook lists. The captured restorable app-channel secret replaces the per-run secret. The telemetry lanes redirect Aspire's composed OTLP export to the owned loopback collector. The recorded composition shows 8192 rate limits on `eventstore`, callers `eventstore`/`eventstore-admin` and both SDK modes on `works`, and `Development` everywhere. Each value equals the literal in `WorksPreviewModel.cs` and every module's launched and in-process value (T80).
- **T77 runtime identity.** Every `works` and `works-ha` host loaded Platform's hosting startup and startup hook from bytes equal to Platform's own host-output DLL.
- **T83 re-observation.** The restore phase re-inventories the stopped restored fleet before the retained start and re-hashes the runtime binaries inside it. It records observed counts and flags. The re-observed fleet equals the snapshot on every entry except the capture-only DUMP inventory: 722 Redis keys, the logical value inventory, Scheduler inventory (now including the volume root) and every configuration file. The runtime binaries match on all 1,070 entries.
- **Rollback.** The original NoOp receipt replays, the conflicting replay is refused with one durable collision, and date resume, child cancellation and child expiry each count one. The restored sweep is complete with twelve candidates, and the attributed operator audit is restored. The rollback start used the snapshot's Scheduler volume (T101).

## Inputs

The run bound 261 Platform consumer sources, 21 R10 configuration files, the verifier and its three helpers, 141 Works fixture sources and 211 Works consumer sources. [`input-binding.json`](input-binding.json) re-hashes them after the run with zero mismatches. Both repositories carry the corrections as uncommitted working-tree changes: Platform on HEAD `15ef6d9`, Works on HEAD `1ab960c`. No Git, submodule or publication work was performed. Concurrent work by another session in Platform is not an input to this run and was preserved: the Platform EventStore gitlink moved to `682512c`, and an untracked `spec-ext-protection-1-production-engine.md` appeared.

## Preparation, focused diagnostics and failed attempts

These runs are diagnostic only; the canonical run above is the sole admission evidence.

- **[Preparation](preparation/ledger.json).** `--prepare-only` passed on the corrected graph and proved the byte-identical runtime before any live fact.
- **[Focused facts](focused-diagnostics/composite-gates.json).** On the preparation output, the callback (154.9 s), HA (88.8 s), projection (143.0 s), historical (125.3 s) and restart (665.7 s) facts each passed. The verifier's host-record and persisted-outcome gates then passed on their combined records, all eleven rows. The callback diagnostic first showed that Aspire composes the OTLP export keys the telemetry lanes redirect.
- **[Canonical attempt 1](canonical-attempts/attempt1/attempt.json) failed** in the supporting set. One `WorksPlatformFleetRestoreTests` case could not connect to its fresh Redis container's published host port, because the fixture's readiness pinged Redis only through `docker exec`. The fixture now also waits until the host binding answers `PING` (real Docker only). The class then passed 24/24, and attempt 2 is the canonical run above.
- **Environment.** The dapr-init `dapr_redis` container had exited, so it was started before preparation. No inotify change was needed ([environment](environment-capacity.json)).

## Deterministic suites

[Release and Debug](deterministic-suites/) each pass with zero skips: Unit 589, Property 3, Architecture 269, and Integration 908, which excludes the `*SmokeTests` and `*Platform*` classes. The new partition-refusal test runs here, and the composition-guard, runtime-identity and fleet re-observation regressions run in the supporting set above. [`secret-pattern-check.json`](secret-pattern-check.json) finds no credential material in this packet.

## Not claimed

- 4.16 acceptance: independent review remains with the root.
- Story 4.9 resumption.
- Qualification of public EventStore 3.126.0, which appeared on nuget.org on 2026-10-18. The spec keeps 3.125.1, and selecting a newer producer needs an owner decision and a Builds submodule update.
- Real-data or new-catalog admission: the AD-28 gates still apply.
- Production admission: the Works preview stays development/test only, and its broker/network policy hashes are format-checked only until AD-28.
- Full Agents EXT-HOST-1 qualification.
