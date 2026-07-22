# Test Automation Summary — Story 4.4 (Resolve the Tenant's What's Next Queue)

Workflow: `bmad-dev-story` followed by a `bmad-qa-generate-e2e-tests` QA gap-filling pass. Framework reused:
**xUnit v3 + Shouldly** (unit, architecture) and **FsCheck**
(property). Story 4.4 is the **pure read-side** realization of **FR-20** (the tenant "what's next" query): a
tenant-scoped projection + read model + query-shaping that returns a tenant's `Queued`+`Assigned` items
ordered by Priority → earliest Due Date → identity (neither sorts last), with a pure query-side
authorization seam and a notifier change-signal. Following the Epic-3 pattern, it ships the **pure**
projection logic now and defers the runtime adapter (live `IDomainQueryHandler`/`IReadModelStore`/
`IProjectionChangeNotifier` wiring → Stories 4.5/4.6, gated on the EventStore projection-model
reconciliation). The claimable pool **is** a read projection (AR-10/B1), not an authoritative queue
aggregate. There is no UI/HTTP/MCP surface.

Story 4.3 final baseline was **562** green tests (UnitTests 449, IntegrationTests 80, ArchitectureTests 31,
PropertyTests 2). The Story 4.4 dev-story pass added **+28** tests (+25 unit, +2 architecture, +1 property),
reaching **590** green: UnitTests 474, IntegrationTests 80, ArchitectureTests 33, PropertyTests 3. The
follow-up `bmad-qa-generate-e2e-tests` QA pass then added **+9** unit tests to close residual AC/design-
decision gaps, raising the total to **599** green: UnitTests 483, IntegrationTests 80, ArchitectureTests 33,
PropertyTests 3. **No production code was changed by the QA pass** — only the unit-test file was extended;
the v1 catalog (`WorkItemV1Catalog.Count` 36) and golden corpus are unchanged.

**Production code is read-side only.** Story 4.4 adds three pure production types plus a change-signal
record — **no** event, command, rejection, or value-object type. The durable wire surface is frozen:
`WorkItemV1Catalog.Count` stays **36** (14 events / 14 commands / 8 rejections) and the golden corpus is
byte-unchanged (DC3). `WhatsNextItem` is a plain `System.Text.Json` record, not a
`[PolymorphicSerialization]` catalog type.

**Reconciliation (Task 1) — confirmed before any change.**

- The canonical pure read-side precedent is `WorkItemRollUpProjection` (per-item
  `SortedDictionary<long, IEventPayload>` LWW, tenant-ordinal guards, `EventMatchesDelivery`,
  rebuild-on-change) + the `WorkItemRollUp`/`OwnRemaining`/`RolledRemaining` read models — mirrored here.
- The delivery envelope `WorkItemRollUpEvent(TenantId, WorkItemId, long Sequence, IEventPayload)` is reused
  unchanged (DC6); no parallel envelope was forked.
- `WorkItemId.Value` is the **raw inner id** (`AggregateIdentity(...).AggregateId`), **not** tenant-composed
  — so colliding inner ids across tenants are byte-identical, making the explicit `(tenant, id)` key
  essential and the id tiebreak a strict total order *within* a tenant.
- `WorkItemExecutorBindingView`'s XML doc named Story 4.4 as its scaled populator; the what's-next read
  model carries `ExecutorBinding?` directly (no executor-kind discriminator).
- `IExecutorRouter` remains an abstraction with **no** wired impl (`BoundaryPortTests`); the what's-next
  query takes **no** routing/eligibility/authority input; `Projections` still references only `Contracts`
  (+ `EventStore.Contracts`) — the live query/notifier runtime stays in the deferred substrate adapter.

## Production code changed

- **New:** `src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs` — the read-model contract (Task 2): a
  plain `System.Text.Json` `sealed record` exposing tenant/work-item identity, status, the ordering inputs
  (Priority + DueDate), `ExecutorBinding?` (zero kind branching), `OwnRemaining?`, distinct
  `RolledRemaining?` + `RolledRemainingByUnit` (composed where available), `AwaitConditions`, and a
  `LatestAcceptedSourceSequence` watermark. No UI-specific types.
- **New:** `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs` — the pure projection
  (Tasks 3/6): `Project(delivery) → WhatsNextProjectionChange` and
  `WhatsNext(tenant, rollUpLookup?) → IReadOnlyList<WhatsNextItem>`. Per-item `(tenant, id)` key,
  `SortedDictionary` LWW, eligibility = `{Queued, Assigned}`, queued items keep their last binding,
  cross-tenant ordinal guard, rolled-remaining composed only from a supplied roll-up (DC7), and a stable
  `ProjectionType = "works-whats-next"` token.
- **New:** `src/Hexalith.Works.Projections/Strategies/WhatsNextOrdering.cs` — the total ordering comparator
  (Task 4, DC2): Priority rank (absent/`Unknown` last) → Due Date (absent last) → `WorkItemId.Value`
  ordinal.
- **New:** `src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs` — the pure query-side
  authorization filter (Task 5, DC4): authoritative-tenant check + optional caller predicate, fail-closed,
  mirroring `ProjectQueryTenantFilter`.
- **New:** `src/Hexalith.Works.Projections/Models/WhatsNextProjectionChange.cs` — the notifier change-signal
  record (Task 6, DC1).

## Tests added

### Unit tests (`tests/Hexalith.Works.UnitTests`) — +25 cases

- [x] `WhatsNextQueueProjectionTests` — **new file, +25 cases** covering Tasks 2–6:
  - **Task 2 (AC #3):** own/rolled remaining stay distinct types on the model; the read model exposes only
    owned data with no UI/executor-kind property surface.
  - **Task 3 (AC #1/#3/#5):** only `Queued`+`Assigned` are returned; requeued rejection is eligible while a
    non-requeue rejection is not; a queued item keeps its last `ExecutorBinding`; own-remaining is derived
    and rolled-remaining is composed only where a roll-up lookup is supplied (else null/empty); a
    unit-mismatched progress retains the last valid own-remaining; **colliding inner ids across two tenants
    with identical schedules never cross and flip with the queried tenant**; tenant/id-mismatched payloads
    and non-positive sequences are ignored; out-of-order + duplicated delivery converge to the same queue
    and order.
  - **Task 4 (AC #2):** priority ordering across all four ranks; earliest-due-date within one priority;
    `WorkItemId.Value` ordinal tiebreak within identical priority+due-date; only-priority before only-due-
    date; **neither-present sorts last**; absent/`Unknown` priority both rank last.
  - **Task 5 (AC #1):** wrong-tenant items dropped; a caller predicate removes only the rejected item;
    null/empty authoritative tenant is fail-closed (empty list / null single).
  - **Task 6 (AC #4):** a change is reported when an item enters or leaves the pool or is rescheduled to a
    new priority; **no** change for binding- or remaining-only updates, or duplicate-sequence delivery;
    the `"works-whats-next"` projection token is stable.

### Architecture tests (`tests/Hexalith.Works.ArchitectureTests`) — +2 cases

- [x] `ScaffoldGovernanceTests.P0_WorkItemSurfaceHasNoWhatsNextRoutingEligibilityOrLiveSurfaceTypeAndCatalogStays36`
  — **new fitness test** (Task 8, AC #1/#4 + DC3). Mirrors the 4.2/4.3 declared-type-name scans: forbids a
  what's-next routing/surface vocabulary (`RoutingEngine`, `EligibilityScore`/`EligibilityEngine`/
  `EligibilityFilter`, `EscalationLadder`, `ExecutorRanking`, a concrete `*Router` impl with the
  abstraction-only `IExecutorRouter` port excluded, and surface types `*DataGrid`/`*Hub`/`*SignalR*`/
  `*WebShell`/`*MailSurface`/`*EmailSurface`/`*McpTool`/`*Chatbot`) and is paired with the reflection-based
  frozen-catalog assertion `polymorphicCatalogCount.ShouldBe(36)`.
- [x] `ScaffoldGovernanceTests.P0_WorkItemKernelDoesNotLogPayloadsOrPii` — **new fitness test** (Task 7,
  AC #5/NFR-6). Scans the kernel (`Contracts`, `Server`, `Projections`) for any logging symbol (`ILogger`,
  `LoggerMessage`, `Log*`, `Console.Write`) and asserts none exist, so payloads/PII/obligation text can
  never be logged from the pure core. The existing guardrails (`BoundaryPortTests`,
  `DependencyDirectionTests`, `EventStoreApiSurfaceCharacterizationTests`, `BoundaryDecisionRecordTests`,
  `LifecycleTransitionMatrixDocTests`, the rest of `ScaffoldGovernanceTests`) are preserved unchanged and
  green.

### Property tests (`tests/Hexalith.Works.PropertyTests`) — +1 case

- [x] `WhatsNextOrderingConvergencePropertyTests` — **new file, +1 property** (Task 9, AC #2/#5; FsCheck
  wiring mirrors `WorkItemRollUpConvergencePropertyTests`). For any generated set of items (random
  priorities, due dates, and eligibility) and any permutation + duplication of their delivery, the tenant
  what's-next queue converges to the **same ordered list** (order-tolerant), the ordering is a **strict
  total order** (every adjacent pair compares strictly less — no two distinct items compare equal under the
  full comparator including the id tiebreak), and a colliding foreign-tenant item never leaks across
  tenants.

## Documentation

- [x] `docs/whats-next-projection.md` — **new** (mirrors `docs/work-roll-up-projection.md`): the eligible
  set, the ordering rule + exact comparator (DC2), the read-model field contract, rolled-remaining
  composition (DC7), tenant scoping + query-side authorization as distinct controls, the idempotent/order-
  tolerant rebuild posture (checkpoint-per-aggregate), the notifier seam + deferred runtime wiring, and the
  no-logging privacy posture.
- [x] `docs/boundary-decision-record.md` — added a Story 4.4 note in *Notes and cross-references*: the
  what's-next queue is a pure read projection + query-shaping; ordering is Priority→DueDate→identity
  (neither last); tenant scoping + query-side authorization are distinct controls; Works adds no
  routing/eligibility type and no durable catalog type (stays 36); the notifier requirement references the
  substrate seam; live query/notifier runtime is the deferred Aspire wiring (4.5/4.6). The module/seam
  enumeration is preserved (`BoundaryDecisionRecordTests` green).

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

The QA pass traced AC #1–#5 and design decisions DC1/DC4/DC5/DC7 against the dev-story coverage. The
dev-story pass already covered the eligibility set, the full ordering matrix, colliding-id cross-tenant
isolation, the authorization filter *in isolation*, and the change-signal basics. Nine genuine,
non-redundant gaps were found and auto-applied as new `[Fact]` cases (UnitTests 474 → **483**). This is a
pure event-sourced read-side library with **no HTTP/API or UI surface**, so the "API" lane is the
projection/query methods (`Project`/`WhatsNext`/`WhatsNextQueryAuthorization`) and the "E2E" lane is the
deterministic event-delivery → read-model flow — both already present; the gaps were assertion-level. No
production code changed; catalog stays **36**.

- [x] **AC #1 — query-side authorization composes with tenant scoping as a *distinct* control (unit, +1).**
  `Query_side_authorization_composes_with_tenant_scoping_as_a_distinct_control` projects two tenants with
  *colliding* inner ids, confirms the projection's own tenant scoping returns only tenant A, then proves the
  authorization filter independently drops a foreign item that reached the list (preserving survivor order)
  and a caller predicate narrows further — AC #1's "query-side authorization is applied *in addition to*
  tenant scoping" verbatim, which the dev-story pass only tested with the two controls in isolation.
- [x] **AC #1/#3 — suspend → resume keeps an item out of the pool until re-queued (unit, +1).**
  `A_suspended_then_resumed_item_stays_out_of_the_pool_until_it_is_re_queued` adds the missing
  `WorkItemResumed` projection coverage: Claim→Suspend→Resume leaves the item InProgress (ineligible) with
  its await conditions cleared, and a later `WorkItemAssigned` legitimately re-admits it (resume is not
  terminal).
- [x] **DC5/AC #3 — ReEstimated own-remaining derivation (unit, +1).**
  `Re_estimate_establishes_then_updates_own_remaining_and_a_unit_mismatch_retains_it` covers the ReEstimated
  effort path the dev-story pass left untested in this projection (only ProgressReported's mismatch path was
  exercised): establish-from-nothing, a matching-unit re-estimate, and a unit-mismatched ReEstimated that
  retains the last valid own-remaining (refuse-don't-coerce).
- [x] **AC #1/#5 — a terminal item never re-enters the pool (unit, +1).**
  `A_terminal_item_never_re_enters_the_pool_even_under_a_later_requeue_or_assign` pins the safety invariant
  that a closed item can never reappear in the claimable pool: the terminal guard absorbs a later requeue
  rejection and assignment and reports **no** queue change.
- [x] **AC #4 — change-signal due-date dimension + ineligible boundary (unit, +2).**
  `Project_reports_a_change_when_an_eligible_item_is_rescheduled_to_a_new_due_date` extends the change-signal
  proof along the due-date axis (the dev-story pass covered only the priority axis), and
  `Project_reports_no_change_when_an_ineligible_item_is_rescheduled` pins the boundary that the signal tracks
  the *eligible* set/order only.
- [x] **AC #3 — `LatestAcceptedSourceSequence` freshness watermark (unit, +1).**
  `Latest_accepted_source_sequence_reflects_the_highest_accepted_sequence_under_out_of_order_delivery`
  asserts the AC-named watermark is surfaced on the read model and is order-tolerant (max accepted sequence).
- [x] **DC7 — rolled-remaining "where available" is *per item* (unit, +1).**
  `Rolled_remaining_is_composed_per_item_only_where_a_roll_up_is_available` proves a lookup that resolves one
  of two eligible items leaves the other's rolled remaining null/empty — not all-or-nothing.
- [x] **AC #1 — authorization filter order/trim/null-set contract (unit, +1).**
  `Authorization_filter_preserves_order_trims_the_tenant_and_rejects_a_null_result_set` locks the documented
  behaviours: survivor order is preserved, the authoritative tenant id is trimmed before the ordinal
  comparison, and a null result set fail-fasts (distinct from the fail-closed empty-tenant path).

## Story 4.4 Validation (after QA pass)

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — passed
  with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **483/483** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` — **80/80** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` — **33/33** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **3/3** passed
  (`Ok, passed 100 tests.` ×3).

### Story 4.4 Test Counts

| Suite | Story 4.3 Final | Story 4.4 dev-story | Story 4.4 after QA pass | Delta vs 4.3 |
|-------|----------------:|--------------------:|------------------------:|------:|
| UnitTests | 449 | 474 | **483** | +34 |
| IntegrationTests | 80 | 80 | **80** | — |
| ArchitectureTests | 31 | 33 | **33** | +2 |
| PropertyTests | 2 | 3 | **3** | +1 |
| **Total** | **562** | 590 | **599** | **+37** |

### Not-applicable runtime / UI surfaces

- No production UI, DataGrid, SignalR hub, MCP/chatbot/email adapter, routing engine, eligibility filter,
  escalation ladder, executor ranking, authority gate, or AI decision record — all out of scope and
  deferred (the **live** `IDomainQueryHandler`/`/query` endpoint, `IReadModelStore` persistence, and
  `IProjectionChangeNotifier`/SignalR broadcast → Stories 4.5/4.6, gated on the EventStore projection-model
  reconciliation; reminder/reactor recovery → Story 4.6).
- No Dapr dispatch, EventStore stream reads, clock/timer, or actor runtime here: the projection is the
  **deterministic** in-memory derivation of eligibility + ordering from event deliveries, modelled with no
  threads/sleeps/network/containers. Browser/UI E2E is **not applicable**.

### Checklist

- [x] Reconciled the existing roll-up read-side precedent and substrate seams before writing code; reused
  the delivery envelope (DC6) and confirmed `Projections` references only `Contracts` (+ EventStore.Contracts).
- [x] Defined the `WhatsNextItem` read-model contract (plain STJ record, distinct own/rolled types, no UI/
  executor-kind surface; AC #3).
- [x] Built the pure `WhatsNextQueueProjection`: eligibility `{Queued, Assigned}`, queued keeps last
  binding, cross-tenant `(tenant, id)` isolation, rolled-remaining composed where available (AC #1/#3/#5).
- [x] Implemented the total, deterministic, order-tolerant comparator (Priority→DueDate→identity, neither
  last; AC #2).
- [x] Added the pure query-side authorization filter as a distinct control, fail-closed (AC #1).
- [x] Satisfied the notifier requirement via the substrate seam + a pure change-signal and a stable
  `"works-whats-next"` token; no live surface wired (AC #4).
- [x] Added cross-tenant + privacy guards: mutation-validated colliding-id isolation test + a no-logging
  fitness check (AC #5).
- [x] Added the catalog-stays-36 / no-routing / no-surface fitness guard; existing guardrails preserved
  green (AC #1/#4).
- [x] Added the optional order-independent convergence + total-order property test (Task 9).
- [x] Authored `docs/whats-next-projection.md` and the boundary-record + test-summary updates.
- [x] No new durable event/command/rejection types; `WorkItemV1Catalog.Count` stays 36 and the golden
  corpus is byte-unchanged.
- [x] QA gap-fill pass (`bmad-qa-generate-e2e-tests`) closed nine AC/design-decision gaps (AC #1
  authorization-composes-with-scoping + filter order/trim/null contract; AC #1/#3 suspend→resume eligibility;
  DC5/AC #3 ReEstimated own-remaining; AC #1/#5 terminal-never-re-enters; AC #4 due-date change-signal +
  ineligible boundary; AC #3 freshness watermark; DC7 per-item rolled composition) — +9 unit cases, no
  production change.
- [x] Build clean (0 warnings / 0 errors) and all four test binaries green (599/599 after the QA pass; 590 at dev-story).
- [x] Left the unrelated `Hexalith.Tenants` gitlink change untouched; ran no recursive submodule commands.

---

# Test Automation Summary — Story 4.3 (Claim Queued Work with Single-Claim-Wins)

Workflow: `bmad-dev-story`. Framework reused: **xUnit v3 + Shouldly** (unit, integration, architecture) and
**FsCheck** (property). Story 4.3 is a **deterministic-concurrency-proof + guardrail** story for **FR-18**
(push/pull coexist; single-claim-wins), **NFR-3** (single-writer/optimistic; two claims → one success +
domain rejection), and **AR-10/B1** (single-aggregate claim under expected-version). The claim transition
(`ClaimWorkItem`/`WorkItemClaimed`), the rejection (`WorkItemTransitionRejected`), and the pure
`WorkItemLifecycle.Decide` table were all built in Story 2.1 and proved uniform across executor kinds in
Story 4.1; the **expected-version optimistic-concurrency mechanism is owned by `Hexalith.EventStore`** (the
`AggregateActor → EventPersister → ETag SaveStateAsync` pipeline). There is no UI/HTTP/MCP surface; the
executable path is **command → `WorkItemAggregate.Handle` → durable event → replayed `WorkItemState`**.

Story 4.2 final baseline was **549** green tests (UnitTests 438, IntegrationTests 80, ArchitectureTests 30,
PropertyTests 1). The Story 4.3 dev-story pass added **+12** tests (+10 unit, +1 architecture, +1 property),
reaching **561** green: UnitTests 448, IntegrationTests 80, ArchitectureTests 31, PropertyTests 2.

**No production code was changed (DC3).** Story 4.3 adds **no** event, command, rejection, or value-object
type. The reconciliation (Task 1) confirmed the claim path and the substrate-concurrency surface already
exist exactly as specified, so single-claim-wins is *proved and guarded*, not re-shaped. The durable wire
surface is frozen: `WorkItemV1Catalog.Count` stays **36** (14 events / 14 commands / 8 rejections) and the
golden corpus under `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/` (incl.
`WorkItemClaimed.v1.json`) is byte-unchanged.

**Reconciliation (Task 1) — confirmed before any change.**

- `Queued → Claim = Accept(InProgress)` and `Assigned → Claim = Accept(InProgress)`; **every other status —
  including `InProgress` and `Suspended` — rejects `Claim`**. `Handle(ClaimWorkItem)` emits
  `WorkItemClaimed(WorkItemId.Value, NextSequence(state), TenantId, WorkItemId, Binding)` on accept and
  `WorkItemTransitionRejected(FromStatus, "Claim")` otherwise.
- The EventStore substrate surface 4.3 relies on exists (`ConcurrencyConflictException`, `AggregateActor`),
  asserted by `EventStoreApiSurfaceCharacterizationTests`; `MaxPersistenceConflictRetries` default is 1.
- `IExecutorRouter` remains an abstraction with **no** wired impl (asserted by `BoundaryPortTests`);
  `Handle(ClaimWorkItem)` validates only `TenantId`/`WorkItemId`/`Binding` and the lifecycle cell — no
  eligibility/authority/routing input. `AuthorityLevel` stays carried-not-enforced (DC4).

## Production code changed

- **None.** This is a proof/guardrail story; the kernel, lifecycle table, and catalog are unchanged.

## Tests added

### Unit tests (`tests/Hexalith.Works.UnitTests`) — +10 cases

- [x] `WorkItemClaimConcurrencyTests` — **new file, +10 cases** (Tasks 2–3, AC #1–#5). Builds on (does not
  duplicate) Story 4.1's `WorkItemUniformExecutorBindingTests` and the exhaustive `WorkItemLifecycleTests`
  (status, Claim) matrix:
  - **Task 2 / AC #2/#5 (1):** `Two_claims_at_the_same_expected_version_collide_and_exactly_one_wins_with_the_loser_domain_rejected`
    — a `Queued` item at version `N`; two claims with **different** valid bindings are handled against the
    same observed state and **both** emit a `WorkItemClaimed` at sequence `N+1` (the expected-version
    collision — only one append can land). Applying the winner advances to `InProgress` at `N+1` bound to A;
    re-handling the loser against the now-advanced state (exactly what the substrate's conflict-retry does)
    yields a single `WorkItemTransitionRejected(InProgress, "Claim")`, applying it is a no-op, and there is
    exactly one accepted `WorkItemClaimed` and exactly one observable `IRejectionEvent`. **Deterministic —
    no threads/Task.Run/sleeps (RR-3);** a class XML-doc records that the live ETag append/retry/exhaustion
    path is exercised under Aspire in Story 4.5.
  - **Task 3 / AC #1 (Theory ×2):** `Claim_from_a_claimable_status_emits_one_claimed_act_binding_the_claimant_and_transitions_to_in_progress`
    — from both claimable entries (`Queued` and `Assigned`), one `WorkItemClaimed` at `Sequence + 1`
    carrying the supplied binding, replay rests `InProgress` bound to the claimant, plus an identity
    assertion (`AggregateId == WorkItemId.Value`, `TenantId`, `WorkItemId`).
  - **Task 3 / AC #3 (Theory ×7):** `Claim_from_a_non_claimable_status_is_rejected_with_no_binding_status_or_sequence_mutation`
    — for each non-claimable status (`Created`, `InProgress`, `Suspended`, and the four terminals
    `Completed`/`Cancelled`/`Rejected`/`Expired`), each arranged **carrying a known binding**, a
    `ClaimWorkItem` returns `WorkItemTransitionRejected(FromStatus = <status>, AttemptedAct = "Claim")`,
    emits **no** `WorkItemClaimed`, and applying the result leaves `Status`, `Sequence`, and
    `ExecutorBinding` unchanged.

### Architecture tests (`tests/Hexalith.Works.ArchitectureTests`) — +1 case

- [x] `ScaffoldGovernanceTests.P0_WorkItemSurfaceHasNoClaimEligibilityRoutingOrConcurrencyRejectionTypeAndCatalogStays36`
  — **new fitness test** (Task 4, AC #4 + DC1/DC4). Mirrors
  `P0_WorkItemSurfaceHasNoExecutorKindSpecificHandoffOrReassignTypeAndCatalogStays36`: scans declared
  **type names** under `src/` (reusing the `declarationRegex` + exclusion set) for a forbidden claim/routing
  vocabulary (`ClaimEligibility*`, `EligibilityFilter*`, `EligibilityEngine*`, `ClaimRouter*`,
  `RoutingScore*`, `ExecutorRanking*`, `EscalationLadder*`, `ClaimDecisionRecord*`, and the DC1-forbidden
  `ClaimRejected`/`ConcurrencyRejected`) and asserts none exist; paired with the reflection-based
  frozen-catalog assertion (`polymorphicCatalogCount.ShouldBe(36)`). The existing guardrails
  (`P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority`, `P0_ScaffoldContainsOnlyTheV1ProjectSet`,
  `P0_KernelProjectsStayInfrastructureFree`, both `EventStoreApiSurfaceCharacterizationTests` facts,
  `DependencyDirectionTests`, `BoundaryDecisionRecordTests`, `LifecycleTransitionMatrixDocTests`,
  `BoundaryPortTests`) are preserved unchanged and green.

### Property tests (`tests/Hexalith.Works.PropertyTests`) — +1 case

- [x] `WorkItemClaimConvergencePropertyTests` — **new file, +1 property** (Task 5, AC #2/#5; FsCheck wiring
  mirrors `WorkItemRollUpConvergencePropertyTests`). For a `Queued` item and any generated set of `K ≥ 2`
  distinct claims, all `K` compute a claim at the same `N+1` (collision); applying **any** chosen winner
  advances to `InProgress`, and the other `K−1` re-handle to `WorkItemTransitionRejected(InProgress,
  "Claim")` — proving single-claim-wins is **order-independent** (exactly one accepted, `K−1` rejected,
  whoever won). A test-class comment records that *duplicate-delivery* idempotency is a **substrate** concern
  (CausationId/offset dedup; NFR-9/AR-11), not the kernel — a duplicate claim at the domain level is a
  rejection, never `DomainResult.NoOp` (DC5). `Hexalith.Works.Server` + `Hexalith.Works.Testing`
  `ProjectReference`s were added to the PropertyTests project (no Hexalith `PackageReference`; dependency
  direction unchanged).

## Documentation

- [x] `docs/lifecycle-transition-matrix.md` — finalized the claim note (replacing "Single-claim-wins
  concurrency is Story 4.3"): single-claim-wins is the composition of the pure lifecycle and the EventStore
  substrate's expected-version (ETag) optimistic concurrency; the loser of a same-expected-version race
  re-handles to `WorkItemTransitionRejected(InProgress, "Claim")` (DC1); no new rejection type; catalog stays
  36; retry-exhaustion is infra (Story 4.5). **No cell changed** (still 1:1 with `WorkItemLifecycle.cs`).
- [x] `docs/boundary-decision-record.md` — added a Story 4.3 note in *Notes and cross-references*:
  single-claim-wins is the kernel lifecycle + EventStore-owned expected-version composition; Works adds no
  claim-eligibility/routing/escalation/ranking/AI type and no `ClaimRejected`/`ConcurrencyRejected`; claim is
  unconditional in v1 (eligibility deferred to the Theme-4 `IExecutorRouter` seam); the claimable pool is a
  Story 4.4 read projection. The module/seam enumeration is preserved (`BoundaryDecisionRecordTests` green).

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

The QA pass traced AC #1–#5 and design decisions DC1–DC5 against the dev-story coverage. Most of the
surface was already well-asserted: AC #1 (claim emits/binds/transitions), AC #2/#5 (deterministic
single-claim-wins, unit + order-independent property), AC #3 (not-claimable rejection with no mutation),
and AC #4 (claim-eligibility/routing fitness guard + catalog-stays-36) are all covered, and the candidate
"authority-unconditional claim" gap proved already covered — `WorkItemUniformExecutorBindingTests` already
drives a `Read`-authority external party through claim (DC4, carried-not-enforced). This is a pure
event-sourced domain library with **no HTTP/API or UI surface**, so the "API" lane is the aggregate command
handlers and the "E2E" lane is the serialization-flow integration test — both already present; the gap was
assertion-level, not new-surface. One genuine gap was found and auto-applied as a new `[Fact]` (UnitTests
448 → **449**). No production code changed; catalog stays **36**.

- [x] **DC5 — a duplicate claim by the *current holder* of an InProgress item is a rejection, not a NoOp
  (unit, +1 case).** `Duplicate_claim_by_the_current_holder_of_an_in_progress_item_is_rejected_not_a_no_op`
  proves the DC5 invariant the dev-story pass documented only in a property-test *comment* and never
  asserted. The AC #3 theory re-claims `InProgress` with a **different** party and never pins `IsNoOp`;
  duplicate-delivery idempotency is a **substrate** concern (the `AggregateActor` CausationId/offset dedup,
  NFR-9/AR-11), but at the kernel a second claim against an already-`InProgress` item — *even from the
  executor that already holds it* — must surface `WorkItemTransitionRejected(InProgress, "Claim")`, never
  `DomainResult.NoOp` (the lifecycle returns NoOp only on the terminal self-duplicate diagonals). The test
  pins `IsRejection`, **`IsNoOp == false`**, no `WorkItemClaimed`, the rejection identity, and that applying
  it mutates nothing — so a future "collapse duplicate claim into a NoOp" change is a visible build break.

## Story 4.3 Validation (after QA pass)

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — passed
  with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **449/449** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` — **80/80** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` — **31/31** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **2/2** passed
  (`Ok, passed 100 tests.` ×2).

### Story 4.3 Test Counts

| Suite | Story 4.2 Final | Story 4.3 dev-story | Story 4.3 after QA pass | Delta vs 4.2 |
|-------|----------------:|--------------------:|------------------------:|------:|
| UnitTests | 438 | 448 | **449** | +11 |
| IntegrationTests | 80 | 80 | **80** | — |
| ArchitectureTests | 30 | 31 | **31** | +1 |
| PropertyTests | 1 | 2 | **2** | +1 |
| **Total** | **549** | 561 | **562** | **+13** |

### Not-applicable runtime / E2E surfaces

- No production UI, MCP/chatbot/email adapter, executor router, eligibility filter, escalation ladder,
  authority gate, routing score, AI decision record, signed link, LLM/NL parsing, or cost-governance
  package — all out of scope and deferred (claimable-pool "what's-next" queue projection/query → Story 4.4;
  the **live** ETag append/retry/exhaustion behavior under the Aspire command/event pipeline → Story 4.5;
  reminder/reactor recovery → Story 4.6).
- No Dapr dispatch, EventStore stream reads, clock/timer, or actor runtime here: the single-claim-wins proof
  is the **deterministic domain outcome** of the expected-version collision (RR-3), modelled with no
  threads/sleeps; the AppHost/Aspire runtime lane was **not exercised**. Browser/UI E2E is **not applicable**.

### Checklist

- [x] Reconciled the existing claim + substrate-concurrency surface before changing code; confirmed the
  claim path, the EventStore expected-version mechanism ownership, and that no eligibility/routing type exists.
- [x] Proved deterministic single-claim-wins via expected-version collision (two claims → one
  `WorkItemClaimed`, loser → `WorkItemTransitionRejected(InProgress, "Claim")`), with no thread race (RR-3).
- [x] Proved the focused happy-path claim (AC #1) and the not-claimable rejection with no mutation (AC #3).
- [x] Added an AC #4 fitness guard (no claim-eligibility/routing/escalation/ranking/AI type; no
  `ClaimRejected`/`ConcurrencyRejected`; catalog stays 36); existing guardrails preserved unchanged and green.
- [x] Added an optional order-independent single-claim-wins property test (Task 5; falsifiable value beyond
  the two-claimant unit proof).
- [x] No new durable event/command/rejection types; `WorkItemV1Catalog.Count` stays 36 and the golden
  corpus is byte-unchanged.
- [x] QA pass closed the under-asserted DC5 gap (duplicate claim by the holder is a rejection, never a NoOp).
- [x] Build clean (0 warnings / 0 errors) and all four test binaries green (562/562).
- [x] Left the unrelated `Hexalith.Tenants` gitlink change untouched; ran no recursive submodule commands.

---

# Test Automation Summary — Story 4.2 (Assign, Reassign, and Hand Off Work)

Workflow: `bmad-dev-story`. Framework reused: **xUnit v3 + Shouldly** for unit, integration, and
architecture lanes. Story 4.2 is a **behavioral-proof, edge-cell-finalization, and guardrail** story for
**FR-17** (bind / reassign / hand off via one uniform operation) and **FR-18** (push/pull coexist;
requeue re-emits `WorkItemQueued`). The lifecycle mechanics it asserts were already built by Story 2.1
(`AssignWorkItem`/`WorkItemAssigned`, `QueueWorkItem`/`WorkItemQueued`, `ClaimWorkItem`/`WorkItemClaimed`,
the `WorkItemTransitionRejected` rejection, and the pure `WorkItemLifecycle.Decide` table) and hardened by
Story 4.1 (uniform `ExecutorBinding`, reassignment-latest-wins). There is no UI/HTTP/MCP surface; the
executable path is **command → `WorkItemAggregate.Handle` → durable event → concrete
`JsonSerializerDefaults.Web` JSON → replayed `WorkItemState`**.

Story 4.1 final baseline was **528** green tests (UnitTests 419, IntegrationTests 79, ArchitectureTests
29, PropertyTests 1). The Story 4.2 dev-story pass added **+19** tests (+17 unit, +1 integration, +1
architecture), reaching **547** green: UnitTests 436, IntegrationTests 80, ArchitectureTests 30,
PropertyTests 1.

**No production code was changed.** Story 4.2 adds **no** event, command, rejection, or value-object type.
The reconciliation (Task 1) confirmed the uniform surface already existed exactly as specified, and the
deferred `InProgress`-reassignment edge cell (Task 2/D4) was already `Reject` in `WorkItemLifecycle.cs` and
in the matrix doc — so only a finalization **note** was added, not a code or cell change. The durable wire
surface is frozen: `WorkItemV1Catalog.Count` stays **36** (14 events / 14 commands / 8 rejections) and the
golden corpus under `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/` is byte-unchanged.

**Reconciliation (Task 1) — confirmed before any change.**

- `AssignWorkItem(TenantId, WorkItemId, ExecutorBinding)` is the single bind/rebind/hand-off command;
  `QueueWorkItem(TenantId, WorkItemId)` (no binding) is the single requeue path; `ClaimWorkItem(…,
  ExecutorBinding)` is the single `InProgress`-entry command.
- There is **no** executor-kind-specific hand-off command/event (`HandoffToBot`, `ReassignToHuman`,
  `WorkItemHandedOff`, …) and `ExecutorBinding` carries no kind discriminator — reassignment and hand-off
  differ only by `ExecutorBinding` field values.
- `WorkItemLifecycle.Decide` already returns `Reject` for `InProgress`/`Suspended` → `Assign`/`Queue`
  (the `_ => Reject` arm); `Apply(WorkItemQueued)` does not clear `ExecutorBinding` (D2);
  `Apply(WorkItemTransitionRejected)` is a no-op (D5).

## Production code changed

- **None.** This is a proof/finalization/guardrail story; the kernel, lifecycle table, and catalog are
  unchanged.

## Tests added

### Unit tests (`tests/Hexalith.Works.UnitTests`) — +19 cases (+17 dev-story, +2 QA gap-fill)

> The QA gap-fill pass added two more `[Fact]` cases to this same `WorkItemHandoffTests` file
> (`Reassign_to_the_same_binding_…_not_a_noop` and `Assign_from_the_pool_overrides_the_binding_requeue_left_in_state_…`)
> and strengthened the AC #1 and terminal-rejection theories in place — see **Gaps closed by the QA pass** below.

- [x] `WorkItemHandoffTests` — **new file, +19 cases** (Tasks 3–5 + QA gap-fill, AC #1–#5). Builds on (does not
  duplicate) Story 4.1's `WorkItemUniformExecutorBindingTests` and the exhaustive `WorkItemLifecycleTests`
  matrix:
  - **Task 3 / D2 (1):** `Requeue_keeps_the_last_executor_binding_in_replayed_state` — after
    `Assigned(bindingA) → Queue`, replayed `Status == Queued`, `Sequence` advanced by exactly 1, and
    `ExecutorBinding` still equals `bindingA`. Locks the deliberate choice (queueing is not a binding act;
    ownership presentation is Story 4.4) so a future change is a visible break.
  - **AC #1 (Theory ×2):** `Assign_from_an_assignable_status_emits_one_assigned_act_carrying_the_supplied_binding`
    — `AssignWorkItem` from `Created` and `Queued` emits exactly one `WorkItemAssigned` at `Sequence + 1`
    carrying the supplied binding; replay rests at `Assigned` with that binding.
  - **AC #2 (Theory ×3):** `Reassign_from_assigned_with_a_different_binding_is_a_fresh_act_through_the_same_handler_and_latest_wins`
    — a second `AssignWorkItem` with a different binding from `Assigned` is accepted through the same
    handler (`Assigned → Assigned` rebind), emits a fresh `WorkItemAssigned` (a distinct raw act, **not** a
    NoOp), and replay makes the second binding authoritative — across the three representative executors
    (system agent `Mcp`, internal user `Cli`, external party `Email`), differing only by field values.
  - **AC #3, the novel assertion (2):** `Human_to_system_to_human_handoff…` and
    `System_to_human_to_system_handoff…` drive a hand-off chain (covered both directions per FR-17) and
    assert the **ordered event history** preserves each hand-off as raw-act evidence — three contiguous
    `WorkItemAssigned` events at consecutive sequences, each with its own binding, not collapsed — then
    that the **latest** binding is authoritative for the next executor act (the most-recent party's
    `ClaimWorkItem`/`WorkItemClaimed` binds that party).
  - **AC #4 (1):** `Requeue_returns_work_to_the_pool_so_a_different_executor_can_claim_it` — the full
    `Assigned(bindingA) → QueueWorkItem → WorkItemQueued (Queued) → ClaimWorkItem(bindingB) → WorkItemClaimed
    (InProgress)` path; a **different** executor claims the requeued item (the `QueueWorkItem` requeue path,
    distinct from Story 2.5's `RejectWorkItem` decline — D6).
  - **AC #5 (Theory ×4 + Theory ×4):** `Assign_from_each_terminal_status_is_rejected_with_no_binding_mutation_and_no_sequence_burn`
    and `Queue_from_each_terminal_status_is_rejected_so_the_shared_pool_cannot_reopen_a_closed_item` — for
    each terminal (`Completed`, `Cancelled`, `Rejected`, `Expired`, each arranged carrying a known
    binding), `AssignWorkItem`/`QueueWorkItem` returns `WorkItemTransitionRejected(FromStatus = <terminal>,
    AttemptedAct = "Assign"|"Queue")`, emits no success event, and applying the result leaves `Status`,
    `Sequence`, and `ExecutorBinding` unchanged.

### Integration tests (`tests/Hexalith.Works.IntegrationTests`) — +1 case

- [x] `WorkItemHandoffChainContractFlowTests` — **new file, +1 case** (Task 4, AC #3). A human → system →
  human hand-off chain crosses the real write path (`WorkItemAggregate.Handle`) and `JsonSerializerDefaults.Web`
  serialization into an independent replay rebuilt only from round-tripped events: each `WorkItemAssigned`
  raw act survives the wire in order (contiguous sequences, distinct bindings, not collapsed), the latest
  binding is authoritative at every step, and the most-recent party's claim binds that party.

### Architecture tests (`tests/Hexalith.Works.ArchitectureTests`) — +1 case

- [x] `ScaffoldGovernanceTests.P0_WorkItemSurfaceHasNoExecutorKindSpecificHandoffOrReassignTypeAndCatalogStays36`
  — **new fitness test** (Task 6, AC #2/#5, FR-17). Mirrors
  `P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority`: scans production `src/**/*.cs`
  (excluding `bin`/`obj`, `*.g.cs`, `*Assembly.cs`, and the value-object definition files) for **declared
  type names** (not raw substrings) matching `HandoffTo*`, `ReassignTo*`, `AssignTo<Kind>*`, `*HandedOff`,
  `Unassign*`, or `ReturnToPool*` — the executor-kind-specific hand-off/reassign vocabulary FR-17 forbids —
  and asserts none exist. Paired with a frozen-catalog assertion: the count of `Polymorphic`-derived
  concrete types in the `Hexalith.Works.Contracts` assembly is **36** (the architecture-project-local
  equivalent of `WorkItemV1Catalog.Count`, which ArchitectureTests cannot reference directly). The existing
  guardrails (`P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority`,
  `P0_ScaffoldContainsOnlyTheV1ProjectSet`, `P0_KernelProjectsStayInfrastructureFree`,
  `DependencyDirectionTests`, `BoundaryDecisionRecordTests`, `LifecycleTransitionMatrixDocTests`) are
  preserved unchanged and green.

## Documentation

- [x] `docs/lifecycle-transition-matrix.md` — added two notes to the Transition-matrix section (no cell
  changed): **D4** finalizing Story 2.1's deferred `InProgress`-reassignment edge cell (active work is not
  directly reassigned/requeued in v1; `InProgress`/`Suspended` rows stay `Assign = R`/`Queue = R`; hand-off
  is via `Assigned` rebind or `Assigned → Queue → (re)Claim`; no `InProgress → Assigned` transition), and a
  note that `QueueWorkItem`/`WorkItemQueued` is the requeue path and the queued item retains its last
  binding in state while ownership presentation is a Story 4.4 projection concern (D2/D6).
- [x] `docs/boundary-decision-record.md` — added a one-line Story 4.2 note in *Notes and cross-references*:
  assign/reassign/hand-off/requeue/claim are one uniform vocabulary with no executor-kind-specific
  command/event; hand-off is symmetric and auditable through the raw-act event history; the v1 catalog
  stays 36. The existing module/seam enumeration is preserved (`BoundaryDecisionRecordTests` still green).

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

All four additions are genuine, non-redundant gaps traced against AC #1–#5 and design decisions D2/D3/D5
— each closes a behaviour the dev-story pass left under-asserted, and none duplicates the existing
`WorkItemHandoffTests`/`WorkItemLifecycleTests`/`WorkItemUniformExecutorBindingTests` coverage. This is a
pure event-sourced domain library: there is **no HTTP/API or UI surface**, so the "API" lane is the
aggregate command handlers and the "E2E" lane is the serialization-flow integration test — both already
present; the gaps were assertion-level, not new-surface. Two are new `[Fact]` cases (UnitTests 436 →
**438**); two strengthen existing theories in place. No production code changed; catalog stays **36**.

- [x] **AC #5 — emitted payload is an `IRejectionEvent` (unit, strengthened, +0 cases).** The two
  terminal-rejection theories asserted the concrete `WorkItemTransitionRejected` type and `IsRejection`,
  but never the interface contract the AC names verbatim ("the command emits an `IRejectionEvent`"). Both
  `Assign_from_each_terminal_status_…` and `Queue_from_each_terminal_status_…` now assert the emitted
  `IEventPayload` `ShouldBeAssignableTo<IRejectionEvent>()` directly — decoupled from the `IsRejection`
  helper's own `is IRejectionEvent` implementation — locking the rejection-as-event payload contract (D5).
- [x] **AC #2 / D3 — same-binding reassignment is still a fresh raw act (unit, +1 case).**
  `Reassign_to_the_same_binding_from_assigned_is_still_a_fresh_raw_act_not_a_noop` proves D3's stronger
  claim that the dev baseline left untested: the existing reassign theory only used a *different* binding,
  so "even to the same binding, `Assigned → Assign` emits a new `WorkItemAssigned` at `Sequence + 1` and
  is never collapsed to a NoOp" was unproven. The test pins `IsNoOp == false`, the fresh event/sequence,
  and the advanced stream — so a future "collapse identical rebinds" optimization is a visible break.
- [x] **FR-18 / D2×AC #3 — push-from-pool overrides the requeue-retained binding (unit, +1 case).**
  `Assign_from_the_pool_overrides_the_binding_requeue_left_in_state_and_the_pushed_binding_wins` proves
  the untested interplay of D2 (requeue keeps the last binding in state) and AC #3 (latest binding is
  authoritative): `Assigned(bindingA) → Queue` leaves `bindingA` as a stale-by-design carryover, then a
  push `AssignWorkItem(bindingC)` from `Queued` makes `bindingC` authoritative on replay — proving the
  "push" half of FR-18 coexists with "pull" (claim) and that latest-act-wins holds even over a stale
  in-state value, not just a freshly cleared field.
- [x] **AC #1 — emitted `WorkItemAssigned` is correctly addressed (unit, strengthened, +0 cases).** The
  AC #1 theory asserted only `Sequence` and `Binding`; it now also asserts `AggregateId == WorkItemId.Value`,
  `TenantId`, and `WorkItemId` on the emitted event, so a misrouted emission can no longer pass on the
  binding/sequence alone.

## Story 4.2 Validation (after QA pass)

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **438/438** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **80/80** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **30/30** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed (`Ok, passed 100 tests.`).

### Story 4.2 Test Counts

| Suite | Story 4.1 Final | Story 4.2 dev-story | Story 4.2 after QA pass | Delta vs 4.1 |
|-------|----------------:|--------------------:|------------------------:|------:|
| UnitTests | 419 | 436 | **438** | +19 |
| IntegrationTests | 79 | 80 | **80** | +1 |
| ArchitectureTests | 29 | 30 | **30** | +1 |
| PropertyTests | 1 | 1 | **1** | — |
| **Total** | **528** | 547 | **549** | **+21** |

### Not-applicable runtime / E2E surfaces

- No production UI, MCP/chatbot/email adapter, executor router, eligibility filter, escalation ladder,
  authority gate, routing score, signed link, LLM/NL parsing, or cost-governance package — all out of
  scope and deferred (single-claim-wins → Story 4.3; what's-next queue projection/query → Story 4.4; Aspire
  command/event pipeline → Story 4.5; reminder/reactor recovery → Story 4.6). The AppHost/Aspire runtime
  lane was **not exercised** (no command/event pipeline proof here).
- No Dapr dispatch, EventStore stream reads, clock/timer, or reminder/reactor recovery surface, so the
  integration lane is contract-flow + serialization only; browser/UI E2E is **not applicable**.

### Checklist

- [x] Reconciled the existing assign/requeue/claim/reject surface before changing code; confirmed the
  uniform path and that no executor-kind-specific hand-off command/event exists.
- [x] Finalized the `InProgress`-reassignment edge cell as `Reject` (D4) — matrix doc note added; code and
  cells unchanged because they were already correct.
- [x] Locked requeue binding-in-state semantics (D2) with a focused unit test.
- [x] Proved assign/reassign/hand-off symmetry and ordered raw-act hand-off history (both directions), the
  full requeue→reclaim-by-a-different-executor path, and terminal-state assignment/queue rejection with no
  binding mutation / no sequence burn across all four terminals.
- [x] Added a fitness guard asserting no executor-kind-specific hand-off/reassign type and that the v1
  catalog stays 36; existing guardrails preserved unchanged and green.
- [x] No new durable event/command/rejection types; `WorkItemV1Catalog.Count` stays 36 and the golden
  corpus is unchanged.
- [x] Build clean (0 warnings / 0 errors) and all four test binaries green (549/549 after the QA pass; 547 at dev-story).
- [x] QA gap-fill pass (`bmad-qa-generate-e2e-tests`) closed four AC/design-decision gaps (AC #5
  `IRejectionEvent` payload contract; AC #2/D3 same-binding fresh raw act; FR-18/D2×AC #3 push-from-pool
  overrides retained binding; AC #1 emitted-event identity) — +2 unit cases, no production change.

---

# Test Automation Summary — Story 4.1 (Bind Work to a Uniform Party Executor)

Workflow: `bmad-dev-story` followed by a `bmad-qa-generate-e2e-tests` QA gap-filling pass. Framework
reused: **xUnit v3 + Shouldly** for unit, integration, and
architecture lanes. Story 4.1 is a contract / replay / read-model / guardrail story for FR-17 and
FR-19: every executor (system agent, internal user, external party) is represented by one
`ExecutorBinding` (`PartyId` + `Channel` + `AuthorityLevel`) and **no domain behavior branches on
executor kind**. There is no UI/HTTP/MCP surface; the executable path is **command →
`WorkItemAggregate.Handle` → durable event → concrete `JsonSerializerDefaults.Web` JSON → replayed
`WorkItemState` → read-model view**.

Story 3.6 final baseline was **481** green tests (UnitTests 389, IntegrationTests 63, ArchitectureTests
28, PropertyTests 1). The Story 4.1 dev-story pass added **+35** tests (+25 unit, +9 integration, +1
architecture), reaching **516** green (UnitTests 414, IntegrationTests 72, ArchitectureTests 29,
PropertyTests 1). The follow-up `bmad-qa-generate-e2e-tests` QA pass then added **+12** tests (+5 unit,
+7 integration) to close residual end-to-end, fourth-binding-carrier, and binding-shape gaps, raising the
total to **528** green: UnitTests 419, IntegrationTests 79, ArchitectureTests 29, PropertyTests 1. **No
production code was changed by the QA pass** — only test files were added/extended; the v1 catalog
(`WorkItemV1Catalog.Count` 36) and golden corpus are unchanged.

**Reconciliation (Task 1) — confirmed before any change.** The `ExecutorBinding(PartyId, Channel,
AuthorityLevel)` shape was already used uniformly across `CreateWorkItem`, `AssignWorkItem`,
`ClaimWorkItem`, `SpawnChild`, their events (`WorkItemCreated`, `WorkItemAssigned`, `WorkItemClaimed`,
`ChildSpawned`), `WorkItemState` replay, and `WorkItemAggregate` (which passes the binding through
without inspecting `Channel`/`AuthorityLevel`). There is no legacy `ExecutorId`, `BindingKind`,
`BotExecutor`/`HumanExecutor`/`ExternalExecutor`, or `ExecutorKind`/`PartyKind` discriminator in
production code. The one real gap was that `ExecutorBinding` rejected unknown `Channel` but not unknown
`AuthorityLevel`.

**No durable wire shape changed.** No new events or commands were added; `WorkItemV1Catalog.Count`
remains **36** and the golden corpus is unchanged. The new read-model `WorkItemExecutorBindingView` is a
plain `Contracts/Models` record (not polymorphic-serialized), and the `ExecutorBinding` validation
hardening tightens construction only — every existing payload carries a valid `AuthorityLevel`.

## Production code changed (small, in-scope)

- [x] `src/Hexalith.Works.Contracts/ValueObjects/ExecutorBinding.cs` — **modified.** Added
  `AuthorityLevel.Unknown` / undefined-enum rejection mirroring the existing `Channel` guard
  (Task 2, AC #1/#3), plus an XML summary documenting the one-shape, carried-not-enforced, no-kind
  contract. Public shape (`PartyId`, `Channel`, `AuthorityLevel`) unchanged.
- [x] `src/Hexalith.Works.Contracts/Models/WorkItemExecutorBindingView.cs` — **new.** Smallest
  contract-level read model exposing executor data uniformly (`TenantId`, `WorkItemId`, nullable
  `ExecutorBinding`). No executor-kind discriminator, display name, party profile, contact detail, UI
  type, SignalR/query wiring, or queue projection (Story 4.4 owns the queue projection) (Task 4, AC
  #3/#4).

## Tests added

### Unit tests (`tests/Hexalith.Works.UnitTests`) — +25 cases

- [x] `WorkItemContractValueObjectTests` — **+6 cases** (Task 2, AC #1/#3). `AuthorityLevel.Unknown`
  rejection, undefined `(AuthorityLevel)999` rejection, and a Theory ×4 proving every known authority
  level (`Read`/`Contribute`/`Coordinate`/`Administer`) is carried.
- [x] `WorkItemUniformExecutorBindingTests` — **new file, +13 cases** (Task 3, AC #1–#3). Table-driven
  system-agent / internal-user / external-party bindings flow through the identical
  create/assign/claim handlers; the event and replayed state preserve `PartyId`, `Channel`, and
  `AuthorityLevel` verbatim. A reassignment fact proves a second `AssignWorkItem` makes the latest
  binding authoritative with no dedicated handoff command.
- [x] `WorkItemExecutorBindingViewTests` — **new file, +6 cases** (Task 4, AC #3/#4). `AuthorityLevel`
  survives projection from replayed state into `WorkItemExecutorBindingView`; the view reflects the
  latest binding after reassignment, carries a null binding when none is bound, and exposes only
  identity + `ExecutorBinding` (a structural guard rejects any kind/display/contact property drift).

### Integration tests (`tests/Hexalith.Works.IntegrationTests`) — +9 cases

- [x] `UniformExecutorBindingSerializationTests` — **new file, +9 cases** (Task 3, AC #1–#3). The three
  representative bindings round-trip through concrete `JsonSerializerDefaults.Web` serialization on
  `WorkItemCreated`, `WorkItemAssigned`, and `WorkItemClaimed`; `channel` and `authorityLevel` are
  asserted present in the JSON and equal after round-trip (and into replayed state for the created event).

### Architecture tests (`tests/Hexalith.Works.ArchitectureTests`) — +1 case

- [x] `ScaffoldGovernanceTests.P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority` —
  **new fitness test** (Task 5, AC #1/#3/#5, SM-3). Scans production `src/**/*.cs` (excluding
  `bin`/`obj`, `*.g.cs`, `*Assembly.cs`, and the value-object definition files) for `switch`/`case`/
  equality/pattern branching over `Channel`, `AuthorityLevel`, or `PartyId` — plus relational operators
  (`>`/`>=`/`<`/`<=`) on the ordered `AuthorityLevel` enum, the realistic carried-not-enforced
  enforcement shape (hardened during the senior-developer review). Validation inside
  `ExecutorBinding` itself is allowed; the aggregate, projections, reactor, and read models must treat
  the binding as opaque data. Forbidden adapter/LLM/routing/email/MCP/UI/security/cost-governance
  project and package introduction into the kernel remains enforced by the existing
  `P0_ScaffoldContainsOnlyTheV1ProjectSet` (project-name fragments) and
  `P0_KernelProjectsStayInfrastructureFree` (csproj reference) guards, which were preserved unchanged.

## Documentation

- [x] `docs/boundary-decision-record.md` — added a Story 4.1 note in *Notes and cross-references*: one
  `ExecutorBinding` shape, Party identity is a reference, `Channel` is an interaction medium,
  `AuthorityLevel` is carried-not-enforced, no executor-kind branch discriminator, and the new
  `WorkItemExecutorBindingView` exposes only that data. The existing module/seam enumeration was
  preserved (`BoundaryDecisionRecordTests` still green).

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

All three additions are genuine, non-redundant gaps verified against AC #1–#5 and the design
guardrails. The dev-story baseline already proved value-object rejection, uniform create/assign/claim
binding through event + replay, reassignment-latest-wins, view projection (incl. null/structural), the
three representative bindings round-tripping `WorkItemCreated`/`WorkItemAssigned`/`WorkItemClaimed`, and
the zero-executor-kind-branch fitness scan. The residual gaps were the **full write-path-to-view chain**
through the real aggregate, the **fourth binding-carrying pair** (`SpawnChild`/`ChildSpawned`), and a
**structural shape lock** on `ExecutorBinding` itself:

- [x] **AC #2/#3/#4 — end-to-end aggregate → serialize → replay → view (integration, +7 cases).**
  `UniformExecutorBindingLifecycleFlowTests` (**new file**). A Theory ×3 drives each representative
  executor through the real `WorkItemAggregate.Handle` write path (Create → Assign → Claim), replays each
  event round-tripped through `JsonSerializerDefaults.Web` into an independent state, and asserts the full
  binding + `AuthorityLevel` surfaces in the `WorkItemExecutorBindingView`. A reassignment fact rebinds
  system → internal → external mid-lifecycle through the same `AssignWorkItem` path and proves the latest
  authority is authoritative through claim and complete and in the view — not the create-time authority.
  The dev baseline called handlers in isolation/synthetic state and never projected the view from a
  serialized chain; the one existing full-lifecycle flow used a single binding.
- [x] **AC #2/#3 — `SpawnChild`/`ChildSpawned` uniform binding (unit + integration, +5 cases).**
  `WorkItemUniformExecutorBindingTests.SpawnChild_carries_the_uniform_child_binding_through_the_spawned_event`
  (Theory ×4) proves the fourth binding-carrying command/event preserves the uniform shape for every
  representative executor through the real aggregate, and
  `UniformExecutorBindingSerializationTests.ChildSpawned_round_trips_the_uniform_child_binding_with_authority`
  (Theory ×3) proves the nested child `authorityLevel` survives the EventStore-persisted JSON form. The
  uniform-binding proof previously covered only create/assign/claim, omitting the spawn carrier.
- [x] **AC #1 — `ExecutorBinding` structural shape lock (unit, +1 case).**
  `WorkItemContractValueObjectTests.ExecutorBinding_exposes_exactly_party_channel_and_authority_with_no_executor_kind_discriminator`
  asserts the value object exposes exactly `PartyId`, `Channel`, `AuthorityLevel`, stays sealed (no
  bot/human/external subtype hierarchy), and carries no `Kind`/`Subtype`/`Discriminator`/`Type` property
  — locking AC #1's "no executor-kind-specific subtype or branch discriminator" on the binding itself
  (only the read-model view had such a guard before).

## Story 4.1 Validation (after QA pass)

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **419/419** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **79/79** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **29/29** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed (`Ok, passed 100 tests.`).

### Story 4.1 Test Counts

| Suite | Story 3.6 Final | Story 4.1 dev-story | QA gap pass | QA Delta |
|-------|----------------:|--------------------:|------------:|---------:|
| UnitTests | 389 | 414 | **419** | +5 |
| IntegrationTests | 63 | 72 | **79** | +7 |
| ArchitectureTests | 28 | 29 | **29** | — |
| PropertyTests | 1 | 1 | **1** | — |
| **Total** | **481** | **516** | **528** | **+12** |

### Not-applicable runtime / E2E surfaces

- No production UI, MCP/chatbot/email adapter, executor routing, eligibility filtering, escalation,
  `AuthorityLevel` enforcement, signed links, security hardening, LLM/NL parsing, or cost governance —
  all out of scope and deferred (Stories 4.2–4.6 / Themes 4–6). The AppHost/Aspire runtime lane was
  **not exercised** (no command/event pipeline proof here — Story 4.5).
- No Dapr dispatch, EventStore stream reads, clock/timer, or reminder/reactor recovery surface, so the
  integration lane is contract-flow + serialization only; browser/UI E2E is **not applicable**.

### Checklist

- [x] Reconciled the existing executor-binding surface before changing code; confirmed one shape and no
  kind discriminator.
- [x] `ExecutorBinding` rejects `AuthorityLevel.Unknown` and undefined authority; public shape unchanged.
- [x] One binding shape proven for system/internal/external through create/assign/claim and reassignment.
- [x] `AuthorityLevel` survives concrete `System.Text.Json` round-trip and projection/replay into the view.
- [x] Read-model contract exposes executor data with no UI/routing/kind scope.
- [x] Fitness test asserts zero executor-kind branching; kernel-purity and dependency-direction tests
  preserved unchanged.
- [x] No new durable event/command types; `WorkItemV1Catalog.Count` stays 36 and the golden corpus is
  unchanged.
- [x] QA gap pass added end-to-end (aggregate → serialize → replay → view), fourth-binding-carrier
  (`SpawnChild`/`ChildSpawned`), and `ExecutorBinding` structural-shape coverage; no production code changed.
- [x] Build clean (0 warnings / 0 errors) and all four test binaries green (528/528 after the QA pass).

---

# Test Automation Summary — Story 3.6 (Cascade Terminal Work Through Active Descendants)

Workflow: `bmad-dev-story` followed by a `bmad-qa-generate-e2e-tests` QA gap-filling pass. Framework
reused: **xUnit v3 + Shouldly** for unit, integration, and
architecture lanes. Story 3.6 realizes the pure-domain and pure-reactor portion of FR-10 cascade
semantics: a parent cancel/expire terminal event is translated into same-kind terminal command intents
for caller-supplied, still-active descendants, and each descendant aggregate applies its own lifecycle
table. Duplicate/redelivered cascade commands stay safe through **target-aggregate idempotency** — no
out-of-band dedup store. There is no UI/HTTP surface; the executable end-to-end path is **parent terminal
event → `JsonSerializerDefaults.Web` JSON → pure `TerminalCascadeTranslator` → descendant command intent →
`WorkItemAggregate.Handle` → descendant terminal event → replayed `WorkItemState` / roll-up projection**.

Runtime cascade dispatch, checkpoint persistence, retry loops, durable continuation, AppHost restart
recovery, reminder reconciliation, and Aspire crash/recovery proof are **out of scope** and deferred to
Story 4.6.

Story 3.5 final baseline was **457** green tests (UnitTests 368, IntegrationTests 60, ArchitectureTests
28, PropertyTests 1). The Story 3.6 dev-story pass added **+20** tests (+18 unit, +2 integration), raising
the total to **477** green: UnitTests 386, IntegrationTests 62, ArchitectureTests 28, PropertyTests 1. The
follow-up `bmad-qa-generate-e2e-tests` QA pass then added **+4** tests (+3 unit, +1 integration) to close
residual defensive-contract and cross-terminal-through-serialization gaps, raising the total to **481**
green: UnitTests 389, IntegrationTests 63, ArchitectureTests 28, PropertyTests 1. **No production code was
changed by the QA pass** — only test files were extended; the v1 catalog (`WorkItemV1Catalog.Count` 36)
and golden corpus are unchanged.

**No durable wire shape changed.** The cascade reuses the existing terminal commands (`CancelWorkItem`,
`ExpireWorkItem`) and terminal events (`WorkItemCancelled`, `WorkItemExpired`); `WorkItemV1Catalog.Count`
remains **36** and the golden corpus is unchanged. The new Reactor types (`CascadeDescendant`,
`TerminalCascadeTranslator`) are Reactor-local, pure, and **not** decorated for polymorphic serialization,
so the frozen v1 catalog is untouched. No aggregate/lifecycle code changed — the existing AR-13 transition
table already satisfies AC #2/#3 — so `docs/lifecycle-transition-matrix.md` is left intact and referenced
from the cascade doc.

## Production code added (pure reactor slice)

- [x] `src/Hexalith.Works.Reactor/CascadeDescendant.cs` — **new.** Reactor-local input record carrying
  only `TenantId`, `WorkItemId`, and an `IsTerminal` marker (no EventStore envelope, Dapr metadata,
  checkpoint state, parent status decision, roll-up total, Party data, or adapter detail). Mirrors the
  `AwaitingParent` style.
- [x] `src/Hexalith.Works.Reactor/TerminalCascadeTranslator.cs` — **new.** Pure, mechanical translator
  mirroring `ChildCompletionResumeTranslator`: maps a parent `WorkItemCancelled` to descendant
  `CancelWorkItem` intents and a parent `WorkItemExpired` to descendant `ExpireWorkItem` intents.
  Fail-closed tenant equality (D3), explicit terminal-candidate skip (D2), input-order preserving, no tree
  traversal, no acceptance decision (D1/D4). `Hexalith.Works.Reactor` still references
  `Hexalith.Works.Contracts` only — `DependencyDirectionTests` unchanged and green.

## Tests added

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemTerminalCascadeIdempotencyTests` — **new file, +8 cases (2 facts + Theory ×6)** (AC #2/#3).
  Proves the cascade-delivery contract at the transition table: duplicate `CancelWorkItem` against
  `Cancelled` and duplicate `ExpireWorkItem` against `Expired` return `DomainResult.NoOp` with no terminal
  event, no rejection event, and no sequence burn; and cross-terminal commands (cancel of
  `Expired`/`Completed`/`Rejected`, expire of `Cancelled`/`Completed`/`Rejected`) return
  `WorkItemTransitionRejected` with no terminal success event and no state/sequence change.
- [x] `TerminalCascadeTranslatorTests` — **new file, +8 facts** (AC #1/#4/#5/#6). Proves cancelled/expired
  parents emit the matching command intents for same-tenant active descendants in input order;
  already-terminal candidates are skipped (no duplicate terminal intent); cross-tenant descendants are
  ignored even when work item ids collide; empty input yields no commands; and the input model carries no
  status acceptance decision (redelivery yields one intent per entry — idempotency is the target
  aggregate's job, not a translator dedup).
- [x] `WorkItemRollUpProjectionTests` — **+2 facts** (AC #1/#2/#3). A cascade-cancelled parent/child
  subtree zeros each descendant's contribution and drops the open subtree from a still-active ancestor's
  rolled Remaining; duplicate/stale cascade-expire delivery converges to zero contribution (replay-safe).

### Integration tests (`tests/Hexalith.Works.IntegrationTests`)

- [x] `TerminalCascadeContractFlowTests` — **new file, +2 facts** (AC #1/#2/#3/#6). A parent terminal
  event crosses the real `System.Text.Json` boundary, the pure translator produces the descendant command
  intent, that intent crosses the boundary, and an independent descendant aggregate applies its own
  terminal transition; redelivery of the same cascade command to the now-terminal descendant is an
  idempotent no-op (no duplicate event, no sequence burn). Cross-tenant (colliding id) and already-terminal
  descendants are never targeted.
- [x] `Hexalith.Works.IntegrationTests.csproj` — added `Hexalith.Works.Reactor` and `Hexalith.Works.Testing`
  project references (test project only; not governed by the `src/`-scoped dependency-direction fitness).

## Documentation

- [x] `docs/work-tree-shape-guard.md` — added a **Terminal Cascade Through Active Descendants** section:
  tenant-safe descendant discovery is supplied to the pure translator, the translator emits only
  mechanical command intents, tenant equality fails closed, target aggregates decide outcomes via AR-13,
  idempotency lives on both sides, and runtime checkpoint/recovery is deferred to Story 4.6.
- [x] `docs/lifecycle-transition-matrix.md` — **unchanged.** Code behavior did not change; the AR-13
  per-state Cancel/Expire table and idempotent no-op list remain the single source of truth, referenced
  from the cascade doc.

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

All four additions are genuine, non-redundant gaps verified against AC #1–#6 and design decisions D1–D4.
The dev-story baseline already covered happy-path cancel/expire translation, active-vs-terminal filtering,
fail-closed cross-tenant ignore, the aggregate idempotency/cross-terminal matrix, roll-up zeroing, and
*same-kind* cascade redelivery through serialization. The residual gaps were the translator's **defensive
contract** (no null-guard coverage — the QA checklist's required critical-error path), an **interleaved
filter-and-order** case, and the **cross-terminal cascade through the real serialization boundary**:

- [x] **Defensive contract — null guards (unit, +2 facts).**
  `Null_parent_terminal_event_or_null_descendant_list_throws_for_both_cascade_kinds` and
  `Null_descendant_element_fails_closed_with_argument_null_exception` prove the pure translator's
  `ArgumentNullException.ThrowIfNull` guards fire for a null trigger event, a null candidate list, and a
  null element inside the list, across both the cancel and expire overloads. A null candidate fails closed
  (eager materialization throws) rather than emitting a partial cascade. The baseline had no
  critical-error-path coverage on the translator.
- [x] **AC #1/#6 — interleaved filter and input order (unit, +1 fact).**
  `Mixed_batch_preserves_active_target_order_while_skipping_terminal_and_cross_tenant_candidates` proves
  active targets keep their relative input order when terminal and foreign-tenant (colliding-id) candidates
  are interleaved between them — the baseline only proved order with contiguous active candidates.
- [x] **AC #3 — cross-terminal cascade through serialization (integration, +1 fact).**
  `Parent_cancel_cascade_reaching_an_already_expired_descendant_rejects_through_serialization_without_a_duplicate_terminal_event`
  models a stale snapshot: the caller marks a descendant active, the translator emits a `CancelWorkItem`
  intent, but by delivery time the descendant has already Expired on its own. Across the real
  `JsonSerializerDefaults.Web` boundary the cross-terminal cascade command rejects
  (`WorkItemTransitionRejected`, `FromStatus=Expired`, `AttemptedAct="Cancel"`) with no duplicate terminal
  event and no status/sequence change — asserting persisted descendant end-state, not just a result flag.
  The baseline integration flow proved only *same-kind* redelivery no-op, not cross-terminal rejection
  through the pipeline.

## Story 3.6 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **389/389** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **63/63** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **28/28** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed (`Ok, passed 100 tests.`).

### Story 3.6 Test Counts

| Suite | Story 3.5 Final | Story 3.6 dev-story | QA gap pass | QA Delta |
|-------|----------------:|--------------------:|------------:|---------:|
| UnitTests | 368 | 386 | **389** | +3 |
| IntegrationTests | 60 | 62 | **63** | +1 |
| ArchitectureTests | 28 | 28 | **28** | — |
| PropertyTests | 1 | 1 | **1** | — |
| **Total** | **457** | **477** | **481** | **+4** |

### Not-applicable runtime / E2E surfaces

- No Dapr dispatch, EventStore stream reads, runtime checkpoint persistence, retry loops, durable cascade
  continuation, AppHost restart recovery, reminder reconciliation, timer/clock, or Aspire crash/recovery
  surface — deliberately deferred to Story 4.6, so the AppHost/Aspire integration lane was **not exercised**
  for cascade in this story.
- No UI/MCP/HTTP/browser surface (Story 3.6 is a pure Contracts + Server + Reactor + Projections + docs
  slice), so browser/UI E2E is **not applicable**.

### Checklist

- [x] Unit tests cover the aggregate cascade-delivery idempotency contract (duplicate self-terminal no-op,
  cross-terminal rejection) close to the transition table.
- [x] Unit tests cover the pure reactor translation (cancel/expire kinds, active vs terminal filtering,
  fail-closed tenant equality, no acceptance decision, input-order determinism).
- [x] Integration tests cover the cascade across the serialization boundary into descendant aggregates with
  idempotent redelivery.
- [x] Roll-up regression proves cascade terminal events zero descendant contribution and remove the open
  subtree from ancestor roll-ups.
- [x] No new durable event/command types; `WorkItemV1Catalog.Count` stays 36 and the golden corpus is
  unchanged.
- [x] `Hexalith.Works.Reactor` stays Contracts-only (no Dapr/Aspire/EventStore-runtime/clock/IO);
  `DependencyDirectionTests` unchanged and green.
- [x] Documentation updated (cascade section in `docs/work-tree-shape-guard.md`); AR-13 matrix left intact.
- [x] Build clean (0 warnings / 0 errors) and all four test binaries green (481/481 after the QA pass).
- [x] QA gap pass added defensive null-guard, interleaved filter/order, and cross-terminal-through-
  serialization coverage; no production code changed.

---

# Test Automation Summary — Story 3.5 (Suspend and Resume on Await-Conditions)

Workflow: `bmad-dev-story` followed by a `bmad-qa-generate-e2e-tests` QA gap-filling pass. Framework
reused: **xUnit v3 + Shouldly** for unit, integration, architecture, and property lanes. Story 3.5
implements first-match await-condition suspension and resume in the pure domain slice, with no runtime
dispatch, reminders, clocks, Dapr, HTTP, filesystem, or adapter I/O. There is no UI/HTTP surface; the
executable end-to-end path is **command → `WorkItemAggregate.Handle` → durable event → concrete
`JsonSerializerDefaults.Web` JSON → replayed `WorkItemState`** (plus the pure reactor translation), and
that is the layer the QA pass targets.

Story 3.4 baseline was **416** green tests. The Story 3.5 dev-story pass finished at **435** green tests
(UnitTests 352, IntegrationTests 54, ArchitectureTests 28, PropertyTests 1). The QA gap-filling pass
then added **+22** tests (+16 unit, +6 integration) to close residual branch/AC coverage gaps, raising
the total to **457** green tests: UnitTests 368, IntegrationTests 60, ArchitectureTests 28,
PropertyTests 1. **No production code was changed** — only test files were added/extended; the v1
catalog and golden corpus are unchanged (the new tests exercise existing durable types through the
aggregate, reactor, and replay, not new wire shapes).

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

All were genuine, non-redundant gaps verified against AC #1–#7 and design decisions D1–D6. The dev
baseline already covered multi-condition suspend, invalid-source suspend rejection, keyless suspend
rejection, suspended-progress rejection, matching/non-matching resume, the duplicate consumed-key
no-op, kind-aware construction validation, the matching/non-matching reactor translation, and the
ChildCompleted/ExternalSignal serialization flows. The genuine gaps were uncovered **branches**, the
**date seam never crossing serialization**, and one explicit **AC #2 clause**:

- [x] **AC #1/#3 — resume from non-suspended statuses (unit).**
  `Resume_from_any_non_suspended_status_is_rejected_and_burns_no_sequence` (Theory ×8) proves
  `ResumeWorkItem` is a transition rejection from `Created`, `Assigned`, `Queued`, **`InProgress`**,
  and the four terminals, burning no sequence. The baseline only proved the suspended sources; the
  never-suspended-`InProgress` reject branch (key ≠ a still-null last-consumed key) was uncovered.
- [x] **AC #4 — keyless resume while suspended (unit).**
  `Resume_while_suspended_with_no_supplied_condition_is_rejected_and_preserves_await_set` covers the
  `AwaitCondition is null` reject branch while `Suspended` (the baseline only sent a *different* key).
- [x] **AC #7 / D3 / D5 — the `DateReached` seam (unit + integration).** Date awaits were never
  resumed-by-match nor serialized anywhere. Added
  `Date_reached_resume_matches_the_same_instant_in_a_different_offset_and_replays_to_in_progress` and
  `Date_reached_resume_one_second_off_does_not_match_and_keeps_the_item_suspended` (unit), plus a new
  `AwaitConditionSerializationContractFlowTests` integration class proving all three kinds round-trip
  through `System.Text.Json`, the `DateReached` UTC-normalized correlation key survives round-trip,
  kind-aware inequality survives the boundary, and a full suspend(`DateReached`)→resume(`DateReached`)
  flow converges to `InProgress` across serialization with UTC-offset-equivalent keys.
- [x] **D1 — first-match consuming the child key from a mixed suspension (unit).**
  `Matching_resume_can_consume_the_child_completion_condition_from_a_mixed_suspension` proves first-match
  is not external-signal-specific: the child-completion key releases a multi-kind suspension and still
  clears the whole set.
- [x] **AC #6 / D3 / D4 — reactor translator edges (unit).** Added empty-list, multi-parent (one
  matching / one not / one matching → two intents), and kind-collision (`ExternalSignal(child-id)` must
  **not** match `ChildCompleted`) cases so the mechanical, kind-aware fan-out is locked in.
- [x] **AC #2 — explicit suspended-child roll-up regression (unit).**
  `Suspended_child_keeps_contributing_remaining_and_resume_only_flips_status` asserts the *intermediate*
  suspended state contributes its current Remaining (parent rolled 9) and that resume changes only the
  status — the baseline asserted only the final post-resume state.

## Gaps closed by the dev-story baseline

### Contracts and aggregate behavior

- [x] `AwaitCondition` now carries kind-aware stable keys for `ChildCompleted(childId)`,
  `DateReached(instant)`, and `ExternalSignal(correlationId)`.
- [x] `SuspendWorkItem` carries one or more await conditions; the aggregate rejects keyless suspend
  attempts and leaves state/sequence unchanged.
- [x] `ResumeWorkItem` carries a matching condition; the aggregate accepts only current suspended-set
  matches, rejects mismatches without sequence burn, and no-ops only duplicate consumed keys after replay.
- [x] `WorkItemSuspended` records the full condition set and still tolerates legacy single/null payloads.
- [x] `WorkItemResumed` records the consumed condition so duplicate detection survives rehydration.

### Unit tests

- [x] Added focused suspend/resume coverage for multiple conditions, invalid lifecycle sources,
  keyless suspend rejection, suspended progress rejection with Remaining retained, matching resume,
  non-matching resume, duplicate consumed-key no-op, and kind-aware construction validation.
- [x] Added pure reactor tests proving child-completion input translates to parent `ResumeWorkItem`
  intents only for matching `ChildCompleted(childId)` conditions and carries no parent-status decision.
- [x] Updated lifecycle, progress, spawn-child, and roll-up regression tests to use condition-carrying
  suspend/resume events while preserving Story 3.2/3.3/3.4 behavior.

### Integration, serialization, and docs

- [x] Updated lifecycle contract-flow tests to serialize real await-condition suspend/resume commands.
- [x] Updated polymorphic catalog samples and golden corpus files for condition-carrying
  `WorkItemSuspended` and consumed-key `WorkItemResumed`.
- [x] Added explicit legacy payload tolerance for older suspend/resume JSON.
- [x] Updated `docs/lifecycle-transition-matrix.md` and `docs/work-tree-shape-guard.md` with the
  first-match suspend/resume policy and mechanical child-completion reactor rule.

## Story 3.5 Validation (after QA pass)

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **368/368** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **60/60** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **28/28** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed (`Ok, passed 100 tests.`).

### Story 3.5 Test Counts

| Suite | Story 3.4 Final | Story 3.5 dev-story | Story 3.5 + QA pass | QA Delta |
|-------|----------------:|--------------------:|--------------------:|---------:|
| UnitTests | 335 | 352 | **368** | +16 |
| IntegrationTests | 52 | 54 | **60** | +6 |
| ArchitectureTests | 28 | 28 | **28** | — |
| PropertyTests | 1 | 1 | **1** | — |
| **Total** | **416** | **435** | **457** | **+22** |

### Files touched by the QA pass

- `tests/Hexalith.Works.UnitTests/WorkItemSuspendResumeTests.cs` — +4 methods (one Theory ×8) = +11 cases.
- `tests/Hexalith.Works.UnitTests/ChildCompletionResumeTranslatorTests.cs` — +3 reactor edge cases.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` — +1 AC #2 suspended-child regression.
- `tests/Hexalith.Works.IntegrationTests/AwaitConditionSerializationContractFlowTests.cs` — **new file**,
  +6 cases (one Theory ×3) covering the `AwaitCondition` value object and the `DateReached` serialization seam.

### Checklist

- [x] API/contract tests cover suspend/resume command → aggregate → event → JSON → replay, including the
  previously-unserialized `DateReached` await-condition seam.
- [x] E2E/UI tests marked not applicable (Story 3.5 is pure Contracts + Server + Reactor + docs; no
  UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`).
- [x] Tests cover happy paths (multi-kind suspend, matching resume across all three kinds, date-seam
  round-trip).
- [x] Tests cover critical error/edge cases (resume from every non-suspended status, keyless resume,
  date near-miss, kind-collision in the reactor).
- [x] All generated tests run successfully (457/457).
- [x] Tests use clear descriptions and semantic assertions.
- [x] No hardcoded waits or sleeps — `DateReached` is command-delivered data; no wall-clock is read.
- [x] Tests are independent (each arranges its own replayed state).
- [x] Test summary updated with coverage metrics.

---

# Test Automation Summary — Story 3.4 (Preserve Heterogeneous Unit Subtotals)

Workflow: `bmad-dev-story` followed by a `bmad-qa-generate-e2e-tests` QA gap-filling pass. Framework
reused: **xUnit v3 + Shouldly** for focused unit/architecture coverage and **FsCheck** for convergence
coverage. Story 3.4 hardens Story 3.3 roll-up behavior by making heterogeneous-unit subtotals explicit
and adding read-side fail-closed handling for already persisted unit-incompatible events. There is no
UI/HTTP surface for this story; the executable end-to-end path is command handling (write-side guard)
and projection delivery facts into the pure recursive roll-up strategy, then consumer read-model
inspection — that is the layer the QA pass targets.

Story 3.3 final baseline was **404** green tests (UnitTests 325, IntegrationTests 52,
ArchitectureTests 26, PropertyTests 1). The Story 3.4 dev-story pass added **+7** unit tests, **+1**
architecture fitness test, and extended the existing property test to generate mixed-unit trees plus
deterministic degraded-event convergence (subtotal **412**). The QA gap-filling pass then added **+3**
unit tests and **+1** architecture fitness test to close residual AC coverage gaps, raising the total to
**416** green tests.

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

All four were genuine, non-redundant gaps verified against the six acceptance criteria:

- [x] **AC #2 — within-unit summation vs cross-unit separation.** Every prior mixed-unit test had each
  Unit appear once. `Same_unit_children_sum_within_bucket_while_a_different_unit_child_stays_separate`
  proves two same-Unit children fold into one subtotal (5+3 ⇒ 8 hour) while a different-Unit child stays
  separate (4 point) and no coerced single value appears.
- [x] **AC #5 — sticky-degraded continuation.**
  `Degraded_node_refuses_only_the_bad_event_then_applies_a_later_matching_unit_event` proves fail-closed
  refuses *only* the unit-incompatible event, a later matching-unit progress still burns down from the
  last valid value, and the node stays degraded with exactly one re-derived diagnostic.
- [x] **AC #4 — write-side-to-read-side bridge.**
  `Rejected_unit_mismatched_command_emits_no_event_so_projection_stays_fresh_and_not_degraded` ties the
  aggregate rejection (no `ProgressReported` emitted) to the projection staying unchanged and **not**
  degraded — the end-to-end contrast with the AC #5 persisted-bad-event path.
- [x] **AC #5 — diagnostic metadata-only structural guard.**
  `P0_RollUpProjectionDiagnosticExposesOnlyMetadataNeverPayloadValues` locks `RollUpProjectionDiagnostic`
  to exactly `TenantId`, `WorkItemId`, `EventType`, `Sequence` so a future change cannot reintroduce a
  payload-bearing field — the drift guard mirroring the existing no-coerced-total guard.

## Gaps closed this run

### Contracts and projections

- [x] Added `RollUpProjectionDiagnostic` and additive `WorkItemRollUp.Degraded` /
  `ProjectionDiagnostics` read-model properties.
- [x] `WorkItemRollUpProjection` now refuses persisted `ProgressReported` and `ReEstimated` events whose
  unit disagrees with an established node unit, retaining the last valid value and surfacing
  metadata-only diagnostics.
- [x] Degraded state and diagnostics are re-derived during replay and remain pure data; no logging,
  clocks, ids, filesystem, Dapr, or runtime I/O were introduced.

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] Added explicit same-unit subtotal coverage: one unit produces one labeled subtotal and a non-null
  single `RolledRemaining`.
- [x] Added deeper three-unit mixed-tree coverage proving per-unit grouping and no coerced all-unit total.
- [x] Added same-unit progress/re-estimate mixed-tree coverage proving matching-unit updates leave other
  units untouched.
- [x] Added fail-closed coverage for unit-mismatched `ProgressReported` and `ReEstimated`, including
  retain-last-valid values, degraded marking, metadata-only diagnostics, and duplicate/out-of-order
  convergence.
- [x] Added establishment boundary and terminal precedence coverage.
- [x] Tightened command-side unit mismatch tests to assert no `ProgressReported`/`ReEstimated` event is
  emitted from rejected commands.

### Property tests (`tests/Hexalith.Works.PropertyTests`)

- [x] Extended the convergence property to generate heterogeneous-unit trees and compare
  `RolledRemainingByUnit`, `Degraded`, and `ProjectionDiagnostics` under duplicate/permuted delivery.
- [x] Included a deterministic post-establishment unit mismatch in generated scenarios and asserted the
  degraded outcome converges.

### Architecture and documentation

- [x] Updated `docs/work-roll-up-projection.md` with heterogeneous-unit semantics, command-side unit
  rejection, read-side fail-closed behavior, retain-last-valid degraded marking, and metadata-only
  diagnostic rules.
- [x] Architecture fitness remains inside the existing roll-up owned locations:
  `src/Hexalith.Works.Contracts/Models` and `src/Hexalith.Works.Projections`.

## Story 3.4 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **335/335** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **52/52** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **28/28** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed; FsCheck reported **100** generated cases.

### Story 3.4 Test Counts

| Suite | Story 3.3 Final | Story 3.4 dev-story | Story 3.4 + QA pass | Delta |
|-------|----------------:|--------------------:|--------------------:|------:|
| UnitTests | 325 | 332 | **335** | +10 |
| IntegrationTests | 52 | 52 | **52** | — |
| ArchitectureTests | 26 | 27 | **28** | +2 |
| PropertyTests | 1 | 1 | **1** | extended |
| **Total** | **404** | **412** | **416** | **+12** |

### Checklist

- [x] Same-unit and mixed-unit roll-up subtotals are explicitly verified.
- [x] Projection fail-closed behavior is covered for persisted incompatible progress and re-estimate
  events.
- [x] Degraded diagnostics expose only tenant, work item, event type, and sequence metadata.
- [x] Command-side unit mismatch guards are verified to emit no deliverable projection event.
- [x] Property coverage verifies heterogeneous-unit and degraded convergence under duplicate/permuted
  delivery.
- [x] Documentation, build, architecture tests, and all runtime test binaries passed.

---

# Test Automation Summary — Story 3.3 (Maintain Recursive Roll-Up with Per-Child Sequence)

Workflow: `bmad-qa-generate-e2e-tests` (QA gap-filling pass) after `bmad-dev-story`. Framework reused:
**xUnit v3 + Shouldly** for focused projection/unit coverage and **FsCheck** for convergence coverage.
Story 3.3 adds explicit roll-up read-model contracts, a pure recursive projection strategy,
tenant-safe traversal, duplicate/out-of-order convergence, terminal zero-contribution behavior,
mixed-unit single-value protection, and a narrowed architecture guard that permits roll-up only in the
owned contracts/projections locations. There is no UI/browser surface for this story; the executable
end-to-end path is projection delivery facts into the pure recursive roll-up strategy and consumer
read-model inspection.

Story 3.2 final baseline was **386** green tests (UnitTests 307, IntegrationTests 52,
ArchitectureTests 26, PropertyTests 1). Story 3.3 adds **+18** unit tests and replaces the scaffold
property with a real FsCheck convergence property, raising the total to **404** green tests.

## Gaps closed this run

### Contracts and projections

- [x] Added `OwnRemaining`, `RolledRemaining`, and `WorkItemRollUp` read-model contracts under
  `src/Hexalith.Works.Contracts/Models`.
- [x] Added `WorkItemRollUpEvent` delivery facts and `WorkItemRollUpProjection` pure strategy under
  `src/Hexalith.Works.Projections`.
- [x] Projection derives own remaining from create/progress/re-estimate events, assigns terminal
  contribution to zero, recomputes recursive rolled remaining, and exposes mixed units as per-unit
  values without fabricating a single total.
- [x] Tenant equality is checked at edge creation and traversal so cross-tenant/colliding ids cannot
  leak child totals.

### QA gap-fill tests (`tests/Hexalith.Works.UnitTests`)

- [x] Added child-before-edge convergence coverage so progress delivered before parent edge
  materialization still rolls up when `ChildSpawned` later establishes the relationship.
- [x] Added stale-after-terminal coverage proving a lower-sequence non-terminal child event cannot
  resurrect contribution after completion.
- [x] Added non-roll-up lifecycle event coverage for assignment, queue, claim, suspend, resume, and
  reschedule events so they remain tolerated without changing effort totals.
- [x] Added invalid delivery-fact coverage for non-positive sequence and tenant/payload mismatches.

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemRollUpProjectionTests` covers one parent/child, nested descendants, own-vs-rolled type
  distinction, duplicate delivery, out-of-order delivery, all terminal zero-contribution events,
  `Requeue: true` non-terminal behavior, edge materialization from both `WorkItemCreated.Parent` and
  `ChildSpawned`, child-before-edge delivery, stale-after-terminal ordering, ignored non-roll-up
  lifecycle events, invalid delivery facts, tenant isolation, mixed units, and unestimated items.

### Property tests (`tests/Hexalith.Works.PropertyTests`)

- [x] Replaced the scaffold FsCheck availability test with `WorkItemRollUpConvergencePropertyTests`.
- [x] FsCheck generates bounded tenant-safe tree shapes with nested descendants, optional terminal child
  events, duplicate/permuted delivery, and a cross-tenant colliding id; each generated case converges to
  canonical sequence-order replay without leaking foreign-tenant totals.

### Architecture and documentation

- [x] Updated `ScaffoldGovernanceTests` so roll-up is allowed only in Contracts read-models and
  Projections implementation/input code while reminders remain deferred.
- [x] Added `docs/work-roll-up-projection.md` describing the per-child latest-state rule, tenant
  equality assertion, terminal zero contribution, and own-vs-rolled remaining distinction.

## Story 3.3 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **325/325** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **52/52** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed; FsCheck reported **100** generated cases.

### Story 3.3 Test Counts

| Suite | Story 3.2 Final | Story 3.3 Final | Delta |
|-------|----------------:|----------------:|------:|
| UnitTests | 307 | **325** | +18 |
| IntegrationTests | 52 | **52** | — |
| ArchitectureTests | 26 | **26** | — |
| PropertyTests | 1 | **1** | replaced scaffold |
| **Total** | **386** | **404** | **+18** |

### Checklist

- [x] Read-model contracts distinguish own remaining from eventual rolled remaining.
- [x] Projection tests cover recursive propagation, duplicates, out-of-order delivery, terminal events,
  non-terminal requeue, tenant isolation, mixed units, and unestimated items.
- [x] Property coverage verifies convergence under generated tree shapes with duplicate/permuted delivery
  and tenant-collision noise.
- [x] Architecture fitness and documentation updated for Story 3.3 roll-up scope.
- [x] All validation commands passed.

---

# Test Automation Summary — Story 3.2 (Spawn Child Work from a Parent)

Workflow: `bmad-qa-generate-e2e-tests` (QA gap-filling pass) after `bmad-dev-story`. Framework reused:
**xUnit v3 + Shouldly**, pure aggregate unit tests, contract-flow integration tests, schema-evolution
golden corpus, and existing architecture/property guardrails. Story 3.2 adds `SpawnChild`,
`ChildSpawned`, parent replay of spawned child references, and the minimal child-completion await
condition used when a spawn suspends its parent. This is a pure `Contracts` + `Server` + docs slice
with **no UI/browser surface**, so the executable end-to-end path is **command →
`WorkItemAggregate.Handle` → durable event → concrete `JsonSerializerDefaults.Web` JSON → replayed
`WorkItemState`**; browser/UI E2E is **not applicable**.

The dev-authored Story 3.2 baseline was **375** green tests (UnitTests 296, IntegrationTests 52,
ArchitectureTests 26, PropertyTests 1). This QA run mapped the dev coverage against AC #1–#5 and the
recorded design decisions (D1–D5), discovered **5 genuine branch-level gaps**, and auto-applied
**+11** unit test cases (5 new test methods; two are Theories), raising the total to **386** green.
**No production code was changed** — only `WorkItemSpawnChildTests.cs` was extended.
`WorkItemV1Catalog.Count` remains **36** (14 success events, 14 commands, 8 rejection events) and the
`ChildSpawned.v1.json` golden fixture is unchanged — the new tests exercise existing durable types
through the aggregate and replay, not new wire shapes.

## Gaps auto-applied this QA run

Mapped against AC #1–#5, the dev baseline already covered spawn from `Created`, spawn-with-await from
`InProgress`, suspended-parent progress rejection, the four tree-guard rejection paths, missing/terminal
parent rejection, await-requires-`InProgress`, replay determinism, caller-supplied child ids, and the
full serialization/golden/legacy round-trips. The genuine gaps were uncovered **branches** and one
explicit **AC clause** — each closed below in `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs`:

- [x] `SpawnChild_without_suspension_is_accepted_from_every_live_status` (AC #1 / Task 4, Theory ×5) —
  plain spawn is **accepted** from `Created`, `Assigned`, `Queued`, `InProgress`, **and** `Suspended`,
  each emitting one `ChildSpawned` at `Sequence + 1`, replaying the child reference, and leaving the
  parent's lifecycle status unchanged. The baseline only proved acceptance from `Created`; for the other
  live statuses it proved only the *negative* (await-requires-`InProgress`), never that a plain spawn
  succeeds.
- [x] `SpawnChild_without_obligation_returns_missing_obligation_rejection_for_the_child` (AC #1
  CreateWorkItem semantics, Theory `null`/`""`/`"   "`) — a missing obligation returns
  `WorkItemCannotBeCreatedWithoutObligation` raised against the **child** id, rejection-only, with no
  `ChildSpawned`. This Handle branch had zero coverage (every prior test supplied an obligation).
- [x] `SpawnChild_tree_guard_rejection_is_replay_safe_and_burns_no_parent_sequence` (AC #4 "the
  rejection is replay-safe") — proves a rejected spawn mutates no parent state and **consumes no
  sequence number**: re-handling is deterministic, and a subsequent valid spawn still receives the next
  contiguous sequence. The explicit AC #4 replay-safety clause was previously unverified for spawn.
- [x] `SpawnChild_duplicate_event_replay_is_idempotent_and_distinct_children_accumulate_in_order`
  (AC #5 / Task 3 determinism) — applying the same `ChildSpawned` twice yields a single child reference
  (exercises the `Apply(ChildSpawned)` `Contains` dedup branch), while two distinct events accumulate
  both ids in order. The dedup branch and multi-child accumulation were untested.
- [x] `SpawnChild_retry_with_existing_child_parent_equal_to_proposed_parent_is_accepted` (D3 guard
  idempotency) — a retry whose `ExistingChildParent` equals the proposed parent is **accepted** (not a
  second-parent rejection), exercising the guard's same-parent-idempotency branch. The baseline's
  second-parent case supplied a *different* existing parent, so the accept-on-same-parent branch was
  uncovered.

## Gaps closed by the dev-story baseline

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemSpawnChildTests` covers successful spawn from an existing parent, replay of parent-owned
  child id references, and construction of equivalent child `CreateWorkItem` facts with the same tenant
  and `ParentWorkItemReference`.
- [x] Spawn with await intent emits `ChildSpawned` then `WorkItemSuspended`, advances parent sequences
  monotonically, and replays to `Suspended` with `AwaitCondition(childWorkItemId)`.
- [x] Suspended parents reject `ReportProgress` through the existing lifecycle rejection path.
- [x] Tree-guard rejection paths cover cross-tenant ancestor facts, self/cycle facts, second-parent
  facts, and max-depth overflow, each returning rejection-only results.
- [x] Missing/terminal parent states reject `SpawnChild`; await intent is accepted only from
  `InProgress`.
- [x] Replay determinism verifies sequence, status, child references, and await conditions reconstruct
  identically; caller-supplied child ids are preserved.

### Integration tests (`tests/Hexalith.Works.IntegrationTests`)

- [x] `WorkItemSpawnChildContractFlowTests` round-trips `SpawnChild`, `ChildSpawned`,
  `WorkItemSuspended.AwaitCondition`, and legacy `WorkItemSuspended` JSON without the new optional field.
- [x] `WorkItemV1Catalog` now includes `SpawnChild`, `ChildSpawned`, and an await-condition sample on
  `WorkItemSuspended` for polymorphic registration coverage.
- [x] `SchemaEvolutionGoldenCorpusTests` now freezes and round-trips `ChildSpawned.v1.json`, including
  additive unknown-field tolerance.

### Documentation

- [x] `docs/work-tree-shape-guard.md` records that `SpawnChild` reuses the pure caller-fed tree guard and
  stores only lightweight parent-owned child references.

## Story 3.2 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **307/307** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **52/52** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 3.2 Test Counts

| Suite | Story 3.1 Final | Dev 3.2 Baseline | QA Final | QA Delta |
|-------|----------------:|-----------------:|---------:|---------:|
| UnitTests | 278 | 296 | **307** | +11 |
| IntegrationTests | 46 | 52 | **52** | — |
| ArchitectureTests | 26 | 26 | **26** | — |
| PropertyTests | 1 | 1 | **1** | — |
| **Total** | **351** | **375** | **386** | **+11** |

### Checklist

- [x] API/contract tests cover spawn command/event flow, await-condition payloads, and envelope omission.
- [x] E2E/UI tests marked not applicable (Story 3.2 is pure Contracts + Server + docs; no UI/browser
  surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly).
- [x] Tests cover happy paths (spawn, spawn with await, replay, child-create facts).
- [x] Tests cover critical error/edge cases (missing/terminal parent, await from non-`InProgress`,
  cross-tenant ancestor, self/cycle, second parent, depth overflow).
- [x] All generated tests run successfully (386/386).
- [x] Tests use clear descriptions and semantic assertions.
- [x] No hardcoded waits or sleeps.
- [x] Tests are independent and arrange their own facts/state.
- [x] Tests saved to the appropriate existing project directories.
- [x] Test summary includes coverage metrics.

## Notes

- This QA run is **gap-filling only** — no production code was changed; only
  `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs` was extended (+5 test methods / +11
  cases). No golden fixture or catalog change was needed (`Count` stays **36**).
- The five gaps were each an uncovered branch or explicit AC clause: (1) plain-spawn acceptance from the
  four live statuses beyond `Created`; (2) the missing-obligation child rejection branch; (3) AC #4's
  replay-safety clause (rejected spawn burns no sequence); (4) the `Apply(ChildSpawned)` dedup branch
  plus multi-child accumulation; (5) the guard's same-parent idempotency accept branch on retry.

---

# Test Automation Summary — Story 3.1 (Guard Tenant-Safe Work Tree Shape)

Workflow: `bmad-qa-generate-e2e-tests` after `bmad-dev-story`. Framework reused: **xUnit v3 +
Shouldly**, pure domain unit tests, contract-flow integration tests, and existing
architecture/property guardrails. Story 3.1 adds a pure caller-fed tree attachment guard, specific
rejection payloads for second-parent/cycle/depth failures, create-path delegation for parent
validation, and documentation of the work-tree shape rules.

Story 2.5 final baseline was **332** green tests (UnitTests 260, IntegrationTests 45,
ArchitectureTests 26, PropertyTests 1). Story 3.1 adds **+18** unit tests and **+1** integration test,
raising the total to **351** green. `WorkItemV1Catalog.Count` is now **34** (13 success events, 13
commands, 8 rejection events). No durable success event shape changed, so no golden fixtures were added.

## Gaps closed this run

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkTreeAttachmentGuardTests` covers valid root items, valid first parent attachment,
  idempotent same-parent validation, same-tenant casing normalization, second-parent rejection,
  self-parent cycle rejection, ancestor-chain cycle rejection, cross-tenant parent rejection,
  cross-tenant ancestor rejection, default max-depth boundary acceptance, one-over-depth rejection, and
  uncapped breadth.
- [x] QA-generated gap tests cover same-tenant ancestor casing normalization, same-parent idempotency
  with different tenant casing, policy override acceptance above the default depth, and policy override
  rejection below the default depth.
- [x] `WorkItemCreateTests.CreateWorkItem_with_self_parent_reference_returns_cycle_rejection_without_mutating_state`
  proves create-time self-parenting returns a rejection-only result and leaves replay state at sequence
  `0`.
- [x] `WorkItemCreateTests.CreateWorkItem_with_existing_different_parent_returns_second_parent_rejection_and_leaves_state_unchanged`
  proves supplied current child state with a different parent rejects without advancing sequence or
  replacing the stored parent.

### Integration tests (`tests/Hexalith.Works.IntegrationTests`)

- [x] `WorkItemCreateContractFlowTests.CreateWorkItem_with_invalid_tree_shape_serializes_specific_rejection_payloads_without_envelope`
  round-trips the new rejection payloads and proves EventStore envelope metadata remains absent.
- [x] `WorkItemV1Catalog` now includes the three new public rejection payloads so polymorphic
  registration and concrete additivity tests cannot drift.

### Documentation

- [x] `docs/work-tree-shape-guard.md` records single-parent, acyclic, single-tenant, max-depth default
  `32`, policy-supplied override, uncapped breadth, and Story 3.1 exclusions.

## Story 3.1 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **278/278** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **46/46** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 3.1 Test Counts

| Suite | Story 2.5 Final | Story 3.1 Final | Delta |
|-------|----------------:|----------------:|------:|
| UnitTests | 260 | **278** | +18 |
| IntegrationTests | 45 | **46** | +1 |
| ArchitectureTests | 26 | **26** | — |
| PropertyTests | 1 | **1** | — |
| **Total** | **332** | **351** | **+19** |

### Checklist

- [x] API/contract tests generated for guard decisions and public rejection payload serialization.
- [x] E2E/UI tests marked not applicable (Story 3.1 is pure Contracts + Server + docs; no UI/browser
  surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly).
- [x] Tests cover happy paths (root, first parent, same-parent idempotency, same-tenant casing,
  at-limit depth, policy override above default, uncapped breadth).
- [x] Tests cover critical error/edge cases (second parent, self cycle, ancestor cycle, cross tenant,
  one-over-depth, smaller policy override).
- [x] All generated tests run successfully (351/351).
- [x] Tests use clear descriptions and semantic assertions.
- [x] No hardcoded waits or sleeps.
- [x] Tests are independent and arrange their own facts/state.
- [x] Tests saved to the appropriate existing project directories.
- [x] Test summary includes coverage metrics.

---

# Test Automation Summary — Story 2.5 (Complete, Cancel, Reject, and Expire Work)

Workflow: `bmad-qa-generate-e2e-tests`. Framework reused: **xUnit v3 + Shouldly**, Tier-1 domain and
contract-flow tests. Story 2.5 required no new production payloads or lifecycle table changes: the
existing aggregate and `docs/lifecycle-transition-matrix.md` already matched the terminal-state
semantics. This run strengthened focused verification around completion, cancel, terminal rejection,
planning-act guards, and expiry purity.

Story 2.4 final baseline was **289** green tests (UnitTests 217, IntegrationTests 45,
ArchitectureTests 26, PropertyTests 1). Story 2.5 adds **+43** unit test cases and keeps integration,
architecture, and property counts stable, raising the total to **332** green. (The automated review
added 8 cases on top of the original 35 to close a reject-disambiguation coverage gap; see below.)

## Gaps closed this run

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemLifecycleTests.Cancel_from_each_non_terminal_status_emits_cancelled_and_replay_rests_terminal`
  — covers cancel from `Created`, `Assigned`, `Queued`, `InProgress`, and `Suspended`, asserting
  `WorkItemCancelled`, `Sequence + 1`, and replay to terminal `Cancelled`.
- [x] `WorkItemLifecycleTests.Expire_from_each_non_terminal_status_emits_expired_and_replay_rests_terminal`
  — covers expiry from `Created`, `Assigned`, `Queued`, `InProgress`, and `Suspended`, asserting
  `WorkItemExpired`, `Sequence + 1`, and replay to terminal `Expired` without aggregate clock input.
- [x] `WorkItemLifecycleTests.Commands_after_cancel_are_rejected_and_leave_state_unchanged`
  — covers post-cancel progress, reschedule, assign, queue, claim, suspend, resume, complete, reject,
  and expire; every command returns `WorkItemTransitionRejected` and preserves status, effort,
  schedule, binding, and sequence.
- [x] `WorkItemLifecycleTests.Planning_acts_from_terminal_statuses_are_transition_rejections`
  — proves `ReEstimate` and `RescheduleWorkItem` reject from `Completed`, `Cancelled`, `Rejected`,
  and `Expired` without sequence advancement.
- [x] `WorkItemLifecycleTests.Noop_results_have_no_events_and_rejections_never_mix_success_payloads`
  — confirms terminal no-op results carry no events and illegal terminal commands return rejection-only
  payloads.
- [x] `WorkItemLifecycleTests.Reject_without_explicit_requeue_uses_default_requeue_and_rests_at_queued`
  — proves the public default `RejectWorkItem(TenantId, WorkItemId)` path emits `WorkItemRejected`
  with `Requeue = true`, advances sequence once, and replays to `Queued` for reassignment.
- [x] `WorkItemProgressTests.Progress_driven_completion_rejects_later_non_idempotent_terminal_commands`
  — after Remaining reaches zero and `WorkItemCompleted` replays, later progress, assignment,
  reschedule, and suspend commands are rejected and do not advance sequence.
- [x] `WorkItemProgressTests.Progress_driven_completion_noops_only_exact_duplicate_complete`
  — confirms exact duplicate completion is the idempotent no-op after progress-driven completion.
- [x] `WorkItemLifecycleTests.Default_reject_from_any_non_assigned_status_is_a_transition_rejection_and_never_reopens`
  — **(added by automated review)** proves a default `RejectWorkItem` from `Created`, `Queued`,
  `InProgress`, `Suspended`, `Completed`, `Cancelled`, `Rejected`, and `Expired` returns a
  `WorkItemTransitionRejected` (never a `WorkItemRejected`, never a no-op) and leaves status/sequence
  unchanged. Closes the gap where the data-driven matrix Theory excludes the `Reject` act, so the
  Task 4 claim that a requeue reject of an already-`Rejected` item never reopens it was unverified.

### Architecture tests (`tests/Hexalith.Works.ArchitectureTests`)

- [x] `ScaffoldGovernanceTests.P0_WorkItemKernelRemainsPure` now scans `Contracts`, `Server`, and
  `Projections`, and explicitly bans clock/timer APIs, generated IDs, Dapr, HTTP, and filesystem
  calls in the domain kernel. This covers expiry as an adapter-fired signal with no aggregate clock.

## Story 2.5 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **260/260** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **45/45** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 2.5 Test Counts

| Suite | Story 2.4 Final | Story 2.5 Final | Delta |
|-------|----------------:|----------------:|------:|
| UnitTests | 217 | **260** | +43 |
| IntegrationTests | 45 | **45** | — |
| ArchitectureTests | 26 | **26** | — |
| PropertyTests | 1 | **1** | — |
| **Total** | **289** | **332** | **+43** |

## Notes

- No production code or documentation matrix changes were required; Story 2.5 behavior was already
  encoded in `WorkItemLifecycle`, `WorkItemAggregate`, `WorkItemState`, and
  `docs/lifecycle-transition-matrix.md`.
- `WorkItemV1Catalog.Count` remains **31**. Terminal events and commands already existed in the v1
  catalog and golden corpus, so no new fixtures were generated.

### Checklist

- [x] API/contract tests generated (work-item command -> aggregate -> event/rejection -> replay; JSON
  contract-flow coverage already exists for terminal success and rejection payloads).
- [x] E2E/UI tests marked not applicable (Story 2.5 is pure Contracts + Server + Tier-1; no UI/browser
  surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly).
- [x] Tests cover happy paths (complete, cancel, reject default requeue/non-requeue, expire).
- [x] Tests cover critical error/edge cases (post-terminal rejections, closed no-op list, post-cancel
  rejection, planning-act terminal guards, expiry purity).
- [x] All generated tests run successfully (332/332).
- [x] Tests use clear descriptions and semantic assertions.
- [x] No hardcoded waits or sleeps.
- [x] Tests are independent and arrange their own replayed state.
- [x] Test summary includes coverage metrics.

---

# Test Automation Summary — Story 2.4 (Re-Estimate and Reschedule Work)

Workflow: `bmad-qa-generate-e2e-tests`. Role: QA automation engineer (test generation only — no code
review or story validation). Framework detected and reused: **xUnit v3 + Shouldly**, Tier-1 (no
Dapr/Aspire/containers/network/clock). Story 2.4 is a pure `Contracts` + `Server` domain slice with **no
UI, MCP, public route, AppHost, Dapr, projection, or adapter surface**, so browser/UI E2E is **not
applicable**; the executable end-to-end path is **command → `WorkItemAggregate.Handle` → raw-act event →
concrete `JsonSerializerDefaults.Web` JSON → replayed `WorkItemState`**, exercised by the unit +
contract-flow tests below.

The dev-authored Story 2.4 baseline was **285** green tests (UnitTests 213, IntegrationTests 45,
ArchitectureTests 26, PropertyTests 1). This QA run mapped the dev coverage against AC #1–#5 and the five
recorded Key Design Decisions (D1–D5), discovered four genuine gaps, and auto-applied **+4** unit tests,
raising the total to **289** green. **No production code was changed** — only test files were extended.

## Gaps auto-applied this run

The dev baseline already covered, per AC: same-Unit re-estimate up + replay (AC #1), the
created-with-effort Unit-mismatch rejection (AC #2), negative-estimate rejection, the below-Done clamp
with no completion (D5), first-estimate establishment on an unestimated item (D2), terminal/Unknown
rejection, schedule replacement with Priority + Due Date (AC #3), both-null "sorts last" acceptance
(AC #4), acceptance from every live status, the `WorkItemEffort.ReEstimate` value-object contract, the
AC #5 reflection guard, the golden-corpus freeze/round-trip/additive trio for both new events, and the
JSON contract-flow convergence for both. The genuine gaps were the **D2→AC #2 interaction** (Unit
immutability after establishing the first estimate *via* re-estimate), the **zero re-estimate boundary**
(lower bound of "non-negative" + D5), the **D3 whole-replacement clear** (distinct from a per-field
patch), and the **due-date-only partial schedule** — each closed below.

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemReEstimateTests.ReEstimate_after_establishing_first_estimate_rejects_a_different_unit_and_preserves_it`
  (D2 → AC #2) — establishes the Unit on a previously-unestimated item via `ReEstimate`, then a
  subsequent different-Unit re-estimate is rejected with `WorkItemReEstimateRejected`, the established
  Unit + Estimated are preserved, and `Sequence` does not advance. The story mandates that the AC #2
  immutability rejection "still applies once a Unit is established"; the baseline only covered the
  created-with-effort path, not the D2 establish-then-reject path.
- [x] `WorkItemReEstimateTests.ReEstimate_to_zero_clamps_done_and_remaining_without_completing`
  (AC #1 boundary + D5) — zero is the lower boundary of the AC #1 "non-negative value": it is accepted,
  Done clamps to 0, Remaining derives to 0, and the act emits **only** `ReEstimated` — never
  `WorkItemCompleted`. The baseline covered a below-Done clamp (4 of done 6) but not the exact-zero
  boundary.
- [x] `WorkItemRescheduleTests.Reschedule_with_empty_schedule_clears_a_previously_set_schedule_whole`
  (D3) — replacing a fully-populated schedule with an empty one clears **both** `Priority` and `DueDate`,
  proving whole-replacement rather than the rejected per-field-patch alternative (where null would mean
  "leave unchanged"). The baseline's both-null test started from an item that never had a schedule, so it
  did not exercise the clear-an-existing-schedule semantic.
- [x] `WorkItemRescheduleTests.Reschedule_with_only_a_due_date_is_accepted_and_replayed`
  (AC #3/#4 partial schedule) — a due date with no priority is accepted and replayed without coercing the
  missing priority into a default band. The inverse partial (priority, no due date) was already exercised
  by the live-status theory; the due-date-only partial was untested.

### Pre-existing coverage (dev-authored, verified green — not regenerated)

- [x] `WorkItemReEstimateRescheduleContractFlowTests` — command → `WorkItemAggregate.Handle` → concrete
  `JsonSerializerDefaults.Web` JSON → replay for both `ReEstimated` and `WorkItemRescheduled`, asserting
  convergence of effort/schedule and absence of `$type` plus EventStore envelope fields.
- [x] `WorkItemV1Catalog` — catalog count **26 → 31** (13 success events, 13 commands, 5 rejection
  events); new payloads resolve through the empty `Polymorphic` base.
- [x] `SchemaEvolutionGoldenCorpusTests` — frozen deserialize + round-trip + additive unknown-field
  tolerance for `ReEstimated.v1.json` and `WorkItemRescheduled.v1.json`.
- [x] `docs/lifecycle-transition-matrix.md` + `LifecycleTransitionMatrixDocTests` — both new commands
  documented as non-lifecycle planning acts and gated in `RequiredCommands`.

## Story 2.4 Validation

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -v minimal` — passed with
  **0 warnings and 0 errors** (warnings-as-errors).
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **217/217** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **45/45** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 2.4 Test Counts

| Suite | Story 2.3 Final | Dev 2.4 Baseline | QA Final | QA Delta |
|-------|----------------:|-----------------:|---------:|---------:|
| UnitTests | 188 | 213 | **217** | +4 |
| IntegrationTests | 39 | 45 | **45** | — |
| ArchitectureTests | 26 | 26 | 26 | — |
| PropertyTests | 1 | 1 | 1 | — |
| **Total** | **254** | **285** | **289** | **+4** |

### Checklist

- [x] API/contract tests generated (re-estimate/reschedule command → aggregate → JSON → replay).
- [x] E2E/UI tests marked not applicable (Story 2.4 is pure Contracts + Server + Tier-1; no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (re-estimate up/establish; schedule replace + partial schedule replay).
- [x] Tests cover critical error/edge cases (D2→AC #2 Unit immutability; zero boundary; whole-replacement clear).
- [x] All generated tests run successfully (289/289).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; no clock/RNG/IO).
- [x] Tests are independent (each arranges its own state via replay; no order dependency).
- [x] Test summary updated with coverage metrics.

## Notes

- This run is **QA gap-filling only** — no production code was changed; only `WorkItemReEstimateTests.cs`
  and `WorkItemRescheduleTests.cs` were extended (+2 tests each).
- Four genuine, AC/design-decision-anchored gaps were closed: (1) Unit immutability (AC #2) reached via
  the D2 establish-then-reject path, not just created-with-effort; (2) the zero lower boundary of the
  AC #1 "non-negative" estimate, reinforcing D5 (no completion on a planning act); (3) D3
  whole-schedule-replacement proven by clearing an existing schedule (distinguishes it from the rejected
  per-field patch); (4) the due-date-only partial schedule (the priority-only partial was already
  covered).
- The new tests need no golden fixture or catalog change (`Count` stays **31**) — they exercise existing
  durable event types through the aggregate and replay, not new wire shapes.

---

# Test Automation Summary — Story 2.3 (Report Progress with Unit-Tagged Burn-Down)

Workflow: `bmad-qa-generate-e2e-tests`. Role: QA automation engineer (test generation only — no code
review or story validation). Framework detected and reused: **xUnit v3 + Shouldly**, Tier-1 (no
Dapr/Aspire/containers/network). Story 2.3 is a pure `Contracts` + `Server` domain slice with **no UI,
MCP, public route, or host surface**, so browser/UI E2E is **not applicable**; the executable
end-to-end path is **command → `WorkItemAggregate.Handle` → raw-act event → concrete JSON transport
shape → replayed `WorkItemState`**, exercised by the unit + contract-flow tests below.

The dev-authored Story 2.3 baseline was **247** green tests (UnitTests 183, IntegrationTests 37,
ArchitectureTests 26, PropertyTests 1). This QA run discovered AC-aligned coverage gaps and
auto-applied **+7** tests, raising the total to **254** green. **No production code was changed** —
only test files were added/extended.

## Gaps auto-applied this run

Mapped against AC #1–#5, the dev baseline already covered single-report progress, over-progress
clamping, completion event order, the rejection paths (non-positive delta, Unit mismatch, unestimated,
out-of-`InProgress`), explicit completion for unestimated work, schema-evolution freeze/round-trip, and
the auto-complete JSON flow. The genuine gaps were the **multi-report burn-down ("burns down as a
fact")** behavior, the **exact-zero completion boundary**, the **`WorkItemEffort.Report` value-object
contract**, and the **non-completing JSON round-trip** — each closed below:

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemProgressTests.ReportProgress_applied_repeatedly_accumulates_done_and_burns_down_remaining`
  (AC #2) — two sequential reports (3 then 2 of 8) each append a `ProgressReported` at the next
  sequence, accumulate replayed `Done` (3 → 5) and burn down `Remaining` (5 → 3), and do **not**
  auto-complete while `Remaining` stays positive (asserts a single event per report, omitted `Note`
  replays as `null`). Previously only a single report was tested.
- [x] `WorkItemProgressTests.ReportProgress_with_delta_exactly_equal_to_remaining_completes_in_order`
  (AC #3 boundary) — a delta that lands `Remaining` **exactly** on zero (not over) completes
  synchronously, emitting `ProgressReported` then `WorkItemCompleted` at `+1`/`+2`. The pre-existing
  unit test only covered the over-shoot (clamp) path; exact-equal was implicit (integration only).
- [x] `WorkItemEffortTests.WorkItemEffort_report_accumulates_done_and_re_derives_remaining`
  (AC #1/#2) — chained `Report(3).Report(2)` advances `Done` and re-derives `Remaining` without
  storing it.
- [x] `WorkItemEffortTests.WorkItemEffort_report_rejects_non_positive_delta` (AC #2/#5, theory `-1`/`0`)
  — the value object's own positive-delta guard throws `ArgumentOutOfRangeException`. The guard existed
  but was only enforced indirectly via the aggregate; it is now directly gated.

### Integration / contract-flow tests (`tests/Hexalith.Works.IntegrationTests`)

- [x] `WorkItemProgressContractFlowTests.Partial_progress_round_trips_through_json_and_burns_down_without_completing`
  (AC #1/#2) — a single sub-completion report (3 of 8) survives concrete `JsonSerializerDefaults.Web`
  serialization and replays to `Done=3` / `Remaining=5` / `InProgress` / `Sequence=4`, with `Note`
  preserved and **no** `WorkItemCompleted`. The only prior integration test exercised the auto-complete
  path (8 of 8); the non-completing burn-down path through JSON was untested.
- [x] `WorkItemProgressContractFlowTests.Repeated_progress_round_trips_through_json_and_accumulates_then_completes`
  (AC #2/#3) — two reports delivered through concrete JSON accumulate `Done` across reports (3 then 5),
  and the report that lands `Remaining` on zero round-trips with its paired `WorkItemCompleted` to a
  `Completed` / `Sequence=6` replay that matches the write side.

## Story 2.3 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors** (warnings-as-errors).
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **188/188** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **39/39** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 2.3 Test Counts

| Suite | Story 2.2 Baseline | Dev 2.3 Baseline | QA Final | QA Delta |
|-------|-------------------:|-----------------:|---------:|---------:|
| UnitTests | 166 | 183 | **188** | +5 |
| IntegrationTests | 34 | 37 | **39** | +2 |
| ArchitectureTests | 26 | 26 | 26 | — |
| PropertyTests | 1 | 1 | 1 | — |
| **Total** | **227** | **247** | **254** | **+7** |

### Story 2.3 Coverage Notes

- Unit tests cover positive progress, **cumulative multi-report burn-down**, over-progress clamping,
  **exact-zero completion boundary**, progress-driven completion event order, negative/zero delta
  rejection, Unit mismatch rejection, unestimated progress rejection, status-based rejection outside
  `InProgress`, explicit completion for unestimated `InProgress`/`Suspended` work, and the
  `WorkItemEffort.Report` accumulation/guard contract.
- Integration tests cover command → aggregate → concrete JSON → replay for **partial (non-completing)**,
  **repeated/accumulating**, and **auto-complete** progress, keeping EventStore envelope fields out of
  concrete payloads.
- Schema tests cover frozen `ProgressReported.v1.json`, round-trip deserialization, and additive unknown
  field tolerance.
- Architecture tests verify the lifecycle matrix mentions `ReportProgress`, kernel purity still passes,
  and roll-up/reminder terms remain banned from `src`.

### Checklist

- [x] API/contract tests generated (partial-burn-down + repeated-accumulate JSON round-trips).
- [x] E2E/UI tests marked not applicable (Story 2.3 is pure Contracts + Server + Tier-1; no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (single + cumulative burn-down; partial + completing JSON replay).
- [x] Tests cover critical error/edge cases (exact-zero boundary; non-positive `Report` delta guard).
- [x] All generated tests run successfully (254/254).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; no clock/RNG/IO).
- [x] Tests are independent (each arranges its own state via replay; no order dependency).
- [x] Test summary updated with coverage metrics.

---

# Historical Test Automation Summary — Story 2.2 (Record Raw-Act Events and Replay State)

Workflow: `bmad-qa-generate-e2e-tests`. Role: QA automation engineer (test generation only — no code
review or story validation). Baseline before this run (dev-authored, green): **221** tests
(UnitTests 166, IntegrationTests 28, ArchitectureTests 26, PropertyTests 1).
Framework detected and reused: **xUnit v3 + Shouldly**, Tier-1 (no Dapr/Aspire/containers/network).
All tests auto-applied this run; all green.

## Generated Tests (gaps auto-applied this run)

### API / Contract & Serialization Tests

- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/*.v1.json` — **+7 frozen golden
  fixtures**, completing the RR-6 / NFR-12 back-compatibility corpus. The dev started the corpus with
  3 of the 10 durable success events (`WorkItemCreated`, `WorkItemAssigned`, `WorkItemCompleted`), yet
  the corpus README states *"Every event ever produced must remain deserializable forever."* The seven
  unfrozen events — including the two that carry distinguishing payload (`WorkItemClaimed` → executor
  binding, `WorkItemRejected` → the `Requeue` resting-status flag) — were not gated. Added frozen v1
  fixtures for `WorkItemQueued`, `WorkItemClaimed`, `WorkItemSuspended`, `WorkItemResumed`,
  `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`. **Generated from the production serializer**
  (a temporary emitter, run once then deleted — not hand-authored), so camelCase, enum-name casing, and
  property order are byte-accurate to the EventStore-persisted concrete form (no `$type`).
- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` —
  **+5 tests** wiring the new fixtures into the gate (now 10/10 durable success events):
  - `WorkItemClaimed_DeserializesFromFrozenBytesAndRoundTrips` — deserialize-from-frozen asserts every
    field incl. the binding (`partyId` / `channel=Mcp` / `authorityLevel=Coordinate`); re-serialize →
    deserialize round-trips to an equal record.
  - `WorkItemRejected_DeserializesFromFrozenBytesAndRoundTrips` — asserts the frozen `requeue: false`
    discriminator survives exactly (it steers replay to `Rejected` vs `Queued`).
  - `Base_shape_lifecycle_events_deserialize_from_frozen_bytes_and_round_trip` — the five
    `(AggregateId, Sequence, TenantId, WorkItemId)` events (`Queued`/`Suspended`/`Resumed`/`Cancelled`/
    `Expired`), each frozen independently so a future per-event field addition is gated by its own entry.
  - `WorkItemClaimed_ToleratesAdditiveUnknownField` / `WorkItemRejected_ToleratesAdditiveUnknownField` —
    inject an unknown `futureField` into the frozen bytes; the enriched events still deserialize (additive,
    no-`V2` tolerance). Vacuous-pass guard: `File.Exists(path)` inside `ReadGolden` reports a missing
    fixture as the root cause before any value assertion.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` — **+1 test** closing
  the AC #2 *"order-tolerant projections"* gap (previously only implicit — every existing replay test
  applied events in arrival order):
  - `Out_of_order_event_stream_replays_to_completed_when_resorted_by_sequence` — persists the six-event
    lifecycle stream through JSON, delivers it **out of order** (deterministic reverse — no RNG), then a
    projection recovers the canonical order purely from `Sequence` and replays into an independent
    `WorkItemState`, converging to `Completed` / `Sequence = 6` identical to the write side. Guards: the
    delivered sequences are asserted to be the contiguous, gap-free `1..6` and the stream count `== 6`
    before the order-tolerance claim is made.

### E2E Tests

- [x] Browser/UI E2E is **not applicable** to Story 2.2: the slice is pure `Contracts` + Tier-1 tests —
  serialization registration and the raw-act event catalog, with no UI, MCP, public route, or host
  surface (host/Dapr/Aspire wiring is deferred to Stories 4.5/4.6). The executable end-to-end path is
  **command → `WorkItemAggregate.Handle` → raw-act event → JSON transport shape → replayed
  `WorkItemState`**, exercised end-to-end by the contract-flow + golden-corpus tests above.

### Pre-existing coverage (dev-authored, verified green — not regenerated)

- [x] `WorkItemSerializationRegistrationTests` — AC #5: every one of the 23 v1 types resolves through the
  empty `Polymorphic` base, emits `$type` == type name (no version suffix), and round-trips to the
  concrete type. Two vacuous-pass guards (catalog count == 23; resolver reports ≥23 derived types).
- [x] `WorkItemRawActAdditivityTests` — AC #1/#2/#3 regression guard: concrete-type serialization emits
  **no** `$type` and **no** EventStore envelope fields, and a concrete `WorkItemCreated` still replays to
  `Created` (proves the polymorphic registration is purely additive).
- [x] `WorkItemCreateContractFlowTests` / `WorkItemLifecycleContractFlowTests` — create + full-lifecycle
  serialized write → persist → replay, reference-only payloads, the requeue flag steering replay, and
  rejection-only results (`WorkItemTransitionRejected`, cross-tenant-parent, missing-obligation) that
  carry context but no `sequence`.

## Coverage

Mapped against the Story 2.2 surface (10 success events + 10 commands + 3 rejection events; the
PolymorphicSerializations registration; the golden corpus):

| AC | What it requires | Status |
|----|------------------|--------|
| #1 | Accepted act → past-tense v1 event carrying verbatim replay values | Pre-existing (create/lifecycle flow + additivity); **reinforced** by 7 new frozen fixtures |
| #2 | Event carries `(AggregateId, Sequence)` for **order-tolerant** projections; no envelope spoofing | **Gap closed** — added out-of-order-resort-by-`Sequence` replay test; envelope-absence already guarded |
| #3 | In-order replay through `Apply` reconstructs state deterministically; no interpreted/AI/sibling data | Pre-existing (full-lifecycle serialized replay; reference-only payloads) |
| #4 | Rejection → `IRejectionEvent`; result never mixes success + rejection payloads | Pre-existing (per-path `IsRejection`/`IsSuccess` exclusivity; mixed-payload throw guarded in EventStore lib) |
| #5 | Catalog registered & resolvable by PolymorphicSerializations; golden corpus started, additive/no-`V2` | Pre-existing registration + **gap closed**: corpus completed to **10/10** durable success events |

**Durable-event corpus coverage: 10 / 10** success events frozen (was 3 / 10).
**Not corpus candidates (by design):** the 10 commands and 3 rejection events are not appended to the
event stream (commands are transient inputs; rejections are returned to the caller with no `Sequence`),
so they are not part of the persisted-bytes back-compat gate — they remain covered by the resolution and
contract-flow tests.

### Test counts (built Release, warnings-as-errors → 0 warnings / 0 errors)

| Suite | Before | After | Δ |
|-------|-------:|------:|--:|
| UnitTests | 166 | 166 | — |
| IntegrationTests | 28 | **34** | +6 |
| ArchitectureTests | 26 | 26 | — |
| PropertyTests | 1 | 1 | — |
| **Total** | **221** | **227** | **+6** |

## Validation

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  **0 warnings, 0 errors** (warnings-as-errors).
- Generated xUnit v3 executables run directly (Microsoft.Testing.Platform named-pipe is blocked in this
  sandbox, per the established pattern):
  - **UnitTests: 166/166** (unchanged)
  - **IntegrationTests: 34/34** (was 28/28 → **+6**)
  - **ArchitectureTests: 26/26** (unchanged — the `DependencyDirectionTests` update was dev-authored)
  - **PropertyTests: 1/1** (unchanged)
  - Total: **227/227**, 0 failures.

```bash
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build  Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests
```

## Checklist

- [x] API/contract tests generated (golden-corpus completion + order-tolerance replay).
- [x] E2E/UI tests marked not applicable (Story 2.2 is pure Contracts + Tier-1; no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (full out-of-order lifecycle replay to `Completed`; all 10 durable events deserialize from frozen bytes).
- [x] Tests cover critical error/edge cases (additive unknown-field tolerance on enriched events; out-of-order delivery recovered by `Sequence`).
- [x] All generated tests run successfully (227/227).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; the corpus reads copied-to-output fixtures only).
- [x] Tests are independent (no order dependency; each builds its own state; registration is an idempotent static ctor).
- [x] Test summary created with coverage metrics.

## Notes

- This run is **QA gap-filling only** — no production code was changed; only test files and frozen
  fixtures were added/modified.
- Two genuine gaps were discovered and auto-applied: (1) the back-compat golden corpus gated only 3 of
  the 10 durable success events despite its own "every event ever produced must remain deserializable"
  rule — completed to 10/10; (2) AC #2's *order-tolerant projections* claim was only implicit (all replay
  tests applied events in arrival order) — added a shuffle-then-resort-by-`Sequence` replay proof.
- Golden fixtures were generated from the production serializer (temporary emitter, deleted after the run)
  rather than hand-authored, matching the dev's established methodology so casing/ordering are exact. The
  existing `SchemaEvolution\Golden\**\*.json` `<None>` glob copies the new files to output — no csproj
  change was needed.
- The 10 commands and 3 rejection events are intentionally **not** in the persisted-bytes corpus
  (transient inputs / caller-returned, never stream-appended); they stay covered by the polymorphic
  resolution test and the contract-flow rejection tests.

---

# Test Automation Summary — Story 4.5 (Prove the Command/Event Pipeline Under Aspire)

Workflow: `bmad-dev-story`. Story 4.5 is the first **runtime adapter-edge** proof: it adds a runnable Works
domain-service host (`src/Hexalith.Works`), the Works AppHost topology, local Dapr components, and runtime
projection/query adapters that consume the deferred Story 4.4 seams — without shipping any production adapter.
The pure kernel (`Contracts`, `Server`, `Projections`, `Reactor`) is unchanged.

**Baseline (Story 4.4 final, commit `60b3230`): 599 green** (UnitTests 483, IntegrationTests 80,
ArchitectureTests 33, PropertyTests 3), catalog **36**.

**Story 4.5 final: 608 green + 1 skipped** — UnitTests **483** (unchanged), IntegrationTests **85**
(80 + 3 adapter convergence + 1 topology model-inspection + 1 Aspire smoke that **skips** without
Docker/Dapr), ArchitectureTests **38** (33 + 5 runtime-adapter governance guards), PropertyTests **3**
(unchanged). Catalog stays **36**; golden corpus byte-unchanged; no production code in `Contracts`/`Server`/
`Projections`/`Reactor` changed.

## Production code changed

- **New runnable host** `src/Hexalith.Works/` (Web SDK, non-packable): `Program.cs` (canonical
  `AddEventStoreDomainService` + bespoke async `/project` + `UseEventStoreDomainService`),
  `WorkItemEventStoreAggregate` (the `EventStoreAggregate<WorkItemState>` adapter, `[EventStoreDomain("work")]`,
  14 `Handle` wrappers delegating to the pure kernel), `Projections/WorksWhatsNextReadModel.cs` (keys + tenant
  index read model), `Projections/WorkItemProjectionDispatcher.cs` (event-decode → pure projections → persisted
  tenant index + roll-up + notifier), `Queries/WhatsNextQueryHandler.cs` (`IDomainQueryHandler`, pure ordering +
  authorization).
- **AppHost topology** `src/Hexalith.Works.AppHost/`: `Program.cs` (`AddHexalithEventStore` +
  `AddEventStoreDomainModule`, `work` domain-service registration via the K8s-safe `wildcard_work_v1` key),
  cross-repo `IProjectMetadata` classes, and `DaprComponents/*.yaml` (statestore, accesscontrol[.works]
  [.eventstore-admin], resiliency).

## Tests added

### IntegrationTests (`tests/Hexalith.Works.IntegrationTests`) — +5 (4 run, 1 skipped)

- [x] `WorkItemProjectionQueryAdapterTests` — **+3 deterministic Tier-1 cases** (in-memory `IReadModelStore`,
  no Docker): an Assigned/Queued item is projected into the tenant `works-whats-next` index and returned by the
  query; a Completed item falls out of the eligible set; the query fails closed to empty when no index exists.
  Proves AC #2 projection convergence at the adapter edge using the same concrete (no-`$type`) event form
  EventStore persists.
- [x] `WorksAppHostTopologyTests` — **+1 model-inspection case** (runs without Docker): asserts the AppHost
  resource graph composes `eventstore`, `eventstore-admin`, and a Dapr-sidecar `works` domain service with the
  shared Dapr components, and composes **no** UI/MCP/chatbot/email/routing/cost/security surface (AC #1/#3).
- [x] `WorksCommandPipelineSmokeTests` — **+1 Tier-3 case, SKIPPED in this run.** Starts the full topology and
  submits an authenticated `CreateWorkItem` through `/api/v1/commands`, polling `/api/v1/commands/status/{id}`
  to a terminal status (AC #2 persist-then-publish). Prerequisite-gated on Docker + Redis + Dapr placement(50005)
  + scheduler(50006); placement/scheduler are not running in the sandbox so it skips with a clear reason.

### ArchitectureTests (`tests/Hexalith.Works.ArchitectureTests`) — +5 governance guards

- [x] `RuntimeAdapterGovernanceTests` — the runnable host is the only Works project allowed EventStore-runtime /
  Dapr references; the AppHost uses the platform `AddHexalithEventStore`/`AddEventStoreDomainModule` (no
  hand-rolled Dapr wiring); `WorkItemAggregate` stays pure static while the `EventStoreAggregate<…>` adapter lives
  in the host; no production-surface vocabulary (Mcp/Chatbot/EmailSurface/MailSurface/DataGrid/WebShell/
  RoutingEngine/EligibilityScore/EscalationLadder/CostMeter/SpendGovernance) and no `IExecutorRouter`
  implementation in Works source; adapter logs carry only bounded metadata (no payload/obligation/secret/token
  placeholders, no interpolated log calls).
- The `ScaffoldGovernanceTests` roll-up-location guard was extended to allow the runtime adapter host (it
  legitimately consumes `WorkItemRollUpProjection`/`WorkItemRollUpEvent`); `BuildConfigurationTests` Aspire SDK
  pin was reconciled to 13.4.5.

## Skipped infrastructure conditions

- The Tier-3 Aspire command-pipeline lane (`WorksCommandPipelineSmokeTests`) **skipped** — Dapr placement(50005)
  and scheduler(50006) are not running in the sandbox. It runs in a Docker + `dapr init` environment with those
  services started. The model-inspection topology test and the deterministic adapter-convergence tests run and
  pass without containers, so a miswired topology or adapter still fails the build.

## Reconciliation / drift

- Aspire pins were bumped 13.4.3 → **13.4.5** (and `Aspire.AppHost.Sdk` to 13.4.5) to match the checked-out
  `Hexalith.EventStore` submodule, which `Hexalith.EventStore.Aspire` requires — a ProjectReference-rule
  alignment, not a discretionary upgrade. `Microsoft.IdentityModel.JsonWebTokens` 8.19.0 was added centrally for
  the smoke test's dev JWT.

## Verification commands

```
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal   # 0 warn / 0 err
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests                     # 483
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests       # 85 (1 skipped)
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests     # 38
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests             # 3
```

---

# Test Automation Summary — Story 4.6 (Prove Reminder and Reactor Recovery)

Workflow: `bmad-dev-story`. Story 4.6 adds the adapter-edge runtime proof for date-based reminders and
terminal-cascade restart recovery. Runtime code is confined to `src/Hexalith.Works` and AppHost/config; the pure
kernel projects (`Contracts`, `Server`, `Projections`, `Reactor`) remain free of Dapr actors, clocks, logging,
network/filesystem I/O, EventStore gateway/runtime APIs, and checkpoint stores.

**Baseline (Story 4.5 final, commit `d5cf5c7`): 608 green + 1 skipped**, catalog **36**.

**Story 4.6 final: 620 green + 2 skipped** — UnitTests **483** (unchanged), IntegrationTests **95**
(93 green + 2 Tier-3 skips; +9 deterministic recovery cases, the existing topology case strengthened, and the
gated Aspire reminder-recovery lane added), ArchitectureTests **41** (38 + 3 recovery-governance guards),
PropertyTests **3** (unchanged). Catalog stays **36**; golden corpus byte-compatible; no durable
command/event/rejection type added.

> Senior Developer Review (AI) follow-up resolution: the gated Tier-3 Aspire reminder-recovery lane the review
> flagged as missing is now authored (`WorksReminderRecoveryPipelineSmokeTests`), and the two LOW review fixes
> (stream paging `+1`; date-reminder scheduler honors its `CancellationToken`) are applied. The IntegrationTests
> count therefore rises from 94 (93 + 1 skip) to 95 (93 + 2 skips) — the new lane skips when Docker/Dapr are absent.

## Production code changed

- **Date-reminder runtime** under `src/Hexalith.Works/Reminders`: deterministic reminder/actor naming,
  Dapr actor reminder registration/fire handling, date-resume command construction, pending-date-await
  projection, stream-reading pending-await source, and startup reconciliation service.
- **Cascade recovery runtime** under `src/Hexalith.Works/Recovery/Cascade`: bounded checkpoint records,
  deterministic target command construction, at-least-once dispatch, checkpoint replay, read-model checkpoint
  store, and stream-reading direct-descendant source.
- **Runtime composition** under `src/Hexalith.Works/Runtime` plus `Program.cs`: EventStore command-gateway
  submitter, event decoder, recovery options, bounded `LoggerMessage` definitions, actor registration, recovery
  service registration, and actor endpoint mapping.
- **AppHost/config**: Works receives the EventStore command-gateway base address; `statestore.yaml` documents why
  `works` remains scoped to the actor-capable state store for reminder state and cascade checkpoints.

## Tests added/updated

### IntegrationTests (`tests/Hexalith.Works.IntegrationTests`) — +9 deterministic adapter tests

- [x] `DateReminderRecoveryRuntimeTests` — deterministic reminder naming, date-resume command construction,
  pending-date-await projection/clearing, and reconciliation behavior for due vs. future awaits. Duplicate
  reconciliation overwrites the same deterministic reminder registration and reissues the same deterministic
  resume id.
- [x] `CascadeRecoveryRuntimeTests` — checkpoint creation, active-vs-terminal descendant filtering via the pure
  translator, target command determinism, attempted/completed checkpoint transitions, replay after an injected
  mid-cascade failure, and duplicate parent-terminal redelivery reusing the persisted checkpoint without
  rediscovery.

### IntegrationTests (`tests/Hexalith.Works.IntegrationTests`) — strengthened existing model lane

- [x] `WorksAppHostTopologyTests` now also asserts the shared Dapr state store is actor-capable, `works` remains
  scoped to it, and the AppHost injects the EventStore command-gateway base address used by reminder/cascade
  recovery. This remains a model/file-inspection lane and runs without Docker.
- [x] `WorksCommandPipelineSmokeTests` remains the Tier-3 Aspire command-pipeline lane and skipped in this run
  because Redis/Dapr placement/scheduler prerequisites were absent.

### IntegrationTests (`tests/Hexalith.Works.IntegrationTests`) — +1 gated Tier-3 reminder-recovery lane

- [x] `WorksReminderRecoveryPipelineSmokeTests` — the gated Tier-3 Aspire reminder-recovery lane (AC #1/#3): it
  starts the Works AppHost, parks a work item on a past `DateReached` await (Create → Assign → Claim → Suspend),
  **restarts the AppHost** against the same `dapr init` Redis, reissues the date resume through the production
  `DateResume.BuildSubmission` command factory on `POST /api/v1/commands`, and proves **exactly one** accepted
  `WorkItemResumed` from the re-readable per-aggregate stream (`POST /api/v1/streams/read`), idempotent under a
  second pass (the duplicate deterministic resume no-ops). It **skips cleanly** (mirroring the command-pipeline
  lane) when Redis :6379 / Dapr placement :50005 / scheduler :50006 are absent. **Substrate limitation, documented
  not faked:** the restarted host's `ReminderReconciliationService` runs, but its tenant-wide
  `StreamReadingPendingDateAwaitSource` scan is bounded by the EventStore stream-read gateway (per-aggregate route
  only; domain-wide reads are contract-defined but not yet enabled by `StreamsController`), so the lane reissues
  via the adapter's own deterministic command factory rather than tenant-wide auto-discovery. The reconciliation
  **decision logic** (discover due awaits → reissue idempotently) stays proven deterministically by
  `DateReminderRecoveryRuntimeTests`.

### ArchitectureTests (`tests/Hexalith.Works.ArchitectureTests`) — +3 and one guard updated

- [x] `ScaffoldGovernanceTests` replaced the old "reminders deferred" assertion with the Story 4.6 ownership
  assertion: reminder/recovery code is allowed only in the runnable Works host and AppHost/config/test/docs
  locations, not in pure projects.
- [x] `RuntimeAdapterGovernanceTests` now asserts Dapr actor packages are confined to the runnable host;
  reminder actor, command gateway, stream-read, and checkpoint tokens do not appear in pure projects; pure
  projects stay free of Dapr actors, clocks, logging, network/filesystem I/O, read-model stores, and EventStore
  gateway/runtime APIs; reminder/checkpoint records do not expand the durable polymorphic catalog; runtime logs
  retain bounded metadata-only templates.

## Skipped infrastructure conditions

- Two Tier-3 Aspire lanes are present and both **skipped** in this run because Redis :6379, Dapr placement
  :50005, and Dapr scheduler :50006 were not all reachable in the sandbox: Story 4.5's **command-pipeline** lane
  (`WorksCommandPipelineSmokeTests`, `CreateWorkItem → Completed`) and Story 4.6's **reminder-recovery** lane
  (`WorksReminderRecoveryPipelineSmokeTests`, park-on-`DateReached` → AppHost restart → exactly-one
  `WorkItemResumed`, idempotent). Start Docker, run `dapr init`, and start Dapr placement/scheduler services to
  run both live AppHost lanes.
- No claim is made that either live lane ran in this sandbox — both `Assert.Skip(...)` with a clear reason. The
  reminder reconciliation/reissue decision logic and cascade replay remain proven **deterministically** as well
  (adapter tests with fakes + injected `TimeProvider`, no sleeps, Dapr, or containers), so the behavior is
  covered whether or not the live lanes are exercised.

## Verification commands

```
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal   # 0 warn / 0 err
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests                     # 483
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests       # 95 (2 skipped)
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests     # 41
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests             # 3
```

## Checklist

- [x] Deterministic reminder names and duplicate registration behavior are tested.
- [x] Reminder fire/reconciliation command construction is tested; the aggregate receives a deterministic
  `DateReached` await condition and remains clock-free.
- [x] Reminder reconciliation is idempotent and tested with an injected `TimeProvider`; no sleeps or timing races.
- [x] Cascade checkpoint transitions and replay are tested with in-memory stores.
- [x] Duplicate/redelivered parent terminal events reuse persisted checkpoints.
- [x] Already-terminal descendant candidates are skipped before dispatch when the re-readable source marks them
  terminal; duplicate terminal commands remain aggregate-idempotent.
- [x] AppHost/config model lane asserts actor-capable state store scope and command-gateway configuration.
- [x] Governance tests enforce runtime placement, log privacy, and catalog size **36**.
- [x] Tier-3 prerequisites and skipped live lanes are documented honestly.
- [x] The gated Tier-3 Aspire reminder-recovery lane is authored (park-on-`DateReached` → AppHost restart →
  exactly-one `WorkItemResumed`, idempotent), skips cleanly without Docker/Dapr, and its substrate limitation is
  documented. Two LOW review fixes applied (stream paging `+1`; scheduler honors its `CancellationToken`).

---

# Test Automation Summary — Story 4.7 (Trigger Reactor Translators from the Live Event Stream)

Workflow: `bmad-dev-story`. Story 4.7 adds a durable, host-edge Works event subscription; mechanically feeds
parent-terminal events into cascade dispatch and child-completion events into the unchanged pure resume
translator; derives descendant terminality from persisted roll-ups; and discovers incomplete cascade checkpoints
through an ETag-safe durable index on startup.

**Reconciled baseline (correct-course commit `9526c31`):** UnitTests **496**, IntegrationTests **96/98**
(the command and reminder Tier-3 lanes were already red at their first gateway submission), ArchitectureTests
**44**, PropertyTests **3**, catalog **37**. This supersedes the older Story 4.6 ledger of 620 green + 2 skipped.

**Story 4.7 final validation: 657/657 green, zero skips** — UnitTests **496/496**, IntegrationTests
**114/114**, ArchitectureTests **44/44**, PropertyTests **3/3**. Catalog remains **37**. The **16** new
Integration tests comprise **15 deterministic cases** and **1 live reactor/restart lane**. All three Tier-3
classes pass against the current EventStore submodule, and the story advances to `review`.

## Production/runtime coverage

- The AppHost overrides the EventStore `work` publisher topic to the shared `work.events` topic. Works registers
  the EventStore subscription options, durable Dapr marker store, and typed handler contracts, then maps a
  host-local Web-JSON endpoint plus `MapSubscribeHandler`/`UseCloudEvents`.
- The generic SDK decode trap is characterized with real catalog bytes: its default serializer silently
  misbinds camel-case Works records. `WorksDomainEventProcessor` instead uses `WorksEventDecoder`, validates
  envelope/payload identity, preserves terminal-ack versus retry outcomes, and deduplicates completed deliveries.
- Cancellation/expiration handlers delegate mechanically to `CascadeDispatcher`. Descendant discovery reads
  each child's roll-up status, treating missing/unreadable entries as active; stale rolled-remaining is never
  trusted.
- Child completion re-reads the child's stream for its same-tenant parent and the parent's stream for current
  awaits, feeds `ChildCompletionResumeTranslator` unchanged, and submits deterministic `ResumeWorkItem` commands.
- The checkpoint store maintains an ETag-updated incomplete index, and a startup hosted service drives
  `ReplayAsync` from that index without tenant hand-configuration or descendant rediscovery.

## Tests added — IntegrationTests +16

- `WorksDomainEventProcessorTests` — **4**: default-DI activation; generic silent misbind characterization; all
  three consumed Web-JSON events dispatch; completed marker deduplicates; malformed known bytes are acknowledged
  and marked complete.
- `TerminalCascadeEventHandlerTests` — **2**: cancellation and expiration consumers delegate to the dispatcher.
- `StreamReadingCascadeDescendantSourceTests` — **2**: terminal roll-up skips, active and missing roll-ups target;
  transient parent-stream failures propagate so the durable delivery remains retryable.
- `ChildCompletionEventHandlerTests` — **1**: completed child drives the unchanged translator and deterministic
  resume submission.
- `StreamReadingChildCompletionAwaitingParentSourceTests` — **4**: current await is rebuilt; resume clears it;
  cross-tenant parent references fail closed; transient gateway failures propagate for subscription retry.
- `CascadeCheckpointIndexRecoveryTests` — **2**: incomplete/completed index lifecycle and interrupted-attempt
  startup convergence with an idempotent second pass and no descendant rediscovery.
- `WorksCascadeRecoveryPipelineSmokeTests` — **1 gated Tier-3 lane**: live completed-child resume through the
  unchanged translator, parent/children cancellation, paced mid-cascade stop, AppHost restart, index replay, and
  exactly-one terminal-event assertions.

Existing focused recovery/translator lanes remained green: `CascadeRecoveryRuntimeTests`,
`TerminalCascadeTranslatorTests` (**11**), `ChildCompletionResumeTranslatorTests` (**5**), and
`DateReminderRecoveryRuntimeTests` (**5**). The two Reactor translator source files remain byte-identical.

## Live broad-gate result

Redis :6379, Dapr placement :50005, and scheduler :50006 were reachable, so none of the three Tier-3 tests
skipped. Both suppressed EventStore hosts were explicitly built Release first (0 warnings, 0 errors). The live
classes share one non-parallel xUnit collection because local Dapr name resolution uses fixed `works` and
`eventstore` application ids; the harness also suppresses expected resource-log noise without suppressing xUnit
failures. The final direct Integration binary reported **114/114 passed, 0 skipped**:

- `WorksCommandPipelineSmokeTests`: unique work item reached `Completed` through the command gateway.
- `WorksReminderRecoveryPipelineSmokeTests`: AppHost restart plus duplicate reissue converged to exactly one
  `WorkItemResumed`.
- `WorksCascadeRecoveryPipelineSmokeTests`: `WorkItemCompleted` resumed the awaiting parent through the live
  topic, parent cancellation dispatched the first descendant, and startup checkpoint replay dispatched the
  outstanding descendant after restart; each descendant contains exactly one `WorkItemCancelled`.

The gateway repair supplies the Works Dapr app port, waits for EventStore actor placement and the Works app-health
probe, preserves the aggregate adapter's case-sensitive command payload casing, and routes internal recovery
traffic through the allow-listed Works Dapr identity. `WorksCommandPipelineSmokeTests` also uses a per-run-unique
aggregate id, closing the warm-Redis collision debt.

## Verification commands and results

```
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal   # 0 warn / 0 err
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests                     # 496/496
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests       # 114/114 (0 skipped)
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests     # 44/44
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests             # 3/3
```
