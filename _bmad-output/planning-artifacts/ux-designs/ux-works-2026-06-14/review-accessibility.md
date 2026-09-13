# Accessibility and Inclusive-Interaction Review — Works

## Overall verdict

**Strong as a WCAG 2.2 AA implementation contract.** The editorial restructuring preserved the FrontComposer accessibility inheritance and every Works-specific contract for semantics, accessible names, focus recovery, live announcements, limits, reflow, localization, and channel behavior. All nine promoted references remain subordinate visual evidence and do not contradict the two authoritative spines.

This verdict is contract readiness, not pre-approval of a future implementation. A built FrontComposer surface must still pass its inherited shell, override-diagnostic, browser/assistive-technology, contrast, and release-evidence gates. Theme 3 email remains intentionally non-implementable until its supported-client/high-contrast evidence gate passes.

Finding count: **0 critical · 0 high · 0 medium · 0 low**.

## Critical severity

No findings.

## High severity

No findings.

## Medium severity

No findings.

## Low severity

No findings.

## Centralized Theme 3 email gate — preserved

The single normative gate at `EXPERIENCE.md:248-256` retains all earlier implementation blockers and evidence requirements:

- the team must approve a named supported-email-client matrix and the high-contrast modes exercised;
- evidence must show perceivable text, links, and action boundaries, plus visible focus or selection wherever the client exposes it;
- output must use client-safe semantic markup, a complete plain-text alternative, system fonts, descriptive links with unique purposes, 44 × 44 CSS px action targets, narrow-width behavior, and 200% text sizing;
- the mock remains visual-only and Theme 3 email remains non-implementable until that evidence exists.

The gate is referenced consistently from the delivery-horizon state table, component contracts, platform rules, journey, and visual-reference catalog (`DESIGN.md:103-115,133`; `EXPERIENCE.md:115,209-221,305-311,366-379`). Theme 6 still exclusively owns production binding, single use, absolute expiry, prior-use evidence, step-up, idempotency, and fresh-link recovery. The email mock repeats the blocked status, evidence categories, plain-text parity, target size, and Theme 3/6 separation (`mockups/key-email-as-ui.html:58-110`). No requirement was weakened or lost through centralization.

## Restructured-contract regression checks

### Semantics, names, and Burn-Down edge states — resolved

The component table gives each Works composition a semantic/name contract, including native Fluent tree/grid ownership without duplicate ARIA, obligation-specific row action names and linked disabled reasons, persistent field/error associations, independently labelled History and Conversation regions, purposeful email links, and a pressed-state pause control (`EXPERIENCE.md:258-272`; `DESIGN.md:117-135`).

The Burn-Down matrix still covers determinate, unestimated, zero-estimate, overrun, non-terminal zero, progress-completed, explicit residual-completed, and corrected/reopened cases. It forbids `aria-valuemax="0"`, prevents percentage clamping, supplies value text, and leaves completion/reopen announcements to `work-status` (`EXPERIENCE.md:274-287`). DESIGN renders a bar only where that matrix allows it (`DESIGN.md:117-125`).

### Keyboard, removed-row focus, validation, and announcements — resolved

All operations remain keyboard-operable in DOM order with no positive `tabindex`. Accordion, tree, and grid detail use inherited focus behavior; failures target stable headings or linked errors. Confirmed removal and resumed batches preserve filters, selection, scroll, and valid expansion; focus returns to the corresponding control if the keyed row remains, otherwise to the row at the same visible filtered/sorted index, the nearest preceding row, and finally the labelled grid or empty-state heading. Unrelated background changes never move focus (`EXPERIENCE.md:289-296`).

Obligation's 4,000-character limit and every act-note field's 1,000-character limit remain visible and programmatically associated before entry, with bounded threshold speech and value retention across validation, authorization, domain, and infrastructure failure (`DESIGN.md:42,129`; `EXPERIENCE.md:111,206,230,268,292`; `mockups/key-capture.html:57-73`).

`pause-live-updates` still announces pause once, updates the visible associated queued count silently, exposes it on focus, and produces one deduplicated atomic summary on resume. Accepted act, attachment, resume, correction, and row-removal speech is coalesced; polling/render churn stays silent and assertive alerts remain limited to immediate blockers (`EXPERIENCE.md:116,231,272,294-296`).

### Reflow, targets, modes, and localization — resolved

The floor still requires 320 CSS px reflow and 400% zoom without clipping or overlap outside an essential labelled two-dimensional region, WCAG text-spacing overrides, 24 × 24 CSS px web targets or the spacing exception, 44 × 44 CSS px email targets, reduced-motion behavior, and forced-colors evidence. Meaning never depends on color, shape, position, or motion alone (`DESIGN.md:67-77`; `EXPERIENCE.md:298-303`).

Copy and accessible names remain resource-backed; dates, numbers, Units, currencies, and Theme 6 expiry use locale-aware formatting. External content retains known `lang`, uses `dir` and bidi isolation, and keeps verbatim Raw Acts distinct from derived translation or interpretation (`EXPERIENCE.md:235-242`).

### Journeys and non-web evidence — resolved

UJ-1 through UJ-4 and Dana's reference flow retain linked rejection/recovery, preserved input, text-only v1 output, exact command outcomes, tree/focus stability, the Theme 3 email gate, Theme 6 separation, email target/plain-text requirements, language/direction handling, and the removed-row focus fallback (`EXPERIENCE.md:319-393`). The v1 harness and future MCP/CLI references preserve lifecycle, exact Work Status, effort/Unit, freshness, actor/tenant context, and next-safe-action evidence in structured text without color dependence (`EXPERIENCE.md:305-311`; `mockups/key-v1-harness.html:46-75`; `mockups/key-mcp-cli.html:43-73`).

## Changed-mock regression checks

- **Actor-sensitive Claim:** What's next identifies the signed-in coordinator, leaves Queued Claim available, and gives each unavailable Assigned Claim a visible unique reason referenced by `aria-describedby` (`mockups/key-whats-next.html:111,148-178`). The Work Item example uses a lawful actor/history sequence and explicitly defers availability to server capability metadata (`mockups/key-work-item.html:106,133-153`; `EXPERIENCE.md:139-151,187-207`).
- **Truthful progress:** all mock progressbars have positive maxima and consistent Done/Estimated/Remaining text; Assigned rows show zero accepted progress. Unestimated work has no progressbar (`mockups/key-whats-next.html:148-178`; `mockups/key-work-item.html:121-125`).
- **Work Tree math and grouping:** the tree has an active unestimated descendant without numeric Remaining or a bar, excludes completed residual work, and reconciles to 7 interactions and 12 hours. It is the sole always-visible primary visualization, followed by one Fluent-based supporting accordion with Roll-Up first/expanded, Unavailable as its alternate state, and child evidence second (`DESIGN.md:85-91`; `EXPERIENCE.md:67-82,170-177`; `mockups/key-work-tree.html:109-194`).
- **Narrow presentation:** shell/navigation linearize, Roll-Up and evidence columns stack, and horizontal movement is confined to labelled essential tree/grid regions (`mockups/key-work-tree.html:65-94`; `mockups/key-whats-next.html:67-95,141-183`).

## Visual-reference audit

All nine promoted HTML references were reinspected: Capture, Chatbot, Email, MCP/CLI, Roadmap Operations, v1 Harness, What's next, Work Item, and Work Tree. Each contains source-level and visible visual-only/non-copyable warnings, says `DESIGN.md` and `EXPERIENCE.md` win, and denies raw HTML/CSS implementation authority (`DESIGN.md:103-115`; `mockups/key-*.html`). No mock contradicts the current domain grouping, journey, horizon, accessibility, or responsive contracts. Mock colors, sample values, raw ARIA/HTML, illustrative media queries, and controls remain non-normative.

## Blockers

No accessibility blocker prevents finalizing the UX spine pair. The Theme 3 email evidence gate is a deliberate implementation-entry blocker for that future channel, not an unresolved spine decision.
