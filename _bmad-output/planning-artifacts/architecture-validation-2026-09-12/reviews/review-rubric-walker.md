# Rubric-walker review — Hexalith.Works architecture (2026-09-12)

**Target:** `_bmad-output/planning-artifacts/architecture.md` (read-only; 1,197 lines; binding register at lines 26–453)  
**Intent:** standalone BMad Architecture Validate  
**Evidence baseline:** current repository, current PRD/addendum/epics/sprint status, the required Hexalith baseline, and the 2026-09-08 rubric report (consulted only after the current assessment was formed)  
**Reviewer scope:** every Good-spine checklist item, plus authority/hygiene observations that affect how builders consume the register

## Verdict

**FAIL — UPDATE REQUIRED BEFORE AD-21/AD-22/AD-25 OR STORY 4.9 IMPLEMENTATION.** There is no immediate critical that invalidates the already-built pure kernel, but five high-severity consistency gaps make the document unsafe as the contract for the next cross-repository work: the binding register contradicts the authoritative resume semantics; the post-2026-09-08 product contract is not absorbed; AD-22's “event alone” fan-out source is insufficient; overdue/open obligations are attached to stories and matrix rows that do not carry them; and AD-21's runtime/policy seams remain unallocated while the brownfield system still exposes the authority hole it is meant to close.

## Tiered findings

### Critical

None. The current kernel's implemented resume, completion, projection, and dependency behavior has concrete code/tests/docs, and the architecture explicitly distinguishes several target-state ADs from adopted reality. The gate failure is about unsafe next-step divergence, not evidence of present data corruption.

### High

#### RW12-01 — AD-13 contradicts AD-17, the PRD, the transition matrix, and live code

**Issue.** The register declares itself the winning consistency contract (`architecture.md:28-36`). AD-13 then says “no current match is a no-op” (`architecture.md:159-165`), while AD-17 makes `docs/lifecycle-transition-matrix.md` authoritative (`architecture.md:196-202`). The authoritative matrix says a nonmatching resume is rejected and only a repeat of the consumed condition is a no-op (`docs/lifecycle-transition-matrix.md:165-175`). PRD FR-15 states the same (`_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md:275-285`), and the aggregate enforces it (`src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:223-250`; retained replay state at `src/Hexalith.Works.Contracts/State/WorkItemState.cs:146-163`).

**Divergence prevented only after correction.** Reminder, child-resume, and external-signal builders can otherwise choose incompatible retry/acknowledgement behavior. The register's own precedence rule cannot resolve a conflict between two ADs.

**Recommended disposition:** **autofix** — make AD-13 say: while suspended, a nonmatching condition is a domain rejection that leaves the set intact; after a successful resume, only the consumed condition is an idempotent no-op; any other post-resume condition is rejected. Propagate the correction to narrative lines 466, 688, and 818–820.

#### RW12-02 — The spine does not cover the current product contract

**Issue.** The target was last updated 2026-09-06 (`architecture.md:1-20`) and still claims 25 FRs (`architecture.md:457-469`, `architecture.md:1062-1069`). The PRD was amended on 2026-09-08 and now defines FR-26 plus product-level rules whose independent builders need a shared home: explicit completion at any Remaining (`prd.md:196-205`), terminal and unestimated roll-up contribution/count (`prd.md:231-242`), Unit inheritance at spawn (`prd.md:244-250`), exact resume outcomes (`prd.md:275-285`), the Reactor's cross-aggregate consistency window (`prd.md:298-306`), two exact “what's next” views and predicate (`prd.md:347-354`), and provisional performance acceptance (`prd.md:453`, `prd.md:467-474`). The architecture mapping stops at FR-25 (`architecture.md:988-999`), and its own input list omits the amended baseline (`architecture.md:7-16`). The PRD addendum explicitly assigns the owed architecture edits (`_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md:55-72`).

**Divergence.** A registry author can freeze or not freeze an inherited Unit; projection authors can omit or shape the unestimated-descendant count differently; query authors can expose different views; platform tests can use incompatible convergence criteria. The architecture's “register wins” rule would also override the newer product semantics where they disagree.

**Recommended disposition:** **discuss, then autofix** — confirm that the 2026-09-08 PRD is the current driving spec, record it as such, and re-distill only the load-bearing product calls into stable ADs/rules. Do not copy the whole PRD into the spine.

#### RW12-03 — AD-22's source rule cannot derive contribution “from the event alone,” and its fence behavior is undefined

**Issue.** AD-22 says a descendant delivery writes the descendant's own contribution to every ancestor and that “the value is computable from the event alone” (`architecture.md:318-333`). That is false for the Raw-Act contracts the architecture also binds: `ProgressReported` carries only a delta (`src/Hexalith.Works.Contracts/Events/ProgressReported.cs:8-15`) and `ReEstimated` carries a new estimate but not prior Done (`src/Hexalith.Works.Contracts/Events/ReEstimated.cs:8-15`). The live projection must fold node state before it can derive terminal-aware own Remaining (`src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:204-232`). AD-22 also orders writers to “follow the fence” (`architecture.md:327-333`) while AD-16 admits the capture-through-Commit fence protocol does not exist yet (`architecture.md:184-194`).

**Divergence.** One SDK implementation may fan out event deltas, another may re-read/fold the descendant, and a third may add derived values to payloads, violating the Raw-Act rule. During rebuild, writers may nack, buffer, park, or write another generation because no rule chooses one.

**Recommended disposition:** **discuss** — bind the contribution source as a per-descendant folded projection/state snapshot with a canonical watermark (or another explicit source), not the event alone; bind the fence admission/acknowledgement behavior and make AD-22 acceptance depend on R4's concrete fence protocol.

#### RW12-04 — Revisit/acceptance conditions do not map to executable work, and VAL-H10 is overdue

**Issue.** The open register assigns VAL-H06/H07/H08 to Story 4.9 R7/R6/R4 acceptance and VAL-H09 to its design review (`architecture.md:436-453`); AD-23/AD-24 also make their security controls Story 4.9 acceptance (`architecture.md:344-395`). Story 4.9's actual ACs do not carry the identity provenance, origin-negative tests, positive production-policy reminder test, checkpoint protocol, reminder HA/backup policy, or namespace table (`_bmad-output/planning-artifacts/epics.md:1335-1383`). AD-25 has no implementing story; the sprint file has Story 4.9 in backlog and no registry/fan-out/expiry stories (`_bmad-output/implementation-artifacts/sprint-status.yaml:47-80`). VAL-H10 says it must bind before the registry story is drafted (`architecture.md:449`), but the PRD and addendum now specify the registry contract in detail (`prd.md:252-260`, `prd.md:287-306`; `addendum.md:38-47`). The existing R11 seam carries deterministic causation as MessageId (`src/Hexalith.Works/Runtime/IWorkCommandSubmitter.cs:27-48`; `src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs:18-32`) but does not bind key retention, original-result replay, or the complete transport contract named by VAL-H10.

**Divergence.** Story 4.9 can satisfy every written AC while leaving register obligations open, yet the register's revisit condition would imply closure. The registry design has already crossed its declared safety gate.

**Recommended disposition:** **discuss** — amend Story 4.9 or point each row to a concrete story/backlog item and proof artifact; add registry, fan-out, and expiry stories; treat VAL-H10 as an immediate blocker to further registry implementation, with explicit credit for the deterministic-ID work already present.

#### RW12-05 — AD-21 leaves policy/timer/translation ownership open while brownfield still trusts caller-fed topology

**Issue.** AD-21 binds reserve→spawn→attach/release translations, a platform-configured reservation timeout, registry-authoritative depth/ancestry, and a restriction on direct `SpawnChild` (`architecture.md:272-316`). AD-20's matrix allocates date/expiry reminder work to R6 and only cascade/child-completion work to R7; no row owns registry translations, the reservation-timeout timer, or the point that injects trusted MaxDepth/policy (`architecture.md:254-270`). Current contracts still expose caller-fed ancestry, depth, maximum depth, and existing-parent facts (`src/Hexalith.Works.Contracts/Commands/SpawnChild.cs:6-32`), and the aggregate consumes them as the write-side authority (`src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:39-53`, `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:103-116`). The epics file contains only a pending-additions note, not stories (`_bmad-output/planning-artifacts/epics.md:402-410`).

**Divergence.** Works, EventStore, and Platform builders can each assume another owns the translator, timer, or policy enrichment. Until implementation, ordinary callers can still reach the exact stale/caller-fed authority model AD-21 prevents.

**Recommended disposition:** **autofix after owner confirmation** — extend an existing migration row or add explicit rows for registry translations, reservation timeout, and trusted policy enrichment; add an interim-exposure/open item with a concrete mitigation and revisit condition; sequence the registry story after VAL-H10 and before AD-22.

### Medium

#### RW12-06 — Binding altitude remains ambiguous

The preamble calls every narrative section analysis/rationale/cold-start seed (`architecture.md:28-36`), while the handoff separately says Implementation Patterns are binding (`architecture.md:1177-1187`). Several genuine divergence controls exist only in that prose: zero branching on executor kind (`architecture.md:822-824`), reference-not-copy and the two ports (`architecture.md:826-828`), the Raw-Act/two-counter payload contract (`architecture.md:780-800`), and logging/privacy (`architecture.md:843-848`). Builders can reasonably apply different precedence.

**Recommended disposition:** **autofix** — explicitly name the register plus the exact pattern subsections that are binding, or promote the durable non-obvious rules to ADs.

#### RW12-07 — The operational/environmental envelope is incomplete

The deployment section assigns broad runtime composition to Platform (`architecture.md:695-703`) and R8 mentions recovery alerting (`architecture.md:267`), but the spine neither decides nor explicitly defers secrets ownership/rotation, environment and promotion topology, EventStore backup/restore and DR, regional posture, API-surface versioning, supply-chain policy, or runbook ownership. The one existing operations guide is not cited (`docs/operations/subscriber-dead-letter-operator.md:1-32`, `docs/operations/subscriber-dead-letter-operator.md:62-81`). Scheduler backup is mentioned only as an unbound reminder precondition (`architecture.md:146-149`).

**Recommended disposition:** **defer** — add a compact platform-owned dimensions table with owners and revisit conditions; cite existing operational authority instead of designing it in this domain spine.

#### RW12-08 — Brownfield ratification is only conditional, but document status hides that

The target correctly describes AD-21/22/25 as future work and correctly prohibits host removal before replacement proof (`architecture.md:250-252`, `architecture.md:1096-1111`). Current reality still includes Works AppHost and ServiceDefaults in the solution (`Hexalith.Works.slnx:50-64`), and governance tests intentionally ratify Dapr/reminders in the Works runtime edge (`tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs:10-38`). That is a legitimate migration state, not by itself an architecture violation. However, frontmatter still says `status: 'complete'` and `updatedAt: '2026-09-06'` (`architecture.md:1-6`) while the body is conditional and the driving spec/code moved on.

**Recommended disposition:** **autofix** — expose readiness/target-state status in frontmatter and list the exact interim exceptions. Keep the migration safeguards; do not falsely rewrite current host-edge code as already platform-owned.

#### RW12-09 — Several Rules do not enforce all of their own Binds/Prevents

- AD-12's Rule is only a future revisit instruction, while its clock-free prevention is enforceable now (`architecture.md:151-158`).
- AD-17 binds multiple validation domains but its Rule only names configuration ownership (`architecture.md:196-202`).
- AD-18 says an allowlist is “machine truth” for Testing and the executable (`architecture.md:204-214`), but the allowlist governs only Contracts, Server, Projections, and Reactor (`tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:7-26`).
- AD-19 forbids prose-current version statements (`architecture.md:216-224`) while AD-20 embeds the 13.4.6/13.5.3 transition (`architecture.md:238-242`).

**Recommended disposition:** **autofix** — turn AD-12/17 into falsifiable rules, narrow or extend AD-18's named machine truth, and remove dated “current” pins from AD-20 in favor of build-file authority.

#### RW12-10 — The open register contains resolved or untestable stale state

VAL-H11 is still “Open” and says “before Story 1.5 ships” (`architecture.md:450`), but the repository now has a concrete compatibility matrix that explicitly closes it and binds writer, reader, unknown-type, malformed-payload, and rollout behavior (`docs/eventstore-api-surface-constraints.md:5-24`). VAL-M01 still says the architecture lane currently fails compilation and revisits at a nonexistent “next test-infrastructure story” (`architecture.md:452`). A focused test run in this review could not adjudicate the lane because another process held `Hexalith.EventStore.Contracts.deps.json`; this is an environment/concurrency failure, not evidence for the document's compilation claim.

**Recommended disposition:** **autofix** — close VAL-H11 with the actual authority path and replace VAL-M01 with a verifiable statement plus a named CI/backlog owner.

### Low

#### RW12-11 — Structural hygiene still misleads automated consumers

The input list includes a dangling non-`references/` `Hexalith.Projects` path and omits both correct-course artifacts (`architecture.md:7-16`); the status/frontmatter conflicts with the body's conditional readiness; ADV-U*, SEC-DI-*, EXT-HOST-1, and TECH-01 tokens resolve only if a reader guesses old review folders; the cold-start directory tree lists folders that do not exist (`architecture.md:888-960`).

**Recommended disposition:** **autofix** — refresh metadata/references and label the directory tree explicitly as historical/target seed or remove it from the spine.

#### RW12-12 — No formal parent spine was inherited; the required baseline is nevertheless honored at target state

No parent `ARCHITECTURE-SPINE.md` is named in frontmatter or the register, so parent-AD contradiction checking is not applicable. The required Hexalith baseline is the standing inherited constraint: domain modules must not duplicate platform hosting/runtime plumbing (`references/Hexalith.AI.Tools/hexalith-llm-instructions.md:59-63`, `references/Hexalith.AI.Tools/hexalith-llm-instructions.md:121-125`), must use `.slnx` (`hexalith-llm-instructions.md:65-68`), and use the named stack bands (`hexalith-llm-instructions.md:33-40`). AD-18/19/20 and the target-state boundary honor those constraints. Current Works host projects are explicitly transitional, which must remain visible until Story 4.9 closes.

**Recommended disposition:** **ignore** as a defect; add a short “Inherited constraints” section for traceability during the next update.

## Good-spine checklist scorecard

| Checklist item | Result | Evidence-based assessment |
|---|---|---|
| Fixes all real divergence points for the level below | **FAIL** | Strong on event sourcing, ordering, concurrency, tenancy, and host ownership; misses a derivable fan-out source, exact registry runtime ownership, inherited Unit semantics, new query/read-model contracts, and executable story allocation (RW12-02/03/04/05). |
| Every AD Rule is enforceable and prevents its stated divergence | **FAIL** | AD-13 is wrong; AD-12/17/18/19 have Rule/Binds/enforcement mismatches (RW12-01/09). |
| Nothing Deferred/open can allow units to diverge before revisit | **FAIL** | VAL-H10's due point has passed; Story 4.9 lacks the obligations assigned to its acceptance; AD-25 has no story; multiple R-row protocols remain undefined (RW12-04/05). |
| Named technology is verified-current | **CONCERN / TECHNOLOGY-LENS DEPENDENCY** | Repository authority currently pins .NET SDK 10.0.400 and Aspire AppHost SDK 13.5.3 (`global.json:1-11`), within the inherited bands. AD-19/20's self-contradiction remains. External currentness/fit should be accepted or rejected by the dedicated technology reviewer, not inferred here. |
| Ratifies rather than contradicts the brownfield codebase | **CONCERN** | Most adopted kernel rules match live code. AD-13 contradicts it; AD-21/22/25 are honest target state but their interim exposure and document readiness are insufficiently visible (RW12-01/05/08). |
| Covers the driving spec's capabilities | **FAIL** | The amended PRD has 26 FRs and several new cross-unit contracts; architecture still declares 25 and its mapping ends at FR-25 (RW12-02). |
| Honors inherited parent constraints | **PASS, TRANSITIONAL** | No formal parent spine exists. AD-18/19/20 align the target to the mandatory Hexalith domain/platform boundary and stack; Works-owned hosting remains an acknowledged migration exception (RW12-12). |
| Every structural dimension is decided, deferred, or open | **CONCERN** | Domain/data/test/security directions are extensive, but secrets, environments/promotion, DR/backup, regional posture, API versioning, supply chain, and runbook ownership remain silent (RW12-07). |

**Score:** 1 pass, 3 concerns, 4 fails. The architecture does not pass the standalone validation gate.

## Prior-gate delta (2026-09-08 → 2026-09-12)

The target architecture itself is unchanged (`updatedAt: 2026-09-06`; filesystem modification time also remains 2026-09-06), so no architecture-text finding can be considered fixed by this validation.

| Prior finding(s) | Current classification | Delta |
|---|---|---|
| RW-01 (resume contradiction) | **Persistent** | Live code and the matrix now make the correct behavior especially concrete; AD-13 remains wrong (RW12-01). |
| RW-02 (Story 4.9/AD-25 acceptance allocation) | **Persistent** | Story 4.9 and sprint routing are unchanged; obligations still lack executable ownership (RW12-04). |
| RW-03 (VAL-H11 condition lapsed) | **Partly closed outside the spine** | `docs/eventstore-api-surface-constraints.md:5-24` now contains the missing compatibility contract and calls VAL-H11 closed. The architecture's open row is stale, so metadata/authority reconciliation remains (RW12-10). |
| RW-04 (registry runtime seams unowned) | **Persistent** | AD-20 still has no registry translator/timeout/policy-enrichment row; brownfield code still trusts caller-fed facts (RW12-05). |
| RW-05 (own contribution undefined) | **Persistent** | PRD and live projection define terminal-aware contribution, but the register does not; AD-22's event-alone claim remains untenable (RW12-02/03). |
| RW-06 (2026-09-08 PRD not absorbed) | **Persistent / operationally stronger** | The PRD is now the durable current contract and current code implements key changes, while architecture still claims 25 FRs (RW12-02). |
| RW-07 (freshness witness/fence) | **Persistent** | No creation-evidence field/source or fence behavior has been bound (RW12-03). |
| RW-08 (binding altitude) | **Persistent** | Preamble and handoff still disagree (RW12-06). |
| RW-09 (silent dimensions) | **Persistent** | No ownership/defer table was added; the runbook remains unreferenced (RW12-07). |
| RW-10 (VAL-M01 stale) | **Persistent, unverified mechanically** | Row and revisit condition are unchanged. Today's focused run was blocked by a concurrent file lock, not compilation semantics (RW12-10). |
| RW-11 through RW-14 | **Persistent** | Version-authority contradiction, non-falsifiable rules, metadata/tree hygiene, and AD-18 allowlist scope remain (RW12-09/11). |

**Worsened:** VAL-H10's “before registry story is drafted” condition is now plainly overdue because the amended PRD/addendum carry a detailed registry contract, even though sprint planning has not created the story. Spec drift is also more actionable because code has adopted the newer resume/completion contract.  
**New:** no wholly new source defect was introduced into the unchanged architecture. This review elevates the already-observed AD-22 contribution-source problem into its own high finding because the current event shapes prove “event alone” is not implementable; that is a refinement of prior RW-05/RW-07, not a new regression.

## Recommended update order

1. **Autofix the authority defects:** RW12-01, close/stale register rows in RW12-10, clarify binding altitude, Rules, metadata, and target/interim status.
2. **Hold one architecture discussion before new registry/fan-out work:** choose the folded contribution source and fence behavior (RW12-03), then assign registry translation/timeout/policy seams (RW12-05).
3. **Repair executable planning:** bind the remaining VAL-H10 transport behavior, add registry/fan-out/expiry work, and amend Story 4.9's acceptance or its referenced matrix authority (RW12-04).
4. **Reconcile the 2026-09-08 PRD:** distill only the cross-unit decisions into stable ADs, update the coverage map/count, and record the spec baseline (RW12-02).
5. **Explicitly defer platform dimensions:** owners and revisit conditions for operations/environment concerns (RW12-07).

## Validation note

No architecture source was modified. The focused command

`dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release --no-restore`

did not complete: `GenerateDepsFile` failed because another process held `references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/bin/Release/net10.0/Hexalith.EventStore.Contracts.deps.json`. This concurrent file-lock result is not treated as evidence that the architecture-test source does or does not compile.
