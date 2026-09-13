---
name: Works
description: Calm operational visual contract for Work Item coordination through Hexalith.FrontComposer and Blazor Fluent UI V5.
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
typography:
  inherited:
    note: 'Use the active FrontComposer FcTypoToken mappings and FluentText parameters; Works defines no type ramp.'
  numeric:
    note: 'Use an inherited Fluent text role with tabular numerals only where changing values would otherwise jitter.'
components:
  burn-down-meter:
    implementation-base: 'FluentProgressBar + FluentText'
    visual-rule: 'Effort shows labeled Estimated, Done, Remaining, and Unit; a progress bar appears only where its determinate value is truthful.'
  roll-up-summary:
    implementation-base: 'FluentStack + FluentText + FrontComposer projection-health components'
    visual-rule: 'Own Remaining, rolled Remaining by Unit, unestimated-descendant count, and freshness are visually separate.'
  work-status:
    implementation-base: 'FcStatusIcon using an existing BadgeSlot + adjacent FluentText label'
    visual-rule: 'Every Work Status maps to one of the six supported BadgeSlot semantics and keeps its visible localized label; no custom glyph, pill, or palette.'
  party-reference:
    implementation-base: 'FluentAvatar + FluentText + FluentStack + FcFluentIcons'
    visual-rule: 'One neutral Party treatment shows resolved name and Channel without styling by executor kind.'
  work-tree:
    implementation-base: 'FluentTreeView + FluentTreeItem'
    visual-rule: 'Tree hierarchy contains work-status, party-reference, and compact burn-down-meter content without replacing native disclosure visuals; on the future Work page it is the sole always-visible primary visualization.'
  queue-row:
    implementation-base: 'Generated Fluent data grid + FcExpandInRowDetail + FcExpandedRowHiddenBanner'
    visual-rule: 'Dense row anatomy follows the generated grid; Works adds obligation, schedule, executor, Work Status, Burn-Down, and legal actions.'
  capture-command:
    implementation-base: 'Generated command form + FcFieldPlaceholder + FcAuthorizedCommandRegion + FcLifecycleWrapper'
    visual-rule: 'A short obligation-first form uses inherited Fluent inputs, keeps verified/server-derived context visually read-only, and shows the 4,000-character Obligation and 1,000-character act-note limits beside their fields.'
  work-history:
    implementation-base: 'FluentStack + FluentText + FcFluentIcons in a FrontComposer projection template'
    visual-rule: 'Authorized Raw Acts form a quiet chronological list; inferred interpretation and Conversation content are visually separate.'
  conversation-panel:
    implementation-base: 'Separately owned Hexalith.Conversations FrontComposer view inside FluentAccordionItem'
    visual-rule: 'Conversation content has its own labeled region and independent loading, posting, and error presentation.'
  natural-language-response:
    implementation-base: 'FluentTextArea + FluentButton + FcLifecycleWrapper'
    visual-rule: 'Theme 3 free text is subordinate to constrained actions and retains the original response through review or recovery.'
  email-action-set:
    implementation-base: 'Email-safe presentation tables and semantic anchor links'
    visual-rule: 'Theme 3 constrained-action hierarchy follows the email implementation gate in EXPERIENCE.md; Theme 6 adds bound-link, expiry, and step-up treatments.'
  pause-live-updates:
    implementation-base: 'FluentButton + FluentBadge + FcProjectionConnectionStatus'
    visual-rule: 'A persistent text control shows paused/running state and queued count without depending on color or motion.'
  cost-meter:
    implementation-base: 'FluentProgressBar + FluentText'
    visual-rule: 'Theme 5 cost is a separately labeled meter and per-currency/Unit Roll-Up resolved through inherited semantic roles, never an effort color variant or converted total.'
---

# Works Design

This file owns how Works looks. `EXPERIENCE.md` owns behavior, states, accessibility, and journeys.

## Brand & Style

Works is a work-item coordination kernel with a calm operational posture. It makes Remaining and responsibility legible under load without looking like a celebratory task app. The interface is precise, quiet, and evidence-led: no streaks, gamification, decorative dashboards, or visual branching between human, system, and external Parties.

Works inherits FrontComposer and Blazor Fluent UI V5 wholesale. Fluent owns component anatomy, type, spacing, radii, elevation, focus, semantic color, hover, selection, light/dark behavior, reduced-motion treatment, and forced-colors treatment. FrontComposer owns the active accent, shell composition, and shared projection/command visuals. This spine defines only the domain-specific compositions below.

## Colors

Works inherits the active FrontComposer accent and every Fluent semantic role; it defines no color token or accent placement. Canvas, foreground, border, focus, hover, selected, progress, success, warning, error, disabled, status, and Cost therefore resolve through the inherited system in every supported mode.

Work Status is always slot icon plus visible label; Burn-Down and Cost always carry text and value semantics. No Works-specific hover, dark-mode, status, Burn-Down, track, Cost, or email palette is permitted. Every load-bearing inherited foreground/background, focus indicator, and meter boundary needs WCAG 2.2 AA evidence in supported themes and forced colors.

The historical [color-theme exploration](.working/color-themes-1.html) is visual-only, superseded, and non-contractual.

## Typography

Use `{typography.inherited}` for every title, body, label, helper, status, and error. Page titles remain real route-level headings through FrontComposer. Use `{typography.numeric}` for live Remaining, Estimated, Done, rolled totals, counts, and Theme 5 Cost where tabular numerals prevent jitter. Works does not set font family, size, weight, line height, letter spacing, or foreground.

## Layout & Spacing

Use FrontComposer and Fluent layout/spacing defaults, including page, toolbar, tab, grid, and command layouts; Works defines no spacing scale. A future Works web surface is one Module workspace with route-backed tabs, not a family of primary rail entries. Dense queue and tree content may use a bounded labeled two-dimensional region where necessary; the surrounding page retains one logical reading order.

Every page, dialog, or detail panel with two or more sibling titled content regions uses one `FluentAccordion`, with one `FluentAccordionItem` per region and the primary/first item expanded. Page titles, breadcrumbs, toolbars, navigation chrome, and a single primary grid, form, detail, or visualization may stay outside; never hide the sole primary region.

On the future Work page, the Attached Work Tree is that sole always-visible primary visualization. One supporting accordion places Roll-Up first and expanded, with Unavailable as the alternate state inside that item; child-coordination evidence is the next item.

## Elevation & Depth

Use inherited Fluent surface layering and component elevation. Hierarchy comes from headings, spacing, disclosure, Work Status, and numeric relationships. Do not add bespoke shadows, nested card stacks, gradients, tinted dashboard bands, or elevation to signal progress.

## Shapes

Use the active Fluent component radii; Works defines no radius override. Work Status is not a pill family: use `FcStatusIcon`, visible label, and tooltip. Counts use `FluentBadge`. Email action shapes follow tested email-client constraints and do not establish a Blazor radius system.

## Components

### Visual references

The promoted HTML artifacts give every named IA/horizon group a visual reference, but remain **visual-only**:

| IA/horizon group | Visual-only reference | Contract boundary |
|---|---|---|
| v1 headless harness | [Harness evidence](mockups/key-v1-harness.html) | Structured builder evidence, never production navigation. |
| Theme 2 remainder | [MCP/CLI](mockups/key-mcp-cli.html) | Command-shaped adapter anatomy; exact tools, resources, syntax, and protocol remain source-open. |
| Uncommitted FrontComposer web | [What's next](mockups/key-whats-next.html), [Work Item detail](mockups/key-work-item.html), [Work Tree/Roll-Up](mockups/key-work-tree.html), [Capture](mockups/key-capture.html) | Proposed Module composition only; no shipping commitment. |
| Theme 3 | [Chatbot](mockups/key-chatbot.html), [email action](mockups/key-email-as-ui.html) | Natural-language recovery and email hierarchy, subject to the [email implementation gate](EXPERIENCE.md#theme-3-email-implementation-gate). |
| Themes 4–6 roadmap | [Operational reference states](mockups/key-roadmap-operations.html) | A reference-state board, not a page: Theme 5 Cost is the only defined seam; Theme 4 routing/escalation and Theme 6 Admin/Audit/participant capabilities remain source-open. |

Open these artifacts only for the named information grouping and density. Sample values and every implementation detail—including raw HTML/CSS, palettes, navigation, controls, comments, and lifecycle copy—are illustrative and must not be copied into Blazor. Use the stated FrontComposer/Fluent bases; these spines win on conflict.

### Component contracts

These 13 IDs and names are the canonical Works-specific component vocabulary shared with `EXPERIENCE.md`.

| Component ID | Visual contract |
|---|---|
| **burn-down-meter** | Build on `FluentProgressBar` and inherited Fluent text. Always show Estimated, Done, Remaining, and Unit as text; tabular numerals are the only Works type delta. Render the bar only where the accessibility matrix in `EXPERIENCE.md` defines truthful determinate semantics. Work Status, not the meter, owns completion. |
| **roll-up-summary** | Build from `FluentStack`, `FluentText`, and FrontComposer projection-health treatments. Keep own Remaining, rolled Remaining, per-Unit totals, unestimated-descendant count, and freshness labels distinct. On the Work page it occupies the first expanded supporting accordion item; Unavailable replaces its result within that item and is never the number zero. |
| **work-status** | Build on `FcStatusIcon` with the existing `BadgeSlot` mapping below, plus visible localized text and a keyboard-accessible tooltip. Do not supply a glyph or invent status artwork. Never create tinted status pills; the label distinguishes statuses that share a slot. |
| **party-reference** | Compose `FluentAvatar`, `FluentText`, `FluentStack`, and `FcFluentIcons`. Resolve the Party name and display Channel text; use a neutral fallback when unresolved. Do not infer or color-code bot/person/external kind from an Executor Binding. |
| **work-tree** | Use `FluentTreeView`/`FluentTreeItem` disclosure and hierarchy, with nested `work-status`, `party-reference`, and compact `burn-down-meter` content. It is the future Work page's sole always-visible primary visualization. Only Attached edges render as tree nodes; Reserved and Creating stay in the supporting child-coordination accordion item. Eventual cascade is described in text, not strike-through or color alone. |
| **queue-row** | Start with the generated Fluent data grid and FrontComposer row-detail components. The Works delta is compact obligation, exact schedule order, resolved executor, `work-status`, `burn-down-meter`, and legal row actions. Hover, focus, selection, resizing, virtualization, and separators remain inherited. |
| **capture-command** | Use the generated command form, authorization region, placeholders, and lifecycle wrapper. Fluent inputs own labels and validation. Obligation is visually primary with visible 4,000-character help; any act-note field carries visible 1,000-character help. Verified tenant/actor context and server-controlled or derived fields are read-only evidence, not inputs. Optional estimate, Unit, schedule, parent, binding, Conversation, and Expectation references follow generated density. |
| **work-history** | Present authorized Raw Acts through an L2 projection template using `FluentStack`, `FluentText`, and `FcFluentIcons`; a minimal semantic chronological list is allowed because Fluent has no timeline component. Preserve verbatim user wording while keeping actor, time, act, and optional note scannable. Do not render raw EventStore payloads. |
| **conversation-panel** | Compose the separately owned Hexalith.Conversations projection/command view in its own labeled `FluentAccordionItem`. Its loading, posting, and error visuals are independent of `work-history`; no Works-owned comment composer or merged stream. |
| **natural-language-response** | Theme 3 only. Use `FluentTextArea`, `FluentButton`, and lifecycle feedback. It remains visibly subordinate to constrained actions, retains submitted words, and distinguishes applied, needs-confirmation, and recovery states without a bespoke AI palette. |
| **email-action-set** | Outside the Blazor shell. Follow the normative [Theme 3 email implementation gate](EXPERIENCE.md#theme-3-email-implementation-gate). When allowed, visually prioritize descriptive constrained actions over reply-in-own-words in the email-safe presentation; mock colors remain illustrative. Theme 6 adds production binding, single-use, absolute-expiry, idempotency, and step-up treatments. |
| **pause-live-updates** | Use a labeled `FluentButton`, `FluentBadge` queued count, and the shared connection-status treatment. Paused/running and queued state remain visible in reduced motion and forced colors; no pulse is required. |
| **cost-meter** | Theme 5 only. Reuse `FluentProgressBar` and inherited text as a separately labeled Cost meter, with locale-formatted currency/Unit and its own Roll-Up. Keep each currency/Unit group separate with freshness or Unavailable; never merge through silent conversion. Distinguish Cost from Effort by words and placement, not a fixed gold palette. |

### Work Status variants

`FcStatusIcon` resolves its fixed icon from one of six existing slots. The adjacent localized label is required because several Work Status values intentionally share a slot.

| Contract value | Display label | `BadgeSlot` | Semantic meaning |
|---|---|---|---|
| `Created` | Created | `Neutral` | Recorded, not assigned or queued; an initial binding may already be present. |
| `Assigned` | Assigned | `Info` | A responsible Party is bound; its matching actor must Claim to start. |
| `Queued` | Queued | `Info` | Available in the tenant pull pool; the admitted Claim winner becomes bound. |
| `InProgress` | In progress | `Accent` | Claimed and active; responsibility-bound actions require actor/binding match. |
| `Suspended` | Suspended | `Warning` | Parked on one or more Await-Conditions; Handoff preserves this status and its conditions. |
| `Completed` | Completed | `Success` | Reached through progress or explicit Complete; only progress completion may reopen after correction. |
| `Cancelled` | Cancelled | `Neutral` | Terminal cancellation, including a completed eventual parent cascade. |
| `Rejected` | Rejected | `Danger` | Terminal only for a non-requeue Reject from Assigned; default Reject rests as Queued. |
| `Expired` | Expired | `Warning` | Terminal expiry after the accepted expiry act. |

Work Status never reuses FrontComposer command-lifecycle visuals as if they were the same state. Submitting, Acknowledged, Syncing, Confirmed, Rejected, IdempotentConfirmed, NeedsReview, Warning, and Degraded describe command evidence, not a Work Item's resting Status.

## Do's and Don'ts

| Do | Don't |
|---|---|
| Inherit the active FrontComposer accent and Fluent semantic roles wholesale | Add a Works accent token, manual mode alias, status palette, or second brand color |
| Use inherited semantic roles plus labels, icons, values, and boundaries | Encode progress, Cost, Work Status, freshness, or availability by color alone |
| Use the exact canonical component IDs and implementation bases above | Copy raw HTML/CSS or stale navigation/lifecycle semantics from the visual-only mocks |
| Keep own Remaining, rolled Remaining, unestimated count, and freshness separate | Present eventual or unavailable Roll-Up as synchronous, fresh, or zero |
| Keep one neutral `party-reference` treatment | Infer executor kind or branch styling from the Executor Binding |
| Keep Work Status and command lifecycle visually and semantically distinct | Call Acknowledged “completed,” or use command Rejected as Work Status Rejected |
| Inherit typography, spacing, radii, elevation, focus, hover, and mode behavior | Recreate Fluent component styling or a hand-authored type ramp |
| Reserve email HTML conventions for the email channel | Treat the email exception as permission for raw interactive Blazor controls |
