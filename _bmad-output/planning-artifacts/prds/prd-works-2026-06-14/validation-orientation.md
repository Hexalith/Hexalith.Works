# Validation Orientation — Hexalith.Works PRD

- **Oriented artifact:** `prd.md` + `addendum.md`, both `status: final`, updated 2026-09-08
- **Run scope:** source extraction/orientation only; no PRD or addendum changes
- **Decision trail:** `.memlog.md` (bootstrap from the prior decision log, then append-only through the 2026-09-08 update)
- **Original inputs:** product brief + brief addendum (2026-06-14), brainstorming session (44 ideas / 6 themes), and the two June reconciliation extracts
- **Downstream context sampled:** `../../architecture.md` and `../../epics.md`

## Product and stakes

Hexalith.Works v1 is a **headless, event-sourced, multi-tenant work-item coordination kernel** for Hexalith builders. It owns coordination facts—Obligation, Effort Burn-Down, Schedule, lifecycle, one Executor Binding, Work Tree references, and Await-Conditions—while sibling modules own identity, conversation, persistence, tenancy, and IDs. Its defining product bets are:

1. **Everything is a Party:** system/AI, internal users, and external people share one `PartyId + Channel + AuthorityLevel` binding and one command model, with no executor-kind branching.
2. **AI is never the system of record:** the authenticated Raw Act is canonical; interpretations are rebuildable projections.
3. **Work is measurable and durable:** effort burns down, rolls up a Work Tree, and long-running work suspends/resumes across restart.

The PRD has launch-grade architectural stakes even though v1 is foundation software: it defines additive serialized contracts, tenant and actor provenance, cross-aggregate process behavior, projection correctness, and package ownership across Works, EventStore, Platform, and sibling modules. Its direct v1 consumer is the Hexalith builder; end-user channels remain a primary product horizon rather than a v1 surface.

Current v1 scope is **Theme 1 plus the kernel subset of Theme 2**: `WorkItem`, Work-Tree Registry, Reactor, lifecycle/events, roll-up, executor binding, ports/references, deterministic “what’s next,” the minimal EventStore domain-service host, and a platform-owned Aspire test topology. MCP/CLI production command surfaces are the Theme-2 remainder; email/chatbot/LLM interaction, routing, cost governance, and Theme-6 hardening are deferred. Platform-owned authenticated identity/tenant derivation and trusted origin are v1 baseline, not deferred hardening.

## Source commitments and precedence

### Product brief and brief addendum

The June brief committed to a two-sided problem: users lose work because capture requires leaving the channel where it surfaced, while builders stitch together task, durable-execution, and approval systems. It made builders and end users co-primary product audiences and emphasized omnichannel capture, an external-by-email peer executor, dual effort/cost burn-down, Raw-Act auditability, and “coherence, not primitive novelty” as the advantage. Its five named breakthroughs were Everything is a Party; answer-contract triple duty; parallel effort/cost meters; start-cheap-escalate and budget-degrade as one ladder; and AI outside the system of record.

The brief also selected Themes 1 and 2 for v1, but its “omnichannel from day one” language was in tension with its foundation-only scope. The current PRD records the later, user-approved interpretation explicitly: **channel seam on day one, production adapters later**. It likewise narrows end users from direct v1 consumers to a primary product audience served later. Validation should treat these as deliberate scoped amendments, not accidental omissions.

### Brainstorming session

The 44-idea source established the irreducible work model: quantitative progress; Priority + Due Date; narrative history; uniform executors; pluggable Units; restart-safe continuation; spawn/suspend; recursive roll-up; and generic event awaiting. Theme 2 included both thin-core boundaries and Channels #1–3, including MCP and CLI. It also originated the deferred interaction/routing/economics/security mechanisms and the one-ladder-both-ways insight.

The current PRD intentionally refines or overrides several brainstorming details:

- `Resumed` is a transition back to `InProgress`, not a resting state.
- “What’s next” in v1 is a deterministic projection/query, not routing arbitration.
- Trees are bounded at default depth 32 despite the source’s implied/no-limit position.
- MCP and CLI retain Theme-2 lineage but move to a clearly named deferred Theme-2 remainder.
- Completion is no longer simply “Remaining = 0”: progress-to-zero and explicit completion are two acts producing the same completion event.

### June reconciliation extracts

`reconcile-brief.md` and `reconcile-brainstorm.md` describe the **June** PRD and are historical extraction evidence, not findings against the September artifact. The September update addressed most of their material gaps: the two-sided problem and dual audience are explicit; “omnichannel day one” is reconciled; triple-duty and one-ladder framing were restored; the no-moat stance returned; Raw-Act notes were added; restart durability became an acceptance signal; multiple Await-Conditions were normalized; MCP/CLI became a Theme-2 remainder; and the cost-aware-scheduling boundary tension is named.

Residual source tensions worth keeping visible are the deliberate end-user/direct-consumer narrowing, the default depth cap, the v1 sort-vs-routing boundary, and the fact that one Conversation across channel changes is implied by FR-21 rather than stated as an explicit cross-channel convergence invariant.

### Current decision precedence

Where June sources and the current contract differ, the user-approved 2026-09-05/06/08 PRD amendments and `.memlog.md` are the product-decision trail. Architecture decisions bind mechanism, but the PRD’s 2026-09-08 product semantics deliberately supersede stale architecture prose (most visibly AD-13’s resume wording); the downstream handoff table records the required repairs.

## Material amendments and decisions

### 2026-09-05

- Added post-creation Conversation linking: `LinkConversation` / `ConversationLinked`, first-link authoritative, same-ID retry no-op, conflicting replacement rejected.
- Moved Aspire topology and ServiceDefaults ownership to `Hexalith.Platform`; Works keeps only the canonical minimal EventStore domain-service executable.

### 2026-09-06

- Replaced FR-20 “creation order” with deterministic `WorkItemId` ordinal as the final tiebreak.

### 2026-09-08 drift-correction update

- Added the tenant-scoped **Work-Tree Registry** as tree-edge authority: reserve → attach/release; direct `SpawnChild` became Reactor-only.
- Added **FR-26 Reactor** as the sole owner of mechanical cross-aggregate event→command coordination, with eventual effects, checkpoint recovery, workload identity, and tenant delegation.
- Made FR-6’s full lifecycle table normative: `Claim` is the only entry to `InProgress`; Reject is legal only from `Assigned`; active work cannot be reassigned directly.
- Bound resume semantics: a current non-match is a domain rejection; only repetition of the consumed condition is a no-op.
- Bound completion semantics: progress-to-zero emits `ProgressReported` then `WorkItemCompleted`; explicit Complete is legal from `InProgress` or `Suspended` at any Remaining; `ReEstimated` never completes.
- Bound roll-up to flattened, state-based per-descendant LWW contributions at quiescence; added unavailable repair/rebuild state, unestimated-descendant count, and child Unit inheritance.
- Bound actor provenance to authenticated envelope identity, never a caller field or the Executor Binding; internal acts use workload identity + tenant delegation + causation.
- Defined FR-20’s executor and coordinator views, exact filters, priority direction, null ordering, and tiebreak.
- Made the v1 authorization blast radius explicit: every authenticated tenant member may perform every v1 act; `AuthorityLevel` is carried but not read.
- Added field bounds (Obligation 4,000; event note 1,000), notifier behavior, performance fixtures, stronger SM-1/SM-3/SM-5, and new SM-6.

The update preserved FR-1…FR-25 IDs and added FR-26. The memlog explicitly records that **reviewer re-validation and structure/prose polish were not rerun** after the update. Existing `review-*.md` and `validation-report.*` therefore explain why the update happened; they do not validate the current September PRD.

## Assumptions, callouts, and open bindings

Not every `[ASSUMPTION]` is unresolved: the June set was explicitly accepted by the user. Confirmed but still challengeable contract choices include explicit-only completion for unestimated work, on-demand Expectation resolution, no Unit conversion, multi-condition first-match resume, Reject-defaults-to-requeue, Due-Date/TTL expiry, cascade rather than orphaning, eventual roll-up, tenant-closed trees, max depth 32, unrestricted tenant claim eligibility, the four-value AuthorityLevel set, and Conversation-by-reference only.

The September additions with the most validation leverage are:

- Obligation ≤ 4,000 and Raw-Act note ≤ 1,000.
- Unestimated items contribute 0 plus an unestimated-descendants count.
- The post-parent-termination cascade window permits descendant progress/completion until the eventual command lands.
- Indicative fixture: depth 32 × fan-out 50 (~1,600 items), rolled read < 200 ms, convergence within 5 seconds. These budgets are explicitly provisional.
- Transport idempotency keys are v1; signed per-act tokens remain Theme 6.

Three owned PM callouts remain:

1. Revisit live reassignment at Theme-4 planning; v1 cannot hand off `InProgress`/`Suspended` work directly.
2. Revisit `Reopen` if Theme-3 surfaces reveal premature completion.
3. Add AuthorityLevel enforcement before the first multi-team production tenant or at Theme-4 planning, whichever comes first.

Open architecture/platform bindings that affect safe downstream execution:

- **VAL-H06 / R7:** cascade and child-completion checkpoint concurrency; confirm FR-26’s consistency window.
- **VAL-H07 / R6:** Scheduler HA/backup and reminder callback failure policy.
- **VAL-H08 / R4:** capture-through-Commit projection rebuild fence.
- **VAL-H09:** governed cross-tenant/control-plane exception for global recovery registries; PRD tenant NFR must be updated when closed.
- **VAL-H10 / R11:** transport idempotency semantics; must bind before the registry story is drafted.
- **VAL-H11:** reader/writer and unknown-type schema compatibility matrix before catalog change ships.
- **VAL-H12:** immutable-event privacy lifecycle before production data is admitted.

## Downstream consumers and drift risks

### Architecture (`../../architecture.md`)

The architecture register contains the essential mechanism decisions (AD-01…AD-25), including Registry, roll-up fan-out, identity provenance, trusted origin, and expiry reminders, but the document is dated 2026-09-06 and predates the product update. Known live drift includes:

- Requirements Overview still says 25 FRs, one aggregate owning parent/children, and recursive per-child roll-up; current PRD says 26 FRs, Registry-owned edges, and flattened per-descendant LWW.
- AD-13 and later implementation prose say “no current match = no-op”; current FR-15 says non-match rejection and consumed-match-only no-op.
- AD-25 still says Due-Date/TTL policy is per-work-type/tenant; current PRD removed v1 work type and says per-tenant.
- The open register lacks the FR-26 eventual-window confirmation against R7; Unit inheritance at spawn is not explicitly carried into AD-21.

Because the register declares itself authoritative over its own prose, reviewers should separate **register-level mechanism gaps** from stale narrative summaries—and still flag conflicts where an AD rule itself contradicts current product semantics.

### Epics and stories (`../../epics.md`)

The epic artifact is dated 2026-09-05 with partial 2026-09-06 corrections and is the highest drift-risk consumer. Current gaps include:

- Inventory/coverage stops at FR-25; FR-26 is absent.
- FR-13/FR-16 and Stories 3.1/3.2 still make `SpawnChild` caller-facing and the Work Item/tree guard authoritative; no Registry reserve story exists.
- FR-11/Story 3.3 still use recursive direct-child roll-up rather than flattened per-descendant slots; Unit inheritance and unavailable/unestimated fields are missing.
- AR-6 still allows progress delta `≥ 0` and uses per-work-type/tenant policy, although Story 2.3 already requires a positive delta.
- Completion remains framed primarily as Remaining=0/unestimated-explicit, omitting explicit completion at any Remaining and the no-Reopen callout.
- Lifecycle stories do not clearly bind `Claim` as the single `InProgress` entry or Reject’s exact two outcomes as PRD-owned decisions.
- NFR-9 retains the pre-update no-op/idempotency model and says no v1 transport token.
- Story 4.4 lacks FR-20’s executor-vs-coordinator views and new additive read-model fields/bounds.
- Story 4.9 lacks PRD §9 / AD-23/24 acceptance: OIDC claim derivation, deny-before-dispatch, workload identity/delegation, mTLS/trusted-origin negatives, and positive reminder registration under production policy.
- Changed success signals (SM-1 claim/cascade, rewritten SM-3, Channel-focused SM-5, new SM-6) are not propagated.

### Internal traceability/mechanical risks

- The PRD/addendum’s brief link uses `../briefs/...` and their brainstorming link uses `../../brainstorming/...`; both are wrong from this workspace. Actual sources are `../../briefs/...` and `../../../brainstorming/...` respectively.
- The addendum’s non-binding event sketch says acting identity comes from “the binding + EventStore envelope,” while current FR-7 explicitly forbids inferring actor from Executor Binding. Treat the PRD rule as controlling.
- Existing validation/review files are pre-update evidence. A fresh reviewer gate must not reuse their verdicts as current judgments.

## Reviewer orientation

Validate the September contract as a high-rigor distributed-domain PRD, not the June draft. Pay particular attention to decision-readiness at the Registry/Reactor boundary, testability of eventual consistency and trusted-origin rules, explicit ownership across Works/EventStore/Platform, completeness of the assumptions index, and whether the PRD’s current product semantics can be consumed without falling back to stale architecture or epic prose.
