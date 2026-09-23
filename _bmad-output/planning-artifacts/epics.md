---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/.memlog.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md'
  - '_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-14.md'
---

# works - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for works, decomposing the requirements from the PRD, UX Design if it exists, and Architecture requirements into implementable stories.

## Forward Target Requirements Inventory

### Functional Requirements

FR1: A builder or Executor can create a tenant-scoped root Work Item with a required Obligation and optional initial effort/Unit, Schedule, Executor Binding, Conversation reference, and Expectation reference. Ordinary creation cannot supply a parent; child creation requires matching origin-restricted Reactor and Registry authorization. Creation emits `WorkItemCreated`, assigns canonical identity, and rests at `Created` even when a binding is supplied.

FR2: A Work Item carries a trimmed, non-empty human-readable Obligation and an optional Expectation reference resolved on demand through `IExpectationResolver`; interpreted expectation data and long-form content are not stored in the aggregate. New command admission accepts at most 4,000 characters and rejects longer content without mutation, while previously accepted longer `WorkItemCreated` payloads remain deserializable and replayable.

FR3: A Work Item carries one Effort Burn-Down containing Unit-tagged Estimated, cumulative Done, and derived `Remaining = max(Estimated - Done, 0)`. Estimated is non-negative; the first explicit or inherited Unit is immutable; progress, correction, or re-estimation in another Unit rejects; and incompatible Units are never silently combined.

FR4: A Work Item carries an optional Schedule of Priority and Due Date. Priority uses the additive-tolerant ordered values `Critical`, `High`, `Normal`, and `Low`; either field can be supplied at creation and changed later through an event-recorded act; missing values are valid and sort last.

FR5: A Work Item records at most one parent reference, zero or more child references, and one or more Await-Conditions while Suspended. The Work-Tree Registry owns authoritative parent-child edges; only `Attached` edges participate in tree behavior, while Work Item references are a convenience mirror and the whole Await-Condition set is cleared on the first accepted resume.

FR6: The aggregate enforces the normative lifecycle across `Created`, `Assigned`, `Queued`, `InProgress`, `Suspended`, `Completed`, `Cancelled`, `Rejected`, and `Expired`. Claim is the only entry to `InProgress`; Reject is legal only from `Assigned` and defaults to requeue; Handoff preserves active Status; invalid acts produce stable domain rejections; and only explicitly defined terminal, consumed-resume, and attachment duplicates are semantic no-ops.

FR7: Every accepted state change and progress fact is an append-only, past-tense Raw-Act Domain Event with authenticated actor, timestamp, causation, and verbatim payload supplied through trusted EventStore envelope provenance. The additive catalog includes correction, reopen, Handoff, Conversation, Registry, and spawn-rejection evidence; optional act notes are limited to 1,000 characters; success and rejection payloads remain distinct; and no caller-supplied actor is authoritative.

FR8: The bound Executor can report a strictly positive progress delta in the established Unit while `InProgress`; reaching zero emits `ProgressReported` followed by `WorkItemCompleted`. Explicit Complete may finish `InProgress` or `Suspended` work at any Remaining without changing Estimated or Done. `CorrectProgress` appends an absolute cumulative Done value at or above zero, permits visible overrun, and may reopen only a progress-completed item when positive Remaining is restored; explicit completion remains terminal.

FR9: An authenticated tenant member can re-estimate or reschedule any non-terminal Work Item. `ReEstimate` replaces Estimated, preserves cumulative Done, Unit, Status, and history, derives non-negative Remaining, permits `Done > Estimated` as visible overrun, and never completes or reopens the item; only `CorrectProgress` changes Done. Reschedule records Priority/Due Date changes and updates query ordering.

FR10: Cancel and Expire terminate any non-terminal Work Item, while the bound Executor may Reject an `Assigned` item with requeue or terminal semantics. Expiry accepts only the currently effective Due Date/TTL schedule witness and treats stale callbacks as audited no-ops. Cancel/Expire eventually cascade through all reachable active `Attached` descendants, include concurrent pre-boundary attachments, preserve already-terminal descendants, and remain durably discoverable until quiescent.

FR11: The system maintains an eventually consistent, rebuildable, idempotent Roll-Up that exposes own Remaining separately from recursive effective contribution by Unit, active-unestimated descendant count, freshness, and explicit stale/unavailable state. It stores absolute last-known descendant contributions by source stream position, handles out-of-order and duplicate delivery, uses only `Attached` topology, emits key-only change notifications, and never presents partial repair/rebuild data as fresh.

FR12: Roll-Up never silently combines incompatible Units. A child without an explicit Unit inherits its parent's Unit for its first estimate; a child with another Unit must declare it explicitly; a unitless parent and estimated child require an explicit child Unit; and mixed-Unit trees expose separate per-Unit subtotals without conversion.

FR13: The tenant-scoped, event-sourced Work-Tree Registry is the sole authority for a single-parent, acyclic, single-tenant tree. It serializes reservations against current Registry state, deterministically rejects cycles, second parents, cross-tenant edges, exhausted operational quota, or excessive depth, uses a configurable per-tenant depth limit of 32 by default, and imposes no domain breadth cap beneath the Platform quota.

FR14: An `InProgress` Work Item can suspend on one or more typed Await-Conditions, recording each condition kind and correlation key. A Suspended item accepts no progress, retains all active conditions until the first accepted match, and continues contributing its current effective Remaining to Roll-Up.

FR15: The Reactor resumes a Suspended Work Item only when a command exactly matches an active Await-Condition by kind and key. The accepted resume records the consumed condition, clears the full set, and returns to `InProgress`; replay of that consumed condition is the sole resume no-op, while every other non-match or post-resume condition is a domain rejection.

FR16: Child creation begins with the Registry's public reserve act carrying the complete child payload. The durable lifecycle is `Reserved -> Creating -> Attached` or `Reserved -> Released`; only `Reserved` may release. Reactor-authorized child creation and matching durable `WorkItemCreated` evidence precede attachment, which then drives idempotent parent bookkeeping. Late released/superseded legs reject stably, `Creating` remains recoverable/quarantinable, and terminal-parent attachment schedules cascade catch-up.

FR17: A Work Item uses one `ExecutorBinding(PartyId, Channel, AuthorityLevel)` for every system, internal, or external doer without executor-kind branches. Assign/reassign handles pre-active responsibility; an authenticated current Executor uses Handoff during `InProgress` or `Suspended` to change the binding without changing Status, Burn-Down, Schedule, or Await-Conditions; Channel changes use the same operations.

FR18: Push and pull coexist: a Work Item can move between `Assigned` and `Queued`, and Claim is the common start act. A queued tenant member may claim only as themself; an assigned item may be claimed only by its bound Executor; single-writer concurrency guarantees one winner and domain-rejects competing stale claims.

FR19: The binding persists an additive-tolerant AuthorityLevel, proposed as `Read`, `Contribute`, `Coordinate`, or `Administer`, without branching on its value in v1. v1 still enforces tenant membership, actor-to-current-binding relationships for responsibility-bound acts, and designated workload origin for Reactor/system acts; planning and coordination acts remain at the authenticated tenant-member floor until later role enforcement.

FR20: A tenant-scoped "what's next" projection exposes one query with two authorized views. The Executor view requires the requested PartyId to equal the authenticated Party and returns that Party's `Assigned` work plus the shared `Queued` pool; the coordinator/all-work view is restricted to trusted internal origin. Results order by present Priority, earliest present Due Date, then ordinal WorkItemId, with no routing score or creation coordinate.

FR21: Works references sibling-owned identity, dialogue, persistence, isolation, and ID data through correlation/reference value objects and never denormalizes their content. Conversation may be linked at creation or once later; a same-ID repeat is a no-op, a conflicting relink rejects, later linkage requires non-terminal state, and Works stores no Conversation content.

FR22: Works exposes `IExpectationResolver` and `IExecutorRouter` as domain-owned ports. v1 ships a structured no-LLM expectation resolver, leaves the router unwired, and keeps LLM, cost, routing, and infrastructure types outside the domain assembly.

FR23: v1 includes a tracked owns-versus-references boundary decision record enumerating each sibling module, what Works owns, what it references, and why; architecture and fitness tests must consume this authority.

FR24: Works provides the canonical minimal EventStore domain-service executable, while Hexalith.Platform owns the Aspire AppHost and composes EventStore and shared infrastructure for full lifecycle testing. Works must not ship an AppHost, Aspire, or ServiceDefaults project or duplicate health, telemetry, projection/query, Dapr, subscription, reminder, or recovery plumbing.

FR25: The complete command/event pipeline is testable without production adapters. Tier-1 aggregate and projection tests remain pure, while integration tests use EventStore testing fakes/builders or the platform-owned Aspire topology only for genuine runtime boundaries and distinguish accepted, domain-rejected, authorization-denied, idempotent, and infrastructure outcomes.

FR26: The Works-owned Reactor is the sole mechanical process manager for every cross-aggregate behavior, including Registry child creation/attachment, child-completion resume, cascade termination, and date/expiry reminders. It translates committed events to deterministic commands without business policy; effects are eventual, checkpointed, origin-restricted, safely replayed, recoverable across crashes and page boundaries, durably discoverable until resolution, and readiness-degrading when stranded.

### NonFunctional Requirements

NFR1: Every aggregate, identity, state key, projection key, checkpoint, query, result set, and log context must be tenant-scoped. Command and query authorization must supplement tenant keying, and negative tests must prove membership denial, unauthorized views, and absence of cross-tenant leakage.

NFR2: Identity and origin must fail closed. Platform ingress authenticates callers and derives canonical Tenant and acting Party from verified claims; immutable authorized context reaches the serialized aggregate turn; internal commands use short-lived, auditable workload delegation; payload identity is never authority; missing, forged, or mismatched identity is denied before dispatch, persistence, query execution, or tenant-existence disclosure.

NFR3: Event-sourcing invariants are mandatory: pure `Handle`, in-memory-only `Apply`, commands in/events out, EventStore-owned envelope metadata, persist-before-publish, success/rejection separation, and infrastructure failures surfaced outside domain-rejection events.

NFR4: Same-aggregate concurrency must provide one logical writer, bounded conflict retry with rehydration, no lost updates, one successful claim in a race, domain rejection for ordinary losers, and an explicit infrastructure failure with no append/publication if retry is exhausted.

NFR5: All delivery is at least once and may be reordered. Consumers acknowledge only after their durable state, effect, checkpoint, or quarantine record commits, and correctness must never depend on broker order.

NFR6: Idempotency must be explicit at three layers: narrowly named semantic no-ops, per-stream-position read-model idempotency, and transport keys that replay the originally recorded success/no-op/rejection throughout the supported retry, replay, restore, and retention horizon. A fresh rejection must never be treated as proof of prior success.

NFR7: Recovery, replay, rebuild, and restore must be complete and observable across pagination/checkpoint boundaries and concurrent writes. Pending saga/reminder work remains discoverable, failed evidence is quarantined through the same validation path, partial generations never become reader-visible, stranded work degrades readiness and alerts operators, and a supported restore reconciles checkpoints, reminders, projections, and accepted writes deterministically.

NFR8: Domain purity and dependency direction are enforced. Domain code has no direct infrastructure, clock, LLM, cost, or routing dependency; cross-module data is referenced rather than copied; reusable runtime capability is implemented in EventStore/Platform before Works consumes it; and project-reference fitness tests keep outer packages from leaking into inner contracts.

NFR9: Observability uses bounded source-generated structured logging and RFC 9457 Problem Details at HTTP edges. Event/command bodies, Raw Acts, personal data, tenant-confidential content, secrets, tokens, and stack traces must not appear in logs, metrics, traces, or error responses.

NFR10: Durable data cannot admit non-synthetic shared production use until classification, minimization, retention/legal hold, tenant offboarding/erasure, audit posture, encryption/secret rotation, backup/restore ordering, recovery objectives, and a restore drill are approved and evidenced. Privileged repair/replay/rebuild/quarantine/offboarding actions are individually audited, and audit-sink failure blocks privileged mutation.

NFR11: Kernel performance remains qualitative until gate G8 binds measurable budgets. Roll-Up and "what's next" must update incrementally without per-query whole-stream reads; the depth-32 by fan-out-50 (~1,600-item), sub-200-ms rolled-read, and five-second convergence figures remain non-gating benchmarks until environment, data set, percentile, window, retry horizon, failure policy, coordination deadlines, and recovery objectives are defined.

NFR12: Durable contracts evolve additively and serialization-tolerantly with no replacement `V2` types. A versioned N-to-N+1 compatibility matrix and bidirectional golden corpus must cover additive fields, defaults, unknown enum/type values, rollout order, rollback/downgrade stance, and explicit quarantine/failure for unsupported evidence; historical bytes remain readable.

NFR13: Runtime and repository conformance uses the checked `.slnx`, .NET 10+, C# 14+, centrally managed package versions, Dapr as the only infrastructure abstraction, `System.Text.Json`, and `Hexalith.PolymorphicSerializations`; warnings are errors and the repository's coding/test conventions remain binding.

NFR14: The Works module remains domain-centric. It may ship Contracts, Server, Projections, Reactor, Client if needed, Testing, and a minimal domain-service executable, but host topology, ServiceDefaults, health, telemetry, generic projections/queries, Dapr wiring, subscriptions, reminders, recovery, and operational policy remain EventStore/Platform responsibilities.

NFR15: Test evidence must cover pure unit behavior, property-based order/convergence cases, persisted end-state at integration boundaries, tenant and origin negative paths, replay/schema compatibility, command outcome distinctions, architecture fitness, and platform-hosted lifecycle/recovery paths. Existing and new relevant tests must pass before a story is complete.

NFR16: Future human-facing surfaces must meet WCAG 2.2 AA with keyboard and assistive-technology parity, visible non-color state meaning, zoom/reflow, text-spacing, reduced-motion and forced-colors support, accessible validation/recovery, stable focus through live updates, and a user-controlled pause for non-essential continuous updates. This is a future-surface quality floor, not a v1 UI commitment.

NFR17: Production ingress must use mutually authenticated transport, declared trust-domain/namespace boundaries, deny-by-default application/network/broker policy, audited workload purposes, secret/Scheduler/state/audit configuration, and machine-readable fail-closed startup admission. Both the full negative-origin matrix and a valid production reminder path require evidence.

NFR18: The eight PRD exit gates remain blocking dependencies: transport idempotency, durable reminders, reader-safe rebuild/recovery, any governed tenant control-plane exception, serialized-contract compatibility, durable-data lifecycle/DR, verified identity/trusted origin, and binding performance/recovery budgets.

### Additional Requirements

- The repository already has an implemented structural seed; Architecture does not prescribe a new greenfield starter template. Preserve the historical Story 1.1 identity, **Set Up Initial Project from Starter Template**, as delivered evidence rather than regenerating or retroactively redefining setup work.
- Preserve the target project structure: `Hexalith.Works.Contracts`, `.Server`, `.Projections`, `.Reactor`, the minimal `.Works` executable, and Unit, Property, Integration, and Architecture test projects. Transitional AppHost and ServiceDefaults code remains only until Platform parity is proven.
- Enforce dependency flow `Server -> Contracts`, `Projections -> Contracts`, `Reactor -> Contracts`, Testing to pure Works units, and the executable to Works units plus EventStore SDK. Every added production reference needs an explicit architecture-fitness rule.
- Use Hexalith.EventStore for all persistence and reusable delivery/runtime behavior and Dapr as the only infrastructure abstraction; Works must not create a parallel repository, broker client, database client, custom envelope, or pre-persist publication path.
- Assign new aggregate identities at the authenticated command-creation edge using Hexalith.Commons sortable ULIDs; handlers, Reactor translations, state folds, and projections must never generate IDs. Existing AggregateIdentity-compatible non-whitespace IDs remain readable.
- Implement Roll-Up with tenant-manifest-selected generation keys and CAS-merged descendant slots: topology watermarks are ordered by Registry envelope sequence, contribution slots by the descendant's envelope sequence, equal-position byte conflicts quarantine/degrade, tombstones defeat older attachments, and non-self contributions require an exact `Attached` topology watermark.
- Keep aggregate own Status/effort and projected rolled state in distinct public types and serialized fields. Record completion kind so correction can reopen only progress completion, while historical streams without the field infer it deterministically from preceding evidence.
- Implement typed durable reminder intents for Date Resume and Expiry through EventStore/Platform seams. Persist canonical target, due instant, schedule token, source position, and typed payload; use recovery reconciliation because indexes are discovery aids, and never read wall-clock or policy inside `Handle`.
- Implement the Work-Tree Registry as tenant/domain `work-tree`/aggregate `registry`, including serialized ParentAdmissionWitness validation, full reserved-payload digest, stable ReservationId, monotonic fencing token, exact `WorkItemCreated` attachment evidence, terminal-parent catch-up, topology-reader APIs, snapshot/replay bounds, saturation/backpressure, and tenant quota enforcement.
- Derive deterministic cross-aggregate EffectIds, MessageIds, and IdempotencyKeys through the versioned EventStore codec and persist a private target-scoped effect receipt atomically with target outcome. Same-key semantic conflicts quarantine; receipts share source evidence retention and are not lifecycle-pruned.
- Use the AD-27 exclusive-lower-bound paging rule: the next page starts from `LastSequenceReturned` without increment. Validate domain, canonical tenant, aggregate, strictly increasing positions, and complete decoding on live, recovery, replay, repair, and rebuild paths; park/quarantine unresolved evidence durably and degrade readiness.
- Shared rebuild uses one CAS-created tenant/family epoch with sealed inventory, writer leases, a durable catch-up journal, staging generations, manifest-only promotion, stale/unavailable reader state during catch-up, and deterministic abort/restore reconciliation. Readers must never see partial generations.
- Platform-host migration is parity-gated across Architecture rows R1-R11. Do not remove transitional Works hosting until the Platform producer/API version, consumer story, proof command, rollback evidence, and all relevant topology, delivery, projection, query, reminder, process-runner, operations, executable, security, and command-submission checks are green.
- Production authorization uses EventStore-validated OIDC context and purpose-bound workload delegation, rechecked inside the serialized aggregate turn for responsibility-bound acts. Production routes fail closed under mTLS, ACL, trust-domain, broker-origin, tenant, purpose, and command-digest validation; user-visible code must not expose internal trust details.
- Persist and validate an effective expiry ScheduleToken so only the current Due Date/TTL intent can expire work. Registration, cancellation, and rescheduling derive mechanically from committed lifecycle events; token/digest collisions quarantine.
- Treat Obligation, notes, Await/correlation values, and durable bodies as confidential tenant data and identifiers/causation as restricted operational metadata. Architecture sets production targets of RPO <= 15 minutes, RTO <= 4 hours, and quarterly restore drills, subject to accountable owner approval.
- Apply reader/validator/catalog/golden-corpus-first schema rollout, producer second; retain readers on rollback while new bytes exist. Required security/fencing fields fail closed, unknown enum values never coerce, malformed or unknown state-affecting evidence quarantines, and legacy child streams require explicit migration rather than inferred attachment.
- Standardize UTC `DateTimeOffset` at edges, canonical encoded IDs in keys, options-free PascalCase concrete writers with tolerant readers, RFC 9457 edge errors, source-generated structured logging, typed options at hosts, and xUnit v3/Shouldly/NSubstitute tests including convergence properties and persisted end-state assertions.
- Use the architecture's observed version authorities and compatibility process for SDK/runtime/package changes; any update requires restore, Release build, focused integration, and Platform parity evidence. No dependency update is implied by this planning workflow.
- Add a tracked CI step that executes the built Release architecture-test assembly; compilation alone is not architecture-fitness evidence.
- Resolve the approved source conflict in favor of PRD FR8/FR9 and the 2026-09-14 proposal: `ReEstimate` preserves cumulative Done, clamps only Remaining, allows visible `Done > Estimated`, never completes/reopens/changes Status, and only `CorrectProgress` changes Done. Architecture AD-17 and any candidate story text must be amended before implementation.
- Treat the change from clamp-Done replay to preserve-Done replay as a semantic migration: replay representative legacy streams through aggregate and projection folds, compare live/rebuilt state, rebuild disposable projections, document snapshot/cache invalidation, and change write-side/read-side interpretations atomically.
- Preserve exact historical story identities and delivered evidence for Epics 1-4: Stories 1.1-1.5, 2.1-2.5, 3.1-3.6, and 4.1-4.9 retain their approved titles and artifacts; later target acceptance criteria must not be retroactively applied to them. The human-approved 2026-09-23 split resets unfinished Story 4.9 to backlog while its prerequisites are drafted.
- Quarantine the existing rewritten target bodies under non-executable provisional labels `F1-A`-`F1-D`, `F2-A`-`F2-H`, `F3-A`-`F3-J`, and `F4-A`-`F4-J`. An `F*` label cannot enter sprint status or inherit historical delivery status and requires a unique final ID, dependency review, validated artifact, and aligned traceability before promotion.
- Preserve the approved Epic 5 remediation scopes without yet changing sprint tracking: overflow-safe progress/event ordinals; a singular executable lifecycle authority; overrun-preserving re-estimation plus bounded act notes; contract-derived durable-catalog completeness; and the 4,000-character Obligation admission bound with historical replay compatibility.
- Preserve Epic 5 dependencies: Story 5.2 precedes final 5.3 integration; 5.3 depends on 5.4 for changed durable evidence; 5.5 depends on 5.4 before any new rejection producer; 5.1 and 5.4 can proceed independently; named retrospective gates must be green before stories are marked done or unattended implementation resumes.
- Keep `sprint-status.yaml` unchanged until `epics.md`, PRD, Architecture, and five validated Epic 5 artifacts agree. Then update it once and atomically, preserving every historical key/status, marking Epics 1-3 done, Epic 4 in progress, Epic 5/backlog keys in backlog, and adding no provisional `F*` keys.
- Follow the approved remediation order: align PRD and Architecture authority, reconcile historical/provisional epic provenance, create and validate Stories 5.1-5.5, update tracking atomically, implement in dependency order with migration evidence, rerun implementation readiness to PASS, and only then rerun sprint planning.
- Do not roll back delivered source behavior or rewrite historical acceptance evidence. The approved plan is additive except for the explicit replay interpretation of downward re-estimation, and it authorizes planning reconciliation—not code implementation, dependency updates, commits, pushes, or premature tracker generation.
- The human-approved 2026-09-23 Story 4.9 split adds draft prerequisite Stories 4.10–4.16 and their backlog tracker keys. These IDs do not promote or inherit delivery status from provisional `F4-*` candidates; Story 4.9 remains the sole host-removal gate.

### UX Design Requirements

UX-DR1: For v1, provide a headless builder-facing harness presentation that groups accepted, domain-rejected, authorization-denied, idempotent, and infrastructure-unknown outcomes with current domain evidence and the next safe action using structurally readable text; do not simulate production navigation or claim a web UI commitment.

UX-DR2: Any future Works web surface must compose through Hexalith.FrontComposer and Blazor Fluent UI V5, inherit the active accent and Fluent typography, spacing, radii, elevation, focus, semantic colors, motion, light/dark, and forced-colors behavior, and define no Works-specific theme, token ramp, status palette, raw HTML/CSS/JavaScript substitute, or third-party component where an inherited component exists.

UX-DR3: A future FrontComposer surface must expose one Works Module entry while preserving Home, use route-backed `What's next` and `Work` tabs, keep Work Item detail subordinate to Work, open Capture through registered generated command routes, and add no Admin/Audit/navigation promise until the owning roadmap contracts exist.

UX-DR4: Every future page, dialog, or detail panel with two or more sibling titled regions must use one `FluentAccordion` with one item per region and the primary item expanded. Page chrome and one sole primary grid/form/detail/visualization may remain outside, and the only primary content must never be hidden.

UX-DR5: Implement `burn-down-meter` from `FluentProgressBar` and `FluentText`, always naming Estimated, cumulative Done, Remaining, and Unit as text. Render a progressbar only for truthful determinate states; separately handle unestimated, zero estimate, overrun, non-terminal zero, progress-completed, explicit residual-completed, and corrected/reopened states without deriving Work Status from the visual fraction.

UX-DR6: Implement `roll-up-summary` from Fluent layout/text and FrontComposer projection-health components, visibly separating own Remaining, rolled Remaining per Unit, active-unestimated descendant count, and freshness. During rebuild/repair, retain only labeled trustworthy last data or replace the result with explicit Unavailable—never zero or an apparently fresh partial value.

UX-DR7: Implement `work-status` with `FcStatusIcon`, the specified existing `BadgeSlot`, an adjacent localized label, and keyboard-accessible tooltip for all nine domain statuses. Never use custom glyphs/pills/palettes or conflate domain Work Status with FrontComposer command lifecycle states.

UX-DR8: Implement `party-reference` with `FluentAvatar`, text/stack, and FrontComposer icons; resolve Party name on demand, display Channel, provide a stable neutral PartyId fallback, and never infer or visually branch on human/system/external executor kind.

UX-DR9: Implement the future `work-tree` using native `FluentTreeView`/`FluentTreeItem` semantics, showing only authoritative `Attached` edges with nested status, Party, and compact Burn-Down. Keep Reserved/Creating/released/superseded evidence outside the tree, describe eventual cascade in text, preserve hierarchy/selection/focus on refresh, and make this the Work page's sole always-visible primary visualization.

UX-DR10: Implement `queue-row` on the generated Fluent data grid and FrontComposer row-detail components, carrying compact Obligation, exact schedule order, resolved Executor, Work Status, Burn-Down, and server-authorized legal actions. Claim loss or actor mismatch must refresh safely while preserving filters, scroll, selection, and deterministic focus fallback.

UX-DR11: Implement `capture-command` with generated forms, authorization regions, placeholders, and lifecycle wrappers. Make Obligation primary; expose 4,000-character Obligation and 1,000-character act-note limits before entry; keep verified tenant/actor and server-derived fields read-only; preserve all safe input through validation, denial, rejection, unknown acceptance, and retry; and distinguish acknowledgement from projection confirmation.

UX-DR12: Implement `work-history` as an authorized semantic chronological list through a FrontComposer L2 projection template, showing trusted actor/origin, locale-formatted time, act, and optional note. Preserve verbatim Raw Acts, original/corrected progress, Handoff, and delayed-child rejection evidence without rendering raw payloads, hidden envelope fields, or infrastructure diagnostics.

UX-DR13: Implement `conversation-panel` as the separately owned Hexalith.Conversations projection/command view in its own labeled accordion region, with independent link, loading, posting, rejection, error, and recovery states; never copy Conversation content into Works or merge it with Raw-Act history.

UX-DR14: For Theme 3 only, implement `natural-language-response` with Fluent text area/button and lifecycle feedback, subordinate to constrained actions. Preserve original words, label interpretation as derived, distinguish applied/high-confidence from confirmation-needed/low-confidence and failure states, and never guess or execute unconstrained natural language as instructions.

UX-DR15: For Theme 3 email, implement `email-action-set` only after the email evidence gate passes, using client-safe semantic presentation, unique descriptive links, constrained actions before reply-in-own-words, stable submission/success pages, complete plain-text parity, narrow-width/200%-text support, and 44-by-44 CSS-pixel targets. Theme 6 separately supplies bound single-use expiry, prior-use evidence, step-up, forwarding safety, and fresh-link/no-login recovery.

UX-DR16: Implement `pause-live-updates` with a labeled Fluent button, queued-count badge, and shared connection status. Pausing freezes visual application rather than receipt, announces once, changes the visible associated count silently, and resumes through one coherent batch and one deduplicated summary without focus theft or lost reading position.

UX-DR17: For Theme 5 only, implement `cost-meter` as a separately labeled Fluent meter with locale-formatted currency/Unit, per-currency/Unit Roll-Up, and the same freshness/Unavailable behavior as effort; never combine currencies silently or distinguish Cost through a fixed palette alone.

UX-DR18: Derive action availability from server-authoritative capability metadata, not visible Status, AuthorityLevel, or inferred actor identity. Retain safe input and distinguish domain rejection, authorization denial, idempotent no-op, and infrastructure-unknown outcomes; refresh evidence and explain the next legal action without leaking tenant or trust internals.

UX-DR19: Provide explicit presentations for Created-with-binding, unestimated, explicit residual completion, correction-driven reopen, overrun, non-terminal zero, immutable-Unit validation, multiple Await-Conditions, active Handoff, Reserved/Creating/Attached/released/superseded attachment, eventual resume/cascade, claim loss, denied actions, Conversation independence, full command lifecycle, and projection health states.

UX-DR20: Meet WCAG 2.2 AA semantics for every canonical component: persistent labels and linked errors, correct native tree/grid/accordion semantics, accessible names including Work Item context, semantic chronological history, named freshness/Unavailable state, non-color status meaning, and no duplicate manual ARIA where Fluent already supplies it.

UX-DR21: Preserve focus, filtered position, scroll, selection, and valid expansion across confirmed actions and live projection changes. If a focused row disappears, focus the same keyed control when possible, otherwise the row now at its old index, nearest previous row, grid, or empty-state heading; background Reactor/projection updates never steal focus and announcements are coalesced.

UX-DR22: At 320 CSS pixels and 400% zoom, reflow primary content and actions in one dimension without clipping, allowing two-dimensional scrolling only in a clearly labeled relationship-preserving grid/tree region. Support text-spacing overrides, 24-by-24 CSS-pixel platform targets or valid spacing exceptions, reduced motion, forced colors, and state communication independent of color or animation.

UX-DR23: Fail closed before rendering protected content. Show only authorized Raw Acts through safe text primitives; omit payloads, envelope internals, secrets, tokens, diagnostics, and unrelated tenant identifiers; treat a Conversation ID as no grant to Conversation content; and keep responsibility, status legality, tenant filtering, and trusted-origin authorization as distinct checks.

UX-DR24: Resource-back all copy, labels, errors, accessible names, action names, subjects, and templates; locale-format dates, times, durations, numbers, Units, and future currencies; preserve Raw Acts verbatim; carry known `lang`, apply safe `dir` and bidi isolation to mixed-script data, and show absolute email expiry when that feature exists. PM must resolve English-only versus full locale/RTL scope before Theme 3 email acceptance.

UX-DR25: MCP, CLI, chatbot, and other text surfaces must preserve verified tenant/actor context, exact command lifecycle/outcome, Work Status, effort/Unit, projection freshness, and next legal action. Theme 2 input is exact and command-shaped; natural-language input begins only in Theme 3.

UX-DR26: Reuse exactly the 13 canonical Works component IDs—`burn-down-meter`, `roll-up-summary`, `work-status`, `party-reference`, `work-tree`, `queue-row`, `capture-command`, `work-history`, `conversation-panel`, `natural-language-response`, `email-action-set`, `pause-live-updates`, and `cost-meter`—with the horizon and implementation bases defined by the UX spines; visual mockups are density/grouping references only and their markup, tokens, routes, controls, sample values, and lifecycle copy are non-contractual.

### Forward Target FR Coverage Map

FR1: Epic 1 - Create a tenant-scoped root Work Item through the stable kernel contract.
FR2: Epic 1 - Carry a bounded Obligation and resolve an optional Expectation reference; Epic 5 owns the undelivered admission-bound compatibility delta.
FR3: Epic 2 - Maintain immutable-Unit Effort Burn-Down with derived non-negative Remaining.
FR4: Epic 2 - Carry and change Priority/Due Date Schedule facts.
FR5: Epic 3 - Represent Work Tree references and multiple Await-Conditions against authoritative Registry topology.
FR6: Epic 2 - Enforce the complete lifecycle and stable outcomes; Epic 5 hardens its singular executable authority.
FR7: Epic 2 - Record authenticated append-only Raw Acts; Epic 5 hardens note bounds and catalog completeness.
FR8: Epic 2 - Report/correct progress and complete work; Epic 5 hardens overflow and correction evidence.
FR9: Epic 2 - Re-estimate and reschedule without changing cumulative Done; Epic 5 owns the preserve-Done replay migration.
FR10: Epic 2 - Cancel, reject, and expire work; Epic 3 supplies eventual descendant-cascade coordination.
FR11: Epic 3 - Maintain an idempotent, rebuildable, freshness-aware recursive Roll-Up.
FR12: Epic 3 - Inherit Units safely and expose mixed-Unit subtotals without conversion.
FR13: Epic 3 - Enforce tenant-safe, single-parent, acyclic, bounded-depth topology through the Work-Tree Registry.
FR14: Epic 3 - Suspend active work on one or more typed Await-Conditions.
FR15: Epic 3 - Resume only on an exact matching trigger with narrow consumed-condition idempotency.
FR16: Epic 3 - Create and attach child work through the fenced Registry/Reactor protocol.
FR17: Epic 4 - Bind, reassign, and hand off every executor kind through one uniform binding.
FR18: Epic 4 - Support push/pull assignment and single-winner Claim behavior.
FR19: Epic 4 - Carry AuthorityLevel while enforcing tenant, responsibility, and trusted-origin relationships.
FR20: Epic 4 - Expose authorized Executor/coordinator "what's next" views with deterministic ordering.
FR21: Epic 1 - Reference sibling-owned identity, Conversation, persistence, tenant, and ID data without copying it.
FR22: Epic 1 - Expose expectation and routing ports while keeping deferred concerns outside the domain.
FR23: Epic 1 - Track the owns-versus-references boundary decision record.
FR24: Epic 4 - Exercise the Works domain service through the platform-owned Aspire topology.
FR25: Epic 4 - Prove command/event outcomes through pure and boundary-appropriate automated tests.
FR26: Epic 3 - Coordinate every cross-aggregate effect through the durable mechanical Reactor.

## Delivered Baseline Story Map

The following story identities, statuses, and evidence sources are the immutable delivered or tracked baseline. Forward requirements do not retroactively alter their acceptance records.

| Story | Delivered or tracked title | Status | Durable authority |
| --- | --- | --- | --- |
| 1.1 | Set Up Initial Project from Starter Template | done | `_bmad-output/implementation-artifacts/1-1-set-up-initial-project-from-starter-template.md` |
| 1.2 | Create a Tenant-Scoped Work Item | done | `_bmad-output/implementation-artifacts/1-2-create-a-tenant-scoped-work-item.md` |
| 1.3 | Reference Sibling Modules Without Copying Data | done | `_bmad-output/implementation-artifacts/1-3-reference-sibling-modules-without-copying-data.md` |
| 1.4 | Expose Boundary Ports and Decision Record | done | `_bmad-output/implementation-artifacts/1-4-expose-boundary-ports-and-decision-record.md` |
| 1.5 | Link a Conversation After Creation | done | `_bmad-output/implementation-artifacts/spec-1-5-link-a-conversation-after-creation.md` |
| 2.1 | Define the Lifecycle State Machine | done | `_bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md` |
| 2.2 | Record Raw-Act Events and Replay State | done | `_bmad-output/implementation-artifacts/2-2-record-raw-act-events-and-replay-state.md` |
| 2.3 | Report Progress with Unit-Tagged Burn-Down | done | `_bmad-output/implementation-artifacts/2-3-report-progress-with-unit-tagged-burn-down.md` |
| 2.4 | Re-Estimate and Reschedule Work | done | `_bmad-output/implementation-artifacts/2-4-re-estimate-and-reschedule-work.md` |
| 2.5 | Complete, Cancel, Reject, and Expire Work | done | `_bmad-output/implementation-artifacts/2-5-complete-cancel-reject-and-expire-work.md` |
| 3.1 | Guard Tenant-Safe Work Tree Shape | done | `_bmad-output/implementation-artifacts/3-1-guard-tenant-safe-work-tree-shape.md` |
| 3.2 | Spawn Child Work from a Parent | done | `_bmad-output/implementation-artifacts/3-2-spawn-child-work-from-a-parent.md` |
| 3.3 | Maintain Recursive Roll-Up with Per-Child Sequence | done | `_bmad-output/implementation-artifacts/3-3-maintain-recursive-roll-up-with-per-child-sequence.md` |
| 3.4 | Preserve Heterogeneous Unit Subtotals | done | `_bmad-output/implementation-artifacts/3-4-preserve-heterogeneous-unit-subtotals.md` |
| 3.5 | Suspend and Resume on Await-Conditions | done | `_bmad-output/implementation-artifacts/3-5-suspend-and-resume-on-await-conditions.md` |
| 3.6 | Cascade Terminal Work Through Active Descendants | done | `_bmad-output/implementation-artifacts/3-6-cascade-terminal-work-through-active-descendants.md` |
| 4.1 | Bind Work to a Uniform Party Executor | done | `_bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md` |
| 4.2 | Assign, Reassign, and Hand Off Work | done | `_bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md` |
| 4.3 | Claim Queued Work with Single-Claim-Wins | done | `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md` |
| 4.4 | Resolve the Tenant's What's Next Queue | done | `_bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md` |
| 4.5 | Prove the Command/Event Pipeline Under Aspire | done | `_bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md` |
| 4.6 | Prove Reminder and Reactor Recovery | done | `_bmad-output/implementation-artifacts/4-6-prove-reminder-and-reactor-recovery.md` |
| 4.7 | Trigger Reactor Translators from the Live Event Stream | done | `_bmad-output/implementation-artifacts/4-7-trigger-reactor-translators-from-the-live-event-stream.md` |
| 4.8 | Register and Reconcile Date Reminders Durably | review | `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` |
| 4.9 | Migrate Works Hosting to the Platform Boundary | backlog | `_bmad-output/implementation-artifacts/sprint-status.yaml` and `_bmad-output/implementation-artifacts/epic-4-context.md` |
| 4.10 | Publish EventStore Projection Delivery and Rebuild Fence | backlog | `_bmad-output/implementation-artifacts/spec-4-10-publish-eventstore-projection-delivery-and-rebuild-fence.md` |
| 4.11 | Publish EventStore Typed Reminder Reconciliation | backlog | `_bmad-output/implementation-artifacts/spec-4-11-publish-eventstore-typed-reminder-reconciliation.md` |
| 4.12 | Publish EventStore Checkpointed Process and Recovery Runtime | backlog | `_bmad-output/implementation-artifacts/spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md` |
| 4.13 | Publish EventStore Trusted Effect Submission | backlog | `_bmad-output/implementation-artifacts/spec-4-13-publish-eventstore-trusted-effect-submission.md` |
| 4.14 | Adopt SDK Projection and Query Seams in Works | backlog | `_bmad-output/implementation-artifacts/spec-4-14-adopt-sdk-projection-and-query-seams-in-works.md` |
| 4.15 | Adopt SDK Reminder, Process, and Command Seams in Works | backlog | `_bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md` |
| 4.16 | Prove Platform Works Parity and Rollback | backlog | `_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md` |

## Forward Candidate Identity Map

| Rewritten source range | Preserved provisional range | Execution status |
| --- | --- | --- |
| 1.1–1.4 | `F1-A`–`F1-D` | Non-executable; requires unique final IDs and validated traceability before promotion |
| 2.1–2.8 | `F2-A`–`F2-H` | Non-executable; requires unique final IDs and validated traceability before promotion |
| 3.1–3.10 | `F3-A`–`F3-J` | Non-executable; requires unique final IDs and validated traceability before promotion |
| 4.1–4.10 | `F4-A`–`F4-J` | Non-executable; requires unique final IDs and validated traceability before promotion |

## Epic List

Epics 1-4 preserve the delivered story identities and evidence recorded by sprint status and durable artifacts. Later target decompositions remain under non-executable `F1-*` through `F4-*` labels until promoted with unique IDs and validated traceability. Epic 5 remains the remediation epic; the 2026-09-23 approved split separately adds Epic 4 migration prerequisites 4.10–4.16 without promoting any `F4-*` candidate.

### Epic 1: Create and Integrate a Tenant-Safe Work Kernel

Builders can create tenant-scoped Work Items, reference sibling-owned data safely, and integrate through stable domain boundaries without custom infrastructure.

**FRs covered:** FR1, FR2, FR21, FR22, FR23

### Epic 2: Advance Work Through a Trustworthy Lifecycle

Executors and coordinators can schedule, claim, progress, correct, complete, reject, cancel, and expire work through an auditable lifecycle.

**FRs covered:** FR3, FR4, FR6, FR7, FR8, FR9, FR10

### Epic 3: Coordinate Durable Work Trees and Sagas

Builders and Executors can decompose work into tenant-safe trees, observe reliable Roll-Ups, suspend on conditions, resume durably, and propagate terminal outcomes.

**FRs covered:** FR5, FR11, FR12, FR13, FR14, FR15, FR16, FR26

### Epic 4: Assign, Discover, and Operate Work Reliably

Tenant participants can assign or claim work uniformly, discover the correct next work, and exercise the complete command pipeline through the platform-owned runtime.

**FRs covered:** FR17, FR18, FR19, FR20, FR24, FR25

### Epic 5: Stabilize the Work Item Contract and Planning Record

Maintainers can evolve and replay the Work Item contract safely while restoring deterministic planning provenance and closing the approved correctness gaps.

**Target deltas covered:** FR2, FR6, FR7, FR8, FR9, NFR12, NFR15, and the approved planning-provenance requirements.

**Dependency flow:** Epic 1 enables Epic 2; Epic 2 enables Epic 3; Epic 4 operates the capabilities delivered by Epics 1-3; Epic 5 is additive remediation over the delivered baseline. Core-file overlap between Epics 2, 3, and 5 is intentional because consolidating them would violate the approved historical-versus-forward provenance boundary.

## Epic 1: Create and Integrate a Tenant-Safe Work Kernel

Builders can create tenant-scoped Work Items, reference sibling-owned data safely, and integrate through stable domain boundaries without custom infrastructure.

### Story 1.1: Set Up Initial Project from Starter Template

As a Hexalith builder,
I want a clean Works module scaffold aligned with the Hexalith ecosystem,
So that I can implement the Work Item kernel in a verified, buildable module without inventing technical layers.

**Acceptance Criteria:**

**Given** the Hexalith.Works umbrella repository with only root-declared submodules available
**When** the Works module scaffold is created
**Then** it contains the architecture-defined Contracts, Server, Projections, Reactor, Testing, AppHost, ServiceDefaults, sample, and focused test projects required at the time of delivery
**And** it contains no UI, MCP, portal, routing, LLM, cost-governance, security, email, or production-channel project.

**Given** the scaffolded module
**When** package and solution configuration is inspected
**Then** dependencies and versions use central package management without inline package versions
**And** the repository uses the `.slnx` solution format rather than `.sln`.

**Given** the scaffolded projects
**When** dependency direction is checked
**Then** Contracts remains low-dependency and infrastructure-free, while Server, Projections, and Reactor reference inward without cycles
**And** architecture tests reject prohibited adapter, runtime, UI, LLM, routing, or cost dependencies.

**Given** the live Hexalith.EventStore API surface
**When** its concurrency, projection, ETag/notifier, identity, and rebuild capabilities are characterized
**Then** the supported APIs and constraints are recorded before later domain behavior relies on them
**And** differences from assumed expected-version or atomic-swap behavior are documented explicitly.

**Given** only root-level submodules are initialized
**When** the scaffold's restore, Release build, and focused test commands run
**Then** the affected projects build with warnings treated as errors and the architecture tests pass
**And** no recursive or nested-submodule initialization is required.

**Given** Story 1.1's scope boundary
**When** its delivered files are reviewed
**Then** it contains only scaffold, configuration, dependency rules, baseline verification, and EventStore characterization
**And** lifecycle, Burn-Down, Roll-Up, Await-Condition, Executor Binding, and Reactor behavior remain outside this story.

**Given** this story was completed under the former Works-owned hosting architecture
**When** current planning references its delivered acceptance record
**Then** its `done` status and baseline evidence remain historical and are not rewritten against later requirements
**And** AppHost and ServiceDefaults are treated as superseded delivery evidence, with migration and removal owned by Story 4.9.

### Story 1.2: Create a Tenant-Scoped Work Item

As a Hexalith builder,
I want to create the first tenant-scoped Work Item through the domain contract,
So that Works proves it can record a durable, replayable obligation without copying sibling-module data.

**Acceptance Criteria:**

**Given** a caller supplies a `TenantId`, an edge-assigned `WorkItemId`, and a non-empty Obligation description
**When** `CreateWorkItem` is handled against no prior state
**Then** the domain returns a `WorkItemCreated` payload and replay produces Status `Created`
**And** the aggregate identity is consistent with `{tenant}:work:{workItemId}`.

**Given** a caller supplies optional initial Effort, Unit, Schedule, parent reference, Executor Binding, or Conversation correlation ID
**When** the Work Item is created
**Then** `WorkItemCreated` carries only the supplied coordination facts and reference IDs
**And** no Party, Tenant, Conversation, EventStore envelope, or Commons implementation data is copied into aggregate state.

**Given** a caller supplies no Estimated effort
**When** the Work Item is created
**Then** creation succeeds and Remaining is represented as undefined until estimated
**And** the item is not considered completed by a Remaining-equals-zero rule.

**Given** a caller supplies a missing or whitespace Obligation description
**When** `CreateWorkItem` is handled
**Then** creation returns only the defined domain rejection event
**And** the rejection is not mixed with a success event or state mutation.

**Given** `CreateWorkItem` is handled and its event replayed
**When** purity and architecture tests run
**Then** the handler does not generate IDs, read a clock, perform I/O, call Dapr, or populate EventStore envelope metadata
**And** applying the emitted event deterministically recreates the same state.

**Given** later target requirements add origin-restricted child creation and a 4,000-character admission bound
**When** Story 1.2's delivered status is evaluated
**Then** those later requirements do not retroactively alter its historical acceptance record
**And** their executable ownership remains with forward candidate F1-B and remediation Story 5.5.

### Story 1.3: Reference Sibling Modules Without Copying Data

As a Hexalith builder,
I want Work Items to carry only reference value objects for sibling-module concepts,
So that Works owns coordination facts while Parties, Conversations, Tenants, EventStore, and Commons remain the systems of record.

**Acceptance Criteria:**

**Given** the Works contracts define references to sibling concepts
**When** Work Item commands, events, state, and read-model contracts are inspected
**Then** Parties use `PartyId`, Conversations use a correlation/reference ID, Tenants use `TenantId`, and Work IDs arrive from the edge
**And** no aggregate handler generates an identifier.

**Given** a Work Item is created with Party, Conversation, Tenant, and parent/work references
**When** its event payloads and replayed state are inspected
**Then** they contain only stable reference IDs and coordination facts
**And** they contain no Party display/contact data, tenant profiles, Conversation messages, EventStore envelopes, or ID-generation details.

**Given** no Conversation correlation ID is supplied
**When** a Work Item is created or replayed
**Then** the Work Item remains valid
**And** Works creates no comment or Conversation storage.

**Given** a future adapter or projection needs sibling-owned details
**When** the domain contract is inspected
**Then** it exposes only references resolvable on demand outside the aggregate
**And** Contracts requires no sibling client, server, infrastructure, or implementation dependency.

**Given** tenant isolation is mandatory
**When** commands, events, keys, and log scopes are derived for a Work Item
**Then** the tenant reference participates in coordination identity and same-tenant validation
**And** tests prove cross-tenant work references cannot be accepted as same-tenant data.

### Story 1.4: Expose Boundary Ports and Decision Record

As a Hexalith builder,
I want Works to expose explicit domain ports and a boundary decision record,
So that future LLM, routing, cost, security, and sibling-module integrations attach without changing the kernel's ownership model.

**Acceptance Criteria:**

**Given** the Works contract surface
**When** its domain ports are reviewed
**Then** `IExpectationResolver` is available as a domain-owned abstraction with a no-LLM implementation for the delivered version
**And** Work Item behavior remains valid when no interpreted Expectation is available.

**Given** routing is deferred from the delivered version
**When** the contract surface is inspected
**Then** it exposes the `IExecutorRouter` abstraction only
**And** no routing implementation, scoring engine, escalation mechanism, LLM integration, or cost-governance behavior is wired.

**Given** the Contracts, Server, and Projections dependency graph
**When** architecture rules are evaluated
**Then** those projects contain no implementation dependency on LLM, routing, cost, UI, channel, or infrastructure concerns
**And** architecture fitness tests enforce the boundary.

**Given** the generated boundary decision record
**When** `docs/boundary-decision-record.md` is reviewed
**Then** it records the owns-versus-references boundary for Parties, Conversations, EventStore, Tenants, Commons, and PolymorphicSerializations
**And** it explains how Works owns coordination while sibling modules own identity, dialogue, persistence, isolation, and identifier generation.

**Given** future themes add adapters around the kernel
**When** the boundary record and domain ports are reviewed
**Then** they preserve named seams for AI expectation resolution, routing, cost governance, and security
**And** those capabilities are explicitly outside the delivered version's implementation scope.

### Story 1.5: Link a Conversation After Creation

As a Hexalith builder,
I want to link an existing Conversation to a Work Item after creation,
So that independently created work and dialogue can be correlated without rewriting history or copying Conversation data.

**Acceptance Criteria:**

**Given** an unlinked Work Item in any non-terminal state and a valid opaque Conversation correlation ID
**When** `LinkConversation` is handled
**Then** exactly one `ConversationLinked` event is returned
**And** replay retains the current lifecycle status, stores the reference, and advances the state ordinal.

**Given** a Work Item already carries the same Conversation correlation ID
**When** the same link is requested in any lifecycle state, including a terminal state
**Then** the result is `DomainResult.NoOp`
**And** no event or state mutation occurs.

**Given** a Work Item already carries a different authoritative Conversation correlation ID
**When** a replacement link is requested
**Then** `WorkItemConversationLinkRejected` identifies the tenant, Work Item, existing ID, and proposed ID
**And** the original link, lifecycle status, and ordinal are retained.

**Given** an unlinked Work Item is Completed, Cancelled, Rejected, or Expired
**When** `LinkConversation` is handled
**Then** `WorkItemTransitionRejected` reports the current status and attempted act `LinkConversation`
**And** no state mutation occurs.

**Given** the Work Item is missing or its state is `Unknown`
**When** `LinkConversation` is handled
**Then** `WorkItemTransitionRejected(Unknown, "LinkConversation")` is returned
**And** no Work Item or Conversation is implicitly created.

**Given** duplicated, conflicting, malformed, or out-of-order Conversation-link evidence reaches replay and projection paths
**When** aggregate state and read models are rebuilt
**Then** the first valid authoritative reference is preserved, accepted source watermarks converge deterministically, and malformed evidence fails closed
**And** queries expose only the opaque Conversation reference without changing what's-next eligibility.

**Given** contract, architecture, polymorphic, and golden-corpus verification runs
**When** the delivered Story 1.5 surface is inspected
**Then** the historical catalog contains exactly 40 types—15 commands, 15 success events, and 10 rejections—with frozen coverage for the three added contracts
**And** prior serialized bytes remain compatible and no Conversations implementation dependency, generated identifier, clock, I/O, direct persistence, routing, LLM, cost, UI, channel, or hosting behavior is introduced.

### Forward Candidate F1-A: Integrate Through a Stable Headless Contract

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith builder,
I want to reference stable Works contracts and domain ports,
So that I can integrate work coordination without copying sibling data or adding infrastructure dependencies (FR21, FR22, FR23).

**Acceptance Criteria:**

**Given** the existing brownfield `.slnx` and project structure
**When** the Works contract boundary is built
**Then** `Contracts` contains only durable commands, events, value objects, models, and domain-owned ports
**And** no starter project, production adapter, UI dependency, or new Works-owned AppHost/ServiceDefaults project is introduced.

**Given** Server, Projections, Reactor, Testing, and the executable consume Works contracts
**When** architecture fitness tests inspect project references
**Then** Server, Projections, and Reactor depend inward on Contracts rather than one another
**And** any forbidden infrastructure, LLM, routing, cost, FrontComposer, or Fluent dependency fails the test.

**Given** a builder references Party, Conversation, Tenant, or aggregate identity
**When** those values cross the Works boundary
**Then** Works carries validated correlation identifiers only
**And** no sibling-owned profile, conversation content, membership record, persistence model, or generated identity is copied into domain state.

**Given** a builder needs expectation or routing integration
**When** the domain ports are resolved
**Then** `IExpectationResolver` and `IExecutorRouter` are available from the contract boundary
**And** v1 supplies a structured no-LLM expectation resolver while requiring no router implementation.

**Given** the owns-versus-references boundary record
**When** a maintainer reviews each sibling-module relationship
**Then** the record identifies what Works owns, what it references, and the rationale
**And** its claims agree with the enforced project-dependency rules.

**Given** the v1 delivery horizon
**When** package contents and tracked UX contracts are inspected
**Then** the shipped capability remains a headless builder-facing kernel
**And** future MCP, CLI, web, chatbot, email, routing, Cost, and hardening requirements remain documented at their assigned horizons without speculative implementation.

### Forward Candidate F1-B: Create and Rehydrate a Tenant-Scoped Work Item

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith builder,
I want to create and replay a Work Item in an authenticated tenant context,
So that its obligation and initial coordination facts remain durable and deterministic (FR1, FR2, FR7).

**Acceptance Criteria:**

**Given** an authenticated tenant member, a caller-assigned valid WorkItemId, and an Obligation of 1 to 4,000 characters
**When** `CreateWorkItem` is handled for a new stream
**Then** one `WorkItemCreated` success event carries the WorkItemId, Tenant reference, and verbatim Obligation
**And** applying the event produces Status `Created` without generating an ID, reading a clock, or consulting an external system inside the aggregate.

**Given** valid optional estimate and Unit, Priority, Due Date, Executor Binding, Conversation reference, and Expectation reference, plus parent/reservation evidence only for an origin-validated reserved-child command
**When** the Work Item is created
**Then** `WorkItemCreated` preserves every supplied value in the durable contract
**And** an initial Executor Binding does not emit `WorkItemAssigned` or change the resting Status from `Created`, while an ordinary parent-bearing root command is denied before Handle.

**Given** no initial estimate
**When** a valid Work Item is created
**Then** creation succeeds with Estimated, Done, and Remaining unestablished
**And** the state remains valid for later estimation or explicit completion under the lifecycle rules.

**Given** an empty, whitespace-only, or longer-than-4,000-character Obligation
**When** creation is handled
**Then** the result contains the defined domain rejection and no success event
**And** no Work Item state is created or published.

**Given** the authenticated context derives a Tenant or acting Party different from a caller-supplied assertion, or the caller has no target-tenant membership
**When** the command reaches the trust boundary
**Then** it is denied before aggregate dispatch, append, publication, or tenant-existence disclosure
**And** the denial is audited without treating the supplied identity as authority.

**Given** a new command created at the target edge or a historical persisted WorkItemId
**When** identity is validated
**Then** the target edge uses the Hexalith.Commons sortable ULID generator while existing AggregateIdentity-compatible non-whitespace IDs remain readable
**And** replay, handlers, projections, and Reactor code never generate replacement aggregate identities.

**Given** a persisted `WorkItemCreated` envelope
**When** state is rebuilt by applying the event from an empty state
**Then** the resulting Work Item is identical to the originally accepted state
**And** authenticated actor, timestamp, canonical position, and tenant-delegation metadata come from the EventStore envelope rather than spoofable Works payload fields.

### Forward Candidate F1-C: Link a Conversation Without Owning Dialogue

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith builder,
I want a Work Item to reference one separately owned Conversation,
So that dialogue can accompany coordinated work without becoming duplicated aggregate content (FR7, FR21).

**Acceptance Criteria:**

**Given** a valid Conversation correlation ID is supplied during Work Item creation
**When** `WorkItemCreated` is applied
**Then** the Work Item stores that correlation ID as its authoritative Conversation reference
**And** no Conversation message, participant, profile, or derived dialogue value is copied into Works state.

**Given** a non-terminal Work Item with no Conversation reference and an authenticated tenant member
**When** `LinkConversation` supplies a valid correlation ID
**Then** one `ConversationLinked` event records the reference and applying it links the Work Item
**And** actor and timestamp evidence comes from the authenticated EventStore envelope.

**Given** a Work Item already linked to a Conversation
**When** `LinkConversation` repeats the identical correlation ID before or after terminal closure
**Then** the command is an acknowledged idempotent no-op with no new event
**And** the existing Conversation reference and Raw-Act history remain unchanged.

**Given** a Work Item already linked to one Conversation
**When** `LinkConversation` supplies a different correlation ID
**Then** the command produces the defined domain rejection and no success event
**And** the original reference remains authoritative and unchanged.

**Given** an unlinked Work Item in `Completed`, `Cancelled`, `Rejected`, or `Expired`
**When** a new Conversation link is attempted
**Then** the command produces the defined domain rejection
**And** terminal state and history remain unchanged.

**Given** a caller can access the Work Item but lacks access to the referenced Conversation
**When** the Conversation is resolved
**Then** Hexalith.Conversations applies its own authorization, loading, retention, and error rules
**And** possession of the correlation ID does not disclose or authorize Conversation content.

**Given** an invalid, empty, or noncanonical Conversation correlation value
**When** it is supplied at creation or through `LinkConversation`
**Then** the relevant command is rejected without persisting malformed reference data
**And** safe diagnostic output excludes the submitted content and hidden envelope details.

### Forward Candidate F1-D: Preserve Durable Contract Compatibility

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith maintainer,
I want durable Works contracts protected by compatibility evidence,
So that new capabilities can be added without making historical Work Items unreadable or changing their Raw Acts (FR7, FR21).

**Acceptance Criteria:**

**Given** every durable Works command, success event, rejection event, value object, and projection message currently produced
**When** catalog and serialization tests run
**Then** every concrete durable type is registered with a stable discriminator and round-trips through Hexalith.PolymorphicSerializations
**And** no existing discriminator is renamed, reused for different semantics, or replaced by a `V2` type.

**Given** a historical golden payload that omits fields added later
**When** the current reader deserializes and applies it
**Then** the payload remains readable with deterministic documented defaults
**And** unknown additive fields are tolerated without changing the meaning of existing fields.

**Given** the durable serialization conventions
**When** current contracts are written and historical variants are read
**Then** writers emit options-free PascalCase concrete payloads without an at-rest polymorphic marker
**And** readers remain case-insensitive and tolerant only where the additive schema policy permits.

**Given** a change introduces a durable type, enum value, or required security/fencing field
**When** the change is prepared for rollout
**Then** reader, validator, catalog, and golden-corpus support lands and passes before any producer emits the new bytes
**And** rollback retains readers for those bytes while they remain in durable storage.

**Given** malformed, unknown, discriminator-conflicting, or security-incomplete state-affecting evidence
**When** contract validation runs
**Then** the evidence fails closed with a typed quarantine disposition rather than coercion, partial application, or silent discard
**And** authenticated audited replay must pass through the same validation path before the evidence can affect state.

**Given** a command produces an expected invalid-domain outcome
**When** its `DomainResult` is inspected
**Then** the rejection implements `IRejectionEvent` and the result contains no success payload
**And** infrastructure faults remain exceptions or dead-letter outcomes rather than durable domain rejections.

**Given** Raw-Act text, identifiers, optional notes, or referenced values are serialized, tested, or diagnosed
**When** compatibility and logging tests execute
**Then** authorized event payloads preserve the accepted verbatim values and bounded fields
**And** logs, metrics, traces, and ProblemDetails contain only bounded metadata—not payloads, secrets, personal data, or full command bodies.

**Given** the compatibility suite is executed repeatedly on the same golden corpus
**When** no contract change has occurred
**Then** byte expectations, reconstructed state, and rejection dispositions remain deterministic
**And** the focused xUnit v3 suite passes with Shouldly assertions under nullable and warnings-as-errors rules.

## Epic 2: Advance Work Through a Trustworthy Lifecycle

Executors can move Work Items through assignment, progress, suspension, resumption, correction, and terminal outcomes while preserving exact accepted acts and explicit invalid outcomes.

### Story 2.1: Define the Lifecycle State Machine

As an executor,
I want Work Items to enforce a clear lifecycle,
So that every accepted transition is predictable and every invalid transition is rejected as a domain fact.

**Acceptance Criteria:**

**Given** a Work Item in Status `Created`
**When** the executor assigns it or queues it
**Then** the transition to `Assigned` or `Queued` is accepted
**And** any unsupported transition from `Created` is rejected as an `IRejectionEvent`.

**Given** a Work Item in `Assigned` or `Queued`
**When** the executor starts or claims work according to the lifecycle rules
**Then** the item can transition to `InProgress`
**And** `Assigned` to `Queued` and `Queued` to `Assigned` transitions are accepted where requeue or direct assignment is valid.

**Given** a Work Item in `InProgress`
**When** it is suspended
**Then** the item transitions to `Suspended`
**And** resumption is represented only as a transition back to `InProgress`, not as a resting `Resumed` status.

**Given** a Work Item in any terminal status
**When** a further lifecycle command is handled
**Then** no transition out of `Completed`, `Cancelled`, non-requeuable `Rejected`, or `Expired` is accepted
**And** non-idempotent lifecycle commands emit an `IRejectionEvent`, while only exact duplicate terminal commands documented in the lifecycle matrix return `DomainResult.NoOp`.

**Given** a bound executor rejects an assignment with the default requeue behavior
**When** the rejection is handled
**Then** `WorkItemRejected` may be emitted as Raw-Act evidence
**And** the resulting resting status is `Queued`, not terminal `Rejected`.

**Given** lifecycle rules are defined
**When** the story is complete
**Then** `docs/lifecycle-transition-matrix.md` enumerates accepted, rejected, and idempotent no-op outcomes for every command across all nine statuses
**And** later lifecycle stories reference this artifact rather than selecting behavior locally.

**Given** the lifecycle implementation is tested
**When** the transition matrix is exercised
**Then** every legal and illegal transition across the nine statuses is covered by deterministic tests
**And** the handler remains pure, using no clock, random generator, I/O, Dapr, or EventStore envelope ownership.

### Story 2.2: Record Raw-Act Events and Replay State

As a Hexalith builder,
I want every accepted Work Item act to be recorded as a replayable Raw-Act event,
So that the Work Item history is durable, auditable, and independent of interpreted projections.

**Acceptance Criteria:**

**Given** a Work Item state change or progress fact is accepted
**When** the domain result is produced
**Then** it contains a past-tense domain event from the delivered catalog
**And** the event stores the verbatim reported values required to replay the act.

**Given** a domain event is emitted
**When** its payload is inspected
**Then** it carries `AggregateId` and `Sequence` for order-tolerant projections
**And** Works does not populate or spoof EventStore envelope metadata.

**Given** a sequence of Work Item events exists
**When** the events are replayed in order through `Apply`
**Then** the same Work Item state is reconstructed deterministically
**And** no interpreted expectation, AI output, or sibling-module denormalization is required.

**Given** a command is rejected
**When** the domain result is inspected
**Then** the rejection is represented as an `IRejectionEvent`
**And** the same domain result does not mix success payloads with rejection payloads.

**Given** serialization compatibility is required
**When** the delivered event and command catalog is registered
**Then** `Hexalith.PolymorphicSerializations` resolves every registered payload type
**And** a frozen golden-payload corpus proves additive, no-`V2` evolution while concrete EventStore serialization remains unchanged.

### Story 2.3: Report Progress with Unit-Tagged Burn-Down

As an executor,
I want to report progress in the Work Item's Unit,
So that Remaining effort burns down as a fact and completion happens when Remaining reaches zero.

**Acceptance Criteria:**

**Given** a Work Item has an Effort `Meter(Unit, Estimated, Done)`
**When** state is inspected after replay
**Then** Remaining is derived as `Estimated - Done`
**And** Remaining is never represented below zero.

**Given** an executor reports a positive Done delta in the Work Item's Unit
**When** `ReportProgress` is handled for an estimated Work Item
**Then** `ProgressReported` is emitted
**And** replaying the event increases Done and decreases Remaining by the reported delta, clamped at zero.

**Given** progress causes Remaining to reach zero
**When** the event sequence is replayed
**Then** the Work Item transitions synchronously to `Completed`
**And** `WorkItemCompleted` is emitted as part of the accepted completion path.

**Given** a Work Item has no Estimated effort
**When** progress is reported
**Then** the item does not complete through the Remaining-equals-zero path
**And** completion requires an explicit complete act.

**Given** progress uses a negative delta or a Unit different from the Work Item's established Unit
**When** `ReportProgress` is handled
**Then** the command is rejected as a domain rejection
**And** replayed state is unchanged.

### Story 2.4: Re-Estimate and Reschedule Work

As an executor,
I want to re-estimate and reschedule a Work Item as first-class acts,
So that overruns, partial progress, priority changes, and due-date changes are recorded without treating them as errors.

**Acceptance Criteria:**

**Given** a Work Item has an established Effort Unit
**When** an executor re-estimates effort in the same Unit with a non-negative value
**Then** `ReEstimated` is emitted
**And** replayed state updates Estimated and derived Remaining consistently with existing Done.

**Given** a re-estimate uses a different Unit after the first estimate
**When** `ReEstimate` is handled
**Then** the command is rejected as a domain rejection
**And** the Work Item's Unit remains unchanged.

**Given** an executor changes Priority or Due Date
**When** `RescheduleWorkItem` is handled
**Then** `WorkItemRescheduled` is emitted
**And** replayed state reflects the new Schedule facts.

**Given** no Priority or Due Date is supplied
**When** the Work Item is replayed
**Then** the Schedule remains valid
**And** the future what's-next projection has enough data to sort the item last.

**Given** Priority is represented in the delivered version
**When** the contract is inspected
**Then** Priority uses the ordered enum shape selected by architecture
**And** no routing score, escalation band, LLM confidence, or cost policy is introduced.

### Story 2.5: Complete, Cancel, Reject, and Expire Work

As an executor or coordinator,
I want Work Items to terminate through explicit domain acts,
So that completion and abnormal endings are auditable, replayable, and enforce terminal-state rules.

**Acceptance Criteria:**

**Given** an estimated Work Item reaches Remaining zero through progress
**When** state is replayed
**Then** `WorkItemCompleted` makes the item terminal and later progress, schedule, assignment, or suspend commands emit an `IRejectionEvent`
**And** exact duplicate completion or terminal commands return `DomainResult.NoOp` only where `docs/lifecycle-transition-matrix.md` explicitly lists them as idempotent.

**Given** an unestimated Work Item is explicitly completed
**When** the complete act is handled
**Then** `WorkItemCompleted` is emitted
**And** completion does not rely on the Remaining-equals-zero rule.

**Given** a non-terminal Work Item is cancelled
**When** `CancelWorkItem` is handled
**Then** `WorkItemCancelled` is emitted
**And** the item becomes terminal with no further progress accepted.

**Given** a bound executor rejects an assignment
**When** `RejectWorkItem` is handled with the default requeue behavior
**Then** `WorkItemRejected` is emitted
**And** the item returns to `Queued` for reassignment.

**Given** a bound executor rejects an assignment as non-requeuable
**When** `RejectWorkItem` is handled
**Then** `WorkItemRejected` is emitted
**And** the item becomes terminal.

**Given** expiry is Due-Date or TTL driven
**When** an expiry command is handled
**Then** `WorkItemExpired` is emitted
**And** the item becomes terminal without the aggregate reading a clock.

**Given** cancel and expire may later cascade through a Work Tree
**When** the nine-status cancel/expire transition table is reviewed
**Then** every source status has an explicit decision
**And** already-terminal descendants are defined as unaffected for downstream cascade execution.

### Forward Candidate F2-A: Enforce One Authoritative Lifecycle

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith builder,
I want every Work Item act governed by one lifecycle definition,
So that commands, replay, projections, and user-facing evidence cannot disagree about Work Status (FR6).

**Acceptance Criteria:**

**Given** the normative lifecycle transition matrix and the lifecycle commands registered at this story boundary
**When** every currently implemented accepted cell is exercised from each applicable source Status
**Then** each act emits only its specified event and produces exactly the documented resting Status
**And** `Claim` is the sole transition into `InProgress`; no `WorkItemStarted` event or implicit progress-as-start path exists.

**Given** an act that is not legal from the current Status
**When** the aggregate handles it
**Then** the defined `IRejectionEvent` is returned with no success event or state mutation
**And** replaying the rejection does not alter the aggregate's domain state.

**Given** a Work Item already in `Completed`, `Cancelled`, `Rejected`, or `Expired`
**When** the exact terminal act that established that state is redelivered
**Then** the command is an acknowledged no-op with no additional event
**And** every different transition attempt from that terminal state is rejected.

**Given** an Assigned Work Item
**When** its Executor rejects the assignment with requeue enabled or disabled
**Then** one `WorkItemRejected` event rests the item at `Queued` by default or terminal `Rejected` when non-requeueable
**And** no separate `WorkItemQueued` event is emitted for the requeue outcome.

**Given** a lifecycle-neutral planning or reference act registered at the current story boundary
**When** any such currently registered act succeeds
**Then** the current Work Status is unchanged unless the governing contract explicitly defines a paired lifecycle event
**And** later stories that register another such act must add its matrix and projection evidence atomically rather than being prerequisites for this story.

**Given** a lifecycle contract change
**When** the change is submitted
**Then** the transition matrix, durable catalog and validators, aggregate Handle/Apply behavior, projections, golden corpus, and focused unit/integration/fitness tests change atomically
**And** CI fails if any duplicate transition table or contradictory expected Status remains.

**Given** builder-facing structured evidence or a future surface consumes a command result
**When** it presents state
**Then** Work Status uses exactly Created, Assigned, Queued, InProgress, Suspended, Completed, Cancelled, Rejected, or Expired
**And** command lifecycle states such as Acknowledged, Confirmed, or Rejected remain semantically distinct from Work Status.

### Forward Candidate F2-B: Assign, Queue, and Claim Through One Binding

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an Executor or tenant coordinator,
I want work to be pushed to one Party or pulled from a shared queue through the same binding model,
So that system, internal, and external doers can start work without executor-specific domain paths (FR6, FR17, FR18, FR19).

**Acceptance Criteria:**

**Given** a Created or Queued Work Item and a valid `ExecutorBinding(PartyId, Channel, AuthorityLevel)`
**When** an authenticated tenant member assigns it
**Then** `WorkItemAssigned` records the complete binding and the item rests at `Assigned`
**And** assigning during creation remains distinct: a binding on `WorkItemCreated` still rests at `Created` until this explicit act occurs.

**Given** an Assigned Work Item
**When** a coordinator assigns a different Party or changes Channel for the same Party
**Then** the same Assign operation emits one `WorkItemAssigned` and the latest binding becomes authoritative
**And** Status, Schedule, and Effort remain unchanged.

**Given** a Created or Assigned Work Item
**When** an authenticated tenant member queues or requeues it
**Then** `WorkItemQueued` records every explicit entry into the shared pool and the item rests at `Queued`
**And** because Queue is not a binding act, an existing last Executor Binding remains in aggregate state until Assign or Claim replaces it while `Queued` Status makes the item available to the tenant pool.

**Given** an Assigned Work Item
**When** its authenticated bound Party submits Claim
**Then** `WorkItemClaimed` moves it to `InProgress` while retaining that Party as the responsible Executor
**And** a different acting Party is denied inside the serialized actor turn before Handle or append, rather than receiving a domain rejection.

**Given** a Queued Work Item and an admitted tenant Party
**When** Claim is submitted
**Then** EventStore derives PartyId from authenticated context and Channel/AuthorityLevel from trusted tenant policy, emits `WorkItemClaimed`, and binds the winner
**And** no caller-provided Executor Binding is accepted as authority for the claim.

**Given** two admitted Parties concurrently claim the same Queued Work Item
**When** actor serialization and any bounded ETag retry complete
**Then** exactly one `WorkItemClaimed` is persisted and the losing command is re-evaluated into the current-state domain rejection
**And** retry exhaustion instead returns an infrastructure concurrency failure without a loser append or publication.

**Given** bindings for a system Party, internal Party, and external Party
**When** the same assign-to-claim sequence is executed for each
**Then** the event streams differ only in `PartyId`, `Channel`, and `AuthorityLevel` field values
**And** architecture fitness tests fail if Server or Projections branch on executor kind or require a new event type for an additive Channel/AuthorityLevel value.

**Given** any defined AuthorityLevel value
**When** assignment or claim authorization is evaluated in the delivered version
**Then** the value is persisted as descriptive binding data and does not grant access
**And** authenticated tenant membership and responsibility rules remain the authoritative authorization inputs.

### Forward Candidate F2-C: Track Effort in One Immutable Unit

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an Executor,
I want effort represented consistently as Estimated, Done, and Remaining in one Unit,
So that progress and later tree totals remain meaningful without silent conversion (FR3).

**Acceptance Criteria:**

**Given** a valid first estimate at Work Item creation or through the first accepted ReEstimate
**When** the estimate is applied
**Then** `WorkItemEffort(Unit, Estimated, Done)` becomes the sole delivered-version effort state and the supplied Unit is established immutably
**And** initial Done is zero unless a backward-compatible historical event explicitly records otherwise.

**Given** established Estimated and Done values
**When** Remaining is requested or state is replayed
**Then** Remaining is derived as `max(Estimated - Done, 0)` and is never stored as an independently authoritative aggregate field
**And** repeated derivation produces the same value without reading projections or external state.

**Given** a negative initial or replacement Estimated value
**When** effort validation runs
**Then** the command returns the defined domain rejection with no effort mutation or success event
**And** zero Estimated remains valid without implicitly completing the Work Item.

**Given** an established Unit
**When** progress, correction, or re-estimation supplies a different Unit
**Then** the command is rejected and Unit, Estimated, Done, Remaining, and Status remain unchanged
**And** no implicit conversion or replacement Unit is attempted.

**Given** a Work Item with no accepted estimate
**When** its effort is inspected
**Then** Estimated, Done, and Remaining are represented as unestablished rather than numeric zero
**And** the headless evidence contract can distinguish “Not estimated” from zero estimated work.

**Given** Done exceeds Estimated after an accepted planning or correction act
**When** effort is derived
**Then** the original Estimated and cumulative Done remain visible, Remaining is zero, and the current non-terminal Status is preserved unless a separate completion event exists
**And** consumers can identify the overrun without a misleading completion percentage.

**Given** delivered-version package contents
**When** architecture and contract tests inspect Burn-Down types
**Then** no second effort abstraction, Cost meter, conversion policy, or cost-specific domain dependency exists
**And** the existing shape remains suitable for adding a separately labeled Cost concern in its future horizon.

### Forward Candidate F2-D: Report Progress and Complete Work

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a responsible Executor,
I want to report completed effort or explicitly declare work complete,
So that completion is an attributable domain fact rather than an inferred status flag (FR6, FR7, FR8).

**Acceptance Criteria:**

**Given** an `InProgress` Work Item with an established Unit and positive Remaining
**When** its authenticated bound Executor reports a strictly positive delta in that Unit that leaves Remaining above zero
**Then** one `ProgressReported` event records the verbatim delta and optional note and cumulative Done increases
**And** Status remains `InProgress` while Remaining is derived from the updated effort.

**Given** a ReportProgress delta of zero or less, a different Unit, or a Work Item not in `InProgress`
**When** the command is handled
**Then** the defined domain rejection is returned with no success event
**And** Unit, Estimated, Done, Remaining, and Status remain unchanged.

**Given** an otherwise valid progress delta equals or exceeds current Remaining
**When** progress is accepted
**Then** `ProgressReported` is emitted first and `WorkItemCompleted(CompletionKind=Progress)` second in the same domain result
**And** applying both events rests the item at `Completed` with Roll-Up contribution semantics of zero.

**Given** an estimated or unestimated Work Item in `InProgress` or `Suspended`
**When** the authenticated bound Executor submits explicit Complete
**Then** one `WorkItemCompleted(CompletionKind=Explicit)` event rests it at `Completed`
**And** Estimated and Done remain exactly as last reported rather than being rewritten to manufacture zero Remaining.

**Given** a Work Item completed explicitly with residual or unestablished Remaining
**When** its state and authorized history are read
**Then** completion cause, historical Estimated, Done, and residual or unestablished Remaining remain distinguishable
**And** consumers do not render the residual fraction as completed progress even though terminal contribution is zero.

**Given** a caller who is a tenant member but is not the current bound Executor
**When** ReportProgress or Complete is submitted
**Then** authorization is denied inside the serialized actor turn before Handle or append
**And** no domain rejection, success event, or binding-derived actor impersonation occurs.

**Given** duplicate Complete delivery after the item is already Completed
**When** the same terminal intent is retried
**Then** it returns the defined acknowledged no-op without an additional history event
**And** a later ReportProgress attempt remains a domain rejection.

**Given** headless evidence consumes the accepted progress or completion result
**When** it formats the outcome
**Then** it names Estimated, cumulative Done, Remaining, Unit, exact Work Status, and completion cause as text
**And** it does not equate command acknowledgement with domain completion or rely on color or percentage alone.

### Forward Candidate F2-E: Correct Progress and Reopen Eligible Work

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a responsible Executor,
I want to correct cumulative progress without deleting the original report,
So that audited mistakes can be repaired and only progress-completed work reopens when effort remains (FR7, FR8).

**Acceptance Criteria:**

**Given** an estimated `InProgress` Work Item and its authenticated bound Executor
**When** `CorrectProgress` supplies an absolute cumulative Done value from zero through Estimated in the established Unit
**Then** one `ProgressCorrected` event records the new cumulative value and bounded optional note
**And** the original progress events remain unchanged in history while Status stays `InProgress`.

**Given** a Work Item completed through `CompletionKind=Progress`
**When** an authorized correction reduces cumulative Done below Estimated
**Then** `ProgressCorrected` is emitted followed by `WorkItemReopened` in the same accepted result
**And** applying both events restores positive Remaining and rests the item at `InProgress` without erasing its prior completion evidence.

**Given** a Work Item completed through progress
**When** an authorized correction leaves cumulative Done equal to Estimated
**Then** the correction evidence is recorded without `WorkItemReopened`
**And** Status remains `Completed` because Remaining is still zero.

**Given** a Work Item completed through `CompletionKind=Explicit`
**When** CorrectProgress is attempted
**Then** the command is rejected with no correction or reopen event
**And** explicit completion remains terminal even if historical effort shows residual Remaining.

**Given** an unestimated item, a negative absolute Done value, a Done value above Estimated, a different Unit, or an optional note longer than 1,000 characters
**When** CorrectProgress is handled
**Then** the defined domain rejection is returned without mutation
**And** the invalid submitted value remains available to headless or future client recovery without entering logs or durable state.

**Given** a caller who does not match the current Executor Binding
**When** CorrectProgress is submitted
**Then** authorization is denied inside the serialized actor turn before domain handling
**And** AuthorityLevel or a caller-supplied actor field cannot bypass the responsibility check.

**Given** historical completion events written before `CompletionKind` existed
**When** state is replayed
**Then** progress completion is inferred only from the immediately preceding progress-to-zero transition and all other historical completion remains explicit
**And** the inference is deterministic across aggregate state, projections, and golden-corpus tests.

**Given** authorized history or projection evidence after correction
**When** it is consumed
**Then** original progress, corrected cumulative Done, resulting Remaining, prior completion cause, and any reopen event remain separately observable
**And** downstream presentation can announce reopening once without rewriting or hiding the Raw Acts.

### Forward Candidate F2-F: Re-Estimate and Reschedule Work

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an authenticated tenant coordinator,
I want to revise effort and scheduling facts without changing lifecycle state,
So that plans can respond to new information while their history remains explicit (FR4, FR9, FR20).

**Acceptance Criteria:**

**Given** a non-terminal Work Item with an established Unit
**When** an authenticated tenant member submits ReEstimate with a non-negative absolute Estimated value in that Unit
**Then** `ReEstimated` records the replacement estimate and applying it derives the new Remaining
**And** the current Work Status is unchanged and no completion event is emitted, even when Remaining becomes zero.

**Given** a replacement estimate below the current cumulative Done
**When** ReEstimate is accepted under the architecture's authoritative numeric rule
**Then** applied Done is clamped consistently to the accepted Estimated bound and Remaining is zero
**And** the event, aggregate, projection folds, lifecycle matrix, validator, and golden corpus encode the same rule without an implicit completion.

**Given** an unestimated non-terminal Work Item
**When** its first valid ReEstimate supplies Estimated and Unit
**Then** the estimate is accepted and that Unit becomes immutable
**And** a later ReEstimate in another Unit is rejected without changing effort or Status.

**Given** a non-terminal Work Item
**When** an authenticated tenant member changes Priority, Due Date, or both
**Then** one `WorkItemRescheduled` event records the changed Schedule facts and Status remains unchanged
**And** at least one schedule value must actually change for a new event to be emitted.

**Given** Priority is supplied or absent
**When** schedule validation runs
**Then** accepted values are `Critical`, `High`, `Normal`, and `Low`, while absence remains valid
**And** an unknown or malformed durable value fails closed rather than being coerced to a known priority.

**Given** Platform resolves an effective expiry instant for create or reschedule
**When** the Schedule fact is admitted
**Then** the event persists that instant and the deterministic schedule witness required by later reminder handling
**And** aggregates never read tenant policy or wall-clock time to derive it.

**Given** a later Platform policy change
**When** historical Work Item events are replayed
**Then** their persisted Schedule and expiry witness produce the same state as originally accepted
**And** the new policy affects only future create or reschedule commands.

**Given** a terminal Work Item or an unauthenticated or cross-tenant caller
**When** ReEstimate or Reschedule is submitted
**Then** terminal invalidity produces the defined domain rejection, while identity failure is denied before Handle
**And** neither path mutates effort, Schedule, Status, or query state.

**Given** an accepted schedule change reaches the read side
**When** the what's-next projection catches up
**Then** ordering reflects the latest Priority and Due Date without storing a derived rank in the event
**And** command acknowledgement remains distinct from projection confirmation.

### Forward Candidate F2-G: Hand Off Active Work Without Changing Its State

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a responsible Executor,
I want to hand active work to another Party through one auditable operation,
So that responsibility can change without cancelling progress or losing suspended conditions (FR7, FR17, FR19).

**Acceptance Criteria:**

**Given** an `InProgress` Work Item and its authenticated bound Executor
**When** `HandoffWorkItem` supplies a different valid Executor Binding
**Then** one `WorkItemHandedOff` event records the previous and new responsibility and the new binding becomes authoritative
**And** Status remains `InProgress` while Effort, Schedule, parent/child references, and Conversation reference remain unchanged.

**Given** a `Suspended` Work Item with one or more Await-Conditions and its authenticated bound Executor
**When** HandoffWorkItem is accepted
**Then** Status remains `Suspended` and the entire Await-Condition set is preserved byte-for-byte
**And** only the Executor Binding and additive Raw-Act history change.

**Given** a Created, Assigned, Queued, Completed, Cancelled, Rejected, or Expired Work Item
**When** HandoffWorkItem is attempted
**Then** the lifecycle's defined domain rejection is returned without a success event
**And** callers use Assign or rebind for Assigned work rather than disguising it as active Handoff.

**Given** a tenant member who is not the current bound Executor
**When** HandoffWorkItem is submitted
**Then** authorization is denied inside the serialized actor turn before Handle or append
**And** caller-supplied actor data or AuthorityLevel cannot authorize the handoff.

**Given** the same Party changes Channel during active work
**When** the valid new binding is handed off
**Then** the operation uses the same `HandoffWorkItem` and `WorkItemHandedOff` contract as a Party-to-Party handoff
**And** no channel-specific event, lifecycle path, or executor-kind branch is introduced.

**Given** system, internal, and external Party bindings
**When** identical active-handoff scenarios are executed
**Then** resulting event streams differ only in binding field values
**And** an architecture fitness test fails if a new doer kind requires Server or Projection changes or a new event type.

**Given** authorized history after a confirmed handoff
**When** it is consumed by the headless evidence contract or a future presentation
**Then** old and new responsibility, authenticated actor, time, Status, and preserved effort and await facts remain distinguishable
**And** responsibility is not shown as changed before authoritative confirmation.

**Given** future Theme 4 routing is absent
**When** delivered-version Handoff behavior is built
**Then** the explicit accepted binding is applied without routing candidates, scores, escalation policy, or AuthorityLevel enforcement
**And** future routing may add its decision evidence without reshaping this operation.

### Forward Candidate F2-H: Discover the Next Work Deterministically

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an Executor or tenant coordinator,
I want a stable view of claimable and assigned work,
So that I can choose what to do next without hidden routing policy or replay-dependent ordering (FR4, FR6, FR18, FR20).

**Acceptance Criteria:**

**Given** an authenticated tenant member requests the coordinator view without an Executor PartyId
**When** the what's-next query executes
**Then** it returns exactly the target tenant's Work Items whose Status is `Assigned` or `Queued`
**And** query authorization and result filtering prevent items from any other tenant or unauthorized scope from appearing.

**Given** an authenticated tenant member requests an Executor view with a PartyId
**When** the query executes
**Then** it returns that Party's `Assigned` Work Items plus every `Queued` Work Item in the same tenant
**And** Assigned items bound to other Parties and all items in other Statuses are excluded.

**Given** result items with present or absent Schedule fields
**When** they are ordered
**Then** present Priority sorts `Critical`, `High`, `Normal`, `Low`, followed by absent Priority; within each group, earliest present Due Date precedes absent Due Date
**And** ordinal WorkItemId is the final deterministic tiebreak without a stored rank or cross-aggregate creation coordinate.

**Given** duplicate or out-of-order lifecycle, assignment, or schedule events
**When** the projection handler receives them
**Then** it applies only the newest valid position for the source Work Item and never duplicates or regresses a row
**And** acknowledgement occurs only after the read-model mutation or quarantine disposition is durable.

**Given** accepted Assign, Queue, Claim, Reject, terminal, or Reschedule evidence
**When** the projection catches up
**Then** the item enters, leaves, changes binding, or reorders exactly according to its latest authoritative state
**And** EventStore emits a payload-free changed-key notification only after the read-model change commits.

**Given** a query caller without tenant membership, with a mismatched tenant assertion, or without access to a matching result
**When** the query is evaluated
**Then** it fails closed before tenant existence or protected item data is disclosed
**And** logs and ProblemDetails omit Work Item payloads, Party data, and secret authorization details.

**Given** the delivered version has no routing implementation
**When** the query is executed
**Then** eligibility, score, cost, confidence, workload kind, and escalation policy do not filter or order results
**And** `IExecutorRouter` remains unwired without preventing the query from functioning.

**Given** two otherwise identical event histories are replayed into empty projection state
**When** the resulting views are compared
**Then** rows, filters, binding facts, and ordering are identical
**And** no derived ordering position is required in a durable domain event.

**Given** headless evidence renders a query result or a stale Claim outcome
**When** the view is consumed
**Then** it names exact Work Status, responsible Party reference, Channel, Priority, Due Date, Effort and Unit, freshness, and next legal action as available
**And** claim loss is presented as current domain evidence rather than a generic concurrency crash.

## Epic 3: Coordinate Durable Work Trees and Sagas

Coordinators can decompose work into tenant-safe trees, observe recursive progress, wait on durable conditions, and execute idempotent cascades and saga consequences.

### Story 3.1: Guard Tenant-Safe Work Tree Shape

As a coordinator,
I want Work Items to form a tenant-safe acyclic tree,
So that parent-child coordination cannot create loops, duplicate parents, or cross-tenant roll-up leaks.

**Acceptance Criteria:**

**Given** a Work Item is attached to a parent
**When** the parent-child relationship is validated
**Then** the child has at most one parent
**And** the relationship stores references by ID rather than embedding child state.

**Given** a proposed parent-child relationship would create a cycle
**When** the relationship is handled
**Then** the command is rejected as a domain rejection
**And** the existing Work Tree state is unchanged.

**Given** a proposed parent-child relationship crosses tenants
**When** the relationship is handled
**Then** the command is rejected as a domain rejection
**And** no projection or traversal can silently treat the items as same-tenant data.

**Given** a proposed relationship exceeds the configured maximum depth
**When** the relationship is handled
**Then** the command is rejected as a domain rejection
**And** the default maximum depth is documented as 32 unless overridden by tenant or type policy.

**Given** tree-shape validation is tested
**When** negative-path tests run
**Then** cycle, second-parent, cross-tenant, and maximum-depth cases are covered
**And** breadth is not capped by the domain guard.

### Story 3.2: Spawn Child Work from a Parent

As a coordinator,
I want a Work Item to spawn child work,
So that a larger obligation can be broken into smaller replayable obligations without losing parent context.

**Acceptance Criteria:**

**Given** a parent Work Item is eligible to spawn child work
**When** `SpawnChild` is handled
**Then** `ChildSpawned` is emitted on the parent
**And** the child creation request follows `CreateWorkItem` semantics with a parent reference.

**Given** child work is spawned
**When** the child Work Item is created
**Then** the child carries the same Tenant as the parent
**And** the parent reference is stored as a reference ID.

**Given** a parent optionally suspends while spawning a child
**When** the spawn request includes an await-on-child intent
**Then** the parent records an Await-Condition for the child completion
**And** no progress is accepted on the parent while it is `Suspended`.

**Given** the spawn request violates the tree guard
**When** `SpawnChild` is handled
**Then** no parent event and no child creation intent are accepted
**And** the rejection is replay-safe.

**Given** spawn behavior is tested
**When** events are replayed
**Then** parent state, child reference, and optional Await-Condition reconstruct deterministically.

### Story 3.3: Maintain Recursive Roll-Up with Per-Child Sequence

As an objective owner,
I want a parent Work Item to expose rolled Remaining effort across its subtree,
So that I can trust the all-in Remaining effort of an objective as descendants progress.

**Acceptance Criteria:**

**Given** a Work Tree has parent and child Work Items
**When** child progress, re-estimate, completion, or terminal events are projected
**Then** the parent exposes own Remaining and subtree rolled Remaining
**And** rolled Remaining equals own Remaining plus the recursive rolled Remaining of direct children.

**Given** child events are delivered more than once
**When** the Roll-Up projection processes duplicates
**Then** the projection does not double-count child contribution
**And** the projected value converges to the same result as a single delivery.

**Given** child events arrive out of order
**When** the Roll-Up projection compares child event sequences
**Then** stale or lower-sequence contributions are ignored
**And** the latest per-child contribution wins.

**Given** a child Work Item becomes terminal through completion, cancellation, rejection, or expiry
**When** the Roll-Up projection processes the terminal child event
**Then** that child contributes zero Remaining to its ancestors
**And** replaying the terminal event does not double-subtract the contribution.

**Given** Roll-Up state is exposed to consumers
**When** read-model contracts are inspected
**Then** own Remaining and rolled Remaining use distinct fields or types
**And** no consumer can confuse eventual rolled Remaining with aggregate-authoritative own Remaining.

**Given** Roll-Up correctness is tested
**When** property-style tests permute and duplicate child events
**Then** all permutations converge to the same projection result
**And** tenant equality is asserted at every traversal hop.

### Story 3.4: Preserve Heterogeneous Unit Subtotals

As an objective owner,
I want mixed-unit Work Trees to show separate subtotals,
So that Works never fabricates a misleading single Remaining-effort number across incompatible Units.

**Acceptance Criteria:**

**Given** a Work Tree contains only one Unit
**When** Roll-Up is projected
**Then** the subtree exposes a single rolled subtotal for that Unit.

**Given** a Work Tree contains multiple Units
**When** Roll-Up is projected
**Then** the subtree exposes one rolled subtotal per Unit
**And** no implicit conversion or summation across Units occurs.

**Given** a child changes effort through progress or re-estimate
**When** the child Unit matches its established Unit
**Then** the matching per-Unit subtotal updates incrementally.

**Given** a progress or re-estimate command carries a Unit incompatible with the child's established Unit
**When** the command is handled
**Then** the command is rejected before event emission
**And** no Roll-Up projection update is produced from that invalid act.

**Given** replay or delivery exposes an already-persisted child event whose Unit violates the child's established Unit contract
**When** the Roll-Up projection processes the event
**Then** the projection fails closed by refusing the incompatible contribution, retaining the last valid projected value or marking that Work Item projection degraded
**And** logs include only tenant, Work Item, event type, and sequence metadata, never payload values, and no mixed-unit Roll-Up view is published as fresh.

**Given** future UI surfaces need Burn-Down and Roll-Up data
**When** `RollUpView` or an equivalent read model is inspected
**Then** it exposes labeled per-Unit subtotals
**And** it does not expose a coerced all-unit total.

### Story 3.5: Suspend and Resume on Await-Conditions

As a coordinator,
I want a Work Item to suspend on one or more Await-Conditions and resume on the first matching trigger,
So that long-running work can park safely until a child completes, a date arrives, or an external signal is received.

**Acceptance Criteria:**

**Given** an `InProgress` Work Item
**When** it is suspended with one or more Await-Conditions
**Then** `WorkItemSuspended` records each Await-Condition kind and correlation key
**And** the item transitions to `Suspended`.

**Given** a Work Item is `Suspended`
**When** progress is reported before a matching resume
**Then** the progress command is rejected
**And** current Remaining still participates in Roll-Up.

**Given** a resume command carries a correlation key matching one current Await-Condition
**When** `ResumeWorkItem` is handled
**Then** `WorkItemResumed` is emitted with the consumed Await-Condition key, the item transitions back to `InProgress`, and all Await-Conditions from that suspension are cleared.

**Given** a `ResumeWorkItem` command carries no key matching the current Await-Condition set while the item is `Suspended`
**When** the command is handled
**Then** the command emits a domain rejection
**And** the item remains `Suspended`.

**Given** a `ResumeWorkItem` command repeats the consumed key from the accepted `WorkItemResumed` event
**When** the duplicate command is handled after the item has already resumed
**Then** the command returns `DomainResult.NoOp`
**And** no duplicate `WorkItemResumed` event is emitted.

**Given** child-completion resumes are required
**When** a child completes
**Then** the pure reactor translation can produce a parent `ResumeWorkItem` command intent for matching child-completion Await-Conditions
**And** the aggregate, not the reactor, decides whether the resume is accepted.

**Given** date and external resumes are required seams
**When** the contracts are inspected
**Then** `DateReached` and `ExternalSignal` Await-Condition cases exist
**And** the aggregate never reads a clock or calls an external adapter.

### Story 3.6: Cascade Terminal Work Through Active Descendants

As a coordinator,
I want cancellation and expiry of parent work to cascade through still-active descendants,
So that an open subtree cannot keep burning down after its parent has terminated.

**Acceptance Criteria:**

**Given** a parent Work Item is cancelled or expired
**When** descendants are still active
**Then** the cascade process can issue terminal command intents for those descendants
**And** the descendants apply their own transition rules through the aggregate.

**Given** a descendant is already terminal
**When** a parent cancellation or expiry cascade is processed
**Then** the descendant is unaffected
**And** no duplicate terminal event is emitted.

**Given** cascade terminal commands are delivered more than once to the same descendant
**When** the descendant aggregate handles a duplicate cancel or expire command for the already-applied terminal outcome
**Then** the command is idempotent according to `docs/lifecycle-transition-matrix.md`
**And** no duplicate terminal event is emitted.

**Given** the reactor translates parent terminal events
**When** its pure translation is tested
**Then** it emits only mechanical command intents
**And** it does not decide domain outcomes that belong in `Handle`.

**Given** Story 3.6 scope is reviewed
**When** cascade ownership is checked
**Then** the story covers aggregate transition behavior, idempotent target commands, tenant-safe descendant selection contracts, and pure mechanical command intents
**And** it does not implement Dapr dispatch, checkpoint persistence, AppHost restart recovery, reminder reconciliation, or Aspire recovery proof.

**Given** a parent and descendant belong to different tenants
**When** cascade traversal is attempted
**Then** tenant equality checks fail closed
**And** no cross-tenant terminal command is produced.

### Forward Candidate F3-A: Derive Stable Identities for Cross-Aggregate Effects

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith runtime integrator,
I want every cross-aggregate effect to have a deterministic durable identity,
So that Reactor, reminder, cascade, and recovery retries refer to the same logical command after crashes or redelivery (FR26).

**Acceptance Criteria:**

**Given** a committed source event and one target effect
**When** the EventStore-owned versioned codec derives its EffectId
**Then** it encodes tenant, source domain, source aggregate, source envelope sequence, effect kind, target domain, target aggregate, and immutable effect ordinal in fixed order
**And** no broker order, traversal order, random value, clock, payload Sequence, or handler-local state participates.

**Given** canonical text and numeric tuple values
**When** the tuple is encoded
**Then** text is canonical UTF-8 preceded by a four-byte big-endian length and integers are signed eight-byte big-endian
**And** SHA-256 renders most-significant-bit first through the specified Crockford alphabet with final zero-bit padding as exactly 52 uppercase characters.

**Given** an effect identifier has been derived
**When** gateway identifiers are created
**Then** EffectId remains the unprefixed 52-character digest and MessageId and IdempotencyKey both use `wrk-<EffectId>`
**And** each identifier satisfies gateway-safe validation without lossy truncation or alternate encoding.

**Given** multiple effects of the same kind originate from one event
**When** effect ordinals are assigned
**Then** each ordinal is an immutable catalog value for source event, effect kind, and target role
**And** reordering a descendant page or handler iteration cannot change an existing effect identity.

**Given** the same canonical effect tuple is processed by Reactor, reminder, recovery, and test code
**When** each implementation invokes the shared codec
**Then** every caller produces the identical EffectId, MessageId, IdempotencyKey, and semantic command digest
**And** Works contains no private copy of the encoding algorithm.

**Given** noncanonical Tenant, identifier, instant, or text input
**When** effect identity validation runs
**Then** the operation fails before command submission rather than normalizing or hashing ambiguous bytes
**And** bounded diagnostics identify the invalid field without logging the command payload or confidential values.

**Given** every registered effect family, including child creation, attachment bookkeeping, child resume, reminder callback, and cascade
**When** golden-vector tests execute across supported packages and runtimes
**Then** the tuple, semantic digest, EffectId, MessageId, and IdempotencyKey match checked-in expectations
**And** producers cannot be registered until their vectors and versioned catalog entries pass.

### Forward Candidate F3-B: Submit Cross-Aggregate Commands Idempotently

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Reactor developer,
I want target command submission to remember each effect's durable disposition,
So that redelivery cannot duplicate a logical cross-aggregate act or reinterpret it after recovery (FR26).

**Acceptance Criteria:**

**Given** a valid internal command with its EffectId, full canonical tuple, semantic command digest, causation, and workload delegation
**When** EventStore dispatches it to the target actor partition
**Then** target events and metadata plus a private effect-inbox receipt commit atomically
**And** acknowledgement occurs only after that atomic commit succeeds.

**Given** the target command succeeds, rejects, or is a defined no-op
**When** its receipt is persisted
**Then** the receipt records EffectId, full tuple, semantic digest, exact disposition, workload, delegation purpose, and causation
**And** no receipt event is appended to a Work Item or Work-Tree Registry domain stream.

**Given** the same EffectId, tuple, and semantic digest are retried
**When** the target receipt already exists
**Then** EventStore returns the recorded success, rejection, or no-op disposition without redispatching Handle
**And** no duplicate target event, side effect, or publication is created.

**Given** an existing EffectId is retried with a different tuple or semantic command digest
**When** the inbox compares the submission with its receipt
**Then** the operation fails as an identity conflict and is captured in the tenant-scoped quarantine
**And** it cannot execute, overwrite the receipt, or masquerade as an ordinary domain rejection.

**Given** a workload-delegation token accompanies the command
**When** EventStore validates submission
**Then** signature, audience, expiry, exact EffectId and tuple, target, command type and digest, tenant, purpose, causation, and mTLS-attested application identity must all match
**And** an unlisted origin-command pair is denied and audited before target dispatch.

**Given** an effect receipt and its source and target domain evidence
**When** lifecycle cleanup, legal hold, offboarding, backup, or restore occurs
**Then** the receipt is not pruned with the Work Item lifecycle and follows the tenant's approved retention class
**And** receipt and corresponding source and target evidence erase only as one authorized tenant operation.

**Given** a replay request predates the retained source-evidence floor
**When** it attempts to resubmit the effect
**Then** the request rejects or quarantines rather than executing against unverifiable history
**And** gateway terminal records may optimize lookup but are not treated as the correctness boundary.

**Given** success, rejection, no-op, duplicate, conflict, crash-before-commit, and crash-after-commit integration scenarios
**When** the focused test lane runs
**Then** persisted target state and receipt contents match the expected end state for each scenario
**And** tests assert durable contents rather than only response codes or mock invocation counts.

### Forward Candidate F3-C: Run Mechanical Reactor Effects Durably

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Works process author,
I want Reactor translations executed by a checkpointed EventStore process runner,
So that cross-aggregate coordination survives duplicate delivery, paging, and process restarts without a second domain kernel (FR26).

**Acceptance Criteria:**

**Given** the generic process-runner capability is absent from the current EventStore SDK
**When** this story begins implementation
**Then** the owning EventStore repository first publishes a named package or API contract and focused provider test for checkpointed event-to-command processing
**And** the Works consumer is not marked complete against an unpublished or test-only seam.

**Given** a committed Works or Registry event plus explicit durable process state
**When** the Works Reactor translates it
**Then** the translation returns only deterministic target commands and effect ordinals
**And** it contains no policy decision, external I/O, clock read, generated identity, or business branch that belongs in a target aggregate.

**Given** a page of source events
**When** the process runner reads and validates the page
**Then** it treats EventStore `FromSequence` as an exclusive lower bound and reuses the last returned sequence without incrementing it for the next page
**And** domain, canonical tenant, aggregate, strictly increasing envelope position, and full state-affecting decoding are validated before translation.

**Given** a translated event produces one or more target commands
**When** the runner submits them
**Then** it derives each command's stable identity through the shared codec and uses the idempotent submission seam
**And** the source checkpoint advances only after every required disposition or quarantine capture is durable.

**Given** a crash occurs after a source event is read but before all effects and checkpoint state commit
**When** the process restarts
**Then** it resumes from the durable checkpoint and reissues outstanding commands with the identical EffectIds
**And** target receipts prevent duplicate logical effects while allowing the checkpoint to converge.

**Given** duplicate or out-of-order source delivery
**When** Reactor processing occurs
**Then** per-stream positions, effect receipts, and target state-machine behavior prevent duplicated or regressed effects
**And** broker ordering is never a correctness assumption.

**Given** an internal Reactor submission
**When** it reaches EventStore
**Then** it carries a short-lived workload delegation for the exact tenant, target, command, purpose, and causation
**And** ordinary users, unrelated workloads, borrowed user identities, and global-admin shortcuts cannot invoke origin-restricted commands.

**Given** a translation needs many descendants or relationships
**When** it executes
**Then** it consumes stable cursor-paged data through the domain-owned topology reader and checkpoints page progress
**And** it never relies on an unbounded in-memory traversal loop.

**Given** focused crash, page-boundary, duplicate, reorder, rejection, no-op, quarantine, and restart tests
**When** the producer and Works consumer lanes run
**Then** durable target state, receipts, and checkpoints prove no lost or duplicated effects
**And** compilation alone is not accepted as process-runner evidence.

### Forward Candidate F3-D: Reserve Only Valid Work-Tree Attachments

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a builder coordinating related work,
I want the Work-Tree Registry to reserve only valid parent-child edges,
So that concurrent callers cannot create cycles, multiple parents, cross-tenant trees, or unbounded depth (FR5, FR13, FR16).

**Acceptance Criteria:**

**Given** a tenant's Work-Tree Registry
**When** its aggregate identity and stream route are resolved
**Then** it uses canonical identity `(tenant, domain=work-tree, aggregateId=registry)` and the registered Registry command and event contracts
**And** no Work Item stream, caller projection, or alternate registry becomes a second topology authority.

**Given** a caller requests child attachment
**When** the public reserve command is constructed
**Then** it carries the complete child-creation payload verbatim, including Obligation, optional effort and Unit, Schedule, binding, Conversation and Expectation references, child identity, and suspend intent
**And** a versioned canonical digest binds that payload without interpreting it inside the Registry.

**Given** a proposed parent and child
**When** EventStore obtains a ParentAdmissionWitness inside the serialized parent turn
**Then** the witness binds tenant, parent, child, payload digest, suspend flag, and parent envelope sequence
**And** `suspend=false` requires a non-terminal parent while `suspend=true` requires an `InProgress` or already `Suspended` parent before reservation dispatch.

**Given** trusted tenant policy for MaxDepth, reservation timeout, and topology quota
**When** reservation is admitted
**Then** the Registry evaluates single-parent, acyclic, single-tenant, and configured depth rules against its own rehydrated state
**And** missing policy, exhausted quota, invalid canonical tenant, cross-tenant references, a second parent, a cycle, or excessive depth rejects before `EdgeReserved`.

**Given** two concurrent commands try to attach the same child to different parents
**When** the tenant Registry actor serializes them
**Then** exactly one valid edge can become Reserved and every competing attempt receives a deterministic current-state rejection
**And** no child Work Item is created for a rejected reservation.

**Given** a valid reservation
**When** `EdgeReserved` is applied
**Then** Registry state records the edge as `Reserved` with the complete payload digest and required admission evidence
**And** Reserved edges remain invisible to the authoritative Work Tree, Roll-Up, child resume, and cascade.

**Given** the parent changes after admission but before the Registry handles an identical retry
**When** the same ParentAdmissionWitness is replayed
**Then** exact witness retry is idempotent and produces the same reservation disposition
**And** later reachable parent-state outcomes are handled by fenced attachment rules rather than trusting new caller-supplied ancestry facts.

**Given** arbitrary breadth beneath a tenant
**When** reservations remain within the Platform topology quota
**Then** the domain imposes no separate fan-out cap and the Registry remains snapshot and replay bounded with observable saturation and backpressure
**And** quota-level latency evidence is required before production admission.

### Forward Candidate F3-E: Create and Attach a Reserved Child

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a builder coordinating a Work Tree,
I want a valid reservation to create and attach exactly the intended child,
So that only durable fenced evidence can extend the authoritative tree (FR5, FR12, FR13, FR16, FR26).

**Acceptance Criteria:**

**Given** an `EdgeReserved` record ready for child creation
**When** the Registry authorizes creation
**Then** `ChildCreateAuthorized` moves the reservation to `Creating` and establishes a stable ReservationId plus monotonic FencingToken
**And** a Creating reservation cannot be timeout-released or replaced by another attachment attempt.

**Given** a Creating reservation
**When** the Reactor submits `CreateWorkItem` for the child
**Then** the command carries the exact tenant, parent, child, ReservationId, FencingToken, reserved-payload digest, and reserved child values
**And** EventStore revalidates the reservation token immediately before dispatch through the idempotent effect seam.

**Given** an ordinary caller submits parent-bearing CreateWorkItem without valid Registry authorization
**When** the command reaches the gateway
**Then** it is denied and audited before aggregate Handle or append
**And** no child stream or topology evidence is created.

**Given** a reserved child with no explicit Unit and a parent whose Unit is established
**When** child creation applies its first estimate
**Then** the child inherits the parent's Unit for that estimate
**And** a different-Unit child is accepted only when its Unit was explicit in the reserved payload.

**Given** child creation succeeds
**When** `WorkItemCreated` is persisted
**Then** its durable evidence exactly matches tenant, parent, child, ReservationId, FencingToken, and reserved-payload digest
**And** the Registry accepts no incomplete, mismatched, inferred, or caller-asserted evidence as attachment proof.

**Given** valid durable child-created evidence
**When** the Registry processes it
**Then** `EdgeAttached` makes that exact edge authoritative and the Reactor submits token-bearing parent bookkeeping
**And** only after attachment may the child appear in topology reads, Roll-Up, child-completion resume, or cascade.

**Given** the parent is non-terminal when attached bookkeeping arrives
**When** its origin-restricted SpawnChild act is handled
**Then** it idempotently emits `ChildSpawned` and records the child reference in every permitted parent Status
**And** a requested suspend adds `ChildCompleted(child)` while moving `InProgress` to `Suspended` or unioning the condition into an already Suspended set.

**Given** the parent became Completed, Cancelled, Rejected, or Expired after admission
**When** Attached bookkeeping arrives
**Then** the edge remains authoritative and parent terminal state is unchanged
**And** Cancelled or Expired parents durably schedule matching child-cascade catch-up before acknowledgement.

**Given** duplicate child-create, attachment, or parent-bookkeeping delivery for the identical fenced reservation
**When** processing repeats
**Then** effect receipts and target state return the recorded disposition or defined no-op without duplicate streams, edges, child references, or Await-Conditions
**And** a delayed command with a released, superseded, or mismatched token cannot mutate either child or topology.

### Forward Candidate F3-F: Suspend Work on Multiple Await-Conditions

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a responsible Executor,
I want to park active work on one or more explicit conditions,
So that durable coordination can wait without polling, blocking a process, or losing what must happen next (FR5, FR14).

**Acceptance Criteria:**

**Given** an `InProgress` Work Item and its authenticated bound Executor
**When** Suspend supplies a non-empty set of valid typed Await-Conditions
**Then** `WorkItemSuspended` records the complete set and the item rests at `Suspended`
**And** duplicate identical conditions are normalized according to the durable set contract without losing distinct conditions.

**Given** ChildCompleted, DateReached, and ExternalSignal Await-Conditions
**When** their values are validated and serialized
**Then** each carries an explicit kind and canonical correlation key or UTC instant
**And** equal text in different kinds does not make the conditions equal or interchangeable.

**Given** a date-based Await-Condition
**When** Suspend is handled or state is replayed
**Then** the persisted target instant is consumed as command data
**And** aggregate Handle reads no wall clock, scheduler, tenant policy, or external system.

**Given** a Suspended Work Item
**When** ReportProgress is attempted
**Then** the lifecycle returns the defined domain rejection without changing effort or Await-Conditions
**And** explicit Complete and authorized Handoff remain available only according to their independently defined rules.

**Given** a Work Item in any Status other than `InProgress`
**When** an ordinary Suspend command is submitted
**Then** it is rejected without adding or replacing Await-Conditions
**And** the internal fenced attachment path may union a reserved ChildCompleted condition only through its separately authorized bookkeeping contract.

**Given** a tenant member who is not the bound Executor
**When** Suspend is submitted
**Then** authorization is denied inside the serialized actor turn before Handle or append
**And** no caller-provided actor or AuthorityLevel value can substitute for the authenticated responsibility match.

**Given** an optional suspension note
**When** its length is at most 1,000 characters
**Then** the verbatim note is retained in the Raw Act, while a longer note is domain-rejected without state mutation
**And** the note is never treated as a comment thread, condition expression, or executable instruction.

**Given** suspended state is consumed by headless evidence or a future detail view
**When** it is rendered
**Then** every active Await-Condition and exact `Suspended` Status can be named independently
**And** the evidence does not claim a trigger has resumed the item before `WorkItemResumed` is authoritative.

### Forward Candidate F3-G: Resume Only on an Exact Matching Trigger

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an Executor waiting on external progress,
I want the first matching trigger to resume my Work Item exactly once,
So that duplicate, unrelated, or ambiguous signals cannot advance durable work (FR5, FR14, FR15, FR26).

**Acceptance Criteria:**

**Given** a Suspended Work Item with multiple active Await-Conditions
**When** an authorized Resume command carries a condition whose kind and key exactly match one member
**Then** `WorkItemResumed` records the consumed condition, clears the entire active set, and returns the item to `InProgress`
**And** no second condition can win after that accepted transition.

**Given** a Suspended Work Item
**When** Resume carries a correlation key whose text matches but whose condition kind differs
**Then** the command produces the defined domain rejection and the complete active set remains intact
**And** ExternalSignal, ChildCompleted, and DateReached never satisfy one another implicitly.

**Given** a Suspended Work Item and a condition that matches none of its active set
**When** Resume is handled
**Then** it produces the defined domain rejection with no success event or mutation
**And** the delivery is not acknowledged as an idempotent success merely because it may be retried.

**Given** a successful resume has recorded its last consumed condition
**When** that exact condition is delivered again
**Then** the command returns the sole resume-specific idempotent no-op without a second event
**And** the aggregate retains only the bounded last-consumed value rather than an unbounded trigger history.

**Given** a Work Item is not Suspended after a successful resume or terminal transition
**When** any condition other than the exact last consumed one is submitted
**Then** Resume is domain-rejected without changing Status or history
**And** adapters all observe the same disposition from the aggregate rather than defining local duplicate rules.

**Given** a child-completion, date, or external-signal Resume
**When** it reaches the gateway
**Then** its named workload or adapter origin, tenant delegation, purpose, causation, target, and command digest must satisfy the origin policy
**And** an ordinary user or wrong workload is denied before Handle, even with a valid matching condition.

**Given** an Attached child emits `WorkItemCompleted`
**When** the Reactor resolves its exact parent through `IWorkTreeTopologyReader`
**Then** it submits a deterministic Resume carrying `ChildCompleted(childId)` to that parent through the durable process runner
**And** missing or merely Reserved topology is retryable and remains unacknowledged rather than being treated as no parent.

**Given** a child has completed or a date trigger has fired but the Resume effect has not committed
**When** parent state is read
**Then** the parent remains authoritatively `Suspended` until `WorkItemResumed` lands
**And** headless or future presentation may show “trigger received; resume pending” only when durable coordination evidence supports it.

**Given** duplicate and reordered trigger deliveries plus aggregate replay
**When** focused tests reconstruct state and re-run each condition
**Then** exactly one matching trigger produces `WorkItemResumed`, its duplicate no-ops, and every nonmatch rejects
**And** the results are identical across child, date, and external condition kinds without requiring a production external adapter in the delivered version.

### Forward Candidate F3-H: Resume or Expire from Durable Schedule Witnesses

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an Executor or coordinator,
I want date waits and expiry to survive restarts and rescheduling,
So that time-based work advances only from current durable evidence rather than an in-memory clock (FR4, FR10, FR15, FR26).

**Acceptance Criteria:**

**Given** the generic typed-reminder and reconciliation seam is absent from the current EventStore SDK
**When** implementation begins
**Then** the owning EventStore repository first publishes the named package or API contract and focused registration, callback, cancellation, and recovery tests
**And** the Works consumer is not completed against bespoke local reminder plumbing.

**Given** an accepted lifecycle or Schedule event requires DateResume or Expiry work
**When** Works translates it
**Then** it emits a typed `PendingWorkIntent` containing kind, canonical tenant and item, due UTC instant, ScheduleToken, source envelope sequence, and typed callback payload
**And** EventStore—not the aggregate—owns generic registration and reconciliation.

**Given** a Work Item reminder actor is addressed
**When** its actor identity is derived
**Then** it uses the registered application, actor type, and `wra-<digest>` identity computed by the shared canonical codec
**And** a stored full-tuple mismatch for the digest is treated as collision plus quarantine.

**Given** create or reschedule establishes an effective expiry instant
**When** the corresponding Expiry intent is built
**Then** its ScheduleToken and reminder name bind tenant, item, UTC ticks, and state-change schedule revision
**And** rescheduling cancels or supersedes stale intent mechanically without rewriting historical policy facts.

**Given** a DateResume or Expiry reminder fires
**When** its callback submits Resume or Expire
**Then** the command carries the current persisted schedule witness, deterministic effect identity, named workload delegation, tenant purpose, and causation
**And** the aggregate changes state only after EventStore validates the current witness and designated origin.

**Given** an older reminder fires after reschedule, cancellation, completion, or another accepted resume
**When** its callback is processed
**Then** the witness mismatch or current lifecycle produces the defined rejection or no-op disposition without stale state change
**And** repeated delivery returns the durable effect receipt rather than registering a duplicate logical act.

**Given** a crash, scheduler outage, missed firing, or lost discovery-index entry
**When** periodic reconciliation runs
**Then** authoritative streams and pending intent state rediscover and resubmit every outstanding current reminder
**And** tenant and pending indexes are treated only as bounded discovery aids with CAS-protected updates and audited prune or offboarding.

**Given** Platform composes the reminder runtime
**When** production readiness is evaluated
**Then** Platform owns scheduler persistence, high availability, backup, callback failure policy, tenant timing policy, trust configuration, and alerts
**And** no production admission is allowed without those owners and a successful restore and reconciliation proof.

**Given** security tests cover reminder routes
**When** direct-port, wrong-application, wrong-tenant, wrong-purpose, expired-delegation, and valid production-policy callbacks execute
**Then** every invalid origin fails before dispatch and the valid callback reaches the correct tenant and item command path
**And** audit evidence contains bounded identifiers and causation without payload contents or tokens.

### Forward Candidate F3-I: Terminate Work and Cascade Across Attached Descendants

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a tenant coordinator,
I want cancellation, rejection, and expiry to have explicit and recoverable consequences,
So that abnormal termination is auditable and open descendant work eventually reaches a consistent outcome (FR5, FR10, FR26).

**Acceptance Criteria:**

**Given** a Work Item in any non-terminal Status and an authenticated tenant member
**When** Cancel is accepted
**Then** one `WorkItemCancelled` event makes the item terminal and rejects later progress or lifecycle advancement
**And** cancellation authorization does not derive from AuthorityLevel or a caller-supplied actor field.

**Given** a Work Item in any non-terminal Status and an authorized Expiry workload carrying the current ScheduleToken
**When** Expire is accepted
**Then** one `WorkItemExpired` event makes the item terminal, including when it was Suspended
**And** a passed Due Date, stale token, ordinary-user request, or unverified workload cannot expire it.

**Given** an Assigned Work Item and its authenticated bound Executor
**When** Reject is submitted with default requeue or explicit non-requeue intent
**Then** one `WorkItemRejected` rests the item at `Queued` or terminal `Rejected` respectively
**And** Reject from another Status or actor follows the defined domain-rejection or authorization-denial path without mutation.

**Given** a parent becomes Cancelled or Expired
**When** the Reactor processes the terminal event
**Then** it enumerates only authoritative Attached descendants through stable cursor-paged topology and submits the same terminal intent to each still-active descendant
**And** Reserved or Creating edges are excluded until attachment evidence triggers its separately durable cascade catch-up.

**Given** a cascade page contains multiple descendants
**When** commands are submitted
**Then** every effect uses its immutable catalog ordinal, deterministic EffectId, workload delegation, and target receipt
**And** page progress is checkpointed so a crash safely reissues unfinished effects without restarting an unbounded in-memory traversal.

**Given** the eventual window after a parent's Cancel or Expire but before a descendant receives its command
**When** that descendant reports progress or completes under its own current state
**Then** the act is accepted or rejected solely by that descendant's aggregate and remains a legitimate Raw Act
**And** a descendant that reaches any terminal Status before cascade is left unchanged by the later command.

**Given** duplicate cascade delivery or Reactor restart
**When** the same descendant effect is processed again
**Then** the stored disposition or exact duplicate terminal no-op is returned without another termination event
**And** the process checkpoint converges only after every required descendant disposition is durable.

**Given** a terminal parent receives a late Attached edge
**When** attachment processing completes
**Then** Cancelled or Expired parent state schedules matching child-cascade catch-up before acknowledgement
**And** Completed or Rejected parent state remains terminal without inventing a cascade rule not defined by the product contract.

**Given** headless or future presentation asks to terminate a parent
**When** confirmation and eventual results are shown
**Then** Cancel or Expire discloses descendant propagation, default Reject distinguishes requeue from terminal rejection, and active descendants may be labeled pending propagation
**And** no child is visually marked terminal before its own authoritative event arrives.

### Forward Candidate F3-J: Recover and Repair Tree Coordination Safely

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a platform operator,
I want incomplete or conflicting tree coordination to remain recoverable and visible,
So that crashes and malformed evidence cannot create ghost edges or silently strand work (FR5, FR10, FR13, FR15, FR16, FR26).

**Acceptance Criteria:**

**Given** a Reserved edge has no child-creation authorization or durable child evidence before its trusted timeout
**When** periodic recovery evaluates it
**Then** the Registry may emit `EdgeReleased` and the reservation becomes terminally invisible to tree consumers
**And** a later command carrying the released token cannot create or attach a child.

**Given** a reservation has reached `Creating`
**When** its expected child evidence is delayed, missing, malformed, or conflicting
**Then** recovery never timeout-releases or reuses its fencing token
**And** it durably quarantines the unresolved coordination while preserving evidence for retry or audited repair.

**Given** a quarantined Creating reservation
**When** an authenticated operator performs repair
**Then** the operator may attach only verified matching child evidence or mark a proven permanent conflict as orphaned through the registered disposition path
**And** every repair decision, origin, tenant delegation, evidence reference, and result is appended to the privileged audit sink before mutation is acknowledged.

**Given** Registry rebuild finds cyclic, multiply parented, cross-tenant, or asymmetric historical claims
**When** topology is materialized
**Then** the Registry stream remains authoritative while affected subtree fan-out, cascade, and Roll-Up are suppressed
**And** consumers receive explicit Unavailable or degraded state until authenticated repair resolves the contradiction.

**Given** live, replay, repair, or rebuild processing reads source pages
**When** the shared recovery validator encounters a wrong domain, noncanonical tenant, wrong aggregate, non-increasing position, or undecodable state-affecting record
**Then** it captures a durable tenant-scoped quarantine item before acknowledging the delivery
**And** no path silently skips, partially applies, or locally reinterprets the evidence.

**Given** bounded hot retries have been exhausted
**When** unresolved coordination remains
**Then** recovery health stays degraded, bounded kind and reason metrics and alerts remain active, and periodic retry continues
**And** readiness becomes healthy only after an authenticated audited disposition and successful catch-up.

**Given** a backup restore or process-host migration
**When** Reactor processing restarts
**Then** process-effect checkpoints restore separately and reconcile against source events, target receipts, pending intents, and deterministic EffectIds
**And** missing checkpoint state causes safe reissue rather than inferred success or duplicated effects.

**Given** operators inspect reservation, attachment, resume, reminder, or cascade recovery
**When** status evidence is rendered
**Then** Reserved, Creating, Attached, Released or superseded, delayed-leg rejection, pending resume or cascade, quarantine, and recovered states remain distinguishable as text
**And** diagnostics expose bounded identifiers, freshness, and a safe next action without payloads, secrets, stack traces, or trust internals.

**Given** property and integration tests inject duplicate delivery, reordering, page boundaries, crashes, stale tokens, malformed evidence, and operator disposition
**When** the recovery suite completes
**Then** persisted Registry state, child streams, effect receipts, process checkpoints, quarantine records, and readiness state match the expected end state
**And** no test passes solely from response codes, mock counts, sleeps, or assumed broker ordering.

## Epic 4: Assign, Discover, and Operate Work Reliably

Executors and operators can bind, claim, query, dispatch, recover, and observe work through tenant-safe runtime paths with deterministic outcomes.

### Story 4.1: Bind Work to a Uniform Party Executor

As a Hexalith builder,
I want every executor to be represented by one `ExecutorBinding`,
So that system agents, internal users, and external parties use the same domain model.

**Acceptance Criteria:**

**Given** the executor binding contract is inspected
**When** it is used by Work Item commands, events, state, and read models
**Then** it contains `PartyId`, `Channel`, and `AuthorityLevel`
**And** it does not contain an executor-kind-specific subtype or branch discriminator.

**Given** a Work Item is assigned to a system, internal user, or external party
**When** the binding is persisted
**Then** the same value-object shape is used for all three cases
**And** the only variation is field values such as Party ID, Channel, and AuthorityLevel.

**Given** `AuthorityLevel` is carried in the delivered version
**When** create, assign, or reassign events are replayed
**Then** AuthorityLevel is preserved in state and read models
**And** no delivered-version behavior branches on AuthorityLevel.

**Given** future UI surfaces need a single Party-chip treatment
**When** read-model contracts are inspected
**Then** executor kind, Channel, and AuthorityLevel are exposed as data
**And** no separate model is required for bot, human, or external-executor presentation.

**Given** architecture-fitness tests run
**When** domain code is scanned
**Then** there is no branch on executor kind
**And** no LLM, routing, email, MCP, UI, or security adapter is introduced for executor binding.

### Story 4.2: Assign, Reassign, and Hand Off Work

As a coordinator,
I want to assign and hand off work through one operation,
So that moving work between a bot, a colleague, or an external party is symmetric and auditable.

**Acceptance Criteria:**

**Given** a Work Item can accept assignment
**When** `AssignWorkItem` is handled with an `ExecutorBinding`
**Then** `WorkItemAssigned` is emitted
**And** replayed state contains the supplied binding.

**Given** a Work Item is already assigned
**When** `AssignWorkItem` is handled with a different `ExecutorBinding`
**Then** the same command path handles reassignment
**And** no executor-kind-specific handoff command is required.

**Given** work is handed off from a human executor to a system executor or back
**When** events are replayed
**Then** the latest binding is authoritative for future executor acts
**And** the event history preserves each handoff as Raw-Act evidence.

**Given** an assigned Work Item is returned to the shared pool
**When** it is requeued
**Then** `WorkItemQueued` is emitted
**And** the item becomes claimable according to lifecycle rules.

**Given** assignment is attempted from a terminal state
**When** the command is handled
**Then** the command emits an `IRejectionEvent`
**And** no binding mutation occurs after terminal closure.

### Story 4.3: Claim Queued Work with Single-Claim-Wins

As an executor,
I want to claim work from a shared queue,
So that system agents and people can pull from the same backlog without double ownership.

**Acceptance Criteria:**

**Given** a Work Item is `Queued`
**When** an executor claims it with an `ExecutorBinding`
**Then** `WorkItemClaimed` is emitted
**And** the item transitions to `InProgress` bound to the claimant.

**Given** two executors race after EventStore has rehydrated the same persisted `Queued` state
**When** their candidate updates contend on EventStore's Dapr state-store ETag
**Then** exactly one atomic save succeeds and EventStore retries the conflict from freshly rehydrated state
**And** the normal retry path produces the existing `WorkItemTransitionRejected(InProgress, "Claim")`, while retry exhaustion surfaces an infrastructure `ConcurrencyConflict` without loser append, publication, or dead-letter side effects.

**Given** a Work Item is not `Queued`
**When** an executor attempts to claim it
**Then** the command is rejected as not claimable
**And** no binding or status change occurs.

**Given** claim eligibility filtering is deferred to Theme 4
**When** delivered-version claim behavior is inspected
**Then** any executor in the tenant may claim a queued item
**And** no routing score, eligibility engine, escalation ladder, or AI decision record is implemented.

**Given** claim behavior is tested
**When** deterministic concurrency tests run
**Then** they exercise the EventStore actor and persister conflict injector and prove save, retry, re-handle, publication, and retry-exhaustion behavior without timing-dependent thread races
**And** no Works command exposes an expected-version or ETag field.

### Story 4.4: Resolve the Tenant's What's Next Queue

As an executor,
I want to ask what work is next for a tenant,
So that assigned and claimable work can be ordered without introducing a routing engine.

**Acceptance Criteria:**

**Given** a tenant has `Queued` and `Assigned` Work Items
**When** the what's-next query is executed
**Then** the query returns only that tenant's eligible queued and assigned items
**And** query-side authorization and result filtering are applied in addition to tenant scoping.

**Given** returned Work Items have Priority and Due Date values
**When** the query orders results
**Then** it sorts by Priority, then earliest Due Date, then creation order
**And** items with neither Priority nor Due Date sort last.

**Given** returned Work Items include Burn-Down, Status, and executor data
**When** read-model contracts are inspected
**Then** they expose Status, own Remaining, rolled Remaining where available, Executor Binding fields, and Await-Condition data without UI-specific types.

**Given** projection updates occur
**When** Work Item events change queue eligibility or ordering
**Then** the projection emits change notifications or uses the substrate notifier seam so future SignalR surfaces can update live
**And** no web shell, DataGrid, MCP, chatbot, or email surface is built in the delivered version.

**Given** cross-tenant data exists with colliding IDs or similar schedules
**When** the query is executed for one tenant
**Then** no item from another tenant is returned
**And** logs do not expose payloads or personal data.

### Story 4.5: Prove the Command/Event Pipeline Under Aspire

As a Hexalith builder,
I want an Aspire-hosted proof of the Works command and event pipeline,
So that I can verify the kernel, projections, and substrate wiring without shipping production adapters.

**Acceptance Criteria:**

**Given** the historical Works AppHost is started for local testing
**When** topology is inspected
**Then** it wires Works, ServiceDefaults, EventStore dependencies, projection infrastructure, and Dapr components needed for command and event tests
**And** it does not expose production UI, MCP, chatbot, email, routing, cost, or security-hardening adapters.

**Given** the command and event pipeline is exercised under Aspire
**When** the sequence create, progress, spawn child, suspend, resume, and complete runs
**Then** events persist before publication
**And** state and projections converge to the expected result.

**Given** integration and smoke tests run
**When** configured delivered-version test lanes complete
**Then** Tier-1 tests remain pure and do not require Aspire
**And** Aspire is used only for boundary and runtime proof.

**Given** observability is inspected
**When** pipeline errors occur
**Then** failures surface with correlation and tenant context
**And** logs avoid event payloads, personal data, secrets, raw tokens, and full command bodies.

**Given** this story was completed under the former Works-owned hosting topology
**When** current planning references its runtime evidence
**Then** its historical completion record remains valid and is not rewritten against the platform-host target
**And** Story 4.9 must preserve equivalent or stronger passing proof before removing the Works-owned host.

### Story 4.6: Prove Reminder and Reactor Recovery

As a Hexalith builder,
I want reminder and Reactor recovery proved separately from the core pipeline,
So that date resumes and cascade continuation survive restarts without making the kernel depend on clocks or infrastructure.

**Acceptance Criteria:**

**Given** a date-based Await-Condition exists
**When** the Dapr actor reminder fires
**Then** the adapter issues a `ResumeWorkItem` command with the deterministic Await-Condition key
**And** the aggregate remains clock-free.

**Given** a date-based reminder is registered more than once for the same Work Item and Await-Condition
**When** reminder registration is retried
**Then** the reminder name is deterministic
**And** duplicate registration does not produce duplicate accepted resume events.

**Given** the historical AppHost restarts while date-based resumes are pending
**When** recovery runs
**Then** reminder reconciliation re-scans pending `DateReached` Await-Conditions
**And** firings lost before recording are reissued as idempotent resume commands.

**Given** Story 3.6 provides pure cascade command intents and idempotent target commands
**When** the Reactor runtime dispatches cascade commands
**Then** Story 4.6 owns at-least-once dispatch, checkpoint persistence, checkpoint replay, and historical AppHost restart proof
**And** checkpoint state is persisted after each target command attempt or at a documented safe boundary.

**Given** the Reactor restarts during cascade processing
**When** checkpoint replay resumes under Aspire
**Then** outstanding descendants still requiring termination are discovered from a re-readable projection, already-terminal descendants are not terminated again, and a mid-cascade restart converges
**And** no clock, Dapr, or infrastructure dependency is added to the kernel.

**Given** Reactor translation is tested
**When** parent terminal events or child-completion events are processed
**Then** the Reactor emits only mechanical command intents
**And** all domain decisions still round-trip through aggregate `Handle`.

**Given** current planning relocates hosting to Hexalith.Platform
**When** this story's completed evidence is used
**Then** its recovery guarantees remain historical requirements
**And** Story 4.9 must reproduce equivalent or stronger proof before retiring the Works-owned runtime topology.

### Story 4.7: Trigger Reactor Translators from the Live Event Stream

As a Hexalith builder,
I want the running Works host to consume the domain event stream and drive the Reactor translators,
So that cascade and child-completion resume actually execute in the live topology instead of only in component tests.

**Acceptance Criteria:**

**Given** the historical Works host runs under the Aspire topology
**When** a parent Work Item reaches `Cancelled` or `Expired`
**Then** an at-least-once event-consumption path invokes the cascade dispatcher and active descendants receive idempotent terminal commands discovered from a re-readable projection
**And** already-terminal descendants are identified from the persisted Roll-Up read model, not hardcoded as active.

**Given** a parent is suspended on a `ChildCompleted` Await-Condition
**When** the child completes in the running topology
**Then** a `WorkItemCompleted` consumer feeds the unchanged `ChildCompletionResumeTranslator` from a re-readable awaiting-parents source and the parent resumes through an idempotent `ResumeWorkItem` submission
**And** every decision still round-trips through aggregate `Handle`.

**Given** the host crashes mid-cascade
**When** it restarts
**Then** a startup recovery pass discovers incomplete cascade checkpoints from a durable index and drives checkpoint replay
**And** the cascade converges without duplicate terminal effects.

**Given** the new consumption path is inspected
**When** fitness and governance tests run
**Then** the kernel and Reactor projects remain free of any new dependency
**And** the host contains no shadow-kernel conditional a pure `Handle` could not have produced.

**Given** hosting ownership moves to Hexalith.Platform
**When** Story 4.9 migrates the live path
**Then** this story's completed cascade, resume, and restart behaviors remain required parity evidence
**And** the Works-owned host is not removed until the replacement lane passes.

### Story 4.8: Register and Reconcile Date Reminders Durably

As a Hexalith builder,
I want date reminders registered when an item suspends and reconciled from a working pending-await source,
So that date-based resumes execute in steady state and survive recovery without hand configuration.

**Acceptance Criteria:**

**Given** an item suspends with a `DateReached` Await-Condition in the running topology
**When** the event path observes the suspension
**Then** a self-targeted durable Dapr reminder is registered with the deterministic name and duplicate registration remains idempotent
**And** the item resumes when the date fires without requiring a host restart.

**Given** the reconciliation-on-recovery pass runs
**When** pending `DateReached` awaits are scanned
**Then** the scan reads a tenant-scoped pending-date-await index read model maintained by the projection dispatcher plus per-aggregate stream reads
**And** it never issues the tenant-wide null-aggregate stream read the gateway rejects.

**Given** the host restarts after reminder firings were lost before recording
**When** recovery completes
**Then** overdue awaits are reissued as idempotent resume commands and future awaits are re-registered
**And** reconciliation operates without per-tenant hand configuration.

**Given** the kernel is inspected
**When** fitness tests run
**Then** `Handle` and the Reactor remain clock-free.

**Given** Story 4.8 remains in review with deferred follow-up explicitly recorded
**When** its historical requirement and implementation evidence are assessed
**Then** the completed reminder-runtime proof and review patches remain attributable to Story 4.8 without silently closing deferred work
**And** final platform-host removal remains exclusively owned by Story 4.9, after prerequisite Stories 4.10–4.16.

### Story 4.9: Migrate Works Hosting to the Platform Boundary

> Final cutover story. The human-approved 2026-09-23 split moved producer, consumer, and Platform parity delivery into draft prerequisite Stories 4.10–4.16 while preserving this story's identity and AD-20 R1–R11 host-removal gate.

As a Hexalith platform maintainer,
I want Works hosted through the shared EventStore domain-service SDK and a platform-owned Aspire topology,
So that domain modules contain domain code rather than duplicated hosting and infrastructure plumbing.

**Acceptance Criteria:**

**Given** the Works repository is inspected after migration
**When** its solution and published artifacts are enumerated
**Then** no `Hexalith.Works.AppHost`, `Hexalith.Works.ServiceDefaults`, or other Works-owned `*.AppHost`, `*.Aspire`, or `*.ServiceDefaults` project remains.

**Given** the runnable Works domain service starts
**When** its composition is inspected
**Then** it uses the canonical `AddEventStoreDomainService(...)` and `UseEventStoreDomainService()` SDK path
**And** it does not fork platform service defaults, health, telemetry, projection or query actors, Dapr wiring, event delivery, or subscription plumbing.

**Given** Works requires domain-specific projections, queries, reminder intents, or Reactor translations
**When** those capabilities are implemented
**Then** they use documented EventStore handler and store interfaces and remain pure or domain-focused
**And** any missing reusable runtime capability is added to EventStore or Platform first rather than copied into Works.

**Given** local and automated topology tests run from the designated platform host repository
**When** the migration evidence is collected
**Then** create, progress, spawn, suspend, child resume, date resume, cascade, claim conflict, query, ordinary projection, and shared rebuild scenarios retain equivalent or stronger evidence.

**Given** a shared projection rebuild runs
**When** inventory capture through atomic Commit is in progress
**Then** readers remain on the prior committed generation, ordinary projection delivery is quiesced or excluded by an equivalent platform fence, and delivery resumes and catches up after Commit.

**Given** repository architecture tests run
**When** a Works-owned hosting project or generic platform-plumbing implementation is introduced
**Then** the tests fail with a clear boundary violation.

**Given** migration sequencing is reviewed
**When** obsolete Works hosting projects are removed
**Then** the replacement platform topology is already green
**And** rollback consists of restoring the prior host composition until the platform lane is accepted.

**Given** the story's prerequisite is evaluated before implementation
**When** ownership and destination are confirmed
**Then** the named platform host is `Hexalith.Platform` and the accountable owner is the Platform Maintainer (Hexalith)
**And** no current Works hosting project is removed before the target topology has equivalent passing runtime evidence.

**Given** Stories 4.10–4.16 have not all been accepted
**When** the Works host-removal gate is evaluated
**Then** Story 4.9 remains blocked from cutover even if the Platform topology starts successfully.

### Story 4.10: Publish EventStore Projection Delivery and Rebuild Fence

As a platform SDK maintainer, I want generic event delivery and a bounded shared-projection epoch, so domain modules can rebuild without lost acknowledged writes. This is the R3–R4 producer prerequisite for Story 4.14.

**Acceptance Criteria:** Given concurrent delivery through capture and Commit, when a rebuild promotes, then readers select one committed generation and catch-up converges. Given a crash, conflict, or 10,000-item capture/stage, when recovery runs, then no acknowledged envelope is lost and the published contract tests pass.

### Story 4.11: Publish EventStore Typed Reminder Reconciliation

As a platform SDK maintainer, I want generic typed reminder registration and reconciliation, so Works can provide domain intents without owning Scheduler mechanics. This is the R6 producer prerequisite for Story 4.15.

**Acceptance Criteria:** Given duplicate, stale, lost, or restarted reminder delivery, when the SDK reconciles, then one logical effect or audited no-op remains and pending intents stay discoverable.

### Story 4.12: Publish EventStore Checkpointed Process and Recovery Runtime

As a platform SDK maintainer, I want a checkpointed process runner with strict paging, quarantine, and readiness, so Reactor translations recover after failures. This is the R7–R8 producer prerequisite for Story 4.15.

**Acceptance Criteria:** Given a page boundary, crash, or invalid evidence, when the runner resumes, then no source envelope is skipped, unresolved work remains durable, and readiness degrades until authorized disposition.

### Story 4.13: Publish EventStore Trusted Effect Submission

As a platform SDK maintainer, I want deterministic effect IDs and target-partition receipts, so cross-aggregate commands remain idempotent after redelivery and restore. This is the R11 producer prerequisite for Story 4.15.

**Acceptance Criteria:** Given identical or conflicting replay, when the target actor receives an effect, then it returns the durable prior outcome or quarantines the conflict without a second mutation; wrong origin, tenant, or purpose is denied.

### Story 4.14: Adopt SDK Projection and Query Seams in Works

As a Works maintainer, I want domain folds and tenant query policy behind the published SDK seams, so generic delivery, rebuild, and query transport leave Works. This consumes Story 4.10 for R3–R5 and preserves the old host for rollback.

**Acceptance Criteria:** Given live delivery, rebuild, restart, and authorized or denied queries, when SDK consumers run, then persisted projections converge and only permitted tenant results are disclosed.

### Story 4.15: Adopt SDK Reminder, Process, and Command Seams in Works

As a Works maintainer, I want pure reminder and Reactor translations driven by published SDK runtime APIs, so Works no longer owns generic reminder, recovery, and submission machinery. This consumes Stories 4.11–4.13 for R6–R8/R11 and preserves the old host.

**Acceptance Criteria:** Given callback replay, lost firing, cascade, child resume, crash, or unauthorized submission, when SDK consumers run, then persisted end states converge once logically and failures remain quarantined/readiness-affecting.

### Story 4.16: Prove Platform Works Parity and Rollback

As the Platform Maintainer, I want one executable R1–R11 verifier and rollback drill, so Story 4.9 can decide cutover from persisted evidence. This completes R1/R2/R10 hosting and security after producer and Works consumer stories.

**Acceptance Criteria:** Given named producer versions and Works consumers, when `eng/verify-works-host.sh` runs from Platform, then every required row passes with a persisted end state, negative security case, and rollback evidence; the Agents clean-checkout gate still passes. No Works host is removed in this story.

### Forward Candidate F4-A: Fold Absolute Work Contribution Snapshots

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a builder consuming Work Tree progress,
I want each Work Item's latest own contribution represented as an absolute projection snapshot,
So that Roll-Up can converge under duplicate or out-of-order delivery without interpreting event deltas (FR3, FR7, FR8, FR9, FR10, FR11).

**Acceptance Criteria:**

**Given** a committed accepted state-changing Work Item event
**When** the Works projection fold processes its EventStore envelope
**Then** it derives an absolute `WorkContributionSnapshot` containing canonical tenant, item, source envelope sequence, Status, own Remaining grouped by Unit, and unestimated count
**And** the snapshot represents folded current state rather than the current event's delta.

**Given** a non-terminal estimated Work Item
**When** its contribution is folded
**Then** the snapshot contributes its derived own Remaining under exactly its established Unit and unestimated count zero
**And** it contains no rolled ancestor total, conversion, routing value, or interpreted Expectation.

**Given** a non-terminal unestimated Work Item
**When** its contribution is folded
**Then** numeric own contribution is zero and unestimated count is one
**And** the state remains distinguishable from estimated zero work.

**Given** a Work Item in Completed, Cancelled, Rejected, or Expired
**When** its contribution is folded
**Then** own contribution is zero and unestimated count is zero regardless of historical Estimated, Done, or residual Remaining
**And** explicit residual completion does not rewrite the underlying Effort history.

**Given** a progress-completed Work Item is corrected and reopened
**When** `ProgressCorrected` and `WorkItemReopened` are folded in envelope order
**Then** its positive current Remaining reappears in the absolute snapshot only after the reopen evidence
**And** original progress and completion events remain unchanged.

**Given** a duplicate or older envelope for the same Work Item
**When** the pure fold is evaluated against its per-stream position
**Then** it cannot regress the latest contribution state or create an additive delta
**And** equal-position unequal canonical evidence is returned as a conflict disposition for quarantine.

**Given** a snapshot is produced
**When** durable domain history is inspected
**Then** the snapshot is used only as a non-domain projection message and is never appended to Work Item or Registry streams
**And** it never advances a Works payload Sequence or forges EventStore envelope metadata.

**Given** aggregate and projection package references
**When** architecture fitness tests run
**Then** Projections owns the fold and depends on Contracts rather than Server
**And** EventStore can treat the snapshot as opaque without referencing Works domain types in its generic runtime.

**Given** the complete durable event catalog
**When** property tests fold equivalent histories with allowed duplicate and reorder patterns
**Then** quiescent snapshots are identical and contain no derived value in any source domain payload
**And** the focused tests cover unestimated, zero, overrun, both completion kinds, correction and reopen, and all terminal states.

### Forward Candidate F4-B: Merge Roll-Up State Across Attached Ancestry

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an objective owner,
I want descendant contributions merged into every Attached ancestor without lost updates,
So that whole-tree Remaining converges correctly under concurrent, duplicate, and reordered projection delivery (FR5, FR11, FR12, FR13).

**Acceptance Criteria:**

**Given** the generic relationship-aware CAS projection seam is absent from the current EventStore SDK
**When** implementation begins
**Then** the owning EventStore repository first publishes a named merge and relationship API, package contract test, provider proof, and changed-key notification seam
**And** Works does not introduce custom durable projection routing or database-specific clients as a substitute.

**Given** a tenant and ancestor WorkItemId
**When** a Roll-Up document key is resolved
**Then** the logical key follows `works:v3:tenant:<encoded-tenant>:rollup:<encoded-ancestor>` and the tenant manifest selects its epoch-qualified physical key
**And** readers never discover a generation by listing or choosing physical keys directly.

**Given** a Roll-Up document receives topology evidence
**When** the pure Works merge applies it
**Then** each descendant topology watermark is last-writer-wins only by Registry envelope sequence and carries ReservationId plus Attached or tombstoned state
**And** a newer tombstone removes the contribution and rejects older attachment writes.

**Given** a `WorkContributionSnapshot` for a descendant
**When** it is merged
**Then** its contribution slot is last-writer-wins only by that descendant's EventStore envelope sequence
**And** sequence values from different streams are never compared to choose a global winner.

**Given** the ancestor's own snapshot or a non-self descendant snapshot
**When** eligibility is evaluated
**Then** the exact ancestor self slot requires no attachment watermark, while every non-self slot requires matching current Attached topology evidence
**And** Reserved, missing, stale, mismatched, or tombstoned relationships are not counted or acknowledged as complete.

**Given** concurrent topology and contribution updates target one Roll-Up document
**When** EventStore performs ETag read, merge, and retry
**Then** the atomic commit preserves every independent slot and materializes deterministic ordinal per-Unit totals plus unestimated-descendant count
**And** retry exhaustion surfaces an infrastructure conflict without a partial acknowledgement or lost update.

**Given** equal source sequence is received twice
**When** canonical bytes are equal or unequal
**Then** equal bytes are a no-op, while unequal bytes create conflict quarantine and degrade the affected Roll-Up
**And** neither case additively accumulates a contribution.

**Given** an edge transitions to Attached after a contribution was previously withheld
**When** deterministic backfill resolves exact ancestry through `IWorkTreeTopologyReader`
**Then** the current absolute snapshot is merged into the item and every valid ancestor
**And** a missing or Reserved relation remains retryable and unacknowledged rather than being accepted as an empty ancestor set.

**Given** duplicate, reorder, concurrent-branch, attachment, tombstone, and CAS-conflict integration scenarios
**When** focused tests complete
**Then** persisted Roll-Up document contents, ETags, topology watermarks, contribution slots, totals, and quarantine state match expected end states
**And** tests prove convergence without relying on broker order, response codes, or mock counts alone.

### Forward Candidate F4-C: Expose Trustworthy Whole-Tree Progress

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an objective owner,
I want own and recursive Remaining reported with Unit and freshness evidence,
So that I can understand the whole Work Tree without mistaking stale, unavailable, or unestimated data for completed work (FR11, FR12).

**Acceptance Criteria:**

**Given** an authorized Roll-Up query for an estimated single-Unit tree at quiescence
**When** the read model is returned
**Then** it exposes the selected item's own Remaining separately from the recursive total of each Attached descendant exactly once
**And** the result identifies Unit, unestimated-descendant count, freshness position and time, and availability state.

**Given** a tree contains descendants with different explicitly established Units
**When** Roll-Up is materialized and queried
**Then** each Unit has a separate deterministic subtotal and no implicit conversion or combined number is produced
**And** same-Unit descendants share one subtotal regardless of depth.

**Given** unestimated non-terminal descendants
**When** Roll-Up is queried
**Then** they add zero to numeric totals and increment the active-unestimated count
**And** a numeric rolled zero remains distinguishable from “nothing estimated yet.”

**Given** terminal descendants, explicit residual completion, or a corrected and reopened descendant
**When** their latest snapshots converge
**Then** terminal items contribute zero while the reopened item contributes its restored current Remaining
**And** the read shape remains distinct from authoritative own Work Item state.

**Given** normal projection lag, reconnection, repair, or rebuild
**When** a consumer reads Roll-Up
**Then** it receives explicit Fresh, Stale, Reconnecting, or Unavailable evidence and the last trustworthy result only when safe
**And** a partial or untrustworthy result is omitted rather than returned as fresh or numeric zero.

**Given** an unauthorized, unauthenticated, or cross-tenant caller
**When** it queries a Roll-Up key
**Then** membership, query authorization, and result filtering fail closed before tenant existence or Work Item data is disclosed
**And** logs and ProblemDetails contain no payload, Party profile, raw identifier concatenation, or hidden authorization detail.

**Given** a durable Roll-Up mutation commits
**When** EventStore invokes the notifier seam
**Then** it publishes only the changed logical key and no model payload
**And** clients can refresh from the authorized read endpoint without polling or treating notification delivery as authoritative state.

**Given** the provisional delivered-version fixture with depth 32 and top-level fan-out 50, approximately 1,600 items
**When** every published tree event has been acknowledged by the projection
**Then** Roll-Up converges within the configured five-second bound and an authorized read answers in under 200 ms from the read model
**And** tests await durable acknowledgements rather than sleeping for an assumed delay.

**Given** headless evidence or a future `roll-up-summary` or `work-tree` presentation consumes the read model
**When** it formats results
**Then** own Remaining, per-Unit rolled Remaining, unestimated count, freshness, and Unavailable are textually and semantically separate
**And** only Attached nodes appear in the authoritative tree while background updates do not imply synchronous cross-aggregate state.

### Forward Candidate F4-D: Rebuild Read Models Behind a Tenant Epoch

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a platform operator,
I want all related Works read-model families rebuilt behind one atomic tenant fence,
So that readers never observe a partial generation or lose live updates during repair and migration (FR11, FR20, FR24, FR25).

**Acceptance Criteria:**

**Given** an authenticated audited rebuild request for a tenant
**When** EventStore creates a new `works-runtime-v3` epoch
**Then** the epoch seals the tenant inventory and source high-watermarks shared by Roll-Up, what's-next, topology, pending intents, quarantine and parking, and related read families
**And** EventStore is the sole durable epoch, manifest, journal, and generation writer.

**Given** a rebuild is requested while active writers are running
**When** `BeginBuilding` evaluates readiness
**Then** it fails until every required writer holds the rebuild-protocol lease for the new epoch
**And** every later writer commit validates the current epoch and lease before acknowledgement.

**Given** a stale writer or old host generation attempts a read-model write after the fence changes
**When** its commit is validated
**Then** the write fails without acknowledgement or mutation
**And** recovery refreshes the writer against the current epoch rather than accepting an unfenced update.

**Given** the epoch is in Building state and a live source update arrives
**When** an active writer processes it
**Then** the live update and consumer checkpoint append atomically to the epoch's durable catch-up journal
**And** neither the active generation nor incomplete staging documents are mutated directly.

**Given** a catch-up journal entry
**When** its identity and content are validated
**Then** identity is tenant, epoch, family, source stream, and envelope position with canonical digest conflict detection
**And** duplicate identical entries no-op while unequal evidence for one identity quarantines and degrades.

**Given** staging is built from the sealed inventory and source high-watermarks
**When** completeness is evaluated
**Then** it contains every document required by each family, including empty or tombstoned state needed for correct reads
**And** promotion is prohibited while any required page, quarantine disposition, or source range is unresolved.

**Given** staging is complete
**When** Commit succeeds
**Then** one manifest change atomically selects the new generation and queued catch-up drains only into that promoted generation
**And** readers use the manifest as sole selector and remain stale or unavailable until every post-capture position is applied and the fence opens.

**Given** a rebuild is aborted
**When** cleanup runs
**Then** the journal drains safely into the active generation before staging deletion or stale-state clearance
**And** a crash during abort can resume without losing or double-applying acknowledged live updates.

**Given** Reactor effect checkpoints and target receipts exist
**When** a read-model rebuild or backup restore completes
**Then** process-effect state restores and reconciles separately from disposable projections using source events and deterministic EffectIds
**And** projection promotion never fabricates completion of a pending cross-aggregate effect.

**Given** capture-boundary, live-write, stale-writer, commit, abort, crash, and concurrent-family tests
**When** the rebuild lane runs
**Then** readers observe only the prior complete generation or the promoted complete generation with explicit stale or unavailable intervals
**And** persisted manifests, journals, checkpoints, generations, and catch-up end state prove no acknowledged write was omitted.

### Forward Candidate F4-E: Compose the Minimal Works Domain Service

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith platform integrator,
I want Works exposed through the canonical EventStore domain-service SDK,
So that the kernel can run without owning reusable host, Dapr, projection, telemetry, or recovery plumbing (FR22, FR24).

**Acceptance Criteria:**

**Given** the target Works executable
**When** its application bootstrap is inspected
**Then** it uses the canonical minimal EventStore SDK composition to register Works contracts, aggregates, projections, Reactor translations, and required domain ports
**And** Works-specific host plumbing is absent beyond domain registration and typed configuration binding owned by the documented boundary.

**Given** a runtime capability for event delivery, projection merge, notification, query routing, reminders, process checkpoints, health, telemetry, or recovery
**When** the executable is composed
**Then** the reusable implementation comes from the published EventStore SDK or Platform seam
**And** any missing generic capability is added and contract-tested in its owning repository before Works consumes it.

**Given** the production Works project graph
**When** architecture fitness tests inspect references
**Then** Contracts, Server, Projections, and Reactor retain their inward dependency rules and only the executable composes EventStore or runtime adapters
**And** no domain project references Dapr, Aspire, database or broker clients, ServiceDefaults, FrontComposer, Fluent UI, LLM, routing, or Cost implementations.

**Given** the Works solution is built locally
**When** restore and compilation run
**Then** the `.slnx`, centrally pinned package versions, .NET 10 and C# 14 settings, nullable analysis, and warnings-as-errors are used
**And** no legacy `.sln`, inline package version, Dockerfile, or out-of-band dependency pin is introduced.

**Given** the delivered-version horizon
**When** executable endpoints and registrations are enumerated
**Then** only the domain-service command, query, projection, Reactor, health, and operational seams required by the delivered version are present
**And** no production web, MCP, CLI, chatbot, email, LLM, routing, Cost, or Theme 6 product surface is exposed.

**Given** current transitional Works AppHost and ServiceDefaults code still supplies a parity row not yet proven in Platform
**When** this minimal target service is introduced
**Then** transitional code remains clearly identified and operational until the corresponding migration acceptance is complete
**And** this story does not delete or replace it merely because target composition compiles.

**Given** structured diagnostics are emitted by the service
**When** source-generated logging and RFC 9457 error mapping run
**Then** they include bounded correlation, tenant, command type, disposition, and health metadata only
**And** event and command payloads, Raw-Act text, secrets, tokens, Party data, and stack traces are excluded from public output.

**Given** focused unit, architecture, and service-start tests
**When** the story is validated
**Then** the minimal executable resolves all registered domain capabilities through published seams and passes health wiring without a production adapter
**And** a forbidden dependency, duplicate runtime service, or unresolved required registration fails the build or test lane.

### Forward Candidate F4-F: Authorize Commands from Verified Identity

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a tenant member,
I want every user and workload act authorized from verified identity and current responsibility,
So that payload assertions, stale reads, or broad service privileges cannot mutate another Party's or tenant's work (FR17, FR18, FR19, FR20, FR24).

**Acceptance Criteria:**

**Given** an external caller reaches Platform ingress
**When** identity is authenticated
**Then** Platform derives acting Party and tenant membership from verified OIDC claims through Hexalith.Tenants
**And** a TenantId or actor carried in the request payload or envelope is never accepted as authority.

**Given** a caller assertion disagrees with verified identity or membership is absent
**When** a command or query is attempted
**Then** it is denied before aggregate dispatch, persistence, query execution, or tenant-existence disclosure
**And** the denial is audited as authorization evidence rather than emitted as a domain rejection.

**Given** ReportProgress, CorrectProgress, Complete, Suspend, Handoff, or Reject targets a Work Item
**When** authorization executes inside its serialized actor turn after rehydration
**Then** the authenticated acting Party must equal the current Executor Binding
**And** immutable EventStore-issued command context—not payload data, read-model state, or an edge-only check—drives the decision.

**Given** Claim targets an Assigned or Queued item
**When** responsibility authorization runs
**Then** Assigned Claim requires the current bound Party while Queued Claim binds the admitted authenticated Party using trusted tenant policy for Channel and AuthorityLevel
**And** a target Claim accepts no caller-provided binding as authority.

**Given** Assign, ReEstimate, Reschedule, Cancel, root Create, Queue, or LinkConversation
**When** an authenticated target-tenant member submits it
**Then** the delivered version applies the documented tenant-member authorization floor and the aggregate decides domain validity
**And** persisted AuthorityLevel remains descriptive data with no authorization branch.

**Given** Reactor, reminder, cascade, external-resume, repair, replay, rebuild, or quarantine work
**When** Platform issues workload delegation
**Then** a short-lived asymmetric JWT binds audience, exact effect tuple and EffectId, target, command type and digest, tenant, named purpose, causation, issue time, and expiry
**And** EventStore recomputes every binding and matches the target application to mTLS and ACL-attested identity before dispatch.

**Given** Registry-authorized child Create or SpawnChild or any other origin-restricted command
**When** the origin policy is evaluated
**Then** only the specifically allow-listed workload and purpose may invoke it and no global-admin workload bypass exists
**And** every unlisted origin-command pair fails before Handle or append.

**Given** a query for what's-next, Roll-Up, topology, history, or recovery state
**When** authorization succeeds at tenant scope
**Then** result filtering independently removes unauthorized data before serialization
**And** possession of a key, PartyId, correlation ID, or delegated workload identity cannot expand the caller's readable scope.

**Given** positive and negative authorization tests
**When** missing identity, wrong tenant, actor mismatch, stale binding, caller-supplied actor, wrong workload, wrong purpose, target mismatch, expired token, and valid cases execute
**Then** only the exact permitted cases reach domain Handle or query and every denial leaves domain and event state unchanged
**And** audit records contain bounded reason, identity reference, target, and causation without token or payload contents.

### Forward Candidate F4-G: Fail Closed on Production Routes and Origins

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a platform operator,
I want the production Works topology to reject undeclared routes, publishers, and insecure configuration,
So that development conveniences and spoofed workload identities cannot reach durable domain paths (FR24, FR25).

**Acceptance Criteria:**

**Given** a production deployment profile
**When** Works, EventStore, Dapr, scheduler, state store, broker, and supporting services communicate
**Then** Dapr mTLS uses the declared trust domain and namespace, network and application access are deny-by-default, and broker connections use TLS with producer and consumer ACLs
**And** direct service ports and undeclared cross-service routes are unavailable to ordinary workloads.

**Given** Works or Registry domain events are published
**When** broker origin policy is evaluated
**Then** only EventStore's named producer identity may publish them and only registered consumers may subscribe
**And** forged publisher identity or caller-supplied high envelope sequence is rejected before state-affecting processing.

**Given** reminder, Reactor, replay, rebuild, repair, quarantine, or operator routes
**When** a request reaches them
**Then** only the named workload or operator identities and exact purposes declared by policy are admitted
**And** a valid application identity alone does not grant access to an unrelated command or tenant.

**Given** the machine-readable production profile is loaded at startup
**When** required trust, ACL, delegation keys, secret-store, scheduler, state-store, broker, audit, or tenant-policy configuration is missing or permissive
**Then** startup or production admission fails closed with degraded or unready health
**And** the application cannot silently fall back to a development profile or insecure default.

**Given** the selected Dapr hosting integration is a preview version
**When** production admission is requested
**Then** it is rejected unless the accountable Platform Maintainer records a time-bounded exception, owner, risk, expiry, and stable-upgrade trigger
**And** development or test approval is not presented as production compatibility evidence.

**Given** secrets or workload signing keys rotate
**When** old and new configuration overlap
**Then** Platform's secret-store policy preserves authorized availability only for the documented transition window and rejects expired material afterward
**And** secret values never enter logs, traces, metrics, health payloads, or ProblemDetails.

**Given** negative production-policy tests
**When** direct-port, wrong application, wrong namespace or trust domain, unauthorized publication, forged sequence, wrong tenant or purpose, ordinary-user SpawnChild, and missing-profile cases run
**Then** every request fails before domain dispatch or protected disclosure
**And** a valid production-policy reminder callback and ordinary authorized command both pass through the intended secured path.

**Given** policy denial or audit or secret dependency failure
**When** operational health is reported
**Then** readiness and bounded alerts identify the affected capability without tenant payload data or trust internals
**And** production traffic remains blocked until the configured policy and dependency are verifiably healthy.

### Forward Candidate F4-H: Protect and Restore Durable Tenant Data

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As an accountable data owner,
I want Works data classified, retained, erased, backed up, and restored through explicit ownership,
So that real tenant work is not admitted without enforceable privacy, audit, and disaster-recovery guarantees (FR7, FR21, FR24, FR25).

**Acceptance Criteria:**

**Given** Works durable fields are inventoried
**When** data classification is reviewed
**Then** Obligation, notes, Await and correlation values, and durable bodies are classified as confidential tenant data, while identifiers and causation are restricted operational metadata
**And** Works owns minimization and classification without duplicating sibling-owned content.

**Given** durable state is stored or transported
**When** platform controls are evaluated
**Then** EventStore owns encryption in transit and at rest plus tenant-keyed retention and erasure mechanisms, while Platform owns secret rotation, backup and restore, disaster recovery, and the privileged audit sink
**And** Works domain code contains no provider-specific encryption, database, backup, or secret-management implementation.

**Given** a privileged repair, replay, rebuild, quarantine disposition, offboarding, or authorization denial
**When** the operation executes
**Then** bounded actor or workload, tenant, purpose, causation, target, and result evidence is written to the append-only audit sink
**And** audit-sink failure blocks privileged mutation instead of allowing an unaudited success.

**Given** a tenant retention, legal-hold, or offboarding operation
**When** durable data is selected
**Then** streams, Registry state, projections, pending intents, checkpoints, quarantine, effect receipts, and corresponding source and target evidence follow one approved tenant policy
**And** effect-correctness evidence is not erased independently from the events it proves.

**Given** no approved accountable owner, retention, legal-hold, offboarding policy, or successful restore drill exists
**When** non-synthetic shared data or a new durable catalog type is proposed
**Then** production admission fails before the data or new type is accepted
**And** the missing owner or evidence is reported as a readiness blocker rather than an assumed default.

**Given** production recovery objectives
**When** backup and disaster-recovery plans are validated
**Then** they target RPO of at most 15 minutes, RTO of at most four hours, and a documented restore drill at least quarterly
**And** the measured result, accountable owner, environment, and any failure are retained as audit evidence.

**Given** a restore drill
**When** the recovered environment is verified
**Then** Work Item streams, Registry topology, Roll-Up and what's-next projections, pending reminder intents, process checkpoints, effect receipts, quarantine and audit state, tenant keys, and access policies are restored or deterministically rebuilt
**And** source-to-target reconciliation proves no acknowledged domain act or pending effect was silently lost.

**Given** confidential tenant data is handled by observability, errors, tests, backups, or operator tools
**When** outputs are inspected
**Then** payloads, Raw-Act text, Party data, secrets, tokens, and full command bodies are absent from logs, metrics, traces, health, and ProblemDetails
**And** test fixtures use synthetic or minimized data unless an explicitly approved protected-data process applies.

**Given** an authorized offboarding completes
**When** tenant data and control indexes are pruned
**Then** tombstones prevent stale discovery or recovery from recreating removed state and the operation is crash-reconcilable
**And** no other tenant's namespace, encryption material, audit evidence, or readiness is affected.

### Forward Candidate F4-I: Gate Migration to the Platform-Owned Host

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Works maintainer,
I want host ownership migrated only after exact Platform parity is proven,
So that removing transitional Works infrastructure cannot silently drop runtime, security, or recovery behavior (FR24, FR25).

**Acceptance Criteria:**

**Given** architecture migration rows R1 through R11
**When** readiness is evaluated
**Then** every applicable row records its owning producer artifact, published package or API version, contract test, Works consumer story, exact proof command, result, environment, and accountable owner
**And** an absent producer seam, compile-only check, stale result, or undocumented local substitute keeps that row non-green.

**Given** topology and defaults, subscription, projection and rebuild, query, reminder, process runner, recovery, executable, security, and command-submission consumers
**When** Platform parity scenarios run
**Then** each behavior matches the accepted Works baseline under normal, failure, restart, duplicate, security-negative, and recovery paths
**And** persisted end state and health evidence—not process startup alone—prove parity.

**Given** any applicable R1–R11 row is incomplete or failing
**When** migration is proposed
**Then** the current Works AppHost, ServiceDefaults, route, Dapr wiring, reminder or recovery source, or other transitional owner remains available
**And** documentation continues to distinguish CURRENT behavior from TARGET ownership.

**Given** every applicable parity row is green
**When** rollback is exercised before removal
**Then** the prior known-good topology can be restored with compatible streams, Registry, projections, intents, checkpoints, receipts, security policy, and tenant keys
**And** rollback meets the declared recovery objectives without accepting unverified data loss.

**Given** parity and rollback have passed against the locked head and package set
**When** transitional Works hosting is removed
**Then** Works retains only the minimal domain-service executable and removes bespoke AppHost, Aspire, ServiceDefaults, `/project` route, local Dapr actor and reminder, telemetry, and recovery plumbing covered by Platform
**And** the `.slnx`, architecture fitness rules, documentation, and test topology are updated atomically to the target ownership model.

**Given** runtime compatibility is part of the proof
**When** versions are inspected
**Then** the current owning files—not prose—authoritatively identify .NET and Aspire SDK, Dapr integration and runtime, package, image, and EventStore versions
**And** any version change receives restore, Release build, focused integration, and Platform parity evidence before acceptance.

**Given** a preview hosting integration remains in the target stack
**When** production parity is assessed
**Then** the migration remains development or test-only unless the accountable Platform Maintainer's time-bounded production exception is active
**And** exception expiry or availability of a stable replacement reopens the compatibility gate.

**Given** the migrated target topology
**When** architecture, startup, security, integration, and rollback tests run
**Then** no duplicate Works and Platform runtime owner remains and every required domain capability is resolvable through the published seams
**And** failed parity or changed producer or consumer heads invalidates the recorded acceptance until the exact evidence is rerun.

### Forward Candidate F4-J: Prove the Complete Kernel Through the Builder Harness

> Non-executable planning candidate. Assign a unique final story ID before implementation or sprint tracking.

As a Hexalith builder,
I want one repeatable evidence harness for the complete Works kernel,
So that lifecycle, coordination, projections, security, recovery, and architectural boundaries are proven together before release (FR1-FR26).

**Acceptance Criteria:**

**Given** the platform-owned Aspire topology and an authenticated synthetic tenant
**When** the canonical delivered-version journey creates, assigns or queues, claims, progresses, reserves and attaches a child, suspends, completes the child, resumes the parent, and completes the parent
**Then** every expected persisted Work Item and Registry event, target receipt, Reactor checkpoint, reminder disposition, query row, and Roll-Up value is observed in causal order at quiescence
**And** the Works repository uses no production channel adapter or duplicated platform runtime to drive the journey.

**Given** the process restarts while a parent is Suspended or a Registry or Reactor effect is incomplete
**When** the topology recovers
**Then** event replay reconstructs aggregate state, pending effects are reissued with stable identities, exact trigger semantics resume the parent, and projections converge
**And** no acknowledged Raw Act, Await-Condition, edge, effect disposition, or progress fact is lost or duplicated.

**Given** two Executors race for one Queued item and a terminal parent has an open Attached subtree
**When** claim and cascade scenarios execute
**Then** exactly one claim succeeds, the loser receives the defined current-state rejection, and every still-active descendant eventually receives the correct Cancel or Expire result while prior terminal descendants remain unchanged
**And** persisted end-state assertions cover streams, binding, topology, receipts, checkpoints, and projections.

**Given** system, internal, and external Executor Bindings
**When** the identical assign-to-claim-to-progress-to-complete journey runs for each
**Then** golden event streams differ only in binding field values and adding an allowed Channel or AuthorityLevel requires no Server or Projection branch or new event type
**And** the architecture fitness lane fails on any executor-kind specialization.

**Given** fresh empty Roll-Up and what's-next generations
**When** all authorized tenant streams are replayed and every event is acknowledged
**Then** rebuilt read models are identical to the live models at quiescence, including per-Unit totals, unestimated count, filters, ordering, freshness, and attachment state
**And** no domain payload contains rolled totals, ordering positions, interpreted Expectations, or other derived projection data.

**Given** the provisional depth-32 and fan-out-50 performance fixture
**When** final events are published under the platform lane
**Then** Roll-Up convergence is acknowledgement-driven within five seconds and authorized read-model access completes in under 200 ms
**And** failure reports separate the numeric budget from environmental or broad-gate blockers without weakening the requirement.

**Given** pure Tier-1 tests and integration boundaries
**When** validation runs
**Then** aggregate Handle and Apply, Reactor translations, projection folds, and merge properties execute without Dapr, network, browser, or containers, while integration uses EventStore testing support or Platform Aspire only for genuine boundaries
**And** each test project runs individually with xUnit v3 and persisted end-state assertions where durable storage is involved.

**Given** accepted, domain-rejected, authorization-denied, idempotent, and infrastructure-unknown outcomes
**When** the delivered-version builder harness renders them
**Then** structured headings and text identify outcome class, current authoritative domain evidence, projection freshness where applicable, and a safe next action
**And** output remains understandable without color, never calls acknowledgement completion, preserves safe retry context, and excludes payloads, secrets, personal data, tokens, and trust internals.

**Given** extracted UX requirements UX-DR1 through UX-DR26
**When** story traceability is audited
**Then** delivered-version-actionable headless evidence and domain or read-model semantics map to the relevant stories, while every web, email, natural-language, routing, Cost, and visual-component implementation remains explicitly assigned to its future or uncommitted horizon
**And** no UX requirement is silently dropped, falsely claimed as implemented, or used to introduce production UI into the delivered version.

**Given** a Release restore and build has completed
**When** CI quality gates execute
**Then** focused Unit, Property, Integration, and Architecture test assemblies run and the tracked architecture step invokes `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests.dll`
**And** compilation alone, a solution-level test shortcut, skipped security or recovery evidence, or unresolved readiness gate cannot mark the epic complete.

## Epic 5: Stabilize the Work Item Contract and Planning Record

Maintainers can close approved lifecycle, compatibility, and planning gaps without rewriting delivered evidence or destabilizing durable Work Item history.

### Story 5.1: Make Progress and Event Ordinals Overflow-Safe

As a Hexalith maintainer,
I want progress arithmetic and event ordinal allocation to fail safely at their numeric limits,
So that valid work never wraps, partially succeeds, or corrupts durable replay.

**Acceptance Criteria:**

**Given** an estimated Work Item with a valid positive progress delta equal to or greater than Remaining
**When** progress is handled
**Then** Done saturates to Estimated without evaluating an overflowing decimal addition
**And** the accepted result follows the existing progress-plus-completion event order.

**Given** a valid positive progress delta below Remaining
**When** progress is handled
**Then** the delta is added exactly
**And** arithmetic cannot overflow by construction.

**Given** a command requires one success-event ordinal while the current Sequence is `long.MaxValue`
**When** ordinal headroom is preflighted
**Then** handling fails with the architecture-defined typed integrity fault before result construction
**And** no success event is returned, appended, or published.

**Given** progress would produce both `ProgressReported` and `WorkItemCompleted`
**When** fewer than two event ordinals remain, including current Sequence `long.MaxValue - 1`
**Then** handling fails before constructing either event
**And** no partial result, state mutation, append, or publication occurs.

**Given** focused numeric-boundary tests
**When** they exercise `decimal.MaxValue`, `long.MaxValue`, and `long.MaxValue - 1`
**Then** saturating progress, exact sub-Remaining progress, one-event exhaustion, and two-event exhaustion produce their specified deterministic outcomes
**And** replay remains unchanged for accepted histories below the limits.

### Story 5.2: Make the Lifecycle Authority Executable and Singular

As a Hexalith maintainer,
I want every Work Item act governed by one executable lifecycle authority,
So that handlers, documentation, tests, and durable rejection evidence cannot drift apart.

**Acceptance Criteria:**

**Given** progress, re-estimation, rescheduling, or any lifecycle command is handled
**When** Status legality is evaluated
**Then** the decision comes from the shared executable lifecycle policy
**And** no handler-local status table or contradictory legality branch remains.

**Given** the executable policy accepts an act
**When** its `LifecycleOutcome` is consumed
**Then** the handler uses or explicitly verifies the policy's target Status
**And** emitted events and applied state produce exactly that target without recomputing it independently.

**Given** the executable policy rejects an act
**When** the domain rejection is produced
**Then** it carries the stable durable `AttemptedAct` defined for that matrix cell
**And** every rejected cell is covered by a focused assertion.

**Given** the human-readable lifecycle Markdown matrix
**When** verification runs
**Then** the document is generated from or mechanically cell-compared with the executable policy
**And** any missing, extra, or contradictory cell fails the test.

**Given** ReEstimate is evaluated across lifecycle state
**When** matrix coverage runs
**Then** all five non-terminal statuses and every terminal or `Unknown` status have an explicit tested outcome
**And** no status falls through to an implicit handler default.

**Given** existing durable diagnostic act-name values
**When** the singular policy is introduced
**Then** historical values remain readable and retain their meaning
**And** any normalization uses an additive compatibility mapping rather than rewriting durable history.

### Story 5.4: Derive Durable Catalog Completeness from Contracts

As a Hexalith quality maintainer,
I want durable-catalog completeness derived from the Contracts assembly,
So that new or orphaned durable types cannot evade compatibility evidence through hand-maintained counts.

**Acceptance Criteria:**

**Given** the built Works Contracts assembly
**When** the completeness test reflects over its durable contract surface
**Then** it derives the complete set of concrete decorated commands, success events, and `IRejectionEvent` types
**And** a hand-authored count may be supplemental but cannot define the universe being checked.

**Given** any derived durable type
**When** its compatibility evidence is inspected
**Then** it has exactly one stable discriminator and one non-vacuous catalog sample
**And** it has every writer and reader golden representation required by the serialization policy.

**Given** the derived contract set and the catalog and golden evidence sets
**When** completeness is compared
**Then** a contract without evidence fails
**And** evidence without a corresponding concrete contract also fails.

**Given** a decorated durable type is introduced without updating its catalog sample or required golden corpus
**When** the focused mutation or fixture test runs
**Then** the completeness gate demonstrably fails for the missing entry
**And** the test cannot pass vacuously because of an empty reflected or evidence set.

**Given** existing frozen discriminators and golden payloads
**When** the assembly-derived gate is adopted
**Then** their discriminator values and payload bytes remain unchanged
**And** historical reader compatibility continues to pass before any new producer is enabled.

### Story 5.3: Preserve Re-Estimate Overrun and Enforce Bounded Act Notes

As a Hexalith maintainer,
I want re-estimation to preserve audited progress and all act notes to follow one bounded policy,
So that planning changes remain truthful, replayable, and safe to diagnose.

**Acceptance Criteria:**

**Given** a Work Item whose cumulative Done exceeds a proposed non-negative replacement estimate in the established Unit
**When** `ReEstimate` is accepted from an eligible Status
**Then** the replacement Estimated value is recorded while cumulative Done remains unchanged and Remaining derives as `max(Estimated - Done, 0)`
**And** Unit and Status remain unchanged and no completion or reopen event is emitted.

**Given** accepted re-estimation evidence is consumed by aggregate replay, projection folds, serialized queries, or rebuild
**When** each path reaches quiescence
**Then** every path reports the same Estimated, cumulative Done, Remaining, Unit, and Status
**And** visible `Done > Estimated` overrun is not clamped, hidden, or treated as corruption.

**Given** representative pre-change streams that previously rebuilt with clamped Done
**When** the semantic migration is validated
**Then** checked-in migration fixtures prove the new aggregate and read-side interpretation atomically preserves Done
**And** affected disposable projections are rebuilt with documented snapshot and cache invalidation before rollout.

**Given** the inventory of every currently note-bearing lifecycle and planning command
**When** a note is `null` or contains from zero through 1,000 characters
**Then** the command accepts and preserves the supplied note verbatim when its other rules pass
**And** every inventoried command uses the same common policy.

**Given** any inventoried note contains more than 1,000 characters
**When** its command validator or aggregate handler evaluates it
**Then** the defined domain rejection is returned with no success event or state mutation
**And** logs and ProblemDetails do not echo the submitted note.

**Given** note-bearing durable contracts and tests
**When** compatibility verification runs
**Then** command validators, aggregate handling, catalog samples, golden payloads, and boundary tests all enforce the same limit
**And** any changed durable evidence is introduced only after Story 5.4's catalog-completeness gate is available.

### Story 5.5: Enforce the Obligation Bound Without Rewriting History

As a Hexalith builder,
I want new Work Item obligations bounded at admission while historical events remain replayable,
So that aggregate content stays appropriately small without invalidating previously accepted work.

**Acceptance Criteria:**

**Given** a new `CreateWorkItem` command whose Obligation has a trimmed length from 1 through 4,000 characters
**When** admission validation and aggregate handling run
**Then** creation is accepted when all other rules pass and the existing Raw-Act contract preserves the accepted text verbatim
**And** surrounding whitespace is ignored only when determining whether the value is empty and whether its bounded content exceeds the limit.

**Given** a new Obligation is empty, whitespace-only, or has a trimmed length greater than 4,000 characters
**When** `CreateWorkItem` is handled
**Then** the defined domain rejection is returned with no success event or state mutation
**And** nothing is appended or published.

**Given** the Obligation admission rule is represented at every boundary
**When** the command validator, aggregate, contract catalog, golden payloads, headless evidence, and tests are inspected
**Then** they use the same 4,000-character limit and trimming rule
**And** logs and ProblemDetails do not echo rejected Obligation content.

**Given** a historical `WorkItemCreated` payload contains a previously accepted Obligation longer than 4,000 characters
**When** current readers deserialize and replay it
**Then** the event remains readable and reconstructs the same historical state
**And** the new command-admission bound is not retroactively applied during replay.

**Given** implementation requires a new rejection type for the over-limit case
**When** rollout is prepared
**Then** reader, validator, discriminator, catalog sample, and required golden representations land and pass through Story 5.4's completeness gate before the producer is enabled
**And** rollback retains the reader while the new durable evidence exists.

**Given** focused Obligation-boundary tests
**When** they exercise empty or whitespace-only, 1-character, 4,000-character, and 4,001-character inputs with surrounding-whitespace variants
**Then** every admission outcome matches the common trimming and length rules
**And** a historical oversized-event fixture proves replay compatibility.
