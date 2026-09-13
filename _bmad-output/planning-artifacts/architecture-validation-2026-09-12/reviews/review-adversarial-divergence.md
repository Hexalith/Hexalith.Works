# Adversarial Divergence Review — `architecture.md`

- **Lens:** configured BMad Architecture adversarial-divergence reviewer.
- **Target:** `/home/administrator/projects/hexalith/works/_bmad-output/planning-artifacts/architecture.md`, binding register AD-01…AD-25.
- **Assessment date:** 2026-09-12.
- **Mode:** standalone Validate; read-only review of the spine. Current code and specification are corroborating evidence, not edits.
- **Attack model:** independently built units one level below the initiative spine: Works Contracts/Server/Projections/Reactor, the Work-Tree Registry, EventStore SDK R3/R4/R6/R7/R11 seams, and the `Hexalith.Platform` host.

## Verdict

**FAIL — implementation must not start on the AD-21/AD-22 registry and live fan-out slice from this register alone.** Four critical and nine high-severity seams still permit independently compliant units to persist incompatible roll-up state, acknowledge incomplete topology, create a child through different authoritative acts, accept stale saga work, or apply different authorization and recovery rules.

Counts: **critical 4 · high 9 · medium 3** (16 findings).

The most dangerous new construction is the AD-22 freshness rule itself: registry watermark `>= reservation sequence` can be true while the edge remains `Reserved`; the fan-out then sees no `Attached` ancestry, acknowledges the descendant delivery, and may never repair the missing ancestor contribution. The second is a reservation timeout racing an already-issued `SpawnChild`: no reservation token is carried to the parent, so a conforming registry can release the edge while a conforming parent later creates asymmetric topology.

## Critical findings

### ADV-0912-01 — Persisted roll-up shape and merge ownership are not a shared contract

- **Compliant construction A:** Works defines one serialized ancestor document containing own state, a `Dictionary<descendantId, Slot>`, and derived rolled totals. A Works-supplied merge function performs per-slot LWW.
- **Compliant construction B:** the generic EventStore R4 fan-out stores one key per `(tenant, ancestor, descendant)` slot and materializes a separate consumer document. It still provides one LWW slot per descendant, compares only the descendant's sequence, and computes `own + sum(slots)` exactly as AD-06 says.
- **Clash:** both satisfy the logical AD-06/AD-22 invariants, but they disagree on persisted keys, serialized shape, atomic visibility, ownership of derived totals, and which component resolves equal-sequence writes. A query or ordinary projection built against A cannot read B. If both target today's roll-up key, whole-document replacement destroys independent slot writes.
- **Consequence:** silent loss of ancestor slots or the parent's own fields; the RR-1 convergence property can pass inside each unit and fail when the units are composed.
- **AD/source references:** AD-06 (`architecture.md:82-95`), AD-07 (`:97-103`), AD-20 R4 (`:263`), AD-22 (`:318-342`). The register names the logical slot but no persisted DTO, key layout, merge interface, writer ownership, or read materialization contract.
- **Current-code corroboration:** `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs:29-38` has a single document watermark and no slot map; `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:440-451` replaces the complete document using `LatestAcceptedSourceSequence`; `docs/work-roll-up-projection.md:94-100` documents that document-level guard. EventStore now exposes `IAsyncDomainProjectionHandler`, but its result contract does not define Works slot state (`references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IAsyncDomainProjectionHandler.cs:8-23`; `DomainProjectionHandlerResult.cs:13-44`).
- **Closing rule / disposition:** **Autofix after one short design decision.** Bind the persisted slot DTO, all state-store keys, the per-field/per-slot merge rule, the one owner allowed to write each key, and the query materialization rule. Require a Works-owned `IRollUpSlotMerge` (or equivalent exact signature) invoked by R4; forbid a document-level watermark from arbitrating descendant slots. Route the replacing story before AD-22 implementation.

### ADV-0912-02 — AD-22 assigns contribution calculation to two incompatible interpretations of “event”

- **Compliant construction A:** the SDK interprets “on a descendant's state-changing projection delivery” and “value is computable from the event alone” literally as a raw Works domain-event delivery. It derives a slot update directly from `ProgressReported.DoneDelta`, `ReEstimated.Estimated`, or a terminal event.
- **Compliant construction B:** Works interprets “projection delivery” as the output of the descendant's folded single-aggregate projection. It emits the current `(own remaining by unit, terminal, descendant sequence)` snapshot and the SDK merely transports it to all ancestors.
- **Clash:** `ProgressReported` is delta-only. The current remaining value requires the previous estimate and accumulated done; terminal contribution additionally depends on lifecycle state. A and B therefore write different numbers for the same event history while both can cite AD-22's wording.
- **Consequence:** systematic ancestor-total corruption after a second progress report, re-estimate, unit-invalid historical event, or terminal transition.
- **AD/source references:** AD-06 (`architecture.md:82-95`), AD-22 (`:320-337`), raw-act rule (`:782-800`). “The value is computable from the event alone” at `:324-326` conflicts with raw-act delta payloads and leaves the fold owner unbound.
- **Current-code corroboration:** `ProgressReported.cs:8-15` contains `DoneDelta`, not the resulting remaining value; `ReEstimated.cs:8-15` contains only the new estimate; `WorkItemState.cs:176-200` folds those events over prior state; `WorkItemRollUpProjection.cs:222-265` needs folded effort and terminal state to compute contribution.
- **Closing rule / disposition:** **Autofix.** State that only the descendant's Works projection computes and emits an absolute contribution snapshot; R4 never interprets Works domain payloads. Bind the snapshot fields and require one snapshot for every accepted state-changing descendant delivery, including terminal contribution `0`.

### ADV-0912-03 — Child creation, attachment evidence, and the freshness witness have no single authoritative act

- **Compliant construction A:** the parent-side `ChildSpawned` event is treated as creation evidence. The projection fabricates the child node from the complete spawn payload, and the registry transitions `Reserved -> Attached` immediately, exactly as AD-21 states.
- **Compliant construction B:** the Reactor translates `ChildSpawned` into `CreateWorkItem` and treats the child's `WorkItemCreated` stream event as the creation act and repair evidence, matching the PRD. It puts the registry witness on `WorkItemCreated` rather than on the parent event.
- **Clash:** AD-21 attaches on `ChildSpawned` but later defines release/repair in terms of whether the *child stream* shows creation evidence. AD-22 says the “item's creation evidence” records the registry sequence but does not name the carrier, and neither current event carries it. The register also does not say whether “registry sequence” is the registry envelope `SequenceNumber` or a Works payload ordinal.
- **Consequence:** one implementation has attached topology without a child aggregate; another has a child stream not recognized by the registry. A replay can create two equal-sequence child facts, a timeout can release a healthy edge, and R4 cannot verify the witness.
- **AD/source references:** AD-21 attachment/failure/repair (`architecture.md:275-308`), AD-22 witness (`:327-333`), two-sequence rule (`:791-796`). Current PRD FR-16 fixes the intended three writes at `prds/prd-works-2026-06-14/prd.md:287-296`, while the PRD addendum still says `Reserved -> Attached` on `ChildSpawned` and then creates the child (`addendum.md:38-45`).
- **Current-code corroboration:** `WorkItemAggregate.Handle(SpawnChild)` emits `ChildSpawned` and no `CreateWorkItem` (`src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:71-150`); `WorksHost.cs:70-84` registers no `ChildSpawned` handler; the projection fabricates child state from spawn facts (`WorkItemRollUpProjection.cs:112-123,146-157`). `WorkItemCreated.cs:8-18`, `ChildSpawned.cs:8-19`, and `CreateWorkItem.cs:18-29` carry no reservation identity or registry envelope sequence.
- **Closing rule / disposition:** **Discuss, then autofix the register and PRD addendum.** Bind one creation act and evidence chain. Recommended: Reactor translates `ChildSpawned -> CreateWorkItem(..., ReservationId, ReservationEnvelopeSequence)`; `WorkItemCreated` carries those additive fields; only a matching `WorkItemCreated` moves the registry to `Attached`; `ChildSpawned` is parent intent/evidence, not child creation. State explicitly that the witness is the registry event envelope `SequenceNumber`.

### ADV-0912-04 — The AD-22 watermark test can acknowledge a still-Reserved edge as empty ancestry

- **Compliant construction A (registry projection unit):** it projects `EdgeReserved` at registry sequence `r`; the edge remains `Reserved` and is excluded from ancestor enumeration until later `ChildSpawned` evidence produces `Attached` at sequence `a`.
- **Compliant construction B (fan-out unit):** on the child's first state-changing delivery, it checks the exact AD-22 predicate `registry watermark >= r`. The watermark is `r`, so ancestry is “resolved”; it enumerates `Attached` edges, finds none, performs a no-op fan-out, and acknowledges the delivery.
- **Clash:** each unit follows the literal rules, but the freshness witness proves only that the reservation was observed, not that its final attachment state was observed. A different delivery order (registry reaches `a` first) produces the expected ancestor slot. The same events therefore converge to different read state depending on cross-stream arrival order.
- **Consequence:** a child with only its creation event can remain permanently absent from every ancestor total. No later child event is guaranteed to repair the lost contribution, and shared rebuild becomes routine repair despite AD-22 forbidding that role.
- **AD/source references:** AD-21 says only `Attached` edges are enumerated (`architecture.md:293-302`); AD-22 defines resolved ancestry solely as watermark `>=` reservation sequence and says no partial ack or silent skip (`:327-335`). Those rules are jointly insufficient.
- **Current-code corroboration:** this protocol is not yet implemented: no Work-Tree Registry command/event/state type exists under `src/`; current ordinary projection is still per-aggregate (`WorkItemProjectionDispatcher.cs:96-128`) and current shared rebuild derives edges from `ChildSpawned`/`WorkItemCreated.Parent` (`WorkItemSharedRebuildManifestBuilder.cs:159-194`). The absence means there is no hidden code convention that resolves the ambiguity.
- **Closing rule / disposition:** **Autofix before the registry story.** The child creation witness must name the matching `Attached` transition sequence, and ancestry is resolved only when the registry view contains that exact attachment in `Attached` state with watermark `>= attachedSequence`. A `Reserved` or missing matching attachment is retryable/not-acknowledged. Alternatively, an `EdgeAttached` handler must deterministically backfill every current descendant contribution before it acknowledges attachment.

## High findings

### ADV-0912-05 — A released reservation does not fence an already-issued late `SpawnChild`

- **Compliant construction A:** the registry timeout adapter fires after the configured bound and writes `Reserved -> Released`, as AD-21 requires.
- **Compliant construction B:** the Reactor/platform eventually delivers the already-issued, authenticated `SpawnChild`. The pure parent aggregate has the registry-derived ancestry snapshot but no reservation identity, status, lease, or epoch to validate, so it accepts and emits `ChildSpawned`.
- **Clash:** the registry now says no edge exists while the parent and possibly child streams record one. Neither unit violated its own rule; the saga lacks a fence across its non-atomic legs.
- **Consequence:** asymmetric topology, repair suppression, orphan child creation, or a later child-completion resume/cascade based on evidence the registry has terminally released.
- **AD/source references:** AD-21 timeout and repair (`architecture.md:293-308`) and command shape (`:279-290`); AD-09 at-least-once/reorder (`:119-126`). The rule restricts origin but carries no reservation token to the target (`:311-316`).
- **Current-code corroboration:** `SpawnChild.cs:19-32` and `ChildSpawned.cs:8-19` have no reservation token; `WorkItemAggregate.cs:71-150` cannot ask the registry and therefore cannot reject a stale reservation. No reservation-timeout type exists in current `src/`.
- **Closing rule / disposition:** **Discuss then autofix.** Carry a stable `ReservationId` plus current fencing token on every saga leg; the target must present proof of the still-current reservation at an SDK/platform admission stage before aggregate dispatch. Define `Released` as terminal for that token; late evidence produces an explicit orphan outcome and compensating policy, never silent re-attachment.

### ADV-0912-06 — Registry identity, topic, route, and payload identity are unbound

- **Compliant construction A:** implement one registry aggregate as `{tenant}:work-tree:{tenant}`, publish `work-tree.events`, and expose `/work-tree/events`.
- **Compliant construction B:** keep the domain `work`, use a reserved aggregate id such as `tree`, publish on existing `work.events`, and distinguish registry events by type.
- **Clash:** both are tenant-scoped, event-sourced single-writer actors and satisfy AD-21. Yet R3 routing, domain registration, envelope identity checks, replay, topic ACLs, and actor-key collision rules differ. A processor built for one silently rejects or never receives the other.
- **Consequence:** the reserve reactor never runs, or a registry actor collides with a legal Work Item id; attachment stops without an aggregate-level error.
- **AD/source references:** AD-21 (`architecture.md:272-284`), R3 (`:262`), open namespace item VAL-H09 (`:448`), data boundary (`:984-986`).
- **Current-code corroboration:** the host subscribes only to `work.events` at `/work/events` (`WorksHost.cs:66-84`); `WorksDomainEventProcessor.cs:40-60,122-138,186-197` accepts domain `work` and a closed WorkItem-shaped identity table; `WorksEventIdentity.cs:11-38` expects `TenantId` plus `WorkItemId`; `AggregateIdentity.PubSubTopic` derives a different topic outside tenant `system` (`references/Hexalith.EventStore/.../AggregateIdentity.cs:88-94`).
- **Closing rule / disposition:** **Autofix with VAL-H09.** Bind the registry's exact `(tenant, domain, aggregateId)`, topic override, subscription route, aggregate-id property, domain-service registration, and reserved-id rule. Put the row in the namespace/ownership table before adding the durable registry types.

### ADV-0912-07 — Four topology consumers can remain mutually compliant while using different authorities

- **Compliant construction A:** AD-22 fan-out and R7 cascade enumerate only registry `Attached` edges because AD-21 calls the registry authoritative.
- **Compliant construction B:** child-completion recovery and shared rebuild treat `WorkItemCreated.Parent` and `ChildSpawned` as independently sufficient topology evidence, because AD-21 explicitly calls these records attachment/repair evidence and does not bind one enumeration interface for every consumer.
- **Clash:** a parented `CreateWorkItem` can exist without a registry reservation; A sees no edge, B sees an edge. Conversely, a registry edge can be attached before a child stream exists; A sees it, a child-stream reader does not.
- **Consequence:** roll-up, cascade, child-completion resume, and rebuild operate on different trees; tenant closure and repair suppression are applied inconsistently.
- **AD/source references:** AD-21 authority and repair (`architecture.md:275-316`), AD-22 (`:320-342`), R4/R7 (`:263,266`). AD-24 restricts direct `SpawnChild`, but does not restrict a non-root `CreateWorkItem` (`:377-383`).
- **Current-code corroboration:** `CreateWorkItem.cs:18-29` exposes public caller-fed `Parent`/ancestor/depth facts; `WorkItemAggregate.cs:39-66` persists them; child-completion uses `WorkItemCreated.Parent` (`StreamReadingChildCompletionAwaitingParentSource.cs:37-59`); cascade uses parent `ChildSpawned` (`StreamReadingCascadeDescendantSource.cs:17-23,62-72`); shared rebuild uses both (`WorkItemSharedRebuildManifestBuilder.cs:159-194`).
- **Closing rule / disposition:** **Discuss public API compatibility, then autofix.** Bind one registry enumeration interface as the sole runtime source for fan-out, cascade, child-completion, and rebuild. Treat item-stream references as evidence only. Allow parented `CreateWorkItem` only as the fenced Reactor leg; external creates are roots. Require a one-time tenant migration before enabling registry-backed consumers.

### ADV-0912-08 — Transport idempotency remains open at the exact seam AD-21 depends on

- **Compliant construction A:** derive `MessageId`/causation by concatenating the causal event fields and command target; reuse it on retry.
- **Compliant construction B:** hash a canonical tuple into a bounded `MessageId` and also supply EventStore's semantic `IdempotencyKey`/intent adapter, with a fresh correlation id for each operator-visible attempt.
- **Clash:** both are deterministic and at-least-once safe inside their unit, but the same logical saga intent receives different dedup identities across originators or after ownership moves to R11. EventStore will admit both, and raw concatenation can be rejected for legal aggregate ids or length.
- **Consequence:** duplicate reserve/spawn/release acts, rejected recovery commands, or different replay outcomes after the R11 migration.
- **AD/source references:** AD-02 explicitly excludes transport idempotency (`architecture.md:49-55`); R11 allocates but does not bind it (`:270`); AD-21 makes VAL-H10 a prerequisite (`:313-316`); VAL-H10 remains open (`:449`).
- **Current-code corroboration:** cascade and child-completion concatenate raw ids (`CascadeCommands.cs:17-44`; `ChildCompletionResume.cs:15-27`), while date resume hashes (`DateResume.cs:23-41`; `DateReminderName.cs:35-60`). The submitter maps causation to `MessageId` and leaves `IdempotencyKey` null (`EventStoreGatewayWorkCommandSubmitter.cs:19-32`). EventStore limits `MessageId` to 128 alphanumeric/hyphen characters while aggregate ids allow dot/underscore and 256 characters (`SubmitCommandRequestValidator.cs:23-41,57-62`) and now exposes `IIdempotencyIntentAdapter` with a retention tier (`IIdempotencyIntentAdapter.cs:5-25`).
- **Closing rule / disposition:** **Autofix as a new AD before any registry contract is drafted.** Bind one canonical tuple, hash/encoding, maximum length, causal envelope field, correlation semantics, EventStore `IdempotencyKey` use/non-use, retention tier, and replay result. Require every R6/R7/R11 originator to call the same pure function.

### ADV-0912-09 — Shared-rebuild exclusion is a choice, not a protocol

- **Compliant construction A:** Platform quiesces the `work.events` subscription from inventory capture through Commit and then resumes it.
- **Compliant construction B:** EventStore leaves delivery live but fences named projection handlers by delivery-reservation token; fan-out handlers retry when fenced.
- **Clash:** both satisfy AD-16's “quiesced or platform-fenced” language, but the subscription is not the only writer. The projection actor can call `/project`/named handlers independently, and a per-delivery reservation token does not identify the shared rebuild generation. A and B can interleave current-key writes with a staged manifest and choose different catch-up watermarks.
- **Consequence:** Commit promotes a generation missing acknowledged live writes, or a post-Commit stale dispatch overwrites rebuilt state.
- **AD/source references:** AD-16 (`architecture.md:184-194`), R4 (`:263`), AD-22 says fan-out writers “follow the fence” despite the protocol remaining open (`:327-335`), VAL-H08 (`:447`).
- **Current-code corroboration:** Works explicitly documents that its rebuild handler does not arbitrate a concurrent live writer (`docs/work-roll-up-projection.md:75-84`); ordinary persisted writes carry no rebuild epoch (`WorkItemProjectionDispatcher.cs:430-451`). EventStore's current `IAsyncDomainProjectionRebuildHandler` produces candidate operations (`DomainProjectionRebuildPlan.cs:5-36`), while its projection reservation fencing is per delivery (`ProjectionDeliveryReservation.cs`, `ProjectionDeliveryIdempotencyCoordinator.cs`) rather than a Works-bound capture-through-Commit epoch.
- **Closing rule / disposition:** **Discuss with EventStore/Platform, then autofix AD-16.** Bind an epoch per `(tenant, projection family)`; name every admitted writer; bind inventory watermark, Begin/Commit/abort state transitions, non-ack behavior for stale epochs, and exact catch-up proof. The platform test must inject writes at each capture/stage/commit boundary.

### ADV-0912-10 — Cascade checkpoint state assumes a single writer without fencing one

- **Compliant construction A:** one R7 replica owns a cascade and uses LWW checkpoint saves; statuses progress `Pending -> Attempted -> Completed`.
- **Compliant construction B:** multiple platform replicas can replay the same durable checkpoint after failover. Each reads the same target list and saves its local copy after every attempt, relying on target-command idempotency.
- **Clash:** both are checkpoint-driven and re-readable as AD-10/R7 require. Without replica ownership, ETag/CAS, or a lease/fence, B can overwrite another replica's completed targets with older `Attempted`/`Pending` state or mark the checkpoint complete from a stale snapshot.
- **Consequence:** duplicate storm, indefinitely regressing checkpoints, lost target progress, or an incomplete cascade removed from recovery discovery.
- **AD/source references:** AD-10 (`architecture.md:128-135`), R7 (`:266`), VAL-H06 explicitly leaves monotonic states/CAS/replica ownership for later (`:445`).
- **Current-code corroboration:** `CascadeCheckpoint.cs:21-40` has statuses but no version/owner/fence; `CascadeDispatcher.cs:153-210` performs read/modify/save cycles; `ReadModelCascadeCheckpointStore.cs:29-80` reads then uses plain `SaveAsync`, assumes a single writer in its comment (`:7-13`), and guards only completed-to-incomplete based on a non-atomic prior read.
- **Closing rule / disposition:** **Autofix before R7 migration.** Bind a single-owner lease with monotonically increasing fencing token or one ETag/CAS merge that is monotonic per target and for overall completion. A save with a stale token must fail retryably and never remove recovery discovery. Add a two-replica crash/failover acceptance test.

### ADV-0912-11 — Tenant delegation and command-origin policy have no carrier or enforcement locus

- **Compliant construction A:** Platform derives tenant delegation from signed OIDC/SPIFFE claims and passes only verified tenant/actor in a server-owned execution context.
- **Compliant construction B:** the SDK client writes `hx-delegated-tenant`/origin fields into `SubmitCommandRequest.Extensions`, while Dapr mTLS app id authenticates the workload.
- **Clash:** AD-23 permits either as an “explicit, auditable tenant-delegation context.” The receiving gateway can ignore the sender's chosen carrier. AD-24 also restricts particular command types (`SpawnChild`) to one workload, but Dapr access control is route/app based, not command-type based; a broadly admitted Works workload can submit any Works command.
- **Consequence:** production denies valid Reactor calls or accepts forged cross-tenant/internal-only commands. A compromised reminder/cascade component inherits the ability to cancel, expire, attach, or mutate any item in a delegated tenant.
- **AD/source references:** AD-23 internal delegation and baseline authorization (`architecture.md:344-368`), AD-24 exclusive originators (`:370-395`), R10/R11 (`:269-270`).
- **Current-code corroboration:** `WorkCommandSubmission` carries tenant and ids but no authenticated principal/delegation type (`IWorkCommandSubmitter.cs:27-45`); `EventStoreGatewayWorkCommandSubmitter.cs:23-30` supplies no extensions. Local EventStore access control grants app `works` every POST route (`src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.yaml:18-28`), demonstrating the current seam's granularity. `SubmitCommandRequest` accepts a free-form extensions dictionary (`references/Hexalith.EventStore/.../SubmitCommandRequest.cs:17-26`).
- **Closing rule / disposition:** **Discuss with Platform, then autofix AD-23/24.** Bind a server-derived, signed delegation context; exact claim/extension names are less important than declaring the gateway as sole validator. Bind `CommandOriginPolicy[(domain, commandType)] -> allowed workload identities`, evaluated after authentication and tenant derivation but before dispatch. Domain payload identity stays an assertion only. Add wrong-origin and wrong-delegated-tenant negatives per internal command family.

### ADV-0912-12 — “Self-targeted” reminders can be hosted by two different actors, and expiry has no typed recovery intent

- **Compliant construction A:** keep today's dedicated `DateReminderActor` in the Works service, hashed per `(tenant,item)`, and add expiry/reservation reminder kinds there.
- **Compliant construction B:** R6 registers reminders on the EventStore aggregate actor that already owns `{tenant}:work:{id}`; “self-targeted” means the authoritative item actor, while Works supplies only domain intents.
- **Clash:** AD-11/R6 never name actor type, app id, actor id, state owner, or whether the SDK is hosted in Works or EventStore. AD-24 permits register/cancel only from the Works identity, which denies B. Reconciliation can inspect only the actor family it chose. Separately, “same indexed protocol” lets A reuse the current date-await index while B creates an expiry index; the current entry has no intent kind, so A would reissue `ResumeWorkItem` for an overdue expiry.
- **Consequence:** duplicate or orphan reminders, a production access-control denial masked by sandbox, missed expiry, or a missed expiry incorrectly replayed as resume.
- **AD/source references:** AD-11 (`architecture.md:137-149`), R6 (`:265`), AD-24 (`:377-395`), AD-25 identity/recovery (`:397-416`).
- **Current-code corroboration:** `DaprDateReminderScheduler.cs:17-38` targets a dedicated hashed `DateReminderActor`; that actor stores registrations and submits resume (`DateReminderActor.cs:17-76`). `PendingDateAwait.cs:10-14` has no kind; `PendingDateAwaitProjection.cs:24-81` consumes only suspend/resume/terminal events; `DateReminderReconciler.cs:101-130` always builds a date resume. `ExpireWorkItem.cs:12-14` has no schedule generation or firing key.
- **Closing rule / disposition:** **Autofix after choosing the actor host.** Bind one actor type/app/identity and one typed pending-intent DTO `{Kind, Tenant, Target, FireInstant, ScheduleOrReservationToken}` for date resume, expiry, and reservation timeout. Name the indexes and reconciliation owner. Every callback carries the current schedule/reservation token; stale callbacks are no-ops before state mutation.

### ADV-0912-13 — AD-13 and AD-17 prescribe opposite state-mutation results for the same resume

- **Compliant construction A:** an aggregate team follows AD-13's binding rule: `ResumeWorkItem` with no current match is a no-op; duplicate is a no-op.
- **Compliant construction B:** a lifecycle/test team follows AD-17's rule that `docs/lifecycle-transition-matrix.md` is authoritative; it persists `WorkItemTransitionRejected` for a non-matching resume and reserves no-op only for repetition of the last consumed condition.
- **Clash:** no implementation can obey both ADs. A advances no envelope position; B appends a rejection that consumes an EventStore envelope position. The register's own precedence rule cannot resolve a conflict inside the register.
- **Consequence:** divergent event histories, projection offsets, audit records, idempotency results, and client-visible outcomes under at-least-once delivery.
- **AD/source references:** AD-13 (`architecture.md:159-165`) versus AD-17 (`:196-202`). Current PRD FR-15 says the B behavior (`prds/prd-works-2026-06-14/prd.md:280-285`); the addendum explicitly lists the owed architecture correction (`addendum.md:61-63`).
- **Current-code corroboration:** `WorkItemAggregate.Handle(ResumeWorkItem)` rejects a non-match and no-ops only the consumed repeat (`src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:223-250`); `docs/lifecycle-transition-matrix.md:154-171` says accepted resume clears the set and non-match is a rejection.
- **Closing rule / disposition:** **Autofix immediately.** Amend AD-13 to the current PRD/matrix behavior: while suspended, no match is a domain rejection; after resume, only the exact consumed condition is a no-op; every other resume is a rejection. Keep the AD ID stable.

## Medium findings

### ADV-0912-14 — Slot removal and repair suppression cannot be expressed in AD-06's sequence space

- **Compliant construction A:** after an edge is released or quarantined, retain the descendant slot at its last value but mark the ancestor unavailable/degraded; only descendant-stream events may change the slot.
- **Compliant construction B:** delete/tombstone the slot from a registry repair event so the detached/released descendant no longer contributes.
- **Clash:** A obeys AD-06's “slot written only from its own descendant stream” but can retain a stale contribution. B obeys AD-21 repair authority but has no descendant sequence for LWW and therefore invents a cross-stream comparison. AD-06 binds no attachment identity or slot lifecycle.
- **Consequence:** repaired/released topology produces different totals, child lists, and availability state across rebuild and live delivery.
- **AD/source references:** AD-06 (`architecture.md:82-95`), AD-21 repair (`:303-308`), AD-22 unavailable semantics (`:333-335`).
- **Current-code corroboration:** `WorkItemRollUp.cs:37-48` separates child ids/diagnostics but has no slot or attachment id; current projection keeps terminal children in `ChildKeys` while contribution becomes zero (`WorkItemRollUpProjection.cs:234-265`). Current docs assert no detach event exists (`docs/work-roll-up-projection.md:86-91`).
- **Closing rule / disposition:** **Autofix with ADV-0912-01.** Key a slot by `(descendantId, attachmentId)` and bind an independent registry-controlled attachment tombstone sequence. Descendant contribution LWW and attachment lifecycle LWW must never compare their counters. Define child-list and degraded/unavailable semantics for tombstoned and quarantined slots.

### ADV-0912-15 — Global control-plane namespaces can collide across Works and EventStore

- **Compliant construction A:** Works keeps its singleton recovery registries under the reserved tenant-like token `tenants`.
- **Compliant construction B:** EventStore uses `system` for platform-level topics/identities and migrates generic R6/R7 registries there.
- **Clash:** each unit reserves only its own control-plane token. A real tenant accepted by one can collide with the other's global key/topic convention, and moving recovery ownership creates two discovery registries.
- **Consequence:** cross-tenant recovery omission, global index overwrite, or tenant data entering a platform topic.
- **AD/source references:** AD-15 explicitly leaves this open (`architecture.md:174-182`), VAL-H09 (`:448`), data boundary (`:984-986`).
- **Current-code corroboration:** Works reserves only `tenants` and has a byte-identical collision at `projection:works:pending-date-await:tenants` (`WorksReadModelKeys.cs:57-105`); EventStore gives tenant `system` special topic semantics (`AggregateIdentity.cs:88-94`).
- **Closing rule / disposition:** **Autofix an interim rule; complete VAL-H09 before migration.** Reserve both tokens at ingress now, select one canonical platform namespace for new global registries, and list every global/tenant key, topic, owner, retention policy, and migration rule in the namespace table.

### ADV-0912-16 — “Additive tolerant” does not bind reader/writer compatibility for the new cross-repository contracts

- **Compliant construction A:** Works serializes enums as strings and treats an unknown enum value as a terminal decode error; unknown object fields are ignored.
- **Compliant construction B:** an SDK consumer maps an unknown enum to a default/`Unknown` value and continues, while requiring newly added identity fields for authorization or fencing.
- **Clash:** both can describe themselves as additive/tolerant. Registry edge states, reminder kinds, slot DTOs, and reservation tokens cross repository/version boundaries; rolling Works and Platform in different orders yields reject-vs-default and required-vs-optional behavior.
- **Consequence:** a safe additive deployment in one order becomes a poisoned subscription, skipped security field, or silently misclassified state in the other.
- **AD/source references:** AD-01 evolution rule (`architecture.md:38-47`), AD-21 new durable types (`:279-284`), VAL-H11 remains open with a lapsed Story 1.5 condition (`:450`).
- **Current-code corroboration:** the current event catalog uses `JsonStringEnumConverter` (for example `AwaitConditionKind.cs:5-6`) and golden payloads prove existing bytes, but no checked compatibility matrix defines old-reader/new-writer, new-reader/old-writer, unknown enum/type, required additive security field, or rollout order. The PRD addendum states AD-21 will add several durable contracts (`addendum.md:26-30`).
- **Closing rule / disposition:** **Autofix the revisit condition and discuss compatibility policy.** Bind a per-contract matrix, unknown enum/type behavior, required-field rollout pattern, and Platform/EventStore/Works upgrade order before AD-21 types enter the catalog. Security/fence fields require dual-read/dual-write or admission gating; they cannot be silently defaulted.

## Prior-gate delta — comparison performed after the fresh assessment

The target spine itself has not changed since the prior gate: `architecture.md` still reports `updatedAt: 2026-09-06`, and its filesystem modification time is 2026-09-06. Therefore none of the 2026-09-08 adversarial holes was closed in the binding register.

| Prior 2026-09-08 finding | 2026-09-12 disposition | Delta |
|---|---|---|
| ADV-01 persisted roll-up shape | **Open** → ADV-0912-01 | EventStore now has a named async projection seam, which makes the owner boundary easier to implement, but no Works slot DTO/merge/key contract was bound. |
| ADV-02 contribution source | **Open** → ADV-0912-02 | No spine or payload change; raw `ProgressReported` remains delta-only. |
| ADV-03 child creation/witness | **Open** → ADV-0912-03 | The 2026-09-08 PRD now explicitly says registry reserve → parent `ChildSpawned` → child `WorkItemCreated`, sharpening the conflict with AD-21's evidence wording; the witness still has no carrier. |
| ADV-04 registry identity/routing | **Open** → ADV-0912-06 | No registry types or routes exist; no namespace row was added. |
| ADV-05 transport idempotency | **Open** → ADV-0912-08 | EventStore now exposes a mature idempotency-intent/retention seam, but Works still sends only `MessageId = CausationId`; the architecture still makes VAL-H10 a prerequisite without binding it. |
| ADV-06/07 reminder ownership and expiry protocol | **Open** → ADV-0912-12 | The post-gate reminder fixes improve date-recovery liveness (`5c86eab`) but do not add expiry kind, schedule token, actor-host ownership, or registry-timeout semantics. |
| ADV-08 topology sources | **Open** → ADV-0912-07 | PRD now calls the registry sole authority; code still has three independent evidence consumers and public parented create. |
| ADV-09/10 reservation duplicate/timeout | **Open**, reframed → ADV-0912-05 | Fresh attack isolates the stronger cross-unit failure: release and late authenticated spawn are both legal because no reservation fence reaches the parent. |
| ADV-11 slot removal | **Open** → ADV-0912-14 | No slot lifecycle or second sequence space was bound. |
| ADV-12 shared rebuild fence | **Open** → ADV-0912-09 | EventStore gained projection-delivery fencing and named rebuild seams, but those do not themselves bind the Works capture-through-Commit epoch or writer set. |
| ADV-13 control-plane tenant ids | **Open** → ADV-0912-15 | Works still reserves only `tenants`; EventStore still special-cases `system`. |
| ADV-14 delegation carrier | **Open** → ADV-0912-11 | No request carrier or command-type origin policy was added. |
| Prior gate's AD-13 contradiction | **Open** → ADV-0912-13 | PRD/addendum and code now make the intended rejection behavior even clearer; binding AD-13 remains wrong. |
| — | **New** → ADV-0912-04 | The previous review noted the missing witness carrier but did not isolate the `Reserved` watermark/ack race. |

## Gate conclusion

The architecture has strong high-level ownership, purity, and tenant-isolation intent, but the AD register is not yet a consistency contract for the next independently built units. The minimum safe Update is:

1. Bind the absolute contribution producer plus the persisted slot/key/merge/read contract (ADV-0912-01/02/14).
2. Bind one child-creation evidence chain, an `Attached`-state freshness witness, and a reservation fence carried through every saga leg (ADV-0912-03/04/05).
3. Bind registry identity/routing, sole topology enumeration, and the shared idempotency function before drafting the registry story (ADV-0912-06/07/08).
4. Bind shared-rebuild and cascade fencing plus internal delegation/origin enforcement before Story 4.9 acceptance (ADV-0912-09/10/11).
5. Fix AD-13 immediately; bind reminder intent/actor ownership, namespace governance, and schema rollout before adding the new durable types (ADV-0912-12/13/15/16).

