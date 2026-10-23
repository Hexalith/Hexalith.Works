---
title: 'Close five deferred conversation boundary items'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred boundary items remain open: null-correlation conversation projection evidence, persisted conversation event identity, independent domain and tenant envelope mismatch tests, malformed required-field dispatch tests, and reserved-tenant command-envelope hardening across aggregate adapters. Investigation confirms a replay identity defect, incomplete malformed-payload and reserved-tenant matrices, and existing implementations for the other protections that need current executable evidence and ledger reconciliation.

**Approach:** Validate ConversationLinked identity against an established work-item state before changing its correlation or ordinal; prove corrupted replay cannot poison a later valid link. Exercise every missing/null required command field through real EventStore reflection dispatch, extend reserved-envelope and reserved-payload cases across all fifteen commands, and verify existing projection and individual envelope-mismatch controls. Close exactly these five ledger entries, independently review, verify, commit, and push Works main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline is clean Works main at `47c88ee6df42d5d3d2148419afd77a7a2959fade`. No intent gaps or new irreversible implementation steps; the user explicitly authorizes committing and pushing main. This is a bounded change to one existing replay method, existing test classes, ledger, and this spec. Select oneshot. No dependency, schema, producer, submodule, or deployment changes are required.
- Code map: `src/Hexalith.Works.Contracts/State/WorkItemState.cs` owns `Apply(ConversationLinked)`. Require established tenant/item, matching payload tenant/item and aggregate string before mutation. Preserve correlation null refusal and existing first-reference precedence, including valid later-event sequencing.
- `tests/Hexalith.Works.UnitTests/WorkItemLinkConversationTests.cs` supplies representative populated states; cover foreign and absent identities, uncreated state, unchanged state after refusal, and subsequent valid replay. `WorkItemConversationProjectionTests` already proves malformed correlation cannot advance either projection watermark; retain that behavior.
- `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs` supplies the real reflection dispatcher, fifteen-command catalog, raw envelopes, and persisted event fixtures. Expand omitted/null fields and canonical/mixed-case reserved envelope and payload matrices, retaining ordinary command behavior and separate domain/tenant/aggregate mismatch cases. Replay persisted EventEnvelope bytes through this dispatcher as an additional boundary control.
- Verify focused UnitTests and IntegrationTests builds/runs in Debug with project references, then the full unit suite and deterministic integration lane as appropriate. Preserve exact commands and results. No Works AppHost exists, and these checks use the pure state and in-process runtime adapter, so live Platform topology is unnecessary for this batch. Check whitespace and commitlint, fetch and inspect main divergence, then push without bypassing validation.

- The eight new unit replay cases first failed against the baseline (`-method '*ConversationReplay*'`): 8 failures, proving the missing guard. With the fix, the complete Debug unit assembly passes 597/597 with no errors, failures, skips or unexecuted tests.
- Both broad integration builds (`dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1` and the same command without `--no-restore`) exit 1 in pre-existing Platform.Composition sources. The restored build still reports missing CompositionRunPlan.SelectedTests, ResourceClaimStateDirectory and CompositionResourceClaimStore, plus CA1062 in PlatformHostedPublishing. No referenced source was edited to mask these failures.
- Fallback: `/tmp/works-five-conversation-focus/FocusedAdapters.csproj` imports the repository build/central package configuration, links the exact edited LinkConversationRuntimeAdapterTests and unchanged WorkItemV1Catalog, references the real Works runtime project, uses the original friend assembly name and runner configuration, and builds Debug source references. This isolated lane builds with zero warnings/errors and executes 161/161 cases, including all changed runtime tests and existing independent mismatch controls. Its first run caught two incorrect test expectations for ArgumentNullException.ParamName; those now assert the actual caller expression `command.ConversationCorrelationId`.


## Review Triage Log

- **Low, rejected — malformed event ordinals:** The reviewer noted that a matching-identity event can carry a nonpositive or decreasing payload Sequence. This behavior predates the change and is shared by other replay overloads; no legitimate writer path was shown to emit such a history (`NextSequence` derives the next ordinal from established state). Recovery already validates positive domain ordinals separately. The requested identity guard preserves existing first-link precedence and later ordinals. A broad replay-order policy would require compatibility analysis, so this rare corrupted-history concern is not expanded into this identity patch.
- **Medium, patched — raw persisted omissions:** Added actual serialized missing tenant/item/aggregate fields, null aggregate and empty aggregate controls alongside foreign and null tenant/item controls. These execute the SDK deserializer and state fold.
- **Medium, patched — snapshots:** The full corrupted-payload matrix runs both from creation plus tail and from an established typed snapshot plus tail. A separate valid snapshot-tail test preserves same-link no-op and conflicting-link rejection. Snapshot identity is accepted without replaying the creation event.
- **Low, patched — corruption recovery wording:** The same corrupted history is retried and must remain refused. The healthy-history control uses a separate valid input to prove no adapter-instance poisoning; it does not repair stored history or promise successful commands against unchanged corruption. Actual history repair remains an operator concern outside this change.
- **Medium, patched — reproducibility:** Retained `evidence/five-deferred-conversation-boundaries-2026-10-23/reproduce-focused.py` generates the complete isolated project with repository configuration, exact linked tests/runtime, original friend assembly name, and original runner config. It builds before executing and stops on a failed build. The manifest records commands, results, source/assembly hashes and logs/XML for both successful lanes and the broad gate blocker.


## Verification

- `dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj --configuration Debug --no-restore -m:1`: exit 0, zero warnings/errors.
- `dotnet tests/Hexalith.Works.UnitTests/bin/Debug/net10.0/Hexalith.Works.UnitTests.dll -result-xml /tmp/works-five-conversation-unit.xml`: exit 0, **597/597** passed, zero errors/failures/skips/unexecuted.
- `python3 _bmad-output/implementation-artifacts/evidence/five-deferred-conversation-boundaries-2026-10-23/reproduce-focused.py --work-root /tmp/works-five-conversation-reproduced`: exit 0; fresh Debug build with zero warnings/errors, **167/167** exact-source adapter cases passed, zero errors/failures/skips/unexecuted. Build/run argv and complete generated project inputs are retained in the evidence manifest. This is a focused fallback, not a full IntegrationTests or live Platform pass.
- Broad integration build remains blocked as described above; exact commands and full logs are retained separately. The new replay matrix checks corrupted EventEnvelope payloads with and without established snapshots, and the valid snapshot control preserves retry/conflict behavior. Repeated corrupt-history retries remain refused; healthy input is independent, not automatic repair.
- `git -c core.whitespace=cr-at-eol diff --check`: exit 0. Changed source trailing whitespace and final newline checks pass; existing file newline styles are preserved to keep the patch focused.
- `npm run commitlint -- --edit /tmp/works-five-conversation-commit.txt`: exit 0 for `fix: close five deferred conversation boundary items`.
- Independent follow-up confirms the four review fixes and finds no concrete regression. The pre-existing malformed-ordinal policy concern remains outside this patch; no new work is deferred. Exactly the five selected ledger entries are closed.
- `git fetch origin main` and `git rev-list --left-right --count HEAD...origin/main`: exit 0, `0 0` before the authorized commit. No submodule files, revisions, dependencies, or deployment configuration changed.
- [Retained evidence](evidence/five-deferred-conversation-boundaries-2026-10-23/manifest.json) binds final source and assembly hashes, exact commands/results, compressed logs/XML, broad-gate blockers, the original red replay run, and the complete executable focused-lane recipe.
