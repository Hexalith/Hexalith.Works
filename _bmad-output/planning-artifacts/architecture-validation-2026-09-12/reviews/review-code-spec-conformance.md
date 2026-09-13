# Architecture Validation — Code / Spec Conformance Lens

- **Target:** `_bmad-output/planning-artifacts/architecture.md`
- **Mode:** standalone Validate, brownfield, platform altitude
- **Repository baseline:** root `HEAD 0cbc7c442da23f0565966fecf43d2d2046855630` on 2026-09-12
- **Comparison baseline:** prior gate at `0527d12`; inspected `0527d12..HEAD`, including `5c86eab` and all root submodule pointer changes
- **Decision rule:** the binding register AD-01…AD-25 was evaluated before legacy prose. `Ratified` means current code/config/tests materially implement the decision; `Drifted` means live behavior or ownership contradicts it; `Target / unbuilt` means the document clearly describes a future seam and current code does not yet provide the complete capability.
- **Spine changed:** no

## Verdict

**FAIL — BLOCKED FOR RELIABLE IMPLEMENTATION / RELEASE HANDOFF.** The deterministic aggregate core and most local projection/query invariants remain strong, and the focused build/test evidence is green. However, current code has a **HIGH, user-visible recovery correctness defect**: both Story 4.7 stream readers skip one event at every multi-page boundary. The v1 expiry trigger (AD-25/FR-10), Registry (AD-21/FR-13/16), generic fan-out (AD-22/FR-11), trusted identity floor (AD-23/24), and target Platform host (AD-20/FR-24) remain unbuilt or incomplete. The current 26-FR PRD is also newer than the architecture and epics, leaving those mandatory capabilities without executable story coverage. No prior architecture-conformance finding is fully closed; two materially worsened and one new HIGH finding was found.

This is not a rejection of the core domain architecture. It is a gate against treating the current code, stories, or Story 4.9 as sufficient proof of the adopted v1/platform target.

## Fresh verification evidence

| Check | Result | Meaning |
|---|---|---|
| Root Git state | `main`, `HEAD 0cbc7c4`, expected origin; review-output files were already dirty/untracked | Assessment is tied to the requested commit. Existing user/concurrent changes were preserved. |
| `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -m:1` | **PASS**, 0 warnings/errors | Architecture test assembly compiles. |
| Direct architecture-test executable | **PASS 237/237**, 0 skipped | Current fitness suite is green, but it does not establish all target capabilities. |
| `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -m:1` | **PASS**, 0 warnings/errors | Current Works code compiles with bumped EventStore source. |
| Seven focused integration classes covering reminders, event processing, link adapter, cascade and child-completion stream readers | **PASS 93/93**, 0 skipped | Existing tests pass. The two stream-reader suites do not exercise a correct multi-page boundary and therefore do not detect DW-86. |
| Live Aspire/mTLS/platform-host lane | Not run; no root-declared `Hexalith.Platform` checkout or `verify-works-host` lane exists in this workspace | No target Platform ownership or production-boundary claim is ratified by this review. |

## AD-01…AD-25 conformance map

Summary: **12 ratified, 7 drifted, 6 target/unbuilt**. `Target/unbuilt` is not automatically a document defect; it becomes release-blocking when the v1 PRD requires the capability but no producing story or acceptance lane owns it.

| AD | Classification | Current evidence | Spec / story consequence |
|---|---|---|---|
| AD-01 | **Ratified** | `WorkItemEventStoreAggregate` is a thin EventStore adapter over pure handlers in `WorkItemAggregate`; the catalog/fitness tests guard handler and payload coverage. | Pure decide/apply boundary remains credible. |
| AD-02 | **Ratified** | Commands/events carry stable typed IDs and `CreateWorkItem` supplies the work-item identity; stream identity is validated at adapter boundaries. | No evidence of generated post-create identity. |
| AD-03 | **Ratified** | `Priority`, rank ordering, and `WhatsNextOrdering` use deterministic identity tie-breaking. | Matches the resolved FR-20 ordering decision. |
| AD-04 | **Ratified** | Progress/re-estimation enforce unit compatibility; `ReEstimate` is the explicit unit-change path. | Current behavior matches the adopted write invariant. |
| AD-05 | **Drifted** | Public code still uses `WorkItemEffort` / `Estimated` / `Done` / `Unit`; no `Meter` type exists. | Semantic behavior is usable, but the binding ubiquitous-language rename is unimplemented. |
| AD-06 | **Drifted** | Live `WorkItemRollUp` stores child IDs and recursively folds child models rather than adopted per-descendant LWW contribution slots. `PersistRollUpAsync` selects the incoming model on an equal source sequence (`WorkItemProjectionDispatcher.cs:446-448`), so concurrent child merges can overwrite each other. | The current projection is not demonstrably convergent under the adopted shape. |
| AD-07 | **Ratified** | `OwnRemaining` and `RolledRemaining` are separate; terminal/completion rules do not mutate aggregate truth from projection totals. | Matches aggregate/projection ownership. |
| AD-08 | **Ratified** | Commands carry no expected version/ETag. EventStore actor serialization plus bounded ETag conflict handling supplies concurrency; claim tests cover one-winner/domain-rejection behavior. | Current mechanism fits the decision. |
| AD-09 | **Ratified** | Event processing and projections are designed for at-least-once, order-tolerant handling and do not claim broker-global ordering. | Current transport posture fits. |
| AD-10 | **Drifted** | Translators are pure, but both re-readable Story 4.7 sources page with `FromSequence = LastSequenceReturned + 1` (`StreamReadingCascadeDescendantSource.cs:83-86`; `StreamReadingChildCompletionAwaitingParentSource.cs:152-159`). EventStore defines `FromSequence` as **exclusive** (`StreamReadRequest.cs:9,15-23`), so one event is silently skipped per page boundary. | Checkpoint/recovery correctness required by FR-10/15/26 is false for sufficiently long streams. See DW-86. |
| AD-11 | **Drifted** | Date reminder registration and indexed reconciliation exist. But `IndexedPendingDateAwaitSource` treats a parked projection as authoritative reason not to read the stream (`:115-126`), while `ReminderReconciliationService` stops forever after the bounded startup attempts (`:37-61`). There is no unpark/replay or persistent degraded-readiness contract. | Durable recovery is conditional on a fallible derived artifact and a one-shot host window, contrary to the authoritative-stream recovery intent. |
| AD-12 | **Ratified** | Expiry is a normal explicit command/event and the kernel reads no clock. | Trigger automation is separately missing under AD-25. |
| AD-13 | **Drifted** | While suspended, a resume with no currently matching condition is rejected (`WorkItemAggregate.cs:237-240`); only a repeat of the last consumed condition in `InProgress` no-ops (`:229-234`). | Binding AD-13 says the no-current-match case is a no-op. PRD/epics also remain internally inconsistent on this point. |
| AD-14 | **Ratified** | `AuthorityLevel` is carried without aggregate authorization branching; fitness tests prohibit policy dependencies in the kernel. | Matches v1 carried-but-unenforced semantics. |
| AD-15 | **Ratified** | Current relationship traversal checks tenant equality at hops and query keys are tenant-scoped. | This ratifies storage/traversal isolation only; trusted tenant provenance remains AD-23/24 target work. |
| AD-16 | **Target / unbuilt** | Shared rebuild Begin/Accumulate/Finalize/Stage/Commit and old-generation reads exist, but no capture-through-Commit ordinary-delivery fence/quiescence protocol exists. | VAL-H08/R4 remains correctly open; reader-safe atomic visibility is not fully proved under concurrent delivery. |
| AD-17 | **Ratified** | Aggregate guards and lifecycle matrix tests cover non-negotiable domain rules; projections do not decide aggregate transitions. | Fits the enforcement boundary. |
| AD-18 | **Ratified** | Dependency tests enforce direct-to-Contracts edges and reject Server/Projections coupling. | Package dependency direction is implemented. |
| AD-19 | **Drifted** | SDK/package pins are centralized, but the active Dapr runtime is hard-coded in Works (`DaprSelfHostedMtls.cs:47,90,111`, `1.18.3`) although the decision allocates runtime/deployment ownership to Platform. | Version provenance describes the target, not the current authoritative runtime pin. |
| AD-20 | **Target / unbuilt** | `Hexalith.Works.AppHost`, `Hexalith.Works.ServiceDefaults`, and `aspire.config.json` remain live. Fitness tests require and pin that Works AppHost (`BuildConfigurationTests.cs:23-45,113-121`). Story 4.9 is backlog. | The migration matrix is not implemented; current fitness protects the interim owner. |
| AD-21 | **Target / unbuilt** | No Work-Tree Registry aggregate/state machine exists. `SpawnChild` remains the caller-fed Work Item path. | FR-13/16 and Registry reserve/attach/release/timeout behavior have no executable story; VAL-H10 still blocks drafting. |
| AD-22 | **Target / unbuilt** | Ordinary projection explicitly reports parent rolled totals unavailable because no relationship fan-out supplies child contributions (`WorkItemProjectionDispatcher.cs:31-37`). | FR-11 generic fan-out has no producing story or available EventStore seam. |
| AD-23 | **Target / unbuilt** | `WhatsNextQueryHandler` trusts `QueryEnvelope.TenantId` (`:47-59`); no proved OIDC claim-to-tenant membership derivation, Reactor workload identity, or delegated-tenant context exists end-to-end. | Mandatory PRD identity provenance is absent from Story 4.9 acceptance. |
| AD-24 | **Drifted** | Works owns partial local mTLS/ACL topology and one positive smoke path. The required negative boundary suite and production positive proof are absent, and the target owner is Platform. | Present controls are neither in the adopted owner nor sufficient production-admission evidence. |
| AD-25 | **Target / unbuilt** | `ExpireWorkItem` exists, but there is no due-date/TTL expiry reminder intent, scheduler, durable registration, reconciliation policy, or expiry-specific option set. | FR-10 promises automatic expiry; no story owns delivery. |

## Current specification, story, and capability coverage

The effective product source is `prds/prd-works-2026-06-14/prd.md` plus `addendum.md`, both updated 2026-09-08. The target architecture is dated 2026-09-06 and still claims a 25-FR baseline; `epics.md` is stamped 2026-09-05 with partial later notes. That ordering matters: the later product contract adds FR-26 and strengthens Registry, Reactor, identity, and acceptance requirements which the downstream planning artifacts have not absorbed.

| Required capability | Current implementation | Current allocation / revisit condition | Disposition |
|---|---|---|---|
| Automatic due-date/TTL expiry (FR-10, AD-25) | Expiry command/event only; no automatic trigger/recovery | No story. R6 is still described as an EventStore target seam. | **Discuss, then add a v1 story before release planning.** Do not count `ExpireWorkItem` as automatic expiry. |
| Registry-owned tree shape and reserve→attach/release (FR-13/16, AD-21) | Absent; old Work Item `SpawnChild` flow remains | Stories 3.1/3.2 are already done under the superseded model. A pending epics note says new work is needed. VAL-H10 must be bound before drafting. | **Discuss / correct-course now.** The prerequisite is still open, and no story can lawfully implement the current PRD until it is resolved. |
| Generic relationship fan-out (FR-11, AD-22, R4) | Absent; ordinary parent roll-up is unavailable and shared rebuild compensates only during rebuild | No producing EventStore story/version and no Works integration story | **Discuss / allocate producer and consumer work.** |
| Reactor/checkpoint/process-runner contract (FR-26, AD-10, R7) | Works contains runtime handlers and checkpointed cascade pieces; EventStore has no generic process-runner seam; two stream readers are incorrect at page boundaries | Stories 3.5/3.6/4.6-4.8 predate FR-26 coverage. VAL-H06 defines the still-open concurrency protocol. | **Fix DW-86 immediately; then correct-course FR-26 acceptance and R7 ownership.** |
| Trusted identity and tenant provenance (PRD §9, AD-23/24) | Partial envelope equality and local Dapr controls; no authoritative identity derivation/membership/delegation proof | Story 4.9 does not enumerate the required positive/negative security cases; R6/R7/R8 remain open | **Add an explicit security slice or expand Story 4.9 before implementation.** |
| Platform-owned host (FR-24, AD-20) | Works-owned AppHost/ServiceDefaults remain and are required by architecture tests | Story 4.9 is backlog; its prerequisite text is stale because AD-20 now names Platform, but no replacement lane exists | **Defer removal; first prove the replacement topology and every migration row.** |
| Two executor views by PartyId (FR-20) | What's-next queue exists; no complete personal/delegated two-view delivery traced here | No explicit story/AD acceptance owner | **Discuss and map to a query story before release.** |
| Unestimated-descendant count/notifier; spawn Unit inheritance; obligation/note bounds | Count and unit inheritance are not represented by the adopted Registry payload/story; generic notifier pieces do not close the product behavior; bounds are absent | Addendum handoffs H10/H12 remain outstanding | **Correct-course architecture and epics.** |

Story/revisit observations:

- Sprint status still has Story 4.8 in `review`, Story 4.9 in `backlog`, and no AD-21, AD-22, or AD-25 delivery stories.
- The epics inventory and coverage map stop at FR-25; FR-26 has no executable coverage. Stories 3.1/3.2 retain the superseded caller-facing Work Item ownership model.
- Story 4.9 covers broad topology parity and shared-rebuild fencing, but not the AD-20 R1-R11 matrix as independently testable rows, nor AD-23/24 claim derivation, membership, mismatch/non-disclosure, workload delegation, forged internal command, topic ACL, and production-ingress negative tests.
- VAL-H10 remains a hard predecessor for the Registry story. VAL-H06/H07/H08 still gate process-runner, reminder, and rebuild concurrency semantics. VAL-H11 is unusual: `docs/eventstore-api-surface-constraints.md:5-23` says Story 1.5 closed it and records the compatibility matrix, while the architecture register still says `Open`. The implementation evidence exists; the spine status is stale.
- The PRD addendum handoff rows H1-H12 remain largely outstanding: especially AD-13 reconciliation, FR-26 coverage, Registry/Reactor story decomposition, Story 4.9 security acceptance, cascade-window confirmation, unestimated descendants, length bounds, and Unit inheritance.

## Platform-boundary ownership audit

The architecture correctly names a target split, but the current repository still owns both domain behavior and general hosting/runtime behavior:

| Boundary row | Current owner/evidence | Conformance assessment |
|---|---|---|
| R1 host topology and ServiceDefaults | Works AppHost/ServiceDefaults; Works tests require them | **Target not reached.** Keep until a green Platform replacement exists, but stop calling the current project a domain boundary. |
| R2/R3 projections and queries | Domain handlers/models in Works over EventStore generic APIs | **Direction is credible.** These are legitimate Works domain extensions. |
| R4 relationship fan-out | No reusable EventStore seam; Works shared rebuild/recursive logic substitutes incompletely | **Target capability absent.** |
| R5 shared rebuild | EventStore supplies generic lifecycle; Works supplies domain graph/handler | **Partially conformant.** Missing live-delivery fence keeps AD-16 open. |
| R6 reminders/reconciliation | Date reminder infrastructure is Works-owned; expiry counterpart absent | **Target capability absent in EventStore/Platform; current owner is interim.** |
| R7 checkpointed process runner | Works owns cascade/recovery; generic EventStore seam absent | **Target capability absent; current reader defect makes parity evidence red.** |
| R8/R9 ACL, mTLS, components | Works AppHost/configuration | **Wrong target owner and incomplete negative proof.** |
| R10 liveness/readiness | Partial Works host checks; reminder recovery can give up without a durable degraded state | **Insufficient for production acceptance.** |
| R11 command gateway | Generic EventStore gateway exists and Works consumes it | **Available and correctly reusable.** |

The EventStore pointer bump to `6b0247a` compiles successfully and contains substantial hosting/auth/admin work, but no new generic R4 fan-out, R6 durable-reminder, or R7 process-runner API was found in the relevant source/diff. The Tenants bump to `2fac183` changes Keycloak/environment and query ETag behavior but does not provide a Works claim-to-tenant integration. Conversations (`b819a7c`) and FrontComposer (`1b3608c`) are mostly planning/schema changes with no closure of the Works migration rows. A submodule pointer advance is therefore compatibility evidence, not Platform-boundary acceptance evidence.

## Tiered findings

### HIGH

#### CONF-0912-01 — NEW — Multi-page reactor recovery silently omits stream events

`StreamReadRequest.FromSequence` is explicitly an exclusive lower bound. Both Story 4.7 re-readable sources set the next request to `LastSequenceReturned + 1`; EventStore will begin after that value and skip one additional event. A child-spawn event at a boundary can vanish from cascade discovery; an await/resume event at a boundary can produce the wrong parent-resume decision. Existing tests cover page budgets, not the correct boundary. The same defect is already accurately recorded as open DW-86.

**Disposition: autofix code/tests, then re-review.** Set the next `from` to `LastSequenceReturned` (as the corrected pending-date-await reader does), retain the opaque continuation token, and add >200-event tests with relevant events exactly at and immediately after boundaries. Include restart/replay assertions, not only returned counts.

#### CONF-0912-02 — PERSISTED — Automatic expiry is a v1 promise with no trigger or story

AD-12 ratifies the pure explicit expiry transition; it does not implement AD-25. No scheduler intent, registration, reconciliation, TTL/due-date policy, or producing story exists. This leaves FR-10 materially unimplemented and can also make a parent never begin the promised expiry cascade.

**Disposition: discuss, then add/estimate an expiry-reminder story.** Bind due-time precedence, reschedule/terminal cancellation, reminder identity, crash recovery, late firing, duplicate no-op, and parity under the Platform-owned R6 lane.

#### CONF-0912-03 — PERSISTED — Current PRD capabilities have no executable downstream baseline

The 2026-09-08 PRD adds FR-26 and makes Registry/Reactor/identity behavior normative. Architecture and epics still claim 25 FRs; Stories 3.1/3.2 encode the prior direct-spawn design; FR-26, Registry, fan-out, and expiry have no implementable story set. The sprint file reflects the old decomposition.

**Disposition: discuss / formal correct-course before further affected implementation.** Resolve VAL-H10; supersede the obsolete Story 3.1/3.2 acceptance rather than silently rewriting historical completion; add Registry, fan-out, expiry, and Reactor recovery slices; update every FR/NFR/SM coverage map.

#### CONF-0912-04 — PERSISTED — Story 4.9 cannot prove AD-20/23/24 or authorize removal of the current host

Story 4.9 names Platform migration but does not turn R1-R11 into producing-version and parity gates. It omits the mandatory identity/trusted-origin positive and negative cases, and EventStore still lacks R4/R6/R7 generic seams. Meanwhile, current fitness tests require the Works AppHost they are eventually meant to forbid. The four submodule bumps do not produce the missing Platform topology or security lane.

**Disposition: discuss / gate Story 4.9 entry.** Expand acceptance to the complete R1-R11 matrix, including source repository/owner, minimum API/version evidence, positive and negative security tests, liveness/degraded readiness, runtime image authority, rollback, and a Platform-owned `verify-works-host` lane. Remove Works hosting only after that lane is green.

### MEDIUM

#### CONF-0912-05 — WORSENED IN SCOPE — Projection parking can permanently suppress authoritative reminder recovery

The `5c86eab` remediation correctly prevents poisoned `/project` delivery from looping and fixes reserved-tenant, copy-on-write, diagnostic, and test gaps. It also deliberately sends identity mismatches and `NotSupportedException` decode failures into terminal parking. `IndexedPendingDateAwaitSource` then skips a parked item without reading its authoritative stream; there is no unpark/replay. A derived projection artifact can therefore veto a valid date await. Startup reconciliation additionally stops after its bounded attempts and never retries until another restart; empty/stale registry and projection-index blind windows remain.

**Disposition: discuss, then implement an operations/recovery contract.** Separate “projection cannot be rendered” from “stream cannot be folded for reminder intent”; add unpark/replay or independent stream recovery, periodic/durable reconciliation, explicit exhaustion/degraded readiness, and blind-window/backfill proof.

#### CONF-0912-06 — WORSENED EVIDENCE — Current roll-up persistence is not the adopted convergent shape

AD-06 calls for per-descendant LWW slots. Current recursive snapshots carry one aggregate-level source watermark. On equal watermarks, `PersistRollUpAsync` chooses the incoming full model; two concurrent child reconciliations can each start from the same parent and the later write can discard the other's child merge. The current read model also cannot distinguish every descendant contribution independently.

**Disposition: discuss; fix before claiming convergence.** Implement the AD-06 slot shape or an equivalently proved CRDT/merge rule with per-child sequence, permutation/duplication/concurrent-merge tests, and shared/live rebuild identity.

#### CONF-0912-07 — PERSISTED — AD-13 resume semantics disagree with live code and planning prose

AD-13 says a resume that matches no current await condition is a no-op. Live kernel code rejects it while suspended, and epics/PRD language mixes no-op and domain-rejection as interchangeable idempotent outcomes. Those are observably different and matter to replay/checkpoint completion.

**Disposition: discuss and choose one normative contract.** If no-op is intended, change kernel/tests/stories; if rejection is intended, update AD-13 and specify how the process runner records a redelivered rejection as already complete without pretending it is the original success.

#### CONF-0912-08 — PERSISTED — Registry and fan-out targets have neither producer capability nor consumer stories

AD-21/22 are not merely refactors: they replace the already-completed direct-spawn/recursive-roll-up model. EventStore does not expose the required generic fan-out seam, and the Registry aggregate/state model is absent from Works. A pending prose note is not executable ownership.

**Disposition: discuss / allocate both sides.** Name the EventStore producer issue and minimum version for R4, then create ordered Registry reserve/attach/release/timeout, Reactor spawn, attached-edge fan-out, and migration/rebuild stories.

#### CONF-0912-09 — PERSISTED — Architecture fitness enforces the interim hosting boundary

Dependency fitness is strong inside the domain packages, but `BuildConfigurationTests` explicitly requires the Works AppHost and `aspire.config.json` to point to it. `KernelDependencyPolicy` treats AppHost/ServiceDefaults as allowed adapters. This guards current operability but not the adopted target that a Works module ships neither project.

**Disposition: autofix planning/test design before migration.** Define a two-phase test policy: interim tests prove the old lane until replacement is green; target boundary tests then reject Works-owned hosting and run equivalent checks from Platform.

#### CONF-0912-10 — PERSISTED — New PRD/addendum handoffs remain outside the binding spine and stories

FR-26 coverage, the cascade-window confirmation, Unit inheritance, unestimated descendant visibility, length bounds, executor views, and security provenance remain in later PRD/addendum text rather than complete AD/story acceptance. Architecture still reports full 25-FR coverage and “no critical gaps,” which is no longer credible.

**Disposition: autofix documentation after semantic decisions.** Update requirements overview, binding register/open findings, implementation handoff, epics inventory, coverage map, and sprint plan as one coordinated baseline.

### LOW

#### CONF-0912-11 — PERSISTED — Meter terminology remains a document/code mismatch

The code's `WorkItemEffort` vocabulary predates AD-05. This is not currently a behavioral defect, but generated APIs, tests, and future stories can split terminology.

**Disposition: discuss.** Either schedule a compatibility-aware rename/alias migration or amend AD-05 to ratify the established public names.

#### CONF-0912-12 — PERSISTED — Binding decisions coexist with contradictory legacy prose

Later sections still describe one Work Item aggregate owning parent/children, recursive roll-up, current host ownership, 25 FRs, and old SDK/version assumptions. Readers who do not privilege the register can implement the wrong design.

**Disposition: autofix.** Mark superseded passages explicitly or replace them after the semantic gate; keep normative rules in the register/open-findings tables.

#### CONF-0912-13 — STALE STATUS — VAL-H11 is implemented in evidence but open in the register

`docs/eventstore-api-surface-constraints.md` explicitly records the concrete-writer/tolerant-reader compatibility matrix, unknown enum/type behavior, and rollout order, and says Story 1.5 closes VAL-H11. The architecture register still labels it open even though its “before Story 1.5 ships” trigger has passed.

**Disposition: autofix.** Verify the cited corpus tests once, then mark VAL-H11 resolved with the evidence link; do not leave a passed revisit condition open indefinitely.

#### CONF-0912-14 — PERSISTED — Several operational rules remain load-bearing prose rather than executable gates

Parking recovery, reconciliation exhaustion, current-to-target runtime pin ownership, and production admission conditions are scattered across comments, boundary records, and deferred work. The register does not consistently expose their status.

**Disposition: defer code where appropriate, but promote the release-relevant rules into named findings/acceptance.**

## Change inspection: `0527d12..0cbc7c4`

| Change | Conformance effect |
|---|---|
| `5c86eab fix: keep date-reminder recovery live after parked projection faults` | **Local closures:** reserved `tenants` rejection is applied at command/projection boundaries; projection failure classification and event IDs improve; parking writes are copy-on-write/idempotent; focused tests broaden. **Architecture delta:** terminal parking now covers more malformed known-event failures, while reminder reconciliation still treats that derived park as reason not to read the stream. CONF-06/15 therefore persist and the parking-veto concern widens. |
| `0cbc7c4 Refactor code structure for improved readability and maintainability` | Root commit primarily records the prior validation artifacts and submodule pointers. It does not update `architecture.md`, the current PRD/epics baseline, or direct Works capability code. |
| EventStore `7598f67..6b0247a` | Current Works builds against it. Generic core projection/query/rebuild/gateway APIs remain available, but no R4 relationship fan-out, R6 reminder/reconciliation, or R7 process-runner capability closes AD-20. |
| Tenants `e7f3666..2fac183` | Keycloak/environment and query ETag changes do not establish Works OIDC claim-to-tenant membership/delegation. |
| Conversations `3d941eb..b819a7c`; FrontComposer `053b200..1b3608c` | No relevant closure of AD-20…AD-25 was found. Treat the pointer changes as dependency movement, not acceptance evidence. |

## Prior-gate delta

The prior code/spec review reported **15 ratified / 4 drifted / 6 not built**. The fresh stricter/current map is **12 / 7 / 6**: AD-10 moved to drifted because DW-86 directly violates re-readable recovery; AD-11 moved from caveated ratification to drift because parking and one-shot reconciliation can suppress the authoritative recovery outcome; AD-19 moved to drift because the live runtime pin is Works-owned. Target count is unchanged.

| Prior finding | 2026-09-12 status | Evidence delta |
|---|---|---|
| CONF-01 — AD-25 automatic expiry absent | **Persisted** | No trigger, options, reminder, reconciliation, or story. |
| CONF-02 — Story 4.9 omits AD-23/24 and migration detail | **Persisted** | Story remains backlog and acceptance is unchanged; submodule bumps supply no Platform lane. |
| CONF-03 — Platform-owned topology remains Works-owned | **Persisted** | AppHost/ServiceDefaults remain; no Platform checkout/lane is root-declared. |
| CONF-04 — fitness requires the Works AppHost | **Persisted** | `BuildConfigurationTests` still pins it. |
| CONF-05 — AD-13 no-op differs from kernel rejection | **Persisted** | Kernel branch is unchanged. |
| CONF-06 — parking artifact vetoes reminder recovery | **Worsened in scope** | `5c86eab` routes more known-event failures to terminal parking; source still skips parked candidates and has no unpark. |
| CONF-07 — PRD amendment not absorbed | **Persisted** | Architecture/epics still stop at 25 FRs while current PRD has FR-26. |
| CONF-08 — AD-21 Registry unbuilt/unstoried | **Persisted** | No aggregate/story was added. |
| CONF-09 — AD-22 fan-out unbuilt/unstoried | **Persisted** | No generic seam/story was added. |
| CONF-10 — Meter naming drift | **Persisted** | Public names remain `WorkItemEffort` etc. |
| CONF-11 — AD-06 live shape differs | **Worsened evidence** | Equal-sequence whole-model overwrite is now explicitly evidenced (also deferred-work line 875). |
| CONF-12 — legacy architecture prose is stale | **Persisted** | No spine update in the commit range. |
| CONF-13 — epics retain superseded semantics | **Persisted** | No epics update in the commit range. |
| CONF-14 — load-bearing rules outside register | **Persisted** | Later PRD/addendum/deferred rules remain unbound. |
| CONF-15 — startup reconciliation gives up after about five seconds | **Persisted** | Service still exits after configured attempts with no periodic rerun/degraded readiness. |
| New — Story 4.7 page cursor skips events | **New HIGH** | DW-86 plus direct EventStore contract/code comparison; focused tests lack the boundary case. |

Narrow code-review items closed by `5c86eab` are acknowledged, but none closes an architecture-level prior finding. No prior finding is fully closed in this gate.

## Gate conditions

Minimum conditions to move from FAIL to a credible implementation gate:

1. Fix and regression-test DW-86 in both stream readers.
2. Correct-course the architecture/epics/sprint baseline to the 2026-09-08 PRD: resolve VAL-H10, allocate Registry, fan-out, Reactor recovery, and automatic expiry, and map FR-26 plus the §9 security NFR.
3. Expand Story 4.9 into a verifiable R1-R11/platform-security migration contract; name producing repositories/versions for absent EventStore seams and establish a Platform-owned parity lane before deleting the Works host.
4. Resolve AD-13 and AD-06 semantics in code or in the binding decisions; do not leave rejection/no-op and recursive/slot models both normative.
5. Make reminder reconciliation independently recoverable from projection parking and observable after retry exhaustion.

The green builds and 330 focused tests are positive evidence for the code that exists. They do not waive the five conditions above or convert explicitly target-state seams into shipped v1 capability.
