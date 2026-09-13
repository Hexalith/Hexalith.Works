---
stepsCompleted: [1, 2]
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

<!-- Repeat for each epic in epics_list (N = 1, 2, 3...) -->

## Epic {{N}}: {{epic_title_N}}

{{epic_goal_N}}

<!-- Repeat for each story (M = 1, 2, 3...) within epic N -->

### Story {{N}}.{{M}}: {{story_title_N_M}}

As a {{user_type}},
I want {{capability}},
So that {{value_benefit}}.

**Acceptance Criteria:**

<!-- for each AC on this story -->

**Given** {{precondition}}
**When** {{action}}
**Then** {{expected_outcome}}
**And** {{additional_criteria}}

<!-- End story repeat -->
