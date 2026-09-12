# Adversarial Divergence Review — architecture.md (Hexalith.Works)

- **Lens:** adversarial divergence — build two units one level down that each obey every AD to the letter yet build incompatibly.
- **Target:** `_bmad-output/planning-artifacts/architecture.md` (register AD-01…AD-25, lines 26–455 binding; prose subordinate).
- **Date:** 2026-09-08. Read-only review; evidence cited from `src/`, `tests/`, `docs/`, `references/Hexalith.EventStore/src`, PRD addendum, epics.
- **Units attacked:** kernel (Contracts/Server), Projections, Reactor, reminder/expiry adapter, Work-Tree Registry (AD-21), EventStore SDK seams (R3/R4/R6/R7/R11), `Hexalith.Platform` host; Works team vs Platform Maintainer.

## Verdict

**FAIL** — three critical holes remain in AD-06/AD-21/AD-22 where a Works-built unit and an SDK/platform-built unit can both be register-compliant on the very next stories (registry + fan-out) and still write different roll-up document shapes, compute different contribution numbers, and disagree on who creates the child aggregate and what "creation evidence" is.

Counts: **critical 3 · high 5 · medium 6 · low 1** (15 findings).

---

### ADV-01 — [critical] — Roll-up persisted document has one watermark; AD-06 slots need N; the two merge rules cannot coexist on one key

**Scenario.** Unit A (Works Projections + the existing `/project` dispatcher) keeps `WorkItemRollUp` as the persisted per-item document: it carries a single `LatestAcceptedSourceSequence` and a `ChildWorkItemIds` list, and the dispatcher's write policy keeps whichever document has the higher watermark. Unit B (SDK R4 "relationship-aware fan-out seam", Platform Maintainer) implements AD-22 literally: on every descendant delivery it writes a `(descendantId → (descendantSeq, contribution))` slot into each ancestor's roll-up key. Both cite AD-06/AD-22 verbatim. At runtime B's slot write for descendant D (D's stream seq 7) lands on ancestor P's key; A's next single-aggregate dispatch for P (P's stream seq 3) compares `3 < 7` and either refuses its own update or, when P later reaches seq 8, overwrites B's slot document wholesale. LWW is now comparing P's and D's sequence spaces — exactly what AD-06 forbids.

**Divergence/risk.** Same key, two document shapes, two merge functions. Convergence property (RR-1) cannot hold across the two writers; the read side can silently lose slots or lose P's own fields.

**Which AD should have closed it and why it doesn't.** AD-06 binds the *logical* slot shape but never names the persisted document type, the per-slot merge function, or that a document-level watermark is forbidden; AD-22 assigns the fan-out to the SDK while leaving "Works owns the pure contribution/merge strategy" without a seam signature, so the SDK cannot know what to merge with. AD-07 only separates rolled vs own *types*, not slot storage.

**Source.** architecture.md:82–95 (AD-06), 318–342 (AD-22, "Ownership"), 263 (R4).

**Corroborating evidence.**
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs:29–38` — single `LatestAcceptedSourceSequence`, no slot map.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:446–457` — `current.LatestAcceptedSourceSequence > model.LatestAcceptedSourceSequence ? current : model` (document-level LWW).
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:198–215` — union-merges `ChildWorkItemIds` from the persisted doc; no slot merge exists.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:234–265` — rolled value is still computed by recursive in-memory traversal, not by summing slots.
- `docs/work-roll-up-projection.md:94–100` — the write-ordering guard is documented as a single monotonic watermark per key.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IDomainProjectionHandler.cs:11–18` — the SDK `/project` seam is stateless single-read-model; multi-document fan-out has no existing seam contract to inherit.

**Proposed closing rule (tighten AD-06 + AD-22).** "The persisted roll-up document is `WorkItemRollUpDocument { Own: OwnRemaining?, Status, Slots: Map<descendantId, (descendantSequence, ContributionByUnit, Terminal)>, OwnSequence }` declared in `Contracts/Models`. Merge is per-field: `Own/Status/OwnSequence` LWW by the item's own sequence; each `Slots[d]` LWW by `descendantSequence` only. No document-level watermark may gate a write. The SDK fan-out seam signature is `IRollUpSlotMerge.Merge(current, slotWrite) → next` supplied by Works; the SDK never compares sequences itself. Rolled totals are derived on read from `Own + Σ Slots`."

**Suggested disposition.** autofix (register text) + discuss (which story replaces `WorkItemRollUp`).

---

### ADV-02 — [critical] — "Own contribution computable from the event alone" is false; SDK fan-out and Works strategy will compute different numbers

**Scenario.** Unit B (SDK fan-out, per AD-22 "the value is computable from the event alone, so a single delivery serves every ancestor level") reads `ProgressReported.DoneDelta` and writes contribution = −delta or = delta into ancestor slots. Unit A (Works strategy) computes contribution = `Estimated − ΣDone` clamped, and 0 when the item is terminal, which requires the descendant's folded state (`InitialEffort`, prior `ReEstimated`, status). Both are compliant; B's slot values are wrong for any item with prior progress, any re-estimate, any terminal transition, and any mixed-Unit history.

**Divergence/risk.** Silent numeric corruption of every ancestor total — the exact SM-2 failure AD-06 exists to prevent — with no test able to catch it unless the two units are tested together.

**Which AD should have closed it and why it doesn't.** AD-22 asserts the false premise. AD-06 says "valued with that descendant's own contribution per Unit" without saying *who folds* the descendant stream to produce it.

**Source.** architecture.md:318–326 (AD-22 "own contribution … computable from the event alone"), 82–95 (AD-06).

**Corroborating evidence.**
- `src/Hexalith.Works.Contracts/Events/ProgressReported.cs` (delta-only payload; see also architecture.md pattern example line ~838).
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs:26–36` — `Remaining = Estimated − Done`, `Report(delta)` needs prior `Done`.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:222–232` — `ToOwnRemaining` returns 0 for `Terminal`, else needs `OwnEffort` state.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs:176–201` — `Apply(ProgressReported)` and `Apply(ReEstimated)` fold state; a mismatched-Unit `ReEstimated` is *retained-not-applied*, unknowable from the event.

**Proposed closing rule (amend AD-22).** "The descendant's contribution is produced only by the descendant's own single-aggregate projection (its folded state) as `(descendantId, descendantSequence, ContributionByUnit, Terminal)`; the fan-out transports that computed slot to every `Attached` ancestor. The SDK fan-out seam never inspects Works payloads. A contribution slot is emitted on every state-changing delivery of the descendant, including terminal transitions (contribution 0)."

**Suggested disposition.** autofix.

---

### ADV-03 — [critical] — Who creates the child aggregate, and what is "creation evidence"? Three documents give three answers; the freshness witness has no home

**Scenario.** Unit A (Works kernel/Reactor team): `SpawnChild` on the parent emits `ChildSpawned` and *that is* the child's creation — the child node exists only as spawn facts in projections (today's behaviour: no handler issues `CreateWorkItem`); the registry marks `Attached` on `ChildSpawned` evidence (AD-21). Unit B (Registry/Platform team, following the PRD addendum): after `ChildSpawned` "the pipeline creates the child (`CreateWorkItem` with `ParentWorkItemReference`)" and the registry's Released rule is judged on "the child *stream* shows no creation evidence" (AD-21 repair). Now: (1) B's reservation times out on any child A never streams → healthy edges auto-`Released`; (2) A's projection has two writers for the child's node — the parent stream at provisional seq 1 and the child stream's `WorkItemCreated` at seq 1 — with equal-sequence LWW undefined; (3) AD-22's witness ("creation evidence records the registry sequence of its reservation") must ride `Reserve → SpawnChild → ChildSpawned → CreateWorkItem → WorkItemCreated`, and **no type in that chain has a field for it**; A puts it on `ChildSpawned`, B on `WorkItemCreated`, the fan-out reads one of them.

**Divergence/risk.** Registry state machine and item streams disagree on attachment; witness serialized shape diverges; child identity collision (a `CreateWorkItem` for an id that already exists as a root is a plain rejection today, not a registry release). "Verifiable root provenance via plain `CreateWorkItem`" is unverifiable while `CreateWorkItem.Parent` remains a public, caller-fed field (see ADV-08).

**Which AD should have closed it and why it doesn't.** AD-21 defines Attached on `ChildSpawned` but Released on child-stream evidence — two different evidence sources in one entry. AD-22 names the witness but not its carrier type, nor whether "registry sequence" is the envelope `SequenceNumber` or the payload ordinal (the doc itself says two counters coexist, line 791). AD-02 covers ID minting only.

**Source.** architecture.md:272–316 (AD-21: "Attachment protocol", "Failure lifecycle" 293, "Repair semantics" 303), 327–333 (AD-22 witness), 791 (two counters).

**Corroborating evidence.**
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:71–150` — `Handle(SpawnChild)` emits `ChildSpawned` (+`WorkItemSuspended`); nothing creates the child.
- `src/Hexalith.Works/Runtime/WorksHost.cs:70–84` — registered domain-event handlers: Cancelled, Expired, Completed, Suspended — none for `ChildSpawned`.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:112–123, 150–157` — child node fabricated from parent's spawn facts, `LatestAcceptedSourceSequence = max(…,1)`; `WorkItemCreated` at seq 1 is an equal-sequence write.
- `docs/work-tree-shape-guard.md:35–42` — "facts needed for the command pipeline to create the child" (pipeline unnamed).
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md:41` — "the pipeline then creates the child (`CreateWorkItem` with `ParentWorkItemReference`)".
- `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`, `ChildSpawned.cs`, `Commands/CreateWorkItem.cs` — no attachment/registry-sequence field on any of them.

**Proposed closing rule (new AD-26 "Child creation act").** "A spawned child is created by exactly one act: the Reactor translates `ChildSpawned` → `CreateWorkItem(child, Parent=(tenant,parent), AttachmentSequence=<registry envelope SequenceNumber of the EdgeReserved>)`; `WorkItemCreated` gains the additive field `AttachmentSequence?` — this is the AD-22 witness and is the only creation evidence the registry accepts. The registry transitions `Reserved → Attached` on the child's `WorkItemCreated{AttachmentSequence == reservation}` (never on `ChildSpawned`), and `Reserved → Released` on the dedicated spawn rejection, on a `CreateWorkItem` rejection for that child (also translated), or on timeout. A `CreateWorkItem` carrying `Parent`/`AttachmentSequence` is accepted at the gateway only from the Reactor's workload identity (extends AD-24). Projections never fabricate a child node from `ChildSpawned`; the parent's `ChildSpawned` only records the child id."

**Suggested disposition.** discuss (changes the saga's evidence order) → then autofix register + PRD addendum line 41.

---

### ADV-04 — [high] — The registry aggregate's identity, domain, topic, and subscription route are unbound; the R3 processor's identity contract will skip its events

**Scenario.** Unit A (Registry, Works team) places the registry in domain `work` with aggregate id e.g. `tree` (tenant-scoped, one per tenant) so the existing wildcard routing, `work.events` topic and `/work/events` subscription carry its events. Unit B (R3 durable event processor, SDK/platform) keeps today's identity contract: envelope `Domain == "work"`, payload must expose `TenantId` and `WorkItemId` properties and `AggregateId == WorkItemId`. Every `EdgeReserved(TenantId, ParentWorkItemId, ChildWorkItemId)` is dropped as `envelope-identity-mismatch` or `no-registered-handler`, so the mechanical reactor never sees reservations. Alternatively A chooses domain `work-tree`: then `AggregateIdentity.PubSubTopic` derives `{tenant}.work-tree.events`, no topic override or domain-service registration exists, and nothing is routed at all — while the item id `tree` remains a legal `WorkItemId` that collides with the registry actor id inside domain `work`.

**Divergence/risk.** Reactor starved of registry events (no attachments ever complete), or an id collision between an ordinary work item and the registry actor.

**Which AD should have closed it and why it doesn't.** AD-21 says "tenant-scoped, event-sourced aggregate (its own single-writer actor)" and "registry commands/events are additive serialized types" but never fixes `{tenant}:{domain}:{aggregateId}`, topic, or subscription; AD-15/VAL-H09 defers the namespace table to Story 4.9.

**Source.** architecture.md:272–283 (AD-21 owner), 262 (R3), 448 (VAL-H09), 756–762, 984.

**Corroborating evidence.**
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:40–44, 122–138, 186–197` — domain gate, identity triple check, closed descriptor table keyed by payload type.
- `src/Hexalith.Works/Runtime/WorksEventIdentity.cs:11–38` — requires `TenantId` + `WorkItemId` properties and `AggregateId == WorkItemId`.
- `src/Hexalith.Works.AppHost/Program.cs:92–99` — `wildcard_work_v1` routes domain `work` only; `TopicOverrides__work = work.events`.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Identity/AggregateIdentity.cs:92–94` — topic derived from `{tenant}.{domain}.events`.
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemId.cs` — any regex-valid string is a `WorkItemId`.

**Proposed closing rule (tighten AD-21).** "The registry is aggregate `{tenant}:work-tree:{tenant}` (domain `work-tree`, aggregate id = tenant id). Its events publish on topic `work-tree.events` (platform override) and are consumed on `/work-tree/events` by the same R3 processor, whose identity contract is per aggregate type: `TenantId` + the type's declared aggregate-id property. The domain-service registration `wildcard_work-tree_v1` is a Story 4.9/R1 platform obligation. The VAL-H09 table lists both rows."

**Suggested disposition.** autofix (register) + defer table to VAL-H09.

---

### ADV-05 — [high] — Deterministic MessageId derivation already has three incompatible styles in Works; VAL-H10 open while AD-21 depends on it; SDK offers a second idempotency carrier

**Scenario.** Unit A (Works Reactor/registry story) derives reserve→spawn→release message ids by string concatenation (`cascade-{kind}-{tenant}-{parent}-{seq}-{child}` style). Unit B (SDK R11 generic gateway client, Platform Maintainer) requires "ULID-safe" `MessageId`s and expects idempotent intent to travel in `IdempotencyKey` via `IIdempotencyIntentAdapter`. A's ids embed aggregate ids that legally contain `.` and `_` (aggregate-id regex) which the gateway `MessageId` regex (alphanumeric + hyphen, ≤128 chars) rejects — so a legal work item id `epic_12.a` makes every cascade/resume submission fail validation, and long ids overflow 128 chars. Meanwhile date-resume uses a SHA-256 hashed name. Two originators, three schemes, one seam.

**Divergence/risk.** Gateway rejects mechanical commands for a subset of legal ids (silent recovery gap); duplicate suppression differs by originator; the registry saga has no defined replay result for a re-submitted reserve.

**Which AD should have closed it and why it doesn't.** AD-02 explicitly declines transport idempotency; R11 names the seam but VAL-H10 is open, and AD-21 conditions the registry story on it without stating the derivation function's inputs/format.

**Source.** architecture.md:49–55 (AD-02), 270 (R11), 313–316 (AD-21 dependency note), 449 (VAL-H10).

**Corroborating evidence.**
- `src/Hexalith.Works/Recovery/Cascade/CascadeCommands.cs:18–19` and `src/Hexalith.Works/Recovery/ChildCompletion/ChildCompletionResume.cs:20` — raw concatenation of tenant/ids/sequence.
- `src/Hexalith.Works/Reminders/DateResume.cs:33` — hashed (`DateReminderName.For`) id.
- `src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs:23–30` — `MessageId = CausationId`, `IdempotencyKey` never set.
- `references/Hexalith.EventStore/src/Hexalith.EventStore/Validation/SubmitCommandRequestValidator.cs:23–26, 36–41, 93–97` — identifier regex + 128 cap; separate `IdempotencyKey` rule.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Identity/AggregateIdentity.cs:11` — aggregate id regex allows `.` `_`.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IIdempotencyIntentAdapter.cs:6–25` — server-side canonical-intent seam Works does not implement.

**Proposed closing rule (bind VAL-H10 as AD-27).** "Every internal originator derives `MessageId = "{originator}-{sha256hex32(tenant ␟ aggregate ␟ causal-event-type ␟ causal-envelope-sequence ␟ intent)}"` via one pure function in `Hexalith.Works.Reactor` (`WorksMessageId.For(...)`); `CausationId` = the causal envelope `MessageId`; `IdempotencyKey` is not used by Works. Replay of an already-admitted `MessageId` returns the original result and appends nothing. Retention tier for reactor intents is bound in R11."

**Suggested disposition.** autofix (register) — must precede the registry story.

---

### ADV-06 — [high] — Two reminder registrars can be built on two different actors, and AD-24's origin rule then denies one of them

**Scenario.** Unit A (Works reminder adapter, today) registers reminders on a dedicated `DateReminderActor` type hosted in the Works process, actor id = hash(tenant, item). Unit B (SDK R6 "generic durable-reminder seam", Platform Maintainer) reads AD-11 "a Work Item … registers a *self-targeted* durable actor reminder" and registers it on the SDK aggregate actor (`{tenant}:work:{id}`, which lives in the EventStore process). Both are compliant. AD-24 then requires callbacks "only from the Works actor identity" and register/cancel "only from the Works service's own workload identity (self-targeted)" — B's registration originates from the `eventstore` app id and is denied by the deny-by-default policy (or the sandbox exemption hides it until production). Reconciliation (indexed source) re-registers on A's actor; B's registrations are invisible to it → double firing or orphaned reminders.

**Divergence/risk.** Two reminder registries for one item; AD-24's positive-path test passes on whichever actor the test author picked.

**Which AD should have closed it and why it doesn't.** AD-11 says "self-targeted" without naming the actor type or host process; R6 moves the `DateReminderActor` to the SDK without saying it remains a Works-hosted actor type; AD-24 assumes a single "Works actor identity".

**Source.** architecture.md:137–149 (AD-11), 265 (R6), 377–383 (AD-24 originators), 697.

**Corroborating evidence.**
- `src/Hexalith.Works/Reminders/DateReminderActor.cs:17, 34–48` — separate actor type, `IRemindable`, state keyed by reminder name.
- `src/Hexalith.Works/Reminders/DaprDateReminderScheduler.cs:27–28` — actor id = `DateReminderName.ActorId(tenant, item)` (hashed), not the aggregate id.
- `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs:93–98` — actor registered in the Works host.
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml` — app-id based allow list; no actor-route entry.

**Proposed closing rule (tighten AD-11/AD-24).** "All Works reminders (date resume, expiry, reservation timeout) are registered on one actor type `WorksReminderActor` hosted in the Works service process (actor id = `sha256hex32(tenant ␟ aggregate)`), never on the EventStore aggregate actor. The SDK R6 seam is a library the Works host composes; it does not host actors. AD-24 'Works actor identity' means this actor's app id."

**Suggested disposition.** autofix.

---

### ADV-07 — [high] — Expiry cannot reuse the pending-date-await index or the reminder-name function as written; two units will key expiry differently or reissue the wrong command

**Scenario.** Unit A (Works adapter) adds expiry entries to the existing `PendingDateAwaitTenantIndex` (AD-25: "same indexed pending-source protocol"). The reconciler treats every past-due entry as a `DateReached` await and submits `ResumeWorkItem` — so a missed expiry is *resumed*, not expired (and rejected for a non-suspended item, persisting `WorkItemTransitionRejected`). Unit B (SDK seam) builds a second index `projection:works:pending-expiry:{tenant}` with its own tenant registry key under its own reserved tenant id. Also: the reminder-name function is bound as `(tenant, workItemId, awaitConditionKey)`; expiry has no await condition, `DueDate` is a `DateOnly` with no instant, and the fire instant depends on platform TZ/TTL configuration — A names by `DueDate` string, B by computed UTC instant; on `WorkItemRescheduled` neither can cancel the previous name without the prior schedule (not on the event).

**Divergence/risk.** Missed expiries become resumes; two registries; orphaned reminders on reschedule; config change (TTL) silently changes names.

**Which AD should have closed it and why it doesn't.** AD-25 asserts reuse of the AD-11 protocol without extending the entry shape (`kind`), the reconciler's dispatch, or the name function; line 760 binds the name inputs to an await-condition key.

**Source.** architecture.md:397–412 (AD-25, "Identity" 404, "Recovery" 408, "Policy" 410), 760–762, 699.

**Corroborating evidence.**
- `src/Hexalith.Works/Reminders/PendingDateAwaitProjection.cs:25–31, 37–82` — only `WorkItemSuspended`-sourced `DateReached` awaits; `WorkItemCreated`/`WorkItemRescheduled` are not state-affecting.
- `src/Hexalith.Works/Reminders/PendingDateAwait.cs:10–14` — no `kind`.
- `src/Hexalith.Works/Reminders/DateReminderReconciler.cs:101–118` — past-due ⇒ `DateResume.BuildSubmission` unconditionally.
- `src/Hexalith.Works/Reminders/DateReminderName.cs:25–54` — prefix `work-date-resume`, inputs `(tenant, item, correlationKey)`.
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemSchedule.cs:3` — `DueDate` is `DateOnly?`.
- `docs/lifecycle-transition-matrix.md:250–260` — `ExpireWorkItem` on `Suspended` is `→Expired`, but a `ResumeWorkItem` there with no matching condition is `R` (rejection).

**Proposed closing rule (tighten AD-25).** "Pending entries carry `Kind ∈ {DateResume, Expiry, ReservationTimeout}` and a `Key`; the reconciler dispatches by `Kind` (`Expiry ⇒ ExpireWorkItem`). Reminder name = `work-{kind}-{sha256hex32(tenant ␟ item ␟ key)}` where the expiry `key` is the computed UTC firing instant (ISO-8601 'O'), computed once by the adapter from `DueDate` + platform options and recorded in the index entry so cancellation on reschedule/terminal uses the *stored* key. Index entries for expiry are maintained from `WorkItemCreated`, `WorkItemRescheduled`, and terminal events."

**Suggested disposition.** autofix.

---

### ADV-08 — [high] — Tree topology has three sources of truth and four consumers; AD-21 restricts `SpawnChild` but leaves `CreateWorkItem.Parent` as a public, caller-fed edge

**Scenario.** Unit A (Contracts/Server, unchanged) keeps `CreateWorkItem(Parent, ProposedParentAncestors, …)` as a public command; edges created this way never touch the registry. Unit B (registry-backed fan-out + cascade per AD-21) enumerates `Attached` registry edges only. A root created with `Parent=P` by an ordinary tenant member is invisible to B's roll-up and cascade, but visible to today's stream-reading child-completion source (`WorkItemCreated.Parent`) and shared rebuild — so a parent resumes on a child B never counted, and a cancel cascade issued by the R7 stream reader reaches descendants the registry says don't exist.

**Divergence/risk.** Divergent trees per consumer; "single-parent" is enforced in the registry while the item stream carries a second parent fact; repair suppression cannot detect an edge the registry never recorded.

**Which AD should have closed it and why it doesn't.** AD-21 "no unit mutates tree shape except through the registry protocol" is stated, but the only origin restriction is on `SpawnChild` (AD-24); `CreateWorkItem.Parent` (and its caller-fed ancestor facts) is untouched, and AD-21 says the registry is authoritative only "among existing items".

**Source.** architecture.md:308–316 (AD-21 rule), 377–383 (AD-24), 266 (R7), 263 (R4).

**Corroborating evidence.**
- `src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs` — public `Parent`, `ProposedParentAncestors`, `ProposedParentDepth`, `MaxDepth`.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:12–66` — `Handle(CreateWorkItem)` validates the edge from caller facts and persists `Parent`.
- `src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:41–50` — parent discovered from `WorkItemCreated.Parent` only.
- `src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:62–72` — descendants discovered from parent's `ChildSpawned` only.
- `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:167–184` — rebuild uses both.

**Proposed closing rule (tighten AD-21 rule).** "`CreateWorkItem` with a non-null `Parent` (or any ancestor/depth/`MaxDepth` field) is accepted only from the Reactor's workload identity (AD-24) as the ADV-03 creation act; public creates are roots. The registry is the *sole* enumerator for fan-out, cascade, child-completion resume discovery, and rebuild inventory; `WorkItemCreated.Parent`/`ChildSpawned` are evidence for repair only. A one-time migration seeds the registry from existing streams before AD-22 delivery is enabled for a tenant."

**Suggested disposition.** discuss (public API change) → autofix.

---

### ADV-09 — [medium] — Duplicate/redelivered `SpawnChild` against a non-live parent releases a healthy `Attached` edge (ADV-U2 hole)

**Scenario.** Registry (unit B) reserved edge (P, C); the reactor delivered `SpawnChild` and P emitted `ChildSpawned` → `Attached`. P then completes. The reactor redelivers the same `SpawnChild` (at-least-once, AD-09). Unit A's `Handle(SpawnChild)` checks liveness *first* and returns a rejection for a `Completed` parent before it ever looks at `SpawnedChildWorkItemIds`; under AD-21 that rejection must be the "dedicated additive rejection event carrying (tenant, parent, child)" which the mechanical reactor translates to a registry release. B, being mechanical, releases the `Attached` edge. Both units followed their AD text; the "duplicate for an already-Attached pair is a no-op" promise is only realizable if the parent aggregate — not the registry — can tell it is a duplicate, and the parent's order of checks is unspecified. Second variant: the same parent re-reserving the same still-`Reserved` child — AD-21 defines no outcome (idempotent no-op vs second `EdgeReserved` → second `SpawnChild` → second `ChildSpawned` event, since `Handle` has no already-spawned guard).

**Divergence/risk.** Compensating release of a live edge; duplicate `ChildSpawned` sequence burn and a second provisional child node.

**Which AD should have closed it and why it doesn't.** AD-21 "Failure lifecycle" defines the no-op for `Attached` pairs but assigns it to no unit and does not guard `Released` against being applied to `Attached`; AD-13 idempotency covers resume only.

**Source.** architecture.md:293–302.

**Corroborating evidence.**
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:78–82` — `IsLive` check precedes everything; no `SpawnedChildWorkItemIds.Contains` check anywhere in `Handle(SpawnChild)`.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs:165–174` — dedup only in `Apply`.

**Proposed closing rule (tighten AD-21).** "In `Handle(SpawnChild)` the duplicate check (`state.SpawnedChildWorkItemIds` contains child ⇒ `NoOp`) precedes every other guard including liveness. The registry's `Release` is legal only from `Reserved`; a release for an `Attached` or `Released` edge is `NoOp`. A reserve for an existing `Reserved`/`Attached` `(parent, child)` pair from the same parent is `NoOp` (no second `EdgeReserved`)."

**Suggested disposition.** autofix.

---

### ADV-10 — [medium] — Reservation timeout has no owner, no reminder family, and no defined race with late evidence

**Scenario.** Unit A (Works) registers a per-reservation reminder (a third reminder family) on its reminder actor; unit B (Platform) runs a periodic sweep in the platform lane using the registry read model; both are "platform-configured". With both present, a reservation is released twice (fine) — but a late `ChildSpawned`/`WorkItemCreated` arriving after `Released` has no defined transition: A's registry re-attaches (Released → Attached), B's treats it as divergent evidence and suppresses the subtree. `Released` reversibility is unspecified, as is re-parenting/detach (AD-21 mentions neither; the roll-up doc states "no Works event detaches a child").

**Divergence/risk.** Registry state machines diverge on the same event history; suppression vs silent re-attach.

**Which AD should have closed it and why it doesn't.** AD-21 says "bounded, platform-configured reservation timeout" and stops; AD-25 names only date and expiry reminders; AD-01 forbids the registry from reading a clock, so *some* adapter must fire it.

**Source.** architecture.md:293–302, 303–307, 397–412.

**Corroborating evidence.** `docs/work-roll-up-projection.md:90–92` ("no Works event detaches a child"); no timeout code exists (`grep -rn "timeout" src/Hexalith.Works.Server` → none). Reminder families in code: date only (`DateReminderName.cs:25`).

**Proposed closing rule (tighten AD-21 + AD-25).** "Reservation timeout is a `ReservationTimeout` reminder in the AD-25 reminder family, registered by the reminder adapter from `EdgeReserved` and cancelled from `EdgeAttached`/`EdgeReleased`; firing submits `ReleaseReservation(tenant, parent, child, reservationSequence)`. `Released` is terminal for that reservation; creation evidence arriving after `Released` is recorded as `AttachmentOrphaned` and the Reactor translates it to `CancelWorkItem(child)`. Re-parenting and detach are out of v1 scope: no command moves an `Attached` edge."

**Suggested disposition.** autofix.

---

### ADV-11 — [medium] — Registry-driven slot removal is impossible under AD-06's "written only from its own descendant's stream"

**Scenario.** After repair or `Released`/orphan handling the registry (unit B) needs ancestor documents to drop descendant D's slot. AD-06 says every slot is written only from D's own stream and LWW-compared in D's sequence space; a registry event has no D-sequence, so B either (a) writes a tombstone with a fabricated sequence (violates AD-06) or (b) leaves the slot (D keeps contributing to a subtree it no longer belongs to). Unit A (Works strategy) picks (b); the SDK fan-out picks (a). Also unresolved: what a slot holds when D's contribution is 0 (kept with 0, or removed — affects `ExposedChildCount`/`ChildWorkItemIds`).

**Divergence/risk.** Ancestors disagree after repair; degraded-flag semantics differ.

**Which AD should have closed it and why it doesn't.** AD-06 defines slot writes but no slot lifecycle; AD-21 repair "suppresses dependent roll-up" without a slot-level mechanism.

**Source.** architecture.md:82–95, 303–307.

**Corroborating evidence.** `WorkItemRollUp.cs:37, 44` (`ChildWorkItemIds`, `ExposedChildCount` derived from structure, not slots); `WorkItemRollUpProjection.cs:248–261` (terminal node contributes 0 but stays in `ChildKeys`).

**Proposed closing rule (tighten AD-06).** "Slots are keyed by `(descendantId, attachmentSequence)`; a slot is retained (value 0) after the descendant goes terminal and is *removed* only by a registry-sourced write keyed by the registry envelope sequence for that `attachmentSequence` — the only write that may touch a slot from outside the descendant's stream. `ChildWorkItemIds` lists only descendants with a live slot."

**Suggested disposition.** autofix.

---

### ADV-12 — [medium] — AD-16 fence vs AD-22 fan-out: no token exists, and fan-out writes are not on the path a delivery quiesce can stop

**Scenario.** Unit B (Platform, Story 4.9 R4) implements the fence as "pause the `work.events` subscription during capture→Commit". Unit A's fan-out (and today's `/project` dispatcher) is invoked by the EventStore projection actor, not the subscription, so it keeps writing `projection:works:rollup:*` keys during staging; the shared-rebuild handler explicitly does not arbitrate against live writers. Alternatively B fences with an epoch in `ReadModelWriteContext`; A's writes carry no epoch, so they are either all refused (outage) or all admitted (race).

**Divergence/risk.** Staged generation and live writes interleave; Commit promotes a manifest missing live slot writes.

**Which AD should have closed it and why it doesn't.** AD-16 declares the fence an open platform obligation while AD-22 declares fan-out writers already "fenced writers" — an obligation on a protocol that has no shape.

**Source.** architecture.md:184–194, 333–336, 447 (VAL-H08).

**Corroborating evidence.** `docs/work-roll-up-projection.md:82–86` ("does not arbitrate a concurrent live writer"); `WorksHost.cs:112–125` (`/project` is a direct route from the projection actor); `WorkItemProjectionDispatcher.cs:446–457` (no epoch/generation token in the write predicate); `references/…/Hexalith.EventStore.Client/Projections/IReadModelFreshness.cs` (SDK read-model metadata carries `ProjectionVersion`, not a fence epoch).

**Proposed closing rule (bind minimally in AD-16).** "The fence is an epoch integer per `(tenant, projection)` stored under a platform-owned key; `Begin` increments it, `Commit`/abort decrements the writer gate. Every writer of `projection:works:*` keys (ordinary dispatch, fan-out, reconciliation) reads the epoch at the start of its write and passes it in `ReadModelWriteContext.Epoch`; `ReadModelWritePolicy` refuses (retryable) writes whose epoch is not the current admitted epoch. Refused deliveries are not acknowledged."

**Suggested disposition.** discuss (owner is Platform) → autofix text.

---

### ADV-13 — [medium] — Two reserved control-plane tenant ids (`tenants` in Works, `system` in EventStore) and unlisted global registries

**Scenario.** Works keys its cross-tenant recovery registry under tenant id `tenants` and hard-rejects envelopes with that tenant; the SDK reserves `system` for platform-level topics. Unit B (SDK R6/R7 generic reconciliation seam) naturally keys its registry under `system`; unit A keeps `tenants`; the cascade checkpoint index adds a third global key. A tenant actually named `system` passes Works' reserved-id check and collides with SDK semantics; a tenant named `tenants` passes the SDK and collides with Works.

**Divergence/risk.** Cross-tenant key collision, two enumerations for recovery (VAL-H09/SEC-DI-06).

**Which AD should have closed it and why it doesn't.** AD-15 defers to VAL-H09, which is "open — replace … during R3/R6/R7 migration" with no interim reservation list.

**Source.** architecture.md:174–183, 448, 984–986.

**Corroborating evidence.** `src/Hexalith.Works/Projections/WorksReadModelKeys.cs:56–78`; `WorksDomainEventProcessor.cs:52–60`; `references/…/AggregateIdentity.cs:92–94` (`system`); `src/Hexalith.Works/Recovery/Cascade/CascadeCheckpointIndex.cs` (separate index).

**Proposed closing rule (interim in AD-15).** "Reserved tenant ids are exactly `{system, tenants}`; both are rejected at ingress by platform and at Works' event processor. Every cross-tenant key is listed in the VAL-H09 table with an owner before any new one is added; new global registries use the `system` namespace."

**Suggested disposition.** autofix (interim rule) + defer (table).

---

### ADV-14 — [medium] — Workload identity + tenant-delegation context has no carrier; per-command origin restriction has no enforcement point

**Scenario.** Unit A (Works Reactor) conveys the delegation context in `SubmitCommandRequest.Extensions["delegated-tenant"]`; unit B (Platform ingress) expects an `act`/`azp` claim in the workload token and ignores `Extensions`. AD-24's "`SpawnChild` accepted only from the reactor's workload identity" must be enforced *per command type* at the EventStore gateway (platform-owned), but the gateway's routing/ACL is per app-id and per domain/tenant wildcard — no command-type policy exists, so the sandbox exemption passes and production either denies all `SpawnChild` or none. Works, seeing `context.UserId` on every envelope, may also be built to trust it because "the platform did it".

**Divergence/risk.** Incompatible delegation carriers; origin restriction unenforceable; envelope identity treated as authority in sandbox.

**Which AD should have closed it and why it doesn't.** AD-23 names "an explicit, auditable tenant-delegation context" without shape/transport; AD-24 states the restriction without the enforcement locus.

**Source.** architecture.md:356–360 (AD-23 delegation), 377–383 (AD-24), 386–395.

**Corroborating evidence.** `references/…/Hexalith.EventStore/Models/SubmitCommandRequest.cs:15` (`Extensions` free-form); `src/Hexalith.Works.AppHost/Program.cs:92–99` (routing per domain/tenant wildcard); `accesscontrol.works.yaml` (per app-id routes); `references/…/EventStoreDomainEventContext.cs:34` (`UserId` surfaced to Works handlers); `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs:60–65` (reactor authenticates with `DAPR_API_TOKEN` only).

**Proposed closing rule (tighten AD-23/AD-24).** "Delegation context = two gateway-verified request extensions `hx-workload` (SPIFFE id) and `hx-delegated-tenant`, populated by the SDK gateway client from the workload identity, never by domain code; the gateway rejects any command whose `Tenant` differs from `hx-delegated-tenant`. Origin restriction is a platform registration table `CommandOriginPolicy[commandType] → allowed workloads` evaluated by the gateway before routing. Works handlers must not read `UserId`/`TenantId` from context for any authorization decision (fitness test)."

**Suggested disposition.** discuss (Platform Maintainer) → autofix text.

---

### ADV-15 — [low] — AD-03 identity order: ordinal on case-preserved ids across originators does not approximate creation order

**Scenario.** Originator A mints uppercase Crockford ULIDs (Commons); originator B (REST client) sends lowercase GUIDs; `WorkItemId` preserves aggregate-id case (tenant/domain are lowercased, aggregate id is not). Ordinal order sorts every uppercase id before every lowercase id, so the FR-20 tiebreak is stable but the "approximates creation order" advice silently fails whenever two originators coexist. Both units are compliant.

**Divergence/risk.** Low — ordering stays deterministic; only the product expectation drifts.

**Which AD should have closed it and why it doesn't.** AD-03 makes ULID advisory and does not bind case normalization.

**Source.** architecture.md:57–66.

**Corroborating evidence.** `references/…/AggregateIdentity.cs:28–36` (aggregate id case-preserved); `WhatsNextOrdering.cs:68–71` and `WorkItemRollUpProjection.cs:196–202` (ordinal); `WorkItemId.cs` (normalized through `AggregateIdentity`).

**Proposed closing rule.** "Tiebreak is `StringComparer.Ordinal` on the stored `WorkItemId.Value`, no normalization; the creation-order approximation is a per-originator property only (documented, not promised)."

**Suggested disposition.** ignore / defer (doc note).

---

## Notes on the ADV-U1…U7 fixes attacked

- **U1 (flattened slots):** correct logically, but unrealizable on the current persisted document and write policy (ADV-01) and dependent on a false "from the event alone" premise (ADV-02); slot lifecycle still open (ADV-11).
- **U2 (Reserved→Attached/Released saga):** evidence source for `Attached` (`ChildSpawned`) contradicts evidence source for `Released` (child stream) (ADV-03); the no-op promise is assigned to no unit and is defeated by the parent's guard order (ADV-09); timeout owner absent (ADV-10).
- **U3 (SpawnChild origin restriction):** leaves `CreateWorkItem.Parent` open (ADV-08) and has no gateway enforcement locus (ADV-14).
- **U4 (freshness witness):** no carrier type, and "registry sequence" is ambiguous between envelope and payload counters (ADV-03).
- **U5 (reservation carries full SpawnChild payload):** fine, but the child creation act it feeds is undefined (ADV-03).
- **U6 (self-targeted reminder origin):** "self" is ambiguous between the Works reminder actor and the SDK aggregate actor (ADV-06).
- **U7 (R11 seam):** the seam exists; the derivation policy it is supposed to carry is already inconsistent inside Works and clashes with the gateway's validator and second idempotency channel (ADV-05).
