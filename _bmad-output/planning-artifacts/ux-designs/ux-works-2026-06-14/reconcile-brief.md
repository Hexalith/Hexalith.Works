# Reconciliation — Product Brief

Updated: 2026-09-12

The June brief remains the qualitative vision source. Product scope and behavior come from the rendered PRD/addendum except where the five accepted PRD-memlog decisions in the dedicated override reconciliation supersede them.

Source aliases:

- B: _bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/brief.md
- BA: _bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/addendum.md
- P: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md
- O: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/reconcile-prd-memlog.md

For volatile durable-attachment, delayed-leg, actor/origin, active-Handoff and CorrectProgress/reopen behavior, use O rather than restating the decisions here. The rendered PRD's older no-active-Handoff and no-Reopen text is superseded. (O:12-34)

## Qualitative vision to retain

| Brief idea | UX implication |
|---|---|
| A thin Work Item coordination kernel—not a task database or workflow-diagram engine—coordinates system/AI, internal and external doers while referencing sibling modules. (B:10-17, B:59-79) | Keep the coordination-not-content boundary and avoid task-app/BPMN framing. |
| **Everything is a Party:** one Executor Binding gives every doer one treatment. (B:19-25, B:96-108) | Preserve neutral Party presentation and one responsibility model; do not invent executor-kind workflows. |
| **Progress is a fact** and work burns down; Effort and future Cost can roll up one tree. (B:61-72, B:96-101) | Keep Burn-Down as the signature, with Cost as Theme 5 and with current completion/correction semantics from P and O. |
| **AI in the loop, never in the system of record:** Raw Act is canonical and interpretation is recomputable. (B:24-25, B:99-101) | Keep authenticated verbatim act provenance; derived translation or interpretation never replaces it. |
| Capture in seconds, no app; external one-tap advance; one operation for handoff; one rolled answer. (B:139-148) | Preserve these as horizon outcomes, not v1 shipping claims. The accepted active-Handoff override restores the handoff promise. (O:23, O:33) |
| “For the person” and “for the ecosystem” describe work captured anywhere and an agent-addressable shared queue. (B:170-183) | Preserve the human outcome and agent-era positioning without flattening the delivery horizons. |
| The synthesis—not any individual primitive—is the differentiation. (B:89-111; BA:56-62) | Keep the coherent backlog+saga+ledger story; do not claim novelty for routing, durable execution, or magic links alone. |

## June claims superseded or narrowed

| June claim | Current disposition |
|---|---|
| Builders and end users both consume the kernel directly. (B:113-125; BA:106-109) | v1 directly serves builders through a platform-hosted harness; end-user surfaces arrive by later horizon. (P:37-54, P:409-428) |
| Creation is omnichannel from day one through email, Chatbot, MCP and CLI. (B:81-87; BA:110-111) | Day one supplies channel seams. MCP/CLI are Theme 2 remainder; email/chatbot/NL are Theme 3. (P:480-487) |
| v1 contains all Themes 1 and 2. (B:150-168; BA:15-45, BA:116-117) | v1 is Theme 1 plus only the kernel subset of Theme 2. (P:21-25) |
| Works ships/stands up an Aspire host. (B:129-135, B:152-160; BA:42-45) | Works supplies the minimal EventStore domain-service host; Hexalith.Platform owns the AppHost topology. (P:378-397) |
| “Done means Remaining = 0” is the complete invariant. (B:61-65; BA:24-27) | Progress-to-zero and explicit Complete are distinct; explicit completion can preserve residual Remaining. CorrectProgress and conditional reopen are governed by O. (P:196-205; O:24, O:34) |
| A Work Item itself owns child creation/topology. (B:61-72; BA:19-31) | The Registry owns edges and Reactor coordinates eventual effects. Current durable attachment and delayed-leg rejection come from O. (P:252-306; O:20-21) |
| Effort and Cost both burn down and roll up as current functionality. (B:19-25, B:96-98, B:145-146) | v1 contains Effort only; Cost is Theme 5. (P:75-82, P:489) |
| Signed-link behavior and external email behavior arrive together. (B:81-87, B:99-104) | Email/NL interaction is Theme 3; binding, expiry, step-up and forwarding controls are Theme 6. (P:487-490) |
| Handoff and escalation are one identical binding mechanism. (B:19-21, B:73-79) | Active Handoff is a distinct accepted act that preserves Status. Escalation candidates/rungs remain Theme 4 and outside the one Executor Binding. Do not revive the rendered PRD's superseded no-active-Handoff restriction. (P:488; O:23, O:33) |
| Comment narrative and event stream can read as one stored history. (B:66-70) | Works stores Raw Acts and bounded act notes; Conversation content remains separately owned behind a correlation ID. (P:185-194, P:356-362) |

## Dropped qualitative ideas worth surfacing

- Retain the two-sided pain: people lose work to inbox/chat/shadow lists while builders stitch together task, durable-execution and approval systems. (B:34-57)
- Keep “who is on the hook for the next step” alongside Remaining; responsibility is as central as progress. (B:51-57)
- Keep push and pull as one shared backlog for human teams and AI fleets. (B:73-79, B:178-183)
- Keep the answer-contract's future triple duty—UX accelerator, validator and prompt-injection boundary—inside the Theme 3/6 horizon. (B:81-87; BA:56-62)
- Keep “start-cheap-escalate ↔ budget-degrade” as one ladder traversed in opposite directions. (BA:56-62)
- Keep the honest positioning: mature primitives, novel coherence. (B:89-111; BA:64-87)

The brief defines no web IA/default route, visual theme, configurable Works accent, iconography, accessibility target, localization policy, responsive breakpoints, keyboard model, email support matrix, or recovery copy. Current Works UX inherits the active FrontComposer accent and makes no accent API a Works dependency. (DESIGN.md:71-77, DESIGN.md:151)
