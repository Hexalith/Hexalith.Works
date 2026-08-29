---
title: 'Shared roll-up reconciliation'
type: 'bugfix'
created: '2026-08-29'
status: 'done'
baseline_revision: '535d1b464810b5fd5650a386d43a09c2596554b4'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/implementation-artifacts/spec-refuse-stale-persisted-rollups.md'
warnings:
  - 'The repository pins .NET SDK 10.0.301, which is not installed; verification used the installed 10.0.400 SDK by invoking MSBuild.dll directly.'
  - 'The Aspire *SmokeTests classes are excluded from the integration run: they hang in DCP startup in this sandbox. All 221 deterministic integration tests and the full 528-test unit suite passed.'
deferred:
  - summary: >-
      The pending-date-await index and tenant registry stay unversioned and are neither rebuilt nor pruned
      by the shared reconciliation, so a work item dropped from a tenant's authoritative membership can
      retain reminder-recovery entries that no query can resolve.
    evidence: |-
      WorkItemSharedRebuildManifestBuilder emits operations only for the v2 tenant index, per-item roll-ups,
      and candidate-known legacy roll-up keys; WorksReadModelKeys.PendingDateAwaitIndexKey and
      PendingDateAwaitRegistryKey carry no generation token and appear in no manifest operation. Recovery
      therefore still enumerates awaits for ids that GetWorkItemQueryHandler now refuses as non-members.
      The underlying orphan-after-erasure gap predates this change; membership-based unreachability makes it
      observable from the query surface.
    location: >-
      src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Dormant read models written before stale-total refusal keep serving obsolete roll-ups, and parents linked only by `WorkItemCreated.Parent` omit their children because normal `/project` delivery replays one aggregate at a time.

**Approach:** Opt Works into EventStore's versioned shared-projection rebuild protocol, fold the operator-supplied authoritative tenant histories through one relationship-aware projection, and atomically promote schema-versioned tenant-index and per-item documents after applying the existing boundary sanitizer.

## Boundaries & Constraints

**Always:** Reuse the existing EventStore `/project/rebuild/shared/v1` lifecycle and atomic batch API; discover relationships from both `ChildSpawned` and `WorkItemCreated.Parent`; keep tenant identities closed; make legacy/unlisted documents unreachable and delete every legacy key identified by the authoritative candidate; preserve own effort, status, structure, watermarks, and compatible unavailable rolled shapes.

**Block If:** The existing shared-rebuild seam cannot receive complete tenant histories, cannot atomically promote the required manifest within its validated bounds, or a cross-tenant/missing-child relationship cannot be represented conservatively without fabricating evidence.

**Never:** Edit the deferred-work ledger; mutate live read models during candidate accumulation; duplicate the pure relationship algorithm; weaken normal-dispatch ordering guards or the refusal policy; expose the internal rebuild route publicly; add infrastructure dependencies to Contracts, Server, or Projections.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Legacy backfill | Seeded unversioned stale index and roll-up plus complete authoritative histories | Stage leaves the live view intact; commit atomically exposes current-schema documents and retires identified legacy keys | Retry/abort never leaks a partial candidate |
| Parented create | Parent stream lacks `ChildSpawned`; child create names the parent and later progresses | Parent structure includes the child and rolled totals use the child's current evidence in both item and queue views | No single-stream substitute total |
| Incomplete relationship | Parent references a missing, erased, malformed, or foreign-tenant child | Own/status/structure survive; both rolled fields are unavailable | Fail closed without cross-tenant keys |
| Stale document | Existing item is absent from the rebuilt authoritative candidate | New index omits it and current readers cannot resolve its per-item document | Do not retain query-visible orphan state |

</intent-contract>

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IAsyncDomainSharedProjectionRebuildHandler.cs:14` and `DomainSharedProjectionRebuildDispatcher.cs:180` -- existing deterministic Begin/Accumulate/Finalize/Stage/Commit/Verify seam; consume read-only, including its ordered histories, catalog identity, candidate, and batch limits.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:89` -- normal one-stream writer, decoder, ordering guard, and `ToBoundarySafeRollUp`; extract/reuse identity decoding and sanitization without changing `/project` compatibility.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:57` -- pure authoritative graph fold already merges `WorkItemCreated.Parent` and `ChildSpawned`; reuse one instance across all histories.
- `src/Hexalith.Works/Projections/WorksReadModelKeys.cs:8` and `WorksWhatsNextTenantIndex.cs:8` -- legacy/current generation keys and authoritative tenant membership used for safe migration and pruning.
- `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs:47` and `GetWorkItemQueryHandler.cs:47` -- current unversioned readers; switch to current-schema lookup and fail closed for non-members.
- `src/Hexalith.Works/Runtime/WorksHost.cs:43` -- assembly discovery and Dapr read-model registration already expose the shared protocol; ensure the public Works handler is discovered without replacing the bespoke route.
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml:19` -- admit only the EventStore caller to the internal shared-rebuild route.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs:145` and EventStore Testing's `InMemoryReadModelStore` -- existing persisted-state/legacy seeds plus atomic stage/commit fake.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`, read-model generation types, and both query handlers -- centralize reusable decode/sanitize behavior, preserve legacy writes until v2 promotion, enforce authoritative membership, and preserve normal replay ordering/compatibility.
- [x] `src/Hexalith.Works/Projections/SharedRebuild/*.cs` -- add one public shared handler and one-type-per-file deterministic candidate/manifest helpers; accumulate complete histories, reconcile one pure roll-up/queue graph, sanitize every boundary model, and emit an ordinal single-store replacement/deletion plan.
- [x] `src/Hexalith.Works/Runtime/WorksHost.cs` and `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml` -- wire discovery/batch bounds and the least-privilege internal route.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs` -- drive the real shared lifecycle with EventStore's atomic fake and cover the matrix, retries, tenant collisions, legacy JSON, persisted end state, and query-visible results.
- [x] `docs/eventstore-api-surface-constraints.md`, `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md`, and `CHANGELOG.md` -- document the operator rebuild, schema migration, atomic visibility, bounded failure, and parented-create convergence.

**Acceptance Criteria:**
- Given a sealed authoritative tenant inventory, when the EventStore shared lifecycle commits, then the tenant index and all rebuilt per-item views switch as one verified batch and no staged or retired document remains query-visible.
- Given both relationship encodings and current child histories, when rebuild output is queried, then parent item/index models agree on converged totals, child membership, schema version, and tenant isolation.
- Given incomplete or legacy evidence, when normal and rebuild paths persist/read models, then boundary sanitization is identical, stale totals are never returned, and reliable local fields remain intact.

## Spec Change Log

- 2026-08-29: Added fail-closed v2 generation validation, bounded commit-window re-reads, legacy-survivor compatibility, malformed-relationship handling, cancellation checks, and production wiring verification following adversarial review.

## Review Triage Log

### 2026-08-29 — Review pass 1

- intent_gap: 0
- bad_spec: 0
- patch: 16 (high: 4, medium: 10, low: 2)
- defer: 0
- reject: 11 (high: 1, medium: 8, low: 2)
- Addressed: preserve legacy dispatch until a valid v2 manifest exists; close query and cascade promotion races; reject missing, unsupported, or null-shaped current manifests without downgrade; sanitize malformed/foreign/cyclic/multiple-parent relationship evidence; propagate cancellation through manifest construction; validate production handler discovery, route uniqueness, batch bounds, ACL scope, membership filtering, and embedded identity; document the required projection-delivery fence; treat constructor-level malformed events as incomplete evidence.
- Rejected: proposals outside the bundle's platform contract or contradicted by its sealed authoritative-inventory/quiescence precondition, including inventing a second operator endpoint, enumerating physically undiscoverable keys, changing EventStore's shared lifecycle, and adding notifications to an offline reconciliation path.

### 2026-08-29 — Review pass 2

- intent_gap: 0
- bad_spec: 0
- patch: 9: (high 1, medium 6, low 2)
- defer: 1: (high 0, medium 1, low 0)
- reject: 20: (high 0, medium 12, low 8)
- addressed_findings:
  - `[high]` `[patch]` An ordinary `/project` dispatch of a reconciled parent republished a single-aggregate substitute rolled total and dropped the reconciled child structure, because the parent's own stream carries no evidence of a child that named it in `WorkItemCreated` and the equal-watermark write lets the fresh model win. The dispatcher now reads the aggregate's persisted document for the active generation, retains reconciled child identities the current replay cannot derive, and refuses both rolled shapes for that dispatch.
  - `[medium]` `[patch]` A known Works event that failed to decode was skipped silently on the normal path (the extracted decoder also catches value-object `ArgumentException`), while the rebuild path treats the same evidence as incomplete. Malformed non-state-affecting evidence now refuses the rolled shapes on both paths.
  - `[medium]` `[patch]` `FinalizeAsync` over an inventory that accumulated no history would have deleted the legacy tenant index and published an authoritative manifest with no members, making the whole tenant unreachable. An empty candidate is now refused.
  - `[medium]` `[patch]` `AccumulateAsync` had no guard for a null event list (`NullReferenceException`), a blank aggregate id (deferred failure inside `Build`), or a redelivered aggregate id (a second history for the same stream). It now guards the identity, tolerates a null event list, and replaces a redelivered aggregate's history.
  - `[medium]` `[patch]` A malformed or non-object candidate payload surfaced as a raw `JsonException`/null-collection dereference out of the rebuild lifecycle; candidate deserialization now fails closed with a bounded `InvalidOperationException`.
  - `[medium]` `[patch]` Both roll-up identity checks and the current-generation queue filter dereferenced embedded identities that a corrupt persisted document can deserialize as null, turning a fail-closed read into an unhandled fault. The checks are now null-safe.
  - `[medium]` `[patch]` A nameless `ProjectionEventDto.EventTypeName` faulted the catalog lookup with `ArgumentNullException` outside the decoder's catch filter; it is now refused as an unknown type.
  - `[medium]` `[patch]` No test exercised an ordinary dispatch against a committed v2 manifest — the post-migration steady state — nor the new legacy-generation identity refusal. Added reconciliation-durability, new-aggregate admission, empty-inventory, redelivery, and legacy-identity regressions.
  - `[low]` `[patch]` The CHANGELOG omitted the operator quiescence precondition and the legacy identity-gating behavior change; both are now recorded, together with the reconciled-structure retention rule in `docs/work-roll-up-projection.md`.
  - `[low]` `[patch]` The tenant-composition, additive-rollout, and reminder-registry rationale comments lost when `WorksWhatsNextReadModel.cs` was split are restored on `WorksReadModelKeys` and `WorksWhatsNextTenantIndex`.
- Deferred: the unversioned pending-date-await index/registry are not reconciled or pruned against v2 membership (recorded in frontmatter `deferred`).
- Rejected: proposals contradicted by the intent's own authority or by the established platform contract — deleting undiscoverable current-generation orphans (the intent authorizes unreachability for unlisted documents and deletion only for candidate-known legacy keys), re-litigating the operator quiescence fence as a blocker (settled and documented in pass 1), the rebuild/normal asymmetry on unknown event types (the rebuild's refusal is the fail-closed direction), retaining a foreign-tenant parent reference (unchanged pre-existing pure-projection behavior with a diagnostic), candidate re-serialization cost (mandated by the platform's accumulate contract), reader-ladder duplication and future v3 key parameterization (speculative), and cosmetic naming, doc-duplication, and fixture-realism observations.

### 2026-08-29 — Review pass 3

- intent_gap: 0
- bad_spec: 0
- patch: 5: (high 2, medium 2, low 1)
- defer: 0
- reject: 24: (high 0, medium 14, low 10)
- addressed_findings:
  - `[high]` `[patch]` The `docs/eventstore-api-surface-constraints.md` rewrite dropped the sentence the
    `P1_EventStoreImplementationConstraintsAreRecorded` fitness test pins ("not a shadow-projection plus
    atomic-swap model"), leaving the whole `Hexalith.Works.ArchitectureTests` suite red at HEAD. Neither
    earlier pass ran that project. The pinned phrasing is restored on the checkpoint-path sentence, where it
    is still accurate; the new shared-lifecycle paragraph is unchanged.
  - `[high]` `[patch]` The reconciled-child retention compared counts (`persisted.ExposedChildCount >
    projected.ExposedChildCount`) and then replaced the child list wholesale. A parent reconciled from a
    `WorkItemCreated.Parent` child that later spawns its own child projects an equal or larger count, so the
    reconciled identity was silently dropped from the persisted document and from the query surface until the
    next operator rebuild — contradicting the retention rule's own "can only become more complete, never
    wrong". Retention is now a union of persisted and replayed child ids, ordered by the published ordinal
    key, and refuses the rolled shapes only when the union actually added evidence. Proven by a new
    regression that fails against the pre-fix dispatcher.
  - `[medium]` `[patch]` No test pinned the ordinary-dispatch `malformedEvidence` refusal for a known,
    non-state-affecting, non-`ChildSpawned` event: the only malformed-dispatch fixtures were a `ChildSpawned`
    (already refused by event-type name) and a state-affecting event (which throws earlier), so removing the
    flag from the sanitizer call left every test green. Added a childless-leaf regression over an undecodable
    `ProgressReported`.
  - `[medium]` `[patch]` The legacy counterpart of the fail-closed manifest guard — a legacy index whose
    `Items`/`LastSequences`/`MemberWorkItemIds` deserialize as null — had no coverage; only the
    current-generation branch was pinned. Added a regression asserting the dispatch throws and neither
    rewrites the legacy index nor creates a current one.
  - `[low]` `[patch]` The `WorksHost` batch-limit comment claimed the raised bounds match the platform's
    10,000-aggregate admission bound. The 4 MiB canonical-manifest ceiling is the tighter of the two and is
    what actually bounds a rebuild, since the manifest embeds every written document. The comment now says so
    and names the fail-closed refusal as the intended outcome.
- Rejected: findings contradicted by the intent's own authority, by the platform contract, or by the code as
  it stands — pruning undiscoverable current-generation orphans and retiring legacy keys for aggregates
  outside the sealed candidate (the intent scopes deletion to candidate-known keys and authorizes
  unreachability for the rest); `lastSequences = 0` for a member with no decodable evidence (nothing was ever
  accepted for that member, so zero is the correct absence of a watermark); aborting the whole rebuild on a
  cross-identity payload (fail-closed is the right direction for an authoritative reconciliation); the
  `PrepareRelationshipPayload` malformed-parent branch being untested (`TenantId`/`WorkItemId`/
  `ParentWorkItemReference` all reject null and blank in their constructors, so that state cannot survive
  deserialization — the branch is an unreachable defensive guard); recursion depth and large-inventory
  behaviour at the 10,000-aggregate bound (already a recorded residual risk, pathological input);
  accumulate-time re-serialization cost (mandated by the platform accumulate contract, rejected in pass 2);
  raising or re-deriving the canonical-byte ceiling (the bounded failure is the design); absent logging on
  the fail-closed query and rebuild paths, a post-commit completion handler, change notifications, and an
  operator runbook (all outside the intent's surface); the foreign-parent leaf re-exposing its own rolled
  total on a later ordinary dispatch (a leaf's own total is provable from its own stream — no stale number);
  the claim that retry-to-success is untested (`Abort_retry_and_colliding_tenant_ids...` aborts and then
  rebuilds to a commit on the same store); the `UseCurrentSchemaAsync` guard being skipped when a dispatch
  projects no model (that dispatch writes no generation-keyed document); membership `Contains` cost and read
  amplification (one linear scan per query behind a store round-trip); and cosmetic naming, nullability,
  duplicate-key-alias, corrupt-document exception-type, and test-summary observations.

## Design Notes

Use version-qualified keys plus an authoritative current-schema tenant index/manifest. The shared batch writes the complete current view and deletes candidate-known legacy keys; membership-first readers make any undiscoverable historical orphan unreachable. Candidate and operation ordering are ordinal and deterministic, and platform size limits fail closed rather than producing a partial migration.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -p:NuGetAudit=false -m:1 -v minimal` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorkItemSharedProjectionRebuildHandlerTests` -- expected: all migration/reconciliation cases pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorkItemProjectionQueryAdapterTests` -- expected: normal adapter regressions remain green.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemRollUpProjectionTests` -- expected: pure recursive convergence remains green.
- `git diff --check` -- expected: no whitespace errors and no deferred-work-ledger diff.

## Auto Run Result

Status: done
Blocking condition: none

### Summary

Third review pass over the committed shared roll-up reconciliation. Four independent layers (blind,
edge-case, verification-gap, intent-alignment) ran against the full diff since `535d1b46`. The pass found two
high-severity defects the earlier passes missed — a red fitness suite caused by a documentation rewrite, and a
count-based child-retention heuristic that silently discards reconciled structure — plus two verification gaps
and one inaccurate comment. All five were patched and reverified; nothing new was deferred; twenty-four
proposals were rejected.

### Files Changed In This Pass

- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` — reconciled-child retention is now a union
  of the persisted and replayed child identities, ordinally ordered, instead of a count comparison followed by
  a wholesale replacement.
- `docs/eventstore-api-surface-constraints.md` — restored the "not a shadow-projection plus atomic-swap model"
  phrasing on the checkpoint-path sentence that `BuildConfigurationTests` pins.
- `src/Hexalith.Works/Runtime/WorksHost.cs` — the batch-limit comment now states that the canonical-byte
  ceiling, not the operation bound, is what actually bounds a rebuild, and that oversized rebuilds are refused
  whole.
- `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs` — new
  `Later_child_spawn_merges_with_reconciled_children_instead_of_replacing_them` regression.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` — new
  `Undecodable_non_relationship_event_refuses_rolled_shapes_on_the_ordinary_path` regression.
- `tests/Hexalith.Works.IntegrationTests/WorkItemReadModelGenerationQueryTests.cs` — new
  `Null_legacy_collections_fail_closed_and_dispatch_does_not_rewrite_the_legacy_index` regression.

### Review Findings Breakdown

- Patches applied: 5 (high 2, medium 2, low 1).
- Items deferred: 0. The pending-date-await gap recorded in pass 2 stands unchanged in frontmatter.
- Items rejected: 24.

### Follow-up

`followup_review_recommended: true`. Patched severities this pass: high 2, medium 2, low 1. A high-severity
patch alone sets the flag; the score is `3 × 2 + 1 × 1 = 7`, also ≥ 5.

### Verification

- `~/.dotnet/dotnet ~/.dotnet/sdk/10.0.400/MSBuild.dll <project> -t:Build -p:Configuration=Release
  -p:NuGetAudit=false -m:1 -v:minimal` for IntegrationTests, UnitTests, PropertyTests and ArchitectureTests —
  zero warnings, zero errors. (The pinned 10.0.301 SDK is not installed, so the spec's `dotnet build` form
  cannot run here.)
- `WorkItemSharedProjectionRebuildHandlerTests` — 12 passed (11 pre-existing + 1 new).
  `WorkItemProjectionQueryAdapterTests` — 30 passed. `WorkItemReadModelGenerationQueryTests` — 8 passed.
  `GetWorkItemQueryHandlerTests` — 4. `StreamReadingCascadeDescendantSourceTests` — 6.
  `PendingDateAwaitIndexDispatcherTests` — 11. `WorksAppHostTopologyTests` — 7.
  `WorksDomainEventSubscriptionTests` — 9.
- All deterministic integration classes (`-class- "*SmokeTests"`) — 246 passed, 0 failed.
- Full unit suite — 528 passed, 0 failed. Property tests — 3 passed.
- `Hexalith.Works.ArchitectureTests` — 207 passed, 0 failed. This suite was red at the start of the pass
  (`P1_EventStoreImplementationConstraintsAreRecorded`) and is green after the documentation patch.
- Negative control for the high-severity retention fix: with the pre-fix count comparison restored and
  rebuilt, `Later_child_spawn_merges_with_reconciled_children_instead_of_replacing_them` fails (1 of 12); with
  the union in place it passes.
- `git diff --check` — clean.
- Deferred-work ledger: not edited by this pass. Its uncommitted DW-73 row is the orchestrator's own
  bookkeeping and is committed verbatim so the working copy is left clean.

### Residual Risks

- The operator quiescence fence between inventory capture and Commit remains a documented precondition, not an
  enforced one; the Works handler still cannot arbitrate a concurrent `LastWrite` writer.
- Reconciled child structure retained across ordinary dispatches is only as fresh as the last rebuild: a
  parent's rolled totals stay unavailable until the next shared reconciliation, which is the intended
  fail-closed shape rather than a stale number.
- No test drives the rebuild anywhere near the 10,000-aggregate admission bound or the 4 MiB canonical-manifest
  ceiling, and the completeness walk is recursive, so behaviour at those bounds — including a pathologically
  deep parent chain — remains unproven.
- The whole shared lifecycle is proven in-process against `InMemoryReadModelStore`; the deployed Dapr route,
  a real state store, and EventStore-side inventory capture are covered only by topology and route-shape
  assertions.
