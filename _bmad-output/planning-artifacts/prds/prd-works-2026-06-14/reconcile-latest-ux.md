# Reconciliation extract — latest UX design and validation

- Inputs: `ux-designs/ux-works-2026-06-14/DESIGN.md`, `EXPERIENCE.md`, and `validation-report.md`
- Compared with: current `prd.md`, `addendum.md`, and `.memlog.md`
- UX validation date: 2026-09-12
- Extraction purpose: requirement-level PRD update only; visual and UI implementation mechanisms are separated below

## Authority and freshness

The UX pair is not a newer product-definition source. Both documents remain dated 2026-06-14 and were derived from the pre-amendment PRD, whereas the governing PRD was materially amended through 2026-09-08. The 2026-09-12 UX validation therefore correctly judges the pair **broken as a current downstream contract**. Its drift findings are evidence for correcting the UX documents, not evidence for reverting the amended PRD.

The current PRD is already explicit that v1 is a headless domain kernel and that production Web, email, MCP, and chatbot surfaces are deferred. No UX source justifies widening v1.

## Requirement-level gaps worth carrying into the PRD

### UX-R1 — Add a future-surface accessibility and recovery floor

The PRD has strong domain and platform NFRs, but no product-level accessibility requirement for the human-facing surfaces promised in Themes 3–6. The UX validation establishes that a bare statement of WCAG 2.2 AA is insufficient because the product depends on real-time changing queues and meters, bespoke work-tree/progress presentations, action-heavy forms, and no-login email interactions.

Recommended product-level clarification, explicitly **outside v1 acceptance**: when a production human-facing surface ships, it must meet WCAG 2.2 AA and provide equivalent keyboard and assistive-technology operation, non-color state meaning, measurable zoom/reflow support, reduced-motion and forced-colors behavior, accessible validation/rejection recovery, and recoverable stale/offline/error states. Live changes must preserve focus and context, avoid noisy announcements, and allow a user to pause or otherwise control non-essential continuous updating.

This is an outcome requirement. Component roles, accessible-name formulas, focus matrices, announcement timing, test tooling, and exact responsive breakpoints belong in the UX specification or architecture/addendum, not the PRD.

### UX-R2 — Make the no-login email promise recoverable and bind its security dependency

UJ-4 promises that an external Party can advance work from email with one tap and no login. The roadmap currently places email-as-UI and magic links in Theme 3, while the minimum authority protections for a single-use, bound, expiring action link are placed in Theme 6. The UX validation also finds that the current expired-link copy sends a no-login recipient to “Open Works,” which breaks the journey.

Recommended product-level clarification: a production external action must not ship until the link is bound to the intended act/context, expiring, single-use/idempotent, safe against forwarding-as-authority, and covered by the required step-up policy. A used or expired link must offer a channel-appropriate no-login recovery path, such as requesting a replacement or replying by email, and communicate its expiry in user-understandable terms. This can be expressed as a Theme 3 → Theme 6 dependency rather than adding security machinery to v1.

The signing/token transport, URL shape, resend service, email markup, and exact timing values are architecture/addendum or UX decisions.

### UX-R3 — Keep authorization claims aligned with the enforced product horizon

The UX contract says rail items and surfaces are filtered by `AuthorityLevel`, and treats permission-denied behavior as if authority enforcement already existed. That conflicts with the accepted v1 decision in FR-19 and the memlog: `AuthorityLevel` is carried but not read, and the v1 gate is authenticated tenant membership. There is also no production web shell in v1.

Recommended product-level clarification for future surfaces: visibility and action availability must reflect server-enforced authorization, must never imply a stronger permission model than the platform enforces, and must preserve query-side tenant filtering. Do not add `AuthorityLevel` UI gating to v1. The existing FR-19 production-admission note remains authoritative until Theme 4/6 defines enforcement.

### UX-R4 — Decide localization scope before external email becomes a release commitment

The UX validation identifies an unresolved product boundary for external-party email: dates, units, names, statuses, Raw Acts, language metadata, wrapping, and bidirectional text have no declared locale/RTL scope. Because UJ-4 reaches recipients outside the authenticated shell, silently assuming English-only behavior is a product decision, not merely styling.

Recommended treatment: record a PM-owned roadmap open item to choose either an explicit English-only launch boundary or resource-backed localization, locale formatting, and bidirectional-text handling before Theme 3 email acceptance is written. Do not infer the choice from the current UX files.

## Conflicts with accepted PRD decisions — correct downstream, do not import

| UX statement | Governing PRD/memlog decision | Reconciliation |
| --- | --- | --- |
| UJ-1 generates a web form/queue/MCP tool; UJ-2 begins with production MCP tool calls. | §0, §2.2, §5, and §12: v1 is the headless kernel; MCP/CLI are Theme 2 remainder and web/email/chatbot are deferred. | Relabel these as roadmap flows or rewrite the v1 flows around the domain contract and platform-hosted harness. No v1 PRD expansion. |
| UJ-3 uses Dana, direct spawn, and an email-reply trigger. | PRD UJ-3 uses Ada’s release-checklist item, Work-Tree Registry reservation, Reactor-driven child-completion resume, and a competing date trigger. | Restore the current PRD journey verbatim and cover reserve → spawn → suspend → Reactor resume plus the date-race edge case. |
| Burn-Down copy says Remaining reaching zero unconditionally completes; the state set omits explicit completion above zero, re-estimate-to-zero without completion, unestimated items, and unavailable rebuilding subtrees. | FR-8/FR-9/FR-11: only accepted progress-to-zero auto-completes; explicit Complete may leave nonzero Burn-Down; `ReEstimated` never completes; unestimated and unavailable states are defined. | Rebuild UX states from the current completion and Roll-Up model. Do not weaken the PRD semantics. |
| “Hand off” is presented as one symmetric action in all states. | FR-6/FR-17: direct rebind is legal while `Assigned`; active `InProgress`/`Suspended` work cannot be directly reassigned, and the Theme 4 live-handoff question is deliberately deferred. | Add action availability/rejection states; retain the one command shape without claiming universal state availability. |
| Cascade presentation includes cancelled, rejected, and expired parents. | FR-10: only Cancel and Expire cascade; Reject is legal only from `Assigned` and either requeues or terminates that item. | Remove rejection cascade from the UX contract. |
| History merges Works events and comments into one Works-owned stream. | FR-7/FR-21 and the memlog: Works stores Raw Acts and a Conversation correlation ID only; it never stores conversation content or a comment thread. | Keep Works history and Conversations content as separately owned composed sources, including independent loading/error behavior. |
| Permission states are authority-filtered in the current executable IA. | FR-19: v1 has tenant-membership authorization only; `AuthorityLevel` is carried-not-enforced. | Scope authority-driven visibility to the future enforcement theme and server policy. |

## Addendum/downstream-only findings — keep out of PRD requirements

The following validated issues matter, but they are technical or visual mechanisms rather than product capabilities:

- Correct the FrontComposer source to include `references/` and bind the actual Fluent UI V5 package version.
- Replace nonexistent or avoidably bespoke component choices with supported FrontComposer/Fluent primitives; define the mandated accordion structure for multi-section detail surfaces.
- Replace literal colors, incomplete dark-mode variants, status tints, and custom type ramps with mode-aware Fluent semantic roles/tokens; verify contrast in light, dark, and forced-colors modes.
- Complete component semantics, focus/announcement matrices, responsive thresholds, token mappings, and mock-to-component mappings in the UX specification.
- Mark web mockups visual-only where they use unsafe raw controls or locally invented theme variables; preserve the email-client markup exception.
- Partition the UX contract by v1, Theme 2 remainder, and Themes 3–6; inherit mutable FrontComposer mechanics instead of copying them.

If architecture needs any of these constraints to select a platform seam, capture that mechanism in `addendum.md`; the governing product outcome should remain capability-level in `prd.md`.

## Suggested PRD application

1. Add UX-R1 as a roadmap-scoped human-surface NFR, without making it a v1 success metric.
2. Add UX-R2 as an explicit production dependency/recovery rule spanning Themes 3 and 6.
3. Preserve FR-19 and add only the future-surface clarification in UX-R3 if reviewers could otherwise read the UX contract as current enforcement.
4. Carry UX-R4 as a PM-owned open roadmap decision, not an assumption.
5. Add a downstream handoff entry requiring `DESIGN.md` and `EXPERIENCE.md` to be updated against the 2026-09-08 PRD before either is used for implementation.

## Post-update reconciliation — 2026-09-13

### Covered requirement-level findings

- The update preserves the headless-v1 boundary and labels UJ-1/UJ-2 as capability scenarios rather than widening v1 into a production UI or MCP release.
- UX-R1 is covered by §12's future human-surface quality floor: WCAG 2.2 AA, equivalent keyboard/assistive operation, non-color meaning, zoom/reflow, preference modes, recovery states, stable focus/context, and user control of non-essential live updating are now roadmap requirements outside v1 acceptance.
- UX-R2 is covered in both UJ-4 and the Theme 3 roadmap row: production no-login actions depend on Theme 6 link safeguards and must provide a no-login used/expired-link recovery path.
- UX-R3 is covered at product altitude by the normative §9 actor/relationship/query matrix and by FR-19's explicit distinction between relationship enforcement and carried-not-read `AuthorityLevel` values.
- UX-R4 is preserved as a PM-owned roadmap decision that must close before Theme 3 external-email acceptance; no localization choice was invented.
- Addendum handoff H16 correctly blocks implementation from the June UX pair until it is rebased on the 2026-09-13 contract, including the newly approved Handoff and progress-correction semantics.

### Remaining gaps and conflicts

1. **§9 duplicates the identity/trusted-origin NFR with contradictory authorization.** The retained 2026-09-08 bullet still says the minimum for all commands is authenticated tenant membership and calls `AuthorityLevel` “carried-not-enforced”; the immediately following 2026-09-13 replacement requires the relationship/origin matrix and says “carried-not-read.” Remove or explicitly supersede the old bullet so future UX cannot choose the weaker rule.
2. **FR-18 retains an over-broad claim assumption.** Its final consequence still says v1 allows “any Executor of the tenant to claim,” while the amended consequence above it, §9, and §14 distinguish self-claim of `Queued` work from bound-Executor-only claim of `Assigned` work. Rewrite the inline assumption to the exact current relationship rule.
3. **The no-login/step-up boundary is linked but not decided.** UJ-4 now requires both one-tap/no-login and Theme 6 step-up policy, but does not state which action classes remain eligible for the no-login promise and which risks force additional authentication. Keep this as an explicit Theme 3/6 product decision so downstream UX does not either weaken security or silently break the journey.
4. **The June UX pair remains materially conflicting until H16 is executed.** It still contains the wrong horizon and UJ-3 scenario, unconditional completion, stale Roll-Up states, rejection cascade, Works-owned comments, and pre-update handoff/authorization behavior. The updated PRD is authoritative; neither UX document is implementation-ready.

### Qualitative intent check

No material product-level qualitative intent was lost. The update retains the central feel of the source: progress is a fact rather than a status flag, every doer uses one binding model, Handoff is symmetric across executor kinds, and the external journey remains one-tap/no-login with recovery. The calm, factual voice; exact empty/error copy; visual hierarchy; and “quiet under load” character remain appropriately owned by `DESIGN.md`/`EXPERIENCE.md` and should be preserved when H16 rebases them.

### Downstream-only items correctly left out

The update correctly did not pull Fluent/FrontComposer component names, package pins, token/color values, accordion composition, contrast pairs, focus/announcement matrices, breakpoints, email markup, or mock-to-component mappings into the PRD. These remain UX specification work, with architecture/addendum involvement only where a platform seam must be selected.
