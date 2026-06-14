---
name: Works
status: final
sources:
  - "{planning_artifacts}/prds/prd-works-2026-06-14/prd.md"
  - "{planning_artifacts}/prds/prd-works-2026-06-14/addendum.md"
  - "{planning_artifacts}/briefs/brief-works-2026-06-14/brief.md"
  - "{planning_artifacts}/briefs/brief-works-2026-06-14/addendum.md"
  - "{project-root}/Hexalith.FrontComposer/_bmad-output/project-context.md"
updated: 2026-06-14
---

# Works — Experience Spine

> Multi-surface coordination kernel. **v1 is a headless domain kernel** (no production channel
> adapter / end-user UI); this spine designs the **human-facing horizon (Themes 3–6)** ahead of
> build, *through* Hexalith.FrontComposer, so the kernel's seams are cut in the right places.
> `DESIGN.md` is the visual identity reference; this spine is the experience. Spine wins on conflict
> with any mock.

## Foundation

Three surface classes, one domain:

- **Web shell** — Blazor + **Fluent UI v5 (RC)** composed by **FrontComposer** (`[Projection]` →
  view + Fluxor state + live SignalR subscription + MCP Markdown resource; `[Command]` → form, with
  density spec-locked). **Desktop-first, responsive** to tablet; phone = read + simple-advance. The
  coordination console for internal users, coordinators, tenant admins, auditors, and builders.
- **Email-as-UI** — the external-party surface; **no login, one tap**. Lives outside the shell (it
  *is* email). *(Theme 3.)*
- **MCP + Chatbot** — agent / conversational surface: capture + advance via MCP tools or a single
  chatbot sentence; projections served as tenant-scoped Markdown resources. A **contract** surface,
  not visual design — but it carries the NL "answer in my words" escape hatch.

Everything is **tenant-scoped** (mandatory isolation, every layer). The design leverage on
FrontComposer is four levels — full-view override (L4) → projection template (L2) → field slots
(L3) → generated default — recorded per surface in **Composition Map** below.

## Information Architecture

Web shell (`<FrontComposerShell>`, left rail):

| Surface | Reached from | Purpose |
|---|---|---|
| **What's next** | App open (doer landing) · `g n` | My queue: assigned-to-me (push) + claimable (pull), ordered Priority → Due Date |
| **Work** | Rail · `g w` | Browse/search work items; the Work Tree + Roll-Up "one number" |
| **Work Item detail** | Queue / tree row | Obligation · Burn-Down · Schedule · Executor · Await-Condition · children · unified history |
| **Capture** | Global (`c` / palette) | "Capture in seconds" quick-create; mirrors the email one-liner & chatbot sentence |
| **Admin** | Rail (admins) | Tenant policy: escalation ladders, authority levels, spend caps *(Themes 4/5)* |
| **Audit** | Rail (auditors) | Query the signed non-repudiation record *(Theme 6)* |

External / agent surfaces: **Email-as-UI** (reached from an inbox, no nav) and **MCP/Chatbot**
(reached from an agent runtime or chat). Rail items are tenant- and authority-filtered: a surface a
Party can't use is **hidden**, not shown blocked.

→ Composition references: [`mockups/key-whats-next.html`](mockups/key-whats-next.html),
[`mockups/key-work-item.html`](mockups/key-work-item.html),
[`mockups/key-work-tree.html`](mockups/key-work-tree.html),
[`mockups/key-email-as-ui.html`](mockups/key-email-as-ui.html). Spine wins on conflict.

## Voice and Tone

Microcopy. Aesthetic posture lives in `DESIGN.md`. Factual, terse, same tone to every audience —
builder, coordinator, and emailed stranger hear the same voice.

| Do | Don't |
|---|---|
| "What's next?" | "Your tasks" / "To-do list" |
| "3 in motion · 2 awaiting · 4 to claim" | "You have 9 items!" |
| "Nothing waiting. You're clear." | "No tasks 🎉 Great job!" |
| "Remaining: 3 interactions" | "75% complete" *(progress is a count, not a percent badge)* |
| "Done." / "Burned down." | "Task completed successfully ✓" |
| "Someone else got there first." *(lost claim)* | "Error: concurrency conflict (409)" |
| "Waiting on: reply from Acme Logistics (email)" | "Status: SUSPENDED" |
| "One tap moves this forward." | "Please click the button below to proceed." |
| "None of these — answer in my words." | "Other (free text)" |
| Past-tense Raw Acts: "Acme Logistics replied: '…'" | Interpreted/editorialized history |

## Component Patterns

Behavioral. Visual specs live in `DESIGN.md.Components`. Names are identical across both spines.

| Component | Use | Behavioral rules |
|---|---|---|
| **Burn-Down meter** | Queue, detail, tree | Live-updates via SignalR as progress is reported. `Done = Remaining is 0` → status flips Completed, bar fills. Heterogeneous Units render one bar/subtotal each; **never summed**. Never negative. |
| **Roll-Up "one number"** | Work, detail (parent) | Shows own Remaining vs subtree-rolled Remaining. Recomputes live as descendants change. Per-Unit subtotals preserved up the tree; no cross-Unit conversion. |
| **Status pill** | Everywhere an item appears | One of nine statuses. Suspended shows its await-condition inline. Pill is supplementary to the burn-down, never the sole progress signal. |
| **Party chip** | Queue, detail, tree, history | Click → party detail. **Identical interaction & visual for every executor kind.** Reassign/handoff is one action — "Hand off…" — regardless of target kind (bot / human / external). |
| **Work-tree node** | Work, detail children | Expand/collapse. Carries mini Burn-Down + Status pill + Party chip. Cascade (cancel/reject/expire) visibly propagates to descendants. |
| **Queue row (DataGrid / FC-TBL)** | What's next | Two modes: **assigned-to-me** (push, shows owner) and **claimable** (pull, shows **Claim**). Claim is optimistic; **single claim wins** → on loss the row updates to claimed-by-other. |
| **Action-link set** | Email-as-UI | Each button = one valid domain act, **single-use, expiring, signed**. One tap = work progressed; no login. "None of these — answer in my words" opens free text mapped onto the item's valid action space. |
| **NL escape hatch** | Email, chatbot, MCP | Free text always accepted; mapped onto valid actions; **confidence-gated auto-apply** (Theme 3). NL is **data, never instructions** (prompt-injection boundary). |
| **Capture bar** | Global capture, email one-liner, chatbot sentence | Minimum to create = Obligation + Tenant; everything else defaulted. No multi-step wizard. |
| **History timeline entry** | Detail | Events + comments are one stream. Renders the Raw Act verbatim (actor + timestamp + payload). Filter by act type. |
| **Cost meter** *(Theme 5, deferred)* | Detail, tree | A second Burn-Down for spend, parallel to effort; same live-update + roll-up rules. Specified now to shape the seam; not built in v1. |

## State Patterns

| State | Surface | Treatment |
|---|---|---|
| Cold load | What's next, Work, detail | Fluent `Skeleton` rows matching layout; resolve on data. |
| Empty queue | What's next | "Nothing waiting. You're clear." |
| Empty tree / no children | Work, detail | "No child work yet." Single create affordance. |
| Suspended / awaiting | Anywhere | Amber Suspended pill + inline await-condition ("Waiting on: reply (email)"). Resume is automatic on first matching trigger. |
| Heterogeneous Units | Burn-Down, Roll-Up | Per-Unit subtotals side by side + caption "shown separately, never summed." |
| Lost claim (concurrency) | What's next | Row updates to claimed-by-other: "Someone else got there first." No modal. |
| Reconnecting (SignalR drop) | Global | Fluent `MessageBar` once: "Reconnecting…"; numbers freeze, then refresh on reconnect. No data loss (server-side authority). |
| Permission denied (tenant / authority) | Rail + any surface | Surface **hidden**, not a blocked screen. Query-side result filtering — cross-tenant data is never rendered. |
| Email link used / expired | Email-as-UI | "This link has been used." / "This link expired. Open Works to continue." *(Theme 6.)* |
| Cascade | Work tree, detail children | Descendants of a cancelled/rejected/expired parent render muted + "cascaded from parent." |
| Save failed | Detail, capture | Fluent `MessageBar` (intent error), plain ProblemDetails text; input retained; retry on next action. |
| Admin / Audit states | Admin, Audit | Cold-load / empty / permission states **deferred with their themes** (4–6); intentionally unspecified in v1 ("don't over-fit to deferred themes"). |

## Interaction Primitives

- **Push & pull coexist** — assign to a specific Party (push) *and* claim from a shared queue
  (pull); single claim wins.
- **Handoff = one operation** — reassigning to a bot, a colleague, or an emailed stranger is the
  same symmetric action. No branch on executor kind anywhere in the UI.
- **Capture in seconds, no app** — global quick-capture (`c` / command palette), a one-line email,
  or a single chatbot sentence — all converge on one Work Item.
- **One tap = work progressed** — the email action-links advance work with no login.
- **NL always accepted** — "answer in my words" everywhere a Party can respond; confidence-gated
  auto-apply; NL is data, never instructions.
- **Real-time by default** — projections live-update via SignalR; no manual refresh.
- **Keyboard** — command palette for capture/navigate; `g n` / `g w` go to surfaces.
- **Banned:** implicit cross-Unit conversion; branching UI on executor kind; treating NL as
  instructions; "are you sure?" modals on trusted single acts; status-flag-only progress;
  speculative routing/cost/security UI beyond the named seams in v1.

## Accessibility Floor

Behavioral. Visual contrast lives in `DESIGN.md` (Fluent v5 is WCAG-AA by default; brand deltas
contrast-verified). Inherits the ecosystem **a11y/visual specimen gate** (Playwright
`npm run test:a11y`).

- WCAG 2.2 AA across the web shell.
- **Progress is never color-only** — the Burn-Down conveys state by number + bar length + text
  label together; screen readers announce "Remaining 3 of 8 interactions."
- SignalR updates announced via `aria-live` (polite) so live changes reach assistive tech without
  stealing focus.
- **Tenant isolation reflected in the UI** — assistive tech never reaches cross-tenant data
  (hidden, not just visually suppressed).
- `Tab` order matches reading order; `Esc` closes the topmost overlay; focus ring inherits Fluent's
  AA-contrast `ring`.
- **Email-as-UI**: every action link labeled with the act it performs; ≥44px targets; plain-text
  fallback carries the same actions as URLs.

## Composition Map (FrontComposer)

Where the design effort lands per surface — so downstream dev knows what is generated vs authored.

| Surface | FrontComposer mechanism | Bespoke effort |
|---|---|---|
| Capture | `[Command]` form (Inline/Compact by density) | Capture bar styling; one-liner & sentence parity |
| What's next queue | `[Projection]` → `FluentDataGrid` + **L2 projection template** | Burn-Down + Party chip + Status pill as **L3 field slots**; push/pull tabs; claim action |
| Work Item detail | `[Projection]` → **L2 template** | Burn-Down hero, history timeline, children roll-up as **L3 slots**; "Hand off…" command |
| Work Tree / Roll-Up | `[Projection]` → **L4 full-view override** | Tree + heterogeneous-Unit roll-up is genuinely bespoke |
| Email-as-UI | **Fully bespoke**, outside the shell | Action-link set + NL escape hatch; email-client-safe render *(Theme 3)* |
| Admin (policy) | `[Command]` forms (generated) | Thin — seam-shaping only *(Themes 4/5)* |
| Audit | `[Projection]` + query (generated) | Thin — seam-shaping only *(Theme 6)* |
| MCP / Chatbot | Generated MCP tools + Markdown resources | NL escape hatch contract; server injects `TenantId`/`UserId`/IDs |

## Channel & Surface Matrix

Channel and Executor are **orthogonal** — a Party may change Channel mid-work. Which acts each
channel supports (✔ = supported, — = not this channel):

| Act | Web shell | Email | MCP | Chatbot |
|---|---|---|---|---|
| Create (capture) | ✔ | ✔ (one-liner) | ✔ | ✔ (one sentence) |
| Claim from queue | ✔ | — | ✔ | ✔ |
| Report progress | ✔ | ✔ (one tap) | ✔ | ✔ |
| Complete | ✔ | ✔ (one tap) | ✔ | ✔ |
| Hand off / reassign | ✔ | — | ✔ | ✔ |
| Answer in NL | ✔ | ✔ | ✔ | ✔ |
| Browse tree / roll-up | ✔ | — | ✔ (Markdown) | — |
| Configure policy | ✔ (Admin) | — | — | — |

## Inspiration & Anti-patterns

- **Lifted from Linear/Height:** the keyboard-first queue and command-palette capture; status
  vocabulary as a small fixed set.
- **Lifted from agile burndown charts:** progress as Remaining-to-zero, made a first-class object
  rather than a report.
- **Lifted from Fluent UI v5:** the entire surface vocabulary. Works' brand is *what it adds to
  Fluent*, not a from-scratch system — a deliberate posture.
- **Rejected — BPMN / workflow-diagram authoring:** Works is not a process-diagram engine; there
  are no authored diagrams (PRD non-goal).
- **Rejected — storing the *content* of work:** Works coordinates work, it is not the system of
  record for the work's artifacts (PRD non-goal).
- **Rejected — AI in the system of record:** interpretations are Projections; the record holds Raw
  Acts only.
- **Rejected — per-executor-kind UI:** three different surfaces for bot / human / external is the
  exact thing "everything is a Party" exists to kill.
- **Rejected — status-flag-only progress:** a status chip without a burn-down hides whether work is
  actually moving.

## Responsive & Platform

| Surface | Behavior |
|---|---|
| Web shell `≥ lg` | Rail visible; What's next + detail are two-column; tree fully expanded. |
| Web shell `md` | Rail collapses to icons; columns stack. |
| Web shell `< md` (phone) | Rail → Fluent overlay; **read + simple-advance** only (claim, report, complete). Authoring (handoff, policy) defers to desktop. |
| Email | **Mobile-first**, email-client-safe (table layout, inline styles, system fonts, ≥44px taps). |
| MCP / Chatbot | Text / Markdown; no layout. |

## Key Flows

### Flow 1 — Wiring Works in (Sam, builder, integrating a module) — UJ-1

1. Sam annotates a domain command `AssignWorkItem` and a `WorkItemSummary` projection in his module.
2. FrontComposer generates the form (3 fields → `CompactInline`) and the queue view; `TenantId`/IDs
   are excluded automatically.
3. He runs the Aspire test host; the generated What's next queue renders his work items live.
4. **Climax:** Sam never wrote a screen. His domain types *became* the UI — the queue, the form, and
   an MCP tool — and the burn-down already works because he expressed Effort, not a status flag.
5. *Failure:* a density/contract mismatch → the **dev-only customization diagnostic panel** (DEBUG +
   Development) flags it; production never renders the panel.

### Flow 2 — An agent burns down work on the uniform surface (Atlas, a digital coworker, over MCP) — UJ-2

1. Atlas calls `tools/list`; the queue projection is a tenant-scoped Markdown resource. It claims
   "Verify tax ID — Acme" from the **claimable** pool.
2. It reports progress (1 of 2 interactions → 2 of 2); the Burn-Down meter on Dana's human console
   updates live via SignalR.
3. Remaining hits 0 → status flips **Completed**; the parent Roll-Up "one number" drops.
4. **Climax:** Atlas used the *same* queue, claim, and burn-down a human uses — Dana watches a bot
   and a colleague advance adjacent rows with zero branching, one Party chip treatment for both.
5. *Failure:* Atlas tries to claim a row a human already took → "Someone else got there first."
   (single claim wins); it moves to the next claimable row.

### Flow 3 — Spawn, suspend, resume (Dana, coordinator) — UJ-3

1. On "Onboard Acme as a supplier," Dana spawns a child "Collect insurance certificate" and assigns
   it to Acme Logistics via email.
2. The parent **suspends** on an await-condition (reply received); its pill goes amber, "Waiting on:
   reply from Acme Logistics (email)."
3. Acme replies; the matching trigger fires; the saga **resumes** automatically.
4. **Climax:** Dana did nothing to resume it — the parent woke itself on the first matching trigger,
   the Roll-Up recomputed live, and the history shows "Resumed — reply received" as a Raw Act.
5. *Failure:* the await never matches → the item stays visibly **Suspended** with its condition, not
   silently stuck; Dana can reschedule or cancel (cascading to descendants).

### Flow 4 — Email capture and external one-tap (Mary + an emailed supplier, no login) — UJ-4

1. Mary captures a to-do in **one line of email**: "Confirm Q3 delivery dates with Acme." Works
   creates the Work Item, tenant-scoped, from the one-liner.
2. Works emails Acme Logistics — a Party it only reaches by inbox. The email **is** the UI: a
   read-only summary + action buttons "Confirm Aug 14 / Propose another date / Can't fulfill."
3. The supplier taps **"Confirm Aug 14"** on their phone — **no login**. One tap = work progressed;
   the signed single-use link advances the item and expires.
4. **Climax:** Mary sees "Acme Logistics replied: 'Aug 14 works'" in her queue, the Burn-Down drops,
   and neither side ever logged into a task app. Capture and advance both happened from the inbox.
5. *Failure:* the supplier wants different terms → taps **"None of these — answer in my words,"**
   types "We can do Aug 16," and the NL is mapped onto the valid action space (Propose-date) — as
   **data**, never executed as an instruction.
