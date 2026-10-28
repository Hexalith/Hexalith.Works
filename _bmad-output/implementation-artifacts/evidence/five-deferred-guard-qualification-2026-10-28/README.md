# Five deferred guard qualifications — 2026-10-28

Starting Works revision: `e53439bf08b87ea8041e5147a18a706055ad30db`.
The user requested five deferred items and a push to main. This packet closes four
deliberate follow-up reviews and one missing executing build-graph proof.

| Ledger item | Independent review and executing evidence |
| --- | --- |
| DW-60, prerequisite cancellation | The current shared harness replaces the retired cascade-local probe. Caller cancellation takes precedence before connection and after success, socket failure and cancellation; unrelated cancellation escapes. Eight existing controlled races plus real loopback controls remain in `WorksAppHostSmokeHarnessTests`, outside the `*SmokeTests` exclusion. The new `TcpPrerequisiteProbeAppliesItsOwnTimerToAPendingConnection` exercises the production port/deadline wiring against a connection waiting for cancellation. It pins the two-second default, checks the forwarded port, rejects immediate cancellation with a 1900 ms elapsed floor and bounds completion by ten seconds. |
| DW-72, projection ordering | Named SDK dispatch owns checkpoint, identity and generation ordering; legacy single-aggregate projection is refused. The expanded `StaleNamedRedeliveryReturnsExplicitOutcomeAndPreservesAuthoritativeQueryState` tests stale create after queueing and stale queue after cancellation through a reconstructed coordinator. Every runtime-family document and manifest-discovered history page retains exact bytes and generation; checkpoint, retained sequence/membership and terminal queue absence survive. Both served detail and what's-next queries are asserted. Current conflict, gap and concurrent rebuild controls also pass. |
| DW-74, shared reconciliation | `WorkItemSharedRebuildManifestBuilder.BuildEpoch` rebuilds the complete authoritative graph; ordinary catch-up folds retained family histories. The deleted dispatcher's child-retention heuristic no longer owns this behavior. Current lifecycle tests prove parented-create topology, missing/malformed/foreign/cyclic relationship refusal, atomic visibility, authoritative membership, pending-await pruning, abort/retry, tenant collisions and topology retention through reconstruction. Partition/admission tests preserve the previous generation on malformed inventory. SDK rollback documents are intentionally retained. |
| DW-83, dependency discovery | An independent audit of `MsBuildProjectEvaluation` and `KernelDependencyPolicy` found the four triggering corrections covered: conditional owning declarations, global package references, inherited-property removal and ancestor conditions. `ProjectReferenceGlobalPropertiesSelectChildImportClosure` now executes both `GlobalPropertiesToRemove` and `UndefineProperties`. The complete selected classes also qualify imports, canonical same-basename refusal, freshness, supported lanes, provenance, cycles and native SDK relocation/refusal. |
| Story 4.9, duplicate external nodes | `ParallelSolutionNodeIdentityTests` restores and compiles an isolated package-free diamond through the real root props/targets. Four actual `-m:4` solution builds cover Debug/Release and default/explicit EventStore selection. Every one of six canonical projects executes once with the selected configuration/version and `ShouldUnsetParentConfigurationAndPlatform=false`, and emits its DLL. A separate temporary mutation removes only the transitive-forwarding target; its serialized build reproduces duplicate nodes and the same execution-count oracle refuses it. No production target was changed. |

The runtime and discovery audits were performed by separate agents before the
parent's execution. They identified the timer, terminal stale-replay and metadata
coverage additions above, without establishing a surviving production defect.
The workflow's separate Blind Hunter review is triaged in the associated spec.

Verification:

- Debug Integration and Architecture builds: zero warnings/errors.
- Five Integration classes: **149 passed**, zero failures/errors/skips/not-run.
- Four Architecture classes: **274 passed**, zero failures/errors/skips/not-run.
- Complete final Python tooling suite: **98 passed**, including all **3** graph
  tests with four positive build lanes and the temporary mutation control.
- Script syntax checks and focused Ruff checks passed. The script checker reported
  shellcheck/Ruff unavailable locally; the separate pinned Ruff invocation passed.

`manifest.json` records exact commands, source and assembly hashes, class counts,
raw-evidence hashes and canonical starting submodule identities. Compressed logs
and xUnit XML retain the execution results. The Integration and Architecture
assembly hashes were unchanged before and after their respective test runs.

The required baseline command
`aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json`
exited **2** with **NU1605**, reporting Dapr hosting and EventStore Aspire/
ServiceDefaults downgrades. Its raw output is retained separately. No live
Platform requalification is claimed; real-store abort/concurrency, population,
retention and broader Platform source-closure items remain separate ledger work.
This batch changes test coverage and evidence, with no production runtime change.
