---
project: works
date: 2026-06-15
workflow: bmad-correct-course
mode: Batch
status: approved-and-applied
triggering_artifact: _bmad-output/planning-artifacts/implementation-readiness-report-2026-06-15.md
primary_artifact_to_update: _bmad-output/planning-artifacts/epics.md
scope_classification: Minor
approved_by: Administrator
approved_at: 2026-06-15
---

# Sprint Change Proposal: Implementation Readiness Story Clarifications

## 1. Issue Summary

The implementation readiness assessment completed on 2026-06-15 with overall status
`NEEDS WORK`. It found no critical blockers: PRD coverage is complete, all 25 PRD FRs are
covered by epics, UX is documented and aligned, and architecture supports the v1 headless-kernel
scope.

The trigger is story execution ambiguity in `_bmad-output/planning-artifacts/epics.md`, not a
product-scope or architecture failure.

Evidence from the readiness report:

- Major issue 1: Story 3.6 and Story 4.6 split cascade/recovery responsibility ambiguously.
- Major issue 2: several acceptance criteria allow multiple valid implementation outcomes instead
  of pinning a concrete policy.
- Medium/minor concerns: preserve v1/deferred UX scope, optionally improve Epic 4 naming, keep Story
  1.1 bounded, and keep validation-heavy stories focused on executable proof.

## 2. Impact Analysis

### Epic Impact

Epic 1 remains valid. Story 1.1 needs a scope-control acceptance criterion so scaffold work does not
creep into domain behavior.

Epic 2 remains valid. Story 2.1 should own a named lifecycle transition matrix artifact, and Story
2.5 should reference that artifact while pinning terminal-command behavior.

Epic 3 remains valid. Story 3.4 and Story 3.5 need concrete policy outcomes. Story 3.6 should be
narrowed to domain-level cascade semantics, idempotent target commands, tenant-safe traversal, and
pure mechanical command intents.

Epic 4 remains valid. Story 4.6 should explicitly own runtime checkpointing, restart replay,
reminder reconciliation, and Aspire proof for recovery. Epic 4 title wording can be softened from
"Runtime Proof" to "Builder Runtime Validation" without changing scope.

No epic order or priority change is required.

### Story Impact

Stories requiring edits:

- Story 1.1: add scope guard for scaffold-only work.
- Story 2.1: create and reference `docs/lifecycle-transition-matrix.md`.
- Story 2.5: pin terminal command outcomes.
- Story 3.4: split invalid command Unit handling from projection poison/fail-closed handling.
- Story 3.5: pin first-match Await-Condition behavior.
- Story 3.6: remove runtime checkpoint/restart ownership from this story.
- Story 4.2: pin terminal-state assignment behavior.
- Story 4.6: explicitly own recovery runtime, checkpoint replay, and Aspire validation.

### Artifact Conflicts

PRD: no update required. The PRD already supports first-match Await-Conditions, idempotent resume,
cancel/expire cascade to active descendants, terminal states, headless v1 scope, and deferred
Themes 3-6.

Architecture: no update required. The architecture already states that the reactor lives outside
the kernel, is mechanical, and owns runtime delivery/checkpoint/reminder concerns outside
`Contracts`/`Server`/`Projections`.

UX: no update required. UX remains future-facing and aligned with the v1 headless scope. The proposal
reinforces that UI/channel/email/MCP/Admin/Audit/routing/cost/security work remains deferred except
for v1 read-model and notification seams already in the epics.

Sprint status: no update required unless story IDs are added, removed, renumbered, or status fields
exist elsewhere. This proposal changes story text only.

### Technical Impact

No implementation code is affected yet. The eventual story edits will reduce implementation risk by
making lifecycle outcomes, resume idempotency, Unit validation, and cascade recovery boundaries
testable before code starts.

## 3. Recommended Approach

Recommended path: Direct Adjustment.

Rationale:

- The PRD and architecture do not need reopening.
- The required changes are targeted story edits in the existing epic structure.
- No rollback is applicable because this is pre-implementation planning.
- MVP scope remains unchanged.
- Risk is low if the edits are applied before the affected stories are implemented.

Effort estimate: Low.

Timeline impact: low. The proposed edits should be quick and should avoid rework during Stories
2.1, 2.5, 3.4, 3.5, 3.6, 4.2, and 4.6.

Risk assessment: low. The main residual risk is over-specifying runtime mechanics before the live
EventStore API surface is verified in Story 1.1. The proposal avoids that by pinning responsibilities
and observable outcomes, not concrete storage implementation details.

## 4. Detailed Change Proposals

All changes target `_bmad-output/planning-artifacts/epics.md`.

### Proposal A: Rename Epic 4 for User-Outcome Framing

Section: Epic List and Epic 4 heading.

OLD:

```markdown
### Epic 4: Shared Work Execution and Runtime Proof
...
## Epic 4: Shared Work Execution and Runtime Proof
```

NEW:

```markdown
### Epic 4: Shared Work Execution and Builder Runtime Validation
...
## Epic 4: Shared Work Execution and Builder Runtime Validation
```

Rationale: preserves FR-24/FR-25 builder value while reducing technical-milestone wording.

### Proposal B: Keep Story 1.1 Bounded to Scaffold and Verification

Story: 1.1 Set Up Initial Project from Starter Template.

Section: Acceptance Criteria.

ADD after the baseline build/test criterion:

```markdown
**Given** Story 1.1 is complete
**When** implemented scope is reviewed
**Then** it contains scaffold, build configuration, dependency boundaries, baseline build/test proof,
and live EventStore API-surface verification only
**And** Work Item lifecycle, burn-down, roll-up, suspend/resume, executor-binding, and reactor
runtime behavior remain in their later stories.
```

Rationale: Story 1.1 is broad by necessity, but this makes the stopping point explicit.

### Proposal C: Make Story 2.1 Own the Lifecycle Transition Matrix

Story: 2.1 Define the Lifecycle State Machine.

Section: Terminal-state acceptance criterion.

OLD:

```markdown
**Given** a Work Item in any terminal status
**When** a further lifecycle command is handled
**Then** no transition out of `Completed`, `Cancelled`, non-requeuable `Rejected`, or `Expired` is accepted
**And** the result is a domain rejection or no-op according to the transition matrix.
```

NEW:

```markdown
**Given** a Work Item in any terminal status
**When** a further lifecycle command is handled
**Then** no transition out of `Completed`, `Cancelled`, non-requeuable `Rejected`, or `Expired` is accepted
**And** non-idempotent lifecycle commands emit an `IRejectionEvent`
**And** only exact duplicate terminal commands explicitly listed in `docs/lifecycle-transition-matrix.md`
return `DomainResult.NoOp`.
```

ADD before the lifecycle implementation test criterion:

```markdown
**Given** lifecycle rules are defined
**When** Story 2.1 is complete
**Then** `docs/lifecycle-transition-matrix.md` exists and enumerates accepted, rejected, and
idempotent no-op outcomes for each command across all 9 statuses
**And** later lifecycle stories reference this artifact rather than choosing behavior locally.
```

Rationale: the readiness report accepts a named policy artifact if the same story creates it. This
pins the default behavior while allowing deliberate idempotent no-op cases to be listed.

### Proposal D: Pin Story 2.5 Terminal Outcomes

Story: 2.5 Complete, Cancel, Reject, and Expire Work.

Section: first acceptance criterion.

OLD:

```markdown
**Given** an estimated Work Item reaches Remaining zero through progress
**When** state is replayed
**Then** `WorkItemCompleted` makes the item terminal
**And** later progress, schedule, assignment, or suspend commands are rejected or no-op according to the transition matrix.
```

NEW:

```markdown
**Given** an estimated Work Item reaches Remaining zero through progress
**When** state is replayed
**Then** `WorkItemCompleted` makes the item terminal
**And** later progress, schedule, assignment, or suspend commands emit an `IRejectionEvent`
**And** exact duplicate completion or terminal commands return `DomainResult.NoOp` only where
`docs/lifecycle-transition-matrix.md` explicitly lists them as idempotent.
```

Rationale: normal post-terminal mutation attempts are rejected. Idempotent no-op is reserved for
explicit duplicate terminal cases listed in the transition matrix.

### Proposal E: Split Invalid Unit Command Handling from Projection Fail-Closed Handling

Story: 3.4 Preserve Heterogeneous Unit Subtotals.

Section: incompatible Unit acceptance criterion.

OLD:

```markdown
**Given** a child event carries a Unit incompatible with the child's established Unit
**When** the event is handled or projected
**Then** the invalid command is rejected before projection or the projection fails closed according to the contract
**And** no mixed-unit corruption is hidden.
```

NEW:

```markdown
**Given** a progress or re-estimate command carries a Unit incompatible with the child's established Unit
**When** the command is handled
**Then** the command is rejected before event emission
**And** no Roll-Up projection update is produced from that invalid act.

**Given** replay or delivery exposes an already-persisted child event whose Unit violates the child's established Unit contract
**When** the Roll-Up projection processes the event
**Then** the projection fails closed by refusing the incompatible contribution, retaining the last valid projected value or marking that Work Item projection degraded
**And** logs include only tenant, work item, event type, and sequence metadata, never payload values
**And** no mixed-unit Roll-Up view is published as fresh.
```

Rationale: command validation and projection poison handling are different failure domains. This
change makes each testable and prevents silent corruption.

### Proposal F: Pin First-Match Await-Condition Behavior

Story: 3.5 Suspend and Resume on Await-Conditions.

Section: matching resume acceptance criterion.

OLD:

```markdown
**Given** a resume command carries a correlation key matching one current Await-Condition
**When** `ResumeWorkItem` is handled
**Then** `WorkItemResumed` is emitted
**And** the item transitions back to `InProgress`
**And** unmatched Await-Conditions are cleared or retained according to the documented first-match policy.
```

NEW:

```markdown
**Given** a resume command carries a correlation key matching one current Await-Condition
**When** `ResumeWorkItem` is handled
**Then** `WorkItemResumed` is emitted with the consumed Await-Condition key
**And** the item transitions back to `InProgress`
**And** all Await-Conditions from that suspension are cleared.
```

Section: non-matching and duplicate resume acceptance criterion.

OLD:

```markdown
**Given** a resume command carries no matching key
**When** `ResumeWorkItem` is handled
**Then** the command is rejected or treated as an idempotent no-op according to whether the key was already consumed
**And** duplicate triggers do not emit duplicate resume events.
```

NEW:

```markdown
**Given** a `ResumeWorkItem` command carries no key matching the current Await-Condition set while the item is `Suspended`
**When** the command is handled
**Then** the command emits a domain rejection
**And** the item remains `Suspended`.

**Given** a `ResumeWorkItem` command repeats the consumed key from the accepted `WorkItemResumed` event
**When** the duplicate command is handled after the item has already resumed
**Then** the command returns `DomainResult.NoOp`
**And** no duplicate `WorkItemResumed` event is emitted.
```

Rationale: "first match" now has a concrete policy: first accepted trigger resumes the item and
clears the suspension's full Await-Condition set. Duplicate consumed-key delivery is idempotent;
unmatched keys are rejected.

### Proposal G: Narrow Story 3.6 to Domain Cascade Semantics and Pure Intent

Story: 3.6 Cascade Terminal Work Through Active Descendants.

Section: checkpoint retry acceptance criterion.

OLD:

```markdown
**Given** cascade command emission is retried after a failure
**When** the cascade process resumes from a checkpoint
**Then** target commands remain idempotent
**And** the cascade uses a re-readable projection of descendants still requiring termination, not an in-memory list.
```

NEW:

```markdown
**Given** cascade terminal commands are delivered more than once to the same descendant
**When** the descendant aggregate handles a duplicate cancel or expire command for the already-applied terminal outcome
**Then** the command is idempotent according to `docs/lifecycle-transition-matrix.md`
**And** no duplicate terminal event is emitted.
```

ADD after the pure reactor translation criterion:

```markdown
**Given** Story 3.6 scope is reviewed
**When** cascade ownership is checked
**Then** the story covers aggregate transition behavior, idempotent target commands, tenant-safe descendant selection contracts, and pure mechanical command intents
**And** it does not implement Dapr dispatch, checkpoint persistence, AppHost restart recovery, reminder reconciliation, or Aspire recovery proof.
```

Rationale: Story 3.6 should not own runtime checkpointing or restart proof. It creates the domain and
pure-intent behavior that Story 4.6 proves under runtime conditions.

### Proposal H: Pin Terminal Assignment Behavior in Story 4.2

Story: 4.2 Assign, Reassign, and Hand Off Work.

Section: terminal assignment acceptance criterion.

OLD:

```markdown
**Given** assignment is attempted from a terminal state
**When** the command is handled
**Then** the command is rejected or no-ops according to the transition matrix
**And** no binding mutation occurs after terminal closure.
```

NEW:

```markdown
**Given** assignment is attempted from a terminal state
**When** the command is handled
**Then** the command emits an `IRejectionEvent`
**And** no binding mutation occurs after terminal closure.
```

Rationale: assignment after terminal closure is not an idempotent duplicate terminal act; it should
be rejected.

### Proposal I: Make Story 4.6 Own Runtime Recovery and Aspire Proof

Story: 4.6 Prove Reminder and Reactor Recovery.

Section: reactor restart acceptance criterion.

OLD:

```markdown
**Given** the reactor restarts during cascade processing
**When** checkpoint replay resumes
**Then** outstanding descendants still requiring termination are discovered from a re-readable projection
**And** already-terminal descendants are not terminated again.
```

NEW:

```markdown
**Given** Story 3.6 provides pure cascade command intents and idempotent target commands
**When** the reactor runtime dispatches cascade commands
**Then** Story 4.6 owns at-least-once dispatch, checkpoint persistence, checkpoint replay, and AppHost restart proof
**And** checkpoint state is persisted after each target command attempt or at a documented safe boundary.

**Given** the reactor restarts during cascade processing
**When** checkpoint replay resumes under Aspire
**Then** outstanding descendants still requiring termination are discovered from a re-readable projection
**And** already-terminal descendants are not terminated again
**And** the test proves convergence after a mid-cascade restart without adding clock, Dapr, or infrastructure dependencies to the kernel.
```

Rationale: this explicitly moves runtime ownership to Story 4.6 and ties it to the Aspire proof
rather than the domain cascade story.

## 5. Checklist Execution Notes

| Checklist Item | Status | Notes |
| --- | --- | --- |
| 1.1 Triggering story | Done | Trigger is readiness report; affected stories are 1.1, 2.1, 2.5, 3.4, 3.5, 3.6, 4.2, 4.6. |
| 1.2 Core problem | Done | Story acceptance criteria contain ambiguous outcomes and overlapping cascade/recovery ownership. |
| 1.3 Evidence | Done | Evidence captured from readiness report and direct `epics.md` story text. |
| 2.1 Current epic impact | Done | Epics remain valid; story text changes required. |
| 2.2 Epic-level changes | Done | Optional Epic 4 title rename; no new/deleted epics. |
| 2.3 Remaining epics | Done | Epic 1 scope guard; Epic 2 matrix ownership; Epic 3 cascade boundary; Epic 4 runtime ownership. |
| 2.4 New or obsolete epics | N/A | No new or obsolete epics required. |
| 2.5 Order or priority change | N/A | Existing sequence remains correct. |
| 3.1 PRD conflicts | Done | No PRD changes needed. |
| 3.2 Architecture conflicts | Done | No architecture changes needed; proposal aligns with kernel/adapter split. |
| 3.3 UX conflicts | Done | No UX changes needed; proposal preserves headless v1/deferred theme boundary. |
| 3.4 Other artifacts | Done | `docs/lifecycle-transition-matrix.md` will be created by Story 2.1 after approval. |
| 4.1 Direct adjustment | Viable | Low effort, low risk. |
| 4.2 Potential rollback | Not viable | No implemented story rollback applies. |
| 4.3 PRD MVP review | Not viable | MVP scope remains achievable. |
| 4.4 Selected approach | Done | Direct Adjustment. |
| 5.1 Issue summary | Done | See Section 1. |
| 5.2 Impact and adjustment needs | Done | See Sections 2 and 4. |
| 5.3 Recommended path | Done | See Section 3. |
| 5.4 MVP impact and action plan | Done | MVP unchanged; edit story text, then rerun readiness. |
| 5.5 Handoff plan | Done | Minor scope: Developer agent can apply story edits after approval. |
| 6.1 Checklist completion | Done | All applicable analysis items addressed. |
| 6.2 Proposal accuracy | Done | Proposal is tied to readiness findings and direct artifact text. |
| 6.3 User approval | Done | Approved by Administrator on 2026-06-15; approved edits applied to `epics.md`. |
| 6.4 Sprint status update | N/A | No story IDs/status entries change. |
| 6.5 Next steps | Done | Re-run implementation readiness or epic-quality review after the story artifact update. |

## 6. Implementation Handoff

Scope classification: Minor.

Route to: Developer agent for direct planning-artifact edits after user approval.

Responsibilities:

- Apply approved edits to `_bmad-output/planning-artifacts/epics.md`.
- Do not change PRD, architecture, or UX unless a later review finds new conflicts.
- Do not create UI, MCP, chatbot, email, routing, cost, Admin, Audit, or security-hardening v1
  scope from deferred UX material.
- Re-run the implementation readiness check, or at minimum the epic-quality review, after edits.

Success criteria:

- Story 3.6 clearly owns domain cascade semantics and pure mechanical command intent only.
- Story 4.6 clearly owns runtime checkpoint/restart/reminder recovery proof under Aspire.
- Story 2.1 creates `docs/lifecycle-transition-matrix.md`.
- Ambiguous "or" acceptance criteria are replaced with concrete outcomes.
- MVP scope remains unchanged and v1 stays headless.

## 7. Approval Record

Approved by Administrator on 2026-06-15 and applied to `_bmad-output/planning-artifacts/epics.md`.

Applied changes:

- Renamed Epic 4 to "Shared Work Execution and Builder Runtime Validation".
- Added a Story 1.1 scaffold scope guard.
- Made Story 2.1 own `docs/lifecycle-transition-matrix.md`.
- Pinned terminal command, Unit validation, Await-Condition, and terminal assignment outcomes.
- Split Story 3.6 domain cascade intent from Story 4.6 runtime recovery proof.

Next step: re-run implementation readiness, or at minimum the epic-quality review portion, before
starting implementation stories.
