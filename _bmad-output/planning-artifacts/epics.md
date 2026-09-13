---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md'
---

# works - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for works, decomposing the requirements from the PRD, UX Design if it exists, and Architecture requirements into implementable stories.

## Requirements Inventory

### Functional Requirements

FR1: A builder or Executor can create a tenant-scoped Work Item with a required non-empty Obligation and optional initial estimate and Unit, Schedule, parent reference, Executor Binding, Conversation reference, and Expectation reference; creation emits `WorkItemCreated`, assigns canonical identity, and rests at `Created` even when a binding is supplied.

FR2: A Work Item carries a required human-readable Obligation of at most 4,000 characters and an optional Expectation reference resolved on demand through `IExpectationResolver`; interpreted expectation data and long-form work content are not stored in the aggregate.

FR3: A Work Item carries one Effort Burn-Down consisting of Unit-tagged Estimated, Done, and derived non-negative Remaining values; Estimated is non-negative, progress uses the established Unit, and the Unit becomes immutable when the first estimate is accepted.

FR4: A Work Item carries an optional Schedule of Priority and Due Date; Priority uses the additive-tolerant ordered values `Critical`, `High`, `Normal`, and `Low`, and either schedule field can be changed later through an event-recorded act.

FR5: A Work Item records at most one parent reference, zero or more child references, and one or more Await-Conditions while Suspended; the Work-Tree Registry owns the authoritative parent-child edges, and the item records only Attached relationships.

FR6: The aggregate enforces the normative Work Item lifecycle across `Created`, `Assigned`, `Queued`, `InProgress`, `Suspended`, `Completed`, `Cancelled`, `Rejected`, and `Expired`; only defined transitions succeed, invalid acts produce domain rejections, and exact duplicate terminal acts are no-ops.

FR7: Every accepted state change and progress fact is recorded as an append-only, past-tense Raw-Act Domain Event with authenticated actor, timestamp, verbatim payload, and bounded optional note of at most 1,000 characters; success and rejection payloads remain distinct.

FR8: The bound Executor can report a strictly positive progress delta in the established Unit while `InProgress`; progress that reaches zero emits `ProgressReported` followed by `WorkItemCompleted`, while explicit Complete can finish an `InProgress` or `Suspended` item without rewriting its last Estimated and Done values.

FR9: An authenticated tenant member can re-estimate and reschedule any non-terminal Work Item; re-estimation records a new absolute estimate, clamps Remaining at zero without completing the item, fixes the Unit when it is the first estimate, and schedule changes update query ordering.

FR10: A Work Item can terminate through Cancel or Expire from any non-terminal state, and an assigned Executor can Reject with requeue or terminal semantics; Cancel and Expire eventually cascade through all still-active Attached descendants while already-terminal descendants remain unchanged.

FR11: The system maintains an eventually consistent, rebuildable, idempotent Roll-Up projection that exposes own Remaining, recursive descendant Remaining by Unit, unestimated-descendant count, freshness, and explicit unavailable state without rereading whole streams per query or presenting partial results as fresh.

FR12: Roll-Up never silently combines incompatible Units; a child without an explicit Unit inherits its parent's Unit for its first estimate, mixed-Unit trees require an explicit child Unit, and their results are exposed as separate per-Unit subtotals without conversion.

FR13: The tenant-scoped Work-Tree Registry enforces a single-parent, acyclic, single-tenant tree at edge reservation against its own state, serializes concurrent attachment attempts, applies a configurable maximum depth of 32 by default, and does not impose a domain breadth cap.

FR14: An `InProgress` Work Item can suspend on one or more typed Await-Conditions, recording each condition kind and correlation key; a Suspended item accepts no progress but continues contributing its current Remaining to Roll-Up.

FR15: The Reactor resumes a Suspended Work Item only when a command exactly matches an active Await-Condition by kind and key; the accepted resume records the consumed condition, clears the full set, and returns to `InProgress`, with only replay of that consumed condition treated as a no-op.

FR16: Child creation begins through the Work-Tree Registry's public reserve act carrying the complete child payload; the Reactor drives the reserved edge through child authorization and creation to durable attachment, and only Attached edges participate in the tree, Roll-Up, resume, and cascade behavior.

FR17: A Work Item uses one `ExecutorBinding(PartyId, Channel, AuthorityLevel)` for every system, internal, or external doer, and binding, reassignment, Channel changes, and handoff use uniform operations without branching on executor kind.

FR18: Push and Pull coexist: an item can be assigned to a specific Executor or queued for tenant members to claim, Assigned and Queued modes can be changed, Claim is the single entry to `InProgress`, and concurrent claims yield exactly one success with ordinary rejection for losers.

FR19: Executor Binding carries an additive-tolerant AuthorityLevel from `Read`, `Contribute`, `Coordinate`, or `Administer`; v1 persists but does not use AuthorityLevel for authorization decisions.

FR20: The read side exposes a tenant-scoped "what's next" query in Executor and coordinator views, filtering to `Assigned` and `Queued` items and ordering deterministically by present Priority, present earliest Due Date, then ordinal WorkItemId, with missing values sorted last.

FR21: Works stores Reference Value Objects rather than copied sibling-module data for Party, Conversation, Tenant, persistence, and IDs; a Conversation correlation can be supplied at creation or linked once later, with same-ID retries as no-ops and conflicting replacement rejected.

FR22: Contracts expose `IExpectationResolver` and `IExecutorRouter` as domain-owned ports; v1 includes a no-LLM expectation resolver, requires no router implementation, and keeps LLM, cost, routing, and infrastructure types out of domain assemblies.

FR23: v1 includes a tracked owns-versus-references boundary decision record enumerating each sibling-module boundary, what Works owns, what it references, and why.

FR24: Works exposes the canonical minimal EventStore domain-service executable, while a platform-owned Aspire topology composes Works with EventStore and shared infrastructure for end-to-end testing; the target Works domain module contains no AppHost, Aspire, ServiceDefaults, or duplicated runtime plumbing.

FR25: The complete command/event pipeline is testable without production adapters: aggregate and projection tests remain pure, while integration tests use EventStore testing support or the platform-owned Aspire topology only for genuine runtime boundaries.

FR26: A Works-owned Reactor is the sole process manager for registry-to-child creation, child-completion resume, cascading termination, and date/expiry coordination; it mechanically translates committed events into idempotent commands while EventStore and Platform own delivery, checkpoints, retries, reminders, and operations.

### NonFunctional Requirements

NFR1: Every aggregate identity, state key, projection key, query, control path, and log is tenant-scoped; parent-child relationships are tenant-closed, and reads apply authorization and result filtering in addition to key-level tenant scoping.

NFR2: Identity and tenant authority are derived from authenticated claims and membership, never caller-supplied fields; mismatches and absent membership fail before dispatch or disclosure, while internal operations use named workload identities with explicit auditable tenant delegation.

NFR3: Event-sourcing preserves pure `Handle` and in-memory `Apply` behavior, persists before publishing, leaves envelope metadata to EventStore, represents expected invalid acts as `IRejectionEvent` outcomes, and routes infrastructure failures to exception/dead-letter handling.

NFR4: Same-aggregate commands are serialized with bounded optimistic-concurrency retry; races produce one committed success and current-state domain rejections or an explicit infrastructure conflict after retry exhaustion, never silent loss.

NFR5: Roll-Up and "what's next" projections are disposable and rebuildable solely from durable event streams, carry no authoritative domain state, and reproduce the live read models at quiescence.

NFR6: Domain assemblies remain infrastructure-, LLM-, cost-, and routing-free; aggregates read no clock or external system, and all such concerns enter through commands, ports, adapters, or platform-owned services.

NFR7: Observability uses structured, bounded metadata and RFC 9457 error shapes with correlation and tenant context, while excluding event payloads, command bodies, personal data, secrets, and derived private content from logs, metrics, traces, and ProblemDetails.

NFR8: Incremental projections must remain responsive for realistic trees without per-query stream replay; the provisional v1 evidence fixture is depth 32 with top-level fan-out 50 (about 1,600 items), Roll-Up convergence within 5 seconds after final acknowledgement, and read-model response under 200 ms.

NFR9: Durable commands, events, values, enums, and read models evolve additively and serialization-tolerantly without renamed discriminators or `V2` event types; every historical durable payload remains readable and unknown required security or fencing data fails closed.

NFR10: Idempotency is explicit at three layers: aggregate commands resolve redelivery through state-defined no-op or rejection behavior, projections deduplicate by durable positions, and cross-aggregate transport reuses a deterministic idempotency identity that returns the original disposition or quarantines conflicts.

NFR11: Raw Acts support audit and later non-repudiation by preserving authenticated actor, timestamp, origin/causation, and verbatim authorized input; interpretations remain labeled, recomputable projections rather than system-of-record facts.

NFR12: The Effort Burn-Down and Roll-Up must remain structurally reusable for a later, separately labeled Cost Meter without changing v1 Effort semantics or silently converting Units.

NFR13: v1 must remain a headless, domain-centric kernel: it ships no production web, MCP, CLI, chatbot, email, LLM interpretation, routing engine, spend governance, or Theme 6 hardening, while retaining only the named seams required for additive later delivery.

### Additional Requirements

- AR1: This is a brownfield implementation over the existing `.slnx` and target project structure; no greenfield starter template is specified, and existing projects must not be recreated as an Epic 1 starter task.
- AR2: Use the current structural boundaries: `Contracts`, `Server`, `Projections`, `Reactor`, the minimal Works executable, and Unit, Property, Integration, and Architecture test projects.
- AR3: Preserve inward dependency flow: Server, Projections, and Reactor depend on Contracts rather than on one another; Testing references only the pure units it exercises; reusable mechanics are contributed to EventStore before Works consumes them.
- AR4: Durable Works contracts use Hexalith.PolymorphicSerializations and EventStore-owned canonical envelopes; writer/reader/catalog/golden-corpus rollout follows reader-first, producer-second ordering with rollback readers retained while new bytes exist.
- AR5: Existing aggregate IDs remain readable, while the target authenticated edge creates sortable ULIDs through Hexalith.Commons; handlers, Reactor translations, projections, and replay never generate aggregate IDs, except for the reserved literal registry aggregate identity.
- AR6: `WorkItemEffort(Unit, Estimated, Done)` remains the sole v1 effort contract, Remaining is derived, and Unit inheritance or immutability changes must be implemented consistently across create, child creation, progress, correction, projection, query, and validation paths.
- AR7: Implement the target Roll-Up as tenant/generation-scoped CAS-merged documents with per-descendant topology watermarks and absolute contribution slots; topology is last-writer-wins by registry envelope position, contribution is last-writer-wins by that descendant's position, and equal-position byte conflicts quarantine and degrade.
- AR8: Keep own Work Item state and rolled projection state in distinct types and serialized fields so eventual data cannot control lifecycle behavior or masquerade as synchronous aggregate state.
- AR9: Carry the architecture's superseding completion contract: `WorkItemCompleted` records `CompletionKind`; `CorrectProgress` appends an absolute audited `ProgressCorrected`, and only correcting a progress-completed item back to positive Remaining emits `WorkItemReopened` and returns it to `InProgress`; explicit completion remains terminal.
- AR10: Same-item concurrency is EventStore-owned: Works commands expose no ETag/version, actor serialization handles normal contention, conflicts rehydrate and re-handle within a bounded retry, and exhausted conflicts surface as infrastructure failures without publishing a loser event.
- AR11: All subscriptions, projections, Reactor paths, reminders, and recovery paths tolerate duplicate and out-of-order delivery and acknowledge only after durable state, checkpoint, side effect, or quarantine capture commits.
- AR12: The Reactor contains pure mechanical event-to-command translations only, persists progress through EventStore-owned process checkpoints, reissues deterministic effects after crash, and uses a re-readable cursor-paged topology projection rather than an in-memory cascade loop.
- AR13: Time enters through typed durable reminder intents; Works owns intent translation, EventStore owns generic reminder registration/reconciliation, and Platform owns scheduler persistence, availability, backup, and callback policy.
- AR14: A passed Due Date alone never changes domain state; Expiry and DateReached resume require a witnessed command carrying the currently persisted schedule identity, and stale reminders must reject rather than expire rescheduled work.
- AR15: Preserve exact resume semantics across every adapter: an active exact match resumes and clears the set, a nonmatch rejects without mutation, replay of the consumed condition is the sole no-op, and every other post-resume or terminal trigger rejects.
- AR16: Carry the architecture's superseding active-work binding contract: `HandoffWorkItem` is the only binding change permitted in `InProgress` or `Suspended`, emits `WorkItemHandedOff`, changes only the binding, and preserves Status and Await-Conditions.
- AR17: Canonical TenantId input is already-normalized lowercase ASCII slug data of 1–64 characters; trust boundaries reject rather than normalize invalid values, tenant data and control namespaces are disjoint, and keys encode canonical bytes rather than raw concatenation.
- AR18: Shared rebuild uses one tenant/family epoch with sealed inventory and source high-watermarks, leased writers, a durable catch-up journal, atomic manifest promotion, explicit stale/unavailable state, and abort behavior that drains the journal back to active before cleanup.
- AR19: Lifecycle changes must land atomically across the normative transition matrix, validators, durable catalog, aggregate behavior, projections, golden corpus, and conflicting fitness/unit/integration tests; no handler or projection may invent its own transition rules.
- AR20: Platform-host migration is parity-gated across architecture rows R1–R11; transitional Works AppHost/ServiceDefaults/runtime code remains until each applicable topology, telemetry, subscription, projection, query, reminder, process-runner, operations, executable, security, and command-submission proof is green with rollback demonstrated.
- AR21: A missing target runtime seam requires a named producer artifact, minimum published EventStore/Platform API and contract test, a consumer story, and a proof command before dependent Works work or removal can be marked complete.
- AR22: The Work-Tree Registry uses the tenant/domain/aggregate identity `(tenant, work-tree, registry)` and a fenced lifecycle `Reserved → Creating → Attached` or `Reserved → Released`; only Reserved may timeout-release, while Creating is preserved for repair or verified attachment.
- AR23: Parent admission and registry reservation bind tenant, parent, child, complete reserved child payload, suspend intent, parent sequence, payload digest, ReservationId, and monotonic FencingToken; EventStore revalidates the token immediately before child creation dispatch.
- AR24: Parent-bearing child creation outside a valid reservation is prohibited; only durable child-created evidence matching tenant, parent, child, ReservationId, FencingToken, and payload digest permits attachment, after which token-bearing parent bookkeeping is idempotent in every parent status.
- AR25: All fan-out, cascade, child-resume recovery, rebuild, and repair use one Works-owned `IWorkTreeTopologyReader` over the Registry read model with exact-token ancestry resolution and stable cursor-paged descendant enumeration; Reserved or missing evidence is retryable and unacknowledged.
- AR26: Platform supplies trusted per-tenant MaxDepth, reservation timeout, and topology quota before admission; missing or exhausted quota fails before reservation, while the domain breadth remains uncapped and EventStore supplies snapshots, replay bounds, saturation, backpressure, and quota-level latency evidence.
- AR27: Works folds each accepted state-changing event into an absolute `WorkContributionSnapshot`; EventStore treats it as opaque projection input, merges it into self and Attached ancestors, never appends it to domain streams, and publishes payload-free changed-key notifications only after durable read-model changes.
- AR28: External commands use Platform-authenticated OIDC identity and tenant membership; internal effects use short-lived asymmetric workload-delegation JWTs bound to audience, exact deterministic effect tuple, target, command type/digest, tenant, purpose, causation, issue time, and expiry.
- AR29: Responsibility-bound commands are authorized inside the serialized aggregate turn against immutable EventStore-issued context; caller payloads and read models never authorize mutation, denied commands do not call Handle or append, and AuthorityLevel remains descriptive rather than authoritative.
- AR30: Production fails closed with Dapr mTLS, declared trust domain/namespace, deny-by-default application/network access, TLS broker connections, producer/consumer ACLs, named internal identities, machine-readable production profile, and startup refusal when any required trust, policy, secret, scheduler, store, or audit configuration is absent.
- AR31: Security tests must reject direct-port, wrong-application, wrong-trust-domain, unauthorized-publish, forged-sequence, wrong-tenant, wrong-purpose, and ordinary-user origin-restricted calls, while proving a valid production-policy reminder path.
- AR32: Expiry persists an effective expiry instant and deterministic ScheduleToken on create/reschedule; reminder names and callbacks bind that witness, stale or colliding tuples quarantine, and policy changes affect future commands without changing historical replay.
- AR33: Every cross-aggregate effect uses the versioned EventStore-owned deterministic codec and immutable effect ordinal to derive a 52-character Crockford Base32 SHA-256 EffectId; MessageId and IdempotencyKey both reuse `wrk-<EffectId>`.
- AR34: EventStore atomically stores a target-scoped effect receipt with target events and metadata; identical key/digest retries return the recorded success, rejection, or no-op without redispatch, while semantic or tuple conflicts quarantine and effect evidence follows tenant retention/legal-hold rules.
- AR35: Recovery uses one strict validator and the EventStore exclusive-lower-bound cursor rule on live, replay, repair, and rebuild paths; it validates domain, canonical tenant, aggregate, monotonic positions, and full decoding, periodically retries quarantined work, alerts on bounded reason metrics, and keeps readiness degraded until audited disposition.
- AR36: Classify Obligation, notes, correlation/await values, and durable bodies as confidential tenant data; protect identifiers and causation as restricted metadata, encrypt durable data, audit privileged operations and denials, and block privileged mutation if the append-only audit sink is unavailable.
- AR37: Before non-synthetic shared data or new durable catalog types are admitted, accountable owners must approve retention, legal hold, and offboarding and prove stream/registry/projection/intent/checkpoint/audit/key restoration; production objectives are RPO ≤ 15 minutes, RTO ≤ 4 hours, and quarterly restore drills.
- AR38: Malformed, unknown, conflicting, or security-incomplete state-affecting evidence is captured in a tenant-scoped quarantine that degrades the affected capability; replay or disposition is authenticated, audited, and re-enters the same validation path.
- AR39: Use .NET 10/C# 14, Dapr only through the permitted abstraction, centrally pinned packages, nullable and warnings-as-errors, options-free PascalCase durable writing with tolerant reading, source-generated structured logging, and xUnit v3 + Shouldly + NSubstitute tests with persisted end-state assertions at real boundaries.
- AR40: Implementation readiness remains conditional on the documented recovery, ordered-delivery, Platform migration, runtime compatibility, security, production-data, lifecycle-migration, and CI architecture-fitness gates; stories must preserve this dependency order and may not claim production readiness without their named evidence.

### UX Design Requirements

UX-DR1: Preserve delivery horizons explicitly: v1 is a headless kernel and builder-facing test harness; MCP/CLI are Theme 2 remainder, chatbot/email are Theme 3, routing is Theme 4, Cost is Theme 5, hardening is Theme 6, and the FrontComposer web Module is uncommitted post-v1 work.

UX-DR2: Any future Works UI must compose through Hexalith.FrontComposer and Blazor Fluent UI V5, inherit the active theme, accent, semantic roles, typography, spacing, radii, elevation, focus, hover, selection, reduced-motion, and forced-colors behavior, and define no Works-specific theme tokens or copied Fluent styling.

UX-DR3: Use calm, factual, evidence-led, resource-backed language that names what is known and the next legal action; never gamify work, rewrite a Raw Act, expose internal errors, or describe acknowledged/eventual work as synchronously complete.

UX-DR4: A future web experience uses one Works Module entry with route-backed What's next and Work tabs, subordinate Work Item detail, and generated command routes; it adds no separate primary navigation for Capture, tree, Admin, Audit, or roadmap concepts.

UX-DR5: A page, dialog, or detail panel with two or more sibling titled regions uses one `FluentAccordion` with the primary item first and expanded; page chrome and one sole primary grid, form, detail, or visualization may remain outside and must never be hidden.

UX-DR6: Implement `burn-down-meter` with `FluentProgressBar` and `FluentText`, always labeling Estimated, cumulative Done, Remaining, and Unit, and render a determinate progress bar only for states where its fraction is truthful.

UX-DR7: Implement `roll-up-summary` with Fluent layout/text plus FrontComposer projection-health treatments, keeping own Remaining, rolled per-Unit Remaining, unestimated-descendant count, freshness, stale values, and Unavailable semantically distinct; never substitute zero for unavailable or unestimated work.

UX-DR8: Implement `work-status` with `FcStatusIcon`, the prescribed existing BadgeSlot mapping, adjacent visible localized text, and an accessible tooltip for all nine statuses; never use a custom glyph, palette, tinted pill, or command-lifecycle state as Work Status.

UX-DR9: Implement `party-reference` with Fluent avatar/text/layout and FrontComposer icons, showing resolved Party name and Channel with a stable neutral fallback; do not infer, label, or color-code executor kind from the binding.

UX-DR10: Implement `work-tree` with native Fluent tree semantics and disclosure, rendering only Attached edges in the authoritative hierarchy; show Reserved, Creating, Released/superseded, and delayed-leg evidence separately, preserve selection/disclosure on refresh, and describe eventual cascade without color or strike-through alone.

UX-DR11: Implement `queue-row` on the generated Fluent data grid and FrontComposer row-detail components, preserving the exact tenant/Executor filter and Priority/DueDate/WorkItemId ordering, actor-aware legal actions, filters, scroll, selection, and deterministic focus recovery after claim loss or row removal.

UX-DR12: Implement `capture-command` on generated command forms, authorization regions, placeholders, and lifecycle wrappers; make Obligation primary, expose its 4,000-character limit and every note's 1,000-character limit before entry, keep verified/server-derived values read-only, preserve input through all failures, and distinguish acknowledgement from projection confirmation.

UX-DR13: Implement `work-history` as an authorized semantic chronological list through a FrontComposer projection template, showing trusted actor/origin, localized time, act, bounded note, original and correction/handoff evidence without exposing raw payloads, hidden envelope data, or Conversation content.

UX-DR14: Implement `conversation-panel` as the separately owned Hexalith.Conversations projection/command view in its own labeled accordion item, with independent loading, posting, error, and authorization behavior; a correlation ID never grants access or merges dialogue into Works history.

UX-DR15: For Theme 3, implement `natural-language-response` with Fluent text area/button and lifecycle feedback, keep constrained actions primary, preserve submitted words, label interpretation and confidence, require confirmation when unsafe, and retain text on rejection or connectivity failure.

UX-DR16: For Theme 3, implement `email-action-set` with client-safe semantic markup, descriptive constrained-action links, reply-in-own-words fallback, success/recovery states, and plain-text parity; Theme 6 alone adds production link binding, single use, absolute expiry, prior-use evidence, idempotency, and step-up.

UX-DR17: Implement `pause-live-updates` with a labeled Fluent button, associated queued-count badge, and shared connection status; pause visual application but not receipt, announce pause once, keep count updates silent until focus, and apply/announce one coherent deduplicated batch on resume without focus theft.

UX-DR18: For Theme 5, implement `cost-meter` as a separately labeled Fluent meter and per-currency/Unit Roll-Up with freshness and Unavailable behavior; distinguish it from Effort by wording and placement rather than a fixed palette, and never silently convert or merge Units.

UX-DR19: Drive action visibility, disabled reasons, and submission authorization from server-authoritative capability metadata; do not infer authority from Status, Executor Binding, or AuthorityLevel, and distinguish audited authorization denial from domain rejection, idempotent no-op, and infrastructure-unknown outcomes.

UX-DR20: Cover the Works-specific state matrix, including Created-with-binding, unestimated, explicit residual completion, progress completion and correction/reopen, overrun, non-terminal zero, Unit rejection, multiple await conditions, active Handoff, reservation lifecycle, eventual resume/cascade, claim loss, access loss, Conversation boundary states, command lifecycle, and projection health.

UX-DR21: Keep Work Status, command lifecycle, aggregate state, and eventual projection evidence visually and semantically separate; never optimistically rewrite another aggregate or announce attachment, resume, cascade, completion, reopening, or handoff before authoritative confirmation.

UX-DR22: Render Roll-Up and live projection evidence with `Fresh`, `Stale`, `Reconnecting`, or `Unavailable` text plus time/position, retain the last trustworthy result when safe, omit untrustworthy numbers, group mixed Units separately, and coalesce updates without polling-dependent UX.

UX-DR23: Preserve tenant privacy and safe text rendering: fail closed before protected content appears, show only authorized Raw Acts, omit secrets/tokens/internal metadata, apply `lang`, `dir`, and bidi isolation to user content and identifiers, localize all product copy and values, and label translations or interpretations as derived.

UX-DR24: Meet WCAG 2.2 AA using native Fluent semantics, keyboard operability, visible/programmatically associated labels, errors and limits, no positive tabindex, non-color state cues, coalesced live announcements, stable focus restoration, and no focus movement from background projections or Reactor effects.

UX-DR25: Implement the complete Burn-Down accessibility edge-state matrix for ordinary determinate, unestimated, zero-estimate, overrun, non-terminal-zero, progress-completed, explicit residual-completed, and corrected/reopened states; the meter itself is never a live region and Work Status alone announces completion or reopening.

UX-DR26: At 320 CSS px and 400% zoom, primary content and actions reflow without clipping or two-dimensional scrolling except for a clearly labeled irreducible grid/tree region; support text-spacing overrides, ≥24×24 CSS px platform targets, reduced motion, and forced colors without losing state or focus evidence.

UX-DR27: The v1 test harness presents accepted, domain-rejected, authorization-denied, idempotent, and infrastructure-unknown evidence as structured headings and text with current domain state and a safe next action, remaining readable without color and without pretending to be production navigation.

UX-DR28: Theme 3 email implementation is blocked until named supported clients/high-contrast modes have evidence for perceivable text/links/action boundaries, client-safe semantic markup, plain-text parity, system fonts, unique descriptive links, 44×44 CSS px targets, narrow width, and 200% text sizing.

UX-DR29: Future responsive surfaces prioritize Obligation, Work Status, responsible Party, Remaining, and legal actions on small screens; secondary detail uses the same accordion or row-detail information architecture rather than a separate mobile design.

UX-DR30: Visual mockups are reference-only for information grouping and density; do not copy their raw HTML, CSS, palettes, navigation, sample values, controls, lifecycle language, or uncommitted roadmap behavior into the implementation.

### FR Coverage Map

FR1: Epic 1 - Create a tenant-scoped Work Item with canonical identity and valid initial state.
FR2: Epic 1 - Carry a bounded Obligation and resolve an optional Expectation reference without storing interpretation.
FR3: Epic 2 - Track immutable-Unit Effort through Estimated, Done, and derived Remaining.
FR4: Epic 2 - Maintain and change Priority and Due Date as event-recorded Schedule facts.
FR5: Epic 3 - Record Attached parent/child references and multiple Await-Conditions under Registry authority.
FR6: Epic 2 - Enforce the complete Work Item lifecycle and its rejection/no-op outcomes.
FR7: Epic 1 - Preserve every accepted state change as an append-only authenticated Raw Act.
FR8: Epic 2 - Report positive progress and complete through Burn-Down or an explicit act.
FR9: Epic 2 - Re-estimate and reschedule non-terminal work without implicit completion.
FR10: Epic 3 - Cancel, reject, expire, and eventually cascade termination through Attached descendants.
FR11: Epic 4 - Maintain an idempotent, rebuildable, freshness-aware recursive Roll-Up.
FR12: Epic 4 - Inherit Units safely and expose mixed-Unit trees as separate subtotals.
FR13: Epic 3 - Enforce tenant-closed, single-parent, acyclic, depth-bounded tree shape at reservation.
FR14: Epic 3 - Suspend active work on one or more typed Await-Conditions.
FR15: Epic 3 - Resume only from an exact matching trigger with narrowly defined idempotency.
FR16: Epic 3 - Reserve, create, and durably attach child work through the Registry and Reactor.
FR17: Epic 2 - Bind, reassign, change Channel, and hand off work without executor-kind branching.
FR18: Epic 2 - Support push assignment, shared queues, and single-winner claiming.
FR19: Epic 2 - Persist additive AuthorityLevel values without using them as v1 authorization.
FR20: Epic 2 - Query deterministic next work for an Executor or tenant coordinator.
FR21: Epic 1 - Reference sibling-owned Party, Conversation, Tenant, persistence, and identity data without copying it.
FR22: Epic 1 - Expose domain-owned expectation and routing ports while keeping the domain pure.
FR23: Epic 1 - Maintain the tracked owns-versus-references boundary decision record.
FR24: Epic 4 - Exercise the canonical Works service through the platform-owned Aspire topology without target host duplication.
FR25: Epic 4 - Verify the complete command/event pipeline with pure and boundary-appropriate automated tests.
FR26: Epic 3 - Coordinate every cross-aggregate effect through the mechanical, recoverable Reactor.

## Epic List

### Epic 1: Create a Trustworthy Work Item Kernel
Builders can create and replay tenant-scoped Work Items through stable domain contracts without copying sibling-module data or introducing infrastructure into the domain.

**FRs covered:** FR1, FR2, FR7, FR21, FR22, FR23

### Epic 2: Move Work Reliably from Assignment to Completion
Tenant members and Executors can prioritize, assign, queue, claim, hand off, progress, correct, complete, terminate, and discover work through one executor-neutral model.

**FRs covered:** FR3, FR4, FR6, FR8, FR9, FR17, FR18, FR19, FR20

### Epic 3: Coordinate Durable Work Trees and Waits
Builders and Executors can attach child work safely, suspend on durable conditions, resume after matching triggers, and propagate terminal outcomes through a recoverable Reactor process.

**FRs covered:** FR5, FR10, FR13, FR14, FR15, FR16, FR26

### Epic 4: See and Verify Whole-Tree Progress
Builders and objective owners can obtain trustworthy per-Unit whole-tree progress and verify that projections, recovery, and the complete pipeline operate through the platform-owned topology.

**FRs covered:** FR11, FR12, FR24, FR25

## Epic 1: Create a Trustworthy Work Item Kernel

Builders can create and replay tenant-scoped Work Items through stable domain contracts without copying sibling-module data or introducing infrastructure into the domain.

### Story 1.1: Integrate Through a Stable Headless Contract

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

### Story 1.2: Create and Rehydrate a Tenant-Scoped Work Item

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

### Story 1.3: Link a Conversation Without Owning Dialogue

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

### Story 1.4: Preserve Durable Contract Compatibility

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

## Epic 2: Move Work Reliably from Assignment to Completion

Tenant members and Executors can prioritize, assign, queue, claim, hand off, progress, correct, complete, terminate, and discover work through one executor-neutral model.

### Story 2.1: Enforce One Authoritative Lifecycle

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

### Story 2.2: Assign, Queue, and Claim Through One Binding

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
**When** the same assign → claim sequence is executed for each
**Then** the event streams differ only in `PartyId`, `Channel`, and `AuthorityLevel` field values
**And** architecture fitness tests fail if Server or Projections branch on executor kind or require a new event type for an additive Channel/AuthorityLevel value.

**Given** any defined AuthorityLevel value
**When** assignment or claim authorization is evaluated in v1
**Then** the value is persisted as descriptive binding data and does not grant access
**And** authenticated tenant membership and responsibility rules remain the authoritative authorization inputs.

### Story 2.3: Track Effort in One Immutable Unit

As an Executor,
I want effort represented consistently as Estimated, Done, and Remaining in one Unit,
So that progress and later tree totals remain meaningful without silent conversion (FR3).

**Acceptance Criteria:**

**Given** a valid first estimate at Work Item creation or through the first accepted ReEstimate
**When** the estimate is applied
**Then** `WorkItemEffort(Unit, Estimated, Done)` becomes the sole v1 effort state and the supplied Unit is established immutably
**And** initial Done is zero unless a backward-compatible historical event explicitly records otherwise.

**Given** Established Estimated and Done values
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

**Given** v1 package contents
**When** architecture and contract tests inspect Burn-Down types
**Then** no second effort abstraction, Cost meter, conversion policy, or cost-specific domain dependency exists
**And** the existing shape remains suitable for adding a separately labeled Cost concern in its future horizon.

### Story 2.4: Report Progress and Complete Work

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
**Then** completion cause, historical Estimated, Done, and residual/unestablished Remaining remain distinguishable
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

### Story 2.5: Correct Progress and Reopen Eligible Work

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
**And** the invalid submitted value remains available to headless/future client recovery without entering logs or durable state.

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

### Story 2.6: Re-Estimate and Reschedule Work

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

**Given** a terminal Work Item or an unauthenticated/cross-tenant caller
**When** ReEstimate or Reschedule is submitted
**Then** terminal invalidity produces the defined domain rejection, while identity failure is denied before Handle
**And** neither path mutates effort, Schedule, Status, or query state.

**Given** an accepted schedule change reaches the read side
**When** the "what's next" projection catches up
**Then** ordering reflects the latest Priority and Due Date without storing a derived rank in the event
**And** command acknowledgement remains distinct from projection confirmation.

### Story 2.7: Hand Off Active Work Without Changing Its State

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
**And** callers use Assign/rebind for Assigned work rather than disguising it as active Handoff.

**Given** a tenant member who is not the current bound Executor
**When** HandoffWorkItem is submitted
**Then** authorization is denied inside the serialized actor turn before Handle or append
**And** caller-supplied actor data or AuthorityLevel cannot authorize the handoff.

**Given** the same Party changes Channel during active work
**When** the valid new binding is handed off
**Then** the operation uses the same `HandoffWorkItem`/`WorkItemHandedOff` contract as a Party-to-Party handoff
**And** no channel-specific event, lifecycle path, or executor-kind branch is introduced.

**Given** system, internal, and external Party bindings
**When** identical active-handoff scenarios are executed
**Then** resulting event streams differ only in binding field values
**And** an architecture fitness test fails if a new doer kind requires Server/Projection changes or a new event type.

**Given** authorized history after a confirmed handoff
**When** it is consumed by the headless evidence contract or a future presentation
**Then** old and new responsibility, authenticated actor, time, Status, and preserved effort/await facts remain distinguishable
**And** responsibility is not shown as changed before authoritative confirmation.

**Given** future Theme 4 routing is absent
**When** v1 Handoff behavior is built
**Then** the explicit accepted binding is applied without routing candidates, scores, escalation policy, or AuthorityLevel enforcement
**And** future routing may add its decision evidence without reshaping this operation.

### Story 2.8: Discover the Next Work Deterministically

As an Executor or tenant coordinator,
I want a stable view of claimable and assigned work,
So that I can choose what to do next without hidden routing policy or replay-dependent ordering (FR4, FR6, FR18, FR20).

**Acceptance Criteria:**

**Given** an authenticated tenant member requests the coordinator view without an Executor PartyId
**When** the "what's next" query executes
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

**Given** v1 has no routing implementation
**When** the query is executed
**Then** eligibility, score, cost, confidence, workload kind, and escalation policy do not filter or order results
**And** `IExecutorRouter` remains unwired without preventing the query from functioning.

**Given** two otherwise identical event histories are replayed into empty projection state
**When** the resulting views are compared
**Then** rows, filters, binding facts, and ordering are identical
**And** no derived ordering position is required in a durable domain event.

**Given** headless evidence renders a query result or a stale Claim outcome
**When** the view is consumed
**Then** it names exact Work Status, responsible Party reference, Channel, Priority, Due Date, Effort/Unit, freshness, and next legal action as available
**And** claim loss is presented as current domain evidence rather than a generic concurrency crash.

## Epic 3: Coordinate Durable Work Trees and Waits

Builders and Executors can attach child work safely, suspend on durable conditions, resume after matching triggers, and propagate terminal outcomes through a recoverable Reactor process.

### Story 3.1: Derive Stable Identities for Cross-Aggregate Effects

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

### Story 3.2: Submit Cross-Aggregate Commands Idempotently

As a Reactor developer,
I want target command submission to remember each effect's durable disposition,
So that redelivery cannot duplicate a logical cross-aggregate act or reinterpret it after recovery (FR26).

**Acceptance Criteria:**

**Given** a valid internal command with its EffectId, full canonical tuple, semantic command digest, causation, and workload delegation
**When** EventStore dispatches it to the target actor partition
**Then** target events/metadata and a private effect-inbox receipt commit atomically
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
**Then** signature, audience, expiry, exact EffectId/tuple, target, command type/digest, tenant, purpose, causation, and mTLS-attested application identity must all match
**And** an unlisted origin-command pair is denied and audited before target dispatch.

**Given** an effect receipt and its source/target domain evidence
**When** lifecycle cleanup, legal hold, offboarding, backup, or restore occurs
**Then** the receipt is not pruned with the Work Item lifecycle and follows the tenant's approved retention class
**And** receipt and corresponding source/target evidence erase only as one authorized tenant operation.

**Given** a replay request predates the retained source-evidence floor
**When** it attempts to resubmit the effect
**Then** the request rejects or quarantines rather than executing against unverifiable history
**And** gateway terminal records may optimize lookup but are not treated as the correctness boundary.

**Given** success, rejection, no-op, duplicate, conflict, crash-before-commit, and crash-after-commit integration scenarios
**When** the focused test lane runs
**Then** persisted target state and receipt contents match the expected end state for each scenario
**And** tests assert durable contents rather than only response codes or mock invocation counts.

### Story 3.3: Run Mechanical Reactor Effects Durably

As a Works process author,
I want Reactor translations executed by a checkpointed EventStore process runner,
So that cross-aggregate coordination survives duplicate delivery, paging, and process restarts without a second domain kernel (FR26).

**Acceptance Criteria:**

**Given** the generic process-runner capability is absent from the current EventStore SDK
**When** this story begins implementation
**Then** the owning EventStore repository first publishes a named package/API contract and focused provider test for checkpointed event-to-command processing
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

### Story 3.4: Reserve Only Valid Work-Tree Attachments

As a builder coordinating related work,
I want the Work-Tree Registry to reserve only valid parent-child edges,
So that concurrent callers cannot create cycles, multiple parents, cross-tenant trees, or unbounded depth (FR5, FR13, FR16).

**Acceptance Criteria:**

**Given** a tenant's Work-Tree Registry
**When** its aggregate identity and stream route are resolved
**Then** it uses canonical identity `(tenant, domain=work-tree, aggregateId=registry)` and the registered Registry command/event contracts
**And** no Work Item stream, caller projection, or alternate registry becomes a second topology authority.

**Given** a caller requests child attachment
**When** the public reserve command is constructed
**Then** it carries the complete child-creation payload verbatim, including Obligation, optional effort/Unit, Schedule, binding, Conversation/Expectation references, child identity, and suspend intent
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
**Then** the domain imposes no separate fan-out cap and the Registry remains snapshot/replay bounded with observable saturation and backpressure
**And** quota-level latency evidence is required before production admission.

### Story 3.5: Create and Attach a Reserved Child

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

### Story 3.6: Suspend Work on Multiple Await-Conditions

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

### Story 3.7: Resume Only on an Exact Matching Trigger

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
**Then** its named workload/adapter origin, tenant delegation, purpose, causation, target, and command digest must satisfy the origin policy
**And** an ordinary user or wrong workload is denied before Handle, even with a valid matching condition.

**Given** an Attached child emits `WorkItemCompleted`
**When** the Reactor resolves its exact parent through `IWorkTreeTopologyReader`
**Then** it submits a deterministic Resume carrying `ChildCompleted(childId)` to that parent through the durable process runner
**And** missing or merely Reserved topology is retryable and remains unacknowledged rather than being treated as no parent.

**Given** a child has completed or a date trigger has fired but the Resume effect has not committed
**When** parent state is read
**Then** the parent remains authoritatively `Suspended` until `WorkItemResumed` lands
**And** headless/future presentation may show “trigger received; resume pending” only when durable coordination evidence supports it.

**Given** duplicate and reordered trigger deliveries plus aggregate replay
**When** focused tests reconstruct state and re-run each condition
**Then** exactly one matching trigger produces `WorkItemResumed`, its duplicate no-ops, and every nonmatch rejects
**And** the results are identical across child, date, and external condition kinds without requiring a production external adapter in v1.

### Story 3.8: Resume or Expire from Durable Schedule Witnesses

As an Executor or coordinator,
I want date waits and expiry to survive restarts and rescheduling,
So that time-based work advances only from current durable evidence rather than an in-memory clock (FR4, FR10, FR15, FR26).

**Acceptance Criteria:**

**Given** the generic typed-reminder and reconciliation seam is absent from the current EventStore SDK
**When** implementation begins
**Then** the owning EventStore repository first publishes the named package/API contract and focused registration, callback, cancellation, and recovery tests
**And** the Works consumer is not completed against bespoke local reminder plumbing.

**Given** an accepted lifecycle or Schedule event requires DateResume or Expiry work
**When** Works translates it
**Then** it emits a typed `PendingWorkIntent` containing kind, canonical tenant/item, due UTC instant, ScheduleToken, source envelope sequence, and typed callback payload
**And** EventStore—not the aggregate—owns generic registration and reconciliation.

**Given** a Work Item reminder actor is addressed
**When** its actor identity is derived
**Then** it uses the registered application, actor type, and `wra-<digest>` identity computed by the shared canonical codec
**And** a stored full-tuple mismatch for the digest is treated as collision plus quarantine.

**Given** create or reschedule establishes an effective expiry instant
**When** the corresponding Expiry intent is built
**Then** its ScheduleToken and reminder name bind tenant, item, UTC ticks, and state-change schedule revision
**And** rescheduling cancels/supersedes stale intent mechanically without rewriting historical policy facts.

**Given** a DateResume or Expiry reminder fires
**When** its callback submits Resume or Expire
**Then** the command carries the current persisted schedule witness, deterministic effect identity, named workload delegation, tenant purpose, and causation
**And** the aggregate changes state only after EventStore validates the current witness and designated origin.

**Given** an older reminder fires after reschedule, cancellation, completion, or another accepted resume
**When** its callback is processed
**Then** the witness mismatch or current lifecycle produces the defined rejection/no-op disposition without stale state change
**And** repeated delivery returns the durable effect receipt rather than registering a duplicate logical act.

**Given** a crash, scheduler outage, missed firing, or lost discovery-index entry
**When** periodic reconciliation runs
**Then** authoritative streams and pending intent state rediscover and resubmit every outstanding current reminder
**And** tenant/pending indexes are treated only as bounded discovery aids with CAS-protected updates and audited prune/offboarding.

**Given** Platform composes the reminder runtime
**When** production readiness is evaluated
**Then** Platform owns scheduler persistence, high availability, backup, callback failure policy, tenant timing policy, trust configuration, and alerts
**And** no production admission is allowed without those owners and a successful restore/reconciliation proof.

**Given** security tests cover reminder routes
**When** direct-port, wrong-application, wrong-tenant, wrong-purpose, expired-delegation, and valid production-policy callbacks execute
**Then** every invalid origin fails before dispatch and the valid callback reaches the correct tenant/item command path
**And** audit evidence contains bounded identifiers and causation without payload contents or tokens.

### Story 3.9: Terminate Work and Cascade Across Attached Descendants

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
**Then** Cancel/Expire discloses descendant propagation, default Reject distinguishes requeue from terminal rejection, and active descendants may be labeled pending propagation
**And** no child is visually marked terminal before its own authoritative event arrives.

### Story 3.10: Recover and Repair Tree Coordination Safely

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
**And** consumers receive explicit Unavailable/degraded state until authenticated repair resolves the contradiction.

**Given** live, replay, repair, or rebuild processing reads source pages
**When** the shared recovery validator encounters a wrong domain, noncanonical tenant, wrong aggregate, non-increasing position, or undecodable state-affecting record
**Then** it captures a durable tenant-scoped quarantine item before acknowledging the delivery
**And** no path silently skips, partially applies, or locally reinterprets the evidence.

**Given** bounded hot retries have been exhausted
**When** unresolved coordination remains
**Then** recovery health stays degraded, bounded kind/reason metrics and alerts remain active, and periodic retry continues
**And** readiness becomes healthy only after an authenticated audited disposition and successful catch-up.

**Given** a backup restore or process-host migration
**When** Reactor processing restarts
**Then** process-effect checkpoints restore separately and reconcile against source events, target receipts, pending intents, and deterministic EffectIds
**And** missing checkpoint state causes safe reissue rather than inferred success or duplicated effects.

**Given** operators inspect reservation, attachment, resume, reminder, or cascade recovery
**When** status evidence is rendered
**Then** Reserved, Creating, Attached, Released/superseded, delayed-leg rejection, pending resume/cascade, quarantine, and recovered states remain distinguishable as text
**And** diagnostics expose bounded identifiers, freshness, and a safe next action without payloads, secrets, stack traces, or trust internals.

**Given** property and integration tests inject duplicate delivery, reordering, page boundaries, crashes, stale tokens, malformed evidence, and operator disposition
**When** the recovery suite completes
**Then** persisted Registry state, child streams, effect receipts, process checkpoints, quarantine records, and readiness state match the expected end state
**And** no test passes solely from response codes, mock counts, sleeps, or assumed broker ordering.

## Epic 4: See and Verify Whole-Tree Progress

Builders and objective owners can obtain trustworthy per-Unit whole-tree progress and verify that projections, recovery, and the complete pipeline operate through the platform-owned topology.

### Story 4.1: Fold Absolute Work Contribution Snapshots

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
**And** the focused tests cover unestimated, zero, overrun, both completion kinds, correction/reopen, and all terminal states.

### Story 4.2: Merge Roll-Up State Across Attached Ancestry

As an objective owner,
I want descendant contributions merged into every Attached ancestor without lost updates,
So that whole-tree Remaining converges correctly under concurrent, duplicate, and reordered projection delivery (FR5, FR11, FR12, FR13).

**Acceptance Criteria:**

**Given** the generic relationship-aware CAS projection seam is absent from the current EventStore SDK
**When** implementation begins
**Then** the owning EventStore repository first publishes a named merge/relationship API, package contract test, provider proof, and changed-key notification seam
**And** Works does not introduce custom durable projection routing or database-specific clients as a substitute.

**Given** a tenant and ancestor WorkItemId
**When** a Roll-Up document key is resolved
**Then** the logical key follows `works:v3:tenant:<encoded-tenant>:rollup:<encoded-ancestor>` and the tenant manifest selects its epoch-qualified physical key
**And** readers never discover a generation by listing or choosing physical keys directly.

**Given** a Roll-Up document receives topology evidence
**When** the pure Works merge applies it
**Then** each descendant topology watermark is last-writer-wins only by Registry envelope sequence and carries ReservationId plus Attached/tombstoned state
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
**When** EventStore performs ETag read/merge/retry
**Then** the atomic commit preserves every independent slot and materializes deterministic ordinal per-Unit totals plus unestimated-descendant count
**And** retry exhaustion surfaces an infrastructure conflict without a partial acknowledgement or lost update.

**Given** equal source sequence is received twice
**When** canonical bytes are equal or unequal
**Then** equal bytes are a no-op, while unequal bytes create conflict quarantine and degrade the affected Roll-Up
**And** neither case additively accumulates a contribution.

**Given** an edge transitions to Attached after a contribution was previously withheld
**When** deterministic backfill resolves exact ancestry through `IWorkTreeTopologyReader`
**Then** the current absolute snapshot is merged into the item and every valid ancestor
**And** a missing/Reserved relation remains retryable and unacknowledged rather than being accepted as an empty ancestor set.

**Given** duplicate, reorder, concurrent-branch, attachment, tombstone, and CAS-conflict integration scenarios
**When** focused tests complete
**Then** persisted Roll-Up document contents, ETags, topology watermarks, contribution slots, totals, and quarantine state match expected end states
**And** tests prove convergence without relying on broker order, response codes, or mock counts alone.

### Story 4.3: Expose Trustworthy Whole-Tree Progress

As an objective owner,
I want own and recursive Remaining reported with Unit and freshness evidence,
So that I can understand the whole Work Tree without mistaking stale, unavailable, or unestimated data for completed work (FR11, FR12).

**Acceptance Criteria:**

**Given** an authorized Roll-Up query for an estimated single-Unit tree at quiescence
**When** the read model is returned
**Then** it exposes the selected item's own Remaining separately from the recursive total of each Attached descendant exactly once
**And** the result identifies Unit, unestimated-descendant count, freshness position/time, and availability state.

**Given** a tree contains descendants with different explicitly established Units
**When** Roll-Up is materialized and queried
**Then** each Unit has a separate deterministic subtotal and no implicit conversion or combined number is produced
**And** same-Unit descendants share one subtotal regardless of depth.

**Given** unestimated non-terminal descendants
**When** Roll-Up is queried
**Then** they add zero to numeric totals and increment the active-unestimated count
**And** a numeric rolled zero remains distinguishable from “nothing estimated yet.”

**Given** terminal descendants, explicit residual completion, or a corrected/reopened descendant
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

**Given** the provisional v1 fixture with depth 32 and top-level fan-out 50 (about 1,600 items)
**When** every published tree event has been acknowledged by the projection
**Then** Roll-Up converges within the configured five-second bound and an authorized read answers in under 200 ms from the read model
**And** tests await durable acknowledgements rather than sleeping for an assumed delay.

**Given** headless evidence or a future `roll-up-summary`/`work-tree` presentation consumes the read model
**When** it formats results
**Then** own Remaining, per-Unit rolled Remaining, unestimated count, freshness, and Unavailable are textually and semantically separate
**And** only Attached nodes appear in the authoritative tree while background updates do not imply synchronous cross-aggregate state.

### Story 4.4: Rebuild Read Models Behind a Tenant Epoch

As a platform operator,
I want all related Works read-model families rebuilt behind one atomic tenant fence,
So that readers never observe a partial generation or lose live updates during repair and migration (FR11, FR20, FR24, FR25).

**Acceptance Criteria:**

**Given** an authenticated audited rebuild request for a tenant
**When** EventStore creates a new `works-runtime-v3` epoch
**Then** the epoch seals the tenant inventory and source high-watermarks shared by Roll-Up, what's-next, topology, pending intents, quarantine/parking, and related read families
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
**Then** it contains every document required by each family, including empty/tombstoned state needed for correct reads
**And** promotion is prohibited while any required page, quarantine disposition, or source range is unresolved.

**Given** staging is complete
**When** Commit succeeds
**Then** one manifest change atomically selects the new generation and queued catch-up drains only into that promoted generation
**And** readers use the manifest as sole selector and remain stale/unavailable until every post-capture position is applied and the fence opens.

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
**Then** readers observe only the prior complete generation or the promoted complete generation with explicit stale/unavailable intervals
**And** persisted manifests, journals, checkpoints, generations, and catch-up end state prove no acknowledged write was omitted.

### Story 4.5: Compose the Minimal Works Domain Service

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
**Then** Contracts, Server, Projections, and Reactor retain their inward dependency rules and only the executable composes EventStore/runtime adapters
**And** no domain project references Dapr, Aspire, database/broker clients, ServiceDefaults, FrontComposer, Fluent UI, LLM, routing, or Cost implementations.

**Given** the Works solution is built locally
**When** restore and compilation run
**Then** the `.slnx`, centrally pinned package versions, .NET 10/C# 14 settings, nullable analysis, and warnings-as-errors are used
**And** no legacy `.sln`, inline package version, Dockerfile, or out-of-band dependency pin is introduced.

**Given** the v1 horizon
**When** executable endpoints and registrations are enumerated
**Then** only the domain-service command, query, projection, Reactor, health, and operational seams required by v1 are present
**And** no production web, MCP, CLI, chatbot, email, LLM, routing, Cost, or Theme 6 product surface is exposed.

**Given** current transitional Works AppHost and ServiceDefaults code still supplies a parity row not yet proven in Platform
**When** this minimal target service is introduced
**Then** transitional code remains clearly identified and operational until the corresponding migration acceptance is complete
**And** this story does not delete or replace it merely because target composition compiles.

**Given** structured diagnostics are emitted by the service
**When** source-generated logging and RFC 9457 error mapping run
**Then** they include bounded correlation, tenant, command type, disposition, and health metadata only
**And** event/command payloads, Raw-Act text, secrets, tokens, Party data, and stack traces are excluded from public output.

**Given** focused unit, architecture, and service-start tests
**When** the story is validated
**Then** the minimal executable resolves all registered domain capabilities through published seams and passes health wiring without a production adapter
**And** a forbidden dependency, duplicate runtime service, or unresolved required registration fails the build/test lane.

### Story 4.6: Authorize Commands from Verified Identity

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
**Then** Assigned Claim requires the current bound Party while Queued Claim binds the admitted authenticated Party using trusted tenant policy for Channel/AuthorityLevel
**And** a target Claim accepts no caller-provided binding as authority.

**Given** Assign, ReEstimate, Reschedule, Cancel, root Create, Queue, or LinkConversation
**When** an authenticated target-tenant member submits it
**Then** v1 applies the documented tenant-member authorization floor and the aggregate decides domain validity
**And** persisted AuthorityLevel remains descriptive data with no authorization branch.

**Given** Reactor, reminder, cascade, external-resume, repair, replay, rebuild, or quarantine work
**When** Platform issues workload delegation
**Then** a short-lived asymmetric JWT binds audience, exact effect tuple/EffectId, target, command type/digest, tenant, named purpose, causation, issue time, and expiry
**And** EventStore recomputes every binding and matches the target application to mTLS/ACL-attested identity before dispatch.

**Given** Registry-authorized child Create/SpawnChild or any other origin-restricted command
**When** the origin policy is evaluated
**Then** only the specifically allow-listed workload and purpose may invoke it and no global-admin workload bypass exists
**And** every unlisted origin-command pair fails before Handle or append.

**Given** a query for what's-next, Roll-Up, topology, history, or recovery state
**When** authorization succeeds at tenant scope
**Then** result filtering independently removes unauthorized data before serialization
**And** possession of a key, PartyId, correlation ID, or delegated workload identity cannot expand the caller's readable scope.

**Given** positive and negative authorization tests
**When** missing identity, wrong tenant, actor mismatch, stale binding, caller-supplied actor, wrong workload, wrong purpose, target mismatch, expired token, and valid cases execute
**Then** only the exact permitted cases reach domain Handle/query and every denial leaves domain/event state unchanged
**And** audit records contain bounded reason, identity reference, target, and causation without token or payload contents.

### Story 4.7: Fail Closed on Production Routes and Origins

As a platform operator,
I want the production Works topology to reject undeclared routes, publishers, and insecure configuration,
So that development conveniences and spoofed workload identities cannot reach durable domain paths (FR24, FR25).

**Acceptance Criteria:**

**Given** a production deployment profile
**When** Works, EventStore, Dapr, scheduler, state store, broker, and supporting services communicate
**Then** Dapr mTLS uses the declared trust domain/namespace, network and application access are deny-by-default, and broker connections use TLS with producer/consumer ACLs
**And** direct service ports and undeclared cross-service routes are unavailable to ordinary workloads.

**Given** Works or Registry domain events are published
**When** broker origin policy is evaluated
**Then** only EventStore's named producer identity may publish them and only registered consumers may subscribe
**And** forged publisher identity or caller-supplied high envelope sequence is rejected before state-affecting processing.

**Given** reminder, Reactor, replay, rebuild, repair, quarantine, or operator routes
**When** a request reaches them
**Then** only the named workload/operator identities and exact purposes declared by policy are admitted
**And** a valid application identity alone does not grant access to an unrelated command or tenant.

**Given** the machine-readable production profile is loaded at startup
**When** required trust, ACL, delegation keys, secret-store, scheduler, state-store, broker, audit, or tenant-policy configuration is missing or permissive
**Then** startup or production admission fails closed with degraded/unready health
**And** the application cannot silently fall back to a development profile or insecure default.

**Given** the selected Dapr hosting integration is a preview version
**When** production admission is requested
**Then** it is rejected unless the accountable Platform Maintainer records a time-bounded exception, owner, risk, expiry, and stable-upgrade trigger
**And** development/test approval is not presented as production compatibility evidence.

**Given** secrets or workload signing keys rotate
**When** old and new configuration overlap
**Then** Platform's secret-store policy preserves authorized availability only for the documented transition window and rejects expired material afterward
**And** secret values never enter logs, traces, metrics, health payloads, or ProblemDetails.

**Given** negative production-policy tests
**When** direct-port, wrong application, wrong namespace/trust domain, unauthorized publication, forged sequence, wrong tenant/purpose, ordinary-user SpawnChild, and missing-profile cases run
**Then** every request fails before domain dispatch or protected disclosure
**And** a valid production-policy reminder callback and ordinary authorized command both pass through the intended secured path.

**Given** policy denial or audit/secret dependency failure
**When** operational health is reported
**Then** readiness and bounded alerts identify the affected capability without tenant payload data or trust internals
**And** production traffic remains blocked until the configured policy and dependency are verifiably healthy.

### Story 4.8: Protect and Restore Durable Tenant Data

As an accountable data owner,
I want Works data classified, retained, erased, backed up, and restored through explicit ownership,
So that real tenant work is not admitted without enforceable privacy, audit, and disaster-recovery guarantees (FR7, FR21, FR24, FR25).

**Acceptance Criteria:**

**Given** Works durable fields are inventoried
**When** data classification is reviewed
**Then** Obligation, notes, Await/correlation values, and durable bodies are classified as confidential tenant data, while identifiers and causation are restricted operational metadata
**And** Works owns minimization/classification without duplicating sibling-owned content.

**Given** durable state is stored or transported
**When** platform controls are evaluated
**Then** EventStore owns encryption in transit/at rest plus tenant-keyed retention/erasure mechanisms, while Platform owns secret rotation, backup/restore, disaster recovery, and the privileged audit sink
**And** Works domain code contains no provider-specific encryption, database, backup, or secret-management implementation.

**Given** a privileged repair, replay, rebuild, quarantine disposition, offboarding, or authorization denial
**When** the operation executes
**Then** bounded actor/workload, tenant, purpose, causation, target, and result evidence is written to the append-only audit sink
**And** audit-sink failure blocks privileged mutation instead of allowing an unaudited success.

**Given** a tenant retention, legal hold, or offboarding operation
**When** durable data is selected
**Then** streams, Registry state, projections, pending intents, checkpoints, quarantine, effect receipts, and corresponding source/target evidence follow one approved tenant policy
**And** effect correctness evidence is not erased independently from the events it proves.

**Given** no approved accountable owner, retention/legal-hold/offboarding policy, or successful restore drill exists
**When** non-synthetic shared data or a new durable catalog type is proposed
**Then** production admission fails before the data or new type is accepted
**And** the missing owner/evidence is reported as a readiness blocker rather than an assumed default.

**Given** production recovery objectives
**When** backup and disaster-recovery plans are validated
**Then** they target RPO of at most 15 minutes, RTO of at most four hours, and a documented restore drill at least quarterly
**And** the measured result, accountable owner, environment, and any failure are retained as audit evidence.

**Given** a restore drill
**When** the recovered environment is verified
**Then** Work Item streams, Registry topology, Roll-Up/what's-next projections, pending reminder intents, process checkpoints, effect receipts, quarantine/audit state, tenant keys, and access policies are restored or deterministically rebuilt
**And** source-to-target reconciliation proves no acknowledged domain act or pending effect was silently lost.

**Given** confidential tenant data is handled by observability, errors, tests, backups, or operator tools
**When** outputs are inspected
**Then** payloads, Raw-Act text, Party data, secrets, tokens, and full command bodies are absent from logs, metrics, traces, health, and ProblemDetails
**And** test fixtures use synthetic/minimized data unless an explicitly approved protected-data process applies.

**Given** an authorized offboarding completes
**When** tenant data and control indexes are pruned
**Then** tombstones prevent stale discovery or recovery from recreating removed state and the operation is crash-reconcilable
**And** no other tenant's namespace, encryption material, audit evidence, or readiness is affected.

### Story 4.9: Gate Migration to the Platform-Owned Host

As a Works maintainer,
I want host ownership migrated only after exact Platform parity is proven,
So that removing transitional Works infrastructure cannot silently drop runtime, security, or recovery behavior (FR24, FR25).

**Acceptance Criteria:**

**Given** architecture migration rows R1 through R11
**When** readiness is evaluated
**Then** every applicable row records its owning producer artifact, published package/API version, contract test, Works consumer story, exact proof command, result, environment, and accountable owner
**And** an absent producer seam, compile-only check, stale result, or undocumented local substitute keeps that row non-green.

**Given** topology/defaults, subscription, projection/rebuild, query, reminder, process runner, recovery, executable, security, and command-submission consumers
**When** Platform parity scenarios run
**Then** each behavior matches the accepted Works baseline under normal, failure, restart, duplicate, security-negative, and recovery paths
**And** persisted end state and health evidence—not process startup alone—prove parity.

**Given** any applicable R1–R11 row is incomplete or failing
**When** migration is proposed
**Then** the current Works AppHost, ServiceDefaults, route, Dapr wiring, reminder/recovery source, or other transitional owner remains available
**And** documentation continues to distinguish CURRENT behavior from TARGET ownership.

**Given** every applicable parity row is green
**When** rollback is exercised before removal
**Then** the prior known-good topology can be restored with compatible streams, Registry, projections, intents, checkpoints, receipts, security policy, and tenant keys
**And** rollback meets the declared recovery objectives without accepting unverified data loss.

**Given** parity and rollback have passed against the locked head and package set
**When** transitional Works hosting is removed
**Then** Works retains only the minimal domain-service executable and removes bespoke AppHost, Aspire, ServiceDefaults, `/project` route, local Dapr actor/reminder, telemetry, and recovery plumbing covered by Platform
**And** the `.slnx`, architecture fitness rules, documentation, and test topology are updated atomically to the target ownership model.

**Given** runtime compatibility is part of the proof
**When** versions are inspected
**Then** the current owning files—not prose—authoritatively identify .NET/Aspire SDK, Dapr integration/runtime, package, image, and EventStore versions
**And** any version change receives restore, Release build, focused integration, and Platform parity evidence before acceptance.

**Given** a preview hosting integration remains in the target stack
**When** production parity is assessed
**Then** the migration remains development/test-only unless the accountable Platform Maintainer's time-bounded production exception is active
**And** exception expiry or availability of a stable replacement reopens the compatibility gate.

**Given** the migrated target topology
**When** architecture, startup, security, integration, and rollback tests run
**Then** no duplicate Works/platform runtime owner remains and every required domain capability is resolvable through the published seams
**And** failed parity or changed producer/consumer heads invalidates the recorded acceptance until the exact evidence is rerun.

### Story 4.10: Prove the Complete Kernel Through the Builder Harness

As a Hexalith builder,
I want one repeatable evidence harness for the complete Works kernel,
So that lifecycle, coordination, projections, security, recovery, and architectural boundaries are proven together before release (FR1-FR26).

**Acceptance Criteria:**

**Given** the platform-owned Aspire topology and an authenticated synthetic tenant
**When** the canonical v1 journey creates, assigns or queues, claims, progresses, reserves and attaches a child, suspends, completes the child, resumes the parent, and completes the parent
**Then** every expected persisted Work Item/Registry event, target receipt, Reactor checkpoint, reminder disposition, query row, and Roll-Up value is observed in causal order at quiescence
**And** the Works repository uses no production channel adapter or duplicated platform runtime to drive the journey.

**Given** the process restarts while a parent is Suspended or a Registry/Reactor effect is incomplete
**When** the topology recovers
**Then** event replay reconstructs aggregate state, pending effects are reissued with stable identities, exact trigger semantics resume the parent, and projections converge
**And** no acknowledged Raw Act, Await-Condition, edge, effect disposition, or progress fact is lost or duplicated.

**Given** two Executors race for one Queued item and a terminal parent has an open Attached subtree
**When** claim and cascade scenarios execute
**Then** exactly one claim succeeds, the loser receives the defined current-state rejection, and every still-active descendant eventually receives the correct Cancel/Expire result while prior terminal descendants remain unchanged
**And** persisted end-state assertions cover streams, binding, topology, receipts, checkpoints, and projections.

**Given** system, internal, and external Executor Bindings
**When** the identical assign → claim → progress → complete journey runs for each
**Then** golden event streams differ only in binding field values and adding an allowed Channel or AuthorityLevel requires no Server/Projection branch or new event type
**And** the architecture fitness lane fails on any executor-kind specialization.

**Given** fresh empty Roll-Up and "what's next" generations
**When** all authorized tenant streams are replayed and every event is acknowledged
**Then** rebuilt read models are identical to the live models at quiescence, including per-Unit totals, unestimated count, filters, ordering, freshness, and attachment state
**And** no domain payload contains rolled totals, ordering positions, interpreted Expectations, or other derived projection data.

**Given** the provisional depth-32/fan-out-50 performance fixture
**When** final events are published under the platform lane
**Then** Roll-Up convergence is acknowledgement-driven within five seconds and authorized read-model access completes in under 200 ms
**And** failure reports separate the numeric budget from environmental or broad-gate blockers without weakening the requirement.

**Given** pure Tier-1 tests and integration boundaries
**When** validation runs
**Then** aggregate Handle/Apply, Reactor translations, projection folds, and merge properties execute without Dapr, network, browser, or containers, while integration uses EventStore testing support or Platform Aspire only for genuine boundaries
**And** each test project runs individually with xUnit v3 and persisted end-state assertions where durable storage is involved.

**Given** accepted, domain-rejected, authorization-denied, idempotent, and infrastructure-unknown outcomes
**When** the v1 builder harness renders them
**Then** structured headings and text identify outcome class, current authoritative domain evidence, projection freshness where applicable, and a safe next action
**And** output remains understandable without color, never calls acknowledgement completion, preserves safe retry context, and excludes payloads, secrets, personal data, tokens, and trust internals.

**Given** extracted UX requirements UX-DR1 through UX-DR30
**When** story traceability is audited
**Then** v1-actionable headless evidence and domain/read-model semantics map to the relevant stories, while every web, email, natural-language, routing, Cost, and visual-component implementation remains explicitly assigned to its future or uncommitted horizon
**And** no UX requirement is silently dropped, falsely claimed as implemented, or used to introduce production UI into v1.

**Given** a Release restore/build has completed
**When** CI quality gates execute
**Then** focused Unit, Property, Integration, and Architecture test assemblies run and the tracked architecture step invokes `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests.dll`
**And** compilation alone, a solution-level test shortcut, skipped security/recovery evidence, or unresolved readiness gate cannot mark the epic complete.
