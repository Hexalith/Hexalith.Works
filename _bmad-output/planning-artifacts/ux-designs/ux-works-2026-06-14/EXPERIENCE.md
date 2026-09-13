---
name: Works
description: Behavioral contract for Work Item coordination across the headless kernel and explicitly partitioned future surfaces.
status: final
updated: 2026-09-12
sources:
  - "{planning_artifacts}/prds/prd-works-2026-06-14/prd.md"
  - "{planning_artifacts}/prds/prd-works-2026-06-14/.memlog.md"
  - "{planning_artifacts}/prds/prd-works-2026-06-14/addendum.md"
  - "{planning_artifacts}/briefs/brief-works-2026-06-14/brief.md"
  - "{planning_artifacts}/briefs/brief-works-2026-06-14/addendum.md"
  - "{planning_artifacts}/architecture.md"
  - "{project-root}/references/Hexalith.FrontComposer/_bmad-output/project-context.md"
  - "{project-root}/references/Hexalith.FrontComposer/_bmad-output/planning-artifacts/ux-designs/ux-frontcomposer-2026-09-09/DESIGN.md"
  - "{project-root}/references/Hexalith.FrontComposer/_bmad-output/planning-artifacts/ux-designs/ux-frontcomposer-2026-09-09/EXPERIENCE.md"
---

# Works Experience

This file owns how Works behaves. `DESIGN.md` owns its visual contract. Product facts and horizons come from the amended Works PRD; FrontComposer owns shared shell, projection, command, theme, and accessibility behavior.

**Contents:** [Foundation](#foundation) · [Information Architecture](#information-architecture) · [Voice and Tone](#voice-and-tone) · [Component Patterns](#component-patterns) · [Lifecycle and Action Matrix](#lifecycle-and-action-matrix) · [Roll-Up and Freshness](#roll-up-and-freshness) · [State Patterns](#state-patterns) · [Interaction Primitives](#interaction-primitives) · [Privacy and Localization](#privacy-and-localization) · [Accessibility Floor](#accessibility-floor) · [Responsive & Platform](#responsive--platform) · [Inspiration & Anti-patterns](#inspiration--anti-patterns) · [Key Flows](#key-flows) · [FR audit index](#functional-requirement-traceability)

## Foundation

Works is a tenant-scoped, event-sourced coordination kernel. A Work Item makes four things clear: what is owed, who is responsible, how effort burns down, and which durable domain evidence permits its next state change. Every doer is represented by one Executor Binding (`PartyId + Channel + AuthorityLevel`); the experience never branches on an inferred human, system, or external Party kind.

The v1 outcome is headless. Builders exercise the full command pipeline through the platform-hosted domain-service test harness; Works ships no production web, MCP, CLI, chatbot, or email adapter in v1. Every future surface described here is labeled by horizon and must preserve the same domain decisions.

### Delivery horizons

| Horizon | Committed experience scope |
|---|---|
| **v1: Theme 1 plus Theme 2 kernel slice** | Domain aggregates, contracts, Reactor, projections, ports, boundary record, and automated/manual command-pipeline exercise through the platform-hosted test harness. No end-user surface. |
| **Theme 2 remainder** | Exact MCP tools/resources and CLI commands over the kernel. Command-shaped input only; no natural-language interpretation. |
| **Theme 3** | Chatbot natural language, email-as-UI, magic-link actions, and response interpretation/recovery. |
| **Theme 4** | Routing, escalation, and AuthorityLevel enforcement. The auditable active Handoff already defined by the kernel remains distinct from future routing policy. |
| **Theme 5** | Cost as a separate meter and Roll-Up, grouped by currency/Unit without silent conversion and governed by effort-style freshness/Unavailable behavior. |
| **Theme 6** | Security hardening, non-repudiation, audit/participant controls, and production readiness. |
| **Uncommitted post-v1** | A FrontComposer Works web Module. The web composition below is a UX contract for a possible surface, not a shipping commitment or a substitute for horizon planning. |

### Source precedence

Resolve conflicts in this order:

1. The latest five accepted entries in the PRD workspace `.memlog.md` override conflicting rendered PRD/addendum text until regeneration.
2. The amended PRD/addendum govern all other product semantics and horizons.
3. Current FrontComposer context/spines govern shell and composition.
4. Current Works architecture supplies compatible implementation constraints.
5. The brief/addendum supply vision.

The cited PRD sources own volatile domain mechanics; this spine keeps only UX consequences and a usable action matrix. Do not fill an unresolved mechanism by inventing protocol fields.

## Information Architecture

### Current and planned entry points

| Entry point | Horizon | Information architecture |
|---|---|---|
| Platform-hosted test harness | v1 | Builder-facing command and event evidence only; no simulated production navigation. Group accepted, domain-rejected, authorization-denied, and infrastructure-unknown results with current domain evidence and a next safe action. |
| MCP and CLI | Theme 2 remainder | Command-shaped input identifies the registered act and target with verified tenant/actor context kept non-editable. Results preserve command lifecycle/outcome, exact Work Status, effort/Unit, freshness, and next legal action; exact tool/resource names and syntax remain source-open. |
| Chatbot and email | Theme 3 | Constrained actions first, natural-language fallback second; email is a single-purpose external surface with no shell. |
| FrontComposer Works Module | Uncommitted post-v1 | One Module navigation entry. FrontComposer Home remains at `/` and `/home`. `/{module}` aliases the Module's default route. Within Works, **What's next** and **Work** are route-backed tabs; Work Item detail is subordinate to Work. Capture opens the generated command route. |

The Works Module does not add separate primary entries for What's next, Work, Capture, a Work Item, or the Work Tree. A possible default route is selected by the future Module contract rather than hard-coded here. Admin and Audit are roadmap concepts only: they receive no current rail item, tab, route, or authorization promise until their Theme 4/6 product contracts are complete. The roadmap visual is a reference-state board, not a page or navigation proposal; it adds no routing, escalation, Admin, Audit, or participant capability.

### Future web surfaces and composition

Any future web Module composes through the current FrontComposer sources under `{project-root}/references/Hexalith.FrontComposer`; the obsolete path without `references/` is invalid. Customization precedence is **L4 full view → L2 projection template → generated default**. L3 field slots participate only when the selected renderer explicitly delegates to them.

Every page, dialog, or detail panel with two or more sibling titled regions uses one `FluentAccordion`, one item per region, with the primary/first item expanded. A page heading, breadcrumbs, toolbar, navigation, and one primary grid, form, detail, or visualization may remain outside; the sole primary region is never hidden.

| Surface | Experience and anatomy | FrontComposer composition |
|---|---|---|
| Works Module shell | Preserve Home and one Works Module entry with route-backed tabs. | `FrontComposerShell`, navigation, `FcPageHeader`, `FcPageLayout`, `FcPageTabs`, and `FcPageToolbar`. |
| What's next | Executor view = that Party's `Assigned` items plus every tenant `Queued` item; coordinator view = all tenant `Assigned` and `Queued` items. Sort by present Priority (`Critical`, `High`, `Normal`, `Low`), present earliest Due Date, then `WorkItemId` ordinal; missing Priority/Due Date sort last. v1 has no eligibility, ranking, or routing policy. | Generated Fluent data grid plus `queue-row`; use L2 only for the documented Works row/detail delta. |
| Work | Search/browse with Attached `work-tree` as the sole always-visible primary visualization. One supporting accordion contains `roll-up-summary` first/expanded, with Unavailable as its alternate state, then child-coordination evidence outside the authoritative tree. | L2 projection template; use L4 only if the registered Module/page contract cannot express the whole page. |
| Work Item detail | Page header and toolbar stay outside one accordion; the primary Work Item is first/expanded. Other titled regions follow; `work-history` and `conversation-panel` remain separate. | L2 projection template plus the separately composed Conversations view. |
| Capture and other commands | Capture uses `/commands/{BoundedContext}/{CommandTypeName}`; concrete values come from registered metadata. Verified tenant/actor and server-controlled or derived fields are not editable. | Generated form and lifecycle/authorization components. Use L3 only for an explicitly delegated field delta; use L4 only for an exceptional complete form. |
| Work history | Raw Act chronology remains a domain composition within Work Item detail. | L2 projection template using Fluent primitives and inherited FrontComposer states. |

The nine-artifact visual coverage inventory and authority boundary are in [`DESIGN.md`](DESIGN.md#visual-references); no IA/horizon group remains spine-only. Those references commit no route, capability, markup, token, or lifecycle behavior. Works inherits the active FrontComposer accent and defines no accent API mapping.

## Voice and Tone

Use factual, calm, short language for every Party. Say what is known and what action is legal; do not editorialize a Raw Act or imply synchronous certainty where the system is eventual.

| Prefer | Avoid |
|---|---|
| “Nothing waiting.” | “You crushed your tasks!” |
| “Remaining: 3 interactions.” | “75% done” when the unit is not percentage |
| “Someone else claimed this item.” | “Concurrency error 409.” |
| “Update received. Waiting for the latest projection.” | “Completed” while the command is only acknowledged |
| “Roll-Up unavailable. Last updated 14:32.” | Showing `0` during repair or rebuild |
| “This action is no longer available. Request a fresh link.” *(when Theme 6 hardening reports expiry or prior use)* | A dead end after an expired or used email action |

Labels, errors, instructions, and accessible names are resource-backed. Raw Acts preserve authorized verbatim wording and are never silently rewritten as a system interpretation.

## Component Patterns

These 13 IDs and names are the canonical Works-specific component vocabulary shared with `DESIGN.md`.

| Component ID | Implementation base | Behavior contract |
|---|---|---|
| **burn-down-meter** | `FluentProgressBar` + `FluentText` | Name Estimated, cumulative Done, Remaining, and immutable Unit. Use the accessibility edge-state matrix and lifecycle consequences below rather than deriving completion from the rendered fraction. |
| **roll-up-summary** | `FluentStack` + `FluentText` + FrontComposer projection-health components | Separate own Remaining from rolled Remaining by Unit, unestimated-descendant count, and freshness. On Work, it is the first expanded supporting accordion item; last committed values remain visible while safe, and Unavailable replaces the result rather than rendering zero. |
| **work-status** | `FcStatusIcon` with existing `BadgeSlot` + adjacent `FluentText` | Announce and display exactly Created, Assigned, Queued, In progress, Suspended, Completed, Cancelled, Rejected, or Expired using the slot mapping in `DESIGN.md`. The visible label disambiguates shared slots. It is a domain resting state, never FrontComposer command lifecycle. |
| **party-reference** | `FluentAvatar` + `FluentText` + `FluentStack` + `FcFluentIcons` | Resolve Party name on demand, expose Channel, and keep a stable PartyId-based accessible identity when resolution is unavailable. Never infer Party kind from the binding. |
| **work-tree** | `FluentTreeView` + `FluentTreeItem` | Keyboard disclosure follows Fluent behavior. Render only Attached edges; show Reserved, Creating, Released, superseded, and delayed-leg rejection as separate coordination evidence. Preserve hierarchy and selection on refresh, and expose eventual cascade/resume without moving focus. |
| **queue-row** | Generated Fluent data grid + `FcExpandInRowDetail` + `FcExpandedRowHiddenBanner` | Apply the exact What's-next predicate/order and actor-aware action availability. Claim loss or actor mismatch refreshes safely. If confirmation removes the focused row, use the ordered focus fallback below while preserving filters and scroll. |
| **capture-command** | Generated command form + `FcFieldPlaceholder` + `FcAuthorizedCommandRegion` + `FcLifecycleWrapper` | Obligation is primary and exposes its 4,000-character limit before entry. Every optional act-note field exposes its 1,000-character limit before entry. Verified tenant/actor context and server-controlled or derived fields are evidence, never editable input. Preserve values through denial, rejection, or infrastructure retry; distinguish acknowledgement from projection confirmation. |
| **work-history** | L2 projection template using `FluentStack`, `FluentText`, and `FcFluentIcons` | Show authorized Raw Acts chronologically with trusted actor/origin, locale-formatted time, act, and optional note. Preserve original progress plus additive correction, Handoff parties, and audited delayed-child rejection without exposing raw payloads, hidden envelope fields, or infrastructure data. |
| **conversation-panel** | Separately owned Hexalith.Conversations FrontComposer view in a `FluentAccordionItem` | A Conversation correlation ID is a link, not Works-owned content. Load/post/error state is independent; linking never merges dialogue into `work-history`. |
| **natural-language-response** | `FluentTextArea` + `FluentButton` + `FcLifecycleWrapper` | Theme 3 only. Constrained actions remain primary. Preserve the user's original text, show interpretation confidence/outcome, require review when unsafe to apply, and retain text on rejection or connectivity failure. |
| **email-action-set** | Email-safe presentation tables + semantic anchor links | Apply the [Theme 3 email implementation gate](#theme-3-email-implementation-gate), then provide constrained actions, submit/success, and reply-in-own-words. Theme 6 adds bound single-use links, absolute expiry, prior-use evidence, step-up, and fresh-link recovery. |
| **pause-live-updates** | `FluentButton` + `FluentBadge` + shared projection connection status | Announce pause once; update the visible associated queued count silently and expose it on focus; announce one deduplicated atomic summary on resume. Apply the batch without focus theft or lost reading position. |
| **cost-meter** | `FluentProgressBar` + `FluentText` | Theme 5 only. Keep Cost distinct from Effort, locale-format and group totals by currency/Unit without conversion, and reuse Roll-Up freshness/Unavailable rules. No Cost input or result appears before its horizon. |

FrontComposer command lifecycle is inherited unchanged: Submitting, Acknowledged, Syncing, Confirmed, Rejected, IdempotentConfirmed, NeedsReview, Warning, and Degraded. For example, a Confirmed `Complete` command can produce Work Status Completed; a command-lifecycle Rejected outcome is not Work Status Rejected.

## Lifecycle and Action Matrix

The PRD workspace memlog owns the superseding Handoff/CorrectProgress decisions; the rendered PRD owns unchanged cells. Read this status matrix in parallel with **Actor and origin availability**: a legal status cell does not authorize an actor. `→X` means accepted and resting at X, `R` means domain rejection with no state change, and `NoOp` means an acknowledged duplicate with no event; authorization denial happens before the matrix.

| From / act | Assign | Queue | Claim | Suspend | Resume | Handoff | Complete | Cancel | Reject | Expire |
|---|---|---|---|---|---|---|---|---|---|---|
| **Created** | →Assigned | →Queued | R | R | R | R | R | →Cancelled | R | →Expired |
| **Assigned** | →Assigned (rebind) | →Queued | →InProgress | R | R | R | R | →Cancelled | →Queued (default) / →Rejected (non-requeue) | →Expired |
| **Queued** | →Assigned | R | →InProgress | R | R | R | R | →Cancelled | R | →Expired |
| **InProgress** | R | R | R | →Suspended | R | →InProgress (binding only) | →Completed | →Cancelled | R | →Expired |
| **Suspended** | R | R | R | R | →InProgress | →Suspended (binding only) | →Completed | →Cancelled | R | →Expired |
| **Completed** | R | R | R | R | R | R | NoOp | R | R | R |
| **Cancelled** | R | R | R | R | R | R | R | NoOp | R | R |
| **Rejected** | R | R | R | R | R | R | R | R | NoOp for duplicate non-requeue Reject; requeue Reject is R | R |
| **Expired** | R | R | R | R | R | R | R | R | R | NoOp |

Starting work is Claim. Assign/rebind remains separate and does not replace active Handoff. Handoff is an auditable binding change available by status in InProgress and Suspended; it preserves Work Status and, when Suspended, the Await-Condition set. Its actor eligibility and confirmation policy remain source-open, so every surface uses the server-authoritative capability and denial result rather than inferring permission.

### Actor and origin availability

| Authority result | Action presentation and recovery |
|---|---|
| Current server capability allows | Enable the named action and still treat submission-time authorization as authoritative. AuthorityLevel is descriptive data, not the decision. |
| Executor-bound capability | Where the authoritative source classifies an action as executor-bound, the authenticated acting Party must match the current Executor Binding. Derive availability from the server; never infer or submit an actor from the binding. |
| Current capability says unavailable | Disable or omit the action according to FrontComposer disclosure rules; when shown disabled, associate a visible reason with that action. Do not imply the status transition itself is illegal when actor/origin is the issue. |
| Executor mismatch after submission | Treat as an audited authorization denial, not a domain rejection. Retain input, refresh binding and capabilities, keep or restore focus at the action outcome, and identify the currently responsible Party only when the viewer may see it. |
| Tenant denial / no access | Fail closed before content or tenant existence is disclosed; replace protected content with inherited no-access recovery. |
| Queued Claim | An admitted tenant actor may Claim; the accepted winner becomes the bound Executor. Competing/stale claims recover through the domain/infrastructure outcomes below. |
| Trusted-origin action | System/Reactor actions are never exposed as user actions. A missing or invalid designated origin is denied and audited; the UI reports unavailable system processing without exposing trust internals. |

The full executor-bound action list and Handoff actor-eligibility rule are not yet rendered in the PRD. Consumers must not hard-code either list from this UX document; bind controls to authoritative command capability metadata and denial outcomes.

### Cross-cutting action consequences

The governing PRD and accepted PRD-memlog decisions own command and Reactor mechanics; this section records only their UX consequences.

- **Effort and completion:** accepted progress-to-zero may complete; explicit Complete is a separate terminal cause; cumulative CorrectProgress preserves prior evidence and may reopen only progress-completed work; ReEstimate/Reschedule never complete. CorrectProgress bounds, Unit/note rules, and confirmation remain source-open and come from authoritative command metadata.
- **Await and Conversation:** present accepted, consumed/idempotent, conflicting, and rejected outcomes from authoritative evidence. Name every active Await-Condition and keep Conversation correlation separate from content; do not reproduce matching or storage rules here.
- **Child and termination:** use the Reserved/Creating/Attached/released-or-superseded states below and UJ-3 for presentation. Only Attached work enters tree/Roll-Up/cascade, only Cancel/Expire cascade, and delayed invalid work cannot mutate topology. The durable evidence name/protocol remains source-open.

### Outcome semantics

| Outcome | UX response |
|---|---|
| Domain rejection | Keep the attempted input, explain the current domain fact and next legal action, refresh relevant projection evidence, and do not label it a crash. Claim loss is this class. |
| Authorization denial | Retain safe input, refresh authoritative actor/origin capability, and distinguish tenant denial, executor mismatch, and unavailable trusted-origin processing without leaking tenant or trust details. No domain event is implied. |
| Idempotent no-op | Confirm that the requested outcome was already recorded; do not fabricate a new history entry or treat it as an error. |
| Infrastructure error | Preserve input and context, state whether acceptance is unknown, offer a safe retry only through FrontComposer lifecycle rules, and never claim success without evidence. |

## Roll-Up and Freshness

The governing PRD (FR-11/FR-12) owns Roll-Up calculation and the accepted PRD memlog owns corrected-completion and attachment effects. The UI renders projection evidence; it does not recompute it.

- Show three distinct results: the selected item's own Remaining; recursive rolled Remaining for its Attached subtree, grouped by Unit; and the active-unestimated descendant count. Never add mixed Units or substitute zero for unestimated work.
- Terminal contribution is zero even when explicit completion retains residual history. A correction-driven reopened contribution appears only when freshly projected; overrun or non-terminal zero never implies completion.
- Show freshness time/position and `Fresh`, `Stale`, `Reconnecting`, or explicit `Unavailable` text. Normal projection lag is stale, not unavailable. During repair/rebuild, retain the last committed result with its timestamp; if no trustworthy result exists, say unavailable and omit the number.
- Related attachment, resume, reopen, and cascade changes remain eventual; do not optimistically rewrite other aggregate state.

## State Patterns

### Inherited FrontComposer matrices

Inherit the current `{project-root}/references/Hexalith.FrontComposer/.../EXPERIENCE.md` shell, navigation, Home/account/access, projection, command, form, grid, live-update, and recovery matrices. This includes Loading, Empty, Data, Stale, Reconnecting, FallbackPolling, SlowQuery, MaxItems, Reconnected, Query error, offline, denied/no-access, authorization loss, field/server validation, dirty form, Submitting, Acknowledged, Syncing, Confirmed, Rejected, IdempotentConfirmed, NeedsReview, Warning, and Degraded. Works adds domain copy and state combinations; it does not fork those shared behaviors.

### Works-specific state coverage

| State or edge | Required presentation and recovery |
|---|---|
| Created with Executor Binding | Show Created Work Status and binding together; do not relabel Assigned until the Assign transition exists. |
| Unestimated | “Not estimated”; omit numeric Remaining and require explicit Complete unless an estimate is later accepted. |
| Explicit residual completed | Completed remains primary and terminal; preserve historical Estimated/Done/Remaining and explain zero Roll-Up contribution. CorrectProgress must not be offered as a reopen path. |
| Progress completed / corrected reopen | History identifies progress completion. An accepted cumulative correction that restores positive Remaining adds correction/reopen evidence and returns Work Status to In progress; never erase the original report or completion. |
| Overrun | Show Done > Estimated, Remaining 0, and the actual non-terminal Status; offer only legal current actions. |
| Non-terminal zero | Show Remaining 0 and the actual non-terminal Work Status without a completion announcement or misleading 100% progressbar. |
| Immutable Unit error | Link the error to progress/estimate input, keep submitted value, name the established Unit, and focus the summary without erasing work. |
| Multiple await conditions | Name every condition; first match resumes and clears all. Later UI refresh explains consumed/no-op or unmatched/rejected evidence. |
| Active Handoff | InProgress stays InProgress; Suspended stays Suspended with its Await-Conditions. Refresh `party-reference`, retain focus/context, and append old/new responsibility evidence only after confirmation. |
| Reserved / Creating | Show pending coordination outside the authoritative tree. Creating persists through safe redelivery/recovery until durable child-created evidence permits Attached; never imply a child exists from dispatch intent alone. |
| Attached | Add the child to tree/Roll-Up/cascade only after durable child-created evidence and attachment are projected; announce the material addition once without moving unrelated focus. |
| Released / superseded / delayed leg | State that no child was attached. Remove optimistic pending content when authoritative, retain audited rejection evidence, and require a new reservation for retry; delayed redelivery cannot mutate child or topology. Whether Released and Superseded need distinct visual treatment remains source-open. |
| Eventual resume | Keep Suspended plus “trigger received; resume pending” only when evidence supports it; recover from stale/reconnecting without claiming InProgress early. |
| Eventual cascade | Parent terminal and descendant still active may coexist. Explain pending propagation; keep descendant acts visible; settle without focus theft. |
| Claim loss | Say another Party claimed it, remove illegal Claim after refresh, retain row/grid position, and direct the Party to the next available item. |
| Action disabled / actor mismatch / denied | Associate a reason with a disabled action. After denial, retain safe input, refresh binding and capabilities, distinguish authorization from domain rejection, and fail closed where access is lost. |
| Conversation boundary | Show unlinked, link pending/rejected, linked, content loading, content error, post pending/rejected, and recovered independently of Works history. Never substitute Raw Acts for missing Conversation content. |
| Capture/action lifecycle | Cover empty/dirty/validating/invalid/submitting/acknowledged/syncing/confirmed/authorization-denied/domain-rejected/idempotent/unknown/degraded. Show 4,000-character Obligation and 1,000-character note help before entry and preserve submitted text after failure. |
| Projection health | Cover loading, empty, data, stale, reconnecting, fallback polling, slow query, query failure, offline, no access, reconnected/recovered, and Roll-Up unavailable. Keep last trustworthy data labeled when safe. |

### Theme 3 and Theme 6 email and natural-language states

| Surface state | Horizon | Required behavior |
|---|---|---|
| Email evidence gate | Theme 3 entry | Implementation remains blocked until the [Theme 3 email implementation gate](#theme-3-email-implementation-gate) passes. |
| Email ready | Theme 3 | State obligation, sender/context, constrained actions, and a reply-in-own-words path. If Theme 6 hardening is active, also state absolute expiry and any step-up condition. |
| Email submitting | Theme 3 | The response page has a stable progress title and blocks accidental duplicate local activation without depending on animation. This is interaction safety, not a claim that Theme 6 single-use enforcement exists. |
| Email success | Theme 3 | State the exact accepted act and that projection catch-up may follow; do not call acknowledgement domain completion unless it completed. |
| Email already used | Theme 6 | Treat prior use as idempotent evidence when appropriate; show what was already recorded and a safe return path. |
| Email expired | Theme 6 | Explain that no action was applied and offer reply-based recovery or a way to request a fresh link; never strand the supplier. |
| Natural-language high confidence | Theme 3 | Show the proposed or applied constrained act and preserve the original words in authorized history. |
| Natural-language low confidence | Theme 3 | Do not guess. Ask a specific confirmation or offer constrained actions; retain the response. |
| Natural-language rejection/failure | Theme 3/6 | Distinguish domain rejection from connectivity, interpretation, or link-policy failure; preserve text and offer the recovery appropriate to the active horizon. |

## Interaction Primitives

- **Select and inspect:** queue/grid selection does not trigger an act. Opening row detail uses FrontComposer expansion; opening the Work Item route preserves return context.
- **Act on work:** derive controls and disabled reasons from authoritative capability, then use the action matrix and outcome table. Do not synthesize actor eligibility, command validation, trusted origin, or acceptance from visible state.
- **Correct or hand off:** keep original evidence visible while proposed change is pending. Confirmed correction appends cumulative evidence and announces any reopen once; confirmed active Handoff updates only `party-reference` while preserving status/context. Explicit completion never offers reopen.
- **Wait and coordinate:** name every Await-Condition. Apply resume, attachment, released/superseded, and delayed-leg presentations from State Patterns; coalesce material eventual changes without moving focus.
- **Terminate:** Cancel/Expire disclose eventual descendant impact before confirmation; Assigned Reject clearly distinguishes default requeue from terminal non-requeue.
- **Capture and act notes:** expose “4,000 characters maximum” with Obligation and “1,000 characters maximum” with every act-note field before entry. If a counter exists, keep it available on demand and announce bounded thresholds, never each keystroke. Preserve the complete value after client, authorization, domain, or infrastructure failure so it can be shortened.
- **Pause live updates:** pausing freezes visual application, not receipt. Announce pause once; update the visible `FluentBadge` count silently, expose the count on focus, then apply one coherent batch and announce one deduplicated atomic summary on resume.

Register no Works-specific keyboard accelerator in this contract. A future Module may add FrontComposer-managed accelerators only with visible discoverability and inherited conflict handling.

## Privacy and Localization

- Tenant identity and authenticated membership fail closed before content is rendered. Query filtering, status legality, responsibility matching, and trusted-origin authorization are separate checks. AuthorityLevel remains carried, not enforced, and is never styled as an authorization claim.
- Work history shows only authorized Raw Acts. Preserve verbatim human-authored act wording and optional notes, but render through safe text primitives and omit hidden technical data, envelope internals, secrets, tokens, raw EventStore payloads, diagnostic stack traces, and unrelated tenant identifiers.
- Conversation content belongs to Hexalith.Conversations and follows that module's authorization, retention, loading, and posting rules. A Work Item correlation ID does not grant content access.
- User and external-party content carries its known language through `lang`; direction uses `dir` and bidi isolation so IDs, dates, numbers, Party names, and mixed-script content cannot reorder surrounding UI. Unknown-language text uses a safe locale fallback without claiming a language.
- All product copy, labels, errors, accessible names, action names, email subjects, and templates are resource-backed. Dates, times, durations, numbers, Units, and Theme 5 currency use the active locale/time zone while preserving an unambiguous absolute email expiry.
- Raw Acts remain verbatim within the authorized view; translations or machine interpretations are labeled derived views, never replacements for the recorded words.

## Accessibility Floor

Works inherits FrontComposer's WCAG 2.2 AA target and adds the following acceptance contract.

### Theme 3 email implementation gate

The email mock is not implementation-ready. Before Theme 3 email work starts:

1. Approve the email clients and high-contrast modes that the product will support.
2. In each supported client/mode, record evidence that text, links, and action boundaries are perceivable, and that focus or selection is visible when the client exposes it.
3. Verify client-safe semantic markup, a complete plain-text version, system fonts, descriptive links with unique purposes, 44 × 44 CSS px action targets, narrow-width use, and 200% text sizing.

Until that evidence exists, the email reference remains visual-only and the email channel is not implementable. Passing this gate makes only the Theme 3 interaction ready to implement; Theme 6 still owns production link binding, single-use, absolute expiry, prior-use evidence, and risk-based step-up.

### Semantics and accessible names

| Component | Required semantic/accessibility contract |
|---|---|
| `burn-down-meter` | Follow the normative edge-state matrix below. Accessible name = “Effort for {authorized obligation or WorkItemId}”; visible text always carries Estimated, cumulative Done, Remaining, and Unit where defined. |
| `roll-up-summary` / `cost-meter` | Label the scope, metric, Unit/currency, freshness, and unavailable state. Mixed Units are separate groups; updates are coalesced. |
| `work-status` | Visible label plus matching accessible name and tooltip; icon is supplemental. Do not expose icon filenames. |
| `party-reference` | Name = resolved Party name plus Channel; unresolved fallback includes the stable Party reference without inferring kind. |
| `work-tree` | Use Fluent's native tree/treeitem semantics and keyboard model. Expose each Work Item's name, hierarchy, expanded state, and selected state without manually duplicating ARIA supplied by Fluent. |
| `queue-row` | Grid semantics remain intact. Row actions include obligation or WorkItemId in their accessible names. Disabled actions expose their reason; denied/mismatch outcomes use a linked row/action status. |
| `capture-command` / `natural-language-response` | Persistent visible labels, limits, instructions, and errors are programmatically linked to fields; error summary links focus to the exact field while retaining input. |
| `work-history` | Semantic list with an accessible region heading; actor, time, act, and note retain meaningful reading order. Decorative timeline marks are hidden. |
| `conversation-panel` | Independently labeled region/status; posting and load errors are associated with Conversation controls, not Works lifecycle. |
| `email-action-set` | Use semantic anchor links named by destination purpose; the [Theme 3 email implementation gate](#theme-3-email-implementation-gate) governs channel-specific semantics, parity, and target size. |
| `pause-live-updates` | Button exposes pressed/paused state and queued count; a separate status announces resume result. |

### Burn-Down edge-state semantics

The bar itself is never a live region. After an accepted effort act, one contextual status may summarize the value change; `work-status` alone announces Completed or reopened In progress.

| Edge state | Progress semantics | Visible/accessible value text | Update announcement |
|---|---|---|---|
| Ordinary determinate (`Estimated > 0`, `0 ≤ Done ≤ Estimated`, `Remaining > 0`) | `role="progressbar"`; `aria-valuemin="0"`, `aria-valuemax=Estimated`, `aria-valuenow=Done` | “Done {Done} of {Estimated} {Unit}; Remaining {Remaining}” | One polite effort summary after the accepted act; no announcement per rendered increment. |
| Unestimated | Omit the bar or make its track decorative; no progressbar ARIA values | “Not estimated; Done not established; Remaining not established” plus Unit only if established | No percentage/value announcement. |
| Zero estimate | Omit/decorate the bar; never expose `aria-valuemax="0"` | “Estimated 0 {Unit}; Done 0; Remaining 0” with actual non-terminal Work Status | No completion announcement. |
| Overrun (`Done > Estimated`) | Omit/decorate the bar so clamping cannot hide overrun | “Done {Done}; Estimated {Estimated}; over by {difference} {Unit}; Remaining 0” with actual Work Status | One polite correction/re-estimate summary; no completion announcement. |
| Non-terminal zero | Omit/decorate the bar; do not expose a misleading complete percentage | “Remaining 0 {Unit}; work status {current non-terminal label}” | Work Status remains unchanged and is not announced as Completed. |
| Progress-completed | For positive Estimated, keep `role="progressbar"` with min 0, max Estimated, now Estimated and value text “Done {Estimated} of {Estimated}; Remaining 0” | Show completion cause as “Completed by progress” in authorized history | Meter stays silent; `work-status` announces Completed once. |
| Explicit residual-completed | Omit/decorate the bar because its fraction no longer represents status | “Completed explicitly; Estimated {Estimated or Not estimated}; Done {Done or Not established}; residual Remaining {Remaining or Not established}; Roll-Up contribution 0” | Meter stays silent; `work-status` announces Completed once. |
| Corrected/reopened | Re-evaluate into ordinary, overrun, or non-terminal-zero semantics after confirmed cumulative correction | Show original report in history plus corrected cumulative Done/Remaining and “In progress” when reopened | `work-status` announces In progress once when reopen occurs; meter does not announce completion. |

### Focus, validation, and live change

- Every operation works by keyboard. Use Fluent focus visuals and DOM order; do not assign positive `tabindex`. Opening accordion/tree/grid detail moves focus only when the initiating control's contract requires it; closing returns focus to the initiator.
- Validation is never color-only. A linked summary receives focus after failed submission, each message identifies its field and resolution, and correcting/resubmitting does not discard unrelated values. Obligation's 4,000-character and act note's 1,000-character limits are visible and programmatically associated before entry; counters announce bounded thresholds only.
- Domain rejection, claim loss, Theme 6 expired/used email action, offline recovery, and query recovery move focus to a stable heading/summary or retain it on the initiating control. Background projection updates, Reactor resume/cascade, and command confirmation never steal focus.
- Before applying a confirmed act or resumed batch, remember the focused control's role, keyed row, and visible index in the filtered/sorted grid. Afterward, return to the corresponding control if that row remains; otherwise focus the row now occupying the old index, then the nearest preceding row, then the labeled grid or empty-state heading. Preserve filters, selection, scroll, and valid expansion; announce the material outcome/removal once. Unrelated background updates never move focus.
- Coalesce live announcements: announce the meaningful status/count/result once, not every changed cell or child. Use polite status for normal projection/freshness changes and assertive alert only for an immediate blocking error.
- `pause-live-updates` is always reachable while updates are active. Announce pause once; keep queued-count changes silent while updating the visible/button-associated `FluentBadge`; expose the current count on focus; announce one atomic deduplicated summary on resume. Pause never conceals command evidence or security/access loss.

### Reflow, motion, targets, and contrast

- At 320 CSS px width and at 400% browser zoom, no two-dimensional scrolling is required except a clearly labeled data grid/tree region whose relationship cannot be linearized. Primary content and actions reflow in one dimension without clipping or overlap.
- Support WCAG text-spacing overrides without lost content or controls. Platform interactive targets are at least 24 × 24 CSS px or meet the spacing exception; email actions are at least 44 × 44 CSS px.
- Reduced motion removes nonessential transitions and never suppresses state text. Live values must not pulse to communicate change.
- Forced-colors mode preserves native/inherited boundaries, status labels, meter labels, focus, selected/expanded state, and actionable email fallback text. No state depends on Works accent or color alone.

## Responsive & Platform

- **v1 harness:** terminal/test-runner output uses headings and text labels for accepted, domain-rejected, authorization-denied, and infrastructure-unknown evidence, plus current domain evidence and next safe action. It remains structurally readable without claiming the web accessibility contract.
- **Future FrontComposer web:** desktop supports dense coordinated work; smaller viewports prioritize obligation, Work Status, executor, Remaining, and legal actions. Secondary detail moves into the same accordion/row-detail pattern, not a separate mobile information architecture.
- **Touch:** future platform controls meet the 24 CSS px target floor; destructive confirmation and row actions remain keyboard/touch operable without hover. Email uses the stricter 44 CSS px target.
- **Email:** the [Theme 3 email implementation gate](#theme-3-email-implementation-gate) owns client, contrast, markup, target, and reflow requirements. Show absolute expiry only when Theme 6 link hardening supplies it.
- **MCP/CLI/chatbot:** text output preserves verified tenant/actor context, command lifecycle/outcome, exact Work Status, effort/Unit, freshness, and a next legal action. Theme 2 input is exact and command-shaped; natural language starts only in Theme 3.

## Inspiration & Anti-patterns

Preserve the useful inspiration: a short obligation-first capture, compact assigned-plus-queue view, clear Work Tree hierarchy, Burn-Down/Roll-Up evidence, and constrained one-tap email actions with reply fallback.

Avoid task-app gamification, inferred executor personas, manual status palettes, progress-only percentages, synchronous Roll-Up claims, unified history that copies Conversation content, disguising active Handoff as Assign/requeue, erasing original progress during correction, raw event payloads, stale mock navigation, dead-end magic links, and any roadmap surface presented as currently shipping.

## Key Flows

### UJ-1. A builder wires Works into a module.

**Protagonist and horizon:** A Hexalith builder; v1 headless kernel through the platform-hosted test harness.

1. Reference the Works domain assembly and the owning-module reference value objects for Party, Conversation, and Tenant.
2. Supply the no-LLM `IExpectationResolver`; do not wire an LLM or routing implementation.
3. Submit Create through the normal authenticated tenant command pipeline, optionally with an Executor Binding.
4. Observe accepted event evidence and reconstruct the Work Item. A binding on creation does not change Created to Assigned.
5. Verify obligation, identity, tenant, optional references, immutable Unit rule, and Raw Act actor provenance without copying sibling-module data.

**Climax:** one pure domain integration produces the event-sourced Work Item under the correct tenant with no custom infrastructure path.

**Failure and accessibility beats:** Invalid reference/value input is a linked domain rejection, not a host crash; an infrastructure failure says acceptance is unknown and preserves the command evidence for safe diagnosis. The v1 harness emits structured text with no color-only distinction. A future `capture-command` focuses a linked error summary, retains every value, and distinguishes Acknowledged from Confirmed without stealing focus.

### UJ-2. A system/AI executor burns down work through one uniform surface.

**Protagonist and horizon:** A service Party with Channel = MCP and machine authority; the domain path is v1, while a production MCP adapter is Theme 2 remainder.

1. The service Party is bound through the same Executor Binding shape used for any other Party.
2. Server-authoritative capability permits the Assigned Claim and verifies responsibility at submission; assignment alone does not start work. A Queued Claim instead binds its admitted winner.
3. The bound service Party reports progress through the uniform command contract. Each accepted act records trusted actor evidence, cumulative Done/Remaining, and projection freshness without branching on executor kind.
4. When capability permits Handoff, the active item changes to another Executor Binding while remaining In progress. The new responsible Party continues through the same surface; history keeps both responsibility facts.
5. The progress act that reaches zero records progress-driven completion. An explicit Complete remains a separate cause and terminal path.
6. If an earlier progress report was wrong, CorrectProgress appends corrected cumulative Done without erasing it. Restored positive Remaining reopens only the progress-completed item to In progress; the corrected work can then continue.

**Climax:** the exact command path that advances a service Party can advance a human or external Party without kind-specific domain code.

**Failure and accessibility beats:** An actor mismatch or unavailable Handoff is an authorization outcome, not a domain rejection; retain safe input and refresh binding/capability. Invalid progress/correction follows server-provided validation without this spine inventing bounds. Duplicate evidence and transport failure remain distinct. Future surfaces name Work Status and Unit, preserve input, keep original/corrected acts, and let Work Status announce completion or reopen once.

### UJ-3. A Work Item spawns a child, suspends, and resumes.

**Protagonist and horizon:** Ada's release-checklist Work Item, driven by a system Party on MCP; v1 kernel/Reactor behavior, with the MCP adapter in Theme 2 remainder.

1. Acting for Ada's release-checklist Work Item, the system Party submits the public Work-Tree Registry reserve request with the complete sign-off child payload and “suspend awaiting child” intent.
2. The edge is Reserved: it is not yet visible in `work-tree`, Roll-Up, or cascade.
3. Trusted Reactor coordination advances the reservation to Creating and requests child creation. Creating remains pending evidence outside the authoritative tree, including across safe recovery/redelivery.
4. Only observed durable child-created evidence permits Attached. The child now enters `work-tree`, Roll-Up, and cascade; subsequent parent bookkeeping preserves the accepted suspend intent without pretending the writes were atomic.
5. Ada's item rests Suspended with a child-completion condition and, in the alternate case, a date condition. The Roll-Up shows only Attached work and its freshness.
6. The sign-off child progresses and completes. Its durable completion evidence causes the Reactor to request the matching resume; Ada's item remains Suspended during the eventual window, then returns to InProgress and clears the set.
7. Alternate path: the matching date arrives first and independently resumes the item; a later repeat of that consumed condition is a no-op, while a different/non-matching trigger rejects.

**Climax:** durable reserve/attach, child completion, and Reactor resume produce a coherent tree without synchronous cross-aggregate assumptions.

**Failure and accessibility beats:** A Released or Superseded reservation never attaches. A delayed saga leg is deterministically rejected and audited with no child/topology mutation; redelivery shows the same rejection and retry starts a fresh reservation. A prolonged Creating state remains explicit for supported recovery. Future tree/status updates preserve disclosure/focus, announce material attachment/resume once, label freshness, and expose every await condition in text.

### UJ-4. (Deferred — Theme 3 horizon, not built in v1.)

**Protagonists and horizon:** Mary and an external supplier reached only by inbox; Theme 3.

1. Before implementation, the team satisfies the [Theme 3 email implementation gate](#theme-3-email-implementation-gate); the mock alone cannot pass it.
2. Mary captures the obligation in one line; the same Works command contract creates/binds the item without treating email as an executor kind.
3. The supplier receives an email with the obligation, sender/context, constrained actions, and “reply in my own words.” No routine Works account or task-app login is required; Theme 6 may introduce risk-based step-up without creating such an account.
4. A constrained action submits and returns a page that states the exact accepted act. Only Theme 6 defines how a production action link is bound to its authorized action context and adds single-use, expiry, idempotency, and fresh-link recovery.
5. A reply retains the supplier's verbatim authorized words as the Raw Act source. Natural-language interpretation is a labeled derived projection; high-confidence constrained intent may proceed, while low confidence requests confirmation instead of guessing.
6. The Work Item and separately owned Conversation content update through their own lifecycles and freshness states.

**Climax:** one low-friction inbox interaction moves work forward while the supplier's exact words remain the evidence.

**Failure and accessibility beats:** Theme 3 delivery/action failure, domain rejection, low confidence, and service outage—and Theme 6 expired/used/step-up outcomes—each have distinct copy and recovery. Email actions are descriptive and at least 44 × 44 CSS px with plain-text parity; response pages have a real heading, linked errors, stable focus, `lang`/`dir` and bidi isolation, no color-only meaning, and no forced task-app-account dead end.

### Reference flow — Dana coordinates the future Works Module (non-source)

**Protagonist and horizon:** Dana, a coordinator; uncommitted post-v1 FrontComposer example used only to close the proposed web IA.

1. Dana starts from preserved FrontComposer Home and opens the single Works Module entry.
2. The default route-backed tab opens What's next with the coordinator's Assigned-plus-Queued view. Dana retains filters and position while opening a row.
3. Dana moves to Work Item detail, where one `FluentAccordion` opens the primary Work Item and keeps history separate from Conversation content. Server capabilities expose only currently available actions and associated disabled reasons.
4. Dana follows Work to its route-backed tab; the Attached tree remains visible while Dana opens the supporting Roll-Up or child-coordination accordion item, then returns through preserved route context.
5. Dana opens Capture through the registered generated command route, completes the resource-backed form with visible limits, and returns to the prior Module context after confirmation.

**Climax:** one Module entry, two route-backed tabs, subordinate detail, and generated Capture cover the proposed web needs without displacing Home or creating extra rail destinations.

**Failure and accessibility beats:** A denied direct route uses inherited no-access recovery without disclosing Works data. Stale/reconnecting views retain labeled trustworthy data. If refresh removes Dana's focused queue row, use the control/row/index fallback in [Focus, validation, and live change](#focus-validation-and-live-change); navigation and resumed batches preserve valid accordion, filter, selection, expansion, and scroll context without stealing focus. This example is neither a PRD UJ nor a shipping commitment.

## Functional Requirement Traceability

This final audit index maps UX treatment and disposition without restating the governing PRD.

| FR | UX treatment | Horizon/disposition |
|---|---|---|
| FR-1 | `capture-command`; Created-with-binding evidence | v1 harness; future generated command form |
| FR-2 | Obligation-first labels; optional expectation reference | v1 contract; all later surfaces |
| FR-3 | `burn-down-meter`; edge-state semantics and cumulative correction display | v1 contract plus PRD memlog override |
| FR-4 | Schedule display and exact What's-next ordering | v1 projection; later surfaces |
| FR-5 | `work-tree`; parent/child and multi-await presentation | v1 contract/projection; later web |
| FR-6 | Nine `work-status` variants and legal-action matrix | v1 contract; all later surfaces |
| FR-7 | Trusted actor/origin history; additive Handoff/correction/rejection evidence | v1 evidence plus PRD memlog overrides |
| FR-8 | Progress/explicit completion and correction-driven conditional reopen | v1 contract plus PRD memlog override |
| FR-9 | ReEstimate/Reschedule; no completion by estimate | v1 contract; later actions |
| FR-10 | Cancel/Reject/Expire and only Cancel/Expire cascade | v1 contract/Reactor; later actions |
| FR-11 | `roll-up-summary`, freshness, residual terminal contribution | v1 projection; later surfaces |
| FR-12 | Per-Unit totals plus unestimated count | v1 projection; later surfaces |
| FR-13 | Reserved/Creating evidence, durable attachment, delayed-leg rejection | v1 registry plus PRD memlog override |
| FR-14 | Named multi-await suspension state | v1 contract; later detail |
| FR-15 | Match, consumed no-op, eventual resume states | v1 contract/Reactor; later detail |
| FR-16 | Reserved → Creating → durable child evidence → Attached; fresh-reservation recovery | v1 registry/Reactor plus PRD memlog override |
| FR-17 | Neutral `party-reference`; Assign separate from active Handoff | v1 contract plus PRD memlog override |
| FR-18 | Assigned/Queued Claim, queued winner binding, claim-loss recovery | v1 projection/contract; Theme 2+ adapters |
| FR-19 | Server actor capability; AuthorityLevel carried, not enforced | v1 responsibility override; roles Theme 4 |
| FR-20 | Exact Executor/coordinator predicate and ordering | v1 projection; later surfaces |
| FR-21 | `conversation-panel` boundary/link states | v1 correlation contract; later composition |
| FR-22 | Exact Theme 2 commands and no-LLM resolver boundary | ports in v1; adapters by horizon |
| FR-23 | Source-authority/boundary reference | v1 planning/delivery artifact |
| FR-24 | Platform-hosted harness, no Works-owned host UI | v1 only |
| FR-25 | Capability/denial, domain, no-op, and infrastructure evidence | v1 tests; inherited later UX |
| FR-26 | Eventual create/attach, resume, reopen, and cascade states | v1 Reactor; later surfaces |
