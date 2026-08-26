# Deferred Work

### DW-1: Kernel-purity test is transitive-blind

origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
location: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:100-134
reason: `P0_KernelProjectsStayInfrastructureFree` only string-matches kernel project-file text, so a `ProjectReference` to `Hexalith.EventStore.Client` can transitively introduce `Dapr.Client` undetected; the story Dev Notes assign the stronger check to Story 1.2, when `Works.Server` first subclasses the EventStore aggregate base.
status: open

### DW-2: Warnings-as-errors defeats the "scaffolding phase" analyzer intent

origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
location: .editorconfig:56-60; Directory.Build.props:11
reason: `TreatWarningsAsErrors=true` promotes the scaffolding-phase CA1062, CA1822, and CA2007 warnings to build errors without a `WarningsNotAsErrors` escape hatch; this is latent while the scaffold is empty, but domain code will require either `WarningsNotAsErrors` or editorconfig severities aligned with the intended policy.
status: open

### DW-3: Placeholder tests prove little

origin: migrated from legacy ledger ("Deferred from: code review of 1-1-set-up-initial-project-from-starter-template (2026-06-16)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests; tests/Hexalith.Works.PropertyTests
reason: `ScaffoldIntegrationTests` and `ScaffoldPropertyTests` only prove their own assembly loads, with no Aspire boot or FsCheck `Prop.ForAll`, and some forbidden-token governance assertions cannot fire; this was accepted for the scaffold-only story, with real integration and property coverage deferred to later stories.
status: open

### DW-4: Rejection-event sequencing & stream-persistence contract

origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
location: src/Hexalith.Works.Contracts/Events/Rejections/WorkItemCannotReferenceParentFromAnotherTenant.cs; src/Hexalith.Works.Contracts/State/WorkItemState.cs:44; src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:34
reason: `WorkItemCannotReferenceParentFromAnotherTenant` and `WorkItemCannotBeCreatedWithoutObligation` carry no `Sequence` or `AggregateId` and apply as no-ops, while `state is null ? 1 : 2` assumes rejection never advances the stream; a rejection-only stream therefore replays like a never-created aggregate and a later create can re-emit sequence 1, an inherited Story 1.2 contract deferred until EventStore append and replay wiring.
status: open

### DW-5: Self-parent reference accepted

origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
location: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:25
reason: A same-tenant parent whose `WorkItemId` equals the child work item's own id passes the cross-tenant guard and replays as its own parent because `ParentWorkItemReference` has no self-reference or cycle check; this was intentionally deferred with the acyclic, depth, and tree rules to Epic 3.
status: open

### DW-6: `ConversationCorrelationId` is unvalidated

origin: migrated from legacy ledger ("Deferred from: code review of 1-3-reference-sibling-modules-without-copying-data (2026-06-16)"), 2026-08-27
location: src/Hexalith.Works.Contracts/ValueObjects/ConversationCorrelationId.cs
reason: Unlike `PartyId`, `TenantId`, and `WorkItemId`, `ConversationCorrelationId` does not use `AggregateIdentity` and therefore accepts colons, whitespace, non-ASCII text, and unbounded length; tighten it if it begins participating in a composite key or topic where tenant-isolation-safe identity rules matter.
status: open

### DW-7: Persisted parent roll-up never converges to child progress

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:94-123,180-193
reason: Audit F-PROJ-1 (major): the `/project` dispatcher replays and persists only the dispatched aggregate, so a parent's persisted `WorkItemRollUp` and `WhatsNextItem.RolledRemaining` retain each child's spawn-time `InitialEffort` forever; Story 4.7 deliberately trusts only persisted terminal status during live cascade discovery, leaving cross-aggregate convergence dependent on an EventStore projection reconciliation seam or an interim refuse-don't-fake or re-merge decision documented in `docs/eventstore-api-surface-constraints.md`.
status: open

### DW-8: "Mutation-validated cross-tenant negative tests" gate does not exist

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs; tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs:35-39
reason: Audit F-PROJ-2 (major): no mutation harness runs in this repository, five redundant roll-up tenant checks let any single check be deleted without failing existing tests, and the relative property assertion accepts foreign `child-*` ids, although the whats-next single check is killed by an existing test; add a Stryker-style isolation-path gate or per-hop seam tests using `InternalsVisibleTo`.
status: open

### DW-9: `Apply(ReEstimated)` trusts a stored mismatched Unit

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: src/Hexalith.Works.Contracts/State/WorkItemState.cs:171-178
reason: Audit F-DOMAIN-4 (minor): replaying a corrupted or hand-written `ReEstimated` with a different Unit preserves the old Unit in aggregate state while the roll-up projection refuses the event, creating divergent Unit views; command-side validation protects the normal write boundary, so the optional hardening is to mirror the projection's defensive skip.
status: open

### DW-10: Completed by Story 4.7 — Tier-3 fixed aggregate id and live gateway repair.

origin: migrated from legacy ledger ("Deferred from: architecture/domain audit correct-course (2026-07-21)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs; tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs; src/Hexalith.Works.AppHost/Program.cs
reason: Story 4.7 introduced unique smoke-test aggregate ids and EventStore commit `c6b72caa` repaired AppHost port, actor-placement dependency, payload-casing, and Dapr caller-identity wiring; the legacy text declares the command and recovery smoke lanes passing without skips and this item closed, but the authoritative manifest requires migration as open for subsequent sweep reconciliation.
status: open

### DW-11: Works's Dapr pubsub component has zero access-control scoping

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream (2026-07-22)"), 2026-08-27
location: src/Hexalith.Works.AppHost/Program.cs
reason: `AddHexalithEventStore` is called without a `pubSubComponentPath`, generating an unscoped pubsub component; the proper zero-trust fix requires adding `works` to `scopes` and `subscriptionScopes` in the read-only Hexalith.EventStore submodule, while endpoint-level Dapr caller allow-listing remains a compensating control.
status: open

### DW-12: Cascade checkpoint index is a single global cross-tenant key rewritten O(2N) per cascade

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 (2026-07-22)"), 2026-08-27
location: src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:21,49-51,86-117; src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs:177-201
reason: Every incomplete target save rewrites one global cross-tenant checkpoint index, so concurrent cascades can exhaust the three ETag retries, abort saves, trigger Dapr redelivery churn, and let a hot tenant starve another; resolution needs a design choice between transition-only index writes and per-tenant sharding.
status: open

### DW-13: Subscription endpoint drops unbindable envelope bodies into a Dapr poison-retry loop

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 (2026-07-22)"), 2026-08-27
location: src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:33-42
reason: An unbindable `EventStoreDomainEventEnvelope` throws `BadHttpRequestException` before the processor can terminally acknowledge malformed payloads, causing indefinite Dapr redelivery; this mirrors the EventStore SDK endpoint pattern, and the deferred platform-level mitigation is max-redelivery or dead-letter configuration.
status: open

### DW-14: Child-completion await-clearing on terminal parent events is untested

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/StreamReadingChildCompletionAwaitingParentSourceTests.cs
reason: `RebuildAwaitConditions` clears await conditions for resumed, cancelled, expired, completed, and rejected parents through one shared switch arm, but only resumed is tested; add per-type cases if these paths gain distinct behavior.
status: open

### DW-15: Stale-prune exact-boundary is untested

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/CascadeCheckpointIndexRecoveryTests.cs
reason: Recovery tests cover zero age and 25 hours against a 24-hour threshold but not exact equality, so a strict-greater-than to greater-than-or-equal regression could change whether an abandoned checkpoint is pruned.
status: open

### DW-16: Tier-3 cascade smoke-lane skip message does not report which port was absent

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs
reason: `PrerequisitesAvailableAsync` collapses Redis, placement, and scheduler probes into one boolean and emits a static all-ports skip message, obscuring which dependency is unreachable; surface the specific missing port while retaining the existing end-state assertions.
status: open

### DW-17: SDK-misbind characterization test asserts only bare inequality

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs
reason: The SDK misbinding characterization asserts only that the decoded event differs from the source event without proving the casing-related field default, so a future SDK binding fix could make it fail for the wrong reason; assert a named defaulted field.
status: open

### DW-18: AppHost topology test uses presence-only assertions

origin: migrated from legacy ledger ("Deferred from: code review of 4-7-trigger-reactor-translators-from-the-live-event-stream — Round 2 tests (2026-07-23)"), 2026-08-27
location: tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs
reason: Presence-only `HealthCheckAnnotation` and source-substring assertions can pass for comments or broken values despite claiming runtime gating; the live smoke lane currently supplies behavioral proof, but these checks should become value-asserting if that lane is descoped.
status: open
