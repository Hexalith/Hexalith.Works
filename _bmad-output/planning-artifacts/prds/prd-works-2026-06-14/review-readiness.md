# Behavioral readiness review — Hexalith.Works

20 November 2026 · PRD revision of 20 November on repository base `5287e187`

**Verdict.** Two requirements give different expected outcomes for the same command. Resolve them before accepting the child-attachment and effect-retry scenarios. The review also found two cross-feature acceptance gaps and one incorrect recovery example.

| Critical | High | Medium | Low |
| --- | --- | --- | --- |
| 0 | 2 | 2 | 1 |

## High

### Parent status after a child attaches

FR-6 says `SpawnChild` “does not change the parent's Status, including after terminal closure” (PRD line 195). FR-16 gives the caller a “whether the parent suspends” flag (line 329). After an authorized child attaches, a parent that requested the wait can consequently pass one specification by staying `InProgress` and fail the other. AD-21 defines the intended effect of this flag by parent status. [PRD, FR-6 and FR-16](prd.md) · [Architecture, AD-21, lines 367–382](../../architecture.md)

**Resolution.** State in FR-6 and FR-16 that processing a new attachment with the flag set suspends an `InProgress` parent on that child. An already `Suspended` parent retains its other waits and gains the child condition. Terminal parents retain their status. Specify an idempotent retry that neither duplicates the bookkeeping nor introduces another wait. Check the flag-cleared path separately.

### Reused effect key with different content

Section 10 and G1 promise the “original success, no-op, or rejection” when a message key repeats (lines 519 and 527). AD-26 permits that replay only when the effect tuple and semantic command digest match the recorded receipt. Consider two submissions with the same key and target but different command content. The PRD would allow the second submission to receive the first success. The architecture requires conflict and quarantine. [PRD, §10 and G1](prd.md) · [Architecture, AD-26, lines 490–499](../../architecture.md) · [Epics, effect-receipt requirement, line 124](../../epics.md)

**Resolution.** Qualify FR-26, §10 and G1 for internal effects. Verify both an identical retry that replays its disposition and a same-key semantic mismatch that enters quarantine without dispatching or mutating the target.

## Medium

### Reopening a child after its ancestor's cascade has finished

FR-8 permits the bound Executor to reopen a progress-completed child by correcting Done (line 223). FR-10 and FR-26 allow a terminated parent's cascade to leave that child alone during its first pass because it was already terminal (lines 246–247 and 346). If the correction commits after cascade quiescence, the child is active under its cancelled or expired ancestor. The late-attachment rule at FR-16 line 334 only covers newly attached children. The PRD supplies no acceptance result for this ordering. [PRD, FR-8, FR-10, FR-16 and FR-26](prd.md)

**Resolution.** Product should define whether a correction reactivating a previously skipped child triggers a fresh cascade or whether that child may continue. Specify the observable child state and Roll-Up after convergence, and cover the ordering across a restart. Keep the corrected effort as a recorded fact in either case.

### Golden comparison across executor identities

SM-3 requires event streams for three different executors to “differ only in the binding's field values” (line 545). FR-7 and §9 also require trusted actor metadata for the progress and completion acts (lines 212–213 and 500). The system, internal-user and external Party fixtures have different identities. A byte-level comparison of their persisted envelopes must therefore find differences outside the binding. The product brief requires uniform domain behavior across these executors. [PRD, FR-7, §9 and SM-3](prd.md) · [Product brief, line 137](../../briefs/brief-works-2026-06-14/brief.md) · [Representative executor fixtures, lines 33–38](../../../../tests/Hexalith.Works.IntegrationTests/UniformExecutorBindingLifecycleFlowTests.cs)

**Resolution.** Compare the event sequence, lifecycle outcomes and normalized domain payloads for parity. Independently assert the correct authenticated actor on every actor-owned event and exclude per-run transport metadata from the parity comparison.

## Low

### Crash recovery promises strictly positive Remaining

FR-8 says “A crash or abandonment leaves Remaining > 0” (line 228). In FR-9's worked example, lowering Estimated to 4 with 7 Done leaves a valid `InProgress` item at zero (line 235). If the service stops there, replay must preserve zero and the original Status. An unestimated item also has no numeric Remaining. [PRD, FR-1, FR-8 and FR-9](prd.md)

**Resolution.** State that recovery restores the last committed lifecycle and effort facts. Include the non-terminal zero case in the restart example.

## Other checks

FR-20's executor view requires the authenticated Party's ID and includes that Party's assigned work and the shared queue. The coordinator view has its own trusted-origin rule. FR-11 and SM-2 distinguish an active unestimated item from terminal work and sum contributions once per attached item and Unit. UJ-2 exercises an MCP binding in the harness and §12 places operation enrollment on the shared McpCli roadmap. These cases have consistent product expectations. [PRD, UJ-2, FR-11, FR-12, FR-20, §9 and §12](prd.md)

The release decision separately requires the evidence identified in FR-24 and G3, including integration and hosted parity for the candidate and raw-event recovery for affected snapshots. This document review assessed the requirements for that evidence. [PRD, FR-24 and G3](prd.md)
