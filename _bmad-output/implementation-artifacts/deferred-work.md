# Deferred Work

This is the maintained work ledger. Entries with an `archived` date retain their lifecycle summary here;
find the matching **DW number** in the [full archive](deferred-work-archive.md) for the original location,
reason, decisions and resolution evidence. The ledger's current status takes precedence over an older
archived snapshot. Entries without DW numbers are identified by their `source_spec` and summary.

The archive was first compacted on 2026-09-18 in commit
[`5856fab739d37e9d7f34ec9b0f5d2df30fe400e9`](https://github.com/Hexalith/Hexalith.Works/commit/5856fab739d37e9d7f34ec9b0f5d2df30fe400e9).
Historical findings describe the code at their recorded review, not necessarily the current implementation.
Keep original evidence and human decisions; append dated corrections or resolutions when rechecking them.
See the [2026-10-23 tracking reconciliation](spec-five-deferred-tracking-integrity.md) for metadata repairs
and the [concurrent Story 4.8 review audit](#story-4-8-concurrent-review-audit) for superseded topology claims.

### DW-1: Kernel-purity test is transitive-blind

status: done 2026-08-27
resolution: resolved by sweep bundle dw-kernel-transitive-dependency-guard
resolution-undo: 5205efa379203d325360cd1365decb0d05f518a3d7b9e85ef9dae1db9076c5d5 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
archived: 2026-09-18

### DW-2: Warnings-as-errors defeats the "scaffolding phase" analyzer intent

status: done 2026-08-27
resolution: resolved by sweep bundle dw-analyzer-severity-policy-alignment
resolution-undo: c5cbd6d2aa8b399fd48aeec5810f4a0dc116522225ae7a930158488d49ad588a 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
archived: 2026-09-18

### DW-3: Placeholder tests prove little

status: done 2026-08-27
origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
archived: 2026-09-18

### DW-4: Rejection-event sequencing & stream-persistence contract

status: done 2026-08-27
resolution: resolved by sweep bundle dw-envelope-canonical-sequencing
resolution-undo: 8ddd93e83ece5733892f7e3c22229ca6bbb11498dba824730dc963a106b8c774 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
archived: 2026-09-18

### DW-5: Self-parent reference accepted

status: done 2026-08-27
origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
archived: 2026-09-18

### DW-6: `ConversationCorrelationId` is unvalidated

status: done 2026-09-01
origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
archived: 2026-09-18

### DW-7: Persisted parent roll-up never converges to child progress

status: done 2026-08-27
resolution: resolved by sweep bundle dw-refuse-stale-persisted-rollups
resolution-undo: ddefab4380f8905628c9b3aeb714f69399d7c2991b6b63f91621f55d32918f0e 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
archived: 2026-09-18

### DW-8: "Mutation-validated cross-tenant negative tests" gate does not exist

status: done 2026-08-27
resolution: resolved by sweep bundle dw-rollup-tenant-isolation-gate
resolution-undo: 158f7ee039cd50584e23ba256190839eb05a20aea675c2c6641408a138bb8661 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
archived: 2026-09-18

### DW-9: `Apply(ReEstimated)` trusts a stored mismatched Unit

status: done 2026-08-27
resolution: resolved by sweep bundle dw-reestimate-replay-unit-hardening
resolution-undo: e631ddbe2597aa647e13c95e6f52b4eb765756181d6ec852e8a9099f23deb037 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
archived: 2026-09-18

### DW-10: Completed by Story 4.7 — Tier-3 fixed aggregate id and live gateway repair.

status: done 2026-08-27
origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
archived: 2026-09-18

### DW-11: Works's Dapr pubsub component has zero access-control scoping

status: done 2026-08-27
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream (2026-07-22)"), 2026-08-27
archived: 2026-09-18

### DW-12: Cascade checkpoint index is a single global cross-tenant key rewritten O(2N) per cascade

status: done 2026-08-27
resolution: resolved by sweep bundle dw-cascade-transition-only-indexing
resolution-undo: e628801ff9ae6ab4db426ccc16253fa1cbd02b0a5fec85d459aa1569eff7a955 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 (2026-07-22)"), 2026-08-27
archived: 2026-09-18

### DW-13: Subscription endpoint drops unbindable envelope bodies into a Dapr poison-retry loop

status: done 2026-08-27
resolution: resolved by sweep bundle dw-dapr-subscription-topology-hardening
resolution-undo: 9d0b70160443abe5d272b224acf45f97375b4700088e4a908274f27e6fad285a 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 (2026-07-22)"), 2026-08-27
archived: 2026-09-18

### DW-14: Child-completion await-clearing on terminal parent events is untested

status: done 2026-08-27
resolution: resolved by sweep bundle dw-recovery-edge-case-test-hardening
resolution-undo: 0ab05a78f4d20558bd4d462f7ffa46eb6c6a2aabdce71d243420206f93c19db9 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
archived: 2026-09-18

### DW-15: Stale-prune exact-boundary is untested

status: done 2026-08-27
resolution: resolved by sweep bundle dw-recovery-edge-case-test-hardening
resolution-undo: 0ab05a78f4d20558bd4d462f7ffa46eb6c6a2aabdce71d243420206f93c19db9 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
archived: 2026-09-18

### DW-16: Tier-3 cascade smoke-lane skip message does not report which port was absent

status: done 2026-08-27
resolution: resolved by sweep bundle dw-recovery-edge-case-test-hardening
resolution-undo: 0ab05a78f4d20558bd4d462f7ffa46eb6c6a2aabdce71d243420206f93c19db9 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
archived: 2026-09-18

### DW-17: SDK-misbind characterization test asserts only bare inequality

status: done 2026-08-27
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
archived: 2026-09-18

### DW-18: AppHost topology test uses presence-only assertions

status: done 2026-08-27
resolution: resolved by sweep bundle dw-dapr-subscription-topology-hardening
resolution-undo: 9d0b70160443abe5d272b224acf45f97375b4700088e4a908274f27e6fad285a 2026-08-27 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
archived: 2026-09-18

### DW-19: The governed kernel project set is a hard-coded four-name list that nothing reconciles against what actually exists under `src/`, so a fifth kernel project would be silently ungoverned.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 476e642905c2
source_spec: `spec-kernel-transitive-dependency-guard.md`
archived: 2026-09-18

### DW-20: The forbidden-family taxonomy exists twice with nothing reconciling the two lists: the direct project-file text scan keeps its own literal string list while the evaluated-closure policy keeps structured families.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 57d35d9aaaaf
source_spec: `spec-kernel-transitive-dependency-guard.md`
archived: 2026-09-18

### DW-21: Two further kernel-purity fitness tests keep their own hand-maintained kernel project lists that did not adopt the centralized governed set, and one of them omits Reactor.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e
origin: spec-deferred aa6c514b60ba
source_spec: `spec-kernel-transitive-dependency-guard.md`
archived: 2026-09-18

### DW-22: `IsFrameworkLibrary` exempts every `Microsoft.*` and `System.*` name from segment-based classification, so a Microsoft-branded adapter is governed only when an explicit rule names it.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 295933146e55
source_spec: `spec-kernel-transitive-dependency-guard.md`
archived: 2026-09-18

### DW-23: Follow-up review still recommended for dw-kernel-transitive-dependency-guard after the damping cap was spent

status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e
origin: review-budget-followup
source_spec: `spec-kernel-transitive-dependency-guard.md`
archived: 2026-09-18

### DW-24: The 14-arm supported-payload allowlist has no drift guard, so a new work-item event type added to Contracts but omitted from the switch is silently refused with no failing test.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 578a38dc7a24
source_spec: `spec-rollup-tenant-isolation-gate.md`
archived: 2026-09-18

### DW-25: Exposed child order follows HashSet insertion order, so replays that permute delivery order can expose the same children in different order, and the convergence property cannot observe it.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 8d7db14257f2
source_spec: `spec-rollup-tenant-isolation-gate.md`
archived: 2026-09-18

### DW-26: ChildContributionCount counts children that passed the output filter, not children that actually contributed effort, so the public contract's name overstates what the number means.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 10a80ad1448a
source_spec: `spec-rollup-tenant-isolation-gate.md`
archived: 2026-09-18

### DW-27: Follow-up review still recommended for dw-rollup-tenant-isolation-gate after the damping cap was spent

status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e
origin: review-budget-followup
source_spec: `spec-rollup-tenant-isolation-gate.md`
archived: 2026-09-18

### DW-28: A persisted same-unit ReEstimated event with a negative estimate can still throw during aggregate replay.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-negative-reestimate-replay-guard
resolution-undo: f63efa4ed3687450c195d3e89ce77d5c1aad677a260b68a7045fd7d20934e66a 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 92fa2ce13a28
source_spec: `spec-reestimate-replay-unit-hardening.md`
archived: 2026-09-18

### DW-29: Make endpoint result mapping fail retryably for unknown future EventStoreDomainEventProcessingResult values.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e
origin: spec-deferred c3f0a312e23e
source_spec: `spec-dapr-subscription-topology-hardening.md`
archived: 2026-09-18

### DW-30: The resiliency CRD's statestore target declares retry/timeout/circuitBreaker at the top level instead of under inbound/outbound, so Dapr drops those policies.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e
origin: spec-deferred eaa2c317b239
source_spec: `spec-dapr-subscription-topology-hardening.md`
archived: 2026-09-18

### DW-31: Nothing consumes, drains, alerts on, or documents the deadletter.work.events topic.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e
origin: spec-deferred b7f701b76c90
source_spec: `spec-dapr-subscription-topology-hardening.md`
archived: 2026-09-18

### DW-32: Follow-up review still recommended for dw-dapr-subscription-topology-hardening after the damping cap was spent

status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e
origin: review-budget-followup
source_spec: `spec-dapr-subscription-topology-hardening.md`
archived: 2026-09-18

### DW-33: External test cancellation is converted into an unavailable-port result by the pre-existing TCP probe.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-probe-cancellation-propagation
resolution-undo: a9b2a73b17dc064fa75b9944c43a3245b866510db555c0962a1bd902c3b09e94 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 9ae6f9653e9f
source_spec: `spec-recovery-edge-case-test-hardening.md`
archived: 2026-09-18

### DW-34: Rejection payloads are now durable persisted bytes but have no entry in the frozen golden-payload corpus; their shape freeze lives only in an in-test signature table.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 72838eb68eb0
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-35: Snapshot-backed rehydration after a persisted rejection is unproven.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 5529e78a3460
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-36: Several older documentation paragraphs still say the v1 catalog "stays 36" while the fitness-asserted count is 37.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-v1-catalog-count-reconciliation
resolution-undo: d14ab66c4bb7ff2377be30a1297960dd1e5973c6cae6066ca9e75a455bf8debe 2026-08-28 7374617475733a206f70656e
origin: spec-deferred c4c1db7ffe36
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-37: Mid-stream and repeated-rejection envelope/payload divergence is unproven at the persistence layer.

status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e
origin: spec-deferred efac8f417df1
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-38: The golden-payload corpus is camelCase while the bytes EventStore actually persists are PascalCase, yet both are documented as "the EventStore-persisted form".

status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 6b155a733168
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-39: No executable test proves that a rejection DomainResult routed through the EventStore command pipeline reaches persistence; only source-text characterization covers it.

status: done 2026-08-28
origin: spec-deferred 4aa7d4178162
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-40: The claim tests point at a Story 4.5 Aspire lane for live ETag conflict/retry coverage that does not exist.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-claim-conflict-proof-docs
resolution-undo: 6cf3d6d05557a2a1522c8b916f3049ee5b337631786714f9bb52a5fba22394a8 2026-08-29 7374617475733a206f70656e
origin: spec-deferred 96069162f44f
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-41: Three ScaffoldGovernanceTests fitness method names still end "AndCatalogStays36" while the assertion in the same methods is polymorphicCatalogCount.ShouldBe(37).

status: done 2026-08-28
resolution: resolved by sweep bundle dw-v1-catalog-count-reconciliation
resolution-undo: d14ab66c4bb7ff2377be30a1297960dd1e5973c6cae6066ca9e75a455bf8debe 2026-08-28 7374617475733a206f70656e
origin: spec-deferred 3ac7ddef56e1
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-42: Follow-up review still recommended for dw-envelope-canonical-sequencing after the damping cap was spent

status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e
origin: review-budget-followup
source_spec: `spec-envelope-canonical-sequencing.md`
archived: 2026-09-18

### DW-43: A delayed older full-replay request can overwrite newer persisted work-item projection state.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-projection-write-ordering-guard
resolution-undo: bb280afa3e98e0e59e3943ec36b0029d2739c580568bb8b7779f4b9278ed6480 2026-08-29 7374617475733a206f70656e
origin: spec-deferred 1362c100f6fb
source_spec: `spec-refuse-stale-persisted-rollups.md`
archived: 2026-09-18

### DW-44: Per-item roll-up persistence does not use the documented optimistic-concurrency write policy.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-projection-write-ordering-guard
resolution-undo: bb280afa3e98e0e59e3943ec36b0029d2739c580568bb8b7779f4b9278ed6480 2026-08-29 7374617475733a206f70656e
origin: spec-deferred 6baca031f280
source_spec: `spec-refuse-stale-persisted-rollups.md`
archived: 2026-09-18

### DW-45: Roll-ups and tenant-index entries persisted before the refusal change keep their stale child-dependent totals until their own aggregate is dispatched again.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-shared-rollup-reconciliation
resolution-undo: 58378fc662e0ae296c6b3166d2b6631fbfb9de554de32a461c8865100db10bdb 2026-08-29 7374617475733a206f70656e
origin: spec-deferred ced4955ae997
source_spec: `spec-refuse-stale-persisted-rollups.md`
archived: 2026-09-18

### DW-46: A parent whose children were attached by a parented create still publishes a rolled total that silently omits them.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-shared-rollup-reconciliation
resolution-undo: 58378fc662e0ae296c6b3166d2b6631fbfb9de554de32a461c8865100db10bdb 2026-08-29 7374617475733a206f70656e
origin: spec-deferred 8b641af15c5f
source_spec: `spec-refuse-stale-persisted-rollups.md`
archived: 2026-09-18

### DW-47: Follow-up review still recommended for dw-cascade-transition-only-indexing after the damping cap was spent

status: done 2026-08-28
origin: review-budget-followup
source_spec: `spec-cascade-transition-only-indexing.md`
archived: 2026-09-18

### DW-48: Exact dependency-direction allowlists inspect literal project files but not ProjectReference items introduced by imported MSBuild props or targets.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-msbuild-dependency-discovery
resolution-undo: a3ed9f095f783e0e1a9344fb936307fa6a5b319403a9451b8aec9f36112eb63f 2026-08-29 7374617475733a206f70656e
origin: spec-deferred f984e193f381
source_spec: `spec-kernel-governance-drift-hardening.md`
archived: 2026-09-18

### DW-49: Evaluated dependency artifact freshness does not cover the complete custom MSBuild import closure.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-msbuild-dependency-discovery
resolution-undo: a3ed9f095f783e0e1a9344fb936307fa6a5b319403a9451b8aec9f36112eb63f 2026-08-29 7374617475733a206f70656e
origin: spec-deferred 94642d4aefa7
source_spec: `spec-kernel-governance-drift-hardening.md`
archived: 2026-09-18

### DW-50: Exact ProjectReference allowlists normalize by project filename rather than canonical evaluated path identity.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-msbuild-dependency-discovery
resolution-undo: a3ed9f095f783e0e1a9344fb936307fa6a5b319403a9451b8aec9f36112eb63f 2026-08-29 7374617475733a206f70656e
origin: spec-deferred ef210fd53a67
source_spec: `spec-kernel-governance-drift-hardening.md`
archived: 2026-09-18

### DW-51: The Hexalith-source consumption gate still reads PackageReference and PackageVersion item specifications raw, outside the shared fail-closed discovery.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-msbuild-dependency-discovery
resolution-undo: a3ed9f095f783e0e1a9344fb936307fa6a5b319403a9451b8aec9f36112eb63f 2026-08-29 7374617475733a206f70656e
origin: spec-deferred 36cdab656fd8
source_spec: `spec-kernel-governance-drift-hardening.md`
archived: 2026-09-18

### DW-52: ExposedChildCount remains independently constructible from ChildWorkItemIds and can represent an inconsistent read model.

status: done 2026-08-29
resolution: resolved by sweep bundle dw-rollup-exposed-count-invariant
resolution-undo: 94e3b0b28087e68fc7ca936676f0f4614d1a516c98becc3949cd282a37ac1c48 2026-08-29 7374617475733a206f70656e
origin: spec-deferred 124b7489f14f
source_spec: `spec-rollup-contract-drift-hardening.md`
archived: 2026-09-18

### DW-53: The Contracts-derived gate binds payload admission but not roll-up effect, so a new event registered only to green the gate is accepted, consumes its sequence slot, and advances the watermark with no state change.

status: done 2026-09-05
origin: spec-deferred 96d65c591dec
source_spec: `spec-rollup-contract-drift-hardening.md`
archived: 2026-09-18

### DW-54: WhatsNextQueueProjection keeps a structurally identical hand-maintained payload allowlist over the same delivery envelope, with no Contracts-derived gate, so the drift this story closed for roll-up stays open there.

status: done 2026-09-05
origin: spec-deferred 05c45a10513b
source_spec: `spec-rollup-contract-drift-hardening.md`
archived: 2026-09-18

### DW-55: The dead-letter capture parser's fixtures are hand-written literals rather than derived from the publisher type, so a rename on the producing side breaks capture in production while both parser tests stay green.

status: done 2026-09-05
origin: spec-deferred 3bd2f3fc99d1
source_spec: `spec-dapr-subscription-operations-hardening.md`
archived: 2026-09-18

### DW-56: Completed-marker failure can still be acknowledged as processed

status: done 2026-10-23
origin: migrated from legacy ledger ("Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-08-28)"), 2026-08-28
archived: 2026-10-23

### DW-57: Event processor does not reject non-work domains

status: done 2026-09-05
resolution: resolved by sweep bundle dw-domain-event-processing-hardening
resolution-undo: 3a19f3e607e67615252688bc602dc3efb8ba600ece76cf066c65c8c09296997a 2026-09-05 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-08-28)"), 2026-08-28
archived: 2026-09-18

### DW-58: The command-pipeline smoke-test prerequisite probe still collapses caller-requested cancellation into an unavailable result.

status: done 2026-10-23
origin: spec-deferred dc9e48616d2d
source_spec: `spec-probe-cancellation-propagation.md`
severity: medium
archived: 2026-10-23

### DW-59: The deterministic probe cases never run in the repository's habitual deterministic lane, because they live in a class that lane excludes by name.

status: done 2026-10-23
origin: spec-deferred 282ced3de175
source_spec: `spec-probe-cancellation-propagation.md`
severity: medium
archived: 2026-10-23

### DW-60: Follow-up review still recommended for dw-probe-cancellation-propagation after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-probe-cancellation-propagation.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260828-191811-dea9; this entry preserves the lingering recommendation for a deliberate later review.
status: open

### DW-61: Three untouched contract-flow test summaries still describe camelCase Web JSON as the real EventStore write path, contradicting the corrected claims this bundle landed elsewhere.

status: done 2026-10-22
origin: spec-deferred 2c19a16a4d17
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: medium
archived: 2026-10-23

### DW-62: The frozen WorkItemCannotReferenceParentFromAnotherTenant catalog sample carries a same-tenant parent, so its evidence contradicts the rejection it names.

status: done 2026-10-22
origin: spec-deferred fcb2bd62083e
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
archived: 2026-10-23

### DW-63: Nothing binds the EventStore revision quoted in the maintained docs to the actual checked-out submodule gitlink, so the corrected pin can silently rot on the next bump.

status: done 2026-10-23
origin: spec-deferred 7a41b5161f32
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
archived: 2026-10-25

### DW-64: The byte-exact corpus never freezes the PascalCase at-rest form of EffortEstimate, ObligationReference, or ConversationCorrelationId, because every catalog sample leaves them null.

status: done 2026-10-22
origin: spec-deferred bb511d744622
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: medium
archived: 2026-10-23

### DW-65: Story48Streams serializes stand-in EventStore stream bytes with camelCase Web options, the same contradiction DW-61 records for three contract-flow tests but at a helper DW-61 does not name.

status: done 2026-10-22
origin: spec-deferred e4191a19474f
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
archived: 2026-10-23

### DW-66: Both corpus membership gates enumerate the copied build output, so a fixture deleted from source survives in bin/ and membership still passes on an incremental build.

status: done 2026-10-22
origin: spec-deferred 8bfcda99d79e
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
archived: 2026-10-23

### DW-67: Pre-change ineligible index state can lack a durable accepted-sequence watermark after the old index-first partial-failure window.

status: done 2026-08-31
origin: spec-deferred bf5afcdcbbe6
source_spec: `spec-projection-write-ordering-guard.md`
archived: 2026-09-18

### DW-68: The retained tenant-index sequence watermark is never pruned, so the single per-tenant what's-next document grows by one permanent entry per work item ever projected.
origin: spec-deferred c229b8333c33
location: src/Hexalith.Works/Projections/WorksWhatsNextReadModel.cs:57
source_spec: `spec-projection-write-ordering-guard.md`
severity: medium
reason: WorksWhatsNextTenantIndex.LastSequences keeps an entry after the item leaves the eligible set and nothing removes it. Before this bundle the document was bounded by the currently eligible item count. It is read, copied, and rewritten on every dispatch for the tenant and read on every what's-next query, so a long-lived tenant eventually meets the state-store value-size limit. The sibling PendingDateAwaitTenantIndex has the same unbounded shape, so a retention policy (cap, TTL, or compaction) is a cross-index design decision rather than a local fix.
status: open
decision: 2026-09-01 Shard retained watermarks — Partition both index families into bounded tenant-scoped shards while retaining every ordering tombstone; add migration, routing, size-bound, and stale-replay tests.
decision: 2026-09-01 Shard retained watermarks — Partition both index families into bounded tenant-scoped shards while retaining every ordering tombstone; add migration, routing, size-bound, and stale-replay tests.

### DW-69: A refused stale replay still issues a conditional write through ReadModelWritePolicy, bumping the key's ETag instead of skipping the store round-trip.
origin: spec-deferred c87599ee7a12
location: references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/ReadModelWritePolicy.cs:78
source_spec: `spec-projection-write-ordering-guard.md`
severity: low
reason: ReadModelWritePolicy.UpdateAsync unconditionally calls TrySaveAsync with whatever the transform returns, so both ordering guards refuse at the value surface only: the persisted document is unchanged, but the version advances and a concurrent legitimate writer can lose an attempt from its bounded retry budget. This is the pre-existing platform contract (the old unconditional SaveAsync also wrote), so the change adds no write it did not already make; suppressing the write needs a no-op signal in the EventStore write policy, which this story's Block If fences off.
status: open
decision: 2026-09-01 Add no-op outcome — Add a backward-compatible write-or-no-change transform result or overload to ReadModelWritePolicy, skip TrySaveAsync for no-change, update Works callers, and add platform concurrency tests.
decision: 2026-09-01 Add no-op outcome — Add a backward-compatible write-or-no-change transform result or overload to ReadModelWritePolicy, skip TrySaveAsync for no-change, update Works callers, and add platform concurrency tests.

### DW-70: The sibling pending-date-await index transform still mutates the instance it loaded, so a rejected write can leave that mutation visible in a reference-returning store.

status: done 2026-10-23
origin: spec-deferred 6f4552a53c41
source_spec: `spec-projection-write-ordering-guard.md`
severity: low
archived: 2026-10-23

### DW-71: The /project response still echoes the freshly computed item even when both ordering guards refused the replay, so a caller can receive state that was never persisted.

status: done 2026-10-23
origin: spec-deferred 2cfe338125c2
source_spec: `spec-projection-write-ordering-guard.md`
severity: low
archived: 2026-10-23

### DW-72: Follow-up review still recommended for dw-projection-write-ordering-guard after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-projection-write-ordering-guard.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260829-091730-0d52; this entry preserves the lingering recommendation for a deliberate later review.
status: open

### DW-73: The pending-date-await index and tenant registry stay unversioned and are neither rebuilt nor pruned by the shared reconciliation, so a work item dropped from a tenant's authoritative membership can r

status: done 2026-10-23
origin: spec-deferred c1087044cae6
source_spec: `spec-shared-rollup-reconciliation.md`
severity: medium
archived: 2026-10-23

### DW-74: Follow-up review still recommended for dw-shared-rollup-reconciliation after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-shared-rollup-reconciliation.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260829-091730-0d52; this entry preserves the lingering recommendation for a deliberate later review.
status: open

### DW-75: Hexalith source-consumption project discovery remains constrained by the Hexalith.Works*.csproj filename glob.

status: done 2026-10-23
origin: spec-deferred 9a356fa164ff
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
archived: 2026-10-23

### DW-76: PackageVersion ownership does not trace Works-owned property overrides consumed by the approved shared catalog.

status: done 2026-10-23
origin: spec-deferred 61f77899777d
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
archived: 2026-10-25

### DW-77: The architecture-test lane is bound to the build machine's installed MSBuild layout instead of resolving it at run time.

status: done 2026-10-23
origin: spec-deferred 769960e1cb23
source_spec: `spec-msbuild-dependency-discovery.md`
severity: medium
archived: 2026-10-25

### DW-78: The Release lane pins Platform=AnyCPU for every evaluated project regardless of its declared Platforms.

status: done 2026-10-23
origin: spec-deferred 27c11709b8c0
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
archived: 2026-10-23

### DW-79: Restored NuGet package build props enter the custom-import freshness closure.

status: done 2026-10-23
origin: spec-deferred c816fd817c7c
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
archived: 2026-10-23

### DW-80: Dependency declarations that only an inactive non-Release condition guards remain invisible when they arrive through an import.

status: done 2026-10-23
origin: spec-deferred 65a78e5cf80d
source_spec: `spec-msbuild-dependency-discovery.md`
severity: medium
archived: 2026-10-23

### DW-81: Runtime-adapter confinement still decides EventStore-runtime and Dapr direction from owning-file XML and basename prefixes.

status: done 2026-10-23
origin: spec-deferred b052bd148f70
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
archived: 2026-10-25

### DW-82: The architecture-test project pins one exact System.Diagnostics.EventLog assembly version to keep the MSBuild reference set warning-free.

status: done 2026-10-23
origin: spec-deferred f3e8a31d6d81
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
archived: 2026-10-25

### DW-83: Follow-up review still recommended for dw-msbuild-dependency-discovery after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260829-091730-0d52; this entry preserves the lingering recommendation for a deliberate later review.
status: open

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-02)

### DW-84: Story 4.8's diff bundles out-of-scope schema-v2 migration and roll-up convergence work into `WorkItemProjectionDispatcher`

status: done 2026-09-05
origin: code-review 4-8-register-and-reconcile-date-reminders-durably
source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
archived: 2026-09-18

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-05)

### DW-85: `WorkItemRollUpPayloadCoverageTests` is now a near-strict subset of `ProjectionPayloadCoverageTests`'s roll-up coverage assertion

status: done 2026-10-23
origin: code-review 4-8-register-and-reconcile-date-reminders-durably
source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
severity: low
archived: 2026-10-23

## Deferred from: review remediation of 4-8-register-and-reconcile-date-reminders-durably (2026-09-08)

### DW-86: Story 4.7's two stream-reading sources advance their page cursor one event too far

status: done 2026-10-23
origin: review-remediation 4-8-register-and-reconcile-date-reminders-durably
source_spec: `_bmad-output/implementation-artifacts/4-7-trigger-reactor-translators-from-the-live-event-stream.md`
severity: high
archived: 2026-10-23

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Complete deterministic and live verification for the concurrent Story 4.8 AppHost-owned Dapr placement/scheduler path.
  evidence: The default branch composes placement and scheduler on ports 51005/51006, but the topology test forces external endpoints and command/cascade/reminder prerequisites still require the superseded 50005/50006 or 6050/6060 services, so the owned path can remain unasserted or skip.
  audit: 2026-10-23: See the [concurrent-review reconciliation](#story-4-8-concurrent-review-audit) for superseded deterministic claims and current Platform ownership; original evidence and status are preserved.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Pin configuration- and policy-level Dapr ACL trust domains in the concurrent Story 4.8 topology tests.
  evidence: All four YAML files changed those values to `localhost`, while deterministic tests assert only `spec.mtls.controlPlaneTrustDomain`; restoring `public` on an allow policy would leave tests green and deny the intended Sentry identity.
  audit: 2026-10-23: See the [concurrent-review reconciliation](#story-4-8-concurrent-review-audit) for superseded deterministic claims and current Platform ownership; original evidence and status are preserved.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Reconcile the concurrent Story 4.8 specification with its current localhost trust domain and the 40-type catalog baseline.
  evidence: Its implementation note still recommends `controlPlaneTrustDomain: "public"`, and its frozen constraint/verification still says catalog 37 even though the concurrent code uses `localhost` and Story 1.5 makes the current catalog 40.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Review the concurrent Story 4.8 scheduler's embedded-etcd network exposure.
  evidence: `DaprSelfHostedMtls.AddControlPlane` passes `--etcd-client-listen-address=0.0.0.0`; co-networked containers can reach that listener even though its port is not published to the host, so the scheduler should bind loopback unless remote clients are required.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Reject null-correlation `ConversationLinked` evidence in the what's-next projection.
  status: done 2026-10-23
  resolution: Existing descriptor rejection is confirmed by WorkItemConversationProjectionTests; the extended control proves a valid same-sequence link is still accepted after malformed evidence. See [spec-five-deferred-conversation-boundaries.md](spec-five-deferred-conversation-boundaries.md) and its retained verification evidence.
  evidence: The separately committed Story 1.5 descriptor treats a null correlation as an accepted intentional no-op and can advance the source watermark for malformed evidence; Story 4.8 does not own that projection contract.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Validate persisted `ConversationLinked` identity before applying it to aggregate state.
  status: done 2026-10-23
  resolution: WorkItemState.Apply now refuses absent/unestablished or foreign tenant, item and aggregate identities before mutation. Unit and real SDK byte-replay controls preserve state and allow subsequent healthy replay. See [spec-five-deferred-conversation-boundaries.md](spec-five-deferred-conversation-boundaries.md) and its retained verification evidence.
  evidence: The separately committed Story 1.5 replay overload validates the correlation only, so a foreign tenant/work-item/aggregate identity in corrupted persisted evidence could become authoritative and reject a later legitimate link.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Add independent domain and tenant mismatch coverage for the Story 1.5 runtime envelope boundary.
  status: done 2026-10-23
  resolution: Existing independent domain, tenant and aggregate mismatch facts are retained and executed through the real EventStore aggregate dispatcher. See [spec-five-deferred-conversation-boundaries.md](spec-five-deferred-conversation-boundaries.md) and its retained verification evidence.
  evidence: The pre-verified review found only aggregate-ID mismatch coverage; removing either the domain or tenant comparison currently leaves the focused adapter tests green.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Add malformed required-field dispatch coverage for `LinkConversation`.
  status: done 2026-10-23
  resolution: Raw dispatch now exercises missing and explicit-null tenant, work-item and correlation fields, preserves supplied persisted state, and proves a later healthy command still emits the expected link. See [spec-five-deferred-conversation-boundaries.md](spec-five-deferred-conversation-boundaries.md) and its retained verification evidence.
  evidence: The pre-verified review found no raw runtime-adapter case with missing/null tenant, work-item, or conversation correlation, so the separately committed Story 1.5 fail-closed guards can regress undetected.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Audit stale concurrent-review records at the end of the deferred-work ledger.
  status: done 2026-10-23
  resolution: Completed the [dated concurrent-review audit](#story-4-8-concurrent-review-audit), preserving original findings while identifying superseded default-topology and workload-trust claims and the current Platform ownership. See [spec-five-deferred-tracking-integrity.md](spec-five-deferred-tracking-integrity.md).
  evidence: Two Story 1.5 review entries describe a now-unasserted default control plane and localhost workload ACL domains, but the current Story 4.8 topology asserts the default path and retains `public` for workload/policy trust domains; existing records are append-only in this workflow.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Separate or reconcile the three submodule pointer advances bundled with the earlier Story 4.8 commit.
  evidence: Parties, Projects, and Tenants pointers advanced relative to the 4.8 baseline despite its no-submodule constraint; they are already committed concurrent user-owned changes and were preserved as required by repository instructions.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-06)

- DeleteReminderAsync GET-after-DELETE may need to treat 204 as gone (`tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs:441`). Unverified on Dapr 1.18.3; live 4/4 implies GET returns 404 here. Settle by capturing the GET status immediately after a successful DELETE against this runtime.
- deferred-work.md gained unstructured concurrent-review bullets without DW-id fields (`_bmad-output/implementation-artifacts/deferred-work.md:805`). Append-only ledger from a concurrent review; several bullets are already contradicted by this patch. Needs a sweep, not an in-band 4.8 code fix.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-07)

- Startup reminder reconciliation gives up permanently after ~5 s and never runs again until the next host restart (`src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:37-61`). `ReminderReconciliationMaxAttempts = 5` × `ReminderReconciliationRetryDelayMilliseconds = 1000`, fixed delay, no backoff or jitter, and no distinct "gave up" event separating budget exhaustion from a single failure. A state store or command gateway unready longer than that voids AC #3 for a whole host lifetime. Deferred as pre-existing — already recorded in the story's 2026-09-05 triage log as out-of-scope — but the retry budget itself is new in this diff, so it warrants a dedicated story (periodic re-run, or backoff plus an explicit exhaustion signal) rather than another round of the same deferral.
- `Projections/SharedRebuild/*` carries four real defects that belong to `spec-shared-rollup-reconciliation.md`, not Story 4.8 (`src/Hexalith.Works/Projections/SharedRebuild/`). (1) `WorkItemSharedProjectionRebuildHandler.AccumulateAsync` is O(n²) in candidate size — each accumulated aggregate deserializes the whole candidate, rebuilds the list, and reserializes it. (2) The candidate retains raw `ProjectionEventDto[]` against the 1 MiB `ProjectionDispatchOptions.MaxSharedRebuildCandidateBytes` default, which Works never raises, so a tenant rebuild can fail on candidate size long before the 10,000-aggregate admission bound `WorksHost` sizes for. (3) `WorkItemSharedRebuildRelationshipGraph.IsRolledTotalUnavailable` recurses once per member with cycle detection but no depth cap, so a deep parent/child chain raises an uncatchable `StackOverflowException`. (4) `WorkItemSharedRebuildManifestBuilder` throws on one out-of-identity payload instead of marking that aggregate incomplete, aborting the whole tenant rebuild rather than degrading it. Needs review against its own acceptance criteria.
  - Resolution 2026-09-15 (`spec-4-8-register-and-reconcile-date-reminders-durably-4.md`): defect **(4) only** is closed as a stale record. The baseline already degraded foreign-identity evidence through the shared manifest builder's pure incomplete path; this patch verified that existing behavior with `WorkItemSharedProjectionRebuildHandlerTests.Foreign_payload_identity_marks_the_affected_shared_rebuild_roll_up_incomplete` and added bounded identity-specific telemetry to the separate live `/project` path. Defects **(1)–(3) remain open** unchanged.
- Reminder recovery has a durability blind window and no backfill (`src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:41-51`). Registration is immediate on the `work.events` subscription, but the index recovery reads is written only by `WorkItemProjectionDispatcher.MaintainPendingDateAwaitIndexAsync` on `ProjectionPollerService`'s refresh-interval cadence, so a crash inside that window leaves the await unindexed — and since reconciliation is a single startup pass, it is never reissued on the next start either. Separately, the tenant registry is populated only by post-deploy `/project` dispatches, so an item suspended before this deploy with no subsequent events is never dispatched, never indexed, and never discovered; the retired `StreamReadingPendingDateAwaitSource` scanned configured tenants directly. Recorded `maybe-false` on reachability: settle by checking whether `ProjectionPollerService` redispatches every aggregate's full stream on a projection-cursor reset (backfill exists, finding is moot) or only aggregates with events after the cursor (finding is real and medium). The boundary record's Story 4.8 entry documents the growth limitation but neither window.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-2.md`
  summary: Reconcile the Story 4.8 catalog sentence in `docs/eventstore-api-surface-constraints.md` with current count 40.
  evidence: Lines 301-302 still say the catalog was 37 at Story 4.8 completion and that Story 1.5 came later; this close-out did not touch that file, and sprint already has Story 1.5 done with `WorkItemV1Catalog.Count` 40.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-08, Group 1)

<a id="story-4-8-canonical-unpark-replay"></a>
- No unpark, delete, or operator replay path for `projection:works:parked:{tenant}:{id}` (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:596-650`). Parking's intended terminal disposition is the Error log plus "needs operator action"; an unpark/replay tool is a new ops feature, not an in-band 4.8 fix.
- Shared rebuild never emits operations for pending-date-await index/registry or parking keys (`src/Hexalith.Works/Projections/SharedRebuild/`). Re-observed; already recorded 2026-09-07 against `spec-shared-rollup-reconciliation.md`.
- Index durability window + one-shot empty-registry success + retired tenant-wide scan (`src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:41-51`, `ReminderReconciliationService.cs:37-61`). Re-observed; already recorded 2026-09-07 (blind window / no backfill, and the ~5 s give-up).
- Equal-sequence `PersistRollUpAsync` can overwrite a concurrently merged child set (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:452-454`). Roll-up child-reconciliation / F-PROJ-1 is out of Story 4.8 scope (DW-84 / shared-rebuild).
- `UseCurrentSchemaAsync` is read once per dispatch and reused across later awaits (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:200`). Documented generation-switch race in the dispatcher rework; not Story 4.8 reminder/index behavior.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-3.md`
  summary: Mixed-case reserved tenant `TENANTS` misses the `/project` Ordinal guard, then `TenantId` lowercases to `tenants`.
  status: done 2026-10-23
  resolution: The original custom projection handler is superseded by SDK hosting. `spec-five-deferred-ingress-authority.md` makes the common reserved-tenant/key guard case-independent and verifies current stream acquisition, all command adapters and protected HTTP command refusal with unchanged persisted projection contents for uppercase and mixed-case spellings. [Verification evidence](evidence/five-deferred-ingress-authority-2026-10-23/manifest.json).
  evidence: Pre-existing order: `ThrowIfReservedTenantId(request.TenantId)` then `new TenantId(...)`. EventStore already lowercases persisted stream identity, so the poller does not send `TENANTS`; a hand-crafted `/project` body could. Not introduced by Group 1.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-15, Group 1 patch round)

- DW-56 is `status: done 2026-09-05` (see the stable `DW-56` heading, resolved by sweep bundle `dw-domain-event-processing-hardening`) while Story 4.8 asserts six times that it stays open (`4-8-…md:125`, `:146`, `:412`, `:434`, `:630`, `:686`) and `spec-4-8-…-3.md` Implementation Notes claim "DW-56 left open." It closed before this round's baseline `6e2fb4d`, so this round did not close it — but it re-asserted the claim as verification evidence. Net effect: the one deliberately-unfixed 2026-09-01 `[Review][Patch] [Low]` marker-store item now cross-references a closed ledger entry and has no live tracking anywhere. Needs a human call: either the sweep genuinely resolved the marker-store family and the story's cross-reference should be retired, or DW-56 should be reopened. Fix edits tracking artifacts, not code.
- Moving the decoder skip from EventId 4501 to 4504 (`src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:121`) is the correct fix for the genuine collision with `ProjectionDecodeFailed`, but it ships as an undeclared telemetry break — any alert keyed on 4501 now matches a different event — and no registry or fitness test asserts the 45xx/46xx EventIds are unique, so the next collision is unguarded. Deferred: the fix is a telemetry-contract note plus a uniqueness guard, not an in-band code correction.
- Re-observation, already recorded: no unpark, delete, or operator replay path for a parked aggregate (see the 2026-09-08 Group 1 entry above). Recorded again only because this round raised its cost — `IndexedPendingDateAwaitSource` now also skips parked candidates, so a parked aggregate loses date-reminder recovery as well as `/project`, while log 4502 still tells the operator it "needs operator action" they have no tool to take.
- Re-observation, already recorded: mixed-case reserved tenant `TENANTS` misses the `/project` Ordinal guard (see the `source_spec: spec-4-8-…-3.md` entry above, triage E1). This review narrows it: the `/process` guard does **not** have the hole because `RefuseReservedTenant` reads the already-normalized `command.TenantId.Value` (`TenantId`'s constructor routes through `AggregateIdentity`, which lowercases at `AggregateIdentity.cs:28`), and the EventStore poller sends lowercase stream identity — so only a hand-crafted direct `POST /project` reaches it. The new shared `ThrowIfReservedTenantId` helper was the natural place to normalize once.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-15, Group 1 production/runtime)

- Equal-watermark roll-up replacement can discard concurrently merged child evidence or restore totals from an older clean prefix (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:446`). Roll-up convergence is outside Story 4.8 and already belongs to F-PROJ-1/shared-rebuild work.
- Shared rebuild leaves an old current-schema roll-up readable when a rebuilt manifest member produces no replacement model (`src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:134`). Introduced by the separately committed shared-rollup reconciliation work, not this reminder story.
- Shared rebuild records relationship edges before the projection accepts the event sequence, allowing duplicate/rejected evidence to create ghost edges (`src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:59`). Introduced by the separately committed shared-rollup reconciliation work, not this reminder story.
- Child-completion and cascade stream readers advance the exclusive cursor by `last + 1` and forward continuation tokens the gateway rejects, skipping one boundary event per page (`src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:152`). Pre-existing Story 4.7/4.6 readers; already represented by DW-86 and needs both readers plus multi-page tests fixed together.
- The child-completion reader neither validates page domain nor fails closed on undecodable lifecycle evidence (`src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:137`). Story 4.7 recovery surface outside Story 4.8.
- The cascade reader validates neither page/payload identity nor malformed `ChildSpawned` evidence before omitting a descendant (`src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:62`). Pre-existing Story 4.6/4.7 recovery surface outside Story 4.8.
- Cascade terminal-state discovery performs serial per-child roll-up reads that repeatedly reload the tenant manifest, creating an N+1 recovery path (`src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:114`). Separately committed cascade/shared-rollup performance work.
- The production Dapr event marker's `TryAcquireAsync` does not persist an in-progress lease, so concurrent delivery of one message can execute handlers twice (`src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:62`). Requires the EventStore marker protocol/cross-repository ownership rather than a Story 4.8 patch.
- A hand-crafted `/project` request with mixed-case `TENANTS` passes the raw ordinal reserved-id check and is then canonicalized to `tenants` (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:114`). Pre-existing direct-endpoint-only issue already recorded by the prior 2026-09-15 review.
- Deterministic host composition does not pin the three terminal-event handlers registered for cancellation, expiry, and completion (`tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs:109`). Story 4.7 registration coverage, outside the reminder slice.
- `global.json` pins SDK 10.0.401 while the architecture fitness test still asserts 10.0.400 (`tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:16`). Unrelated SDK/tooling drift already tracked outside Story 4.8.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Complete reserved-tenant command-envelope hardening across all aggregate adapters.
  status: done 2026-10-23
  resolution: All fifteen command adapters are exercised against canonical and mixed-case reserved envelope tenants and reserved payload tenants with ordinary envelopes; ordinary kernel-result preservation controls remain active. See [spec-five-deferred-conversation-boundaries.md](spec-five-deferred-conversation-boundaries.md) and its retained verification evidence.
  evidence: Split from the 4,488-token remediation spec after review showed this independently shippable adapter-boundary bundle needs all-command reserved-envelope and retained reserved-payload tests.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Complete reminder recovery and scheduling telemetry plus retry-verification hardening.
  evidence: Split from the 4,488-token remediation spec because parking-read classification, parked-skip accounting, scheduler-failure diagnostics, reconciliation exhaustion, and their focused tests form an independently shippable recovery bundle.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Register a production `IProjectionChangeNotifier` for the Works host or explicitly retain the projection seam as inactive.
  status: done 2026-10-28
  resolution: `spec-five-deferred-contract-and-proof-integrity.md` explicitly retains notification as inactive in the canonical host. Current adoption, queue and boundary documentation distinguish active SDK persistence/query serving from the pure change result/token and historical deferred notifier contract. No transport was selected or registered.
  evidence: `AddEventStoreDomainService` uses Client registrations, `WorksHost` resolves the notifier optionally, and no production Works registration supplies an implementation; this predates the projection hardening patch and needs a transport/dependency decision.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Make live projection unknown-event handling fail closed without advancing durable watermarks beyond understood evidence.
  status: done 2026-10-23
  resolution: The SDK replacement refuses unknown evidence before materializing partial state. InvalidOrdinaryEvidenceParksWithoutAdvancingVisibleState proves persisted queue, roll-up, pending-date, topology, complete reconstructed history and folded watermarks survive unchanged after reconstruction; durable parking makes queries unavailable. The journal checkpoint advances over parked evidence by design. Unknown rebuild evidence also preserves the committed generation. [Qualification spec](spec-five-deferred-evidence-admission.md); [retained verification](evidence/five-deferred-evidence-admission-2026-10-23/manifest.json).
  evidence: Unknown event types do not mark live roll-ups incomplete, while the pending-date watermark still uses the maximum raw sequence; a forward-version event can therefore expose an available total and prevent later repair when that event becomes known.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Bound generic projection decoder skip telemetry consistently with identity-mismatch telemetry.
  status: done 2026-10-23
  resolution: WorkItemProjectionEventDecoder already applies the same 128-character single-line sanitization to EventIds 4504 and 4505. WorkItemProjectionDecoderTelemetryTests executes unknown, empty, null and malformed paths, asserts exact structured fields, bounded type/correlation, no control/line separators and no payload or exception disclosure, preserving classification; an identity-mismatch control executes 4505 under the same assertions. [Qualification spec](spec-five-deferred-evidence-admission.md); [retained verification](evidence/five-deferred-evidence-admission-2026-10-23/manifest.json).
  evidence: EventId 4504 still accepts raw namespace-qualified event type and correlation text for unknown or malformed payloads, permitting oversized diagnostic records outside the newly bounded EventId 4505 branch.

- source_spec: none
  summary: Complete reminder recovery and scheduling telemetry plus retry-verification hardening.
  evidence: Split from the current Story 4.8 build so the independently shippable reserved-tenant command-envelope hardening can be implemented and reviewed first.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-5.md`
  summary: Preserve typed incomplete-scan context when partial reminder processing ends in a non-caller cancellation.
  status: done 2026-10-24
  resolution: The legacy reconciler is retired. Executing SDK replacement controls isolate foreign cancellation, retain earlier and later success counts plus candidate diagnostics, persist incomplete discovery, report Degraded readiness and complete a healthy sweep after reconstruction. [Specification](spec-five-deferred-reminder-recovery-policy.md); [retained verification](evidence/five-deferred-reminder-recovery-policy-2026-10-24/manifest.json).
  evidence: `DateReminderReconciler` has excluded every `OperationCanceledException` from its typed rewrap since before this story baseline, so a scheduler-owned cancellation can discard failed-tenant, failed-candidate, and parked-skip context.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-5.md`
  summary: Distinguish final recovery exhaustion from retryable EventId 4603 failures.
  evidence: The shared pre-existing EventId 4603 template says the recovery step will be retried even when `ReminderReconciliationService` has reached its final configured attempt; a separate exhausted-policy decision is needed.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-5.md`
  summary: Bound pre-existing pending-date tenant and stream failure telemetry.
  evidence: EventIds 4604 and 4606 still attach raw provider or gateway exceptions, so their messages and stack traces can exceed the bounded reason-code/identity telemetry used by the new 4608 and 4609 paths.
  - Resolution 2026-09-17 (`spec-4-8-register-and-reconcile-date-reminders-durably-7.md`): closed as a superseded telemetry claim. The 2026-09-16 human decision restored structured exceptions to 4608 and 4609 as well as 4603/4605, retaining bounded identity/count fields and exception-type reason codes while keeping causes in the protected structured exception slot. EventIds 4604 and 4606 follow that same ratified contract, so removing only their causes would recreate the inconsistency the decision closed.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-16)

<a id="story-4-8-canonical-scheduler-exhaustion"></a>
- A permanently failing date-reminder scheduler has no terminal disposition: `WorkItemSuspendedReminderHandler` warns 4609 and rethrows, so a scheduler that always fails wedges that `work.events` delivery indefinitely with no budget, park, or dead-letter route — unlike the sibling `/project` path, which has a parking budget. The same `foreach` also means the first failing await of a multi-date suspension permanently prevents the remaining awaits from registering. spec-5's matrix freezes the rethrow and its Never list forbids a new exhaustion policy, so both halves need their own spec. [src/Hexalith.Works/Reminders/WorkItemSuspendedReminderHandler.cs:63-81]
- `deferred-work.md` still carries the reserved-tenant command-envelope entry and the reminder recovery/scheduling entry as open (the latter recorded twice, the second with `source_spec: none`) although the 2026-09-16 bundles implement them. The precedent for closing is the `Resolution 2026-09-15` sub-bullet on defect (4). Left open so a sweep does not reschedule finished work without a human confirming the mapping. [_bmad-output/implementation-artifacts/deferred-work.md:924]
- No non-reminder EventId 4603 call site pins whether its record carries an exception. `ReminderReconciliationService` is now the only one of seven callers that omits it, so one event id has two payload shapes; the six cascade/child-completion/actor callers are unpinned either way. Belongs with the existing bounded-recovery-telemetry item for 4604/4606. Settled by whichever way the open 4603 decision in the story resolves. [src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:31]
  - Resolution 2026-09-17 (`spec-4-8-register-and-reconcile-date-reminders-durably-7.md`): closed as stale on insertion. Commit `06d64b0`, which appended this line, also restored `ReminderReconciliationService`'s exception argument, so all seven 4603 call sites already shared the structured-exception shape. The focused hosted-service tests pin the reminder caller; this record supplies no remaining behavioral gap for Story 4.8.
- `ArchitectureTests` is red at 236/237: `global.json` pins SDK `10.0.401` while `BuildConfigurationTests` asserts `10.0.400`. Pre-existing tooling drift, honestly recorded by the bundle, but the fitness gate is not green on HEAD. [tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:16]
  - Correction 2026-09-17 (`spec-4-8-register-and-reconcile-date-reminders-durably-7.md`): the same close-out commit added one operator-documentation fact, so the accurate result on that tree was **237/238**, not 236/237. The sole SDK-pin mismatch remains real and open; this correction changes only the historical count.
- `WorksReadModelKeys.IsReservedTenantId` is still `StringComparison.Ordinal`, so a hand-crafted `POST /project` carrying `TENANTS` passes the reserved-id guard and is then canonicalized to `tenants`. Pre-existing and already recorded above at the `spec-4-8-…-3.md` entry; the reserved-tenant spec's Never list explicitly puts mixed-case direct `/project` requests out of scope. The `/process` command path is normalized through `AggregateIdentity` and does not have the hole. [src/Hexalith.Works/Projections/WorksReadModelKeys.cs:69]
- `Handle(LinkConversation …)` compares the raw `envelope.TenantId` ordinally against `command.TenantId?.Value`, which `AggregateIdentity` has already lowercased, so a mixed-case envelope tenant is refused for `LinkConversation` alone while the other fourteen commands accept it. Pre-existing — the 2026-09-16 bundle added the normalized reserved-tenant guard above those lines but did not touch them. [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:113-115]

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably (2026-09-16, six-finding close-out bundle)

- Reserved-tenant normalization landed at one of four ingresses. `WorksReadModelKeys.IsReservedTenantId` is still `StringComparison.Ordinal`, and `WorkItemProjectionDispatcher.cs:114` and `WorksDomainEventProcessor.cs:52` pass un-normalized ids while `WorkItemEventStoreAggregate.cs:157` now lowercases, so the command adapter is stricter than the other two ingresses for the same reserved id. The underlying hole is already recorded above at `deferred-work.md:888` and `:946`, and the reserved-tenant spec's Never list puts mixed-case direct `/project` requests out of scope. Normalizing inside `IsReservedTenantId` would close all four at once but widens behavior across three ingresses Story 4.8 does not own. [src/Hexalith.Works/Projections/WorksReadModelKeys.cs:69]
- `CascadeRecoveryService` keeps the loose cancellation filter that the 2026-09-16 bundle replaced in its structural twin, and no test reads that path. `CascadeRecoveryService.cs:25` still reads `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` and swallows a genuine dependency fault as shutdown with no log at all, where `ReminderReconciliationService.cs:44-46` now requires the exact stopping token and otherwise logs 4603 with the cause; `CascadeRecoveryReconciler.cs:89` has the same filter and aborts the remaining index entries instead of continuing. Grepping `tests/` returns only two DI-descriptor assertions for `CascadeRecoveryService` and no behavioral test of `ExecuteAsync`. The existing 4603 ledger item covers exception *attachment* only, so the cancellation-filter non-adoption is untracked. Outside spec-6's frozen scope. [src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:25]
<a id="story-4-8-canonical-due-now-telemetry"></a>
- The recovery submit branch has no bounded failure telemetry while its sibling scheduler branch does. `DateReminderReconciler.cs:119` is a bare `await _submitter.SubmitAsync(submission, cancellationToken)`, so a submit fault during startup recovery escapes carrying no tenant, work item, or reminder name — it reaches EventId 4603 with the gateway exception attached, but `EventStoreGatewayWorkCommandSubmitter` is a thin gateway wrapper whose exceptions carry HTTP-shaped detail and no Works identity. The sibling branch at `:135-156` logs bounded 4609 with the structured cause before rethrowing. Either branch throwing mid-loop also skips `DateRemindersReconciled` (`:167`) for that tenant, so partial `tenantReissued`/`tenantRescheduled` progress is lost and the remaining tenants are never processed. Deferred 2026-09-17 (human): a `DateResumeIssueFailed` EventId would be new operator surface with no execution task and no Code Map entry in spec-6, and it belongs with the two open items in this same family — the permanently failing scheduler with no terminal disposition, and the first failing await of a multi-date suspension blocking its siblings — so one future spec should decide 4610, exhaustion policy, and per-await ordering together. The 2026-09-17 review rewrote the 4605 runbook row so the shipped operator documentation no longer promises evidence that no event emits. [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:119]
<a id="story-4-8-canonical-in-tenant-cancellation"></a>
- The in-tenant cancellation ordering remains open by decision. `IndexedPendingDateAwaitSource`'s three exact-token filters (`:82`, `:139`, `:181`) bare-`throw;`, so a non-cancellation candidate failure followed by an exact-caller `OperationCanceledException` inside the same tenant scan still discards that tenant's accumulated `pending` and counts — `ScanTenantAsync` holds them as locals. The 2026-09-17 review closed only the between-tenant leak at `:69` (Option A); closing this one costs five sites and reverses the 2026-09-16 ratified exact-token policy. Recorded in the class doc rather than fixed. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:82]

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-7.md`
  summary: Decide whether exact caller cancellation after the next tenant scan starts may discard evidence collected from earlier tenants.
  evidence: A cancellation arriving after the outer boundary check but before or during the next tenant-index read can rethrow the exact caller token and bypass the typed incomplete result. Preserving earlier evidence in that case requires changing an exact-token in-tenant filter that this spec explicitly leaves unchanged.
  - Correction 2026-09-17 (`spec-4-8-register-and-reconcile-date-reminders-durably-8.md`): closed as incorrectly sited. The outer exact-caller catch can break to the existing typed incomplete-result path whenever an earlier tenant or candidate failure is already recorded, without changing any of the three in-tenant exact-token filters. The focused regression preserves earlier partials, counts, and cause, reads the cancellation-throwing tenant index once, and does not read another tenant. The separately recorded in-tenant ordering limitation remains open.

## Deferred from: code review of spec-4-8-register-and-reconcile-date-reminders-durably-7.md (2026-09-17)

- EventId 4603's runtime template still says the step "will be retried at-least-once" even though this close-out can log 4603 and then complete from a canceled retry delay with no further attempt. Pre-existing shared template; already recorded under spec-5 as "Distinguish final recovery exhaustion from retryable EventId 4603 failures". Changing it is operator surface for all seven `Reason` values and is outside spec-7's Code Map. [src/Hexalith.Works/Runtime/WorksRecoveryLog.cs:36]
- Exact caller cancellation after a prior in-tenant candidate failure still discards that tenant's locals. spec-7 Never list freezes in-tenant cancellation changes; already recorded in the 2026-09-16 six-finding close-out section. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:145-197]
- `DateReminderReconciler` remarks still say an incomplete scan is rethrown so the hosted service retries it, but `ReminderReconciliationService` now logs 4603 and can return from the canceled retry delay without another attempt. spec-7 Never list leaves `DateReminderReconciler` unchanged. [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:33-38]

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md`
  summary: Correct EventId 4603's pre-existing unconditional retry promise across all recovery callers.
  status: done 2026-10-24
  resolution: The legacy event and its callers are absent from the current Works assembly. Current SDK guidance describes actual pass counts and retained work without an unconditional retry promise; executing controls verify candidate failure/pass completion/disabled diagnostics and continuing periodic recovery. [Specification](spec-five-deferred-reminder-recovery-policy.md); [retained verification](evidence/five-deferred-reminder-recovery-policy-2026-10-24/manifest.json).
  evidence: The shared template still says the step "will be retried at-least-once" on a final startup attempt and at call sites with no in-process repeat; spec-8 explicitly forbids changing that template, so a separate operator-surface decision is required.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md`
  summary: Decide whether exact caller cancellation inside a tenant scan should retain that tenant's local partial results and failure evidence.
  evidence: An exact cancellation unwinds `ScanTenantAsync` before its local result is returned, delaying already collected current-tenant evidence until another startup pass; this behavior predates spec-8 and its frozen scope leaves all three in-tenant filters unchanged.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
  summary: Add navigation and provenance guidance between the compacted deferred-work ledger and its archive.
  status: done 2026-10-23
  resolution: Added reciprocal ledger/archive navigation, the original compaction revision, DW-number lookup guidance and the rule that current ledger status takes precedence over historical snapshots. See [spec-five-deferred-tracking-integrity.md](spec-five-deferred-tracking-integrity.md).
  evidence: The pre-existing 2026-09-18 archive migration leaves active-ledger stubs without an archive link and starts the archive at an H3 entry without a title, policy, backlink, or provenance note, making historical evidence unnecessarily difficult to discover.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
  summary: Reconcile duplicate and ambiguously dated decision fields in the deferred-work archive.
  status: done 2026-10-23
  resolution: Removed exact duplicate decision copies in seven snapshots; DW-56 retains its 2026-08-29 decision and source-backed 2026-09-01 reaffirmation, plus both archive snapshots and its reopening/reclosure history. See [spec-five-deferred-tracking-integrity.md](spec-five-deferred-tracking-integrity.md).
  evidence: Several pre-existing archived entries repeat identical decision fields, while DW-56 records the same decision with two dates and no amendment marker; no runtime parser is affected, but the historical record lacks a clear canonical interpretation.

## Deferred from: code review of spec-4-8-register-and-reconcile-date-reminders-durably-9.md (2026-09-18)

- Compacted ledger stubs and `deferred-work-archive.md` have no title, policy, backlink, or archive path. Reconfirmed on `a292e3b...HEAD`; already recorded at the spec-9 `source_spec` navigation bullet above. [\_bmad-output/implementation-artifacts/deferred-work-archive.md:1]
- `Clean_shutdown_between_tenants_preserves_the_exact_caller_cancellation` still stubs and verifies with `Arg.Any<CancellationToken>()`. Pre-existing sibling fact; spec-9 only hardened the new final-index regression. [tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs:582]
- Story 4.8 still treats DW-56 as an open marker-store patch after the ledger archived it as `done 2026-09-05`. Pre-existing tracking contradiction already recorded under the `DW-56` heading. [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:125]
- 4604/4606 architecture pins still omit the retry-budget and exhaustion phrases that 4608 now asserts. Pre-existing; spec-9 required mirroring 4604/4606 wording into 4608, not tightening those older pins. [tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs:92]

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Repair the Story 4.7 cascade startup checkpoint replay that fails to terminate the outstanding child after a mid-cascade restart.
  evidence: `WorksCascadeRecoveryPipelineSmokeTests.Reactor_translators_run_live_and_interrupted_cascade_converges_after_restart` fails phase 2 with `WaitForEventCountAsync(s_secondChild, WorkItemCancelled, 1)` returning 0 against a 60-second budget [tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:89]. Reproduced 3/3 on 2026-09-19 — broad Release run, isolated Release run, and isolated Debug source-mode run — so the CI/CD spec's Release package-mode switch is excluded as the cause. The defect was previously masked: the host inotify ceiling killed `dapr-sentry` and the AppHost reported `eventstore: FailedToStart` before this fact's logic executed, so the suite never genuinely exercised it. `CascadeRecoveryService.ExecuteAsync` swallows any reconciler exception into a `RecoveryStepFailed` log line [src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:28-32], so the failed replay surfaces only as a zero event count; diagnosis needs the live Works host log. Outside the CI/CD spec's frozen intent, which scopes test repair to the SDK assertion drift and the AppHost restart race.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Route the remaining live AppHost integration files through `WorksAppHostSmokeHarness` so the control-plane port settling repair covers every restart boundary.
  evidence: The spec repaired start/teardown port settling inside `WorksAppHostSmokeHarness`, but only `WorksMtlsAuthorizationSmokeTests` and `WorksReminderRecoveryPipelineSmokeTests` consume it. `WorksCascadeRecoveryPipelineSmokeTests` declares its own `WithAppHostAsync` whose `finally` disposes and returns without waiting for ports 50001/51005/51006 [tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:576-580], while `WorksCommandPipelineSmokeTests` starts the AppHost directly and has the same teardown gap. Restart-based facts in those two live files remain exposed to the race the spec set out to close; `WorksDomainEventSubscriptionTests` and `WorksRecoveryOptionsTests` do not start an AppHost and are not part of this item.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Make published Works assemblies carry the released package version instead of the build-time MinVer height version.
  status: done 2026-10-25
  resolution: Current packer compiles with the requested version and ships symbols/SourceLink. A fresh five-package pack at 0.0.0-deferred.20261025 passes metadata validation and five isolated package-only consumers, including reflected informational-version checks. See [qualification spec](spec-five-deferred-release-authority.md) and [retained packet](evidence/five-deferred-release-authority-2026-10-25/manifest.json).
  evidence: Verified 2026-09-19 by opening `nupkgs/Hexalith.Works.Contracts.0.0.0-ci-test.nupkg`: the nuspec reads `0.0.0-ci-test` while the embedded `lib/net10.0/Hexalith.Works.Contracts.dll` carries `AssemblyInformationalVersion 0.0.0-preview.0.221+61d292b2c2be507930fc56b9b73ae6d753461633` and `AssemblyVersion 0.0.0.0`. `scripts/pack-release-packages.py:96-115` packs with `--no-build --no-restore`, so `-p:Version`/`-p:PackageVersion`/`-p:MinVerVersionOverride` can only restamp nuspec metadata, and `-p:ContinuousIntegrationBuild=true` is inert because `CoreCompile` is skipped. `scripts/validate-nuget-packages.py` reads nuspec metadata only and never opens `lib/**/*.dll`, so the divergence passes every gate. Fixing it changes the release pack strategy (pack with build, or stamp the version at compile time), which the CI/CD spec's tasks do not settle. Related: the packages also ship no symbols and no SourceLink (`IncludeSymbols`/`SymbolPackageFormat`/`PublishRepositoryUrl`/`EmbedUntrackedSources` are unset despite a declared `RepositoryUrl`).

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Reconcile the centrally pinned Hexalith package versions with the checked-out submodule commits, or gate the drift.
  evidence: Verified 2026-09-19: `git describe --tags` in `references/Hexalith.EventStore` reports `v3.106.0-15-g9de3fc77` while `references/Hexalith.Builds/Props/Directory.Packages.props:9` pins `HexalithEventStoreVersion` 3.106.0 (same shape for PolymorphicSerializations at 1.19.3). Debug compiles against source 15 commits ahead of the packages Release packs and publishes against. No fitness test compares the two; grepping the test tree for `3.106`, `HexalithEventStoreVersion` and `1.19.3` returns nothing. Whether drift should fail the build is a policy decision the frozen intent does not settle.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Add a Debug source-mode compile tier so the local development lane is actually verified.
  evidence: Verified 2026-09-19: `references/Hexalith.Builds/.github/workflows/domain-ci.yml:349` is the only build step and is `--configuration Release`; every `dotnet test` invocation there is `--configuration Release --no-build`, and `domain-release.yml:342` is Release-only too. The three Debug-aware fitness tests (`DependencyDirectionTests.cs:422,440` and the Debug/Release loop in `RuntimeAdapterGovernanceTests.cs`) all go through `MsBuildProjectEvaluation.TryEvaluate`, which builds a `Microsoft.Build.Evaluation.Project` and reads items without ever compiling. Nothing compiles the solution in Debug, so the sibling-source graph the dual-mode switch exists to serve can break while CI is fully green. Adding a Debug tier expands the CI contract beyond the spec's frozen description.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Resolve that the Release AppHost graph is not purely package-based because EventStore Operations is unpublishable.
  evidence: Verified 2026-09-19 from `src/Hexalith.Works.AppHost/bin/Release/net10.0/Hexalith.Works.AppHost.deps.json`: `Hexalith.EventStore.Aspire/3.106.0 package` coexists with `Hexalith.EventStore.Contracts/3.106.0 project`, `Hexalith.EventStore.ServiceDefaults/3.106.0 project` and `Hexalith.EventStore.Admin.Abstractions/3.106.0 project` pulled transitively through the retained Operations `ProjectReference`. Operations is `IsPackable=false` upstream with no `PackageVersion` entry, so the split is forced rather than accidental, but it means the Release lane does not exercise the packaged EventStore graph it is described as validating, and it keeps a submodule checkout as a hard Release prerequisite. Resolving it needs an upstream packaging decision or an explicit documented exception.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Close the mTLS control-plane health race that still leaves live AppHost lanes intermittently red, beyond the port-freeness repair.
  evidence: On 2026-09-19, after the harness port-settling repair landed, `WorksReminderRecoveryPipelineSmokeTests.Recovery_reissues_a_parked_date_await_from_the_durable_index_without_hand_configuration` failed at startup with `dapr-placement-mtls [state=Running,health=Unhealthy]` while `eventstore`, `works`, `eventstore-operations` and `eventstore-admin` stayed `Waiting` until the 5-minute budget expired. The frozen Intent names this class of defect ("an intermittent AppHost restart race in which the mTLS scheduler stayed unhealthy") and the delivered repair waits only for the fixed control-plane ports to be observably free. Port availability is not control-plane health, so a resource can hold its port, report Running, and never become Healthy. Closing it needs a readiness wait on control-plane resource health, not just socket availability.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Add a dependency-review license allow/deny policy once the shared Builds workflow accepts one.
  evidence: The review called for `allow-licenses`/`deny-licenses` on `.github/workflows/dependency-review.yml` given the repo declares `PackageLicenseExpression=MIT`, but the shared `dependency-review.yml` at pinned Builds SHA `04d961759994396132bb2b113ee465b64740a543` exposes only `fail-on-severity`; passing an undeclared input fails workflow validation. `fail-on-severity: moderate` was set instead. Lifting this requires adding the license inputs upstream in Hexalith.Builds and then re-pinning this caller.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Repair EventStore Operations actor-request serialization so its public dead-letter capture and list endpoints execute successfully.
  evidence: A focused live Works AppHost run on 2026-09-20 reached a healthy Dapr control plane and published the raw dead-letter CloudEvent successfully, but pinned EventStore 3.106.0 logged `System.Runtime.Serialization.InvalidDataContractException` because `Hexalith.EventStore.Operations.Models.DeadLetterCaptureRequest` and `DeadLetterListRequest` cannot be serialized for Dapr actor remoting. Capture returns HTTP 500 and the public Admin adapter converts repeated list failures to HTTP 503, so the wire endpoint cannot currently provide acceptance evidence. The defect belongs to the root-declared `references/Hexalith.EventStore` dependency and cannot be repaired under this spec's frozen no-submodule-modification boundary.

- source_spec: `_bmad-output/implementation-artifacts/spec-implement-works-ci-cd.md`
  summary: Make the shared commitlint workflow validate every commit introduced by a force-push whose previous SHA is unreachable.
  evidence: At pinned Builds commit `04d961759994396132bb2b113ee465b64740a543`, `.github/workflows/commitlint.yml` falls back to `npx commitlint --last` when `github.event.before` is unreachable. A multi-commit force-push can therefore introduce malformed earlier commit messages without detection; repair requires an upstream Hexalith.Builds change followed by a Works caller re-pin.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
  summary: Reconstruct the compacted deferred-work entry titles that end mid-word in both the live ledger and archive.
  status: done 2026-10-23
  resolution: Restored complete DW-20, DW-53, DW-54 and DW-55 headings in both locations from their surviving source-spec summaries, preserving every original reason and resolution. See [spec-five-deferred-tracking-integrity.md](spec-five-deferred-tracking-integrity.md).
  evidence: DW-20, DW-53, DW-54, and DW-55 retain visibly truncated headings in both locations; the corruption predates the spec-9 close-out and is historical-data hygiene rather than reminder-runtime behavior.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
  summary: Broaden EventId 4608 operator guidance to cover non-availability parking-read failures and identify the parking record without implying its key is logged.
  status: done 2026-10-24
  resolution: The historical runbook now includes malformed data and other non-caller read failures, and directs authorized lookup from the emitted tenant/item tuple without claiming a record key is logged. Current SDK diagnostics have separate digest/reason/count guidance. [Specification](spec-five-deferred-reminder-recovery-policy.md); [retained verification](evidence/five-deferred-reminder-recovery-policy-2026-10-24/manifest.json).
  evidence: The parking lookup catches every non-exact-cancellation exception and logs tenant, work item, and reason, while the runbook names only availability/authorization and a “named parking key” that EventId 4608 does not emit; this wording predates the final spec-9 review patches.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-20)

- Cascade and command-pipeline live starters omit the new occupancy and health waits. Reconfirmed on `28724f2...HEAD`; already recorded at the CI/CD routing item above (`deferred-work.md:904`). [tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:543]
- Compacted ledger stubs and archive still have no title, policy, backlink, or archive path. Reconfirmed; already recorded at the spec-9 navigation `source_spec`. [_bmad-output/implementation-artifacts/deferred-work-archive.md:1]
- EventId 4608 still tells operators to restore a named parking key that the log does not emit. Reconfirmed; already recorded at `deferred-work.md:944`. [docs/operations/subscriber-dead-letter-operator.md:135]
- 4604/4606 architecture pins still omit the retry-budget and exhaustion phrases that 4608 now asserts. Reconfirmed; already recorded at `deferred-work.md:898`. [tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs:92]
- DW-20/53/54/55 live-ledger headings remain truncated mid-word. Reconfirmed; already recorded at `deferred-work.md:940`. [_bmad-output/implementation-artifacts/deferred-work.md:144]
- spec-8 triage rewrote original `false | reject` rows to `superseded | patch`, so the spec-8-era record is no longer recoverable from that table. Fix edits another spec. [_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md:104]
- Killed MSBuild child uses unbounded `WaitForExitAsync(CancellationToken.None)`. maybe-false: settle by showing `Kill(entireProcessTree: true)` can leave a live child on this runner. [tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:333]
- EventStore and Admin nested hosts omit `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER`. maybe-false: settle with a DCP command line showing those hosts still `dotnet run` after `SuppressBuild => false`. [src/Hexalith.Works.AppHost/Program.cs:92]
- spec-9 Code Map still cites `deferred-work.md:967`, which the 2026-09-20 File List close-out ledger append retargeted onto the new unpark `source_spec`. Deferred: fix edits another spec. [_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md:43]

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Add a post-startup reminder-reconciliation retry or terminal readiness policy after the bounded startup attempts are exhausted.
  status: done 2026-10-24
  resolution: The legacy startup service is retired. The actual SDK BackgroundService completes at least six failed passes, retains durable incomplete discovery and Degraded readiness, then recovers to Healthy in the same running service after dependency repair; disabling it explicitly remains Degraded. [Specification](spec-five-deferred-reminder-recovery-policy.md); [retained verification](evidence/five-deferred-reminder-recovery-policy-2026-10-24/manifest.json).
  evidence: `ReminderReconciliationService` returns permanently after its configured attempts, so a dependency outage lasting beyond startup can leave durable reminder recovery inactive until another host restart; this predates the final harness increment.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Determine whether an empty pending-await tenant registry can race ahead of projection backfill and strand pre-index reminder recovery.
  status: done 2026-10-24
  resolution: An actual empty SDK pass completes before Works projection backfill. Without any event wakeup or restart, the same services discover the later committed family through the real Works catalog/source, persist the reminder candidate/witness and arm it; subsequent reconstruction repairs a lost scheduler arm. This qualifies retained family membership, not a restore missing every discovery inventory. [Specification](spec-five-deferred-reminder-recovery-policy.md); [retained verification](evidence/five-deferred-reminder-recovery-policy-2026-10-24/manifest.json).
  evidence: The source treats an empty registry as a successful scan and reconciliation is single-run; settle this maybe-false finding by proving whether the EventStore projection poller replays pre-index aggregates after that scan exits.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Provide a supported unpark or replay path for parked aggregates that still need date-reminder recovery.
  evidence: `IndexedPendingDateAwaitSource` cleanly skips a durable parking record without reading the authoritative stream, so a stale or erroneous park can suppress an overdue resume indefinitely; this is pre-existing projection-recovery behavior.
  - Canonical entry: [2026-09-08 Group 1 unpark/replay finding](#story-4-8-canonical-unpark-replay); this close-out row is a later re-observation, not a second work item.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Preserve in-tenant partial reminder-scan evidence when exact caller cancellation follows an earlier candidate failure.
  evidence: The parking and stream exact-token catches rethrow immediately and discard that tenant's local partials/counts; the source remarks explicitly retain this prior human-decided limitation.
  - Canonical entry: [2026-09-16 in-tenant cancellation finding](#story-4-8-canonical-in-tenant-cancellation); this row preserves the later review provenance only.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Define per-await failure isolation and exhaustion for steady-state registration and startup reconciliation.
  evidence: Both `WorkItemSuspendedReminderHandler` and `DateReminderReconciler` fail fast on the first persistent await failure, allowing it to starve later valid awaits across every retry; the behavior predates the final harness increment.
  - Canonical entries: [2026-09-16 scheduler exhaustion finding](#story-4-8-canonical-scheduler-exhaustion) and [due-now recovery telemetry finding](#story-4-8-canonical-due-now-telemetry); this row does not create another starvation task.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Add bounded identity-bearing telemetry for due-now resume submission failures during reminder reconciliation.
  evidence: The due-now branch has only the eventual generic 4603 exception, unlike scheduler EventId 4609, so operators cannot identify the tenant, work item, or reminder that stopped the pass.
  - Canonical entry: [2026-09-16 due-now recovery telemetry finding](#story-4-8-canonical-due-now-telemetry); this row is retained only as close-out review history.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Retain or retry a fired date reminder until its submitted resume command reaches an acceptable terminal outcome.
  evidence: The gateway submitter returns an accepted response without polling terminal command status, after which `DateReminderActor` removes its durable registration; a later rejected, publish-failed, or timed-out command has no in-process retry trigger.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Establish and verify a bounded cancellation policy for stalled Dapr actor reminder-registration calls.
  evidence: The scheduler checks cancellation before actor remoting but cannot pass the token into `ScheduleResumeAsync`; settle this maybe-false finding with the configured Dapr remoting timeout or a stalled-proxy integration proof.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Prove Docker redirected reads always complete after probe cancellation and child termination on supported runners.
  status: done 2026-10-23
  resolution: `spec-five-deferred-verification-reliability.md` exercises both real redirected pipes through the production process adapter and harness on timeout and caller cancellation; records each read task, requires terminal completion and child exit, and preserves the exact caller token. Executed on Linux; other runners remain subject to their own execution.
  evidence: The production adapter uses token-aware `ReadToEndAsync` and kills the process first, but the current evidence does not establish that pipe reads cannot ignore cancellation indefinitely; a real child-process test would settle the maybe-false hang risk.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Decide whether the projection ingress must reject duplicate or gapped positive event sequences.
  status: done 2026-10-23
  resolution: The selected SDK rejects duplicate, conflicting, gapped and reordered rebuild histories as malformed_projection_outcome before inventory admission. InvalidHistoryCannotPublishPartialProjection proves zero accepted owners and unchanged committed generation/documents. Ordinary identical redelivery remains idempotent; existing executing SDK controls prove conflicting equal sequences park and missing-owner sequence delivery converges without serving partial state. [Qualification spec](spec-five-deferred-evidence-admission.md); [retained verification](evidence/five-deferred-evidence-admission-2026-10-23/manifest.json).
  evidence: `WorkItemProjectionDispatcher` sorts replay events but does not validate contiguity; settle this maybe-false finding by proving whether the EventStore projection-delivery contract can emit duplicate or gapped sequences.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Validate stream-page cursor metadata or document the EventStore invariant that makes malformed cursor shapes unreachable.
  status: done 2026-10-23
  resolution: The replacement WorksSourceStreamReader invokes the SDK StreamReadPageValidator and requires the authoritative head. SecondPageMetadataMustMatchItsEvidence injects corrupt second pages after a valid prefix: cursor/range mismatches, inconsistent last/head, hidden tail, truncated-at-head/empty, duplicate/nonpositive positions and foreign domain/aggregate. Every invalid matrix case refuses whole context after exactly two reads. Review reproduced a cross-page head regression that hid an already-observed tail; WorksSourceStreamReader now retains and validates the observed head. A growing-head control preserves the newly appended tail. [Qualification spec](spec-five-deferred-evidence-admission.md); [retained verification](evidence/five-deferred-evidence-admission-2026-10-23/manifest.json).
  evidence: `PendingDateAwaitStreamReader` trusts `IsTruncated`, `LatestSequence`, and `LastSequenceReturned`; inconsistent metadata could hide an unread tail or advance beyond returned events, but reachability requires gateway-contract evidence or fault injection.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Give repeated occurrences of the same date await distinct durable resume identities without losing redelivery idempotence.
  status: done 2026-10-24
  resolution: The SDK replacement binds scheduling and command identities to the suspension envelope. `spec-five-deferred-date-reminder-correctness.md` executes actual SDK submissions for same-date suspensions at sequences two and four, verifies distinct message IDs and exact resume commands, persists Submitted/replay audits, and retains identical receipt identities on repeated convergence after reconstruction. A stale earlier-occurrence callback submits nothing and preserves the current witness. [Verification evidence](evidence/five-deferred-date-reminder-correctness-2026-10-24/manifest.json).
  evidence: `DateResume` derives the EventStore message id only from tenant, work item, and instant; a legitimate re-suspension on that same instant within the 24-hour command-status retention can be deduplicated as the earlier occurrence. Fixing this pre-existing Story 4.6 design requires carrying a suspension occurrence or sequence through pending-await, reminder, and command identity.

<a id="story-4-8-canonical-reconciliation-stale-delay"></a>

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Recompute date-reminder delay at each scheduling operation during reconciliation.
  status: done 2026-10-24
  resolution: The retired reconciler is replaced by the SDK coordinator. `spec-five-deferred-date-reminder-correctness.md` executes a real SDK reconciliation pass over two retained Works targets with lost reminders; a two-minute first repair reduces the second delay from ten to eight minutes while persisted due witnesses remain unchanged. A deadline crossed during an earlier repair instead submits the later due intent and persists its disposition. [Verification evidence](evidence/five-deferred-date-reminder-correctness-2026-10-24/manifest.json).
  evidence: `DateReminderReconciler` snapshots `now` once before processing every candidate, so a future await scheduled late in a large pass receives its original relative delay and fires late by the time already spent processing earlier candidates; this behavior predates Story 4.8's baseline.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Normalize duplicate identical date awaits at the durable await-set boundary.
  status: done 2026-10-24
  resolution: `PendingDateAwaitProjection` now deduplicates normalized date conditions, matching the authoritative reminder source. `spec-five-deferred-date-reminder-correctness.md` covers equivalent UTC instants, mixed non-date awaits, distinct dates, ordinary delivery and full rebuild, persisted end-state, restart and redelivery without changing source events. [Verification evidence](evidence/five-deferred-date-reminder-correctness-2026-10-24/manifest.json).
  evidence: `PendingDateAwaitProjection` preserves duplicate `DateReached` conditions, producing repeated schedule or submit attempts and inflated reconciliation counts even though deterministic reminder and message ids prevent duplicate accepted outcomes. The normalization belongs with the pre-existing durable await-set contract.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Harden local EventStore and Admin Dapr invocation policies to deny by default and least privilege.
  evidence: The local-development EventStore and Admin configurations predate this story with `defaultAction: allow`, and the Works caller receives EventStore `POST /**`; enumerate the command and stream-read routes every Works recovery path needs before narrowing these policies.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Apply fail-closed page and payload identity validation to the child-completion and cascade recovery stream readers.
  status: done 2026-10-23
  resolution: Both replacement paths use WorksSourceStreamReader. InvalidRecoveryContextCannotProduceEffects executes child-completion and cancellation cascade against paged related evidence with foreign domain/aggregate, foreign payload tenant/aggregate, missing required members, unknown facts, mismatched relationships and malformed ChildSpawned. A valid earlier child cannot escape as a partial cascade when a later child is corrupt. No effects escape; corrected complete reads verify exact command payloads. SDK runner controls reconstruct quarantine with retained checkpoints, no pending plan and no submissions. Existing paged expiry-cascade controls also pass. [Qualification spec](spec-five-deferred-evidence-admission.md); [retained verification](evidence/five-deferred-evidence-admission-2026-10-23/manifest.json).
  evidence: The pre-existing Story 4.7 readers do not consistently validate stream domain or decoded event identity and silently discard malformed lifecycle or child evidence, allowing false parent-resume decisions or incomplete cascade checkpoints. Their separate paging defect remains owned by DW-86.

## Deferred from: code review of spec-4-8-register-and-reconcile-date-reminders-durably-11.md (2026-09-21)

- Spec-10 Verification Observed is still titled “final reviewed tree” and lists harness 33/33, Integration 526/526, and live 4/4, which can be read as current against spec-11’s 41/534 and no-new-live-credit evidence. Deferred: fix edits another spec. [_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-10.md:212]
- The dated 2026-09-20 spec-10 five-patch close-out File List still omits `spec-4-8-register-and-reconcile-date-reminders-durably-10.md`, unlike earlier dated blocks that include their implementing specs. Deferred: pre-existing spec-10 BH-12 inventory gap, not one of spec-11’s six named patches. [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:711]

## Deferred from: final bmad-build review of Story 4.8 (2026-09-21)

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Preserve pending-date-await index repairability when a decoder upgrade recognizes a previously unknown event.
  status: done 2026-10-24
  resolution: The SDK complete-replay boundary replaces pending documents independently of their old watermarks. `spec-five-deferred-date-reminder-correctness.md` executes the production partitioned dispatcher to repair synthetic legacy high-watermark/tombstone state, with and without old parking, from unchanged currently-decodable persisted history. Staging retains the old generation; promotion repairs discovery and clears parking. Existing unknown-event controls continue to refuse current undecodable evidence. This models the old persisted outcome; it does not execute an older decoder. [Verification evidence](evidence/five-deferred-date-reminder-correctness-2026-10-24/manifest.json).
  evidence: `WorkItemProjectionDispatcher` advances the raw maximum-sequence watermark while folding only decoded events, so its `storedLastSequence >= incomingLastSequence` guard can reject the same replay after a later decoder upgrade would make that event state-affecting.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Recompute date-reminder delay for each steady-state registration.
  status: done 2026-10-24
  resolution: The retired handler delegates scheduling to the SDK coordinator. `spec-five-deferred-date-reminder-correctness.md` executes two current Works date intents with first-arm latency, checks each relative delay against its own operation time and stable persisted absolute witnesses, and verifies no redundant rearm after reconstruction. Lost-reminder controls cover lookup latency and elapsed-delay clamping; a deadline crossed during earlier registration submits the later due intent while preserving an independent future witness. [Verification evidence](evidence/five-deferred-date-reminder-correctness-2026-10-24/manifest.json).
  evidence: `WorkItemSuspendedReminderHandler` snapshots the clock once before scheduling every pending await, so later registrations can fire late by the latency spent awaiting earlier actor calls; this pre-existing behavior belongs with the reconciliation stale-delay family.
  - Canonical entry: [reconciliation stale-delay family](#story-4-8-canonical-reconciliation-stale-delay); this row records the steady-state sibling without creating a second reconciliation work item.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-21)

- `Recovery_re_registers_a_still_future_await_that_later_fires` still deletes the Host 1 reminder and stops the host without waiting for the `WorkItemSuspended` durable consumer marker (`WorksReminderRecoveryPipelineSmokeTests.cs:174`). Pre-existing sibling of the three-host overdue fact this increment patched; Host 2 can still pass on subscription redelivery rather than startup re-registration.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-21, Group 1 Reminders)

- One reconciler submit/schedule failure still exits `ProcessAsync` and starves later awaits. Pre-existing; Group 1 re-observation only.
  - Canonical entry: [per-await isolation and exhaustion](#story-4-8-canonical-scheduler-exhaustion)
- Due-now `SubmitAsync` still has no bounded tenant/work-item/reminder failure log (EventId 4603 only). Pre-existing; Group 1 re-observation only.
  - Canonical entry: [due-now recovery telemetry](#story-4-8-canonical-due-now-telemetry)
- Exact caller cancellation after an in-tenant candidate failure still discards that tenant's local pending and counts. Pre-existing; source remarks retain the exact-token filters.
  - Canonical entry: [in-tenant cancellation](#story-4-8-canonical-in-tenant-cancellation)
- `ReminderReconciliationService` still returns after `ReminderReconciliationMaxAttempts` with no post-startup retry or readiness degrade. Pre-existing; Group 1 re-observation only.
  - Canonical entry: the 2026-09-20 post-startup retry/readiness `source_spec` row
- `PendingDateAwaitStreamReader` still trusts truncated-page cursor metadata and does not apply the dispatcher's AD-27 non-positive/monotonic envelope checks. maybe-false (would be medium): settle with gateway-contract evidence or fault injection that `LastSequenceReturned` can stall at `from`, or that a page can carry non-positive or non-increasing `SequenceNumber`s.
  - Canonical entry: the 2026-09-20 stream-page cursor metadata `source_spec` row

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-21, Group 2 Projections)

- No unpark, delete, or operator replay path; a later successful shared rebuild still leaves `projection:works:parked:{tenant}:{id}` in place so `/project` and reminder recovery keep skipping. Pre-existing; Group 2 re-observation only.
  - Canonical entry: [2026-09-08 Group 1 unpark/replay](#story-4-8-canonical-unpark-replay)
- Mixed-case reserved tenant `TENANTS` still misses `WorksReadModelKeys.IsReservedTenantId` (`StringComparison.Ordinal`) on `/project`, then `TenantId` lowercases to `tenants`. Pre-existing; reserved-tenant spec Never list puts mixed-case direct `/project` out of scope.
- `UseCurrentSchemaAsync` is still read once per dispatch and reused across later awaits, so a concurrent schema cut-over can make this dispatch write resurrected legacy keys. Documented DW-84 generation-switch race; not Story 4.8 reminder/index behavior.
- Equal-sequence pending-date watermarks (`storedLastSequence >= incomingLastSequence`) can still reject the same full replay after a decoder upgrade would make a previously skipped event state-affecting. Pre-existing; Group 2 re-observation only.
  - Canonical entry: the 2026-09-21 final bmad-build index-repairability `source_spec` row

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-22, Group 3 Runtime)

- Concurrent deliveries that both observe no durable marker both run handlers. Pre-existing Dapr marker-store contract: `TryAcquireAsync` reads state and does not persist an in-progress lease, so `Acquired` is not exclusive. Adding a lease is an EventStore protocol change, not a Works-only patch. [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:62]

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-22)

- Domain-event `Skipped`, `Duplicate`, and `MarkerFailure` still log raw `EventTypeName` and `CorrelationId`. Pre-existing; commit `c2a59b8` only added the caught exception argument on `MarkerFailure`. [src/Hexalith.Works/Runtime/Events/WorksDomainEventLog.cs:31]

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-22)

- `ReadModelWriteContext.WithEventDiagnostics` still copies the first raw correlation id and up to eight raw event-type names into read-model conflict and exhaustion logs. Pre-existing platform helper; this close-out sanitized projection EventIds 4500 and 4504 only. [references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/ReadModelWritePolicy.cs:254]

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Determine whether projection parking failures must reset after an intervening healthy replay at the same sequence.
  evidence: The durable counter is not reset on the healthy path, but the decoder is deterministic for an immutable stream; establish whether the projection contract or a supported upgrade/rollback can produce a successful delivery between two failures at the same sequence before changing the persistence policy.

## Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-09-22, incremental f1f377f)

- `WorksAppHostTopologyTests` never disposes `IDistributedApplicationTestingBuilder` (no `await using`) in any topology fact, including the new Keycloak credential fact; each undisposed builder retains its host configuration sources. Pre-existing file-wide pattern; fix all facts together. [tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs:246]

- source_spec: `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md`
  summary: Define a trusted-effect HTTP status contract that separates collision (409), denial (403), unavailable or unconfigured policy (503), and transient failure, and keep that distinction across Dapr actor remoting.
  evidence: Review findings B1/E12, 2026-09-29. Actor-side throws in `references/Hexalith.EventStore/src/Hexalith.EventStore/Controllers/TrustedEffectsController.cs` (collision, deletion fence, erasure marker, missing receipt) arrive as `ActorMethodInvocationException`. `GlobalExceptionHandler` turns them into 500. A missing policy returns 403. The owner deferred this to the 4.15 producer adoption because it needs a typed outcome or a result discriminator, which is new public surface.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md`
  summary: Redesign the trusted-effect client SDK surface: typed gateway errors that keep ProblemDetails, a DI registration helper, and a client-side context without the ignored `Workload` field.
  evidence: Review finding B2, 2026-09-29. `HttpTrustedEffectSubmitter` calls `EnsureSuccessStatusCode` and drops the body. It has no helper like `AddEventStoreGatewayClient`. `TrustedEffectContext.Workload` is never sent and the gateway derives the workload from Dapr authentication. This API is published in EventStore 3.109.0, so any change is a versioned breaking change. Deferred to 4.15.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md`
  summary: Give delegation issuers a supported way to compute the `command_digest` claim without referencing the EventStore Server package and the target domain's intent adapter.
  evidence: Review finding B3, 2026-09-29. The digest is `RenderDigest(SHA256(descriptor.CanonicalIntent))`, computed through `IIdempotencyIntentAdapterRegistry` and `CanonicalIdempotencyIntentEncoder` in `Hexalith.EventStore.Server`. A non-.NET IdP cannot reproduce it. This ties to the AD-23 delegation shape and the 4.15 issuer integration. The guide's claim table is patched in 4.13; the helper or protocol decision is deferred.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md`
  summary: Bound the tenant lifecycle trusted-effect inventory: per-effect or sharded state, compaction of settled effects, and validation cheaper than O(n·m) on every load.
  evidence: Review findings B10/E5, 2026-09-29. `IdempotencyTenantLifecycleRecord.TrustedEffects` gains one entry per admitted effect until purge. Every load runs SHA-256 validation over the whole array. Each effect rewrites the single state document about four times. Harmless at synthetic scale while production admission is closed. Must be fixed before 4.16 production admission.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md`
  summary: Erase ordinary-command idempotency records and legacy redirects in tenant-owned aggregate partitions as part of the broader AD-28 offboarding workflow.
  evidence: Review findings B6/E20, 2026-09-29. `idempotency:*` records carry `ResultPayload`, are written with no TTL, and are never deleted; lifecycle purge reaches only admission tombstones, directory aliases, and legacy inventory. Actor state cannot be enumerated, so erasing them needs a per-partition key index. This predates 4.13, which removes only `idempotency:wrk-*` records.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md`
  summary: Replace `CoordinatedCommandActor`'s non-fenced replay detection by event `CausationId` with the target's idempotency inspection by message ID.
  evidence: Review findings B19/E10/E11, 2026-09-29. EventStore commit `e851dd726c08c27f46a95adea6c3797e56929416` belongs to separate work, not 4.13. Events carry `command.CausationId ?? CorrelationId` (`EventPersister.cs:58`). A chained command whose causation equals this command's MessageId gives a false positive and bypasses the coordinated source conflict. An explicit causation that differs from the MessageId gives a replay miss.

## Deferred from: code review of spec-4-13-publish-eventstore-trusted-effect-submission.md (2026-09-29, iteration 1)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md`
  summary: Measure the per-command cost of the tenant-deletion closure check in `AggregateActor.ProcessCommandCoreAsync` and set a perf budget before 4.16 production admission.
  evidence: Review iteration 1, 2026-09-29. The B5 review fix added `StateManager.ContainsStateAsync` reads to every ordinary command (`references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs:687-690`). Dapr.Actors 1.18.10 `ActorStateManager.ContainsStateAsync` does not cache absent keys, so each read is a sidecar/state-store round trip on the hot path. The owner chose to measure first. If the cost matters, the options are a per-activation closure flag updated by the fence and erasure methods, or a marker folded into a state read the command path already performs, which changes a durable type and needs AD-28 approval.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-09-29, iteration 0)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Define retention, TTL, and tenant-offboarding erasure for typed-reminder disposition records before 4.16 production admission.
  evidence: Review finding BH10, 2026-09-29. `ReminderCoordinator.TryWriteDispositionAsync` writes `eventstore:reminders:v1:{ActorTypeName}:item:{wra-id}:disposition:{subject}` with no TTL, one key per reminder name or evidence digest. These records carry `Tenant`, outlive the erasure of item state, and no erasure path reaches them. The frozen intent requires AD-28 owner approval of retention, legal hold, and offboarding before any new durable type or real data is admitted, so the policy belongs to the 4.16 gate. The per-subject last-writer-wins overwrite also means only the latest outcome is retained until the Platform append-only audit backend lands.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-09-30)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Prove that only the Dapr Scheduler can reach the typed-reminder callback under the production mTLS/ACL profile.
  evidence: Unverified high finding from the production-code review. `ReminderCallbackTokenFilter` authenticates the local sidecar with `APP_API_TOKEN`, but the reviewed diff does not establish whether a different Dapr workload can service-invoke the actor reminder callback and receive the target sidecar's automatically injected app-channel token. Settle with a Story 4.16 cross-application negative test under the production ACL profile; if it reaches the callback, add a Scheduler-only admission control before production opens.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-09-30, iteration 1)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Exclude the Story 6.1 P1R 3.109 version-pinned evidence projects from the shared consumer-package authority validator.
  evidence: Review findings R1-EC10 and R1-BH1. The three tracked `6-1-p1r-3109` evidence projects intentionally use exact public package versions and disable central package management, but `ContractsPackageDependencyTests._standaloneEvidenceProbeProjects` lists only the earlier 3.108.1 evidence. The broad Contracts binary confirms 18 deterministic validator errors. This is unrelated post-baseline Story 6.1 work.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Bound every external command in the Story 6.1 public-package evidence replay.
  evidence: Review finding R1-EC11. Network downloads use a 90-second timeout, but `verify_public_packages.py` invokes `git`, `dotnet nuget verify`, and the package validator through `subprocess.run` without a timeout, so a stalled tool can hang evidence replay indefinitely. This is unrelated post-baseline Story 6.1 work.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: URI-escape Story 6.1 rollback-probe state keys before placing them in Dapr state-read paths.
  evidence: Review finding R1-EC12. The probe writes the caller-supplied prefix as a JSON state key but interpolates it raw into the read URI. Documented evidence uses safe prefixes, yet reserved characters can select another path or query. This is unrelated post-baseline Story 6.1 work.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Establish whether an approved restore can contain reminder item state while omitting both its tenant registry and candidate index entries.
  evidence: Review finding R1-BH9 (unverified medium). Reconciliation cannot discover item state absent from both index documents, but no approved reminder backup/restore model exists yet. Story 4.16's restore drill must prove cross-key restore ordering and atomicity; if partial discovery loss is reachable, add an independent stream or item-state enumeration/rebuild seam.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-09-30, chunk 1)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Keep a quarantined reminder name unarmed until an operator disposition path exists.
  evidence: Chunk 1 review. `ConvergeCoreAsync` drops a current stream intent whose name is already quarantined, or whose quarantine reason is `stored-entry-invalid` or `stored-entry-duplicate` (`ReminderCoordinator.cs:592-614`). Nothing in this story clears `ReminderQuarantineRecord` or a `Quarantined` entry. The frozen spec assigns operator disposition to Story 4.16.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Cap or shard per-item reminder quarantine evidence before production admission.
  evidence: Chunk 1 review. `MaxCandidatesPerTenant` bounds the discovery index only. Each distinct quarantine digest is appended to the item document, and every convergence rewrites a disposition for each existing record (`ReminderCoordinator.cs:717-730`). Retention and offboarding of this evidence belong to the Story 4.16 AD-28 gate. Quarantine `RecordedAt` itself stays stable because `AddQuarantine` dedupes by digest.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Give operators a repair for a candidate whose actor id does not re-derive from its tuple.
  evidence: Chunk 1 review. `ReminderReconciler.RunPassAsync` counts that row as incomplete and continues (`ReminderReconciler.cs:87-92`). `CompletePass` does not prune while the pass is incomplete, so readiness stays degraded and no later pass removes or repairs the row. Deleting it could discard the only stored target coordinates. Operator repair belongs to Story 4.16.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-10-01, iteration 2)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Give operators a repair for a blank tenant left in the reminder registry.
  evidence: Review finding R2-BH3. `ListCandidatesAsync` rejects a blank tenant, and `ReminderReconciler` counts that exception as an incomplete pass without removing the registry row, so readiness stays degraded. Operator repair belongs to Story 4.16.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Confirm whether one hung Dapr reminder-actor call can block the rest of a reconciliation pass.
  evidence: Review finding R2-BH5 (unverified medium). `DaprReminderActorInvoker` checks cancellation only before creating the proxy, and `ReminderActor` runs the turn with `CancellationToken.None`. Settle this by checking the Dapr actor HTTP timeout: a timeout that throws is already isolated per candidate; an infinite wait would block later tenants.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-10-01, iteration 3, chunk A)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Add retention, TTL, audit history, and erasure for reminder disposition records and index rows.
  evidence: Iteration 3 BH2/BH15, carrying iteration 0 BH10. `TryWriteDispositionAsync` writes one last-write-wins key per subject with no TTL (`ReminderCoordinator.cs:1603`). Item state, candidates, the tenant registry, and dispositions are never removed when Story 4.13 erasure removes a stream or a tenant is offboarded. This waits for the Story 4.16 AD-28 gate and the Platform audit sink.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Stop re-auditing and Error-logging historical quarantine records on every convergence without losing the audit retry.
  evidence: Iteration 3 BH3/EC10, carrying R2-BH1. The loop at `ReminderCoordinator.cs:750` rewrites a disposition and logs event `200207` at Error for every stored record on every pass. It is also how failed actor-collision and repair audits are retried, so the fix needs a persisted audited flag.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Repair or retire index candidates that can never converge.
  evidence: Iteration 3 BH4/EC4/EC3, carrying chunk 1, R2-EC6, R2-BH3, and R2-BH4. Four kinds of candidate keep every pass incomplete, readiness Degraded, and the loop on `RetryInitialDelay`: null rows, actor-ID mismatches (`ReminderReconciler.cs:89`), stored headers re-indexed without re-derivation on the fail-closed and actor-collision paths, and folds that always throw, for example after erasure. Operator repair belongs to Story 4.16.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Confirm that every call inside a reminder actor turn is bounded by a finite timeout.
  evidence: Iteration 3 BH9 (unverified medium), carrying R2-BH5. `ReminderActor` passes `CancellationToken.None`, and `DaprReminderActorInvoker` checks cancellation only before creating the proxy (`DaprReminderActorInvoker.cs:27`). Settle this by measuring the Dapr actor proxy, store, and submitter timeouts under a hung dependency.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Prove AC4 against the named public EventStore release that contains the R6 API.
  evidence: Iteration 3 AA5. `PackagedReminderApiRunsWithoutWorksTypes` has passed only against local pack `3.110.0-local.431`. Public 3.110.0 (source `27279fe6`) lacks `ReminderIdentityCodec`, `IReminderIntentSource`, and `AddEventStoreReminders`. This is the close gate set by the frozen release decision.
  status: resolved (2026-10-04)
  resolution: The owner-published EventStore `3.112.0` release (source `38efbefd5da65d538723f9f85eca6a186dfc0a2f`) passed `PackagedReminderApiRunsWithoutWorksTypes` with one Pass and zero errors/skips, exercising all three isolated package-only consumers. Retained evidence: [passing public-package proof](evidence/story-4-11-public-3.112.0/public-reminder-proof.xml) and [public package summary](evidence/story-4-11-public-3.112.0/summary.json).
- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Give every Dapr actor host a liveness-only `/healthz` so a failing readiness check cannot evict its actors from placement.
  evidence: Iteration 3 EC13, deferred by owner decision on 2026-10-01. `MapEventStoreReminders` calls `MapActorsHandlers` (`EventStoreReminderEndpointExtensions.cs:30`). Dapr.Actors.AspNetCore 1.18.10 `MapActorHealthChecks` maps `/healthz` with no predicate, and domain-service hosts map only `/health`, `/alive`, and `/ready`, so an Unhealthy `ready` check (missing app-channel token, unreachable state store, or a host-added check) answers 503 and Dapr disconnects a host that reports actor types from placement (dapr/dapr#7355). Reason: the gateway and Operations hosts share the same Dapr SDK `/healthz` behavior; fix every actor host once in the R2 service defaults and prove it in 4.16.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Preserve the explicit persist-before-publication invariant when refreshing the Epic 4 agent context.
  status: done 2026-10-23
  resolution: The context now explicitly requires EventStore to persist events before publication, independently of delivery acknowledgement (architecture AD-01). See [spec-five-deferred-epic-context.md](spec-five-deferred-epic-context.md) for authorities and validation.
  evidence: Continuation review C3-BH9 (2026-10-01). An earlier committed refresh of `epic-4-context.md` replaced the explicit publication-order statement with acknowledgement-after-durable-commit wording. The architecture remains authoritative and is loaded by this spec, but future context refreshes should carry the explicit order. This run did not edit the context, and the build workflow routes agent-context edits to deferred work.
- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Reconcile the open intent-gap table in the Works CI/CD spec and retain its broad Integration TRX outside `/tmp`.
  evidence: Story 4.11 review C7-BH13. `spec-implement-works-ci-cd.md` still places a 2026-09-20 "Review iteration 3" table after iteration 5, with open intent-gap rows 72–74, 93, and 103, and records the 514/514 Integration pass only as `/tmp/works-loop2-final-broad.vn9QkM/final-broad.trx`. This is outside Story 4.11.

- source_spec: `spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Custom store can resolve an eraser for a different backend (C11-EC1).
  evidence: ReadModelStoreServiceCollectionExtensions at the preserved f378afdb baseline already falls back to DaprReadModelStore when a custom IReadModelStore lacks conditional erasure. Reminder cleanup exposes this existing shared registration defect. Qualify matching store/eraser bindings centrally rather than changing generic registrations in this reminder continuation.

- source_spec: `spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Mapped HTTP cancellation forwarding lacks regression tests (C11-VG2).
  status: done 2026-10-23
  resolution: `spec-five-deferred-verification-reliability.md` adds eleven consumer controls invoking actual mapped SDK query, replay-state and project HTTP delegates with live RequestAborted; each forwards the exact token and observes handler-triggered cancellation without a success body. These isolate mapping and JSON binding; protected ingress remains covered separately.
  evidence: Pre-verified HTTP-boundary gap belongs to existing unrelated /query, /replay-state and /project changes from ff1f5588e2b0d6807b8df018637ae3dbdf9c86fb (git blame). The 4.11 reminder change adds only its opt-in mapping hunk to that shared file.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Qualify the Epic 4 context Claim-only InProgress rule to preserve the existing Suspended-to-Resume transition.
  status: done 2026-10-23
  resolution: The context distinguishes Claim from Assigned/Queued and exact-current-await Resume from Suspended, matching WorkItemLifecycle and architecture AD-13. See [spec-five-deferred-epic-context.md](spec-five-deferred-epic-context.md) for authorities and validation.
  evidence: Story 4.11 continuation review C12-BH7 (2026-10-08). `epic-4-context.md` says Claim is the only entry to InProgress, while the existing `WorkItemLifecycle` also resumes suspended work. This run did not modify lifecycle behavior or context; agent-context corrections belong to a dedicated refresh.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Restore the explicit runtime persisted-end-state and recovery evidence requirement in the next Epic 4 context refresh.
  evidence: Story 4.11 continuation review C12-BH9 (2026-10-08). The earlier context refresh dropped that sentence while retaining Tier-1 isolation. Story 4.11 itself retains executed persisted-state evidence for all five frozen matrix rows; the omission affects future context-guided stories.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-10-09, C15)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Make the transitional Works host pass the EventStore Story 5.5 startup route inventory, which sibling-source Debug builds already enforce.
  evidence: Story 4.11 delta review C15-BH1b (2026-10-09), high. Works Debug defaults `UseHexalithProjectReferences=true`, so `WorksHost` compiles against EventStore source, which has carried Story 5.5 since Works `dcd90e7` pinned `07d1e23a`. With `xUnit1051` demoted (the Debug integration-test build otherwise fails on 19 such analyzer errors), `WorksDomainEventSubscriptionTests.WorksHostExposesOneDeliveryRouteAndOneDiscoveryRoute` fails with "route inventory violates the internal trust boundary". The violations are `/project` (the bespoke `MapPost` lacks `.RequireEventStoreDomainServicePolicy("/project")`), `/work/events` (`MapWorksDomainEvents()` lacks `.RequireEventStoreSidecarChannel()`), and `/dapr/config` plus `/actors/*` (bare `MapActorsHandlers()` after `UseEventStoreDomainService`). Package mode is unaffected until the `3.114.0` pin moves to ≥3.117. Pre-existing; not caused by the C13/C14 patches.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Add app-channel token configuration to the EventStore Kubernetes sample domain service.
  evidence: Story 4.11 delta review C15-BH5 (2026-10-09). In `references/Hexalith.EventStore/samples/deploy/kubernetes/dapr-annotations-example.yaml`, `sample_annotations` has no `dapr.io/app-token-secret` and there is no `APP_API_TOKEN` entry, while `EventStoreDomainServiceSecurityStartupValidator` fails every SDK domain service's startup without the token outside Development. This is a pre-existing EventStore Story 5.5 sample gap; the sample is not a reminder host.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Document `/alive` as the Dapr app health-check path for every EventStore domain service, not only reminder hosts.
  evidence: Story 4.11 delta review C15-EC1 (2026-10-09). An SDK domain service without actor handlers never maps `/healthz`, so a Dapr app health check on the default path fails there too. The typed-reminder guide is correctly scoped to reminder hosts; general deployment guidance belongs to EventStore's domain-service documentation.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-10-10, C21)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Make an Aspire-composed reminder host get the right `EventStore:Reminders:Workload` default instead of documenting a manual override.
  evidence: Story 4.11 delta review C21-BH4 (2026-10-10), medium. `AddEventStoreDomainModule` already holds `appId` and sets `EventStore__DomainService__AppId` and `EventStore__DomainService__ServiceVersion`, but not the reminder `Workload` (`references/Hexalith.EventStore/src/Hexalith.EventStore.Aspire/HexalithEventStoreDomainModuleExtensions.cs:65`). `ResolveWorkload` (`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreReminderServiceCollectionExtensions.cs:126`) falls back to `DAPR_APP_ID`, then the application name, and reads neither `EventStore:DomainService:AppId` nor `Authentication:WorkloadIssuer:Workload`. Since neither EventStore's Aspire helper nor CommunityToolkit Dapr hosting sets `DAPR_APP_ID`, every such host defaults to the application name (`Hexalith.Works`) while the issuer `azp` is the app ID (`works`). Candidate fixes: set `EventStore__Reminders__Workload` in the Aspire helper, or let `ResolveWorkload` prefer the resolved domain-service app ID. Pre-existing runtime and package behavior; the C19/C20 guides document the workaround, and a source fix needs a new named public EventStore release.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Carry the `EventStore__Reminders__Workload` requirement into the Story 4.15 Works adoption spec.
  status: done 2026-10-28
  resolution: `spec-five-deferred-contract-and-proof-integrity.md` adds the workload handoff to Story 4.15 implementation notes and current configuration docs. `AddWorksReminderAndCascadeRecovery` already sets resolved reminder workload `works` and both effect purposes; existing scheduling/recovery-policy facts verify those options. The canonical host needs no redundant environment override; frozen intent is unchanged.
  evidence: Story 4.11 delta review C21-BH5 (2026-10-10), medium. The requirement lives only in EventStore `docs/guides/typed-reminders.md` (the Story 4.15 handoff list) and this spec's C19 record. `spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md` (draft) never mentions it, and the Works AppHost calls `AddEventStoreDomainModule(…, "works", …)` (`src/Hexalith.Works.AppHost/Program.cs:141`) without it. Without the override, Works reminder submissions fail before HTTP as `delegation-failed` or `delegation-unavailable`, or at the gateway as `403`. Set it to `works` in the 4.15 acceptance criteria unless C21-BH4 is fixed at the source first.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-10-10, C27)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Qualify the Epic 4 context claim-loss guidance so a surface does not promise the winning party's identity without an authorized source.
  status: done 2026-10-23
  resolution: Claim-loss guidance now refreshes authorized evidence and permits a winner identity only from an authorized source; the rejection contract supplies no winner. See [spec-five-deferred-epic-context.md](spec-five-deferred-epic-context.md) for authorities and validation.
  evidence: Story 4.11 review C27-BH6 (2026-10-10), low. `epic-4-context.md:49` says a claim loss names the other party, but `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs` records only status and attempted act, and the executor query filters another party's assigned items. Agent-context changes are deferred by the build workflow.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Qualify Epic 4 problem-detail guidance so tenant context appears only after authorization permits disclosure.
  status: done 2026-10-23
  resolution: Problem details now include tenant context only when authorization permits disclosure, preserving the architecture AD-23 non-disclosure boundary. See [spec-five-deferred-epic-context.md](spec-five-deferred-epic-context.md) for authorities and validation.
  evidence: Story 4.11 review C27-BH7 (2026-10-10), low. `epic-4-context.md:37` says problem details carry tenant context, while the epic's AD-23 acceptance requires denial before tenant-existence disclosure. Agent-context changes are deferred by the build workflow.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Restore the terminal-work mutation and defined no-op rule in the Epic 4 context.
  evidence: Story 4.11 review C27-EC8 (2026-10-10), low. The refreshed `epic-4-context.md` omits the rule, although `docs/lifecycle-transition-matrix.md:196` requires terminal invalidity to reject except for defined idempotent no-ops. Agent-context changes are deferred by the build workflow.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-10-10, C29)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Restore the distinct idempotent no-op outcome in Epic 4 headless experience guidance.
  evidence: Story 4.11 review C29-BH4 (2026-10-10), low. `epic-4-context.md:48` lists four outcome classes but omits the distinct idempotent no-op required by `epics.md:182`; future surfaces could report a no-op as accepted work. This is an agent-context correction.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Restore projection freshness and explicit Unavailable guidance in the Epic 4 context.
  status: done 2026-10-23
  resolution: Headless guidance now requires freshness, safe and labeled stale results, or explicit Unavailable; partial or untrustworthy results cannot appear fresh or as numeric zero (UX-DR6/25). See [spec-five-deferred-epic-context.md](spec-five-deferred-epic-context.md) for authorities and validation.
  evidence: Story 4.11 review C29-BH5 (2026-10-10), low. `epic-4-context.md:48` omits the freshness and Unavailable behavior required by `epics.md:158,196`; a later surface could present a stale or repairing projection as zero or current. This is an agent-context correction.

## Deferred from: code review of spec-4-11-publish-eventstore-typed-reminder-reconciliation.md (2026-10-10, C30)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Apply the eight queued Epic 4 context corrections in one `epic-4-context.md` refresh before the next Epic 4 story is drafted.
  evidence: Story 4.11 review C30-BH11 (2026-10-10), low. The eight one-line corrections are C3-BH9, C12-BH7, C12-BH9, C27-BH6, C27-BH7, C27-EC8, C29-BH4, and C29-BH5 (`deferred-work.md:1195-1262`). They sit under three review headings with no owner or trigger, and each delta review raises them again (C29 carried six). This is an agent-context correction.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md`
  summary: Make the sprint tracker's `generated` stamp pass `sprint_plan.py validate`.
  status: done 2026-10-23
  resolution: Normalized the original generation stamp to a supported minute-resolution format; its exact original offset/seconds remain in a comment, all development statuses and action items are preserved, and the existing validator now reports valid=true with no problems. See [spec-five-deferred-tracking-integrity.md](spec-five-deferred-tracking-integrity.md).
  evidence: Story 4.11 review C30-EC5 (2026-10-10), low. `validate` flags only `generated: 2026-06-15T18:47:50+02:00` (`sprint-status.yaml:37`) against `%m-%d-%Y %H:%M`, while `status` accepts it. The sprint-planning generator wrote the ISO form. This is pre-existing: C28-EC1 fixed only `last_updated`.

## Deferred from: code review of spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md (2026-10-11, C12 chunk 1)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Monotonic process candidate retention caps the lifetime source count near the supported capacity.
  evidence: Story 4.12 review C12 BH7+AA9 (2026-10-11), medium. `RetainedCandidates` only grows (`ProcessSweepStore.cs:162`) and sources are never retired. Option ceilings (T ≥ 100 ms, Q ≥ (3+5E)T, A ≤ 30 min) cap `SupportedCandidateCount` near 540, so past that lifetime count readiness stays `capacity-exceeded` permanently. This waits on the AD-28 owner retention and offboarding policy and matters for the Story 4.15 handoff.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Losing or restoring the process sweep record resets Cycle and Generation below persisted source values.
  evidence: Story 4.12 review C12 EC11 (2026-10-11), unverified medium. If only the sweep key is lost (`ProcessSweepStore.cs:20`), a source `LastDispositionGeneration` above the new generation makes `RevalidateAsync` return false until generations catch up (`ProcessCheckpointStore.cs:135`). A contract for state-provider restore and partial key loss, or the AD-28 restore proof, would settle whether this is reachable. Joins cycle 10 B9.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: The single process sweep record is a serialization and CAS point for every source mutation.
  evidence: Story 4.12 review C12 BH10 (2026-10-11), unverified medium. Each checkpoint or status change reads and re-serializes a record of up to 256 KiB (`ProcessSweepState.cs:143`), hashes every map key, and CASes it with 8 write attempts. A load test with concurrent direct `RunAsync` callers would settle whether CAS exhaustion occurs at the supported capacity.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: The AD-28 synthetic-only gate for process control records exists only in documentation.
  evidence: Story 4.12 review C12 AA-C10, carried from cycle 4 B13 (2026-10-10), unverified high. `docs/guides/checkpointed-processes.md:38` states the gate, but no code enforces it. Platform admission and release evidence would settle whether code enforcement is required.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Direct runner and disposition invalidation reset an in-progress cycle and abort hosted passes.
  evidence: Story 4.12 review C12 BH4+AA3 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessSweepStore.cs:249`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: An unaccepted publication admission pins the catalog and freezes candidate service.
  status: done 2026-10-24
  resolution: Current SDK yields from rejected catalog admission to retained candidates: a three-unit pass with all 128 publication slots occupied still checkpoints a healthy Works source. The unaccepted key remains held until space frees; the candidate-starvation defect is superseded, while admission remains fail-closed. See [qualification spec](spec-five-deferred-process-recovery-policy.md) and [retained packet](evidence/five-deferred-process-recovery-policy-2026-10-24/manifest.json).
  evidence: Story 4.12 review C12 BH8+AA2 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessReconciler.cs:235`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: The sweep-record byte ceiling wedges reconciliation.
  evidence: Story 4.12 review C12 BH9+AA1+EC9 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessReconciler.cs:105`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: An undrainable pending observation or publication freezes all hosted processing.
  evidence: Story 4.12 review C12 EC7+EC8 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessSweepStore.cs:117`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: The process-wide read pools refuse admission without waiting and fail under ordinary concurrency.
  evidence: Story 4.12 review C12 BH12+AA5 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessReadDeadline.cs:19`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Carried: orphaned transition handoff after a failed source CAS.
  evidence: Story 4.12 review C12 BH3+EC5+AA-C1; cycle 10 E1, cycle 11 B8/E1 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessCheckpointStore.cs:85`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Carried: a malformed persisted `CatalogAfterKey` can sort past every source and complete an incomplete catalog scan.
  evidence: Story 4.12 review C12 AA-C2; cycle 10 B4, cycle 11 B3 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessSweepState.cs:88`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Carried: a large durable tail keeps one unreadable candidate chain ahead of later tenants for many passes.
  evidence: Story 4.12 review C12 AA-C3; cycle 10 B6, cycle 11 B5 (2026-10-11), high; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessReconciler.cs:395`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Local read-admission exhaustion counts as a source retry and parks healthy sources.
  evidence: Story 4.12 review C12 EC3 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/CheckpointedProcessRunner.cs:312`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: A catalog failure discards the catalog continuation every cycle.
  evidence: Story 4.12 review C12 EC12 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessReconciler.cs:226`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: A missing or erased source stream parks the source permanently as `source-read`.
  evidence: Story 4.12 review C12 EC1 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/CheckpointedProcessRunner.cs:86`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Maximum-size domain events can never be processed.
  evidence: Story 4.12 review C12 AA4 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/CheckpointedProcessRunner.cs:277`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: A backlog drains at four envelopes per source per pass, while the guide says eight.
  evidence: Story 4.12 review C12 BH5 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/CheckpointedProcessRunner.cs:75`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: The process runtime has no logging or metrics.
  evidence: Story 4.12 review C12 BH15+AA8 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessReconciler.cs:721`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Carried: a full pending-observation map stalls both scans while only one marker is retried per pass.
  evidence: Story 4.12 review C12 AA-C4; cycle 10 B7, cycle 11 B6 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessReconciler.cs:105`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Carried: a restored missing page cannot clear its unknown-page flag after a complete membership recheck.
  evidence: Story 4.12 review C12 AA-C5; cycle 10 B8, cycle 11 B7 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/ProcessCheckpointStore.cs:357`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Carried: admitted command bytes can exceed the trusted-effects endpoint's 1 MiB request cap after base64 encoding.
  evidence: Story 4.12 review C12 AA-C6; cycle 10 B2, cycle 11 B1 (2026-10-11), medium; decision deferred by the owner. Primary site `src/Hexalith.EventStore.DomainService/EventStoreProcessOptions.cs:76`; full evidence is in the spec's Review Findings. Deferral reason: Cycle 12 found new gaps in the sweep-record design again; deferred to a dedicated process-runtime hardening story that must land before Story 4.15 adopts the seam (runtime is synthetic-only under AD-28).

## Deferred from: code review of spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md (2026-10-11, C12 chunk 2)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Fairness tests check only the end state under generous loop caps (12–30 passes), so a regression from 2 passes to 20 still passes.
  status: done 2026-10-24
  resolution: Current SDK/Works consumer qualification pins later-source progress to two two-unit passes and three one-unit passes across reconstruction while a failed publication remains pending; no generous eventual-success loop. See [qualification spec](spec-five-deferred-process-recovery-policy.md) and [retained packet](evidence/five-deferred-process-recovery-policy-2026-10-24/manifest.json).
  evidence: Story 4.12 review C12 chunk 2 BH14 (2026-10-11), medium. Primary site `tests/Hexalith.EventStore.DomainService.Tests/Processes/ProcessPublicationTests.cs:269`; the same pattern appears across the Processes fairness and starvation tests. Deferral reason: pass-count bounds depend on the owner-deferred fairness redesign (cycle-12 B2, B8, E2, E7); pin bounds derived from `SourcesPerPass` in the process-runtime hardening story.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Pending-publication saturation is tested only at the store level; no reconciler test shows the catalog cursor held on the rejected key, Degraded readiness, and later acceptance.
  status: done 2026-10-24
  resolution: A 128-intent saturation control executes the SDK reconciler: the unaccepted key stays held with no new index, readiness stays Degraded, and reconstructed reconciliation admits it after a real pending publication repair frees space. Acceptance preserves unresolved evidence; this is not a full-queue throughput claim. See [qualification spec](spec-five-deferred-process-recovery-policy.md) and [retained packet](evidence/five-deferred-process-recovery-policy-2026-10-24/manifest.json).
  evidence: Story 4.12 review C12 chunk 2 AA9 (2026-10-11), medium. Primary site `tests/Hexalith.EventStore.DomainService.Tests/Processes/ProcessPublicationTests.cs:243`. Deferral reason: reconciler behavior under an unaccepted admission is the owner-deferred cycle-12 B2 design gap; write the test against the hardening story's policy.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Read-admission failure is not tested across a reconciler pass; the cycle-9 rule "must not publish Healthy or lose the source cursor" is proven only for the runner and the pool.
  status: done 2026-10-24
  resolution: Five executing consumer cases inject the actual SDK admission exception at tenant/candidate page, tail and source-state reads, retain the exact persisted cursor and one worker-pressure reason, stay Degraded, then recover after reconstruction and fresh coverage. Synthetic boundary injection qualifies handling, not pool capacity. See [qualification spec](spec-five-deferred-process-recovery-policy.md) and [retained packet](evidence/five-deferred-process-recovery-policy-2026-10-24/manifest.json).
  evidence: Story 4.12 review C12 chunk 2 AA16 (2026-10-11), medium. Primary site `tests/Hexalith.EventStore.DomainService.Tests/Processes/ProcessRunnerEdgeTests.cs:69`. Deferral reason: depends on the owner-deferred pool-sizing (B5/E5) and catalog-continuation (E6) decisions.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: The checked-in `python3 scripts/verify-process-packages.py` has no passing run at EventStore `1c919430`; it exited 130 twice locally, while the manual pack plus `validate-consumer-package-references.py` fallback passed all three consumers.
  evidence: Story 4.12 review C12 chunk 2 AA17 (2026-10-11), low. Primary site `scripts/verify-process-packages.py`. Deferral reason: environment-blocked locally; the next CI run of the process-packages job settles it.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Track the process-runtime hardening dependency explicitly before Story 4.15 consumes the checkpointed process seam.
  evidence: Story 4.12 test-patch review B2 (2026-10-12), medium. The spec defers several high-impact runtime findings to a hardening story before Story 4.15, but `sprint-status.yaml` has no hardening story or dependency. An explicit tracking gate would keep the handoff from proceeding on the synthetic-only implementation.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Verify persisted process sweep claims with host-supplied Dapr JSON serializer options.
  evidence: Story 4.12 follow-up review V1 (2026-10-12), medium. The Web-default JSON contract test pins the shipped default shape, but `DaprReadModelStore` uses the host's `DaprClient.JsonSerializerOptions` and host registration can preserve a supplied client. A custom naming policy could make earlier sweep claims unreadable while the default-options test passes. Settle the supported host serializer policy and prove restore through the configured Dapr client.

## Deferred from: code review of spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md (2026-10-12, production chunk)

These four items were reconfirmed on the current production code. They do not open a second action; the cycle-12 entries remain the record.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: A probe-capped scan with no tail still completes the cycle.
  status: done 2026-10-24
  resolution: The bounded probe completion is intentional retry policy: an executing no-tail linked-page-hole control proves Complete with unresolved evidence stays Degraded, and the next cycle after reconstruction processes restored page contents and becomes Healthy. Completion is not complete coverage; blocking the next cycle would prevent repair retry. See [qualification spec](spec-five-deferred-process-recovery-policy.md) and [retained packet](evidence/five-deferred-process-recovery-policy-2026-10-24/manifest.json).
  evidence: 2026-10-12 production review, medium. `ProcessReconciler.cs:279`. Deferral reason: refusing Complete would stop the next cycle from retrying the hole. The four-probe cap is already documented, and Healthy already requires Unresolved == 0.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Orphaned transition handoff after a failed source CAS.
  evidence: 2026-10-12 production re-review, high. `ProcessCheckpointStore.cs:86` still writes the sweep token before the source CAS. Already deferred in cycle 12.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: A catalog failure drops its continuation on the next cycle.
  evidence: 2026-10-12 production re-review, high. `ProcessReconciler.cs:240` still sets `CatalogComplete`, and cycle reset still clears `CatalogAfterKey`. Already deferred in cycle 12.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: One pending observation is drained per pass.
  evidence: 2026-10-12 production re-review, medium. `ProcessReconciler.cs:93` still finishes at most one observation per pass. Already deferred in cycle 12.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md`
  summary: Serialized event bytes and the default four-envelope request disagree with the guide's eight-envelope wording.
  evidence: 2026-10-12 production re-review, medium. `CheckpointedProcessRunner.cs:75` and `:277`. Already deferred in cycle 12.


- source_spec: `_bmad-output/implementation-artifacts/spec-4-14-adopt-sdk-projection-and-query-seams-in-works.md`
  summary: Publish the approved corrected upstream EventStore/Platform artifacts and update Works to publicly restorable pins before merge or release.
  evidence: B9 and carried R2-B7; clean CI does not generate or retrieve ignored local candidate identities. Local source/candidate validation is authorized; public publication is not. The accepted delivery gate remains unresolved.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-14-adopt-sdk-projection-and-query-seams-in-works.md`
  summary: Benchmark ordinary projection delivery near admitted history/document limits and establish whether full-family read amplification requires optimization (medium, unverified).
  evidence: R2-B9 traces sequential loading of member histories, parking, and proposed documents, but no latency-bearing boundary run or measured delivery failure establishes operational harm. A representative persisted-capacity fixture with measured store latency, delivery duration and throughput would settle the claim.

## Deferred from: code review of spec-4-14-adopt-sdk-projection-and-query-seams-in-works (2026-10-14)

- `FoldCatchUpAsync` failure classification: history/parking reads sit outside the `try` (raw exception, possible unbounded retry) and a foreign member's missing parking or a sequence gap parks the incoming healthy aggregate (medium, unverified). Settle by reading `DomainProjectionDispatcher` handling of non-`SharedProjectionFoldFailureException` throws and the intended parking attribution.
- `AddEventStoreReadModelStore` `MaxOperations = 4N+3` may be too small for multi-page histories (medium, unverified). Settle with a 128-aggregate multi-page rebuild fixture.
- `FoldCatchUpAsync` derives its scope from payload `TenantId` (high, unverified). Settle by confirming the SDK dispatcher binds the payload tenant to the journal scope.

## Deferred from: code review chunk 2 of spec-4-14-adopt-sdk-projection-and-query-seams-in-works (2026-10-15)

- The three chunk-1 entries above are settled (2026-10-15): failure classification is intended fail-closed; `MaxOperations` was patched to 3 + 4N + 256; the payload-tenant scope is bound by the SDK dispatcher. See the spec's Review Findings.
- Env-gated durable tests (`Dapr_Redis_restart_recovers_staged_family_and_concurrent_delivery_when_enabled`, `Complete_isolated_keyspace_restore_removes_sdk_epoch_then_legacy_replay_recovers_queue`) return silently when their Dapr/Redis endpoints are unset; `Assert.Skip` reports `FAIL_SKIP` under the fail-on-skip policy. Needs a lane/trait split or a CI step for `run-sdk-rollback-restore.sh` (medium). **Closed 2026-10-22 (Story 4.9 review round 2, decision (a)):** `run-sdk-rollback-restore.sh` and the restore test were removed by the Story 4.9 cutover; the six `WORKS_SDK_DAPR_GRPC_ENDPOINT` durable branches in `WorksSdkProjectionQueryTests` are retired as superseded by the Platform live recovery, projection and historical restart lanes, which run against the Platform-composed Dapr Redis store. The durable-only fact is deleted, the five deterministic in-memory bodies remain, and each retired branch maps to its named replacement in `evidence/story-4-9/verification/retired-test-replacements.json`.
- AppHost drops the configured `works-client-username/password` in favour of generated `security-eventstore-client-*` parameters; the topology test lost its value assertion. Confirm intent (medium, unverified).
- Denial-before-state-access tests do not observe coordinator reads and lack a positive control (medium, unverified).
- Hard-coded `Hexalith.Platform.Identity` catalog exemption and candidate version pins under `#if !DEBUG` (low).

## Deferred from: code review of spec-4-14-adopt-sdk-projection-and-query-seams-in-works (2026-10-15, pass 3)

- `MaxOperations` (40,259) is only checked by a test that restates the formula; add a plan-count test over a few aggregates with forced multi-page histories asserting count ≤ `WorksRebuildOperationBound.MaxOperations` (medium).
  status: done 2026-10-23
  resolution: [spec-five-deferred-rebuild-admission.md](spec-five-deferred-rebuild-admission.md) — Actual SDK plans with three multi-page owners and the 10,000-owner ceiling use 21 and 40,035 operations respectively, within the registered 40,259 limit; unique plan keys and exact history-page counts are asserted. The 10,000-owner case combines 10,032 history pages within the 16 MiB tenant budget; its independent queue-document staging limit is recorded below.
- Unverified (medium if true): the 40,259-operation ceiling may exceed `MaxCanonicalManifestBytes` (8 × default) first; settle by sizing the canonical manifest of a maximal epoch plan.
  status: done 2026-10-23
  resolution: [spec-five-deferred-rebuild-admission.md](spec-five-deferred-rebuild-admission.md) — A full 16 MiB tenant history produces a logical canonical manifest exceeding 8 MiB, but the real SDK stages it through bounded batches and commits every byte. The unchanged production limits are applied to the actual store, measured on every executed batch, and histories are compared after coordinator reconstruction. This qualifies the tenant-byte ceiling and the separately tested operation-count ceiling, not every possible projection shape.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Benchmark Works recovery catalog enumeration at supported large candidate populations (medium, unverified).
  evidence: Review BH6 found full membership reread/sort per keyset page, but no measured allowed population exceeds the SDK deadline; run a representative multi-tenant capacity benchmark with configured freshness and discovery limits to settle the claim.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Pre-existing SDK ProcessReadDeadline can exceed its deadline while synchronously invoking a provider cancellation callback (medium).
  evidence: Iteration 5 BH5-6; the timeout catch still calls work.Cancel synchronously, exactly as in SDK baseline 6394d0500e6e277b8975cb397c9f0389f9a16929. Add a deterministic callback-blocking race regression, then make cancellation nonblocking while retaining source/slot lifetime through actual completion.
- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Pre-existing shared control-index read validation accepts persisted registries exceeding writer tenant, receipt or byte bounds (medium).
  evidence: Iteration 5 BH5-7; unchanged SharedProjectionControlIndexWriter.ValidateState in SDK baseline 6394d0500e6e277b8975cb397c9f0389f9a16929 omits the writer's 64-tenant, 256-receipt and 32-KiB ceilings. Verify over-limit corruption is rejected by strict and partial readers before tenant acquisition, preserving valid historical indexes and offboarding.

## Deferred from: code review of spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works (2026-10-16, chunk A)

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: ReminderConvergenceResult is discarded by WorksDomainEventProcessor before returning Processed (medium, unverified).
  status: done 2026-10-23
  resolution: Superseded by the Story 4.9 SDK cutover. [spec-five-deferred-reminder-delivery.md](spec-five-deferred-reminder-delivery.md) verifies that retained unresolved work permits acknowledgement after durable discovery, convergence exceptions release the delivery marker, and duplicate delivery does not repeat convergence; SDK scheduling-failure recovery is verified separately. [Verification evidence](evidence/five-deferred-reminder-delivery-2026-10-23/manifest.json).
  evidence: Settle by reading SDK IReminderRegistrar.ConvergeAsync/ReminderConvergenceResult: if it can report incomplete without throwing, return RetryableInProgress.
- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: ValidateWitnessAddress compares envelope.TenantId ordinally while IsValidFor lowercases the tenant (medium, unverified).
  status: done 2026-10-23
  resolution: `spec-five-deferred-ingress-authority.md` proves SDK envelopes retain raw tenant casing while canonical stream identity lowercases it; witnessed create/reschedule reflection dispatch now accepts canonical-equivalent tenants. Foreign tenants, domains and case-sensitive aggregates still refuse. Protected delegated origin binding continues to require its exact authenticated tuple. [Verification evidence](evidence/five-deferred-ingress-authority-2026-10-23/manifest.json).
  evidence: Settle by checking whether the gateway already normalizes envelope tenant case.
- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: WorksReminderIntentSource validates only the final expiry witness (medium, unverified). RESOLVED in chunk B review 2026-10-16: WorksSourceStreamReader applies WorksExpiryEvidence to every committed envelope, so the claim holds.
  evidence: Settle by confirming WorksSourceStreamReader (chunk B) validates every encountered witness before later history replaces it.

## Deferred from: code review of spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works (2026-10-16)

- `PackageVersion Remove` for Hexalith.EventStore.Contracts is gated on UseHexalithProjectReferences only (Directory.Packages.props:16-21); medium if true, unverified. Settle by a Debug restore with HexalithEventStoreFromSource=false and checking for NU1010.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: "Supply published corrected EventStore producers or an approved exact-artifact CI feed before Story 4.16 public-producer acceptance."
  evidence: "RB6-6: the baseline already selected unpublished Story 4.14 candidates; the latest public EventStore 3.122.0 lacks IReminderTargetCatalog. Local .10 package/source/live proofs pass, but ignored local feeds are absent on clean CI runners. Publication remains a separate frozen-intent gate."

## Resolution evidence: Story 4.15 publication (2026-10-16)

The review-time RB6-6 unpublished EventStore restore prerequisite is superseded by verified public `3.123.0` for all nine SDK packages. Historical deferred entries and their evidence remain unchanged. [Final producer, exact restore and acceptance proof](evidence/story-4-15/public-3.123.0-final-2026-10-16/implementation.md) does not accept Story 4.16 or production admission.

## Deferred from: code review of spec-4-15 (2026-10-17, src/ chunk)

- Trusted-origin identity (target/causation/digest) not compared to envelope in `WorkItemEventStoreAggregate.Handle(ExpireWorkItem)`; unverified medium — confirm SDK boundary enforces it.
- Legacy-to-SDK runtime handoff (outstanding Dapr date reminders, cascade checkpoints) when `UseSdkRuntime` flips; unverified medium — settle in Story 4.16 rollback proof.
- `MapActorsHandlers()` skipped under SDK mode; unverified medium — confirm SDK maps actor endpoints.
- `WorksProcessSourceCatalog` `sources.Add` duplicate-key `ArgumentException` outside per-tenant try; unverified medium — check control index duplicate/case-variant tenants.

## Deferred from: code review of spec-4-15 (2026-10-17, src/scripts chunk 8679d7d..e7f67cd)

- `WorksSourceStreamReader` raw-field budget pre-check omits `ProtectionMetadata`; unverified medium — confirm upstream envelope bound.
- Partitioned rebuild accepts ordinal gaps in the partition inventory; unverified medium — confirm SDK contiguity guarantee.
  status: done 2026-10-23
  resolution: [spec-five-deferred-rebuild-admission.md](spec-five-deferred-rebuild-admission.md) — SDK dispatch validates all authoritative ordinals and omits partitions for erased owners. An executing erased ordinal zero/live ordinal one rebuild commits only the surviving authoritative history; requiring contiguous Works partition ordinals would reject this valid SDK contract.
- Oversized single-aggregate history not rejected before finalize (`MaxItemBytes`); unverified low-medium.
  status: done 2026-10-23
  resolution: [spec-five-deferred-rebuild-admission.md](spec-five-deferred-rebuild-admission.md) — Both partition preparation APIs already call the bounded history serializer. Executing tests admit exactly 4 MiB as 64 complete pages and refuse one byte beyond that limit before finalization; SDK accumulation also preserves the accepted inventory, creates no owner scratch keys and retains the previous readable generation on refusal.
- `WorksDomainEventProcessor` ignores `ProcessSource.Validate` ArgumentException (possible endless redelivery); unverified medium.
- `Directory.Packages.props` Contracts PackageVersion removal lacks `HexalithEventStoreFromSource` condition (possible NU1010); unverified medium — restore in mixed mode.
- `Directory.Solution.targets` ignores `Targets`/`BuildProjectInSolution` metadata and may double-build; unverified low-medium — binlog.

## Deferred from: code review of spec-4-15 (2026-10-17, src-only pass)

- `FinalizePartitionsAsync` accepts an empty partition list; unverified medium — confirm the SDK never finalizes an empty inventory.
  status: done 2026-10-23
  resolution: [spec-five-deferred-rebuild-admission.md](spec-five-deferred-rebuild-admission.md) — The shared manifest builder already refuses an empty candidate. Direct partition finalization and the real SDK lifecycle are tested; a cold coordinator observes the same committed generation, roll-up bytes and retained history after refusal.

## Deferred from: code review of spec-4-15 (2026-10-17, tests/ integration pass)

- Source-built `eventstore` stamped `Version=3.124.0` unconditionally in the SDK smoke fixture and bound-metadata startup (`WorksSdkRecoveryPipelineSmokeTests.cs:660`, `WorksAppHostSmokeHarness.cs:254`); unverified medium — confirm whether DCP builds project resources with these environment values as MSBuild properties and whether the stamp reaches evidence. Update 2026-10-18 (post-reopen delta review): the fixture now stamps `WorksSdkProofProducer.Version` (`WorksSdkRecoveryPipelineSmokeTests.cs:678,685,729`, `WorksAppHostSmokeHarness.cs:254`), which resolves to public 3.124.0 in source Debug while the source-built hosts carry the unpublished correction and three post-3.124.0 SDK commits; the retained `review-fixes-2026-10-18/raw/candidate-startup-mismatch-fail--*` run confirms these environment values reach child builds.
- Commit 99976bb's `QueryWindowStore.Observe` starts at the same point the old `ClearReceivedCalls` ran (`WorksSdkProtectedHttpTests.cs:387`), so post-start hosted-service reads would still fail the denial check; unverified medium — identify the post-start store reader or loop the test under CPU load.
- Platform host-evidence wording, for Story 4.16 (`WorksAppHostSmokeHarness.cs:352`, `references/Hexalith.Platform/eng/verify_works_host.py:75,88`): the verifier's `readinessPassed` check can never fail because the harness writes a start record only after every startup probe passes, and its "loaded SDK assemblies" error refers to the top-level list, which holds the test runner's assemblies (hosts are bound per module in `runtimeModules`). Not a false pass; Works-side comments landed in the 4.15 review. Reason deferred: changing the record shape mid-4.16 would invalidate the 52 captured start files, so rename (`worksReadiness`, `runnerSdkAssemblies`) when 4.16 next re-captures its live facts.

## Deferred from: code review of spec-4-15 (2026-10-17, resumed independent review)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Bound synchronous and cancellation-ignoring Parties query-authority acquisition with shared admission and lifetime handling.
  status: done 2026-10-23
  resolution: `spec-five-deferred-ingress-authority.md` reuses SDK AuthoritativeStreamReadDeadline.ReadWithAdmissionAsync for the existing two-second identity budget. Synchronous stalls are isolated; caller cancellation and timeout deny before any actual coordinator/store read; task admission survives abandonment until provider completion. The separate pre-existing SDK cancellation-callback worker lifetime limitation is explicitly tracked below. [Verification evidence](evidence/five-deferred-ingress-authority-2026-10-23/manifest.json).
  evidence: BR7-2: baseline 8679d7d700925a870e9931b3c43c745c063e459b directly awaits ResolvePartyIdentityAsync without an invocation/admission bound; current WorksQueryPolicy.cs:52-59 can block synchronously or retain cancellation-ignoring requests. This R5 issue predates the R6–R8/R11 adoption; use a shared SDK/provider seam and regressions for sync blocking, refused admission and completion-based release.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Isolate query deadline completion from provider cancellation callbacks.
  status: done 2026-10-23
  resolution: `spec-five-deferred-ingress-authority.md` separates query waiting from provider cancellation through the existing SDK deadline. Blocking callbacks installed after the query waiter cannot hold timeout or caller-cancellation completion; all four strengthened controls fail against the original policy and pass with the change. Callback-worker retention is a separate SDK resource-lifetime follow-up below. [Verification evidence](evidence/five-deferred-ingress-authority-2026-10-23/manifest.json).
  evidence: ER7-1: /tmp/story415-edge-probe reproduced a callback entering at two seconds and holding AuthorizeAsync pending at three seconds until released. The baseline query path is already provider-bound; the later R5 WaitAsync improvement uses the provider token as its deadline token. Verify distinct deadline/provider lifetimes and nonblocking cancellation at the shared seam.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Preserve subsequent solution targets when serializing solution project builds.
  status: done 2026-10-25
  resolution: The destructive Directory.Solution.targets was already removed. The executing two-project Build,Pack regression uses committed solution policy and requires each Build and Pack target exactly once. See [qualification spec](spec-five-deferred-release-authority.md) and [retained packet](evidence/five-deferred-release-authority-2026-10-25/manifest.json).
  evidence: BR7-3: independent CI commit f9ddce953f37b1e11db9cdc3d88ba2c1ca62d2d5 removes ProjectReference items in Directory.Solution.targets. Root package-free .csproj probe /tmp/story415-root-msbuild-5e_gzxzq, dotnet msbuild x.slnx -nologo -t:Build,Pack, exits 0 with BUILD_A and no PACK_A. Repair reusable build orchestration with a multi-target regression; initial .proj parse failure is separate.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Remove unused issue and pull-request write grants from release publication authority after governance alignment.
  status: done 2026-10-28
  resolution: `spec-five-deferred-contract-and-proof-integrity.md` removes both unused grants from the inlined caller-owned job, preserving contents/OIDC/actions authority. Executed locked GitHub success/failure hooks use committed configuration and a client refusing issue/PR API access; re-enabled-hook controls hit that boundary. The tooling CI job installs locked npm dependencies for this regression. No publication was dispatched.
  evidence: BR7-8: release.yml grants issues: write and pull-requests: write while .releaserc.json disables success/failure comments and labels; installed @semantic-release/github skips those writes. This is independent publication-authority work, which the frozen adoption intent leaves as a separate gate.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Prevent ordinary package restores from confusing local candidate archives with published producers.
  status: done 2026-10-25
  resolution: Historical candidate feeds now require WorksAllowLocalEventStoreCandidate=true. Fresh- and warm-cache offline controls refuse candidates from each feed by default, admit them only with explicit opt-in and a separate default cache, and verify exact archive hashes/source. Ordinary builds also refuse residual candidate-cache assets; explicit caller sources/caches remain caller-owned. See [qualification spec](spec-five-deferred-release-authority.md) and [retained packet](evidence/five-deferred-release-authority-2026-10-25/manifest.json).
  evidence: BR7-9: baseline Directory.Build.props already adds the local Story 4.14 feed whenever present, and current props also adds the 4.15 feed. A matching ID/version can satisfy restore from those sources. Current packers refuse stable identities and isolated public verification checks official hashes; general restore source mapping/identity provenance belongs to shared build governance.

## Deferred from: code review of spec-4-15 (2026-10-17, resumed closeout review)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Record legacy pending-date discovery before scheduling so failed scheduler calls retain a recoverable target.
  status: done 2026-10-23
  resolution: Superseded by removal of the legacy scheduler. [spec-five-deferred-reminder-delivery.md](spec-five-deferred-reminder-delivery.md) proves current Works discovery precedes convergence, and the SDK RegistrationPersistsWitnessAndIndexBeforeArming and PendingStateSurvivesSchedulerFailure tests verify durable witness/index ordering and recovery after scheduler outage. [Verification evidence](evidence/five-deferred-reminder-delivery-2026-10-23/manifest.json).
  evidence: The legacy suspended handler writes the fallback index after scheduling all reminders; a failed scheduler call with missing projection delivery leaves no durable discovery entry. The index write was introduced by in-story commit 07e23a6 (2026-10-17), after baseline 8679d7d; before that commit the handler wrote no fallback index, so this scheduling-failure window predates the new write.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Clear stale legacy pending-date discovery entries with a sequence-fenced empty fold.
  status: done 2026-10-23
  resolution: Superseded by the SDK generation-selected pending-date fold. [spec-five-deferred-reminder-delivery.md](spec-five-deferred-reminder-delivery.md) verifies persisted empty membership and sequence 3 after resume/cancel, restart and stale suspension redelivery, preserving checkpoint 3; SDK RemovedIntentReleasesStateThenIndex verifies reminder discovery cleanup. [Verification evidence](evidence/five-deferred-reminder-delivery-2026-10-23/manifest.json).
  evidence: PendingDateAwaitDiscoveryIndex.RecordAsync returns when the pending set is empty, so old entries continue to be scanned and can grow the tenant document. PendingDateAwaitDiscoveryIndex was introduced by in-story commit 07e23a6 (2026-10-17), after baseline 8679d7d. The same stale-entry claim was rejected as low in the 2026-10-17 src/scripts review because the legacy consumer rereads authoritative streams; preserve that earlier verdict when reassessing this follow-up.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Verify that release preparation and exact-source CI use compatible Builds action behavior.
  status: done 2026-10-25
  resolution: Exact committed CI and release action-chain comparison proves identical initialization and compatible SDK selection, restore and Release compilation. Fourteen executing controls cover scoped caller inputs, step activation/context/order, complete SDK configuration, restore sources, build outputs/options and approved checkout/dependency identity. See [qualification spec](spec-five-deferred-release-authority.md) and [retained packet](evidence/five-deferred-release-authority-2026-10-25/manifest.json).
  evidence: CI invokes Builds workflow 04d9617 while release preparation invokes action 397c94a. Compare their implementations or run dry release preparation to determine whether the version difference changes accepted output; medium impact remains unverified.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Mark Platform-only integration facts skipped when the Platform artifact is not selected.
  status: done 2026-10-23
  resolution: `spec-five-deferred-verification-reliability.md` verifies the existing T17 repair by explicitly running Historical, Projection and HA without Platform selection: exactly three FAIL_SKIP results, zero passes, expected exit 1 under failSkips=true. This is a negative control, not a successful parity run.
  evidence: WorksPlatformHaPipelineSmokeTests and WorksPlatformProjectionPipelineSmokeTests return without assertions outside the Platform lane, so ordinary runs count their unexecuted behavior as passes. Platform parity is the separate Story 4.16 gate.

## Deferred from: code review of spec-4-15 (2026-10-18, post-reopen delta)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Publish the create-only receipt-conflict correction and requalify the default EventStore pin.
  evidence: `Directory.Packages.props:6` keeps public 3.124.0 by default, and `review-fixes-2026-10-18/implementation.md` records that public 3.124.0 leaves the shared-projection date head at journal position 11 with six pending entries. Story 4.15 acceptance uses source plus unpublished `3.124.1-story415.20261018.1`. The correction is SDK `216b7906` on `origin/main` (rebased from verified `b9e2d2aa`). Publication and pin qualification are separate frozen-intent gates. Story 4.16 currently verifies Platform parity on 3.124.0 without this defect as an input; requalify it against the first published release that contains the correction.


## Deferred from: code review of spec-4-15 (2026-10-18, post-reopen build)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Story 4.16 fleet-restore verification must account for captured Redis hash fields expiring before restore or verification.
  evidence: P18-E2; WorksSdkProofRedis.RedisValueFingerprintAsync records exact hash members and HPEXPIRETIME deadlines, but VerifyRestoredRedisAsync skips only expired whole keys. A field legitimately expiring after capture changes the expected logical fingerprint and makes faithful restore fail. Only the explicit Platform branch and WorksPlatformFleetRestoreTests call this helper. Preserve still-live field bytes/deadlines and corruption detection while testing field expiration across capture/restore; Platform parity is a separate frozen-intent gate.

## Deferred from: code review of spec-4-15 (2026-10-18, post-review-patch delta)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md`
  summary: Requalify Works live source Debug restart and callback on the final recovery decoder and the current upstream SDK source.
  evidence: The last restart/callback runs bind SDK `216b7906` and the pre-patch `WorksRecoveryEventDecoder`; the decoder then changed three times (post-reopen patches, P18-B1, uniform payload `Sequence` presence), the contextual reader added two lifecycle refusals (RP-B5 repeated creation, closeout-review reschedule-before-creation) and the SDK gitlink moved to `1aaa1737` (28 `src` files, including `AggregateActor`, `EventPersister`, `EventPublisher`, `DaprLogicalEventReader`) and then `f43b121c` on `84bb115a` (five upstream Server commits). `post-reopen-review/report.json` records `currentSourceLiveRerun: false`. The user accepted deterministic-only closure: decoder tightening cannot reject real payloads (no default-value omission), and the lifecycle refusals cannot reject real histories because `WorkItemAggregate` never emits a second creation or a pre-creation reschedule; live requalification on the current upstream SDK is a Story 4.16 input under the approved scope rule. Settle with source Debug restart and callback runs at the gitlink Story 4.16 qualifies.

## Resolution evidence: Story 4.16 public 3.125.1 qualification (2026-10-18)

Historical entries above stay unchanged. The [qualification packet](evidence/story-4-16/public-3.125.1-2026-10-18/README.md) binds the following resolutions. It does not accept Story 4.16; independent review remains.

- Published corrected producers: public EventStore 3.125.1 (tag `v3.125.1`, producer `55432c50`) contains the Story 4.15 journal-closeout correction (`216b7906`) and both Story 4.16 producer corrections (`9dbb3aaf`). The Works default pin selects it. The canonical Platform verifier exits 0 against its nine official archives, with runtime source qualified against the official commit, trees and tag.
- Live source Debug restart and callback requalification on the final decoder: both facts pass on the retained Works host at exact `v3.125.1` source (`source-debug-lane/lane3`). The Works SDK gitlink remains `v3.125.0`; its runtime source differs from `v3.125.1` only in two Aspire hosting files.
- Platform host-evidence wording: verifier errors now name the top-level `sdkAssemblies` list as the test runner's assemblies. The record field names stay unchanged so earlier start files remain comparable.

## Deferred from: Story 4.16 public 3.125.1 qualification (2026-10-18)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Isolate Dapr name resolution for direct developer runs of the Platform Works preview.
  evidence: Canonical attempt 2 (`evidence/story-4-16/public-3.125.1-2026-10-18/attempt2-mdns-cross-talk/diagnosis.json`) showed default mDNS discovery round-robining the Works sidecar onto an unrelated session's non-mTLS `eventstore` sidecar. The live fixture now uses a run-owned SQLite registry. `WorksPreviewModel` still points sidecars at committed configurations without `nameResolution`, unlike the Builds run renderer and Platform `MergeRunDiscovery`. Development/test preview only; production uses platform-managed discovery.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Align the Works EventStore gitlink with the qualified public producer when Git work is authorized.
  evidence: Works Debug defaults to the `v3.125.0` gitlink (`f43b121c`), while qualification used an exact isolated `v3.125.1` export. The two differ only in `EventStorePlatformProjectMetadata.cs` and `RepositoryProjectPaths.cs`. No Git or submodule work was authorized for this implementation.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Diagnose intermittent control-plane readiness failures of live restarts on the shared host.
  evidence: Source Debug lanes 1 and 2 failed before any fixture assertion: EventStore placement was disconnected and the sidecar unresponsive on a restart, then the placement health probe stayed unhealthy. Diagnostic run 7 showed the same placement symptom. Lane 3 and the canonical run passed with healthy probes. Concurrent sessions held load averages of 6–17 and contended for the fixed control-plane ports.

## Deferred from: Story 4.16 review iteration 1 (2026-10-18)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Fix EventStore so a rate-limited (HTTP 429) internal projection update is retried or surfaced, not silently dropped.
  evidence: Triage T3. `src/Hexalith.Works.AppHost/Program.cs:94-97` records that EventStore's default 100-per-second limiter rejects the host's internal actor/projection traffic with 429, and the swallowed projection update leaves the pending-date index empty. The retained host raises the limits to 100000; the Story 4.16 parity fixture injected 8192 on Platform starts. On 2026-10-18 the owner chose to compose explicit dev/test limits in the Platform preview and to track the drop as an EventStore producer defect. Fixing it needs a producer change and a release published by the owner.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Bind the Works production profile's `brokerPolicySha256` and `networkPolicySha256` to committed policy files at the AD-28 production gate.
  evidence: Triage T25. `WorksProductionProfile` only checks that both values are hex. No broker or network policy file exists for them to match. Production admission stays closed. On 2026-10-18 the owner deferred the binding to the AD-28 gate, which must supply the committed policy files the hashes are compared against.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Restore an unconditional default EventStore pin, or refuse a Release build whose effective `HexalithEventStoreVersion` differs from `WorksPinnedEventStoreVersion` without an explicit opt-in.
  status: done 2026-10-25
  resolution: Release restore/compile compares the effective SDK against the literal committed Directory.Packages.props pin and rejects ambient or command-line drift with HXW0003; SDK assets inspection also refuses stale mismatched package graphs in builds without restore. Candidate qualification requires explicit true; source Debug and the default public pin retain their behavior. See [qualification spec](spec-five-deferred-release-authority.md) and [retained packet](evidence/five-deferred-release-authority-2026-10-25/manifest.json).
  evidence: Triage T15. The pin at `Directory.Packages.props:6` was unconditional at the Story 4.16 baseline (`3.123.0`). Story 4.15 `fbcacfb` made it conditional, as `Directory.Build.props:42` already was. An ambient `HexalithEventStoreVersion` environment variable now silently selects another SDK for Release restores, while global `-p:` overrides would have worked either way.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Limit Dapr resiliency retries and circuit breaking on the `works` and `eventstore` app targets to transport failures before production admission.
  evidence: Triage T22. Platform `DaprComponents/resiliency/resiliency.yaml` (since `b9410d3`, 2026-09-27) gives the `works` target `defaultRetry` and `defaultBreaker` (`consecutiveFailures > 3`, 30 s). The Story 4.16 operations and projection checks wait up to 60 s / 45 s through 500 "circuit breaker open" answers after their own 403/400 negative cases, and the test comments say a denial "consumed its retry policy". So four refused operator or projection calls can block legitimate ingress for 30 s, and refused POSTs are re-sent.

## Resolution evidence: Story 4.16 review iteration 1 corrections (2026-10-18)

Historical entries above stay unchanged. The [corrections packet](evidence/story-4-16/review1-corrections-2026-10-18/README.md) binds these resolutions. It does not accept Story 4.16; independent review remains.

- Live parity no longer replaces Platform's `DOTNET_STARTUP_HOOKS` or `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` (triage T1). Every Platform `works` and `works-ha` start loaded `WorksRecoveryHostingStartup` before the proof and registered telemetry once, and the verifier now refuses a start that does not.
- Parity runs at Platform-composed development/test EventStore rate limits, and the fixture injects none (T3). The EventStore 429 drop entry above stays open.
- The verifier derives each row's four proofs from named phases and qualifying fields, and refuses duplicate phases (T5). R9 gained a real negative phase, `platform-sdk-named-mode-legacy-route-refused`.
- The R10 trust probe now records server-side refusals (T7): `tlsv13 alert certificate required` for a client without a certificate, `bad certificate` for a foreign-CA client, and probe rejection of a mismatched SPIFFE identity.
- The 4.15 "Platform host-evidence wording" deferral is resolved for `readinessPassed` (T6), which can now fail: a Degraded-expected start records `false` with `worksReadinessExpectation: Degraded`, and the verifier accepts `false` only with that expectation on a Platform start. The suggested renames (`worksReadiness`, `runnerSdkAssemblies`) were not made, so earlier start files keep their field names.
- The 4.15 deferral "Mark Platform-only integration facts skipped" is resolved (T17). `WorksPlatformHistoricalPipelineSmokeTests`, `WorksPlatformHaPipelineSmokeTests` and `WorksPlatformProjectionPipelineSmokeTests` call `Assert.Skip` outside the Platform lane. Under the repository's `failSkips: true` policy, an unfiltered integration run therefore reports them as failed skips rather than passes. Deterministic lanes exclude `*SmokeTests` and `*Platform*`.

## Deferred from: Story 4.16 review iteration 2 (2026-10-19)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: `WorksSdkProtectedHttpTests.QueryWindowStore` counts only `IReadModelStore` reads, so a query path that reads Works state through an SDK batch or bulk read-model store would leave its non-disclosure checks at zero operations (triage T109, unverified medium).
  status: done 2026-10-23
  resolution: `spec-five-deferred-verification-reliability.md` verifies the existing three-interface wrapper and DI replacements with an executing regression: each single/bulk/batch operation increments the observed request counter, while background and disabled windows do not. Existing authorized/denied HTTP cases also pass.
  evidence: Works `src` resolves no `IReadModelBatchStore` or `IReadModelBulkStore`. To settle it, trace which store interfaces the 3.125.1 SDK query and authorization path resolves during the trusted-origin matrix. If any of them reads Works state, register the window wrapper for those interfaces too, and confirm the operation count moves.

## Resolution evidence: Story 4.16 review iteration 2 corrections (2026-10-19)

Historical entries above stay unchanged. The [corrections packet](evidence/story-4-16/review2-corrections-2026-10-19/README.md) binds these resolutions. It does not accept Story 4.16; independent review remains.

- Live parity no longer grants `Works:Query:PermittedCoordinatorWorkloads` (triage T78). The R5 restart query and the R1/R4/R5 rollback, rebuild and quarantine queries are delegated Executor queries through the `eventstore` gateway workload, resolved by the test-only Parties identity setup on Platform and retained-host starts. Each phase records `queryIdentity`, and a non-delegated full-tenant query is recorded as `Forbidden` on a Platform start.
- On `platform-target` starts the fixture sets no key Platform composes for that resource (T84). A pre-build composition guard refuses any other overlap; the only permitted overlaps are the hook-list extension, the captured restorable app-channel secret and the telemetry lanes' OTLP redirect to an owned loopback collector. The HA replica now mirrors Platform's `DOTNET_ENVIRONMENT` too.
- The held review iteration 2 patches T58, T63, T65, T70, T77, T79–T83, T85, T91, T95, T98 and T100–T102 are applied. Path-mapped deterministic builds make the consumer's `Hexalith.Platform.Works.Runtime.dll` byte-identical to Platform's host output (T77).
- The 2026-10-18 entry "Story 4.16 fleet-restore verification must account for captured Redis hash fields expiring before restore or verification" keeps its scope. The restore phase now re-observes the stopped restored fleet itself (T83) and omits only the capture-only DUMP inventory.

## Deferred from: Story 4.16 review iteration 2 corrections (2026-10-19)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Public EventStore 3.126.0 is available on nuget.org, while this story still qualifies the 3.125.1 producer its spec keeps.
  evidence: The NuGet registration lists 3.126.0 for Server, Contracts, Client, DomainService, Aspire and ServiceDefaults (Server published 2026-10-18T21:00:06Z). Platform's Builds catalog (`references/Hexalith.Builds`, 4aaeed8) and both Works props still select 3.125.1, and the spec's iteration 1 and 2 KEEP lists retain the 3.125.1 qualification. Selecting 3.126.0 needs an owner decision, a Builds submodule update and a fresh producer qualification. That work is outside this iteration's no-submodule scope.
- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: The executor-visible twelfth recovery source needs a retuned synthetic process profile: 1.9-second operations and a 56-second source quantum.
  evidence: The Executor view shows only its own Assigned and the Queued work, and the eleven existing recovery sources all end terminal or `InProgress`, so delegated restart and rollback queries need one Assigned source. Under the previous profile (two-second operations, 60-second quantum) SDK validation cannot fit twelve sources in the 30-minute window. A five-effect envelope also needs a quantum of at least 29 operation timeouts. The retune is fixture-only and recorded in `docs/sdk-reminder-process-adoption.md`. If review prefers the original profile, the alternative is a separate executor-only fleet or a narrower rollback query proof.

## Resolution evidence: Story 4.16 review iteration 3 corrections (2026-10-19)

Historical entries above stay unchanged. The [corrections packet](evidence/story-4-16/review3-corrections-2026-10-19/README.md) binds these resolutions. It does not accept Story 4.16; independent review remains.

- The Platform EventStore receiver now names exactly two callers (triage T131): `works` on its exact routes, and `eventstore-admin` with the selected producer's production-template grant (`/**`, GET/POST/PUT, `deploy/dapr/accesscontrol.yaml:34-46` at `v3.125.1`). Admin.Server's documented Dapr invocations of `eventstore` are no longer refused on Platform, so the two hosts no longer diverge there. EventStore authorization still protects the command surface, and `WorksDaprPolicyTests` pins both callers and their operations.
- The held review iteration 3 patches T123, T126, T127, T129, T130, T132, T133, T155 and T157 are applied. The recovery alert raise must now follow the phase's observed Degraded state (T132), and the composition guard also refuses a fixture callback that removes a Platform key or rewrites it only where Platform composed it (T133).
- The carried defers T112 (resiliency circuit breaker, logged as T22) and T148 (query-window batch reads, logged as T109) keep their existing entries above; no new defer was appended in iteration 3.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Validate positive domain event Sequence values during Works recovery decoding without conflating them with EventStore envelope sequence numbers.
  status: done 2026-10-22
  resolution: `spec-five-deferred-runtime-safeguards.md` requires positive recovery ordinals while preserving historical decoding, unsequenced rejections, and separate envelope positions. Catalog, paged-reader, and persisted process quarantine/checkpoint regressions pass.
  evidence: Review T207; WorksRecoveryEventDecoder checks field presence only and WorksSourceStreamReader checks envelope ordering separately. The decoder last changed in accepted Story 4.15 commit d8f254e865477fadb694ad3b8f843ccf3ab86427; add explicit zero/negative and domain progression regressions before deciding the compatible validation rule.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Verify suspension callback discovery-write failure and successful redelivery using the real handler through WorksDomainEventProcessor.
  status: done 2026-10-23
  resolution: Superseded legacy processor replaced by the real `EventStoreDomainEventProcessor` and `WorksRecoveryWakeupHandler<WorkItemSuspended>`. [spec-five-deferred-reminder-delivery.md](spec-five-deferred-reminder-delivery.md) injects discovery-store and registrar failures, proves released markers, reconstructs the processor, verifies persisted discovery and completion on redelivery, then duplicate suppression. [Verification evidence](evidence/five-deferred-reminder-delivery-2026-10-23/manifest.json).
  evidence: Review T212; the accepted Story 4.15 discovery write (07e23a6fc54fca9644e844de5d1dff6f49b58b22) has healthy persistence coverage, while its processor retry test substitutes another handler. Inject registry/tenant-index failure after reminder scheduling, require retryable delivery, then restore the store and assert persisted discovery before completed deduplication.

## Deferred from: code review of spec-4-16-prove-platform-works-parity-and-rollback.md (2026-10-20)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Extend the Platform verifier's Works fixture inventory beyond IntegrationTests C# sources.
  status: done 2026-10-23
  resolution: `spec-five-deferred-verifier-safeguards.md` binds complete IntegrationTests/Testing inputs, copied resources and runner configuration before execution, with mutation/addition/removal regressions.
  evidence: Review T220; `fixture_sources` (`P/eng/verify_works_host.py:149-154`) hashes only `tests/Hexalith.Works.IntegrationTests/**/*.cs` plus `ProjectMetadataPaths.cs`. The IntegrationTests project file, `tests/Hexalith.Works.Testing`, `xunit.runner.json` and copied golden JSON are unbound. This scope predates the iteration 5 corrections; compiled project-file effects are bound through the consumer binary hash.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Bind the runtime SDK export's linked Builds catalog in the verifier ledger.
  status: done 2026-10-23
  resolution: `spec-five-deferred-verifier-safeguards.md` binds the selected SDK Builds location, resolved target and complete Props inventory separately from official producer sources; qualification and final closure reject drift.
  evidence: Review T221; `runtime_source_snapshot` (`P/eng/verify_works_producer.py:32-41`) records only `src/**` and three root build files, while the export's `references/Hexalith.Builds` link selects the SDK dependency graph. The 3.126.0 run's `b435f2e3` catalog is bound only by the packet's `builds-source-provenance.json`. Record the resolved catalog commit or Props hashes and re-check them at the end of the run.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Give the verifier's end-of-run "changed during execution" checks an executing regression.
  status: done 2026-10-23
  resolution: `spec-five-deferred-verifier-safeguards.md` extracts the final closure checks and executes unchanged and independent source, artifact, verifier, fixture, retained-host, dependency, manifest and port-drift controls.
  evidence: Review T222 (pre-verified gap); no test reaches `P/eng/verify_works_host.py:1022-1046`, so removing or weakening any final source, artifact, verifier or fixture re-check leaves the evidence-gate tests green. Extract the end-of-pipeline checks into a testable function.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Add cross-tenant registry preservation and ETag-conflict coverage for the accepted Story 4.15 suspend-subscription discovery writer (review T235).
  status: done 2026-10-23
  resolution: Superseded writer replaced by SDK process/reminder discovery. [spec-five-deferred-reminder-delivery.md](spec-five-deferred-reminder-delivery.md) executes real suspension handlers with a competing registry CAS and verifies three tenants and their exact persisted candidates after reconstruction; SDK IndependentRegistrationsRetryDiscoveryCas covers both same-tenant and cross-tenant reminder registration. [Verification evidence](evidence/five-deferred-reminder-delivery-2026-10-23/manifest.json).
  evidence: The pre-verified review found that WorkItemSuspendedReminderHandlerTests only registers one tenant, while dispatcher preservation tests execute a different writer. PendingDateAwaitDiscoveryIndex and the handler test were introduced by accepted 4.15 commit 07e23a6fc54fca9644e844de5d1dff6f49b58b22. Seed tenant A, handle tenant B with a forced registry conflict, and assert both remain in persisted discovery state; no runtime defect is claimed.

## Deferred from: code review of spec-4-16-prove-platform-works-parity-and-rollback.md, iteration 8 (2026-10-20)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md`
  summary: Compare complete Platform consumer source membership at the end of a verifier run, and make the packet input recheck's scope claim match what it compares (review T241).
  evidence: The end-of-run loop at `P/eng/verify_works_host.py:1029-1031` re-hashes only the paths recorded at `:875-878`, so a Platform `src/**/*.cs` added after that baseline compiles into self-consistent DLL hashes without being bound; iteration 7 rejected the same gap as T225. The review6 `tools/bind_inputs.py:26-29,44-45` also re-hashes recorded keys only, while `input-binding.json` calls it a "complete" comparison with Works source "membership". Fold into the T222 end-of-pipeline extraction; the verifier's Works consumer end check already compares the full inventory.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Align release README authentication instructions with the independently committed OIDC release workflow.
  status: done 2026-10-23
  resolution: `spec-five-deferred-host-boundaries.md` aligns release prerequisites with NUGET_USER, exact trusted-publisher matching and the temporary OIDC key minted by the committed workflow.
  evidence: Review B2: README still requests a stored NuGet API key, while the release migration in 9dbd2f07d3313b6df3c491e9cf3510b87b32fe65 obtains a key using NUGET_USER. This was not introduced by the hosting cutover.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Tighten the pre-existing mTLS refusal probe against post-authentication transport resets.
  status: done 2026-10-23
  resolution: `spec-five-deferred-host-boundaries.md` requires an authenticated server plus a structured certificate-refusal alert, distinguishes foreign-CA evidence from certificate_required, and proves genuine refusals alongside authenticated EOF/reset controls in both TLS protocols.
  evidence: Review B8: VerifyAsync can count authenticated-server/no-SETTINGS transport failure as refusal; comparison with pre-cutover 696781961155d764ed805843f55b40dc5882e887 confirms the behavior predates this change. Add a post-authentication reset control while preserving fail-closed behavior.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Require serverAuth application policy in the pre-existing custom certificate trust proof.
  status: done 2026-10-22
  resolution: `spec-five-deferred-runtime-safeguards.md` adds serverAuth to the custom-root chain and proves client-only refusal with otherwise trusted chains and the actual matching-SPIFFE loopback TLS probe.
  evidence: Review B9: the X509Chain policy omits application policy before and after cutover. Verify with a clientAuth-only certificate negative control in the owning proof.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Measure ordinary family-fold latency and capacity against real store budgets (unverified medium).
  evidence: Review B10: family-wide reads predate cutover; no admitted-population latency or operational-budget failure has been demonstrated. Settle with real-store population/capacity measurements under the selected SDK profile.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Extend Platform canonical source closure to consumed root-dependency build and source inputs.
  evidence: Review R2B3: a temporary fixture changed Hexalith.Builds/Props/Hexalith.Build.props without changing dependency_inputs, which binds only Directory.Packages.props. This unchanged owner policy predates the CI bootstrap; current qualification independently captures all 1,779 Builds inputs. Record dependency revisions and consumed overlays and test drift refusal in the owner.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Terminate owned command descendants when the Platform verifier times out or is cancelled.
  status: done 2026-10-23
  resolution: `spec-five-deferred-verifier-safeguards.md` runs commands in owned POSIX groups and terminates group members and reaps the leader on timeout, cancellation or completion; real controls cover launch-time signals, cleanup grace and detached-session ownership boundaries.
  evidence: Review R2B5: subprocess.run(timeout=1800) only kills the direct process; a temporary owned fixture confirmed its sleep descendant survives timeout and explicitly cleaned that PID. The helper is unchanged from the pre-existing verifier. Use an owned process group and a descendant-cleanup regression.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Bind reusable EXT-HOST-1 component attempts to actual suite and dependency inputs.
  evidence: Review R2B7: the pre-existing attempt key contains HEAD, plan and profile (now also Release mode), and a matching key returns stored success without inspecting dirty suite or external guard sources. The fresh Works gate does not reuse this complete-profile success path; fix and test cache source identity in EXT-HOST-1.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Assess Debug-only runtime coverage of Works supporting conformance facts (unverified medium).
  evidence: Review R2B8: the pre-existing full gate builds the Debug Works consumer and runs 114 Platform Debug tests, but executes the 255 supporting Works facts only in Release. No current source-only runtime defect is identified. Execute those methods against Debug plus a controlled Debug-only failing mutation to settle whether this omission hides behavior before changing the declared requirement.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Bound outstanding noncooperative identity-provider operations across repeated authorization timeouts.
  status: done 2026-10-23
  resolution: `spec-five-deferred-ingress-authority.md` verifies sixteen shared SDK slots across separate policy instances: repeated timeouts cannot admit another incomplete identity task or stream read; a single completed/faulted provider frees exactly one slot, and actual completion permits source acquisition again. This bounds outstanding provider tasks; callback-worker cleanup remains a separate pre-existing SDK limitation below. [Verification evidence](evidence/five-deferred-ingress-authority-2026-10-23/manifest.json).
  evidence: Review R2B9: WorksQueryPolicyDeadlineTests proves the provider task can remain incomplete after the fail-closed response, and WorksQueryPolicy has no concurrency bound on subsequent calls. The policy is unchanged from pre-cutover 696781961155d764ed805843f55b40dc5882e887. Fix in the owning identity-client boundary and verify repeated noncooperative calls.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Reject unsupported Parties gateway schemes at host startup.
  status: done 2026-10-22
  resolution: `spec-five-deferred-runtime-safeguards.md` restricts the effective Parties address to HTTP/HTTPS and verifies startup refusal for override and fallback schemes, preserving valid explicit-override precedence.
  evidence: R3B3: pre-cutover 696781961155d764ed805843f55b40dc5882e887 already used absolute-only validation and HttpClient BaseAddress assignment; validate HTTP/HTTPS for both address sources and prove refusal before requests.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Align or validate the Works and SDK query gateway identities for custom deployments.
  status: done 2026-10-23
  resolution: `spec-five-deferred-host-boundaries.md` validates exact paired identities at startup and proves default/custom signed caller-authority HTTP queries, workload enrollment and denied caller/digest requests before any identity or state access; deployment guidance requires restart for changes.
  evidence: R3B4: pre-cutover Works policy uses Works:Query:GatewayWorkloadId while already-selected SDK 3.126.0 uses Authentication:QueryAuthority:GatewayWorkloadId; prove paired settings with a nondefault caller-authority host request.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Record safe structured diagnostics for projection query failures.
  status: done 2026-10-22
  resolution: `spec-five-deferred-runtime-safeguards.md` emits source-generated event 4510 with bounded tenant/correlation and finite categories. Both adapters cover corrupt documents, invalid manifest shape, SDK outages, cancellation, forbidden queries, and throwing loggers without payload or exception-text disclosure.
  evidence: R3B7: both SDK query catch blocks discard exceptions, identically to pre-cutover 696781961155d764ed805843f55b40dc5882e887; preserve safe responses and test bounded correlation/scope/category diagnostics without payload disclosure.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Record bounded tenant discovery diagnostics while retaining incomplete-scan retry.
  status: done 2026-10-22
  resolution: `spec-five-deferred-runtime-safeguards.md` emits event 4511 for global acquisition and tenant-local discovery failures, preserving incomplete retry, healthy tenants, caller cancellation, persisted membership, and the original global failure contract even when logging fails.
  evidence: R3B8: WorksProcessSourceCatalog is byte-identical to pre-cutover and loses invalid-membership/store-outage causes in its catch; add safe scoped diagnostics without returning partial invalid families.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Qualify unpaged authorized queue responses at real invocation limits before defining paging behavior.
  evidence: R3B9, unverified medium: pre-existing WhatsNextQueryHandler ignores Paging; no response-size/deadline failure is demonstrated. Real-store and real-invocation tests at admitted item/byte limits would settle the claim.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Define and document the Parties service-bearer rotation boundary.
  status: done 2026-10-23
  resolution: `spec-five-deferred-host-boundaries.md` documents overlapping authority and restart/replacement ordering; signed HTTP requests rehearse revocation, an existing secret-provider reload without credential refresh, and successful replacement against unchanged persisted projections, with zero denied state reads.
  evidence: R3B10: pre-cutover and current typed clients capture IOptions.Value headers; in-process secret changes do not refresh authority. Document/rehearse restart with replacement authority or separately specify/test a refreshable provider.

## Deferred from: code review of spec-4-9-migrate-works-hosting-to-the-platform-boundary.md, round 2 (2026-10-22)

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Prove no duplicate MSBuild nodes across transitive external reference chains (unverified medium).
  evidence: `P0_SolutionReferencesKeepOneGlobalPropertySetPerWorksProject` checks only first-hop ProjectReference global properties; hosted run 39674116771 and parallel local Release/Debug builds pass, but the original failure was intermittent. Settle with a graph-build or binlog node-identity assertion over Release and Debug solution builds.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Filter linked-fixture build outputs by path relative to each owning root.
  status: done 2026-10-23
  resolution: `spec-five-deferred-verifier-safeguards.md` filters Identity, Composition, explicit Compile links and Parties inputs relative to their owners; bin/obj workspace-parent controls retain source bindings and exclude generated outputs.
  evidence: Pre-existing `linked_fixture_snapshot` (`references/Hexalith.Platform/eng/verify_works_host.py:953`) tests `{"bin","obj"}` against absolute `path.parts`; a Works root below a `bin` or `obj` directory would drop every Identity/Composition/Parties source from the binding.

## Concurrent Story 4.8 review audit (2026-10-23)

<a id="story-4-8-concurrent-review-audit"></a>

The [original Story 1.5 review](https://github.com/Hexalith/Hexalith.Works/blob/ddd571996df3057be39bb337cb0a9a0ff6f983ff/_bmad-output/implementation-artifacts/spec-1-5-link-a-conversation-after-creation.md#L96)
described a concurrently changing AppHost. The B/V identifiers below refer to that review, whose original
findings remain historical evidence. The following two claims must not be read as current defects:

| Historical claim | Reconciliation and source evidence |
| --- | --- |
| The default placement/scheduler path has no deterministic assertion (B8/V4). | At [`56a235c28753d2f87ef6897b39a032a1989b5610`](https://github.com/Hexalith/Hexalith.Works/blob/56a235c28753d2f87ef6897b39a032a1989b5610/tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs#L235), `DefaultAppHostComposesTheMtlsActorControlPlane` asserts the default resources, mTLS arguments, localhost 51005/51006 endpoints, scheduler storage and sidecar dependencies. The deterministic absence claim was superseded by that source. |
| All trust domains changed to localhost, so workload/policy ACLs must follow (B14 and the related ACL review context). | At [the same revision's `AssertMtls`](https://github.com/Hexalith/Hexalith.Works/blob/56a235c28753d2f87ef6897b39a032a1989b5610/tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs#L600), the test explicitly pins `controlPlaneTrustDomain` to `localhost`, `accessControl.trustDomain` to `public`, and every policy's `trustDomain` to `public`. [Story 4.8 implementation notes](spec-4-8-register-and-reconcile-date-reminders-durably.md#implementation-notes) record the same distinction. B10/V5's historical absence-of-assertions claim is superseded by these assertions. |

[Current hosting ownership and verification history](../../docs/platform-hosting-cutover.md) place the
control plane and policies in Platform. The [retired-test replacement inventory](evidence/story-4-9/verification/retired-test-replacements.json)
maps `WorksAppHostTopologyTests.cs` to its replacement coverage. This audit inspects retained source and
existing evidence; it does not claim a new live run or close separate live-prerequisite, scheduler-security
or catalog/specification entries. The historical Story 1.5 review is preserved unchanged.

## Deferred from: code review of spec-five-deferred-ingress-authority.md (2026-10-23)

- source_spec: `_bmad-output/implementation-artifacts/spec-five-deferred-ingress-authority.md`
  summary: Retain shared SDK provider admission and cancellation-source lifetime until both the provider task and initiated cancellation callbacks terminate (medium).
  evidence: An isolated probe compiling the unchanged AuthoritativeStreamReadDeadline source at EventStore `6a0b6441f6d78a3007678a1d0ec336e9cb8ff961` admitted 20 providers while all 20 cancellation callbacks remained blocked, despite its sixteen task slots: each callback canceled its returned task before blocking, and the task-completion continuation released admission. The mechanism predates this consumer change; original Works authority resolution also leaves such callbacks running after provider-task completion. The five-item fix bounds outstanding provider tasks and query response completion, not completed-task callbacks. Correct the technical-module helper to release only after both lifetimes and retain its source accordingly; add an executing combined race control, then publish and qualify a corrected SDK producer before claiming callback-worker admission. Do not duplicate SDK mechanics in Works. Reproducer and output are retained in this spec's verification packet.

## Deferred from: code review of spec-five-deferred-rebuild-admission.md (2026-10-23)

- source_spec: `_bmad-output/implementation-artifacts/spec-five-deferred-rebuild-admission.md`
  summary: Qualify Works tenant population against the SDK individual-document ceiling, or partition the queue document before claiming 10,000-owner staging capacity.
  evidence: Executing review control: a valid 10,000-owner plan with 64-character IDs and 16 multi-page histories fits the tenant byte budget (10,347,008 bytes) and operation ceiling (40,035 of 40,259), but its queue document is 1,360,142 canonical value bytes. The actual SDK coordinator refuses staging at its independent 256 KiB mutation bound before any candidate write, preserving the seeded generation and exact prior roll-up bytes. This pre-existing document-size limit is separate from the successfully qualified 16 MiB history rebuild over four owners and the canonical batch cap; raising that cap does not partition a single document. A population contract or bounded queue-document partitioning requires a separate domain design. Regression: ActualRebuildPlansFitTheRegisteredOperationCeiling; verification packet in evidence/five-deferred-rebuild-admission-2026-10-23/manifest.json.


### Story 4.9 publication-resume review follow-ups — 2026-10-25

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Sanitize identity-provider failure logging in WorksQueryPolicy.
  evidence: The complete exception is passed to LogWarning at the authorization boundary, bypassing bounded diagnostic categories; the same call exists in pre-cutover6967819. Verify a generated private canary never reaches logging.
  status: done 2026-10-28
  resolution: `spec-five-deferred-telemetry-boundaries.md` — EventId 4512 records finite categories and bounded tenant/correlation metadata without raw exceptions; both query handlers have private-canary regressions.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Contain authorization logger failures so an identity outage still returns a fail-closed denial.
  evidence: WorksQueryPolicy logs inside its provider-error catch without shielding sink exceptions; both query handlers await that policy outside their read-error catch, unchanged from pre-cutover6967819. Exercise a throwing logger at this boundary.
  status: done 2026-10-28
  resolution: `spec-five-deferred-telemetry-boundaries.md` — The bounded authorization diagnostic contains IsEnabled and Log exceptions; both query handlers retain Forbidden with zero Works state reads under throwing sinks.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Apply consistent retained-membership validation to current-generation query manifests.
  evidence: The unchanged IsValidCurrent validator accepts Items/LastSequences keys omitted from MemberWorkItemIds; SDK query readers can return absence while WorksProcessSourceCatalog rejects that contradictory persisted shape. Verify malformed persisted manifests explicitly; no new partial-write cause was established.
  status: done 2026-10-28
  resolution: `spec-five-deferred-history-admission.md` — Shared current-generation validation refuses omitted item/watermark membership, duplicate members and blank members; both query adapters retain unavailable responses and exact persisted bytes.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Establish projection-store cancellation bounds for SDK query reads (unverified medium).
  evidence: Coordinator read/status awaits lack an independent caller-side acquisition deadline, unchanged from the pre-cutover SDK path. The finding does not show the real Dapr store ignoring cancellation; a real-store timeout/cancellation probe plus an admitted non-cooperative control would settle the operational effect.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Bind reusable Platform package-cache admission to independent public-feed integrity evidence.
  evidence: published_producers records feed/SHA-512 metadata and archive-to-DLL equality but does not authenticate the archive against independently obtained public bytes; the boundary predates this resume. This release separately verified the official Aspire/Client/DomainService packages; add reusable forged-cache controls without weakening those checks.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Correct the existing public query summaries to distinguish absence from projection unavailability.
  status: done 2026-10-28
  resolution: `spec-five-deferred-contract-and-proof-integrity.md` corrects both handler XML summaries: an available projection can yield NotFound/empty results; missing, invalid or unavailable required projection documents yield projection-unavailable metadata. Recorded parked members remain omitted from successful degraded queue results. Existing SDK query regressions pass with unchanged runtime behavior.
  evidence: GetWorkItemQueryHandler and WhatsNextQueryHandler XML summaries still promise NotFound/empty results for unavailability, although the pre-existing SDK paths return projection-unavailable failures; both mismatch and behavior are present at pre-cutover6967819.

## Deferred from: code review of spec-4-9-migrate-works-hosting-to-the-platform-boundary (2026-10-26)

Hosting/runtime and architecture-guards group, Works `18e94d3..ab020fa`. These entries retain each independent finding's verdict; repeated findings refer to existing work and do not claim new cutover regressions.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26-B1 — Sanitize Parties identity-provider failure logging (medium; existing follow-up).
  evidence: WorksQueryPolicy.cs:77 passes the complete exception to LogWarning; the same call exists at pre-cutover6967819 and is already recorded in the 2026-10-25 publication-resume follow-ups. Verify a private exception canary never enters logging; do not duplicate the owning correction.
  status: done 2026-10-28
  resolution: Duplicate of the owning identity-provider logging item above; resolved by `spec-five-deferred-telemetry-boundaries.md`, counted once.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26-B3 — Apply required scalar-member validation to projection evidence (medium; pre-existing).
  evidence: WorkItemSharedRebuildManifestBuilder.cs:59 uses WorkItemProjectionEventDecoder, whose ordinary deserializer accepts a missing ReEstimated.Estimated as zero; WorksRecoveryEventDecoder rejects the same missing member. The decoder/call exist at18e94d3. Verify corrupt-member refusal and legitimate historical payload compatibility before choosing the owning parser correction.
  status: done 2026-10-28
  resolution: `spec-five-deferred-history-admission.md` — Projection and recovery share required scalar-member checks, with catalog-wide omission/nonpositive-sequence regressions and frozen writer/compatibility corpus controls.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26-B4 — Establish and enforce the supported projection discriminator set (medium; pre-existing).
  evidence: WorkItemProjectionEventDecoder.SimpleTypeName resolves Foreign.Namespace.WorkItemCancelled to the Works cancellation type; recovery checks the exact full or short Works name. This behavior exists at18e94d3. Establish legitimate historical aliases and verify corrupt foreign aliases cannot change a projection before tightening admission.
  status: done 2026-10-28
  resolution: `spec-five-deferred-history-admission.md` — Projection admission now requires the exact catalog short or full name; foreign namespace aliases are refused while frozen historical payloads remain readable.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26-B5 — Require creation evidence before deriving state-changing recovery reminders (medium; pre-existing).
  evidence: WorksSourceStreamReader.cs:112 applies WorksExpiryEvidence, which rejects pre-creation rescheduling but accepts an initial WorkItemSuspended; WorksReminderIntentSource can derive a date reminder from that corrupt history. The paths exist at pre-cutover6967819. Preserve rejection-only histories while verifying whole-history creation ordering and no reminder registration on corrupt evidence.
  status: done 2026-10-28
  resolution: `spec-five-deferred-history-admission.md` — Every state-changing recovery event requires preceding creation; rejection-only histories remain supported. Actual SDK convergence retains no reminder witness/candidate or scheduler arm for an initial suspension.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26-B6 — Measure what's-next query cost over retained terminal membership (maybe-false; unverified medium).
  evidence: WhatsNextQueryHandler.cs:113 loads parking and roll-up documents for all retained members before executor/status filtering; SharedProjectionEpochCoordinator.ReadManyAsync reads logical keys sequentially. This predates cutover, and no admitted-population timeout was demonstrated. Real-store population measurements against the actual caller deadline would settle the operational effect before any filtering/storage redesign.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26-B7 — Measure ordinary full-family projection-fold cost (maybe-false; unverified medium; existing follow-up).
  evidence: WorkItemSharedProjectionRebuildHandler.cs:170 reloads retained member histories and refolds the tenant per ordinary delivery. The same concern is already in the 4.14 and 4.9 performance follow-ups. No measured operational-budget failure was demonstrated; settle using real-store latency and representative admitted population/history limits before changing folding semantics.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26-B8 — Measure whole-catalog rescans during paged discovery (maybe-false; unverified medium).
  evidence: WorksProcessSourceCatalog.cs:27 scans tenant membership and materializes all sources before applying afterKey at83 on each page. This predates cutover and no recovery/readiness deadline failure was demonstrated. Measure complete multi-page sweeps with real-store latency; any cursor optimization must preserve incomplete-scope detection and progress for healthy tenants.

## Deferred from: code review of spec-4-9-migrate-works-hosting-to-the-platform-boundary (2026-10-26, CI/build group)

CI/build and scripts group, Works `18e94d3..ab020fa`. Both entries predate this story's cutover work or concern repository settings rather than code.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26C-B5 — Drop release `issues`/`pull-requests: write` scopes no longer required after inlining (low; pre-existing).
  status: done 2026-10-28
  resolution: Duplicate of the owning release-authority item above; corrected and locally verified by `spec-five-deferred-contract-and-proof-integrity.md`, counted once. A future authorized release remains the live publication check; this batch did not publish.
  evidence: release.yml:54-55 keeps scopes that 4ef0217 added only for the reusable workflow's declaration; 640c8b5 (pre-cutover 6967819) inlined the job, and .releaserc.json disables fail/success comments and labels. contents: write already dominates; verify removal with the next authorized release.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R26C-A4 — Decide whether the Platform parity job must be a required merge check on `main` (low; repository setting).
  evidence: release.yml verify-source requires a successful push CI run, so parity blocks release; `main` has no branch protection or rules, so a red parity job does not block merges.

- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: Define and validate empty per-aggregate rebuild histories separately from empty tenant inventories and rejected-only histories.
  evidence: BuildEpoch retains each supplied aggregate as membership and parking, but ValidateHistory accepts Events=[] and the fold emits no roll-up. The same loop and zero-event behavior exist in pre-cutover 696781961155d764ed805843f55b40dc5882e887. Verify the supported capture contract and reject malformed existing-aggregate emptiness without breaking legitimate absence/rejection-only cases.
  status: done 2026-10-28
  resolution: `spec-five-deferred-history-admission.md` — The domain rebuild handler refuses null/empty aggregate evidence separately from empty inventory; rejection-only history retains membership and source bytes without a roll-up. SDK lifecycle refusal preserves the prior committed generation and served documents after reconstruction.

## Deferred from: code review of spec-4-9-migrate-works-hosting-to-the-platform-boundary (2026-10-27, Platform live proofs and harness group 3A)

Group 3A, Works `18e94d3..986b56f`, limited to the Platform live-proof and harness integration tests. All three entries concern proof code already present at pre-cutover `6967819`.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R27A-A1 — R10 lacks AD-24's wrong-trust-domain, forged-high-sequence and ordinary-user SpawnChild negatives (medium; pre-existing; AD-21/AD-23 target).
  evidence: The historical lane (`WorksPlatformHistoricalPipelineSmokeTests.cs:92`) submits SpawnChild with the ordinary dev JWT and requires Completed. The trust probes use only the `public` trust domain, and the projection forgery injects sequence 0. Platform `PROOFS["R10"]` (`verify_works_host.py:744`) requires none of these negatives. Either record R10's narrowing to historical-lane equivalence (AC3), or add the negatives and their Platform proof entries once registry-authorized child creation (AD-21/AD-23) is storied.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R27A-A3 — AC2's shared-rebuild failure branch has only an in-memory proof (medium; pre-existing).
  evidence: `WorksPlatformProjectionChecks.cs:184` drives Begin→Commit only. Abort under concurrent delivery is proven only by `WorksSdkProjectionQueryTests.Aborted_concurrent_rebuild_keeps_old_generation_and_allows_live_delivery_afterward` over `InMemoryReadModelStore`. At the next Platform requalification, add a live abort-under-concurrent-delivery check asserting the served generation and catch-up convergence on the real state store.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: R27A-V1 — Test the Platform telemetry collector's label and shape refusals (medium; pre-existing).
  evidence: The live recovery facts use `telemetry.Failures.ShouldBeEmpty()` as their forbidden-label oracle. `WorksPlatformTelemetryCollectorTests` never sends a well-formed point the collector must refuse: a forbidden attribute, a non-allowlisted reason, a reason label on `alert`, an unlabeled reason gauge, or a non-gauge instrument. Inverting `WorksPlatformTelemetryCollector.cs:233` keeps every test green. Add an exporter-driven theory in that supporting class.
  status: done 2026-10-28
  resolution: `spec-five-deferred-telemetry-boundaries.md` — Real OTLP HTTP/protobuf exporter theories exercise forbidden attributes, unknown reasons, reason on alert, unlabeled reason, and nongauge instruments, asserting refusal and no accepted points.


- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: G3A-B1 — Validate effective OTLP endpoint/protocol in the existing Platform fixture-composition guard.
  evidence: The fresh-assembly reflection probe accepts effective https://foreign.example:4318/grpc because IsOwnedTelemetry checks preliminary fixtureWrites. The complete guard is normalized-byte-identical to pre-cutover696781961155d764ed805843f55b40dc5882e887. Validate combined values while retaining the requirement that the fixture redirects both keys, and add a conditional-callback refusal control. Parent probe and source lineage are retained in the group3A packet.
  status: done 2026-10-28
  resolution: `spec-five-deferred-telemetry-boundaries.md` — The guard validates combined endpoint/protocol values while requiring both explicit fixture keys; actual conditional-callback tests cover foreign endpoint, grpc protocol, removals, and a valid redirect.
- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: G3A-B3 — Bind the existing phase-local recovery alert-clear wait to the exporter observation time after settledAt.
  evidence: The receipt-only predicate predates cutover. Canonical acceptance already refuses an exported clear older than the fresh raise; the narrower phase-local boundary can still accept a delayed after-raise/pre-settled zero. Add that delayed-point control without weakening the canonical after-last-raise ordering or changing domain deadlines.
  status: done 2026-10-28
  resolution: `spec-five-deferred-telemetry-boundaries.md` — The phase requires both exporter timestamp and collector receipt after settlement; delayed/equal exporter observations are refused by deterministic controls. Canonical after-last-raise ordering is unchanged.
- source_spec: `/home/administrator/projects/hexalith/works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md`
  summary: G3A-B7 — Replace optimization-removable asserts in the legacy Story4.15 isolated package-proof HASHES block.
  status: done 2026-10-28
  resolution: `spec-five-deferred-contract-and-proof-integrity.md` replaces every HASHES assert with an explicit failure. The production heredoc is executed on valid public/candidate graphs and project/version/hash/count/preexisting-archive refusals under optimization levels 0, 1 and 2. Invalid inputs emit no qualifying proof; the pre-correction optimized controls failed as expected.
  evidence: All four assert checks are present before cutover. The exact block, with an invalid project graph, exits1 normally but exits0 with isolatedPackageProof:[] under python -O. Replace graph/version/archive/count assertions with explicit failures and add optimized-environment controls; this is an intermediate-block repro, not a claim that the complete shell helper or canonical gate passed invalid inputs.

## Deferred from: review of spec-five-deferred-telemetry-boundaries (2026-10-28)

- source_spec: `_bmad-output/implementation-artifacts/spec-five-deferred-telemetry-boundaries.md`
  summary: Assess telemetry admission when a Platform project composes no OTLP keys (unverified medium).
  evidence: The pre-existing composition guard checks affected Platform-owned keys only; fixture-only OTLP keys are outside its overlap inventory and can bypass pair validation. The current canonical Platform capture lacking both keys was not demonstrated. Reproduce against an actual supported Platform composition with absent OTLP settings before broadening the guard from overlap protection to fixture-wide telemetry admission.
