# Reconciliation — accepted PRD memlog overrides

Updated: 2026-09-12

This record isolates the five newest accepted product decisions that have not yet been rendered into `prd.md`, and maps them to required UX-spine changes.

Sources:

- `M`: `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/.memlog.md`
- `P`: `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`

## Source precedence

The memlog names `prd.md` as the intended system of record (`M:1-4`), but its latest five entries are later accepted overrides (`M:39-43`) appended after the rendered PRD was finalized (`M:37`). For each direct conflict, the latest accepted override governs this reconciliation until it is rendered into `prd.md`; the current PRD remains authoritative everywhere else. These entries are product decisions, not UX assumptions.

## Overrides and required UX implications

| Accepted entry text anchor | Governing decision | Required UX-spine treatment |
|---|---|---|
| “**keep the edge Reserved until durable child-creation evidence exists, and only then mark it Attached**” (`M:39`) | `ChildSpawned` or dispatch intent is not enough to make an edge authoritative. The observable creation sequence is **Reserved → Creating → durable child evidence → Attached**. `Creating` is the user-facing in-flight phase between an accepted reservation and durable evidence; it must not be represented as an authoritative Attached edge. | After Add child is accepted, show a distinct pending/Creating state. Do not include it in the authoritative tree, Roll-Up or cascade as Attached, and do not imply completion merely because the saga leg was dispatched. Promote it to Attached only when durable child-creation evidence is observed. Make recovery/retry visible as continued Creating rather than duplicate children. |
| “**A delayed child-spawn saga leg targeting a released or superseded reservation is deterministically rejected**” (`M:43`) | A late leg cannot create/attach a child or mutate topology. The rejection is audited and redelivery returns the same rejection; silent no-op is forbidden. | Add an explicit failed/superseded child-creation state and history entry. Remove optimistic pending rows when release/supersession becomes authoritative, surface that no child was attached, and never silently convert a late response to success. Retry must start a new valid reservation rather than revive the old one. |
| “**responsibility-bound executor acts require trusted actor provenance to match the bound Executor, and system/Reactor acts remain origin-restricted**” (`M:40`) | Authenticated tenant membership is only the baseline. Executor-responsibility acts require the authenticated actor to match the current Executor Binding. System/Reactor-only acts still require their designated trusted workload origin. AuthorityLevel remains carried, not enforced. | Separate “responsible Party” from generic tenant membership in action availability and rejection copy. Never infer actor from the binding or accept a caller-supplied actor. Present origin/actor provenance in Raw Act history. Do not use AuthorityLevel badges as authorization claims. Distinguish executor mismatch, tenant denial and invalid system origin without leaking tenant existence. |
| “**explicit auditable Handoff transition for InProgress and Suspended work that changes the Executor Binding without changing Status**” (`M:41`) | Active handoff is now a distinct product act. InProgress stays InProgress and Suspended stays Suspended; ordinary Assign/Reassign rules are otherwise unchanged. | Restore **Hand off…** on active and suspended items as its own auditable action, not an Assign/Queue workaround. Show old and new responsible Parties in history. Preserve the visible Status and, for Suspended work, its awaiting state through handoff. Do not describe Handoff as a lifecycle-state transition. |
| “**CorrectProgress act that adjusts cumulative Done without erasing history**” and “**if correction restores positive Remaining after progress-driven auto-completion, reopen the item to InProgress, while explicitly completed items remain terminal**” (`M:42`) | Progress correction is additive and auditable; ReEstimate is no longer the correction mechanism. A corrected positive Remaining reopens only an item auto-completed by ReportProgress. An explicitly completed item never reopens and continues contributing zero to Roll-Up. | Add a Correct progress action and append-only correction history; never overwrite the original report. Display corrected cumulative Done/Remaining. Distinguish completion cause in state/history: progress-completed may reopen to InProgress after correction, explicit-completed remains terminal and rolled contribution stays zero. Error and confirmation copy must make this asymmetry clear. |

## Rendered PRD drift to resolve

| Rendered PRD text | Drift from accepted override |
|---|---|
| FR-16 says `ChildSpawned` makes the edge Attached before the child's stream begins with `WorkItemCreated`. (`P:287-295`) | Superseded by `M:39`: Attached must wait for durable child-creation evidence. The UX must not use the current PRD's ChildSpawned-first attachment sequence. |
| FR-16 defines Released on parent rejection/timeout and attached-pair duplicate no-op, but does not define a superseded reservation or deterministic rejection of a delayed leg. (`P:292-295`) | Extended by `M:43`: delayed work against Released or Superseded is an audited, replay-stable rejection with no topology mutation. |
| FR-7 correctly says the actor is authenticated and system acts use trusted workload provenance. (`P:185-193`) FR-19 nevertheless says any tenant member may perform every act. (`P:333-339`) | `M:40` narrows action authorization: responsibility-bound acts require actor–binding match; system/Reactor acts remain origin-restricted. Membership-only behavior is superseded, while carried-not-enforced AuthorityLevel remains. |
| FR-6/FR-17 reject direct active reassignment and require handoff while Assigned or by requeue then Claim. (`P:157-183`, `P:314-323`) | Superseded by `M:41` only for the new explicit Handoff act on InProgress/Suspended. Ordinary Assign/Reassign transition rules remain unchanged. |
| FR-8 says over-report correction is ReEstimate and completion has no Reopen. (`P:196-206`) | Superseded by `M:42`: CorrectProgress adjusts cumulative Done; positive Remaining reopens progress-auto-completed work to InProgress, but never explicitly completed work. |

## UX details not decided by these entries

- The exact domain-event/evidence name that proves durable child creation is not stated. UX may say **Creating** but must not invent an event contract.
- The complete command list classified as “responsibility-bound executor acts,” and the exact end-user denial copy, remain to be rendered in the PRD.
- Handoff actor eligibility, confirmation policy and failure recovery are not specified beyond trusted provenance, auditability, binding change and Status preservation.
- CorrectProgress validation bounds, Unit rules, concurrency copy and whether correction needs a note are not specified here. The spine should expose these as open product details rather than infer them.
- Whether a Released versus Superseded reservation needs distinct visual treatment is undecided; both must share the no-child/no-topology-mutation outcome and audited deterministic rejection for delayed legs.

## Qualitative intent preserved

These overrides repair four defining promises rather than broaden the horizon: durable tree truth, least-privilege executor responsibility, real human⇄AI handoff during active work, and an honest append-only history that can correct progress without erasing the original act.
