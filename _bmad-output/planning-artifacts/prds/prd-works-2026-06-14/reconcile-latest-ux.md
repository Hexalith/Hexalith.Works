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

