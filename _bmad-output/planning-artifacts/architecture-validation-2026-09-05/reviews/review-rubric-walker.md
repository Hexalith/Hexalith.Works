# Architecture Reviewer Gate — Rubric Walker

**Target:** `_bmad-output/planning-artifacts/architecture.md`  
**Intent:** Validate only; source architecture not modified  
**Date:** 2026-09-05  
**Verdict:** **FAIL — one blocking ownership gap and four high-severity convergence defects prevent this document from serving as the current architecture spine.**

## Executive assessment

The document has strong domain invariants: event sourcing, pure `Handle`/`Apply`, payload-versus-envelope sequence separation, tenant-closed work trees, order-tolerant projections, a mechanical reactor, and a clear domain/platform boundary are all treated seriously (`architecture.md:94-108`, `232-268`, `343-427`). It also maps all 25 FRs to intended structure (`architecture.md:542-556`) and explicitly defers Themes 3–6 (`architecture.md:226-230`).

It does not pass the Good-spine checklist in its current form. The platform-host decision is intentionally incomplete and blocks Story 4.9; automatic expiry has no architecture; the dependency rule contradicts the live project graph; the FR-20 creation-order contract is not implementable from the selected read model and the live implementation substitutes identity order; and the legacy document shape makes decision references ambiguous. Current technology pins and claimed build-time enforcement have also drifted from the brownfield repository.

| Severity | Count |
| --- | ---: |
| Critical | 1 |
| High | 4 |
| Medium | 2 |
| Low | 0 |

## Findings

### RW-01 — CRITICAL — The platform-host boundary has no named target or owner

**Architecture evidence:** The selected starter says a “designated platform/host repository” owns Aspire and ServiceDefaults (`architecture.md:151-159`), prohibits Works-owned AppHost/ServiceDefaults (`architecture.md:161-173`), and repeats that external ownership as a runtime invariant (`architecture.md:261-268`, `524-528`). Yet the run instructions say the concrete repository and command remain a Story 4.9 prerequisite (`architecture.md:584-591`), the validation section calls this the critical gap (`architecture.md:631-647`), and the completion checklist leaves it unchecked (`architecture.md:691-701`).

**Brownfield/input evidence:** The mandatory baseline forbids domain-owned AppHost, Aspire, ServiceDefaults, and duplicated platform plumbing (`references/Hexalith.AI.Tools/hexalith-llm-instructions.md:121-134`). The live solution still includes both prohibited projects (`Hexalith.Works.slnx:50-58`), the AppHost still owns Aspire/Dapr composition (`src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj:1-25`), and the Works host still owns Dapr read models, event subscriptions, reminders, recovery, and a bespoke `/project` endpoint (`src/Hexalith.Works/Runtime/WorksHost.cs:38-89`, `98-123`). The approved change proposal explicitly says Story 4.9 cannot enter implementation until the repository and owner are named (`_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-05.md:377-406`).

**Checklist impact:** The operational/environmental envelope and inherited baseline are not executable. Two teams cannot independently implement the migration without choosing different destination repositories, owners, SDK seams, and acceptance lanes; safe removal is blocked.

**Disposition:** **Discuss.** Name the platform-host repository, accountable owner, integration-test command/lane, and the precise split of Works-specific versus EventStore/platform runtime components. This is not safe to defer because the approved plan makes it a prerequisite.

### RW-02 — HIGH — FR-10 automatic expiry has no trigger, ownership, or recovery decision

**Architecture evidence:** C2 designs durable reminders only for a Work Item *parked on `DateReached`*, and those reminders emit `ResumeWorkItem` (`architecture.md:261-264`). E2 merely says Due-Date/TTL comes from per-work-type/tenant policy (`architecture.md:265-267`). The document later admits that the concrete policy source/mechanism remains unresolved (`architecture.md:649-655`). It never binds which component observes Due Date/TTL, schedules or cancels expiry, emits `ExpireWorkItem`, or reconciles missed expiry firings.

**Spec/brownfield evidence:** FR-10 requires `WorkItemExpired` to fire when Due Date or configured TTL passes (`_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md:175-183`). The command contract itself states that TTL/date sourcing and the scheduled signal are out of scope at that layer (`src/Hexalith.Works.Contracts/Commands/ExpireWorkItem.cs:6-10`). The current host consumes an already-emitted `WorkItemExpired` for cascade and registers resume reminders for `WorkItemSuspended`, but contains no corresponding root-expiry producer (`src/Hexalith.Works/Runtime/WorksHost.cs:64-89`).

**Checklist impact:** A v1 capability is missing from the architecture, and a deferred item can make independently built units diverge between no automatic expiry, Due-Date-only expiry, TTL-only expiry, or incompatible reminder ownership/recovery behavior.

**Disposition:** **Discuss.** Add a distinct expiry scheduling/reconciliation decision (separate from `DateReached` resume), including policy authority, reschedule/cancel semantics, at-least-once behavior, idempotency key, and recovery ownership. If automatic expiry is no longer v1, change FR-10 explicitly instead of leaving it implicit.

### RW-03 — HIGH — The documented dependency direction would violate the live enforced graph

**Architecture evidence:** Both the structure rule and boundary summary state `Contracts ← Server ← Projections` (`architecture.md:326-334`, `530-532`), which means Projections depends on Server.

**Brownfield evidence:** `Hexalith.Works.Projections` references Contracts directly and does not reference Server (`src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj:8-10`). The architecture fitness allowlist explicitly requires Server, Projections, and Reactor each to reference Contracts only (`tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:7-25`). The accepted boundary record says the same (`docs/boundary-decision-record.md:138-140`).

**Checklist impact:** This is a direct brownfield contradiction in a machine-enforced rule. Following the architecture literally would introduce a forbidden project reference and fail the stated fitness policy.

**Disposition:** **Autofix in an Update.** Replace the chain with an unambiguous graph: `Server → Contracts`, `Projections → Contracts`, `Reactor → Contracts`; the executable host may reference all inward units plus the SDK.

### RW-04 — HIGH — “What's next” does not preserve the specified creation-order tie-break

**Architecture evidence:** A2 binds ordering as Priority, then Due Date, then creation order (`architecture.md:232-240`). The architecture also claims complete FR coverage (`architecture.md:614-629`).

**Spec/brownfield evidence:** FR-20 requires Priority → earliest Due Date → creation order (`_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md:281-287`; `_bmad-output/planning-artifacts/epics.md:1141-1157`). The live comparer uses ordinal `WorkItemId.Value` instead (`src/Hexalith.Works.Projections/Strategies/WhatsNextOrdering.cs:62-71`), and the read model has no creation timestamp or cross-item creation-order key (`src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs:27-38`). Runtime documentation explicitly acknowledges that identity order substitutes for creation order because the kernel lacks a timestamp (`docs/whats-next-projection.md:16-32`). `WorkItemId` accepts an arbitrary nonblank aggregate ID, so ordinal ID order is not a creation-order guarantee (`src/Hexalith.Works.Contracts/ValueObjects/WorkItemId.cs:5-16`).

**Checklist impact:** The architecture does not cover a testable part of FR-20, and the existing implementation has ratified a materially different ordering rule. Equal-priority/equal-date work can be returned in a different order than requested.

**Disposition:** **Discuss.** Either add an immutable, replay-stable creation-order key supplied by EventStore/edge and keep FR-20, or obtain product approval to amend PRD/epics/architecture to identity order. Do not claim the two are equivalent.

### RW-05 — HIGH — Legacy document shape makes decision identity and enforcement ambiguous

**Architecture evidence:** This is a 732-line collaborative decision document with analysis, rationale, project seed, and self-validation mixed together (`architecture.md:22-24`, `90-129`, `209-287`, `444-591`, `593-732`). It has no stable `AD-n` entries and no per-decision `Binds` / `Prevents` / `Rule` fields. Its identifiers already collide semantically: `D-1` means reactor placement (`architecture.md:123-128`), while `D1` means AuthorityLevel (`architecture.md:242-246`); `D-2` means claim cardinality (`architecture.md:125-127`), while `D2` means tenant isolation (`architecture.md:242-245`). Later text uses `D-1` again for reactor placement (`architecture.md:660-669`).

**Checklist impact:** Many individual rules are actionable, but the artifact as a whole is not a reliable consistency contract: downstream references can name the wrong decision, updates must reconcile repeated prose manually, and structural seed (full tree, versions, catalog, test layout) crowds out durable invariants. The dependency and version findings above demonstrate actual drift this shape did not prevent.

**Disposition:** **Discuss.** Distill the accepted invariants into the current architecture-spine form with stable `AD-n` IDs and explicit Binds/Prevents/Rule; move rationale/history to the memlog and leave code-owned structure/version seed out of the durable spine. Preserve a mapping from legacy A1–E2/D-* labels so existing stories remain traceable.

### RW-06 — MEDIUM — Named technology pins are internally inconsistent and no longer current

**Architecture evidence:** The constraints name SDK `10.0.300` (`architecture.md:58-67`), while the starter table names SDK `10.0.301`, Dapr `1.18.4`, xUnit `3.2.2`, and Fluent UI `5.0.0-rc.3` and claims these were verified current (`architecture.md:189-202`). The validation section repeats SDK `10.0.301`, Dapr `1.18.4`, Aspire `13.4.6`, and xUnit `3.2.2` as current (`architecture.md:595-602`).

**Brownfield/current evidence:** The tracked SDK is `10.0.400` (`global.json:1-11`). Central pins are Dapr `1.18.5`, Aspire packages `13.5.3`, Fluent UI `5.0.0-rc.5-26219.1`, and xUnit v3 `4.0.0` (`references/Hexalith.Builds/Props/Directory.Packages.props:109-121`, `136-146`, `226-227`, `318-321`). Official current-package evidence agrees: [.NET 10 SDK 10.0.400](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [Dapr.AspNetCore 1.18.5](https://www.nuget.org/packages/Dapr.AspNetCore/), [Aspire.Hosting 13.5.3](https://www.nuget.org/packages/Aspire.Hosting/13.5.3), and [xunit.v3 4.0.0](https://www.nuget.org/packages/xunit.v3).

**Checklist impact:** The named-tech/currentness check fails, and an implementation agent could attempt a downgrade. These are code-owned seed now that the repository exists.

**Disposition:** **Autofix in an Update.** Prefer naming the authoritative files/policy rather than copying volatile pins into the spine. If exact versions remain, update them and record a verification date/source.

### RW-07 — MEDIUM — Claimed “build-time” fitness enforcement is not substantiated

**Architecture evidence:** Purity and no-branch rules are repeatedly described as build-time fitness functions (`architecture.md:102-105`, `261-268`, `391-399`, `409-427`), and the development workflow says architecture-fitness tests run in the build (`architecture.md:584-589`).

**Brownfield/verification evidence:** The repository build target contains no test invocation (`Directory.Build.targets:1-2`); inclusion of the test project in the solution does not make `dotnet build` execute it. The focused verification command

```text
dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0
```

failed before tests ran with 21 compile errors, chiefly inaccessible nested `RollUpNode` members in `WorkItemRollUpProjection.cs` and unassigned descriptor out parameters. In addition, the architecture test still asserts SDK `10.0.301` (`tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:10-20`) against tracked `global.json` `10.0.400` (`global.json:1-4`).

**Checklist impact:** The document presents important rules as mechanically enforceable when the current build does not run them and the focused lane cannot reach execution. The rules remain good, but their stated enforcement mechanism is presently false.

**Disposition:** **Discuss.** Either wire an explicit CI/build gate and keep “build-time,” or describe the exact required test lane honestly. Repair/refresh the existing architecture-test lane before using it as validation evidence.

## Good-spine checklist coverage

| Checklist dimension | Result | Evidence / reason |
| --- | --- | --- |
| Real divergence points fixed | **Fail** | Strong coverage of event sourcing, tenancy, concurrency, roll-up, and kernel/platform boundaries, but host ownership, root expiry, dependency direction, and ordering remain divergent (RW-01–RW-04). |
| Rules enforceable and prevent stated divergence | **Fail** | Many rules name tests, but the legacy shape lacks explicit Binds/Prevents/Rule and current build-time enforcement is not operational (RW-05, RW-07). |
| Deferred items are safe | **Concern** | Themes 3–6 are cleanly deferred; the unresolved host target and expiry policy are v1 hazards and cannot remain implicit/deferred (RW-01, RW-02). |
| Named technology verified current | **Fail** | Multiple pins conflict within the architecture and with current repository/official versions (RW-06). |
| Brownfield ratification | **Fail** | The document acknowledges the AppHost migration, but still differs from the live topology, project graph, queue ordering, and validation lane (RW-01, RW-03, RW-04, RW-07). |
| Spec capability coverage | **Concern** | The FR map is broad and mostly strong; FR-10 expiry initiation and FR-20 creation order are not actually covered (RW-02, RW-04). |
| Inherited constraints | **Fail** | No parent spine was supplied, but the mandatory Hexalith domain-module boundary is inherited. It is correctly stated yet not actionable until the host target is named and migration is completed (RW-01). |
| Structural dimensions complete | **Concern** | Domain/data/integration/test dimensions are unusually thorough. Deployment/environments/operations are only partially closed because the owning host, execution lane, and expiry operations are unresolved (RW-01, RW-02). |

## Deterministic lint disposition

`architecture-validation-2026-09-05/lint-spine.json` reports eight low-severity “possible placeholder” findings for `{tenant}`, `{domain}`, and `{id}`. They are intentional canonical key examples at `architecture.md:44`, `63`, `234`, `321`, and `539`, not unfilled template fields. **Disposition: ignore all eight.**

The lint result also calls the analyzed file `ARCHITECTURE-SPINE.md`, while the target is `architecture.md`. More importantly, because the target uses legacy A1–E2 prose instead of `AD-n` entries, the linter cannot assess missing Binds/Prevents/Rule fields. Its mechanical result must not be read as evidence that decision shape is sound.

## Recommended gate action

Do not treat `architecture.md` as a final build substrate yet. Resolve RW-01, RW-02, and RW-04 with the responsible humans; apply the clear dependency/version corrections; then distill the durable decisions into a stable architecture spine and rerun the full Reviewer Gate against the corrected brownfield state.
