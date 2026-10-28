---
title: 'Refuse corrupt persisted Works history and membership'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five related deferred findings so persisted Works evidence cannot silently produce incorrect projections, query absence, or reminder intents:

1. R26-B3: require explicit scalar event members before projection folding.
2. R26-B4: admit only the catalog's exact short and full event type names.
3. R26-B5: require creation before every state-changing recovery event.
4. Refuse empty per-aggregate rebuild histories independently of empty tenant inventories.
5. Require current-generation manifests to retain membership for their item and sequence keys.

Preserve PascalCase stored payloads, camelCase compatibility payloads, additive optional fields, rejection-only histories, and conservative projection relationship sanitization. Malformed rebuild evidence must leave the committed generation and persisted documents unchanged. Invalid query manifests must report projection unavailability. Rejection-only and valid created histories must retain their supported behavior.

Use existing internal validation seams, add executing regression and compatibility controls, update the five ledger entries with evidence, and commit and push to main as requested. No public API, dependency, schema, or infrastructure changes are required.

</frozen-after-approval>

## Implementation Notes

- Baseline revision: `4dd71245d9bec28eb7252929f8fa87e711d750fa`. The four previously dirty submodules were clean when the user approved continuation; this change does not alter submodule pointers.
- Extracted required scalar checks into `Runtime/WorksEventScalarMembers.cs` and reused them in projection and recovery decoders. Exact short/full catalog names remain supported; arbitrary namespace aliases are refused. Existing relationship sanitization remains covered by the rebuild suite.
- `WorksExpiryEvidence` now refuses every state-changing payload before creation while preserving command refusals and existing reschedule/repeated-creation diagnostics. `WorkItemSharedRebuildManifestBuilder` refuses empty aggregate evidence; rejection-only capture retains membership/history without a roll-up.
- Shared current-generation manifest validation now rejects item/watermark keys absent from membership, blank members, and duplicate members. Both query adapters retain unavailable responses and unchanged persisted bytes.
- Added catalog-wide compatibility and missing-scalar controls, committed-generation preservation cases, reminder creation-order controls, rejection-only rebuild controls, and malformed-manifest query controls. Updated older scalar and terminal-context fixtures to include creation before state changes while retaining their original validation assertions.
- Runtime baseline command `aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json` exited 2: `NU1605` downgrades for CommunityToolkit.Aspire.Hosting.Dapr (13.6.2-beta.959 to 13.6.1-beta.935) and EventStore Aspire/ServiceDefaults (3.128.3 to 3.128.0). No AppHost started; `aspire ps --format Json` returned `[]`. No dependency versions were changed to bypass this blocker.

- Independent review prompted stronger frozen-corpus, nonpositive-sequence, empty-lifecycle preservation, and actual SDK reminder-convergence controls. Required-member negative fixtures now include valid creation prefixes so the new guard cannot mask removed field validation. Scalar checks reuse one parsed JSON root per decoded event.

## Review Triage Log

- Medium, patched: older malformed-required-shape tests lacked creation prefixes and could pass for the wrong reason; added valid prefixes, gateway tails, and adjusted persisted checkpoint/failure offsets.
- Medium, patched: generated compatibility samples did not bind the changed decoder to frozen historical bytes; added direct decoding of every checked-in Web and EventPersister event fixture.
- Medium, patched: empty aggregate refusal lacked an SDK lifecycle preservation control; added lifecycle refusal with unchanged generation/history/served documents after coordinator reconstruction, alongside direct domain-handler refusal and rejection-only success.
- Medium, patched: reminder-source refusal alone did not prove convergence-side persistence/scheduling behavior; added actual SDK coordinator convergence proving no item witness, tenant candidate index, control index, or scheduler arm survives corrupt initial suspension.
- Low, rejected: a new required scalar could theoretically be omitted from both the production decision table and test table. No current missing decision was identified; a general future contract-classification system exceeds a simple correction, while current catalog compatibility and every currently required scalar have executing coverage.
- Low, patched: projection tests omitted explicit zero/negative sequences; added all sequenced catalog events under both JSON casings.
- Low, patched: required-property checks reparsed complete payloads; both decoders now parse once and reuse the root for sequence and other scalar checks. No measured latency regression is claimed.

## Verification

- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1 --nologo`: passed, zero warnings/errors, sibling-source dependencies.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1 --nologo`: passed, zero warnings/errors, pinned public dependencies.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -explicit off -result-xml /tmp/works-five-history-integration.xml`: 1,069 passed, zero failures/skips; seven explicit live facts not run. Live Platform qualification remains blocked by the baseline AppHost package downgrades recorded above.
- Release assembly focused run: 280 passed, zero failures/skips/not-run. Classes: WorkItemProjectionEventAdmissionTests, WorkItemProjectionDecoderTelemetryTests, WorkItemSharedProjectionRebuildHandlerTests, WorksSourceStreamReaderTests, WorksSdkProjectionQueryTests, WorksSdkRecoverySourceTests, WorksSdkIteration4Tests, WorksSdkPartitionedRebuildTests, WorksSdkProcessRecoveryTests, WorksSdkReminderRecoveryPolicyTests. Result: `/tmp/works-five-history-release-focused.xml`.
- `git -c core.whitespace=cr-at-eol diff --check`: passed; CRLF matches repository guidance.
- `npm run commitlint -- --edit /tmp/works-five-history-commit.txt`: passed.
