---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: 'complete'
completedAt: '2026-06-14'
updatedAt: '2026-09-06'
inputDocuments:
  - '_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/brief.md'
  - '_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md'
  - '_bmad-output/brainstorming/brainstorming-session-2026-06-14-0910.md'
  - 'Hexalith.Projects/_bmad-output/project-context.md (project context — ecosystem conventions)'
workflowType: 'architecture'
project_name: 'Hexalith.Works'
user_name: 'Administrator'
date: '2026-06-14'
---

# Architecture Decision Document — Hexalith.Works

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Architecture Decision Register (AD)

_Added 2026-09-06 to resolve the 2026-09-05 validation gate (VAL-C01…C05, VAL-H01) and give this
legacy document stable decision identity (VAL-H04). This register is the **binding consistency
contract**: each entry carries a stable `AD-n` ID with **Binds** (what it fixes across independently
built units), **Prevents** (the divergence it exists to stop), and **Rule** (the falsifiable
statement builders follow). Entries marked `[ADOPTED]` ratify decisions already settled by the
ecosystem, this document's earlier steps, or live code. The narrative sections below remain
analysis, rationale, and cold-start seed; **where prose and this register disagree, the register
wins.** Legacy labels (A1–A5, B1–B3, C1–C4, D1–D2, E1–E2, D-1–D-4) map to AD entries in the
legacy-label map at the end of this section._

### AD-01 `[ADOPTED]` — Event-sourced kernel on Hexalith.EventStore

- **Binds:** the persistence/eventing substrate for every Works unit — persist-then-publish;
  EventStore owns envelope metadata (`SequenceNumber`, acting party, timestamp); Works emits
  payloads only; `Hexalith.PolymorphicSerializations` for every durable type; additive,
  serialization-tolerant evolution, no `V2` types.
- **Prevents:** units inventing parallel persistence, envelope, or serialization schemes.
- **Rule:** `Handle(state, command)` is pure and returns events/rejections; `Apply` mutates only
  in-memory state; rejections are `IRejectionEvent`s, infrastructure failures are
  exceptions/dead-letter; nothing publishes before persistence succeeds.

### AD-02 `[ADOPTED]` — Aggregate ID assigned at the edge *(legacy A1)*

- **Binds:** `CreateWorkItem` carries a `WorkItemId` produced by the `Hexalith.Commons` helper at
  the command-creation edge.
- **Prevents:** nondeterministic replay and untestable creates from in-`Handle` ID generation.
- **Rule:** no ID generation (GUID/RNG) inside `Handle`/`Apply`/reactor. Edge assignment alone is
  **not** a transport idempotency contract — that remains open (see VAL-H10 in the open register).

### AD-03 `[ADOPTED]` — Priority is a small ordered enum *(legacy A2)*

- **Binds:** `Priority = Critical/High/Normal/Low`, additive-tolerant; input to "what's next"
  ordering (Priority → Due Date → deterministic identity order; none sorts last).
- **Prevents:** numeric routing bands leaking Theme-4 machinery into v1.
- **Rule:** ordering logic consumes the enum's declared order only. The FR-20 tiebreak is
  **deterministic identity order** (`WorkItemId` ordinal) — the VAL-H03 product fork was resolved
  by the approved 2026-09-06 correct-course; Works records no cross-aggregate creation coordinate.
  Edge originators are advised (not bound) to mint sortable ULIDs via `Hexalith.Commons` so
  identity order approximates creation order in practice.

### AD-04 `[ADOPTED]` — Unit immutable after first estimate *(legacy A3)*

- **Binds:** per-item `Unit` value object frozen at first estimate.
- **Prevents:** silent unit coercion across mixed-unit trees.
- **Rule:** `ProgressReported`/`ReEstimated` carrying a different Unit are domain-rejected;
  mixed-Unit roll-up exposes per-Unit subtotals, never a coerced single figure.

### AD-05 `[ADOPTED]` — Cost-ready burn-down Meter *(legacy A4)*

- **Binds:** `Meter(Unit, Estimated, Done)` with derived `Remaining` (clamped ≥ 0); one Effort
  meter in v1; a future Cost meter reuses the identical type (Theme 5).
- **Prevents:** parallel burn-down shapes per concern.
- **Rule:** no new meter-like type may be introduced for effort or cost.

### AD-06 `[ADOPTED]` — Roll-up is per-child envelope-position LWW *(legacy A5)*

- **Binds:** the roll-up document shape — **one LWW slot per descendant** (not per direct child),
  keyed by that descendant's id and its **own** envelope `SequenceNumber`, valued with that
  descendant's **own contribution** per Unit; `rolled = own + Σ descendant slots`. Every slot is
  written only from its own descendant's event stream, so LWW always compares within a single
  sequence space. *(Amended 2026-09-06: the earlier recursive phrasing "`rolled = own + Σ per-child
  rolled`" put values from different levels — and therefore different stream-sequence spaces — into
  one slot and is retired; the flattened form computes the identical total — ADV-U1.)*
- **Prevents:** additive-delta corruption under redelivery/reorder (RR-1, SM-2); LWW comparisons
  across two different streams' sequence numbers.
- **Rule:** stale/lower-sequence writes to a slot are ignored; replays never double-count; never
  additive deltas; the RR-1 FsCheck convergence property must run over trees ≥ 3 levels deep. Live
  delivery of contributions to ancestors is AD-22.

### AD-07 `[ADOPTED]` — Authority split on the numbers, type-separated *(legacy B3)*

- **Binds:** own-Remaining and Status (incl. `Remaining 0 → Completed`) are aggregate-authoritative
  and synchronous; rolled-Remaining is an eventually consistent projection with a distinct type,
  field name, and serialized shape.
- **Prevents:** consumers gating control flow on the eventual value; a projection flipping status.
- **Rule:** the two numbers never share a type or serialized shape; a projection never flips status.

### AD-08 `[ADOPTED]` — Claim concurrency is EventStore-owned; commands carry no version *(legacy B1, D-2)*

- **Binds:** claim is a single-aggregate operation; the claimable pool is a read projection, never
  an authoritative queue aggregate. EventStore serializes same-item commands through the Dapr actor
  **turn lock** — the normal loser mechanism is a domain rejection produced against freshly
  committed state (`WorkItemTransitionRejected(InProgress, "Claim")`). The Dapr state-store
  **ETag** conflict on the atomic actor-state save is the fallback recovery path: EventStore
  rehydrates and re-handles within its bounded retry; exhaustion surfaces an infrastructure
  `ConcurrencyConflict` with no loser append/publication/dead-letter effect.
- **Prevents:** callers supplying expected versions; multi-aggregate claim non-atomicity; tests
  demanding a genuine ETag race for every two-request contest.
- **Rule:** no Works command exposes a version or ETag field. Tests must cover both the
  turn-serialized domain rejection and the injected ETag conflict/retry/exhaustion path.

### AD-09 `[ADOPTED]` — Delivery posture: at-least-once; ordering never relied upon *(legacy B2, D-4)*

- **Binds:** Dapr pub/sub guarantees at-least-once delivery; **ordering is component- and
  configuration-specific and is not a portable Works contract.**
- **Prevents:** broker-ordering assumptions creeping into projections or the reactor.
- **Rule:** write-path ordering comes from the single-writer actor; read-path correctness from
  idempotent, order-tolerant projections (AD-06) plus substrate offset dedup — regardless of what
  ordering the deployed broker happens to offer.

### AD-10 `[ADOPTED]` — Reactor outside the kernel, mechanical *(legacy C1, D-1)*

- **Binds:** `react(event) → command[]` is pure, mechanical event→command translation living
  outside the aggregate kernel; runtime delivery/checkpoint/subscription plumbing comes from
  EventStore platform seams composed by the platform host.
- **Prevents:** a shadow kernel; kernel growth disguised as "orchestration" (SM-C1/SM-C2).
- **Rule:** the reactor contains no conditional a pure `Handle` could not have produced; targets
  are idempotent; cascade is checkpoint-driven off a re-readable projection, never an in-memory loop.

### AD-11 `[ADOPTED]` — Date resume via durable actor reminders, reconciled discovery *(legacy C2)*

- **Binds:** a Work Item parked on `DateReached` registers a self-targeted durable Dapr actor
  reminder (Scheduler-backed); firing raises `ResumeWorkItem` — `Handle` never reads a clock.
  Recovery discovery uses the proven registry/indexed pending-await protocol: **aggregate streams
  are the authoritative truth; the tenant registry and per-tenant pending-await index are discovery
  aids only**, updated after the authoritative write.
- **Prevents:** tenant-wide stream scans (rejected by the gateway); lost registrations going
  undiscovered; the index being treated as truth.
- **Rule:** reminder durability is **conditional** on Scheduler persistence/HA/backup and an
  explicit callback failure policy — both bound in the platform lane (AD-20 matrix R6). The phrase
  "durable by construction" is retired; the narrow guarantee is "registration persists across
  supported failover; missed firings are reconciled by the indexed recovery pass."

### AD-12 `[ADOPTED]` — Deadlines are advisory-until-fired *(legacy C3, D-3)*

- **Binds:** expiry/overdue is a property of the timer adapter having fired; the kernel may hold a
  "live" item that is in reality overdue.
- **Prevents:** clock reads leaking into `Handle`.
- **Rule:** re-validate this decision against Theme 5 (cost-aware scheduling) before that theme
  builds; retrofitting a logical clock is a redesign, not a patch.

### AD-13 `[ADOPTED]` — Await-condition set; first-match, idempotent resume *(legacy C4)*

- **Binds:** `AwaitCondition = ChildCompleted(childId) | DateReached(instant) |
  ExternalSignal(correlationId)`; a suspended item holds a set and resumes on first match.
- **Prevents:** divergent resume semantics per satisfier.
- **Rule:** `ResumeWorkItem` is idempotent — no current match is a no-op; a duplicate is a no-op.

### AD-14 `[ADOPTED]` — AuthorityLevel carried, not enforced *(legacy D1)*

- **Binds:** ordered set `{Read, Contribute, Coordinate, Administer}` on the binding; no v1
  behavior branches on it.
- **Prevents:** speculative permission machinery (SM-C2).
- **Rule:** AuthorityLevel is an additive seam for Themes 4/6 and is **not** a substitute for the
  AD-23 authorization floor.

### AD-15 `[ADOPTED]` — Tenant isolation at every layer *(legacy D2)*

- **Binds:** tenant-scoped identity, state/projection keys, queries, and logs; query-side
  authorization is a distinct control from key-prefixing; parent/child references are
  tenant-closed; the roll-up asserts tenant-equality at every hop.
- **Prevents:** silent cross-tenant leaks via traversal; key-prefix-only reads.
- **Rule:** mutation-validated negative tests are mandatory (RR-4). Tenant identity **provenance**
  is AD-23; the cross-tenant recovery registries remain an open governed-exception item
  (VAL-H09/SEC-DI-06 in the open register).

### AD-16 `[ADOPTED]` — Rebuild is reader-safe and atomically visible at Commit *(legacy E1)*

- **Binds:** independent aggregate projections use EventStore's pausable checkpoint rebuild;
  relationship-aware projections use the bounded `/project/rebuild/shared/v1`
  Begin/Accumulate/Finalize/Stage/Commit lifecycle over a sealed tenant inventory; ordinary
  delivery is quiesced or platform-fenced from inventory capture through Commit, then catches up.
- **Prevents:** readers observing staged or partial state.
- **Rule:** readers stay on the prior generation until atomic promotion. The concrete
  capture-through-Commit **fence protocol** (token/epoch, admitted writers, watermark, catch-up
  proof) is not yet supplied by the EventStore API — it is an open platform obligation (VAL-H08)
  allocated in the AD-20 matrix (R4) and must be bound before Story 4.9 acceptance claims this NFR.

### AD-17 `[ADOPTED]` — Validation domains *(legacy E2)*

- **Binds:** `ProgressReported` delta > 0; `Estimated` ≥ 0; `Remaining` clamped ≥ 0; Unit immutable
  after first set; `docs/lifecycle-transition-matrix.md` is authoritative for transitions.
- **Prevents:** per-implementer validation drift.
- **Rule:** Due-Date/TTL policy values come from **platform-host configuration** (AD-25); the
  kernel never reads configuration.

### AD-18 — Dependency graph is direct-to-Contracts *(corrects the legacy chain)*

- **Binds:** `Server → Contracts`; `Projections → Contracts`; `Reactor → Contracts`;
  `Testing →` kernel; the minimal `Hexalith.Works` executable references the EventStore SDK plus
  the inward units. **Projections does not reference Server.**
- **Prevents:** the forbidden `Projections → Server` reference the legacy chain
  `Contracts ← Server ← Projections` implied (VAL-H02), which fails
  `DependencyDirectionTests`.
- **Rule:** any new project reference must be added to the architecture-fitness allowlist first;
  the allowlist in `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`
  is the machine truth.

### AD-19 — Version authority lives in the build files, not this document

- **Binds:** `global.json` and `references/Hexalith.Builds/Props/Directory.Packages.props` are the
  only authoritative version pins. **Dapr runtime and Dapr .NET SDK are distinct fields**; the
  platform host (AD-20) owns the runtime/deployment pin, central package management owns SDK/package
  pins.
- **Prevents:** builders downgrading to stale copies of pins embedded in prose (VAL-H05/TECH-01).
- **Rule:** this document never states a version as "current"; any version table here is a dated
  historical snapshot subordinate to the authoritative files.

### AD-20 — Platform host: `Hexalith.Platform`, owned by Platform Maintainer (Hexalith) *(NEW — resolves VAL-C01)*

- **Binds:**
  - **Repository:** [`Hexalith.Platform`](https://github.com/Hexalith/Hexalith.Platform) is the
    designated platform/host repository — the Story 4.9 destination for all Works runtime topology.
    It already carries the identical contract for Hexalith.Agents (EXT-HOST-1).
  - **Accountable owner:** **Platform Maintainer (Hexalith)** — owns the replacement topology, the
    conformance lane, and migration acceptance.
  - **Package boundaries:** Works ships `Contracts`/`Server`/`Projections`/`Reactor`/`Testing` plus
    the minimal domain-service executable only; `Hexalith.EventStore` ships every generic runtime
    seam (SDK); `Hexalith.Platform` ships topology, composition, and operational policy. Missing
    reusable capability is added to the EventStore SDK first, never copied into Works.
  - **Supported Aspire family:** a **single 13.5.x family** — `Aspire.AppHost.Sdk` and
    `Aspire.Hosting*` packages move together on the 13.5 line; `Hexalith.Platform` upgrades from its
    current 13.4.6 scaffold before or with Works composition. The mixed 13.4.6/13.5.3 graph is
    retired, not grandfathered; the vestigial `Aspire.AppHost.Sdk` pin still present in Works'
    `global.json` is removed together with the AppHost in Story 4.9 (R1).
  - **Topology/conformance lane:** `Hexalith.Platform` owns a `verify-works-host` lane (mirroring
    its `verify-agents-host.sh`) plus the Story 4.9 acceptance scenarios — create, progress, spawn,
    suspend, child resume, date resume, cascade, claim conflict, query, ordinary projection, shared
    rebuild — run from the platform topology.
- **Prevents:** the double-abandonment failure: Works deleting adapter-edge behavior as
  "platform-owned" while the platform composes only the minimal executable, leaving reminders,
  cascades, checkpoints, subscriptions, and projections with no runtime owner.
- **Rule:** Story 4.9 may remove a Works hosting project **only after** the corresponding migration
  matrix row below is green in the `Hexalith.Platform` lane. Rollback is restoring the prior Works
  host composition until the platform lane is accepted.

**Migration matrix (seam by seam):** target owners follow the approved allocation rule — generic
capability → EventStore SDK; domain-specific behavior → Works behind SDK interfaces; topology and
operational policy → `Hexalith.Platform`.

| # | Seam (today in Works) | Target owner | Proof before removal |
|---|---|---|---|
| R1 | Aspire topology + Dapr components: topics, state stores, Scheduler/placement, dead-letter, access control (`Hexalith.Works.AppHost`) | `Hexalith.Platform` apphost (13.5.x) | `verify-works-host` + full Story 4.9 scenario suite |
| R2 | ServiceDefaults: health, telemetry, resilience (`Hexalith.Works.ServiceDefaults`) | EventStore DomainService SDK defaults, composed by `Hexalith.Platform` | Host starts with no forked defaults (4.9 AC2) |
| R3 | Event subscription endpoint + durable event processor (decode, identity checks, terminal-skip) | EventStore SDK generic delivery pipeline; Works keeps pure domain event handlers | Subscription scenarios + AD-24 negative tests |
| R4 | Projection/query plumbing: dispatcher, read-model stores, `/project` endpoint, generations — **plus the new relationship-aware fan-out seam (AD-22) and the shared-rebuild fence protocol (AD-16/VAL-H08)** | EventStore SDK; Works keeps pure projection strategies | Ordinary projection + roll-up convergence + fenced-rebuild scenarios |
| R5 | Query handlers (`WhatsNext`, `GetWorkItem`) | Works domain handlers behind the SDK query seam | Query scenarios + AD-23 authorization tests |
| R6 | Reminders: registration, `DateReminderActor`, indexed pending-await source, reconciliation — **plus expiry reminders (AD-25)** | EventStore SDK generic durable-reminder + reconciliation seam; Works supplies domain intents | Date-resume, expiry, and recovery scenarios; Scheduler HA/backup and callback failure policy bound here (AD-11) |
| R7 | Cascade **and child-completion resume** dispatch, checkpoints, recovery (`Recovery/Cascade/` + `Recovery/ChildCompletion/`) | EventStore SDK checkpointed process-runner seam; Works keeps the mechanical translators | Cascade + crash-recovery scenarios **including a missed-child-completion crash**; the VAL-H06 checkpoint concurrency protocol is bound here |
| R8 | Recovery health/alerting posture (exhaustion ⇒ durable evidence + degraded readiness) | `Hexalith.Platform` operational contract (SDK-supported) | Degraded-readiness negative test (SEC-DI-05) |
| R9 | Minimal domain-service host (`Hexalith.Works` — `AddEventStoreDomainService`/`UseEventStoreDomainService`) | **Stays in Works** | 4.9 AC2 |
| R10 | mTLS, access control, network policy (AD-24) | `Hexalith.Platform` | AD-24 negative test suite + the positive production-policy reminder-registration test |
| R11 | Outbound command submission seam (`IWorkCommandSubmitter` / `EventStoreGatewayWorkCommandSubmitter`) — the shared path every internal originator (reminders, cascade, child-completion, AD-21 reactor) submits through | EventStore SDK generic gateway client; Works keeps the deterministic MessageId/causation derivation policy | Deterministic-identity tests; the VAL-H10 transport-idempotency binding lands here |

### AD-21 — Per-tenant Work-Tree Registry owns tree attachment *(NEW — resolves VAL-C02)*

- **Binds:**
  - **Authoritative owner:** a tenant-scoped, event-sourced **Work-Tree Registry** aggregate (its
    own single-writer actor under the EventStore ETag-backed save) owns every
    `(tenant, childId) → parentId` edge and derives ancestry and depth from its own state.
  - **Attachment protocol:** attachment round-trips through the registry — reserve the edge in the
    registry's pure `Handle` (accept ⇒ edge-reserved event; violation ⇒ domain rejection), then the
    mechanical reactor (AD-10) drives `SpawnChild` on the parent. So that the translation stays
    mechanical, the reservation command/event carries the **complete `SpawnChild` payload verbatim**
    (raw-act: reported values, uninterpreted) plus the registry-derived ancestry/depth snapshot the
    parent receives as assertions (ADV-U5). Registry commands/events are additive serialized types
    governed like every other contract (catalog + golden corpus).
  - **Authoritative facts & freshness:** single-parent, acyclicity, bounded depth, and tenant
    closure are evaluated inside the registry's `Handle` against its own rehydrated state — there is
    no staleness window. Caller-supplied `ProposedParentAncestors`, `ProposedParentDepth`,
    `MaxDepth`, and `ExistingChildParent` on `SpawnChild` are demoted to **assertions** revalidated
    against registry-derived values; the pure `WorkTreeAttachmentGuard` remains as an assertion
    check, not an authority.
  - **Conflict behavior:** concurrent attaches serialize on the registry actor; exactly one edge is
    accepted, every other attempt receives a deterministic domain rejection.
  - **Failure lifecycle (the saga's second leg — ADV-U2):** an edge is explicit
    **`Reserved` → `Attached`** (on `ChildSpawned` evidence) **or `Reserved` → `Released`** (on
    spawn rejection, or on a bounded, platform-configured reservation timeout). The spawn rejection
    for a reserved edge is a **dedicated additive rejection event carrying
    `(TenantId, ParentWorkItemId, ChildWorkItemId)`** so the reactor can mechanically translate it
    to the registry release — the existing `WorkItemTransitionRejected` shape stays frozen. A
    duplicate `SpawnChild` for an already-`Attached` identical `(parent, child)` pair is a defined
    **no-op**, never a rejection (so redelivery cannot trigger a compensating release of a healthy
    edge). **Cascade and the AD-22 fan-out enumerate `Attached` edges only** — `Reserved` edges are
    invisible to both.
  - **Repair semantics:** the registry stream is authoritative **for topology among existing
    items**. A `Reserved` edge whose child stream shows no creation evidence resolves
    **automatically to `Released`** — it is a routine race outcome, not an operator case. Rebuild
    detects genuinely divergent evidence (cyclic, multiply-parented, asymmetric `Attached` claims)
    and suppresses dependent roll-up/cascade results for that subtree until an audited operator
    repair.
- **Prevents:** two compliant parent aggregates accepting the same child; cycles admitted through
  stale or empty caller-fed ancestor lists (the `SpawnChild` permissive-defaults hole).
- **Rule:** no unit mutates tree shape except through the registry protocol — the **public entry
  point is the registry's reserve command**, and direct `SpawnChild` submission is restricted to the
  reactor's workload identity (AD-24); arbitrary external callers never supply trusted topology
  facts or policy limits. Dependency note: the reserve→spawn→release translations need
  deterministic causation/message IDs, which advances the open VAL-H10 transport-idempotency
  binding to *before the registry story is drafted*.

### AD-22 — Live recursive roll-up via registry-backed fan-out *(NEW — resolves VAL-C03)*

- **Binds:** FR-11's incremental delivery design. On a descendant's state-changing projection
  delivery, the projection layer resolves the ancestor chain of **`Attached` edges** from the
  Work-Tree Registry read model (AD-21) and writes the descendant's **own contribution** into
  **each ancestor's** roll-up document using the AD-06 flattened
  per-`(descendantId, descendantEventSequence)` LWW slots — the value is computable from the event
  alone, so a single delivery serves every ancestor level. Ordering: LWW by the descendant's own
  envelope sequence — no atomic multi-document write is required for convergence.
  **Ancestry resolution carries a freshness witness (ADV-U4):** the item's creation evidence
  records the registry sequence of its reservation (or its verifiable root provenance via plain
  `CreateWorkItem`), and ancestry is *resolved* iff the registry read model's watermark ≥ that
  attachment sequence — so genuine roots resolve immediately as empty ancestry (no-op fan-out),
  while a lagging registry projection yields **not-acknowledged ⇒ redelivered** with guaranteed
  progress; no partial ack, no silent skip, no livelock. Fan-out writers are **fenced writers under
  AD-16** during a shared rebuild — they follow the fence, never race the staged generation.
  "Unavailable" roll-up semantics are reserved for declared repair windows (AD-21 suppression) and
  rebuild staging (AD-16) — they are **not** the ordinary steady state.
- **Ownership:** the relationship-lookup + multi-document fan-out capability is generic — it lands
  in the EventStore SDK first (AD-20 matrix R4); Works owns the pure contribution/merge strategy.
- **Prevents:** the current contradiction — a Works projection with no graph input while the
  platform's single-aggregate `/project` path persists parent rolled values as unavailable until an
  operator-run shared rebuild.
- **Rule:** the ordinary delivery path must converge every ancestor without operator action; shared
  rebuild is for repair and migration, never routine roll-up. FR-11 stands as written.

### AD-23 — Identity provenance and the authorization floor *(NEW — resolves VAL-C04)*

- **Binds:**
  - **Authentication:** every external caller authenticates at the `Hexalith.Platform` ingress
    (OIDC). Works never authenticates and never treats a payload/envelope identity field as
    authority.
  - **Claim-to-tenant derivation:** the authoritative tenant and actor are derived from verified
    claims mapped through **Hexalith.Tenants** membership. Enforcement is owned by the platform
    host; membership truth is owned by Hexalith.Tenants.
  - **Assertion-mismatch behavior:** envelope/payload `TenantId`/`UserId` are assertions compared
    against the derived identity — a mismatch or missing binding is **denied before** aggregate
    dispatch, persistence, query execution, or any tenant-existence disclosure.
  - **Service delegation:** internal originators — reactor, reminder callbacks, **the reminder
    registrar in the delivery pipeline (registration/cancellation/reschedule of AD-11/AD-25
    reminders)**, replay, recovery — act under an authenticated **workload identity plus an
    explicit, auditable tenant-delegation context**, never a borrowed user identity.
  - **Minimum authorization:** commands ⇒ authenticated member of the target tenant; queries ⇒
    tenant member plus query-side result filtering; admin/rebuild/replay/quarantine disposition ⇒ a
    dedicated platform-operator role, individually audited.
- **Prevents:** an authenticated (or internal) caller selecting another tenant while satisfying
  every key-equality check — tenant prefixes only prevent accidental collisions after an untrusted
  assertion is accepted.
- **Rule:** enforcement and its negative tests are **Story 4.9 platform acceptance**; production
  ingress is prohibited until this contract is live. AuthorityLevel (AD-14) stays
  carried-not-enforced; Theme 6 covers step-up/signed-link hardening, not this baseline.

### AD-24 — Trusted origin for events, reminders, replay, and callbacks *(NEW — resolves VAL-C05)*

- **Binds:** in production topology:
  - Dapr **mTLS** with a declared trust domain and namespace; deny-by-default app-level access
    control and network policy — no direct workload HTTP reachability to subscription, actor,
    projection, replay, or admin routes.
  - Broker access over TLS with producer/consumer ACLs.
  - **Exclusive originators:** only EventStore publishes `work.events`; reminder callbacks are
    accepted only from the Works actor identity; **actor reminder register/cancel/reschedule is
    accepted only from the Works service's own workload identity (self-targeted, ADV-U6)**;
    replay/rebuild/quarantine actions only from the authorized operations identity; and
    **registry-internal commands are origin-restricted** — `SpawnChild` is accepted at the gateway
    only from the reactor's workload identity (the public attachment entry is the AD-21 registry
    reserve command, ADV-U3).
  - Identity-field equality checks (payload vs envelope) are necessary but never sufficient —
    **authenticated provenance is required before an envelope `SequenceNumber` is trusted** as
    canonical.
- **Prevents:** a self-consistent forged event with a high canonical sequence poisoning LWW
  watermarks and triggering cascade, cancellation, expiry, or resume side effects.
- **Rule:** Story 4.9 acceptance includes negative migration tests: direct-route call, spoofed app
  ID, wrong trust domain/namespace, unauthorized topic publication, forged high `SequenceNumber`,
  and **direct `SpawnChild` submission under an ordinary tenant-member identity (denied)** — plus
  one **positive**-path test: an expiry/date reminder registered successfully **under the
  production access policy** (so deny-by-default cannot silently strand AD-25 while the sandbox
  exemption passes). The local sandbox may relax only explicitly named controls under a dev-only
  exemption recorded in the `Hexalith.Platform` repository.

### AD-25 — Automatic expiry via expiry reminders; policy from platform configuration *(NEW — resolves VAL-H01)*

- **Binds:**
  - **Trigger owner:** the reminder subsystem (same family as AD-11). Every Work Item with a Due
    Date or applicable TTL policy holds one durable **expiry reminder**, registered, cancelled, and
    rescheduled mechanically from lifecycle events (`WorkItemCreated`, `WorkItemRescheduled`,
    terminal events) by the reminder adapter — never by the kernel.
  - **Identity:** the expiry reminder name is deterministic and tenant-inclusive, so registration is
    idempotently re-registerable and collision-free across tenants.
  - **Firing:** emits `ExpireWorkItem`; expiring an already-terminal item follows the per-state
    cancel/expire transition table (defined no-op/rejection) — firing is idempotent.
  - **Recovery:** missed expiry firings are reconciled through the same indexed pending-source
    protocol as `DateReached` (AD-11), under the same platform-lane durability bindings (AD-20 R6).
  - **Policy authority:** Due-Date/TTL defaults per work-type/tenant are **`Hexalith.Platform` host
    configuration** — typed options consumed by the reminder adapter. The kernel never reads
    configuration; a later migration to Tenants-owned settings requires no kernel change.
- **Prevents:** conformant implementations shipping no automatic expiry, Due-Date-only expiry,
  TTL-only expiry, or incompatible recovery behavior (the VAL-H01 divergence).
- **Rule:** `Handle` still never reads a clock; expiry enters the domain only as `ExpireWorkItem`
  from the reminder adapter. **FR-10 stands unchanged** — no correct-course pass is required.

### Legacy-label map

The legacy prose uses two colliding label families (`D-1…D-4` open decisions vs `D1/D2` security
decisions). This map is authoritative; downstream references should migrate to AD IDs.

| Legacy label | AD entry | | Legacy label | AD entry |
|---|---|---|---|---|
| A1 | AD-02 | | C1 | AD-10 |
| A2 | AD-03 | | C2 | AD-11 |
| A3 | AD-04 | | C3 | AD-12 |
| A4 | AD-05 | | C4 | AD-13 |
| A5 | AD-06 | | D1 (AuthorityLevel) | AD-14 |
| B1 | AD-08 | | D2 (tenant isolation) | AD-15 |
| B2 | AD-09 | | E1 | AD-16 |
| B3 | AD-07 | | E2 | AD-17 |
| D-1 (reactor placement) | AD-10 | | D-3 (deadline semantics) | AD-12 |
| D-2 (claim cardinality) | AD-08 | | D-4 (substrate ordering) | AD-09 |

### Open findings register (2026-09-05 gate residue)

Items the gate raised that this update deliberately does **not** close; each has a disposition and
a revisit condition (the accountable role is the AD-20 owner unless a row names another). None
blocks Story 4.9 entry, but R-flagged items block its acceptance.

| Finding | Status | Revisit condition |
|---|---|---|
| VAL-H03 — FR-20 creation order vs live identity order | **Resolved 2026-09-06** — approved correct-course blessed deterministic identity order (`WorkItemId` ordinal) as the FR-20 tiebreak; PRD/epics/AD-03 amended, no creation coordinate added | Closed (sprint-change-proposal-2026-09-06) |
| VAL-H06 — cascade checkpoint concurrency protocol (monotonic states, ETag/CAS, replica ownership) | Bound in AD-20 matrix **R7** spec work | Story 4.9 R7 acceptance |
| VAL-H07 — reminder end-to-end durability (Scheduler HA/backup, callback failure policy, continuous retry/alerting) | Narrowed by AD-11; full binding in **R6** | Story 4.9 R6 acceptance |
| VAL-H08 — shared-rebuild capture-through-Commit fence protocol | Allocated to EventStore SDK + platform in **R4** (AD-16) | Story 4.9 R4 acceptance |
| VAL-H09 — namespace/ownership table; governed control-plane exception for global recovery registries | Open — replace the universal `{tenant}:work:{id}` derivation claim with a namespace table during R3/R6/R7 migration | Story 4.9 design review |
| VAL-H10 — transport command idempotency (MessageId/IdempotencyKey reuse, retention, replay result) | Open — bind at the R11 submission seam | **Before the AD-21 registry story is drafted** (its reserve→spawn→release translations need deterministic causation IDs — advanced from the earlier Theme 3/R3 condition) |
| VAL-H11 — schema compatibility matrix (reader/writer direction, enum/unknown-type behavior, rollout order) | Open — catalog governance work | Before the Story 1.5 catalog change ships |
| VAL-H12 — privacy lifecycle for immutable event data (classification, retention, erasure) | Open — required **before production data is admitted** | Production-readiness review |
| VAL-M01 — architecture-test lane not wired into build; lane currently fails compilation | Open — repair the lane, then wire an explicit CI gate or correct the "build-time" wording | Next test-infrastructure story |
| VAL-M04 — RFC 9457 is an acceptance contract, not proven by default `AddProblemDetails` | Wording corrected in this update; conformance tests still owed | Story 4.9 R2/R5 acceptance |

## Project Context Analysis

### Requirements Overview

**Functional Requirements (25 FRs across 7 feature groups):**

| Feature group | FRs | Architectural meaning |
|---|---|---|
| 4.1 Work Item Aggregate & State | FR-1–5 | A single event-sourced aggregate root owning obligation, executor binding, unit-tagged burn-down, schedule, status, parent/children refs, await-conditions; everything else is a Reference Value Object (correlation ID). |
| 4.2 Lifecycle State Machine & Domain Events | FR-6–10 | A pure, explicit state machine (9 statuses); each transition a past-tense raw-act Domain Event; illegal transitions are `IRejectionEvent` domain rejections, not exceptions; completion = `Remaining 0`. The success-event catalog contains 15 events after the additive FR-21 correction. |
| 4.3 Effort Burn-Down & Recursive Roll-Up | FR-11–13 | Eventually-consistent, idempotent Roll-Up projection (`rolled = own + Σ rolled(children)`) on substrate projection infra; per-Unit subtotals (no cross-Unit coercion); acyclic single-parent single-tenant tree, bounded depth. |
| 4.4 Suspend / Resume Saga | FR-14–16 | Durable saga; `Handle` is clock-free — date/timer and external resumes enter as commands from adapters; resume keyed to an Await-Condition and idempotent; child-completion + date native, external signal via correlation-key port (deferred adapter). |
| 4.5 Executor Binding ("everything is a Party") | FR-17–19 | One value object `PartyId + Channel + AuthorityLevel`; assign/reassign/handoff = one uniform operation; push+pull coexist with single-claim-wins; AuthorityLevel carried-not-enforced in v1. |
| 4.6 Thin-Core Boundaries & Module Ports | FR-20–23 | "What's next" query projection; correlation-ID references to Parties/Conversations/EventStore/Tenants/Commons; additive `LinkConversation`/`ConversationLinked` support; ports `IExpectationResolver` (no-LLM impl) + `IExecutorRouter` (abstraction only); a written boundary decision record is a v1 deliverable. |
| 4.7 Platform-Hosted Runtime Test Harness | FR-24–25 | Canonical minimal EventStore domain-service host in Works; platform-owned Aspire topology and ServiceDefaults; Tier-1 pure tests; integration tests use substrate fakes/platform topology only at real boundaries. No production adapters. |

**Non-Functional Requirements (architecture drivers):**

- **Tenant isolation — mandatory, every layer**: identity, state keys, projection keys, queries, logs (`{tenant}:{domain}:{aggregateId}`); query-side authorization/result filtering required *in addition to* command-side checks. Negative-path tests for both cross-tenant and query-side paths.
- **Event-sourcing invariants**: persist-then-publish; `Handle(...)` pure → returns domain results/events; `Apply(...)` mutates only in-memory state; rejections are events, infra failures are exceptions/dead-letter; Works returns payloads only (EventStore owns envelope metadata).
- **Concurrency**: single-writer / optimistic-concurrency per Work Item; concurrent conflicting commands (e.g. two claims) → one success, rest domain-rejected; no lost updates. (Mechanism is an architecture decision; behavior is a v1 requirement.)
- **Projections rebuildable**: Roll-Up and "what's next" derive purely from event streams; replayable from scratch; hold no authoritative state. Readers remain on the prior generation until atomic Commit; ordinary projection delivery is quiesced or platform-fenced during shared inventory capture through Commit, then catches up.
- **Domain purity**: domain assembly takes no infra and no LLM/cost/routing dependency; `Handle` reads no clock/external system.
- **Observability & privacy**: structured logging only — never log payloads, personal data, secrets, or full command bodies; errors via ProblemDetails/RFC 9457 with correlation/tenant context.
- **Performance (qualitative for v1)**: incremental projection updates (no whole-stream re-read per query); no numeric budgets pinned — acceptance is build-signal based (SM-1…SM-5).

**Scale & Complexity:**

- Primary domain: **backend / event-sourced .NET 10 domain library + minimal EventStore domain-service host** (headless kernel; no Works-owned Aspire host and no v1 UI or channel adapters).
- Complexity level: **medium overall, high architectural rigor** — small public surface deliberately constrained by enterprise-grade substrate invariants; counter-metrics SM-C1 ("don't grow the kernel") and SM-C2 ("don't over-fit to deferred themes") are explicit guardrails against accidental scope.
- Estimated architectural components (ecosystem package layout): **Contracts** (events/commands/value objects) · **Server** (aggregate + handlers + ports + no-LLM resolver) · **Projections** (Roll-Up + "what's next") · minimal **domain-service executable** · **Testing** (fakes/builders). Aspire topology and ServiceDefaults are external platform concerns.

### Technical Constraints & Dependencies

**Inherited substrate (hard constraints, not open design space):**
- .NET 10 — SDK version pinned by `global.json` (`rollForward: latestPatch`); C# nullable + implicit usings + warnings-as-errors; central NuGet package management (`Directory.Packages.props`). Version authority is **AD-19** — the build files, never this document.
- **Dapr is the only permitted infrastructure abstraction** in domain services — no direct Redis/PostgreSQL/Cosmos/broker clients in Contracts/Client/domain.
- **EventStore** foundation: canonical `{tenant}:{domain}:{aggregateId}` identity; persist-then-publish; EventStore owns envelope metadata.
- **`Hexalith.PolymorphicSerializations`** for event/command payloads; `System.Text.Json` conventions.
- **Additive, serialization-tolerant schema evolution only** — no `V2` event types; every event ever produced must remain backward-compatibly deserializable.
- Naming: file-scoped namespaces under `Hexalith.*`; commands imperative (no `Command` suffix); events past-tense (no `Event` suffix); prefer sealed records; `Async` suffix; `_camelCase` fields; `I`-prefixed interfaces.
- Repo discipline: umbrella repo, root submodules only (never `--recursive`); Works holds **domain-centric code plus the canonical minimal EventStore domain-service host**. It ships no `*.AppHost`, `*.Aspire`, or `*.ServiceDefaults` project; topology is composed by the platform host repository **`Hexalith.Platform`** (AD-20).

**Sibling-module dependencies (referenced by correlation ID, never copied):**
- Identity → `Hexalith.Parties` (`PartyId`) · Dialogue → `Hexalith.Conversations` (`ConversationCorrelationId`) · Persistence/events → `Hexalith.EventStore` · Isolation → `Hexalith.Tenants` (`TenantId`) · IDs → `Hexalith.Commons`.

**Open questions explicitly deferred to this architecture phase (PRD §13):**
1. Aggregate-ID derivation (Commons helper; caller- vs system-assigned).
2. Priority representation (enum vs numeric band) backing FR-4/FR-20.
3. Optimistic-concurrency mechanism (ETag/version) realizing §9 concurrency + single-claim-wins.
4. Timer/scheduler adapter raising date/timer resume commands (FR-15) and its delivery guarantees.
5. Projection rebuild/replay operational story for Roll-Up and "what's next".
6. Validation domains (`ProgressReported` deltas, Unit immutability, Due-Date/TTL config source).

### Cross-Cutting Concerns Identified

- **Tenant isolation** — enforced at aggregate identity, state/projection keys, queries (incl. result filtering), and logs.
- **Concurrency & idempotency** — single-writer/optimistic per aggregate; single-claim-wins; resume idempotent against state; substrate offset dedup so replays don't double-count.
- **Projection consistency & rebuild** — eventual consistency, incremental updates, full replayability; no authoritative read-side state.
- **Domain purity via ports** — `IExpectationResolver` / `IExecutorRouter` keep LLM/cost/routing in adapters; clock/external triggers enter as commands.
- **Additive schema evolution & serialization** — `PolymorphicSerializations`; tolerant deserialization; no breaking event changes.
- **Observability & privacy** — structured logging, no sensitive payloads; RFC 9457 ProblemDetails with correlation/tenant context.
- **Seam preservation (designed-for, not built)** — ports, raw-act audit model, cost-ready burn-down, AuthorityLevel field — preserved without speculative machinery (SM-C2).

### Architectural Risk & Assumption Stress-Test (pre-decision)

_Derived from an advanced-elicitation pass (Assumption Audit · Pre-mortem · Cascading-Failure · Second-Order · Inversion) and a four-voice architect roundtable (Winston / Amelia / Murat / Dr. Quinn). These are constraints the architecture must satisfy and risks it must carry into the decisions that follow._

**Load-bearing invariants (honor when deciding):**

1. **Roll-Up = idempotent per-descendant accounting keyed by EventStore envelope `SequenceNumber`, never additive deltas and never clock/arrival order.** The projection stores each descendant's latest own contribution as `(descendantId → lastObservedEnvelopeSequence, ownContribution)`; lower-sequence (stale/replayed) writes are ignored. Normative invariant (AD-06, flattened form): `rolled-Remaining(node) = own-Remaining(node) + Σ last-known descendant own-Remaining` — each slot written only from its own descendant's stream, so LWW compares within one sequence space. Out-of-order and at-least-once redelivery tests are mandatory, over trees ≥ 3 levels (SM-2).
2. **Concurrency is two separate worlds.** Write-path: EventStore's single-writer actor **turn lock** serializes same-item commands — the normal loser is handled against committed state and receives an observable `IRejectionEvent`; the **ETag-backed** atomic actor-state save is the fallback conflict/recovery path (AD-08). Read-path: projections take **no locks**; they reconcile idempotently and order-tolerantly. Do not put a version check on a projection.
3. **Authority split on the numbers (type-separated).** Own-Remaining and Status (including the `Done = Remaining 0 → Completed` transition) are **aggregate-authoritative and synchronous**; only **rolled-Remaining is an eventually-consistent projection** — a projection never flips status. The two numbers must not share a type, field name, or serialized shape, so no consumer can gate control flow on the eventual value.
4. **A reactor / process-manager (event → command) is a real v1 component — and it lives OUTSIDE the kernel.** The kernel emits events and accepts commands and references no adapter. The reactor drives the two inherently multi-aggregate, non-atomic flows: child-completion → parent-resume (FR-15) and cascade cancel/expire → descendants (FR-10). Its hard contract is **at-least-once delivery + idempotent target commands + a checkpoint** so cascade is resumable (driven off a re-readable "descendants still needing cancel" projection, not an in-memory loop).
5. **The reactor is mechanical — no shadow kernel (collapses SM-C1 + SM-C2 into one leverage point).** The reactor contains no conditional a pure `Handle` could not have produced; every *decision* round-trips through the aggregate. This single falsifiable rule is the highest-leverage defense against kernel growth, because the kernel will be tempted to grow *at the reactor* and call it "just orchestration."
6. **Cascade correctness is a function of the per-state cancel/expire transition table** (cancelling an already-`Completed` child is a real domain decision, defined for each of the 9 states before the reactor can safely cascade). Idempotency on the command-*emit* side is a distinct problem from idempotency on the projection side; both are required.
7. **Time is a domain invariant currently delegated to infrastructure.** A clock-free `Handle` cannot distinguish "expired" from "not yet told it expired" — expiry is a property of *the timer adapter having fired*. v1 decision to record explicitly: **deadlines are advisory-until-fired**; the kernel may hold a "live" item that is, in reality, overdue, and no v1 query detects this without the timer. Re-validate against Theme 5 (cost-aware scheduling) before building, since retrofitting a logical clock is a redesign, not a patch.
8. **The timer/scheduler adapter is a partial SPOF for date-based resumes** and must be **durable + reconciliation-on-recovery** (at-least-once + idempotent resume; on restart, re-scan `DateReached` await-conditions for firings lost before they were recorded).
9. **Clock-free purity needs a mechanical test gate**, not a convention: no `DateTime.Now/UtcNow`, `DateTimeOffset.Now`, `Stopwatch`, `ITimer`, RNG, or I/O in `Works.Server` / `Works.Projections` (and the reactor's `react(event) → command[]` is **also pure**). Expiry/TTL enters only as a command.
10. **Tenant isolation in the roll-up requires more than key-prefixing.** Key-prefixing protects storage access, not tree traversal: parent/child references must be **tenant-closed**, and the roll-up must **assert tenant-equality at every hop** (turning a silent cross-tenant leak into a loud failure). Rebuild is per-tenant and reader-safe; shared rebuild delivery is quiesced or platform-fenced during capture-through-Commit.
11. **Projection rebuild must be reader-available and atomically visible at Commit.** Independent aggregate projections use EventStore's pausable checkpoint rebuild. Relationship-aware projections use the bounded `/project/rebuild/shared/v1` lifecycle over a sealed tenant inventory. Ordinary delivery is quiesced from inventory capture through Commit, or excluded by an equivalent platform fence, and catches up afterward. Readers stay on the prior generation until promotion and never observe staged or partial state.

**Inversion guardrails (anti-patterns that violate the success metrics):** additive roll-up totals (SM-2); timer/cascade/ranking/cost logic inside the domain assembly (SM-C1); infra/LLM type references from Contracts/Server (SM-4); clock/RNG in `Handle` *or* the reactor (SM-1); `switch (binding.Kind)` anywhere (SM-3); key-prefix-only tenant reads (isolation).

**Risk register (to carry into design + test strategy):**

| ID | Risk | Primary gate |
|---|---|---|
| RR-1 | Stale-write / out-of-order roll-up corruption (silent) | Property test (FsCheck): any permutation + duplication of a child-event multiset converges to identical state — fixed seed, build-gate |
| RR-2 | Mid-cascade / mid-reactor-step crash inconsistency | Chaos / crash-injection at each step boundary in the Aspire host (integration-gate); add **SM-1b: mid-reactor-step crash converges** |
| RR-3 | Double-claim on a Queued item | Deterministic ETag-conflict test (same persisted actor state → one atomic save commits, loser gets observable rejection event after retry); not a thread-race |
| RR-4 | Cross-tenant roll-up leak via recursive traversal | Mutation-validated negative tests (delete the isolation check → test goes red); seed colliding IDs in the other tenant |
| RR-5 | Purity / clock / identity / no-branch erosion over time | Architecture fitness functions (banned-symbol analyzer + no-branch-on-executor-kind), run every build |
| RR-6 | Serialization back-compat ("no V2 / tolerant evolution") unfalsifiable | Golden-payload corpus + round-trip contract test; start the corpus in v1 even near-empty |

**Test-type taxonomy (set up front):** *unit* (pure `Handle`/`Apply`) · *property* (roll-up convergence, claim idempotence) · *architecture-fitness* (SM-3 zero-branching, SM-4 purity, clock-free) · *contract* (serialization back-compat; Dapr pub/sub envelope) · *integration/topology* (platform-owned Aspire host: persist-then-publish seam) · *chaos* (crash-at-step-boundary; delivery-fenced shared rebuild). SM-1/SM-2 are scenario acceptance tests; SM-3/SM-4 are continuous fitness functions; **SM-C2 is a review-gate, not a build-gate** (you cannot unit-test "we didn't build too much").

**Open decisions carried into the decision steps (record, resolve later):**

- **D-1 Reactor placement** — confirmed direction: *outside the kernel* (Aspire host / adapter layer); the kernel references no reactor/timer/external adapter. (Strong roundtable consensus; pending user confirmation.)
- **D-2 Claim cardinality** — is "claim a Queued item" a **single-aggregate** operation under one optimistic-concurrency check (clean deterministic loser), or does it also write a separate queue/index aggregate (re-introduces multi-aggregate non-atomicity → inherits RR-2 crash semantics)?
- **D-3 Deadline semantics & AuthorityLevel** — is a deadline a **domain truth** (then design the logical-clock seam now) or an **adapter event** (then accept "advisory-until-fired" in writing)? Relatedly: does **any v1 behavior branch on `AuthorityLevel`**? If not, state explicitly that it is carried additively for deferred themes (SM-C2 honesty).
- **D-4 Unverified substrate premise** — confirm the **Dapr per-aggregate ordering + at-least-once** guarantees for the chosen broker before the convergence/idempotency proofs are meaningful.

## Starter Template Evaluation

### Primary Technology Domain

Backend / event-sourced **.NET 10 domain library + minimal EventStore domain-service host**
(headless kernel; no Works-owned Aspire host, v1 UI, or channel adapters). The stack is fully
dictated by the Hexalith ecosystem — this step selects the shared domain-service SDK boundary,
not a greenfield boilerplate. No open language/framework/database/cloud decisions exist.

### Starter Options Considered

- **`Hexalith.EventStore.DomainService` (selected runtime boundary)** — the authoritative domain-module
  SDK supplies standard service defaults, health/telemetry, aggregate/query/projection discovery, runtime
  activation, and canonical endpoints. Works consumes this SDK through a minimal executable.
- **Hexalith.Parties (domain-layout pattern donor only)** — useful for Contracts/Server/Testing
  conventions, but its historical AppHost/ServiceDefaults layout is not copied into Works.
- **`dotnet new` from scratch** — rejected; re-derives the build infrastructure, packaging,
  analyzers, and conventions that `Hexalith.Builds` already provides.
- **Third-party boilerplate** — rejected; irrelevant to a pinned .NET 10 / Dapr / EventStore
  ecosystem.

### Selected Starter: Hexalith canonical domain-module layout via `Hexalith.Builds` + `Hexalith.EventStore.DomainService`

**Rationale for Selection:**
Works uses the ecosystem's shared MSBuild infrastructure (`Hexalith.Builds`) and consumes
`Hexalith.EventStore.DomainService` for its runtime boundary. Domain projects retain the familiar
Contracts/Server/Projections/Testing layout, while the runnable `Hexalith.Works` executable stays at
the canonical minimal host seam. **`Hexalith.Platform`** (AD-20) owns Aspire topology and
ServiceDefaults. This preserves machine-checkable dependency direction and central package management
without copying generic platform plumbing into the domain module.

**v1 project set (create these):**

| Project | Role | In v1? |
|---|---|---|
| `Hexalith.Works.Contracts` | Events, commands, value objects (ExecutorBinding, effort Meter, AwaitCondition), Reference Value Objects including additive Conversation linking, port interfaces — low-dependency, no infra | ✅ |
| `Hexalith.Works.Server` | Aggregate `Handle`/`Apply`, lifecycle state machine, no-LLM `IExpectationResolver` impl, domain services | ✅ |
| `Hexalith.Works.Projections` | Roll-Up (per-child envelope-position accounting) + "what's next" query | ✅ |
| `Hexalith.Works.Reactor` | Pure event→command translators outside the kernel: child-completion→resume (`ChildCompletionResumeTranslator`) and terminal cascade→descendant cancel/expire (`TerminalCascadeTranslator`); references `Contracts` only, no dispatch/clock/infra. Realized in Epic 3 (resolves D-1). | ✅ |
| `Hexalith.Works.Testing` | Fakes/builders: `InMemoryEventLog`, `ReorderingProjectionDriver`, `RollUpProjectionBuilder` (tenant-required) | ✅ |
| `Hexalith.Works` | Minimal EventStore domain-service executable: registers the Works domain assembly with `AddEventStoreDomainService(...)` and maps it with `UseEventStoreDomainService()`. Domain-specific handlers are discovered through SDK contracts; generic Dapr, projection/query, subscription, health, and telemetry plumbing remains platform-owned. | ✅ |
| `Hexalith.Works.AppHost` + `Hexalith.Works.ServiceDefaults` | Prohibited in the Works domain module. The replacement Aspire topology and ServiceDefaults live in `Hexalith.Platform` (AD-20). | ❌ |
| `.Client` | Consumer-facing integration | ◐ minimal/optional |
| `.UI` / `.Mcp` / `.AdminPortal` / `.ConsumerPortal` / `.Picker` / `.Security` | Channel & surface adapters | ❌ Themes 3–6 |

**Reactor placement note (ties to D-1):** the pure mechanical translators remain domain-focused and
outside the aggregate kernel; `Server`/`Projections` stay clock-free and infra-free. Runtime dispatch,
checkpointing, reminders, subscriptions, and recovery are expressed through EventStore platform seams and
composed by the platform host. Story 4.9 migrates the historical Works-owned runtime implementation to this
boundary without changing the translators or moving decisions out of `Handle`.

**Repo scaffolding (umbrella root, mirror siblings):** `global.json` (SDK pinned),
`Directory.Build.props`/`.targets`, `Directory.Packages.props`, `Directory.Solution.props`/`.targets`,
`Hexalith.Works.slnx`, `package.json` + `release.config.cjs`
(semantic-release + commitlint), `MSBuild.rsp`; `src/`, `tests/`. Shared deps come from
the root submodules (`Hexalith.Builds`, `Hexalith.EventStore`, `Hexalith.Commons`,
`Hexalith.PolymorphicSerializations`, `Hexalith.Parties`, `Hexalith.Conversations`,
`Hexalith.Tenants`).

**Inherited stack versions — historical snapshot (2026-06-14); not implementation pins.** The
authoritative pins are `global.json` and `references/Hexalith.Builds/Props/Directory.Packages.props`
(**AD-19**). The **Dapr runtime** pin is a distinct field owned by the platform host (AD-20) and is
never inferred from the Dapr .NET SDK package version; the supported Aspire family is the single
13.5.x line (AD-20). The June table is retained as historical evidence only:

| Component | June 2026 snapshot (historical) | Note |
|---|---|---|
| .NET SDK | `10.0.301`, `rollForward: latestPatch` | superseded — see `global.json` |
| Dapr .NET SDK | `1.18.4` | superseded — see central packages; runtime pin is platform-owned |
| .NET Aspire | Platform-owned pin | AD-20: single 13.5.x family owned by `Hexalith.Platform` |
| xUnit | v3 `3.2.2` + Microsoft.Testing.Platform | superseded — current v3 line defaults to MTP v2 |
| Serialization | `Hexalith.PolymorphicSerializations` | event/command payloads (unchanged) |
| Fluent UI Blazor | `5.0.0-rc.3` | superseded; still **unused in v1** (headless) |

Versions are **ecosystem-pinned by policy** ("do not casually upgrade"); Works aligns to the
authoritative central files (AD-19), never to version numbers embedded in prose.

**Migration note (approved 2026-09-05):** the original scaffold created Works-owned AppHost and
ServiceDefaults projects before the stricter baseline was adopted. Story 4.9 removes those projects only
after the replacement platform topology proves equivalent runtime behavior. Story 1.1 remains historical
evidence; this section is the current target architecture.

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (block implementation):**
- A1 Aggregate-ID assigned at the edge (Commons), not inside `Handle`.
- A5 Roll-Up per-child envelope-position LWW data model.
- B1 Single-aggregate claim under EventStore-owned concurrency (turn lock + ETag fallback, AD-08).
- B2 No reliance on pub/sub ordering (idempotent, order-tolerant projections).
- C1 Reactor lives outside the kernel; C2 Dapr actor reminders for date resumes.

**Important Decisions (shape the architecture):**
- A2 Priority = ordered enum; A3 Unit immutable; A4 cost-ready `Meter`.
- B3 Authority split (synchronous own-Remaining/Status vs eventual rolled-Remaining).
- C3 Deadlines advisory-until-fired; C4 await-condition discriminated set.
- D2 Tenant-equality asserted at every roll-up hop; E1 per-tenant, reader-safe rebuild with delivery quiescence/fencing.

**Deferred Decisions (Themes 3–6, seams only):**
- AI-inferred Expectation / magic links / NL parsing (Theme 3) — seam: `IExpectationResolver`, `ExternalSignal` await-kind, Channel.
- Executor routing/escalation (Theme 4) — seam: `IExecutorRouter` port, push/pull states, `AuthorityLevel` field, additive assignment events.
- Cost meter + spend governance (Theme 5) — seam: cost-ready `Meter` + reusable roll-up.
- Security hardening (Theme 6) — seam: raw-act event model, idempotency, `AuthorityLevel`.

### Data Architecture

- **Event-sourced on `Hexalith.EventStore`** (pre-decided). Canonical identity **`{tenant}:work:{workItemId}`**; persist-then-publish; EventStore owns envelope metadata; payloads via `Hexalith.PolymorphicSerializations`; additive/serialization-tolerant evolution (no `V2`).
- **A1 — Aggregate-ID derivation:** assigned at the command-creation edge via **`Hexalith.Commons`** ID helper and passed into `CreateWorkItem`; `Handle` never generates IDs. *Rationale:* keeps `Handle` pure, makes replay deterministic, enables domain-level idempotent create (client retry → same ID; the transport idempotency contract remains open — VAL-H10). *Affects:* Contracts (command shape), all test builders.
- **A4 — Burn-Down:** `Meter(Unit, Estimated, Done)` with derived `Remaining` (never < 0); one **Effort** meter in v1; a parallel **Cost** meter reuses the identical type (Theme 5). *Affects:* Contracts, Server, Projections.
- **A3 — Unit:** per-item value object, **immutable after first estimate**; `ProgressReported`/`ReEstimated` must carry the same Unit or are rejected; mixed-Unit roll-up exposes **per-Unit subtotals**, never a coerced single figure.
- **A2 — Priority:** small **ordered enum** (`Critical/High/Normal/Low`), additive-tolerant; backs "what's next" ordering (Priority → Due Date → deterministic identity order; none sorts last — VAL-H03 resolved 2026-09-06). *Rationale:* YAGNI vs numeric routing bands (Theme 4, SM-C2).
- **A5 — Roll-Up projection (AD-06, flattened form):** one LWW slot per **descendant**, keyed by **`(descendantId, descendantEventSequence)`** and valued with that descendant's **own contribution** per Unit; `rolled = own + Σ descendant slots`; **idempotent + order-tolerant** (stale/lower-sequence writes ignored; replays don't double-count; each slot written only from its own descendant's stream). Built on EventStore projection infra (CachingProjectionActor, ETag actors, notifiers). Live ancestor delivery is the **registry-backed fan-out (AD-22)** — ancestors resolved from the Work-Tree Registry read model with a freshness witness. *Validates SM-2; mitigates RR-1.*
- **B3 — Consistency split (type-separated):** own-Remaining + Status are **aggregate-authoritative and synchronous** (including `Done = Remaining 0 → Completed`); **rolled-Remaining is an eventually-consistent projection** with a distinct type/field/serialized shape so no consumer can gate control flow on it.

### Authentication & Security

- **D2 — Tenant isolation (mandatory, every layer):** identity/state/projection keys, queries, logs all tenant-scoped; **query-side authorization is a distinct control from key-prefixing**; the roll-up **asserts tenant-equality at every hop** (parent/child references are tenant-closed); single-tenant tree enforced through the **Work-Tree Registry attachment protocol (AD-21)**. *Negative-path tests (mutation-validated) required (RR-4).*
- **D1 — `AuthorityLevel`:** ordered set `{Read, Contribute, Coordinate, Administer}` **carried on the binding but not enforced in v1 — no v1 behavior branches on it.** Recorded explicitly as an additive seam for Themes 4/6 (SM-C2 honesty); additive-tolerant so behavior can attach later without a `V2`.
- **Identity provenance (AD-23):** external callers authenticate at the `Hexalith.Platform` ingress (OIDC); the authoritative tenant/actor derive from verified claims via **Hexalith.Tenants** membership. Payload/envelope `TenantId`/`UserId` are assertions — a mismatch is denied before dispatch, persistence, or query, without disclosing tenant existence. Internal originators (reactor, reminders, replay, recovery) use workload identity plus an audited tenant-delegation context. Minimum authorization: commands = tenant member; queries = member + result filtering; admin/rebuild/replay = audited platform-operator role. Enforcement and its negative tests are Story 4.9 acceptance; production ingress is prohibited until the contract is live. Theme 6 covers step-up/signed-link hardening only — not this baseline.
- **Trusted origin (AD-24):** production routes accept traffic only from authenticated sidecar/workload identities in the declared trust domain/namespace, behind deny-by-default access control and network policy; broker access uses TLS + producer/consumer ACLs; exclusive originators — only EventStore publishes `work.events`, reminder callbacks only from the Works actor identity, replay/rebuild only from the operations identity. Negative migration tests are part of the Story 4.9 lane.
- Identity value objects themselves are referenced from `Hexalith.Parties`/`Hexalith.Tenants` (correlation IDs), never re-implemented.

### API & Communication Patterns

- **Public surface = the domain contract** (events, commands, value objects, ports) — no production channel adapter in v1. Errors via **ProblemDetails / RFC 9457** with correlation/tenant context — an acceptance contract proven by conformance tests, not assumed from the default `AddProblemDetails` registration (open register VAL-M04); domain rejections are `IRejectionEvent` (never exceptions).
- **B1 — Concurrency & claim (AD-08):** commands carry no expected-version or ETag input. EventStore serializes commands against one Work Item through its **single-writer actor turn lock**; the Dapr state-store **ETag** on the atomic actor-state save is the fallback conflict path. **Claim is a single-aggregate operation** on the `WorkItem`; the claimable pool is a **read projection**, not an authoritative queue aggregate. Two racing claims → exactly one commits; the normal loser is turn-serialized and handled against committed state, producing the existing `WorkItemTransitionRejected(InProgress, "Claim")`; an actual state-store conflict is retried from fresh state within the bounded policy. Retry exhaustion surfaces an infrastructure `ConcurrencyConflict` with no loser append/publication/dead-letter effect. *Resolves D-2; avoids multi-aggregate non-atomicity.*
- **B2 — Delivery posture (AD-09):** Dapr pub/sub guarantees **at-least-once**; ordering is component- and configuration-specific and never a portable contract — Works does **not** rely on broker ordering. Write-path ordering comes from the single-writer actor; read-path correctness comes from idempotent, order-tolerant projections (A5) + substrate offset dedup. *Resolves D-4.*
- **C1 — Reactor / process-manager:** its domain translation lives **outside the aggregate kernel** and remains mechanical **event→command translation only — no shadow-kernel logic** (every decision round-trips through a pure `Handle`). Runtime delivery/checkpoint/subscription plumbing is supplied by EventStore platform seams and composed by the platform host. Contract: at-least-once delivery + **idempotent target commands** + **checkpoint-driven, resumable cascade** (cascade reads a re-readable "descendants still needing cancel" projection, not an in-memory loop). *Resolves D-1; mitigates RR-2; the single highest-leverage SM-C1/SM-C2 guard.*
- **C4 — Await-condition & resume:** discriminated value `{ ChildCompleted(childId) | DateReached(instant) | ExternalSignal(correlationId) }`; a suspended item holds a **set** and resumes on **first match**; resume is **idempotent** (key no longer matching = no-op; duplicate = no-op). v1 satisfiers: child-completion (reactor), date (reminder, below), external (generic command; concrete adapter deferred to Theme 3).
- **Ports:** `IExpectationResolver` (no-LLM impl shipped) and `IExecutorRouter` (abstraction only, no impl wired). Domain references no LLM/cost/routing/infra type.

### Frontend Architecture

- **Not applicable in v1** — Works is a headless domain kernel; no production UI/channel adapters ship (UX `DESIGN.md`/`EXPERIENCE.md` design the Theme 3–6 horizon through `Hexalith.FrontComposer`, but v1 builds none of it). The kernel only keeps projections **SignalR-ready** (live-update friendly) without shipping a surface.

### Infrastructure & Deployment

- **C2 — Timer/scheduler adapter (AD-11):** **Dapr actor reminders via the Scheduler service** (Scheduler-backed by default since Dapr 1.15; the runtime pin is a distinct platform-owned field — AD-19/AD-20). A `WorkItem` parked on `DateReached` registers a **self-targeted, durable reminder**; on fire it raises an internal `ResumeWorkItem(date)` command — `Handle` never reads a clock. Registration persists across supported failover; **end-to-end durability is conditional** on Scheduler persistence/HA/backup and an explicit callback failure policy, bound in the platform lane (AD-20 R6). **Reconciliation-on-recovery** uses the registry/indexed pending-await discovery protocol — aggregate streams are the truth, the index is discovery only. The general Jobs API is *not* needed in v1 (cross-service scheduling is deferred). *Resolves OQ-4.*
- **C3 — Deadline semantics:** **adapter event, "advisory-until-fired"** — the kernel may hold a "live" item that is, in reality, overdue; no v1 query detects this without the timer firing. Recorded; **re-validate against Theme 5** (cost-aware scheduling) before that theme builds, since adding a logical clock later is a redesign.
- **Automatic expiry (AD-25):** every Work Item with a Due Date or applicable TTL policy holds one durable, tenant-inclusive **expiry reminder**, registered/cancelled/rescheduled mechanically from lifecycle events by the reminder adapter — never by the kernel. Firing emits `ExpireWorkItem` (idempotent against terminal state per the transition table); recovery uses the AD-11 reconciliation protocol. Due-Date/TTL policy values are **`Hexalith.Platform` host configuration**. *Resolves FR-10's trigger/ownership gap (VAL-H01) with FR-10 unchanged.*
- **E1 — Projection rebuild:** **per-tenant, reader-available, and atomically visible at Commit**. Independent aggregate projections use EventStore's pausable checkpoint rebuild. Relationship-aware Works projections use the bounded `/project/rebuild/shared/v1` Begin/Accumulate/Finalize/Stage/Commit lifecycle over a sealed tenant inventory. Ordinary projection delivery is quiesced from inventory capture through Commit, or excluded by an equivalent platform fence; it resumes and catches up afterward. Readers remain on the prior generation until promotion and never observe staged or partial state. *Resolves OQ-5 without promising uninterrupted projection delivery.*
- **E2 — Validation domains:** `ProgressReported` delta > 0 (zero/negative deltas rejected;
  `docs/lifecycle-transition-matrix.md` is authoritative) with `Remaining` clamped ≥ 0; `Estimated` ≥ 0; Unit immutable after first set; Due-Date/TTL sourced from **`Hexalith.Platform` host configuration** per work-type/tenant (AD-25 — the kernel never reads configuration). *Resolves OQ-6.*
- **Runtime host:** Works exposes the canonical minimal EventStore domain-service executable; the `Hexalith.Platform` AppHost + SDK-supplied ServiceDefaults (AD-20) compose manual and automated integration tests. Works ships no AppHost/Aspire/ServiceDefaults project and duplicates no platform health, telemetry, Dapr, query/projection, or subscription plumbing. **Clock-free purity + no-branch-on-executor-kind are enforced as build-time architecture fitness functions** (RR-5).

### Decision Impact Analysis

**Implementation sequence:**
1. **Scaffold** the module (step-3 layout) — precondition for any green build.
2. **Contracts** — value objects (`ExecutorBinding`, `Meter`, `AwaitCondition`, Reference Value Objects, Priority enum), additive v1 catalog (15 commands + 15 state-changing success events + 10 rejection events = 40 durable types), port interfaces, and rejection payloads without state-changing `(AggregateId, Sequence)` fields.
3. **Server** — `WorkItem` aggregate `Handle`/`Apply`, 9-state machine + per-state cancel/expire table, no-LLM `IExpectationResolver`, plus the **Work-Tree Registry aggregate (AD-21)**; EventStore owns atomic persistence (AD-08).
4. **Projections** — Roll-Up (per-child envelope-position LWW, delivered via registry-backed fan-out — AD-22) + "what's next"; tenant-equality assertions; rebuild support.
5. **Testing** — `InMemoryEventLog`, `ReorderingProjectionDriver`, `RollUpProjectionBuilder` (tenant-required); property/architecture-fitness gates.
6. **Platform runtime composition** — expose domain handlers through the EventStore SDK; wire reminders, cascade translation, and the Works service in the `Hexalith.Platform` AppHost (AD-20); run SM-1/SM-1b durability tests there.

**Cross-component dependencies:**
- A1 (ID at edge) gates every Contracts command shape **and** all test builders.
- A5 + B2 (per-child envelope-position, order-tolerant) use EventStore envelope `SequenceNumber` as the canonical persisted and projection-delivery position. State-changing Works payloads additionally carry `(AggregateId, Sequence)`, where payload `Sequence` is the state-changing ordinal; rejection payloads remain frozen without either field.
- B1/AD-08 (EventStore-owned concurrency) gates the aggregate concurrency contract — decide before writing `Handle`.
- C1/C2 (reactor + reminders outside the kernel) keep Server/Projections clock-free and infra-free — protects SM-C1/SM-4.
- D2/AD-15 (tenant-equality at every hop) couples Projections to the registry-backed tree authority (AD-21); AD-22's fan-out depends on the AD-21 registry read model.

## Implementation Patterns & Consistency Rules

### Pattern Categories Defined

**Critical conflict points identified:** ~12 areas where AI agents could make divergent choices.
Most generic web concerns (DB casing, REST routes, JSON wrappers) are **not applicable** (headless
event-sourced kernel) or **pre-locked** by `.editorconfig` + the ecosystem `project-context.md`.
The rules below are the **Works-specific** consistency contract; ecosystem rules (file-scoped
namespaces, sealed records, `_camelCase`, `Async` suffix, central package management) are inherited
verbatim and not restated.

### Naming Patterns

**Namespaces & files:** file-scoped namespaces matching folder path under `Hexalith.Works.*`
(`Hexalith.Works.Contracts`, `.Server`, `.Projections`, `.Testing`); one public type per file,
file named after the type.

**Commands** — imperative, **no `Command` suffix**, sealed records:
`CreateWorkItem`, `AssignWorkItem`, `QueueWorkItem`, `ClaimWorkItem`, `ReportProgress`, `ReEstimate`,
`RescheduleWorkItem`, `SpawnChild`, `SuspendWorkItem`, `ResumeWorkItem`, `CompleteWorkItem`,
`CancelWorkItem`, `RejectWorkItem`, `ExpireWorkItem`, `LinkConversation`.

**Events** — past-tense, **no `Event` suffix**, sealed records (the additive v1 success catalog, 15):
`WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `ProgressReported`,
`ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`, `WorkItemResumed`,
`WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`,
`ConversationLinked`. Rejection events implement `IRejectionEvent`; the additive link operation adds
`WorkItemConversationLinkRejected` for a conflicting relink. Infrastructure retry exhaustion remains
`ConcurrencyConflict` and is not a domain event.

**Value objects:** `WorkItemId`, `ExecutorBinding`, `Channel`, `AuthorityLevel`, `Priority`,
`Unit`, `Meter`, `AwaitCondition` (+ cases `ChildCompleted`/`DateReached`/`ExternalSignal`),
Reference Value Objects `PartyId`/`ConversationCorrelationId`/`TenantId`.

**Identity & derived keys** — the logical aggregate identity is canonical `{tenant}:work:{workItemId}`;
no agent invents a parallel identity scheme. The **universal-derivation claim is retired**: the live
system legitimately uses a shared `work.events` topic, generation-based projection keys, hashed
reminder identities, and notifier groups — a per-namespace ownership table replaces this rule during
Story 4.9 (open register VAL-H09). Reminder names are a deterministic, **tenant-inclusive** function
of `(tenant, workItemId, awaitConditionKey)` so they are idempotently (re)registerable and
collision-free across tenants (AD-25).

### Structure Patterns

**Package boundaries & dependency direction (machine-checkable, AD-18):**
`Server → Contracts`; `Projections → Contracts` (the read side references `Contracts` only — never
`Server`); `Reactor → Contracts`. `Contracts` stays low-dependency — **no infra, no LLM**.
`Testing` references the above. The minimal
`Hexalith.Works` EventStore domain-service executable and the pure reactor translators reference
inward; `Hexalith.Platform` and the EventStore SDK own topology, ServiceDefaults, delivery,
scheduling, and infrastructure (AD-20). **Nothing in `Server`/`Projections` references an adapter, a clock, Dapr, or an LLM
type, and Works ships no AppHost/Aspire/ServiceDefaults project.**

**Tests:** in `Hexalith.Works.Testing` (reusable fakes/builders) and per-project `tests/` (xUnit
**v3** + Shouldly + NSubstitute). Tier-1 (`Handle`/`Apply`, projection handlers, validators) is
**pure** — no Dapr/Aspire/network/containers. Fakes/builders (`InMemoryEventLog`,
`ReorderingProjectionDriver`, `RollUpProjectionBuilder`) before any new test double.

### Format Patterns

**Event payload = the Raw Act, verbatim** — store reported values, never interpreted/derived ones
(interpretation is a Projection). State-changing raw-act payloads carry **`(AggregateId, Sequence)`**,
where `Sequence` is the state-changing ordinal. Frozen rejection payloads carry refusal context without
those fields. Order-tolerant projections receive the canonical EventStore envelope `SequenceNumber`;
the acting Party + timestamp also come from that envelope — **Works never populates envelope metadata.**

**`DomainResult` never mixes** success and rejection payloads. Rejections are events, not
exceptions; infrastructure failures are exceptions/dead-letter.

**Two sequence counters intentionally coexist.** EventStore envelope `SequenceNumber` is the canonical,
gapless persisted stream position used for reads, replay, and projection delivery; every persisted
success or `IRejectionEvent` consumes one. Works payload `Sequence` is only the ordinal of a
state-changing event and is copied into `WorkItemState.Sequence`. Applying a rejection is a no-op, so a
rejection at envelope position 1 followed by create at position 2 correctly yields
`WorkItemCreated.Sequence == 1`.

**Serialization:** `Hexalith.PolymorphicSerializations` for every event/command; `System.Text.Json`
conventions; additive, tolerant evolution only (**no `V2`**); start a **golden-payload corpus** in
v1 so back-compat is falsifiable.

**Burn-Down numbers:** `Meter(Unit, Estimated, Done)` → derived `Remaining` (clamped ≥ 0);
mixed-Unit roll-up → **per-Unit subtotals**, never a coerced single number. **Authoritative
own-Remaining and eventual rolled-Remaining are distinct types** — never interchangeable.

**Errors:** ProblemDetails / **RFC 9457** with correlation + tenant context.

### Communication Patterns

**Event flow is persist-then-publish.** `Handle(state, command) → events` (pure); projection/state
`Apply(...)` mutates only in-memory state. No publish before persistence succeeds.

**Reactor pattern (outside the kernel):** `react(event) → command[]` is **mechanical and pure** —
event-to-command translation only. **No conditional a pure `Handle` could not have produced**
(every decision round-trips through the aggregate). Targets are **idempotent**; cascade is
**checkpoint-driven** off a re-readable "descendants still needing cancel" projection.

**Await/resume:** a Suspended item holds a **set** of `AwaitCondition`s and resumes on **first
match**; `ResumeWorkItem(correlationKey)` is **idempotent** (no current match = no-op; duplicate =
no-op). Date resumes arrive only as commands from the reminder adapter — never a clock read.

**Executor binding ("everything is a Party"):** assign/reassign/handoff/claim use the identical
command path; **zero branching on executor kind** — no `switch (binding.Kind)` / `if channel ==`
anywhere in the domain. The only variation is field values on `ExecutorBinding`.

**Reference, never copy:** identities/dialogue/persistence/isolation/IDs are correlation IDs
resolved on demand from the owning sibling module. LLM/cost/routing live behind ports
(`IExpectationResolver`, `IExecutorRouter`), never in the domain.

### Process Patterns

**Domain purity:** `Handle` and `Apply` (and the reactor) read **no clock, no RNG, no I/O, no
external system**; IDs are supplied at the edge (Commons). Enforced as a build-time fitness
function (banned-symbol analyzer over `Server`/`Projections`).

**Concurrency & idempotency (AD-08):** writes are serialized per aggregate by EventStore's actor
**turn lock** (the normal loser is handled against committed state → domain `IRejectionEvent`); the
**ETag-backed** atomic actor-state save is the fallback conflict path (conflict → retry/re-handle;
exhaustion → infrastructure `ConcurrencyConflict`); reads/projections take **no locks** and are
**idempotent + order-tolerant** (per-child envelope-position LWW + offset dedup).
Single-claim-wins is a single-aggregate operation.

**Tenant scoping (every layer):** every command, query, key, projection, and log is tenant-scoped;
**query-side authorization is enforced in addition to key-prefixing**; the roll-up asserts
tenant-equality at every hop. Negative-path tests are mandatory.

**Logging/privacy:** structured logging only — **never** log event payloads, personal data,
secrets, raw tokens, or full command bodies.

### Enforcement Guidelines

**All AI agents MUST:**
- Keep `Handle`/`Apply`/reactor pure (no clock/RNG/I/O); take IDs as input.
- Treat EventStore envelope `SequenceNumber` as canonical for every persisted event and projection
  delivery. Carry payload `(AggregateId, Sequence)` only on state-changing Works events; payload
  `Sequence` is the state-changing ordinal. Keep frozen rejection payloads free of those fields, apply
  them as no-ops, and preserve them as persisted `IRejectionEvent`s.
- Use per-child envelope-position LWW for roll-up (never additive deltas); assert tenant-equality per hop.
- Never branch on executor kind; never reference a clock/Dapr/LLM/infra type from
  `Contracts`/`Server`/`Projections`.
- Register every new event/command with `PolymorphicSerializations`; evolve additively (no `V2`);
  extend the golden-payload corpus.

**Pattern enforcement (build gates):** architecture-fitness tests (purity/banned-symbols,
no-branch-on-kind, dependency-direction); property tests (roll-up convergence under
permutation+duplication); mutation-validated cross-tenant negative tests; golden-payload contract
tests. SM-C2 ("don't over-fit deferred themes") is a **review-gate**, not a build-gate. Pattern
changes are recorded in this document and `project-context.md`.

### Pattern Examples

**Good:**
- `public sealed record ProgressReported(WorkItemId AggregateId, long Sequence, decimal DoneDelta, Unit Unit, string? Note) : IDomainEvent;`
- Roll-up (AD-06 flattened form): `slots[descendantId] = (descendantSequence, descendantOwnContribution); rolled = own + slots.Values.Sum(...)` — stale `descendantSequence` ignored; each slot written only from that descendant's own stream.
- Claim race: same-actor requests are turn-serialized — the second is handled against committed
  `InProgress` state and persists the existing `WorkItemTransitionRejected` refusal; an injected
  state-store conflict instead retries from fresh state within the bounded policy (AD-08).

**Anti-patterns:**
- `var id = Guid.NewGuid();` inside `Handle` · `if (DateTime.UtcNow > dueDate)` inside the domain.
- `parentRemaining += delta;` in the roll-up.
- `switch (binding.Kind) { case Bot: … case Human: … }`.
- Reactor deciding "is this the last child?" itself instead of letting `Handle` decide.
- Logging the full command body or event payload.

## Project Structure & Boundaries

### Complete Project Directory Structure

The Works domain module is created **at the umbrella-repo root**, alongside the dependency
submodules (which are not modified). New files/dirs only:

```
works/                                        # umbrella repo root = Hexalith.Works
├── global.json                               # SDK pin (authoritative — AD-19), rollForward latestPatch, MTP runner
├── Directory.Build.props / .targets          # walk-up; import Hexalith.Builds shared config
├── Directory.Packages.props                  # central versions, aligned to current sibling pins
├── Directory.Solution.props / .targets
├── Hexalith.Works.slnx
├── package.json / release.config.cjs / commitlint.config.mjs   # semantic-release + commitlint
├── MSBuild.rsp
├── README.md / CHANGELOG.md
├── CLAUDE.md / AGENTS.md                      # (exist)
├── docs/
│   └── boundary-decision-record.md           # FR-23 tracked deliverable (owns-vs-references)
├── Hexalith.Builds/ … Hexalith.Conversations/  # (existing root submodules = dependencies)
│
├── src/
│   ├── Hexalith.Works.Contracts/             # KERNEL · low-dependency · no infra, no LLM
│   │   ├── Commands/                          # CreateWorkItem, AssignWorkItem, QueueWorkItem, ClaimWorkItem,
│   │   │                                      #   ReportProgress, ReEstimate, RescheduleWorkItem,
│   │   │                                      #   SpawnChild, SuspendWorkItem, ResumeWorkItem,
│   │   │                                      #   CompleteWorkItem, CancelWorkItem, RejectWorkItem,
│   │   │                                      #   ExpireWorkItem, LinkConversation
│   │   ├── Events/                            # 15 success events + 10 IRejectionEvent types
│   │   ├── ValueObjects/                      # WorkItemId, ExecutorBinding, Channel,
│   │   │                                      #   AuthorityLevel, Priority, Unit, Meter,
│   │   │                                      #   AwaitCondition{ChildCompleted|DateReached|ExternalSignal}
│   │   ├── State/                             # WorkItemState (rehydration target for Apply)
│   │   ├── Results/                           # DomainResult + rejection results
│   │   ├── Models/                            # read-model contracts: WhatsNextItem, RollUpView
│   │   └── Ports/                             # IExpectationResolver, IExecutorRouter, Expectation
│   │
│   ├── Hexalith.Works.Server/                 # KERNEL · domain behavior · PURE (no clock/RNG/IO)
│   │   ├── Aggregates/                        # WorkItem: Handle/Apply, 9-state machine,
│   │   │                                      #   per-state cancel/expire transition table, tree guard
│   │   ├── Resolvers/                         # no-LLM IExpectationResolver implementation
│   │   ├── Validation/                        # ProgressReported/ReEstimate/Unit validators
│   │   └── Registration/                      # DI/service registration extensions
│   │
│   ├── Hexalith.Works.Projections/            # KERNEL · read side · PURE handlers
│   │   ├── Strategies/                        # WorkItemRollUpProjection (per-(childId,childSequence) LWW),
│   │   │                                      #   WhatsNextQueueProjection, WhatsNextOrdering, WhatsNextQueryAuthorization
│   │   └── Models/                            # pure read-model strategy types + WhatsNextProjectionChange signal
│   │                                          # (shared rebuild is EventStore-owned; readers stay on the old
│   │                                          #  generation while delivery is quiesced/fenced until atomic commit)
│   │
│   ├── Hexalith.Works.Reactor/                # PURE (outside kernel) · mechanical event→command translators only
│   │   ├── ChildCompletionResumeTranslator.cs #   child-completion → ResumeWorkItem intent
│   │   ├── TerminalCascadeTranslator.cs       #   parent-terminal → descendant cancel/expire intents
│   │   ├── CascadeDescendant.cs / AwaitingParent.cs   #   pure value types for the translators
│   │   └── WorksReactorAssembly.cs            #   references Contracts only — no dispatch/clock/infra
│   │
│   └── Hexalith.Works/                         # canonical minimal EventStore domain-service executable
│       ├── Program.cs                         #   AddEventStoreDomainService / UseEventStoreDomainService
│       └── WorkItemEventStoreAggregate.cs     #   discovery wrapper delegating each command to the pure kernel
│
│   # No Works-owned AppHost, Aspire, ServiceDefaults, Dapr component, delivery, scheduling,
│   # query/projection plumbing, or subscription infrastructure. Story 4.9 migrates the historical
│   # adapter-edge implementation into Hexalith.Platform / the EventStore SDK per the AD-20
│   # migration matrix before removal here.
│
├── tests/
│   ├── Hexalith.Works.Testing/                # reusable: InMemoryEventLog, ReorderingProjectionDriver,
│   │                                          #   RollUpProjectionBuilder (tenant-required), WorkItemBuilder
│   ├── Hexalith.Works.UnitTests/              # Tier-1 pure: Handle/Apply, projection handlers, validators
│   ├── Hexalith.Works.PropertyTests/          # FsCheck: roll-up convergence (permutation+duplication)
│   ├── Hexalith.Works.ArchitectureTests/      # fitness: purity/banned-symbols, no-branch-on-kind, deps
│   └── Hexalith.Works.IntegrationTests/       # platform topology + chaos/crash-injection (SM-1/SM-1b)
```

`.Client` (consumer integration), `.UI`, `.Mcp`, portals, `.Security` are **deliberately absent**
in v1 (Themes 3–6; SM-C1/SM-C2).

### Architectural Boundaries

**Kernel vs platform (the load-bearing boundary):** the **kernel** = `Contracts` + `Server` +
`Projections` — pure, no clock/RNG/I/O, no Dapr/LLM type. `Reactor` contains pure mechanical
translations only, and `Hexalith.Works` is the canonical minimal EventStore domain-service host.
`Hexalith.Platform` and the EventStore SDK own delivery, checkpoints, subscriptions, reminders,
ServiceDefaults, Dapr components, and Aspire topology per the AD-20 migration matrix. **The kernel
references no adapter.**

**Dependency direction (machine-checkable, AD-18):** `Server → Contracts`; `Projections →
Contracts` (never `Server`); `Reactor → Contracts`; `Testing →` kernel; the minimal domain-service
host references the SDK and kernel; the external platform host composes published modules. No
cycles and no inward reference to an adapter.

**Sibling-module boundaries (referenced, never copied):** `EventStore` (persistence/events/actors/
projection infra) · `Parties` (`PartyId`) · `Conversations` (`ConversationCorrelationId`) · `Tenants`
(`TenantId`, isolation) · `Commons` (ID generation) · `PolymorphicSerializations` (payloads). All
via correlation IDs resolved on demand.

**Data boundaries:** the logical aggregate identity is `{tenant}:work:{id}`; concrete state,
projection, topic, and reminder keys follow the per-namespace ownership table being bound in Story
4.9 (open VAL-H09); roll-up asserts tenant-equality per hop; projections hold no authoritative state.

### Requirements to Structure Mapping

| FR group | Primary location |
|---|---|
| 4.1 Aggregate & State (FR-1–5) | `Contracts/ValueObjects` + `State`; `Server/Aggregates` |
| 4.2 Lifecycle & Events (FR-6–10) | `Contracts/Events` + `Commands`; `Server/Aggregates` (state machine + cancel/expire table) |
| 4.3 Roll-Up (FR-11–13) | `Projections/Handlers` + `Strategies` (registry-backed fan-out, AD-22); `Server/Aggregates` — Work-Tree Registry authority (AD-21) with the pure tree guard as assertion check |
| 4.4 Suspend/Resume Saga (FR-14–16) | `Contracts` (AwaitCondition, Suspend/Resume/SpawnChild); `Server/Aggregates`; `Reactor` (pure translation); platform runtime (delivery + timer) |
| 4.5 Executor Binding (FR-17–19) | `Contracts/ValueObjects` (ExecutorBinding/Channel/AuthorityLevel); `Server` |
| 4.6 Boundaries & Ports (FR-20–23) | `Contracts/Ports` + `Models`; `Contracts/Commands/LinkConversation`; `Contracts/Events/ConversationLinked`; `Server/Resolvers`; `Projections/Handlers` (WhatsNext); `docs/boundary-decision-record.md` |
| 4.7 Platform-Hosted Runtime (FR-24–25) | minimal `Hexalith.Works` domain-service executable + designated external platform topology + `tests/*` |

**Cross-cutting concerns:** tenant isolation → identity/keys/queries across `Server` + `Projections`
(+ negative tests in IntegrationTests); concurrency/idempotency → `Server` append + `Projections`
strategies; observability/privacy → all layers (structured logs, RFC 9457).

### Integration Points

**Internal (event-sourced flow):** command → `Server.Handle` (pure) → events persisted by EventStore
→ published (persist-then-publish) → `Projections` update (idempotent, order-tolerant) **and**
`Reactor` translates events → commands (child-completion→parent-resume, cascade). Date await →
`Reactor/Timer` Dapr reminder → `ResumeWorkItem`.

**External integrations:** none in v1 beyond sibling Hexalith modules (no production channel adapter).
Projections are SignalR-ready for the deferred UI horizon.

**Data flow:** each persisted EventStore envelope supplies canonical `SequenceNumber` ordering and a
Works payload. State-changing raw-act payloads carry `AggregateId,Sequence`; rejection payloads retain
their frozen context-only shapes. Own-Remaining/Status are synchronous on the aggregate;
rolled-Remaining and "what's next" are eventual projections.

### File Organization Patterns

- **Configuration:** central (`Directory.Packages.props`, `global.json`, `Directory.Build.*`) at root;
  per-project `.csproj` carry no inline versions. Dapr/topology configuration belongs to
  `Hexalith.Platform` (AD-20).
- **Source:** one public type per file; folders = namespaces under `Hexalith.Works.*`.
- **Tests:** Tier-1 pure (`UnitTests`, `PropertyTests`, `ArchitectureTests`) vs boundary
  (`IntegrationTests`); reusable doubles in `Testing`.
- **Assets:** none (headless); the FR-23 boundary record + golden-payload corpus live in `docs/` and
  `tests/`.

### Development Workflow Integration

- **Run:** start the `Hexalith.Platform` Aspire host that composes the published Works
  domain-service module (AD-20); `verify-works-host` is the conformance entry point. The former
  Story 4.9 naming prerequisite is resolved.
- **Build:** `dotnet build Hexalith.Works.slnx`; warnings-as-errors; architecture-fitness tests run
  in the build.
- **Deploy:** v1 ships no production deployment from this domain repository. Release tooling is
  semantic-release + commitlint for its domain packages and minimal domain-service executable.

## Architecture Validation Results

### Coherence Validation ⚠️

**Decision Compatibility:** The approved correction makes the decisions mutually reinforcing:
Event-sourcing on `EventStore` · Dapr-only infrastructure · pure kernel + adapter ring ·
per-descendant envelope-position LWW roll-up · EventStore-owned concurrency (turn lock, ETag fallback — AD-08) · Dapr actor reminders for
date resumes · explicit *do-not-rely-on-pub/sub-ordering* posture. Version authority is AD-19
(`global.json` + central packages); the supported Aspire family is the single 13.5.x line (AD-20).
No compatibility claim is made beyond what those files and the platform conformance lane prove.

**Pattern Consistency:** Implementation patterns (state-changing raw-act payloads carrying
`(AggregateId, Sequence)` ordinals; rejection payloads remaining context-only; envelope
`SequenceNumber` driving persisted order; pure `Handle`/`Apply`/reactor; idempotent order-tolerant
projections; zero branching on executor kind; reference-not-copy) directly enforce the decisions.
Naming follows ecosystem conventions (imperative commands, past-tense events, sealed records).

**Structure Alignment:** The kernel (`Contracts`/`Server`/`Projections`) and pure `Reactor`
translations remain in Works; the minimal domain-service executable exposes them through EventStore.
`Hexalith.Platform` owns runtime topology and infrastructure (AD-20). Story 4.9 must migrate
the historical Works-owned hosting projects per the AD-20 matrix before the structure fully conforms.

### Requirements Coverage Validation ⚠️

**Functional Requirements Coverage:** All 25 FRs across 7 groups have a concrete home after adding
Story 1.5 for the missing post-creation Conversation link
(see Requirements→Structure mapping). Spot checks: FR-11–13 (roll-up/tree-guard/heterogeneous-unit)
→ `Projections` per-child envelope-position + per-Unit subtotals + Server tree guard; FR-17 (uniform
assign/handoff, zero branching) → `ExecutorBinding` + fitness test; FR-23 (boundary decision record)
→ `docs/boundary-decision-record.md` tracked deliverable.

**Non-Functional Requirements Coverage:** Tenant isolation (per-hop equality + query-side authz +
mutation-validated negatives) · ES invariants (persist-then-publish, pure Handle, in-memory Apply,
rejection events) · concurrency (turn-serialized single-claim-wins, ETag-backed atomic save as fallback — AD-08) · rebuildable projections
(reader-available with delivery quiesced/fenced capture-through-commit, per-tenant — the concrete
fence protocol is bound at Story 4.9 R4, AD-16) · domain purity
(kernel/platform boundary + fitness functions) · observability/privacy
(RFC 9457, structured logs, no payloads) · performance (qualitative, incremental updates; no numeric
budgets by design — acceptance is build-signal based). All addressed at contract level; AD-16's
fence and the AD-23/AD-24 enforcement are proven in the Story 4.9 platform lane, not before.

### Implementation Readiness Validation ⚠️

**Decision Completeness:** The four readiness conflicts are resolved in planning. The former
naming prerequisite is **resolved (2026-09-06)**: `Hexalith.Platform`, owned by Platform Maintainer
(Hexalith), is bound in AD-20 with a seam-by-seam migration matrix; Story 4.9 may enter
implementation.

**Structure Completeness:** The intended Works-owned directory tree and external platform boundary are
defined. The platform-side destination is `Hexalith.Platform` (AD-20); its detailed layout evolves in
that repository under the AD-20 conformance lane.

**Pattern Completeness:** Naming, structure, format, communication, and process patterns specified
with good/anti-pattern examples and build-gate enforcement (fitness functions, property tests,
mutation-validated negatives, golden-payload contract tests).

### Gap Analysis Results

**Critical Gaps:** none open — the platform host is named (AD-20). Removing the current Works-owned
projects before the equivalent `Hexalith.Platform` topology is green remains prohibited (AD-20 Rule).

**Important Gaps (resolve in the corrective stories):**
- Implement Story 1.5's additive Conversation command/event/rejection catalog and lifecycle-neutral semantics.
- Migrate and prove the current runtime topology in `Hexalith.Platform` under Story 4.9 per the
  AD-20 matrix, then remove the prohibited Works-owned AppHost/ServiceDefaults projects.
- Implement the Work-Tree Registry aggregate and registry-backed roll-up fan-out (AD-21/AD-22) in
  their owning stories; demote `SpawnChild` caller facts to assertions.
- Enumerate the 9-state cancel/expire transition table (Server story).
- Bind the reminder reconciliation protocol's platform-lane durability parameters (AD-11 / AD-20 R6);
  the discovery design — registry + indexed pending-await source — is decided.
- Implement the platform-host expiry policy options and the expiry reminder adapter (AD-25); the
  config source decision is resolved.

**Nice-to-Have Gaps:** seed the golden-payload corpus; benchmark harness (deferred — no v1 numeric
budgets); MCP/CLI command surfaces (Theme 2, deliberately deferred).

### Validation Issues Addressed

- D-1 reactor placement → resolved: pure translation remains outside the kernel in Works; runtime
  delivery/checkpoint/reminder composition is owned by `Hexalith.Platform` + the EventStore SDK (AD-20).
- D-2 claim cardinality → resolved: single-aggregate under EventStore-owned concurrency (turn lock
  primary, ETag fallback — AD-08); claimable pool is a read projection.
- D-3 deadline semantics + AuthorityLevel → resolved: advisory-until-fired; AuthorityLevel carried,
  not enforced, no v1 branch.
- D-4 Dapr ordering → resolved: at-least-once is portable; ordering is component/configuration-specific
  and never relied upon (AD-09) → projections idempotent + order-tolerant; write-order from the
  single-writer actor.

### Architecture Completeness Checklist

**Requirements Analysis**
- [x] Project context thoroughly analyzed
- [x] Scale and complexity assessed
- [x] Technical constraints identified
- [x] Cross-cutting concerns mapped

**Architectural Decisions**
- [x] Critical decisions documented with versions
- [x] Technology stack fully specified
- [x] Integration patterns defined
- [x] Performance considerations addressed

**Implementation Patterns**
- [x] Naming conventions established
- [x] Structure patterns defined
- [x] Communication patterns specified
- [x] Process patterns documented

**Project Structure**
- [x] External platform-host repository named: `Hexalith.Platform`, owner Platform Maintainer (Hexalith) — AD-20 (2026-09-06)
- [x] Component boundaries established
- [x] Integration points mapped
- [x] Requirements to structure mapping complete

### Architecture Readiness Assessment

**Overall Status:** CONCERNS — DECISIONS BOUND (2026-09-06). The five critical validation findings
(VAL-C01…C05) and the FR-10 expiry gap (VAL-H01) are resolved as AD-20…AD-25; the four planning
contradictions retain their explicit resolutions and backlog ownership. Implementation readiness
remains conditional on completing Stories 4.9 and 1.5 plus the AD-21/AD-22 registry work.

**Confidence Level:** High in the corrected boundaries and EventStore API facts; medium in migration
readiness until the `Hexalith.Platform` lane proves the AD-20 matrix.

**Key Strengths:**
- A genuinely thin, pure, event-sourced kernel with a machine-checkable kernel/adapter boundary.
- The hard event-sourcing traps are pre-solved: idempotent per-child envelope-position roll-up with
  registry-backed fan-out (AD-22), turn-serialized single-claim-wins with an ETag fallback (AD-08),
  clock-free saga with Scheduler-backed reminders and a reconciled recovery protocol (AD-11), and
  reader-available rebuild with delivery quiesced/fenced through atomic commit (AD-16).
- "Everything is a Party" enforced as a fitness function (zero branching), not a hope.
- SM-C1/SM-C2 collapsed into one falsifiable rule: the reactor stays mechanical (no shadow kernel).

**Areas for Future Enhancement:**
- Theme 3–6 adapters (LLM interaction, routing, cost, security) on the laid seams.
- Numeric performance budgets + benchmark harness once usage shape is known.

### Implementation Handoff

**AI Agent Guidelines:**
- Treat the **Architecture Decision Register (AD-01…AD-25)** as the binding contract and cite AD IDs;
  where narrative prose and the register disagree, the register wins.
- Follow all architectural decisions exactly as documented; treat the Implementation Patterns &
  Consistency Rules as binding.
- Keep the kernel pure; keep the reactor mechanical; never branch on executor kind; carry
  `(AggregateId, Sequence)` ordinals only on state-changing payloads, keep rejection payloads frozen,
  and roll up accepted deliveries by EventStore envelope `SequenceNumber`.
- Respect the kernel/adapter boundary and dependency direction; reference siblings by correlation ID.

**First Implementation Priority:**
The platform host is named (AD-20: `Hexalith.Platform`, Platform Maintainer (Hexalith)). Implement
Story 4.9 by reproducing and proving equivalent topology per the AD-20 migration matrix in
`Hexalith.Platform` before deleting the Works-owned AppHost/ServiceDefaults projects. Story 1.5
additively implements post-creation Conversation linking. Route the AD-21 Work-Tree Registry and
AD-22 fan-out into the backlog through sprint planning — binding the VAL-H10 transport-idempotency
contract at the R11 seam first. The FR-20 creation-order fork (VAL-H03) is resolved: deterministic
identity order (approved correct-course, 2026-09-06). Re-run sprint-planning readiness after the
story specifications are implementation-ready.
