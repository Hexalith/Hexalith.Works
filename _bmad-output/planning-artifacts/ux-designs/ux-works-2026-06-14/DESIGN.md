---
name: Works
description: Work-item coordination console for the Hexalith ecosystem. Fluent UI v5 (RC) via Hexalith.FrontComposer; this DESIGN.md specifies the Works brand-layer delta only.
status: final
sources:
  - "{planning_artifacts}/prds/prd-works-2026-06-14/prd.md"
  - "{planning_artifacts}/prds/prd-works-2026-06-14/addendum.md"
  - "{planning_artifacts}/briefs/brief-works-2026-06-14/brief.md"
  - "{planning_artifacts}/briefs/brief-works-2026-06-14/addendum.md"
ui_system: "Fluent UI v5 (RC, Microsoft.FluentUI.AspNetCore.Components) via Hexalith.FrontComposer"
colors:
  # Brand-layer deltas only. All unlisted tokens — neutralBackground*, neutralForeground*,
  # neutralStroke*, surfaces, elevation, communication blue defaults, focus ring — INHERIT
  # from Fluent UI v5 design tokens. Do not restate Fluent's palette here.
  brand-primary: '#5B5FC7'        # Fluent "blurple" — Works identity + primary action + active nav
  brand-primary-hover: '#4F52B2'
  brand-foreground: '#FFFFFF'
  brand-primary-dark: '#9299F7'
  brand-foreground-dark: '#0E0E1A'
  # Burn-down (the signature). Done fills over a neutral track; "Done = Remaining is 0".
  burndown-done: '#107C10'
  burndown-done-dark: '#54B054'
  burndown-track: '#EDEBE9'
  burndown-track-dark: '#3B3A39'
  # Cost = a SECOND burn-down (Theme 5). Warm gold, never reused for effort.
  cost-meter: '#C19C00'
  cost-meter-dark: '#DCC149'
  # Lifecycle status vocabulary — grouped by phase. Each is paired with a glyph + label;
  # status is NEVER the sole progress signal (the burn-down is). Light / dark.
  status-created: '#616161'
  status-created-dark: '#ADADAD'
  status-assigned: '#0F6CBD'
  status-assigned-dark: '#479EF5'
  status-queued: '#008272'
  status-queued-dark: '#4DC2B0'
  status-inprogress: '#5B5FC7'
  status-inprogress-dark: '#9299F7'
  status-suspended: '#C19C00'
  status-suspended-dark: '#DCC149'
  status-completed: '#107C10'
  status-completed-dark: '#54B054'
  status-cancelled: '#8A8886'
  status-cancelled-dark: '#797775'
  status-rejected: '#C50F1F'
  status-rejected-dark: '#F1707B'
  status-expired: '#A4373A'
  status-expired-dark: '#E0808A'
typography:
  # Body, title, caption, label inherit Fluent v5's Segoe UI Variable ramp. Only the metric
  # roles are a Works delta — they exist so live-updating numbers don't reflow.
  metric-hero:
    fontFamily: 'Segoe UI Variable'
    fontSize: 40px
    fontWeight: '600'
    lineHeight: '1.1'
    letterSpacing: '-0.01em'
    note: 'font-variant-numeric: tabular-nums — the roll-up "one number"'
  metric:
    fontFamily: 'Segoe UI Variable'
    fontSize: 20px
    fontWeight: '600'
    note: 'font-variant-numeric: tabular-nums — inline burn-down Remaining figures'
rounded:
  # Inherits Fluent v5 corner radii. No Works delta.
  note: 'Inherit Fluent v5 (controlsmall 4px, control 6px, medium 8px). No override.'
spacing:
  # Inherits Fluent v5 4px-based spacing scale. Density is governed by FrontComposer's
  # spec-locked tiers, NOT a hand-authored Works scale.
  note: 'Inherit Fluent v5 spacing (4/8/12/16/20/24/32…). Density: FrontComposer tiers — ≤1 field Inline · 2–4 CompactInline · ≥5 FullPage.'
components:
  burndown-meter:
    track: '{colors.burndown-track}'
    done: '{colors.burndown-done}'
    remaining-number: '{typography.metric}'
    radius: '2px'
  rollup-one-number:
    value: '{typography.metric-hero}'
    cost-value: '{colors.cost-meter}'
  status-pill:
    # background = the status-* token at ~12% tint; text = the status-* token.
    inprogress: '{colors.status-inprogress}'
    suspended: '{colors.status-suspended}'
    completed: '{colors.status-completed}'
    rejected: '{colors.status-rejected}'
  party-chip:
    # identical for every executor kind; differentiated ONLY by a monochrome kind+channel glyph.
    authority-badge: 'monochrome, escalating weight'
  action-link-email:
    primary-background: '{colors.brand-primary}'
    primary-foreground: '{colors.brand-foreground}'
    min-tap-target: '44px'
  nl-escape-hatch:
    link: '{colors.brand-primary}'
    note: 'plain FluentTextField; visually subordinate to the action buttons'
  capture-bar:
    accent: '{colors.brand-primary}'
updated: 2026-06-14
---

## Brand & Style

Works is a **work-item coordination kernel** wearing a console. Its premise is contrarian for a
task tool: *work is an obligation that burns down to zero*, and *every doer is the same kind of
thing*. A bot, a colleague, and a stranger reached only by email are all a **Party**, coordinated
through one identical surface. The visual language has exactly two jobs — make **progress feel like
a fact, not a flag**, and make **handoff feel symmetric, never branched**.

Works inherits **Fluent UI v5** wholesale through Hexalith.FrontComposer. This DESIGN.md specifies
only the brand-layer delta: a single brand color, the burn-down/roll-up visual language, a status
vocabulary, and a tabular-figure typographic role. The 80% of the surface that is Fluent
(neutrals, surfaces, elevation, inputs, the DataGrid, dialogs, the focus ring) inherits Fluent's
specs as-is. Restyling Fluent components is *against* the discipline — Fluent's defaults are the
contract, and FrontComposer generates most components from Works' domain types, not by hand.

The register is **AI-native, operational, calm-under-load**. This is a console an "agent boss"
runs a "frontier firm" from — confident and quiet, never playful. No celebration, no streaks, no
chrome that competes with the numbers.

## Colors

Two ideas carry the palette: one brand color, and a disciplined semantic vocabulary for *progress*
and *lifecycle*. Everything else is Fluent.

- **Brand Blurple (`#5B5FC7` light / `#9299F7` dark)** is the Works identity. Used on the primary
  action, the active nav item, the in-progress status, and the email "one tap" button — the places
  that say *act* or *live*. It is Fluent-native (the Teams brand family) yet distinct from Fluent's
  default communication blue, which Works reserves for the **assigned** status. `[TASTE]` — the
  shipped register is **Momentum** (this blurple); three alternatives (Communication blue, Flow
  teal, Frontier indigo) sit beside it in
  [`.working/color-themes-1.html`](.working/color-themes-1.html). The brand color is swappable in
  this one token.
- **Burn-down Green (`#107C10` light / `#54B054` dark)** fills the *Done* portion of every
  burn-down bar over a neutral **track (`#EDEBE9` / `#3B3A39`)**. Green here means "this is getting
  done," not "success badge." When the bar fills completely, **Remaining is 0** and the item is
  Completed.
- **Cost Gold (`#C19C00` / `#DCC149`)** is the *second* burn-down (Theme 5, deferred). A warm gold
  so money never reads as effort. Never used for anything but the cost meter.
- **Status vocabulary** maps the nine lifecycle states by phase, so the color tells you *where in
  life* an item is — never *how much is done* (that is the burn-down's job):
  | Phase | Status | Token | Light |
  |---|---|---|---|
  | Pre-work | Created | `status-created` | `#616161` grey |
  | Owned (push) | Assigned | `status-assigned` | `#0F6CBD` blue |
  | Pool (pull) | Queued | `status-queued` | `#008272` teal |
  | Live | InProgress | `status-inprogress` | `#5B5FC7` blurple |
  | Parked | Suspended | `status-suspended` | `#C19C00` amber |
  | Done | Completed | `status-completed` | `#107C10` green |
  | Closed | Cancelled | `status-cancelled` | `#8A8886` muted grey |
  | Refused | Rejected | `status-rejected` | `#C50F1F` red |
  | Timed out | Expired | `status-expired` | `#A4373A` desat. red |

  (The phase column is editorial framing for comprehension — the nine **Status** values are the PRD
  glossary terms; the phase names are not.)

Avoid: color-coding the **executor kind** (bot vs human vs external — they are one Party, one
treatment); using a status color to imply progress; gradients or chromatic flourish; any second
brand color.

**Load-bearing contrast** (keep at WCAG AA; verified by the ecosystem a11y specimen gate): brand
blurple as fill + text on white and on dark surface; each `status-*` token as pill text on its own
~12% tint; burn-down green and suspended amber against the neutral track. Fluent's inherited
neutrals are AA by default.

## Typography

Inherit Fluent v5's **Segoe UI Variable** ramp (title / body / caption / label) wholesale. The only
Works delta is a **metric** role for numbers that update live: `metric` (inline burn-down Remaining)
and `metric-hero` (the roll-up "one number"). Both set `font-variant-numeric: tabular-nums` so digits
hold their box as SignalR pushes updates — numbers must never reflow or jitter mid-glance. No display
sizes, no all-caps labels beyond Fluent's own.

## Layout & Spacing

Inherit Fluent v5's 4px-based spacing scale. Works adds **no** spacing tokens. Density is not a
Works decision — it is **spec-locked by FrontComposer**: a command form with ≤1 non-derivable field
renders `Inline`, 2–4 `CompactInline`, ≥5 `FullPage`; derivable fields (`TenantId`, `MessageId`,
`CorrelationId`, `UserId`, timestamps, `[DerivedFrom]`) never appear in forms. The shell is
`<FrontComposerShell>` with a left rail; the console is desktop-first inside Fluent's grid.

## Elevation & Depth

Inherit Fluent v5 elevation. Works uses depth sparingly and never as hierarchy: the **roll-up
panel** and the **focused queue/detail card** may sit one Fluent elevation step above the surface;
everything else is flat. Hierarchy comes from layout, type weight, and the burn-down — not shadow.

## Shapes

Inherit Fluent v5 corner radii (4px controls, 6–8px cards/dialogs). Status pills are the only fully
rounded shape. Email action buttons use 6px (the one place outside Fluent's renderer). No other
shape delta.

## Components

Fluent components used as-is (do not customize): `FluentButton`, `FluentDataGrid` (the FC-TBL
surface), `FluentDialog`, `FluentCard`, `FluentBadge`, `FluentTextField`, `FluentTab`,
`FluentPersona`, `FluentMenu`, `FluentMessageBar`, skeletons, the focus ring. Icons come from the
custom inline-SVG **`FcFluentIcons`** factory, never a Fluent icons NuGet.

Works brand-layer / bespoke components:

- **Burn-Down meter** — the signature. A horizontal bar: `{colors.burndown-done}` fill over
  `{colors.burndown-track}`, with the **Remaining** figure in `{typography.metric}`. Heterogeneous
  Units are **never summed** — render one bar + subtotal per Unit, each labeled. Live-updates in
  place. Never shows negative. Ref: [`mockups/key-work-item.html`](mockups/key-work-item.html).
- **Roll-Up "one number"** — `{typography.metric-hero}` for the subtree-rolled Remaining, shown
  beside own Remaining. Heterogeneous Units appear as **separate** hero subtotals (e.g. "14
  interactions" / "6 hours"), never a single fabricated total. Cost roll-up (deferred) uses
  `{colors.cost-meter}`. Ref: [`mockups/key-work-tree.html`](mockups/key-work-tree.html).
- **Status pill** — text + `FcFluentIcons` glyph + the matching `status-*` color (text at full
  token, background at a ~12% tint). The **Suspended** pill carries its await-condition inline
  ("Waiting on: reply (email)"). Never the only progress signal.
- **Party chip** — built on `FluentPersona`. **Identical for every executor kind**; a tiny
  monochrome glyph pair encodes *kind* (bot / person / external) + *channel* (MCP / CLI / chatbot /
  email). An **Authority** badge ({Read · Contribute · Coordinate · Administer}) is monochrome with
  escalating weight (carried, not enforced in v1). No color-coding by kind, ever.
- **Work-tree node** — obligation + mini Burn-Down meter + Status pill + Party chip, indented with
  Fluent disclosure. Cascade (cancel/reject/expire from a parent) renders children muted/struck
  with a "cascaded from parent" note.
- **Action-link set** — the email-as-UI action buttons; fully bespoke, lives outside the shell. Each
  button = one valid, single-use, expiring, signed domain act; `{colors.brand-primary}` primary fill,
  `{components.action-link-email.min-tap-target}` minimum tap target; a quieter **"None of these —
  answer in my words"** link beneath. Table layout, inline styles, system fonts, plain-text
  fallback. Ref: [`mockups/key-email-as-ui.html`](mockups/key-email-as-ui.html).
- **NL escape hatch** — the "None of these — answer in my words" affordance beneath the action-link
  set (email) and present in chatbot/MCP. Visually **subordinate** to the constrained-safe buttons:
  a quiet `{colors.brand-primary}` text link that opens a plain `FluentTextField`, never a button.
  The taps stay primary; free text is the fallback. Ref:
  [`mockups/key-email-as-ui.html`](mockups/key-email-as-ui.html) (State B).
- **Capture bar** — "capture in seconds": a single obligation line with `{colors.brand-primary}`
  submit; priority/due optional, everything else defaulted. Mirrors the email one-liner and the
  single chatbot sentence.
- **History timeline entry** — actor + timestamp + verbatim Raw Act, past-tense verb; one stream
  merging Domain Events and comments.
- **Cost meter** *(Theme 5, deferred)* — a second Burn-Down meter in `{colors.cost-meter}`, parallel
  to effort. Specified now so the seam is shaped; not built in v1.

## Do's and Don'ts

| Do | Don't |
|---|---|
| Show progress as a shrinking Remaining (burn-down) | Convey progress with a status pill alone |
| One identical Party chip for bot / human / external | Color-code or restyle the UI by executor kind |
| Brand blurple only for *act* / *live* (primary action, active, in-progress) | Introduce a second brand color or use blurple decoratively |
| Tabular figures for every burn-down / roll-up number | Proportional figures that jitter as numbers update live |
| Keep heterogeneous Units as separate subtotals | Sum across Units into one fabricated number |
| Inherit Fluent v5 for everything outside the brand layer | Restyle `FluentDataGrid`, dialogs, inputs, or the focus ring |
| Email: table layout, inline styles, system fonts, ≥44px taps, plain-text fallback | Web fonts, JS, external CSS, or <44px targets in email |
