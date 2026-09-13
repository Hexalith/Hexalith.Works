# Hexalith / FrontComposer Conformance Review

## Overall verdict

**Conforms.** The editorially restructured Works spine pair and all nine promoted visual
references retain the Hexalith UI baseline, current FrontComposer composition contract, and
verified component APIs. The merged surface/composition table preserves shell IA,
customization precedence, the one-accordion discipline, and the corrected Work-page anatomy.

Counts: **0 critical · 0 high · 0 medium · 0 low**.

## Critical findings

None.

## High findings

None.

## Medium findings

None.

## Low findings

None.

## Resolved regression checks

- **Merged shell, surface, and composition contract — resolved.** The combined table preserves
  FrontComposer Home at `/` and `/home`, exactly one future Works Module entry, route-backed
  What's next and Work tabs, subordinate Work Item detail, and generated Capture routing; it
  grants no Admin/Audit entry or route. The same section states **L4 full view → L2 projection
  template → generated default**, with L3 participating only when the selected renderer delegates
  (`EXPERIENCE.md:54-82`). This matches the current FrontComposer shell and customization contracts
  (`references/Hexalith.FrontComposer/_bmad-output/planning-artifacts/ux-designs/ux-frontcomposer-2026-09-09/EXPERIENCE.md:30-61,220`;
  `references/Hexalith.FrontComposer/_bmad-output/project-context.md:113-120`).
- **Work/Tree accordion — resolved.** The Attached Work Tree is the sole always-visible primary
  visualization. One supporting `FluentAccordion` follows, with Roll-Up first and expanded,
  Unavailable as an alternate inside that item, and child-coordination evidence second and outside
  the authoritative tree (`DESIGN.md:85-91,123-127`; `EXPERIENCE.md:67-80,103-110`;
  `mockups/key-work-tree.html:109-194`). The mock has one accordion container and two items; its
  first item is expanded. This conforms to the project-wide grid-/visualization-first exception
  (`references/Hexalith.AI.Tools/hexalith-ux-instructions.md:41-51`;
  `references/Hexalith.FrontComposer/_bmad-output/project-context.md:160-167`).
- **General page-section rule — resolved.** Both spines apply one `FluentAccordion` whenever a
  page, dialog, or detail panel has two or more sibling titled content regions. Page chrome and one
  sole primary grid, form, detail, or visualization may remain outside; the sole primary region is
  never hidden (`DESIGN.md:85-91`; `EXPERIENCE.md:67-80`). Work Item detail retains one accordion,
  while generated single-form and grid-first surfaces do not gain a redundant accordion.
- **Exact component/API reuse — resolved.** Both spines retain the same 13 canonical component IDs.
  Their bases use FrontComposer or Fluent UI V5 components; no raw interactive Blazor component
  family or unsupported status/count API is authorized (`DESIGN.md:117-135`;
  `EXPERIENCE.md:99-119`). The active spines and mock inventory contain no legacy V4/FAST tokens,
  `FcDesaturatedBadge` count use, Works accent alias, or copied FrontComposer accent API.
- **Exact `FcStatusIcon` slots — resolved.** All nine resting Work Status values map only to the
  six real slots: Created/Cancelled → `Neutral`, Assigned/Queued → `Info`, InProgress → `Accent`,
  Suspended/Expired → `Warning`, Completed → `Success`, and Rejected → `Danger`. Every icon keeps an
  adjacent localized label, and command lifecycle remains separate (`DESIGN.md:137-153`;
  `EXPERIENCE.md:103-119`). This matches the real `BadgeSlot` enum and `FcStatusIcon` API/fixed icon
  table (`references/Hexalith.FrontComposer/src/Hexalith.FrontComposer.Contracts/Attributes/BadgeSlot.cs:7-24`;
  `references/Hexalith.FrontComposer/src/Hexalith.FrontComposer.Shell/Components/Badges/FcStatusIcon.razor.cs:23-48`;
  `references/Hexalith.FrontComposer/src/Hexalith.FrontComposer.Shell/Components/Badges/StatusIconTable.cs:16-24`).
- **Queued-count component — resolved.** `pause-live-updates` uses `FluentBadge` for the visible,
  button-associated queued count (`DESIGN.md:99,134`; `EXPERIENCE.md:116,231,296`). It does not
  misuse `FcDesaturatedBadge`, whose actual API is an optimistic status lifecycle wrapper
  (`references/Hexalith.FrontComposer/src/Hexalith.FrontComposer.Shell/Components/Badges/FcDesaturatedBadge.razor.cs:11-46`).
  Current FrontComposer navigation likewise renders numeric counts with `FluentBadge`
  (`references/Hexalith.FrontComposer/src/Hexalith.FrontComposer.Shell/Components/Layout/FrontComposerNavigation.razor:125-136,163-176,224-235`).
- **Fluent V5 and accent inheritance — resolved.** Works inherits FrontComposer and Fluent UI V5
  component anatomy, semantic roles, type, spacing, radii, focus, modes, and the host's active
  accent. It defines no color token, palette, accent placement, or provider API mapping; the old
  color exploration is explicitly visual-only and superseded (`DESIGN.md:67-79,97-99,155-165`;
  `EXPERIENCE.md:82,298-303`). This preserves FrontComposer's accent-as-thread ownership without
  copying its unresolved token/API question into Works.
- **Horizon and source-open boundaries — resolved.** v1 remains a headless kernel exercised through
  the platform-hosted harness. Theme 2 remainder, Themes 3–6, and an optional FrontComposer web
  Module remain explicitly partitioned (`EXPERIENCE.md:24-40,58-65`). Exact adapter syntax,
  executor-bound classification, Handoff eligibility/confirmation, CorrectProgress validation,
  durable evidence names, Released/Superseded presentation, and future routing/Admin/Audit policy
  stay source-open (`EXPERIENCE.md:61,137,151-159,198-221`). The five accepted PRD-memlog overrides
  remain the highest-priority product source (`EXPERIENCE.md:42-52`).
- **Visual-reference authority — resolved.** `DESIGN.md` inventories all nine references and limits
  them to named information grouping and density; raw HTML/CSS, controls, palettes, navigation,
  routes, lifecycle copy, and sample values cannot authorize Blazor implementation
  (`DESIGN.md:103-115`). Each of the nine `key-*.html` files contains both a source-level
  `VISUAL-ONLY / NON-COPYABLE` notice and a prominent visible warning. The warnings also preserve
  their specific boundaries: headless v1, source-open MCP/CLI syntax, uncommitted web, Theme 3/6
  gates, roadmap-not-IA, and non-contractual responsive/media values.
- **Responsive and state matrices — resolved.** The spine retains Works-specific lifecycle,
  authorization, correction/reopen, attachment, Roll-Up/freshness, email/NL, projection-health,
  focus-return, live-update, and Burn-Down edge states (`EXPERIENCE.md:121-221,258-296`). At 320 CSS
  px and 400% zoom it specifies semantic reflow with only bounded essential two-dimensional regions;
  Desktop/Compact/Narrow behavior does not invent numeric framework breakpoints
  (`EXPERIENCE.md:298-311`). Mock viewport/media values remain illustrative only.

## Audit snapshot

- Promoted mocks: **9**.
- Source-level non-copyable notices: **9/9**; visible warning banners: **9/9**.
- Canonical component IDs: **13/13** in each spine.
- Work Status rows: **9**, using exactly the **6** real `BadgeSlot` values.
- Work mock supporting accordion: **1** container; Roll-Up first/expanded; child coordination second.
- Prohibited/stale active contract hits: **0** for `FcDesaturatedBadge` count use, legacy V4/FAST
  token families, Works-owned accent aliases/API mapping, or copied Momentum styling.

## Mechanical notes

- `DESIGN.md` and `EXPERIENCE.md` remain `status: draft` as workflow staging; no Hexalith or
  FrontComposer conformance blocker remains.
- This review changed no spine, mock, reconciliation, validation, source, or code file.
