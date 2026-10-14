# Story 4.14 matrix audit

Audited 2026-10-13 against the frozen spec and the implementation diff from baseline `8708bb935c4306c42d7453b761e203682aad5919`. Story and sprint status remain `in-progress`. This is an implementation acceptance audit; the build workflow has not advanced to review.

The final focused SDK and context-named classes passed 172/172 with no skipped or unrun tests. The isolated live Dapr/Redis run passed all 15 SDK projection tests and the complete restore/replay test. Exact commands and authority-contract evidence are in [implementation-follow-up.md](implementation-follow-up.md); archived command output is indexed in [verification-output.json](verification-output.json).

| Frozen matrix scenario | Passing evidence | Remaining limit |
| --- | --- | --- |
| Live/redelivery/restart | `Ordinary_delivery_and_redelivery_persist_one_complete_runtime_family_and_checkpoint`, `Restart_recovers_checkpoint_and_rejects_conflicting_redelivery`, and `ConcurrentNamedDeliveriesForTwoAggregatesSurviveRebuildPromotionRestartAndRedelivery` verify persisted family documents and checkpoints. The last test also runs against Redis, creates a new client/coordinator, and checks that duplicate named delivery changes no document. | Conflict disposition is assessed separately below. |
| Concurrent rebuild | The two-aggregate named-dispatch regression verifies available stale list/detail reads during sealed capture, promotion, restart, two-stream catch-up, and current reads afterward. `Aborted_concurrent_rebuild_keeps_old_generation_and_allows_live_delivery_afterward` verifies abort drains and resumed delivery. | The frozen SDK deliberately returns unavailable/stale during `CatchingUp` and `Aborting`; Works honors that result. Continuous availability in those phases is not proven. |
| Executor/coordinator query | `PublishedPartiesTransportAuthorizesPersistedExecutorWorkWithSyntheticCurrentAuthority` verifies exact published-client binding and deterministic permitted results against persisted SDK state. Coordinator and contamination tests verify authorization and freshness. | Partial: the successful Executor path uses synthetic authority at the adapter-policy boundary. A successful query through the protected SDK route and authoritative gateway admission remains unproved. |
| Wrong actor/tenant/origin | `Wrong_actor_tenant_and_origin_are_denied_before_any_Works_read`, the published identity transport denial theory, gateway route-spelling theory, and `RejectedNamedProjectionIngressPreservesPersistedFamilyAndCheckpoint` verify no Works reads on denied queries and no persisted family/checkpoint changes on denied named projection ingress. | No successful live Executor admission is inferred from these denial proofs. |
| Corrupt/gapped/conflicting evidence | Malformed-envelope parking, gapped-capture rejection, ordinary-gap parking, and `OrdinaryDeliveryOnAnotherAggregatePreservesTerminalParkingAndDegradedQueries` verify persistent parking, truthful degraded results, and preservation under healthy unrelated delivery. The parking regression also runs against Redis. | Partial: conflicting redelivery is rejected as `IdentityConflict`, but no passing proof establishes its durable unresolved/quarantine disposition and degraded readiness. This matrix row remains incomplete. |
| Rollback | `Complete_isolated_keyspace_restore_removes_sdk_epoch_then_legacy_replay_recovers_queue` restores every captured Redis key, verifies marker removal and legacy state, and replays retained synthetic source. Mode selection tests prove one named writer and a protected legacy conflict route. | The deployed fleet/source-archive and production admission proof remains a Story 4.16/AD-28 gate. |

## Acceptance decision

The authority task and complete matrix-test task stay unchecked. Completion requires supported gateway identity-authority composition and durable conflict-disposition proof; Works must continue to consume those technical-module capabilities rather than add local security or persistence substitutes. Package upgrades are not inherently required by the authority finding.

The source Debug dependency failures, incomplete full integration run, and unsuccessful Aspire baseline recorded in the spec are separate from the passing focused/package/live evidence. The story is not ready for review or completion. Existing submodule changes, frozen intent, package pins, and legacy-default configuration are preserved.

## Evidence checks

Archived output hashes, resolved package identities, test counts, unchanged frozen intent, and the two unchecked task entries were verified. Plain `git diff --check` exits 2 because it classifies the spec's required CRLF terminators on changed lines as trailing whitespace. `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check` exits 0. No repository Git configuration or line-ending policy was changed to obtain that result.

## Continuation audit — 2026-10-14

The parent verified the baseline diff, unchanged frozen requirements, archived hashes, and XML execution results for all 177 focused integration cases. Current commands and prerequisite details are in [implementation-2026-10-14.md](implementation-2026-10-14.md); [verification-2026-10-14.json](verification-2026-10-14.json) also includes the parent rerun after the snapshot-record file move.

| Frozen matrix scenario | Current evidence and disposition |
| --- | --- |
| Live/redelivery/restart | Ordinary delivery, two-aggregate named overlap, restart, and redelivery tests ran and passed. |
| Concurrent rebuild | Capture serves prior-generation stale reads; promotion/catch-up and abort tests ran and passed. Published CatchingUp/Aborting unavailability remains the documented phase limit. |
| Executor/coordinator query | Protected Works Executor HTTP now succeeds with independently configured service authority and synthetic authenticated current Party evidence; permitted/denied coordinator policy tests passed. Real Platform target-actor/Party reader admission remains missing. |
| Wrong actor/tenant/origin | Denial tests passed without Works reads or unauthorized persisted changes. Missing, expired, and rejected service authority were also tested through protected ingress. |
| Corrupt/gapped/conflicting evidence | Corrupt/gap parking and conflicting source-sequence parking passed, including fresh-client Redis recovery. Same-position journal identity conflicts still return IdentityConflict without durable disposition or degraded readiness; this row remains incomplete. |
| Rollback | The final isolated live rerun passed 16/16 projection tests and complete store restore/replay 1/1. Deployed fleet/source archive remains the Story 4.16 gate. |

Story and sprint status remain in-progress. Neither outstanding task is marked complete, and the workflow has not advanced to review. The scoped decision needed is technical-module identity-reader admission plus a producer-owned conflict capability; consuming a changed producer requires renegotiating the frozen EventStore 3.120.0 pin.
