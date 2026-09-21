# Deferred Work

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

### DW-20: The forbidden-family taxonomy exists twice with nothing reconciling the two lists: the direct project-file text scan keeps its own literal string list while the evaluated-closure policy keeps structur

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

### DW-53: The Contracts-derived gate binds payload admission but not roll-up effect, so a new event registered only to green the gate is accepted, consumes its sequence slot, and advances the watermark with no

status: done 2026-09-05
origin: spec-deferred 96d65c591dec
source_spec: `spec-rollup-contract-drift-hardening.md`
archived: 2026-09-18

### DW-54: WhatsNextQueueProjection keeps a structurally identical hand-maintained payload allowlist over the same delivery envelope, with no Contracts-derived gate, so the drift this story closed for roll-up st

status: done 2026-09-05
origin: spec-deferred 05c45a10513b
source_spec: `spec-rollup-contract-drift-hardening.md`
archived: 2026-09-18

### DW-55: The dead-letter capture parser's fixtures are hand-written literals rather than derived from the publisher type, so a rename on the producing side breaks capture in production while both parser tests

status: done 2026-09-05
origin: spec-deferred 3bd2f3fc99d1
source_spec: `spec-dapr-subscription-operations-hardening.md`
archived: 2026-09-18

### DW-56: Completed-marker failure can still be acknowledged as processed

status: done 2026-09-05
resolution: resolved by sweep bundle dw-domain-event-processing-hardening
resolution-undo: 3a19f3e607e67615252688bc602dc3efb8ba600ece76cf066c65c8c09296997a 2026-09-05 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-08-28)"), 2026-08-28
archived: 2026-09-18

### DW-57: Event processor does not reject non-work domains

status: done 2026-09-05
resolution: resolved by sweep bundle dw-domain-event-processing-hardening
resolution-undo: 3a19f3e607e67615252688bc602dc3efb8ba600ece76cf066c65c8c09296997a 2026-09-05 7374617475733a206f70656e
origin: migrated from legacy ledger ("Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-08-28)"), 2026-08-28
archived: 2026-09-18

### DW-58: The command-pipeline smoke-test prerequisite probe still collapses caller-requested cancellation into an unavailable result.
origin: spec-deferred dc9e48616d2d
location: tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs:175
source_spec: `spec-probe-cancellation-propagation.md`
severity: medium
reason: `WorksCommandPipelineSmokeTests.IsPortReachableAsync` catches every `OperationCanceledException` and returns `false`. The reminder-recovery lane now uses `WorksAppHostSmokeHarness.IsPortReachableAsync`, whose filtered catch preserves caller-requested cancellation, so it is no longer part of this deferred item.
status: open

### DW-59: The deterministic probe cases never run in the repository's habitual deterministic lane, because they live in a class that lane excludes by name.
origin: spec-deferred 282ced3de175
location: tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:145
source_spec: `spec-probe-cancellation-propagation.md`
severity: medium
reason: The routine deterministic command recorded across this repository's specs is `Hexalith.Works.IntegrationTests -class- "*SmokeTests"`, an exclude-by-class filter that drops every case in `WorksCascadeRecoveryPipelineSmokeTests`. Confirmed against the built Release assembly: `-list Tests` reports 15 `Port_probe_*`/`Prerequisite_gate_*` cases with no filter and 0 under `-class- "*SmokeTests"`. They still run in an unfiltered full-assembly run, and the spec's own verification command targets the class directly, so the coverage is not orphaned -- but a probe regression is invisible to the lane that is actually run by habit. Relocating them needs a new test class outside this file, which the intent's Block If fences off.
status: open

### DW-60: Follow-up review still recommended for dw-probe-cancellation-propagation after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-probe-cancellation-propagation.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260828-191811-dea9; this entry preserves the lingering recommendation for a deliberate later review.
status: open

### DW-61: Three untouched contract-flow test summaries still describe camelCase Web JSON as the real EventStore write path, contradicting the corrected claims this bundle landed elsewhere.
origin: spec-deferred 2c19a16a4d17
location: tests/Hexalith.Works.IntegrationTests/WorkItemHandoffChainContractFlowTests.cs:13
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: medium
reason: tests/Hexalith.Works.IntegrationTests/WorkItemHandoffChainContractFlowTests.cs:13-14 and UniformExecutorBindingLifecycleFlowTests.cs:17-18 both say "the real write path ... -> concrete JsonSerializerDefaults.Web serialization"; WorkItemProgressContractFlowTests.cs:57 says the event "survives concrete EventStore serialization" while serializing with camelCase JsonOptions. EventPersister writes options-free PascalCase, so the repository now asserts two different things about the same persisted form. These files sit outside the intent's named sweep list, so the omission is deliberate for this story.
status: open

### DW-62: The frozen WorkItemCannotReferenceParentFromAnotherTenant catalog sample carries a same-tenant parent, so its evidence contradicts the rejection it names.
origin: spec-deferred fcb2bd62083e
location: tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs:36
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
reason: tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs:36,81 builds Parent as new ParentWorkItemReference(Tenant, new WorkItemId("parent-001")) with Tenant = "tenant-alpha", the event's own tenant. EnvelopeCanonicalSequencingTests sources "tenant-beta" from its own helper instead. WorkItemV1Catalog is untouched by this change, but both new corpora now freeze those bytes, so correcting the sample later means regenerating two fixtures.
status: open

### DW-63: Nothing binds the EventStore revision quoted in the maintained docs to the actual checked-out submodule gitlink, so the corrected pin can silently rot on the next bump.
origin: spec-deferred 7a41b5161f32
location: docs/eventstore-api-surface-constraints.md:7
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
reason: grep over tests/ finds no assertion on b43e963403efa848eda9621b5e3e7e446c7faa2d or c61739206fd89619b7d29dfb0812225a234066bb; both SHAs exist only as prose in docs/eventstore-api-surface-constraints.md and docs/boundary-decision-record.md. This is the same documentation-drift failure mode DW-42 recorded.
status: open

### DW-64: The byte-exact corpus never freezes the PascalCase at-rest form of EffortEstimate, ObligationReference, or ConversationCorrelationId, because every catalog sample leaves them null.
origin: spec-deferred bb511d744622
location: tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/WorkItemCreated.v1.json
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: medium
reason: All 23 EventPersisterGolden fixtures are produced from WorkItemV1Catalog samples, and those samples leave InitialEffort, Obligation.Reference, and ConversationCorrelationId null on every event that can carry them; the camelCase Golden/WorkItemCreated.v1.json does freeze all three. Those nested contract records therefore have no frozen writer-side form anywhere, so a property rename or shape change inside them cannot turn the exact corpus red. Closing it means changing catalog sample values, which regenerates fixtures in both corpora -- the same coupling DW-62 records.
status: open

### DW-65: Story48Streams serializes stand-in EventStore stream bytes with camelCase Web options, the same contradiction DW-61 records for three contract-flow tests but at a helper DW-61 does not name.
origin: spec-deferred e4191a19474f
location: tests/Hexalith.Works.IntegrationTests/Story48Streams.cs:12
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
reason: tests/Hexalith.Works.IntegrationTests/Story48Streams.cs:12,31 builds StreamReadEvent.Payload with new JsonSerializerOptions(JsonSerializerDefaults.Web) while standing in for real per-aggregate stream pages, and feeds the Story 4.8 recovery sources that decode through WorksEventDecoder. EventPersister writes options-free PascalCase. It is not currently an escape hatch, because the shared decoder is separately pinned against PascalCase bytes by WorksDomainEventProcessorTests, but the fixture now contradicts the persisted form the rest of this bundle established. Outside the intent's named sweep list and outside DW-61's three files.
status: open

### DW-66: Both corpus membership gates enumerate the copied build output, so a fixture deleted from source survives in bin/ and membership still passes on an incremental build.
origin: spec-deferred 8bfcda99d79e
location: tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGoldenCorpusTests.cs:32
source_spec: `spec-envelope-persistence-proof-hardening.md`
severity: low
reason: EventPersisterGoldenCorpusTests.cs:32 and SchemaEvolutionGoldenCorpusTests.cs:26 resolve their corpus directory under AppContext.BaseDirectory and enumerate it with SearchOption.AllDirectories, while Hexalith.Works.IntegrationTests.csproj copies both directories with CopyToOutputDirectory PreserveNewest, which never prunes. A fixture deleted from source therefore still satisfies the bidirectional set-equality check until a clean build. The gate fails closed only in CI. Pre-existing for the Web corpus; inherited by the new exact corpus.
status: open

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
origin: spec-deferred 6f4552a53c41
location: src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:330
source_spec: `spec-projection-write-ordering-guard.md`
severity: low
reason: MaintainPendingDateAwaitIndexAsync does `PendingDateAwaitTenantIndex index = current ?? new(...)` and then mutates `index` in place inside ReadModelWritePolicy's retry transform. The what's-next transform was converted to build replacement dictionaries on every retry precisely so a rejected attempt cannot leak into loaded state; the pending-date sibling was left on the older pattern because this story's Never clause fences off pending-date behavior. No test depends on it today (RejectNextTrySaves is never armed on that key), but the two sibling transforms now disagree on a semantics the docs claim for the index family.
status: open

### DW-71: The /project response still echoes the freshly computed item even when both ordering guards refused the replay, so a caller can receive state that was never persisted.
origin: spec-deferred 2cfe338125c2
location: src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:179
source_spec: `spec-projection-write-ordering-guard.md`
severity: low
reason: DispatchAsync serializes `item` into ProjectionResponse unconditionally, and `indexAccepted` is computed and then discarded. Before this change the writes were unconditional, so the response always matched persisted state; a refused stale replay now returns a document the store does not hold, with no field distinguishing accepted from refused. Adding an acceptance signal changes the projection response contract, which this story's Block If fences off.
status: open
decision: 2026-09-01 Return persisted state — Keep the existing response shape but, after refusal, load and serialize the authoritative persisted item or ineligible state; add stale-replay tests proving response and store agree.
decision: 2026-09-01 Return persisted state — Keep the existing response shape but, after refusal, load and serialize the authoritative persisted item or ineligible state; add stale-replay tests proving response and store agree.

### DW-72: Follow-up review still recommended for dw-projection-write-ordering-guard after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-projection-write-ordering-guard.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260829-091730-0d52; this entry preserves the lingering recommendation for a deliberate later review.
status: open

### DW-73: The pending-date-await index and tenant registry stay unversioned and are neither rebuilt nor pruned by the shared reconciliation, so a work item dropped from a tenant's authoritative membership can r
origin: spec-deferred c1087044cae6
location: src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs
source_spec: `spec-shared-rollup-reconciliation.md`
severity: medium
reason: WorkItemSharedRebuildManifestBuilder emits operations only for the v2 tenant index, per-item roll-ups, and candidate-known legacy roll-up keys; WorksReadModelKeys.PendingDateAwaitIndexKey and PendingDateAwaitRegistryKey carry no generation token and appear in no manifest operation. Recovery therefore still enumerates awaits for ids that GetWorkItemQueryHandler now refuses as non-members. The underlying orphan-after-erasure gap predates this change; membership-based unreachability makes it observable from the query surface.
status: open

### DW-74: Follow-up review still recommended for dw-shared-rollup-reconciliation after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-shared-rollup-reconciliation.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260829-091730-0d52; this entry preserves the lingering recommendation for a deliberate later review.
status: open

### DW-75: Hexalith source-consumption project discovery remains constrained by the Hexalith.Works*.csproj filename glob.
origin: spec-deferred 9a356fa164ff
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:415
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
reason: The live source-consumption gate still enumerates projects by basename, so a differently named root-owned project could evade this specific check. This behavior predates the evaluated-input bundle and is outside its four ledger entries.
status: open

### DW-76: PackageVersion ownership does not trace Works-owned property overrides consumed by the approved shared catalog.
origin: spec-deferred 61f77899777d
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:226
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
reason: Ownership follows the XML file that defines the effective Version metadata. A Works-local property could influence a version expression in the shared catalog while the catalog remains the defining file. This pre-existing property-provenance problem is broader than evaluated PackageReference and PackageVersion item discovery.
status: open

### DW-77: The architecture-test lane is bound to the build machine's installed MSBuild layout instead of resolving it at run time.
origin: spec-deferred 769960e1cb23
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:475
source_spec: `spec-msbuild-dependency-discovery.md`
severity: medium
reason: The test project captures $(MSBuildToolsPath) into an AssemblyMetadataAttribute at compile time and copies the SDK's Microsoft.Build* assemblies next to the test host. A build-once/test-elsewhere pipeline, or an SDK patch installed between build and test, makes ConfigureInstalledMsBuild throw and fails the whole architecture lane. Replacing the hand bootstrap with MSBuildLocator adds a package dependency and is a design decision beyond this bundle.
status: open
decision: 2026-09-01 Adopt MSBuildLocator — Add centrally versioned test-only MSBuildLocator and Microsoft.Build dependencies, register an installed instance before loading MSBuild APIs, remove captured-path coupling, and add a build-once test-under-different-SDK-path regression.
decision: 2026-09-01 Adopt MSBuildLocator — Add centrally versioned test-only MSBuildLocator and Microsoft.Build dependencies, register an installed instance before loading MSBuild APIs, remove captured-path coupling, and add a build-once test-under-different-SDK-path regression.

### DW-78: The Release lane pins Platform=AnyCPU for every evaluated project regardless of its declared Platforms.
origin: spec-deferred 27c11709b8c0
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:29
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
reason: A project declaring a Platforms set that excludes AnyCPU would be evaluated under a platform it does not support, so platform-conditioned imports and ProjectReference items would silently drop out of the evaluated set the gates trust as complete. No project in the repository declares such a set today.
status: open

### DW-79: Restored NuGet package build props enter the custom-import freshness closure.
origin: spec-deferred c816fd817c7c
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:431
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
reason: IsCustomImportPath excludes installed-SDK imports and generated build output, but a package's build/buildTransitive props imported from the global packages folder is neither, so it becomes a restore input. Re-extracting or touching the package cache without restoring would report the evaluated dependency artifact stale.
status: open

### DW-80: Dependency declarations that only an inactive non-Release condition guards remain invisible when they arrive through an import.
origin: spec-deferred 65a78e5cf80d
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/MsBuildProjectEvaluation.cs:22
source_spec: `spec-msbuild-dependency-discovery.md`
severity: medium
reason: Owning-project XML is scanned condition-agnostically, so a conditional declaration in a governed or scanned project file fails closed. An import that declares a ProjectReference or PackageReference under `Condition="'$(Configuration)' == 'Debug'"` produces no evaluated item in the pinned Release lane and no owning-file sentinel, so no gate observes it. Evaluating a second lane, or treating a conditioned dependency item in any custom import as fail-closed, changes what the shared Builds props are allowed to declare and is a design decision beyond this bundle.
status: open
decision: 2026-09-01 Evaluate supported lanes — Evaluate and merge Release and Debug dependency surfaces together with declared framework and platform lanes, then apply canonical governance to the union with imported-condition regression tests.
decision: 2026-09-01 Evaluate supported lanes — Evaluate and merge Release and Debug dependency surfaces together with declared framework and platform lanes, then apply canonical governance to the union with imported-condition regression tests.

### DW-81: Runtime-adapter confinement still decides EventStore-runtime and Dapr direction from owning-file XML and basename prefixes.
origin: spec-deferred b052bd148f70
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs:53
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
reason: RuntimeAdapterGovernanceTests reads declared references from the governed project file only and compares them by name prefix, so an imported item or an unrelated project with an EventStore-runtime basename escapes that specific gate. The escape is partly covered elsewhere because ScaffoldGovernanceTests routes through the evaluated EvaluateProjectFile path, but with a different message. The intent named the dependency-direction, source-consumption, and restore-freshness gates; converting this fourth gate is separate work.
status: open

### DW-82: The architecture-test project pins one exact System.Diagnostics.EventLog assembly version to keep the MSBuild reference set warning-free.
origin: spec-deferred f3e8a31d6d81
location: tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj:41
source_spec: `spec-msbuild-dependency-discovery.md`
severity: low
reason: Dropping `Version=10.0.0.0`/`SpecificVersion` was tried and reintroduces MSB3277 (unification with the SDK's System.Configuration.ConfigurationManager), so the pin is load-bearing under the repository's zero-warning bar. An SDK band that ships a different EventLog assembly version will therefore need this literal edited. This is the same installed-SDK coupling as the MSBuild-layout entry above, but a distinct literal.
status: open
decision: 2026-09-01 Use portable dependency loading — Resolve EventLog consistently through the portable MSBuild loading approach selected for DW-77, remove the exact SDK assembly-version literal, and prove warning-free build and execution across SDK patch-path drift.
decision: 2026-09-01 Use portable dependency loading — Resolve EventLog consistently through the portable MSBuild loading approach selected for DW-77, remove the exact SDK assembly-version literal, and prove warning-free build and execution across SDK patch-path drift.

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
origin: code-review 4-8-register-and-reconcile-date-reminders-durably
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/WorkItemRollUpPayloadCoverageTests.cs
source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
severity: low
reason: Pre-existing, not caused by this diff — the overlap was created when `ProjectionPayloadCoverageTests.cs` was added in the prior 4.8 review round (commit `01d527a`), which duplicates `WorkItemRollUpPayloadCoverageTests`'s single coverage assertion (minus `EffectDisposition`/intentional-no-op checks). This diff only updates the older test to compile against the renamed `WorkItemRollUpPayloadDescriptor.Catalog` API. Two overlapping tests must now be kept in sync by hand; consider retiring or consolidating the older, narrower test.
status: open

## Deferred from: review remediation of 4-8-register-and-reconcile-date-reminders-durably (2026-09-08)

### DW-86: Story 4.7's two stream-reading sources advance their page cursor one event too far
origin: review-remediation 4-8-register-and-reconcile-date-reminders-durably
location: src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:86; src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:159
source_spec: `_bmad-output/implementation-artifacts/4-7-trigger-reactor-translators-from-the-live-event-stream.md`
severity: high
reason: Out of scope, same root cause as the HIGH finding fixed in Story 4.8's `PendingDateAwaitStreamReader`. `StreamReadRequest.FromSequence` is an EXCLUSIVE lower bound — verified in the pinned submodule (`AggregateActor.ReadEventsRangeAsync` reads from `fromSequence + 1`, `FakeAggregateActor` filters `SequenceNumber > fromSequence`, and EventStore's own `DaprBackupCommandService` pages with `readCursor = LastSequenceReturned`, no increment). Both files page with `from = LastSequenceReturned + 1`, so every multi-page per-aggregate read silently drops exactly one event per page boundary; the cascade file's comment even states the wrong rule ("the gateway paginates by FromSequence = LastSequenceReturned + 1"). `StreamsController`'s own inline comment says the same wrong thing, which is likely where the pattern came from. These are Story 4.7 surfaces (cascade descendant discovery and child-completion parent lookup) and need their own change plus multi-page tests, not a drive-by edit inside a reminders story.
status: open

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Complete deterministic and live verification for the concurrent Story 4.8 AppHost-owned Dapr placement/scheduler path.
  evidence: The default branch composes placement and scheduler on ports 51005/51006, but the topology test forces external endpoints and command/cascade/reminder prerequisites still require the superseded 50005/50006 or 6050/6060 services, so the owned path can remain unasserted or skip.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Pin configuration- and policy-level Dapr ACL trust domains in the concurrent Story 4.8 topology tests.
  evidence: All four YAML files changed those values to `localhost`, while deterministic tests assert only `spec.mtls.controlPlaneTrustDomain`; restoring `public` on an allow policy would leave tests green and deny the intended Sentry identity.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Reconcile the concurrent Story 4.8 specification with its current localhost trust domain and the 40-type catalog baseline.
  evidence: Its implementation note still recommends `controlPlaneTrustDomain: "public"`, and its frozen constraint/verification still says catalog 37 even though the concurrent code uses `localhost` and Story 1.5 makes the current catalog 40.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Review the concurrent Story 4.8 scheduler's embedded-etcd network exposure.
  evidence: `DaprSelfHostedMtls.AddControlPlane` passes `--etcd-client-listen-address=0.0.0.0`; co-networked containers can reach that listener even though its port is not published to the host, so the scheduler should bind loopback unless remote clients are required.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Reject null-correlation `ConversationLinked` evidence in the what's-next projection.
  evidence: The separately committed Story 1.5 descriptor treats a null correlation as an accepted intentional no-op and can advance the source watermark for malformed evidence; Story 4.8 does not own that projection contract.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Validate persisted `ConversationLinked` identity before applying it to aggregate state.
  evidence: The separately committed Story 1.5 replay overload validates the correlation only, so a foreign tenant/work-item/aggregate identity in corrupted persisted evidence could become authoritative and reject a later legitimate link.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Add independent domain and tenant mismatch coverage for the Story 1.5 runtime envelope boundary.
  evidence: The pre-verified review found only aggregate-ID mismatch coverage; removing either the domain or tenant comparison currently leaves the focused adapter tests green.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Add malformed required-field dispatch coverage for `LinkConversation`.
  evidence: The pre-verified review found no raw runtime-adapter case with missing/null tenant, work-item, or conversation correlation, so the separately committed Story 1.5 fail-closed guards can regress undetected.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Audit stale concurrent-review records at the end of the deferred-work ledger.
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
  evidence: Split from the 4,488-token remediation spec after review showed this independently shippable adapter-boundary bundle needs all-command reserved-envelope and retained reserved-payload tests.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Complete reminder recovery and scheduling telemetry plus retry-verification hardening.
  evidence: Split from the 4,488-token remediation spec because parking-read classification, parked-skip accounting, scheduler-failure diagnostics, reconciliation exhaustion, and their focused tests form an independently shippable recovery bundle.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Register a production `IProjectionChangeNotifier` for the Works host or explicitly retain the projection seam as inactive.
  evidence: `AddEventStoreDomainService` uses Client registrations, `WorksHost` resolves the notifier optionally, and no production Works registration supplies an implementation; this predates the projection hardening patch and needs a transport/dependency decision.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Make live projection unknown-event handling fail closed without advancing durable watermarks beyond understood evidence.
  evidence: Unknown event types do not mark live roll-ups incomplete, while the pending-date watermark still uses the maximum raw sequence; a forward-version event can therefore expose an available total and prevent later repair when that event becomes known.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`
  summary: Bound generic projection decoder skip telemetry consistently with identity-mismatch telemetry.
  evidence: EventId 4504 still accepts raw namespace-qualified event type and correlation text for unknown or malformed payloads, permitting oversized diagnostic records outside the newly bounded EventId 4505 branch.

- source_spec: none
  summary: Complete reminder recovery and scheduling telemetry plus retry-verification hardening.
  evidence: Split from the current Story 4.8 build so the independently shippable reserved-tenant command-envelope hardening can be implemented and reviewed first.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-5.md`
  summary: Preserve typed incomplete-scan context when partial reminder processing ends in a non-caller cancellation.
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
  evidence: The shared template still says the step "will be retried at-least-once" on a final startup attempt and at call sites with no in-process repeat; spec-8 explicitly forbids changing that template, so a separate operator-surface decision is required.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md`
  summary: Decide whether exact caller cancellation inside a tenant scan should retain that tenant's local partial results and failure evidence.
  evidence: An exact cancellation unwinds `ScanTenantAsync` before its local result is returned, delaying already collected current-tenant evidence until another startup pass; this behavior predates spec-8 and its frozen scope leaves all three in-tenant filters unchanged.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
  summary: Add navigation and provenance guidance between the compacted deferred-work ledger and its archive.
  evidence: The pre-existing 2026-09-18 archive migration leaves active-ledger stubs without an archive link and starts the archive at an H3 entry without a title, policy, backlink, or provenance note, making historical evidence unnecessarily difficult to discover.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
  summary: Reconcile duplicate and ambiguously dated decision fields in the deferred-work archive.
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
  evidence: DW-20, DW-53, DW-54, and DW-55 retain visibly truncated headings in both locations; the corruption predates the spec-9 close-out and is historical-data hygiene rather than reminder-runtime behavior.

- source_spec: `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
  summary: Broaden EventId 4608 operator guidance to cover non-availability parking-read failures and identify the parking record without implying its key is logged.
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
  evidence: `ReminderReconciliationService` returns permanently after its configured attempts, so a dependency outage lasting beyond startup can leave durable reminder recovery inactive until another host restart; this predates the final harness increment.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Determine whether an empty pending-await tenant registry can race ahead of projection backfill and strand pre-index reminder recovery.
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
  evidence: The production adapter uses token-aware `ReadToEndAsync` and kills the process first, but the current evidence does not establish that pipe reads cannot ignore cancellation indefinitely; a real child-process test would settle the maybe-false hang risk.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Decide whether the projection ingress must reject duplicate or gapped positive event sequences.
  evidence: `WorkItemProjectionDispatcher` sorts replay events but does not validate contiguity; settle this maybe-false finding by proving whether the EventStore projection-delivery contract can emit duplicate or gapped sequences.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Validate stream-page cursor metadata or document the EventStore invariant that makes malformed cursor shapes unreachable.
  evidence: `PendingDateAwaitStreamReader` trusts `IsTruncated`, `LatestSequence`, and `LastSequenceReturned`; inconsistent metadata could hide an unread tail or advance beyond returned events, but reachability requires gateway-contract evidence or fault injection.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Give repeated occurrences of the same date await distinct durable resume identities without losing redelivery idempotence.
  evidence: `DateResume` derives the EventStore message id only from tenant, work item, and instant; a legitimate re-suspension on that same instant within the 24-hour command-status retention can be deduplicated as the earlier occurrence. Fixing this pre-existing Story 4.6 design requires carrying a suspension occurrence or sequence through pending-await, reminder, and command identity.

<a id="story-4-8-canonical-reconciliation-stale-delay"></a>

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Recompute date-reminder delay at each scheduling operation during reconciliation.
  evidence: `DateReminderReconciler` snapshots `now` once before processing every candidate, so a future await scheduled late in a large pass receives its original relative delay and fires late by the time already spent processing earlier candidates; this behavior predates Story 4.8's baseline.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Normalize duplicate identical date awaits at the durable await-set boundary.
  evidence: `PendingDateAwaitProjection` preserves duplicate `DateReached` conditions, producing repeated schedule or submit attempts and inflated reconciliation counts even though deterministic reminder and message ids prevent duplicate accepted outcomes. The normalization belongs with the pre-existing durable await-set contract.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Harden local EventStore and Admin Dapr invocation policies to deny by default and least privilege.
  evidence: The local-development EventStore and Admin configurations predate this story with `defaultAction: allow`, and the Works caller receives EventStore `POST /**`; enumerate the command and stream-read routes every Works recovery path needs before narrowing these policies.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Apply fail-closed page and payload identity validation to the child-completion and cascade recovery stream readers.
  evidence: The pre-existing Story 4.7 readers do not consistently validate stream domain or decoded event identity and silently discard malformed lifecycle or child evidence, allowing false parent-resume decisions or incomplete cascade checkpoints. Their separate paging defect remains owned by DW-86.

## Deferred from: code review of spec-4-8-register-and-reconcile-date-reminders-durably-11.md (2026-09-21)

- Spec-10 Verification Observed is still titled “final reviewed tree” and lists harness 33/33, Integration 526/526, and live 4/4, which can be read as current against spec-11’s 41/534 and no-new-live-credit evidence. Deferred: fix edits another spec. [_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-10.md:212]
- The dated 2026-09-20 spec-10 five-patch close-out File List still omits `spec-4-8-register-and-reconcile-date-reminders-durably-10.md`, unlike earlier dated blocks that include their implementing specs. Deferred: pre-existing spec-10 BH-12 inventory gap, not one of spec-11’s six named patches. [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:711]

## Deferred from: final bmad-build review of Story 4.8 (2026-09-21)

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Preserve pending-date-await index repairability when a decoder upgrade recognizes a previously unknown event.
  evidence: `WorkItemProjectionDispatcher` advances the raw maximum-sequence watermark while folding only decoded events, so its `storedLastSequence >= incomingLastSequence` guard can reject the same replay after a later decoder upgrade would make that event state-affecting.

- source_spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
  summary: Recompute date-reminder delay for each steady-state registration.
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
