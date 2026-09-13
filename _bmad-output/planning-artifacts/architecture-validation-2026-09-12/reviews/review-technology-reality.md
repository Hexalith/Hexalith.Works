# Architecture Validation — Technology / Reality Lens

- **Target:** `_bmad-output/planning-artifacts/architecture.md`
- **Mode:** standalone Validate; binding register AD-01…AD-25 reviewed before subordinate prose
- **Assessment date / web access date:** 2026-09-12
- **Reviewer contract:** verify committed technology, version, existence, and fit claims against primary web sources and live repository/build reality; distinguish SDK from runtime, stable from preview, and target seams from available APIs
- **Spine changed:** no

## Verdict

**PASS WITH HIGH CONCERNS.** The architecture's core Dapr/EventStore semantic claims are supported, the named platform repository exists, and the live solution plus all 237 architecture tests pass. However, the target hosting decision has not yet been proven in `Hexalith.Platform`; the only currently selected Aspire 13.5 Dapr hosting integration is a preview package; and AD-19's version-authority model does not describe where the live Dapr runtime is actually pinned. Current repository pins also fell behind security/bug-fix patch releases after the prior gate. None of these invalidates the domain architecture, but AD-20 migration acceptance must not treat them as already-resolved platform capability.

## Reality baseline and verification

The assessment used the checked-out root and root-declared submodules at their current commits, including EventStore `6b0247…` (`v3.103.0-40-g6b0247…`), Builds `a32cb4…`, Commons `6da79…`, and PolymorphicSerializations `8aeed…`. The named external repository was independently checked through its public GitHub repository/API rather than assumed from a nearby clone.

Live checks:

| Check | Result |
|---|---|
| `dotnet --version` / installed runtime | SDK `10.0.400`; runtime `Microsoft.NETCore.App 10.0.11` |
| `aspire --version` | `13.5.3` |
| `dapr --version` | CLI `1.18.0`; runtime reported by CLI `1.18.3` |
| `dotnet build Hexalith.Works.slnx --configuration Release -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 --no-restore` | **PASS**, 0 warnings, 0 errors |
| `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests.dll` | **PASS**, 237/237, 0 failed/skipped/errors |

The build compiled the architecture-test project but did **not** execute its tests. That distinction matters to the document's “run in the build” claim.

## Claim-to-evidence matrix

All web links below are first-party documentation, official repositories/APIs, or official package registries and were accessed 2026-09-12.

| Committed claim | Live/config evidence | Primary-source reality | Disposition |
|---|---|---|---|
| .NET 10; SDK authority in `global.json` with `latestPatch` (AD-19) | `global.json` pins SDK `10.0.400`; installed runtime is `10.0.11` | [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) lists SDK `10.0.401` and runtime `10.0.12` as the current 2026-09-08 security servicing release | **Exists/fits; pinned and installed patch levels are stale.** `latestPatch` cannot cross from feature band 10.0.400 to 10.0.401 without editing the pin. |
| Stable Aspire 13.5.x family (AD-20) | `global.json` and AppHost SDK use `13.5.3`; central `Aspire.Hosting*` pins are `13.5.3` | [Aspire.Hosting 13.5.3](https://www.nuget.org/packages/Aspire.Hosting/13.5.3) and [Aspire.AppHost.Sdk 13.5.3](https://www.nuget.org/packages/Aspire.AppHost.Sdk/13.5.3) are stable packages | **Confirmed for core Aspire packages.** |
| Aspire can host the chosen Dapr topology on the same supported family (AD-20/R1) | Works AppHost references `CommunityToolkit.Aspire.Hosting.Dapr` `13.5.0-preview.1.260825-0345` | [Official NuGet listing](https://www.nuget.org/packages/CommunityToolkit.Aspire.Hosting.Dapr/) shows the 13.5-compatible build is preview; no stable 13.5 package is listed | **Unconfirmed as a fully stable dependency graph.** Preview risk is omitted from the binding decision. |
| Dapr runtime and .NET SDK are separate; central packages own SDK versions and Platform owns runtime/deployment pin (AD-19) | Central packages pin Dapr .NET SDK `1.18.5`; `DaprSelfHostedMtls.cs:47,90,111` hard-codes runtime images `daprio/sentry:1.18.3` and `daprio/dapr:1.18.3`; Platform has no Dapr pin | [Dapr compatibility policy](https://docs.dapr.io/operations/support/support-release-policy/) supports SDK/runtime skew within N-2 minor lines; [runtime v1.18.4](https://github.com/dapr/dapr/releases/tag/v1.18.4) is current; [Dapr.Client 1.18.7](https://www.nuget.org/packages/Dapr.Client/1.18.7) is current in the official registry | **Compatible, but ownership is target-state rather than current reality and both live pin sets are behind.** Runtime `1.18.3` versus SDK `1.18.5` is not itself an incompatibility. |
| Dapr pub/sub is at-least-once and portable ordering must not be assumed (AD-09) | Works event processor contains deduplication/idempotency machinery | [Dapr pub/sub overview](https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-overview/) states at-least-once delivery and component-pluggable behavior | **Confirmed; the non-reliance rule is the safe fit.** |
| Same-actor turns serialize normal command handling; ETag save conflict is the bounded fallback (AD-08) | EventStore `AggregateActor` contains `MaxPersistenceConflictRetries` (default 1), cache clear, rehydrate/re-handle, and terminal `ConcurrencyConflict`; ETag actor state types are present | [Dapr actor reentrancy/locking](https://docs.dapr.io/developing-applications/building-blocks/actors/actor-reentrancy/) documents per-actor locking without reentrancy; [Dapr state ETags](https://docs.dapr.io/developing-applications/building-blocks/state-management/howto-stateful-service/) documents ETag-based concurrency | **Confirmed against the actual EventStore implementation, not inferred from Dapr alone.** |
| Actor reminders are Scheduler-backed; durability is conditional on persistence/HA/backup and callback failure policy (AD-11/AD-25) | Works has classic `Actor`/`IRemindable` adapters; AppHost explicitly runs a Scheduler `1.18.3` container with a persistent named volume | [Actor timers/reminders](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/) documents Scheduler persistence, failover behavior, overwrite, and failure policy; [Scheduler concepts](https://docs.dapr.io/concepts/dapr-services/scheduler/) documents embedded etcd, peers, retries, and persistence | **Semantic claim confirmed; production durability remains an acceptance target, not a proved deployment property.** The live lane is a single self-hosted scheduler, not HA/backup proof. |
| `Hexalith.EventStore` supplies the named core domain-service, projection, query, read-model, cursor, and rebuild APIs (AD-16/AD-20) | `AddEventStoreDomainService`, `UseEventStoreDomainService`, `/project`, `/project/v2`, `/project/v2/reconcile`, `/project/rebuild/shared/v1`, `IDomainProjectionHandler`, `IDomainQueryHandler`, `IReadModelStore`, `ReadModelWritePolicy`, `IQueryCursorCodec`, and `QueryCursorScope` exist in the checked-out submodule | [Hexalith.EventStore.DomainService package index](https://api.nuget.org/v3-flatcontainer/hexalith.eventstore.domainservice/index.json) contains the selected `3.103.0` line | **Confirmed for the named current core seams.** AD-16 correctly calls the capture-through-Commit fence missing. |
| EventStore will own generic relationship fan-out, durable reminder/reconciliation, and checkpointed process-runner seams; generic gateway submission exists (AD-20 R4/R6/R7/R11) | No generic fan-out, durable-reminder/reconciliation, or process-runner API exists in the checked-out EventStore source. `IEventStoreGatewayClient` exists and Works already consumes it. Equivalent reminder/cascade behavior is currently Works-owned. | [EventStore repository](https://github.com/Hexalith/Hexalith.EventStore) confirms the project exists; actual availability was determined from the pinned source/API surface | **R4/R6/R7 are target allocations, not available SDK capabilities; R11 exists now.** The matrix should label that asymmetry explicitly. |
| `Hexalith.Platform` exists, currently has an Aspire 13.4.6 scaffold, owns the Agents external-host contract, and is the Works topology destination (AD-20) | Public repository is non-archived; `apphost.cs` pins `Aspire.AppHost.Sdk@13.4.6` and creates an empty builder; `global.json` is SDK `10.0.302`; no Dapr or Works composition and no `verify-works-host` exists | [Repository](https://github.com/Hexalith/Hexalith.Platform), [repository API](https://api.github.com/repos/Hexalith/Hexalith.Platform), [current apphost.cs](https://raw.githubusercontent.com/Hexalith/Hexalith.Platform/main/apphost.cs), [current global.json](https://raw.githubusercontent.com/Hexalith/Hexalith.Platform/main/global.json), [Agents host contract](https://github.com/Hexalith/Hexalith.Platform/blob/main/docs/ext-host-1-agents-composition.md), [Agents verification script](https://github.com/Hexalith/Hexalith.Platform/blob/main/eng/verify-agents-host.sh) | **Existence/current scaffold facts confirmed. Works fit is a stated target only and remains wholly unproved.** The script verifies the empty scaffold and repository boundary; it is not evidence of a live Agents or Works topology. |
| Works target ships no AppHost/ServiceDefaults, while current hosting remains until Story 4.9 (AD-20) | `src/Hexalith.Works.AppHost`, `src/Hexalith.Works.ServiceDefaults`, `aspire.config.json`, and their solution entries still exist; Dapr components define Redis state/pubsub, resiliency, access control, Sentry, placement, and Scheduler | — | **Accurately described as target versus current state.** Do not delete until Platform reproduces and proves the R1–R11 rows. |
| Test stack is xUnit v3 on Microsoft.Testing.Platform and architecture fitness is build-time truth | `global.json` selects MTP; central `xunit.v3` is `4.0.0`; live assembly execution passes 237 tests; solution build does not execute them | [xunit.v3 4.0.0](https://www.nuget.org/packages/xunit.v3/4.0.0) is stable | **Technology confirmed; execution claim contradicted.** Open register text saying the lane “currently fails compilation” is stale, while prose saying it runs every build is still false. |
| Hexalith serialization/ID packages exist at selected lines (AD-01–03) | Central pins: EventStore `3.103.0`, PolymorphicSerializations `1.19.2`, Commons `2.30.0`; source generators/UniqueId helper are present in pinned submodules | [EventStore index](https://api.nuget.org/v3-flatcontainer/hexalith.eventstore.domainservice/index.json), [PolymorphicSerializations index](https://api.nuget.org/v3-flatcontainer/hexalith.polymorphicserializations/index.json), [Commons index](https://api.nuget.org/v3-flatcontainer/hexalith.commons/index.json) | **Confirmed.** |
| Fluent UI Blazor V5 is future stack and unused in Works v1 | Central pin is `5.0.0-rc.5-26219.1`; no Works project references it | [Official NuGet listing](https://www.nuget.org/packages/Microsoft.FluentUI.AspNetCore.Components/) shows stable `4.14.4`; V5 remains prerelease | **Preview, but explicitly out of v1 scope.** Track at the future UI adoption gate. |

## Findings

### TECH-01 — HIGH — AD-19's authority model is not the live version topology, and current security/bug-fix patches have moved

AD-19 says `global.json` and central package props are the only authoritative pins, with Dapr runtime owned by Platform. In reality, the active Dapr runtime is authoritatively hard-coded three times in Works source (`DaprSelfHostedMtls.cs`) and Platform owns no Dapr configuration. This was already a documentation/current-state mismatch; it is now operationally visible because the official current releases moved after the previous gate: .NET SDK/runtime are `10.0.401`/`10.0.12` while this environment is `10.0.400`/`10.0.11`; Dapr runtime is `1.18.4` while the image pin is `1.18.3`; Dapr.Client is `1.18.7` while the central SDK line is `1.18.5`.

This is not a runtime-versus-SDK compatibility failure: Dapr's own policy supports the same-minor combination. It is a provenance/currentness failure. Also, `rollForward: latestPatch` does not float a `10.0.400` pin to SDK `10.0.401`; the pin must change.

**Disposition: discuss / update before Story 4.9 acceptance.** Make the current-to-target split explicit: until R1 lands, Works source owns the Dapr runtime image pins; afterward Platform owns them. Add Dapr runtime image and Platform `global.json`/Aspire pin locations to the version-source inventory, or narrow “only authoritative” to package/SDK pins. Refresh patches through the normal dependency process and rerun build/integration lanes; do not merely edit prose to claim currentness.

### TECH-02 — HIGH — The “single Aspire 13.5.x family” conceals a preview-only Dapr integration and no platform proof

Core Aspire `13.5.3` is stable. The required Aspire-to-Dapr adapter selected by the current AppHost is not: `CommunityToolkit.Aspire.Hosting.Dapr 13.5.0-preview.1.260825-0345` is the only listed 13.5-compatible line. AD-20 speaks of a supported 13.5 family but neither names this package nor accepts preview dependencies. Meanwhile, `Hexalith.Platform` remains an empty 13.4.6 AppHost scaffold with a different SDK band (`10.0.302`), no Dapr control plane/components, no Works lane, and no Works module composition.

The repository exists and is the legitimate target owner; it is not yet evidence that the topology fits there. R1 must reproduce the non-trivial Sentry/mTLS, placement, Scheduler persistence, Redis components, access control, resilience, dead-letter, and project-resource graph currently held by Works.

**Disposition: discuss / gate.** Bind one of three policies: explicitly allow the preview toolkit with an upgrade trigger and risk owner; remain on a stable compatible Aspire/toolkit family; or implement the Dapr resources without that preview package. Extend `verify-works-host` acceptance to assert the exact runtime image, component files, trust configuration, Scheduler persistence/HA posture, and all R1–R11 scenarios before removing Works hosting projects.

### TECH-03 — MEDIUM — Three EventStore-owned migration seams are specifications, not available APIs

The checked-out EventStore line provides the current domain-service/query/projection/rebuild/gateway primitives, including `/project/v2` and the shared rebuild lifecycle. It does **not** provide the generic relationship fan-out (R4), durable reminder plus reconciliation (R6), or checkpointed process runner (R7) allocated to it. Comparable runtime behavior remains in Works. R11 differs: the generic gateway client already exists and is consumed.

The architecture does say missing reusable capability must be added to EventStore first, so this is not a false claim that all three are live. The problem is status ambiguity: “EventStore ships every generic runtime seam” is written as present tense, while the migration rows do not identify which dependencies are absent or name producing work/version evidence. The “high confidence in EventStore API facts” statement is therefore too broad.

**Disposition: update planning truth.** Add an availability column or per-row status: R4/R6/R7 `absent / producer work required`, R11 `available`. Bind the producing EventStore issue/story, minimum published package version, and contract/API test for each absent seam. Story 4.9 can begin discovery/composition, but the affected rows cannot go green until those APIs exist and are proven from Platform.

### TECH-04 — MEDIUM — Architecture-test reality changed, but both the open register and workflow prose are wrong in opposite directions

`VAL-M01` says the architecture-test lane “currently fails compilation.” Fresh reality: the Release solution build succeeds with zero warnings/errors, and direct MTP execution passes all 237 architecture tests. Yet `dotnet build Hexalith.Works.slnx` only compiled the test assembly; it did not run it, contradicting “architecture-fitness tests run in the build” at architecture.md:1035–1036.

**Disposition: autofix candidate.** Change VAL-M01 to “compiles and passes when explicitly run; not wired into build/CI evidence,” and either wire execution into a defined verification/CI lane or change the workflow wording to name the explicit test command. Keep compilation and execution as separate claims.

### TECH-05 — LOW — Future Fluent UI V5 remains prerelease

The actual central pin is `5.0.0-rc.5-26219.1`, and the official stable package remains 4.14.4. The architecture explicitly excludes UI from Works v1, so this does not affect present fit.

**Disposition: defer.** Revalidate at the first UI theme; do not call V5 stable until NuGet has a stable V5 package and the pin is updated.

## Confirmed claims with no finding

- Dapr at-least-once pub/sub and the prohibition on portable ordering assumptions are correct.
- Dapr actor per-identity turn locking plus EventStore's actual bounded ETag-conflict recovery support AD-08.
- Scheduler-backed reminders and the architecture's conditional durability wording are technically sound; only deployment proof remains open.
- Current EventStore core extension methods, routes, shared rebuild actions, projection/query/read-model/cursor APIs, and gateway client exist in the pinned source.
- `Hexalith.Platform` is real, public, non-archived, and carries the named Agents external-host contract; it simply has no implemented Works topology yet.
- Aspire core 13.5.3, xUnit v3 4.0.0, EventStore 3.103.0, PolymorphicSerializations 1.19.2, and Commons 2.30.0 exist in the official registries and match the checked configuration.
- Current Works AppHost/ServiceDefaults existence is not a contradiction because the architecture explicitly identifies their removal as target state gated by Story 4.9.

## Prior-gate delta (comparison only after fresh assessment)

Compared with the 2026-09-08 technology/reality review:

| Prior item | 2026-09-12 result |
|---|---|
| Preview-only Aspire/Dapr integration and empty Platform host | **Persists; remains HIGH.** Platform public main still has an empty 13.4.6 scaffold and no Works lane. |
| AD-19 current-location/ownership mismatch | **Persists and worsened.** The location mismatch remains, and official .NET/Dapr patch releases now make the checked pins non-current. This gate does not repeat the prior claim that every pin is current. |
| Missing R4/R6/R7 EventStore seams | **Persists, bounded more precisely.** They are legitimate target allocations but unavailable today; R11 is available. |
| Reminder durability | **Semantics reconfirmed.** The architecture's conditional wording is correct; HA/backup deployment proof remains future R6 evidence rather than a new incompatibility. |
| `/project/v2`/fencing substrate and SDK-band divergence | **Still relevant supporting evidence**, consolidated into TECH-02/03 rather than repeated as separate findings. |
| Architecture-test compilation failure | **Changed.** Compilation and 237 tests now pass; only automatic execution remains absent. |
| Fluent UI preview | **Persists, LOW and deferred** because Works v1 is headless. |

## Severity and disposition summary

| Tier | Count | Findings | Required disposition |
|---|---:|---|---|
| Critical | 0 | — | — |
| High | 2 | TECH-01, TECH-02 | Resolve version authority/current-to-target ownership and accept/replace/gate the preview hosting dependency before Platform migration acceptance |
| Medium | 2 | TECH-03, TECH-04 | Mark absent SDK seams with producer/version proof; correct and wire the architecture-test lane claim |
| Low | 1 | TECH-05 | Recheck when UI work enters scope |

**Gate disposition:** retain the architecture's overall `CONCERNS` posture. No technology finding requires rewriting the domain decisions, but AD-19/AD-20 and the migration matrix need explicit current-state/version/preview status before the spine can claim technology-ready Platform composition. Story 4.9 removal gates remain mandatory.
