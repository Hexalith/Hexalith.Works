# Epic 4 Context: Shared Work Execution and Builder Runtime Validation

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Let teams, agents, and external parties share one Party-based executor model so assign, reassign, claim, and handoff use the same binding, while builders prove the full command/event pipeline, projections, reminders, and reactor recovery in a platform-owned Aspire topology. This keeps Works a domain module and gives builders runtime evidence without shipping duplicated platform infrastructure.

## Stories

- Story 4.1: Bind Work to a Uniform Party Executor
- Story 4.2: Assign, Reassign, and Hand Off Work
- Story 4.3: Claim Queued Work with Single-Claim-Wins
- Story 4.4: Resolve the Tenant's What's Next Queue
- Story 4.5: Prove the Command/Event Pipeline in the Platform Topology
- Story 4.6: Prove Reminder and Reactor Recovery
- Story 4.7: Trigger Reactor Translators from the Live Event Stream
- Story 4.8: Register and Reconcile Date Reminders Durably
- Story 4.9: Migrate Works Hosting to the Platform Boundary

## Requirements & Constraints

- Every executor uses one `ExecutorBinding` of `PartyId`, `Channel`, and `AuthorityLevel`. System, internal-user, and external parties share that shape; only field values differ. Channel is orthogonal to the executor and may change mid-work. A Work Item has exactly one binding — the Party currently responsible. Approvers, observers, and escalation rungs are not bindings and must not add a second slot.
- Assign, reassign, and human↔AI handoff are one operation (`AssignWorkItem` → `WorkItemAssigned`). Rebind is accepted while `Assigned`. `InProgress` and `Suspended` reject both assign and queue; handoff of active work requires finishing or cancelling first, or returning to `Assigned` then requeue-and-claim. Requeue emits `WorkItemQueued`. Terminal states reject assignment with no binding mutation. Do not branch on executor kind, and do not wire an `IExecutorRouter` implementation.
- `AuthorityLevel` uses the ordered set `{Read, Contribute, Coordinate, Administer}` and is preserved through create, assign, and reassign. v1 carries it; no behavior or authorization branches on it. Adding a Channel or AuthorityLevel value must not require Server/Projections changes or a new event type.
- Push and pull coexist. `Claim` is the only act that enters `InProgress`, from `Assigned` (the bound executor starting pushed work) or `Queued` (pull). There is no start event; progress on a non-`InProgress` item is rejected. Any executor in the tenant may claim; eligibility, routing scores, and escalation stay deferred.
- Concurrent claims produce exactly one owner. Commands carry no expected version or ETag. The loser is re-handled against fresh state and emits the existing `WorkItemTransitionRejected(InProgress, "Claim")`. Retry exhaustion is an infrastructure `ConcurrencyConflict` with no loser append, publication, or dead-letter side effect. Do not add `ClaimRejected` or `ConcurrencyRejected`.
- "What's next" is a tenant-scoped projection/query of `Queued` and `Assigned` items, not a routing engine. Sort by Priority (the enum's declared order: Critical, High, Normal, Low), then earliest Due Date, then deterministic `WorkItemId` ordinal. Items with neither Priority nor Due Date sort last. Works records no cross-aggregate creation coordinate; minting sortable ULIDs at the edge is guidance only. Query-side authorization and result filtering are required in addition to tenant scoping. Updates are incremental; the projection is rebuildable from the event stream and holds no authoritative state.
- The envelope's acting Party is the identity the platform authenticated, never a caller-supplied field and never inferred from the binding (binding = who is responsible; envelope = who acted). System-originated acts (date resume, expiry, cascade, child-completion resume, reactor spawn) record workload identity, tenant-delegation context, and causation.
- Tenant isolation applies to identities, state and projection keys, queries, and logs. Structured logs must exclude event payloads, personal data, secrets, raw tokens, and full command bodies. Failures expose RFC 9457 Problem Details with correlation and tenant context.
- Works remains headless in v1: no production UI, MCP, chatbot, email, routing, cost, or Theme-6 security adapters. It ships no AppHost, Aspire, or ServiceDefaults project and does not duplicate health, telemetry, Dapr, projection/query, delivery, scheduling, or subscription plumbing. Tier-1 tests stay pure (no Dapr, network, browser, containers, or Aspire). Topology tests are only for real platform boundaries.
- Runtime evidence must cover persist-then-publish and the lifecycle create → progress → spawn → suspend → resume → complete with correct roll-up, plus claim conflict, queries, ordinary projections, shared rebuild, date resume, expiry reminder, cascade, and mid-cascade restart convergence. Time and expiry enter only as commands; `Handle` and reactor translators never read a clock. Deadlines stay advisory until the adapter fires.

## Technical Decisions

- The destination host is `Hexalith.Platform`, owned by the Platform Maintainer. Works keeps `Contracts`, `Server`, `Projections`, pure `Reactor` translators, `Testing`, and the minimal EventStore domain-service executable using `AddEventStoreDomainService(...)` and `UseEventStoreDomainService()`. Missing reusable runtime capability is added to EventStore or the platform first, then consumed. Works supplies reminder and reactor domain intents; generic durable-reminder, delivery, checkpoint, subscription, and reconciliation seams belong to EventStore; topology and operational policy belong to the platform.
- Claim is a single-aggregate operation; the claimable pool is a read projection, not an authoritative queue. EventStore serializes same-item commands with the actor turn lock; a Dapr state-store ETag conflict is the fallback retry path. Tests must cover both the turn-serialized domain rejection and an injected ETag conflict, retry, and exhaustion path — not timing-dependent races.
- Event delivery is at-least-once and unordered. Reactor translators live outside the kernel and emit only mechanical command intents; every decision round-trips through aggregate `Handle`. Cascade and child-completion resume are checkpointed and recovered from re-readable projections, never in-memory loops. Internal originators (reminders, cascade, child-completion resume, recovery) submit through one outbound command-submission seam with deterministic message and causation identity.
- Date-reached and expiry reminders are self-targeted durable actor reminders with deterministic, tenant-inclusive names. Recovery treats aggregate streams as truth and a tenant-scoped pending-await index as a discovery aid only; never issue a tenant-wide null-aggregate stream scan. Reminder durability (Scheduler HA/backup and callback failure policy), the shared-rebuild capture-through-Commit fence, cascade checkpoint concurrency, and outbound command transport idempotency are platform-lane bindings that must exist before hosting migration can claim those guarantees. Due-Date/TTL policy is platform host configuration; the kernel never reads it.
- Production identity is established at the platform ingress. Authoritative tenant and actor come from verified Tenants membership; payload tenant/user fields are assertions compared before dispatch or query. Internal originators use workload identity plus an audited tenant-delegation context. Trusted-origin and deny-by-default network controls are platform acceptance, not kernel behavior. Production ingress is prohibited until that contract is live.
- The platform Aspire line is a single 13.5.x family owned by `Hexalith.Platform`. Remove a Works hosting project only after the matching migration-matrix row is green in that host's conformance lane. Rollback is restoring the prior Works host composition until that lane is accepted.

## UX & Interaction Patterns

v1 ships no end-user surface. Shape read models so a later What's next queue can show assigned (push) and claimable (pull) rows, update live without refresh, and render every executor as one Party chip: expose kind, Channel, and AuthorityLevel as data, never as a branch. Carry all nine statuses and, for Suspended items, await-condition kind and key. Keep own Remaining and rolled Remaining as distinct fields or types; never coerce mixed Units into one figure. Lost-claim copy for a later surface is "Someone else got there first."

## Cross-Story Dependencies

The uniform binding (4.1) and assign/requeue path (4.2) feed claim (4.3) and the What's next query (4.4). Pipeline proof, reminder/reactor recovery, live event-stream triggering, and durable date-reminder registration (4.5–4.8) depend on Epic 2 lifecycle transitions and Epic 3 tree, await-conditions, roll-up, and pure cascade/resume translators; spawn in the live topology now enters through Epic 3's registry reserve, with `SpawnChild` accepted only from the reactor's workload identity. Story 4.9 must preserve that evidence while relocating composition to `Hexalith.Platform`. Do not remove Works-owned hosting until the replacement topology already has equivalent or stronger passing runtime evidence.
