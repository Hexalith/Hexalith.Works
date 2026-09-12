# Architecture Spine Validation — Brownfield Code & Spec Conformance Lens

**Target:** `_bmad-output/planning-artifacts/architecture.md` (updatedAt 2026-09-06; register AD-01…AD-25 binding, lines 26–453)
**Reviewed:** 2026-09-08 (read-only; repo HEAD `0527d12`, working tree clean except this review folder)
**Lens:** does the register ratify what the code already does, is post-2026-09-06 change still inside the ADs, and does the register cover the driving spec (PRD 2026-06-14 as amended 2026-09-08, addendum, epics.md, sprint-status.yaml)?

## Verdict

**PASS WITH CONCERNS** — 15 of 25 ADs are ratified by live code and 6 are honestly marked as not-yet-built, but three high findings remain: a v1 in-scope FR (FR-10 automatic expiry, AD-25) has neither code nor a story; Story 4.9 carries none of the AD-23/AD-24 acceptance the register makes it responsible for; and since the spine was bound on 2026-09-06 Works has taken on platform-owned R1/R10 topology work (mTLS control plane, TLS placement/scheduler, JWT dev-key composition) that the register and open-findings table do not know about.

---

## Sweep A — Ratification table (spine vs live code)

Status legend: **RATIFIED** (code matches) · **DRIFT** (code ≠ AD; the "right side" is named) · **NOT-YET-BUILT** (future work; "marked?" says whether the AD/prose flags it) · **PROSE-CONTRA** (secondary flag: legacy prose still says the opposite).

| AD | Status | Evidence (file:line) | Note |
|---|---|---|---|
| AD-01 Event-sourced kernel | RATIFIED | `src/Hexalith.Works/WorkItemEventStoreAggregate.cs:12-70` (thin delegation to pure kernel); `src/Hexalith.Works.Contracts/Commands/SpawnChild.cs:4` `[PolymorphicSerialization]`; `src/Hexalith.Works.Contracts/Events/Rejections/*` (`IRejectionEvent`); `src/Hexalith.Works/Runtime/WorksHost.cs:44,130` | Persist-then-publish is EventStore-owned; no parallel persistence scheme found. |
| AD-02 ID at the edge | RATIFIED | `Contracts/Commands/CreateWorkItem.cs` (`WorkItemId` on the command); `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:187-205` (bans `Guid.NewGuid`, `DateTime.*Now`, `Random` in kernel) | Transport idempotency correctly left open (VAL-H10). |
| AD-03 Priority enum + identity tiebreak | RATIFIED | `Contracts/ValueObjects/Priority.cs:8-12`; `Projections/Strategies/WhatsNextOrdering.cs:24,71` (absent rank 4; `WorkItemId` ordinal tiebreak); `docs/whats-next-projection.md:24-34` | Enum carries an extra `Unknown = 0` sentinel the AD does not mention (harmless). |
| AD-04 Unit immutable | RATIFIED | `Server/Aggregates/WorkItemAggregate.cs:294,340` (unit mismatch → rejection); `Contracts/ValueObjects/WorkItemEffort.cs:45` (`ReEstimate` preserves Unit) | |
| AD-05 Cost-ready `Meter` | **DRIFT** (naming) + PROSE-CONTRA | `Contracts/ValueObjects/WorkItemEffort.cs:1-26` (`WorkItemEffort(Estimated, Unit, Done)`, derived `Remaining`); no type named `Meter` anywhere under `src/` | Semantics ratified; the register (`architecture.md:77`), prose (`:668, :802, :921`) and epics AR-7 name a type that does not exist. **Code is right**; AD should say "`WorkItemEffort` (the Meter shape)". → CONF-10 |
| AD-06 Per-descendant LWW slots | **DRIFT** (document shape) + PROSE-CONTRA | Invariant OK: `Projections/Strategies/WorkItemRollUpProjection.cs:127,373` (per-node LWW by own sequence, never deltas); `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs:61-91` (3 levels). Shape differs: `:234-258` recursive `CalculateRolled`; `Contracts/Models/WorkItemRollUp.cs` has no slot collection; runtime persists parent rolled as unavailable (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:34`) | The "flattened slot document" is the AD-22 target, not the adopted shape. `architecture.md:465` still prints the retired recursive formula. → CONF-11, CONF-12 |
| AD-07 Type-separated authority | RATIFIED | `Contracts/Models/OwnRemaining.cs:5` vs `RolledRemaining.cs:5`; `WorkItemAggregate.cs:310-314` (Remaining 0 → `WorkItemCompleted` synchronously) | Matches PRD FR-8 (2026-09-08) two-acts-one-event. |
| AD-08 EventStore-owned claim concurrency | RATIFIED | no `Version`/`ETag` member on any `Contracts/Commands/*.cs`; `tests/…IntegrationTests/WorkItemClaimPersistenceConflictTests.cs` + `ConflictInjectingActorStateManager.cs` (ETag path); `tests/…UnitTests/WorkItemClaimConcurrencyTests.cs` (turn-serialized rejection) | Both required test paths exist. |
| AD-09 At-least-once, no ordering reliance | RATIFIED | `src/Hexalith.Works.AppHost/DaprComponents/pubsub.yaml:25` (`pubsub.redis`, no ordering claim); `WorksHost.cs` `AddDaprEventStoreDomainEventMarkerStore()` (offset dedup); AD-06 order-tolerant fold | |
| AD-10 Reactor outside kernel, mechanical | RATIFIED | `Reactor/TerminalCascadeTranslator.cs:76-83`, `Reactor/ChildCompletionResumeTranslator.cs:16-21` (pure selection only); `DependencyDirectionTests.cs:23-25` (Reactor → Contracts only); `Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:50` (monotonic checkpoint) | VAL-H06 (CAS protocol) correctly still open; checkpoint progress writes are LWW today. |
| AD-11 Durable reminders, reconciled discovery | RATIFIED (with new caveat) | `Reminders/DateReminderName.cs:59` (tenant-inclusive SHA-256 name); `Reminders/WorkItemSuspendedReminderHandler.cs:27-44` (registration from folded pending set); `Reminders/IndexedPendingDateAwaitSource.cs` (registry → index → per-aggregate stream re-fold) | Post-update caveat: `IndexedPendingDateAwaitSource.cs:114-126` (commit `0527d12`) lets a projection-side parking record veto the authoritative stream read. → CONF-06; reconciliation pass gives up after ~5 s → CONF-15 |
| AD-12 Advisory-until-fired | RATIFIED | `docs/lifecycle-transition-matrix.md:51`; `Contracts/Commands/ExpireWorkItem.cs` carries no instant; `WorkItemAggregate.cs:457-471` | |
| AD-13 Await set; idempotent resume | **DRIFT** (AD wrong) | `WorkItemAggregate.cs:229-240`: non-matching resume while `Suspended` → `WorkItemTransitionRejected`; only a repeat of `WorkItemState.LastConsumedAwaitCondition` (`Contracts/State/WorkItemState.cs:49`) is `NoOp` | AD rule (`architecture.md:164`) says "no current match is a no-op". Code, PRD FR-15 (`prd.md:285`, 2026-09-08) and Story 3.5 AC (`epics.md:978-988`) all say rejection. **Code/PRD are right.** → CONF-05 |
| AD-14 AuthorityLevel carried not enforced | RATIFIED | `Contracts/ValueObjects/AuthorityLevel.cs`; grep of `Server`/`Projections`/`Reactor` for `AuthorityLevel` or binding-kind branching returns nothing; `ScaffoldGovernanceTests.cs:309` `P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority` | |
| AD-15 Tenant isolation every layer | RATIFIED | `WorkItemRollUpProjection.cs:95,171` (`AllowsEdge` per hop) + `Strategies/WorkItemRollUpTenantIsolation.cs`; `Queries/WhatsNextQueryHandler.cs:103` (`WhatsNextQueryAuthorization.FilterList`); `Reactor/TerminalCascadeTranslator.cs:83` (fail-closed tenant equality); `tests/…UnitTests/WorkItemRollUpTenantIsolationTests.cs` | "Mutation-validated" (RR-4) not verifiable from test text — no mutation harness found; treat as unproven. VAL-H09 correctly open (`ReadModelCascadeCheckpointStore.cs:22` global index key). |
| AD-16 Reader-safe rebuild, fence | NOT-YET-BUILT (fence) — **marked** | `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs` (Begin/Accumulate/Finalize/Stage/Commit exists); fence protocol absent (VAL-H08 row, R4) | Honestly labelled. |
| AD-17 Validation domains | RATIFIED | `WorkItemAggregate.cs:283` (`DoneDelta <= 0` rejected); `WorkItemEffort.cs` (`ThrowIfNegative`); `docs/lifecycle-transition-matrix.md` + `FitnessTests/LifecycleTransitionMatrixDocTests.cs` | Platform-config TTL/Due-Date not built (see AD-25). epics AR-6 still says "delta ≥ 0" → CONF-13. |
| AD-18 Direct-to-Contracts graph | RATIFIED | `tests/…ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:10-25` (exact allowlist: Server/Projections/Reactor → Contracts); `tests/Hexalith.Works.Testing/Hexalith.Works.Testing.csproj:4` (→ Contracts); `src/Hexalith.Works/Hexalith.Works.csproj` (4 kernel refs + `Hexalith.EventStore.DomainService`) | epics AR-22 (`epics.md:298`) still prints the legacy chain `Contracts ← Server ← Projections` → CONF-13. |
| AD-19 Version authority in build files | RATIFIED | `global.json` (SDK 10.0.400, `Aspire.AppHost.Sdk` 13.5.3); `references/Hexalith.Builds/Props/Directory.Packages.props:113-121,139-146` (single Aspire 13.5.x family; Dapr SDK 1.18.5); spine version table labelled historical (`architecture.md:606-618`) | New drift: Dapr **runtime** image tag `1.18.3` is now a literal in Works code (`AppHost/DaprSelfHostedMtls.cs:90,111`) — AD-19 says that pin is platform-owned → CONF-03. epics AR-20 stale `10.0.301` → CONF-13. |
| AD-20 Platform host `Hexalith.Platform` | NOT-YET-BUILT — **marked** (Story 4.9 `backlog`) | `src/Hexalith.Works.AppHost/*`, `src/Hexalith.Works.ServiceDefaults/*` still present; `FitnessTests/KernelDependencyPolicy.cs:14-16` classifies both as *allowed* adapter projects; `FitnessTests/BuildConfigurationTests.cs:29-44,114-120` **require** the Works AppHost and `aspire.config.json` to exist | Directory-structure prose (`:888-963`) and starter table (`:598-603`) describe the target and label the migration — not misleading. But the fitness lane currently asserts the *opposite* of AD-20's rule → CONF-04; and R1/R10 seams grew after binding → CONF-03. |
| AD-21 Work-Tree Registry | NOT-YET-BUILT — **marked** (gap analysis `:1104-1105`; Epic 3 note) | `Contracts/Commands/SpawnChild.cs:29-32` (caller-fed `ProposedParentAncestors`/`MaxDepth`/`ExistingChildParent`); `Server/Aggregates/WorkTreeAttachmentGuard.cs:24-57` treats them as authority; `docs/work-tree-shape-guard.md:3-6,33` documents caller-fed facts with no AD-21 note; no registry story in `sprint-status.yaml` | The transition state is the exact hole AD-21 "Prevents"; no interim mitigation named. PRD FR-13/FR-16 (2026-09-08) already state the registry as the public act; Story 3.2 ACs contradict it. → CONF-08; prose `:463` "single aggregate root" → CONF-12 |
| AD-22 Registry-backed fan-out | NOT-YET-BUILT — **marked** in AD text, **not** in open register, **no story** | `WorkItemProjectionDispatcher.cs:34` ("Parent rolled totals … persisted and exposed as unavailable"); `docs/work-roll-up-projection.md:67-73` | AD says "FR-11 stands as written" but FR-11's incremental ancestor delivery is unmet at runtime and nothing owns closing it. → CONF-09 |
| AD-23 Identity provenance / authz floor | NOT-YET-BUILT — **marked** (Story 4.9 acceptance) | Today: `WorkItemEventStoreAggregate.cs:40-48` (envelope↔payload identity check for `LinkConversation` only); `Runtime/WorksEventIdentity.cs:11-37` (payload equality, asserted not derived); `Queries/WhatsNextQueryHandler.cs:51-54` (trusts `QueryEnvelope.TenantId`) | Story 4.9 ACs (`epics.md:1335-1383`) carry none of it. → CONF-02 |
| AD-24 Trusted origin | **DRIFT** (built ahead, in the wrong owner) + NOT-YET-BUILT (negative suite) | `AppHost/DaprComponents/accesscontrol.works.yaml:8,15-44` (mTLS + deny-by-default ACL, "Local development only" exemption recorded in Works, not `Hexalith.Platform`); `AppHost/DaprSelfHostedMtls.cs:36,80` (Sentry + TLS placement/scheduler); `tests/…IntegrationTests/WorksMtlsAuthorizationSmokeTests.cs:27` (one allow/deny fact) | R10 target owner is `Hexalith.Platform`; the AD-24 negative suite (spoofed app-id, wrong trust domain, forged `SequenceNumber`, direct `SpawnChild`) and the positive production-policy reminder test do not exist. → CONF-03, CONF-02 |
| AD-25 Expiry reminders | NOT-YET-BUILT — **NOT marked as unowned; no story** | grep `ExpireWorkItem` under `src/Hexalith.Works/` hits only `Recovery/Cascade/CascadeCommands.cs`, `CascadeCheckpoint.cs` (cascade of an already-expired parent); no expiry reminder adapter, no TTL/Due-Date options; `sprint-status.yaml` has no expiry story; epics FR-10 → Epic 2 (Story 2.5 done: command only) | R6 (`architecture.md:265`) lists "expiry reminders (AD-25)" as a seam "today in Works" — it is not. PRD FR-10 (`prd.md:221-222`) and §6.1 (`:415`) put it in v1 scope. → CONF-01 |

**Tally:** 15 RATIFIED · 4 DRIFT (AD-05, AD-06, AD-13, AD-24) · 6 NOT-YET-BUILT (AD-16, AD-20, AD-21, AD-22, AD-23, AD-25; AD-25 and AD-22 lack an owning story) · 3 carry PROSE-CONTRA secondary flags (AD-05, AD-06, AD-21).

**Directory-structure seed vs real tree (`find src tests -maxdepth 2`):** the seed (`architecture.md:888-963`) is a labelled *target*: it omits `Hexalith.Works.AppHost`/`ServiceDefaults` with an explicit "Story 4.9 migrates … before removal" comment, so it does not contradict AD-20. It does mis-describe what exists: `Contracts/Results/`, `Server/Validation/`, `Server/Registration/` (`:920, :928-929`) do not exist (real: `Contracts/{Commands,Events,Models,Ports,State,ValueObjects}`, `Server/{Aggregates,Resolvers}`); `tests/Hexalith.Works.Testing` holds only `WorkItemStateBuilder.cs` + `WorksTestingAssembly.cs`, not the `InMemoryEventLog`/`ReorderingProjectionDriver`/`RollUpProjectionBuilder` named at `:598, :712, :777, :954`; `docs/` lists one file, reality has six plus `docs/operations/`; the executable is shown as two files (`:944-946`) while today it holds `Runtime/`, `Reminders/`, `Recovery/`, `Projections/`, `Queries/` — acknowledged by the R3–R8 "today in Works" column, so acceptable if the seed says so.

---

## Sweep B — Post-update drift (changes since 2026-09-05)

| Change (commit, date) | What moved | AD(s) touched | In spine register / memlog? | Assessment |
|---|---|---|---|---|
| `33a27e2` 2026-09-06 20:19 "Implement mTLS for Dapr Sentry" | `AppHost/DaprSelfHostedMtls.cs` (+166: Sentry container, AppHost-owned TLS placement + scheduler `daprio/dapr:1.18.3`, trust-anchor injection), `sentry.yaml`, mTLS blocks in all four `accesscontrol*.yaml`, `WorksAppHostTestReadiness.cs` (+258), `WorksReminderRecoveryPipelineSmokeTests.cs`; `global.json` Aspire 13.4.6 → 13.5.3; **same commit** carried the VAL-H03 spine/PRD/epics/BDR edits | AD-20 R1/R10 (owner = `Hexalith.Platform`), AD-24 (sandbox exemption "recorded in the `Hexalith.Platform` repository"), AD-19 (Dapr runtime pin now a Works literal) | **No.** Register knows only the VAL-H03 closure; `sprint-change-proposal-2026-09-06.md` is VAL-H03/VAL-H10/BDR only; the mTLS scope expansion is recorded only in `4-8-register-and-reconcile-date-reminders-durably.md:370` ("after the human approved the Sentry/mTLS scope expansion"). Memlog (`prd .memlog.md:18`) even notes the VAL-H03 edits "travelled in an unrelated feat commit". | Works took on platform-owned R1/R10 work the day the register bound it elsewhere. Not a contradiction of the AD-20 *rule* (nothing removed) but it enlarges the migration surface and moves the R10 proof into the Works lane. → CONF-03 |
| `42c4318` 2026-09-08 08:51 "WorkItemProjectionParking + WorksProjectionOptions" | `AppHost/Program.cs` (+50: OIDC client id/user/password parameters; dev symmetric-key JWT validation env for `eventstore`/`eventstore-admin`), `DaprSelfHostedMtls.cs` (+56), `WorksAppHostSmokeHarness.cs` (+442), new `WorksMtlsAuthorizationSmokeTests.cs`, `WorksRecoveryOptionsTests.cs`; new parking record + budget; `BuildConfigurationTests.cs` (+24: pins Aspire 13.5.3, AppHost SDK attribute, `aspire.config.json` → Works AppHost) | AD-20 R1 (ingress/JWT composition), AD-23 (ingress auth is platform-owned), AD-11 (parking as a new terminal disposition in the reminder discovery chain) | No register row; parking is documented in `docs/eventstore-api-surface-constraints.md:295-300` and Story 4.8 only. | More R1 growth; the fitness lane now *asserts* the Works AppHost exists (→ CONF-04). Parking introduces a projection-side terminal state the ADs do not define (→ CONF-06). |
| `39643e5` 2026-09-08 | `PendingDateAwaitIndex.cs` → `PendingDateAwaitTenantIndex.cs` | AD-11 (per-tenant index) | n/a | Consistent with AD-11 "per-tenant pending-await index". |
| `0bb802a`, `0e5c126`, `a7e72c2` 2026-09-08 | EventStore/Parties/FrontComposer submodule bumps; Story 4.8 → `review`; `test-summary.md` | AD-19 | n/a | Consistent (pins live in submodule/build files). |
| `e9f3e22`, `6e2fb4d` 2026-09-08 | PRD validation report (grade Fair, "material drift"); PRD/addendum amendment begun; memlog created | — | Memlog `:19` records that AD-21…25 were never routed back to the PRD; `:20` records the drift verdict. Spine unchanged. | Spine's own memlog/open register has no entry for the PRD validation or the incoming amendment. |
| `0527d12` 2026-09-08 15:16 (the formerly-uncommitted set) | `Reminders/IndexedPendingDateAwaitSource.cs:114-126` (skip candidates whose `WorkItemProjectionParking.Parked == true`, no stream read, not counted incomplete); `Runtime/WorksRecoveryLog.cs` (EventId 4607, **Information**); 2 new facts in `IndexedPendingDateAwaitSourceTests.cs`; `sprint-status.yaml` 4.8 `review → in-progress`; `epic-4-context.md` (new lines on one-binding-one-doer, Claim-only entry, actor = authenticated identity, Reactor-owned spawn); **PRD amended** (`prd.md` +129/−?: FR-26 new, FR-5/13/16 rewritten around the registry, FR-6 normative table, FR-15 resume rule, FR-20 two views, §9 identity-provenance NFR, §13 resolved-by-AD table); `addendum.md` handoff table H1–H12 | AD-11 (discovery aid now vetoes truth), AD-20 R8 (no degraded readiness for parked/unreconciled items), AD-13 (PRD now contradicts the AD rule), AD-21/AD-22/AD-23/AD-24/AD-25 (PRD adopted them), AD-03 (PRD adds a PartyId filter the AD lacks) | **No.** `architecture.md` frontmatter still `updatedAt: 2026-09-06`; addendum `:55-72` explicitly lists edits owed to `architecture.md` (H1, H2, H8, H12) and `epics.md` (H3–H6, H9–H11). | The PRD is now *ahead of* the spine on FR-15, FR-20 (two views), FR-11 (unestimated count, notifier), FR-12 (unit inheritance), FR-2/FR-7 (bounds), FR-26. → CONF-05, CONF-06, CONF-07 |
| deferred-work 2026-09-07 (code state, not a new commit) | `Reminders/ReminderReconciliationService.cs:37-61`: startup reconciliation = 5 attempts × 1 s fixed delay, then never again until restart; `IndexedPendingDateAwaitSource.cs:41-51` blind window / no backfill | AD-11 "missed firings are reconciled by the indexed recovery pass"; R8 "exhaustion ⇒ durable evidence + degraded readiness" | Not in open register (VAL-H07 row covers Scheduler HA, not the Works-side pass). | The narrow AD-11 guarantee holds only within a ~5 s startup window; exhaustion produces a log, not degraded readiness. → CONF-15 |

**Does anything outrun or contradict an AD?** Nothing removes a Works hosting project or violates an AD-20 *rule*. But (a) R1/R10 seams *grew* in Works after ownership was bound to `Hexalith.Platform` (mTLS, control plane, JWT dev keys), (b) the AD-11 discovery protocol gained a projection-side veto and a 5 s give-up that the AD does not sanction, and (c) the PRD amendment landed decisions (FR-15 rule, FR-20 PartyId view, FR-26) the register does not carry. None of it is in the open-findings register or the sprint-change proposals.

---

## Sweep C — Spec coverage (FR / NFR / story → governing AD or GAP)

### PRD functional requirements (prd.md, amended 2026-09-08)

| FR | Governing AD(s) | Story (status) | Coverage |
|---|---|---|---|
| FR-1 Create | AD-01, AD-02 | 1.2 done | OK. PRD "creation with binding rests at Created" (`prd.md:116`) matches `WorkItemState.cs:110`. |
| FR-2 Obligation + Expectation ref | AD-01; ports: **none** | 1.2, 1.4 done | Ports (`IExpectationResolver`) governed only by prose (`architecture.md:678`). 4,000-char bound (`prd.md:123`) not in AD-17. **GAP (AD-level, low)** |
| FR-3 Burn-down | AD-04, AD-05, AD-17 | 2.3 done | OK (type-name drift, CONF-10). |
| FR-4 Schedule | AD-03 | 2.4 done | OK. |
| FR-5 Parent/children refs, await set | AD-13, AD-21 | 3.1/3.2/3.5 done | Registry half not built; PRD (`prd.md:143-149`) already registry-shaped. **Partial** (CONF-08). |
| FR-6 State machine (normative table 2026-09-08) | AD-17 (matrix authoritative) | 2.1 done | OK; two tables must stay in lock-step (addendum H7). |
| FR-7 Raw-act events; actor = authenticated identity | AD-01, AD-23 | 2.2 done | OK; registry/spawn-rejection catalog additions unowned (see FR-16). |
| FR-8 Progress + two completion acts | AD-07, AD-17 | 2.3 done | OK (`WorkItemAggregate.cs:310-314`). |
| FR-9 Re-estimate/reschedule | AD-04, AD-17 | 2.4 done | OK. |
| FR-10 Cancel/reject/expire + cascade + **automatic expiry trigger** | AD-12, AD-10 (cascade); **AD-25 (trigger)** | 2.5 done (commands), 3.6 done (cascade); **no story for the expiry reminder adapter** | **GAP (story-level, high)** — CONF-01 |
| FR-11 Recursive roll-up, incremental to every ancestor, unavailable semantics, unestimated count, notifier | AD-06, AD-07, **AD-22**; unestimated-descendants count + change notification (`prd.md:241-242`): **no AD** | 3.3 done; **no AD-22 story** | Runtime persists parent rolled as unavailable — **GAP (story-level, medium)** CONF-09; count/notifier **GAP (AD-level, low)** CONF-07 |
| FR-12 Heterogeneous units; unit inheritance at spawn | AD-04; inheritance (`prd.md:249`): **no AD** | 3.4 done | **GAP (AD-level, low)** — addendum H12 says carry into AD-21 payload. |
| FR-13 Tree guard = registry at reservation | **AD-21** | 3.1 done (pure guard only); **no registry story** | **GAP (story-level, high-ish; VAL-H10-gated)** — CONF-08 |
| FR-14 Suspend | AD-13 | 3.5 done | OK. |
| FR-15 Resume rule | AD-11, AD-13 | 3.5 done | AD-13 rule contradicts PRD/code — CONF-05. |
| FR-16 Spawn via registry reserve; direct `SpawnChild` denied | **AD-21, AD-24** | 3.2 done (direct `SpawnChild`); **no registry story** | Story 3.2 ACs (`epics.md:848-880`) conflict with AD-21 rule — CONF-08. |
| FR-26 Reactor owns cross-aggregate coordination (new) | AD-10, AD-20 R7/R11 | 3.5/3.6/4.6–4.8 done (implicitly); epics FR coverage map (`epics.md:362-389`) has **no FR-26 row** | Mapping gap (addendum H5) — CONF-13. PRD FR-26 consistency-window assumption → open register row owed (H8). |
| FR-17 Uniform binding, zero branching | AD-14; **no-branch rule: prose only** (`architecture.md:815-817`) | 4.1/4.2 done; fitness `ScaffoldGovernanceTests.cs:309` | Load-bearing rule outside the register — CONF-14. |
| FR-18 Push/pull, single claim wins | AD-08 | 4.3 done | OK. |
| FR-19 AuthorityLevel carried | AD-14 | 4.1 done | OK; PRD blast-radius note (`prd.md:337`) consistent with AD-23 floor. |
| FR-20 What's next: ordering + **two views by PartyId** | AD-03, AD-15, AD-23; two-view filter (`prd.md:351`): **no AD, no story** | 4.4 done (single view; `WhatsNextItem.cs` / `WhatsNextQueryHandler.cs` have no executor parameter) | Tiebreak consistent everywhere after the 2026-09-06 correct-course (PRD `:352`, epics `:128-131,1163`, AD-03, `whats-next-projection.md:24-34`, `WhatsNextOrdering.cs:71`). **GAP (two views; low-medium)** — CONF-07 |
| FR-21 Reference, never copy; LinkConversation | AD-01 (serialization); reference-not-copy: prose + BDR | 1.3, 1.5 done | OK (BDR is the tracked artifact). |
| FR-22 Ports | **no AD** (prose `:678`) | 1.4 done | **GAP (AD-level, low)** — CONF-14 |
| FR-23 Boundary decision record | prose `:1000`; AD-20 references | 1.4 done | OK — `docs/boundary-decision-record.md:9-14,99-105` consistent with AD-20 (names `Hexalith.Platform`, removal gated on green matrix rows). |
| FR-24 Platform-hosted runtime | AD-20 | 4.9 backlog | OK (marked). |
| FR-25 Command pipeline in tests | AD-20 lane; test taxonomy prose (`:544`) | 4.5 done | OK. |

### Non-functional requirements (PRD §9/§10; epics NFR-1…12)

| NFR | Governing AD | Coverage |
|---|---|---|
| Tenant isolation (NFR-1) | AD-15 (+VAL-H09 open) | OK; RR-4 mutation validation unproven. |
| **Identity provenance & trusted origin** (PRD §9, added 2026-09-08) | AD-23, AD-24 | **GAP (story-level, high)** — no story AC carries it; Story 4.9 silent — CONF-02. |
| Event-sourcing invariants (NFR-2) | AD-01 | OK. |
| Concurrency (NFR-3) | AD-08 | OK. |
| Rebuildable projections (NFR-4) | AD-16 (+VAL-H08) | OK, fence open and marked. |
| Domain purity (NFR-5) | AD-01, AD-10, AD-11 rule; no-branch: prose | OK for clock/RNG; no-branch → CONF-14. |
| Observability & privacy, RFC 9457 (NFR-6) | **no AD** (prose `:818-821`; VAL-M04 open) | **GAP (AD-level, low)** — CONF-14. |
| Performance qualitative (NFR-7) | none — **explicitly deferred** (`:1075-1077`) | OK (explicit). PRD 2026-09-08 added provisional numbers (`prd.md:532-533`: depth 32 × fan-out 50, < 200 ms, 5 s) — spine still says "no numeric budgets by design" — minor drift, CONF-07. |
| Audit / raw act (NFR-8) | AD-01, AD-23 | OK. |
| Idempotency layers (NFR-9 / PRD §10) | AD-13 (wrong rule), AD-02 + VAL-H10 | PRD §10/§14 now says the transport key "is v1 work"; register says "bind before the registry story" — compatible; epics NFR-9 text stale — CONF-05/13. |
| Cost-ready (NFR-10) | AD-05 | OK. |
| NL-is-data (NFR-11) | deferred Theme 3 — explicit | OK. |
| Additive schema (NFR-12) | AD-01 (+VAL-H11 open) | OK. |

### Epics/stories vs ADs (sprint-status.yaml 2026-09-08)

| Story | Status | AD conflict? |
|---|---|---|
| 1.1–1.5 | done | none (1.1 historical AppHost scaffold acknowledged by AD-20). |
| 2.1–2.5 | done | none; 2.5 covers the `Expire` command only — the AD-25 trigger has no story. |
| 3.1 | done | AC `epics.md:841` "tenant/type policy" vs AD-17/AD-25 per-tenant platform config (minor). |
| 3.2 | done | **Conflicts with AD-21 rule** (public entry is the registry reserve; direct `SpawnChild` reactor-only) and PRD FR-16 (2026-09-08). Needs a supersession note (addendum H4). |
| 3.3, 3.4 | done | AD-06 shape wording (CONF-11). |
| 3.5 | done | matches code and PRD; **contradicts AD-13** (AD is the odd one out). |
| 3.6 | done | none. |
| Epic 3 note (VAL-H10 predecessor) | — | Consistent with AD-21 dependency note and open-register VAL-H10 row. |
| 4.1–4.4 | done | none; 4.4 AC amended to identity order ✓. |
| 4.5–4.7 | done | none. |
| 4.8 | in-progress | consistent with AD-11; parking skip and 5 s give-up are un-registered policy (CONF-06, CONF-15). |
| 4.9 | backlog | ACs omit AD-23/AD-24, R4/R6/R7/R8 proofs; "Prerequisite decision" text (`epics.md:1381`) still says the architect must name the host (resolved by AD-20); AC6 ("architecture tests fail when a Works-owned hosting project is introduced") is currently **inverted** by `BuildConfigurationTests` — CONF-02, CONF-04. |
| AD-21 registry / AD-22 fan-out / AD-25 expiry stories | **do not exist** | — |

`docs/boundary-decision-record.md` and `sprint-change-proposal-2026-09-06.md` are consistent with AD-20 (host named, removal gated on matrix rows) and AD-03 (proposal §4.1–4.5 applied verbatim; success criteria met in PRD/epics/AD-03/docs/code).

**Coverage gap count:** 12 — 4 story-level for in-scope v1 work (FR-10 expiry trigger; FR-13/FR-16 registry; FR-11 AD-22 fan-out; identity-provenance NFR / AD-23–24 acceptance) and 8 AD-level or mapping gaps (FR-20 two views; FR-12 unit inheritance; FR-11 count/notifier; FR-2/FR-7 bounds; FR-22 ports; NFR-6 observability; no-branch-on-kind; FR-26 mapping).

---

## Findings

### CONF-01 — [high] — AD-25 automatic expiry has neither code nor an owning story while FR-10 puts it in v1 scope
**Issue:** AD-25 binds a durable expiry reminder registered/cancelled/rescheduled from lifecycle events and reconciled through the AD-11 pass. Nothing implements it, no story owns it, and matrix row R6 describes "expiry reminders (AD-25)" as a seam that exists "today in Works".
**Divergence/risk:** A conformant v1 ships no automatic expiry — exactly the VAL-H01 divergence AD-25 claims to have resolved. The register's "FR-10 stands unchanged — no correct-course pass is required" hides that FR-10's trigger is unbuilt and unowned; Epic 4 closure is described as gated only on 4.8/4.9 (`sprint-change-proposal-2026-09-06.md:252-254`).
**Source:** `architecture.md:397-416` (AD-25), `:265` (R6 "today in Works"), `:1109-1110` (gap analysis, no owner).
**Corroborating evidence:** `grep -rl ExpireWorkItem src/Hexalith.Works/` → only `Recovery/Cascade/CascadeCommands.cs`, `Recovery/Cascade/CascadeCheckpoint.cs`; `src/Hexalith.Works/Runtime/WorksHost.cs:80-99` registers date-resume reminders only; `sprint-status.yaml` (no expiry story); `epics.md:371` (FR-10 → Epic 2, Story 2.5 = command only); `prd.md:221-222` (trigger = durable reminder adapter), `prd.md:415` (§6.1 in scope).
**Suggested disposition:** discuss — route an "expiry reminder adapter + platform TTL options" story through sprint planning (Epic 2 or 4), correct R6's "today in Works" wording to "target", and add an open-register row until the story exists.

### CONF-02 — [high] — Story 4.9 carries none of the AD-23/AD-24 (and R4/R6/R7/R8) acceptance the register assigns to it
**Issue:** AD-23 rule: "enforcement and its negative tests are Story 4.9 platform acceptance; production ingress is prohibited until this contract is live." AD-24 rule: "Story 4.9 acceptance includes negative migration tests … plus one positive-path test." Story 4.9's seven ACs mention none of OIDC, claim-derived tenant, deny-before-dispatch, mTLS/trust-domain negatives, forged `SequenceNumber`, direct-`SpawnChild` denial, or the R6/R7/R8 durability/degraded-readiness proofs. The PRD's new §9 identity-provenance NFR therefore has no story.
**Divergence/risk:** The only story that can discharge AD-23/AD-24 will be accepted on its current ACs, leaving the authorization floor and trusted-origin controls unproven while the register says they gate production.
**Source:** `architecture.md:344-368` (AD-23 rule), `:370-395` (AD-24 rule), `:254-270` (R4/R6/R7/R8/R10 "proof before removal").
**Corroborating evidence:** `epics.md:1335-1383` (Story 4.9 ACs; `:1381` stale "Solution Architect must name the platform/host repository"); `addendum.md:66` (H6 reaches the same conclusion); `prd.md:447` (new NFR); `tests/Hexalith.Works.IntegrationTests/WorksMtlsAuthorizationSmokeTests.cs:27` (one allow/deny fact only, in the Works lane).
**Suggested disposition:** autofix (epics) — append AD-23/AD-24/R4/R6/R7/R8 acceptance blocks to Story 4.9 citing AD ids, replace the stale prerequisite paragraph with the AD-20 resolution; discuss whether the negative suite should be a separate platform-lane story.

### CONF-03 — [high] — Works absorbed platform-owned R1/R10 topology (mTLS control plane, TLS placement/scheduler, JWT dev keys) after AD-20 bound them to `Hexalith.Platform`; register is silent
**Issue:** Commit `33a27e2` (2026-09-06, the same commit that applied the VAL-H03 closure) added a Works-owned Sentry container, AppHost-owned TLS placement and scheduler containers pinned to `daprio/dapr:1.18.3`, mTLS blocks in every access-control config, and +258 lines of readiness plumbing; `42c4318` (2026-09-08) added OIDC client parameters and development symmetric-key JWT validation for `eventstore`/`eventstore-admin` in the Works AppHost. AD-20 R1/R10 name `Hexalith.Platform` as owner; AD-24 says the sandbox exemption is recorded in the `Hexalith.Platform` repository; AD-19 says the Dapr runtime pin is a platform-owned field. The scope expansion was approved inside Story 4.8's review trail, not in the register, the open-findings table, or a sprint-change proposal.
**Divergence/risk:** The migration surface AD-20 exists to shrink grew by ~1,000 lines the day it was bound; the R10 "proof" now lives in the Works lane rather than `verify-works-host`; a Dapr runtime version is hard-coded in Works code; and the AD-20 "Prevents" (double-abandonment) risk rises because more behaviour must be re-proven in the platform lane before removal.
**Source:** `architecture.md:226-253` (AD-20 binds/rule), `:260` (R1), `:269` (R10), `:216-224` (AD-19), `:392-395` (AD-24 exemption location).
**Corroborating evidence:** `git show 33a27e2 --stat`; `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:36,80,90,111`; `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml:8-17` ("Local development only — … Production must use its platform-managed Dapr control plane"); `src/Hexalith.Works.AppHost/Program.cs` (dev signing key block from `42c4318`); `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:370` ("after the human approved the Sentry/mTLS scope expansion"); `sprint-change-proposal-2026-09-06.md` (no mention).
**Suggested disposition:** discuss — add an open-register row "R1/R10 debt: Works-owned mTLS control plane + JWT composition (2026-09-06/08) must be reproduced in `Hexalith.Platform` before removal"; move the `1.18.3` literal to configuration owned by the platform pin; adopt a rule that host-edge topology changes after 2026-09-06 are logged against a matrix row.

### CONF-04 — [medium] — The fitness lane ratifies the Works-owned AppHost, inverting AD-20's direction and Story 4.9 AC6
**Issue:** `BuildConfigurationTests` now has P0 facts requiring `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` to exist with the pinned Aspire SDK and requiring `aspire.config.json` to point at it; `KernelDependencyPolicy` classifies `Hexalith.Works.AppHost` and `Hexalith.Works.ServiceDefaults` as allowed adapter projects. Story 4.9 AC6 says architecture tests must *fail* when a Works-owned hosting project is introduced.
**Divergence/risk:** Removing the AppHost per AD-20 turns the fitness lane red; nothing today fails on the prohibited projects. The spine's claim that "architecture-fitness tests run in the build" (`:1035-1036`) currently protects the wrong invariant.
**Source:** `architecture.md:226-253` (AD-20 rule), `:1035-1036`; `epics.md:1370-1373` (AC6).
**Corroborating evidence:** `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:29-44,114-120`; `tests/Hexalith.Works.ArchitectureTests/FitnessTests/KernelDependencyPolicy.cs:14-16`; `aspire.config.json`.
**Suggested disposition:** defer to Story 4.9 but record it now in the open register (extend the VAL-M01 row): the lane must flip these assertions in the same change that removes the projects.

### CONF-05 — [medium] — AD-13's resume rule contradicts the code, the amended PRD and Story 3.5
**Issue:** AD-13 rule: "`ResumeWorkItem` is idempotent — no current match is a no-op; a duplicate is a no-op." The aggregate rejects a non-matching resume while `Suspended` and treats only a repeat of the last *consumed* condition as a no-op; PRD FR-15 (2026-09-08) and Story 3.5's AC state the same.
**Divergence/risk:** A builder following the register would silently swallow a wrong-key resume that the shipped kernel (and the product decision) reject — the exact "divergent resume semantics" AD-13 says it prevents. epics NFR-9 still carries the old rule.
**Source:** `architecture.md:159-164` (AD-13), `:811-813` (prose "no current match = no-op").
**Corroborating evidence:** `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:229-240`; `src/Hexalith.Works.Contracts/State/WorkItemState.cs:49`; `prd.md:285`; `epics.md:978-988` (Story 3.5), `epics.md:193` (NFR-9 stale); `addendum.md:62` (H2).
**Suggested disposition:** autofix — rewrite the AD-13 rule and `:811-813` to "non-matching resume while Suspended = domain rejection; repeat of the consumed condition = the only no-op; aggregate retains the last consumed condition", and fix epics NFR-9.

### CONF-06 — [medium] — The parked-candidate skip lets a projection artifact veto the authoritative stream in reminder reconciliation (AD-11), with no register entry
**Issue:** `0527d12` makes `IndexedPendingDateAwaitSource` skip any candidate whose `projection:works:parked:*` record (written by the `/project` decode path) says `Parked = true` — no stream read, not counted incomplete, Information-level log. AD-11 binds "aggregate streams are the authoritative truth; the … index [is a] discovery aid only" and exists to prevent "lost registrations going undiscovered".
**Divergence/risk:** An item holding a genuine `DateReached` await whose projection parked on a *later* undecodable event (or on the identity-mismatch / `NotSupportedException` path the 4.8 review still lists as open) never has its reminder reconciled; the reminder stream reader uses a different decoder (`WorksEventDecoder`) than the projection (`WorkItemProjectionEventDecoder`), so "its stream cannot be rebuilt" is not guaranteed. There is no unpark path and no degraded readiness (R8) — only an operator log. This is an AD-11-level policy decision taken in a story review and absent from the register.
**Source:** `architecture.md:137-150` (AD-11 binds/prevents/rule), `:267` (R8 "exhaustion ⇒ durable evidence + degraded readiness").
**Corroborating evidence:** `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:114-126`; `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` (EventId 4607, `LogLevel.Information`); `src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs:57` vs `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:65-76`; `4-8-register-and-reconcile-date-reminders-durably.md:736` (human decision), `:739` (identity-mismatch path never parks), `:748` (no unpark path — deferred); `deferred-work.md` (2026-09-08 Group 1).
**Suggested disposition:** discuss — either amend AD-11 with an explicit "parked aggregate" disposition (terminal, operator-visible, feeds degraded readiness per R8, unpark/replay path owned by R4/R6) or revert to counting parked candidates as incomplete under a bounded retry budget.

### CONF-07 — [medium] — The spine has not absorbed the 2026-09-08 PRD amendment; several new product decisions have no AD
**Issue:** The PRD was amended after the spine (FR-26 added; FR-5/13/16 rewritten around the registry; FR-6 normative table; FR-15 rule; FR-20 two views by `PartyId`; FR-11 unestimated-descendants count + notifier; FR-12 unit inheritance at spawn; FR-2/FR-7 length bounds; §9 identity-provenance NFR; provisional performance numbers). The addendum's handoff table lists edits owed to `architecture.md` (H1, H2, H8, H12); none are applied and the open-findings register has no row for the PRD validation ("material drift") or the amendment.
**Divergence/risk:** Two documents both claim to be the system of record and now disagree (AD-13 vs FR-15; "no numeric budgets by design" vs PRD §14 fixture numbers; single-view what's-next vs two views). Builders reading the register will not implement the PartyId filter, unit inheritance, or the count field.
**Source:** `architecture.md:6` (`updatedAt: 2026-09-06`), `:457-467` (feature table), `:436-453` (open register), `:1075-1077` (no numeric budgets).
**Corroborating evidence:** `addendum.md:55-72` (H1–H12); `prd .memlog.md:19-38`; `prd.md:116,123,241-242,249,285,298-306,351,447,532-533`; `src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs` and `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs` (no executor parameter).
**Suggested disposition:** discuss — run the owed H1/H2/H8/H12 edits, add AD text (or AD-03 amendment) for the two-view predicate, decide whether unit inheritance/unestimated count/bounds are AD-level or story-level, and add a register row acknowledging the 2026-09-08 PRD amendment as the current spec baseline.

### CONF-08 — [medium] — AD-21 is unbuilt, unstoried, and the interim state is the hole it "prevents"; Stories 3.1/3.2 and the guard doc still describe caller-fed authority
**Issue:** `SpawnChild`/`CreateWorkItem` still trust caller-supplied `ProposedParentAncestors`, `ProposedParentDepth`, `MaxDepth`, `ExistingChildParent`; `WorkTreeAttachmentGuard` is the authority; `docs/work-tree-shape-guard.md` documents that as the design with no AD-21 supersession note; Stories 3.1/3.2 ACs describe direct `SpawnChild`; the registry story cannot be drafted until VAL-H10 is bound. The PRD (2026-09-08) already states the registry as the public act.
**Divergence/risk:** The register says the caller-fed defaults are a cycle/second-parent hole; nothing names an interim mitigation (e.g., origin-restrict `SpawnChild` now, or reject empty ancestor lists for non-root spawns) while the registry waits behind VAL-H10. Planning artifacts disagree on who owns tree topology.
**Source:** `architecture.md:272-316` (AD-21), `:1104-1105` (gap analysis).
**Corroborating evidence:** `src/Hexalith.Works.Contracts/Commands/SpawnChild.cs:11-15,29-32`; `src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs:24-57`; `docs/work-tree-shape-guard.md:3-6,33`; `epics.md:815-880` (Stories 3.1/3.2); `sprint-status.yaml` (no registry story); `prd.md:143-149,252-260,288-296`.
**Suggested disposition:** discuss — add an open-register row "AD-21 interim exposure" with a named mitigation and revisit condition; add supersession notes to Stories 3.1/3.2 and `docs/work-tree-shape-guard.md` (addendum H4); keep VAL-H10 as the drafting gate.

### CONF-09 — [medium] — AD-22 fan-out has no story and no register row; FR-11's incremental-ancestor delivery is unmet at runtime
**Issue:** AD-22 states "FR-11 stands as written" and that the ordinary path "must converge every ancestor without operator action", yet the `/project` dispatcher persists parent rolled values as unavailable until an operator-run shared rebuild, the Epic 3 note only says the story is "routed … through sprint planning", and AD-22 does not appear in the open-findings register.
**Divergence/risk:** SM-2 ("one number") and FR-11 are unsatisfiable in the live topology today with no tracked owner or revisit condition.
**Source:** `architecture.md:318-342` (AD-22), `:402-409` epics Epic 3 note.
**Corroborating evidence:** `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:34`; `docs/work-roll-up-projection.md:67-73` (2026-08-27 refusal decision); `sprint-status.yaml`.
**Suggested disposition:** defer with an explicit register row (owner: R4, revisit: registry story drafted) so the gap stops being invisible.

### CONF-10 — [low] — AD-05 and prose name a `Meter` type; the code type is `WorkItemEffort`
**Issue:** No `Meter` type exists; `WorkItemEffort(Estimated, Unit, Done)` with derived `Remaining` is the meter.
**Divergence/risk:** Builders may introduce a second `Meter` type — the very thing AD-05's rule forbids.
**Source:** `architecture.md:77` (AD-05), `:668`, `:802`, `:921`; `epics.md` AR-7.
**Corroborating evidence:** `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs:1-26`; `grep -rn "record Meter" src` → none.
**Suggested disposition:** autofix — "the Meter shape is realized as `WorkItemEffort`; a Cost meter reuses that type".

### CONF-11 — [low] — AD-06 presents the flattened per-descendant slot *document* as adopted; the live strategy is a recursive fold with per-node LWW
**Issue:** The invariant (per-node LWW by own stream position, never deltas) is ratified; the "one LWW slot per descendant in the ancestor's roll-up document" shape is not what `WorkItemRollUpProjection` or the persisted `WorkItemRollUp` do — it is the AD-22 target.
**Divergence/risk:** A builder may "fix" the working recursive strategy to match an AD-06 shape that only makes sense once registry-backed fan-out exists.
**Source:** `architecture.md:82-95` (AD-06), `:870-876` (pattern example).
**Corroborating evidence:** `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:127,234-258,373`; `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs`.
**Suggested disposition:** autofix — split AD-06 into "invariant (adopted)" and "storage shape (target, lands with AD-22/R4)".

### CONF-12 — [low] — Legacy prose that still contradicts the register or the tree
**Issue:** `:465` prints the retired recursive formula `rolled = own + Σ rolled(children)`; `:463` says "a single event-sourced aggregate root" (AD-21 adds the registry aggregate; addendum H1); `:598, :712, :777, :954` name `InMemoryEventLog`/`ReorderingProjectionDriver`/`RollUpProjectionBuilder` that do not exist; `:920, :928-929` list `Contracts/Results/`, `Server/Validation/`, `Server/Registration/` folders that do not exist; `:1009` "Date await → `Reactor/Timer`" (there is no `Reactor/Timer`; reminders live in `src/Hexalith.Works/Reminders`).
**Divergence/risk:** "Register wins" limits the damage, but the seed is what a cold-start agent copies.
**Source:** lines cited above.
**Corroborating evidence:** `find src tests -maxdepth 2`; `ls tests/Hexalith.Works.Testing` → `WorkItemStateBuilder.cs`, `WorksTestingAssembly.cs`.
**Suggested disposition:** autofix.

### CONF-13 — [low] — epics.md Additional Requirements and inventory still carry pre-register text that contradicts ADs
**Issue:** AR-22 (`epics.md:298`) legacy chain `Contracts ← Server ← Projections` (vs AD-18); AR-6 (`:234`) "delta ≥ 0" (vs AD-17, PRD FR-8); AR-14 (`:268`) reminder name from `(workItemId, awaitConditionKey)` without tenant (vs AD-25/`DateReminderName.cs:59`); AR-20 (`:283`) SDK `10.0.301` as a Works-owned pin (vs AD-19; `global.json` = 10.0.400); AR-8 (`:238`) recursive phrasing (vs AD-06 amendment); NFR-9 (`:193`) old resume rule (vs AD-13 corrected); Story 3.1 AC (`:841`) "tenant/type policy"; FR coverage map (`:362-389`) lacks FR-26 and points FR-16 at pre-registry stories.
**Divergence/risk:** Story authors cite AR-n, not AD-n; the two label families now disagree.
**Source:** `architecture.md:204-224` (AD-17/18/19), `:418-434` (legacy-label map does not cover AR-n).
**Corroborating evidence:** as cited; `addendum.md:63-65,69` (H3/H5/H9 owe several of these).
**Suggested disposition:** autofix (epics) — add AR→AD cross-references and correct the six stale lines.

### CONF-14 — [low] — Load-bearing rules live only in non-binding prose
**Issue:** The preamble (`:26-36`) declares everything outside the register "analysis, rationale, and cold-start seed". Yet the no-branch-on-executor-kind rule (SM-3/FR-17), the ports contract (FR-22), reference-not-copy (FR-21), and structured-logging/RFC 9457 (NFR-6; VAL-M04) exist only in prose (`:815-821`, `:678`, `:812`).
**Divergence/risk:** A builder may treat them as optional; the fitness test that enforces no-branch (`ScaffoldGovernanceTests.cs:309`) has no AD to point at.
**Source:** `architecture.md:26-36`, `:815-821`, `:678`.
**Corroborating evidence:** `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:309,362`; `src/Hexalith.Works.Contracts/Ports/*`; `prd.md:364-371` (FR-22), `:470-472` (SM-3).
**Suggested disposition:** discuss — add AD-26 "Executor-binding uniformity (no branch on kind)" and AD-27 "Ports + observability/privacy floor", or explicitly mark those prose subsections as binding.

### CONF-15 — [medium] — Startup reminder reconciliation gives up after ~5 s with no degraded readiness; AD-11's narrow guarantee and R8 are not met and not registered
**Issue:** `ReminderReconciliationService` runs 5 attempts × 1 s at startup and never again until the next restart; exhaustion is a log. AD-11's retained guarantee is "missed firings are reconciled by the indexed recovery pass"; R8 requires "exhaustion ⇒ durable evidence + degraded readiness". The deferred-work ledger recorded this on 2026-09-07; the register's VAL-H07 row covers Scheduler HA, not the Works-side pass.
**Divergence/risk:** Any transient store/gateway unavailability in the first seconds after a restart silently disables reminder recovery for that host lifetime.
**Source:** `architecture.md:137-150` (AD-11 rule), `:267` (R8), `:444` (VAL-H07 row scope).
**Corroborating evidence:** `_bmad-output/implementation-artifacts/deferred-work.md` ("Startup reminder reconciliation gives up permanently after ~5 s", `src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:37-61`; "durability blind window and no backfill", `IndexedPendingDateAwaitSource.cs:41-51`).
**Suggested disposition:** discuss — widen the VAL-H07 row (or add one) to include the Works-side pass posture, and bind "exhaustion ⇒ degraded readiness" at R8 before Story 4.8 is accepted.

---

## Summary counts

- Severity: **3 high** (CONF-01, 02, 03) · **7 medium** (CONF-04, 05, 06, 07, 08, 09, 15) · **5 low** (CONF-10, 11, 12, 13, 14).
- Ratification: 15 ratified / 4 drift / 6 not-yet-built (2 without an owning story) / 3 prose-contradiction flags.
- Coverage gaps: 12 (4 story-level for in-scope v1 work, 8 AD-level/mapping).
