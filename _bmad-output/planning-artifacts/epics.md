---
stepsCompleted: [1]
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

## Requirements Inventory

### Functional Requirements

FR1: A builder or Executor can create a tenant-scoped root Work Item with a required Obligation and optional initial effort/Unit, Schedule, Executor Binding, Conversation reference, and Expectation reference. Ordinary creation cannot supply a parent; child creation requires matching origin-restricted Reactor and Registry authorization. Creation emits `WorkItemCreated`, assigns canonical identity, and rests at `Created` even when a binding is supplied.

FR2: A Work Item carries a trimmed, non-empty human-readable Obligation and an optional Expectation reference resolved on demand through `IExpectationResolver`; interpreted expectation data and long-form content are not stored in the aggregate. New command admission accepts at most 4,000 characters and rejects longer content without mutation, while previously accepted longer `WorkItemCreated` payloads remain deserializable and replayable.

FR3: A Work Item carries one Effort Burn-Down containing Unit-tagged Estimated, cumulative Done, and derived `Remaining = max(Estimated - Done, 0)`. Estimated is non-negative; the first explicit or inherited Unit is immutable; progress, correction, or re-estimation in another Unit rejects; and incompatible Units are never silently combined.

FR4: A Work Item carries an optional Schedule of Priority and Due Date. Priority uses the additive-tolerant ordered values `Critical`, `High`, `Normal`, and `Low`; either field can be supplied at creation and changed later through an event-recorded act; missing values are valid and sort last.

FR5: A Work Item records at most one parent reference, zero or more child references, and one or more Await-Conditions while Suspended. The Work-Tree Registry owns authoritative parent-child edges; only `Attached` edges participate in tree behavior, while Work Item references are a convenience mirror and the whole Await-Condition set is cleared on the first accepted resume.

FR6: The aggregate enforces the normative lifecycle across `Created`, `Assigned`, `Queued`, `InProgress`, `Suspended`, `Completed`, `Cancelled`, `Rejected`, and `Expired`. Claim is the only entry to `InProgress`; Reject is legal only from `Assigned` and defaults to requeue; Handoff preserves active Status; invalid acts produce stable domain rejections; and only explicitly defined terminal, consumed-resume, and attachment duplicates are semantic no-ops.

FR7: Every accepted state change and progress fact is an append-only, past-tense Raw-Act Domain Event with authenticated actor, timestamp, causation, and verbatim payload supplied through trusted EventStore envelope provenance. The additive catalog includes correction, reopen, Handoff, Conversation, Registry, and spawn-rejection evidence; optional act notes are limited to 1,000 characters; success and rejection payloads remain distinct; and no caller-supplied actor is authoritative.

FR8: The bound Executor can report a strictly positive progress delta in the established Unit while `InProgress`; reaching zero emits `ProgressReported` followed by `WorkItemCompleted`. Explicit Complete may finish `InProgress` or `Suspended` work at any Remaining without changing Estimated or Done. `CorrectProgress` appends an absolute cumulative Done value at or above zero, permits visible overrun, and may reopen only a progress-completed item when positive Remaining is restored; explicit completion remains terminal.

FR9: An authenticated tenant member can re-estimate or reschedule any non-terminal Work Item. ReEstimate replaces Estimated, preserves cumulative Done, Unit, Status, and history, derives non-negative Remaining, permits `Done > Estimated` as visible overrun, and never completes or reopens the item; only `CorrectProgress` changes Done. Reschedule records Priority/Due Date changes and updates query ordering.

FR10: Cancel and Expire terminate any non-terminal Work Item, while the bound Executor may Reject an `Assigned` item with requeue or terminal semantics. Expiry accepts only the currently effective Due Date/TTL schedule witness and treats stale callbacks as audited no-ops. Cancel/Expire eventually cascade through all reachable active `Attached` descendants, include concurrent pre-boundary attachments, preserve already-terminal descendants, and remain durably discoverable until quiescent.

FR11: The system maintains an eventually consistent, rebuildable, idempotent Roll-Up that exposes own Remaining separately from recursive effective contribution by Unit, active-unestimated descendant count, freshness, and explicit stale/unavailable state. It stores absolute last-known descendant contributions by source stream position, handles out-of-order and duplicate delivery, uses only `Attached` topology, emits key-only change notifications, and never presents partial repair/rebuild data as fresh.

FR12: Roll-Up never silently combines incompatible Units. A child without an explicit Unit inherits its parent's Unit for its first estimate; a child with another Unit must declare it explicitly; a unitless parent and estimated child require an explicit child Unit; and mixed-Unit trees expose separate per-Unit subtotals without conversion.

FR13: The tenant-scoped, event-sourced Work-Tree Registry is the sole authority for a single-parent, acyclic, single-tenant tree. It serializes reservations against current Registry state, deterministically rejects cycles, second parents, cross-tenant edges, exhausted operational quota, or excessive depth, uses a configurable per-tenant depth limit of 32 by default, and imposes no domain breadth cap beneath the Platform quota.

FR14: An `InProgress` Work Item can suspend on one or more typed Await-Conditions, recording each condition kind and correlation key. A Suspended item accepts no progress, retains all active conditions until the first accepted match, and continues contributing its current effective Remaining to Roll-Up.

FR15: The Reactor resumes a Suspended Work Item only when a command exactly matches an active Await-Condition by kind and key. The accepted resume records the consumed condition, clears the full set, and returns to `InProgress`; replay of that consumed condition is the sole resume no-op, while every other non-match or post-resume condition is a domain rejection.

FR16: Child creation begins with the Registry's public reserve act carrying the complete child payload. The durable lifecycle is `Reserved → Creating → Attached` or `Reserved → Released`; only `Reserved` may release. Reactor-authorized child creation and matching durable `WorkItemCreated` evidence precede attachment, which then drives idempotent parent bookkeeping. Late released/superseded legs reject stably, `Creating` remains recoverable/quarantinable, and terminal-parent attachment schedules cascade catch-up.

FR17: A Work Item uses one `ExecutorBinding(PartyId, Channel, AuthorityLevel)` for every system, internal, or external doer without executor-kind branches. Assign/reassign handles pre-active responsibility; an authenticated current Executor uses Handoff during `InProgress` or `Suspended` to change the binding without changing Status, Burn-Down, Schedule, or Await-Conditions; Channel changes use the same operations.

FR18: Push and Pull coexist: a Work Item can be assigned to a specific Executor or queued for tenant members to claim, and can move between those modes. Claiming an `Assigned` item requires the bound Executor; claiming a `Queued` item binds the authenticated claimant as themself; both enter `InProgress`; and concurrent claims produce exactly one success with ordinary rejection for losers.

FR19: Executor Binding carries an additive-tolerant AuthorityLevel (`Read`, `Contribute`, `Coordinate`, `Administer`) without value-based authorization in v1. The system nevertheless enforces authenticated tenant membership, current actor-to-binding responsibility for Executor acts, self-claim integrity, designated workload origin for Reactor/reminder acts, and trusted internal origin for privileged views.

FR20: One read-side "what's next" query supports an Executor view and a coordination view. The Executor PartyId must match the authenticated Party and returns that Party's `Assigned` items plus the tenant `Queued` pool; the all-work view is trusted-internal-only. Results are tenant-filtered and ordered by present Priority, present earliest Due Date, then ordinal WorkItemId, with no routing score or creation coordinate.

FR21: Works references Parties, Conversations, Tenants, EventStore, and Commons by correlation/value objects and never copies their owned data. A Conversation ID can be supplied at creation or linked once later while non-terminal; same-ID retries are no-ops, conflicting replacement rejects, exact retries remain harmless after terminal closure, and Works never stores Conversation content.

FR22: The domain exposes `IExpectationResolver` and `IExecutorRouter` as low-dependency ports. v1 includes a structured/no-LLM expectation resolver, leaves routing unwired, and keeps all LLM, cost, routing, hosting, and infrastructure types outside the pure domain assemblies.

FR23: v1 maintains a tracked owns-versus-references boundary decision record enumerating every sibling-module responsibility, referenced identifier, and rationale, and architecture and dependency fitness tests use it as an implementation boundary.

FR24: Works exposes the minimal canonical EventStore domain-service executable, while Hexalith.Platform ultimately owns Aspire topology, ServiceDefaults, Dapr wiring, health, telemetry, projections, subscriptions, and operations. The transitional Works host is removed only after every applicable Platform parity row is proved; the target Works repository contains no AppHost, Aspire, or ServiceDefaults project.

FR25: The command/event pipeline is exercisable without production channel adapters. Tier-1 aggregate, lifecycle, Reactor translation, and projection tests remain pure; integration tests use EventStore/Tenant test support or the Platform Aspire topology only for real boundaries and distinguish accepted, domain-rejected, authorization-denied, idempotent, and infrastructure-unknown outcomes.

FR26: The Works Reactor is the sole cross-aggregate process manager for Registry/child creation, attachment bookkeeping, child-completion resume, cascade termination, and date/expiry effects. It mechanically translates committed events to deterministic commands, operates through substrate checkpoints and retries under its workload identity and tenant delegation, tolerates eventual windows, and keeps unresolved work discoverable with degraded readiness until retry or audited disposition.

### NonFunctional Requirements

NFR1: Tenant isolation is mandatory at every layer. Canonical TenantIds are validated—not normalized—as lowercase ASCII kebab-case values of 1–64 characters; aggregate identities, keys, projections, indexes, checkpoints, queries, logs, references, and control namespaces remain disjoint; reads require independent authorization/result filtering; and cross-tenant negative tests prove non-disclosure.

NFR2: Identity and origin fail closed. Platform ingress derives Tenant and acting Party from verified identity and membership, internal effects use signed workload delegation with explicit purpose and causation, responsibility checks occur inside the serialized aggregate turn, and missing/mismatched identity, forged origin, or unauthorized view is denied before Handle, persistence, query results, or tenant-existence disclosure.

NFR3: Event-sourcing integrity requires pure `Handle`, in-memory-only `Apply`, persist-before-publish, EventStore-owned envelope metadata, additive Raw-Act payloads, explicit domain rejection events, and infrastructure failures routed as exceptions/dead-letter outcomes. Accepted acts cannot be silently lost or reclassified.

NFR4: Same-aggregate commands are serialized with bounded optimistic-concurrency retry. Conflicting claims yield one success and domain rejections for losers; retry exhaustion is an explicit infrastructure conflict with no partial append, publication, or lost update.

NFR5: Idempotency is explicit at semantic, projection, and transport layers. Only named equivalent target states are semantic no-ops; projections apply source positions idempotently; and a reused transport/effect key returns its original success, rejection, or no-op across the supported retry/replay/restore horizon, while key/digest conflicts quarantine.

NFR6: Recovery, replay, and rebuild are lossless and observable. Page/checkpoint readers cannot skip accepted events; live writes during rebuild are journaled and preserved; readers never see a partial generation; pending reminders and process effects are rediscovered after restart/restore; unresolved work remains durable, periodically retried, alerted, and readiness-degrading until audited disposition.

NFR7: Durable contracts evolve additively and serialization-tolerantly with no replacement `V2` event types. A published N↔N+1 compatibility matrix and golden corpus must cover additive fields, enum/type evolution, defaults, rollout order, downgrade stance, malformed/unknown evidence, quarantine, historical bytes, and reader/validator/catalog-first producer rollout.

NFR8: Domain assemblies remain infrastructure-, hosting-, LLM-, cost-, and routing-free; dependencies point inward to Contracts; Dapr is the only domain-service infrastructure abstraction; generic runtime capabilities land in EventStore first; and architecture fitness tests enforce every allowed project edge.

NFR9: Observability uses bounded source-generated structured logs and RFC 9457 ProblemDetails with safe correlation context. Event payloads, Obligations, notes, Conversation content, personal data, secrets, tokens, full commands, trust internals, and unrelated tenant identifiers never enter logs, metrics, traces, or error bodies.

NFR10: Before non-synthetic shared data is admitted, accountable owners must approve durable-field classification/minimization, retention/legal hold, tenant offboarding/erasure, privileged audit, encryption/secret rotation, backup/restore order, disaster-recovery objectives, and a recovery drill. Architecture binds RPO ≤15 minutes, RTO ≤4 hours, and quarterly restore drills unless superseded by approved product/architecture budgets.

NFR11: Roll-Up, Registry, and "what's next" paths update incrementally and remain responsive for realistic depth/fan-out without whole-stream reads. The approximately 1,600-item tree, sub-200-ms rolled read, and five-second convergence figures remain non-gating benchmarks until Product and Architecture define environment, dataset, percentile, measurement window, retry horizon, coordination deadlines, failure policy, and binding recovery/performance budgets.

NFR12: Production routes require a machine-readable fail-closed security profile with mTLS, declared trust domain/namespace, deny-by-default app/network policy, TLS broker connections, producer/consumer ACLs, workload delegation, Scheduler/state/secret/audit configuration, and both negative-origin and positive reminder-path evidence; missing controls block startup or production admission.

NFR13: Delivery uses at-least-once, potentially out-of-order semantics. Every subscriber acknowledges only after its durable state, checkpoint, effect, or quarantine capture commits, and all aggregate, projection, Reactor, reminder, recovery, and repair behavior must remain correct under duplicate and reordered delivery.

### Additional Requirements

- **Starter/brownfield constraint:** The repository already contains the structural seed and historical Story 1.1, "Set Up Initial Project from Starter Template," is delivered. Preserve that Story 1.1 identity and evidence; do not create a new greenfield starter story or assign later target work to the historical ID.
- Preserve the durable historical Epic 1–4 story map exactly: Stories 1.1–1.5, 2.1–2.5, 3.1–3.6, and 4.1–4.9 retain their existing identities, titles, artifacts, and statuses; later requirements do not retroactively change their acceptance record.
- Retain the previous rewritten Epic 1–4 bodies as non-executable forward candidates labeled `F1-A`–`F1-D`, `F2-A`–`F2-H`, `F3-A`–`F3-J`, and `F4-A`–`F4-J`. An `F*` label cannot enter sprint status or inherit a historical status; promotion requires a unique final ID, dependency review, aligned authorities, and a validated story artifact.
- Add Epic 5, "Stabilize the Work Item Contract and Planning Record," as the only new executable remediation epic, with Stories 5.1–5.5 exactly covering overflow safety, singular lifecycle authority, overrun-preserving re-estimation plus note bounds, assembly-derived durable catalog completeness, and 4,000-character Obligation admission with historical replay compatibility.
- Preserve the approved Epic 5 dependency order: 5.1 and 5.4 may proceed independently; 5.2 precedes final 5.3 integration; 5.3 depends on 5.4 for changed durable evidence; and 5.5 depends on 5.4 before any new rejection producer is enabled. The proposal's named retrospective/action gates remain completion prerequisites.
- Keep `sprint-status.yaml` unchanged during epic/story reconciliation. Add Epic 5 and its five backlog keys only after all five story artifacts exist and validate; retain every historical key/status, mark Epics 1–3 done, keep Epic 4 in progress, and never add an `F*` key.
- Correct architecture AD-17 and all forward candidates to the approved rule: ReEstimate preserves cumulative Done, clamps only Remaining, accepts visible overrun, and never completes/reopens/changes Status; CorrectProgress alone changes Done and accepts any value ≥0.
- Treat the ReEstimate replay change as a semantic migration: replay representative old streams through aggregate and projection folds, compare live/rebuilt state, rebuild disposable projections, document snapshot/cache invalidation, and change aggregate/read-side interpretation atomically.
- Make progress arithmetic saturating and overflow-safe; preflight event-ordinal headroom for one- and two-event results so no partial result can be emitted; cover `decimal.MaxValue`, `long.MaxValue`, and `long.MaxValue - 1` boundaries.
- Establish one executable lifecycle authority for legality, outcome, target Status, and stable AttemptedAct. Handlers consume or assert its target, Markdown is generated or mechanically cell-compared, and all ReEstimate statuses and rejection labels are covered.
- Derive the durable contract catalog by reflection over decorated concrete types; require exactly one discriminator, sample, and required golden representation per type; validate bidirectionally and prove the check fails for a missing entry without altering historical bytes.
- Enforce the target project layout: Contracts, Server, Projections, Reactor, Testing, and a minimal executable. Required dependency direction is `Server → Contracts`, `Projections → Contracts`, `Reactor → Contracts`, with executable-to-inward-unit/EventStore SDK edges and machine-checkable fitness rules.
- Use only the `.slnx` solution, .NET 10/C# 14, nullable and warnings-as-errors, centrally managed packages, System.Text.Json conventions, Hexalith.PolymorphicSerializations, Dapr as infrastructure abstraction, and xUnit v3 + Shouldly + NSubstitute for tests.
- Assign Work Item IDs at the authenticated command edge using sortable ULIDs for new identities while remaining backward-readable for existing AggregateIdentity-compatible IDs. Handlers, projections, and Reactor translations never generate aggregate IDs; Registry's literal `registry` ID is the sole reserved exception.
- Implement one versioned EventStore-owned canonical codec for TenantId text, field ordering, command digests, reminder/effect tuples, and Crockford Base32 SHA-256 output. Effect ordinals are catalog values, not traversal order; golden vectors cover every effect family before producer registration.
- Store cross-aggregate effect receipts atomically with target persistence in a private EventStore inbox. Receipts bind the full effect tuple, semantic digest, outcome, workload, delegation purpose, and causation; conflicts quarantine; retention follows source/target evidence and tenant legal-hold/offboarding policy.
- Implement Registry reservation fencing with `ParentAdmissionWitness`, `ReservationId`, monotonic `FencingToken`, reserved-payload digest, origin validation immediately before dispatch, and authoritative `WorkItemCreated` attachment evidence. `Creating` cannot auto-release; repair preserves evidence and audits orphan disposition.
- Provide `IWorkTreeTopologyReader` over Registry read models with exact-token ancestry resolution, stable cursor-paged descendant enumeration, and attachment lookup. Roll-Up, cascade, child-completion recovery, rebuild, and repair use this single topology source.
- Require a Platform-provided per-tenant topology quota before `EdgeReserved`; expose saturation/backpressure, snapshot/bounded Registry replay, and latency proof at the configured quota while preserving no domain breadth cap.
- Persist Roll-Up using generation/epoch-qualified physical keys selected only by the tenant manifest, ETag CAS merge, separate topology watermarks and contribution slots, equal-position digest conflict quarantine, tombstones, and distinct own-versus-rolled DTOs.
- Make EventStore the sole rebuild epoch/journal writer. BeginBuilding requires compatible writer leases; every commit validates epoch/lease; Building journals live writes and checkpoints atomically; promotion switches the manifest atomically; catch-up controls stale state; Abort drains safely; readers never see partial generations.
- Implement typed durable reminder intents and deterministic reminder/schedule identifiers. Works owns intent translation, EventStore owns generic registration/reconciliation, Platform owns Scheduler durability/HA/backup/callback policy, and stale schedule witnesses cannot expire or resume work.
- Use the Platform Workload Delegation Service as the sole asymmetric JWT issuer for internal effects; bind audience, effect tuple, digest, target, command type, tenant, purpose, causation, time, and expiry, and compare claims with mTLS/ACL-attested Dapr app identity before origin policy.
- Apply one strict validator and quarantine path to live delivery, replay, repair, rebuild, and recovery. EventStore paging uses its exclusive lower-bound correctly; pages validate domain, canonical tenant, aggregate, increasing positions, and complete state-affecting decoding.
- Preserve the transitional Works AppHost/ServiceDefaults and bespoke runtime only until every relevant Platform migration row R1–R11 has a named producer artifact, minimum package/API contract, consumer story, proof command, green parity evidence, and rollback proof; then remove Works-owned topology.
- Close PRD exit gates G1–G8 and architecture readiness gates before their named phase: transport replay, reminder durability, reader-safe recovery, governed control-plane exception, contract compatibility, data lifecycle/DR, verified identity/origin, binding performance/recovery budgets, runtime compatibility, lifecycle migration, and CI architecture execution.
- Add a tracked CI lane that builds Release and explicitly executes the Release architecture-test assembly; solution compilation alone is insufficient. Test projects run individually and integration boundaries assert persisted end-state.
- Refresh UX provenance only after PRD and architecture alignment; do not redesign its already-correct overrun, non-terminal-zero, correction/reopen, freshness, and accessible Burn-Down behavior, and do not add a v1 UI.

### UX Design Requirements

UX-DR1: Keep v1 headless. The platform-hosted harness presents structured builder evidence for accepted, domain-rejected, authorization-denied, idempotent, and infrastructure-unknown outcomes with current domain evidence and a next safe action; no production web, MCP, CLI, chatbot, or email surface is implied.

UX-DR2: Any future Works web UI must compose through Hexalith.FrontComposer and Blazor Fluent UI V5, inherit the active theme, typography, spacing, radii, elevation, focus, hover, selection, light/dark, reduced-motion, and forced-colors behavior, and define no Works-specific accent, palette, type ramp, or legacy Fluent/FAST tokens.

UX-DR3: A future web Module has one Works navigation entry, preserves FrontComposer Home, uses route-backed "What's next" and "Work" tabs, keeps Work Item detail subordinate to Work, and launches Capture through the registered generated command route. Admin/Audit and mockup navigation are not current capabilities.

UX-DR4: Every page, dialog, or detail panel with at least two sibling titled regions uses one `FluentAccordion` with one item per region and the primary item expanded. Titles, breadcrumbs, toolbar, navigation, and a sole primary grid/form/detail/visualization stay outside and are never hidden.

UX-DR5: Implement `burn-down-meter` from `FluentProgressBar` and `FluentText`; always label Estimated, cumulative Done, Remaining, and Unit; render progressbar semantics only for truthful determinate states; use tabular numerals only to prevent jitter; and let Work Status—not the meter—own completion/reopen announcements.

UX-DR6: Implement `roll-up-summary` from Fluent layout/text and FrontComposer projection-health components; keep own Remaining, rolled per-Unit totals, unestimated-descendant count, freshness, and Unavailable separate; place it first/expanded in the Work supporting accordion; never show unavailable as zero.

UX-DR7: Implement `work-status` with `FcStatusIcon`, existing `BadgeSlot`, visible localized label, accessible name, and keyboard-accessible tooltip for all nine statuses. Do not invent glyphs, palettes, or tinted pills, and never conflate domain Work Status with FrontComposer command lifecycle.

UX-DR8: Implement `party-reference` with `FluentAvatar`, `FluentText`, `FluentStack`, and `FcFluentIcons`; show authorized resolved Party name plus Channel or a stable neutral reference fallback; never infer, label, or color-code human/system/external kind.

UX-DR9: Implement `work-tree` with native `FluentTreeView`/`FluentTreeItem` semantics and keyboard behavior. Render only `Attached` edges in the authoritative tree; show Reserved/Creating/released/superseded coordination separately; preserve hierarchy, selection, disclosure, and focus during eventual attachment/resume/cascade updates.

UX-DR10: Implement `queue-row` on the generated Fluent data grid and FrontComposer row-detail components, showing obligation, exact Schedule order, authorized executor, Work Status, Burn-Down, and server-authoritative legal actions. Preserve filters, scroll, selection, and deterministic focus fallback after claim loss or row removal.

UX-DR11: Implement `capture-command` using generated command form, authorization region, placeholders, and lifecycle wrapper. Obligation is primary with its visible 4,000-character limit; every act-note field exposes the 1,000-character limit; trusted tenant/actor and server-derived fields are read-only; all values survive validation, denial, rejection, or unknown infrastructure outcome.

UX-DR12: Implement `work-history` as an authorized semantic chronological list using Fluent primitives. Show trusted actor/origin, locale-formatted time, act, and optional note; preserve original and corrected progress, Handoff parties, and delayed-child rejection evidence; never expose raw payloads, hidden envelope fields, secrets, or infrastructure internals.

UX-DR13: Implement `conversation-panel` as the separately owned Hexalith.Conversations view in its own labeled accordion item with independent loading, posting, authorization, error, and recovery states. A correlation link never grants content access or merges Conversation content into Works history.

UX-DR14: Implement Theme 3 `natural-language-response` only at that horizon using `FluentTextArea`, `FluentButton`, and lifecycle feedback. Constrained actions remain primary; preserve original text; label interpretation as derived; auto-apply only safe high-confidence constrained intent; request confirmation at low confidence; retain text through failure.

UX-DR15: Implement Theme 3/6 `email-action-set` only after the email evidence gate. Use email-safe semantic links and tables, descriptive constrained actions before reply-in-own-words, complete plain-text parity, and explicit accepted/recovery outcomes; Theme 6 adds bound single-use credentials, absolute expiry, prior-use evidence, risk-based step-up, and fresh-link/no-login recovery.

UX-DR16: Implement `pause-live-updates` with a labeled `FluentButton`, associated `FluentBadge` count, and shared connection-status treatment. Announce pause once, update queued count silently, apply one coherent batch on resume without focus theft, and announce one deduplicated summary.

UX-DR17: Implement Theme 5 `cost-meter` only at that horizon as a separately labeled Cost meter using Fluent progress/text. Group by locale-formatted currency/Unit, retain freshness/Unavailable, never silently convert totals, and distinguish Cost from Effort by wording and placement rather than color.

UX-DR18: Use calm, factual, short, non-gamified copy. State what is known, projection freshness, exact Work Status, and next legal action; never imply synchronous completion from acknowledgement, translate concurrency into user language, editorialize Raw Acts, or replace unavailable/stale results with zero.

UX-DR19: Derive action availability and disabled reasons from server-authoritative capability metadata. Submission-time authorization remains authoritative; distinguish domain rejection, authorization denial, idempotent no-op, and infrastructure uncertainty; retain safe input, refresh current evidence, and never expose trusted-origin system actions as user controls.

UX-DR20: Cover Works-specific states explicitly: Created-with-binding, unestimated, explicit residual completion, progress completion/corrected reopen, visible overrun, non-terminal zero, immutable-Unit rejection, multiple awaits, active Handoff, Reserved/Creating/Attached/released/superseded child coordination, eventual resume/cascade, claim loss, Conversation boundary states, and projection health.

UX-DR21: Display Roll-Up strictly from projection evidence: own Remaining, recursive per-Unit rolled Remaining, active-unestimated count, and freshness (`Fresh`, `Stale`, `Reconnecting`, `Unavailable`). Preserve last trustworthy values with time where safe, omit untrustworthy numbers, and never optimistically recompute cross-aggregate state in the client.

UX-DR22: Present every field limit and validation relation before entry, focus a linked error summary, associate messages with exact fields, retain unrelated and submitted values, avoid per-keystroke announcements, and preserve the full rejected value so a user can shorten it.

UX-DR23: Make every operation keyboard-operable with Fluent focus and DOM order, no positive tabindex, correct return-to-initiator behavior, and stable focus recovery by control, keyed row, old index, preceding row, grid, then empty-state heading. Background projection/Reactor changes never steal focus.

UX-DR24: Coalesce live announcements into one meaningful status/count/result, use polite status for normal changes and assertive alert only for blocking errors, preserve reading/scroll/filter/selection/expansion context, and keep pause controls reachable while updates are active.

UX-DR25: At 320 CSS px and 400% zoom, primary content/actions reflow in one dimension without clipping; only clearly labeled relational grid/tree regions may require two-dimensional scrolling. Support WCAG text-spacing overrides, reduced motion, forced colors, non-color meaning, platform targets ≥24×24 CSS px, and email actions ≥44×44 CSS px.

UX-DR26: Meet WCAG 2.2 AA for future human-facing surfaces with programmatic labels, visible status text, appropriate native component semantics, meaningful reading order, accessible error/recovery paths, and the specified Burn-Down edge-state ARIA matrix; the progress bar itself is never a live region.

UX-DR27: Fail closed before rendering unauthorized tenant content. Render authorized Raw Acts as safe text, exclude technical/secret/cross-tenant details, treat Conversation authorization separately, and ensure access loss or actor mismatch has a stable, non-leaking recovery state.

UX-DR28: Resource-back all product copy, labels, errors, accessible names, actions, email subjects, and templates; locale-format dates, times, durations, numbers, Units, and future currency; preserve absolute email expiry; apply `lang`, `dir`, and bidi isolation to mixed-language content and identifiers. PM must resolve English-only versus localized/RTL external email before Theme 3 acceptance.

UX-DR29: Before Theme 3 email implementation, approve supported clients and high-contrast modes and record evidence for perceivable text/links/action boundaries, visible focus where exposed, client-safe semantic markup, plain-text parity, system fonts, unique descriptive links, 44×44 targets, narrow-width behavior, and 200% text sizing.

UX-DR30: Future MCP/CLI/chatbot output preserves verified tenant/actor context, exact command lifecycle/outcome, Work Status, effort/Unit, freshness, and next legal action. Theme 2 input remains exact and command-shaped; natural-language interpretation begins only in Theme 3.

### FR Coverage Map

{{requirements_coverage_map}}

## Epic List

{{epics_list}}

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
