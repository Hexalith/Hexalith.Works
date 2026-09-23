# Story 4.9 Pre-Cutover Evidence

This preserves the prerequisite index and the 2026-09-23 partial implementation checkpoint from the draft cutover spec. It is historical evidence, not permission to remove the Works host.

AD-20 makes host removal the last operation. Preserve the prior composition for rollback. EventStore already exposes query, projection, read-model, and shared-rebuild contracts; add the missing runtime mechanics.

## Prerequisite Story Index

| Story | Deliverable | AD-20 rows |
| --- | --- | --- |
| [4.10](spec-4-10-publish-eventstore-projection-delivery-and-rebuild-fence.md) | EventStore projection delivery and rebuild fence | R3–R4 producer |
| [4.13](spec-4-13-publish-eventstore-trusted-effect-submission.md) | EventStore trusted effect submission | R11 producer |
| [4.11](spec-4-11-publish-eventstore-typed-reminder-reconciliation.md) | EventStore typed reminders | R6 producer |
| [4.12](spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md) | EventStore process and recovery runtime | R7–R8 producer |
| [4.14](spec-4-14-adopt-sdk-projection-and-query-seams-in-works.md) | Works projection and query consumers | R3–R5 consumer |
| [4.15](spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md) | Works reminder, process, and command consumers | R6–R8/R11 consumer |
| [4.16](spec-4-16-prove-platform-works-parity-and-rollback.md) | Platform topology, security, parity, and rollback | R1–R11 proof |

## Partial Implementation Checkpoint

2026-09-23 checkpoint (partial, cutover gate open):

- Works recovery readers now use the exclusive `FromSequence` cursor correctly and reject a foreign stream identity or non-advancing page. The existing Works AppHost and ServiceDefaults remain in place.
- EventStore has an opt-in shared-projection epoch API with durable delivery, generation selection, catch-up, control-index discovery, and retry/parking behavior. Large stage manifests, capture maps, and ordinary delivery payloads use digest-checked chunks; a persisted test stages and promotes 10,000 aggregate mutations. Works does not enable or consume this API. `StreamReadPageValidator` is available as a shared contract, but recovery readers have not been migrated to it.
- Platform has a development-only, explicitly enabled Works topology. All 11 declared resources reached healthy state in a local run; this is topology evidence only. Platform's existing Agents verification still passes. The Dapr mTLS helper is in its own included C# file.
- R4 remains open: ordinary delivery is still on Works' bespoke `/project`; the full Works baseline and generation-selected query/recovery readers are not implemented. Captured positions stay unacknowledged while Building or Aborting so an aborted rebuild can receive them again. A pending chunk reservation requires source redelivery after a crash, oversized fold-prepared mutations fail closed, and chunk tombstone retention/offboarding needs further proof.
- R2-R3 and R5-R11 remain unproved; producer contracts, Works consumers, Platform parity commands, and rollback evidence are missing where applicable. No `eng/verify-works-host.sh` or complete R1-R11 matrix exists. No hosting project has been removed.
- The new EventStore contracts have no published producer version, and Works' Release package dependency does not expose them. The local EventStore source tests establish a development seam only; they cannot turn an AD-20 row green without a named artifact, a Works consumer, a Platform proof command, and rollback evidence.
- The EventStore control-index tombstone carries an audit identifier; external authorization, audit-sink evidence, retention approval, and restore drill remain outside this partial API.

## Partial Verification

**Partial checks run on 2026-09-23:** Works Release solution restore/build passed with zero warnings and errors. Works recovery reader classes passed 18/18 tests, and the existing ArchitectureTests runner passed 268/268 tests. EventStore Client projection suite passed 167/167 tests after the chunked durability changes, DomainService fenced integration passed 2/2, and Contracts page-validator tests passed 9/9. EventStore test-project builds reported existing MSB3277 dependency-version warnings. Platform AppHost Release build and `eng/verify-agents-host.sh` passed after separating the mTLS helper; `aspire describe` showed all 11 opt-in Works resources healthy before `aspire stop`. These checks do not satisfy the cutover verification commands above.
