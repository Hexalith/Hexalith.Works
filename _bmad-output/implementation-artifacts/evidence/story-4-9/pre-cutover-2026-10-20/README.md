# Story 4.9 pre-cutover qualification — 2026-10-20

The complete canonical Platform verifier exited 0 before any Works host or runtime removal. All AD-20 R1–R11 rows passed their persisted, negative, restart and rollback proofs (44/44). The stopped-fleet restore and retained Works host query/receipt replay passed.

```bash
HexalithBuildsRoot=/tmp/story416-builds-b435f2e-public-source \
HexalithEventStoreRoot=/tmp/story416-sdk-3.126.0-public-source \
timeout --signal=INT --kill-after=90s 70m \
/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh \
  --works-root /home/administrator/projects/hexalith/works \
  --runtime-sdk-root /tmp/story416-sdk-3.126.0-public-source \
  --artifacts-path /tmp/story49-pre-cutover-20261020-isolated
```

The public EventStore producer is 3.126.0 at `fcf78d6cafb6c0c9f52dc350a9a09aff54ded288`. Its exact exported source was independently qualified against official immutable Git trees and public NuGet archives. Builds used the previously qualified `b435f2e3` source export. No package or source version was relabeled.

Works was `696781961155d764ed805843f55b40dc5882e887`. Platform began at `12c2952a5156f5841c6f9b0dbf3f4125a69d0baf`; an external documentation/evidence-only commit advanced it to `1a2cadfe16c5ec66a7acc4a3cf4711ef13a185e6` during the run. The complete 491-file Works and 610-file Platform ordinary input inventories, the producer catalog, and 44 explicitly linked test fixture files remained identical. See `canonical-revisions-complete.json` and `linked-test-fixtures.json`; the former records both full Platform heads and the exact documentation-only delta. The implementation agent performed no Git mutations.

Validation executed 63 Python evidence/launcher tests, 112 Platform tests in each of Debug/source and Release/package, 257 supporting Works tests, four recovery facts, and one fact each for historical lifecycle/security, independent-host HA and focused projection/rebuild. Every required .NET result has zero failures and zero skips. The persisted packet contains 32 phases, nine Platform starts and one retained rollback start. All 41 sidecar launches used the bound adapter and original hashed daprd; 40 distinct configurations include the shared HA replica configuration. Serially prebuilt runtime outputs remained unchanged.

| Row | Public producer | API | Consumer before cutover | Required proofs |
| --- | --- | --- | --- | --- |
| R1 | Hexalith.EventStore.Aspire 3.126.0 | `AddHexalithEventStore/AddEventStoreDomainModule` | `P/src/Hexalith.Platform.Composition/WorksPreviewModel.cs` | 4/4 |
| R2 | Hexalith.EventStore.DomainService 3.126.0 | `AddEventStoreDomainService` | `src/Hexalith.Works/Runtime/WorksHost.cs` | 4/4 |
| R3 | Hexalith.EventStore.DomainService 3.126.0 | `UseEventStoreDomainService` | `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs` | 4/4 |
| R4 | Hexalith.EventStore.Client 3.126.0 | `SharedProjectionEpochCoordinator` | `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs` | 4/4 |
| R5 | Hexalith.EventStore.DomainService 3.126.0 | `IDomainQueryHandler` | `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs` | 4/4 |
| R6 | Hexalith.EventStore.DomainService 3.126.0 | `AddEventStoreReminders` | `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs` | 4/4 |
| R7 | Hexalith.EventStore.DomainService 3.126.0 | `AddEventStoreProcesses` | `src/Hexalith.Works/Recovery/WorksProcessTranslator.cs` | 4/4 |
| R8 | Hexalith.EventStore.DomainService 3.126.0 | `ProcessDispositionService` | `src/Hexalith.Works/Recovery/WorksProcessDispositionEndpoints.cs` | 4/4 |
| R9 | Hexalith.EventStore.DomainService 3.126.0 | `AddEventStoreDomainService/UseEventStoreDomainService` | `src/Hexalith.Works/Runtime/WorksHost.cs` | 4/4 |
| R10 | Hexalith.EventStore.ServiceDefaults 3.126.0 | `EventStoreWorkloadAuthenticationDefaults` | `src/Hexalith.Works/Runtime/WorksHost.cs` | 4/4 |
| R11 | Hexalith.EventStore.Client 3.126.0 | `HttpTrustedEffectSubmitter` | `src/Hexalith.Works/Recovery/WorksProcessDelegationProvider.cs` | 4/4 |

`ledger.json` maps every row to exact phase hashes, asserted fields, commands, producer packages and consumer source hashes. `persisted-states.jsonl.gz` and `host-starts.jsonl.gz` contain the original JSONL bytes after decompression. `packet-manifest.json` binds stored files and uncompressed bytes. Raw binaries and fleet backups remain in the named temporary artifact directory; they are not duplicated in this source evidence packet. Verifier inputs are exact audit copies, not a separate executable authority.

The test-only launch adapter replaces only Dapr CLI 1.18.2's exact hardcoded `localhost:50001` Sentry endpoint. It verifies and executes the original daprd binary, keeps all other arguments and mTLS trust policy, rejects unexpected endpoint/mTLS arguments and changed binaries, and records per-launch configuration hashes. The fixture selects free run-owned Sentry, placement and Scheduler ports. No foreign fleet was stopped or modified. This evidence qualifies disposable Development topology only; existing production preview admission refusals remain enforced.
