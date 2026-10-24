---
title: 'Close five deferred date-reminder correctness gaps'
type: 'bugfix'
created: '2026-10-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred entries concern repeated-date occurrence identity, reconciliation and steady-state scheduling delays, duplicate date awaits, and pending-index repair after a decoder upgrade. The SDK migration replaced most historical mechanisms, but the current pending-date fold still preserves duplicates and the replacements need focused qualification.

**Approach:** Normalize duplicate date awaits in the existing pure projection; execute identity, scheduling and persisted-rebuild controls against the current Works/SDK implementations. Resolve exactly these five ledger entries with dated evidence, independently review, verify, commit and push Works main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: `7ab935d39795d4f198b44e1f933b8822ce96a14a`; clean Works main. Freeform maintenance, no epic story key. Small local correction plus focused tests and ledger/evidence updates; no public API, topology, dependency, migration or unresolved user choice. The requested commit and push are authorized.
- Selected ledger summaries: Give repeated occurrences of the same date await distinct durable resume identities without losing redelivery idempotence; Recompute date-reminder delay at each scheduling operation during reconciliation; Normalize duplicate identical date awaits at the durable await-set boundary; Preserve pending-date-await index repairability when a decoder upgrade recognizes a previously unknown event; Recompute date-reminder delay for each steady-state registration.
- `PendingDateAwaitProjection.PendingDateAwaits` filters date conditions but lacks the `.Distinct()` already used by `WorksReminderIntentSource.GetCurrentIntentsAsync`. Deduplicate normalized conditions without changing their order, source events, terminal clearing or domain await semantics. Verify UTC-equivalent duplicates, distinct dates, both ordinary SDK commits and full rebuilds, restart and redelivery.
- `WorksReminderIntentSource` binds date intents to suspension envelope sequence and message ID. Extend qualification to stable repeated reads, same-date later suspensions, exact command payload and SDK effect identities; preserve independent expiry.
- Current SDK `ReminderCoordinator` refreshes its clock for each operation and immediately before arming. Existing owner tests `FutureIntentUsesFreshClockWhenArmed` and `SlowFutureLookupUsesRemainingDelay` qualify fold/lookup latency and elapsed deadlines. Add consumer timing controls if needed through actual SDK composition with a test-only scheduler boundary; do not duplicate SDK algorithms or add production wrappers.
- Full replay replaces complete pending documents and watermarks from authoritative histories. Qualify repair from a synthetic legacy high-watermark projection over unchanged currently-decodable envelopes; distinguish that upgrade fixture from executing an older decoder. Unknown current evidence must still refuse/park as existing tests require.
- Relevant constraints: architecture AD-09/11/13/26/27 and state baseline. Persistence stays behind SDK `IReadModelStore` and epoch coordinator; assertions inspect retained documents after reconstruction. No Works AppHost exists and this change requires no live topology.
- Validate explicit Debug runtime/integration builds and direct xUnit named classes; run the deterministic integration suite after focused checks. Record broad build blockers separately and use the documented existing-output fallback only if needed. Run SDK owner timing tests with exact binary/source identity recorded, without claiming new public-package or live qualification. Retain commands, results and hashes, validate commitlint, fetch and inspect divergence before pushing main.

- The broad Debug integration build exits 1 with the two existing Platform `CA1062` errors in `PlatformHostedPublishing.Describe` and `ComposeNativeProbe`; focused compilation succeeds with zero warnings/errors using `BuildProjectReferences=false`. Initial fixture compiles needed the SDK namespace import and configured awaits in private helpers. The first five-case run exposes the duplicate projection defect; three rebuild paths also reveal that the fixture must register its writer before beginning capture. Correct that fixture without changing the SDK admission requirement.

- Corrected pre-fix controls pass three of five cases and fail exactly the ordinary/rebuild duplicate cases, each retaining four entries instead of two. Add `.Distinct()` to the pure date projection. Complete replay controls repair both synthetic legacy variants while comparing reconstructed persisted history across promotion.
- Current consumer scheduling controls execute the actual SDK through its DI composition, using a test-only `DispatchProxy` at the internal scheduler boundary. Six cases pass, including actual due-command submissions with retained synthetic target receipts across same-date occurrences/replay, multiple date registrations, reconciliation across retained targets and slow lookup clamping. The receipt fake models target idempotency; it does not claim a live gateway or actor transport proof.
- An initial five-case owner-assembly run also passes, but its DomainService binary hash differs from the current consumer binary. Treat that as supplementary historical evidence only; current qualification comes from the new consumer tests using the rebuilt source SDK. No submodule files or gitlinks change.

- Post-fix focused qualification passes **86/86**, zero errors/failures/skips/unexecuted, across WorksDateReminderCorrectnessTests, WorksDateReminderSchedulingTests, WorksSdkReminderDeliveryTests, WorksSdkIntentTranslationTests and WorksSdkProjectionQueryTests. Runtime and focused integration Debug builds succeed with zero warnings/errors.
- The whitespace gate initially identifies LF endings in the edited pre-existing projection. Normalize that edited file to required CRLF; final whitespace verification passes with only the generic workspace-load warning. Its semantic production diff remains one `.Distinct()` call. The ledger retains its existing file formatting and every original line; exactly five dated statuses/resolutions are added.

## Review Triage Log

- Medium / patch: scheduling fixtures used synthetic workload/purpose values, so their actual submissions did not qualify production Works wiring. Compose through `AddWorksReminderAndCascadeRecovery` and replace only external boundaries; assert the production values.
- Medium / patch: candidate helper calls bypassed the production dispatcher's partitioned rebuild branch. Run the duplicate-rebuild and legacy-repair operations through `DomainSharedProjectionRebuildDispatcher`, including admission, partition accumulation/finalization, staging and promotion. Retain direct plan construction only to synthesize old persisted state.
- Medium / patch: successful fake target receipts did not prove persisted SDK Submitted/replay metadata. Inspect actual audit records for each submission/replay and count exactly two newly created target receipts across four submissions.
- Medium / patch: due-only convergence did not qualify a stale callback from an earlier same-date occurrence. Fire that callback while the later witness exists and assert no old submission or change to current witness.
- Low / patch: pending-index assertions omitted tenant/item/correlation fields. Compare complete `PendingDateAwait` records after reconstruction for both write paths and repaired generations.
- Low / patch: array length did not prove unchanged mixed-await source evidence. Compare serialized suspension bytes and the complete mixed condition set before/after projection.
- Medium / patch: registration/pass latency cases left every later intent in the future. Add earlier-operation latency that crosses a subsequent due instant, asserting actual submission/disposition and retained independent future witnesses.

- The initial deterministic suite passes **917/917**, zero errors/failures/skips/unexecuted, with live Smoke/Platform classes explicitly excluded. This is intermediate evidence before the review coverage corrections; final results will supersede it. No new runtime defect was reported. All seven findings are verification gaps caused by this change and remain within the existing intent; none is deferred.

- All seven review corrections are implemented. Production Works registration now configures scheduling tests; public external transports/catalog/store remain synthetic boundaries. The identity control inspects SDK Submitted/Success audit records, alternating replay flags and exactly two newly written target receipts; a late old occurrence callback leaves the current witness byte-identical and submits no old command. Crossed-deadline cases cover both ordinary convergence and reconciliation and retain independent future witnesses.
- Duplicate and legacy-repair rebuilds now execute the actual dispatcher partitioned protocol end to end. Complete pending records and serialized mixed-await source preservation are asserted. The only direct candidate build now constructs the explicitly synthetic historical seed, not the operation under qualification.
- One new assertion needed an explicit nullable guard during test compilation; corrected without changing expected behavior. Final post-review runtime and test Debug builds pass with zero warnings/errors. Post-review focused qualification passes **89/89**, zero errors/failures/skips/unexecuted, and whitespace verification passes.

- Independent follow-up confirms all seven corrections and finds no further concrete regression. Existing SDK behavior supplies clock refresh, occurrence identity and replay repair; the production behavior change remains date-await deduplication. No additional deferred entries are created.

## Verification

- Runtime: `dotnet build src/Hexalith.Works/Hexalith.Works.csproj --configuration Debug --no-restore -m:1 -p:BuildProjectReferences=false` succeeds, zero warnings/errors.
- Tests: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1 -p:BuildProjectReferences=false` succeeds, zero warnings/errors. These focused builds consume existing dependency outputs; the separate broad-build Platform `CA1062` blocker remains unchanged.
- Final focused direct xUnit run: **89/89 passed**, zero errors/failures/skips/unexecuted. Final deterministic run: `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-five-date-deterministic.xml` succeeds: **920/920 passed**, zero errors/failures/skips/unexecuted. Live transport and public-package qualification are outside this evidence.
- `dotnet format whitespace Hexalith.Works.slnx --no-restore --verify-no-changes --include` the four edited/new C# files succeeds with only the generic workspace-load warning. `git -c core.whitespace=cr-at-eol diff --check` succeeds. `npm run commitlint -- --edit /tmp/works-five-date-commit.txt` accepts `fix: close five deferred date reminder gaps`.
- [Retained evidence](evidence/five-deferred-date-reminder-correctness-2026-10-24/manifest.json) binds final sources, dependency revisions and actual consumer binaries to commands and compressed logs/XML, including the two-case red duplicate reproduction and broad-build blocker. The red run precedes review fixture strengthening; final source hashes qualify the final green runs.
- All seven independent review findings are patched and verified by follow-up. Exactly five original ledger entries gain dated resolutions, with their evidence preserved; no new deferred entry, dependency version or gitlink change. Fetch before commit shows no divergence from origin/main.
