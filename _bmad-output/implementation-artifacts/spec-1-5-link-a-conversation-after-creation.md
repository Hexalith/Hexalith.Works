---
title: 'Story 1.5: Link a Conversation After Creation'
type: 'feature'
created: '2026-09-06'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '33a27e287862a53ee1b8e837d185a8945b321a11'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/docs/lifecycle-transition-matrix.md'
  - '{project-root}/references/Hexalith.AI.Tools/hexalith-state-instructions.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Work Item may carry a Conversation correlation at creation but cannot acquire one later, leaving independently-created work and dialogue uncorrelated unless history is rewritten.

**Approach:** Add a lifecycle-neutral, event-sourced `LinkConversation` act whose replay and reference-bearing read models converge without copying Conversation data.

## Boundaries & Constraints

**Always:** Preserve this precedence: same ID → `DomainResult.NoOp` in every status; different existing ID → `WorkItemConversationLinkRejected`; missing state → transition rejection from `Unknown`; unlinked terminal → `WorkItemTransitionRejected(status, "LinkConversation")`; unlinked live state → `ConversationLinked`. Success advances the state ordinal without changing status. Use EventStore and tenant-scoped identity. End at exactly 15 commands, 15 success events, and 10 rejections.

**Never:** Replace a link; store Conversation content; add a Conversations implementation dependency; alter existing payload bytes/type names; or add a `V2` type, lifecycle act/status, reactor handler, hosting change, generated ID, clock, I/O, or direct persistence.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First link | Any non-terminal state, no link, valid opaque correlation | One `ConversationLinked`; replay retains status and sets link/next ordinal | N/A |
| Exact retry | Any state with same link, including terminal | `DomainResult.NoOp`; no event or mutation | N/A |
| Conflicting relink | Any state with different authoritative link | Original link/status/ordinal retained | `WorkItemConversationLinkRejected` carries tenant, work item, existing ID, proposed ID |
| Closed unlinked work | Completed, Cancelled, Rejected, or Expired with no link | No mutation | `WorkItemTransitionRejected` with attempted act `LinkConversation` |
| Missing work | Null or `Unknown` state | No implicit Work Item or Conversation | `WorkItemTransitionRejected(Unknown, "LinkConversation")` |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works.Contracts/`, `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`, `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- contract, pure handling/replay, and runtime wrapper; leave `WorkItemLifecycle` unchanged.
- `src/Hexalith.Works.Projections/Strategies/`, `src/Hexalith.Works.Contracts/Models/`, `src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs` -- projected reference and query path; linking is a what's-next no-op that still advances its watermark.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`, schema fixtures, and architecture/integration count gates -- authoritative catalog and compatibility surfaces.
- `docs/lifecycle-transition-matrix.md`, `docs/boundary-decision-record.md`, `docs/eventstore-api-surface-constraints.md`, `docs/whats-next-projection.md` -- maintained semantics, counts, and VAL-H11 compatibility matrix.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works.Contracts/{Commands/LinkConversation,Events/ConversationLinked,Events/Rejections/WorkItemConversationLinkRejected}.cs` -- add the three documented contract shapes with generated polymorphic mapper registration.
- [x] `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`, `src/Hexalith.Works.Contracts/State/WorkItemState.cs`, `src/Hexalith.Works/WorkItemEventStoreAggregate.cs`, `tests/Hexalith.Works.UnitTests/WorkItemLinkConversationTests.cs` -- implement and exhaustively test precedence, replay, status, and sequence semantics.
- [x] `src/Hexalith.Works.Projections/Strategies/*.cs`, `src/Hexalith.Works.Contracts/Models/{WorkItemRollUp,WorkItemView}.cs`, `src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs` and projection/query tests -- persist and expose the correlation without changing what's-next eligibility.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`, `tests/Hexalith.Works.{ArchitectureTests,IntegrationTests}/**/*.cs` -- expand count, projection-coverage, and rejection-replay gates to 40.
- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/` -- generate new camelCase and exact PascalCase fixtures for both events plus a separate command fixture/test; prove all old fixture bytes remain unchanged.
- [x] `docs/{lifecycle-transition-matrix,boundary-decision-record,eventstore-api-surface-constraints,whats-next-projection}.md` -- mark the act implemented, reconcile maintained counts, and bind the VAL-H11 compatibility matrix without rewriting time-stamped story history.

**Acceptance Criteria:**
- Given each matrix state, when `LinkConversation` is handled and results are replayed, then the exact documented success/no-op/rejection occurs and authoritative state is preserved.
- Given projected events arrive duplicated or out of order, when read models rebuild and persist, then the correlation and accepted-source watermark converge deterministically and queries expose only the opaque reference.
- Given catalog, architecture, polymorphic, golden-corpus, and dependency tests run, then the catalog is 40 (15/15/10), all three new contracts have frozen coverage, prior bytes still pass, and no Conversations implementation dependency exists.

## Implementation Notes

- Added the three polymorphic contracts, pure aggregate handling/replay, runtime wrapper, nullable roll-up/view fields, deterministic projections, query exposure, catalog governance, fixtures, and maintained documentation.
- Matrix test audit: `WorkItemLinkConversationTests` executes all five rows across 29 cases (five live first-links, nine all-status retries, nine all-status conflicts, four terminal first-links, and null/Unknown missing-state coverage); all 29 passed. Projection convergence and what's-next watermark behavior add three passing focused cases.
- Compatibility audit: the catalog is 40 (15 commands, 15 success events, 10 rejections); the 46 tracked pre-existing golden JSON files have no diff from baseline; all new command/event fixtures passed exact and tolerant-reader corpus checks; architecture dependency tests passed with no Conversations project/package reference introduced.
- Review remediation hardened envelope/state identity, malformed and conflicting replay, first-link projection authority, camelCase command reading, create-time projection, and legacy read-model compatibility; all routed Story 1.5 findings are covered by focused tests.
- Final verification passed: restore; Release build with zero warnings/errors; Unit 567/567 (including 37 focused link/projection cases); Architecture 236/236; Property 3/3; focused Story 1.5 integration/contract tests 40/40; deterministic non-smoke integration subset 279/279; pure cascade helpers 17/17; and `git diff --check`.
- The unfiltered integration executable remains infrastructure-blocked outside Story 1.5: `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` reached `WorksReminderRecoveryPipelineSmokeTests`, where `dapr-sentry` was unhealthy and `eventstore`, `works`, `eventstore-operations`, and `eventstore-admin` failed to start; the process was later killed with exit 137, so it produced no reliable final count.
- The audit diff also contains concurrent Story 4.8/AppHost work. Those files are unrelated to this story and were preserved without Story 1.5 edits.

## Spec Change Log

## Review Triage Log

| ID | Verdict | Route | Evidence |
| --- | --- | --- | --- |
| B1 | medium | patch | `LinkConversation` is the new handler whose payload identity is not compared with either the established state or the `CommandEnvelope`; EventStore routes state by envelope but `WorkItemEventStoreAggregate` currently discards that envelope, so a mismatched payload can emit or reject under the wrong identity. |
| B2 | medium | patch | `WorkItemState.Apply(ConversationLinked)` replaces a non-null different correlation. A direct conflicting replay therefore violates the first-authoritative-link invariant; preserve the first value while still consuming the event ordinal. |
| B3 | medium | patch | The roll-up fold assigns every later `ConversationLinked`, including one conflicting with `WorkItemCreated` or an earlier link. Preserve the first value and mark conflicting evidence degraded. |
| B4 | medium | patch | `System.Text.Json` accepts a missing/null non-nullable record argument; the decoder then accepts a null `ConversationCorrelationId`, clears the projection value, and advances its watermark. Reject that malformed known payload and guard replay. |
| B5 | false | reject | A full replay containing `WorkItemAssigned` may notify because assignment changes eligibility; `ConversationLinked` itself returns `Changed == false`, so the cited notification is not caused by the link and does not contradict its documented no-op effect. |
| B6 | medium | patch | No test sends `LinkConversation` through reflection-discovered `WorkItemEventStoreAggregate.ProcessAsync`; deleting the wrapper overload leaves the existing pure-handler and serialization tests green while production dispatch fails. |
| B7 | medium | patch | The command golden test proves exact PascalCase bytes and a tolerant read of those same bytes, but does not exercise a camelCase body even though the compatibility contract claims that direction. |
| B8 | medium | defer | The default placement/scheduler topology is not deterministically asserted. These AppHost edits belong to the concurrently active Story 4.8, not Story 1.5. |
| B9 | medium | defer | Command/cascade smoke prerequisites still require the superseded external control-plane ports and can skip the new default path. This is concurrent Story 4.8 work. |
| B10 | medium | defer | Deterministic ACL tests do not pin configuration- and policy-level trust domains, so an allow rule can silently diverge from Sentry. The affected YAML/AppHost tests are concurrent Story 4.8 work. |
| B11 | low | patch | The Story 1.5 callout changed the whole hosting course correction to “Implemented” while AppHost/ServiceDefaults remain pending Story 4.9; clarify that only the link path is implemented. |
| B12 | low | patch | The spec is `in-review` but sprint tracking still says `in-progress`; synchronize the story to the repository's `review` state. |
| B13 | low | reject | The generic verification checklist says all project suites should pass, while the implementation evidence correctly qualifies the blocked unfiltered lane. Workflow policy rejects findings whose fix is an edit to this build's spec. |
| B14 | medium | defer | The concurrent Story 4.8 spec still recommends `public` after its implementation changed all trust domains to `localhost`, which could reintroduce the diagnosed mismatch. It is not Story 1.5-owned. |
| B15 | medium | defer | The concurrent Story 4.8 spec freezes/reports 37 while this story establishes the current 40-type catalog; its intended constraint should be “Story 4.8 adds no types.” It is not Story 1.5-owned. |
| E1 | medium | patch | The EventStore aggregate supports a three-argument handler with `CommandEnvelope`, but the new wrapper uses two arguments and cannot fail closed on envelope/payload tenant or aggregate mismatch. |
| E2 | low | reject | `NextSequence` can overflow at `long.MaxValue`, but the same helper is used by every existing command, requires an effectively unreachable stream length or corrupt state, and a correct cross-cutting fix is more than a direct Story 1.5 correction. |
| E3 | medium | patch | A constructed/deserialized `ConversationLinked` with a null reference reaches replay/projection today and can erase the reference; this is the demonstrated malformed-payload case from B4. |
| E4 | medium | patch | A second conflicting success event replaces the aggregate state's first correlation; no trusted writer should emit it, but defensive replay is required by the story's never-replace rule. |
| E5 | medium | patch | A second conflicting success event replaces the projected first correlation; retain first authority and record a deterministic diagnostic while advancing the accepted-source watermark. |
| E6 | false | reject | Explicit control-plane endpoints are an operator-provided external mTLS pair; syntactic reachability cannot prove protocol/domain compatibility at model construction, and an invalid pair already fails loudly during sidecar startup rather than yielding silent domain behavior. |
| E7 | medium | defer | The concurrent scheduler listens for embedded-etcd clients on `0.0.0.0`, making that port reachable to co-networked containers even without host publication. Review and constrain it in Story 4.8. |
| E8 | medium | defer | The obsolete external-port prerequisite can skip the AppHost-owned control-plane path; this duplicates B9 under the concurrent Story 4.8 root cause. |
| E9 | medium | patch | The “exhaustively test” claim omitted production envelope dispatch plus malformed/conflicting replay paths; the cited gaps are real and are covered by the B1-B4 patch group. |
| V1 | medium | patch | Pre-verified gap: no `ProcessAsync` test covers reflection dispatch, deserialization, first link, retry, conflict, and rehydration for the new wrapper. |
| V2 | medium | patch | Pre-verified gap: the newly added `WorkItemCreated.ConversationCorrelationId` roll-up assignment can be deleted without a projection/query test failing. Add a persisted create-time-correlation query case. |
| V3 | medium | patch | Pre-verified gap: no test deserializes a legacy roll-up JSON document without the additive field and then verifies a successful null-valued query. |
| V4 | medium | defer | Pre-verified gap: the concurrent default AppHost control plane is bypassed by deterministic topology tests and skipped by live prerequisites; defer to Story 4.8. |
| V5 | medium | defer | Pre-verified gap: concurrent ACL trust-domain changes lack deterministic assertions at both access-control levels; defer to Story 4.8. |
| V-O1 | medium | patch | The verification reviewer directly demonstrated that missing `conversationCorrelationId` deserializes as null and is accepted by the current decoder; reject it as malformed and test the path. |

## Design Notes

Use `ExistingConversationCorrelationId` and `ProposedConversationCorrelationId` on the conflict rejection, matching existing/proposed-parent vocabulary. Add nullable read-model properties so older documents remain readable.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1` -- restore succeeds.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1` -- zero warnings and errors.
- Run UnitTests, IntegrationTests, ArchitectureTests, and PropertyTests individually (or built xUnit v3 executables if MTP is blocked) -- all tests pass.
- `git diff --check` -- no whitespace errors; existing golden fixtures are unchanged.
