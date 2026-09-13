# Reconciliation — Live Works / EventStore Reality

**Verdict: FAIL — update required before the spine can be finalized.**

The target direction is broadly compatible with the Hexalith baseline, but the spine does not reliably
distinguish settled product intent, target architecture, and implemented repository reality. Several unbuilt
target contracts are marked `[ADOPTED]`; some directly contradict the lifecycle authority and architecture
fitness tests that the same spine declares binding. The migration/readiness section catches part of this drift,
but not enough for a builder to know which rules can be consumed today.

## Evidence baseline

This review used the working tree as found and made no source, dependency, or submodule change.

| Evidence | Observed reality |
| --- | --- |
| Works repository | `HEAD 0cbc7c4`; branch `main`; user changes already present |
| EventStore root gitlink | `6b0247acc0b3ef60eb00c0f9ac9cbd367f85da20` |
| EventStore checked-out worktree | `a568af4ec963d017ed4343a518d4fbd7d444ad84` (`+` relative to the root gitlink); the two commits after `6b0247ac` are documentation/test/submodule changes, not the APIs assessed below |
| Builds root gitlink | `a32cb422749352cce8dec948aa3e78c8f00eb4cf` |
| Builds checked-out worktree | `fa6472788c14301c2c91c6beb85a8215aae1022c` (`+` relative to the root gitlink) |
| SDK / Aspire pins | .NET SDK `10.0.400`; Aspire AppHost/core `13.5.3` |
| Central packages | CommunityToolkit Aspire Dapr `13.5.0-preview.1.260825-0345`; Dapr .NET `1.18.7`; EventStore packages `3.103.0`; PolymorphicSerializations `1.19.2`; Commons `2.30.0`; xUnit v3 `4.0.0` |
| Transitional Dapr runtime images | `1.18.3` in `DaprSelfHostedMtls.cs` |

## Findings

### RR-01 — Critical — The lifecycle authority and the `[ADOPTED]` rules cannot both be true

AD-17 says `docs/lifecycle-transition-matrix.md` is authoritative and changes atomically with lifecycle code and
tests. AD-07 and AD-14 nevertheless present new behavior as `[ADOPTED]`:

- `HandoffWorkItem` / `WorkItemHandedOff` changes an active binding without changing `InProgress` or `Suspended`.
- `CorrectProgress` / `ProgressCorrected` may emit `WorkItemReopened`.
- `WorkItemCompleted` carries `CompletionKind`.
- a child without an explicit Unit inherits its parent's Unit at first estimate.

The live authority says the opposite or has no such act:

- `docs/lifecycle-transition-matrix.md` lines 228–235 explicitly keep active `Assign` and `Queue` rejected and say
  later stories must not add a local active-handoff path.
- `WorkItemHandoffChainContractFlowTests.cs` lines 13–20 say handoff is ordered `WorkItemAssigned` evidence with
  **no dedicated hand-off command**.
- `ScaffoldGovernanceTests.cs` lines 353–417 fitness-enforce the absence of `WorkItemHandedOff`-style types and
  freeze the catalog at 40 types.
- `WorkItemCompleted.cs` has only `(AggregateId, Sequence, TenantId, WorkItemId)`.
- no `HandoffWorkItem`, `WorkItemHandedOff`, `CorrectProgress`, `ProgressCorrected`, `WorkItemReopened`, or
  `CompletionKind` production type exists.
- `SpawnChild` and `ChildSpawned` carry the optional child `InitialEffort` unchanged; current handling does not
  derive a missing Unit from parent state.

These are legitimate later PRD/memlog decisions, but they are **adopted target changes**, not current lifecycle
truth. A builder following AD-17 would preserve the existing table; a builder following AD-07/14 would break its
fitness gate.

**Required spine change:** distinguish `adopted-product-target` from `adopted-current` (or add explicit
`[TARGET]` markers). Bind one migration gate that updates the catalog, golden corpus, lifecycle document,
aggregate/state/projections, and conflicting fitness tests atomically before any of these rules are considered
implemented. Do not describe the current lifecycle matrix as authoritative without exposing this planned
supersession.

### RR-02 — Critical — AD-06, AD-15, and AD-16 describe an unbuilt v3 persistence system as adopted reality

AD-06 is marked `[ADOPTED]` and binds a `works:v3:tenant:<base64url-tenant>:rollup:<ancestor>` document with
topology watermarks, per-descendant contribution slots, independent sequence spaces, quarantine, and
EventStore-owned CAS merge. AD-15 is also marked `[ADOPTED]` and requires disjoint encoded v3 tenant/control
namespaces. AD-16 is tagged both `[ADOPTED]` and `[ASSUMPTION]` and requires a tenant/family rebuild epoch that
every writer presents.

None is the live persistence contract:

- `WorksReadModelKeys.cs` lines 13–105 defines schema version **2** and raw-id keys under
  `projection:works:*`, including the collision-prone singleton
  `projection:works:pending-date-await:tenants`; the code mitigates that collision by reserving tenant id
  `tenants` rather than by using the v3 namespace.
- `WorkItemProjectionDispatcher` writes one whole `WorkItemRollUp` per item via `ReadModelWritePolicy`; there is
  no descendant-slot DTO, topology watermark, v3 merge, or registry adapter.
- `WorkItemSharedRebuildManifestBuilder.cs` lines 127–156 emits a schema-v2 `LastWrite` manifest and deletes
  legacy keys. It does not require an active epoch from every writer.
- `docs/eventstore-api-surface-constraints.md` lines 137–152 explicitly says the current shared rebuild depends
  on external quiescence and cannot itself exclude a concurrent ordinary `/project` writer.
- current topology is inferred from `ChildSpawned` and `WorkItemCreated.Parent`; there is no Work-Tree Registry
  aggregate or `IWorkTreeTopologyReader`.

AD-21 and AD-22 correctly carry `[ASSUMPTION]`, but the prerequisite persistence decisions they depend on do
not. This creates the false impression that registry/fan-out builders can consume v3 infrastructure now.

**Required spine change:** mark AD-06's descendant-slot form, AD-15's v3 namespace, and AD-16's epoch as target
assumptions until producer contracts exist. Preserve the current schema-v2 model as transitional seed and name
a gated migration: new v3 DTO/key contract, EventStore merge API, dual-read/backfill/cutover, current-writer
fence, and retirement of the reserved-tenant workaround. Remove the contradictory `[ADOPTED] [ASSUMPTION]`
classification from AD-16.

### RR-03 — Critical — The adopted reminder/deadline rule is not the runtime contract

AD-11 is `[ADOPTED]` even though the memlog records the typed reminder design under `[ASSUMPTION]`. It names
`WorkItemReminderActor`, actor id `wrk_<hash(tenant,item)>`, and a typed `PendingWorkIntent` supporting both
`DateResume` and `Expiry`. AD-12 is `[ADOPTED]` and says only an AD-25 command carrying the current persisted
schedule witness may expire an item. AD-25 itself is correctly `[ASSUMPTION]`.

The live code has a narrower, incompatible surface:

- the actor is `DateReminderActor`, with ids beginning `work-date-resume-`;
- state is `DateReminderRegistration`, not `PendingWorkIntent`, and supports date resume only;
- `ExpireWorkItem` carries only `TenantId` and `WorkItemId`;
- `WorkItemAggregate.Handle(ExpireWorkItem, ...)` accepts any non-terminal item through the lifecycle matrix;
  there is no persisted effective expiry instant or `ScheduleToken` check.

An `[ADOPTED]` rule cannot safely depend on the unconfirmed AD-25 witness and simultaneously contradict the
current durable command shape.

**Required spine change:** restore `[ASSUMPTION]` / target status on AD-11 and AD-12, or explicitly split each
into “adopted clock-free kernel” and “target typed-intent/witness protocol.” Gate the latter on additive-reader
rollout, the new command/event fields, actor-state migration, separate DateResume/Expiry recovery tests, and
removal of the current date-only actor contract.

### RR-04 — High — AD-27/AD-29 are target guarantees while live recovery still violates them

The spine's Recovery Correctness readiness gate accurately says two stream cursors must be fixed. The exact live
violations are still present:

- `StreamReadingCascadeDescendantSource.cs` lines 81–86 advances the exclusive `FromSequence` to
  `LastSequenceReturned + 1`.
- `StreamReadingChildCompletionAwaitingParentSource.cs` lines 152–160 does the same.
- `PendingDateAwaitStreamReader.cs` lines 82–96 implements the correct rule by reusing
  `LastSequenceReturned` and is the model the other readers must share.

The rest of AD-27 is also not current:

- `ReminderReconciliationService` is startup-only, stops after bounded attempts, logs, and returns.
- `CascadeRecoveryService` runs one startup pass, catches failure, logs, and returns.
- neither contributes unresolved-work state to readiness or runs periodic durable retry.
- a parked projection is skipped by reminder discovery; the recent Works fix prevents one poisoned aggregate
  from killing the entire scan, but it does not implement the target durable quarantine/disposition/readiness
  contract.
- current projection parking is a per-aggregate `projection:works:parked:*` record, not AD-29's one
  tenant-scoped quarantine path shared by projection, subscription, rebuild, and recovery.

**Required spine change:** keep the cursor gate, and also label AD-27/29 as target (or clearly state “binding but
unimplemented”). Expand the readiness gate to require the shared strict page validator, periodic retry,
durable unresolved records, degraded readiness/metrics/alerts, authenticated unpark/replay/disposition, and
cross-path quarantine convergence. This is implementation work, not a reason to weaken the invariant.

### RR-05 — High — R9 is not “available” under the repository's domain-centric boundary

The Hexalith baseline lines 121–134 requires domain modules to contain domain code only, use the EventStore
two-line host and standard handler seams, and not ship AppHost, ServiceDefaults, Dapr wiring, projection/query
actors, health, telemetry, or event-subscription plumbing.

The current Works repository intentionally carries transitional violations:

- `Hexalith.Works.slnx` includes `Hexalith.Works.AppHost` and `Hexalith.Works.ServiceDefaults`.
- `WorksHost.Build` performs extensive custom registration for read-model storage, subscriptions, Dapr actors,
  reminder/cascade recovery, and a bespoke `/project` mapping.
- `Program.cs` is short, but the composition is not the canonical two-line host promised by R9.
- the Works AppHost owns local Dapr Sentry, placement, Scheduler, Redis component/ACL/policy wiring, auth
  fallbacks, and operations composition.
- the AppHost project still references `Hexalith.Works.ServiceDefaults`, even though the executable correctly
  consumes EventStore ServiceDefaults transitively.

AD-20 calls these transitional, which is directionally correct, but row R9 says “Available” with proof
“canonical two-line SDK composition.” That availability is false at the target boundary, and Structural Seed
omits most of the live transitional surface.

The checked EventStore already provides canonical `/project`, `/project/v2`, named rebuild, shared rebuild,
query, read-model, telemetry, health, and subscription seams. Its own component inventory says a domain should
implement handler interfaces rather than hand-map canonical endpoints. Domain-specific folds/translations stay
in Works; reusable execution and delivery plumbing belongs in EventStore/Platform.

**Required spine change:** mark R9 `Partial / transitional`, list the current AppHost, ServiceDefaults, bespoke
`/project`, Dapr actor, and recovery-host exceptions in Structural Seed, and require separate removal evidence
for each. R2/R3/R4/R6/R7/R9 must converge before the baseline can be claimed, not merely before deleting two
projects.

### RR-06 — High — EventStore and build provenance is stale and non-reproducible

`docs/eventstore-api-surface-constraints.md` lines 28–30 calls EventStore commit `c6173920` the current pin, and
lines 154–158 says Aspire was aligned to `13.4.6`. The tracked Works gitlink is now `6b0247ac`, the checked-out
EventStore is `a568af4e`, and Works is on Aspire `13.5.3`. The checked EventStore also now exposes
`IIdempotencyIntentAdapter` and trusted canonical intent APIs added after `c6173920`, directly relevant to
AD-26. The old document may still be correct for narrow serialization/sequence behavior, but it is not a
current whole-surface characterization.

The root also records Builds at `a32cb422`, while the checked-out Builds tree is `fa647278`; local evaluation
imports the checked-out `Props/Directory.Packages.props`. The spine's package-version rows match that working
file, but do not record this gitlink/worktree split.

There is also an unresolved AD-26 fit gap in the actual gateway:

- `SubmitCommandRequest` supports `IdempotencyKey`, but `EventStoreGatewayWorkCommandSubmitter` omits it and
  only copies Works `CausationId` into `MessageId`.
- EventStore's registered replay-retention tiers are exactly 24 hours (`Mutation`) or seven years (`Commit`),
  while AD-26 assumes terminal records remain at least 90 days. The current public contract has no 90-day tier.

**Required spine change:** record the tracked gitlinks used for reproducible conclusions, separately disclose
the dirty checked-out revisions without adopting them, and refresh the API-surface document against the tracked
EventStore pin. Change R11 availability to “gateway + trusted intent API available; Works adapter, effect-key
contract, and retention fit absent.” Do not alter the user's submodule checkouts as part of this architecture
update.

### RR-07 — Medium — The ULID convention is stronger than the implemented Works boundary

AD-02 and Consistency Conventions state that aggregate ids are ULIDs assigned through Hexalith.Commons. Works
does not own a command-creation edge, `WorkItemId` accepts any non-whitespace identity accepted by
`AggregateIdentity`, and the executable tests routinely use values such as `work-001`. The pure aggregate does
correctly avoid local/random ID generation.

**Required spine change:** retain “handlers never generate ids” as adopted current behavior, but make strict
ULID issuance a target responsibility of the authenticated command-creation edge, with an owner and boundary
proof. If opaque non-whitespace ids remain supported for compatibility, state that explicitly rather than
claiming a repository-wide format invariant that current contracts do not enforce.

## Confirmed, non-contradictory parts

- The event-sourced/CQRS kernel and outer-to-inner project direction are real: Server, Projections, and Reactor
  depend directly on Contracts; the runnable executable is the EventStore adapter edge.
- EventStore persists success/rejection envelopes with canonical envelope positions; Works payload `Sequence`
  is a separate state-change ordinal.
- The current “what's next” ordering and query authorization are implemented as pure Works policies behind the
  EventStore query seam.
- The reminder stream reader confirms `FromSequence` is exclusive; the spine's no-`+1` rule is correct.
- The version table matches the checked build metadata for .NET, Aspire, Dapr packages/runtime, EventStore
  package version, PolymorphicSerializations, Commons, and xUnit. The reproducibility problem is revision
  provenance, not those literal values.
- The spine correctly prevents removal of the transitional Works host before Platform parity, and correctly
  treats registry, live fan-out, typed expiry, transport identity, periodic recovery, and production security as
  gated work. The tags and availability labels must be brought into line with that gate.

## Minimum reconciliation before finalize

1. Add a visible current/target classification and correct the false `[ADOPTED]` labels identified above.
2. Resolve the AD-17 conflict by treating handoff/correction/completion-kind/unit inheritance as an atomic target
   lifecycle migration, not current behavior.
3. Mark v3 roll-up/namespaces/epoch and typed reminder/expiry as unbuilt producer/consumer migrations.
4. Change R9 and R11 from undifferentiated “Available” to truthful partial states and enumerate the transitional
   boundary exceptions.
5. Refresh EventStore/build provenance against tracked gitlinks and keep the user's advanced submodule worktrees
   untouched.
6. Preserve and expand the recovery readiness gate; do not finalize implementation readiness while the two
   cursor defects and startup-only recovery remain.
