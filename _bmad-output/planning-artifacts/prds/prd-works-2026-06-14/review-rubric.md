# PRD Quality Review — Hexalith.Works

## Overall verdict

This is a strategically coherent, unusually substantive technical PRD with explicit scope cuts, product bets, testable consequences, and useful counter-metrics. It is not yet safe as an unqualified implementation contract: normative sections disagree on two core state/tree behaviors, the Roll-Up contribution of explicitly completed work is ambiguous, and the addendum contradicts the PRD's security-critical actor-provenance rule.

## Decision-readiness — adequate

The PRD makes real decisions visible: the kernel-only v1 cut (§0, §5–§6), carried-but-unenforced authority (§4.5 FR-19), eventual cross-aggregate effects (§4.4 FR-26), and no active-work reassignment (§4.2 FR-6) all state what was given up. The two `[NOTE FOR PM]` callouts identify genuine tensions and include revisit conditions. However, the document labels itself `final` while mandatory platform dependencies and mutually inconsistent normative rules still prevent a decision-maker from treating it as a clean build/release authorization.

### Findings

- **[critical]** Normative Resume behavior has two answers (§4.2 FR-6 table; §4.4 FR-15) — The table is explicitly “normative” and says `InProgress × Resume = R`, but FR-15 says that immediately after a successful resume, repeating the consumed Await-Condition is an “idempotent no-op”; the item is then `InProgress`. Both cannot govern the same command. *Fix:* Change the table cell to encode the conditional duplicate no-op, or move Resume's complete state-dependent rule out of the table and make FR-15 the single normative source.
- **[high]** “Final” does not expose one release-dependency gate (§9; §13; addendum “Downstream handoff”) — Production ingress is prohibited until the platform identity baseline is live, while VAL-H07/H08/H09/H10/H12 remain open or required and several downstream artifacts are explicitly stale. The facts are disclosed, but scattered across NFR prose, residual-openness notes, and H1–H10 rather than converted into an actionable readiness decision. *Fix:* Add one v1 dependency/exit-gate table naming each blocking artifact, owner, required evidence, and whether it blocks kernel implementation, integration acceptance, or production admission.

## Substance over theater — strong

The Vision is specific enough to be falsifiable: “everything is a Party,” Raw Acts rather than AI interpretations in the system of record, and a thin event-sourced coordination kernel are concrete bets (§1). The UJs are deliberately lean for a headless developer product, NFRs mostly bind product-specific invariants, and the extensive FR consequences carry real behavior rather than template furniture. The roadmap also names seams without pretending deferred routing, channels, economics, and hardening already exist.

### Findings

No material findings.

## Strategic coherence — strong

The scope follows a clear thesis rather than reading like a backlog: aggregate/lifecycle, Roll-Up, saga coordination, executor unification, and thin-core boundaries all support the stated spine (§1, §4, §6). SM-1 through SM-6 validate lifecycle durability, Roll-Up correctness, executor-kind neutrality, purity, channel uniformity, and rebuildability; SM-C1 and SM-C2 explicitly resist kernel growth and speculative preparation (§11). The roadmap's seam mapping preserves the thesis without silently pulling Themes 3–6 into v1 (§12).

### Findings

No material findings.

## Done-ness clarity — thin

Most FRs are substantially better than ordinary requirements: they give legal states, emitted events, rejection/no-op behavior, concurrency outcomes, and observable test consequences. The dimension still falls to thin because ambiguity remains in the exact critical paths that SM-1 and SM-2 are meant to certify: child creation can apparently bypass the registry, Resume has contradictory normative outcomes, completed-work contribution is defined two ways, and several “bounded” eventual behaviors have no bound.

### Findings

- **[critical]** Public Create can bypass the Work-Tree Registry (§4.1 FR-1; §4.3 FR-13; §4.4 FR-16) — FR-1 allows a builder or Executor to create a Work Item while “optionally supplying … parent reference,” but FR-16 says the public attachment act is registry reserve and the child is created only after reservation/attachment; FR-13 says no Work Item is written for a rejected attachment. A direct Create with a parent reference bypasses the sole tree-shape authority. *Fix:* Forbid parent references on ordinary Create and allow them only on a Reactor-originated child-create command carrying registry evidence, or define an equally guarded registry-first Create flow.
- **[high]** Explicit completion leaves Roll-Up contribution underspecified (§3 “Remaining”; §4.2 FR-8; §4.3 FR-11; §11 SM-2) — FR-8 preserves Estimated and Done when an item completes explicitly above zero, while the Glossary still defines Remaining as `Estimated − Done`; FR-11 and SM-2 then calculate rolled Remaining from each item's “own Remaining,” yet also say a Completed item contributes 0. Implementers cannot tell whether the read model exposes stored Remaining plus a separate effective contribution, or rewrites the own value. *Fix:* Define two named values (for example `ReportedRemaining` and `RollUpContribution`) and use the latter in FR-11/SM-2 formulas and schemas.
- **[high]** Eventual coordination is called bounded without a testable bound (§4.4 FR-16, FR-26; §6.1; §11 SM-1) — Reservation timeout, child creation, resume, and cascade are central v1 behavior, but “bounded, platform-configured” and “eventual” supply neither maximum latency nor an acknowledgement/quiescence condition comparable to SM-2. SM-1 can therefore pass after an arbitrary wait. *Fix:* Define per-flow completion evidence and a configured acceptance bound for registry settlement, resume, and cascade, plus the behavior when the bound is exceeded.
- **[medium]** SM-5 promises a state-preserving “mid-work” channel change that active states forbid (§4.2 FR-6; §4.5 FR-17; §11 SM-5) — FR-6 rejects Assign/Reassign in `InProgress` and `Suspended`, while SM-5 says a Party changes Channel “mid-work” with no Status change. *Fix:* Scope SM-5 explicitly to `Assigned`, or add and specify an active-state binding-change act.

## Scope honesty — adequate

The PRD is candid about what v1 does not ship (§5, §6.2), distinguishes platform identity baseline from Theme 6 hardening, carries assumptions inline with a complete index, and names active-work handoff and Reopen as deferred tensions. The roadmap separates designed-for seams from v1 requirements. Its main weakness is lifecycle labeling: `status: final` obscures the difference between a final product contract and readiness for integration or production while mandatory dependencies remain open.

### Findings

- **[medium]** Provisional performance criteria sit inside a final acceptance contract (§9 Performance; §11 SM-2; §14) — The text says “No numeric targets are pinned,” then specifies a `< 200 ms` read and a `5 s` convergence bound, both marked provisional. This leaves teams free either to enforce or ignore the only numeric performance tests. *Fix:* Either adopt these values as v1 acceptance thresholds or move them to an explicitly non-gating benchmark section with an owner and date for pinning budgets.

## Downstream usability — thin

The PRD has a strong Glossary, globally unique FR/UJ/SM IDs, explicit cross-references, and a useful assumptions roundtrip. But downstream extraction is unsafe while the PRD and its architecture-facing addendum disagree about authenticated actor provenance, while the saga surface is called both a port and a command, and while the lifecycle table conflicts with FR-15.

### Findings

- **[high]** The addendum reintroduces spoofable actor provenance (addendum “Non-binding event / port sketch,” Domain Event catalog; PRD §4.2 FR-7 and §9) — The addendum says acting-Party identity and timestamp come from “the binding + EventStore envelope.” The PRD explicitly says the actor is never inferred from the Executor Binding and always comes from authenticated platform identity; responsibility and action are different facts. This is security-critical drift in the document handed to architecture. *Fix:* Replace the addendum sentence with the PRD rule: authenticated actor and timestamp come from the trusted envelope; Executor Binding never supplies actor identity.
- **[medium]** The external Resume seam is named inconsistently (§4.4 description; §6.1; addendum “Await-Condition”) — The PRD calls it a “generic external resume port,” while FR-15 and the addendum state that Resume is a command, not a port, issued by the Reactor/adapters. *Fix:* Use one term throughout—prefer “Resume command contract plus deferred external adapter”—and reserve Port for the abstractions listed in FR-22.

## Shape fit — strong

The capability-led shape fits a headless, chain-top developer product. Journeys are kept as orientation rather than inflated personas, the FRs dominate the document, compatibility and substrate constraints receive product-specific sections, and downstream technical-how is moved to the addendum. The document is long because its event-sourced contracts and cross-aggregate invariants require precision, not because a template was filled mechanically.

### Findings

No material findings.

## Mechanical notes

- **[low]** UJ-1 and UJ-2 have no named protagonists (§2.3) — They use “A Hexalith builder” and “A service Party,” while UJ-3 and UJ-4 use Ada and Mary. This is minor for a headless capability PRD, but weakens standalone extraction. *Fix:* Name the builder and service/workload persona, or relabel these two as capability scenarios rather than User Journeys.
- **[low]** FR IDs are unique and cover 1–26, but presentation order is non-monotonic (§4.4) — FR-26 appears between FR-16 and FR-17. Cross-references resolve, but range references such as “FR-14–FR-16” can cause downstream extractors to miss the Reactor requirement that makes those FRs work. *Fix:* Renumber only if IDs are not externally stable; otherwise add an explicit FR inventory mapping feature → IDs and call out FR-26 wherever range shorthand is used.
- Glossary terms are generally stable; the notable drift is singular “optional Await-Condition” (§1, §3, §4.1) versus the normative set of multiple Await-Conditions (§4.1 FR-5, §4.4 FR-15).
- FR, UJ, and SM identifiers are unique; UJ-1–UJ-4 and SM-1–SM-6/SM-C1–SM-C2 are contiguous within their schemes. No unresolved FR/UJ/SM cross-reference was found.
- The Assumptions Index roundtrips the inline `[ASSUMPTION]` tags, including grouped, superseded, and narrowed entries. The two `[NOTE FOR PM]` callouts both name an owner and revisit condition.
