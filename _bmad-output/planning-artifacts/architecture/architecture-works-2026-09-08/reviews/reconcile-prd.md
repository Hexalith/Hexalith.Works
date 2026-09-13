# PRD reconciliation — Architecture Spine

## Verdict

**Targeted fixes required.** The spine carries the major September 12 correction set and all four
post-validation product overrides from the PRD memlog. It should not be rolled back to the rendered
PRD's older attachment, authorization, handoff, or completion rules. Five reconciliation issues
remain: one authorization defect, one hazardous split in the product source set, and three dropped
boundary/read-side/configuration contracts.

## Authority used

The PRD memlog is the authority for later decisions. In particular, its four final `override`
entries supersede contradictory prose still present in `prd.md` and `addendum.md`. Product behavior
that the spine can safely cite from the PRD was not treated as needing verbatim duplication; a gap is
reported only where the spine's own rule changes or fails to bind a cross-unit architectural choice,
or where a required architecture deliverable is absent.

## Findings

### R-PRD-01 — HIGH — AD-23 leaves two Executor-responsibility acts open to the wrong actor

**Evidence**

- The latest product override says every responsibility-bound Executor act requires authenticated
  actor provenance matching the current Executor (`.memlog.md:40`).
- The normative lifecycle says an `Assigned` item is claimed by its own pushed Executor
  (`prd.md:179`) and that Reject is a bound Executor declining its own assignment
  (`prd.md:180`, `prd.md:221`).
- AD-23 lists ReportProgress, CorrectProgress, Complete, Suspend, and Handoff as equality-gated, but
  omits Reject and says only that “Claim binds the acting Party”
  (`ARCHITECTURE-SPINE.md:358-360`).

**Why this matters**

As written, an implementation can allow one tenant member to reject another Party's assignment, or
to claim an already-Assigned item and overwrite its binding. Both violate the Executor semantics,
even though the broader tenant membership floor and carried-not-enforced AuthorityLevel remain
correct.

**Required reconciliation**

Amend AD-23 in place: Reject requires `acting Party == current Executor`; Claim from `Assigned`
requires the same equality; Claim from `Queued` binds the authenticated acting Party. Add those cases
to the AD-23/24 negative matrix. Keep Assign/ReEstimate/Reschedule/Cancel at the tenant-member floor
unless a later product decision narrows them.

### R-PRD-02 — HIGH — The cited product sources contradict the memlog overrides that the spine correctly adopted

**Evidence**

- Child attachment: `prd.md:292` and `addendum.md:37-42` still attach on `ChildSpawned`; the memlog
  override requires durable child-creation evidence first (`.memlog.md:39`), which AD-21 correctly
  implements with persisted `WorkItemCreated` (`ARCHITECTURE-SPINE.md:316-322`).
- Responsibility authorization: `prd.md:338` still says tenant membership is the only gate and any
  member may claim/expire; `.memlog.md:40` supersedes that, and AD-23 correctly introduces Executor
  equality and origin restriction, subject to R-PRD-01.
- Active handoff: `prd.md:181` and FR-17 still forbid direct active reassignment; `.memlog.md:41`
  adds explicit Handoff, which AD-14 correctly carries (`ARCHITECTURE-SPINE.md:203-207`).
- CorrectProgress/reopen: `prd.md:204` still says there is no Reopen and FR-8 directs corrections
  through ReEstimate; `.memlog.md:42` adds CorrectProgress and conditional reopen, which AD-07
  correctly carries (`ARCHITECTURE-SPINE.md:136-143`).

**Why this matters**

The spine frontmatter cites both `prd.md` and `.memlog.md`. A builder following the rendered PRD can
produce the exact unsafe/obsolete behavior the spine prevents. This is source ambiguity, not a reason
to weaken the spine.

**Required reconciliation**

Keep the spine rules. Flag `prd.md` and `addendum.md` for a companion PRD update that records the four
overrides in the Amendment history and normative FR text. Until then, state explicitly that the final
four memlog overrides win. Add `addendum.md` to the spine's source list if it remains a load-bearing
input after that refresh.

### R-PRD-03 — MEDIUM — FR-22/FR-23 boundary contract is represented only as a generic map row

**Evidence**

- FR-22 requires the domain-owned `IExpectationResolver` and `IExecutorRouter` ports, a shipped
  no-LLM expectation resolver, and no wired router in v1 (`prd.md:365-369`; `addendum.md:49-51`).
- FR-23 requires a tracked owns-vs-references record enumerating each sibling module and why
  (`prd.md:372-376`).
- The spine says only “FR-21..FR-23 module boundaries | Contracts + fitness tests”
  (`ARCHITECTURE-SPINE.md:504`) and labels Contracts as containing generic “boundary ports”
  (`ARCHITECTURE-SPINE.md:481`). It never names the two ports and does not enumerate Parties,
  Conversations, Tenants, Commons, and EventStore ownership/reference boundaries.

**Why this matters**

One unit can omit the deliberately unused router port, another can store resolved Expectation or
Conversation data, and a third can invent a sibling-module adapter boundary. The PRD specifically
made these seams and the boundary record v1 deliverables.

**Required reconciliation**

Add a compact ownership/reference table (or a dedicated AD) naming what Works owns and what it
references from Parties, Conversations, Tenants, Commons, and EventStore. Bind both ports to the
domain/Contracts boundary; require the no-LLM resolver and keep the router implementation unwired in
v1. This table can satisfy FR-23 without expanding the spine into a component catalog.

### R-PRD-04 — MEDIUM — Read-side availability and notification behavior was dropped

**Evidence**

- FR-11 forbids exposing partial or stale-as-fresh roll-up during repair/rebuild and permits an
  explicit unavailable state (`prd.md:240`; `.memlog.md:31`).
- FR-11 also requires both Roll-Up and “what's next” to emit payload-free, changed-key notifications
  through the substrate notifier seam (`prd.md:242`; `.memlog.md:36`).
- AD-16 keeps readers on the prior generation until promotion, while AD-22 defines convergence and
  backfill, but neither binds how that prior generation is marked to consumers; no spine rule names
  the notifier seam or changed-key-only payload.

**Why this matters**

Independent projection/query builders can expose stale output as current, choose incompatible
repair-state semantics, or implement polling/full-payload notifications. Those choices cross the
Works/EventStore/live-surface boundary and cannot be recovered from compliant structure alone.

**Required reconciliation**

Amend AD-16/AD-22 or add one read-model-delivery rule: partial generations are never visible; any
served prior generation during repair/rebuild is explicitly stale/unavailable rather than fresh; and
both v1 projections publish changed-key-only notifications through the EventStore notifier seam after
their durable update commits.

### R-PRD-05 — MEDIUM — Platform policy ownership lost the product's tenant scope and default

**Evidence**

- The PRD makes tree depth `default 32`, configurable per tenant, with uncapped breadth
  (`prd.md:260`, `prd.md:521`). Due-Date/TTL policy is likewise per tenant.
- AD-21 assigns “trusted MaxDepth/timeout policy” to Platform (`ARCHITECTURE-SPINE.md:329`) and AD-25
  says only “Platform policy” (`ARCHITECTURE-SPINE.md:381-386`). Neither binds tenant-scoped policy or
  the depth-32 default.

**Why this matters**

A global host option and a tenant-resolved option are incompatible configuration contracts. Registry,
reminder translation, and Platform composition could otherwise make different choices while each
claims compliance.

**Required reconciliation**

State that Platform resolves trusted MaxDepth and TTL policy for the target tenant before command
admission; the v1 MaxDepth default is 32 and breadth remains uncapped. Keep policy reads outside
aggregate `Handle` as AD-17/AD-25 already require.

## Confirmed correctly carried

- FR-20's exact two-view predicate and Priority/DueDate/ordinal-ID order are preserved by AD-03.
- Unit immutability, inheritance on first child estimate, and mixed-Unit subtotals are preserved by
  AD-04.
- Folded absolute descendant contributions, unestimated counts, per-stream LWW, attachment-aware
  tombstones, and the provisional five-second quiescence proof are preserved by AD-06/AD-22.
- Exact-match resume, set clearing, and consumed-condition-only no-op are preserved by AD-13.
- WorkItemCreated-gated attachment, explicit active Handoff, and conditional CorrectProgress reopen
  correctly follow the latest memlog overrides rather than the stale rendered PRD.
- Reactor ownership, domain purity, platform-host ownership, transport-effect identity, recovery
  readiness, tenant isolation, and additive fail-closed schema evolution all preserve or safely
  strengthen the PRD's architectural constraints.
