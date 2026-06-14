# Spine Pair Review — Works

## Overall verdict

This is a **strong, source-disciplined spine pair** that a downstream consumer (architect or story-dev, human or AI) can source-extract from cleanly. Every named source resolves, the PRD glossary and UJ vocabulary are inherited verbatim, all four mockups are inline-linked, "spine wins on conflict" is stated in both files, and the Fluent-v5-inheritance posture is applied consistently (deltas only, no restated platform tokens). The contract holds. The few findings are minor cross-file parity gaps (NL escape hatch / Cost meter asymmetry between the two Components tables, one `{rounded.note}` reference that resolves to prose rather than a dimension) — none block extraction or implementation.

## 1. Flow coverage — strong

Checked: every UJ in the PRD (UJ-1..4, §2 lines 50–56) against a Key Flow with named protagonist, numbered steps, an explicit `**Climax:**` beat, and a `*Failure:*` path; plus handoff symmetry (folded into UJ-2/UJ-3 per decision log D13).

- UJ-1 (builder wires Works in) → Flow 1 "Wiring Works in (Sam, builder)" — climax + failure present.
- UJ-2 (system/AI executor on uniform surface) → Flow 2 "(Atlas, a digital coworker, over MCP)" — climax + failure (lost claim).
- UJ-3 (spawn/suspend/resume saga) → Flow 3 "(Dana, coordinator)" — climax + failure (await never matches); even covers the UJ-3 edge case (date trigger) via the Suspended state row.
- UJ-4 (email capture + external one-tap) → Flow 4 "(Mary + an emailed supplier)" — climax + failure (NL escape hatch).

Each flow carries the verbatim `— UJ-n` tag, so traceability is mechanical. Handoff symmetry is correctly treated as a primitive woven through flows rather than a fifth flow.

### Findings
- **low** UJ-4 is a *deferred* horizon (PRD line 56: "Deferred — Theme 3 horizon, not built in v1"); the flow is designed ahead of build, which is the agreed session scope and is clearly framed in the spine header. No fix needed — noting so a consumer doesn't mistake Flow 4 for v1-buildable.

## 2. Token completeness — strong

Checked: every frontmatter token, every `{path.to.token}` prose/component reference, hex presence + light/dark pairing, and the legitimacy of inherited Fluent references.

- All color tokens carry hex; brand, burn-down, cost, and all nine status tokens carry both light and `-dark` variants.
- Inherited Fluent tokens (neutrals, surfaces, elevation, focus ring, spacing, radii) are referenced by name with explicit "INHERIT / do not restate" comments — correct UI-system-inheritance pattern per the spec.
- `metric` / `metric-hero` typography deltas are fully specified with the load-bearing `tabular-nums` note.
- Prose `{colors.*}` / `{typography.*}` references all resolve to defined frontmatter keys.
- Contrast: stated as a class ("Fluent v5 is WCAG-AA by default; brand deltas contrast-verified" — EXPERIENCE Accessibility Floor) rather than per-combo ratios. Acceptable given the inheritance posture, but see finding.

### Findings
- **low** Component token `burndown-meter.radius: '{rounded.note}'` and `radius: '{rounded.note}'` reference `rounded.note`, which exists but is a prose note ("Inherit Fluent v5…"), not a dimension. The reference *resolves*, but a naive resolver would flatten a sentence into a `radius` value. *Fix:* either drop the `radius` key (radius is inherited) or point it at a concrete Fluent radius token name.
- **low** No explicit contrast ratio is stated for the load-bearing burn-down-green-on-track and status-pill-text-on-12%-tint combos. The "AA by default + verified" claim covers it at the policy level. *Fix (optional):* state the two or three load-bearing ratios numerically so a consumer needn't re-derive them.

## 3. Component coverage — adequate

Checked: every component name used anywhere has a real (non-stub) row in DESIGN.md.Components (visual) AND EXPERIENCE.md.Component Patterns (behavioral); names compared across both files.

Shared, dual-covered, name-identical: Burn-Down meter, Roll-Up "one number", Status pill, Party chip, Work-tree node, Action-link set, Capture bar, History timeline entry. All carry substantive rules in both files.

Asymmetries:

### Findings
- **medium** **NL escape hatch** has a dedicated behavioral row in EXPERIENCE.md.Component Patterns (confidence-gated auto-apply; "NL is data, never instructions") but **no dedicated row** in DESIGN.md.Components — it appears only inline inside the Action-link set entry ("a quieter 'None of these — answer in my words' link beneath"). It is arguably a sub-element of the action-link set rather than a standalone component, but a strict cross-extract finds a behavioral component with no visual peer. *Fix:* add a one-line visual note for the NL escape-hatch link in DESIGN.md.Components, or explicitly mark it a sub-element of the action-link set in EXPERIENCE.md so the asymmetry is intentional.
- **low** **Cost meter** appears in DESIGN.md.Components (deferred, Theme 5) but has **no row** in EXPERIENCE.md.Component Patterns (it surfaces only in the Heterogeneous-Units / Roll-Up rules and Composition Map). Defensible because it is unbuilt-in-v1 seam-shaping, and the deferral is labeled in DESIGN.md. *Fix (optional):* a one-line "deferred — Theme 5" stub row in EXPERIENCE.md would make the asymmetry self-documenting.
- **low** **Queue row (DataGrid / FC-TBL)** has a behavioral row in EXPERIENCE.md; in DESIGN.md it is covered as the inherited `FluentDataGrid` ("the FC-TBL surface") rather than a bespoke entry. This is legitimate Fluent inheritance, not a miss — the visual spec is "inherit Fluent." Noted for completeness.
- **low** Name qualifier drift: DESIGN.md says "Action-link set (email-as-UI)"; EXPERIENCE.md says "Action-link set". Same root name + parenthetical surface qualifier — resolves unambiguously, but exact-match tooling would flag it.

## 4. State coverage — strong

Checked: walked each IA surface (What's next, Work, Work Item detail, Capture, Admin, Audit, Email-as-UI, MCP/Chatbot) for expected states (cold-load, empty, suspended, error, offline/reconnecting, permission-denied, plus domain-specific states).

EXPERIENCE.md State Patterns covers: cold load (skeleton), empty queue, empty tree, suspended/awaiting (with inline await-condition + auto-resume), heterogeneous Units, lost claim (concurrency), reconnecting (SignalR drop, numbers freeze), permission denied (hidden not blocked — matches PRD tenant-isolation NFR), email link used/expired, cascade (cancel/reject/expire propagation), and save-failed (ProblemDetails). This set is unusually complete and each treatment cites concrete microcopy.

### Findings
- **low** No explicit state row for **Admin** and **Audit** surfaces (cold/empty/permission). They are deferred-theme surfaces (Themes 4–6) marked thin/seam-shaping in the IA and Composition Map, so this is consistent with the "don't over-fit v1 to deferred themes" PRD directive — but a consumer building those surfaces later has no state guidance. *Fix (defer-acceptable):* note in State Patterns that Admin/Audit states are deferred with the themes.
- **low** Heterogeneous-Unit handling is treated as a state and as a component rule and as a primitive (banned: implicit cross-Unit conversion) — thorough, not a gap; noted as a strength.

## 5. Visual reference coverage — strong

Checked: every file in `mockups/`, `.working/`, `imports/` is linked inline at the relevant section, named, and that "spine wins on conflict" appears once.

- `mockups/key-whats-next.html` — linked in EXPERIENCE IA composition references.
- `mockups/key-work-item.html` — linked in DESIGN Burn-Down meter + EXPERIENCE IA.
- `mockups/key-work-tree.html` — linked in DESIGN Roll-Up + EXPERIENCE IA.
- `mockups/key-email-as-ui.html` — linked in DESIGN Action-link set + EXPERIENCE IA.
- `.working/color-themes-1.html` — linked in DESIGN Colors at the `[TASTE]` brand-color note.
- `imports/` — empty; spines correctly claim no imports/orphans.

"Spine wins on conflict" is stated in both the EXPERIENCE header and the IA composition reference, and in DESIGN by reference. All four mockups verified to use the brand `#5B5FC7` and burn-down `#107C10` tokens and `tabular-nums` (email mockup correctly omits burn-down green and uses table-safe styling), so the mocks are token-consistent with DESIGN.md.

### Findings
- **low** DESIGN Colors `[TASTE]` prose names "three alternative registers (Communication blue, Flow teal, Frontier indigo)"; the linked `color-themes-1.html` contains **four** cards — Momentum (RECOMMENDED, = the chosen blurple), Communication, Flow, Frontier. The prose lists the three *alternatives* to the chosen Momentum, so the count reconciles, but a reader who opens the file sees a "Momentum" card not named in the prose. *Fix:* add "(Momentum — the recommended/shipped register)" to the prose so the linked artifact and the prose name the same set.

## Pass 2 — judgment

### 6. Bloat & overspecification — strong (lean)

No bloat. The two invented EXPERIENCE sections (Composition Map, Channel & Surface Matrix) earn their place: the Composition Map tells downstream dev which FrontComposer leverage level (L2/L3/L4/generated) each surface uses — load-bearing for an AI dev that would otherwise hand-author generated surfaces; the Channel & Surface Matrix encodes the PRD's "channel and executor are orthogonal" invariant as an act×channel grid. DESIGN.md stays disciplined to deltas and does not restate Fluent's palette. The deferred Cost meter / Admin / Audit content is deliberately thin per the PRD non-over-fit directive.

### 7. Inheritance discipline — strong

- Sources resolve: PRD (`prds/prd-works-2026-06-14/prd.md`), brief (`briefs/brief-works-2026-06-14/brief.md`), and the EXPERIENCE-only third source (`Hexalith.FrontComposer/_bmad-output/project-context.md`) all exist on disk.
- Glossary verbatim: Work Item, Obligation, Burn-Down, Roll-Up, Party, Channel, AuthorityLevel, Await-Condition, Saga, Raw Act all used exactly as PRD §3 defines them. The nine Status values (Created, Assigned, Queued, InProgress, Suspended, Completed, Cancelled, Rejected, Expired) match PRD §3/§4.2 exactly.
- UJ mapping verbatim: each Key Flow tagged `— UJ-n` matching the PRD's UJ titles.
- Microcopy attribution: phrases the spines mirror ("what's next", "one number", "single claim wins") resolve to the PRD; "one tap", "answer in my words", "capture in seconds" resolve to the named brief — all to a declared source, none invented.
- EXPERIENCE token references resolve to DESIGN tokens (e.g., amber Suspended → `status-suspended`; metric figures → `typography.metric`).

### Findings
- **low** The DESIGN.md status-phase mnemonic ("Pre-work / Owned (push) / Pool (pull) / Live / Parked / Done / Closed / Refused / Timed out") is an *invented* grouping layered over the PRD's nine statuses. It is accurate (PRD FR-6 explicitly calls Assigned the push entry and Queued the pull entry) and aids comprehension, but the phase labels themselves are not PRD vocabulary. Acceptable as editorial framing; flagged only so a consumer doesn't treat the phase names as glossary terms.

### 8. Shape fit — strong

- DESIGN.md follows the canonical section order: Brand & Style → Colors → Typography → Layout & Spacing → Elevation & Depth → Shapes → Components → Do's and Don'ts. No reorder, no missing-but-present-out-of-order section.
- EXPERIENCE.md carries the required defaults (Foundation, IA, Voice and Tone, Component Patterns, State Patterns, Interaction Primitives, Accessibility Floor, Key Flows) and the warranted triggered sections (Responsive & Platform — multi-surface; Inspiration & Anti-patterns — earns its place with concrete lifted/rejected calls).
- Invented sections (Composition Map, Channel & Surface Matrix) are justified above; both are tabular and terse, consistent with the example spines' register.
- Frontmatter is complete on both files (name, status: final, sources, updated; DESIGN adds description + ui_system).

## Mechanical notes

- **Frontmatter:** both files set `status: final` and `updated: 2026-06-14`. DESIGN.md carries `description` and `ui_system` (good — names the inherited system explicitly). EXPERIENCE.md adds a third source (FrontComposer project-context) not present in DESIGN.md; both DESIGN sources are a subset, which is fine.
- **Source addenda:** spine frontmatter cites `prd.md` and `brief.md` but not their sibling `addendum.md` files (both exist). The reconcile-prd note explicitly says it anchors on "PRD + addendum," so the addenda are de-facto inherited; consider listing them in frontmatter for an exact contract. (low)
- **Cross-refs:** all four mockup links and the color-themes link are valid relative paths that resolve to existing files. The `{rounded.note}` self-reference resolves to a key that exists but holds prose (see 2).
- **Name parity:** the only cross-file name drift is "Action-link set" vs "Action-link set (email-as-UI)" (qualifier) and the NL-escape-hatch / Cost-meter table asymmetry (see 3). No broken or dangling component names.
- **No orphans:** `imports/` empty as claimed; every visual artifact is referenced.
