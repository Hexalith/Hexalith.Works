# Sprint Change Proposal — Close the Remaining Epic 5 Readiness Gates (2026-09-15)

**Project:** works
**Prepared for:** Administrator
**Workflow:** `bmad-correct-course`
**Mode:** Batch
**Status:** Approved for implementation
**Primary evidence:** `_bmad-output/planning-artifacts/implementation-readiness.md`
**Carries forward:** approved `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-14.md`

## 1. Issue Summary

The refreshed 2026-09-15 implementation-readiness assessment remains **FAIL** after substantial
progress on the approved 2026-09-14 correction. The historical Epic 1–4 mappings are restored, the
rewritten target stories are quarantined under non-executable `F*` labels, the PRD contains the
approved overrun-preserving re-estimation rule, and Epic 5 is defined. Those resolved findings remain
closed and must not be reopened.

Four current findings still prevent deterministic sprint planning:

1. **IR-01 — Architecture contradicts the approved re-estimation rule.** PRD FR-8/FR-9 and Story 5.3
   preserve cumulative `Done`, clamp only `Remaining`, and display overrun. Architecture AD-17 still
   bounds `CorrectProgress` to `0..Estimated` and says `ReEstimate` clamps `Done`; candidates F2-E and
   F2-F retain the same obsolete bounds.
2. **IR-02 — Epic 5 has no standalone implementation artifacts.** Stories 5.1–5.5 exist in
   `epics.md`, but no validated `5-1-*` through `5-5-*` artifacts exist in the implementation-artifact
   directory.
3. **IR-03 — multiple Await-Condition admission is underspecified.** The PRD permits multiple active
   conditions but does not bind the collection shape, duplicate policy, complete event shape, or the
   meaning of “first” when more than one trigger is eligible.
4. **IR-04 — UX provenance predates the approved Product amendment.** The UX behavior is already
   aligned, but both UX documents remain dated 2026-09-12 and still describe a temporary memlog-first
   source precedence.

The trigger is the refreshed readiness report rather than a failing implementation story. This is an
**incomplete planning-authority synchronization**: one Product contract is still open, one adopted
architecture decision contradicts the Product authority, downstream candidate criteria inherit stale
rules, and executable story handoffs have not been generated.

`sprint-status.yaml` was correctly preserved. This proposal does not authorize its regeneration.

### Evidence behind the Await-Condition recommendation

The delivered contract and aggregate already establish a compatible behavior:

- `SuspendWorkItem` carries an ordered `IReadOnlyList<AwaitCondition>`.
- Aggregate admission requires at least one condition and rejects null members.
- Each typed `AwaitCondition` canonicalizes its kind-specific key; dates normalize to UTC.
- `WorkItemSuspended` copies and records the complete submitted collection. It does not currently
  deduplicate or reorder it.
- Resume checks exact typed-value membership, records the consumed member, clears the collection,
  no-ops only an exact retry of the consumed condition, and rejects other later triggers.
- The aggregate turn is serialized, so concurrent eligible Resume commands already have one
  deterministic commit winner.

The safest correction is therefore to document and test the delivered behavior, not introduce a new
normalization algorithm or durable payload change inside Story 5.2.

### Decisions proposed

1. Preserve the approved 2026-09-14 proposal and every resolved readiness finding.
2. Adopt the current v1 Await-Condition behavior as the Product rule:
   - Suspend accepts a non-empty ordered collection of valid, non-null typed conditions.
   - Per-condition canonicalization applies; collection-level deduplication and reordering do not.
   - Exact duplicates are preserved for durable compatibility but are semantically redundant.
   - “First match” means the first matching Resume committed in the serialized aggregate turn; it is
     not collection-position priority, broker timestamp priority, or wall-clock simultaneity.
   - The accepted Resume records its consumed condition, clears the complete active collection, and
     wins. An exact consumed-condition retry is the sole no-op; every other later trigger rejects.
   - Legacy singular suspension evidence remains readable as a one-member collection; current writers
     use the plural collection.
3. Keep collection canonicalization or priority semantics outside Epic 5. Any future change requires a
   uniquely identified story with explicit compatibility and migration evidence.
4. Amend AD-17 and candidate F2-E/F2-F to the PRD rule: `CorrectProgress` accepts any absolute
   `Done >= 0`; `ReEstimate` preserves `Done`; only `Remaining` clamps to zero; overrun remains visible;
   Status and Unit do not change.
5. Refresh UX provenance only. Do not redesign the aligned behavior or widen the headless-v1 scope.
6. Create and validate standalone Stories 5.1–5.5 only after the governing Product, Architecture,
   epics, and UX sources agree.
7. Rerun implementation readiness; regenerate sprint tracking once and only after a PASS.

## 2. Impact Analysis

### Epic impact

| Epic or candidate area | Impact | Required disposition |
| --- | --- | --- |
| Epics 1–4 delivered history | None | Preserve titles, IDs, artifacts, evidence, and statuses exactly as currently restored. |
| F2-E Correct Progress | Stale upper bound rejects valid overrun corrections | Replace `0..Estimated` with `Done >= 0`; remove `Done above Estimated` from the rejection case. |
| F2-F Re-Estimate | Stale clamp-Done behavior contradicts PRD and Story 5.3 | Preserve cumulative Done and clamp only Remaining; add replay/rebuild migration proof. |
| F3-F/F3-G Await/Resume | Set wording does not define duplicate or concurrent-match behavior | Bind the current ordered-collection and serialized-commit-winner rules; keep future normalization outside Epic 5. |
| Epic 5 | Scope remains viable | Add no story and remove none. Reconcile Story 5.2's preservation boundary, then generate Stories 5.1–5.5 as standalone artifacts. |

No epic is obsolete, no delivered story is reopened, and no new epic is required. Epic 5 remains the
right remediation container. The Product and Architecture corrections are prerequisites to creating
its executable story artifacts, not new implementation scope.

### Story impact and dependency order

| Story | Impact of this correction | Dependency disposition |
| --- | --- | --- |
| 5.1 Overflow-safe ordinals | No scope change | May be prepared independently after source reconciliation. |
| 5.2 Singular lifecycle authority | Clarify that extraction preserves current Await collection admission and matching behavior; it does not add deduplication or priority semantics | Must precede final 5.3 integration. |
| 5.3 Re-estimate overrun and notes | Unblocked only when AD-17 and F2-E/F2-F agree with the PRD | Depends on 5.4 for changed durable evidence. |
| 5.4 Contract-derived catalog | No scope change | May be prepared independently; precedes new/changed durable producers. |
| 5.5 Obligation admission bound | No scope change | Depends on 5.4 before enabling its new rejection producer. |

The five standalone artifacts should use the existing Epic 5 titles and deterministic slugs:

- `5-1-make-progress-and-event-ordinals-overflow-safe.md`
- `5-2-make-the-lifecycle-authority-executable-and-singular.md`
- `5-3-preserve-re-estimate-overrun-and-enforce-bounded-act-notes.md`
- `5-4-derive-durable-catalog-completeness-from-contracts.md`
- `5-5-enforce-the-obligation-bound-without-rewriting-history.md`

### Artifact conflicts

| Artifact | Current state | Required adjustment |
| --- | --- | --- |
| PRD | Correct numeric rule; incomplete Await admission rule; FR-14 is singular | Record the ordered-collection decision consistently in Glossary, FR-5, FR-14, FR-15, UJ-3, and the memlog; rerun PRD validation. |
| Architecture | AD-13 mostly matches delivered Resume behavior; AD-17 contradicts PRD | Amend AD-13 for collection/commit ordering and AD-17 for overrun-preserving numeric behavior and semantic migration. |
| `epics.md` | Historical mapping and Epic 5 are now sound; selected candidates remain stale | Correct F2-E/F2-F/F3-F/F3-G and add the Story 5.2 preservation boundary without promoting any `F*` label. |
| UX DESIGN/EXPERIENCE | Behavior aligned; dated before accepted amendment | Update provenance/date and source precedence only; retain current state, accessibility, and headless-v1 rules. |
| Lifecycle matrix | Correct high-level Suspend/Resume rows and full-set event wording | Mechanically bind the collection guard and serialized winner without duplicating Product policy in prose. |
| Story artifacts | Missing for all Epic 5 stories | Generate and validate all five only from reconciled authorities. |
| Sprint status | Correctly unchanged | Preserve until readiness PASS; then regenerate once with historical states intact and no `F*` keys. |

No deployment, infrastructure, package, CI/CD, or dependency change is introduced by this planning
correction. Later Story 5 implementation retains its already approved testing, durable catalog,
replay, projection, query, rebuild, and migration obligations.

### UX impact

There is no behavioral redesign. Existing UX already names every active Await-Condition, treats the
first accepted match as clearing all, shows visible Burn-Down overrun, distinguishes non-terminal zero,
retains correction/reopen history, exposes freshness, and meets the stated accessible presentation
floor. Only the authority trail changes:

- update both UX documents to `updated: 2026-09-15` when refreshed;
- make the regenerated final PRD/addendum the normative Product source;
- retain `.memlog.md` as decision provenance, not a standing override of incorporated PRD text;
- cite the corrected Architecture revision after AD-13/AD-17 are aligned;
- keep the current headless-v1 and uncommitted-web boundaries unchanged.

### MVP and delivery impact

The product goal and MVP remain achievable without reduction or redefinition. This proposal adds no
end-user feature, API, durable type, or new Epic 5 implementation story. It closes planning ambiguity
and makes the already approved remediation executable. The engineering estimate for Stories 5.1–5.5
is not changed by this proposal; only their planning prerequisites and acceptance authority are made
deterministic.

## 3. Recommended Approach

### Selected path: Option 1 — Direct Adjustment

Apply a focused Product/Architecture/backlog reconciliation, refresh UX provenance, generate the five
existing Epic 5 story artifacts, and rerun readiness.

**Effort:** Low-to-Medium for planning and story preparation.
**Risk:** Medium until authority alignment is complete; Low after validated reconciliation.

This path preserves delivery evidence and the existing implementation while resolving every current
readiness finding. It avoids both speculative normalization logic and a second restructuring of the
backlog.

### Alternatives considered

**Option 2 — Potential rollback: not viable.** Rolling back delivered behavior or the restored
historical map would destroy useful evidence and would not resolve AD-17, Await admission, or missing
story artifacts. Effort and risk are High.

**Option 3 — PRD/MVP reduction: not warranted.** The MVP and Epic 5 remain viable. Removing
multi-condition suspension or overrun visibility would contradict delivered behavior and accepted
Product intent while still requiring compatibility work. Effort is Medium and product risk is High.

## 4. Detailed Change Proposals

All edits below are proposed. Approval authorizes their ordered implementation through the named
planning workflows; this workflow does not apply them directly.

### 4.1 PRD — close the multiple Await-Condition Product decision

**Artifacts:**

- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/.memlog.md`
- `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/validation-report.md`

**Current:** FR-5 allows multiple conditions and says “first to fire”; FR-14 describes only one
condition; FR-15 defines exact membership and post-resume behavior but not duplicate admission or
concurrent eligible triggers.

**Proposed normative replacement:**

> An `InProgress` Work Item may suspend on a non-empty ordered collection of valid, non-null typed
> Await-Conditions. Each condition is canonicalized by kind; the v1 collection is neither reordered nor
> deduplicated at admission. Exact duplicates are durably preserved but add no matching or priority
> meaning. `WorkItemSuspended` records the complete collection. A legacy singular condition is read as
> a one-member collection.
>
> Resume matches kind and canonical key. If several matching triggers are eligible, “first match” means
> the first matching Resume committed by the serialized aggregate turn, not collection order, message
> timestamp, or wall-clock ordering. That Resume records the consumed condition, clears the complete
> active collection, and wins. An exact retry of the consumed condition is the sole Resume no-op; every
> nonmatch while Suspended and every different later trigger is a domain rejection.

Update the Glossary, Work Item description, FR-5 consequences, FR-14 title/body/consequences, FR-15,
and UJ-3 terminology from ambiguous singular/set wording to this collection contract. Append the
accepted 2026-09-15 decision to `.memlog.md`, explicitly superseding but preserving the prior deferred
entry. Rerun PRD validation and close the validation report's remaining medium finding.

### 4.2 Architecture — align AD-13 and AD-17

**Artifact:** `_bmad-output/planning-artifacts/architecture.md`

**AD-13 current:** exact set member resumes and clears the set; exact consumed retry no-ops.

**AD-13 proposed:**

> While Suspended, only an exact member of the current ordered AwaitCondition collection resumes and
> clears the collection. Collection order gives no trigger priority; the first matching Resume committed
> in the serialized aggregate turn wins. Per-condition canonicalization applies, but v1 admission does
> not reorder or deduplicate the collection. Exact duplicate members are semantically redundant and
> remain readable. A nonmatch rejects without mutation; only replay of the exact consumed condition is
> a no-op, and every other later condition rejects.

**AD-17 current:**

> Progress delta is positive; CorrectProgress absolute Done is within 0..Estimated; Estimated is
> nonnegative; Unit follows AD-04; ReEstimate clamps Done and never completes.

**AD-17 proposed:**

> Progress delta is strictly positive. `CorrectProgress` accepts an absolute cumulative `Done >= 0` in
> the established Unit; Done may exceed Estimated and remains visible. Estimated is nonnegative.
> `ReEstimate` changes only Estimated, preserves cumulative Done and Unit, derives
> `Remaining = max(Estimated - Done, 0)`, and never completes, reopens, or changes Status. Only
> `CorrectProgress` replaces cumulative Done.

Add the already approved semantic-migration gate to AD-17: representative legacy streams that formerly
rebuilt with clamped Done must replay consistently through aggregate and read-side folds; live and
rebuilt states must agree; disposable projections must be rebuilt; affected snapshots/caches must be
invalidated; and write-side/read-side interpretation must change atomically before rollout.

### 4.3 Epics — reconcile candidates without changing executable identity

**Artifact:** `_bmad-output/planning-artifacts/epics.md`

- **F2-E:** change the accepted range from `0..Estimated` to `Done >= 0`; remove `Done above
  Estimated` from the rejection case; require visible overrun and `Remaining = 0` when Done exceeds
  Estimated.
- **F2-F:** replace “applied Done is clamped” with “cumulative Done is preserved”; require Estimated to
  change, Remaining to clamp, Status/Unit to remain unchanged, and aggregate/projection/rebuild evidence
  to converge.
- **F3-F:** replace unspecified “set normalization” with the approved ordered-collection rule. Preserve
  submitted order and exact duplicates; state that duplicates carry no extra priority or release meaning.
- **F3-G:** state that first-success is the serialized aggregate commit winner and not collection order
  or adapter-local timing. Preserve existing exact-match, clear-all, no-op, and rejection behavior.
- **Story 5.2:** add an explicit preservation criterion: extracting the lifecycle authority must retain
  the current non-empty collection guard, exact membership rule, complete collection event/state, and
  serialized-winner behavior; it must not introduce collection normalization or trigger priority.
- **Additional requirements:** mark the Product decision resolved, retain `F*` labels as non-executable,
  and preserve the current Epic 5 dependency graph.

Do not edit historical Story 2.4 or Story 3.5 artifacts, promote an `F*` candidate, add an `F*` tracker
key, or imply that later target criteria were part of delivered acceptance.

### 4.4 UX — refresh provenance only

**Artifacts:**

- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md`
- `_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md`

Update the revision date and source precedence after PRD and Architecture validation. The final amended
PRD/addendum govern Product semantics; `.memlog.md` records provenance and only supplies a temporary
override when an accepted decision has not yet been incorporated into the rendered PRD. Architecture
supplies compatible implementation constraints after Product semantics. Preserve every current
behavioral, visual, accessibility, freshness, and delivery-horizon rule.

### 4.5 Story artifacts — create and validate Epic 5

Use the reconciled PRD, Architecture, epics, lifecycle matrix, retrospectives, and durable evidence to
create the five standalone implementation artifacts listed in §2. Each artifact must contain its
existing Epic 5 scope, dependencies, affected-file guidance, compatibility constraints, test evidence,
and explicit non-goals. Validate all five before adding any tracker key.

Story 5.2 must treat the Await decision as a preservation constraint. If story preparation discovers
that delivered behavior differs from the decision above, stop and route that discrepancy back through
Product and Architecture; do not silently widen Story 5.2.

### 4.6 Sprint status — remain unchanged until PASS

`_bmad-output/implementation-artifacts/sprint-status.yaml` remains unchanged during Product,
Architecture, epics, UX, and story-artifact reconciliation.

After a new implementation-readiness assessment returns PASS, regenerate tracking once and atomically:

- preserve every historical story key and status;
- mark Epics 1–3 done;
- retain Epic 4 in progress and its story statuses;
- add Epic 5 and Stories 5.1–5.5 as backlog;
- add no provisional `F*` key.

### 4.7 Required remediation sequence

1. Obtain explicit approval of this proposal.
2. Use `bmad-prd` to record and validate the Await-Condition decision.
3. Use `bmad-architecture` to amend AD-13, AD-17, and the semantic-migration gate.
4. Use `bmad-create-epics-and-stories` to reconcile F2-E/F2-F/F3-F/F3-G and Story 5.2 in `epics.md`.
5. Use `bmad-ux` to refresh provenance without redesign.
6. Use `bmad-create-epics-and-stories` to create and validate Stories 5.1–5.5 as standalone artifacts.
7. Rerun implementation readiness.
8. Only on PASS, run sprint planning once to regenerate `sprint-status.yaml` as specified in §4.6.
9. Implement Epic 5 in its recorded dependency order and retain the approved replay, rebuild, catalog,
   and retrospective gates.

## 5. Implementation Handoff

**Scope classification:** Moderate — cross-artifact planning reconciliation and executable-story
preparation; no MVP replan, new epic, or code change.

**Route to:** John (PM), Winston (Architect), Product Owner/Sprint Manager, Sally (UX), Amelia/Charlie
(Dev), Dana (QA), and Administrator.

- **John (PM):** own and validate the ordered Await-Condition collection decision and its PRD trace.
- **Winston (Architect):** align AD-13/AD-17, define the replay/rebuild migration gate, and verify the
  lifecycle matrix can remain the single mechanical authority.
- **Product Owner / Sprint Manager:** reconcile candidate and Story 5.2 criteria, create and validate
  Stories 5.1–5.5, and keep tracking frozen until readiness passes.
- **Sally (UX):** refresh source provenance and confirm no behavior, accessibility, or horizon change.
- **Amelia/Charlie (Dev):** review generated stories for implementability and preserve durable behavior;
  perform no Epic 5 implementation until the stories validate and tracking is safely generated.
- **Dana (QA):** verify the Await regression contract, numeric migration evidence, catalog completeness,
  and non-vacuous acceptance proof in the generated stories.
- **Administrator:** approve or revise this proposal, preserve the tracker gate, and rerun readiness and
  sprint planning in the required order.

### Success criteria

- PRD validation has no unresolved multiple Await-Condition admission finding.
- PRD, Architecture, epics, lifecycle matrix, and delivered contract agree on the ordered collection,
  exact match, complete collection, serialized winner, clear-all, narrow no-op, and rejection rules.
- PRD, Architecture, candidate criteria, Story 5.3, replay/rebuild guidance, UX, and tests agree that
  ReEstimate preserves Done and only Remaining clamps.
- UX provenance names the reconciled authorities without changing behavior or delivery scope.
- Five standalone Epic 5 story artifacts exist and validate with the recorded dependencies.
- `sprint-status.yaml` remains byte-for-byte unchanged until readiness PASS.
- The next implementation-readiness verdict is PASS before sprint tracking is regenerated.

## 6. Checklist Outcome

| Item | Status | Finding |
| --- | ---: | --- |
| 1.1 Triggering story | [x] | No single story; the 2026-09-15 readiness rerun exposed four remaining handoff gates after the approved 2026-09-14 correction. |
| 1.2 Core problem | [x] | Incomplete authority synchronization and missing executable story artifacts; not a failed implementation approach. |
| 1.3 Evidence | [x] | Readiness report, PRD/memlog/validation, epics, Architecture, UX, lifecycle matrix, prior approved proposal, tracker, delivered contracts, aggregate behavior, and focused tests. |
| 2.1 Current epic viability | [x] | Epic 5 remains viable; its stories cannot be generated safely until Product and Architecture agree. |
| 2.2 Epic-level changes | [x] | No new or removed epic; reconcile F2/F3 candidates and add a preservation boundary to Story 5.2. |
| 2.3 Remaining epic impact | [x] | Epics 1–4 history is unaffected; Epic 5 dependencies remain unchanged. |
| 2.4 New/obsolete epics | [x] | None. Future canonicalization or priority behavior requires a unique later story if ever selected. |
| 2.5 Priority/order | [x] | Product decision → Architecture/epics → UX provenance → five story artifacts → readiness → tracker. |
| 3.1 PRD conflicts | [x] | FR-5/FR-14/FR-15 need one collection contract; MVP and core goals remain unchanged. |
| 3.2 Architecture conflicts | [x] | AD-13 needs collection ordering language; AD-17 and its migration gate need numeric correction. |
| 3.3 UX conflicts | [x] | No UX behavior conflict; provenance and source precedence only. |
| 3.4 Other artifacts | [x] | Lifecycle matrix, candidates, story artifacts, validation report, replay/rebuild evidence, catalog tests, and tracker gate are affected. |
| 4.1 Direct Adjustment | [x] Viable | Low-to-Medium planning effort; Medium risk before validation and Low after alignment. |
| 4.2 Potential Rollback | [x] Not viable | High effort/risk; destroys valid evidence and does not resolve current gates. |
| 4.3 PRD MVP Review | [x] Not warranted | MVP is achievable; reduction would contradict delivered behavior and accepted intent. |
| 4.4 Recommended path | [x] | Option 1, focused Direct Adjustment. |
| 5.1 Issue summary | [x] | Four current findings and previously resolved findings are explicitly separated. |
| 5.2 Impact and adjustments | [x] | Epic, story, Product, Architecture, UX, lifecycle, validation, and tracker impacts are specified. |
| 5.3 Approach and rationale | [x] | Preserve current durable behavior, correct stale authorities, then create the existing stories. |
| 5.4 MVP/action plan | [x] | MVP unchanged; nine-step gated handoff defined. |
| 5.5 Handoff plan | [x] | PM, Architect, PO/SM, UX, Dev, QA, and Administrator responsibilities assigned. |
| 6.1 Checklist review | [x] | All applicable analysis items are addressed; the tracker update remains a deliberately deferred execution gate. |
| 6.2 Proposal accuracy | [x] | Cross-checked against current planning sources and delivered Await implementation behavior. |
| 6.3 User approval | [x] | Approved by Administrator on 2026-09-15 without revision. |
| 6.4 Sprint-status update | [!] | Intentionally deferred until reconciled sources, five validated story artifacts, and readiness PASS. |
| 6.5 Next steps/handoff | [x] | Owners, order, stop conditions, and success criteria are explicit. |

## 7. Approval Record

**Decision:** Approved for implementation.
**Approver:** Administrator.
**Date:** 2026-09-15.
**Conditions or revisions:** Execute the ordered remediation in §4.7. Preserve delivered historical
evidence and keep `sprint-status.yaml` unchanged until the reconciled authorities and five validated
Epic 5 story artifacts produce an implementation-readiness PASS.

Approval will authorize the ordered planning-artifact corrections and story preparation in §4.7. It
will not authorize code implementation, dependency updates, sprint-status regeneration before PASS,
commit, push, or changes to delivered historical evidence.

## 8. Workflow Execution Log

| Date | Event | Result |
| --- | --- | --- |
| 2026-09-15 | Refreshed readiness trigger accepted | FAIL with IR-01–IR-04; tracker confirmed unchanged. |
| 2026-09-15 | Batch discovery and checklist analysis completed | Current PRD, epics, Architecture, UX, lifecycle matrix, prior proposal, tracker, and delivered contract behavior reviewed. |
| 2026-09-15 | Await implementation evidence checked | Existing ordered collection, complete event, exact membership, clear-all, and serialized winner support a documentation-first Product decision. |
| 2026-09-15 | Path-forward evaluation completed | Focused Direct Adjustment selected; rollback and MVP reduction rejected. |
| 2026-09-15 | Sprint Change Proposal prepared | Moderate scope; submitted for Administrator review. |
| 2026-09-15 | Administrator approval received | Proposal approved without revision and finalized for implementation handoff. |
| 2026-09-15 | Moderate-scope handoff recorded | Routed primarily to Product Owner and Developer agents, with PM, Architecture, UX, and QA prerequisite responsibilities defined in §5. |
