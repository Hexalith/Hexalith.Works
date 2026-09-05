---
title: "Readiness conflict correction — hosting ownership, rebuild consistency, claim concurrency, and conversation linking"
status: proposed-awaiting-review
created: 2026-09-05
trigger: "Sprint-planning readiness verdict CONCERNS: four conflicts between the Hexalith baseline, planning artifacts, runtime documentation, and FR-21 story coverage."
mode: batch
scope: major
---

# Sprint Change Proposal - Readiness Conflict Correction (2026-09-05)

## 1. Issue Summary

The sprint-planning readiness check found four contradictions that must be resolved before the plan can
return to `READY`. The triggering evidence is authoritative and reproducible:

| Conflict | Planning statement | Authoritative evidence | Consequence |
| --- | --- | --- | --- |
| Hosting ownership | PRD FR-24, Architecture, and Story 1.1 require Works-owned `Hexalith.Works.AppHost` and `Hexalith.Works.ServiceDefaults` projects. | The current Hexalith baseline requires a domain module to consume `Hexalith.EventStore.DomainService`, ship only domain-centric code plus a two-line domain-service host, and not ship its own `*.AppHost`, `*.Aspire`, or `*.ServiceDefaults` project. | The plan and completed scaffold encode an obsolete ownership boundary. The actual Works AppHost and ServiceDefaults projects require migration/removal after their topology responsibility is moved to a platform-owned host. |
| Projection rebuild behavior | Architecture E1 and Epics NFR-4/AR-17 promise online, non-disruptive rebuild with no live-delivery interruption. | `docs/eventstore-api-surface-constraints.md` and the projection documents require ordinary projection delivery to be quiesced from authoritative inventory capture through Commit, unless an equivalent platform fence excludes live writers; delivery then resumes and catches up. | The reader-facing atomic promotion is valid, but the writer/delivery side is not non-disruptive. Planning overpromises the implemented platform contract. |
| Claim concurrency | Epics NFR-3/AR-2/AR-10 and Story 4.3 describe caller-supplied or command-supplied expected versions. | `docs/eventstore-api-surface-constraints.md` states that EventStore exposes no `expectedVersion` append argument. EventStore owns optimistic concurrency through the Dapr state-store ETag on the atomic actor-state save. | Callers must not supply a version. The story must test ETag conflict/retry behavior through the EventStore-owned pipeline and distinguish a re-handled domain rejection from retry-exhaustion infrastructure failure. |
| FR-21 coverage | PRD FR-21 says a Conversation correlation ID can be linked at creation or by a later command and corresponding event. | Stories 1.2 and 1.3 cover only creation-time/absent references. The current command and event catalogs contain no later-link operation. | FR-21 is only partially implemented. A new story and additive command/event contract are required. |

This is primarily accumulated planning drift: Story 1.1 discovered the real EventStore concurrency and rebuild
surfaces, and later runtime work documented them accurately, but the upstream PRD/architecture/epics were not
fully reconciled. The hosting baseline has also moved to a stricter domain-service SDK boundary after the original
Works architecture was written.

## 2. Impact Analysis

### Epic impact

- **Epic 1 — Builder-Ready Work Item Kernel:** remains valid, but its scaffold definition must adopt the
  domain-service SDK boundary. Add Story 1.5 to complete FR-21's later-link operation. The already-completed
  Story 1.1 remains historical evidence; its superseded hosting target is corrected through a new migration
  story rather than pretending the original implementation never happened.
- **Epic 2 — Reliable Single-Item Lifecycle and Burn-Down:** no scope change. The new conversation-link act
  must state whether it is lifecycle-neutral and how it behaves after terminal closure, but no existing
  lifecycle transition is redefined.
- **Epic 3 — Work Tree Roll-Up and Durable Await:** no product-scope change. Its projection guarantees must
  refer to EventStore's actual independent-aggregate and shared-rebuild lifecycles, including the delivery
  quiescence/fence precondition.
- **Epic 4 — Shared Work Execution and Builder Runtime Validation:** remains valid but cannot close under the
  current Works-owned AppHost model. Add Story 4.9 to migrate runtime composition to the EventStore
  domain-service SDK and a designated platform-owned AppHost. Story 4.8 remains in review and must be
  revalidated after that migration.
- No epic is obsolete, and no new epic is necessary. Epic priority remains 1 → 4, with the remediation work
  inserted before Epic 1/Epic 4 can be declared done.

### Story impact

- **Correct planning text:** Story 1.1, Story 4.3, Story 4.5, and the hosting references in Stories 4.6–4.8.
- **Add Story 1.5:** link a Conversation after Work Item creation.
- **Add Story 4.9:** migrate Works hosting/runtime composition to the platform-owned host model.
- **Do not reopen historical implementation records:** add a short supersession/correction note to the done
  Story 1.1 and Story 4.3 implementation artifacts; use the new stories for executable work.

### Artifact conflicts

- **PRD:** FR-24, the vision/MVP hosting language, and package-boundary language conflict with the current
  Hexalith baseline. FR-21 itself is correct; FR-7's fixed event catalog must grow additively for its missing
  later-link event.
- **Architecture:** the starter/project structure, kernel/adapter boundary, development workflow, validation
  claims, and E1 rebuild guarantee require revision. The ETag concurrency decision is already mostly correct,
  but all residual expected-version terminology must be removed.
- **Epics/stories:** requirements inventory and acceptance criteria carry all four conflicts.
- **UX:** no visual or interaction change is required for v1 because it remains headless. UX-DR6's future
  unified-history intent remains compatible with an additive conversation-link event.
- **Runtime documentation:** already records the current rebuild and ETag truth and should remain authoritative.
  It needs backlinks from the corrected planning artifacts, not semantic reversal.
- **Sprint tracking:** after approval, add `1-5-link-a-conversation-after-creation: backlog` and
  `4-9-migrate-works-hosting-to-the-platform-boundary: backlog`. Epic 1 and Epic 4 remain `in-progress`;
  Story 4.8 remains `review` until independently accepted.

### Technical impact

- Remove Works ownership of `Hexalith.Works.AppHost` and `Hexalith.Works.ServiceDefaults` only after the
  equivalent local/integration topology is available in the designated platform/host repository.
- Reduce the Works runnable host to the canonical EventStore domain-service shape. Retain domain-owned pure
  aggregates, projection/query handlers, and mechanical domain translations; move generic Dapr topology,
  service defaults, projection/query plumbing, subscription plumbing, health, and telemetry to the platform.
  If the SDK lacks a required generic seam, add that seam to `Hexalith.EventStore` first and consume it from Works.
- Preserve the existing reader-safe shared rebuild: staged data stays invisible and Commit is atomic, while
  ordinary projection delivery is quiesced/fenced during capture-through-Commit and catches up afterward.
- Keep claim command contracts free of version/ETag fields. EventStore owns the ETag, conflict retry, fresh
  rehydrate, and retry-exhaustion behavior.
- Add `LinkConversation`, `ConversationLinked`, and `WorkItemConversationLinkRejected` for conflicting relink attempts as
  additive serialized contracts. This changes the current catalog from 37 entries
  (14 success events + 14 commands + 9 rejections) to 40
  (15 success events + 15 commands + 10 rejections), with new golden-payload coverage.

## 3. Recommended Approach

Selected path: **Direct Adjustment with architecture-led boundary migration**. Do not roll back completed domain
behavior and do not reduce the MVP.

Rationale:

- The EventStore ETag and shared-rebuild implementations already embody the documented runtime truth; changing
  the planning language is lower risk than replacing working mechanisms with obsolete assumptions.
- The domain capability goals remain coherent. FR-21 needs one bounded additive capability, not a requirement
  rewrite or MVP reduction.
- Hosting is the only fundamental boundary change. Moving topology/platform plumbing outward preserves completed
  domain behavior and aligns every future domain module with the shared baseline.
- A rollback would discard validated runtime behavior without resolving where topology should live. Migration
  behind a platform-owned integration lane is safer and keeps the change recoverable.

Effort and timeline impact:

- Planning reconciliation: **low effort / low risk**.
- Conversation-link story: **medium effort / medium risk** because it adds public serialized contracts and
  updates replay, catalog, golden-payload, and projection coverage.
- Hosting migration: **high effort / high risk** because it crosses repository ownership and must preserve live
  command, projection, subscription, reminder, and recovery proofs.
- Overall: reserve **one remediation sprint at minimum**, plus any lead time needed for the platform-host owner to
  accept the topology changes. A calendar commitment should be made only after the target host repository and
  any missing EventStore SDK seams are identified.

Readiness remains `CONCERNS` until the architecture owner names the platform host target and the two remediation
stories are accepted into the backlog. It becomes `READY` when the planning edits are applied and the stories
have testable ownership and dependencies; implementation completion is required before the affected epics become
`done`.

## 4. Detailed Change Proposals

### 4.1 PRD changes

#### PRD §1, §4.7, §6.1, and §8 — hosting ownership

OLD:

> v1 proves the spine ... and an Aspire host that runs it under test.

> FR-24: Run the kernel under an Aspire host. An Aspire AppHost wires Works and its substrate dependencies...

> Package boundaries ... `Aspire`/AppHost ...

NEW:

> v1 proves the spine through a pure domain module and the canonical two-line EventStore domain-service host.
> A platform-owned Aspire AppHost composes Works with its substrate dependencies for local and automated
> integration proof; Works does not ship an AppHost, Aspire, or ServiceDefaults project.

> **FR-24: Run the kernel through the platform-hosted domain-service topology.** Works exposes the canonical
> EventStore domain-service host, and a platform-owned Aspire AppHost composes Works with EventStore and shared
> infrastructure for the end-to-end lifecycle proof. The Works repository contains no `*.AppHost`, `*.Aspire`,
> or `*.ServiceDefaults` project and does not duplicate platform health, telemetry, projection/query, Dapr, or
> subscription plumbing.

> Package boundaries are `Contracts`, `Server`, `Projections`, optional domain-focused supporting libraries,
> a minimal EventStore domain-service executable, and `Testing`; topology and ServiceDefaults remain platform-owned.

Rationale: aligns FR-24 and MVP scope with the current mandatory Hexalith domain-module boundary while retaining
the integration outcome required by SM-1/SM-4.

#### PRD FR-7 — additive event catalog

OLD:

> v1 event catalog ... 14 events: ... `WorkItemExpired`.

NEW:

> v1 event catalog ... 15 events: the existing 14 events plus `ConversationLinked`. The addition is additive,
> preserves every existing payload, and realizes FR-21's post-creation linkage requirement.

Rationale: event-sourced state cannot acquire a later Conversation correlation without a recorded event.

#### PRD FR-21 — make later-link semantics testable

OLD:

> A Conversation correlation ID can be linked to a Work Item at creation or via a later command (and emitted
> on the corresponding event); it is optional and resolved on demand.

NEW:

> A Conversation correlation ID can be supplied at creation or linked later with `LinkConversation`, which emits
> `ConversationLinked`. The first link is authoritative; repeating the same link is an idempotent no-op, while
> attempting to replace it with a different ID is a domain rejection and leaves state unchanged. A later link is
> accepted only while the Work Item is non-terminal; exact duplicate retries remain no-ops after terminal closure.
> Works stores only the correlation ID and never stores conversation content.

Rationale: closes the story gap and defines idempotency, immutability, and terminal-state behavior rather than
leaving each implementation agent to invent them.

### 4.2 Architecture changes

#### Architecture E1 / risk invariants — shared rebuild guarantee

OLD:

> Projection rebuild must be online / non-disruptive (shadow + atomic swap or versioned) ... Rebuild must be
> online, per-tenant, non-blocking.

NEW:

> Projection rebuild is per-tenant, reader-available, and atomically visible at Commit. Independent aggregate
> projections use EventStore's pausable checkpoint rebuild. Relationship-aware Works projections use the bounded
> `/project/rebuild/shared/v1` Begin/Accumulate/Finalize/Stage/Commit lifecycle over a sealed tenant inventory.
> Ordinary projection delivery must be quiesced from inventory capture through Commit, or excluded by an
> equivalent platform fence; delivery resumes and catches up after Commit. Readers continue on the prior
> generation until atomic promotion and never observe staged or partial state.

Rationale: preserves the implemented atomic reader experience without falsely promising uninterrupted live
projection delivery.

#### Architecture starter, project structure, and boundaries — platform-owned hosting

OLD:

> v1 project set includes `Hexalith.Works.AppHost` + `Hexalith.Works.ServiceDefaults`; the adapter ring owns
> delivery, scheduling, and infrastructure and is wired by the Works AppHost.

NEW:

> The Works repository contains domain-centric contracts, aggregate behavior, projection/query handlers, pure
> domain translations, tests, and a minimal `Hexalith.Works` domain-service executable using
> `AddEventStoreDomainService(...)` and `UseEventStoreDomainService()`. The domain-service SDK supplies standard
> endpoints, discovery, service defaults, health, telemetry, read-model policies, and EventStore plumbing.
> A designated platform/host repository owns Aspire topology and integration orchestration. Any missing reusable
> capability is implemented in the platform first, not copied into Works.

Apply this replacement consistently to Requirements Overview, Technical Constraints, Starter Selection,
Infrastructure & Deployment, Implementation Sequence, Structure Patterns, Project Directory Structure,
Architectural Boundaries, Requirements Mapping, Integration Points, Development Workflow, Validation Results,
and Implementation Handoff. Update the architecture diagram/tree to remove Works-owned AppHost and
ServiceDefaults nodes and show the platform AppHost as an external consumer of the Works domain service.

Rationale: the current architecture repeats the obsolete exception in many sections; changing only E1 or the
story text would leave contradictory implementation instructions behind.

### 4.3 Epics requirements-inventory changes

#### NFR-3 / AR-2 / AR-10 — EventStore-owned ETag concurrency

OLD:

> Single-writer/optimistic (expected-version) per Work Item.

> Verify expected-version append.

> Single-aggregate claim under expected-version.

NEW:

> Commands carry no expected-version or ETag input. EventStore owns optimistic concurrency through the Dapr
> state-store ETag on its atomic actor-state save. On a conflict, EventStore rehydrates and re-handles within its
> configured retry policy. For two claims, one save succeeds; a normal retry observes `InProgress` and produces
> the existing `WorkItemTransitionRejected(InProgress, "Claim")`. If the retry budget is exhausted, the result is
> an infrastructure `ConcurrencyConflict`, with no loser append/publication/dead-letter effect.

Rationale: matches the verified API and current deterministic actor/persister tests without leaking substrate
versions into Works commands.

#### NFR-4 / AR-17 — rebuild semantics

OLD:

> Rebuild is online / non-disruptive and per-tenant partitionable (shadow + atomic swap or versioned key).

NEW:

> Rebuild is per-tenant and reader-safe: readers see the prior generation until one atomic Commit. Independent
> aggregate rebuilds use the pausable checkpoint lifecycle; relationship-aware shared rebuilds use the bounded
> staged manifest lifecycle. Ordinary projection delivery is quiesced/fenced for capture-through-Commit and
> catches up afterward. No partial generation is query-visible.

Rationale: distinguishes reader availability from projection-writer availability.

#### AR-1 / AR-22 and FR-24 inventory — hosting project set

OLD:

> Project set includes Works-owned `AppHost` and `ServiceDefaults`; adapters are wired by that AppHost.

NEW:

> Project set excludes Works-owned `*.AppHost`, `*.Aspire`, and `*.ServiceDefaults`. `Hexalith.Works` is a minimal
> EventStore domain-service executable; a designated platform AppHost consumes it. Pure Works handlers and
> translators reference inward; platform runtime/topology references Works from outside the repository boundary.

### 4.4 Existing story corrections

#### Story 1.1 — scaffold target

OLD:

> The repository contains ... `Hexalith.Works.ServiceDefaults`, `Hexalith.Works.AppHost` ...

> Confirm whether expected-version append ... and online rebuild support are available.

NEW:

> The repository contains no Works-owned `*.AppHost`, `*.Aspire`, or `*.ServiceDefaults` project. It contains a
> minimal runnable domain-service host that references `Hexalith.EventStore.DomainService`; integration topology
> is owned by the designated platform host repository.

> Verify that EventStore provides ETag-backed atomic actor-state persistence, conflict retry/exhaustion behavior,
> independent checkpoint rebuild, the bounded shared-rebuild lifecycle, and the required quiescence/fence
> precondition. Do not assume caller-supplied expected versions or uninterrupted live projection delivery.

Historical handling: annotate the completed Story 1.1 implementation artifact as superseded by Story 4.9 for
hosting ownership; do not rewrite its completion evidence.

#### Story 4.3 — single-claim-wins acceptance criteria

OLD:

> **When** both commands use the same expected version
> **Then** exactly one claim succeeds
> **And** the loser receives an observable domain rejection such as `ClaimRejected` or `ConcurrencyRejected`.

> Deterministic tests prove single-claim-wins through expected-version conflict.

NEW:

> **Given** two executors race after EventStore has rehydrated the same persisted `Queued` state
> **When** their candidate updates contend on EventStore's Dapr state-store ETag
> **Then** exactly one atomic save succeeds
> **And** EventStore retries the conflict from freshly rehydrated state
> **And** the normal retry path produces the existing
> `WorkItemTransitionRejected(InProgress, "Claim")`, without adding `ClaimRejected` or
> `ConcurrencyRejected`
> **And** retry exhaustion surfaces an infrastructure `ConcurrencyConflict` without loser-side effects.

> Deterministic tests exercise the real EventStore actor/persister conflict injector and prove save, retry,
> re-handle, publication, and retry-exhaustion behavior without timing-dependent thread races. No Works command
> exposes a version or ETag field.

Historical handling: add a terminology-correction note to the completed Story 4.3 implementation artifact; its
current runtime-proof tests remain valid and are the evidence for the corrected wording.

#### Stories 4.5–4.8 — topology ownership

OLD:

> The Works AppHost wires the runtime, Dapr components, reminders, subscriptions, and recovery proof.

NEW:

> A platform-owned AppHost composes the minimal Works domain service and shared EventStore infrastructure. Works
> supplies domain handlers/contracts only; platform or EventStore SDK components supply generic runtime plumbing.
> The same command/event, reactor, reminder, rebuild, and recovery scenarios remain acceptance requirements and
> are rerun from the platform-owned topology after migration.

Historical handling: preserve completed implementation artifacts as records of the former topology; Story 4.9
owns the migration and replacement evidence.

### 4.5 New Story 1.5 — Link a Conversation After Creation

As a Hexalith builder,
I want to link an existing Work Item to its Conversation after creation,
so that dialogue and the event stream can be correlated without Works storing conversation content.

Acceptance criteria:

1. Given an existing non-terminal Work Item without a Conversation correlation, when `LinkConversation` is
   handled with a valid `ConversationCorrelationId`, then `ConversationLinked` is emitted and replay stores that
   correlation ID without copying conversation data.
2. Given the same correlation is linked again, when the duplicate command is handled in any lifecycle state,
   then the result is `DomainResult.NoOp` and no duplicate event is emitted.
3. Given a different Conversation is linked after one is already authoritative, when the command is handled,
   then `WorkItemConversationLinkRejected` is returned and the stored correlation remains unchanged.
4. Given a terminal Work Item without a link, when a new link is attempted, then the command is domain-rejected
   and terminal closure is preserved.
5. Given `ConversationLinked` is replayed, projections/read models that expose Work Item reference facts converge
   deterministically and the event never contains conversation messages, titles, participants, or profiles.
6. Given serialization and architecture tests run, then the additive catalog contains 15 success events,
   15 commands, and 10 rejections (40 total), golden payloads cover all three new contracts, existing payload
   bytes are unchanged, and no Conversations implementation dependency enters the kernel.

Dependencies: corrected FR-7/FR-21 text. This story is kernel-level and may be implemented independently, but its
platform integration proof should run after Story 4.9 establishes the new host boundary.

### 4.6 New Story 4.9 — Migrate Works Hosting to the Platform Boundary

As a Hexalith platform maintainer,
I want Works hosted through the shared EventStore domain-service SDK and a platform-owned Aspire topology,
so that domain modules contain domain code rather than duplicated hosting and infrastructure plumbing.

Acceptance criteria:

1. Given the Works repository is inspected, then no `Hexalith.Works.AppHost`,
   `Hexalith.Works.ServiceDefaults`, or other Works-owned `*.AppHost`/`*.Aspire`/`*.ServiceDefaults` project
   remains in the solution or published artifacts.
2. Given the runnable Works domain service starts, then its composition uses the canonical
   `AddEventStoreDomainService(...)` and `UseEventStoreDomainService()` SDK path and does not fork platform
   service defaults, health, telemetry, projection/query actors, Dapr wiring, or event-subscription plumbing.
3. Given Works requires domain-specific projections, queries, reminder intents, or reactor translations, then
   they are expressed through the documented EventStore handler/store interfaces and remain pure/domain-focused.
   Any missing reusable runtime capability is added to EventStore/platform first.
4. Given local and automated topology tests run from the designated platform host repository, then create,
   progress, spawn, suspend, child resume, date resume, cascade, claim conflict, query, ordinary projection, and
   shared rebuild scenarios retain equivalent or stronger evidence.
5. Given the shared rebuild runs, then readers remain on the prior generation until atomic Commit, ordinary
   projection delivery is quiesced/fenced during capture-through-Commit, and delivery catches up afterward.
6. Given repository architecture tests run, then they fail if a Works-owned AppHost/ServiceDefaults project or
   generic platform-plumbing implementation is reintroduced.
7. Given migration sequencing is reviewed, then the platform topology is green before obsolete Works hosting
   projects are removed, and rollback consists of restoring the prior host composition until the platform lane
   is accepted.

Dependencies and prerequisite decision: the Solution Architect must name the platform/host repository that owns
the replacement topology. Story 4.9 cannot enter implementation without that target and its owner.

### 4.7 Secondary artifact updates after approval

- Add supersession notes to the historical Story 1.1 and Story 4.3 implementation artifacts.
- Update `docs/boundary-decision-record.md` to name the platform host and the Works/EventStore ownership split.
- Keep `docs/eventstore-api-surface-constraints.md`, `docs/work-roll-up-projection.md`, and
  `docs/whats-next-projection.md` authoritative for ETag and quiesced shared-rebuild behavior.
- Update `docs/lifecycle-transition-matrix.md` with the `LinkConversation` rule without changing existing
  lifecycle transitions.
- Update catalog/golden fixtures and projection payload coverage in Story 1.5.
- Move or replace AppHost topology tests in Story 4.9; retain focused pure Works tests in this repository.
- Update `sprint-status.yaml` only after proposal approval.

## 5. Implementation Handoff

Scope classification: **Major** — the product scope remains stable, but hosting ownership requires a fundamental
cross-repository architecture correction and platform-owner coordination.

- **Product Manager / Product Owner:** approve the unchanged MVP outcome, accept Stories 1.5 and 4.9, and keep
  Epic 1/Epic 4 open until their acceptance evidence is complete.
- **Solution Architect:** name the platform AppHost repository, revise the PRD/architecture/epics consistently,
  decide which current Works runtime components become EventStore SDK capabilities, and approve the migration
  sequence before deletion or relocation begins.
- **EventStore/platform owner:** provide or accept any missing reusable handler, projection, subscription,
  reminder, or recovery seam and own the replacement Aspire topology.
- **Developer:** implement Story 4.9 without losing runtime coverage, then Story 1.5 with additive contract and
  replay coverage. Preserve historical story evidence and existing serialized bytes.
- **Test Architect/QA:** rerun the focused pure suites in Works and the end-to-end scenarios from the new platform
  host, including ETag conflict retry/exhaustion and rebuild delivery fencing.

Success criteria:

- Planning artifacts contain no claim that Works owns AppHost/ServiceDefaults or that callers supply expected
  versions.
- Planning artifacts describe shared rebuild as reader-available/atomically promoted but delivery-quiesced or
  platform-fenced during capture-through-Commit.
- FR-21 traces to a concrete post-creation command/event story and then to executable replay/serialization tests.
- Works contains no prohibited hosting projects or duplicated platform plumbing after Story 4.9.
- The designated platform topology proves all previously covered runtime behaviors before old hosting assets are
  removed.
- Sprint planning reruns with a `READY` verdict, and Epics 1 and 4 reach `done` only after Stories 1.5 and 4.9.

## 6. Checklist Outcome

| Item | Status | Notes |
| --- | ---: | --- |
| 1.1 Triggering story | [x] | Readiness check traces to completed Stories 1.1/4.3/4.5–4.8 and uncovered FR-21 work. |
| 1.2 Core problem | [x] | Technical/platform constraint drift plus incomplete decomposition of an accepted requirement. |
| 1.3 Evidence | [x] | Baseline, full PRD/architecture/epics/UX review, runtime docs, sprint status, project inventory, and command/event catalog inspected. |
| 2.1 Current epic viability | [x] | Epics 1 and 4 remain viable with Stories 1.5/4.9; Epics 2/3 remain valid. |
| 2.2 Epic-level changes | [x] | Add two stories and correct hosting/rebuild/concurrency language; no epic removal. |
| 2.3 Remaining epic impact | [x] | Story 4.8 review evidence must be revalidated after host migration. |
| 2.4 New/obsolete epics | [N/A] | No new or obsolete epic required. |
| 2.5 Priority/order | [x] | Architecture decision → 4.9 host migration → 1.5 platform proof → readiness recheck. Kernel work for 1.5 may begin independently. |
| 3.1 PRD conflicts | [x] | FR-24/hosting, FR-7 catalog, and FR-21 testable semantics require edits; MVP outcome remains achievable. |
| 3.2 Architecture conflicts | [x] | Hosting boundary and E1 rebuild guarantee require broad reconciliation; ETag mechanism retained. |
| 3.3 UX conflicts | [N/A] | v1 remains headless; no screen or interaction change. |
| 3.4 Secondary artifacts | [x] | Boundary/matrix docs, historical notes, tests, catalog fixtures, and sprint status identified. |
| 4.1 Direct Adjustment | [x] Viable | Overall high effort/high risk because of Story 4.9; selected path. |
| 4.2 Potential Rollback | [N/A] Not viable | Discards validated behavior and does not establish correct platform ownership. |
| 4.3 PRD MVP Review | [N/A] | No scope reduction or goal redefinition needed. |
| 4.4 Recommended path | [x] | Direct adjustment with architecture-led cross-repository migration. |
| 5.1 Issue summary | [x] | Section 1. |
| 5.2 Impact summary | [x] | Section 2. |
| 5.3 Recommended path | [x] | Section 3. |
| 5.4 MVP/action plan | [x] | MVP unchanged; Sections 4.5–4.7 define actions and sequencing. |
| 5.5 Handoff plan | [x] | Section 5. |
| 6.1 Checklist completion | [x] | All applicable analysis items addressed. |
| 6.2 Proposal accuracy | [x] | Cross-checked against current baseline, runtime docs, code surface, and sprint status. |
| 6.3 User approval | [!] Action-needed | Awaiting explicit approval after batch review. |
| 6.4 Sprint-status update | [!] Action-needed | Apply only after approval: add Stories 1.5 and 4.9 as backlog. |
| 6.5 Handoff confirmation | [!] Action-needed | Confirm recipients and platform-host owner after approval. |

## 7. Approval Gate

No PRD, architecture, epic, story, sprint-status, source, test, or runtime document has been changed by this
proposal. After review, approval authorizes the planning-artifact and sprint-status edits described above; it
does not by itself authorize cross-repository implementation or deletion of the existing hosting projects.
