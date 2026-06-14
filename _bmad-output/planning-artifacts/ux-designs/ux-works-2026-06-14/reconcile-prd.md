# Reconciliation — PRD (`prd-works-2026-06-14`)

How the PRD's UX-relevant content maps into the spines, and what was intentionally *not* carried.

## Carried into the spines

| PRD concept | Where it landed |
|---|---|
| Work Item, Obligation, Expectation, Burn-Down, Roll-Up, Work Tree, Party, Channel, AuthorityLevel, Await-Condition, Saga, Raw Act, Status (9) | Glossary terms used verbatim across both spines |
| "Everything is a Party" / handoff = one operation | DESIGN Party chip + EXPERIENCE Component Patterns / Interaction Primitives (no branch on kind) |
| "Progress is a fact, not a status flag" | DESIGN Burn-Down meter (signature) + EXPERIENCE State/Component patterns |
| "One number" roll-up + heterogeneous-Unit safety | DESIGN Roll-Up + EXPERIENCE Burn-Down/Roll-Up rules ("never summed") |
| Push + pull / "single claim wins" | EXPERIENCE Queue row + Interaction Primitives + Lost-claim state |
| "What's next?" ordering query | What's next surface (IA) |
| Comment stream + event stream = one history | History timeline entry (both spines) |
| UJ-1..UJ-4 | Key Flows 1–4 (verbatim UJ mapping) |
| Tenant isolation (mandatory) | Foundation + Accessibility Floor + permission-denied state |
| Email-as-UI / constrained-safe links / "answer in my words" / NL-is-data | Action-link set + NL escape hatch (Theme 3, designed ahead) |

## Deliberately carried *thin* (seam-shaping only — PRD SM-C2 "don't over-fit v1 to deferred themes")

- **Cost / spend governance (Theme 5)** — represented only as the deferred **Cost meter** (a second
  burn-down) so the seam exists; no spend-cap or cost-aware-scheduling UX authored.
- **Executor routing & escalation (Theme 4)** — surfaced only as the thin **Admin** surface
  (escalation ladders, authority, caps) in IA/Composition Map; no routing-decision-record UX.
- **Trust / non-repudiation (Theme 6)** — the **Audit** surface + email link single-use/expiry
  states; no auditor query UX detailed.

## Not carried (out of UX scope)

- PRD architecture **Open Questions** (identity derivation, Priority type, concurrency model, timer
  adapter, projection-rebuild, validation domains) — engineering decisions, not UX. Left to
  architecture.
- v1's headless reality (no end-user UI) — acknowledged in Foundation; this spine designs the
  *horizon*, per the agreed scope.

## Dropped qualitative ideas to flag

- None lost. The PRD's UX-bearing language is fully represented; the only compression is the
  intentional thinness of Themes 4–6, which is itself a PRD directive.
