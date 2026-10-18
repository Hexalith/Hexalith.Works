# Verification commands

Commands below ran from the Works root unless another working directory is stated. Full output and xUnit XML are in `raw/`, indexed by `raw-artifacts.json`. Source Debug, candidate Release and default Release are distinct lanes. Commands must run sequentially when they share project output directories.

Final SDK regression, from `references/Hexalith.EventStore`:

```bash
dotnet build tests/Hexalith.EventStore.Client.Tests/Hexalith.EventStore.Client.Tests.csproj --configuration Debug -m:1 -p:UseHexalithProjectReferences=true -p:UseNuGetDeps=false
dotnet tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests.dll -result-xml /tmp/story415-20261018-sdk-all-accepted.xml
```

Final Works source and unchanged auxiliary checks:

```bash
dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1
dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/story415-20261018-debug-integration-accepted.xml
dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/story415-20261018-architecture-accepted.xml
dotnet tests/Hexalith.Works.UnitTests/bin/Debug/net10.0/Hexalith.Works.UnitTests.dll -result-xml /tmp/story415-20261018-UnitTests.xml
dotnet tests/Hexalith.Works.PropertyTests/bin/Debug/net10.0/Hexalith.Works.PropertyTests.dll -result-xml /tmp/story415-20261018-PropertyTests.xml
```

The immutable nine-package producer used `3.124.1-story415.20261018.1` for `Version`, `PackageVersion` and `HexalithEventStoreVersion`, with Release, `UseHexalithProjectReferences=false` and `UseNuGetDeps=true`. Its exact per-project commands and exits are retained in `provenance--pack-results.json.gz`; `provenance--pack.py.gz` refuses an existing candidate. Do not overwrite these archives when repacking a later correction.

Final candidate package lane:

```bash
dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1 -p:HexalithEventStoreVersion=3.124.1-story415.20261018.1 -p:RestoreAdditionalProjectSources=/home/administrator/projects/hexalith/works/.artifacts/story-4-15/feed
dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/story415-20261018-candidate-release-integration-bound.xml
```

The unchanged default Release was then rebuilt without an override and checked independently; its dependency graph and DLL hash prove public **3.124.0**, which remains unfixed:

```bash
dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1
dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorksSdkPackageContractTests' -result-xml /tmp/story415-20261018-default-release-package-contract.xml
```

The source Debug lane was rebuilt after that default Release check. The candidate live restart ran before the default Release rebuild, against the explicitly selected candidate Release consumer. Exact executed wrappers, environments and commands are retained for each attempt in `*--run.py.gz` and `*--result.json.gz`. They select only the relevant fact:

```bash
dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -method '*SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart' -longRunning 45 -result-xml /tmp/story415-20261018-fixed-live-3/restart.xml
dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -method '*SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery' -longRunning 45 -result-xml /tmp/story415-20261018-final-callback/callback.xml
```

The wrappers use `WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS=180`, the retained approved copied Dapr configuration, and their own `WORKS_SDK_PROOF_EVIDENCE_PATH`. They temporarily adjust the kernel watch limit through `sudo -n sysctl` and always restore the original value. No domain wait budget is expanded. The earlier passing source restart uses its separately retained exact wrapper.

The cold package consumer and faithful native-state replay retain their complete projects, programs, NuGet source mappings, logs, restore payload and replay driver. Their original and corrected producers remain separate. The replay driver is deliberately restricted to its owned `works-story415-closeout-probe` Redis fixture. The final packet verifier requires no running services or local package feed:

```bash
python3 _bmad-output/implementation-artifacts/evidence/story-4-15/review-fixes-2026-10-18/verify-evidence.py
```

Final post-review checks use the same source/candidate commands against the final decoder; both complete integration lanes pass 848 cases. `root-closeout/raw/run.py.gz` preserves the exact sequential build/test/live wrapper, and `root-closeout/report.json` records each command and result. The final source and candidate restarts pass all ten persisted phases; callback registration and all auxiliary lanes pass. Validate the complete copied packet with:

```bash
python3 root-closeout/verify-final.py
```
