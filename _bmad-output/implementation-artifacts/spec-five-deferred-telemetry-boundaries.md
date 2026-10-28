---
title: 'Close five deferred authorization and telemetry boundary gaps'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Close five deferred items in Works: sanitize identity-provider failure diagnostics (entry 189); contain authorization logger failures (190); prove collector label/instrument refusals through the real exporter (R27A-V1); validate effective fixture OTLP settings (G3A-B1); and require a post-settlement exporter observation for recovery alert clearance (G3A-B3).

Authorization outages must return Forbidden before any Works state reads, even when a logging sink throws. Logs retain bounded identity/category metadata without raw exception data. The telemetry fixture must explicitly redirect endpoint and protocol together and its combined environment must retain loopback HTTP/protobuf settings. Exporter-generated forbidden labels and instrument shapes must produce refusal evidence, never accepted points. An alert-clear observation taken before settlement must not satisfy the live phase even if delivered afterward. Preserve caller cancellation, valid authority, existing allowlists, canonical after-last-raise ordering, and recovery deadlines. Record five owning resolutions and reconcile duplicate R26-B1 to the same privacy correction. Verify, review, commit, and push to main as requested.

</frozen-after-approval>

## Implementation Notes

- Investigation found no unresolved intent choices. All corrections are local and reversible; the requested commit/push is authorized. The footprint is three small behavioral corrections, diagnostic helper reuse, and focused integration regressions; no dependency or public API changes are needed.
- Code map: `Queries/WorksQueryPolicy.cs` catches provider errors; `Runtime/WorksFailureDiagnostics.cs` already supplies bounded, sink-safe source-generated diagnostics. Extend it and reuse `Category`/`Bound`.
- `WorksQueryPolicyDeadlineTests.cs` already drives both query handlers and asserts denial before store access; reuse its helpers and the existing recording/throwing loggers for privacy and sink-failure controls.
- `WorksPlatformTelemetryCollectorTests.cs` uses a real OTLP HTTP/protobuf exporter. Add forbidden attribute, unknown reason, reason on alert, unlabeled reason, and nongauge controls with refusal/no-point assertions.
- `WorksPlatformCompositionGuard.cs` currently validates preliminary fixture writes only. Validate the combined pair while retaining explicit fixture ownership of both keys; add real callback-based conditional-overwrite/removal regressions and valid redirect controls in its existing test class.
- `WorksSdkRecoveryPipelineSmokeTests.cs` has a receipt-only clear wait. Bind it to the exported timestamp as well; extract a private predicate for deterministic delayed-point controls, preserving canonical ordering outside this phase.
- Runtime baseline: `aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json` exited 2 with NU1605 downgrades for CommunityToolkit.Aspire.Hosting.Dapr (13.6.2-beta.959 to 13.6.1-beta.935) and EventStore Aspire/ServiceDefaults (3.128.3 to 3.128.0). Platform did not start. Focused Debug and Release integration builds/tests will verify these corrections; live requalification remains separately blocked by those pins.

- Implemented source-generated EventId 4512 using the existing finite exception categories and bounded metadata helper; raw provider exceptions are not attached. Both logger IsEnabled and Log failures are contained at the diagnostics boundary. Caller cancellation remains in its existing catch branch.
- Added both-handler privacy and logger-failure theories, real-exporter invalid label/shape controls, combined-environment conditional callback controls (including removals and a valid redirect), and a deterministic delayed-clear timestamp control used by the live phase predicate.
- The first focused run exposed an invalid overlong tenant in the new detail-query fixture (two failures). Corrected to an admitted 64-character tenant; control-character/truncation coverage stays on CorrelationId. The initial Debug and Release builds succeeded with zero warnings/errors.

- Focused Debug execution passed all 45 ordinary tests (47 selected, two explicit live facts not run), with zero failures/skips. Both authorization handlers, exporter negatives, conditional callback refusals/positive redirect, and delayed-clear controls executed.

- Independent blind review completed. Tightened OTLP ownership to require a valid fixture-declared pair and identical effective values, with already-HTTP/protobuf Platform regressions. Added supplementary settlement/clear evidence without changing the canonical point-array contract. Every semantic exporter refusal now contains a valid point before the invalid instrument and asserts the real HTTP 400 response plus zero retained points. Reused the existing HTTP evidence handler; no new public API or dependency.
- Before review amendments, the complete ordinary Debug integration run passed 1,089 tests with zero failures/skips; seven explicit live facts were not run. Final validation follows the review corrections.

## Review Triage Log

- Medium, patched: checking preliminary key presence weakened fixture ownership. Require a valid declared loopback HTTP/protobuf pair and exact effective pair equality; added invalid preliminary and different effective-loopback controls over Platform HTTP/protobuf settings.
- Maybe-false (unverified medium), deferred: fixture-only OTLP settings bypass the pre-existing Platform-overlap inventory. This is outside the changed overlap logic; a current canonical Platform composition omitting OTLP keys was not demonstrated. Recorded a follow-up to establish that supported capture before extending admission policy.
- Low, patched: the new phase-local settlement boundary was absent from durable evidence. Added a supplementary record containing settledAt and the qualifying clear while preserving the canonical metric-point array.
- Low, patched: invalid-only exporter requests did not cover atomic refusal after earlier valid observations. Added a preceding valid gauge and asserted its earlier position in the actual OTLP request plus zero retained points.
- Low, patched: refusal tests did not bind to transport response status. The real exporter now traverses the existing evidence handler and asserts the collector's HTTP 400 response.

- The new transport-status assertion exposed that OTLP uses synchronous HttpClient.Send, unsupported by the reused async-only handler. Added a dedicated test-only observer for both send paths, preserving real HTTP transport and the caller's response. The preceding attempt's timeout remains nonqualifying; final checks use the corrected observer.

## Verification

- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1 --nologo`: passed on final source, zero warnings/errors; log `/tmp/works-five-telemetry-debug-build.log`.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1 --nologo`: passed on final source, zero warnings/errors; log `/tmp/works-five-telemetry-release-build.log`.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -explicit off -class '*WorksPlatformTelemetryCollectorTests' -result-xml /tmp/works-five-telemetry-exporter.xml`: all seven exporter/collector facts passed on final source, including mixed-batch HTTP 400 refusals.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -explicit off -class '*WorksQueryPolicyDeadlineTests' -class '*WorksPlatformCompositionGuardTests' -class '*WorksPlatformTelemetryCollectorTests' -class '*WorksSdkRecoveryPipelineSmokeTests' -result-xml /tmp/works-five-telemetry-release-focused.xml`: 47 passed, zero failures/skips; two explicit live facts not run.
- `git -c core.whitespace=cr-at-eol diff --check`: passed.
- `npm run commitlint -- --edit /tmp/works-five-telemetry-commit.txt`: passed.
- Runtime qualification remains blocked by the exact AppHost command and NU1605 results recorded in Implementation Notes; this change does not claim a fresh live Platform parity run.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -explicit off -result-xml /tmp/works-five-telemetry-debug-all.xml`: final complete ordinary integration suite passed 1,091 tests, zero failures/skips; seven explicit live facts not run (1,098 selected).
- Five owning deferred items are closed; duplicate R26-B1 points to its owning privacy correction and is counted once. Independent review's four in-scope findings were patched; one pre-existing, unverified telemetry-admission concern is recorded as follow-up.
