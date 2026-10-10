---
title: 'Publish EventStore Checkpointed Process and Recovery Runtime'
type: 'feature'
created: '2026-10-10'
status: 'ready-for-dev'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** EventStore has stream paging and effect receipts but no checkpointed process runner. Works-owned Reactor recovery cannot satisfy AD-20 R7–R8 through the SDK, and failed work can strand silently.

**Approach:** Publish a bounded, checkpointed EventStore runner for domain-supplied translations and trusted effects, with periodic reconciliation, durable unresolved evidence, readiness, and disposition hooks.

## Boundaries & Constraints

**Always:** Treat `FromSequence` as exclusive; reuse `LastSequenceReturned` without increment. Validate identity, continuity, and full state-affecting decode before checkpointing. Derive effect IDs from stable source coordinates, kind, target, and ordinal. Advance only after all effects have terminal receipts or the envelope is durably quarantined. Bound pages, retries, and scans; rediscover work after restart. Unresolved or incomplete scans degrade readiness with bounded reason metrics and alert signals. Authenticate and audit disposition, fail closed on audit failure, and re-enter the same validator. Keep new durable types synthetic until AD-28 owner policy and restore proof permit real data.

**Never:** Reimplement receipts, trust gateway status or broker order, acknowledge unrecorded failure, copy Works decisions into EventStore, expose payloads in diagnostics, or remove the Works host. Platform owns production alerts/admission; Story 4.15 supplies Works translators.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
| --- | --- | --- | --- |
| Long source | Full and partial pages | Every position processed; cursor equals last returned sequence | Invalid page quarantines |
| Ambiguous crash | Effect commits before checkpoint | Identical replay settles by receipt, then advances | Unknown result retries |
| Terminal outcomes | Rejection or no-op receipt | Record outcome and advance without re-handling | Unknown disposition quarantines |
| Duplicate/reorder | Old delivery or concurrent runner | CAS prevents regression; receipts prevent duplicates | Conflicts quarantine |
| Bad evidence | Foreign, malformed, unknown event | Durable source-scoped record and degraded readiness | No skip or coercion |
| Parked source | Retry exhausted or dependency down | Periodic discovery and alert persist | Audited disposition revalidates |
| Denied disposition | Wrong tenant or failed audit sink | No record change or source disclosure | Fail closed |

</frozen-after-approval>

## Code Map

Paths here are relative to `references/Hexalith.EventStore/`.

- `src/Hexalith.EventStore.Contracts/Streams/StreamReadPageValidator.cs:19` — reuse scope/order/cursor validation; add decode and continuity.
- `src/Hexalith.EventStore.Client/Gateway/IEventStoreGatewayClient.cs:21` — use `ReadWorkloadStreamAsync`.
- `src/Hexalith.EventStore.Client/Streams/AuthoritativeEventStreamReader.cs:66` — reuse gap checks, not its full-prefix limit.
- `src/Hexalith.EventStore.Client/Effects/ITrustedEffectSubmitter.cs:9`, `src/Hexalith.EventStore.Contracts/Effects/EffectIdentityCodec.cs:19` — reuse receipt and identity.
- `src/Hexalith.EventStore.Client/Projections/IReadModelStore.cs:23` — reuse ETag CAS, never blind writes.
- `src/Hexalith.EventStore.DomainService/ReminderReconciler.cs:17`, `ReminderReadinessHealthCheck.cs:12` — reuse periodic/status patterns, not reminder records.

## Tasks & Acceptance

**Execution:**
- [ ] `src/Hexalith.EventStore.Client/Processes/IProcessTranslator.cs`, `IProcessSourceCatalog.cs`, `src/Hexalith.EventStore.Contracts/Processes/ProcessEffect.cs` — publish authoritative source discovery, full decode/translation, and stable effect-ordinal contracts with explicit unknown-evidence failure.
- [ ] `src/Hexalith.EventStore.DomainService/ProcessCheckpointStore.cs`, `ProcessStateKeys.cs`, `ProcessSourceState.cs` — persist tenant/source-scoped checkpoint, retry, quarantine, and discovery state with CAS and versioned validation; retain unresolved evidence across restart.
- [ ] `src/Hexalith.EventStore.DomainService/CheckpointedProcessRunner.cs` — read bounded workload pages, reuse strict validation, enforce continuity and full decode, submit every deterministic effect, and advance only after durable receipt or quarantine.
- [ ] `src/Hexalith.EventStore.DomainService/ProcessReconciler.cs`, `EventStoreProcessOptions.cs`, `EventStoreProcessServiceCollectionExtensions.cs` — start and periodically rescan authoritative candidates with validated bounds, including persisted parked work.
- [ ] `src/Hexalith.EventStore.DomainService/ProcessReadinessHealthCheck.cs`, `ProcessDispositionService.cs`, `src/Hexalith.EventStore.Client/Processes/IProcessDispositionAuthorizer.cs`, `IProcessDispositionAuditSink.cs` — expose readiness and bounded alert signals; require authentication, audit, and revalidation for disposition.
- [ ] `tests/Hexalith.EventStore.DomainService.Tests/Processes/CheckpointedProcessRunnerTests.cs`, `ProcessRecoveryTests.cs`, `tests/Hexalith.EventStore.Contracts.Tests/Packaging/ProcessPackageContractTests.cs`, `docs/guides/checkpointed-processes.md` — prove every matrix row with persisted end state and fault injection; document a named package API, producer version, R7–R8 handoff, and AD-28 synthetic-only gate.

**Acceptance Criteria:**
- Given a multi-page source with a crash after target commit, when the runner restarts, then the same effect identity replays to one durable target outcome and the checkpoint reaches the final validated position without a skipped envelope.
- Given invalid evidence or exhausted retry, when recovery runs again, then a tenant-scoped unresolved record remains discoverable, readiness and alert evidence remain degraded, and authorized audited disposition re-enters the validator.
- Given the published EventStore SDK contract and a synthetic pure translator, when a package-only consumer runs it, then process execution needs no Works runtime code and exposes the R7–R8 producer API for Story 4.15.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

One per-source CAS record owns checkpoint and unresolved evidence; target receipts close the crash gap. Discovery indexes are aids: incomplete source scans cannot report Ready.

## Verification

**Commands:**
- `dotnet build Hexalith.EventStore.slnx --configuration Debug -m:1` — EventStore source and SDK compile.
- `dotnet test tests/Hexalith.EventStore.Contracts.Tests/Hexalith.EventStore.Contracts.Tests.csproj --configuration Debug` — stream and package contracts pass.
- `dotnet test tests/Hexalith.EventStore.DomainService.Tests/Hexalith.EventStore.DomainService.Tests.csproj --configuration Debug` — checkpoint, crash, restart, readiness, and disposition tests pass.

Record package-consumer commands, persisted end state, and SDK version. Run test projects separately.
