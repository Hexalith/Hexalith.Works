# Deferred Work

### DW-1: Kernel-purity test is transitive-blind

origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:100-134
reason: `P0_KernelProjectsStayInfrastructureFree` only string-matches kernel project-file text, so a `ProjectReference` to `Hexalith.EventStore.Client` can transitively introduce `Dapr.Client` undetected; the story Dev Notes assign the stronger check to Story 1.2, when `Works.Server` first subclasses the EventStore aggregate base.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-kernel-transitive-dependency-guard
resolution-undo: 5205efa379203d325360cd1365decb0d05f518a3d7b9e85ef9dae1db9076c5d5 2026-08-27 7374617475733a206f70656e

### DW-2: Warnings-as-errors defeats the "scaffolding phase" analyzer intent

origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
location: .editorconfig:56-60; Directory.Build.props:11
reason: `TreatWarningsAsErrors=true` promotes the scaffolding-phase CA1062, CA1822, and CA2007 warnings to build errors without a `WarningsNotAsErrors` escape hatch; this is latent while the scaffold is empty, but domain code will require either `WarningsNotAsErrors` or editorconfig severities aligned with the intended policy.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-analyzer-severity-policy-alignment
resolution-undo: c5cbd6d2aa8b399fd48aeec5810f4a0dc116522225ae7a930158488d49ad588a 2026-08-27 7374617475733a206f70656e

### DW-3: Placeholder tests prove little

origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests; tests/Hexalith.Works.PropertyTests
reason: `ScaffoldIntegrationTests` and `ScaffoldPropertyTests` only prove their own assembly loads, with no Aspire boot or FsCheck `Prop.ForAll`, and some forbidden-token governance assertions cannot fire; this was accepted for the scaffold-only story, with real integration and property coverage deferred to later stories.
status: done 2026-08-27
resolution: already resolved: tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs:22-52 executes generated FsCheck Prop.ForAll cases, and tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs:65-87 starts Aspire, submits a command, and asserts terminal completion.

### DW-4: Rejection-event sequencing & stream-persistence contract

origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
location: src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotReferenceParentFromAnotherTenant.cs; src/Hexalith.Works.Contracts/State/WorkItemState.cs:44; src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:34
reason: `WorkItemCannotReferenceParentFromAnotherTenant` and `WorkItemCannotBeCreatedWithoutObligation` carry no `Sequence` or `AggregateId` and apply as no-ops, while `state is null ? 1 : 2` assumes rejection never advances the stream; a rejection-only stream therefore replays like a never-created aggregate and a later create can re-emit sequence 1, an inherited Story 1.2 contract deferred until EventStore append and replay wiring.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-envelope-canonical-sequencing
resolution-undo: 8ddd93e83ece5733892f7e3c22229ca6bbb11498dba824730dc963a106b8c774 2026-08-27 7374617475733a206f70656e
decision: 2026-08-27 Envelope-canonical sequencing — Document EventStore envelope SequenceNumber as the canonical stream position and Works payload Sequence as the state-changing-event ordinal; add rejection-then-create persistence/replay coverage and reconcile the contradictory architecture text.

### DW-5: Self-parent reference accepted

origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
location: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:25
reason: A same-tenant parent whose `WorkItemId` equals the child work item's own id passes the cross-tenant guard and replays as its own parent because `ParentWorkItemReference` has no self-reference or cycle check; this was intentionally deferred with the acyclic, depth, and tree rules to Epic 3.
status: done 2026-08-27
resolution: already resolved: src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs:46-50 rejects self-parenting with WorkItemTreeCycleRejected, and tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs:150-172 proves rejection without state mutation.

### DW-6: `ConversationCorrelationId` is unvalidated

origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
location: src/Hexalith.Works.Contracts/ValueObjects/ConversationCorrelationId.cs
reason: Unlike `PartyId`, `TenantId`, and `WorkItemId`, `ConversationCorrelationId` does not use `AggregateIdentity` and therefore accepts colons, whitespace, non-ASCII text, and unbounded length; tighten it if it begins participating in a composite key or topic where tenant-isolation-safe identity rules matter.
status: open

### DW-7: Persisted parent roll-up never converges to child progress

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:94-123,180-193
reason: Audit F-PROJ-1 (major): the `/project` dispatcher replays and persists only the dispatched aggregate, so a parent's persisted `WorkItemRollUp` and `WhatsNextItem.RolledRemaining` retain each child's spawn-time `InitialEffort` forever; Story 4.7 deliberately trusts only persisted terminal status during live cascade discovery, leaving cross-aggregate convergence dependent on an EventStore projection reconciliation seam or an interim refuse-don't-fake or re-merge decision documented in `docs/eventstore-api-surface-constraints.md`.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-refuse-stale-persisted-rollups
resolution-undo: ddefab4380f8905628c9b3aeb714f69399d7c2991b6b63f91621f55d32918f0e 2026-08-27 7374617475733a206f70656e
decision: 2026-08-27 Refuse stale roll-ups — Persist or expose rolled remaining as unavailable when child contributions cannot be reconciled, while preserving reliable own effort and terminal status.

### DW-8: "Mutation-validated cross-tenant negative tests" gate does not exist

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs; tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs:35-39
reason: Audit F-PROJ-2 (major): no mutation harness runs in this repository, five redundant roll-up tenant checks let any single check be deleted without failing existing tests, and the relative property assertion accepts foreign `child-*` ids, although the whats-next single check is killed by an existing test; add a Stryker-style isolation-path gate or per-hop seam tests using `InternalsVisibleTo`.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-rollup-tenant-isolation-gate
resolution-undo: 158f7ee039cd50584e23ba256190839eb05a20aea675c2c6641408a138bb8661 2026-08-27 7374617475733a206f70656e

### DW-9: `Apply(ReEstimated)` trusts a stored mismatched Unit

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: src/Hexalith.Works.Contracts/State/WorkItemState.cs:171-178
reason: Audit F-DOMAIN-4 (minor): replaying a corrupted or hand-written `ReEstimated` with a different Unit preserves the old Unit in aggregate state while the roll-up projection refuses the event, creating divergent Unit views; command-side validation protects the normal write boundary, so the optional hardening is to mirror the projection's defensive skip.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-reestimate-replay-unit-hardening
resolution-undo: e631ddbe2597aa647e13c95e6f52b4eb765756181d6ec852e8a9099f23deb037 2026-08-27 7374617475733a206f70656e

### DW-10: Completed by Story 4.7 — Tier-3 fixed aggregate id and live gateway repair.

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs; tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs; src/Hexalith.Works.AppHost/Program.cs
reason: Story 4.7 introduced unique smoke-test aggregate ids and EventStore commit `c6b72caa` repaired AppHost port, actor-placement dependency, payload-casing, and Dapr caller-identity wiring; the legacy text declares the command and recovery smoke lanes passing without skips and this item closed, but the authoritative manifest requires migration as open for subsequent sweep reconciliation.
status: done 2026-08-27
resolution: already resolved: tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs:44-45 uses a per-run GUID-derived aggregate id; tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs:23-78 and src/Hexalith.Works.AppHost/Program.cs:34-75 contain the actor-placement and live AppHost repairs.

### DW-11: Works's Dapr pubsub component has zero access-control scoping

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream (2026-07-22)"), 2026-08-27
location: src/Hexalith.Works.AppHost/Program.cs
reason: `AddHexalithEventStore` is called without a `pubSubComponentPath`, generating an unscoped pubsub component; the proper zero-trust fix requires adding `works` to `scopes` and `subscriptionScopes` in the read-only Hexalith.EventStore submodule, while endpoint-level Dapr caller allow-listing remains a compensating control.
status: done 2026-08-27
resolution: already resolved: commit 7011082 (fix(apphost): use scoped shared pubsub); src/Hexalith.Works.AppHost/Program.cs:19-25,68-76 passes the scoped shared YAML, and EventStore commit ac5b0c47cc25c9bdf014a6e16dfe235ef682d586 plus PubSubTopicIsolationEnforcementTests.cs:101-131 proves works-only publish, subscription, and component scopes.
decision: 2026-08-27 Scope shared component — Update the EventStore shared pub/sub YAML and tests to authorize works only for work.events, advance the submodule pin, and pass that YAML through pubSubComponentPath.

### DW-12: Cascade checkpoint index is a single global cross-tenant key rewritten O(2N) per cascade

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 (2026-07-22)"), 2026-08-27
location: src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:21,49-51,86-117; src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs:177-201
reason: Every incomplete target save rewrites one global cross-tenant checkpoint index, so concurrent cascades can exhaust the three ETag retries, abort saves, trigger Dapr redelivery churn, and let a hot tenant starve another; resolution needs a design choice between transition-only index writes and per-tenant sharding.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-cascade-transition-only-indexing
resolution-undo: e628801ff9ae6ab4db426ccc16253fa1cbd02b0a5fec85d459aa1569eff7a955 2026-08-27 7374617475733a206f70656e
decision: 2026-08-27 Transition-only indexing — Update discovery only when a cascade first becomes incomplete and when it becomes complete, preserving per-target checkpoint durability without repeated global index writes.

### DW-13: Subscription endpoint drops unbindable envelope bodies into a Dapr poison-retry loop

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 (2026-07-22)"), 2026-08-27
location: src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:33-42
reason: An unbindable `EventStoreDomainEventEnvelope` throws `BadHttpRequestException` before the processor can terminally acknowledge malformed payloads, causing indefinite Dapr redelivery; this mirrors the EventStore SDK endpoint pattern, and the deferred platform-level mitigation is max-redelivery or dead-letter configuration.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-dapr-subscription-topology-hardening
resolution-undo: 9d0b70160443abe5d272b224acf45f97375b4700088e4a908274f27e6fad285a 2026-08-27 7374617475733a206f70656e

### DW-14: Child-completion await-clearing on terminal parent events is untested

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/StreamReadingChildCompletionAwaitingParentSourceTests.cs
reason: `RebuildAwaitConditions` clears await conditions for resumed, cancelled, expired, completed, and rejected parents through one shared switch arm, but only resumed is tested; add per-type cases if these paths gain distinct behavior.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-recovery-edge-case-test-hardening
resolution-undo: 0ab05a78f4d20558bd4d462f7ffa46eb6c6a2aabdce71d243420206f93c19db9 2026-08-27 7374617475733a206f70656e

### DW-15: Stale-prune exact-boundary is untested

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs
reason: Recovery tests cover zero age and 25 hours against a 24-hour threshold but not exact equality, so a strict-greater-than to greater-than-or-equal regression could change whether an abandoned checkpoint is pruned.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-recovery-edge-case-test-hardening
resolution-undo: 0ab05a78f4d20558bd4d462f7ffa46eb6c6a2aabdce71d243420206f93c19db9 2026-08-27 7374617475733a206f70656e

### DW-16: Tier-3 cascade smoke-lane skip message does not report which port was absent

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs
reason: `PrerequisitesAvailableAsync` collapses Redis, placement, and scheduler probes into one boolean and emits a static all-ports skip message, obscuring which dependency is unreachable; surface the specific missing port while retaining the existing end-state assertions.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-recovery-edge-case-test-hardening
resolution-undo: 0ab05a78f4d20558bd4d462f7ffa46eb6c6a2aabdce71d243420206f93c19db9 2026-08-27 7374617475733a206f70656e

### DW-17: SDK-misbind characterization test asserts only bare inequality

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs
reason: The SDK misbinding characterization asserts only that the decoded event differs from the source event without proving the casing-related field default, so a future SDK binding fix could make it fail for the wrong reason; assert a named defaulted field.
status: done 2026-08-27
resolution: already resolved: tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs:36-69 now proves the generic SDK binds the real Web JSON payload and asserts decoded.ShouldBe(@event); references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/EventStoreDomainEventProcessor.cs:148 uses the shared Web payload options.

### DW-18: AppHost topology test uses presence-only assertions

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs
reason: Presence-only `HealthCheckAnnotation` and source-substring assertions can pass for comments or broken values despite claiming runtime gating; the live smoke lane currently supplies behavioral proof, but these checks should become value-asserting if that lane is descoped.
status: done 2026-08-27
resolution: resolved by sweep bundle dw-dapr-subscription-topology-hardening
resolution-undo: 9d0b70160443abe5d272b224acf45f97375b4700088e4a908274f27e6fad285a 2026-08-27 7374617475733a206f70656e

### DW-19: The governed kernel project set is a hard-coded four-name list that nothing reconciles against what actually exists under `src/`, so a fifth kernel project would be silently ungoverned.
origin: spec-deferred 476e642905c2
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:10
source_spec: `spec-kernel-transitive-dependency-guard.md`
severity: medium
reason: `KernelDependencyPolicy.GovernedProjects` and `DependencyDirectionTests`' allowlists each name the kernel projects independently, and `GovernedProjectSetIsExact` pins the policy list literally. Nothing compares either list against the `src/` directory listing, which is the same class of blind spot DW-1 was filed for. Pre-existing: the original `P0_KernelProjectsStayInfrastructureFree` carried the same hard-coded shape before this story.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e

### DW-20: The forbidden-family taxonomy exists twice with nothing reconciling the two lists: the direct project-file text scan keeps its own literal string list while the evaluated-closure policy keeps structur
origin: spec-deferred 57d35d9aaaaf
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:126
source_spec: `spec-kernel-transitive-dependency-guard.md`
severity: medium
reason: `ScaffoldGovernanceTests.P0_KernelProjectsStayInfrastructureFree` holds a `forbiddenReferences` array of eight raw strings, and `KernelDependencyPolicy.ForbiddenFamily` independently implements eleven families plus segment and prefix lists. Adding a family to one leaves the other blind, and no test compares them. This is the same drift class as DW-19, but for the forbidden set rather than the governed project set.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e

### DW-21: Two further kernel-purity fitness tests keep their own hand-maintained kernel project lists that did not adopt the centralized governed set, and one of them omits Reactor.
origin: spec-deferred aa6c514b60ba
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:205
source_spec: `spec-kernel-transitive-dependency-guard.md`
severity: medium
reason: `P0_WorkItemKernelRemainsPure` lists four kernel roots and `P0_WorkItemKernelDoesNotLogPayloadsOrPii` lists three (Reactor absent), both as local `string[]` literals rather than `KernelDependencyPolicy.GovernedProjects`. The logging gap is currently covered elsewhere by `RuntimeAdapterGovernanceTests.P0_PureProjectsRemainFreeOfActorClockLoggingNetworkFileAndEventStoreRuntimeApis`, which scans all four projects, so this is drift risk rather than an open hole today. Pre-existing: both lists predate this story.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e

### DW-22: `IsFrameworkLibrary` exempts every `Microsoft.*` and `System.*` name from segment-based classification, so a Microsoft-branded adapter is governed only when an explicit rule names it.
origin: spec-deferred 295933146e55
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:637
source_spec: `spec-kernel-transitive-dependency-guard.md`
severity: medium
reason: The exemption is load-bearing for safe framework names such as `System.Security.Cryptography`, whose `Security` segment would otherwise match `_namedAdapterSegments`. The cost is that names such as `Microsoft.<x>.Mcp`, `Microsoft.<x>.Client`, or `Microsoft.<x>.UI` bypass every segment family; the LLM family already needed hand-written `Microsoft.Extensions.AI` and `Azure.AI` rules for exactly this reason. Narrowing the exemption to known framework roots is a false-positive tradeoff that needs a deliberate call.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e
decision: 2026-08-27 Classify Microsoft segments — Keep the System.* framework exemption, remove the blanket Microsoft.* exemption, classify selected Microsoft adapter segments including MCP, Client, and UI, and add safe near-match plus forbidden-family tests while retaining explicit rules and actionable diagnostics.

### DW-23: Follow-up review still recommended for dw-kernel-transitive-dependency-guard after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-kernel-transitive-dependency-guard.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260826-171625-6b20; this entry preserves the lingering recommendation for a deliberate later review.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-kernel-governance-drift-hardening
resolution-undo: 2f35c4a103befa876bcf2d0a93acc3a9f57ddefaaea1f9baca85f92d07bb23c6 2026-08-28 7374617475733a206f70656e

### DW-24: The 14-arm supported-payload allowlist has no drift guard, so a new work-item event type added to Contracts but omitted from the switch is silently refused with no failing test.
origin: spec-deferred 578a38dc7a24
location: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs
source_spec: `spec-rollup-tenant-isolation-gate.md`
severity: medium
reason: TryGetPayloadIdentity enumerates 14 payload types and falls through to (null, null), which AllowsDelivery turns into an unconditional refusal. SupportedDeliveryPayloads in the unit tests restates the same 14 types by hand; nothing ties the two lists together or to the Contracts assembly. A newly introduced event would be dropped from every roll-up read model with a fully green suite. Pre-existing: the removed EventMatchesDelivery switch had the same shape. A fitness test enumerating IEventPayload implementations in Hexalith.Works.Contracts and asserting each is allowlisted or explicitly excluded would close it.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e

### DW-25: Exposed child order follows HashSet insertion order, so replays that permute delivery order can expose the same children in different order, and the convergence property cannot observe it.
origin: spec-deferred 8d7db14257f2
location: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:238
source_spec: `spec-rollup-tenant-isolation-gate.md`
severity: medium
reason: ToReadModel iterates node.ChildKeys, a HashSet<NodeKey> that is only ever added to, so iteration order is insertion order and therefore delivery order. CollectDiagnostics in the same file sorts ordinal; ChildWorkItemIds does not. Both SameRollUp and the new ExpectedLocalChildren assertion in WorkItemRollUpConvergencePropertyTests sort before comparing, so the permutation replay the property exists to exercise cannot see an order divergence. Pre-existing: ChildKeys was already an unordered set and the previous ToReadModel loop iterated it the same way. Sorting outputChildren by WorkItemId.Value ordinal, plus an unsorted assertion in the property, would close it.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e

### DW-26: ChildContributionCount counts children that passed the output filter, not children that actually contributed effort, so the public contract's name overstates what the number means.
origin: spec-deferred 10a80ad1448a
location: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:256
source_spec: `spec-rollup-tenant-isolation-gate.md`
severity: low
reason: ToReadModel derives both ChildWorkItemIds and ChildContributionCount from the same outputChildren list, which is filtered by AllowsOutput. A tenant-local child with no effort is counted, and under a policy where output and contribution differ the count tracks the wrong hop -- Contribution_boundary_includes_local_effort_and_ignores_foreign_effort_from_permissive_edge asserts a count of 2 while RolledRemaining proves only one child contributed. In the shipped configuration the two filters are identical, so this is a naming/semantics mismatch rather than a leak. Pre-existing: the count came from the same tenant-filtered child list before this change.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e
decision: 2026-08-27 Rename to exposed count — Preserve existing behavior but rename the public positional member to ExposedChildCount or an approved equivalent, then update serialization, consumers, tests, and documentation as a deliberate API change.

### DW-27: Follow-up review still recommended for dw-rollup-tenant-isolation-gate after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-rollup-tenant-isolation-gate.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260826-171625-6b20; this entry preserves the lingering recommendation for a deliberate later review.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-rollup-contract-drift-hardening
resolution-undo: c51f1baf0f42425fa8f7b8e868a503c355e6dc53dba6924acd7be68d032b0809 2026-08-28 7374617475733a206f70656e

### DW-28: A persisted same-unit ReEstimated event with a negative estimate can still throw during aggregate replay.
origin: spec-deferred 92fa2ce13a28
location: src/Hexalith.Works.Contracts/State/WorkItemState.cs:176
source_spec: `spec-reestimate-replay-unit-hardening.md`
severity: high
reason: WorkItemState.Apply(ReEstimated) calls WorkItemEffort.ReEstimate for a matching established unit, and that value object rejects negative estimates. WorkItemRollUpProjection already refuses and diagnoses the same corrupted fact, so this separate pre-existing corruption case can wedge aggregate replay.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-negative-reestimate-replay-guard
resolution-undo: f63efa4ed3687450c195d3e89ce77d5c1aad677a260b68a7045fd7d20934e66a 2026-08-28 7374617475733a206f70656e

### DW-29: Make endpoint result mapping fail retryably for unknown future EventStoreDomainEventProcessingResult values.
origin: spec-deferred c3f0a312e23e
location: src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:54
source_spec: `spec-dapr-subscription-topology-hardening.md`
severity: medium
reason: The current endpoint mapping acknowledges every value except RetryableInProgress with HTTP 200. If the referenced EventStore SDK later adds a processing result, the Works endpoint would silently acknowledge that unrecognized outcome instead of retrying it. This behavior predates this bundle and is not caused by the DLQ/topology changes.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e

### DW-30: The resiliency CRD's statestore target declares retry/timeout/circuitBreaker at the top level instead of under inbound/outbound, so Dapr drops those policies.
origin: spec-deferred eaa2c317b239
location: src/Hexalith.Works.AppHost/DaprComponents/resiliency/resiliency.yaml:56
source_spec: `spec-dapr-subscription-topology-hardening.md`
severity: medium
reason: daprd parses `spec.targets.components.<name>` as inbound/outbound sections. Running daprd 1.18.1 against the committed file reduces the `statestore` target to `{"inbound":{},"outbound":{}}`, discarding `retry: defaultRetry`, `timeout: daprSidecar`, and `circuitBreaker: defaultBreaker`. The `pubsub` target uses the correct shape. This predates the bundle and is outside AC #4, which covers the actor state-store metadata/scopes and the inbound retry target only.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e

### DW-31: Nothing consumes, drains, alerts on, or documents the deadletter.work.events topic.
origin: spec-deferred b7f701b76c90
location: src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:50
source_spec: `spec-dapr-subscription-topology-hardening.md`
severity: low
reason: The dead-letter topic is referenced only by the subscription endpoint and its regression test. The intent forbids subscribing Works to its own DLQ, so bounding redelivery necessarily trades an infinite retry loop for retained-but-unobserved messages. An operator drain/alert path and a runbook entry belong to a separate operational decision, not to this bundle.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e
decision: 2026-08-27 Platform DLQ operator — Add a separate reusable EventStore or operations subscriber with narrowly scoped deadletter.work.events access, redacted metrics and alerts, a durable drain or replay workflow, an operator runbook, and integration coverage while keeping Works unsubscribed.

### DW-32: Follow-up review still recommended for dw-dapr-subscription-topology-hardening after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-dapr-subscription-topology-hardening.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260826-171625-6b20; this entry preserves the lingering recommendation for a deliberate later review.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-dapr-subscription-operations-hardening
resolution-undo: f7fdc870700be500be9dd4703686ba265d04eaeb5703c418c00416b72edf62ad 2026-08-28 7374617475733a206f70656e

### DW-33: External test cancellation is converted into an unavailable-port result by the pre-existing TCP probe.
origin: spec-deferred 9ae6f9653e9f
location: tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:452
source_spec: `spec-recovery-edge-case-test-hardening.md`
severity: medium
reason: `IsPortReachableAsync` catches every `OperationCanceledException` and returns false, so cancellation from `TestContext.Current.CancellationToken` is indistinguishable from the helper's two-second probe timeout and may produce a misleading `Assert.Skip`.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-probe-cancellation-propagation
resolution-undo: a9b2a73b17dc064fa75b9944c43a3245b866510db555c0962a1bd902c3b09e94 2026-08-28 7374617475733a206f70656e

### DW-34: Rejection payloads are now durable persisted bytes but have no entry in the frozen golden-payload corpus; their shape freeze lives only in an in-test signature table.
origin: spec-deferred 72838eb68eb0
location: tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden
source_spec: `spec-envelope-canonical-sequencing.md`
severity: medium
reason: tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/ holds only the 14 success events. RejectionShapeSignatures in EnvelopeCanonicalSequencingTests is a second, uncross-referenced freeze surface for the 9 v1 rejections, so the corpus rule (RR-6/NFR-12) does not cover them.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e

### DW-35: Snapshot-backed rehydration after a persisted rejection is unproven.
origin: spec-deferred 5529e78a3460
location: references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventStreamReader.cs:68-88
source_spec: `spec-envelope-canonical-sequencing.md`
severity: medium
reason: EventStreamReader reads the tail from snapshot.SequenceNumber + 1, and returns the snapshot alone when it already sits at the current sequence. A snapshot taken after a rejection envelope therefore folds the no-op away, and no test drives that path. The spec's I/O matrix covers only full replay.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e

### DW-36: Several older documentation paragraphs still say the v1 catalog "stays 36" while the fitness-asserted count is 37.
origin: spec-deferred c4c1db7ffe36
location: docs/lifecycle-transition-matrix.md:198
source_spec: `spec-envelope-canonical-sequencing.md`
severity: medium
reason: docs/lifecycle-transition-matrix.md:198, docs/whats-next-projection.md:120 and docs/boundary-decision-record.md:109/122/134/151 say 36; ScaffoldGovernanceTests asserts polymorphicCatalogCount.ShouldBe(37) and docs/eventstore-api-surface-constraints.md:112 says 37. Pre-existing staleness, surfaced while reconciling sequencing terminology in the same files.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-v1-catalog-count-reconciliation
resolution-undo: d14ab66c4bb7ff2377be30a1297960dd1e5973c6cae6066ca9e75a455bf8debe 2026-08-28 7374617475733a206f70656e

### DW-37: Mid-stream and repeated-rejection envelope/payload divergence is unproven at the persistence layer.
origin: spec-deferred efac8f417df1
location: tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs
source_spec: `spec-envelope-canonical-sequencing.md`
severity: medium
reason: EnvelopeCanonicalSequencingTests covers only pre-create rejections (the spec's I/O matrix rows). create(env 1) -> rejection(env 2) -> assign(env 3, payload ordinal 2), and two rejections before a create, are the cases where an off-by-one between the two counters would first show up.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e

### DW-38: The golden-payload corpus is camelCase while the bytes EventStore actually persists are PascalCase, yet both are documented as "the EventStore-persisted form".
origin: spec-deferred 6b155a733168
location: tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs:14-16
source_spec: `spec-envelope-canonical-sequencing.md`
severity: medium
reason: EventPersister.cs:71 serializes with JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()) -- no options, so PascalCase. SchemaEvolutionGoldenCorpusTests and WorkItemProjectionDispatcher's <remarks> both call the JsonSerializerDefaults.Web (camelCase) samples the persisted form; the 14 Golden/*.json files start "aggregateId". Decoding survives only because Web options are case-insensitive, so a naming-policy change upstream would not turn the corpus red. Surfaced by the first byte-level persisted-form assertion, which this change added.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e
decision: 2026-08-27 Add exact persisted corpus — Preserve current camelCase files as compatibility fixtures, correct their documentation, and add a separate byte-exact PascalCase EventPersister corpus and test tied to shared writer behavior.
decision: 2026-08-27 Add exact persisted corpus — Preserve current camelCase files as compatibility fixtures, correct their documentation, and add a separate byte-exact PascalCase EventPersister corpus and test tied to shared writer behavior.

### DW-39: No executable test proves that a rejection DomainResult routed through the EventStore command pipeline reaches persistence; only source-text characterization covers it.
origin: spec-deferred 4aa7d4178162
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs
source_spec: `spec-envelope-canonical-sequencing.md`
severity: medium
reason: EnvelopeCanonicalSequencingTests calls EventPersister.PersistEventsAsync itself, presupposing the routing decision that AggregateActor.ProcessCommandCoreAsync actually makes. No Works test instantiates AggregateActor, and the three Aspire lanes submit only accepted commands. The always-on guard is now mutation-validated across the whole command path, but it is still a string match over a pinned submodule, not execution.
status: done 2026-08-28
resolution: already resolved: references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.Tests/Actors/AggregateActorDomainResultTests.cs:535-550 executes AggregateActor.ProcessCommandAsync with DomainResult.Rejection and asserts the rejection envelope is written; commit 536a269438ef4edaff1fd83b73bae36c88e7cc23 introduced this executable proof.

### DW-40: The claim tests point at a Story 4.5 Aspire lane for live ETag conflict/retry coverage that does not exist.
origin: spec-deferred 96069162f44f
location: tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs:33-35
source_spec: `spec-envelope-canonical-sequencing.md`
severity: medium
reason: WorkItemClaimConcurrencyTests' class XML doc says the live ETag-backed save / conflict-retry / retry-exhaustion path "is exercised under the Aspire runtime in Story 4.5". WorksCommandPipelineSmokeTests (the Story 4.5 lane) issues no ClaimWorkItem at all; the only runtime claims are single sequential submissions in WorksReminderRecoveryPipelineSmokeTests:185 and WorksCascadeRecoveryPipelineSmokeTests:165,194. Nothing anywhere issues two competing claims. Pre-existing pointer, re-asserted by this change's rewording.
status: open

### DW-41: Three ScaffoldGovernanceTests fitness method names still end "AndCatalogStays36" while the assertion in the same methods is polymorphicCatalogCount.ShouldBe(37).
origin: spec-deferred 3ac7ddef56e1
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:387
source_spec: `spec-envelope-canonical-sequencing.md`
severity: low
reason: ScaffoldGovernanceTests.cs:387, :455 and :524 declare ...AndCatalogStays36; the comment directly above the third says the wire surface "stays frozen at 37". Renaming is not free: roughly ten story-file and test-summary references quote those method names verbatim, so the rename and the reference sweep must land together. Distinct surface from the documentation-paragraph instance already tracked.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-v1-catalog-count-reconciliation
resolution-undo: d14ab66c4bb7ff2377be30a1297960dd1e5973c6cae6066ca9e75a455bf8debe 2026-08-28 7374617475733a206f70656e

### DW-42: Follow-up review still recommended for dw-envelope-canonical-sequencing after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-envelope-canonical-sequencing.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260827-130630-f73f; this entry preserves the lingering recommendation for a deliberate later review.
status: done 2026-08-28
resolution: resolved by sweep bundle dw-envelope-persistence-proof-hardening
resolution-undo: 984d136cda1168d7550a610c0ad1769f6fd7de1e1a60a6905890a5594a1324b8 2026-08-28 7374617475733a206f70656e

### DW-43: A delayed older full-replay request can overwrite newer persisted work-item projection state.
origin: spec-deferred 1362c100f6fb
location: src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:152
source_spec: `spec-refuse-stale-persisted-rollups.md`
severity: medium
reason: WorkItemProjectionDispatcher writes the tenant index and per-item roll-up without comparing the incoming LatestAcceptedSourceSequence to the stored item, so an older request completing later can replace newer status, effort, structure, and availability state. This behavior predates the stale-roll-up refusal change and needs a focused ordering/concurrency design.
status: open

### DW-44: Per-item roll-up persistence does not use the documented optimistic-concurrency write policy.
origin: spec-deferred 6baca031f280
location: src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:191
source_spec: `spec-refuse-stale-persisted-rollups.md`
severity: medium
reason: PersistRollUpAsync calls IReadModelStore.SaveAsync directly, while docs/eventstore-api-surface-constraints.md describes per-item persistence as ReadModelWritePolicy/ETag guarded. The mismatch predates this bundle and should be reconciled separately without expanding the adapter refusal patch.
status: open

### DW-45: Roll-ups and tenant-index entries persisted before the refusal change keep their stale child-dependent totals until their own aggregate is dispatched again.
origin: spec-deferred ced4955ae997
location: src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:123
source_spec: `spec-refuse-stale-persisted-rollups.md`
severity: medium
reason: ToBoundarySafeRollUp is applied only on the write path of a dispatch, and the dispatcher is the only writer of WorksReadModelKeys.RollUpKey and the what's-next tenant index. A child-only dispatch never rewrites the parent's keys, WhatsNextQueryHandler returns stored values verbatim with no read-side sanitization, WorksReadModelKeys carries no schema/version token, and no startup replay, rebuild, or invalidation path exists. A parent that appends no further events of its own therefore keeps serving its spawn-time total indefinitely. Every adapter test starts from a fresh InMemoryReadModelStore, so no test observes a pre-change document. Closing this needs a re-projection/backfill or read-side guard, which the approved adapter-boundary approach ("whenever the dispatched item has child contributions") does not cover.
status: open
decision: 2026-08-27 Version and backfill — Add an internal read-model schema version and an operator-triggered EventStore projection rebuild/backfill that rewrites tenant-index and per-item documents through the boundary sanitizer, with seeded pre-change migration tests.
decision: 2026-08-27 Version and backfill — Add an internal read-model schema version and an operator-triggered EventStore projection rebuild/backfill that rewrites tenant-index and per-item documents through the boundary sanitizer, with seeded pre-change migration tests.

### DW-46: A parent whose children were attached by a parented create still publishes a rolled total that silently omits them.
origin: spec-deferred 8b641af15c5f
location: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:58
source_spec: `spec-refuse-stale-persisted-rollups.md`
severity: medium
reason: CreateWorkItem accepts a Parent, and WorkItemRollUpProjection.Project adds the parent->child edge from WorkItemCreated.Parent on the child's stream. That create emits nothing on the parent's stream, so the parent's own dispatch sees ChildContributionCount == 0 and no ChildSpawned event name, ToBoundarySafeRollUp does not fire, and the parent is persisted as a leaf with an available rolled total that excludes those children. This predates the refusal change; detecting it from a single dispatch would require a cross-aggregate store read or merge protocol, which the intent's Block If excludes.
status: open
decision: 2026-08-27 Platform reconciliation seam — Extend the EventStore projection/rebuild surface with relationship-aware cross-aggregate reconciliation, then persist a parent model that is unavailable or converged based on authoritative child evidence.
decision: 2026-08-27 Platform reconciliation seam — Extend the EventStore projection/rebuild surface with relationship-aware cross-aggregate reconciliation, then persist a parent model that is unavailable or converged based on authoritative child evidence.

### DW-47: Follow-up review still recommended for dw-cascade-transition-only-indexing after the damping cap was spent
origin: review-budget-followup
location: n/a
source_spec: `spec-cascade-transition-only-indexing.md`
severity: low
reason: The follow-up-review damping cap (limits.max_followup_reviews = 1) was spent with the story finalized (status: done, verify green) while the review pass still recommended an independent follow-up. The work was committed by bmad-loop run 20260827-214141-f7db; this entry preserves the lingering recommendation for a deliberate later review.
status: done 2026-08-28
resolution: closed by human decision: Close after three completed review passes, green verification, and two consecutive passes without production control-flow changes.
decision: 2026-08-28 Accept review saturation — Close after three completed review passes, green verification, and two consecutive passes without production control-flow changes.

### DW-48: Exact dependency-direction allowlists inspect literal project files but not ProjectReference items introduced by imported MSBuild props or targets.
origin: spec-deferred f984e193f381
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:277
source_spec: `spec-kernel-governance-drift-hardening.md`
severity: medium
reason: `DependencyDirectionTests.ProjectReferenceNames` loads only the owning `.csproj`. A safe-family imported reference such as Server to Projections would not violate the forbidden-family classifier and could bypass the exact literal allowlist. This limitation predates the current centralized governed-set work.
status: open

### DW-49: Evaluated dependency artifact freshness does not cover the complete custom MSBuild import closure.
origin: spec-deferred 94642d4aefa7
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:811
source_spec: `spec-kernel-governance-drift-hardening.md`
severity: medium
reason: `SharedRestoreInputs` checks the known root restore inputs, but a dependency-affecting custom imported props or targets file could change without making an existing `project.assets.json` fail the timestamp gate. The prior transitive-dependency implementation already carried this limitation.
status: open

### DW-50: Exact ProjectReference allowlists normalize by project filename rather than canonical evaluated path identity.
origin: spec-deferred ef210fd53a67
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:298
source_spec: `spec-kernel-governance-drift-hardening.md`
severity: medium
reason: A reference to an unrelated project with an allowlisted `.csproj` basename can normalize to the permitted name. Closing this safely requires evaluated path identity and is a pre-existing limitation of the exact direction test, not a defect introduced by this bundle.
status: open

### DW-51: The Hexalith-source consumption gate still reads PackageReference and PackageVersion item specifications raw, outside the shared fail-closed discovery.
origin: spec-deferred 36cdab656fd8
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:300
source_spec: `spec-kernel-governance-drift-hardening.md`
severity: low
reason: `DependencyDirectionTests.PackageReferenceNames` matches item names case-sensitively, takes `Include` or `Update` verbatim, and never splits semicolon-delimited item lists, so `Include="Something;Hexalith.Foo"` evades the "Hexalith libraries must come from sibling source" rule. The governed-set and forbidden-family paths this story centralized do not consume this helper, and the rule it serves is outside the kernel-purity scope this bundle reconciled.
status: open

### DW-52: ExposedChildCount remains independently constructible from ChildWorkItemIds and can represent an inconsistent read model.
origin: spec-deferred 124b7489f14f
location: src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs:27
source_spec: `spec-rollup-contract-drift-hardening.md`
severity: medium
reason: WorkItemRollUp already accepted the count as an independent positional integer before this bundle. The approved DW-26 decision preserves that behavior while renaming it, so deriving or validating the value would be a separate contract change.
status: open
decision: 2026-08-28 Derive exposed count — Remove the independent count input, compute ExposedChildCount from ChildWorkItemIds, preserve the intended exposedChildCount wire output, and update constructors, serialization compatibility tests, consumers, and documentation.

### DW-53: The Contracts-derived gate binds payload admission but not roll-up effect, so a new event registered only to green the gate is accepted, consumes its sequence slot, and advances the watermark with no
origin: spec-deferred 96d65c591dec
location: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:137
source_spec: `spec-rollup-contract-drift-hardening.md`
severity: medium
reason: The fitness test compares Contracts payloads with WorkItemRollUpTenantIsolation's identity registry only. WorkItemRollUpProjection.ApplyPayload holds a second, ungated switch. Adding any concrete non-rejection Contracts payload turns the gate red, and the only way to green it is a registry entry -- which converts a fail-closed refusal into a silent no-op acceptance that still advances LatestAcceptedSourceSequence. The spec's Approach scopes the allowlist to the identity registry, so binding ApplyPayload is a separate change.
status: open

### DW-54: WhatsNextQueueProjection keeps a structurally identical hand-maintained payload allowlist over the same delivery envelope, with no Contracts-derived gate, so the drift this story closed for roll-up st
origin: spec-deferred 05c45a10513b
location: src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs:91
source_spec: `spec-rollup-contract-drift-hardening.md`
severity: medium
reason: WhatsNextQueueProjection.EventMatchesDelivery enumerates the same 14 payload types with a fail-closed `_ => false` fallthrough and is driven in production by WorkItemProjectionDispatcher.DispatchAsync. None of its 37 unit tests enumerate payload types, and no architecture test ties its accepted set to Contracts. A 15th non-rejection event would be silently dropped from the tenant what's-next index with a green suite. The spec's Never clause forbids modifying unrelated projections, so this is out of scope for this bundle.
status: open

### DW-55: The dead-letter capture parser's fixtures are hand-written literals rather than derived from the publisher type, so a rename on the producing side breaks capture in production while both parser tests
origin: spec-deferred 3bd2f3fc99d1
location: references/Hexalith.EventStore/src/Hexalith.EventStore.Operations/Capture/DeadLetterEnvelopeParser.cs
source_spec: `spec-dapr-subscription-operations-hardening.md`
severity: high
reason: DeadLetterEnvelopeParser requires data.messageId, tenantId, domain, aggregateId, correlationId and one of the eventTypeName/eventName/eventType aliases; a missing field collapses the identity to "unidentified-<hash>" and permanently disqualifies the item from replay. Every fixture (DeadLetterEnvelopeParserTests, DeadLetterCaptureBodyTests) is a UTF-8 literal typed into the test file, and nothing in either repository builds one by serializing the real producer envelope. This is not hypothetical: the first pass of this story shipped exactly that defect (the parser accepted only eventName/eventType while the publisher emits eventTypeName) and human review, not a test, caught it. Caused by this change but not trivially fixable: Hexalith.EventStore.Operations.Tests would need a reference to Hexalith.EventStore.Server to serialize EventEnvelope, which is a deliberate dependency-surface decision for a shared submodule rather than an in-pass patch.
status: open
decision: 2026-08-28 Direct Server test reference — Add an Operations.Tests-to-EventStore.Server ProjectReference, serialize the real Server.Events.EventEnvelope into a shared structured-CloudEvent test helper, and use it across parser and capture endpoint tests to detect publisher-shape drift.
decision: 2026-08-28 Direct Server test reference — Add an Operations.Tests-to-EventStore.Server ProjectReference, serialize the real Server.Events.EventEnvelope into a shared structured-CloudEvent test helper, and use it across parser and capture endpoint tests to detect publisher-shape drift.

### DW-56: Completed-marker failure can still be acknowledged as processed

origin: migrated from legacy ledger ("Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-08-28)"), 2026-08-28
location: src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:119
reason: Event processing can return `Processed` after `MarkCompletedAsync` fails, leaving no durable deduplication marker for a later duplicate delivery. This behavior predates Story 4.8.
status: open

### DW-57: Event processor does not reject non-work domains

origin: migrated from legacy ledger ("Deferred from: code review of 4-8-register-and-reconcile-date-reminders-durably.md (2026-08-28)"), 2026-08-28
location: src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:227
reason: Event processing validates envelope metadata and payload identity but does not reject an envelope whose `Domain` is not `work`. This behavior predates Story 4.8.
status: open

### DW-58: Sibling smoke-test prerequisite probes still collapse caller-requested cancellation into an unavailable result.
origin: spec-deferred dc9e48616d2d
location: tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs:179; tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:387
source_spec: `spec-probe-cancellation-propagation.md`
severity: medium
reason: `WorksCommandPipelineSmokeTests.IsPortReachableAsync` and `WorksReminderRecoveryPipelineSmokeTests.IsPortReachableAsync` catch every `OperationCanceledException` and return `false`. Both implementations pre-date this bundle and are outside DW-33's cited cascade-recovery probe.
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
