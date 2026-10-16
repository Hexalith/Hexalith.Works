# Story 4.15 review follow-up — 2026-10-16

The five open review patches are implemented and verified. The story remains in-progress because fresh current-source restart/cascade verification is blocked before the test body. The frozen intent is byte-identical to HEAD, SHA-256 `7f3e79ec86b3ccd803b7511b55ccbcc12243cc7ccfef6071fc8eba29ebc8e307`.

## Changes

- `WorksSdkProcessAuthorityTests` exercises the concrete default disposition authorizer and audit sink, including persisted quarantine/ETag preservation after audit refusal. Its delegation cases pin all five literal purposes, exact submission/workload/causation/cancellation bindings, fresh credentials on repeated calls, and null authority, null/empty/blank tokens and unknown-kind refusal.
- The protected hosted disposition test forwards the observed authenticated principal to the real default authorizer, asserts one invocation and HTTP 403, and compares the persisted quarantine including its ETag after every denial. The SDK authenticates the signed workload assertion. An adjacent Authorization bearer is refused with HTTP 401 by the SDK's CredentialConflict rule; the review's premise that absence of a bearer implied an unauthenticated principal was incorrect.
- The architecture fitness test reads evaluated PackageVersion items for Contracts, Works and IntegrationTests: Debug omits the Contracts central pin, Release retains the selected candidate, and both root props files declare the same version. The existing MSBuild snapshot helper now retains evaluated Version metadata.
- Malformed-delivery coverage adds the reserved `tenants` tenant and blank envelope correlation metadata, asserting FailedInvalidPayload, unchanged checkpoint/store cardinality, and no registrar/marker calls.
- Mixed-case tenant coverage proves TenantId normalization, the SDK codec's rejection of a raw noncanonical tenant, and successful create/serialized replay/expire using the canonical witness.

The final live attempt also uses an opt-in test-only `WORKS_SDK_PROOF_DAPR_CONFIG_DIRECTORY` seam. It replaces sidecar configuration paths with temporary copies before the application is built. All four copies retain the committed ACL/mTLS configuration byte-for-byte apart from adding `spec.features` with `HotReload=false`; the [config manifest](temporary-configs.json) binds the originals and copies. Dapr documents this supported opt-out in its [component update guidance](https://docs.dapr.io/operations/components/component-updates/). Default fixture behavior and production configuration are unchanged.

## Fresh deterministic verification

All commands ran from the Works root. [The result manifest](results.json) contains exact commands, raw log paths and SHA-256 values.

| Gate | Result |
| --- | --- |
| Debug integration build with restore | Passed, zero warnings/errors |
| Debug focused authority/composition/protected HTTP | 105 passed, zero failed/skipped |
| Debug integration, excluding SmokeTests | 778 passed, zero failed/skipped |
| Release integration build | Passed, zero warnings/errors |
| Release integration, excluding SmokeTests | 778 passed, zero failed/skipped |
| Unit build and complete suite | 589 passed, zero failed/skipped |
| Architecture build and complete suite | 270 passed, zero failed/skipped |
| Existing Debug property assembly | 3 passed, zero failed/skipped; 100 generated examples per property |
| Final Debug and Release builds including the optional fixture seam | Passed, zero warnings/errors |

Debug uses the current root-declared EventStore source projects. Release uses all nine unchanged local `3.122.0-story415.20261016.9` packages; [dependency modes](dependency-modes.json) records both graphs. No producer source, dependency pin, package archive or publication was changed by this dispatch. The current SDK checkout contains changes made after the historical .9 producer proof, so source and package evidence are distinct.

The first `--no-restore` Debug build failed with CS1704 from stale mixed source/package PolymorphicSerializations assets; an explicit Debug restore resolved it. The initial focused test run was 104/105 because the newly added bearer-plus-assertion case incorrectly expected 403; correcting that expectation to the SDK's 401 CredentialConflict and observing the authenticated workload principal produced the green 105/105 gate. Both preliminary logs are retained in the manifest.

## Runtime evidence and remaining blocker

The unchanged default AppHost started under Aspire, but EventStore exited on existing authentication configuration validation: RequireHttpsMetadata must be true outside Development, and WorkloadIssuer ClientId/ClientSecret are required when Authority is configured. The existing isolated fixture supplies its explicit Development/synthetic authority setup. Both manually started AppHosts were stopped through the exact-target Aspire CLI.

Three bounded current-source live attempts were executed:

1. The unchanged fixture failed both tests when Dapr sidecars could not create file watchers: `failed to create watcher ... no space left on device`.
2. Process-scoped `DOTNET_USE_POLLING_FILE_WATCHER=1` still failed both tests. A captured sidecar fatal log confirms the same watcher failure. Read-only `/proc/*/fdinfo` inspection observed 137 inotify descriptors and 1,048,541 watch entries against limits of 8192 instances and 1,048,576 watches; descriptor totals can double-count inheritance. Most entries belonged to unrelated fluentui-mcp processes; the single observed Works daprd held two watches. No shared limits or unrelated process were changed.
3. Adding the temporary supported HotReload=false configuration copies allowed `SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery` to pass and persist its actual reminder witness plus process candidate. The restart/cascade test then failed before its body at a different startup boundary: `[startup/readiness] Resource 'dapr-sentry': it did not become healthy. State=Exited; Health=unknown; ExitCode=1.` Final result: two total, one passed, one failed, zero skipped. The underlying Sentry stderr is unavailable after automatic container cleanup; its cause is unproven. A read-only `aspire logs dapr-sentry` attempt confirmed that the owned AppHost had already stopped.

The final raw callback state is retained at `/tmp/story415-open-review-live-final-state.jsonl`; [the review copy](persisted-states.json) redacts payload fields case-insensitively. The final attempt does not establish fresh current-source restart/cascade acceptance. The immutable [.9 historical runtime proof](../review-iteration-5/implementation.md) remains historical evidence for those scenarios and was not relabeled.

All owned AppHosts and disposable Redis containers were cleaned up. Temporary configuration copies remain as review evidence. No direct review patch remains incomplete. Fresh current-source restart/cascade runtime acceptance remains blocked at the Sentry startup boundary; the orchestrator must assess that limitation during its matrix audit. Publication, production authority/policy, real-data admission, Platform parity, host removal and complete-store rollback policy retain their existing separate gates.

## Orchestrator audit

[The root audit](orchestrator-audit.json) independently verifies all 20 raw log hashes, the four exact HotReload-only configuration deltas, nine unchanged selected package archives and byte-identical frozen intent. The follow-up source diff was read and all five review patches were checked against the implementation. The Step 03 matrix gate remains incomplete because the fresh restart/cascade test did not reach its body; independent workflow review has not started.
