# Changelog

All notable changes to Hexalith.Works will be documented in this file.

## Unreleased

### Added

- Initial Works scaffold, build configuration, and architecture fitness tests (Epic 1).
- Tenant-scoped work item kernel: event-sourced `WorkItemAggregate` owning obligation, executor
  binding (`ExecutorBinding`/`PartyId`), schedule, status, and parent/child references; sibling
  modules referenced by ID rather than copied (Epic 1).
- Boundary ports and decision record: `IExpectationResolver` with a no-LLM `LiteralExpectationResolver`,
  the `IExecutorRouter` seam, and `ExpectationReference` (Epic 1).
- Work item lifecycle state machine with a single pure transition table (`WorkItemLifecycle`) mirrored
  1:1 by `docs/lifecycle-transition-matrix.md`: assign, queue, claim, suspend, resume, complete,
  cancel, reject, expire (Epic 2).
- Unit-tagged effort burn-down — `ReportProgress`, `ReEstimate`, `RescheduleWorkItem` — with a
  per-item `Unit` that is immutable after the first estimate (Epic 2).
- Terminal lifecycle handling: complete, cancel, reject (requeue / non-requeue), and expire, with
  idempotent terminal no-ops (Epic 2).
- Tenant-safe work tree guard: acyclic, single-parent, single-tenant attachment with a policy-supplied
  maximum depth (default 32) (Epic 3).
- `SpawnChild` parent→child spawning, with an optional suspend-until-child-completes await (Epic 3).
- Recursive remaining-effort roll-up projection with per-child last-writer-wins accounting keyed by the
  EventStore envelope position, and tenant-equality assertions at every traversal hop (Epic 3).
- Heterogeneous-unit roll-up safety: per-unit subtotals, fail-closed on unit mismatch with
  metadata-only diagnostics and a `Degraded` indicator, never a coerced all-unit total (Epic 3).
- Suspend/resume on await-conditions (`ChildCompleted`, `DateReached`, `ExternalSignal`); resume on the
  first matching trigger with an idempotent duplicate-resume no-op (Epic 3).
- Pure reactor translators in `Hexalith.Works.Reactor` — `ChildCompletionResumeTranslator` and
  `TerminalCascadeTranslator` — for cascading cancel/expire through still-active descendants (Epic 3).
- Uniform `ExecutorBinding` executor model (`PartyId` + `Channel` + `AuthorityLevel`) covering system,
  internal, and external parties with one shape — no executor-kind discriminator; `AuthorityLevel`
  carried but not enforced in v1 (Epic 4).
- Assign, reassign, and hand-off through one uniform `AssignWorkItem` operation; return-to-pool requeue
  re-emits `WorkItemQueued` while retaining the last binding in state (Epic 4).
- Single-claim-wins: claiming a `Queued`/`Assigned` item emits `WorkItemClaimed`; the ETag-backed atomic
  actor-state save commits exactly one candidate, and the loser retries and re-handles against the now
  `InProgress` state to receive an observable `WorkItemTransitionRejected` — no new rejection type added
  (Epic 4).
- Tenant "what's next" queue: pure `WhatsNextQueueProjection` + `WhatsNextItem` read model, ordered by
  Priority → earliest Due Date → creation/identity (both-null sorts last), with tenant scoping and a
  distinct query-side authorization filter; `WhatsNextQueryHandler` exposes it (Epic 4).
- Runnable adapter-edge host `Hexalith.Works` proving the command/event pipeline under the Aspire
  AppHost: `WorkItemEventStoreAggregate : EventStoreAggregate<WorkItemState>` wraps the pure kernel,
  with `/process`, `/project`, `/query`, and `/replay-state` endpoints and persist-then-publish (Epic 4,
  Story 4.5).
- Runtime/durable layer in the `Hexalith.Works` host, with the kernel kept clock-free: Dapr actor
  reminders for date resumes (deterministic reminder names), startup reminder reconciliation, reactor
  cascade dispatch with bounded checkpoints, checkpoint replay, and AppHost restart recovery (Epic 4,
  Story 4.6).
- Envelope-canonical sequencing contract: the EventStore envelope `SequenceNumber` is the canonical
  gapless persisted stream position — every persisted success event *and* every `IRejectionEvent`
  consumes one — while the Works payload `Sequence` is only the state-changing-event ordinal. Replay,
  projection delivery, freshness watermarks, and dedup key off the envelope position, so a rejection at
  envelope position 1 followed by a valid create at position 2 correctly yields
  `WorkItemCreated.Sequence == 1`.
- Refuse stale persisted roll-ups: the runtime `/project` adapter delivers one aggregate's stream per
  call, so a parent's child-dependent totals cannot be reconciled within a dispatch. `RolledRemaining`
  and `RolledRemainingByUnit` are now persisted and exposed as unavailable (`null` / empty) whenever the
  replayed item exposes any child (`ExposedChildCount > 0`) — or names a `ChildSpawned` event that could
  not be decoded or accepted — instead of retaining each child's spawn-time effort. Own effort, lifecycle
  status (including terminal status), parent/child structure, tenant identity, diagnostics, and the
  accepted-source watermark are preserved, and locally complete leaf totals remain available.
- Operator-triggered shared roll-up reconciliation through EventStore's internal
  `/project/rebuild/shared/v1` lifecycle. A sealed tenant inventory is folded through one relationship-aware
  graph using both `ChildSpawned` and `WorkItemCreated.Parent`; bounded staging leaves the live generation
  untouched, and commit atomically promotes schema-v2 membership/index and per-item documents while retiring
  candidate-known legacy keys. Incomplete relationship evidence keeps reliable local fields and refuses both
  rolled shapes. Operators must keep ordinary `/project` delivery for the tenant quiesced from inventory
  capture through commit, or supply an equivalent platform fence: the Works handler does not arbitrate a
  concurrent last-write projection writer (see `docs/eventstore-api-surface-constraints.md`).

### Changed

- **Breaking (hosting):** Works no longer ships `Hexalith.Works.AppHost`, `Hexalith.Works.ServiceDefaults`,
  `aspire.config.json` or its own Dapr actor, reminder, event-subscription and recovery plumbing. Hexalith.Platform
  composes the topology; the `Hexalith.Works` executable uses only the canonical
  `AddEventStoreDomainService(...)` / `UseEventStoreDomainService()` SDK composition with Works domain adapters
  (Story 4.9). The archived Works host remains available only as Platform's byte-bound rollback; see
  `docs/platform-hosting-cutover.md`.
- **Breaking (configuration):** the historical runtime switches `Works:Recovery:UseSdkRuntime=false` and
  `Works:Projection:UseSdkDelivery=false` now refuse startup instead of selecting a legacy writer. The five
  legacy-only recovery settings (`RunReconciliationOnStartup`, `ReminderReconciliationMaxAttempts`,
  `ReminderReconciliationRetryDelayMilliseconds`, `CascadeTargetIntervalMilliseconds`,
  `CascadeCheckpointIndexStaleAfterHours`) are deprecated and ignored; SDK-owned reminder/process options apply.
- **Breaking (configuration):** Parties identity queries require an absolute gateway address at startup, from
  `Works:Query:PartiesGatewayBaseAddress` or, when that override is absent or blank,
  `EventStore:CommandGateway:BaseAddress`. A missing, blank or relative address refuses startup; there is no
  implicit default.
- The EventStore producer selection follows the Builds catalog to public 3.126.1. Captured owner identities are
  retained as sealed paged references rather than inline digests.
- Works projection readers serve only the committed EventStore SDK shared-projection generation of the
  `works-runtime-v3` family; legacy unversioned and v2 keys are retained solely as rollback documents for the
  archived host and are never read, served or rewritten by the SDK runtime. A capture that fails finalization
  leaves the last committed generation served. Both roll-up readers require a stored document's embedded
  tenant/work-item identity to match the requested key, so a mis-keyed persisted document is refused instead of
  served.
- Ordinary `/project` dispatches retain the child identities a shared rebuild reconciled for an aggregate and
  refuse both rolled shapes rather than republishing a single-aggregate substitute total over a reconciled
  document; a known Works event that cannot be decoded is likewise treated as incomplete evidence.
- Runtime `/project` per-item roll-up and tenant what's-next index persistence is now monotonic on each
  accepted EventStore envelope position: per-item roll-ups use the shared optimistic-concurrency write policy,
  and the tenant what's-next index retains per-item sequence tombstones so delayed older full replays cannot
  overwrite, delete, or resurrect newer state. Equal-sequence replay can refresh deterministic documents; a
  legacy eligible entry supplies its own watermark whenever that aggregate id has no tombstone entry. The
  persisted what's-next index therefore gains an additive serialized `lastSequences` member; documents written
  before this change omit it and deserialize to an empty map. A retained entry is the sole ordering authority
  for its work item; the legacy item watermark is consulted only when that id has no entry yet. Projection
  change notification is correspondingly narrowed: a dispatch whose tenant-index write was refused as stale
  announces nothing, because no persisted state moved. The two keys remain independently guarded and
  non-atomic, and the pending date-await index the same dispatch maintains is unchanged — it keeps its own
  pre-existing raw stream-sequence watermark and is outside this guarantee.
- **Breaking:** the `WorkItemRollUp` read model's positional member `ChildContributionCount` and its
  serialized `childContributionCount` property are replaced by the derived `ExposedChildCount` getter and
  `exposedChildCount` Web JSON property. The count is no longer a positional constructor input and always
  reflects the tenant-filtered child identities the read model exposes, not children that contributed numeric
  effort. No compatibility alias is retained, so consumers must adopt the new name. Incoming
  `exposedChildCount` values are non-authoritative: persisted documents derive the count from
  `childWorkItemIds`, and normalized output emits that derived value.
- Exposed `ChildWorkItemIds` are now emitted in ordinal `WorkItemId.Value` order instead of internal
  insertion order, so permuted or duplicated deliveries of the same child facts produce an identical
  public sequence.
- The roll-up delivery allowlist is now a single production payload-identity registry guarded by a
  Contracts-derived architecture fitness test: every concrete non-rejection `IEventPayload` in
  `Hexalith.Works.Contracts` must be registered, and only `IRejectionEvent` types may be excluded.

### Deferred

- Live end-to-end proof of the two Tier-3 Aspire lanes (`WorksCommandPipelineSmokeTests`,
  `WorksReminderRecoveryPipelineSmokeTests`) requires Docker + `dapr init` + Dapr placement/scheduler;
  both skip cleanly in a headless sandbox, so their decision logic is currently proven only by
  deterministic adapter tests.
- Tenant-wide / domain-wide pending-await discovery is bounded by the EventStore per-aggregate
  stream-read route; cross-aggregate roll-up convergence and live `IProjectionChangeNotifier` wiring
  remain substrate-deferred.
- Channel & surface adapters (UI / web shell / SignalR, MCP, chatbot, email), executor routing &
  eligibility, and `AuthorityLevel` enforcement remain out of scope for v1 (Themes 3–6).
