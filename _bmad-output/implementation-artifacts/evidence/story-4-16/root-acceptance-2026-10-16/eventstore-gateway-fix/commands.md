# Prepared EventStore gateway fix

The public 3.123.0 producer remains the acceptance dependency. These results prove an unpublished source correction; they do not accept Works 4.16. No package versions or remote state were changed.

The legacy replay lease returned before retrying named projection work. A failed legacy HTTP invocation could therefore block both a newer named head and a retry of the same head for five minutes. The original MinVer override did not change the actual assembly informational version: it remained 3.123.0 despite the modified source. The result is explicitly an unpublished source build, never the public archive. A separate build sets Version, PackageVersion, AssemblyVersion, FileVersion and InformationalVersion explicitly before any candidate live diagnostic.

The correction validates the current authoritative prefix and gives it to the independent named coordinator while preserving the original legacy intent, invocation token, lease and candidate.

In `/tmp/story416-sdk-gateway-isolated`:

```bash
dotnet build tests/Hexalith.EventStore.Server.Tests/Hexalith.EventStore.Server.Tests.csproj -c Debug --artifacts-path /tmp/story416-sdk-gateway-red -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=3.123.1-story416-gateway
/tmp/story416-sdk-gateway-red/bin/Hexalith.EventStore.Server.Tests/debug/Hexalith.EventStore.Server.Tests -noLogo -method Hexalith.EventStore.Server.Tests.Projections.ProjectionRebuildProductionPathTests.NamedDelivery_AdvancedHeadProgressesWhileUnsuccessfulLegacyReplayKeepsItsOriginalLease -result-xml /tmp/story416-sdk-gateway-red.xml
```

Before the correction: exit 1; 1 failed, 0 skipped; the named coordinator received head 1 but never head 2. After the correction and two unchanged-head cases, the same build passed with zero warnings/errors. The byte-identical consumer was copied under the isolated repository’s standard `tests/Hexalith.EventStore.Server.Tests/bin/Debug/net10.0` layout for source-reading checks.

```bash
tests/Hexalith.EventStore.Server.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.Tests -noLogo -class Hexalith.EventStore.Server.Tests.Projections.ProjectionRebuildProductionPathTests -class Hexalith.EventStore.Server.Tests.Projections.DaprProjectionActivationOutboxTests -result-xml /tmp/story416-sdk-gateway-green2.xml
tests/Hexalith.EventStore.Server.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.Tests -noLogo -namespace Hexalith.EventStore.Server.Tests.Projections -result-xml /tmp/story416-sdk-gateway-projections.xml
```

Strict related regression gate: exit 0; 59 passed, 0 failed, 0 skipped, 7.051 seconds. Broader projection namespace: exit 0; 774 passed, 0 failed, 17 existing skipped ATDD tests, 55.318 seconds. Those skipped tests are not counted as acceptance evidence.

The optional unfiltered server run from the external artifacts layout exited 1: 5086 passed, 572 failed, 25 skipped. The output location broke source/evidence-root discovery and the archive had no Git repository; one unrelated semaphore test also exposed a scheduling race. Its exact command and results are retained in the manifest. A standard-layout rerun uses the byte-identical consumer copied under the actual owning SDK repository, so source/evidence and read-only Git checks resolve the actual checkout; it is recorded separately.

In `W/references/Hexalith.EventStore`:

```bash
tests/Hexalith.EventStore.Server.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.Tests -noLogo -result-xml /tmp/story416-sdk-gateway-full-correct-layout.xml
```

The standard-layout full server run completed with exit 0: 5,658 passed, 0 failed, 25 pre-existing skipped ATDD tests, 1,060.346 seconds. No skips count as Works acceptance. The source/evidence/read-only Git checks passed. The separately versioned `3.123.1-story416-gateway` build passed 59/59 strict related regressions with zero skips in 2.980 seconds.
