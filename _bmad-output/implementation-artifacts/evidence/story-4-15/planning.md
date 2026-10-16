# Story 4.15 investigation — 2026-10-15

Planning evidence only: no implementation/build/runtime acceptance is claimed. The producer-scope decision and final spec approval are resolved; the spec is ready-for-dev and implementation has not started. Sprint key: `4-15-adopt-sdk-reminder-process-and-command-seams-in-works`.

## Baseline and scope

Works baseline is `8679d7d700925a870e9931b3c43c745c063e459b` on `main`. Existing edits in the Story 4.9 spec and `sprint-status.yaml` are preserved. Checked-out EventStore is `6394d0500e6e277b8975cb397c9f0389f9a16929`.

Loaded Hexalith baseline/state instructions, tracked root guidance and editor/build/package configuration, solution, architecture AD-20/25–28, epics, Epic 4 context, and producer/4.14 evidence. The cached epic context is newer than all planning artifacts. Two read-only investigation subagents were used as directed by bmad-build; dotnet-inspect checked public APIs.

- Intent gaps: none remain. On 2026-10-15 the user selected option 1: include the generic SDK correction and synthetic local-candidate verification, retaining public publication as a separate gate.
- Irreversibles: none proposed. Isolated synthetic proof only; no publishing, deployment, real-data admission, staging, commits, pushes or submodule initialization.
- Footprint: Works host adapters, additive existing command/event/state fields, pure witness validation, composition, tests/docs; a public SDK exception plus runner classification/docs under the selected scope. No new proprietary transport or Works persistence runtime.

## Verified producer base

Read-only NuGet flat-container index requests on 2026-10-15 returned EventStore `3.122.0` for Contracts, Client and DomainService. All nine inspected SDK nuspecs identify published source `1dac529c4690910c89d4090784a7d46cb9433a9d`. This is a suitable public BASE pin, not a claim that the correction below is published.

The downloaded source archive has SHA-256 `7d00dac9d3511cd906e72b4630a390a88c2c68985644d66f9aded637b7b39003`. Against the checkout, 1,811/1,812 `src/**/*.cs`/`*.csproj` files match exactly; only internal null handling in `DomainService/GatewayAuthorityAccessTokenProvider.cs` differs. GitHub comparison reports two commits ahead, zero behind. Accepted 4.14 projection fixes are therefore present. Public APIs include reminder/process registration, process translation and `ResolveDeliveryConflictsAsync`.

| Public package at 3.122.0 | Archive SHA-256 |
| --- | --- |
| Hexalith.EventStore.DomainService | `1f7d854f3be941fea822cb87185542f4c968dc3cd831ae3918dee55d9ce89fc8` |
| Hexalith.EventStore.Client | `686aee2d7cb2c69e2678601452aebac7a3d57d2bb1177a0872db24e11f12554d` |
| Hexalith.EventStore.Contracts | `5dda152c6d061fdae4052e60a24a75c9e33a1eeda1576e31fa890d3a746da70c` |

Colocated `public-producer-*.json` retain package metadata, source comparison and GitHub comparison summary. `public-*-api.txt` retain dotnet-inspect outputs. Package downloads/source archive stayed in temporary inspection storage; published URLs and hashes permit reproduction. No Works build against 3.122.0 has run.

Current root pin `3.120.1-story414.20261014.10` uses `.artifacts/story-4-14/feed`. Platform.Identity `1.0.0-story414.20261014.5` is a separate test candidate; its public flat-container index returned HTTP 404. Preserve explicit Platform provenance rather than claiming all test dependencies become public. Parties.Client remains 1.2.1. No pins were changed during planning.

## Selected producer correction

`references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Processes/IProcessTranslator.cs` forbids external I/O in its summary, but `docs/guides/checkpointed-processes.md` describes translator-read isolation and `DomainService/CheckpointedProcessRunner.cs::CaptureEnvelopeAsync` uses `WithReadDeadline`. No context/preparation seam exists. ProcessSource has only tenant/domain/aggregate; Works terminal events lack complete parent/child facts.

A Works host adapter must read authoritative relationship context around the PURE Reactor functions. Current runner handling treats any non-timeout read exception as invalid-evidence quarantine, including ordinary dependency outages. The only supported public retryable signals are timeout and non-caller cancellation; read-admission exceptions are internal. Do not fake timeouts or classify corrupt evidence as an outage.

Selected minimal change, subject to final spec approval (paths relative to `references/Hexalith.EventStore/`):

1. `src/Hexalith.EventStore.Client/Processes/IProcessTranslator.cs`, `docs/guides/checkpointed-processes.md`: clarify bounded cancellable read-only host adapters are supported around deterministic pure domain translation; side-effect writes remain forbidden.
2. Add `src/Hexalith.EventStore.Client/Processes/ProcessEvidenceUnavailableException.cs`: public typed transient context-acquisition failure; malformed, foreign or undecodable evidence uses the corruption path.
3. `src/Hexalith.EventStore.DomainService/CheckpointedProcessRunner.cs`: catch this signal before invalid-evidence; use existing retry/park state, bounded reason and degraded readiness without checkpoint advance. Preserve caller cancellation, unknown outcomes, and receipt identities.
4. `tests/Hexalith.EventStore.DomainService.Tests/Processes/{CheckpointedProcessRunnerTests,ProcessRecoveryTests,ProcessReadDeadlineTests}.cs`: prove transient recovery/parking, corrupt quarantine, restart and unchanged checkpoints/receipts. Extend `scripts/verify-process-packages.py` package consumer coverage for the new public contract.
5. Produce fresh immutable local candidate identities/feed and distinguish synthetic proof from publication. Never overwrite old candidates or claim them public. The user selected this local-candidate route rather than waiting for a separate producer release. A new context API is unnecessary.

## SDK map

Paths relative to `references/Hexalith.EventStore/src/`:

- `Hexalith.EventStore.Client/Reminders/{IReminderIntentSource,IReminderRegistrar,IReminderDelegationTokenProvider}.cs`: refold current target streams every call; converge after relevant commits; supply fresh delegation. Distinct current date awaits require unique committed source coordinates/revisions, even on the same date.
- `Hexalith.EventStore.Contracts/Reminders/{ReminderIntent,ReminderIdentityCodec}.cs`: reuse schedule witnesses/names and tuple validation; no Works hashing.
- `Hexalith.EventStore.DomainService/EventStoreReminderServiceCollectionExtensions.cs::AddEventStoreReminders<TSource>()`: SDK actor, callback, reconciliation, quarantine/readiness.
- `Hexalith.EventStore.Client/Processes/{IProcessSourceCatalog,IProcessTranslator,IProcessDelegationProvider}.cs`: keyset catalog, complete state-affecting decode, deterministic ProcessEffect, per-submission delegation. `EventStoreProcessServiceCollectionExtensions.cs::AddEventStoreProcesses<TCatalog,TTranslator>()` supplies CAS checkpoints, runner, periodic recovery and readiness/disposition.
- `Hexalith.EventStore.Client/Effects/{ITrustedEffectSubmitter,HttpTrustedEffectSubmitter}.cs`, `Hexalith.EventStore.Contracts/Effects/{EffectIdentityCodec,EffectKindCatalog,TrustedEffectSubmission,TrustedEffectContext}.cs`: existing effect families/primary ordinal zero, durable target receipts. Identity uses ENVELOPE SequenceNumber; causation uses source envelope MessageId.
- Host supplies gateway, signed delegation, disposition authorizer/audit sink and retained-source policy; absent authority denies. Follow documented workload transport and innermost Dapr invocation order. SDK supplies no production reminder-delegation provider. Synthetic configured providers prove mechanics without granting production admission.

## Works map and decisions

Paths below are relative to `src/Hexalith.Works/` unless stated otherwise.

- `Runtime/{WorksHost,WorksRecoveryExtensions,WorksRecoveryOptions}.cs`: exclusive opt-in SDK runtime; retain default legacy composition until parity. SDK mode requires SDK projection discovery/readers (`Works:Projection:UseSdkDelivery`) and rejects contradictory registration. Preserve protected ingress, existing credentials and 4.14 behavior.
- `Reminders/{DateReminderActor,DaprDateReminderScheduler,DateReminderReconciler,ReminderReconciliationService}.cs`: retire active SDK-mode bespoke services; preserve legacy transitional mode. Retain domain folds in `PendingDateAwaitProjection.cs`, `PendingDateAwaitStreamReader.cs`, `IndexedPendingDateAwaitSource.cs`, after enforcing strict scope/order/gap/decode validation.
- Add `Reminders/{WorksReminderIntentSource,WorksReminderEventHandler}.cs`: source coordinates and current witness come from authoritative stream refold, never callback payload or cached index. Converge after creation/reschedule/terminal/await changes. Discovery must cover expiry-only sources, not just pending dates; no configured tenant list or tenant-wide null-aggregate reads.
- `Recovery/Cascade/{CascadeDispatcher,ReadModelCascadeCheckpointStore,CascadeRecoveryReconciler,CascadeRecoveryService}.cs` and checkpoint/index types: remove active SDK-mode mechanics. Retain strict domain context reading in `StreamReadingCascadeDescendantSource.cs`; preserve SDK generation pinning and parking checks.
- `Recovery/ChildCompletion/{WorkItemCompletedResumeHandler,ChildCompletionResume,StreamReadingChildCompletionAwaitingParentSource}.cs`: replace direct submission with process translation; preserve current exact parent awaits and reject unknown state-affecting evidence.
- Add `Recovery/{WorksProcessSourceCatalog,WorksProcessTranslator}.cs`: complete source discovery from existing SDK family membership/retained authoritative source evidence, including terminal sources. Read necessary same-tenant parent/child facts, then call `src/Hexalith.Works.Reactor/{TerminalCascadeTranslator,ChildCompletionResumeTranslator}.cs`. Preserve active direct-child semantics and exact await matching. Fixed kind/ordinal plus target identity must remain independent of traversal order; any runtime capacity limit retains unresolved work rather than acknowledging a partial plan.
- `Runtime/{IWorkCommandSubmitter,EventStoreGatewayWorkCommandSubmitter}.cs`, `Reminders/DateResume.cs`, `Recovery/Cascade/CascadeCommands.cs`, `Recovery/ChildCompletion/ChildCompletionResume.cs`: ordinary command bridge cannot manufacture trusted source coordinates. Existing cascade/child handlers discard event context and use payload sequences, which diverge after rejections. Date IDs omit suspension position and collide across same-date re-suspension.
- `Runtime/Events/{WorksDomainEventProcessor,WorksDomainEventEndpointExtensions}.cs`: current malformed-delivery completion cannot erase unresolved SDK work. Preserve protected ingress; wakeups must not become a second effect writer. Canonical stream recovery and durable quarantine govern source evidence.

Expiry fields are additive to existing durable types. Inspect `src/Hexalith.Works.Contracts/Commands/{CreateWorkItem,RescheduleWorkItem,ExpireWorkItem}.cs`, matching `Events/{WorkItemCreated,WorkItemRescheduled,WorkItemExpired}.cs`, and `State/WorkItemState.cs`. Persist upstream-resolved effective UTC expiry, token and schedule revision; echo the current witness on scheduled expiry. `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` and host `WorkItemEventStoreAggregate.cs` validate it without a clock. Signed delegated cascade purpose must be admitted separately; omitting a payload witness never grants cascade authority. Historical schedules retain existing semantics and must not acquire invented TTL/DueDate expiry. Review `Commands/SpawnChild.cs`, `Events/ChildSpawned.cs` and child-create translation if expiry facts propagate; a child token binds the child's identity. Use existing SDK Contracts codecs allowed by kernel policy. No domain-level expiry-policy service is proposed; Platform supplies policy before admission, synthetic fixtures supply explicit resolved instants.

## Validation map

Paths relative to `tests/Hexalith.Works.IntegrationTests/`:

- `DateReminderRecoveryRuntimeTests.cs`, `IndexedPendingDateAwaitSourceTests.cs`, `WorksSdkRecoverySourceTests.cs`: reuse domain folds, partial scans, authoritative-stream and pinned-generation checks.
- `CascadeRecoveryRuntimeTests.cs`, `CascadeCheckpointIndexRecoveryTests.cs`, `StreamReadingCascadeDescendantSourceTests.cs`, `StreamReadingChildCompletionAwaitingParentSourceTests.cs`: preserve replay/context checks, extend strict page and sequence divergence cases. Existing in-memory fixtures do not establish Redis persistence.
- `WorksReminderRecoveryPipelineSmokeTests.cs`, `WorksCascadeRecoveryPipelineSmokeTests.cs`: real Redis/AppHost date and cascade restart templates. Add periodic lost firing without host restart, stale expiry/reschedule, child-resume crash, target commit before checkpoint, SDK receipt/checkpoint contents, quarantine/readiness and audited recovery.
- `WorksRecoveryGatewayRoutingTests.cs`, `WorksSdkProtectedHttpTests.cs`, `WorksMtlsAuthorizationSmokeTests.cs`: preserve transport/origin checks; add wrong tenant/workload/purpose/digest, conflicting replay, old source-floor and audit-failure denial before mutation/disclosure.
- `WorksSdkRollbackRestoreTests.cs`, `run-sdk-rollback-restore.sh`: isolated complete-store restore mechanics. Preserve complete 4.14 family/checkpoint state and unchanged legacy host; never describe in-place downgrade as rollback.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs`: current actor-package/catalog-count assertions need deliberate adaptation while retaining kernel purity. Additive command/event fields also require schema/golden compatibility tests and focused pure-domain unit tests.

Before implementation load Aspire orchestration guidance, start the unchanged AppHost and inspect resources. Build local Debug source and Release package graphs. Run test projects individually; use built xUnit assemblies with single-dash `-class`/`-method` for focused lanes. Follow the baseline fallback ladder, preserve exact broad blockers, and never weaken gates. Persist command/results and target store end states; HTTP status, gateway terminal status and mock counts alone are insufficient. Distinguish source, package, synthetic and production proof in the R6–R8/R11 handoff to 4.16.

## Planning validation

The draft has 1,357 tokens with tiktoken o200k_base. YAML/frontmatter, referenced context, frozen-block structure and all four JSON evidence documents validate. No implementation tests were run. Raw `git diff --check` reports CRLF carriage returns as trailing whitespace in the draft and pre-existing user edits; repository `.editorconfig` requires CRLF. The CRLF-aware command `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check` exits 0. No Git configuration was changed and user-owned files were not edited.

## Scope answer — 2026-10-15

The user answered `1` to the scope choices: include the EventStore correction and local-candidate verification, retaining upstream publication as a separate gate. This decision is recorded inside the spec intent block and the Open Questions section is removed. It resolves the scope question; the separate bmad-build Checkpoint 1 for final spec approval is still pending. No implementation, dependency-pin, sprint-status, commit, push or publication action occurred.

## Final spec approval — 2026-10-15

The user explicitly selected `approve and stop`. The approved spec and its exact sprint-status entry are marked `ready-for-dev`. The frozen intent block is unchanged. This supersedes the pending-checkpoint notes above; the revised approved spec has 1,384 tokens. Existing user edits in the Story 4.9 spec and other sprint-status lines are preserved. Stop before implementation; resume a later bmad-build invocation at step 3 without regenerating or renegotiating the approved intent. No code, dependency, commit, push or publication changes occurred.
