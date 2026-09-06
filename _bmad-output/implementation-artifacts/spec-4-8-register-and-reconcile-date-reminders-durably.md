---
title: 'Complete Story 4.8 durable reminder runtime proof'
type: 'bugfix'
created: '2026-09-06'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '062426c97ed7e8be24b51094234cd70f8c79169b'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8's deterministic code exists, but Aspire startup stalls and no live Dapr reminder has been observed resuming an item. Works pins AppHost SDK 13.4.6 while imported packages use 13.5.3.

**Approach:** Align Works with Aspire 13.5.3, compose self-hosted Dapr 1.18.3+ Sentry/mTLS so ACLs receive SPIFFE caller identities, add actor/reminder diagnostics, then rerun the live lane. Patch actor code only if those probes prove a repository defect.

## Boundaries & Constraints

**Always:** Preserve deterministic names, idempotent `DateResume`, durable volumes, operations/resiliency, deny-by-default ACLs, and hard failure after prerequisites. Keep certificates/private keys outside the repository. Use Aspire CLI and stop it afterward. Local Dapr restart/upgrade and Sentry/mTLS setup are authorized for validation.

**Never:** Clear Redis, weaken/bypass ACLs, commit certificates or private keys, skip after prerequisites, fake callbacks, merely extend timeouts, change kernel/catalog 37, update submodules/shared catalogs, remove topology resources, or absorb Story 4.9/unrelated deferred work.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Cold start | Aspire 13.5.3; Dapr 1.18.3+ Sentry/mTLS reachable | AppHost, Sentry, `eventstore`, `works`, and identity-bearing sidecars become ready | Fail with resource/sidecar diagnostics |
| Steady reminder | Unique item awaits a near-future date | Actor is advertised, reminder exists, one resume appears without restart | Distinguish registration from delivery failure |
| Recovery | Overdue/future awaits survive disposal | Reissue overdue and re-register future; each resumes once | Preserve state and name the failed phase |

</frozen-after-approval>

## Code Map

- `global.json`, `src/Hexalith.Works.AppHost/{Hexalith.Works.AppHost.csproj,Program.cs}` -- SDK pins and topology.
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol*.yaml` and AppHost-local mTLS helpers -- Sentry trust and identity-bearing sidecars without committed secrets.
- `src/Hexalith.Works/Reminders/{DateReminderActor,DaprDateReminderScheduler}.cs` -- evidenced-only production boundaries.
- `tests/Hexalith.Works.IntegrationTests/{WorksAppHostTestReadiness,WorksReminderRecoveryPipelineSmokeTests,WorksAppHostTopologyTests}.cs` -- live proof.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs` -- SDK pin.

## Tasks & Acceptance

**Execution:**

- [x] `global.json`, `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`, `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs` -- align AppHost SDK pins/assertion to Aspire 13.5.3.
- [x] `tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs`, `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` -- verify sidecar/placement/actor readiness, inspect the deterministic reminder, and retain phase-specific startup/registration/delivery diagnostics.
- [x] `src/Hexalith.Works.AppHost/Program.cs`, `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` -- correct the diagnosed operations environment relation and pin the preserved topology.
- [x] `src/Hexalith.Works.AppHost/Program.cs`, `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol*.yaml`, `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs`, `tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs`, `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs`, `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` -- compose self-hosted Sentry/mTLS, supply non-repository trust material to every invoking sidecar, retain deny-by-default policies, prove authorized `eventstore -> works/process`, and reject an unauthorized caller.
- [x] `src/Hexalith.Works/Reminders/DateReminderActor.cs`, `src/Hexalith.Works/Reminders/DaprDateReminderScheduler.cs` -- probes have not isolated an actor defect, so no actor/scheduler production change is justified.
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/sprint-status.yaml`, `_bmad-output/implementation-artifacts/tests/test-summary.md` -- record exact evidence; mark done only after both live paths pass.

**Acceptance Criteria:**

- Given restored dependencies, when fitness tests run, then AppHost SDK 13.5.3 matches Aspire hosting.
- Given Redis and Dapr 1.18.3+, when the lane starts, then EventStore/Works are healthy and Works advertises `DateReminderActor` before submission.
- Given self-hosted Sentry/mTLS, when EventStore invokes `works/process`, then its certificate-backed SPIFFE identity matches the existing allow policy while an unauthorized caller remains denied.
- Given a near-future await, when the scheduler fires, then its deterministic reminder yields exactly one accepted `WorkItemResumed` without restart.
- Given durable overdue/future awaits, when unconfigured recovery runs, then overdue is reissued once and future is re-registered and resumes once.
- Given failure, when reported, then its startup/readiness/registration/delivery phase and evidence are retained.

## Implementation Notes

- Aspire 13.5.3 alignment, operations environment propagation, readiness diagnostics, reminder inspection, and deterministic gates were completed before the scope expansion.
- On 2026-09-06 the human selected Option A: add self-hosted Sentry/mTLS while preserving deny-by-default ACLs and external trust material.
- Commit `33a27e2` arrived through a concurrent workspace update containing the Sentry resource, external credential helper, mTLS sidecar configs, ACL smoke coverage, and unrelated planning/submodule changes. Those concurrent changes are preserved.
- Direct `daprd` diagnosis proved the Dapr CLI 1.18-generated sidecar keeps its standalone control-plane trust
  domain at `localhost`. The AppHost therefore uses `localhost` for Sentry, placement, scheduler, and
  `spec.mtls.controlPlaneTrustDomain`, while preserving the application/workload ACL trust domain `public`.
- The plaintext placement/scheduler services created by `dapr init` cannot serve mTLS sidecars. The AppHost now
  composes Dapr 1.18.3 placement and scheduler containers with TLS enabled, external read-only credentials,
  fixed localhost proxy endpoints, and a named scheduler-data volume. Explicit placement/scheduler endpoints
  remain supported only as a complete pair for an externally managed mTLS-compatible control plane.
- Live tracing reached `WorkItemSuspendedReminderHandler` and isolated the first actor defect:
  `DateReminderRegistration` was rejected by Dapr actor remoting because it lacked data-contract metadata. The
  record now carries `[DataContract]`/`[DataMember]`, with a deterministic serializer round-trip regression fact.
- The full live reminder class passes 4/4 with no skips in 939.522 seconds: steady registration/fire, overdue
  discovery/reissue across three lifecycles, future re-registration/fire across two lifecycles, and the mTLS ACL
  allow/deny proof. The affected command and cascade live lanes also pass (1/1 and 18/18 respectively).
- Review remediation made the recovery evidence loss-sensitive: each recovery fact now waits until its exact
  pending await is durable, deletes the corresponding Dapr Scheduler reminder while retaining that index, and
  proves startup reconciliation recreates the missing overdue/future work. The first ungated run exposed the
  former fixed-sleep race; after replacing it with the durable-index observation, the full live class passed 4/4
  in 923.387 seconds. Sidecar control-plane identity is now authoritative and explicit, certificate containment
  resolves symlinks, embedded etcd uses its loopback default, ACL trust-domain levels are pinned, and the actor
  data contract has required stable names/order plus a forward-read fixture.
- Story 4.8 adds no durable catalog type. The current checkout's catalog is 40 because concurrently completed
  Story 1.5 added three types after this spec's 37-type baseline; the 4.8 delta itself remains zero.

## Spec Change Log

## Review Triage Log

| ID | Verdict | Route | Evidence |
| --- | --- | --- | --- |
| B1 | medium | defer | `WhatsNextPayloadDescriptor` accepts a null-correlation `ConversationLinked` as intentional no-op evidence and advances its source watermark. This belongs to the separately committed Story 1.5 projection surface, not Story 4.8. |
| B2 | false | reject | The override branch promises only an external placement/Scheduler pair compatible with the AppHost-owned Sentry and bundle; it does not claim compatibility with a foreign CA or Sentry. Invalid external services fail during sidecar startup. |
| B3 | medium | patch | Dapr 1.18.3 exposes the control-plane trust-domain/namespace flags and recognizes `DAPR_CONTROLPLANE_TRUST_DOMAIN`/`DAPR_CONTROLPLANE_NAMESPACE`, while the callback previously left both implicit. The patch pins `localhost`/`default`. |
| B4 | medium | patch | `TryAdd` allowed stale inherited credentials or namespace to override the AppHost-owned bundle. The callback now assigns every authoritative credential and identity value. |
| B5 | medium | patch | `--etcd-client-listen-address=0.0.0.0` exposed the embedded-etcd client listener to the container network; removing the override restores Scheduler's loopback default. |
| B6 | medium | patch | Lexical containment alone accepted an external symlink targeting the repository. Existing links are now resolved before containment and mount-path selection, with a regression fact. |
| B7 | low | reject | Fixed local ports/volume can collide only across concurrent AppHosts; this acceptance topology is serialized and single-instance, while isolation would require new configuration/resource naming surface. |
| B8 | medium | patch | The pre-host-2 time check could not prove the await remained future after startup. The recovery body now checks again after host-2 readiness. |
| B9 | false | reject | Each `WithAppHostAsync` call creates a fresh five-minute budget, and the absolute four-minute deadline starts before host 1 disposes; host-2 startup reduces, rather than adds to, the remaining delivery wait. |
| B10 | low | patch | Caller cancellation was caught and relabeled as readiness failure. The helper now rethrows caller-requested `OperationCanceledException`. |
| B11 | low | patch | The live facts mutated process-wide `ASPNETCORE_ENVIRONMENT` despite already passing `--environment=Development`. Those global mutations were deleted. |
| B12 | medium | patch | Topology checks pinned only the control-plane domain. `AssertMtls` now pins the workload `accessControl.trustDomain` and every policy trust domain to `public`. |
| B13 | medium | patch | A self-round-trip would survive a coordinated member rename and did not freeze old bytes. Members now have explicit required names/order and tests read a frozen payload forward and reject a missing required member. |
| B14 | false | reject | The build spec's `done` describes completed implementation, while sprint `review` describes the human acceptance stage; these are intentionally different workflow states. |
| B15 | low | reject | The historical verification bullet says 37, but current implementation/final-result notes already distinguish the 37 baseline from current catalog 40. Workflow policy rejects findings whose fix is editing this build spec. |
| B16 | low | defer | The three-field bullets match the current build workflow's required deferred-entry format, but two concurrently committed Story 1.5 review records are stale against the present Story 4.8 tree. Existing entries are append-only in this workflow. |
| B17 | medium | defer | The three pointer advances are real relative to the 4.8 baseline but arrived in the separately committed concurrent workspace update. Repository instructions require preserving those user-owned changes. |
| E1 | medium | defer | `WorkItemState.Apply(ConversationLinked)` validates only the correlation, so foreign persisted identity could become authoritative. This is a separately committed Story 1.5 replay concern. |
| E2 | false | reject | EventStore rehydrates stream events in persisted order; the new apply overload follows the same trusted ordered-replay convention as the other success events, so the cited stale-sequence trigger is not a production path. |
| E3 | false | reject | Public state transitions cannot produce an `Unknown` state with a conversation correlation: the default sentinel has neither identity nor correlation, and applying a link makes no status change. The proposed state is unreachable without reflection/corruption. |
| E4 | low | reject | `NextSequence` can overflow at `long.MaxValue`, but it is the pre-existing shared helper for every command and requires an effectively unreachable stream length; a correct fix is cross-cutting, not a direct link/reminder correction. |
| E5 | medium | defer | This independently confirms B1: malformed null link evidence advances the what's-next watermark. It is deferred with the Story 1.5 projection issue. |
| E6 | medium | patch | Missing DataContract members previously became defaults, permitting malformed reminder input to reach naming/scheduling. All five members are now required and the omission is regression-tested. |
| E7 | medium | patch | This independently confirms B4: inherited Dapr values won because the callback used `TryAdd`. A seeded-environment topology test now proves authoritative replacement. |
| E8 | medium | patch | On Windows, `Path.GetRelativePath` across drives returns an absolute path that the old lexical test rejected. Containment now first requires equal filesystem roots with platform-correct comparison. |
| E9 | low | patch | This independently confirms B10: caller cancellation was wrapped as a resource startup failure and now propagates unchanged. |
| E10 | medium | patch | This independently confirms B12: policy/domain drift was unasserted and is now pinned at both access-control levels. |
| E11 | medium | patch | This independently confirms B6: an outside symlink could target repository content. The resolved target is checked and a symlink regression fact passes. |
| E12 | low | patch | Metadata/reminder polling did not retain phase evidence for `HttpClient.Timeout`. Non-caller `OperationCanceledException` now records bounded timeout diagnostics while caller cancellation still propagates. |
| V1 | high | patch | Pre-verified: durable Scheduler state allowed both recovery facts to pass without startup reconciliation. Both facts now delete the exact reminder after proving the index durable, then require recovery after restart. |
| V2 | medium | defer | Pre-verified: Story 1.5 tests vary only aggregate ID at the new envelope boundary, leaving domain and tenant comparisons unguarded. That separately committed story owns these cases. |
| V3 | medium | defer | Pre-verified: no Story 1.5 runtime-adapter case dispatches missing/null required `LinkConversation` fields. This is outside the Story 4.8 reminder intent. |
| V4 | medium | patch | Pre-verified: deleting the XOR guard left both topology facts green. Two theory cases now prove placement-only and Scheduler-only configuration fail fast. |
| V5 | medium | patch | Pre-verified: repository-local certificate rejection lacked execution coverage. A topology fact now supplies a repository path and asserts fail-closed construction. |

## Design Notes

Evidence ladder: AppHost -> Sentry identity -> sidecar -> actor -> reminder -> callback -> resume. Patch the first failed boundary.

## Verification

**Commands:**

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` -- succeeds.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` -- 0 warnings/errors.
- Run the Release Unit, Architecture, Property binaries and Integration binary with `-class- "*SmokeTests"` -- all deterministic tests pass; catalog 37.
- `dapr --version` -- runtime 1.18.3 or newer.
- Run the Integration binary with `-class "*WorksReminderRecoveryPipelineSmokeTests"` -- all live facts pass; absent prerequisites may skip, present compatible prerequisites may not.

**Final results (2026-09-06):** Release build 0 warnings/errors; Unit 567/567; Architecture 236/236;
Property 3/3; non-smoke Integration 287/287; topology 14/14; reminder live class 4/4 in 923.387 seconds,
no skips; command live 1/1 in 105.366 seconds; cascade class 18/18 in 332.959 seconds, no skips.
`dapr --version` reported CLI 1.18.0/runtime 1.18.3, and
`aspire ps --format Json --non-interactive` returned `[]` after the live runs.
