# PRD Quality Review — Hexalith.Works

## Overall verdict

This is a strong, decision-ready technical PRD whose 2026-09-14 amendment is internally coherent on Burn-Down semantics, delivered-versus-target provenance, and the Story 5.5 Obligation-bound trace. The former FR-6 wording defect and Registry/Reactor sequence contradiction are resolved; one explicitly deferred Product decision about admitting multiple Await-Conditions remains before the next lifecycle-contract implementation or promotion.

## Decision-readiness — strong

The PRD states consequential choices as choices: v1 is a headless kernel rather than an end-user surface (§0, §5–§6), AuthorityLevel is carried but not read (§4.5 FR-19), cross-aggregate effects are eventual (§4.4 FR-26), and re-estimation changes the plan without rewriting cumulative Done or lifecycle Status (§3, FR-6, FR-8, FR-9). Trade-offs are explicit, especially the accepted post-termination cascade window and the decision to defer binding budgets to G8. The `[NOTE FOR PM]` callout identifies a genuine authorization tension, names an owner, and gives a revisit condition.

The `status: final` frontmatter and 2026-09-14 amendment-history row agree with the document's role as the approved product contract. G1–G8 distinguish kernel implementation, distributed acceptance, contract shipment, production ingress, and production-data admission, so “final” does not falsely imply that all operational evidence already exists.

### Findings

No material findings.

## Substance over theater — strong

The Vision's two bets—“everything is a Party” and “AI in the loop but never in the system-of-record”—are specific and falsifiable (§1). Requirements carry product-specific consequences, rejection/no-op behavior, event provenance, recovery semantics, and negative tests rather than generic scalability or security furniture. The roadmap identifies the exact v1 seams that deferred themes build on without pretending that those deferred capabilities ship now (§12).

### Findings

No material findings.

## Strategic coherence — strong

The capability set follows one thesis: a thin event-sourced coordination spine built from Work Item lifecycle, Burn-Down/Roll-Up, Saga coordination, Executor Binding, and module boundaries (§1, §4, §6). SM-1–SM-6 validate those bets through lifecycle durability, Roll-Up correctness, executor-kind neutrality, domain purity, channel-uniform handoff, and rebuild identity, while SM-C1/SM-C2 resist kernel growth and speculative machinery (§11). The 2026-09-14 rules reinforce that thesis: re-estimation changes only the target, Done remains historical progress, and `CorrectProgress` is the audited replacement/correction path.

### Findings

No material findings.

## Done-ness clarity — adequate

Most FRs define observable completion well: inputs and invariants, accepted states, emitted events, rejection/no-op outcomes, authorization relationships, concurrency behavior, and cross-referenced success signals. The amended Burn-Down contract is clear across §3 and FR-3/FR-6/FR-8/FR-9: `Remaining = max(Estimated - Done, 0)`, overrun remains visible, ReEstimate preserves Unit/Done/Status and never completes or reopens work, and only `CorrectProgress` replaces or corrects an already-recorded cumulative Done value. FR-7, FR-16, FR-26, and the addendum now consistently state `EdgeReserved → ChildCreateAuthorized`/`Creating → EdgeAttached → parent bookkeeping`, with release only from `Reserved`.

### Findings

- **[medium]** Multiple Await-Conditions still lack an admission schema (§4.1 FR-5; §4.4 FR-14–FR-15; `.memlog.md` latest assumption) — FR-5 says an item “may hold one or more Await-Conditions” and UJ-3 requires child-or-date first-match behavior, but FR-14 and `WorkItemSuspended` describe one condition; because Suspend is legal only from `InProgress`, a second Suspend cannot add another after the item becomes `Suspended`. The memlog explicitly defers the non-empty-set shape, duplicate normalization, and simultaneous-match ordering to Product before the next lifecycle-contract implementation or promotion, which makes this a controlled decision gate rather than hidden drift. *Fix:* Product must choose and record the Suspend/`WorkItemSuspended` set semantics—or remove simultaneous first-match behavior—at that named revisit point before implementation or promotion.

## Scope honesty — strong

The v1/non-v1 boundary is explicit in Non-Goals, MVP Scope, and the roadmap (§5, §6, §12). The PRD distinguishes platform production-admission baselines from deferred Theme 6 product hardening, carries assumptions inline with an indexed audit trail, and exposes unresolved budgets as named exit gates rather than vague future work. The delivered-versus-target rule in §0 is unusually clear: later requirements cannot rewrite Stories 1.1–4.9 acceptance evidence, Status, identity, or retrospective verdicts. The Await-Condition schema gap is explicitly deferred in the canonical memlog with a Product owner and a concrete revisit condition.

### Findings

No material findings.

## Downstream usability — adequate

The Glossary is strong, FR/UJ/SM IDs are stable and resolvable, the feature inventory makes non-monotonic FR-26 discoverable, and the addendum separates non-binding architecture depth from the product contract. FR-2's 4,000-character Obligation limit is cleanly traced to Story 5.5, including the rule that historical longer payloads remain replayable. The only material extraction risk is the deferred Await-Condition schema, which downstream lifecycle work must resolve at its recorded Product gate.

### Findings

No additional findings beyond the Await-Condition decision gate under Done-ness clarity.

## Shape fit — strong

The capability-spec shape fits a headless, chain-top developer product. Journeys provide orientation without persona theater; functional consequences dominate; compatibility, security provenance, recovery, and exit gates receive depth proportionate to an event-sourced distributed kernel. Architecture mechanism detail is moved to the addendum and explicitly marked non-binding.

### Findings

No material findings.

## Mechanical notes

- **[low]** Two UJs lack named protagonists (§2.3 UJ-1–UJ-2; `.memlog.md` latest assumption) — “A Hexalith builder” and “an authenticated service Party” are floating roles, unlike Ada and Mary in UJ-3/UJ-4. This is low impact because both are explicitly labeled capability scenarios for a headless kernel, and the memlog defers the choice to Product/UX before the next journey-led UX or story-generation pass. *Fix:* At that revisit point, name the builder and service persona or classify UJ-1/UJ-2 outside the UJ identifier scheme.
- FR IDs are unique and cover FR-1–FR-26; FR-26 is intentionally presented after FR-16 and is discoverable through the feature inventory. UJ-1–UJ-4 and SM-1–SM-6/SM-C1–SM-C2 are unique and contiguous within their schemes.
- The Assumptions Index roundtrips current inline assumptions, including grouped and superseded entries. Glossary usage is stable except for the explicitly deferred Await-Condition cardinality/schema decision reported above. All cited relative source paths checked in the PRD and addendum resolve.
