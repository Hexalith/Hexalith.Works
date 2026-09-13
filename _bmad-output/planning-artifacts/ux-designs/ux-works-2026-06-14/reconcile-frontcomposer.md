# Reconciliation — current FrontComposer context and UX spines

Updated: 2026-09-12

This note extracts the composition contract for any future Works web Module. It does not make a web surface part of headless v1.

Source aliases:

- FC: references/Hexalith.FrontComposer/_bmad-output/project-context.md
- FD: references/Hexalith.FrontComposer/_bmad-output/planning-artifacts/ux-designs/ux-frontcomposer-2026-09-09/DESIGN.md
- FE: references/Hexalith.FrontComposer/_bmad-output/planning-artifacts/ux-designs/ux-frontcomposer-2026-09-09/EXPERIENCE.md
- WD / WE: current Works DESIGN.md / EXPERIENCE.md in this directory

The FrontComposer UX spines are draft, while FC is repository context. Works inherits their current shell/composition/accessibility contract for future web work and keeps its domain/horizon decisions in WD/WE.

## Inheritance and accent boundary

Works uses FrontComposer and Blazor Fluent UI V5 wholesale for a future web Module. It does not redefine Fluent theme, typography, spacing, radii, focus, interaction, semantic color, forced-colors, or reduced-motion behavior. FrontComposer's current Fluent package pin is 5.0.0-rc.5-26219.1. (FC:41-42, FC:143-180; WD:71-77)

**Works defines no configurable accent, color token, mode alias, or accent-placement API.** It inherits whatever accent is active in the hosting FrontComposer shell. FrontComposer currently describes a configurable default through FcShellOptions.AccentColor, while the exact inherited Fluent token/public API remains unresolved in FrontComposer itself; that provider-level question is not a Works dependency or blocker. (FD:73-84, FD:150-158; WD:71-77, WD:151; WE:232)

## Composition contract

| Concern | FrontComposer source decision | Works treatment |
|---|---|---|
| Shell / IA | Home remains at / and /home. Each bounded context gets exactly one primary Module entry; its required default Module Tab is aliased by /{module}; projection flyouts are secondary. (FE:30-61) | Any future Works web experience is one Module with route-backed tabs/subsurfaces. What's next, Work, Capture, Admin and Audit are not separate primary rail entries. Current WD/WE commit no shipping route. |
| Customization | Precedence is L4 full view → L2 projection template → generated default. L3 participates only when the chosen renderer delegates to a field slot. (FC:113-120) | Prefer generated projection/command surfaces; use L2 for documented Works compositions, L3 only by explicit delegation, and L4 only for an exceptional whole view. |
| Generated forms | Non-derivable field density is 0–1 Inline, 2–4 CompactInline, 5+ FullPage; server-controlled/derived fields never render as editable inputs. (FC:121-123) | Capture and actions expose authoritative fields/constraints and verified context without making tenant, actor, derived state, or server values editable. |
| Component bases | FrontComposer public/generated bases cover shell, page, tabs/toolbars, generated grids/forms, authorization, lifecycle, status, expandable row detail and projection health. (FD:108-140; FE:89-115) | Compose Works burn-down, roll-up, tree, Party, history and Conversation deltas over those bases. Do not create a parallel raw-component system. |
| Multi-section pages | Two or more sibling titled regions use one FluentAccordion; the primary/first region starts expanded. Shell chrome, page title, toolbars and a sole primary grid/form/detail remain outside. (FC:160-167) | Work Item detail may use one accordion for sibling regions; a grid-first queue/tree keeps its primary content visible. |
| Work Status versus command lifecycle | FrontComposer command lifecycle is Submitting, Acknowledged, Syncing, Confirmed, Rejected, IdempotentConfirmed, NeedsReview, Warning and Degraded. (FE:169-187) | Keep this separate from the nine Works resting statuses. A rejected command outcome is not Work Status Rejected, and acknowledgement is not success. |
| Projection state | Loading, Empty, Data, Stale, Reconnecting, FallbackPolling, SlowQuery, MaxItems, Reconnected, Query Error and Permission Error can combine where defined. (FE:145-167) | Preserve last committed values safely, label freshness/degradation, never render Unavailable as zero, and keep safe reads/recovery usable. |
| Focus and validation | Route, overlay, tab, grid/detail and form-error focus have explicit return/summary rules; meaningful live transitions announce once and retry/poll ticks remain silent. (FE:223-235) | Works actions, Handoff, correction, attachment, resume and roll-up updates inherit these rules without focus theft or noisy live regions. |
| Accessibility / responsive | Target WCAG 2.2 AA, semantic landmarks/relationships, 320 CSS px at 400% zoom, text-spacing tolerance, ≥24×24 CSS px targets or exception, reduced motion and forced-colors resilience. Desktop/Compact/Narrow are semantic behaviors; numeric breakpoints are not approved. (FE:237-270) | Keep state in icon/shape plus visible text, preserve domain operation at narrow widths, and use the shared breakpoint watcher. Works email has a separate future client-support gate. |
| Tenant/privacy | Tenant/user scope resolves before queries, preferences, pending state or fresh-row rendering; raw EventStore payloads, tokens, JWT data, stack traces, unrestricted PII and hidden identifiers never surface. (FE:253-260) | Fail closed, use support-safe outcome copy, and keep authorized Raw Act wording distinct from backend payload/envelope data. |

## Current Works reconciliation result

WD now inherits Fluent/FrontComposer visual roles, uses status icon plus visible label rather than a bespoke pill palette, names the supported composition bases, and defines no accent. (WD:71-100, WD:115-151)

WE now preserves Home and one future Works Module, applies the correct L4→L2→generated precedence with conditional L3 delegation, separates command lifecycle from Work Status, inherits the projection/focus/accessibility contracts, and marks every proposed web route as uncommitted. (WE:42-97, WE:220-245)

## Still open

- FrontComposer's exact public accent-setting/token API is unresolved in FD, but Works does not need to resolve or consume it; the host supplies the active accent. (FD:84, FD:158; WD:75)
- FE does not approve numeric breakpoints. Works specifies semantic Desktop/Compact/Narrow behavior only. (FE:262-270)
- A production Works Module, its default tab/route, Admin/Audit availability, and exact page-level IA remain uncommitted product decisions. FrontComposer defines how to compose them, not whether they ship. (FE:53-61; WE:42-60)
- Exact domain action availability comes from server-authoritative capability metadata. FrontComposer authorization/lifecycle wrappers define presentation and recovery, not Works actor/Handoff policy. (FE:117-145; WE:99-140)
- Email-as-UI sits outside the Blazor shell. FrontComposer does not settle supported clients, inline-email styling, 44 px email targets, signed-link recovery, or Theme 3/6 release gates.
- FD/FE remain draft; before implementation, verify their current revision and the exact generated component/API names against FC and the repository.
