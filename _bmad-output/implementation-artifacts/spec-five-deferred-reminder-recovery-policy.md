---
title: 'Close five deferred reminder recovery policy gaps'
type: 'bugfix'
created: '2026-10-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred reminder operations entries still refer to removed startup-only recovery, lost partial evidence after non-caller cancellation, misleading retry wording, parking-read guidance, and an empty discovery registry racing projection backfill. Current SDK replacement behavior needs executing consumer qualification and accurate operator guidance.

**Approach:** Verify continued periodic reconciliation after the former startup budget, retained counts and durable incomplete evidence across non-caller cancellation, and discovery after an empty pass once projection membership arrives. Update the runbook for current SDK diagnostics and correct historical parking guidance. Close exactly those five entries with retained evidence, independently review, validate, commit and push main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: `d3c26ab1f9cde7dfcae5c8bed57a0d50fdbd2445`; clean Works main. Freeform work, no story key. Small test/documentation qualification with no public API, production runtime, dependency, topology, migration, unresolved user choice or irreversible data operation. The requested commit/push is authorized.
- Selected exact summaries: Preserve typed incomplete-scan context when partial reminder processing ends in a non-caller cancellation; Correct EventId 4603's pre-existing unconditional retry promise across all recovery callers; Broaden EventId 4608 operator guidance to cover non-availability parking-read failures and identify the parking record without implying its key is logged; Add a post-startup reminder-reconciliation retry or terminal readiness policy after the bounded startup attempts are exhausted; Determine whether an empty pending-await tenant registry can race ahead of projection backfill and strand pre-index reminder recovery.
- Reuse production `WorksRecoveryExtensions.AddWorksReminderAndCascadeRecovery`, real SDK reconciler/index/coordinator, `InMemoryReadModelStore`, `WorksReminderSchedulerProxy`, authoritative stream fixtures and shared-projection journal/catch-up. SDK internal seams are invoked reflectively as in existing consumer scheduling tests; external actor transport is synthetic.
- Add `WorksSdkReminderRecoveryPolicyTests.cs` and a single-type structured log capture helper. Qualify BackgroundService retries beyond six failures, Degraded-to-Healthy readiness and persisted discovery; foreign cancellation isolates the failed candidate and retains earlier/later counts before a successful reconstructed sweep. Verify disabled recovery explicitly degrades readiness.
- Empty-registry qualification starts with an actual empty completed sweep, then journals committed Works history with control-index membership and reconstructs services. The real catalog plus real reminder source/coordinator must persist the candidate/witness and arm the future reminder. This does not qualify inventories missing all family membership; that distinct restore entry stays open.
- `docs/operations/subscriber-dead-letter-operator.md` already labels 4600-series events historical. Add explicit current 200210/200211/200212 and retention/submission diagnostics, qualify periodic retry/readiness semantics, correct historical 4608 guidance for all read/fold causes and derive the record from tenant/item rather than claim its key was logged. Preserve historical operator evidence and exact caller cancellation semantics.
- Architecture AD-09/11/27 and Hexalith state baseline apply. Assertions inspect serialized persisted documents after reconstruction. There is no Works AppHost; these consumer tests require no live topology.
- Validate Debug test compilation and direct named xUnit classes, then deterministic integration regression. Record exact broad-source blockers separately and use the established focused existing-output fallback when needed. Retain commands/results, source/binary hashes and dependency revisions, validate Conventional Commit with commitlint, fetch and inspect divergence, then push main.

- Implemented five executing cases covering continued periodic recovery, provider cancellation and ordinary outage isolation, empty-sweep/backfill/reconstruction recovery, and disabled-worker diagnostics/readiness. Production registration, SDK owner code, real Works catalog/source and serialized store are exercised; the registrar/actor transport boundary is synthetic where stated. Initial focused qualification passes 5/5, zero failures/errors/skips/unexecuted.
- Broad Debug test build exits 1 with the existing two Platform `CA1062` errors in `PlatformHostedPublishing.Describe` and `ComposeNativeProbe`. Exact command: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1`. The focused fallback adds `-p:BuildProjectReferences=false` and succeeds with zero warnings/errors, consuming available dependency outputs. This does not claim a fresh complete Platform source build.
- Updated the operator runbook with current SDK events, retained-work and readiness semantics, plus corrected historical parking-read guidance. Exactly five original ledger entries gain dated status/resolution fields; original evidence remains intact. No production/dependency/gitlink changes.

## Review Triage Log

- Medium / patch: stopping after observing pass completion could race the next pending pass. Cancel the worker lifetime synchronously at the successful log boundary, then await shutdown before asserting Healthy.
- Medium / patch: reconstruction before backfill could conceal a host-local empty-sweep latch. Run empty/backfilled passes in the same provider; reconstruct only for lost-scheduler repair, and assert the full persisted candidate tuple and witness source/status/revision.
- Medium / patch: isolated candidate cancellation did not combine earlier tenant scan failure with later candidate failure. Inject an earlier malformed tenant index alongside the middle-candidate foreign cancellation/outage; require both incomplete counts, scan/candidate diagnostics and later success, then restore exact tenant bytes and retry after reconstruction.
- Medium / patch: exact-host-cancellation wording implied exception-token matching. Correct the caller-token condition and execute shutdown with a differently tokened provider cancellation, verifying no completed summary, pending readiness and durable cursor resumption.
- Low / patch: historical parking guidance lacked a concrete lookup derivation. Document the retired projection key and distinguish generation parking.
- Medium / patch: linked retained evidence was not yet present at review time. Complete and verify the packet before commit and final ledger acceptance.

- Initial related qualification passes 83/83 and deterministic integration passes 925/925, zero errors/failures/skips/unexecuted. These precede the review corrections and will be superseded by final results. All six review findings are within scope; nothing is deferred.

## Verification

- Post-review focused Debug build succeeds with zero warnings/errors. Final related qualification passes **84/84**; final deterministic integration qualification passes **926/926**, both with zero errors/failures/skips/unexecuted. The deterministic run explicitly excludes live Smoke/Platform classes and does not claim live or public-package qualification.
- Final commands: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1 -p:BuildProjectReferences=false`; `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorksSdkReminderRecoveryPolicyTests' -class '*WorksDateReminderSchedulingTests' -class '*WorksSdkReminderDeliveryTests' -class '*WorksSdkRecoveryCompositionTests' -class '*WorksSdkRecoverySourceTests' -result-xml /tmp/works-five-recovery-final.xml`; `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-five-recovery-final-deterministic.xml`.
- Six new cases exercise the actual SDK with synthetic external boundaries. Combined tenant-index corruption and foreign cancellation retain two incomplete causes and both successful candidate totals; exact original tenant bytes restore all four candidates after reconstruction. Host cancellation with a differently tokened provider exception leaves the durable cursor and pending readiness, emits no completion, and resumes the remaining candidates after restart. Empty/backfilled discovery occurs in the same service lifetime; full witness/candidate assertions and subsequent lost-scheduler repair survive reconstruction.
- Whitespace verification: `dotnet format whitespace Hexalith.Works.slnx --no-restore --verify-no-changes --include tests/Hexalith.Works.IntegrationTests/WorksSdkReminderRecoveryPolicyTests.cs tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryLogCapture.cs` exits 0 with only the generic workspace-load warning. `git -c core.whitespace=cr-at-eol diff --check` passes. Markdown preserves its existing LF format; new C# and spec files use CRLF.
- `npm run commitlint -- --edit /tmp/works-five-recovery-commit.txt` accepts `test: close five deferred reminder recovery gaps`. Fetch shows HEAD and origin/main both `d3c26ab1f9cde7dfcae5c8bed57a0d50fdbd2445`, zero divergence. No dependency or gitlink changes.
- [Retained packet](evidence/five-deferred-reminder-recovery-policy-2026-10-24/manifest.json) includes final commands, actual result XML, compressed logs, source/binary hashes and full dependency revisions. The unsuccessful broad-source build is retained separately from the passing existing-output fallback. All six independent review findings are patched; nothing new is deferred.

- Independent follow-up confirms all six findings resolved, verifies twelve source hashes, five binary hashes, dependency revisions and all nine compressed artifacts, and independently reruns the six policy cases successfully. No remaining findings or new deferred work. Original ledger bytes are preserved with exactly five two-line status/resolution insertions.
