# Story 4.16 review iteration 3 corrections

On 2026-10-19 the canonical Platform verifier **exits 0** on the corrected graph, against named published EventStore **3.125.1**, with status `passed-disposable-synthetic-parity-and-rollback`. All eleven R1–R11 rows derive their persisted, negative, restart and rollback proofs from named phases and qualifying state fields, with zero skips. Story 4.16 stays **in progress** until root's independent review accepts it, and Story 4.9 has not resumed.

```bash
cd /home/administrator/projects/hexalith/platform
bash eng/verify-works-host.sh --works-root /home/administrator/projects/hexalith/works --runtime-sdk-root /tmp/story416-sdk-3.125.1-public-source --artifacts-path /tmp/story416-review3-canonical1
```

The run started at 13:52:46 UTC and finished at 14:16:23 UTC (23.6 minutes). It was the first canonical attempt on this graph. The [admission result](canonical-admission/result.json) summarizes it. The [ledger](canonical-admission/ledger.json), every command log and test XML, the [host starts](canonical-admission/host-starts.jsonl) and the [persisted outcomes](canonical-admission/persisted-states.jsonl.gz) are retained beside it. The earlier [review iteration 2 packet](../review2-corrections-2026-10-19/README.md), [review iteration 1 packet](../review1-corrections-2026-10-18/README.md) and [public 3.125.1 packet](../public-3.125.1-2026-10-18/README.md) are historical supporting evidence only.

## Canonical results

| Check | Result |
| --- | --- |
| Evidence-gate unit tests | 50 passed (37 host, 13 producer) |
| Agents Full (approved contract) | exit 1 `DependencyNotAvailable: EXT-HOST-1`; LocalScaffold exit 0 |
| Platform source Debug / package Release | 110 / 110 passed, zero skips |
| Works Debug and Release consumer builds | passed; the consumer's and Platform's `Hexalith.Platform.Works.Runtime.dll` are byte-identical (`3fe0dde52757c8cc…`) |
| Supporting Release consumer classes | 240 passed in 16 classes, zero skips |
| Producer qualification | 2,102 `src/**` and central build files match the official commit, trees and tag `v3.125.1` |
| Live recovery class (restart, callback, restore) | 4/4, 779.7 s |
| Live historical lifecycle/tree/claim/security | 1/1, 143.3 s |
| Live two-host HA, lost firing, stale callback, mTLS | 1/1, 88.5 s |
| Live focused projection rebuild | 1/1, 117.8 s |
| Host starts | nine `platform-target` (one Degraded-expected, recorded `readinessPassed=false`), one `retained-rollback`, all on owned Scheduler volumes |
| Persisted phases | 32, no duplicate; four cross-phase bindings passed |

The runtime DLL hash differs from iteration 2's `4bdcb8eb…` because Platform HEAD moved from `15ef6d9` to `049e7bc` and the build embeds the commit in the informational version. Both compiles in this run are identical.

## Corrections in this run

- **T131 EventStore receiver callers.** The run bound the edited `DaprComponents/accesscontrol.yaml` (`c6076e9d…`) and `accesscontrol.eventstore-admin.yaml` (`f8db9ef9…`) as R10 configuration consumers. Platform's composition points every `eventstore` sidecar at that receiver policy. The receiver keeps `defaultAction: deny` and the eight exact `works` routes. It adds the selected producer's production-template `eventstore-admin` grant: `/**` with GET, POST and PUT, trust domain `public`, namespace `default`. `WorksDaprPolicyTests` pins exactly those two callers and operations in both Platform lanes. Every live lane passed with the grant in place. No live phase asserts an Admin.Server invocation of EventStore; the grant's admission is proven by the pinned policy, not by a live call.
- **T132 alert raise after degradation.** In the callback lane, `/ready` reported Degraded at 13:57:56.941 UTC. The collector had already received 14 `alert=1` points, the publisher's Unavailable startup exports, and the old predicate would have accepted any of them. The wait and the verifier's R2 negative accepted only the raise received after that observation. The creation-gap phase observed Degraded at 14:04:21.229 UTC and recorded a raise received at 14:04:24.108 UTC.
- **T133 composition guard.** Every `platform-target` start passed the extended guard, which also compares each Platform key's value after the fixture's callbacks. The recorded overlaps stayed within the permitted shapes. `APP_API_TOKEN` appeared on every composed resource. On `works`, the hook-list extensions appeared on every start, and the OTLP redirect appeared only on the five starts of the two telemetry lanes (callback and restart). The recorded composition shows 8192 rate limits on `eventstore`, callers `eventstore`/`eventstore-admin` and both SDK modes on `works`, and `Development` everywhere.
- **T157 trust evidence.** The HA phase's trust record carries only observed fields. The sidecar refused a client without a certificate (`tlsv13 alert certificate required`) and a foreign-CA client (`bad certificate`), and the probe rejected a mismatched SPIFFE identity. The literal `domainRequests` is gone.
- **T123, T129, T130, T155.** The supporting set now includes `WorksAppHostSelectionTests` (6) and `WorksSdkPackageContractTests` (2). The Platform lanes run the new guard, retry de-duplication and 256-attempt tests. The evidence-gate tests prove that `RuntimeError`, `http.client.IncompleteRead` and failures before the first command reach the ledger.
- **Rollback.** The snapshot held 721 Redis keys and 1,070 runtime entries, and the re-observed restored fleet equals it. The retained host's delegated Executor query found the item with the post-capture correlation `synthetic-capture-416-0afbd36c0e`. The original NoOp receipt replays, and the conflicting replay is refused with one durable collision. Date resume, child cancellation and child expiry each count one. The restored sweep is complete with twelve candidates, and the attributed operator audit is restored. A non-delegated full-tenant query was refused as `Forbidden` with zero identity resolutions.

## Inputs

The run bound 261 Platform consumer sources, 21 R10 configuration files, the verifier and its three helpers, 141 Works fixture sources and 211 Works consumer sources. [`input-binding.json`](input-binding.json) re-hashes them after the run with zero mismatches. Platform HEAD is `049e7bc`; the owner committed the iteration 2 corrections there, together with unrelated work. The iteration 3 corrections are uncommitted working-tree changes in Platform and in Works (HEAD `1ab960c`). Platform's EventStore gitlink (`682512c`, another session's change) is not an input: the run used the isolated `v3.125.1` export. No Git, submodule or publication work was performed.

## Supporting checks and attempts

These checks are diagnostic only; the canonical run above is the sole admission evidence.

- **No failed canonical attempt.** The first canonical attempt on the corrected graph is the run above.
- **[Mutation checks](supporting-checks/mutation-checks.json).** Each new deterministic test failed against a deliberately weakened source, and the exact bytes were restored before the suites and the run. The weakened sources were the guard without its combined comparison, each of the three synthetic-authority guards, the retry de-duplication, and the capacity cap removed or moved to 255.
- **[Platform composition Works preview tests](supporting-checks/platform-composition-works-preview.xml).** `WorksPreviewComposesItsPreservedGraphWithoutDeclarationServers`, `WorksPreviewPreservesItsMissingSiblingRefusal` and `FileEntryRefusesMixedWorksAndTimesheetsBeforeSelectingEitherGraph` pass 3/3 on the package Release graph with the edited policies.
- **Environment.** No inotify change was needed, and `dapr_redis` was already running ([environment](environment-capacity.json)). The fixture removed every owned container and volume after the run.

## Deterministic suites

[Release and Debug](deterministic-suites/) each pass with zero skips: Unit 589, Property 3, Architecture 269, and Integration 908, which excludes the `*SmokeTests` and `*Platform*` classes. The new composition-guard test runs in the verifier's supporting set, and the new Platform runtime and policy tests run in its Platform lanes. [`secret-pattern-check.json`](secret-pattern-check.json) finds no credential material in this packet. The [tools](tools/) folder holds the scripts that ran the suites, assembled the canonical evidence, re-bound its inputs, scanned the packet and wrote the [manifest](manifest.json).

## Not claimed

- 4.16 acceptance: independent review remains with the root.
- Story 4.9 resumption.
- Qualification of public EventStore 3.126.0, which appeared on nuget.org on 2026-10-18. The spec keeps 3.125.1, and selecting a newer producer needs an owner decision and a Builds submodule update.
- Real-data or new-catalog admission: the AD-28 gates still apply.
- Production admission: the Works preview stays development/test only, and its broker/network policy hashes are format-checked only until AD-28.
- Full Agents EXT-HOST-1 qualification.
