# Five deferred planning and evidence corrections

Baseline: `2e331b01237a15686a7722a867a8edb956042dc3` (Works main).
This packet qualifies five documentation corrections; it claims no new runtime or live-host proof.

| Canonical finding | Correction | Regression fact |
| --- | --- | --- |
| C12-BH9 | Restore persisted end-state and platform lifecycle/recovery evidence in Epic 4 requirements | `EpicContextRequiresPersistedStateAndPlatformRecoveryEvidence` |
| C27-EC8 | Restore terminal rejection and defined duplicate no-ops, including the Reject flag condition | `EpicContextRetainsTerminalRejectionAndDefinedNoOpExceptions` |
| C29-BH4 | Distinguish idempotent no-op from newly accepted work | `HeadlessOutcomesIncludeDistinctIdempotentNoOp` |
| Spec-11 historical-result follow-up | Preserve spec-10 counts as historical and link superseding spec-11 evidence | `Spec10VerificationKeepsHistoricalResultsAndLinksSupersedingEvidence` |
| Spec-11 inventory follow-up | Include spec-10 in its exact dated closeout File List | `Spec10DatedInventoryIncludesItsOwnSpecification` |

The eight-context-correction ledger entry is an aggregate alias. Five constituents were closed on
2026-10-23; this batch closes its remaining three. It is not counted as additional work.

The initial facts fail **5/5** against the baseline documents, then pass **5/5** after correction,
using the same initial test assembly. The strengthened post-review facts pass **5/5** after a
fresh final-source build; their final full Architecture run retains the same sole existing failure.
The existing lifecycle class passes **144/144**.
Debug Architecture and Unit project builds succeed with zero warnings/errors. Whitespace format
verification succeeds, with the existing generic workspace-load warning.

The full Architecture run executes **404 tests: 403 passed, 1 failed**, with no skips or not-run.
The failure is `P0_CurrentSourceRevisionMarkersMatchGitlinkAndConsumedCheckout`: unchanged API-surface
and boundary-document markers name `39a3d4dc98b786244e732d628a0828b9d19ad96e`, while the baseline
EventStore gitlink is `36c78f3435d2d492a73f1b0707cf865c8735d740`. `integrity.json` verifies these
inputs and the failing test source are byte-identical to baseline. This batch preserves that gate
and records the failure; it does not claim a passing full Architecture suite.

Commands (each result/log is retained as a gzip artifact alongside this file):

```bash
dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Debug --no-restore -m:1
dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -class '*PlanningEvidenceDocumentationTests' -result-xml /tmp/works-five-planning-before.xml
# Apply the five document corrections, retaining the original test assembly.
dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -class '*PlanningEvidenceDocumentationTests' -result-xml /tmp/works-five-planning-after.xml
dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/works-five-planning-architecture.xml
dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj --configuration Debug --no-restore -m:1
dotnet tests/Hexalith.Works.UnitTests/bin/Debug/net10.0/Hexalith.Works.UnitTests.dll -class '*WorkItemLifecycleTests' -result-xml /tmp/works-five-planning-lifecycle.xml
dotnet format whitespace tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --no-restore --verify-no-changes --include tests/Hexalith.Works.ArchitectureTests/FitnessTests/PlanningEvidenceDocumentationTests.cs
```

`manifest.json` binds source and assembly SHA-256, compressed artifacts, integrity conclusions,
exact command arguments, numeric exit codes and XML result counts. Its `initialExecutionAssemblySha256`
identifies the initial red/green/full run separately from the final reviewed assembly. The `final-*`
command records and artifacts supersede initial build/focused/full/format evidence.
`integrity.json` records unchanged frozen intent, exactly five canonical closures plus one alias,
and byte preservation of the original ledger after removing twelve inserted annotation lines.
The historical spec-10/spec-11 results are quoted only at their own checkpoints; neither qualifies
the current tree. No runtime, dependency, gitlink, shared agent entry point or production setting changed.

Independent review and its disposition are recorded in the implementing spec and `review.md`.
Recheck all source/artifact hashes, XML counts and baseline/ledger preservation from the repository root:

```bash
python3 _bmad-output/implementation-artifacts/evidence/five-deferred-planning-evidence-2026-10-28/verify.py
```

This check needs the named baseline Git object and current source files. It does not require generated
assemblies: their recorded SHA-256 values bind the executed lanes, while raw XML/log artifacts remain
portable. Rebuilding and running the focused facts is a separate executable check using the commands above.
